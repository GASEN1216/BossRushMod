using System;
using System.Reflection;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace BossRush.Patches.ItemStatsSystem
{
    internal static class DynamicItemRegistrationPatchSupport
    {
        private sealed class CriticalPatchSpec
        {
            internal readonly MethodBase Original;
            internal readonly Type PatchType;
            internal readonly string Label;

            internal CriticalPatchSpec(MethodBase original, Type patchType, string label)
            {
                Original = original;
                PatchType = patchType;
                Label = label;
            }
        }

        internal static bool Ensure(int typeID)
        {
            if (BossRushDynamicItemRegistry.IsPatchBypassed)
            {
                return false;
            }

            try
            {
                return BossRushDynamicItemRegistry.EnsureRegistered(typeID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRushDynamicItemRegistry] [ERROR] TypeID=" + typeID + " 注册入口异常: " + e);
                return false;
            }
        }

        internal static void EnsureTree(ItemTreeData data)
        {
            if (data == null || data.entries == null)
            {
                return;
            }

            for (int i = 0; i < data.entries.Count; i++)
            {
                ItemTreeData.DataEntry entry = data.entries[i];
                if (entry != null)
                {
                    Ensure(entry.typeID);
                }
            }
        }

        internal static bool EnsureCriticalPatchesApplied(Harmony harmony)
        {
            if (harmony == null)
            {
                return false;
            }

            CriticalPatchSpec[] specs = BuildCriticalPatchSpecs();
            int verified = 0;
            int repaired = 0;

            for (int i = 0; i < specs.Length; i++)
            {
                CriticalPatchSpec spec = specs[i];
                if (spec.Original == null)
                {
                    ModBehaviour.DevLog("[BossRushDynamicItemRegistry] [ERROR] 官方关键方法不存在: " + spec.Label);
                    continue;
                }

                if (!HasOwnedPrefix(spec.Original, harmony.Id, spec.PatchType))
                {
                    try
                    {
                        harmony.CreateClassProcessor(spec.PatchType).Patch();
                        repaired++;
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[BossRushDynamicItemRegistry] [ERROR] 关键补丁补装失败: " + spec.Label + ", " + e);
                    }
                }

                if (HasOwnedPrefix(spec.Original, harmony.Id, spec.PatchType))
                {
                    verified++;
                }
                else
                {
                    ModBehaviour.DevLog("[BossRushDynamicItemRegistry] [ERROR] 关键补丁未生效: " + spec.Label);
                }
            }

            ModBehaviour.DevLog("[BossRushDynamicItemRegistry] 关键补丁验证完成: " + verified + "/" + specs.Length + ", 补装=" + repaired);
            return verified == specs.Length;
        }

        private static CriticalPatchSpec[] BuildCriticalPatchSpecs()
        {
            Type[] intArgument = new Type[] { typeof(int) };
            return new CriticalPatchSpec[]
            {
                new CriticalPatchSpec(
                    AccessTools.Method(typeof(ItemAssetsCollection), "GetMetaData", intArgument),
                    typeof(ItemAssetsCollectionGetMetaDataDynamicRegistrationPatch),
                    "ItemAssetsCollection.GetMetaData(int)"),
                new CriticalPatchSpec(
                    AccessTools.Method(typeof(ItemAssetsCollection), "GetPrefab", intArgument),
                    typeof(ItemAssetsCollectionGetPrefabDynamicRegistrationPatch),
                    "ItemAssetsCollection.GetPrefab(int)"),
                new CriticalPatchSpec(
                    AccessTools.Method(typeof(ItemAssetsCollection), "InstantiateSync", intArgument),
                    typeof(ItemAssetsCollectionInstantiateSyncDynamicRegistrationPatch),
                    "ItemAssetsCollection.InstantiateSync(int)"),
                new CriticalPatchSpec(
                    AccessTools.Method(typeof(ItemAssetsCollection), "InstantiateAsync", intArgument),
                    typeof(ItemAssetsCollectionInstantiateAsyncDynamicRegistrationPatch),
                    "ItemAssetsCollection.InstantiateAsync(int)"),
                new CriticalPatchSpec(
                    AccessTools.Method(typeof(ItemAssetsCollection), "InstantiateAsync_Local", intArgument),
                    typeof(ItemAssetsCollectionInstantiateAsyncLocalDynamicRegistrationPatch),
                    "ItemAssetsCollection.InstantiateAsync_Local(int)"),
                new CriticalPatchSpec(
                    AccessTools.Method(typeof(ItemTreeData), "InstantiateAsync", new Type[] { typeof(ItemTreeData) }),
                    typeof(ItemTreeDataInstantiateAsyncDynamicRegistrationPatch),
                    "ItemTreeData.InstantiateAsync(ItemTreeData)"),
                new CriticalPatchSpec(
                    AccessTools.Method(typeof(InventoryData), "LoadIntoInventory", new Type[] { typeof(InventoryData), typeof(Inventory) }),
                    typeof(InventoryDataLoadIntoInventoryDynamicRegistrationPatch),
                    "InventoryData.LoadIntoInventory(InventoryData, Inventory)"),
                new CriticalPatchSpec(
                    AccessTools.Method(typeof(ItemAssetsCollection), "InstantiateFallbackItem", intArgument),
                    typeof(ItemAssetsCollectionInstantiateFallbackDynamicRegistrationPatch),
                    "ItemAssetsCollection.InstantiateFallbackItem(int)")
            };
        }

        private static bool HasOwnedPrefix(MethodBase original, string owner, Type patchType)
        {
            HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(original);
            if (patchInfo == null)
            {
                return false;
            }

            foreach (Patch prefix in patchInfo.Prefixes)
            {
                MethodInfo patchMethod = prefix.PatchMethod;
                if (prefix.owner == owner && patchMethod != null && patchMethod.DeclaringType == patchType)
                {
                    return true;
                }
            }

            return false;
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
            DynamicItemRegistrationPatchSupport.EnsureTree(data);
        }
    }

    [HarmonyPatch(typeof(InventoryData), "LoadIntoInventory", new Type[] { typeof(InventoryData), typeof(Inventory) })]
    internal static class InventoryDataLoadIntoInventoryDynamicRegistrationPatch
    {
        [HarmonyPrefix]
        private static void Prefix(InventoryData data)
        {
            if (data == null || data.entries == null)
            {
                return;
            }

            for (int i = 0; i < data.entries.Count; i++)
            {
                InventoryData.Entry entry = data.entries[i];
                if (entry != null)
                {
                    DynamicItemRegistrationPatchSupport.EnsureTree(entry.itemTreeData);
                }
            }
        }
    }

    [HarmonyPatch(typeof(ItemAssetsCollection), "InstantiateFallbackItem", new Type[] { typeof(int) })]
    internal static class ItemAssetsCollectionInstantiateFallbackDynamicRegistrationPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(int typeID, ref Item __result)
        {
            if (!DynamicItemRegistrationPatchSupport.Ensure(typeID))
            {
                return true;
            }

            Item prefab = BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(typeID);
            if (prefab == null)
            {
                return true;
            }

            __result = UnityEngine.Object.Instantiate(prefab);
            return false;
        }
    }
}
