// ============================================================================
// L10n.cs - BossRush 本地化工具类
// ============================================================================
// 模块说明：
//   提供简单的本地化支持，根据游戏语言返回中文或英文文本
//   
// 主要功能：
//   - IsChinese: 检测当前游戏语言是否为中文
//   - T(cn, en): 根据语言返回对应文本
//   - Direction(cn): 方位名称本地化
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using SodaCraft.Localizations;

namespace BossRush
{
    /// <summary>
    /// BossRush 本地化工具类
    /// </summary>
    public static class L10n
    {
        // 方位名称映射表
        private static readonly Dictionary<string, string> DirectionMap = new Dictionary<string, string>
        {
            { "东", "East" },
            { "西", "West" },
            { "南", "South" },
            { "北", "North" },
            { "东北", "Northeast" },
            { "东南", "Southeast" },
            { "西北", "Northwest" },
            { "西南", "Southwest" }
        };
        
        /// <summary>
        /// 检测当前游戏语言是否为中文
        /// <para>使用游戏内的 LocalizationManager.CurrentLanguage 而非系统语言</para>
        /// </summary>
        public static bool IsChinese
        {
            get
            {
                try
                {
                    // 使用游戏的本地化管理器获取当前语言设置
                    SystemLanguage lang = LocalizationManager.CurrentLanguage;
                    return (lang == SystemLanguage.Chinese || 
                            lang == SystemLanguage.ChineseSimplified || 
                            lang == SystemLanguage.ChineseTraditional);
                }
                catch
                {
                    // 如果检测失败，默认返回中文（保持向后兼容）
                    return true;
                }
            }
        }
        
        /// <summary>
        /// 根据语言返回对应文本
        /// </summary>
        /// <param name="cn">中文文本</param>
        /// <param name="en">英文文本</param>
        /// <returns>当前语言对应的文本</returns>
        public static string T(string cn, string en)
        {
            // 处理 null 情况
            if (cn == null && en == null) return "";
            if (cn == null) return en;
            if (en == null) return cn;
            
            return IsChinese ? cn : en;
        }
        
        /// <summary>
        /// 方位名称本地化
        /// </summary>
        /// <param name="cnDirection">中文方位名称</param>
        /// <returns>本地化后的方位名称</returns>
        public static string Direction(string cnDirection)
        {
            if (string.IsNullOrEmpty(cnDirection)) return cnDirection;
            
            if (IsChinese)
            {
                return cnDirection;
            }
            
            // 尝试从映射表获取英文方位
            string enDirection;
            if (DirectionMap.TryGetValue(cnDirection, out enDirection))
            {
                return enDirection;
            }
            
            // 未找到映射，返回原始文本
            return cnDirection;
        }
        
        // ========== 龙息武器本地化 ==========
        
        /// <summary>
        /// 获取龙息武器名称
        /// </summary>
        public static string DragonBreathName
        {
            get { return T("龙息", "Dragon's Breath"); }
        }
        
        /// <summary>
        /// 获取龙息武器描述
        /// </summary>
        public static string DragonBreathDesc
        {
            get { return T("龙裔遗族的改装MCX SUPER，命中敌人有50%概率施加龙焰灼烧效果", 
                          "Modified MCX SUPER of Dragon Descendant, 50% chance to apply Dragon Burn on hit"); }
        }
        
        /// <summary>
        /// 获取龙焰灼烧Buff名称
        /// </summary>
        public static string DragonBurnName
        {
            get { return T("龙焰灼烧", "Dragon Burn"); }
        }
        
        /// <summary>
        /// 获取龙焰灼烧Buff描述
        /// </summary>
        public static string DragonBurnDesc
        {
            get { return T("每秒受到最大生命值0.1%+1点真实火焰伤害，最多叠加10层，持续10秒", 
                          "Takes 0.1% max HP + 1 true fire damage per second per layer, stacks up to 10, lasts 10 seconds"); }
        }
        
        // ========== 龙王Boss本地化 ==========
        
        /// <summary>
        /// 获取龙王Boss名称
        /// </summary>
        public static string DragonKingName
        {
            get { return T("龙王", "Dragon King"); }
        }
        
        /// <summary>
        /// 获取龙王出现消息
        /// </summary>
        public static string DragonKingAppeared
        {
            get { return T("龙王 出现了！", "Dragon King has appeared!"); }
        }
        
        /// <summary>
        /// 获取龙王被击败消息
        /// </summary>
        public static string DragonKingDefeated
        {
            get { return T("龙王被击败了！", "Dragon King has been defeated!"); }
        }
        
        /// <summary>
        /// 获取龙王进入狂暴状态消息
        /// </summary>
        public static string DragonKingEnraged
        {
            get { return T("龙王进入狂暴状态！", "Dragon King enters rage mode!"); }
        }
        
    }
}
