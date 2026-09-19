using System;
using System.Reflection;
using HarmonyLib;
using Cysharp.Threading.Tasks;
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
            internal readonly bool RequirePostfix;

            internal CriticalPatchSpec(MethodBase original, Type patchType, string label, bool requirePostfix = false)
            {
                Original = original;
                PatchType = patchType;
                Label = label;
                RequirePostfix = requirePostfix;
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

        // 非激活的运行时克隆 prefab 不执行 Awake；官方 InstantiateSync/Async 也只 Instantiate。
        // 必须在交付实例前补初始化，否则拾取与使用共用的 AgentUtilities.Master 仍为 null。
        internal static void InitializeInstance(Item item)
        {
            if (item == null || !BossRushDynamicItemRegistry.IsBossRushDynamicItemType(item.TypeID)) return;
            if (item.AgentUtilities.Master == item) return;
            try
            {
                item.Initialize();
                if (item.AgentUtilities.Master != item) item.AgentUtilities.Initialize(item);
            }
            catch (Exception e)
            {
                ModBehaviour.CriticalLog("dynamic-item-initialize-" + item.TypeID,
                    "[BossRushDynamicItemRegistry] 物品实例初始化失败: " + item.TypeID + ", " + e.Message);
            }
        }

        internal static async UniTask<Item> InitializeAsyncInstance(UniTask<Item> operation)
        {
            Item item = await operation;
            InitializeInstance(item);
            return item;
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
                    ModBehaviour.CriticalLog(
                        "critical-patch-missing-" + spec.Label,
                        "[BossRushDynamicItemRegistry] [ERROR] 官方关键方法不存在: " + spec.Label);
                    continue;
                }

                if (!HasOwnedRequiredPatches(spec, harmony.Id))
                {
                    try
                    {
                        harmony.CreateClassProcessor(spec.PatchType).Patch();
                        repaired++;
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.CriticalLog(
                            "critical-patch-repair-" + spec.Label,
                            "[BossRushDynamicItemRegistry] [ERROR] 关键补丁补装失败: " + spec.Label + ", " + e);
                    }
                }

                if (HasOwnedRequiredPatches(spec, harmony.Id))
                {
                    verified++;
                }
                else
                {
                    ModBehaviour.CriticalLog(
                        "critical-patch-inactive-" + spec.Label,
                        "[BossRushDynamicItemRegistry] [ERROR] 关键补丁未生效: " + spec.Label);
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
                    "ItemAssetsCollection.InstantiateSync(int)", true),
                new CriticalPatchSpec(
                    AccessTools.Method(typeof(ItemAssetsCollection), "InstantiateAsync", intArgument),
                    typeof(ItemAssetsCollectionInstantiateAsyncDynamicRegistrationPatch),
                    "ItemAssetsCollection.InstantiateAsync(int)", true),
                new CriticalPatchSpec(
                    AccessTools.Method(typeof(ItemAssetsCollection), "InstantiateAsync_Local", intArgument),
                    typeof(ItemAssetsCollectionInstantiateAsyncLocalDynamicRegistrationPatch),
                    "ItemAssetsCollection.InstantiateAsync_Local(int)", true),
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

        private static bool HasOwnedRequiredPatches(CriticalPatchSpec spec, string owner)
        {
            HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(spec.Original);
            if (patchInfo == null)
            {
                return false;
            }

            bool hasPrefix = false;
            foreach (Patch prefix in patchInfo.Prefixes)
            {
                MethodInfo patchMethod = prefix.PatchMethod;
                if (prefix.owner == owner && patchMethod != null && patchMethod.DeclaringType == spec.PatchType)
                {
                    hasPrefix = true;
                    break;
                }
            }
            if (!hasPrefix || !spec.RequirePostfix) return hasPrefix;
            foreach (Patch postfix in patchInfo.Postfixes)
            {
                MethodInfo patchMethod = postfix.PatchMethod;
                if (postfix.owner == owner && patchMethod != null && patchMethod.DeclaringType == spec.PatchType)
                    return true;
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

        [HarmonyPostfix]
        private static void Postfix(Item __result)
        {
            DynamicItemRegistrationPatchSupport.InitializeInstance(__result);
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

        // 静态 InstantiateAsync 的方法体是编译器生成的状态机，反编译源里看不到它到底
        // 走不走 InstantiateAsync_Local。不能靠猜：这里也包一层，InitializeInstance
        // 自身幂等（Master 已经是自己就早返），重复初始化没有代价，漏一条路径就是崩溃。
        [HarmonyPostfix]
        private static void Postfix(int typeID, ref UniTask<Item> __result)
        {
            if (BossRushDynamicItemRegistry.IsBossRushDynamicItemType(typeID))
                __result = DynamicItemRegistrationPatchSupport.InitializeAsyncInstance(__result);
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

        [HarmonyPostfix]
        private static void Postfix(int typeID, ref UniTask<Item> __result)
        {
            if (BossRushDynamicItemRegistry.IsBossRushDynamicItemType(typeID))
                __result = DynamicItemRegistrationPatchSupport.InitializeAsyncInstance(__result);
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
            DynamicItemRegistrationPatchSupport.InitializeInstance(__result);
            return false;
        }
    }
}
