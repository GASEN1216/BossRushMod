using System.Collections;
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
            ZombieModeEnemyRuntimeMarker marker = victim != null ? victim.GetComponent<ZombieModeEnemyRuntimeMarker>() : null;
            if (marker != null && marker.RunId == runId)
            {
                ApplyZombieModeEnemyHurtAffixes(runId, health, damageInfo, marker);
                if (marker.IsBoss)
                {
                    HandleZombieModeBossHurt(runId, marker, victim);
                    if (damageInfo.fromCharacter.IsMainCharacter)
                    {
                        float absorbedFinalDamage = AbsorbZombieModeBossFinalDamage(victim, damageInfo.finalDamage);
                        RestoreZombieModeFinalDamageReduction(health, damageInfo, absorbedFinalDamage);
                    }
                }
                else if (damageInfo.fromCharacter.IsMainCharacter)
                {
                    float absorbedFinalDamage = 0f;
                    ZombieModeBossShieldRuntime allyShield = victim.gameObject.GetComponent<ZombieModeBossShieldRuntime>();
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

            // 安全区破隐：只要玩家是伤害源（damageInfo.fromCharacter 是主角），就视为破隐；
            // 这一统一口径覆盖开火、近战命中、投掷、仇恨道具、被命中后反击 5 种情形。
            // 出区外射击：SafeZoneStealthBroken 在 EnterPreparation 才重置；本准备期内任意时刻破隐都会持续到下一波。
            if (!damageInfo.fromCharacter.IsMainCharacter ||
                !zombieModeRunState.ActiveSafeZoneActive ||
                zombieModeRunState.SafeZoneStealthBroken)
            {
                return;
            }

            BreakZombieModeSafeZoneStealth(runId);
        }

        private void RestoreZombieModeFinalDamageReduction(Health health, DamageInfo damageInfo, float absorbedFinalDamage)
        {
            if (health == null || absorbedFinalDamage <= 0f)
            {
                return;
            }

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

            ZombieModeEnemyRuntimeMarker marker = character != null ? character.GetComponent<ZombieModeEnemyRuntimeMarker>() : null;
            if (marker == null || marker.RunId != runId)
            {
                return;
            }

            if (marker.DeathSettled || marker.RecycledForPerformance)
            {
                return;
            }

            marker.DeathSettled = true;

            zombieModeRunState.LivingZombieCount = Mathf.Max(0, zombieModeRunState.LivingZombieCount - 1);
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
        }

        private void BeginZombieModePreparation(int runId, bool initial, bool extractionOpportunity)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            CleanupZombieModePreparationObjects(runId);
            zombieModeRunState.CombatPhase = initial
                ? ZombieModeCombatPhase.InitialPreparation
                : (extractionOpportunity ? ZombieModeCombatPhase.ExtractionOpportunity : ZombieModeCombatPhase.Preparation);
            zombieModeRunState.PreparationTimer = ZombieModeTuning.PreparationCountdownSeconds;
            zombieModeRunState.PeriodicSpawnTimer = 0f;
            zombieModeRunState.BeaconChanneling = false;
            zombieModeRunState.BeaconChannelStartTime = 0f;
            zombieModeRunState.ExtractionChanneling = false;
            zombieModeRunState.SafeZoneStealthBroken = false;
            CreateZombieModeSafeZone(runId);
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

            if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat)
            {
                TickZombieModeCombatPressure(zombieModeRunState.RunId, deltaTime);
                return;
            }

            if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.InitialPreparation ||
                zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Preparation ||
                zombieModeRunState.CombatPhase == ZombieModeCombatPhase.ExtractionOpportunity)
            {
                TickZombieModeSafeZone();
                if (zombieModeRunState.BeaconChanneling || zombieModeRunState.ExtractionChanneling)
                {
                    return;
                }

                zombieModeRunState.PreparationTimer -= Time.unscaledDeltaTime;
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
            zombieModeRunState.CurrentWave++;
            zombieModeRunState.CurrentWaveKills = 0;
            zombieModeRunState.CurrentWaveBossInstances.Clear();
            zombieModeRunState.CurrentWaveBossesRemaining = 0;
            zombieModeRunState.PeriodicSpawnTimer = 0f;
            zombieModeRunState.PreparationTimer = 0f;
            zombieModeRunState.BeaconChanneling = false;
            zombieModeRunState.BeaconChannelStartTime = 0f;
            zombieModeRunState.ExtractionChanneling = false;
            zombieModeRunState.SafeZoneStealthBroken = false;
            int effectiveSpawnPointCount = zombieModeRunState.EffectiveSpawnPoints.Count > 0
                ? zombieModeRunState.EffectiveSpawnPoints.Count
                : zombieModeRunState.SpawnPoints.Count;
            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.Combat;
            SpawnPendingZombieModeEliteSquad(runId);

            if (IsZombieModeBossWave(zombieModeRunState.CurrentWave))
            {
                zombieModeRunState.CurrentWaveKillTarget = 0;
                zombieModeRunState.CurrentWaveBossesRemaining = GetZombieModeBossCount();
                ShowBigBanner(string.Format(L10n.T("BossRush_ZombieMode_Banner_WaveIncoming"), zombieModeRunState.CurrentWave));
                SpawnZombieModeBossWaveAsync(runId, zombieModeRunState.CurrentWaveBossesRemaining).Forget();
                SpawnZombieModeWaveAsync(runId, effectiveSpawnPointCount, false).Forget();
                return;
            }

            zombieModeRunState.CurrentWaveKillTarget = Mathf.Max(1, effectiveSpawnPointCount + (zombieModeRunState.CurrentWave - 1) * 5);
            ShowBigBanner(string.Format(L10n.T("BossRush_ZombieMode_Banner_WaveIncoming"), zombieModeRunState.CurrentWave));
            SpawnZombieModeWaveAsync(runId, effectiveSpawnPointCount, false).Forget();
        }

        private async UniTask SpawnZombieModeWaveAsync(int runId, int count, bool adjustKillTargetOnFailure = true)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsZombieModeRunValid(runId) || zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat)
                {
                    return;
                }

                CharacterMainControl zombie = await TrySpawnZombieModeNormalZombieAsync(runId, GetZombieModeSpawnPosition());
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

        private void TickZombieModeCombatPressure(int runId, float deltaTime)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            bool bossWave = IsZombieModeBossWave(zombieModeRunState.CurrentWave);
            int remainingToKill = zombieModeRunState.CurrentWaveKillTarget - zombieModeRunState.CurrentWaveKills;
            if (!bossWave && remainingToKill <= 0)
            {
                return;
            }

            zombieModeRunState.PeriodicSpawnTimer += deltaTime;
            if (zombieModeRunState.PeriodicSpawnTimer < ZombieModeTuning.PeriodicSpawnIntervalSeconds)
            {
                return;
            }

            zombieModeRunState.PeriodicSpawnTimer = 0f;
            int effectiveSpawnPointCount = zombieModeRunState.EffectiveSpawnPoints.Count > 0
                ? zombieModeRunState.EffectiveSpawnPoints.Count
                : zombieModeRunState.SpawnPoints.Count;
            int spawnCount = Mathf.Max(1, effectiveSpawnPointCount);
            if (zombieModeRunState.PerformanceTier == ZombieModePerformanceTier.SoftProtect)
            {
                spawnCount = Mathf.Max(1, Mathf.FloorToInt(spawnCount * ZombieModeTuning.PerfSoftSpawnMultiplier));
            }
            else if (zombieModeRunState.PerformanceTier == ZombieModePerformanceTier.ExtremeProtect)
            {
                spawnCount = Mathf.Max(1, Mathf.FloorToInt(spawnCount * ZombieModeTuning.PerfExtremeSpawnMultiplier));
            }

            SpawnZombieModeWaveAsync(runId, spawnCount, false).Forget();
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

                instance.Alive = false;
                instance.PointsSettled = true;
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
            RecycleZombieModeTemporaryNpcs(runId);
            bool bossNode = IsZombieModeBossWave(zombieModeRunState.CurrentWave);
            if (bossNode)
            {
                zombieModeRunState.PollutionFromNatural++;
            }

            ShowBigBanner(string.Format(L10n.T("BossRush_ZombieMode_Banner_WaveCleared"), zombieModeRunState.CurrentWave));
            StartZombieModeCoroutine(ZombieModeSettlementCoroutine(runId, bossNode), runId);
        }

        private IEnumerator ZombieModeSettlementCoroutine(int runId, bool bossNode)
        {
            float deadline = Time.unscaledTime + ZombieModeTuning.SettlementMaxWaitSeconds;
            while (IsZombieModeRunValid(runId) && zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Settling)
            {
                if (!HasZombieModePendingPurificationStars())
                {
                    break;
                }

                if (Time.unscaledTime >= deadline)
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
            catch { }
        }
    }
}
