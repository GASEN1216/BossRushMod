// ============================================================================
// SkyIslandNavInstrumentConfig.cs - 失落的航向仪（Jeff 序章「云上的坐标」的交付物）
// ============================================================================
// TypeID 500103（台账见 docs/reference/Bossrush使用物品ID表.md、docs/contracts.md §1 与 AGENTS.md §4.3）。
//
// 【从哪来】零号区的「断风游猎 · 守」倒下后，航向仪留在官方尸体箱里（SkyIslandPreludeFlow 在
//   BeforeCharacterSpawnLootOnDead 把它塞进头目库存，由官方 InteractableLootbox 一起收进箱子）。
//   箱子被炸掉、背包塞满一类意外，可以回到航向仪残骸那里再拆一具（同一入口，只在手上一具都没有时开放）。
// 【拿来做什么】带回基地交给 Jeff，换 5000 金钱与晴岚航线。交付时消耗掉，不卖钱（Value = 0），
//   也不进任何随机奖池（登记掉落黑名单）。
// 【串到哪条线】天空岛主线的第一环：接取 → 零号区取物 → 回基地交付 → 开船点。
//
// 零新增 bundle：形态照 Integration/AffixForge/AffixForgeStoneConfig.cs，从既有 Mod 物品克隆一个
// prefab 顶上，图标读 Assets/Items/<IconName>.png，缺图时退回风标罗盘的图（同为航海仪器，观感不跑偏）。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>失落的航向仪：注册、配置、本地化，以及序章要用的「有没有 / 发一具 / 收一具」三个入口。</summary>
    public static class SkyIslandNavInstrumentConfig
    {
        public const int TYPE_ID = 500103;
        public const string PREFAB_NAME = "BossRush_SkyIsland_NavInstrument";
        public const string LOC_KEY_DISPLAY = "BossRush_SkyIsland_NavInstrument";
        private const string ICON_NAME = "sky_island_nav_instrument";
        /// <summary>缺专属图标时的退路：风标罗盘也是航海仪器，不会让任务物品顶着别的物品的脸。</summary>
        private const string ICON_FALLBACK = "sky_island_wind_vane_compass";
        private const string LogPrefix = "[SkyIslandNavInstrument] ";

        private static bool runtimeFallbackRegistered;

        public static string GetDisplayName()
        {
            return L10n.T("失落的航向仪", "Lost Navigation Instrument");
        }

        public static string GetDescription()
        {
            return L10n.T(
                "从云上掉下来的仪器，外壳烧穿了，指针还在慢慢转。\n里面存着一串不属于地面的坐标。\n把它交给基地的 Jeff。",
                "An instrument fallen from above the clouds. The casing is burned through, but the needle still turns.\nInside is a set of coordinates that belong to no place on the ground.\nHand it to Jeff back at base.");
        }

        #region 注册与配置

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog(LogPrefix + "物品配置器已注册");
        }

        public static void ConfigureItem(Item item)
        {
            if (item == null) return;
            try
            {
                ModeFItemConfigHelper.ClearInheritedUsage(item);
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.name = "Lost Navigation Instrument";
                item.MaxStackCount = 1;
                item.StackCount = 1;
                // 卖价 0：残骸那边留了一条「手上没有就能再拆一具」的兜底，任何正数售价都会变成刷钱口。
                item.Value = 0;
                item.Quality = 4;
                ModeFItemConfigHelper.SetHiddenMember(item, "description", GetDescription());
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", GetDescription());
                EquipmentHelper.AddTagToItem(item, "Key");
                EquipmentHelper.AddTagToItem(item, "SpecialKey");
                EquipmentHelper.AddTagToItem(item, "Special");
                if (!EquipmentHelperIcon.TryInjectIcon(item, null, ICON_NAME))
                    EquipmentHelperIcon.TryInjectIcon(item, null, ICON_FALLBACK);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "配置物品失败: " + e.Message);
            }
        }

        #endregion

        #region 运行时兜底注册（零新增 bundle）

        /// <summary>从既有物品克隆一个 prefab 顶上。形态照 AffixForgeStoneConfig.EnsureRuntimeRegistration。</summary>
        public static bool EnsureRuntimeRegistration()
        {
            try
            {
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(TYPE_ID); }
                catch (Exception)
                {
                    // prefab 查询失败按「尚未注册」处理，继续走克隆路径
                }
                if (existing != null)
                {
                    ConfigureItem(existing);
                    return true;
                }

                existing = ItemFactory.GetLoadedItem(TYPE_ID);
                if (existing != null)
                {
                    ConfigureItem(existing);
                    try { ItemAssetsCollection.AddDynamicEntry(existing); }
                    catch (Exception)
                    {
                        // 已在表内时重复登记会抛，忽略即可
                    }
                    return true;
                }

                if (runtimeFallbackRegistered)
                {
                    try { return ItemAssetsCollection.GetPrefab(TYPE_ID) != null; }
                    catch (Exception) { return false; }
                }

                Item source = FindRuntimeFallbackSource();
                if (source == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "找不到可克隆的兜底物品");
                    return false;
                }

                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null) return false;
                clone.gameObject.name = PREFAB_NAME;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);
                clone.SetTypeID(TYPE_ID);
                ConfigureItem(clone);
                ItemAssetsCollection.AddDynamicEntry(clone);
                runtimeFallbackRegistered = true;
                ModBehaviour.DevLog(LogPrefix + "运行时兜底物品已注册");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "兜底注册失败: " + e.Message);
                return false;
            }
        }

        /// <summary>供 BossRushDynamicItemRegistry 的 FallbackLoader 调用。</summary>
        public static bool EnsureRuntimeFallbackRegistrationShell()
        {
            return EnsureRuntimeRegistration();
        }

        private static Item FindRuntimeFallbackSource()
        {
            int[] fallbackIds =
            {
                BossRushItemIds.SkyIslandWindVaneCompass,
                BossRushItemIds.RelicEgg,
                BossRushItemIds.PortableSafeZoneDevice,
                BossRushItemIds.ZombieTideBeacon,
                BossRushItemIds.ZombieTideInvitation,
            };
            for (int i = 0; i < fallbackIds.Length; i++)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(fallbackIds[i]);
                    if (prefab != null) return prefab;
                }
                catch (Exception)
                {
                    // 该候选不可用，继续下一个
                }
                try
                {
                    Item loaded = ItemFactory.GetLoadedItem(fallbackIds[i]);
                    if (loaded != null) return loaded;
                }
                catch (Exception)
                {
                    // 同上
                }
            }
            return null;
        }

        #endregion

        #region 序章要用的三个入口

        /// <summary>玩家（背包 + 仓库 + 宠物包）现在有几具航向仪。官方 ItemUtilities 同一口径。</summary>
        internal static int CountOwned()
        {
            try { return ItemUtilities.GetItemCount(TYPE_ID); }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// 把一具航向仪塞进头目库存，随官方尸体箱一起掉出来。
        /// 官方 <c>InteractableLootbox.CreateFromItem</c> 在 BeforeCharacterSpawnLootOnDead 之后才建箱，
        /// 所以这里加进去的东西会和它原本的掉落一起出现在同一个箱子里。
        /// </summary>
        internal static bool TryDropInto(CharacterMainControl boss)
        {
            Item bossItem = boss == null ? null : boss.CharacterItem;
            Inventory inventory = bossItem == null ? null : bossItem.Inventory;
            if (inventory == null) return false;
            Item instrument = TryCreate();
            if (instrument == null) return false;
            try
            {
                EnsureExtraCapacity(inventory);
                if (inventory.AddAndMerge(instrument, 0)) return true;
                ModBehaviour.DevLog(LogPrefix + "头目库存放不下航向仪，本次不掉落（残骸处仍可再拆一具）");
                DestroyQuietly(instrument);
                return false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "掉落失败: " + e.Message);
                DestroyQuietly(instrument);
                return false;
            }
        }

        /// <summary>直接发一具给玩家（残骸兜底）。放不进背包时由官方寄回仓库。</summary>
        internal static bool TryGiveToPlayer()
        {
            Item instrument = TryCreate();
            if (instrument == null) return false;
            try
            {
                ItemUtilities.SendToPlayer(instrument);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "发放失败: " + e.Message);
                DestroyQuietly(instrument);
                return false;
            }
        }

        /// <summary>交付时收走一具。多出来的（兜底重复拆出的）不动，让玩家自己丢。</summary>
        internal static bool TryConsumeOne()
        {
            try
            {
                List<Item> owned = ItemUtilities.FindAllBelongsToPlayer(
                    delegate(Item e) { return e != null && e.TypeID == TYPE_ID; });
                if (owned == null) return false;
                for (int i = 0; i < owned.Count; i++)
                {
                    Item item = owned[i];
                    if (item == null) continue;
                    item.Detach();
                    item.DestroyTree();
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "回收失败: " + e.Message);
                return false;
            }
        }

        private static Item TryCreate()
        {
            try
            {
                BossRushDynamicItemRegistry.EnsureRegistered(TYPE_ID);
                // 缺资源时官方会给一个带同样 TypeID 的空壳，回读分辨不出来；先问 prefab 再实例化。
                if (ItemAssetsCollection.GetPrefab(TYPE_ID) == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "航向仪资源尚未就绪，本次不产出");
                    return null;
                }
                Item instrument = ItemAssetsCollection.InstantiateSync(TYPE_ID);
                if (instrument == null || instrument.TypeID != TYPE_ID)
                {
                    DestroyQuietly(instrument);
                    return null;
                }
                return instrument;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "实例化失败: " + e.Message);
                return null;
            }
        }

        private static void DestroyQuietly(Item item)
        {
            if (item == null) return;
            try { item.DestroyTree(); }
            catch (Exception)
            {
                // 回收失败只丢引用，不阻断流程
            }
        }

        private static void EnsureExtraCapacity(Inventory inventory)
        {
            if (inventory == null) return;
            try
            {
                int contentCount = inventory.Content != null ? inventory.Content.Count : 0;
                int required = Mathf.Max(inventory.Capacity, contentCount + 1);
                if (required > inventory.Capacity) inventory.SetCapacity(required);
            }
            catch (Exception)
            {
                // 扩容失败时 AddAndMerge 会自己失败，届时按「不掉落」降级
            }
        }

        #endregion

        #region 本地化

        /// <summary>DisplayNameRaw 设了就必须注入，否则游戏里显示 *BossRush_SkyIsland_NavInstrument*（AGENTS.md §4.4）。</summary>
        public static void InjectLocalization()
        {
            try
            {
                string displayName = GetDisplayName();
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY, displayName);
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY + "_Desc", GetDescription());
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID, displayName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "本地化注入失败: " + e.Message);
            }
        }

        internal static void ResetStaticCaches()
        {
            runtimeFallbackRegistered = false;
        }

        #endregion
    }
}
