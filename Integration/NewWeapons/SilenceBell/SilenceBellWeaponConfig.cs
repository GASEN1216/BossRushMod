using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>沿用图腾工厂与动态克隆注册（无专属模型）；护罩、头顶条与钟影由运行时负责。</summary>
    public static class SilenceBellWeaponConfig
    {
        private const string LocKey = "BossRush_SilenceBell";
        private static readonly NewWeaponTotemSpec Spec = new NewWeaponTotemSpec
        {
            TypeId = BossRushItemIds.SilenceBell,
            BaseName = SilenceBellConfig.BaseName,
            LogPrefix = SilenceBellConfig.LogPrefix,
            DisplayLabelCN = SilenceBellConfig.DisplayNameCN,
            DisplayNameCN = SilenceBellConfig.DisplayNameCN,
            DisplayNameEN = SilenceBellConfig.DisplayNameEN,
            DescriptionCN = SilenceBellConfig.DescriptionCN,
            DescriptionEN = SilenceBellConfig.DescriptionEN
        };

        public static void RegisterEquipmentConfigurator()
        {
            EquipmentFactory.RegisterConfigurator("SilenceBellWeaponConfig",
                (item, baseName) => { TryConfigure(item, baseName); });
        }

        public static bool TryConfigure(Item item, string baseName)
        {
            if (!NewWeaponConfiguratorCore.ConfigureTotem(item, baseName, Spec)) return false;
            try
            {
                item.DisplayNameRaw = LocKey;
                item.Quality = SilenceBellConfig.ItemQuality;
                item.Value = SilenceBellConfig.ItemValue;
                item.MaxStackCount = 1;
                if (item.StackCount <= 0) item.StackCount = 1;
                item.MaxDurability = 0f;
                ModeFItemConfigHelper.SetHiddenMember(item, "weight", SilenceBellConfig.ItemWeight);
                ModeFItemConfigHelper.ClearInheritedUsage(item);
                InjectLocalization();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(SilenceBellConfig.LogPrefix + " 配置失败: " + e.Message);
                return false;
            }
        }

        public static void InjectLocalization()
        {
            string name = L10n.T(SilenceBellConfig.DisplayNameCN, SilenceBellConfig.DisplayNameEN);
            string description = L10n.T(SilenceBellConfig.DescriptionCN, SilenceBellConfig.DescriptionEN);
            LocalizationHelper.InjectLocalization(LocKey, name);
            LocalizationHelper.InjectLocalization(LocKey + "_Desc", description);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.SilenceBell, name);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.SilenceBell + "_Desc", description);
        }
    }
}
