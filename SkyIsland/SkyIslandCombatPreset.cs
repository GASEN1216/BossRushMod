using System;

namespace BossRush
{
    /// <summary>
    /// COMPAT：只写本次生成的克隆 preset，交给官方 CreateCharacterAsync 装配 Stat 与 AI。
    /// 从未修改的 source 取普通敌人基准，重复准备同一个 clone 也不会复利。
    /// 不复制官方 Boss 的技能、装备、掉落或阵营，不改模板资源。
    /// </summary>
    internal static class SkyIslandCombatPreset
    {
        internal static void Apply(CharacterRandomPreset clone, CharacterRandomPreset source,
            string encounterId, int index, SkyIslandEnemyTier tier, float bossHealthMultiplier = 1f)
        {
            if (clone == null || source == null || ReferenceEquals(clone, source))
                throw new ArgumentException("天空岛属性只能应用到独立克隆");
            SkyIslandCombatBaseline baseline = SkyIslandCombatBalance.For(encounterId, index, tier);
            float factor = SkyIslandCombatBalance.Multiplier;
            // 机动与感知只乘 1.5（SkyIslandCombatBalance.PerceptionMultiplier 注释写了为什么不跟着统一倍率走）。
            float sense = SkyIslandCombatBalance.PerceptionMultiplier;
            clone.health = (baseline != null ? baseline.Health : source.health) * factor;
            // 专属 Boss 血量保底（SkyIslandCombatBalance.BossHealthFloor）。
            if (SkyIslandEnemyArmoryRules.IsBossTier(tier)) clone.health = Math.Max(clone.health, SkyIslandCombatBalance.BossHealthFloor);
            // 难度在既有 Boss 基准与保底之后乘一次；普通巡守和小兵继续使用原来的生命。
            if (SkyIslandEnemyArmoryRules.IsBossTier(tier)) clone.health *= bossHealthMultiplier;
            clone.damageMultiplier = (baseline != null ? baseline.Damage : source.damageMultiplier) * factor;
            // 当前官方工厂直接读取 meleeDamageMultiplier，不以 setMeleeDamageMultiplier 决定是否读取。
            // 普通敌人保留独立近战基准；Boss Wiki 只列通用伤害，将它用作岛内两种攻击的基准。
            clone.setMeleeDamageMultiplier = true;
            clone.meleeDamageMultiplier = (baseline != null ? baseline.Damage : source.meleeDamageMultiplier) * factor;
            clone.moveSpeedFactor = (baseline != null ? baseline.MoveSpeed : source.moveSpeedFactor) * sense;
            clone.bulletSpeedMultiplier = (baseline != null ? baseline.BulletSpeed : source.bulletSpeedMultiplier) * sense;
            clone.gunDistanceMultiplier = (baseline != null ? baseline.Range : source.gunDistanceMultiplier) * sense;
            clone.gunScatterMultiplier = (baseline != null ? baseline.Scatter : source.gunScatterMultiplier) / factor;
            clone.gunCritRateGain = SkyIslandCombatBalance.BoostCrit(baseline != null ? baseline.Crit : source.gunCritRateGain);
            clone.nightVisionAbility = (baseline != null ? baseline.NightVision : source.nightVisionAbility) * sense;
            // aiCombatFactor 只用于 NPC 互打的伤害比，不等于反应速度（官方 Health.Hurt）。
            clone.aiCombatFactor = (baseline != null ? baseline.AiCombat : source.aiCombatFactor) * factor;
            clone.sightDistance = (baseline != null ? baseline.Sight : source.sightDistance) * sense;
            clone.hearingAbility = (baseline != null ? baseline.Hearing : source.hearingAbility) * sense;
            clone.reactionTime = (baseline != null ? baseline.Reaction : source.reactionTime) / factor;
            clone.shootDelay = (baseline != null ? baseline.ShootDelay : source.shootDelay) / factor;
            // 保留参照角色昼夜反应的比例；否则夜间会再吃一次提升，偏离统一倍率。
            clone.nightReactionTimeFactor = baseline != null ? baseline.NightReaction : source.nightReactionTimeFactor;
        }
    }
}
