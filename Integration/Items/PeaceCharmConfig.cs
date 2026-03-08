using System;
using System.Reflection;
using ItemStatsSystem;

namespace BossRush
{
    public static class PeaceCharmConfig
    {
        public const int TYPE_ID = 500031;
        public const string BUNDLE_NAME = "peace_charm";
        public const string ICON_NAME = "PeaceCharm";
        public const string LOC_KEY_DISPLAY = "BossRush_PeaceCharm";
        public const string DISPLAY_NAME_CN = "平安护身符";
        public const string DISPLAY_NAME_EN = "Peace Charm";
        public const string DESCRIPTION_CN = "一枚平安护身符。也许挡不住子弹，但至少承着平安归来的心意。";
        public const string DESCRIPTION_EN = "A peace charm. It may not stop bullets, but it carries a heartfelt wish for your safe return.";
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
                SetHiddenMember(item, "description", GetDescription());
                SetHiddenMember(item, "DescriptionRaw", GetDescription());
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
