using System;
using System.Reflection;
using ItemStatsSystem;

namespace BossRush
{
    public static class ReinforcedRoadblockPackConfig
    {
        public const int TYPE_ID = 500038;
        public const string BUNDLE_NAME = "reinforced_roadblock_pack";
        public const string PREFAB_NAME = "BossRush_ModeF_ReinforcedRoadblockPack";
        public const string LOC_KEY_DISPLAY = "BossRush_ReinforcedRoadblockPack";
        public const string DISPLAY_NAME_CN = "加固路障包";
        public const string DISPLAY_NAME_EN = "Reinforced Roadblock Pack";
        public const string DESCRIPTION_CN = "一个重型加固路障包。使用后进入部署预览：左键确认，右键取消，滚轮旋转，中键90度旋转。确认后部署加固路障，提供强力掩护。";
        public const string DESCRIPTION_EN = "A heavy reinforced roadblock pack. Using it enters placement preview: LMB confirm, RMB cancel, mouse wheel rotate, MMB rotate 90 degrees. Confirm to deploy a reinforced roadblock for heavy protection.";

        public static void ConfigureItem(Item item)
        {
            if (item == null) return;
            try
            {
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.MaxStackCount = 5;
                item.StackCount = 1;
                item.Value = 2800;
                item.Quality = 4;
                item.name = DISPLAY_NAME_EN;
                SetHiddenMember(item, "description", L10n.T(DESCRIPTION_CN, DESCRIPTION_EN));
                SetHiddenMember(item, "DescriptionRaw", L10n.T(DESCRIPTION_CN, DESCRIPTION_EN));
                EquipmentHelper.AddTagToItem(item, "Special");
                ModeFItemUsageHelper.AttachToItem(item);
                ModBehaviour.DevLog("[ReinforcedRoadblockPackConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReinforcedRoadblockPackConfig] ConfigureItem failed: " + e.Message);
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
