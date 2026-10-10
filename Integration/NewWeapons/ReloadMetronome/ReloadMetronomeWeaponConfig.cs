using ItemStatsSystem;

namespace BossRush
{
    internal static class ReloadMetronomeWeaponConfig
    {
        private const string LocKey = "BossRush_ReloadMetronome";
        private static readonly NewWeaponTotemSpec Spec = new NewWeaponTotemSpec
        {
            TypeId = BossRushItemIds.ReloadMetronome,
            BaseName = ReloadMetronomeConfig.BaseName,
            LogPrefix = "[ReloadMetronome]",
            DisplayLabelCN = ReloadMetronomeConfig.NameCN,
            DisplayNameCN = ReloadMetronomeConfig.NameCN,
            DisplayNameEN = ReloadMetronomeConfig.NameEN,
            DescriptionCN = ReloadMetronomeConfig.DescriptionCN,
            DescriptionEN = ReloadMetronomeConfig.DescriptionEN
        };

        public static void RegisterEquipmentConfigurator()
        {
            EquipmentFactory.RegisterConfigurator("ReloadMetronomeWeaponConfig",
                (item, baseName) => { TryConfigure(item, baseName); });
        }

        internal static bool TryConfigure(Item item, string baseName)
        {
            if (!NewWeaponConfiguratorCore.ConfigureTotem(item, baseName, Spec)) return false;
            item.DisplayNameRaw = LocKey;
            ModeFItemConfigHelper.SetHiddenMember(item, "weight", 0.3f);
            ModeFItemConfigHelper.ClearInheritedUsage(item);
            UnityEngine.Sprite icon = ReloadMetronomeIcon.GetSprite(item);
            if (icon != null) item.Icon = icon;
            InjectLocalization();
            return true;
        }

        internal static void InjectLocalization()
        {
            string name = L10n.T(ReloadMetronomeConfig.NameCN, ReloadMetronomeConfig.NameEN);
            string description = L10n.T(ReloadMetronomeConfig.DescriptionCN, ReloadMetronomeConfig.DescriptionEN);
            LocalizationHelper.InjectLocalization("BossRush_ReloadMetronomeShop",
                L10n.T("钟守的旧物匣", "Bell Keeper's Keepsakes"));
            LocalizationHelper.InjectLocalization(LocKey, name);
            LocalizationHelper.InjectLocalization(LocKey + "_Desc", description);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.ReloadMetronome, name);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.ReloadMetronome + "_Desc", description);
        }
    }
}
