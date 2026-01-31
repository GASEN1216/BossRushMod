// ============================================================================
// ColdQuenchFluidConfig.cs - 冷淬液物品配置
// ============================================================================
// 模块说明：
//   定义冷淬液物品的配置常量，包括：
//   - 物品 TypeID
//   - 本地化键名
//   - AssetBundle 名称
//   - 标签配置
// ============================================================================

using System;
using ItemStatsSystem;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 冷淬液物品配置
    /// </summary>
    public static class ColdQuenchFluidConfig
    {
        // ============================================================================
        // 物品基础配置
        // ============================================================================
        
        /// <summary>
        /// 物品 TypeID（需要与 Unity 预制体中的 typeID 一致）
        /// </summary>
        public const int TYPE_ID = 500014;
        
        /// <summary>
        /// AssetBundle 名称
        /// </summary>
        public const string BUNDLE_NAME = "cold_quench_fluid";
        
        /// <summary>
        /// 彩色图标资源名称
        /// </summary>
        public const string ICON_NAME = "ColdQuenchFluid";
        
        /// <summary>
        /// 灰色图标资源名称
        /// </summary>
        public const string ICON_GRAY_NAME = "ColdQuenchFluid_Gray";
        
        // ============================================================================
        // 本地化配置
        // ============================================================================
        
        /// <summary>
        /// 物品本地化键（用于 DisplayNameRaw）
        /// </summary>
        public const string LOC_KEY_DISPLAY = "BossRush_ColdQuenchFluid";
        
        /// <summary>
        /// 物品显示名称（中文）
        /// </summary>
        public const string DISPLAY_NAME_CN = "冷淬液";
        
        /// <summary>
        /// 物品显示名称（英文）
        /// </summary>
        public const string DISPLAY_NAME_EN = "Cold Quench Fluid";
        
        /// <summary>
        /// 物品描述（中文）
        /// </summary>
        public const string DESCRIPTION_CN = "哥布林工匠重铸时用的极寒液体，能够把装备部件进行永久固定。";
        
        /// <summary>
        /// 物品描述（英文）
        /// </summary>
        public const string DESCRIPTION_EN = "An extremely cold liquid used by goblin craftsmen during reforging, capable of permanently locking equipment components.";
        
        // ============================================================================
        // 标签配置
        // ============================================================================
        
        /// <summary>
        /// 标签名称（用于创建 Tag ScriptableObject）
        /// </summary>
        public const string TAG_NAME = "ColdQuench";
        
        /// <summary>
        /// 标签显示名称（中文）
        /// </summary>
        public const string TAG_DISPLAY_CN = "冷淬";
        
        /// <summary>
        /// 标签显示名称（英文）
        /// </summary>
        public const string TAG_DISPLAY_EN = "Cold Quench";
        
        /// <summary>
        /// 标签描述（中文）
        /// </summary>
        public const string TAG_DESC_CN = "用于重铸服务";
        
        /// <summary>
        /// 标签描述（英文）
        /// </summary>
        public const string TAG_DESC_EN = "Used for reforge service";
        
        // ============================================================================
        // UI 提示文本
        // ============================================================================
        
        /// <summary>
        /// 固定属性提示（中文）
        /// </summary>
        public const string LOCK_HINT_CN = "点击固定此属性";
        
        /// <summary>
        /// 固定属性提示（英文）
        /// </summary>
        public const string LOCK_HINT_EN = "Click to lock this stat";
        
        /// <summary>
        /// 已固定提示（中文）
        /// </summary>
        public const string LOCKED_HINT_CN = "已固定";
        
        /// <summary>
        /// 已固定提示（英文）
        /// </summary>
        public const string LOCKED_HINT_EN = "Locked";
        
        /// <summary>
        /// 数量不足提示（中文）
        /// </summary>
        public const string NO_FLUID_HINT_CN = "需要冷淬液";
        
        /// <summary>
        /// 数量不足提示（英文）
        /// </summary>
        public const string NO_FLUID_HINT_EN = "Requires Cold Quench Fluid";
        
        /// <summary>
        /// 固定成功提示（中文）
        /// </summary>
        public const string LOCK_SUCCESS_CN = "属性已固定";
        
        /// <summary>
        /// 固定成功提示（英文）
        /// </summary>
        public const string LOCK_SUCCESS_EN = "Stat locked";
        
        // ============================================================================
        // 缓存的标签对象
        // ============================================================================
        private static Tag cachedReforgeTag = null;
        
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
        /// 获取本地化的固定提示
        /// </summary>
        public static string GetLockHint()
        {
            return L10n.T(LOCK_HINT_CN, LOCK_HINT_EN);
        }
        
        /// <summary>
        /// 获取本地化的已固定提示
        /// </summary>
        public static string GetLockedHint()
        {
            return L10n.T(LOCKED_HINT_CN, LOCKED_HINT_EN);
        }
        
        /// <summary>
        /// 获取本地化的数量不足提示
        /// </summary>
        public static string GetNoFluidHint()
        {
            return L10n.T(NO_FLUID_HINT_CN, NO_FLUID_HINT_EN);
        }
        
        /// <summary>
        /// 获取本地化的固定成功提示
        /// </summary>
        public static string GetLockSuccessHint()
        {
            return L10n.T(LOCK_SUCCESS_CN, LOCK_SUCCESS_EN);
        }
        
        /// <summary>
        /// 获取或创建重铸标签
        /// </summary>
        public static Tag GetReforgeTag()
        {
            if (cachedReforgeTag != null) return cachedReforgeTag;
            
            try
            {
                // 创建 Tag ScriptableObject
                cachedReforgeTag = ScriptableObject.CreateInstance<Tag>();
                
                // 使用反射设置私有字段
                var tagType = typeof(Tag);
                
                // 设置 name（ScriptableObject 的 name 属性）
                cachedReforgeTag.name = TAG_NAME;
                
                // 设置 show = true（让标签在UI中显示）
                var showField = tagType.GetField("show", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (showField != null) showField.SetValue(cachedReforgeTag, true);
                
                // 设置 showDescription = true
                var showDescField = tagType.GetField("showDescription", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (showDescField != null) showDescField.SetValue(cachedReforgeTag, true);
                
                // 设置 color（黑色）
                var colorField = tagType.GetField("color", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (colorField != null) colorField.SetValue(cachedReforgeTag, Color.black);
                
                ModBehaviour.DevLog("[ColdQuenchFluidConfig] 创建重铸标签成功");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ColdQuenchFluidConfig] 创建重铸标签失败: " + e.Message);
            }
            
            return cachedReforgeTag;
        }
        
        /// <summary>
        /// 配置冷淬液物品（由 ItemFactory 调用）
        /// </summary>
        public static void ConfigureItem(Item item)
        {
            if (item == null) return;
            
            try
            {
                // 设置本地化键
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                
                // 添加重铸标签
                Tag reforgeTag = GetReforgeTag();
                if (reforgeTag != null && !item.Tags.Contains(reforgeTag))
                {
                    item.Tags.Add(reforgeTag);
                    ModBehaviour.DevLog("[ColdQuenchFluidConfig] 已添加重铸标签");
                }
                
                ModBehaviour.DevLog("[ColdQuenchFluidConfig] 冷淬液物品配置完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ColdQuenchFluidConfig] 配置物品失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注册配置器到 ItemFactory
        /// </summary>
        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[ColdQuenchFluidConfig] 已注册物品配置器");
        }
    }
}
