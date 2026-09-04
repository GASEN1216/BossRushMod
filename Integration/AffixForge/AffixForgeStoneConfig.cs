// ============================================================================
// AffixForgeStoneConfig.cs - 词缀熔石（材料）物品配置
// ============================================================================
// TypeID 500060（台账见 docs/Bossrush使用物品ID表.md 与 AGENTS.md 4.3）。
//
// 设计要点：
//   - **纯材料**：词缀的身份写在装备自己的 `AFX_` KV 上，本 TypeID 只是"锻造要花的
//     那块石头"。它不带任何 Stat / Modifier，也不进战斗归因，因此可以放心堆叠。
//   - **MaxStack = 20**：与遗种蛋相反——熔石之间完全同质，堆叠不丢任何信息。
//   - **零新增 bundle**：没有专属模型，走 BossRushDynamicItemRegistry 的
//     FallbackLoader 从既有物品克隆一份 prefab 顶上（形态逐字照 RelicEggConfig）。
//   - 图标读 `Assets/Items/affix_forge_stone.png`；缺文件时保持克隆源的图标，
//     不 fail、不抛异常。
//
// 硬约束：
//   - DisplayNameRaw 设为 LOC_KEY_DISPLAY，必须配 InjectLocalization()（AGENTS 4.4），
//     否则游戏内会显示 *BossRush_AffixForgeStone*。
//   - 本文件零事件订阅、零 Harmony、零反射策略新增。
// ============================================================================

using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>词缀熔石物品配置。词缀锻造的唯一消耗材料。</summary>
    public static class AffixForgeStoneConfig
    {
        #region 常量

        /// <summary>词缀熔石 TypeID。</summary>
        public const int TYPE_ID = BossRushItemIds.AffixForgeStone;

        /// <summary>运行时克隆 prefab 的 GameObject 名。</summary>
        public const string PREFAB_NAME = "BossRush_AffixForgeStone";

        /// <summary>显示名本地化键。</summary>
        public const string LOC_KEY_DISPLAY = "BossRush_AffixForgeStone";

        public const string DISPLAY_NAME_CN = "词缀熔石";
        public const string DISPLAY_NAME_EN = "Affix Forge Stone";

        public const string DESCRIPTION_CN = "从 Boss 残骸里烧出来的一块半熔矿石，还带着它主人的脾气。"
            + "拿去找哥布林做词缀锻造，它会把这股脾气刻进你的武器或护甲。"
            + "重铸一次要一块，锁住一条已有词缀要两块。";
        public const string DESCRIPTION_EN = "A half-molten ore burned out of a boss's remains, still carrying its "
            + "owner's temper. Bring it to the goblin for affix forging and that temper gets etched into your "
            + "weapon or armor. One stone per reroll, two to lock an affix you want to keep.";

        /// <summary>
        /// 图标资源名。EquipmentHelperIcon 会按 Assets/Items/{ICON_NAME}.png 找 PNG，
        /// 缺文件时保持克隆源的图标（不 fail）。
        /// </summary>
        public const string ICON_NAME = "affix_forge_stone";

        public const int VALUE = 8000;
        public const int MAX_STACK = 20;
        public const int QUALITY = 5;

        #endregion

        private static bool runtimeFallbackRegistered;

        #region 显示文本

        public static string GetDisplayName()
        {
            return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);
        }

        public static string GetDescription()
        {
            return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);
        }

        #endregion

        #region 注册

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[AffixForgeStoneConfig] Registered item configurator");
        }

        public static void ConfigureItem(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                ModeFItemConfigHelper.ClearInheritedUsage(item);
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.name = DISPLAY_NAME_EN;
                // 熔石之间完全同质，堆叠不丢信息，因此允许堆叠
                item.MaxStackCount = MAX_STACK;
                item.StackCount = 1;
                item.Value = VALUE;
                item.Quality = QUALITY;
                ModeFItemConfigHelper.SetHiddenMember(item, "description", GetDescription());
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", GetDescription());
                EquipmentHelper.AddTagToItem(item, "Special");
                // 专属图标：没有它就会顶着克隆源物品的脸出现在背包里
                EquipmentHelperIcon.TryInjectIcon(item, null, ICON_NAME);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeStoneConfig] ConfigureItem failed: " + e.Message);
            }
        }

        #endregion

        #region 运行时兜底注册（零新增 bundle）

        /// <summary>
        /// 没有专属 bundle，因此从既有物品克隆一个 prefab 顶上。
        /// 形态照 RelicEggConfig.EnsureRuntimeRegistration。
        /// </summary>
        public static bool EnsureRuntimeRegistration()
        {
            try
            {
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(TYPE_ID); }
                catch (Exception)
                {
                    // prefab 查询失败按"尚未注册"处理，继续走克隆路径
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
                    ModBehaviour.DevLog("[AffixForgeStoneConfig] No runtime fallback source item was found");
                    return false;
                }

                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null)
                {
                    return false;
                }

                clone.gameObject.name = PREFAB_NAME;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);
                clone.SetTypeID(TYPE_ID);
                ConfigureItem(clone);
                ItemAssetsCollection.AddDynamicEntry(clone);
                runtimeFallbackRegistered = true;
                ModBehaviour.DevLog("[AffixForgeStoneConfig] Runtime fallback item registered");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeStoneConfig] EnsureRuntimeRegistration failed: " + e.Message);
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
                BossRushItemIds.RelicEgg,
                BossRushItemIds.PortableSafeZoneDevice,
                BossRushItemIds.ZombieTideBeacon,
                BossRushItemIds.ZombieTideInvitation,
                AwenLootSweepTokenConfig.TYPE_ID,
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

        #region 本地化

        public static void InjectLocalization()
        {
            try
            {
                string displayName = GetDisplayName();
                string description = GetDescription();
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY, displayName);
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY + "_Desc", description);
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID, displayName);
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID + "_Desc", description);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN, displayName);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_EN, displayName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeStoneConfig] InjectLocalization failed: " + e.Message);
            }
        }

        #endregion
    }
}
