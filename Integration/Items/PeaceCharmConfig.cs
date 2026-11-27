using System;
using System.Reflection;
using ItemStatsSystem;

namespace BossRush
{
    public static class PeaceCharmConfig
    {
        public const int TYPE_ID = 500031;
        public const string BUNDLE_NAME = "peace_charm";
        public const string LOC_KEY_DISPLAY = "BossRush_PeaceCharm";
        public const string DISPLAY_NAME_CN = "平安护身符";
        public const string DISPLAY_NAME_EN = "Peace Charm";
        public const string DESCRIPTION_CN = "平安符，放背包里就行。生命低于 50% 受击时，有 10% 概率立即回满生命（每张地图限 1 次）。";
        public const string DESCRIPTION_EN = "A good-luck charm for your bag. When hit below 50% HP, 10% chance to restore full HP (once per map).";
        public const float TRIGGER_HEALTH_RATIO = 0.5f;
        public const float TRIGGER_CHANCE = 0.1f;
        public const string WARMTH_BUBBLE_TEXT_CN = "感受到一股暖意...";
        public const string WARMTH_BUBBLE_TEXT_EN = "A warm feeling washes over you...";

        public static string GetDisplayName()
        {
            return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);
        }

        public static string GetDescription()
        {
            return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);
        }

        public static string GetWarmthBubbleText()
        {
            return L10n.T(WARMTH_BUBBLE_TEXT_CN, WARMTH_BUBBLE_TEXT_EN);
        }

        public static void ConfigureItem(Item item)
        {
            if (item == null) return;

            try
            {
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.MaxStackCount = 1;
                item.StackCount = 1;
                item.name = DISPLAY_NAME_EN;
                ModeFItemConfigHelper.SetHiddenMember(item, "description", GetDescription());
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", GetDescription());
                EquipmentHelper.AddTagToItem(item, "Special");

                ModBehaviour.DevLog("[PeaceCharmConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PeaceCharmConfig] ConfigureItem failed: " + e.Message);
            }
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[PeaceCharmConfig] Registered item configurator");
        }
    }
}
