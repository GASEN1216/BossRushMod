// ============================================================================
// PropertyLockSystem.cs - 属性固定系统
// ============================================================================
// 模块说明：
//   管理装备属性的固定功能，包括：
//   - 检查属性是否已固定
//   - 固定/解锁属性
//   - 持久化存储（使用物品 Variables）
//
// 持久化前缀：
//   - RF_LOCK_MOD_xxx  : 固定的 Modifier 属性
//   - RF_LOCK_STAT_xxx : 固定的 Stat 属性
//   - RF_LOCK_VAR_xxx  : 固定的 Variable 属性
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 属性类型枚举（与 ReforgeSystem 中的定义保持一致）
    /// </summary>
    public enum PropertyType
    {
        Modifier,
        Stat,
        Variable
    }
    
    /// <summary>
    /// 属性固定系统
    /// </summary>
    public static class PropertyLockSystem
    {
        // ============================================================================
        // 持久化前缀常量
        // ============================================================================
        
        /// <summary>
        /// Modifier 属性固定前缀
        /// </summary>
        public const string LOCK_MOD_PREFIX = "RF_LOCK_MOD_";
        
        /// <summary>
        /// Stat 属性固定前缀
        /// </summary>
        public const string LOCK_STAT_PREFIX = "RF_LOCK_STAT_";
        
        /// <summary>
        /// Variable 属性固定前缀
        /// </summary>
        public const string LOCK_VAR_PREFIX = "RF_LOCK_VAR_";
        
        // ============================================================================
        // 公开 API
        // ============================================================================
        
        /// <summary>
        /// 检查属性是否已固定
        /// </summary>
        /// <param name="item">物品</param>
        /// <param name="propertyKey">属性键名</param>
        /// <param name="type">属性类型</param>
        /// <returns>是否已固定</returns>
        public static bool IsPropertyLocked(Item item, string propertyKey, PropertyType type)
        {
            if (item == null || item.Variables == null || string.IsNullOrEmpty(propertyKey))
            {
                return false;
            }
            
            string lockKey = GetLockKey(propertyKey, type);
            
            try
            {
                foreach (var variable in item.Variables)
                {
                    if (variable.Key == lockKey)
                    {
                        float value = variable.GetFloat();
                        return value >= 1f;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PropertyLock] 检查固定状态失败: " + e.Message);
            }
            
            return false;
        }
        
        /// <summary>
        /// 固定属性
        /// </summary>
        /// <param name="item">物品</param>
        /// <param name="propertyKey">属性键名</param>
        /// <param name="type">属性类型</param>
        /// <returns>是否成功固定</returns>
        public static bool LockProperty(Item item, string propertyKey, PropertyType type)
        {
            if (item == null || item.Variables == null || string.IsNullOrEmpty(propertyKey))
            {
                ModBehaviour.DevLog("[PropertyLock] 固定失败: 参数无效");
                return false;
            }
            
            // 检查是否已固定
            if (IsPropertyLocked(item, propertyKey, type))
            {
                ModBehaviour.DevLog("[PropertyLock] 属性已固定: " + propertyKey);
                return false;
            }
            
            string lockKey = GetLockKey(propertyKey, type);
            
            try
            {
                // 设置固定标记
                item.SetFloat(lockKey, 1f, true);
                
                // 设置为不显示（避免在物品详情中显示）
                var entry = item.Variables.GetEntry(lockKey);
                if (entry != null)
                {
                    entry.Display = false;
                }
                
                ModBehaviour.DevLog("[PropertyLock] 属性已固定: " + item.DisplayName + "." + propertyKey);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PropertyLock] 固定属性失败: " + e.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 解锁属性（可选功能）
        /// </summary>
        /// <param name="item">物品</param>
        /// <param name="propertyKey">属性键名</param>
        /// <param name="type">属性类型</param>
        /// <returns>是否成功解锁</returns>
        public static bool UnlockProperty(Item item, string propertyKey, PropertyType type)
        {
            if (item == null || item.Variables == null || string.IsNullOrEmpty(propertyKey))
            {
                return false;
            }
            
            // 检查是否已固定
            if (!IsPropertyLocked(item, propertyKey, type))
            {
                return false;
            }
            
            string lockKey = GetLockKey(propertyKey, type);
            
            try
            {
                // 设置为0表示未固定
                item.SetFloat(lockKey, 0f, true);
                
                ModBehaviour.DevLog("[PropertyLock] 属性已解锁: " + item.DisplayName + "." + propertyKey);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PropertyLock] 解锁属性失败: " + e.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 获取物品所有已固定的属性
        /// </summary>
        /// <param name="item">物品</param>
        /// <returns>已固定属性的键名列表（包含类型前缀）</returns>
        public static List<LockedPropertyInfo> GetLockedProperties(Item item)
        {
            List<LockedPropertyInfo> result = new List<LockedPropertyInfo>();
            
            if (item == null || item.Variables == null)
            {
                return result;
            }
            
            try
            {
                foreach (var variable in item.Variables)
                {
                    if (variable.Key == null) continue;
                    
                    // 检查是否是固定标记
                    PropertyType? type = null;
                    string propertyKey = null;
                    
                    if (variable.Key.StartsWith(LOCK_MOD_PREFIX))
                    {
                        type = PropertyType.Modifier;
                        propertyKey = variable.Key.Substring(LOCK_MOD_PREFIX.Length);
                    }
                    else if (variable.Key.StartsWith(LOCK_STAT_PREFIX))
                    {
                        type = PropertyType.Stat;
                        propertyKey = variable.Key.Substring(LOCK_STAT_PREFIX.Length);
                    }
                    else if (variable.Key.StartsWith(LOCK_VAR_PREFIX))
                    {
                        type = PropertyType.Variable;
                        propertyKey = variable.Key.Substring(LOCK_VAR_PREFIX.Length);
                    }
                    
                    if (type.HasValue && !string.IsNullOrEmpty(propertyKey))
                    {
                        try
                        {
                            float value = variable.GetFloat();
                            if (value >= 1f)
                            {
                                result.Add(new LockedPropertyInfo
                                {
                                    PropertyKey = propertyKey,
                                    Type = type.Value
                                });
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PropertyLock] 获取已固定属性失败: " + e.Message);
            }
            
            return result;
        }
        
        /// <summary>
        /// 获取物品已固定属性的数量
        /// </summary>
        public static int GetLockedPropertyCount(Item item)
        {
            return GetLockedProperties(item).Count;
        }
        
        // ============================================================================
        // 内部方法
        // ============================================================================
        
        /// <summary>
        /// 获取固定标记的键名
        /// </summary>
        private static string GetLockKey(string propertyKey, PropertyType type)
        {
            switch (type)
            {
                case PropertyType.Modifier:
                    return LOCK_MOD_PREFIX + propertyKey;
                case PropertyType.Stat:
                    return LOCK_STAT_PREFIX + propertyKey;
                case PropertyType.Variable:
                    return LOCK_VAR_PREFIX + propertyKey;
                default:
                    return LOCK_MOD_PREFIX + propertyKey;
            }
        }
    }
    
    /// <summary>
    /// 已固定属性信息
    /// </summary>
    public class LockedPropertyInfo
    {
        public string PropertyKey;
        public PropertyType Type;
    }
}
