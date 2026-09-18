// ============================================================================
// PetNestPersonality.cs - 性格 -> 实际效果的唯一一张表
// ============================================================================
// 为什么需要它：孵化 roll 出来的性格（莽撞 / 谨慎 / 懒散 / 忠诚）此前在全仓**零消费点**，
// 只是巢页卡片上的一个词。PetNestTuning 的注释早就写明了四种性格该有的行为
// （"贴身缠斗 / 拉开距离 / 攻击欲低但背包大 / 贴着主人不乱跑"），但没有任何代码读它。
//
// 落地纪律：
//   - **不新增系统**。三个消费点全是既有入口：
//     官方 AI 的两个 public 字段（AICharacterController.sightDistance / traceTargetChance，
//     无反射）、PetNestCompanionSpawner 已有的 ApplyPetModifiers 管线、
//     PetNestCompanionAgent 已有的跟随传送阈值；
//   - 属性行复用 PetNestTalentEntry 的形状（statKey / value / percentage），
//     这样 ApplyOneModifier 不需要第二个重载，展示也能复用天赋那一套文案入口；
//   - 百分比一律**小数口径**（0.10 = +10%，同 PetNestTuning.ScarModifierFraction）；
//   - 未知 / 老档缺失的性格回落到中性档，调用方永远不必判空。
//
// 数值取舍（owner 授权「好玩优先」拍板，理由与回退办法见 FIX_TRACKER）：
//   四条性格各有一处明显长板与一处明显短板，量级压在 ±10% 以内——
//   它要能被认出来，但不能变成"必须洗到某个性格"的强度墙。
//   回退办法：把某条的 Modifiers 置空数组，AI 旋钮改回 1f/1f 即恢复纯标签行为。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>一种性格的全部效果。只读快照，构建后不再变更。</summary>
    internal sealed class PetNestPersonalityProfile
    {
        /// <summary>性格 id（PetNestTuning.Personality* 常量）。</summary>
        internal string Id;

        /// <summary>索敌距离倍率，作用在官方 AICharacterController.sightDistance 上。</summary>
        internal float SightDistanceMultiplier;

        /// <summary>追击意愿，直接写官方 AICharacterController.traceTargetChance。</summary>
        internal float TraceTargetChance;

        /// <summary>跟随传送阈值（米）。小 = 贴着主人，大 = 放得开。</summary>
        internal float FollowTeleportDistance;

        /// <summary>额外捡漏背包格子（叠加在 PetNestTuning.CompanionPetCapacityBonus 上）。</summary>
        internal int ExtraPetCapacity;

        /// <summary>入场时挂到角色 Item 上的属性行。空数组表示不挂。</summary>
        internal PetNestTalentEntry[] Modifiers;
    }

    /// <summary>性格效果表。无状态，只读。</summary>
    internal static class PetNestPersonality
    {
        #region 表

        /// <summary>
        /// 中性档：未知 id / 老档缺失性格时使用。
        /// 倍率取 1、传送阈值取 PetNestCompanionAgent.TeleportDistance，
        /// 语义就是"和加性格之前一模一样"。
        /// </summary>
        private static readonly PetNestPersonalityProfile Neutral = Make(
            null, 1f, 1f, PetNestCompanionAgent.TeleportDistance, 0);

        private static readonly PetNestPersonalityProfile[] All =
        {
            // 莽撞：看得远、扑得凶、近战更疼，代价是皮薄
            Make(PetNestTuning.PersonalityReckless, 1.5f, 1f, PetNestCompanionAgent.TeleportDistance, 0,
                Modifier("MeleeDamageMultiplier", 0.10f),
                Modifier("BodyArmor", -0.08f)),

            // 谨慎：不乱扑、更耐揍，代价是枪械输出更低
            Make(PetNestTuning.PersonalityCautious, 0.8f, 0.8f, PetNestCompanionAgent.TeleportDistance, 0,
                Modifier("BodyArmor", 0.10f),
                Modifier("GunDamageMultiplier", -0.05f)),

            // 懒散：攻击欲最低、跑得慢，但多背一格（"背包大"是它的招牌）
            Make(PetNestTuning.PersonalityLazy, 0.7f, 0.5f, 48f, 1,
                Modifier("RunSpeed", -0.06f)),

            // 忠诚：贴着主人不乱跑，命也更硬
            Make(PetNestTuning.PersonalityLoyal, 1f, 1f, 24f, 0,
                Modifier("MaxHealth", 0.08f)),
        };

        private static PetNestPersonalityProfile Make(
            string id, float sightMultiplier, float traceChance, float followTeleportDistance,
            int extraCapacity, params PetNestTalentEntry[] modifiers)
        {
            PetNestPersonalityProfile profile = new PetNestPersonalityProfile();
            profile.Id = id;
            profile.SightDistanceMultiplier = sightMultiplier;
            profile.TraceTargetChance = traceChance;
            profile.FollowTeleportDistance = followTeleportDistance;
            profile.ExtraPetCapacity = extraCapacity;
            profile.Modifiers = modifiers ?? new PetNestTalentEntry[0];
            return profile;
        }

        /// <summary>性格属性行。一律百分比（小数口径）。</summary>
        private static PetNestTalentEntry Modifier(string statKey, float value)
        {
            PetNestTalentEntry entry = new PetNestTalentEntry();
            entry.id = null;
            entry.statKey = statKey;
            entry.value = value;
            entry.percentage = true;
            return entry;
        }

        #endregion

        #region 查询

        /// <summary>
        /// 按性格 id 取效果。未知 id、空 id、老档缺失一律回落中性档，**永不返回 null**。
        /// </summary>
        internal static PetNestPersonalityProfile Resolve(string personalityId)
        {
            if (string.IsNullOrEmpty(personalityId)) return Neutral;
            for (int i = 0; i < All.Length; i++)
            {
                PetNestPersonalityProfile profile = All[i];
                if (profile != null
                    && string.Equals(profile.Id, personalityId, StringComparison.Ordinal))
                {
                    return profile;
                }
            }
            return Neutral;
        }

        /// <summary>按崽取效果。pet 为 null 时回落中性档。</summary>
        internal static PetNestPersonalityProfile Resolve(PetNestPetRecord pet)
        {
            return Resolve(pet != null ? pet.personalityId : null);
        }

        #endregion
    }
}
