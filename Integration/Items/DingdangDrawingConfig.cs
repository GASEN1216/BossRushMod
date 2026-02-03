// ============================================================================
// DingdangDrawingConfig.cs - 叮当涂鸦物品配置
// ============================================================================
// 模块说明：
//   哥布林"叮当"好感度达到10级后赠送给玩家的礼物。
//   一张手绘的涂鸦，使用后打开大图欣赏，不消耗物品。
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Duckov.ItemUsage;
using Duckov.Utilities;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 叮当涂鸦物品配置
    /// </summary>
    public static class DingdangDrawingConfig
    {
        // ============================================================================
        // 常量定义
        // ============================================================================

        /// <summary>物品 TypeID（需要与 Unity 预制体中的 typeID 一致）</summary>
        public const int TYPE_ID = 500016;

        /// <summary>AssetBundle 名称</summary>
        public const string BUNDLE_NAME = "dingdang_drawing";

        /// <summary>物品预制体名称</summary>
        public const string PREFAB_NAME = "DingdangDrawing";

        /// <summary>高清大图资源名称</summary>
        public const string IMAGE_NAME = "DingdangDrawing";

        /// <summary>图标资源名称</summary>
        public const string ICON_NAME = "DingdangDrawing_icon";

        /// <summary>本地化键 - 显示名称</summary>
        public const string LOC_KEY_DISPLAY = "item_dingdang_drawing_name";

        /// <summary>本地化键 - 描述</summary>
        public const string LOC_KEY_DESC = "item_dingdang_drawing_desc";

        /// <summary>显示名称（中文）</summary>
        public const string DISPLAY_NAME_CN = "叮当涂鸦";

        /// <summary>显示名称（英文）</summary>
        public const string DISPLAY_NAME_EN = "Dingdang's Doodle";

        /// <summary>描述（中文）</summary>
        public const string DESCRIPTION_CN = "叮当花了好多好多天才画完的...才、才不是特意画给你的！只是正好画完了而已！...请好好保管。";

        /// <summary>描述（英文）</summary>
        public const string DESCRIPTION_EN = "Dingdang spent so many days drawing this... I-it's not like I drew it especially for you! I just happened to finish it! ...Please take good care of it.";

        /// <summary>使用说明（中文）</summary>
        public const string USAGE_DESC_CN = "使用：欣赏这幅画";

        /// <summary>使用说明（英文）</summary>
        public const string USAGE_DESC_EN = "Use: Admire this drawing";

        // ============================================================================
        // 公共方法
        // ============================================================================

        /// <summary>
        /// 获取本地化显示名称
        /// </summary>
        public static string DisplayName => L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);

        /// <summary>
        /// 获取本地化描述
        /// </summary>
        public static string Description => L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);

        /// <summary>
        /// 获取本地化使用说明
        /// </summary>
        public static string UsageDescription => L10n.T(USAGE_DESC_CN, USAGE_DESC_EN);

        /// <summary>
        /// 配置叮当涂鸦物品（由 ItemFactory 调用）
        /// </summary>
        public static void ConfigureItem(Item item)
        {
            if (item == null) return;

            try
            {
                // 1. 设置显示名称
                item.DisplayNameRaw = LOC_KEY_DISPLAY;

                // 2. 设置耐久度（防止物品被消耗）
                // 直接设置公共属性（与 WikiBook 保持一致）
                item.MaxDurability = 999f;
                item.Durability = 999f;
                ModBehaviour.DevLog("[DingdangDrawing] 已设置耐久度: MaxDurability=999, Durability=999");

                // 3. 添加 UsageUtilities 组件
                UsageUtilities usageUtils = item.GetComponent<UsageUtilities>();
                if (usageUtils == null)
                {
                    usageUtils = item.gameObject.AddComponent<UsageUtilities>();
                }

                // 确保 behaviors 列表存在
                if (usageUtils.behaviors == null)
                {
                    usageUtils.behaviors = new System.Collections.Generic.List<UsageBehavior>();
                }

                // 4. 添加使用行为
                DingdangDrawingUsage drawingUsage = item.gameObject.AddComponent<DingdangDrawingUsage>();
                usageUtils.behaviors.Add(drawingUsage);

                // 5. 关联 UsageUtilities 到 Item
                var usageField = typeof(Item).GetField("usageUtilities",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (usageField != null)
                {
                    usageField.SetValue(item, usageUtils);
                }

                // 6. 添加"叮当"标签（绿色显示）
                AddDingdangTag(item);

                ModBehaviour.DevLog("[DingdangDrawing] 物品配置完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DingdangDrawing] 配置物品失败: " + e.Message);
            }
        }

        /// <summary>
        /// 缓存的叮当标签
        /// </summary>
        private static Tag cachedDingdangTag = null;

        /// <summary>
        /// 获取或创建叮当标签
        /// </summary>
        private static Tag GetDingdangTag()
        {
            if (cachedDingdangTag != null) return cachedDingdangTag;

            try
            {
                // 创建 Tag ScriptableObject
                cachedDingdangTag = ScriptableObject.CreateInstance<Tag>();

                // 使用反射设置私有字段
                var tagType = typeof(Tag);

                // 设置 name（ScriptableObject 的 name 属性）
                cachedDingdangTag.name = "Dingdang";

                // 设置 show = true（让标签在UI中显示）
                var showField = tagType.GetField("show", BindingFlags.NonPublic | BindingFlags.Instance);
                if (showField != null) showField.SetValue(cachedDingdangTag, true);

                // 设置 showDescription = true
                var showDescField = tagType.GetField("showDescription", BindingFlags.NonPublic | BindingFlags.Instance);
                if (showDescField != null) showDescField.SetValue(cachedDingdangTag, true);

                // 设置 color（绿色）- 使用 Color 类型而非 Color32
                var colorField = tagType.GetField("color", BindingFlags.NonPublic | BindingFlags.Instance);
                if (colorField != null) colorField.SetValue(cachedDingdangTag, new Color(0f, 0.78f, 0f, 1f));

                ModBehaviour.DevLog("[DingdangDrawing] 创建叮当标签成功");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DingdangDrawing] 创建叮当标签失败: " + e.Message);
            }

            return cachedDingdangTag;
        }

        /// <summary>
        /// 添加"叮当"标签到物品
        /// </summary>
        private static void AddDingdangTag(Item item)
        {
            try
            {
                Tag dingdangTag = GetDingdangTag();
                if (dingdangTag != null && !item.Tags.Contains(dingdangTag))
                {
                    item.Tags.Add(dingdangTag);
                    ModBehaviour.DevLog("[DingdangDrawing] 已添加叮当标签");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DingdangDrawing] 添加标签失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注册配置器到 ItemFactory
        /// </summary>
        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[DingdangDrawing] 已注册物品配置器");
        }

        /// <summary>
        /// 注入本地化文本
        /// </summary>
        public static void InjectLocalization()
        {
            try
            {
                // 根据当前语言注入对应的本地化文本
                bool isChinese = L10n.IsChinese;

                // 注入显示名称
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY, isChinese ? DISPLAY_NAME_CN : DISPLAY_NAME_EN);

                // 注入描述 - 游戏使用 {DisplayNameRaw}_Desc 格式查找描述
                // DisplayNameRaw = LOC_KEY_DISPLAY = "item_dingdang_drawing_name"
                // 所以描述键应该是 "item_dingdang_drawing_name_Desc"
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY + "_Desc", isChinese ? DESCRIPTION_CN : DESCRIPTION_EN);

                // 注入物品 ID 键（游戏系统使用 Item_{TypeID} 格式查找本地化）
                string itemKey = "Item_" + TYPE_ID;
                LocalizationHelper.InjectLocalization(itemKey, isChinese ? DISPLAY_NAME_CN : DISPLAY_NAME_EN);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", isChinese ? DESCRIPTION_CN : DESCRIPTION_EN);

                // 注入中英文键（兼容性）
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN, isChinese ? DISPLAY_NAME_CN : DISPLAY_NAME_EN);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_EN, isChinese ? DISPLAY_NAME_CN : DISPLAY_NAME_EN);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN + "_Desc", isChinese ? DESCRIPTION_CN : DESCRIPTION_EN);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_EN + "_Desc", isChinese ? DESCRIPTION_CN : DESCRIPTION_EN);

                // 注入"叮当"标签的本地化
                string tagDisplayName = L10n.T("叮当", "Dingdang");
                string tagDescription = L10n.T("来自叮当的礼物", "A gift from Dingdang");
                LocalizationHelper.InjectLocalization("Tag_Dingdang", tagDisplayName);
                LocalizationHelper.InjectLocalization("Tag_Dingdang_Desc", tagDescription);
                // 标签名称本地化（游戏可能使用 Tag.name 作为键）
                LocalizationHelper.InjectLocalization("Dingdang", tagDisplayName);

                ModBehaviour.DevLog("[DingdangDrawing] 本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DingdangDrawing] 本地化注入失败: " + e.Message);
            }
        }

        /// <summary>
        /// 在指定位置生成叮当涂鸦（掉落在地上）
        /// </summary>
        /// <param name="dropPosition">掉落位置</param>
        /// <returns>是否生成成功</returns>
        public static bool SpawnAtPosition(Vector3 dropPosition)
        {
            try
            {
                // 使用 ItemAssetsCollection 生成物品
                Item drawing = ItemAssetsCollection.InstantiateSync(TYPE_ID);

                if (drawing == null)
                {
                    ModBehaviour.DevLog("[DingdangDrawing] [ERROR] 无法生成叮当涂鸦物品，TypeID=" + TYPE_ID);
                    return false;
                }

                // 在指定位置掉落物品（而不是直接发送给玩家背包）
                Vector3 dropDirection = Vector3.forward;
                drawing.Drop(dropPosition, true, dropDirection, 0f);

                ModBehaviour.DevLog("[DingdangDrawing] 叮当涂鸦已掉落在地上，位置=" + dropPosition);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DingdangDrawing] 生成失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 给玩家赠送叮当涂鸦（已废弃，请使用 SpawnAtPosition）
        /// </summary>
        [Obsolete("请使用 SpawnAtPosition 在NPC位置掉落物品")]
        public static bool GiveToPlayer()
        {
            try
            {
                // 直接使用 SpawnAtPosition 在玩家位置掉落
                var main = CharacterMainControl.Main;
                if (main != null)
                {
                    return SpawnAtPosition(main.transform.position);
                }
                ModBehaviour.DevLog("[DingdangDrawing] 赠送失败: 找不到玩家");
                return false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DingdangDrawing] 赠送失败: " + e.Message);
                return false;
            }
        }
    }
}
