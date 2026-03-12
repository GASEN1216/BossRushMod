using System;
using System.Collections.Generic;
using System.Reflection;
using ItemStatsSystem;

namespace BossRush
{
    public static class CalmingDropsConfig
    {
        public const int TYPE_ID = 500030;
        public const string BUNDLE_NAME = "calming_drops";
        public const string ICON_NAME = "CalmingDrops";
        public const int REWARD_COUNT = 5;
        public const string LOC_KEY_DISPLAY = "BossRush_CalmingDrops";
        public const string DISPLAY_NAME_CN = "安神滴剂";
        public const string DISPLAY_NAME_EN = "Calming Drops";
        public const string DESCRIPTION_CN = "羽织亲手调配的安神滴剂，带着淡淡草药香。使用后可清除大部分负面buff。";
        public const string DESCRIPTION_EN = "A calming tincture blended by Yu Zhi. Use it to clear most negative buffs on you.";
        public const string USE_DESC_CN = "使用：清除大部分负面buff";
        public const string USE_DESC_EN = "Use: Clear all negative status effects";
        public const float USE_TIME_SECONDS = 2.5f;

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

        public static void ConfigureItem(Item item)
        {
            if (item == null) return;

            try
            {
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.MaxStackCount = 20;
                item.StackCount = 1;
                item.name = DISPLAY_NAME_EN;
                SetHiddenMember(item, "description", GetDescription());
                SetHiddenMember(item, "DescriptionRaw", GetDescription());

                ConfigureUsage(item);
                EquipmentHelper.AddTagToItem(item, "Injector");
                EquipmentHelper.AddTagToItem(item, "Medic");

                ModBehaviour.DevLog("[CalmingDropsConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CalmingDropsConfig] ConfigureItem failed: " + e.Message);
            }
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[CalmingDropsConfig] Registered item configurator");
        }

        private static void ConfigureUsage(Item item)
        {
            UsageUtilities usageUtils = item.GetComponent<UsageUtilities>();
            if (usageUtils == null)
            {
                usageUtils = item.gameObject.AddComponent<UsageUtilities>();
            }

            SetUsageUtilitiesMaster(usageUtils, item);
            SetUsageTime(usageUtils, USE_TIME_SECONDS);

            CalmingDropsUsage usage = item.GetComponent<CalmingDropsUsage>();
            if (usage == null)
            {
                usage = item.gameObject.AddComponent<CalmingDropsUsage>();
            }

            if (usageUtils.behaviors == null)
            {
                usageUtils.behaviors = new List<UsageBehavior>();
            }

            if (!usageUtils.behaviors.Contains(usage))
            {
                usageUtils.behaviors.Add(usage);
            }

            SetItemUsageUtilities(item, usageUtils);
        }

        private static void SetUsageUtilitiesMaster(UsageUtilities usageUtils, Item item)
        {
            try
            {
                FieldInfo masterField = typeof(UsageUtilities).BaseType.GetField(
                    "master",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                masterField?.SetValue(usageUtils, item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CalmingDropsConfig] SetUsageUtilitiesMaster failed: " + e.Message);
            }
        }

        private static void SetItemUsageUtilities(Item item, UsageUtilities usageUtils)
        {
            try
            {
                FieldInfo field = typeof(Item).GetField(
                    "usageUtilities",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(item, usageUtils);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CalmingDropsConfig] SetItemUsageUtilities failed: " + e.Message);
            }
        }

        private static void SetUsageTime(UsageUtilities usageUtils, float useTime)
        {
            try
            {
                FieldInfo field = typeof(UsageUtilities).GetField(
                    "useTime",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(usageUtils, useTime);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CalmingDropsConfig] SetUsageTime failed: " + e.Message);
            }
        }

        private static void SetHiddenMember(object target, string memberName, object value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo property = target.GetType().GetProperty(memberName, flags);
            if (property != null && property.SetMethod != null)
            {
                property.SetValue(target, value);
                return;
            }

            FieldInfo field = target.GetType().GetField(memberName, flags);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }
    }
}
