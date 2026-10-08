using ItemStatsSystem;

namespace BossRush
{
    /// <summary>复用图腾工厂配置与克隆注册；专属效果由 EmptyMagazineMineRuntime 拥有。</summary>
    public static class EmptyMagazineMineWeaponConfig
    {
        private const string LocKey = "BossRush_EmptyMagazineMine";
        private static readonly NewWeaponTotemSpec Spec = new NewWeaponTotemSpec
        {
            TypeId = BossRushItemIds.EmptyMagazineMine,
            BaseName = EmptyMagazineMineConfig.BaseName,
            LogPrefix = EmptyMagazineMineConfig.LogPrefix,
            DisplayLabelCN = EmptyMagazineMineConfig.DisplayNameCN,
            DisplayNameCN = EmptyMagazineMineConfig.DisplayNameCN,
            DisplayNameEN = EmptyMagazineMineConfig.DisplayNameEN,
            DescriptionCN = EmptyMagazineMineConfig.DescriptionCN,
            DescriptionEN = EmptyMagazineMineConfig.DescriptionEN
        };

        public static void RegisterEquipmentConfigurator()
        {
            EquipmentFactory.RegisterConfigurator("EmptyMagazineMineWeaponConfig",
                (item, baseName) => { TryConfigure(item, baseName); });
        }

        public static bool TryConfigure(Item item, string baseName)
        {
            if (!NewWeaponConfiguratorCore.ConfigureTotem(item, baseName, Spec)) return false;
            item.DisplayNameRaw = LocKey;
            // 官方重量只有只读 UnitSelfWeight，复用现有隐藏字段写入工具。
            ModeFItemConfigHelper.SetHiddenMember(item, "weight", EmptyMagazineMineConfig.ItemWeight);
            ModeFItemConfigHelper.ClearInheritedUsage(item);
            UnityEngine.Sprite icon = EmptyMagazineMineIcon.GetSprite(item);
            if (icon != null) item.Icon = icon;
            InjectLocalization();
            return true;
        }

        public static void InjectLocalization()
        {
            string name = L10n.T(EmptyMagazineMineConfig.DisplayNameCN, EmptyMagazineMineConfig.DisplayNameEN);
            string description = L10n.T(EmptyMagazineMineConfig.DescriptionCN, EmptyMagazineMineConfig.DescriptionEN);
            LocalizationHelper.InjectLocalization(LocKey, name);
            LocalizationHelper.InjectLocalization(LocKey + "_Desc", description);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.EmptyMagazineMine, name);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.EmptyMagazineMine + "_Desc", description);
        }
    }
}
