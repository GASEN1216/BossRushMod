// ============================================================================
// AchievementMedalConfig.cs - 成就勋章物品配置
// ============================================================================
// 模块说明：
//   定义成就勋章物品的配置常量和初始化逻辑
//   成就勋章是一种不消耗的物品，使用后打开成就界面
//   通过 ItemFactory 统一加载和管理
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 成就勋章物品配置
    /// </summary>
    public static class AchievementMedalConfig
    {
        // ============================================================================
        // 物品基础配置
        // ============================================================================
        
        /// <summary>
        /// 物品 TypeID（需要与 Unity 预制体中的 typeID 一致）
        /// </summary>
        public const int TYPE_ID = 500018;
        
        /// <summary>
        /// AssetBundle 名称
        /// </summary>
        public const string BUNDLE_NAME = "achievement_medal";
        
        // ============================================================================
        // 本地化配置
        // ============================================================================
        
        /// <summary>
        /// 物品本地化键
        /// </summary>
        public const string LOC_KEY_DISPLAY = "BossRush_AchievementMedal";
        
        /// <summary>
        /// 物品显示名称（中文）
        /// </summary>
        public const string DISPLAY_NAME_CN = "成就勋章";
        
        /// <summary>
        /// 物品显示名称（英文）
        /// </summary>
        public const string DISPLAY_NAME_EN = "Achievement Medal";
        
        /// <summary>
        /// 物品描述（中文）
        /// </summary>
        public const string DESCRIPTION_CN = "一枚闪闪发光的勋章，记录着你在 Boss Rush 中的辉煌战绩。右键查看成就。";
        
        /// <summary>
        /// 物品描述（英文）
        /// </summary>
        public const string DESCRIPTION_EN = "A shiny medal that records your glorious achievements in Boss Rush. Right-click to view achievements.";
        
        // ============================================================================
        // 商店配置
        // ============================================================================
        
        /// <summary>
        /// 商店库存持久化键
        /// </summary>
        public const string STOCK_SAVE_KEY = "BossRush_MedalStock";
        
        /// <summary>
        /// 默认最大库存
        /// </summary>
        public const int DEFAULT_MAX_STOCK = 1;
        
        // ============================================================================
        // 辅助方法
        // ============================================================================
        
        /// <summary>
        /// 获取本地化的物品名称
        /// </summary>
        public static string GetDisplayName()
        {
            return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);
        }
        
        /// <summary>
        /// 获取本地化的物品描述
        /// </summary>
        public static string GetDescription()
        {
            return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);
        }
        
        // ============================================================================
        // 物品配置
        // ============================================================================
        
        /// <summary>
        /// 配置成就勋章物品（由 ItemFactory 调用）
        /// </summary>
        public static void ConfigureItem(Item item)
        {
            if (item == null) return;
            
            try
            {
                // 设置物品为使用耐久度模式，防止使用后被消耗
                item.MaxDurability = 999f;
                item.Durability = 999f;
                
                ModBehaviour.DevLog("[AchievementMedalConfig] 已设置耐久度: MaxDurability=999, Durability=999");
                
                // 添加 UsageUtilities 组件（如果没有）
                UsageUtilities usageUtils = item.GetComponent<UsageUtilities>();
                if (usageUtils == null)
                {
                    usageUtils = item.gameObject.AddComponent<UsageUtilities>();
                    SetUsageUtilitiesMaster(usageUtils, item);
                }
                
                // 确保 behaviors 列表存在
                if (usageUtils.behaviors == null)
                {
                    usageUtils.behaviors = new List<UsageBehavior>();
                }
                
                // 添加使用行为组件（如果没有）
                AchievementMedalUsageBehavior medalUsage = item.GetComponent<AchievementMedalUsageBehavior>();
                if (medalUsage == null)
                {
                    medalUsage = item.gameObject.AddComponent<AchievementMedalUsageBehavior>();
                }
                
                // 将使用行为添加到 behaviors 列表
                if (!usageUtils.behaviors.Contains(medalUsage))
                {
                    usageUtils.behaviors.Add(medalUsage);
                }
                
                // 设置 Item 的 usageUtilities 字段
                SetItemUsageUtilities(item, usageUtils);
                
                ModBehaviour.DevLog("[AchievementMedalConfig] 成就勋章物品配置完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AchievementMedalConfig] 配置物品失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 设置 UsageUtilities 的 Master 字段（通过反射）
        /// </summary>
        private static void SetUsageUtilitiesMaster(UsageUtilities usageUtils, Item item)
        {
            try
            {
                var masterField = typeof(UsageUtilities).BaseType.GetField("master", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                masterField?.SetValue(usageUtils, item);
            }
            catch { }
        }
        
        /// <summary>
        /// 设置 Item 的 usageUtilities 字段（通过反射）
        /// </summary>
        private static void SetItemUsageUtilities(Item item, UsageUtilities usageUtils)
        {
            try
            {
                var field = typeof(Item).GetField("usageUtilities", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(item, usageUtils);
            }
            catch { }
        }
        
        /// <summary>
        /// 注册配置器到 ItemFactory
        /// </summary>
        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[AchievementMedalConfig] 已注册物品配置器");
        }
        
        /// <summary>
        /// 注入本地化
        /// </summary>
        public static void InjectLocalization()
        {
            try
            {
                string displayName = GetDisplayName();
                string description = GetDescription();
                
                // 注入物品名称和描述（自定义键）
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY, displayName);
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY + "_Desc", description);
                
                // 注入 Unity 预制体中使用的本地化键
                // 预制体 displayName 字段设置为 "AchievementMedal"，游戏会用它作为本地化键查找
                LocalizationHelper.InjectLocalization("AchievementMedal", displayName);
                LocalizationHelper.InjectLocalization("AchievementMedal_Desc", description);
                
                // 注入中文键（备用）
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN, displayName);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN + "_Desc", description);
                
                // 注入物品 ID 键
                LocalizationHelper.InjectLocalization(TYPE_ID.ToString(), displayName);
                LocalizationHelper.InjectLocalization(TYPE_ID.ToString() + "_Desc", description);
                
                // 注入 Item_ 前缀键（游戏可能使用这种格式）
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID, displayName);
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID + "_Desc", description);
                
                ModBehaviour.DevLog("[AchievementMedalConfig] 本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AchievementMedalConfig] 本地化注入失败: " + e.Message);
            }
        }
    }
    
    // ============================================================================
    // AchievementMedalUsageBehavior - 成就勋章使用行为
    // ============================================================================
    
    /// <summary>
    /// 成就勋章使用行为：打开成就界面，不消耗物品
    /// </summary>
    public class AchievementMedalUsageBehavior : UsageBehavior
    {
        /// <summary>
        /// 检查物品是否可以使用
        /// </summary>
        public override bool CanBeUsed(Item item, object user)
        {
            // 成就勋章始终可以使用
            return true;
        }
        
        /// <summary>
        /// 使用物品时调用
        /// </summary>
        protected override void OnUse(Item item, object user)
        {
            try
            {
                // 打开成就界面
                if (AchievementView.Instance != null)
                {
                    AchievementView.Instance.Toggle();
                    ModBehaviour.DevLog("[AchievementMedal] 打开成就界面");
                }
                else
                {
                    ModBehaviour.DevLog("[AchievementMedal] AchievementView.Instance 为空");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AchievementMedal] 打开成就界面失败: " + e.Message);
            }
        }
    }
}
