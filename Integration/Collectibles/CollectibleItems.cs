// ============================================================================
// CollectibleItems.cs - 收集品 100 件的注册与配置（零新增 bundle，克隆注册）
// ============================================================================
// 形态照 Integration/LastShift/LastShiftItems.cs：从既有 Mod 小件物品克隆 prefab、改 TypeID、配属性、
// 经 BossRushDynamicItemRegistry.Publish 注册；图标 collectible_<key> 由 IconNameOf 交给 ModItemIcons，注册时统一套用。
// 配置要点：
//   - 先清掉克隆源的标签，只挂 Special 与 ShowCase：天空岛贵重品类按 Luxury / Gem 抽（SkyIslandLootPools），
//     带上这两个标签就会被岛上随机池抽走；官方搜箱按 requireTags 随机抽，也抽不到只带 Special 的物品。
//     ShowCase（展示品）让收集品能摆上官方陈列柜（2026-11-19 owner）。按字节读游戏资源核过：官方所有随机选物
//     （开箱 LootBoxLoader、地面 LootSpawner、敌人 RandomItemGenerateDescription、黑市）都排除 Special，
//     也没有任何掉落配置按 ShowCase 抽，所以 Special 必须留着。
//   - 清掉克隆源的 ItemGraphic：3D 模型由 CollectibleShowcaseModels 在官方生成模型时按需挂上。
//   - 可以卖（不挂 NotSellable）；不可堆叠；没有使用行为、没有耐久条。
//   - 掉落黑名单在 Config/LootBlacklistRegistry.cs 与 Assets/Data/LootBlacklist.json（500119-500219）。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal static class CollectibleItems
    {
        private static bool _configuratorsRegistered;
        private static readonly HashSet<int> _runtimeRegistered = new HashSet<int>();

        internal static string IconName(CollectibleDefinition def)
        {
            return "collectible_" + def.Key;
        }

        /// <summary>收集品与收藏册的图标名，由 ModItemIcons 统一取用。</summary>
        internal static string IconNameOf(int typeId)
        {
            if (typeId == CollectionBookItem.TypeId) return CollectionBookItem.IconName;
            CollectibleDefinition def = CollectibleCatalog.Get(typeId);
            return def != null ? IconName(def) : null;
        }

        /// <summary>幂等注册 100 件收集品与收藏册的配置器（ItemContentRegistry 调用）。</summary>
        internal static void RegisterConfigurators()
        {
            if (_configuratorsRegistered) return;
            _configuratorsRegistered = true;
            for (int i = 0; i < CollectibleCatalog.Count; i++)
            {
                CollectibleDefinition def = CollectibleCatalog.At(i);
                if (def == null) continue;
                int typeId = def.TypeId;
                ItemFactory.RegisterConfigurator(typeId, delegate(Item item) { ConfigureItem(typeId, item); });
            }
            CollectionBookItem.RegisterConfigurator();
            ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "物品配置器已注册 " + CollectibleCatalog.Count + " 件 + 收藏册");
        }

        internal static void ConfigureItem(int typeId, Item item)
        {
            if (item == null) return;
            CollectibleDefinition def = CollectibleCatalog.Get(typeId);
            if (def == null) return;
            try
            {
                ModeFItemConfigHelper.ClearInheritedUsage(item);
                if (item.Tags != null) item.Tags.Clear();
                item.DisplayNameRaw = def.LocKey;
                item.name = def.LocKey;
                item.MaxStackCount = 1;
                item.StackCount = 1;
                item.Value = def.Value;
                item.Quality = def.Quality;
                // 物品格光晕按档次显示；不写会沿用克隆源的红光晕（CR-2026-11-19-001）。
                item.DisplayQuality = (DisplayQuality)CollectibleTuning.DisplayQualityOf(def.Quality);
                ModeFItemConfigHelper.SetHiddenMember(item, "weight", def.Weight);
                EquipmentHelper.AddTagToItem(item, "Special");
                EquipmentHelper.AddTagToItem(item, "ShowCase");
                CollectibleShowcaseModels.ClearInheritedGraphic(item);
                item.MaxDurability = 0f;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "配置物品失败 " + typeId + ": " + e.Message);
            }
        }

        /// <summary>从既有物品克隆一个 prefab 顶上；供 BossRushDynamicItemRegistry 的 FallbackLoader 调用。</summary>
        internal static bool EnsureRuntimeRegistration(int typeId)
        {
            CollectibleDefinition def = CollectibleCatalog.Get(typeId);
            if (def == null) return false;
            return EnsureClone(typeId, def.LocKey, delegate(Item item) { ConfigureItem(typeId, item); });
        }

        /// <summary>收集品与收藏册共用的克隆注册（形态照 LastShiftItems.EnsureRuntimeRegistration）。</summary>
        internal static bool EnsureClone(int typeId, string objectName, Action<Item> configure)
        {
            try
            {
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(typeId); }
                catch (Exception)
                {
                    // prefab 查询失败按「尚未注册」处理，继续走克隆路径
                }
                if (existing != null) { configure(existing); return true; }
                existing = ItemFactory.GetLoadedItem(typeId);
                if (existing != null)
                {
                    configure(existing);
                    try { BossRushDynamicItemRegistry.Publish(existing); }
                    catch (Exception)
                    {
                        // 已在表内时重复登记会抛，忽略即可
                    }
                    return true;
                }
                if (_runtimeRegistered.Contains(typeId))
                {
                    try { return ItemAssetsCollection.GetPrefab(typeId) != null; }
                    catch (Exception) { return false; }
                }
                Item source = FindRuntimeFallbackSource();
                if (source == null) { ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "找不到可克隆的兜底物品: " + typeId); return false; }
                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null) return false;
                clone.gameObject.name = objectName;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);
                clone.SetTypeID(typeId);
                configure(clone);
                BossRushDynamicItemRegistry.Publish(clone);
                _runtimeRegistered.Add(typeId);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "兜底注册失败 " + typeId + ": " + e.Message);
                return false;
            }
        }

        /// <summary>克隆源与潮闸镇、末班编组站物品同一份顺序：都是零新增 bundle 的小件 Mod 物品。</summary>
        private static Item FindRuntimeFallbackSource()
        {
            int[] fallbackIds = { BossRushItemIds.RelicEgg, BossRushItemIds.PortableSafeZoneDevice, BossRushItemIds.ZombieTideBeacon, BossRushItemIds.ZombieTideInvitation };
            foreach (int id in fallbackIds)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(id);
                    if (prefab != null) return prefab;
                }
                catch (Exception e) { ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 查找兜底模板物品失败: " + e.Message); }
            }
            return null;
        }

        /// <summary>按 TypeID 同步实例化一件收集品；拿不到真 prefab（只会出 FallbackItem）时返回 null。</summary>
        internal static Item InstantiateChecked(int typeId)
        {
            Item item = null;
            try
            {
                if (ItemAssetsCollection.GetPrefab(typeId) == null && !EnsureRuntimeRegistration(typeId)) return null;
                if (ItemAssetsCollection.GetPrefab(typeId) == null) return null;
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null || item.TypeID != typeId) throw new InvalidOperationException("物品实例无效");
                return item;
            }
            catch (Exception e)
            {
                if (item != null) { try { item.DestroyTree(); } catch (Exception) { /* 只清理未交付的物品 */ } }
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "实例化失败 " + typeId + ": " + e.Message);
                return null;
            }
        }

        internal static void ResetStaticCaches()
        {
            _runtimeRegistered.Clear();
            _configuratorsRegistered = false;
            CollectionBookItem.ResetStaticCaches();
        }
    }
}
