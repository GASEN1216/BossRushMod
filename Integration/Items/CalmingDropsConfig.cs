using System;
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
        public const string DESCRIPTION_CN = "羽织亲手调配的安神滴剂，带着淡淡草药香。能稍微压住伤后的躁痛与紧绷，让人勉强睡个安稳觉。";
        public const string DESCRIPTION_EN = "A calming tincture blended by Yu Zhi. It eases post-battle pain and tension enough to help you get some rest.";

        public static string GetDisplayName()
        {
            return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);
        }

        public static string GetDescription()
        {
            return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);
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
