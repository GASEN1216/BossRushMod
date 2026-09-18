// ============================================================================
// ViperDaggerWeaponConfig.cs - 毒蛇匕首装备工厂配置器
// ============================================================================
// 模块说明：
//   声明毒蛇匕首与共享配置流程的差异项（面板、元素、官方毒 buff、文案），
//   流程本身在 NewWeaponConfiguratorCore。三把新近战共用同一条流程，
//   这里不再复制 Stats / MeleeAgent / 标签 / 本地化那四段模板。
// ============================================================================

using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 毒蛇匕首装备工厂配置器
    /// </summary>
    public static class ViperDaggerWeaponConfig
    {
        private static readonly NewWeaponMeleeSpec Spec = new NewWeaponMeleeSpec
        {
            TypeId = NewWeaponIds.ViperDaggerTypeId,
            BaseName = NewWeaponIds.ViperDaggerBaseName,
            ModelBaseName = NewWeaponIds.ViperDaggerModelBaseName,
            LogPrefix = ViperDaggerConfig.LogPrefix,
            DisplayLabelCN = "毒蛇匕首",

            Stats = new Dictionary<string, float>
            {
                { "Damage", ViperDaggerConfig.Damage },
                { "MoveSpeedMultiplier", ViperDaggerConfig.MoveSpeedMultiplier },
                { "BlockBullet", ViperDaggerConfig.BlockBullet },
                { "CritRate", ViperDaggerConfig.CritRate },
                { "CritDamageFactor", ViperDaggerConfig.CritDamageFactor },
                { "ArmorPiercing", ViperDaggerConfig.ArmorPiercing },
                { "AttackSpeed", ViperDaggerConfig.AttackSpeed },
                { "AttackRange", ViperDaggerConfig.AttackRange },
                { "DealDamageTime", ViperDaggerConfig.DealDamageTime },
                { "StaminaCost", ViperDaggerConfig.StaminaCost },
                { "BleedChance", ViperDaggerConfig.BleedChance }
            },

            // 毒属性 + 官方 Poison buff 100% 触发（叠层与爆发由 ViperDaggerRuntime 另算）
            Element = ElementTypes.poison,
            BuffKind = NewWeaponMeleeBuffKind.Poison,
            BuffChance = 1f,

            DisplayNameCN = ViperDaggerConfig.DisplayNameCN,
            DisplayNameEN = ViperDaggerConfig.DisplayNameEN,
            DescriptionCN = ViperDaggerConfig.DescriptionCN,
            DescriptionEN = ViperDaggerConfig.DescriptionEN
        };

        /// <summary>
        /// 尝试配置毒蛇匕首（由 EquipmentFactory / ItemFactory 配置器 / 占位符路径调用）
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            return NewWeaponConfiguratorCore.ConfigureMelee(item, baseName, Spec);
        }

        public static void ResetStaticCaches()
        {
            // 当前无需清理的静态缓存（Spec 是只读数据）
        }
    }
}
