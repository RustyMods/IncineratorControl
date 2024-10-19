using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IncineratorControl.Managers;

public class Recycle : MonoBehaviour
{
    public ZNetView m_nview = null!;
    public Incinerator m_incinerator = null!;

    public void Awake()
    {
        m_nview = GetComponent<ZNetView>();
        m_incinerator = GetComponent<Incinerator>();
        if (!m_nview.IsValid()) return;
        m_nview.Register<long>(nameof(RPC_RequestRecycle), RPC_RequestRecycle);
        
    }

    public bool OnRecycle()
    {
        if (!m_nview.IsValid() || !m_nview.IsOwner() || !PrivateArea.CheckAccess(transform.position)) return false;
        m_nview.InvokeRPC(nameof(RPC_RequestRecycle), Game.instance.GetPlayerProfile().GetPlayerID());
        return true;
    }

    public void RPC_RequestRecycle(long uid, long playerID)
    {
        if (!m_nview.IsOwner()) return;
        if (m_incinerator.m_container.IsInUse() || m_incinerator.isInUse) return;
        if (m_incinerator.m_container.GetInventory().NrOfItems() == 0)
        {
            m_nview.InvokeRPC(uid, nameof(Incinerator.RPC_IncinerateRespons), 3);
            return;
        }
        StartCoroutine(StartRecycle(uid));
    }

    public IEnumerator StartRecycle(long uid)
    {
        m_incinerator.isInUse = true;
        m_nview.InvokeRPC(ZNetView.Everybody, nameof(Incinerator.RPC_AnimateLever));
        var transform1 = transform;
        m_incinerator.m_leverEffects.Create(transform1.position, transform1.rotation);
        yield return new WaitForSeconds(Random.Range(m_incinerator.m_effectDelayMin,
            m_incinerator.m_effectDelayMax));
        m_nview.InvokeRPC(ZNetView.Everybody, nameof(Incinerator.RPC_AnimateLeverReturn));
        if (!m_nview.IsValid() || !m_nview.IsOwner() || m_incinerator.m_container.IsInUse())
        {
            m_incinerator.isInUse = false;
        }
        else
        {
            Invoke(nameof(Incinerator.StopAOE), 4f);
            Instantiate(m_incinerator.m_lightingAOEs, transform1.position, transform1.rotation);
            Inventory inventory = m_incinerator.m_container.GetInventory();
            GetConversions(inventory, out  Dictionary<ItemDrop, int> toAdd, out List<ItemDrop.ItemData> toRemove);
            foreach (ItemDrop.ItemData item in toRemove) inventory.RemoveItem(item);
            foreach (KeyValuePair<ItemDrop, int> kvp in toAdd)
            {
                GameObject prefab = kvp.Key.gameObject;
                int amount = Mathf.CeilToInt(kvp.Value * IncineratorControlPlugin.GetRecycleRate());
                inventory.AddItem(prefab, amount);
            }
            m_nview.InvokeRPC(uid, nameof(Incinerator.RPC_IncinerateRespons), toAdd.Count > 0 ? 2 : 1);
            m_incinerator.isInUse = false;
        }
    }

    private void GetConversions(Inventory inventory, out Dictionary<ItemDrop, int> toAdd, out List<ItemDrop.ItemData> toRemove)
    {
        toAdd = new Dictionary<ItemDrop, int>();
        toRemove = new List<ItemDrop.ItemData>();
        foreach (var item in inventory.m_inventory)
        {
            var recipe = ObjectDB.instance.GetRecipe(item);
            if (!recipe) continue;
            if (recipe.m_requireOnlyOneIngredient)
            {
                // Find first known material in resources, and recycle to that item
                foreach (var requirement in recipe.m_resources)
                {
                    if (!Player.m_localPlayer.IsKnownMaterial(requirement.m_resItem.m_itemData.m_shared.m_name)) continue;
                    UpdateDictionary(ref toAdd, requirement.m_resItem, requirement.m_amount);
                    break;
                }
            }
            else
            {
                // Return all resources
                foreach (var requirement in recipe.m_resources)
                {
                    if (!IncineratorControlPlugin.ReturnUnknown())
                    {
                        if (!Player.m_localPlayer.IsKnownMaterial(requirement.m_resItem.m_itemData.m_shared.m_name)) continue;
                    }
                    UpdateDictionary(ref toAdd, requirement.m_resItem, requirement.m_amount);
                }
            }
            toRemove.Add(item);
        }
    }

    private void UpdateDictionary(ref Dictionary<ItemDrop, int> dict, ItemDrop item, int amount)
    {
        if (dict.ContainsKey(item)) dict[item] += amount;
        else dict[item] = amount;
    }
    
}