// ============================================================================
// DiamondConfig.cs - 钻石物品配置
// ============================================================================
// 模块说明：
//   定义钻石物品的配置常量和初始化逻辑
//   钻石是一种消耗品，使用后可召唤场景中的哥布林NPC跑向玩家
//   与砖石（假钻石）相反，这是真钻石，哥布林会很开心
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 钻石物品配置
    /// </summary>
    public static class DiamondConfig
    {
        // ============================================================================
        // 物品基础配置
        // ============================================================================
        
        /// <summary>
        /// 物品 TypeID（需要与 Unity 预制体中的 typeID 一致）
        /// </summary>
        public const int TYPE_ID = 500017;
        
        /// <summary>
        /// AssetBundle 名称
        /// </summary>
        public const string BUNDLE_NAME = "diamond";
        
        /// <summary>
        /// 图标资源名称
        /// </summary>
        public const string ICON_NAME = "Diamond";
        
        // ============================================================================
        // 本地化配置
        // ============================================================================
        
        /// <summary>
        /// 物品本地化键
        /// </summary>
        public const string LOC_KEY_DISPLAY = "BossRush_Diamond";
        
        /// <summary>
        /// 物品显示名称（中文）
        /// </summary>
        public const string DISPLAY_NAME_CN = "钻石";
        
        /// <summary>
        /// 物品显示名称（英文）
        /// </summary>
        public const string DISPLAY_NAME_EN = "Diamond";
        
        /// <summary>
        /// 物品描述（中文）
        /// </summary>
        public const string DESCRIPTION_CN = "这是一个真的钻石";
        
        /// <summary>
        /// 物品描述（英文）
        /// </summary>
        public const string DESCRIPTION_EN = "This is a real diamond";
        
        /// <summary>
        /// 无哥布林提示（中文）
        /// </summary>
        public const string NO_GOBLIN_HINT_CN = "似乎没有反应...";
        
        /// <summary>
        /// 无哥布林提示（英文）
        /// </summary>
        public const string NO_GOBLIN_HINT_EN = "Nothing seems to happen...";
        
        // ============================================================================
        // 好感度配置
        // ============================================================================
        
        /// <summary>
        /// 使用钻石增加的好感度
        /// </summary>
        public const int AFFINITY_BONUS = 5;
        
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
        
        /// <summary>
        /// 获取本地化的无哥布林提示
        /// </summary>
        public static string GetNoGoblinHint()
        {
            return L10n.T(NO_GOBLIN_HINT_CN, NO_GOBLIN_HINT_EN);
        }
        
        // ============================================================================
        // 物品配置
        // ============================================================================
        
        /// <summary>
        /// 配置钻石物品（由 ItemFactory 调用）
        /// </summary>
        public static void ConfigureItem(Item item)
        {
            if (item == null) return;
            
            try
            {
                // 设置本地化键
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                
                // 添加 UsageUtilities 组件（如果没有）
                UsageUtilities usageUtils = item.GetComponent<UsageUtilities>();
                if (usageUtils == null)
                {
                    usageUtils = item.gameObject.AddComponent<UsageUtilities>();
                    SetUsageUtilitiesMaster(usageUtils, item);
                }
                
                // 添加使用行为组件（如果没有）
                DiamondUsage usage = item.GetComponent<DiamondUsage>();
                if (usage == null)
                {
                    usage = item.gameObject.AddComponent<DiamondUsage>();
                }
                
                // 将使用行为添加到 behaviors 列表
                if (usageUtils.behaviors == null)
                {
                    usageUtils.behaviors = new List<UsageBehavior>();
                }
                if (!usageUtils.behaviors.Contains(usage))
                {
                    usageUtils.behaviors.Add(usage);
                }
                
                // 设置 Item 的 usageUtilities 字段
                SetItemUsageUtilities(item, usageUtils);
                
                ModBehaviour.DevLog("[DiamondConfig] 钻石物品配置完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DiamondConfig] 配置物品失败: " + e.Message);
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
            ModBehaviour.DevLog("[DiamondConfig] 已注册物品配置器");
        }
    }
}
