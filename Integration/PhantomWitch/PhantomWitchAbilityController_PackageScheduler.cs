// ============================================================================
// PhantomWitchAbilityController partial - extracted from PhantomWitchAbilityController.cs
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    public partial class PhantomWitchAbilityController : MonoBehaviour
    {
        private IEnumerator AttackLoop()
        {
            yield return wait1s;
            attackLoopLastTickTime = Time.time;

            ModBehaviour.DevLog("[PhantomWitch] 攻击循环开始");

            while (CurrentPhase != PhantomWitchPhase.Dead && bossCharacter != null)
            {
                if (CurrentPhase == PhantomWitchPhase.Transitioning)
                {
                    attackLoopLastTickTime = Time.time;
                    yield return wait05s;
                    continue;
                }

                CharacterMainControl target;
                bool hasTarget = TryResolveCombatTarget(out target);
                if (!hasTarget)
                {
                    var inst = ModBehaviour.Instance;
                    if (inst != null && (inst.IsModeEActive || inst.IsModeFPreparationPhase))
                    {
                        attackLoopLastTickTime = Time.time;
                        yield return wait05s;
                        continue;
                    }

                    ModBehaviour.DevLog("[PhantomWitch] [AttackLoop] 普通 BossRush 下目标丢失，触发 OnPlayerDeath");
                    OnPlayerDeath();
                    yield break;
                }

                CheckPhaseTransition();
                if (CurrentPhase == PhantomWitchPhase.Transitioning || CurrentPhase == PhantomWitchPhase.Dead)
                {
                    yield break;
                }

                PhantomWitchAttackPackageType[] sequence = CurrentPackageSequence;
                if (sequence == null || sequence.Length == 0)
                {
                    attackLoopLastTickTime = Time.time;
                    yield return wait05s;
                    continue;
                }

                PhantomWitchAttackPackageType packageType = sequence[currentPackageIndex % sequence.Length];
                currentTelemetryPackageType = packageType;
                currentPackageStartedAt = Time.time;
                currentPackageHadAttackLanded = false;
                IncrementPhasePackageCount(CurrentPhase);
                EmitTelemetry("package_start", "packageType=" + packageType + ",phase=" + CurrentPhase);
                LogSkillState(packageType.ToString(), "start", target);
                attackLoopLastTickTime = Time.time;
                currentAttackCoroutine = StartCoroutine(ExecuteAttackPackage(packageType));
                yield return currentAttackCoroutine;
                currentAttackCoroutine = null;
                LogSkillState(packageType.ToString(), "end", target);
                float packageDurationMs = Mathf.Max(0f, (Time.time - currentPackageStartedAt) * 1000f);
                int phaseIndex = GetPhaseTelemetryIndex(CurrentPhase);
                if (phaseIndex >= 0)
                {
                    phasePackageDurationSeconds[phaseIndex] += Mathf.Max(0f, Time.time - currentPackageStartedAt);
                }
                EmitTelemetry("package_end",
                    "packageType=" + packageType
                    + ",phase=" + CurrentPhase
                    + ",durationMs=" + packageDurationMs.ToString("0")
                    + ",hadAttackLanded=" + currentPackageHadAttackLanded);
                EmitTelemetry("weapon_transform_guard",
                    "weaponLocalPosDrift=0,weaponLocalRotDrift=0,packageType=" + packageType);
                attackLoopLastTickTime = Time.time;

                // 每个技能结束都兜底恢复 AI；若协程内部 ResumeAI 已经跑过，再调一次也幂等。
                if (aiController != null && aiController.IsPaused && CurrentPhase != PhantomWitchPhase.Dead)
                {
                    ModBehaviour.DevLog("[PhantomWitch] [AttackLoop] 技能结束但 AI 仍处于暂停，补发 ResumeAI");
                    ResumeAI(target);
                }

                currentPackageIndex = (currentPackageIndex + 1) % sequence.Length;
                yield return GetCurrentPackageIntervalYield();
                attackLoopLastTickTime = Time.time;
            }

            ModBehaviour.DevLog("[PhantomWitch] 攻击循环结束");
        }

        private WaitForSeconds GetCurrentPackageIntervalYield()
        {
            switch (CurrentPhase)
            {
                case PhantomWitchPhase.Phase2:
                    return waitPhase2PackageInterval;
                case PhantomWitchPhase.Phase3:
                    return waitPhase3PackageInterval;
                default:
                    return waitPhase1PackageInterval;
            }
        }

        private IEnumerator ExecuteAttackPackage(PhantomWitchAttackPackageType packageType)
        {
            switch (packageType)
            {
                case PhantomWitchAttackPackageType.FlankPressure:
                    yield return ExecuteFlankPressurePackage();
                    yield break;
                case PhantomWitchAttackPackageType.MidrangeRequiem:
                    yield return ExecuteMidrangeRequiemPackage();
                    yield break;
                case PhantomWitchAttackPackageType.WraithTrailObserve:
                    yield return ExecuteWraithTrailObservePackage();
                    yield break;
                case PhantomWitchAttackPackageType.MidrangeDouble:
                    yield return ExecuteMidrangeDoublePackage();
                    yield break;
                case PhantomWitchAttackPackageType.CurseTrap:
                    yield return ExecuteCurseTrapPackage();
                    yield break;
                case PhantomWitchAttackPackageType.ShortDriftPressure:
                    yield return ExecuteShortDriftPressurePackage();
                    yield break;
                case PhantomWitchAttackPackageType.LastStandSummon:
                    yield return ExecuteLastStandSummonPackage();
                    yield break;
                case PhantomWitchAttackPackageType.MinionRetreat:
                    yield return ExecuteMinionRetreatPackage();
                    yield break;
                default:
                    yield return wait05s;
                    yield break;
            }
        }

        private IEnumerator ExecuteFlankPressurePackage()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);

            if (target != null)
            {
                yield return ExecuteTrackedTeleportStrike(target);
            }
            else
            {
                yield return ExecuteImmediateScytheSweep(null);
            }
        }

        private IEnumerator ExecuteMidrangeRequiemPackage()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            PauseAI();
            SetStealthMode(PhantomWitchStealthMode.SemiStealthWindup);

            TrackEffect(PhantomWitchAssetManager.CreateChannelChargeEffect(
                bossCharacter.transform.position,
                1.2f,
                PhantomWitchConfig.RequiemArcWindup,
                false));
            // 审查 VB-07：安魂弧同样按判定值（RequiemArcRange / 28° / 前移 1.0）画扇形预警，跟随目标方向。
            TrackEffect(PhantomWitchAssetManager.CreateConeTelegraph(
                bossCharacter.transform,
                target != null ? target.transform : null,
                PhantomWitchConfig.RequiemArcRange,
                28f,
                1.0f,
                PhantomWitchConfig.RequiemArcWindup));
            yield return new WaitForSeconds(PhantomWitchConfig.RequiemArcWindup);

            if (!CanContinueAttacking())
            {
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
                yield break;
            }

            SetStealthMode(PhantomWitchStealthMode.Visible);
            ForceScytheAttackAnimation(target);
            Vector3 forward = ResolveAttackForward(target);
            Vector3 origin = bossCharacter.transform.position + forward * 1.0f;
            TrackEffect(PhantomWitchAssetManager.CreateScytheSweepEffect(
                origin,
                forward,
                PhantomWitchConfig.RequiemArcRange,
                28f));
            DealConeDamage(
                PhantomWitchConfig.RequiemArcRange,
                28f,
                PhantomWitchConfig.RequiemArcDamage,
                true,
                1.0f,
                target);
            ResumeAI(target);
        }

        private IEnumerator ExecuteWraithTrailObservePackage()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            if (target == null)
            {
                wraithFallbackCount++;
                EmitTelemetry("wraith_fallback_to_sweep", "reason=missing_target");
                yield return ExecuteScytheSweep();
                yield break;
            }

            PauseAI();
            SetStealthMode(PhantomWitchStealthMode.SemiStealthWindup);
            float windupDuration = Mathf.Max(PhantomWitchConfig.WraithWindupDuration, PhantomWitchConfig.WraithWindupMinGate);
            Vector3 lockedForward = ResolveAttackForward(target);

            GameObject windupOutline = PhantomWitchAssetManager.CreateWraithWindupOutlineEffect(
                bossCharacter.transform.position + lockedForward * 0.9f,
                lockedForward,
                PhantomWitchConfig.WraithWindupOutlineRadius,
                windupDuration);
            if (windupOutline == null)
            {
                wraithFallbackCount++;
                EmitTelemetry("wraith_fallback_to_sweep", "reason=missing_windup_vfx");
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
                yield return ExecuteScytheSweep();
                yield break;
            }

            TrackEffect(windupOutline);

            // 审查 VB-07：两段判定各画一片扇形预警（半径 / 半角 / 前移量与下方 DealConeDamage 的字面量一致）：
            // 第一段 3.2 m / 48° 实色填充，蓄力结束即结算；第二段 3.6 m / 54° 只画虚线外沿，再过 WraithTrailDelay 结算。
            // 判定在出手瞬间朝向目标（ResolveAttackForward），预警跟随目标方向。
            Transform wraithTargetTransform = target != null ? target.transform : null;
            TrackEffect(PhantomWitchAssetManager.CreateConeTelegraph(
                bossCharacter.transform,
                wraithTargetTransform,
                3.2f,
                48f,
                1.15f,
                windupDuration));
            TrackEffect(PhantomWitchAssetManager.CreateConeTelegraphOutline(
                bossCharacter.transform,
                wraithTargetTransform,
                3.6f,
                54f,
                1.0f,
                windupDuration + PhantomWitchConfig.WraithTrailDelay));

            TrackEffect(PhantomWitchAssetManager.CreateChannelChargeEffect(
                bossCharacter.transform.position,
                1.0f,
                windupDuration,
                true));
            yield return RunBodySinkWindup(windupDuration, PhantomWitchConfig.WraithBodySinkDepth);

            if (!CanContinueAttacking())
            {
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
                yield break;
            }

            SetStealthMode(PhantomWitchStealthMode.Visible);
            ForceScytheAttackAnimation(target);
            // 2026-09-23 拍板：刀光与伤害同一个方向。两段 DealConeDamage 都在出手瞬间重新朝向目标，扇形预警也跟着目标转；
            // 旧写法刀光仍按蓄力起点的 lockedForward 画，玩家横移后刀光扫向旧方向、伤害却照样打中。判定一字未改。
            Vector3 releaseForward = ResolveAttackForward(target);
            Vector3 origin = bossCharacter.transform.position + releaseForward * 1.15f;
            TrackEffect(PhantomWitchAssetManager.CreateHeavySlashEffect(origin, releaseForward, 3.2f));
            DealConeDamage(3.2f, 48f, PhantomWitchConfig.WraithTrailDamage, true, 1.15f, target);
            yield return new WaitForSeconds(PhantomWitchConfig.WraithTrailDelay);

            if (CanContinueAttacking())
            {
                ForceScytheAttackAnimation(target);
                Vector3 secondForward = ResolveAttackForward(target);
                Vector3 secondOrigin = bossCharacter.transform.position + secondForward * 1.0f;
                TrackEffect(PhantomWitchAssetManager.CreateScytheSweepEffect(secondOrigin, secondForward, 3.6f, 54f));
                DealConeDamage(3.6f, 54f, PhantomWitchConfig.WraithTrailDamage, false, 1.0f, target);
            }

            ResumeAI(target);
        }

        private IEnumerator ExecuteMidrangeDoublePackage()
        {
            yield return ExecuteMidrangeRequiemPackage();
            if (CanContinueAttacking())
            {
                yield return ExecuteWraithTrailObservePackage();
            }
        }

        private IEnumerator ExecuteCurseTrapPackage()
        {
            float radiusScale = CurrentPhase == PhantomWitchPhase.Phase3
                ? PhantomWitchConfig.CurseRealmPhase3RadiusScale
                : 1f;
            float durationScale = CurrentPhase == PhantomWitchPhase.Phase3
                ? PhantomWitchConfig.CurseRealmPhase3DurationScale
                : 1f;

            yield return ExecuteTelegraphedCurseRealm(radiusScale, durationScale);

            if (CanContinueAttacking())
            {
                if (CurrentPhase == PhantomWitchPhase.Phase3)
                {
                    yield return ExecuteShortDriftPressurePackage();
                }
                else
                {
                    yield return ExecuteFlankPressurePackage();
                }
            }
        }

        private IEnumerator ExecuteShortDriftPressurePackage()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);

            if (target != null)
            {
                yield return ExecuteTrackedTeleportStrike(target);
            }
            else
            {
                yield return ExecuteImmediateScytheSweep(null);
            }
        }

        private IEnumerator ExecuteLastStandSummonPackage()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);

            if (CountLiveMinions() >= PhantomWitchConfig.MaxMinions)
            {
                yield return ExecuteMinionRetreatPackage();
                yield break;
            }

            PauseAI();
            SetStealthMode(PhantomWitchStealthMode.SemiStealthWindup);
            TrackEffect(PhantomWitchAssetManager.CreateSummonCircleEffect(bossCharacter.transform.position));
            TrackEffect(PhantomWitchAssetManager.CreateChannelChargeEffect(
                bossCharacter.transform.position,
                PhantomWitchConfig.SummonCircleRadius * 0.55f,
                PhantomWitchConfig.SummonWindup,
                false));
            yield return waitSummonWindup;
            yield return SpawnMinionPair();
            SetStealthMode(PhantomWitchStealthMode.Visible);
            ResumeAI(target);
        }

        private IEnumerator ExecuteMinionRetreatPackage()
        {
            CharacterMainControl retreatAnchor = GetPreferredRetreatMinion();
            CharacterMainControl target;
            TryResolveCombatTarget(out target);

            if (retreatAnchor != null)
            {
                PauseAI();
                SetStealthMode(PhantomWitchStealthMode.TrueStealthTransition);
                Vector3 retreatPos = SampleNavMeshOrFallback(
                    retreatAnchor.transform.position + (bossCharacter.transform.position - retreatAnchor.transform.position).normalized * 1.8f,
                    bossCharacter.transform.position);
                yield return TeleportTo(retreatPos);
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
            }

            yield return ExecuteWraithTrailObservePackage();
        }

    }
}
