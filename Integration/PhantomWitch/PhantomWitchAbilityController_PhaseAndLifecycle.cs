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
        private void BeginPhaseTransition(PhantomWitchPhase nextPhase)
        {
            if (CurrentPhase == PhantomWitchPhase.Transitioning || CurrentPhase == nextPhase || CurrentPhase == PhantomWitchPhase.Dead)
            {
                return;
            }

            pendingTransitionTargetPhase = nextPhase;
            StartCoroutine(RunPhaseTransition());
        }

        private IEnumerator RunPhaseTransition()
        {
            ModBehaviour.DevLog("[PhantomWitch] 触发阶段转换 -> " + pendingTransitionTargetPhase + " | boss=" + DescribeBossState());
            EmitStealthRatioSnapshot(CurrentPhase);
            CurrentPhase = PhantomWitchPhase.Transitioning;

            if (currentAttackCoroutine != null)
            {
                ModBehaviour.DevLog("[PhantomWitch] [Phase] 停止当前 package 协程以进入新阶段");
                StopCoroutine(currentAttackCoroutine);
                currentAttackCoroutine = null;
            }

            LogSkillState("PhaseTransition", "before PauseAI", null);
            PauseAI();
            if (PhantomWitchConfig.PhaseTransitionClearsActiveRealm)
            {
                ClearActiveBossCurseRealm("phase_transition -> " + pendingTransitionTargetPhase);
            }

            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            Vector3 targetPos = ResolveTeleportPosition(target, 5f, 8f);
            ModBehaviour.DevLog("[PhantomWitch] [Phase] transition teleport targetPos=" + targetPos);

            yield return TeleportTo(targetPos);
            FaceTarget(target);
            TrackEffect(PhantomWitchAssetManager.CreatePhaseTransitionEffect(
                bossCharacter.transform.position));

            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.ShowMessage(
                    pendingTransitionTargetPhase == PhantomWitchPhase.Phase3
                        ? L10n.T(PhantomWitchConfig.Phase3MessageCN, PhantomWitchConfig.Phase3MessageEN)
                        : L10n.T(PhantomWitchConfig.Phase2MessageCN, PhantomWitchConfig.Phase2MessageEN));
            }

            yield return new WaitForSeconds(1.6f);

            CurrentPhase = pendingTransitionTargetPhase;
            nextMinionCensusTime = Time.time + PhantomWitchConfig.MinionCensusInterval;
            if (CurrentPhase == PhantomWitchPhase.Phase3)
            {
                phase3NextMotionSnapshotTime = Time.time + 5f;
                nextHarassMinionPressureTime = Time.time + PhantomWitchConfig.HarassMinionPressureInterval;
            }
            if (ambientPresence != null)
            {
                if (CurrentPhase == PhantomWitchPhase.Phase2)
                {
                    ambientPresence.SetPhase(PhantomWitchPhase.Phase2);
                }
                else if (CurrentPhase == PhantomWitchPhase.Phase3)
                {
                    ambientPresence.SetPhase(PhantomWitchPhase.Phase3);
                }
                else
                {
                    ambientPresence.SetPhase(CurrentPhase);
                }
            }
            currentPackageIndex = 0;
            LogSkillState("PhaseTransition", "before ResumeAI", target);
            ResumeAI(target);

            if (attackLoopCoroutine != null)
            {
                StopCoroutine(attackLoopCoroutine);
            }

            attackLoopCoroutine = StartCoroutine(AttackLoop());
            ModBehaviour.DevLog("[PhantomWitch] 阶段转换完成 -> " + CurrentPhase);
        }

        private IEnumerator ExecuteTelegraphedCurseRealm(float radiusScale, float durationScale)
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            PauseAI();
            SetStealthMode(PhantomWitchStealthMode.SemiStealthWindup);

            Vector3 groundPoint = target != null
                ? target.transform.position
                : bossCharacter.transform.position + bossCharacter.transform.forward * 2f;
            groundPoint = SampleNavMeshOrFallback(groundPoint, bossCharacter.transform.position);

            float scaledRadius = PhantomWitchConfig.BossCurseRealmRadius * radiusScale;
            float warningStartedAt = Time.time;
            GameObject warningCircle = CreateCurseRealmWarningCircle(
                groundPoint,
                scaledRadius,
                PhantomWitchConfig.CurseRealmWarningDuration);
            if (warningCircle != null)
            {
                TrackEffect(warningCircle);
            }
            realmWarningCount++;
            EmitTelemetry("realm_warning_spawn", "origin=" + groundPoint + ",warningMs=" + (PhantomWitchConfig.CurseRealmWarningDuration * 1000f).ToString("0"));

            yield return new WaitForSeconds(PhantomWitchConfig.CurseRealmWarningDuration);

            if (!CanContinueAttacking())
            {
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
                yield break;
            }

            ClearActiveBossCurseRealm("refresh_before_commit");

            float radius = scaledRadius;
            float duration = PhantomWitchConfig.BossCurseRealmDuration * durationScale;
            try
            {
                GameObject host = new GameObject("PhantomWitch_BossCurseRealm");
                host.transform.position = groundPoint;
                PhantomWitchBossCurseRealmRuntime runtime = host.AddComponent<PhantomWitchBossCurseRealmRuntime>();
                runtime.Initialize(groundPoint, radius, duration, CurrentPhase);
                runtime.BindController(this);
                activeBossCurseRealm = runtime;
                TrackEffect(host);
                realmCommitCount++;
            }
            catch (Exception e)
            {
                realmMisfireCount++;
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] Boss curse realm commit failed: " + e.Message);
                EmitTelemetry("realm_clear", "reason=commit_failed");
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
                yield break;
            }
            SetStealthMode(PhantomWitchStealthMode.Visible);
            FaceTarget(target);
            EmitTelemetry("realm_commit",
                "origin=" + groundPoint
                + ",radius=" + radius.ToString("0.00")
                + ",phase=" + CurrentPhase
                + ",commitMs=" + Mathf.Max(0f, (Time.time - warningStartedAt) * 1000f).ToString("0"));
            ResumeAI(target);
        }

        private GameObject CreateCurseRealmWarningCircle(Vector3 groundPoint, float radius, float duration)
        {
            try
            {
                return PhantomWitchAssetManager.CreateCurseRealmWarningCircle(
                    groundPoint,
                    radius,
                    Mathf.Max(duration, 0.01f));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] CreateCurseRealmWarningCircle失败: " + e.Message);
                return null;
            }
        }

        private void ClearActiveBossCurseRealm(string reason)
        {
            if (activeBossCurseRealm == null)
            {
                return;
            }

            PhantomWitchBossCurseRealmRuntime runtime = activeBossCurseRealm;
            activeBossCurseRealm = null;
            if (reason.StartsWith("phase_transition", StringComparison.Ordinal))
            {
                realmForcedClearOnTransitionCount++;
            }
            runtime.ForceTerminate(reason);
            EmitTelemetry("realm_clear", "reason=" + reason);
        }

        public void OnBossDeath()
        {
            PhantomWitchPhase phaseAtDeath = CurrentPhase;
            CurrentPhase = PhantomWitchPhase.Dead;
            EmitStealthRatioSnapshot(phaseAtDeath);
            if (phaseAtDeath == PhantomWitchPhase.Phase3 && !bossFirstPhase3ExitLogged)
            {
                bossFirstPhase3ExitLogged = true;
                EmitTelemetry("boss_first_phase3_exit",
                    "minionCleared=" + minionFirstClearedLogged + ",playerHPRatio=" + GetPlayerHealthRatio().ToString("0.00"));
            }

            Health.OnDead -= OnAnyEntityDeath;
            StopAllCoroutines();
            ClearActiveBossCurseRealm("boss_death");
            RestoreVisibleState();
            CleanupMinions();
            CleanupAllEffects();
            CleanupControllerRuntimeState();
            ReleaseAssetReferenceIfNeeded();
            PrintTelemetrySummary();

            ModBehaviour.DevLog("[PhantomWitch] Boss死亡清理完成");

            // 控制器在独立 GO 上，需显式销毁
            Destroy(gameObject);
        }

        public void OnPlayerDeath()
        {
            PhantomWitchPhase phaseAtStop = CurrentPhase;
            CurrentPhase = PhantomWitchPhase.Dead;
            EmitStealthRatioSnapshot(phaseAtStop);
            Health.OnDead -= OnAnyEntityDeath;

            ModBehaviour.DevLog(
                "[PhantomWitch] 检测到玩家死亡，停止所有攻击"
                + " | cachedTarget=" + DescribeCharacter(playerCharacter)
                + " | boss=" + DescribeBossState());

            StopAllCoroutines();
            ClearActiveBossCurseRealm("player_death");
            RestoreVisibleState();
            CleanupMinions();
            CleanupAllEffects();
            CleanupControllerRuntimeState();
            ReleaseAssetReferenceIfNeeded();

            ModBehaviour.DevLog("[PhantomWitch] 玩家死亡清理完成");

            // 控制器在独立 GO 上，需显式销毁
            Destroy(gameObject);
        }

        public void ReleaseAssetReferenceIfNeeded()
        {
            if (assetReferenceReleased)
            {
                return;
            }

            assetReferenceReleased = true;
            ModBehaviour.ReleasePhantomWitchInstance();
        }

        private void CleanupControllerRuntimeState()
        {
            attackLoopCoroutine = null;
            currentAttackCoroutine = null;

            if (activeSemiStealthEffect != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(activeSemiStealthEffect);
                }
                catch
                {
                }
                activeSemiStealthEffect = null;
            }

            if (ambientPresence != null)
            {
                try
                {
                    ambientPresence.Pause();
                }
                catch
                {
                }

                try
                {
                    UnityEngine.Object.Destroy(ambientPresence);
                }
                catch
                {
                }

                ambientPresence = null;
            }

            if (aiController != null)
            {
                aiController.Cleanup();
                aiController = null;
            }
        }

    }
}
