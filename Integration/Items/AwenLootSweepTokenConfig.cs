using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 阿稳扫箱令配置。
    /// 使用后会让阿稳按由近到远的顺序清理当前局快照中的掉落箱。
    /// </summary>
    public static class AwenLootSweepTokenConfig
    {
        public const int TYPE_ID = 500042;
        public const string BUNDLE_NAME = "awen_loot_sweep_token";
        public const string PREFAB_NAME = "BossRush_AwenLootSweepToken";
        public const string LOC_KEY_DISPLAY = "BossRush_AwenLootSweepToken";
        public const string DISPLAY_NAME_CN = "阿稳扫箱令";
        public const string DISPLAY_NAME_EN = "Awen Loot Sweep Token";
        public const string DESCRIPTION_CN = "刻着鸭邮回收章的铜令。\n在标准BossRush、划地为营或血猎追击中使用。阿稳会从近到远清理当前已有的掉落箱。";
        public const string DESCRIPTION_EN = "A brass token with Awen's cleanup seal.\nUse in standard BossRush, Faction War or Blood Hunt. Awen clears existing lootboxes from nearest to farthest.";
        public const string USE_DESC_CN = "使用：叫阿稳清掉场上现有的掉落箱";
        public const string USE_DESC_EN = "Use: Call Awen to clear the lootboxes already on the field";
        public const float USE_TIME_SECONDS = 1f;

        public static string GetDisplayName()
        {
            return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);
        }

        public static string GetDescription()
        {
            return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);
        }

        public static string GetUseDescription()
        {
            return L10n.T(USE_DESC_CN, USE_DESC_EN);
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[AwenLootSweepTokenConfig] Registered item configurator");
        }

        public static void ConfigureItem(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.name = DISPLAY_NAME_EN;
                item.MaxStackCount = 1;
                item.StackCount = 1;
                item.Value = 3200;
                item.Quality = 4;
                ModeFItemConfigHelper.SetHiddenMember(item, "description", GetDescription());
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", GetDescription());
                EquipmentHelper.AddTagToItem(item, "Special");
                ConfigureUsage(item);
                ModBehaviour.DevLog("[AwenLootSweepTokenConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenLootSweepTokenConfig] ConfigureItem failed: " + e.Message);
            }
        }

        public static bool EnsureRuntimeRegistration()
        {
            return RuntimeItemFallback.Ensure(TYPE_ID, PREFAB_NAME, "[AwenLootSweepTokenConfig] ",
                FindRuntimeFallbackSource, ConfigureItem);
        }

        public static void InjectLocalization()
        {
            try
            {
                bool isChinese = L10n.IsChinese;
                string displayName = isChinese ? DISPLAY_NAME_CN : DISPLAY_NAME_EN;
                string description = isChinese ? DESCRIPTION_CN : DESCRIPTION_EN;

                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY, displayName);
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY + "_Desc", description);
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID, displayName);
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID + "_Desc", description);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN, displayName);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_EN, displayName);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN + "_Desc", description);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_EN + "_Desc", description);
                ModBehaviour.DevLog("[AwenLootSweepTokenConfig] Localization injected");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenLootSweepTokenConfig] InjectLocalization failed: " + e.Message);
            }
        }

        private static Item FindRuntimeFallbackSource()
        {
            return RuntimeItemFallback.FindSource(new int[]
            {
                AwenCourierTokenConfig.TYPE_ID,
                RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID,
                RespawnItemConfig.TAUNT_SMOKE_TYPE_ID
            });
        }

        private static void ConfigureUsage(Item item)
        {
            UsageUtilities usageUtils = item.GetComponent<UsageUtilities>();
            if (usageUtils == null)
            {
                usageUtils = item.gameObject.AddComponent<UsageUtilities>();
            }

            if (usageUtils.behaviors == null)
            {
                usageUtils.behaviors = new List<UsageBehavior>();
            }
            else
            {
                usageUtils.behaviors.Clear();
            }

            AwenLootSweepTokenUsage usage = item.GetComponent<AwenLootSweepTokenUsage>();
            if (usage == null)
            {
                usage = item.gameObject.AddComponent<AwenLootSweepTokenUsage>();
            }

            usageUtils.behaviors.Add(usage);
            ModeFItemConfigHelper.BindUsageUtilitiesToItem(item, usageUtils, USE_TIME_SECONDS);
        }

    }
}
