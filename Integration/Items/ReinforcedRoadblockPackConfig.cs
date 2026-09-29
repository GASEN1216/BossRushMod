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
        public const string DESCRIPTION_CN = "拆成几块的钢板路障，扛起来死沉，立起来能顶住一阵火力。\n使用后进入部署预览：左键确认，右键取消，滚轮旋转，中键旋转 90 度。确认后部署加固路障（500 生命），提供强力掩护。";
        public const string DESCRIPTION_EN = "A roadblock broken down into steel plates. Heavy to carry, but it holds up under fire once it's standing.\nUsing it opens placement preview: left-click to confirm, right-click to cancel, mouse wheel to rotate, middle-click to rotate 90 degrees. Confirming deploys a reinforced roadblock (500 HP) for heavy protection.";

        public static void ConfigureItem(Item item)
        {
            ModeFItemConfigHelper.ConfigureSimpleConsumable(
                item,
                "ReinforcedRoadblockPackConfig",
                TYPE_ID,
                LOC_KEY_DISPLAY,
                DISPLAY_NAME_EN,
                DESCRIPTION_CN,
                DESCRIPTION_EN,
                maxStackCount: 5,
                value: 2800,
                quality: 4);
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
        }
    }
}
