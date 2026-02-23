// ============================================================================
// RespawnItemConfig.cs - 刷怪消耗品物品配置
// ============================================================================
// 模块说明：
//   定义 Mode E（划地为营）刷怪消耗品的配置常量、本地化注入逻辑。
//   包含挑衅烟雾弹（Taunt Smoke）和混沌引爆器（Chaos Detonator）两个物品。
//   玩家使用后可在刷怪点重新生成随机阵营 Boss，增加战斗策略性。
// ============================================================================

using System;
using System.Reflection;
using ItemStatsSystem;
using UnityEngine;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// 刷怪消耗品物品配置（Mode E 刷怪道具）
    /// </summary>
    public static class RespawnItemConfig
    {
        // ============================================================================
        // 物品 TypeID
        // ============================================================================

        /// <summary>挑衅烟雾弹 TypeID</summary>
        public const int TAUNT_SMOKE_TYPE_ID = 500027;

        /// <summary>混沌引爆器 TypeID</summary>
        public const int CHAOS_DETONATOR_TYPE_ID = 500028;

        /// <summary>所有刷怪消耗品 TypeID 列表</summary>
        public static readonly int[] ALL_RESPAWN_ITEM_TYPE_IDS = new int[]
        {
            TAUNT_SMOKE_TYPE_ID,
            CHAOS_DETONATOR_TYPE_ID
        };

        // ============================================================================
        // 本地化常量
        // ============================================================================

        // --- 挑衅烟雾弹 ---
        public const string TAUNT_SMOKE_LOC_KEY = "BossRush_TauntSmoke";
        public const string TAUNT_SMOKE_NAME_CN = "挑衅烟雾弹";
        public const string TAUNT_SMOKE_NAME_EN = "Taunt Smoke";
        public const string TAUNT_SMOKE_DESC_CN = "一枚散发着挑衅气息的烟雾弹。使用后在最近的10个刷怪点重新生成随机阵营Boss。";
        public const string TAUNT_SMOKE_DESC_EN = "A smoke grenade that provokes nearby enemies. Respawns random faction Bosses at the 10 nearest spawn points.";

        // --- 混沌引爆器 ---
        public const string CHAOS_DETONATOR_LOC_KEY = "BossRush_ChaosDetonator";
        public const string CHAOS_DETONATOR_NAME_CN = "混沌引爆器";
        public const string CHAOS_DETONATOR_NAME_EN = "Chaos Detonator";
        public const string CHAOS_DETONATOR_DESC_CN = "一个充满混沌能量的引爆装置。使用后在全图所有刷怪点重新生成随机阵营Boss。";
        public const string CHAOS_DETONATOR_DESC_EN = "A detonator charged with chaotic energy. Respawns random faction Bosses at every spawn point on the map.";

        // ============================================================================
        // 物品信息结构（内部使用）
        // ============================================================================

        private struct RespawnItemInfo
        {
            public int typeId;
            public string locKey;
            public string nameCN;
            public string nameEN;
            public string descCN;
            public string descEN;
        }

        private static readonly RespawnItemInfo[] AllItems = new RespawnItemInfo[]
        {
            new RespawnItemInfo
            {
                typeId = TAUNT_SMOKE_TYPE_ID,
                locKey = TAUNT_SMOKE_LOC_KEY,
                nameCN = TAUNT_SMOKE_NAME_CN,
                nameEN = TAUNT_SMOKE_NAME_EN,
                descCN = TAUNT_SMOKE_DESC_CN,
                descEN = TAUNT_SMOKE_DESC_EN
            },
            new RespawnItemInfo
            {
                typeId = CHAOS_DETONATOR_TYPE_ID,
                locKey = CHAOS_DETONATOR_LOC_KEY,
                nameCN = CHAOS_DETONATOR_NAME_CN,
                nameEN = CHAOS_DETONATOR_NAME_EN,
                descCN = CHAOS_DETONATOR_DESC_CN,
                descEN = CHAOS_DETONATOR_DESC_EN
            }
        };

        // ============================================================================
        // 物品配置
        // ============================================================================

        /// <summary>
        /// 配置刷怪消耗品物品（由 ItemFactory 调用）
        /// </summary>
        public static void ConfigureItem(Item item)
        {
            if (item == null) return;

            try
            {
                int typeId = item.TypeID;

                // 查找对应的物品信息，设置本地化键
                for (int i = 0; i < AllItems.Length; i++)
                {
                    if (AllItems[i].typeId == typeId)
                    {
                        item.DisplayNameRaw = AllItems[i].locKey;
                        break;
                    }
                }

                // 设置品质：挑衅烟雾弹=3(蓝色/Rare)，混沌引爆器=5(紫色/Epic)
                if (typeId == TAUNT_SMOKE_TYPE_ID)
                {
                    item.Quality = 3;
                    item.Value = 1500;
                }
                else if (typeId == CHAOS_DETONATOR_TYPE_ID)
                {
                    item.Quality = 5;
                    item.Value = 3500;
                }

                // 添加 Special 标签（防止进入随机搜集池）
                try
                {
                    Tag specialTag = GameplayDataSettings.Tags.Special;
                    if (specialTag != null && !item.Tags.Contains(specialTag))
                    {
                        item.Tags.Add(specialTag);
                    }
                }
                catch { }

                // ============================================================
                // 注册 UsageBehavior，使物品可通过原版 CA_UseItem 流程使用
                // ============================================================

                // 添加 UsageUtilities 组件（如果没有）
                UsageUtilities usageUtils = item.GetComponent<UsageUtilities>();
                if (usageUtils == null)
                {
                    usageUtils = item.gameObject.AddComponent<UsageUtilities>();
                    SetUsageUtilitiesMaster(usageUtils, item);
                }

                // 添加 RespawnItemUsage 使用行为组件（如果没有）
                RespawnItemUsage usage = item.GetComponent<RespawnItemUsage>();
                if (usage == null)
                {
                    usage = item.gameObject.AddComponent<RespawnItemUsage>();
                }

                // 将使用行为添加到 behaviors 列表
                if (usageUtils.behaviors == null)
                {
                    usageUtils.behaviors = new System.Collections.Generic.List<UsageBehavior>();
                }
                if (!usageUtils.behaviors.Contains(usage))
                {
                    usageUtils.behaviors.Add(usage);
                }

                // 设置 Item 的 usageUtilities 字段（通过反射）
                SetItemUsageUtilities(item, usageUtils);

                ModBehaviour.DevLog("[RespawnItemConfig] 刷怪消耗品配置完成: TypeID=" + typeId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[RespawnItemConfig] 配置物品失败: " + e.Message);
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
                    BindingFlags.NonPublic | BindingFlags.Instance);
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
                    BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(item, usageUtils);
            }
            catch { }
        }

        // ============================================================================
        // 注册与本地化
        // ============================================================================

        /// <summary>
        /// 注册所有刷怪消耗品的配置器到 ItemFactory
        /// </summary>
        public static void RegisterConfigurators()
        {
            for (int i = 0; i < ALL_RESPAWN_ITEM_TYPE_IDS.Length; i++)
            {
                ItemFactory.RegisterConfigurator(ALL_RESPAWN_ITEM_TYPE_IDS[i], ConfigureItem);
            }
            ModBehaviour.DevLog("[RespawnItemConfig] 已注册 " + ALL_RESPAWN_ITEM_TYPE_IDS.Length + " 个刷怪消耗品配置器");
        }

        /// <summary>
        /// 注入所有刷怪消耗品的本地化文本
        /// </summary>
        public static void InjectLocalization()
        {
            try
            {
                bool isChinese = L10n.IsChinese;

                for (int i = 0; i < AllItems.Length; i++)
                {
                    RespawnItemInfo info = AllItems[i];
                    string displayName = isChinese ? info.nameCN : info.nameEN;
                    string description = isChinese ? info.descCN : info.descEN;

                    // 注入本地化键
                    LocalizationHelper.InjectLocalization(info.locKey, displayName);
                    LocalizationHelper.InjectLocalization(info.locKey + "_Desc", description);

                    // 注入物品 ID 键（游戏系统使用 Item_{TypeID} 格式）
                    string itemKey = "Item_" + info.typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);

                    // 注入中英文键（兼容性）
                    LocalizationHelper.InjectLocalization(info.nameCN, displayName);
                    LocalizationHelper.InjectLocalization(info.nameEN, displayName);
                    LocalizationHelper.InjectLocalization(info.nameCN + "_Desc", description);
                    LocalizationHelper.InjectLocalization(info.nameEN + "_Desc", description);
                }

                ModBehaviour.DevLog("[RespawnItemConfig] 本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[RespawnItemConfig] 本地化注入失败: " + e.Message);
            }
        }
    }
}
