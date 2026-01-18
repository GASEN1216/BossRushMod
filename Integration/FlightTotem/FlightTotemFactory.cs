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

            DevLog("[FlightTotem] 本地化注入完成");
        }
    }
}
