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
        private void Update()
        {
            // 控制器在独立 GO 上，若 bossCharacter 被外部销毁则自行清理
            // Destroy 延迟到帧末执行，届时 Unity 自动调用 OnDestroy 完成资源释放
            if (bossCharacter == null)
            {
                Destroy(gameObject);
                return;
            }

            TickAttackLoopWatchdog();
            TickStealthTelemetry();
            TickPhase3MotionSnapshot();
            TickMinionCensusTelemetry();

            if (CurrentPhase != PhantomWitchPhase.Phase3 || bossHealth == null || bossHealth.IsDead)
            {
                return;
            }

            TickMinionHealBonus();
            TickHarassMinionPressure();
        }

        /// <summary>
        /// 看门狗：如果 AttackLoop 长时间没推进（协程被 Unity 静默中断、
        /// 或 ResumeAI 因 yield 等待未执行），强制恢复 AI 并重启循环，
        /// 避免出现"Boss 第一个技能后原地不动"的死局。
        /// </summary>
        private void TickAttackLoopWatchdog()
        {
            if (bossCharacter == null || bossHealth == null || bossHealth.IsDead)
            {
                return;
            }
            if (CurrentPhase == PhantomWitchPhase.Dead || CurrentPhase == PhantomWitchPhase.Transitioning)
            {
                return;
            }
            if (aiController == null)
            {
                return;
            }

            bool stalled = (Time.time - attackLoopLastTickTime) > WatchdogStallThreshold;
            if (!stalled)
            {
                return;
            }

            if (Time.time - lastWatchdogRecoverTime < WatchdogRecoverCooldown)
            {
                return;
            }
            lastWatchdogRecoverTime = Time.time;

            ModBehaviour.DevLog(
                "[PhantomWitch] [Watchdog] AttackLoop 停滞 " + (Time.time - attackLoopLastTickTime).ToString("0.0")
                + "s，强制恢复 AI 并重启攻击循环");

            if (currentAttackCoroutine != null)
            {
                try { StopCoroutine(currentAttackCoroutine); } catch { }
                currentAttackCoroutine = null;
            }
            if (attackLoopCoroutine != null)
            {
                try { StopCoroutine(attackLoopCoroutine); } catch { }
                attackLoopCoroutine = null;
            }

            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            RestoreVisibleState();
            ResumeAI(target);

            attackLoopLastTickTime = Time.time;
            attackLoopCoroutine = StartCoroutine(AttackLoop());
        }

        private void TickMinionHealBonus()
        {
            float healPerSecond = 0f;
            for (int i = liveMinions.Count - 1; i >= 0; i--)
            {
                MinionEntry entry = liveMinions[i];
                CharacterMainControl minion = entry != null ? entry.character : null;
                if (minion == null || minion.Health == null || minion.Health.IsDead)
                {
                    liveMinions.RemoveAt(i);
                    continue;
                }

                if (entry.role == PhantomWitchMinionRole.Sustain)
                {
                    healPerSecond += PhantomWitchConfig.SustainMinionHealRate;
                    if (GetFlatDistance(minion.transform.position, bossCharacter.transform.position) <= PhantomWitchConfig.SustainProximityRadius)
                    {
                        healPerSecond += PhantomWitchConfig.SustainMinionHealRate * (PhantomWitchConfig.SustainProximityBonusMultiplier - 1f);
                    }
                }
            }

            if (healPerSecond > 0f)
            {
                bossHealth.AddHealth(healPerSecond * Time.deltaTime);
            }
        }

        private void TickStealthTelemetry()
        {
            if (CurrentPhase == PhantomWitchPhase.Transitioning || CurrentPhase == PhantomWitchPhase.Dead)
            {
                return;
            }

            int phaseIndex = GetPhaseTelemetryIndex(CurrentPhase);
            switch (currentStealthMode)
            {
                case PhantomWitchStealthMode.TrueStealthTransition:
                    telemetryTrueStealthSec += Time.deltaTime;
                    if (phaseIndex >= 0)
                    {
                        phaseTrueStealthSeconds[phaseIndex] += Time.deltaTime;
                    }
                    break;
                case PhantomWitchStealthMode.SemiStealthWindup:
                    telemetrySemiStealthSec += Time.deltaTime;
                    if (phaseIndex >= 0)
                    {
                        phaseSemiStealthSeconds[phaseIndex] += Time.deltaTime;
                    }
                    break;
                default:
                    telemetryVisibleSec += Time.deltaTime;
                    if (phaseIndex >= 0)
                    {
                        phaseVisibleSeconds[phaseIndex] += Time.deltaTime;
                    }
                    break;
            }

            if (currentStealthMode == PhantomWitchStealthMode.TrueStealthTransition &&
                Time.time - stealthModeEnteredAt >= PhantomWitchConfig.TrueStealthMaxDuration)
            {
                stealthTimeoutCount++;
                EmitTelemetry("stealth_timeout", "packageType=" + currentTelemetryPackageType + ",phase=" + CurrentPhase);
                SetStealthMode(PhantomWitchStealthMode.Visible, "timeout");
            }
        }

        private void TickPhase3MotionSnapshot()
        {
            if (CurrentPhase != PhantomWitchPhase.Phase3)
            {
                return;
            }

            if (phase3NextMotionSnapshotTime < 0f)
            {
                phase3NextMotionSnapshotTime = Time.time + 5f;
                return;
            }

            if (Time.time < phase3NextMotionSnapshotTime)
            {
                return;
            }

            float avgTeleportDistance = phase3TeleportCount > 0 ? phase3TeleportDistance / phase3TeleportCount : 0f;
            float phaseDuration = GetPhaseDurationSeconds(PhantomWitchPhase.Phase3);
            float teleportsPerMin = phaseDuration > 0.01f ? (phase3TeleportCount / phaseDuration) * 60f : 0f;
            float avgPackageInterval = phasePackageCounts[2] > 0 ? phasePackageDurationSeconds[2] / phasePackageCounts[2] : 0f;

            EmitTelemetry("phase3_motion_snapshot",
                "avgTeleportDistance=" + avgTeleportDistance.ToString("0.00")
                + ",teleportsPerMin=" + teleportsPerMin.ToString("0.00")
                + ",avgPackageInterval=" + avgPackageInterval.ToString("0.00"));
            phase3NextMotionSnapshotTime = Time.time + 5f;
        }

        private void TickMinionCensusTelemetry()
        {
            if (CurrentPhase == PhantomWitchPhase.Dead || CurrentPhase == PhantomWitchPhase.Transitioning)
            {
                return;
            }

            if (nextMinionCensusTime < 0f)
            {
                nextMinionCensusTime = Time.time + PhantomWitchConfig.MinionCensusInterval;
                return;
            }

            if (Time.time < nextMinionCensusTime)
            {
                return;
            }

            nextMinionCensusTime = Time.time + PhantomWitchConfig.MinionCensusInterval;
            EmitTelemetry("minion_census",
                "liveCount=" + CountLiveMinions()
                + ",roles=[" + DescribeCurrentLiveRoles() + "]"
                + ",phase=" + CurrentPhase);
        }

        private void TickHarassMinionPressure()
        {
            if (Time.time < nextHarassMinionPressureTime)
            {
                return;
            }

            CharacterMainControl target;
            if (!TryResolveCombatTarget(out target) || target == null || target.Health == null || target.Health.IsDead)
            {
                return;
            }

            Buff curseBuff = PhantomWitchAssetManager.GetCurseBuff();
            if (curseBuff == null)
            {
                return;
            }

            for (int i = liveMinions.Count - 1; i >= 0; i--)
            {
                MinionEntry entry = liveMinions[i];
                CharacterMainControl minion = entry != null ? entry.character : null;
                if (minion == null || minion.Health == null || minion.Health.IsDead)
                {
                    liveMinions.RemoveAt(i);
                    continue;
                }

                if (entry.role != PhantomWitchMinionRole.Harass)
                {
                    continue;
                }

                if (GetFlatDistance(minion.transform.position, target.transform.position) > PhantomWitchConfig.HarassMinionPressureRadius)
                {
                    continue;
                }

                try
                {
                    target.AddBuff(curseBuff, bossCharacter, GetCurrentWeaponTypeId());
                    PhantomWitchCurseSweatVfx.TryAttach(target.gameObject);
                    EmitTelemetry("minion_harass_pulse",
                        "player=" + DescribeCharacter(target)
                        + ",radius=" + PhantomWitchConfig.HarassMinionPressureRadius.ToString("0.0"));
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[PhantomWitch] [WARNING] Harass minion pulse failed: " + e.Message);
                }

                nextHarassMinionPressureTime = Time.time + PhantomWitchConfig.HarassMinionPressureInterval;
                return;
            }
        }

        private void IncrementPhasePackageCount(PhantomWitchPhase phase)
        {
            int phaseIndex = GetPhaseTelemetryIndex(phase);
            if (phaseIndex < 0)
            {
                return;
            }

            phasePackageCounts[phaseIndex]++;
        }

        private int GetPhaseTelemetryIndex(PhantomWitchPhase phase)
        {
            switch (phase)
            {
                case PhantomWitchPhase.Phase1: return 0;
                case PhantomWitchPhase.Phase2: return 1;
                case PhantomWitchPhase.Phase3: return 2;
                default: return -1;
            }
        }

        private float GetPhaseDurationSeconds(PhantomWitchPhase phase)
        {
            int phaseIndex = GetPhaseTelemetryIndex(phase);
            if (phaseIndex < 0)
            {
                return 0f;
            }

            return phaseTrueStealthSeconds[phaseIndex] +
                   phaseSemiStealthSeconds[phaseIndex] +
                   phaseVisibleSeconds[phaseIndex];
        }

        private void EmitStealthRatioSnapshot(PhantomWitchPhase phase)
        {
            int phaseIndex = GetPhaseTelemetryIndex(phase);
            if (phaseIndex < 0)
            {
                return;
            }

            float total = GetPhaseDurationSeconds(phase);
            float ratio = total > 0.01f
                ? (phaseTrueStealthSeconds[phaseIndex] + phaseSemiStealthSeconds[phaseIndex]) / total
                : 0f;

            EmitTelemetry("stealth_ratio_snapshot",
                "phase=" + phase
                + ",trueStealthSec=" + phaseTrueStealthSeconds[phaseIndex].ToString("0.00")
                + ",semiStealthSec=" + phaseSemiStealthSeconds[phaseIndex].ToString("0.00")
                + ",visibleSec=" + phaseVisibleSeconds[phaseIndex].ToString("0.00")
                + ",ratio=" + ratio.ToString("0.00"));
        }

        private int CountLiveMinions()
        {
            int count = 0;
            for (int i = liveMinions.Count - 1; i >= 0; i--)
            {
                MinionEntry entry = liveMinions[i];
                CharacterMainControl minion = entry != null ? entry.character : null;
                if (minion == null || minion.Health == null || minion.Health.IsDead)
                {
                    liveMinions.RemoveAt(i);
                    continue;
                }

                count++;
            }

            return count;
        }

        private int CountOccupiedMinionSlots()
        {
            return CountLiveMinions() + pendingMinionRoles.Count;
        }

        private bool HasRoleAlive(PhantomWitchMinionRole role)
        {
            for (int i = liveMinions.Count - 1; i >= 0; i--)
            {
                MinionEntry entry = liveMinions[i];
                if (entry == null || entry.character == null || entry.character.Health == null || entry.character.Health.IsDead)
                {
                    liveMinions.RemoveAt(i);
                    continue;
                }

                if (entry.role == role)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRoleReservedOrAlive(PhantomWitchMinionRole role)
        {
            return pendingMinionRoles.Contains(role) || HasRoleAlive(role);
        }

        private IEnumerator SpawnMinionPair()
        {
            int count = CountOccupiedMinionSlots();
            if (count >= PhantomWitchConfig.MaxMinions)
            {
                EmitTelemetry("minion_census", "liveCount=" + count + ",reason=skip_spawn");
                yield break;
            }

            List<PhantomWitchMinionRole> missingRoles = new List<PhantomWitchMinionRole>(2);
            if (!HasRoleReservedOrAlive(PhantomWitchMinionRole.Sustain))
            {
                missingRoles.Add(PhantomWitchMinionRole.Sustain);
            }

            if (!HasRoleReservedOrAlive(PhantomWitchMinionRole.Harass))
            {
                missingRoles.Add(PhantomWitchMinionRole.Harass);
            }

            if (missingRoles.Count == 0)
            {
                yield break;
            }

            if (count == 1 && missingRoles.Count != 1)
            {
                minionRosterDesyncCount++;
                EmitTelemetry("minion_roster_desync", "liveCount=" + count + ",rolesSeen=" + DescribeCurrentLiveRoles());
            }

            int totalCount = Mathf.Min(PhantomWitchConfig.PairFillSpawnsPerPackage, missingRoles.Count);
            for (int i = 0; i < totalCount; i++)
            {
                if (!pendingMinionRoles.Add(missingRoles[i]))
                {
                    continue;
                }

                SpawnMinion(i, totalCount, missingRoles[i]).Forget();
                if (i < totalCount - 1)
                {
                    yield return new WaitForSeconds(PhantomWitchConfig.MinionPairFrameGap);
                }
            }

            EmitTelemetry("minion_spawn", "liveCount=" + CountLiveMinions() + ",requested=" + totalCount);
            yield return waitSummonRecovery;
        }

        private void AssignMinionRole(CharacterMainControl minion, PhantomWitchMinionRole role)
        {
            if (minion == null)
            {
                return;
            }

            liveMinions.RemoveAll(delegate(MinionEntry entry)
            {
                return entry == null || entry.character == null || entry.character == minion;
            });
            liveMinions.Add(new MinionEntry
            {
                character = minion,
                role = role,
                spawnTimeGameSec = Time.time
            });
            minionTotalSpawned++;
            minionMaxConcurrent = Mathf.Max(minionMaxConcurrent, CountLiveMinions());
            if (role == PhantomWitchMinionRole.Sustain)
            {
                sawSustainMinion = true;
            }
            else if (role == PhantomWitchMinionRole.Harass)
            {
                sawHarassMinion = true;
            }
            EmitTelemetry("minion_spawn", "liveCount=" + CountLiveMinions() + ",role=" + role + ",name=" + minion.name);
        }

        private CharacterMainControl GetPreferredRetreatMinion()
        {
            for (int i = 0; i < liveMinions.Count; i++)
            {
                MinionEntry entry = liveMinions[i];
                if (entry != null && entry.character != null && entry.character.Health != null && !entry.character.Health.IsDead &&
                    entry.role == PhantomWitchMinionRole.Sustain)
                {
                    return entry.character;
                }
            }

            for (int i = 0; i < liveMinions.Count; i++)
            {
                MinionEntry entry = liveMinions[i];
                if (entry != null && entry.character != null && entry.character.Health != null && !entry.character.Health.IsDead)
                {
                    return entry.character;
                }
            }

            return null;
        }

        private void CheckPhaseTransition()
        {
            if (bossHealth == null || bossHealth.MaxHealth <= 0f || CurrentPhase == PhantomWitchPhase.Transitioning || CurrentPhase == PhantomWitchPhase.Dead)
            {
                return;
            }

            float healthPercent = bossHealth.CurrentHealth / bossHealth.MaxHealth;
            if (CurrentPhase == PhantomWitchPhase.Phase1 && healthPercent <= PhantomWitchConfig.Phase2HealthThreshold)
            {
                BeginPhaseTransition(PhantomWitchPhase.Phase2);
            }
            else if (CurrentPhase == PhantomWitchPhase.Phase2 && healthPercent <= PhantomWitchConfig.Phase3HealthThreshold)
            {
                BeginPhaseTransition(PhantomWitchPhase.Phase3);
            }
        }

    }
}
