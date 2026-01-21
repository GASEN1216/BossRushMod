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
        private const float MAX_UPWARD_SPEED = 10f;             // 最大向上速度
        private const float ACCELERATION_TIME = 0.3f;           // 加速时间
        private const float GLIDING_SPEED_MULTIPLIER = 0.8f;     // 滑翔水平速度倍率
        private const float SLOW_DESCENT_SPEED = -2f;           // 缓慢下落速度
        private const float STARTUP_STAMINA_COST = 5f;          // 启动体力消耗
        private const float STAMINA_DRAIN_PER_SECOND = 50f;     // 飞行体力消耗
        private const float SLOW_DESCENT_STAMINA_DRAIN = 30f;   // 滑翔体力消耗

        // ========== 本地化键 ==========
        private const string LOC_KEY_DISPLAY = "BossRush_FlightTotem";        // 物品显示名本地化键
        private const string LOC_KEY_DESC = "BossRush_FlightTotem_Desc";        // 物品描述本地化键

        // ========== 属性键（全部使用 CustomData 字符对字符样式）==========
        private const string VAR_ABILITY_KEY = "Flight_Ability";               // 翻滚键
        private const string VAR_MAX_UPWARD_SPEED = "Flight_MaxUpwardSpeed";   // 最大向上速度
        private const string VAR_ACCELERATION_TIME = "Flight_AccelerationTime"; // 加速时间
        private const string VAR_GLIDING_MULTIPLIER = "Flight_GlidingMultiplier"; // 滑翔水平移动系数
        private const string VAR_DESCENT_SPEED = "Flight_DescentSpeed";        // 缓慢下落速度
        private const string VAR_STARTUP_STAMINA = "Flight_StartupStamina";    // 启动体力消耗
        private const string VAR_FLIGHT_STAMINA_DRAIN = "Flight_FlightStaminaDrain"; // 飞行体力消耗
        private const string VAR_GLIDING_STAMINA_DRAIN = "Flight_GlidingStaminaDrain"; // 滑翔体力消耗

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

                // 翻滚键：腾云
                string abilityValue = L10n.IsChinese ? "腾云" : "Soar";
                item.Variables.Set(VAR_ABILITY_KEY, abilityValue, true);
                item.Variables.SetDisplay(VAR_ABILITY_KEY, true);

                // 最大向上速度：10
                item.Variables.Set(VAR_MAX_UPWARD_SPEED, MAX_UPWARD_SPEED.ToString("0.#"), true);
                item.Variables.SetDisplay(VAR_MAX_UPWARD_SPEED, true);

                // 加速时间：0.3
                item.Variables.Set(VAR_ACCELERATION_TIME, ACCELERATION_TIME.ToString("0.#") + "s", true);
                item.Variables.SetDisplay(VAR_ACCELERATION_TIME, true);

                // 滑翔水平系数：0.8
                item.Variables.Set(VAR_GLIDING_MULTIPLIER, GLIDING_SPEED_MULTIPLIER.ToString("0.#"), true);
                item.Variables.SetDisplay(VAR_GLIDING_MULTIPLIER, true);

                // 缓慢下落速度：-2
                item.Variables.Set(VAR_DESCENT_SPEED, SLOW_DESCENT_SPEED.ToString("0.#"), true);
                item.Variables.SetDisplay(VAR_DESCENT_SPEED, true);

                // 启动体力消耗：5
                item.Variables.Set(VAR_STARTUP_STAMINA, STARTUP_STAMINA_COST.ToString("0.#"), true);
                item.Variables.SetDisplay(VAR_STARTUP_STAMINA, true);

                // 飞行体力消耗：50/s
                item.Variables.Set(VAR_FLIGHT_STAMINA_DRAIN, STAMINA_DRAIN_PER_SECOND.ToString("0.#") + "/s", true);
                item.Variables.SetDisplay(VAR_FLIGHT_STAMINA_DRAIN, true);

                // 滑翔体力消耗：30/s
                item.Variables.Set(VAR_GLIDING_STAMINA_DRAIN, SLOW_DESCENT_STAMINA_DRAIN.ToString("0.#") + "/s", true);
                item.Variables.SetDisplay(VAR_GLIDING_STAMINA_DRAIN, true);

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

            // ========== CustomData 键本地化（Var_ 前缀）==========
            // 翻滚键
            LocalizationHelper.InjectLocalization("Var_" + VAR_ABILITY_KEY, L10n.T("翻滚键", "Dash"));
            LocalizationHelper.InjectLocalization("腾云", "腾云");
            LocalizationHelper.InjectLocalization("Soar", "Soar");

            // 最大向上速度
            LocalizationHelper.InjectLocalization("Var_" + VAR_MAX_UPWARD_SPEED, L10n.T("最大向上速度", "Max Upward Speed"));

            // 加速时间
            LocalizationHelper.InjectLocalization("Var_" + VAR_ACCELERATION_TIME, L10n.T("加速时间", "Acceleration Time"));

            // 滑翔水平移动系数
            LocalizationHelper.InjectLocalization("Var_" + VAR_GLIDING_MULTIPLIER, L10n.T("滑翔水平移动系数", "Gliding Move Multiplier"));

            // 缓慢下落速度
            LocalizationHelper.InjectLocalization("Var_" + VAR_DESCENT_SPEED, L10n.T("缓慢下落速度", "Slow Descent Speed"));

            // 启动体力消耗
            LocalizationHelper.InjectLocalization("Var_" + VAR_STARTUP_STAMINA, L10n.T("启动体力消耗", "Startup Stamina"));

            // 飞行体力消耗
            LocalizationHelper.InjectLocalization("Var_" + VAR_FLIGHT_STAMINA_DRAIN, L10n.T("飞行体力消耗", "Flight Stamina Drain"));

            // 滑翔体力消耗
            LocalizationHelper.InjectLocalization("Var_" + VAR_GLIDING_STAMINA_DRAIN, L10n.T("滑翔体力消耗", "Gliding Stamina Drain"));

            // ========== 属性值本地化（防止显示星号）==========
            // 最大向上速度值
            string maxSpeedVal = MAX_UPWARD_SPEED.ToString("0.#");
            LocalizationHelper.InjectLocalization(maxSpeedVal, maxSpeedVal);

            // 加速时间值
            string accelVal = ACCELERATION_TIME.ToString("0.#") + "s";
            LocalizationHelper.InjectLocalization(accelVal, accelVal);

            // 滑翔水平系数值
            string glidingVal = GLIDING_SPEED_MULTIPLIER.ToString("0.#");
            LocalizationHelper.InjectLocalization(glidingVal, glidingVal);

            // 缓慢下落速度值
            string descentVal = SLOW_DESCENT_SPEED.ToString("0.#");
            LocalizationHelper.InjectLocalization(descentVal, descentVal);

            // 启动体力消耗值
            string startupVal = STARTUP_STAMINA_COST.ToString("0.#");
            LocalizationHelper.InjectLocalization(startupVal, startupVal);

            // 飞行体力消耗值
            string flightDrainVal = STAMINA_DRAIN_PER_SECOND.ToString("0.#") + "/s";
            LocalizationHelper.InjectLocalization(flightDrainVal, flightDrainVal);

            // 滑翔体力消耗值
            string glidingDrainVal = SLOW_DESCENT_STAMINA_DRAIN.ToString("0.#") + "/s";
            LocalizationHelper.InjectLocalization(glidingDrainVal, glidingDrainVal);

            ModBehaviour.DevLog("[FlightTotemConfig] 本地化注入完成");
        }
    }
}
