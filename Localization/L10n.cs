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
        
    }
}
