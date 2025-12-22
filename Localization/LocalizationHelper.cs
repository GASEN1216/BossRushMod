// ============================================================================
// LocalizationHelper.cs - 本地化公共辅助方法
// ============================================================================
// 模块说明：
//   提供本地化系统的公共辅助方法，供各模块复用
//   - 注入本地化键值
//   - 获取 LocalizationManager 类型
// ============================================================================

using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 本地化公共辅助方法
    /// </summary>
    public static class LocalizationHelper
    {
        // 缓存的 LocalizationManager 类型
        private static Type cachedLocType = null;
        private static bool locTypeSearched = false;
        
        /// <summary>
        /// 获取 LocalizationManager 类型（带缓存）
        /// </summary>
        public static Type GetLocalizationManagerType()
        {
            if (locTypeSearched) return cachedLocType;
            
            locTypeSearched = true;
            
            var types = new string[]
            {
                "SodaCraft.Localizations.LocalizationManager, SodaLocalization",
                "SodaCraft.Localizations.LocalizationManager, TeamSoda.Duckov.Core",
                "SodaCraft.Localizations.LocalizationManager, Assembly-CSharp",
                "LocalizationManager, Assembly-CSharp"
            };

            foreach (var t in types)
            {
                cachedLocType = Type.GetType(t);
                if (cachedLocType != null) break;
            }
            
            return cachedLocType;
        }
        
        /// <summary>
        /// 注入本地化键值到 Dictionary
        /// </summary>
        public static void InjectLocalizedKey(Dictionary<string, string> dict, string key, string value)
        {
            if (dict == null || string.IsNullOrEmpty(key)) return;
            
            if (dict.ContainsKey(key))
            {
                dict[key] = value;
            }
            else
            {
                dict.Add(key, value);
            }
        }
        
        /// <summary>
        /// 注入本地化键值到 IDictionary
        /// </summary>
        public static void InjectLocalizedKeyDict(System.Collections.IDictionary dict, string key, string value)
        {
            if (dict == null || string.IsNullOrEmpty(key)) return;
            
            if (dict.Contains(key))
            {
                dict[key] = value;
            }
            else
            {
                dict.Add(key, value);
            }
        }
        
        /// <summary>
        /// 注入本地化键值到游戏的本地化系统（自动查找字典）
        /// </summary>
        /// <param name="key">本地化键</param>
        /// <param name="value">本地化值</param>
        /// <returns>是否成功注入</returns>
        public static bool InjectLocalization(string key, string value)
        {
            try
            {
                Type locType = GetLocalizationManagerType();
                if (locType == null) return false;
                
                var fields = locType.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                bool injected = false;
                
                foreach (var field in fields)
                {
                    try
                    {
                        var val = field.GetValue(null);
                        if (val == null) continue;

                        Dictionary<string, string> dict = val as Dictionary<string, string>;
                        if (dict != null)
                        {
                            InjectLocalizedKey(dict, key, value);
                            injected = true;
                            continue;
                        }

                        var dictObj = val as System.Collections.IDictionary;
                        if (dictObj != null)
                        {
                            InjectLocalizedKeyDict(dictObj, key, value);
                            injected = true;
                        }
                    }
                    catch { }
                }
                
                return injected;
            }
            catch (Exception e)
            {
                Debug.LogError("[LocalizationHelper] InjectLocalization 出错: " + e.Message);
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
    }
}
