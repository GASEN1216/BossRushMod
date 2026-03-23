using System;
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
        public const string DESCRIPTION_CN = "一个可快速展开的轻型掩体包。使用后进入部署预览：左键确认，右键取消，滚轮旋转，中键90度旋转。确认后部署折叠掩体，提供基础掩护。";
        public const string DESCRIPTION_EN = "A lightweight cover pack that deploys quickly. Using it enters placement preview: LMB confirm, RMB cancel, mouse wheel rotate, MMB rotate 90 degrees. Confirm to deploy a foldable cover for basic protection.";

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
                ModeFItemConfigHelper.SetHiddenMember(item, "description", L10n.T(DESCRIPTION_CN, DESCRIPTION_EN));
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", L10n.T(DESCRIPTION_CN, DESCRIPTION_EN));
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
    }
}
