// ============================================================================
// NewWeaponIds.cs - P0新武器扩展 ID 常量定义
// ============================================================================
// 模块说明：
//   集中管理五把新武器的 TypeID、AssetBundle 路径和 Prefab 名称
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// P0 新武器扩展 - 统一 ID 常量
    /// </summary>
    public static class NewWeaponIds
    {
        // ========== 毒蛇匕首 ==========
        public const int ViperDaggerTypeId = 500048;
        public const string ViperDaggerBundleName = "viper_dagger";
        public const string ViperDaggerBaseName = "ViperDagger";
        public const string ViperDaggerModelBaseName = "ViperDagger_Melee";
        public const string ViperDaggerIconAssetName = "viper_dagger_icon";

        // ========== 召唤法杖 ==========
        public const int SummonStaffTypeId = 500049;
        public const string SummonStaffBundleName = "summon_staff";
        public const string SummonStaffBaseName = "SummonStaff";
        public const string SummonStaffModelBaseName = "SummonStaff_Melee";
        public const string SummonStaffIconAssetName = "summon_staff_icon";

        // ========== 能量盾 ==========
        public const int EnergyShieldTypeId = 500050;
        public const string EnergyShieldBundleName = "energy_shield";
        public const string EnergyShieldBaseName = "EnergyShield";
        public const string EnergyShieldModelBaseName = "EnergyShield_Totem";
        public const string EnergyShieldIconAssetName = "energy_shield_icon";

        // ========== 冰霜长矛 ==========
        public const int FrostSpearTypeId = 500051;
        public const string FrostSpearBundleName = "frost_spear";
        public const string FrostSpearBaseName = "FrostSpear";
        public const string FrostSpearModelBaseName = "FrostSpear_Melee";
        public const string FrostSpearIconAssetName = "frost_spear_icon";

        // ========== 雷电戒指 ==========
        public const int ThunderRingTypeId = 500052;
        public const string ThunderRingBundleName = "thunder_ring";
        public const string ThunderRingBaseName = "ThunderRing";
        public const string ThunderRingModelBaseName = "ThunderRing_Totem";
        public const string ThunderRingIconAssetName = "thunder_ring_icon";
    }
}
