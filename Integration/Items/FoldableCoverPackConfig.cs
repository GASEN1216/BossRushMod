using System;
using System.Reflection;
using ItemStatsSystem;

namespace BossRush
{
    public static class FoldableCoverPackConfig
    {
        public const int TYPE_ID = 500037;
        public const string BUNDLE_NAME = "foldable_cover_pack";
        public const string PREFAB_NAME = "BossRush_ModeF_FoldableCoverPack";
        public const string LOC_KEY_DISPLAY = "BossRush_FoldableCoverPack";
        public const string DISPLAY_NAME_CN = "折叠掩体包";
        public const string DISPLAY_NAME_EN = "Foldable Cover Pack";
        public const string DESCRIPTION_CN = "一个可快速展开的轻型掩体包。使用后在面前部署一个折叠掩体，提供基础掩护。";
        public const string DESCRIPTION_EN = "A lightweight cover pack that deploys quickly. Use to place a foldable cover in front of you.";

        public static void ConfigureItem(Item item)
        {
            if (item == null) return;
            try
            {
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.MaxStackCount = 10;
                item.StackCount = 1;
                item.Value = 1200;
                item.Quality = 2;
                item.name = DISPLAY_NAME_EN;
                SetHiddenMember(item, "description", L10n.T(DESCRIPTION_CN, DESCRIPTION_EN));
                SetHiddenMember(item, "DescriptionRaw", L10n.T(DESCRIPTION_CN, DESCRIPTION_EN));
                EquipmentHelper.AddTagToItem(item, "Special");
                ModeFItemUsageHelper.AttachToItem(item);
                ModBehaviour.DevLog("[FoldableCoverPackConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FoldableCoverPackConfig] ConfigureItem failed: " + e.Message);
            }
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
        }

        private static void SetHiddenMember(object target, string memberName, object value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo property = target.GetType().GetProperty(memberName, flags);
            if (property != null && property.SetMethod != null) { property.SetValue(target, value); return; }
            FieldInfo field = target.GetType().GetField(memberName, flags);
            if (field != null) { field.SetValue(target, value); }
        }
    }
}
