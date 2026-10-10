using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>复用图腾工厂；实例与按需克隆均在注册时完成配置。</summary>
    public static class RollCallLedgerWeaponConfig
    {
        private const string LocKey = "BossRush_RollCallLedger";
        private static readonly NewWeaponTotemSpec Spec = new NewWeaponTotemSpec
        {
            TypeId = BossRushItemIds.RollCallLedger,
            BaseName = RollCallLedgerConfig.BaseName,
            LogPrefix = RollCallLedgerConfig.LogPrefix,
            DisplayLabelCN = RollCallLedgerConfig.DisplayNameCN,
            DisplayNameCN = RollCallLedgerConfig.DisplayNameCN,
            DisplayNameEN = RollCallLedgerConfig.DisplayNameEN,
            DescriptionCN = RollCallLedgerConfig.DescriptionCN,
            DescriptionEN = RollCallLedgerConfig.DescriptionEN
        };

        public static void RegisterEquipmentConfigurator()
        {
            EquipmentFactory.RegisterConfigurator("RollCallLedgerWeaponConfig",
                (item, baseName) => { TryConfigure(item, baseName); });
        }

        public static bool TryConfigure(Item item, string baseName)
        {
            if (!NewWeaponConfiguratorCore.ConfigureTotem(item, baseName, Spec)) return false;
            try
            {
                item.DisplayNameRaw = LocKey;
                item.Quality = RollCallLedgerConfig.ItemQuality;
                item.Value = RollCallLedgerConfig.ItemValue;
                item.MaxStackCount = 1;
                if (item.StackCount <= 0) item.StackCount = 1;
                item.MaxDurability = 0f;
                ModeFItemConfigHelper.SetHiddenMember(item, "weight", RollCallLedgerConfig.ItemWeight);
                ModeFItemConfigHelper.ClearInheritedUsage(item);
                UnityEngine.Sprite icon = RollCallLedgerIcon.GetSprite(item,
                    ProductionIconCache.Get(RollCallLedgerConfig.IconPath));
                if (icon != null) item.Icon = icon;
                InjectLocalization();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RollCallLedgerConfig.LogPrefix + " 配置失败: " + e.Message);
                return false;
            }
        }

        public static void InjectLocalization()
        {
            string name = L10n.T(RollCallLedgerConfig.DisplayNameCN, RollCallLedgerConfig.DisplayNameEN);
            string description = L10n.T(RollCallLedgerConfig.DescriptionCN, RollCallLedgerConfig.DescriptionEN);
            LocalizationHelper.InjectLocalization(LocKey, name);
            LocalizationHelper.InjectLocalization(LocKey + "_Desc", description);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.RollCallLedger, name);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.RollCallLedger + "_Desc", description);
        }
    }
}
