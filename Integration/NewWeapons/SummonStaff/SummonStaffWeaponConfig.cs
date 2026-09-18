// ============================================================================
// SummonStaffWeaponConfig.cs - 召唤法杖装备工厂配置器
// ============================================================================
// 模块说明：
//   声明召唤法杖与共享配置流程的差异项（偏弱的近战面板、无元素、无官方 buff、文案），
//   流程本身在 NewWeaponConfiguratorCore。右键技能「灵魂投射」在 SummonStaffAction。
//
//   文案取自 SummonStaffConfig 的实例属性（它继承 EquipmentAbilityConfig，
//   是实例成员而非常量），因此这里持有一份只读实例，与 NewWeaponPlaceholderRegistry 同源。
// ============================================================================

using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 召唤法杖装备工厂配置器
    /// </summary>
    public static class SummonStaffWeaponConfig
    {
        /// <summary>文案单一来源：与右键技能共用同一个 Config 实例的属性。</summary>
        private static readonly SummonStaffConfig TextSource = new SummonStaffConfig();

        private static readonly NewWeaponMeleeSpec Spec = new NewWeaponMeleeSpec
        {
            TypeId = NewWeaponIds.SummonStaffTypeId,
            BaseName = NewWeaponIds.SummonStaffBaseName,
            ModelBaseName = NewWeaponIds.SummonStaffModelBaseName,
            LogPrefix = "[SummonStaff]",
            DisplayLabelCN = "召唤法杖",

            Stats = new Dictionary<string, float>
            {
                { "Damage", SummonStaffConfig.Damage },
                { "MoveSpeedMultiplier", SummonStaffConfig.MoveSpeedMultiplier },
                { "BlockBullet", SummonStaffConfig.BlockBullet },
                { "CritRate", SummonStaffConfig.CritRate },
                { "CritDamageFactor", SummonStaffConfig.CritDamageFactor },
                { "ArmorPiercing", SummonStaffConfig.ArmorPiercing },
                { "AttackSpeed", SummonStaffConfig.AttackSpeed },
                { "AttackRange", SummonStaffConfig.AttackRange },
                { "DealDamageTime", SummonStaffConfig.DealDamageTime },
                { "StaminaCost", SummonStaffConfig.StaminaCost },
                { "BleedChance", SummonStaffConfig.BleedChance }
            },

            // 法杖自身不带元素与官方 buff：它的价值全在右键投放出去的灵魂战士上
            Element = ElementTypes.physics,
            BuffKind = NewWeaponMeleeBuffKind.None,

            DisplayNameCN = TextSource.DisplayNameCN,
            DisplayNameEN = TextSource.DisplayNameEN,
            DescriptionCN = TextSource.DescriptionCN,
            DescriptionEN = TextSource.DescriptionEN
        };

        /// <summary>
        /// 尝试配置召唤法杖
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
