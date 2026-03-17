using System;
using System.Reflection;
using ItemStatsSystem;

namespace BossRush
{
    public static class EmergencyRepairSprayConfig
    {
        public const int TYPE_ID = 500040;
        public const string BUNDLE_NAME = "emergency_repair_spray";
        public const string PREFAB_NAME = "BossRush_ModeF_EmergencyRepairSpray";
        public const string LOC_KEY_DISPLAY = "BossRush_EmergencyRepairSpray";
        public const string DISPLAY_NAME_CN = "应急维修喷剂";
        public const string DISPLAY_NAME_EN = "Emergency Repair Spray";
        public const string DESCRIPTION_CN = "一罐速效修补喷剂。使用后修复3米内自己放置的最近工事，恢复25%最大生命值。";
        public const string DESCRIPTION_EN = "A quick-fix repair spray. Use to repair the nearest fortification you placed within 3m, restoring 25% max health.";

        public static void ConfigureItem(Item item)
        {
            if (item == null) return;
            try
            {
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.MaxStackCount = 10;
                item.StackCount = 1;
                item.Value = 1600;
                item.Quality = 3;
                item.name = DISPLAY_NAME_EN;
                SetHiddenMember(item, "description", L10n.T(DESCRIPTION_CN, DESCRIPTION_EN));
                SetHiddenMember(item, "DescriptionRaw", L10n.T(DESCRIPTION_CN, DESCRIPTION_EN));
                EquipmentHelper.AddTagToItem(item, "Special");
                ModeFItemUsageHelper.AttachToItem(item);
                ModBehaviour.DevLog("[EmergencyRepairSprayConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EmergencyRepairSprayConfig] ConfigureItem failed: " + e.Message);
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
