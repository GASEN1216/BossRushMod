// ============================================================================
// LocalizationHelper.cs - 本地化公共辅助方法
// ============================================================================
// 模块说明：
//   提供本地化系统的公共辅助方法，供各模块复用
//   使用游戏官方 API: LocalizationManager.SetOverrideText
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using SodaCraft.Localizations;

namespace BossRush
{
    /// <summary>
    /// 本地化公共辅助方法
    /// </summary>
    public static class LocalizationHelper
    {
        /// <summary>
        /// 注入本地化键值到游戏的本地化系统
        /// 使用官方 API: LocalizationManager.SetOverrideText
        /// </summary>
        /// <param name="key">本地化键</param>
        /// <param name="value">本地化值</param>
        /// <returns>是否成功注入</returns>
        public static bool InjectLocalization(string key, string value)
        {
            try
            {
                LocalizationManager.SetOverrideText(key, value);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LocalizationHelper] InjectLocalization 出错: " + e.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 批量注入本地化键值
        /// </summary>
        /// <param name="localizations">键值对字典</param>
        /// <returns>成功注入的数量</returns>
        public static int InjectLocalizations(Dictionary<string, string> localizations)
        {
            if (localizations == null || localizations.Count == 0) return 0;
            
            int count = 0;
            foreach (var kvp in localizations)
            {
                if (InjectLocalization(kvp.Key, kvp.Value))
                {
                    count++;
                }
            }
            return count;
        }
        
        /// <summary>
        /// 获取本地化文本
        /// 使用游戏官方 API: LocalizationManager.GetPlainText
        /// </summary>
        /// <param name="key">本地化键</param>
        /// <returns>本地化后的文本，如果未找到则返回键本身</returns>
        public static string GetLocalizedText(string key)
        {
            try
            {
                return LocalizationManager.GetPlainText(key);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LocalizationHelper] GetLocalizedText 出错: " + e.Message);
                return key;
            }
        }
    }
}
