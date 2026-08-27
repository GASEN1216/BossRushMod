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
        public const string DESCRIPTION_CN = "一罐速效修补喷剂。使用后进入维修选择：3米内靠近鼠标的受损己方工事会高亮，左键确认维修，右键取消，恢复25%最大生命值。";
        public const string DESCRIPTION_EN = "A quick-fix repair spray. Using it enters repair selection: the damaged fortification you placed within 3m closest to the cursor is highlighted, LMB confirms repair, RMB cancels, restoring 25% max health.";

        public static void ConfigureItem(Item item)
        {
            ModeFItemConfigHelper.ConfigureSimpleConsumable(
                item,
                "EmergencyRepairSprayConfig",
                TYPE_ID,
                LOC_KEY_DISPLAY,
                DISPLAY_NAME_EN,
                DESCRIPTION_CN,
                DESCRIPTION_EN,
                maxStackCount: 10,
                value: 1600,
                quality: 3);
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
        }
    }
}
