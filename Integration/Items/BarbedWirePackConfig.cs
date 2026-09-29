using ItemStatsSystem;

namespace BossRush
{
    public static class BarbedWirePackConfig
    {
        public const int TYPE_ID = 500039;
        public const string BUNDLE_NAME = "barbed_wire_pack";
        public const string PREFAB_NAME = "BossRush_ModeF_BarbedWirePack";
        public const string LOC_KEY_DISPLAY = "BossRush_BarbedWirePack";
        public const string DISPLAY_NAME_CN = "阻滞铁丝网包";
        public const string DISPLAY_NAME_EN = "Barbed Wire Pack";
        public const string DESCRIPTION_CN = "一卷缠得比手臂还粗的带刺铁丝网。\n使用后进入部署预览：左键确认，右键取消，滚轮旋转，中键旋转 90 度。确认后部署阻滞铁丝网（200 生命），阻挡并拖慢敌人推进。";
        public const string DESCRIPTION_EN = "A coil of barbed wire thicker than your arm.\nUsing it opens placement preview: left-click to confirm, right-click to cancel, mouse wheel to rotate, middle-click to rotate 90 degrees. Confirming deploys barbed wire (200 HP) that blocks and slows enemy advances.";

        public static void ConfigureItem(Item item)
        {
            ModeFItemConfigHelper.ConfigureSimpleConsumable(
                item,
                "BarbedWirePackConfig",
                TYPE_ID,
                LOC_KEY_DISPLAY,
                DISPLAY_NAME_EN,
                DESCRIPTION_CN,
                DESCRIPTION_EN,
                maxStackCount: 5,
                value: 2200,
                quality: 3);
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
        }
    }
}
