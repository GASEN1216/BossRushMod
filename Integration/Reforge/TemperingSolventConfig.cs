// ============================================================================
// TemperingSolventConfig.cs - 回火液物品配置
// ============================================================================
// 模块说明：
//   冷淬液的反向道具：重铸界面里点一条已固定的属性，消耗一瓶解除固定（数值保留）。
//   零新增 bundle：从冷淬液 prefab 克隆一份顶上（瓶型一致），图标换成 Assets/Items/tempering_solvent.png。
//   获取：叮当商店（好感 4 级起）+ 叮当好感 6 级一次性赠送 2 瓶（老玩家对话即补发）。
//   owner 2026-10-12 授权作为「新商品不进叮当商店」规则的例外。
// ============================================================================

using System;
using ItemStatsSystem;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    public static class TemperingSolventConfig
    {
        public const int TYPE_ID = BossRushItemIds.TemperingSolvent;
        public const string PREFAB_NAME = "BossRush_TemperingSolvent";
        public const string ICON_NAME = "tempering_solvent";
        public const string LOC_KEY_DISPLAY = "BossRush_TemperingSolvent";

        public const string DISPLAY_NAME_CN = "回火液";
        public const string DISPLAY_NAME_EN = "Tempering Solvent";
        public const string DESCRIPTION_CN = "冷淬液的解药。在重铸界面点一条已经固定的属性，就能解除固定，数值不变，之后可以照常重铸。每瓶解一条。";
        public const string DESCRIPTION_EN = "The antidote to Cold Quench Fluid. In the reforge screen, click a locked stat to unlock it. The value stays, and it can be reforged again. One bottle per stat.";

        /// <summary>价格按冷淬液（5000）的 1.5 倍：解锁比锁定少用，但每次都是在纠正一次已经花过钱的选择。</summary>
        public const int ITEM_VALUE = 7500;

        /// <summary>叮当商店解锁等级与库存：与冷淬液同为 4 级，库存 3（每次开店重置）。</summary>
        public const int SHOP_UNLOCK_LEVEL = 4;
        public const int SHOP_MAX_STOCK = 3;
        /// <summary>叮当好感 6 级一次性赠送；已过门槛的老玩家下一次聊天补发。</summary>
        public const int GIFT_AFFINITY_LEVEL = 6;
        public const int GIFT_COUNT = 2;
        public const string GIFT_REWARD_KEY = "tempering_solvent_lv6_gift";

        private const string LogPrefix = "[TemperingSolvent] ";
        private static bool runtimeFallbackRegistered;

        public static string GetDisplayName() { return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN); }
        public static string GetDescription() { return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN); }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog(LogPrefix + "已注册物品配置器");
        }

        public static void ConfigureItem(Item item)
        {
            if (item == null) return;
            try
            {
                ModeFItemConfigHelper.ClearInheritedUsage(item);
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.name = DISPLAY_NAME_EN;
                item.Value = ITEM_VALUE;
                item.Quality = 5;
                item.MaxStackCount = 99;
                ModeFItemConfigHelper.SetHiddenMember(item, "description", GetDescription());
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", GetDescription());
                Tag reforgeTag = ColdQuenchFluidConfig.GetReforgeTag();
                if (reforgeTag != null && !item.Tags.Contains(reforgeTag)) item.Tags.Add(reforgeTag);
                EquipmentHelperIcon.TryInjectIcon(item, null, ICON_NAME);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "配置物品失败: " + e.Message);
            }
        }

        /// <summary>克隆冷淬液 prefab 顶上。形态照 SkyIslandNavInstrumentConfig.EnsureRuntimeRegistration。</summary>
        public static bool EnsureRuntimeRegistration()
        {
            try
            {
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(TYPE_ID); }
                catch (Exception) { /* 查询失败按尚未注册处理 */ }
                if (existing != null)
                {
                    ConfigureItem(existing);
                    return true;
                }
                if (runtimeFallbackRegistered)
                {
                    try { return ItemAssetsCollection.GetPrefab(TYPE_ID) != null; }
                    catch (Exception) { return false; }
                }

                Item source = null;
                try { BossRushDynamicItemRegistry.EnsureRegistered(ColdQuenchFluidConfig.TYPE_ID); }
                catch (Exception) { /* 冷淬液按需注册失败时继续找已加载实例 */ }
                try { source = ItemAssetsCollection.GetPrefab(ColdQuenchFluidConfig.TYPE_ID); }
                catch (Exception) { /* 继续找已加载实例 */ }
                if (source == null) source = ItemFactory.GetLoadedItem(ColdQuenchFluidConfig.TYPE_ID);
                if (source == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "找不到冷淬液作为克隆源");
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
                ModBehaviour.DevLog(LogPrefix + "运行时物品已注册");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "兜底注册失败: " + e.Message);
                return false;
            }
        }

        /// <summary>中英文名称、描述与物品 ID 键；语言切换后由 InjectLocalization_Extra_Integration 重新注入。</summary>
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
                ModBehaviour.DevLog(LogPrefix + "本地化注入失败: " + e.Message);
            }
        }
    }
}
