// ============================================================================
// FrostSpearWeaponConfig.cs - 冰霜长矛装备工厂配置器
// ============================================================================
// 模块说明：
//   声明冰霜长矛与共享配置流程的差异项（面板、冰元素、官方 Cold buff、
//   ColdProtection +1、文案），流程本身在 NewWeaponConfiguratorCore。
// ============================================================================

using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 冰霜长矛装备工厂配置器
    /// </summary>
    public static class FrostSpearWeaponConfig
    {
        private static readonly NewWeaponMeleeSpec Spec = new NewWeaponMeleeSpec
        {
            TypeId = NewWeaponIds.FrostSpearTypeId,
            BaseName = NewWeaponIds.FrostSpearBaseName,
            ModelBaseName = NewWeaponIds.FrostSpearModelBaseName,
            LogPrefix = FrostSpearConfig.LogPrefix,
            DisplayLabelCN = "冰霜长矛",

            Stats = new Dictionary<string, float>
            {
                { "Damage", FrostSpearConfig.Damage },
                { "MoveSpeedMultiplier", FrostSpearConfig.MoveSpeedMultiplier },
                { "BlockBullet", FrostSpearConfig.BlockBullet },
                { "CritRate", FrostSpearConfig.CritRate },
                { "CritDamageFactor", FrostSpearConfig.CritDamageFactor },
                { "ArmorPiercing", FrostSpearConfig.ArmorPiercing },
                { "AttackSpeed", FrostSpearConfig.AttackSpeed },
                { "AttackRange", FrostSpearConfig.AttackRange },
                { "DealDamageTime", FrostSpearConfig.DealDamageTime },
                { "StaminaCost", FrostSpearConfig.StaminaCost },
                { "BleedChance", FrostSpearConfig.BleedChance }
            },

            // 冰属性 + 官方 Cold buff（减速由它提供，运行时只补霜环表现）
            Element = ElementTypes.ice,
            BuffKind = NewWeaponMeleeBuffKind.Cold,
            BuffChance = FrostSpearConfig.FreezeChance,

            ModifierKey = "ColdProtection",
            ModifierValue = FrostSpearConfig.ColdProtectionBonus,

            DisplayNameCN = FrostSpearConfig.DisplayNameCN,
            DisplayNameEN = FrostSpearConfig.DisplayNameEN,
            DescriptionCN = FrostSpearConfig.DescriptionCN,
            DescriptionEN = FrostSpearConfig.DescriptionEN
        };

        /// <summary>
        /// 尝试配置冰霜长矛
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
