// ============================================================================
// FlightTotemFactory.cs - 飞行图腾物品工厂
// ============================================================================
// 模块说明：
//   从 Unity AssetBundle 加载飞行图腾物品
//   使用 EquipmentFactory 统一加载流程
// ============================================================================
// Unity 资源要求：
//   - AssetBundle 名称：flight_totem
//   - 路径：Assets/Equipment/flight_totem
//   - Prefab 命名：FlightTotem_Lv{等级}_Item
//   - 必须配置 Item 组件和 typeID
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    /// <summary>
    /// 飞行图腾物品工厂 - 使用 partial class 扩展 ModBehaviour
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 状态 ==========

        private bool flightTotemInitialized = false;
        private int flightTotemTypeId = FlightConfig.TotemTypeIdBase;
        private Item flightTotemPrefab = null;

        // ========== 初始化 ==========

        /// <summary>
        /// 初始化飞行图腾物品 - 从 Unity AssetBundle 加载
        /// </summary>
        private void InitializeFlightTotemItem()
        {
            if (flightTotemInitialized) return;
            flightTotemInitialized = true;

            try
            {
                // 使用 EquipmentFactory 加载飞行图腾 AssetBundle
                int count = EquipmentFactory.LoadBundle("flight_totem");
                
                if (count > 0)
                {
                    // 获取加载的图腾物品
                    flightTotemPrefab = GetLoadedFlightTotem();
                    
                    if (flightTotemPrefab != null)
                    {
                        flightTotemTypeId = flightTotemPrefab.TypeID;
                        DevLog($"[FlightTotem] 成功加载飞行图腾: TypeID={flightTotemTypeId}");

                        // 配置飞行图腾属性
                        ConfigureFlightTotemEquipment(flightTotemPrefab);
                    }
                    else
                    {
                        DevLog("[FlightTotem] 未找到飞行图腾物品，请检查 Unity Prefab 配置");
                    }
                }
                else
                {
                    DevLog("[FlightTotem] 加载飞行图腾 AssetBundle 失败，请检查 Assets/Equipment/flight_totem 是否存在");
                }
            }
            catch (Exception e)
            {
                DevLog($"[FlightTotem] 初始化失败: {e.Message}");
            }
        }

        /// <summary>
        /// 配置飞行图腾装备属性（使用 Float 存储以支持重铸）
        /// </summary>
        private void ConfigureFlightTotemEquipment(Item item)
        {
            if (item == null) return;

            try
            {
                var config = FlightConfig.Instance;
                DevLog("[FlightTotem] 配置飞行图腾属性...");

                // 最大向上速度（Float存储，支持重铸）
                item.Variables.Set(FlightConfig.VAR_MAX_UPWARD_SPEED, config.MaxUpwardSpeed);
                item.Variables.SetDisplay(FlightConfig.VAR_MAX_UPWARD_SPEED, true);

                // 加速时间（Float存储，支持重铸）
                item.Variables.Set(FlightConfig.VAR_ACCELERATION_TIME, config.AccelerationTime);
                item.Variables.SetDisplay(FlightConfig.VAR_ACCELERATION_TIME, true);

                // 滑翔水平系数（Float存储，支持重铸）
                item.Variables.Set(FlightConfig.VAR_GLIDING_MULTIPLIER, config.GlidingHorizontalSpeedMultiplier);
                item.Variables.SetDisplay(FlightConfig.VAR_GLIDING_MULTIPLIER, true);

                // 缓慢下落速度（Float存储，取绝对值便于显示）
                item.Variables.Set(FlightConfig.VAR_DESCENT_SPEED, Mathf.Abs(config.SlowDescentSpeed));
                item.Variables.SetDisplay(FlightConfig.VAR_DESCENT_SPEED, true);

                // 启动体力消耗（Float存储，支持重铸）
                item.Variables.Set(FlightConfig.VAR_STARTUP_STAMINA, config.StartupStaminaCost);
                item.Variables.SetDisplay(FlightConfig.VAR_STARTUP_STAMINA, true);

                // 飞行体力消耗（Float存储，支持重铸）
                item.Variables.Set(FlightConfig.VAR_FLIGHT_STAMINA_DRAIN, config.StaminaDrainPerSecond);
                item.Variables.SetDisplay(FlightConfig.VAR_FLIGHT_STAMINA_DRAIN, true);

                // 滑翔体力消耗（Float存储，支持重铸）
                item.Variables.Set(FlightConfig.VAR_GLIDING_STAMINA_DRAIN, config.SlowDescentStaminaDrainPerSecond);
                item.Variables.SetDisplay(FlightConfig.VAR_GLIDING_STAMINA_DRAIN, true);

                DevLog("[FlightTotem] 属性配置完成（Float存储，支持重铸）");
            }
            catch (Exception e)
            {
                DevLog($"[FlightTotem] 配置属性失败: {e.Message}");
            }
        }

        /// <summary>
        /// 获取已加载的飞行图腾物品
        /// </summary>
        private Item GetLoadedFlightTotem()
        {
            var config = FlightConfig.Instance;
            
            // 尝试从 EquipmentFactory 缓存中获取
            if (EquipmentFactory.IsCustomEquipment(config.ItemTypeId))
            {
                // 检查是否是武器类型（虽然图腾不是武器，但使用统一接口）
                Item totem = EquipmentFactory.GetLoadedGun(config.ItemTypeId);
                if (totem != null) return totem;
                
                // 检查模型缓存（图腾可能只有 Item 没有 Model）
                var models = EquipmentFactory.GetLoadedModels();
                if (models.ContainsKey(config.ItemTypeId))
                {
                    // 从 ItemAssetsCollection 获取
                    return ItemAssetsCollection.InstantiateSync(config.ItemTypeId);
                }
            }
            
            return null;
        }

        // ========== 本地化 ==========

        /// <summary>
        /// 注入飞行图腾本地化
        /// </summary>
        private void InjectFlightTotemLocalization()
        {
            var config = FlightConfig.Instance;

            // 使用统一的本地化键
            string displayName = L10n.T(config.DisplayNameCN, config.DisplayNameEN);
            string description = L10n.T(config.DescriptionCN, config.DescriptionEN);

            // 主键
            LocalizationHelper.InjectLocalization("BossRush_FlightTotem", displayName);
            LocalizationHelper.InjectLocalization("BossRush_FlightTotem_Desc", description);

            // 物品 ID 键
            string itemKey = "Item_" + config.ItemTypeId;
            LocalizationHelper.InjectLocalization(itemKey, displayName);
            LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);

            // 注入属性本地化
            InjectFlightTotemStatLocalizations();

            DevLog("[FlightTotem] 本地化注入完成");
        }

        /// <summary>
        /// 注入飞行图腾属性本地化
        /// 注意：CustomData 使用 "Var_" + 键 作为本地化键
        /// </summary>
        private void InjectFlightTotemStatLocalizations()
        {
            var config = FlightConfig.Instance;

            // 翻滚键
            LocalizationHelper.InjectLocalization("Var_" + FlightConfig.VAR_ABILITY_KEY, L10n.T("翻滚键", "Dash"));
            LocalizationHelper.InjectLocalization("腾云", "腾云");
            LocalizationHelper.InjectLocalization("Soar", "Soar");

            // 最大向上速度
            LocalizationHelper.InjectLocalization("Var_" + FlightConfig.VAR_MAX_UPWARD_SPEED, L10n.T("最大向上速度", "Max Upward Speed"));

            // 加速时间
            LocalizationHelper.InjectLocalization("Var_" + FlightConfig.VAR_ACCELERATION_TIME, L10n.T("加速时间", "Acceleration Time"));

            // 滑翔水平移动系数
            LocalizationHelper.InjectLocalization("Var_" + FlightConfig.VAR_GLIDING_MULTIPLIER, L10n.T("滑翔水平移动系数", "Gliding Move Multiplier"));

            // 缓慢下落速度
            LocalizationHelper.InjectLocalization("Var_" + FlightConfig.VAR_DESCENT_SPEED, L10n.T("缓慢下落速度", "Slow Descent Speed"));

            // 启动体力消耗
            LocalizationHelper.InjectLocalization("Var_" + FlightConfig.VAR_STARTUP_STAMINA, L10n.T("启动体力消耗", "Startup Stamina"));

            // 飞行体力消耗
            LocalizationHelper.InjectLocalization("Var_" + FlightConfig.VAR_FLIGHT_STAMINA_DRAIN, L10n.T("飞行体力消耗", "Flight Stamina Drain"));

            // 滑翔体力消耗
            LocalizationHelper.InjectLocalization("Var_" + FlightConfig.VAR_GLIDING_STAMINA_DRAIN, L10n.T("滑翔体力消耗", "Gliding Stamina Drain"));

            // ========== 属性值本地化（防止显示星号）==========
            // 最大向上速度值
            string maxSpeedVal = config.MaxUpwardSpeed.ToString("0.#");
            LocalizationHelper.InjectLocalization(maxSpeedVal, maxSpeedVal);

            // 加速时间值
            string accelVal = config.AccelerationTime.ToString("0.#") + "s";
            LocalizationHelper.InjectLocalization(accelVal, accelVal);

            // 滑翔水平系数值
            string glidingVal = config.GlidingHorizontalSpeedMultiplier.ToString("0.#");
            LocalizationHelper.InjectLocalization(glidingVal, glidingVal);

            // 缓慢下落速度值
            string descentVal = config.SlowDescentSpeed.ToString("0.#");
            LocalizationHelper.InjectLocalization(descentVal, descentVal);

            // 启动体力消耗值
            string startupVal = config.StartupStaminaCost.ToString("0.#");
            LocalizationHelper.InjectLocalization(startupVal, startupVal);

            // 飞行体力消耗值
            string flightDrainVal = config.StaminaDrainPerSecond.ToString("0.#") + "/s";
            LocalizationHelper.InjectLocalization(flightDrainVal, flightDrainVal);

            // 滑翔体力消耗值
            string glidingDrainVal = config.SlowDescentStaminaDrainPerSecond.ToString("0.#") + "/s";
            LocalizationHelper.InjectLocalization(glidingDrainVal, glidingDrainVal);
        }
    }
}
