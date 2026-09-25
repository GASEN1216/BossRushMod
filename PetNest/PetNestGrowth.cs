using System;

namespace BossRush
{
    /// <summary>
    /// 幼体成长的纯数值口径。官方 Wiki 的 damageMultiplier 是角色对武器伤害的倍率，
    /// 不是 DPS 占比；运行时读取当前 preset，装备伤害、攻速与 AI 仍由官方处理。
    /// </summary>
    internal static class PetNestGrowth
    {
        internal static int GrowthLevels(int level)
        {
            return Math.Max(0, Math.Min(PetNestTuning.PetMaxLevel, level) - 1);
        }

        internal static float BaseDamageMultiplier(float sourceMultiplier)
        {
            if (sourceMultiplier <= 0f || float.IsNaN(sourceMultiplier) || float.IsInfinity(sourceMultiplier))
                sourceMultiplier = 1f;
            return Math.Max(PetNestTuning.CompanionDamageMinMultiplier,
                Math.Min(PetNestTuning.CompanionDpsShareTarget,
                    sourceMultiplier * PetNestTuning.CompanionBossDamageScale));
        }

        internal static float ModelScale(float lineageScale, int level)
        {
            if (lineageScale <= 0f || float.IsNaN(lineageScale) || float.IsInfinity(lineageScale))
                lineageScale = PetNestTuning.DefaultCubModelScale;
            return lineageScale * (1f + GrowthLevels(level) * PetNestTuning.PetModelScaleGrowthPerLevel);
        }

        internal static int KillExperience(float maxHealth)
        {
            if (maxHealth <= 0f || float.IsNaN(maxHealth) || float.IsInfinity(maxHealth))
                return PetNestTuning.PetExpPerCompanionKill;
            // 先钳浮点再转 int，异常巨血量不能溢出为负数。
            int bonus = (int)Math.Min(PetNestTuning.PetExpCompanionKillHealthBonusCap,
                maxHealth / PetNestTuning.PetExpCompanionKillHealthDivisor);
            return PetNestTuning.PetExpPerCompanionKill + bonus;
        }
    }
}
