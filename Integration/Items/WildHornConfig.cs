// ============================================================================
// WildHornConfig.cs - 荒野号角物品配置
// ============================================================================
// 模块说明：
//   定义荒野号角物品的配置常量和初始化逻辑
//   荒野号角是一种可复用的主动道具，使用后可召唤或呼唤坐骑（马）
//   复用游戏原生马匹系统（CharacterRandomPreset.testVehicle）
// ============================================================================

using System;
using System.Reflection;
using Duckov.ItemUsage;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 荒野号角物品配置
    /// </summary>
    public static class WildHornConfig
    {
        // ============================================================================
        // 物品基础配置
        // ============================================================================

        /// <summary>物品 TypeID（延续现有编号序列：500018=成就勋章）</summary>
        public const int TYPE_ID = 500019;

        /// <summary>AssetBundle 名称</summary>
        public const string BUNDLE_NAME = "wild_horn";

        /// <summary>本地化键 - 显示名称</summary>
        public const string LOC_KEY_DISPLAY = "BossRush_WildHorn";

        // ============================================================================
        // 冷却配置
        // ============================================================================

        /// <summary>使用冷却时间（秒）</summary>
        public const float COOLDOWN_SECONDS = 3f;

        // ============================================================================
        // 狼坐骑模型配置
        // ============================================================================

        /// <summary>狼模型 Prefab 名称（对应 Assets/entity/ 下的 AssetBundle）</summary>
        public const string WOLF_PREFAB_NAME = "wolf_mount";

        /// <summary>狼模型 Animator 中步行动画状态名</summary>
        public const string WOLF_ANIM_WALK = "Walk";

        /// <summary>狼模型正常步行动画速度</summary>
        public const float WOLF_ANIM_WALK_SPEED = 1.0f;

        /// <summary>狼模型奔跑时动画速度倍率（相对于游戏原生的2.0倍，再乘以此倍率）</summary>
        public const float WOLF_ANIM_RUN_SPEED_MULTIPLIER = 1.5f;

        /// <summary>判定坐骑"正在移动"的速度阈值</summary>
        public const float WOLF_MOVE_THRESHOLD = 0.3f;

        /// <summary>狼坐骑喂食物品ID（饺子）</summary>
        public const int WOLF_FEED_ITEM_ID = 449;

        // ============================================================================
        // 狼坐骑骑乘位置配置
        // ============================================================================

        /// <summary>狼坐骑骑乘点相对于马匹原始骑乘点的位置偏移</summary>
        public static readonly Vector3 WOLF_RIDE_POSITION_OFFSET = new Vector3(0f, -0.2f, 0.3f);

        /// <summary>狼坐骑骑乘点相对于马匹原始骑乘点的旋转偏移（欧拉角）</summary>
        public static readonly Vector3 WOLF_RIDE_ROTATION_OFFSET = new Vector3(0f, 0f, 0f);



        // ============================================================================
        // 本地化常量 - 物品名称与描述
        // ============================================================================

        /// <summary>物品显示名称（中文）</summary>
        public const string DISPLAY_NAME_CN = "荒野号角";

        /// <summary>物品显示名称（英文）</summary>
        public const string DISPLAY_NAME_EN = "Wild Horn";

        /// <summary>物品描述（中文）</summary>
        public const string DESCRIPTION_CN = "一只古老的号角，吹响它可以召唤一匹忠诚的坐骑。如果坐骑已在身边，再次吹响可将其呼唤过来。";

        /// <summary>物品描述（英文）</summary>
        public const string DESCRIPTION_EN = "An ancient horn. Blow it to summon a loyal mount. If the mount is already nearby, blow again to call it to your side.";

        /// <summary>使用说明（中文）</summary>
        public const string USAGE_DESC_CN = "使用：召唤/呼唤坐骑";

        /// <summary>使用说明（英文）</summary>
        public const string USAGE_DESC_EN = "Use: Summon/Call mount";

        // ============================================================================
        // 本地化常量 - 提示消息
        // ============================================================================

        /// <summary>召唤成功提示（中文）</summary>
        public const string SUMMON_SUCCESS_CN = "坐骑已召唤！";

        /// <summary>召唤成功提示（英文）</summary>
        public const string SUMMON_SUCCESS_EN = "Mount summoned!";

        /// <summary>呼唤坐骑提示（中文）</summary>
        public const string CALL_MOUNT_CN = "坐骑正在赶来...";

        /// <summary>呼唤坐骑提示（英文）</summary>
        public const string CALL_MOUNT_EN = "Mount is on the way...";

        /// <summary>召唤失败提示（中文）</summary>
        public const string SUMMON_FAIL_CN = "召唤失败...";

        /// <summary>召唤失败提示（英文）</summary>
        public const string SUMMON_FAIL_EN = "Summon failed...";

        /// <summary>冷却中提示（中文）</summary>
        public const string COOLDOWN_CN = "号角还在冷却中...";

        /// <summary>冷却中提示（英文）</summary>
        public const string COOLDOWN_EN = "Horn is cooling down...";

        // ============================================================================
        // 本地化辅助方法
        // ============================================================================

        /// <summary>获取本地化的物品名称</summary>
        public static string GetDisplayName()
        {
            return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);
        }

        /// <summary>获取本地化的物品描述</summary>
        public static string GetDescription()
        {
            return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);
        }

        /// <summary>获取本地化的使用说明</summary>
        public static string GetUsageDescription()
        {
            return L10n.T(USAGE_DESC_CN, USAGE_DESC_EN);
        }

        /// <summary>获取本地化的召唤成功提示</summary>
        public static string GetSummonSuccessHint()
        {
            return L10n.T(SUMMON_SUCCESS_CN, SUMMON_SUCCESS_EN);
        }

        /// <summary>获取本地化的呼唤坐骑提示</summary>
        public static string GetCallMountHint()
        {
            return L10n.T(CALL_MOUNT_CN, CALL_MOUNT_EN);
        }

        /// <summary>获取本地化的召唤失败提示</summary>
        public static string GetSummonFailHint()
        {
            return L10n.T(SUMMON_FAIL_CN, SUMMON_FAIL_EN);
        }

        /// <summary>获取本地化的冷却中提示</summary>
        public static string GetCooldownHint()
        {
            return L10n.T(COOLDOWN_CN, COOLDOWN_EN);
        }

        // ============================================================================
        // 物品配置
        // ============================================================================

        /// <summary>
        /// 配置荒野号角物品（由 ItemFactory 调用）
        /// </summary>
        public static void ConfigureItem(Item item)
        {
            if (item == null) return;

            try
            {
                // 1. 设置显示名称本地化键
                item.DisplayNameRaw = LOC_KEY_DISPLAY;

                // 2. 设置耐久度（防止物品被消耗）
                item.MaxDurability = 999f;
                item.Durability = 999f;

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

                // 4. 添加 WildHornUsage 使用行为组件
                WildHornUsage usage = item.gameObject.AddComponent<WildHornUsage>();
                usageUtils.behaviors.Add(usage);

                // 5. 关联 UsageUtilities 到 Item（通过反射设置私有字段）
                var usageField = typeof(Item).GetField("usageUtilities",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (usageField != null)
                {
                    usageField.SetValue(item, usageUtils);
                }

                // 6. 添加 Special 标签（防止物品进入随机搜集池）
                AddSpecialTag(item);

                ModBehaviour.DevLog("[WildHornConfig] 荒野号角物品配置完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WildHornConfig] 配置物品失败: " + e.Message);
            }
        }

        /// <summary>
        /// 添加 Special 标签到物品（防止进入随机搜集池）
        /// </summary>
        private static void AddSpecialTag(Item item)
        {
            try
            {
                Tag specialTag = GameplayDataSettings.Tags.Special;
                if (specialTag != null && !item.Tags.Contains(specialTag))
                {
                    item.Tags.Add(specialTag);
                    ModBehaviour.DevLog("[WildHornConfig] 已添加 Special 标签");
                }
                else if (specialTag == null)
                {
                    ModBehaviour.DevLog("[WildHornConfig] 警告: 无法获取 Special 标签");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WildHornConfig] 添加 Special 标签失败: " + e.Message);
            }
        }

        // ============================================================================
        // 注册与本地化
        // ============================================================================

        /// <summary>
        /// 注册配置器到 ItemFactory
        /// </summary>
        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[WildHornConfig] 已注册物品配置器");
        }

        /// <summary>
        /// 注入本地化文本
        /// </summary>
        public static void InjectLocalization()
        {
            try
            {
                bool isChinese = L10n.IsChinese;

                // 注入显示名称
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY, isChinese ? DISPLAY_NAME_CN : DISPLAY_NAME_EN);

                // 注入描述（游戏使用 {DisplayNameRaw}_Desc 格式查找描述）
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

                ModBehaviour.DevLog("[WildHornConfig] 本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WildHornConfig] 本地化注入失败: " + e.Message);
            }
        }
    }
}
