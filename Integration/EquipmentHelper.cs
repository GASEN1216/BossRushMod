// ============================================================================
// EquipmentHelper.cs - 装备公共辅助方法
// ============================================================================
// 模块说明：
//   提供装备系统的公共辅助方法，供 DragonSetConfig、EquipmentFactory 等模块复用
//   - 添加属性修改器 (Modifier)
//   - 设置物品常量 (Constant)
//   - 添加标签 (Tag)
// ============================================================================

using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using ItemStatsSystem.Items;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// 装备公共辅助方法
    /// </summary>
    public static class EquipmentHelper
    {
        /// <summary>
        /// 为物品添加 ModifierDescription（装备属性会自动应用到角色身上）
        /// </summary>
        /// <param name="item">装备物品</param>
        /// <param name="statKey">属性键名（如 HeadArmor, BodyArmor, ElementFactor_Physics）</param>
        /// <param name="modType">修改器类型（Add=直接加值, PercentageAdd=百分比加成）</param>
        /// <param name="value">属性值</param>
        /// <param name="display">是否在装备详情 UI 中显示</param>
        public static void AddModifierToItem(Item item, string statKey, ModifierType modType, float value, bool display)
        {
            try
            {
                // 获取或创建 ModifierDescriptionCollection
                ModifierDescriptionCollection modifiers = item.Modifiers;
                
                if (modifiers == null)
                {
                    Debug.Log("[EquipmentHelper] 物品没有 Modifiers，尝试创建...");
                    
                    modifiers = item.gameObject.AddComponent<ModifierDescriptionCollection>();
                    
                    // 设置 Master 字段（ItemComponent 基类）
                    FieldInfo masterField = typeof(ModifierDescriptionCollection).BaseType.GetField("master", 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (masterField != null)
                    {
                        masterField.SetValue(modifiers, item);
                    }
                    
                    // 设置 Item 的 modifiers 字段
                    FieldInfo modifiersField = typeof(Item).GetField("modifiers", 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (modifiersField != null)
                    {
                        modifiersField.SetValue(item, modifiers);
                    }
                }
                
                if (modifiers == null)
                {
                    Debug.LogWarning("[EquipmentHelper] 无法获取或创建 ModifierDescriptionCollection");
                    return;
                }
                
                // 创建 ModifierDescription
                ModifierDescription modDesc = new ModifierDescription(
                    ModifierTarget.Character,  // 目标：角色
                    statKey,                   // 属性键名
                    modType,                   // 修改器类型
                    value,                     // 属性值
                    false,                     // 不覆盖顺序
                    0                          // 顺序值
                );
                
                // 设置 display 字段
                FieldInfo displayField = typeof(ModifierDescription).GetField("display", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (displayField != null)
                {
                    displayField.SetValue(modDesc, display);
                }
                
                // 添加到集合
                modifiers.Add(modDesc);
                
                string modTypeStr = modType == ModifierType.Add ? "+" : (modType == ModifierType.PercentageAdd ? "*+" : "*");
                Debug.Log("[EquipmentHelper] 添加 Modifier: Character/" + statKey + " " + modTypeStr + value + " (display=" + display + ")");
            }
            catch (Exception e)
            {
                Debug.LogError("[EquipmentHelper] AddModifierToItem 出错: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 设置物品的 Constant 值（如 MaxDurability）
        /// </summary>
        public static void SetItemConstant(Item item, string key, float value)
        {
            try
            {
                var constants = item.Constants;
                if (constants != null)
                {
                    constants.SetFloat(key, value, true);
                    Debug.Log("[EquipmentHelper] 设置 Constant: " + key + " = " + value);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EquipmentHelper] SetItemConstant 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 为物品添加指定名称的 Tag
        /// </summary>
        public static void AddTagToItem(Item item, string tagName)
        {
            try
            {
                var allTags = GameplayDataSettings.Tags.AllTags;
                if (allTags == null) return;

                Tag targetTag = null;
                foreach (Tag tag in allTags)
                {
                    if (tag != null && tag.name == tagName)
                    {
                        targetTag = tag;
                        break;
                    }
                }

                if (targetTag == null)
                {
                    Debug.LogWarning("[EquipmentHelper] 未找到 Tag: " + tagName);
                    return;
                }

                var tagsField = typeof(Item).GetField("tags", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (tagsField == null) return;

                var tagCollection = tagsField.GetValue(item);
                if (tagCollection == null) return;

                var listField = tagCollection.GetType().GetField("list", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (listField == null) return;

                var tagList = listField.GetValue(tagCollection) as List<Tag>;
                if (tagList == null)
                {
                    tagList = new List<Tag>();
                    listField.SetValue(tagCollection, tagList);
                }

                // 检查是否已存在
                foreach (Tag t in tagList)
                {
                    if (t != null && t.name == tagName) return;
                }

                tagList.Add(targetTag);
                Debug.Log("[EquipmentHelper] 已添加 Tag: " + tagName);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EquipmentHelper] AddTagToItem 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 添加可维修标签（Repairable）
        /// </summary>
        public static void AddRepairableTag(Item item)
        {
            AddTagToItem(item, "Repairable");
        }
    }
}
