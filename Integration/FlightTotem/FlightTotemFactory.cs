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
        /// 配置飞行图腾装备属性（类似龙装备的配置方式）
        /// </summary>
        private void ConfigureFlightTotemEquipment(Item item)
        {
            if (item == null) return;

            try
            {
                var config = FlightConfig.Instance;
                DevLog("[FlightTotem] 配置飞行图腾属性...");

                // 能力键：使用 CustomData 显示"翻滚键：腾云"（文字对文字）
                item.Variables.Set(FlightConfig.VAR_ABILITY_KEY, L10n.IsChinese ? "腾云" : "Soar", true);
                item.Variables.SetDisplay(FlightConfig.VAR_ABILITY_KEY, true);

                // 最大向上速度
                EquipmentHelper.AddModifierToItem(item, FlightConfig.STAT_MAX_UPWARD_SPEED, ModifierType.Add, config.MaxUpwardSpeed, true);

                // 加速时间
                EquipmentHelper.AddModifierToItem(item, FlightConfig.STAT_ACCELERATION_TIME, ModifierType.Add, config.AccelerationTime, true);

                // 滑翔水平速度倍率（直接显示数值）
                EquipmentHelper.AddModifierToItem(item, FlightConfig.STAT_GLIDING_SPEED, ModifierType.Add, config.GlidingHorizontalSpeedMultiplier, true);

                // 缓慢下落速度（负值）
                EquipmentHelper.AddModifierToItem(item, FlightConfig.STAT_DESCENT_SPEED, ModifierType.Add, config.SlowDescentSpeed, true);

                // 启动体力消耗
                EquipmentHelper.AddModifierToItem(item, FlightConfig.STAT_STARTUP_STAMINA, ModifierType.Add, config.StartupStaminaCost, true);

                // 飞行体力消耗
                EquipmentHelper.AddModifierToItem(item, FlightConfig.STAT_FLIGHT_STAMINA_DRAIN, ModifierType.Add, config.StaminaDrainPerSecond, true);

                // 下落体力消耗
                EquipmentHelper.AddModifierToItem(item, FlightConfig.STAT_DESCENT_STAMINA_DRAIN, ModifierType.Add, config.SlowDescentStaminaDrainPerSecond, true);

                DevLog("[FlightTotem] 属性配置完成");
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
        /// 注意：
        /// - Stat 使用 "Stat_" + 属性键 作为本地化键
        /// - CustomData 使用 "Var_" + 键 作为本地化键
        /// </summary>
        private void InjectFlightTotemStatLocalizations()
        {
            // 能力键：CustomData 本地化（Var_ 前缀）
            LocalizationHelper.InjectLocalization("Var_" + FlightConfig.VAR_ABILITY_KEY, L10n.T("翻滚键", "Dash"));

            // 最大向上速度
            LocalizationHelper.InjectLocalization("Stat_" + FlightConfig.STAT_MAX_UPWARD_SPEED, L10n.T("最大向上速度", "Max Upward Speed"));

            // 加速时间
            LocalizationHelper.InjectLocalization("Stat_" + FlightConfig.STAT_ACCELERATION_TIME, L10n.T("加速时间", "Acceleration Time"));

            // 滑翔水平速度
            LocalizationHelper.InjectLocalization("Stat_" + FlightConfig.STAT_GLIDING_SPEED, L10n.T("滑翔水平速度", "Gliding Speed"));

            // 缓慢下落速度
            LocalizationHelper.InjectLocalization("Stat_" + FlightConfig.STAT_DESCENT_SPEED, L10n.T("缓慢下落速度", "Slow Descent Speed"));

            // 启动体力消耗
            LocalizationHelper.InjectLocalization("Stat_" + FlightConfig.STAT_STARTUP_STAMINA, L10n.T("启动体力消耗", "Startup Stamina"));

            // 飞行体力消耗
            LocalizationHelper.InjectLocalization("Stat_" + FlightConfig.STAT_FLIGHT_STAMINA_DRAIN, L10n.T("飞行体力消耗", "Flight Stamina Drain"));

            // 下落体力消耗
            LocalizationHelper.InjectLocalization("Stat_" + FlightConfig.STAT_DESCENT_STAMINA_DRAIN, L10n.T("下落体力消耗", "Descent Stamina Drain"));
        }
    }
}
