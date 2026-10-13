using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>沿用图腾工厂与动态克隆注册，专属炸弹由运行时负责。</summary>
    public static class PassTheBombWeaponConfig
    {
        private const string LocKey = "BossRush_PassTheBomb";
        private static readonly NewWeaponTotemSpec Spec = new NewWeaponTotemSpec
        {
            TypeId = BossRushItemIds.PassTheBomb,
            BaseName = PassTheBombConfig.BaseName,
            LogPrefix = PassTheBombConfig.LogPrefix,
            DisplayLabelCN = PassTheBombConfig.DisplayNameCN,
            DisplayNameCN = PassTheBombConfig.DisplayNameCN,
            DisplayNameEN = PassTheBombConfig.DisplayNameEN,
            DescriptionCN = PassTheBombConfig.DescriptionCN,
            DescriptionEN = PassTheBombConfig.DescriptionEN
        };

        public static void RegisterEquipmentConfigurator()
        {
            EquipmentFactory.RegisterConfigurator("PassTheBombWeaponConfig",
                (item, baseName) => { TryConfigure(item, baseName); });
        }

        public static bool TryConfigure(Item item, string baseName)
        {
            if (!NewWeaponConfiguratorCore.ConfigureTotem(item, baseName, Spec)) return false;
            try
            {
                item.DisplayNameRaw = LocKey;
                item.Quality = PassTheBombConfig.ItemQuality;
                item.Value = PassTheBombConfig.ItemValue;
                item.MaxStackCount = 1;
                if (item.StackCount <= 0) item.StackCount = 1;
                item.MaxDurability = 0f;
                ModeFItemConfigHelper.SetHiddenMember(item, "weight", PassTheBombConfig.ItemWeight);
                ModeFItemConfigHelper.ClearInheritedUsage(item);
                UnityEngine.Sprite icon = PassTheBombIcon.GetSprite(item,
                    ProductionIconCache.Get(PassTheBombConfig.IconPath));
                if (icon != null) item.Icon = icon;
                InjectLocalization();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(PassTheBombConfig.LogPrefix + " 配置失败: " + e.Message);
                return false;
            }
        }

        public static void InjectLocalization()
        {
            string name = L10n.T(PassTheBombConfig.DisplayNameCN, PassTheBombConfig.DisplayNameEN);
            string description = L10n.T(PassTheBombConfig.DescriptionCN, PassTheBombConfig.DescriptionEN);
            LocalizationHelper.InjectLocalization(LocKey, name);
            LocalizationHelper.InjectLocalization(LocKey + "_Desc", description);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.PassTheBomb, name);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.PassTheBomb + "_Desc", description);
        }
    }
}
