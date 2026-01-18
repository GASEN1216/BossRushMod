// ============================================================================
// FlightTotemConfig.cs - 飞行图腾配置
// ============================================================================
// 模块说明：
//   飞行图腾的属性配置和初始化逻辑
//   - 飞行能力参数
//   - 本地化注入
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// 飞行图腾配置管理
    /// </summary>
    public static class FlightTotemConfig
    {
        // ========== 物品基础名（用于匹配 AssetBundle 中的 Prefab）==========
        private const string FLIGHT_TOTEM_LV1_BASE = "FlightTotem_Lv1";

        // ========== 飞行1阶配置 ==========
        private const float MAX_UPWARD_SPEED = 5f;              // 最大向上速度
        private const float ACCELERATION_TIME = 3f;              // 加速时间
        private const float GLIDING_SPEED_MULTIPLIER = 0.8f;     // 滑翔水平速度倍率
        private const float SLOW_DESCENT_SPEED = -2f;           // 缓慢下落速度
        private const float STARTUP_STAMINA_COST = 5f;          // 启动体力消耗
        private const float STAMINA_DRAIN_PER_SECOND = 50f;     // 飞行体力消耗
        private const float SLOW_DESCENT_STAMINA_DRAIN = 30f;   // 下落体力消耗

        // ========== 本地化键 ==========
        private const string LOC_KEY_DISPLAY = "BossRush_FlightTotem";        // 物品显示名本地化键
        private const string LOC_KEY_DESC = "BossRush_FlightTotem_Desc";        // 物品描述本地化键

        // ========== 属性键 ==========
        private const string VAR_ABILITY_KEY = "Flight_Ability";               // 能力键 CustomData
        private const string STAT_MAX_UPWARD_SPEED = "Flight_MaxUpwardSpeed";
        private const string STAT_ACCELERATION_TIME = "Flight_AccelerationTime";
        private const string STAT_GLIDING_SPEED = "Flight_GlidingSpeed";
        private const string STAT_DESCENT_SPEED = "Flight_DescentSpeed";
        private const string STAT_STARTUP_STAMINA = "Flight_StartupStamina";
        private const string STAT_FLIGHT_STAMINA_DRAIN = "Flight_FlightStaminaDrain";
        private const string STAT_DESCENT_STAMINA_DRAIN = "Flight_DescentStaminaDrain";

        /// <summary>
        /// 尝试配置飞行图腾（自动识别是否为飞行图腾物品）
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName)) return false;

            bool isFlightTotem = baseName.Equals(FLIGHT_TOTEM_LV1_BASE, StringComparison.OrdinalIgnoreCase);

            if (isFlightTotem)
            {
                ConfigureFlightTotemLv1(item);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 配置飞行1阶图腾
        /// </summary>
        private static void ConfigureFlightTotemLv1(Item item)
        {
            if (item == null) return;

            try
            {
                ModBehaviour.DevLog("[FlightTotemConfig] 配置飞行图腾1阶属性...");

                // ========== 设置物品的 displayName 字段（用于本地化） ==========
                item.DisplayNameRaw = LOC_KEY_DISPLAY;

                // 能力键：使用 CustomData 显示"翻滚键：腾云"（文字对文字）
                string abilityValue = L10n.IsChinese ? "腾云" : "Soar";
                item.Variables.Set(VAR_ABILITY_KEY, abilityValue, true);
                item.Variables.SetDisplay(VAR_ABILITY_KEY, true);

                // 最大向上速度
                EquipmentHelper.AddModifierToItem(item, STAT_MAX_UPWARD_SPEED, ModifierType.Add, MAX_UPWARD_SPEED, true);

                // 加速时间
                EquipmentHelper.AddModifierToItem(item, STAT_ACCELERATION_TIME, ModifierType.Add, ACCELERATION_TIME, true);

                // 滑翔水平速度倍率
                EquipmentHelper.AddModifierToItem(item, STAT_GLIDING_SPEED, ModifierType.Add, GLIDING_SPEED_MULTIPLIER, true);

                // 缓慢下落速度（负值）
                EquipmentHelper.AddModifierToItem(item, STAT_DESCENT_SPEED, ModifierType.Add, SLOW_DESCENT_SPEED, true);

                // 启动体力消耗
                EquipmentHelper.AddModifierToItem(item, STAT_STARTUP_STAMINA, ModifierType.Add, STARTUP_STAMINA_COST, true);

                // 飞行体力消耗
                EquipmentHelper.AddModifierToItem(item, STAT_FLIGHT_STAMINA_DRAIN, ModifierType.Add, STAMINA_DRAIN_PER_SECOND, true);

                // 下落体力消耗
                EquipmentHelper.AddModifierToItem(item, STAT_DESCENT_STAMINA_DRAIN, ModifierType.Add, SLOW_DESCENT_STAMINA_DRAIN, true);

                // 注入本地化
                InjectFlightTotemLocalization(item.TypeID);

                ModBehaviour.DevLog("[FlightTotemConfig] 飞行图腾1阶配置完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FlightTotemConfig] [ERROR] ConfigureFlightTotemLv1 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 注入飞行图腾本地化
        /// </summary>
        private static void InjectFlightTotemLocalization(int typeId)
        {
            // 物品名称和描述
            string displayName = L10n.T("腾云驾雾 I", "Cloud Soar I");
            string description = L10n.T("尔等凡鸭怎知我俯瞰众生的疲惫", "Mere ducks cannot fathom my exhaustion overlooking all beings");

            // 物品 displayName 字段对应的本地化键（重要！）
            LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY, displayName);
            LocalizationHelper.InjectLocalization(LOC_KEY_DESC, description);

            // 能力值本地化（防止显示 *腾云*）
            LocalizationHelper.InjectLocalization("腾云", "腾云");
            LocalizationHelper.InjectLocalization("Soar", "Soar");

            // 能力键：CustomData 本地化（Var_ 前缀）
            LocalizationHelper.InjectLocalization("Var_" + VAR_ABILITY_KEY, L10n.T("翻滚键", "Dash"));

            // 最大向上速度
            LocalizationHelper.InjectLocalization("Stat_" + STAT_MAX_UPWARD_SPEED, L10n.T("最大向上速度", "Max Upward Speed"));

            // 加速时间
            LocalizationHelper.InjectLocalization("Stat_" + STAT_ACCELERATION_TIME, L10n.T("加速时间", "Acceleration Time"));

            // 滑翔水平速度
            LocalizationHelper.InjectLocalization("Stat_" + STAT_GLIDING_SPEED, L10n.T("滑翔水平速度", "Gliding Speed"));

            // 缓慢下落速度
            LocalizationHelper.InjectLocalization("Stat_" + STAT_DESCENT_SPEED, L10n.T("缓慢下落速度", "Slow Descent Speed"));

            // 启动体力消耗
            LocalizationHelper.InjectLocalization("Stat_" + STAT_STARTUP_STAMINA, L10n.T("启动体力消耗", "Startup Stamina"));

            // 飞行体力消耗
            LocalizationHelper.InjectLocalization("Stat_" + STAT_FLIGHT_STAMINA_DRAIN, L10n.T("飞行体力消耗", "Flight Stamina Drain"));

            // 下落体力消耗
            LocalizationHelper.InjectLocalization("Stat_" + STAT_DESCENT_STAMINA_DRAIN, L10n.T("下落体力消耗", "Descent Stamina Drain"));

            ModBehaviour.DevLog("[FlightTotemConfig] 本地化注入完成");
        }
    }
}
