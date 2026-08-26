using System.Collections;
using System.Collections.Generic;
using System;
using Cysharp.Threading.Tasks;
using Duckov.UI;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private Action<Health, DamageInfo> zombieModeOnDeadHandler;
        private Action<Health, DamageInfo> zombieModeOnHurtHandler;

        private void RegisterZombieModeEventListeners(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            UnregisterZombieModeEventListeners();
            zombieModeOnDeadHandler = delegate(Health health, DamageInfo damageInfo)
            {
                HandleZombieModeHealthDead(runId, health, damageInfo);
            };
            zombieModeOnHurtHandler = delegate(Health health, DamageInfo damageInfo)
            {
                HandleZombieModeHealthHurt(runId, health, damageInfo);
            };
            Health.OnDead += zombieModeOnDeadHandler;
            Health.OnHurt += zombieModeOnHurtHandler;
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.EventListener, null, null, UnregisterZombieModeEventListeners);
        }

        private void UnregisterZombieModeEventListeners()
        {
            if (zombieModeOnDeadHandler != null)
            {
                Health.OnDead -= zombieModeOnDeadHandler;
                zombieModeOnDeadHandler = null;
            }

            if (zombieModeOnHurtHandler != null)
            {
                Health.OnHurt -= zombieModeOnHurtHandler;
                zombieModeOnHurtHandler = null;
            }
        }

        private void HandleZombieModeHealthHurt(int runId, Health health, DamageInfo damageInfo)
        {
            // 全局事件 hot path 早返：丧尸模式未激活时直接 return，
            // 避免对所有非丧尸伤害事件做 marker 查询。
            if (zombieModeRunState.LifecyclePhase == ZombieModeLifecyclePhase.None)
            {
                return;
            }
            if (!IsZombieModeRunValid(runId) || health == null || damageInfo.fromCharacter == null)
            {
                return;
            }

            CharacterMainControl victim = health.TryGetCharacter();
            // O(1) HashSet 早返替代 GetComponent<ZombieModeEnemyRuntimeMarker>（审查 §3.1）。
            // 非丧尸模式敌人不走 marker 路径，也不能误触发安全区取消。
            ZombieModeEnemyRuntimeMarker marker;
            if (victim == null || !TryGetZombieModeKnownEnemyMarker(victim, out marker))
            {
                return;
            }

            if (marker != null && marker.RunId == runId)
            {
                TryHandleZombieModeSafeZonePlayerAttack(runId, damageInfo, victim);
                ApplyZombieModeEnemyHurtAffixes(runId, health, damageInfo, marker);
                HandleZombieModeOptionHealthHurt(runId, health, damageInfo, victim, marker);
                if (marker.IsBoss)
                {
                    HandleZombieModeBossHurt(runId, marker, victim);
                    if (damageInfo.fromCharacter.IsMainCharacter)
                    {
                        float absorbedFinalDamage = AbsorbZombieModeBossFinalDamage(victim, marker, damageInfo.finalDamage);
                        RestoreZombieModeFinalDamageReduction(health, damageInfo, absorbedFinalDamage);
                    }
                }
                else if (damageInfo.fromCharacter.IsMainCharacter)
                {
                    float absorbedFinalDamage = 0f;
                    ZombieModeBossShieldRuntime allyShield = marker.AllyShield;
                    if (allyShield != null && allyShield.IsShieldActive())
                    {
                        absorbedFinalDamage += allyShield.AbsorbDamage(Mathf.Max(0f, damageInfo.finalDamage - absorbedFinalDamage));
                    }

                    absorbedFinalDamage += ApplyZombieModeShielderAuraFinalDamageReduction(
                        victim,
                        Mathf.Max(0f, damageInfo.finalDamage - absorbedFinalDamage));
                    RestoreZombieModeFinalDamageReduction(health, damageInfo, absorbedFinalDamage);
                }
            }

        }

        // 玩家在任意阶段的当前安全区内直接伤害丧尸时，立即取消整个安全区。
        // 单纯开枪、装填、投掷、误伤非丧尸目标、出圈攻击都不触发。
        private void TryHandleZombieModeSafeZonePlayerAttack(int runId, DamageInfo damageInfo, CharacterMainControl victim)
        {
            if (damageInfo.fromCharacter == null ||
                !damageInfo.fromCharacter.IsMainCharacter ||
                damageInfo.isFromBuffOrEffect ||
                victim == null ||
                !zombieModeRunState.ActiveSafeZoneActive ||
                !IsZombieModePlayerInsideActiveSafeZone() ||
                !ZombieModePhaseGuards.AllowsSafeZone(zombieModeRunState.CombatPhase))
            {
                return;
            }

            CancelZombieModeSafeZone(runId, "PlayerAttack");
        }

        private void RestoreZombieModeFinalDamageReduction(Health health, DamageInfo damageInfo, float absorbedFinalDamage)
        {
            if (health == null || absorbedFinalDamage <= 0f)
            {
                return;
            }

            // 注：Health.OnHurt 在伤害已扣后触发（鸭科夫源码 Health.cs:418），
            // mod 层无法挡掉伤害，只能用 SetHealth 把吸收的部分加回去（heal-back 模式）。
            // 唯一原生替代是 Health.SetInvincible(true)，但是 0/1 全免无法做"吸收 N 点"。
            // 见 docs/代码审查/2026-05-03_丧尸模式代码审查.md §4.1
            TryRestoreZombieModeFinalDamage(health, damageInfo, absorbedFinalDamage);
        }

        private void TryRestoreZombieModeFinalDamage(Health health, DamageInfo damageInfo, float restoreFinalDamage)
        {
            if (health == null || restoreFinalDamage <= 0f)
            {
                return;
            }

            float actualRestore = Mathf.Min(restoreFinalDamage, Mathf.Max(0f, damageInfo.finalDamage));
            if (actualRestore <= 0f)
            {
                return;
            }

            float restoredHealth = Mathf.Min(health.MaxHealth, health.CurrentHealth + actualRestore);
            if (restoredHealth <= 0f)
            {
                return;
            }

            health.SetHealth(restoredHealth);
        }

        private void HandleZombieModeHealthDead(int runId, Health health, DamageInfo damageInfo)
        {
            // 全局事件 hot path 早返：丧尸模式未激活时直接 return。
            if (zombieModeRunState.LifecyclePhase == ZombieModeLifecyclePhase.None)
            {
                return;
            }
            if (!IsZombieModeRunValid(runId) || health == null)
            {
                return;
            }

            CharacterMainControl character = health.TryGetCharacter();
            if (character != null && character.IsMainCharacter)
            {
                FailZombieModeActive(runId);
                return;
            }

            // O(1) HashSet 早返；非丧尸模式敌人死亡直接 ignore（审查 §3.1）。
            ZombieModeEnemyRuntimeMarker marker;
            if (character == null || !TryGetZombieModeKnownEnemyMarker(character, out marker))
            {
                return;
            }

            if (marker == null || marker.RunId != runId)
            {
                return;
            }

            if (marker.DeathSettled || marker.RemovedFromRuntime)
            {
                return;
            }

            // 官方 Health.Hurt 的致死顺序是 OnDead -> SetActive(false) -> OnHurt。
            // 必须在 DeathSettled 和 hot-path marker 注销前处理，否则致死一击不会取消安全区。
            TryHandleZombieModeSafeZonePlayerAttack(runId, damageInfo, character);
            HandleZombieModeOptionHealthDead(runId, health, damageInfo, character, marker);
            marker.DeathSettled = true;
            // 一旦 DeathSettled 就从 hot path 集合移除——后续技能命中尸体不会重新进入 marker 路径。
            UnregisterZombieModeEnemyInstanceId(character);

            zombieModeRunState.LivingZombieCount = Mathf.Max(0, zombieModeRunState.LivingZombieCount - 1);
            if (!marker.IsBoss)
            {
                zombieModeRunState.LivingNormalZombieCount = Mathf.Max(0, zombieModeRunState.LivingNormalZombieCount - 1);
            }

            int pointValue = Mathf.Max(1, marker.PurificationPointValue);
            int starCount = GetZombieModeDeathStarCount(marker);
            SpawnZombieModeDeathStars(runId, character.transform.position, pointValue, starCount);

            if (marker.IsBoss)
            {
                HandleZombieModeBossDefeated(runId, marker, character);
                HandleZombieModeBossDeathEffects(runId, marker, character);
                TrySpawnZombieModeBossDrop(runId, marker, character.transform.position);
                if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat &&
                    zombieModeRunState.CurrentWaveBossesRemaining <= 0)
                {
                    CompleteZombieModeWave(runId);
                }
                PruneZombieModeRunOnlyEnemyRecords(runId);
                return;
            }

            if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                HandleZombieModeEliteDeathEffects(runId, marker, character);
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                HandleZombieModeSpecialDeathEffects(runId, marker, character);
            }

            TrySpawnZombieModeEnemyDrop(runId, marker, character.transform.position);
            zombieModeRunState.CurrentWaveKills++;
            if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat &&
                zombieModeRunState.CurrentWaveKillTarget > 0 &&
                zombieModeRunState.CurrentWaveKills >= zombieModeRunState.CurrentWaveKillTarget)
            {
                CompleteZombieModeWave(runId);
            }
            PruneZombieModeRunOnlyEnemyRecords(runId);
        }

        private void BeginZombieModePreparation(int runId, bool initial, bool extractionOpportunity)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            bool preservePortableSafeZone = zombieModeRunState.ActiveSafeZoneActive &&
                                             zombieModeRunState.ActiveSafeZonePortable;
            CleanupZombieModePreparationObjects(runId, preservePortableSafeZone);
            zombieModeRunState.CombatPhase = initial
                ? ZombieModeCombatPhase.InitialPreparation
                : (extractionOpportunity ? ZombieModeCombatPhase.ExtractionOpportunity : ZombieModeCombatPhase.Preparation);
            zombieModeRunState.PreparationTimer = initial
                ? ZombieModeTuning.PreparationCountdownSeconds
                : GetZombieModeSelectedPreparationDuration(runId);
            zombieModeRunState.PeriodicSpawnTimer = 0f;
            zombieModeRunState.BeaconChanneling = false;
            zombieModeRunState.BeaconChannelStartTime = 0f;
            zombieModeRunState.ExtractionChanneling = false;
            zombieModeRunState.SafeZoneStealthBroken = false;
            if (!preservePortableSafeZone)
            {
                CreateZombieModeSafeZone(runId);
            }
            else
            {
                TickZombieModeSafeZone();
            }
            CleanupZombieModeEnemiesNearPlayerSafeZone(runId, "BeginPreparation");
            EnsureZombieModeAmbientZombiePopulation(runId);
            if (extractionOpportunity)
            {
                EnsureZombieModeExtractionArea(runId);
                ShowZombieModeExtractionOpportunityUi(runId);
            }

            string text = initial
                ? L10n.T("BossRush_ZombieMode_Banner_PreparationStarted")
                : L10n.T("BossRush_ZombieMode_Banner_PreparationNextWave");
            ShowBigBanner(text);
        }

        private void TickZombieModeWaveController(float deltaTime)
        {
            if (!IsZombieModeActive || zombieModeRunState.CombatPhase == ZombieModeCombatPhase.None)
            {
                return;
            }

            if (zombieModeRunState.ActiveSafeZoneActive)
            {
                TickZombieModeSafeZone();
            }

            if (ZombieModePhaseGuards.IsCombatRunning(zombieModeRunState.CombatPhase))
            {
                TickZombieModeAmbientZombiePressure(zombieModeRunState.RunId, deltaTime);
                return;
            }

            if (ZombieModePhaseGuards.AllowsBeacon(zombieModeRunState.CombatPhase))
            {
                TickZombieModeAmbientZombiePressure(zombieModeRunState.RunId, deltaTime);
                if (zombieModeRunState.BeaconChanneling || zombieModeRunState.ExtractionChanneling)
                {
                    return;
                }

                zombieModeRunState.PreparationTimer -= deltaTime;
                if (zombieModeRunState.PreparationTimer <= 0f)
                {
                    StartZombieModeWave(zombieModeRunState.RunId);
                }
            }
        }

        private void StartZombieModeWave(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            CleanupZombieModePreparationObjects(runId);
            // 普通散落物在玩家完成奖励选择和休整后、下一波正式开始时清理；Boss 奖励箱由清理函数保留。
            CleanupZombieModeExpiredDropCandidates(true);
            zombieModeRunState.CurrentWave++;
            zombieModeRunState.CurrentWaveKills = 0;
            zombieModeRunState.CurrentWaveBossInstances.Clear();
            zombieModeRunState.CurrentWaveBossesRemaining = 0;
            zombieModeRunState.PeriodicSpawnTimer = 0f;
            zombieModeRunState.NextSpawnPointIndex = 0;
            zombieModeRunState.PreparationTimer = 0f;
            zombieModeRunState.BeaconChanneling = false;
            zombieModeRunState.BeaconChannelStartTime = 0f;
            zombieModeRunState.ExtractionChanneling = false;
            zombieModeRunState.SafeZoneStealthBroken = false;
            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.Combat;
            ReleaseZombieModeSafeZoneThreatSuppression();
            SpawnPendingZombieModeEliteSquad(runId);

            if (IsZombieModeBossWave(zombieModeRunState.CurrentWave))
            {
                zombieModeRunState.CurrentWaveKillTarget = 0;
                zombieModeRunState.CurrentWaveBossesRemaining = GetZombieModeBossCountForWave(zombieModeRunState.CurrentWave);
                ShowBigBanner(string.Format(L10n.T("BossRush_ZombieMode_Banner_WaveIncoming"), zombieModeRunState.CurrentWave));
                SpawnZombieModeBossWaveAsync(runId, zombieModeRunState.CurrentWaveBossesRemaining).Forget();
                return;
            }

            zombieModeRunState.CurrentWaveKillTarget = Mathf.Max(1, GetZombieModeBaseWaveKillTarget());
            ShowBigBanner(string.Format(L10n.T("BossRush_ZombieMode_Banner_WaveIncoming"), zombieModeRunState.CurrentWave));
        }

        private int GetZombieModeBaseWaveKillTarget()
        {
            int wave = Mathf.Max(1, zombieModeRunState.CurrentWave);
            int cycle = GetZombieModeWaveCycleIndex(wave);
            int stage = GetZombieModeNormalWaveStageIndex(wave);
            return ZombieModeTuning.NormalWaveKillTargetBase +
                   cycle * ZombieModeTuning.NormalWaveKillTargetPerCycle +
                   ZombieModeTuning.NormalWaveKillTargetStageOffsets[stage];
        }

        private async UniTask SpawnZombieModeWaveAsync(int runId, int count, bool adjustKillTargetOnFailure = true)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsZombieModeRunValid(runId) || zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat)
                {
                    return;
                }

                CharacterMainControl zombie = await TrySpawnZombieModeNormalZombieAsync(
                    runId,
                    GetZombieModeSpawnPosition(),
                    isSpawnPhaseStillAllowed: () => zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat);
                if (zombie == null &&
                    adjustKillTargetOnFailure &&
                    IsZombieModeRunValid(runId) &&
                    zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat)
                {
                    zombieModeRunState.CurrentWaveKillTarget = Mathf.Max(zombieModeRunState.CurrentWaveKills, zombieModeRunState.CurrentWaveKillTarget - 1);
                    if (zombieModeRunState.CurrentWaveKills >= zombieModeRunState.CurrentWaveKillTarget)
                    {
                        CompleteZombieModeWave(runId);
                        return;
                    }
                }

                await UniTask.Yield();
            }
        }

        private async UniTask SpawnZombieModeBossWaveAsync(int runId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsZombieModeRunValid(runId) || zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat)
                {
                    return;
                }

                ZombieModeBossKind kind = GetZombieModeBossKindForIndex(i);
                CharacterMainControl boss = await TrySpawnZombieModeBossAsync(runId, GetZombieModeBossSpawnPosition(i), kind);
                if (boss == null)
                {
                    zombieModeRunState.CurrentWaveBossesRemaining = Mathf.Max(0, zombieModeRunState.CurrentWaveBossesRemaining - 1);
                    if (zombieModeRunState.CurrentWaveBossesRemaining <= 0)
                    {
                        CompleteZombieModeWave(runId);
                        return;
                    }
                }

                await UniTask.Yield();
            }
        }

        private void TickZombieModeAmbientZombiePressure(int runId, float deltaTime)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            if (!IsZombieModeAmbientZombieSpawnPhase(zombieModeRunState.CombatPhase))
            {
                return;
            }

            if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat)
            {
                bool bossWave = IsZombieModeBossWave(zombieModeRunState.CurrentWave);
                int remainingToKill = zombieModeRunState.CurrentWaveKillTarget - zombieModeRunState.CurrentWaveKills;
                if (!bossWave && remainingToKill <= 0)
                {
                    return;
                }
            }

            zombieModeRunState.PeriodicSpawnTimer += deltaTime;
            if (zombieModeRunState.PeriodicSpawnTimer < GetZombieModeSpawnIntervalSeconds())
            {
                return;
            }

            zombieModeRunState.PeriodicSpawnTimer = 0f;
            ReconcileZombieModeLivingEnemyCounts(runId);
            int spawnCount = GetZombieModePeriodicSpawnCount();
            if (spawnCount <= 0)
            {
                return;
            }

            SpawnZombieModeWaveAcrossMapAsync(runId, spawnCount, false).Forget();
        }

        private void EnsureZombieModeAmbientZombiePopulation(int runId)
        {
            if (!IsZombieModeRunValid(runId) ||
                !IsZombieModeAmbientZombieSpawnPhase(zombieModeRunState.CombatPhase))
            {
                return;
            }

            ReconcileZombieModeLivingEnemyCounts(runId);
            int spawnCount = GetZombieModePeriodicSpawnCount();
            if (spawnCount <= 0)
            {
                return;
            }

            zombieModeRunState.PeriodicSpawnTimer = 0f;
            SpawnZombieModeWaveAcrossMapAsync(runId, spawnCount, false).Forget();
        }

        private bool IsZombieModeAmbientZombieSpawnPhase(ZombieModeCombatPhase phase)
        {
            return phase == ZombieModeCombatPhase.InitialPreparation ||
                   phase == ZombieModeCombatPhase.Preparation ||
                   phase == ZombieModeCombatPhase.ExtractionOpportunity ||
                   phase == ZombieModeCombatPhase.Combat;
        }

        private int GetZombieModeNormalZombieSpawnSlots()
        {
            int activeOrPending = zombieModeRunState.LivingNormalZombieCount + zombieModeRunState.PendingNormalZombieSpawns;
            return Mathf.Max(0, ZombieModeTuning.MaxNormalZombieCount - activeOrPending);
        }

        private void ReconcileZombieModeLivingEnemyCounts(int runId)
        {
            int livingTotal = CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, true);
            int livingNormal = 0;
            for (int i = 0; i < zombieModeEnemyMarkerScratch.Count; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = zombieModeEnemyMarkerScratch[i];
                if (marker != null && !marker.IsBoss)
                {
                    livingNormal++;
                }
            }

            zombieModeRunState.LivingZombieCount = livingTotal;
            zombieModeRunState.LivingNormalZombieCount = livingNormal;
            zombieModeEnemyMarkerScratch.Clear();
        }

        private int GetZombieModePeriodicSpawnCount()
        {
            int slots = GetZombieModeNormalZombieSpawnSlots();
            if (slots <= 0)
            {
                return 0;
            }

            int activeOrPending = zombieModeRunState.LivingNormalZombieCount + zombieModeRunState.PendingNormalZombieSpawns;
            int desiredSlots = Mathf.Max(0, GetZombieModeAmbientPressureTarget() - activeOrPending);
            int batchSize = GetZombieModeSpawnBatchSize();
            return Mathf.Clamp(
                Mathf.Min(slots, Mathf.Min(desiredSlots, batchSize)),
                0,
                ZombieModeTuning.MaxNormalZombieCount);
        }

        private int GetZombieModeAmbientPressureTarget()
        {
            int pacingWave = GetZombieModePacingWave();
            int target = GetZombieModeWavePressureTarget(pacingWave);
            if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat)
            {
                if (!IsZombieModeBossWave(zombieModeRunState.CurrentWave))
                {
                    int remainingToKill = Mathf.Max(
                        0,
                        zombieModeRunState.CurrentWaveKillTarget - zombieModeRunState.CurrentWaveKills);
                    int preparationFloor = GetZombieModePreparationPressureTarget(zombieModeRunState.CurrentWave + 1);
                    int ebbTarget = Mathf.Max(
                        preparationFloor,
                        remainingToKill * ZombieModeTuning.NormalWavePressurePerRemainingKill);
                    target = Mathf.Min(target, ebbTarget);
                }

                return Mathf.Clamp(target, 0, ZombieModeTuning.MaxNormalZombieCount);
            }

            return GetZombieModePreparationPressureTarget(pacingWave);
        }

        private int GetZombieModePreparationPressureTarget(int wave)
        {
            int preparationTarget = Mathf.CeilToInt(
                GetZombieModeWavePressureTarget(wave) * ZombieModeTuning.PreparationPressureFraction);
            return Mathf.Clamp(
                preparationTarget,
                ZombieModeTuning.PreparationPressureMinimum,
                ZombieModeTuning.PreparationPressureMaximum);
        }

        private int GetZombieModeWavePressureTarget(int wave)
        {
            wave = Mathf.Max(1, wave);
            int cycle = GetZombieModeWaveCycleIndex(wave);
            if (IsZombieModeBossWave(wave))
            {
                return Mathf.Min(
                    ZombieModeTuning.BossWaveSupportPressureMaximum,
                    ZombieModeTuning.BossWaveSupportPressureBase +
                    cycle * ZombieModeTuning.BossWaveSupportPressurePerCycle);
            }

            int stage = GetZombieModeNormalWaveStageIndex(wave);
            return Mathf.Min(
                ZombieModeTuning.MaxNormalZombieCount,
                ZombieModeTuning.NormalWavePressureBase +
                cycle * ZombieModeTuning.NormalWavePressurePerCycle +
                ZombieModeTuning.NormalWavePressureStageOffsets[stage]);
        }

        private int GetZombieModeSpawnBatchSize()
        {
            if (zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat)
            {
                return ZombieModeTuning.PreparationSpawnBatchSize;
            }

            int wave = Mathf.Max(1, zombieModeRunState.CurrentWave);
            int cycle = GetZombieModeWaveCycleIndex(wave);
            if (IsZombieModeBossWave(wave))
            {
                return Mathf.Clamp(1 + cycle / 2, 1, ZombieModeTuning.BossWaveSpawnBatchMaximum);
            }

            int stage = GetZombieModeNormalWaveStageIndex(wave);
            return Mathf.Clamp(
                ZombieModeTuning.NormalWaveSpawnBatchBase + stage / 2 + cycle / 2,
                1,
                ZombieModeTuning.NormalWaveSpawnBatchMaximum);
        }

        private float GetZombieModeSpawnIntervalSeconds()
        {
            if (zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat)
            {
                return ZombieModeTuning.PreparationSpawnIntervalSeconds;
            }

            int wave = Mathf.Max(1, zombieModeRunState.CurrentWave);
            int cycle = GetZombieModeWaveCycleIndex(wave);
            if (IsZombieModeBossWave(wave))
            {
                return Mathf.Max(
                    ZombieModeTuning.BossWaveSpawnIntervalMinSeconds,
                    ZombieModeTuning.BossWaveSpawnIntervalStartSeconds -
                    cycle * ZombieModeTuning.BossWaveSpawnIntervalCycleStepSeconds);
            }

            int stage = GetZombieModeNormalWaveStageIndex(wave);
            return Mathf.Max(
                ZombieModeTuning.NormalWaveSpawnIntervalMinSeconds,
                ZombieModeTuning.NormalWaveSpawnIntervalStartSeconds -
                stage * ZombieModeTuning.NormalWaveSpawnIntervalStageStepSeconds -
                cycle * ZombieModeTuning.NormalWaveSpawnIntervalCycleStepSeconds);
        }

        private int GetZombieModePacingWave()
        {
            return zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat
                ? Mathf.Max(1, zombieModeRunState.CurrentWave)
                : Mathf.Max(1, zombieModeRunState.CurrentWave + 1);
        }

        private static int GetZombieModeWaveCycleIndex(int wave)
        {
            return Mathf.Max(0, (Mathf.Max(1, wave) - 1) / 5);
        }

        private static float GetZombieModeBossHealthScale(int wave)
        {
            return Mathf.Min(
                ZombieModeTuning.BossHealthScaleMaximum,
                1f + GetZombieModeWaveCycleIndex(wave) * ZombieModeTuning.BossHealthScalePerCycle);
        }

        private static int GetZombieModeBossCountForWave(int wave)
        {
            return ZombieModeTuning.BossWaveCountBase +
                   GetZombieModeWaveCycleIndex(wave) * ZombieModeTuning.BossWaveCountPerCycle;
        }

        private static float GetZombieModeBossDamageScale(int wave)
        {
            return Mathf.Min(
                ZombieModeTuning.BossDamageScaleMaximum,
                1f + GetZombieModeWaveCycleIndex(wave) * ZombieModeTuning.BossDamageScalePerCycle);
        }

        private static float GetZombieModeBossRewardScale(int wave)
        {
            return Mathf.Min(
                ZombieModeTuning.BossRewardScaleMaximum,
                1f + GetZombieModeWaveCycleIndex(wave) * ZombieModeTuning.BossRewardScalePerCycle);
        }

        private static int GetZombieModeBossRewardSelectionCount(int wave)
        {
            return GetZombieModeWaveCycleIndex(wave) >= ZombieModeTuning.BossBonusSelectionStartCycle
                ? ZombieModeTuning.BossRewardSelectionMaximum
                : 1;
        }

        private static int GetZombieModeNormalWaveStageIndex(int wave)
        {
            return Mathf.Clamp((Mathf.Max(1, wave) - 1) % 5, 0, 3);
        }

        private float GetZombieModeWaveSpeedMultiplier(int wave)
        {
            return Mathf.Clamp(
                ZombieModeTuning.WaveSpeedMultiplierStart +
                Mathf.Max(0, wave - 1) * ZombieModeTuning.WaveSpeedMultiplierPerWave,
                ZombieModeTuning.WaveSpeedMultiplierStart,
                ZombieModeTuning.WaveSpeedMultiplierMaximum);
        }

        private float GetZombieModeSpawnPointMinPlayerDistance()
        {
            int wave = GetZombieModePacingWave();
            if (wave <= 2)
            {
                return ZombieModeTuning.EarlyWaveSpawnPointMinPlayerDistance;
            }

            if (wave <= 5)
            {
                return ZombieModeTuning.MidWaveSpawnPointMinPlayerDistance;
            }

            return ZombieModeTuning.LateWaveSpawnPointMinPlayerDistance;
        }

        private async UniTask SpawnZombieModeWaveAcrossMapAsync(int runId, int count, bool adjustKillTargetOnFailure = true)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsZombieModeRunValid(runId) ||
                    !IsZombieModeAmbientZombieSpawnPhase(zombieModeRunState.CombatPhase))
                {
                    return;
                }

                if (GetZombieModeNormalZombieSpawnSlots() <= 0)
                {
                    return;
                }

                Vector3 spawnPosition;
                if (!TryGetNextZombieModeMapSpawnPosition(out spawnPosition))
                {
                    await UniTask.Yield();
                    continue;
                }

                CharacterMainControl zombie = await TrySpawnZombieModeNormalZombieAsync(
                    runId,
                    spawnPosition,
                    isSpawnPhaseStillAllowed: () => IsZombieModeAmbientZombieSpawnPhase(zombieModeRunState.CombatPhase));
                if (zombie == null &&
                    adjustKillTargetOnFailure &&
                    IsZombieModeRunValid(runId) &&
                    zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat)
                {
                    zombieModeRunState.CurrentWaveKillTarget = Mathf.Max(zombieModeRunState.CurrentWaveKills, zombieModeRunState.CurrentWaveKillTarget - 1);
                    if (zombieModeRunState.CurrentWaveKills >= zombieModeRunState.CurrentWaveKillTarget)
                    {
                        CompleteZombieModeWave(runId);
                        return;
                    }
                }

                await UniTask.Yield();
            }
        }

        private bool TryGetNextZombieModeMapSpawnPosition(out Vector3 position)
        {
            return TryGetZombieModeReliableSpawnPosition(out position);
        }

        private void HandleZombieModeBossDefeated(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            zombieModeRunState.CurrentWaveBossesRemaining = Mathf.Max(0, zombieModeRunState.CurrentWaveBossesRemaining - 1);
            for (int i = 0; i < zombieModeRunState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = zombieModeRunState.CurrentWaveBossInstances[i];
                if (instance == null || instance.Character != character)
                {
                    continue;
                }

                ZombieModeHunterState hunterState = instance.SkillState as ZombieModeHunterState;
                if (hunterState != null)
                {
                    RemoveZombieModeHunterFrenzyModifiers(hunterState);
                }

                instance.Lifecycle.Alive = false;
                break;
            }

        }

        private bool IsZombieModeBossWave(int wave)
        {
            return wave > 0 && wave % 5 == 0;
        }

        private void BeginZombieModeExtractionOpportunity(int runId)
        {
            BeginZombieModePreparation(runId, false, true);
        }

        private void CompleteZombieModeWave(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat)
            {
                return;
            }

            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.Settling;
            CleanupZombieModeEnemiesNearPlayerSafeZone(runId, "CompleteWave");
            RecycleZombieModeTemporaryNpcs(runId);
            RecycleZombieModeTemporaryRealNpcs(runId);
            bool bossNode = IsZombieModeBossWave(zombieModeRunState.CurrentWave);
            if (bossNode)
            {
                zombieModeRunState.PollutionFromNatural++;
            }

            if (!TryGiveZombieModeWaveClearHealingItem())
            {
                DevLog("[ZombieMode] 波次结束治疗补给发放失败");
            }

            ShowBigBanner(string.Format(L10n.T("BossRush_ZombieMode_Banner_WaveCleared"), zombieModeRunState.CurrentWave));
            StartZombieModeCoroutine(ZombieModeSettlementCoroutine(runId, bossNode), runId);
        }

        private IEnumerator ZombieModeSettlementCoroutine(int runId, bool bossNode)
        {
            float remaining = ZombieModeTuning.SettlementMaxWaitSeconds;
            while (IsZombieModeRunValid(runId) && zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Settling)
            {
                if (!HasZombieModePendingPurificationStars())
                {
                    break;
                }

                if (!IsZombieModeRuntimePaused())
                {
                    remaining -= Time.unscaledDeltaTime;
                }

                if (remaining <= 0f)
                {
                    ForceCollectZombieModePendingPurificationStars(runId);
                    break;
                }

                yield return null;
            }

            if (!IsZombieModeRunValid(runId) || zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Settling)
            {
                yield break;
            }

            ForceCollectZombieModePendingPurificationStars(runId);
            ShowZombieModeRewardSelection(runId, bossNode);
        }

        private int GetZombieModeDeathStarCount(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null)
            {
                return 1;
            }

            if (marker.IsBoss)
            {
                return 8;
            }

            switch (marker.EnemyKind)
            {
                case ZombieModeEnemyKind.Elite:
                    return 5;
                case ZombieModeEnemyKind.Special:
                    return 3;
                default:
                    return 1;
            }
        }

        private void SpawnZombieModeDeathStars(int runId, Vector3 position, int totalValue, int starCount)
        {
            starCount = Mathf.Max(1, starCount);
            int perStar = Mathf.Max(1, Mathf.FloorToInt(totalValue / (float)starCount));
            int remainder = Mathf.Max(0, totalValue - perStar * starCount);
            int created = 0;
            for (int i = 0; i < starCount; i++)
            {
                int value = perStar + (i == 0 ? remainder : 0);
                Vector3 offset = starCount > 1
                    ? Quaternion.Euler(0f, 360f * i / starCount, 0f) * Vector3.forward * 0.4f
                    : Vector3.zero;
                if (CreateZombieModePurificationPoint(runId, position + offset, value))
                {
                    created++;
                }
            }

            if (created <= 0)
            {
                zombieModeRunState.PurificationPoints += totalValue;
            }
        }

        private void FailZombieModeActive(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.FailedExit;
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_Failed"));
            CleanupZombieModeForSceneChange(ZombieModeFailureReason.PlayerDeath);
            try
            {
                if (SceneLoader.Instance != null)
                {
                    UniTaskExtensions.Forget(SceneLoader.Instance.LoadBaseScene(null, true));
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] 死亡后回主场景失败: " + e.Message);
            }
        }
    }
}
