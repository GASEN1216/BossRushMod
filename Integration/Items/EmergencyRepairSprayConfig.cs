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
        public const string DESCRIPTION_CN = "使用后，3米内靠近鼠标的受损己方工事会高亮。\n左键维修，恢复25%最大生命；右键取消。";
        public const string DESCRIPTION_EN = "Use to highlight a damaged allied fortification near your cursor within 3m.\nLeft-click to restore 25% of its max HP. Right-click to cancel.";

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
