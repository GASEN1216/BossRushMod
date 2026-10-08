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
            // 2026-10-08：图标改为生图资源（production_icons 生产包，与五把武器同一套美术管线）。
            // 动态 prefab 跨 Mod 重载保留，由它拥有图标副本，避免生产包卸载后留下失效引用。
            // 包缺失时仍使用同一个 owner 的程序化弹匣图。
            UnityEngine.Sprite icon = EmptyMagazineMineIcon.GetSprite(item,
                ProductionIconCache.Get("Assets/Items/empty_magazine_mine_icon.png"));
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
