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
        public const string DESCRIPTION_CN = "压得扁扁的一叠折叠板，抖开就是一面齐腰高的矮墙。\n使用后进入部署预览：左键确认，右键取消，滚轮旋转，中键旋转 90 度。确认后部署折叠掩体（250 生命），提供基础掩护。";
        public const string DESCRIPTION_EN = "A flat stack of folding panels that shakes out into a waist-high wall.\nUsing it opens placement preview: left-click to confirm, right-click to cancel, mouse wheel to rotate, middle-click to rotate 90 degrees. Confirming deploys foldable cover (250 HP) for basic protection.";

        public static void ConfigureItem(Item item)
        {
            ModeFItemConfigHelper.ConfigureSimpleConsumable(
                item,
                "FoldableCoverPackConfig",
                TYPE_ID,
                LOC_KEY_DISPLAY,
                DISPLAY_NAME_EN,
                DESCRIPTION_CN,
                DESCRIPTION_EN,
                maxStackCount: 10,
                value: 1200,
                quality: 2);
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
        }
    }
}
