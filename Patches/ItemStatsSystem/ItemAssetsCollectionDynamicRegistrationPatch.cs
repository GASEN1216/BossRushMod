using System;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace BossRush.Patches.ItemStatsSystem
{
    internal static class DynamicItemRegistrationPatchSupport
    {
        internal static void Ensure(int typeID)
        {
            if (!BossRushDynamicItemRegistry.IsPatchBypassed)
            {
                BossRushDynamicItemRegistry.EnsureRegistered(typeID);
            }
        }
    }

    [HarmonyPatch(typeof(ItemAssetsCollection), "GetMetaData", new Type[] { typeof(int) })]
    internal static class ItemAssetsCollectionGetMetaDataDynamicRegistrationPatch
    {
        [HarmonyPrefix]
        private static void Prefix(int typeID)
        {
            DynamicItemRegistrationPatchSupport.Ensure(typeID);
        }
    }

    [HarmonyPatch(typeof(ItemAssetsCollection), "GetPrefab", new Type[] { typeof(int) })]
    internal static class ItemAssetsCollectionGetPrefabDynamicRegistrationPatch
    {
        [HarmonyPrefix]
        private static void Prefix(int typeID)
        {
            DynamicItemRegistrationPatchSupport.Ensure(typeID);
        }
    }

    [HarmonyPatch(typeof(ItemAssetsCollection), "InstantiateSync", new Type[] { typeof(int) })]
    internal static class ItemAssetsCollectionInstantiateSyncDynamicRegistrationPatch
    {
        [HarmonyPrefix]
        private static void Prefix(int typeID)
        {
            DynamicItemRegistrationPatchSupport.Ensure(typeID);
        }
    }

    [HarmonyPatch(typeof(ItemAssetsCollection), "InstantiateAsync", new Type[] { typeof(int) })]
    internal static class ItemAssetsCollectionInstantiateAsyncDynamicRegistrationPatch
    {
        [HarmonyPrefix]
        private static void Prefix(int typeID)
        {
            DynamicItemRegistrationPatchSupport.Ensure(typeID);
        }
    }

    [HarmonyPatch(typeof(ItemAssetsCollection), "InstantiateAsync_Local", new Type[] { typeof(int) })]
    internal static class ItemAssetsCollectionInstantiateAsyncLocalDynamicRegistrationPatch
    {
        [HarmonyPrefix]
        private static void Prefix(int typeID)
        {
            DynamicItemRegistrationPatchSupport.Ensure(typeID);
        }
    }

    [HarmonyPatch(typeof(ItemTreeData), "InstantiateAsync", new Type[] { typeof(ItemTreeData) })]
    internal static class ItemTreeDataInstantiateAsyncDynamicRegistrationPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ItemTreeData data)
        {
            if (data == null || data.entries == null)
            {
                return;
            }

            for (int i = 0; i < data.entries.Count; i++)
            {
                ItemTreeData.DataEntry entry = data.entries[i];
                if (entry == null)
                {
                    continue;
                }

                DynamicItemRegistrationPatchSupport.Ensure(entry.typeID);
            }
        }
    }
}
