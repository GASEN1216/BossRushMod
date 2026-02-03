// ============================================================================
// ReverseScaleFactory.cs - 逆鳞图腾物品工厂
// ============================================================================
// 模块说明：
//   从 Unity AssetBundle 加载逆鳞图腾物品
//   使用 EquipmentFactory 统一加载流程
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    /// <summary>
    /// 逆鳞图腾物品工厂 - 使用 partial class 扩展 ModBehaviour
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 状态 ==========

        private bool reverseScaleInitialized = false;
        private int reverseScaleTypeId = ReverseScaleConfig.TotemTypeId;
        private Item reverseScalePrefab = null;

        // ========== 初始化 ==========

        /// <summary>
        /// 初始化逆鳞图腾物品 - 从 Unity AssetBundle 加载
        /// </summary>
        private void InitializeReverseScaleItem()
        {
            if (reverseScaleInitialized) return;
            reverseScaleInitialized = true;

            try
            {
                // 逆鳞图腾通过 EquipmentFactory.LoadBundle 统一加载
                // 配置在 ReverseScaleConfig.TryConfigure 中处理
                DevLog("[ReverseScale] 逆鳞图腾初始化完成");
            }
            catch (Exception e)
            {
                DevLog($"[ReverseScale] 初始化失败: {e.Message}");
            }
        }

        // ========== 本地化 ==========

        /// <summary>
        /// 注入逆鳞图腾本地化
        /// </summary>
        private void InjectReverseScaleLocalization()
        {
            var config = ReverseScaleConfig.Instance;

            // 使用统一的本地化键
            string displayName = L10n.T(config.DisplayNameCN, config.DisplayNameEN);
            string description = L10n.T(config.DescriptionCN, config.DescriptionEN);

            // 主键
            LocalizationHelper.InjectLocalization(ReverseScaleConfig.LOC_KEY_DISPLAY, displayName);
            LocalizationHelper.InjectLocalization(ReverseScaleConfig.LOC_KEY_DESC, description);

            // 物品 ID 键
            string itemKey = "Item_" + config.ItemTypeId;
            LocalizationHelper.InjectLocalization(itemKey, displayName);
            LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);

            // 气泡提示
            string bubbleText = L10n.T(ReverseScaleConfig.BUBBLE_TEXT_CN, ReverseScaleConfig.BUBBLE_TEXT_EN);
            LocalizationHelper.InjectLocalization(ReverseScaleConfig.LOC_KEY_BUBBLE, bubbleText);

            // 注入属性本地化
            InjectReverseScaleStatLocalizations();

            DevLog("[ReverseScale] 本地化注入完成");
        }

        /// <summary>
        /// 注入逆鳞图腾属性本地化
        /// 注意：CustomData 使用 "Var_" + 键 作为本地化键
        /// </summary>
        private void InjectReverseScaleStatLocalizations()
        {
            var config = ReverseScaleConfig.Instance;

            // 恢复生命百分比
            LocalizationHelper.InjectLocalization("Var_" + ReverseScaleConfig.VAR_HEAL_PERCENT, L10n.T("恢复生命百分比", "Health Restore %"));

            // 棱彩弹数量
            LocalizationHelper.InjectLocalization("Var_" + ReverseScaleConfig.VAR_BOLT_COUNT, L10n.T("棱彩弹数量", "Prismatic Bolts"));

            // ========== 属性值本地化（防止显示星号）==========
            // 恢复生命值
            string healValue = (config.HealPercent * 100).ToString("0") + "%";
            LocalizationHelper.InjectLocalization(healValue, healValue);

            // 棱彩弹数量
            string boltValue = config.PrismaticBoltCount.ToString();
            LocalizationHelper.InjectLocalization(boltValue, boltValue);
        }

        /// <summary>
        /// 尝试配置逆鳞图腾（供 EquipmentFactory 调用）
        /// </summary>
        public static bool TryConfigureReverseScale(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName)) return false;

            bool isReverseScale = baseName.Equals(ReverseScaleConfig.ItemBaseName, StringComparison.OrdinalIgnoreCase);

            if (isReverseScale)
            {
                ConfigureReverseScaleEquipment(item);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 配置逆鳞图腾装备属性
        /// </summary>
        private static void ConfigureReverseScaleEquipment(Item item)
        {
            if (item == null) return;

            try
            {
                var config = ReverseScaleConfig.Instance;
                DevLog("[ReverseScale] 配置逆鳞图腾属性...");

                // 设置物品的 displayName 字段（用于本地化）
                item.DisplayNameRaw = ReverseScaleConfig.LOC_KEY_DISPLAY;

                // 恢复生命值：存储为百分比整数（50 = 50%），便于显示和重铸
                float healPercentDisplay = config.HealPercent * 100f; // 0.5 -> 50
                item.Variables.Set(ReverseScaleConfig.VAR_HEAL_PERCENT, healPercentDisplay);
                item.Variables.SetDisplay(ReverseScaleConfig.VAR_HEAL_PERCENT, true);

                // 棱彩弹数量：使用Float存储，支持重铸修改
                item.Variables.Set(ReverseScaleConfig.VAR_BOLT_COUNT, (float)config.PrismaticBoltCount);
                item.Variables.SetDisplay(ReverseScaleConfig.VAR_BOLT_COUNT, true);

                DevLog("[ReverseScale] 逆鳞图腾配置完成 - HealPercent=" + config.HealPercent + ", BoltCount=" + config.PrismaticBoltCount);
            }
            catch (Exception e)
            {
                DevLog($"[ReverseScale] 配置属性失败: {e.Message}");
            }
        }
    }
}
