// ============================================================================
// FlightTotemFactory.cs - 飞行图腾物品工厂
// ============================================================================
// 模块说明：
//   创建和注册飞行1阶图腾物品
//   直接复用原版图腾作为模板，无需额外资源
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;
using Duckov.Utilities;
using Duckov.Economy;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using BossRush.Common.Utils;

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
        /// 初始化飞行图腾物品
        /// </summary>
        private void InitializeFlightTotemItem()
        {
            if (flightTotemInitialized) return;
            flightTotemInitialized = true;

            try
            {
                // 直接使用原版图腾作为模板
                CreateFlightTotemFromVanillaTemplate();
            }
            catch (Exception e)
            {
                DevLog($"[FlightTotem] 初始化失败: {e.Message}");
            }
        }

        /// <summary>
        /// 从原版图腾模板创建飞行图腾
        /// </summary>
        private void CreateFlightTotemFromVanillaTemplate()
        {
            // 查找现有的图腾作为模板
            Tag totemTag = FindTagByName("Totem");
            if (totemTag == null)
            {
                DevLog("[FlightTotem] 未找到 Totem Tag，无法创建图腾");
                return;
            }

            ItemFilter filter = default(ItemFilter);
            filter.requireTags = new Tag[] { totemTag };
            filter.minQuality = 1;
            filter.maxQuality = 8;
            int[] totemIds = ItemAssetsCollection.Search(filter);

            if (totemIds == null || totemIds.Length == 0)
            {
                DevLog("[FlightTotem] 未找到任何图腾模板");
                return;
            }

            // 使用第一个图腾作为模板
            Item templateTotem = ItemAssetsCollection.InstantiateSync(totemIds[0]);
            if (templateTotem == null)
            {
                DevLog("[FlightTotem] 无法实例化模板图腾");
                return;
            }

            // 克隆并配置
            GameObject totemGO = UnityEngine.Object.Instantiate(templateTotem.gameObject);
            totemGO.name = "FlightTotem_Lv1";
            UnityEngine.Object.DontDestroyOnLoad(totemGO);
            totemGO.SetActive(false);

            // 销毁临时模板
            UnityEngine.Object.Destroy(templateTotem.gameObject);

            Item totemItem = totemGO.GetComponent<Item>();
            if (totemItem == null)
            {
                UnityEngine.Object.Destroy(totemGO);
                return;
            }

            // 配置为飞行图腾
            ConfigureFlightTotemItem(totemItem);

            // 注册到物品系统
            ItemAssetsCollection.AddDynamicEntry(totemItem);

            flightTotemPrefab = totemItem;
            flightTotemTypeId = totemItem.TypeID;

            DevLog($"[FlightTotem] 成功创建飞行图腾: TypeID={flightTotemTypeId}");
        }

        /// <summary>
        /// 配置飞行图腾物品属性
        /// </summary>
        private void ConfigureFlightTotemItem(Item totemItem)
        {
            if (totemItem == null) return;

            try
            {
                var config = FlightConfig.Instance;

                // 1. 设置 TypeID
                var typeIdField = BossRush.Common.Utils.ReflectionCache.GetField(
                    typeof(Item),
                    "typeID",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (typeIdField != null)
                {
                    typeIdField.SetValue(totemItem, config.ItemTypeId);
                }

                // 2. 设置 displayName
                var displayNameField = BossRush.Common.Utils.ReflectionCache.GetField(
                    typeof(Item),
                    "displayName",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (displayNameField != null)
                {
                    displayNameField.SetValue(totemItem, "BossRush_FlightTotem");
                }

                // 3. 移除原模板的 Effect 组件
                foreach (var effect in totemItem.GetComponents<Effect>())
                {
                    UnityEngine.Object.DestroyImmediate(effect);
                }

                // 4. 移除原模板的 EffectTrigger 组件
                foreach (var trigger in totemItem.GetComponents<EffectTrigger>())
                {
                    UnityEngine.Object.DestroyImmediate(trigger);
                }

                // 5. 清空使用行为
                var usageUtils = totemItem.GetComponent<UsageUtilities>();
                if (usageUtils?.behaviors != null)
                {
                    usageUtils.behaviors.Clear();
                }

                // 6. 添加 Totem 标签
                AddTagsToItem(totemItem, config.ItemTags);

                // 7. 设置品质
                var qualityField = BossRush.Common.Utils.ReflectionCache.GetField(
                    typeof(Item),
                    "quality",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (qualityField != null)
                {
                    qualityField.SetValue(totemItem, config.ItemQuality);
                }

                DevLog("[FlightTotem] 物品配置完成");
            }
            catch (Exception e)
            {
                DevLog($"[FlightTotem] 配置物品失败: {e.Message}");
            }
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
