using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// Mod 自制 Boss 技能的伤害判定口径（龙王、龙裔、幽灵女巫/冠军之影、丧尸 Boss 共用）。
    /// 自制技能不走官方子弹/近战管线，官方的翻滚豁免与数值倍率都得在这里补齐。
    /// </summary>
    internal static class BossSkillDamageRules
    {
        private static readonly int GunDamageMultiplierHash = "GunDamageMultiplier".GetHashCode();
        private static readonly int MeleeDamageMultiplierHash = "MeleeDamageMultiplier".GetHashCode();

        private const float MinStatScale = 0.1f;
        private const float MaxStatScale = 5f;

        /// <summary>
        /// 瞬发命中（投射物、冲撞、一次性范围斩）遇到翻滚中的目标跳过伤害，
        /// 与官方 Projectile / ExplosionManager / ItemAgent_MeleeWeapon 的 Dashing 豁免一致。
        /// 持续地面区域（毒圈、岩浆、诅咒领域）按官方 ZoneDamage 口径不走这条豁免。
        /// </summary>
        internal static bool IsDodging(CharacterMainControl target)
        {
            return target != null && target.Dashing;
        }

        /// <summary>读 Boss 当前近战伤害倍率 Stat 的基础值；读不到返回 0（调用方视为「无基线」）。</summary>
        internal static float ReadMeleeDamageStatBase(CharacterMainControl boss)
        {
            return ReadStatBase(boss, MeleeDamageMultiplierHash);
        }

        /// <summary>
        /// 枪械伤害倍率 Stat 相对刷怪时基线的比值：全局 Boss 倍率、战役倍率都乘在这个 Stat 上，
        /// 自制技能乘这个比值才能跟着放大。基线无效时返回 1。
        /// </summary>
        internal static float ResolveGunDamageScale(CharacterMainControl boss, float baseline)
        {
            return ResolveScale(boss, GunDamageMultiplierHash, baseline);
        }

        /// <summary>近战伤害倍率 Stat 相对刷怪时基线的比值，口径同 <see cref="ResolveGunDamageScale"/>。</summary>
        internal static float ResolveMeleeDamageScale(CharacterMainControl boss, float baseline)
        {
            return ResolveScale(boss, MeleeDamageMultiplierHash, baseline);
        }

        private static float ResolveScale(CharacterMainControl boss, int statHash, float baseline)
        {
            if (baseline <= 0.0001f)
            {
                return 1f;
            }

            float current = ReadStatBase(boss, statHash);
            if (current <= 0f)
            {
                return 1f;
            }

            float scale = current / baseline;
            if (scale < MinStatScale) return MinStatScale;
            if (scale > MaxStatScale) return MaxStatScale;
            return scale;
        }

        private static float ReadStatBase(CharacterMainControl boss, int statHash)
        {
            if (boss == null)
            {
                return 0f;
            }

            try
            {
                Item item = boss.CharacterItem;
                if (item == null)
                {
                    return 0f;
                }

                Stat stat = item.GetStat(statHash);
                return stat != null ? stat.BaseValue : 0f;
            }
            catch (Exception)
            {
                // 角色销毁途中取 Stat 可能抛异常，按无倍率处理（根 §4.7）
                return 0f;
            }
        }
    }
}
