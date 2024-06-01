using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using ServerSync;
using YamlDotNet.Serialization;

namespace IncineratorControl.Managers;

public static class IncineratorManager
{
    private static readonly string m_folderPath = Paths.ConfigPath + Path.DirectorySeparatorChar + "IncineratorControl";
    private static readonly string m_filePath = m_folderPath + Path.DirectorySeparatorChar + "configs.yml";

    private static readonly CustomSyncedValue<string> m_serverSync = new(IncineratorControlPlugin.ConfigSync, "IncineratorControl_ServerSync_Data", "");

    private static List<ObliterateConversion> m_data = new();
    
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    private static class InitializeIncineratorManager
    {
        private static void Postfix(ZNetScene __instance)
        {
            if (!__instance) return;
            WriteDefaultData(__instance);
        }
    }

    [HarmonyPatch(typeof(Incinerator), nameof(Incinerator.OnIncinerate))]
    private static class Incinerator_OnIncinerate_Prefix
    {
        private static void Prefix(Incinerator __instance)
        {
            if (!__instance) return;
            if (__instance.name.Replace("(Clone)", string.Empty) != "incinerator") return;
            UpdateIncinerator(__instance);
        }
    }

    private static void WriteDefaultData(ZNetScene __instance)
    {
        if (!Directory.Exists(m_folderPath)) Directory.CreateDirectory(m_folderPath);
        if (File.Exists(m_filePath)) return;
        var incinerator = __instance.GetPrefab("incinerator");
        if (!incinerator) return;
        if (!incinerator.TryGetComponent(out Incinerator component)) return;
        var serializer = new SerializerBuilder().Build();
        List<ObliterateConversion> data = new();
        foreach (var conversion in component.m_conversions)
        {
            ObliterateConversion info = new()
            {
                m_result = conversion.m_result.name,
                m_resultAmount = conversion.m_resultAmount,
                m_requireOnlyOneIngredient = conversion.m_requireOnlyOneIngredient,
                m_priority = conversion.m_priority
            };
            foreach (var requirement in conversion.m_requirements)
            {
                info.m_requirements.Add(new()
                {
                    m_prefabName = requirement.m_resItem.name,
                    m_amount = requirement.m_amount
                });
            }
            data.Add(info);
        }

        var serial = serializer.Serialize(data);
        File.WriteAllText(m_filePath, serial);
    }

    private static void UpdateIncinerator(Incinerator component)
    {
        component.m_conversions = GetConversions();
    }

    public static void SetupWatcher()
    {
        FileSystemWatcher watcher = new FileSystemWatcher(m_folderPath, "*.yml");
        watcher.EnableRaisingEvents = true;
        watcher.IncludeSubdirectories = true;
        watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        watcher.Changed += OnFileChange;
        watcher.Created += OnFileChange;
        watcher.Deleted += OnFileChange;
    }

    private static void OnFileChange(object sender, FileSystemEventArgs e)
    {
        IncineratorControlPlugin.IncineratorControlLogger.LogDebug("Read file called");
        ReadFileData(true);
    }

    public static void SetupServerSync()
    {
        m_serverSync.ValueChanged += () =>
        {
            if (m_serverSync.Value.IsNullOrWhiteSpace()) return;
            try
            {
                var deserializer = new DeserializerBuilder().Build();
                var data = deserializer.Deserialize<List<ObliterateConversion>>(m_serverSync.Value);
                m_data = data;
            }
            catch
            {
                IncineratorControlPlugin.IncineratorControlLogger.LogDebug("Failed to deserialize server data");
            }
        };
    }

    private static List<Incinerator.IncineratorConversion> GetConversions()
    {
        List<Incinerator.IncineratorConversion> output = new();
        foreach (var conversion in m_data)
        {
            if (GetConversionData(conversion, out Incinerator.IncineratorConversion data))
            {
                output.Add(data);
            }
        }

        return output;
    }

    private static bool GetConversionData(ObliterateConversion data, out Incinerator.IncineratorConversion conversion)
    {
        conversion = new Incinerator.IncineratorConversion();
        ItemDrop? result = GetItemDrop(data.m_result);
        if (result == null) return false;
        conversion.m_result = result;
        conversion.m_resultAmount = data.m_resultAmount;
        conversion.m_requireOnlyOneIngredient = data.m_requireOnlyOneIngredient;
        conversion.m_priority = data.m_priority;
        conversion.m_requirements = new List<Incinerator.Requirement>();
        foreach (Requirement? requirement in data.m_requirements)
        {
            ItemDrop? prefab = GetItemDrop(requirement.m_prefabName);
            if (prefab == null) continue;
            conversion.m_requirements.Add(new Incinerator.Requirement
            {
                m_resItem = prefab,
                m_amount = requirement.m_amount
            });
        }

        return conversion.m_requirements.Count > 0;
    }

    private static ItemDrop? GetItemDrop(string itemName)
    {
        if (!ObjectDB.instance) return null;
        var prefab = ObjectDB.instance.GetItemPrefab(itemName);
        if (!prefab) return null;
        return prefab.TryGetComponent(out ItemDrop component) ? component : null;
    }

    public static void ReadFileData(bool checkZNet = false)
    {
        if (!Directory.Exists(m_folderPath)) Directory.CreateDirectory(m_folderPath);
        if (checkZNet)
        {
            if (!ZNet.instance && !ZNet.instance.IsServer()) return;
        }
        if (!File.Exists(m_filePath)) return;
        try
        {
            var deserializer = new DeserializerBuilder().Build();
            var file = File.ReadAllText(m_filePath);
            m_data = deserializer.Deserialize<List<ObliterateConversion>>(file);
            if (ZNet.instance && ZNet.instance.IsServer())
            {
                m_serverSync.Value = file;
            }
        }
        catch
        {
            IncineratorControlPlugin.IncineratorControlLogger.LogWarning("Failed to deserialize file");
        }
    }
}

[Serializable]
public class ObliterateConversion
{
    public List<Requirement> m_requirements = new();
    public string m_result = "Coal";
    public int m_resultAmount = 1;
    public bool m_requireOnlyOneIngredient = true;
    public int m_priority = 0;
}

[Serializable]
public class Requirement
{
    public string m_prefabName = null!;
    public int m_amount = 1;
}