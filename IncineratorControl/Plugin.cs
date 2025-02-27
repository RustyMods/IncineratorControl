﻿using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using IncineratorControl.Managers;
using JetBrains.Annotations;
using ServerSync;

namespace IncineratorControl
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class IncineratorControlPlugin : BaseUnityPlugin
    {
        internal const string ModName = "IncineratorControl";
        internal const string ModVersion = "1.0.1";
        internal const string Author = "RustyMods";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource IncineratorControlLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
        public static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
        public enum Toggle { On = 1, Off = 0 }
        private static ConfigEntry<Toggle> _serverConfigLocked = null!;
        private static ConfigEntry<Toggle> _recycle = null!;
        private static ConfigEntry<float> _recycleModifier = null!;
        private static ConfigEntry<Toggle> _recycleUnknown = null!;
        public static bool Recycle() => _recycle.Value is Toggle.On;
        public static float GetRecycleRate() => _recycleModifier.Value;
        public static bool ReturnUnknown() => _recycleUnknown.Value is Toggle.On;
        public void Awake()
        {
            InitConfigs();
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
            IncineratorManager.SetupWatcher();
            IncineratorManager.ReadFileData();
            IncineratorManager.SetupServerSync();
        }

        private void InitConfigs()
        {
            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On,
                "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
            _recycle = config("Recycle", "Enabled", Toggle.Off, "If on, incinerator recycles instead of converts");
            _recycleModifier = config("Recycle", "Rate", 1f,
                new ConfigDescription("Set percentage of returned resources for item",
                    new AcceptableValueRange<float>(0f, 1f)));
            _recycleUnknown = config("Recycle", "Unknown", Toggle.On, "Return unknown recipe materials");
        }

        private void OnDestroy() => Config.Save();

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                IncineratorControlLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                IncineratorControlLogger.LogError($"There was an issue loading your {ConfigFileName}");
                IncineratorControlLogger.LogError("Please check your config entries for spelling and format!");
            }
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription =
                new(
                    description.Description +
                    (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                    description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description,
            bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order;
            [UsedImplicitly] public bool? Browsable;
            [UsedImplicitly] public string? Category;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer;
        }
    }
}