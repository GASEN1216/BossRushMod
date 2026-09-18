// ============================================================================
// EnergyShieldWeaponConfig.cs - 能量盾装备工厂配置器
// ============================================================================
// 模块说明：
//   声明能量盾与共享图腾配置流程的差异项（BodyArmor +3、模型、文案），
//   流程本身在 NewWeaponConfiguratorCore。正面吸收逻辑在 EnergyShieldRuntime。
// ============================================================================

using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 能量盾装备工厂配置器
    /// </summary>
    public static class EnergyShieldWeaponConfig
    {
        private static readonly NewWeaponTotemSpec Spec = new NewWeaponTotemSpec
        {
            TypeId = NewWeaponIds.EnergyShieldTypeId,
            BaseName = NewWeaponIds.EnergyShieldBaseName,
            ModelBaseName = NewWeaponIds.EnergyShieldModelBaseName,
            LogPrefix = EnergyShieldConfig.LogPrefix,
            DisplayLabelCN = "能量盾",

            ModifierKey = "BodyArmor",
            ModifierValue = EnergyShieldConfig.BodyArmorBonus,

            DisplayNameCN = EnergyShieldConfig.DisplayNameCN,
            DisplayNameEN = EnergyShieldConfig.DisplayNameEN,
            DescriptionCN = EnergyShieldConfig.DescriptionCN,
            DescriptionEN = EnergyShieldConfig.DescriptionEN
        };

        /// <summary>
        /// 尝试配置能量盾
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            return NewWeaponConfiguratorCore.ConfigureTotem(item, baseName, Spec);
        }

        public static void ResetStaticCaches()
        {
            // 当前无需清理的静态缓存（Spec 是只读数据）
        }
    }
}
