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
            // 非丧尸模式敌人不走 marker 路径；但仍要走安全区破隐检测（玩家是伤害源时）。
            if (victim == null || !IsZombieModeKnownEnemy(victim))
            {
                TryProcessZombieModeSafeZoneStealthBreak(runId, damageInfo);
                return;
            }

            ZombieModeEnemyRuntimeMarker marker = victim.GetComponent<ZombieModeEnemyRuntimeMarker>();
            if (marker != null && marker.RunId == runId)
            {
                ApplyZombieModeEnemyHurtAffixes(runId, health, damageInfo, marker);
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

            TryProcessZombieModeSafeZoneStealthBreak(runId, damageInfo);
        }

        // 安全区破隐：只要玩家是伤害源（damageInfo.fromCharacter 是主角），就视为破隐；
        // 这一统一口径覆盖开火、近战命中、投掷、仇恨道具、被命中后反击 5 种情形。
        // 出区外射击：SafeZoneStealthBroken 在 EnterPreparation 才重置；本准备期内任意时刻破隐都会持续到下一波。
        private void TryProcessZombieModeSafeZoneStealthBreak(int runId, DamageInfo damageInfo)
        {
            if (damageInfo.fromCharacter == null ||
                !damageInfo.fromCharacter.IsMainCharacter ||
                damageInfo.isFromBuffOrEffect ||
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

            // 注：Health.OnHurt 在伤害已扣后触发（鸭科夫源码 Health.cs:418），
            // mod 层无法挡掉伤害，只能用 SetHealth 把吸收的部分加回去（heal-back 模式）。
            // 唯一原生替代是 Health.SetInvincible(true)，但是 0/1 全免无法做"吸收 N 点"。
            // 见 docs/项目可能的待修复问题/2026-05-03_丧尸模式代码审查.md §4.1
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
            if (character == null || !IsZombieModeKnownEnemy(character))
            {
                return;
            }

            ZombieModeEnemyRuntimeMarker marker = character.GetComponent<ZombieModeEnemyRuntimeMarker>();
            if (marker == null || marker.RunId != runId)
            {
                return;
            }

            if (marker.DeathSettled || marker.RecycledForPerformance)
            {
                return;
            }

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

            if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat)
            {
                TickZombieModeAmbientZombiePressure(zombieModeRunState.RunId, deltaTime);
                return;
            }

            if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.InitialPreparation ||
                zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Preparation ||
                zombieModeRunState.CombatPhase == ZombieModeCombatPhase.ExtractionOpportunity)
            {
                TickZombieModeSafeZone();
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
                zombieModeRunState.CurrentWaveBossesRemaining = GetZombieModeBossCount();
                ShowBigBanner(string.Format(L10n.T("BossRush_ZombieMode_Banner_WaveIncoming"), zombieModeRunState.CurrentWave));
                SpawnZombieModeBossWaveAsync(runId, zombieModeRunState.CurrentWaveBossesRemaining).Forget();
                return;
            }

            zombieModeRunState.CurrentWaveKillTarget = Mathf.Max(1, GetZombieModeBaseWaveKillTarget());
            ShowBigBanner(string.Format(L10n.T("BossRush_ZombieMode_Banner_WaveIncoming"), zombieModeRunState.CurrentWave));
        }

        private int GetZombieModeInitialWaveSpawnCount(int effectiveSpawnPointCount)
        {
            int baseCount = Mathf.Max(1, effectiveSpawnPointCount);
            return Mathf.Clamp(baseCount, 1, ZombieModeTuning.MaxInitialWaveSpawnCount);
        }

        private int GetZombieModeBaseWaveKillTarget()
        {
            return 32 + Mathf.Max(0, zombieModeRunState.CurrentWave - 1) * 5;
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
            if (zombieModeRunState.PeriodicSpawnTimer < ZombieModeTuning.PeriodicSpawnIntervalSeconds)
            {
                return;
            }

            zombieModeRunState.PeriodicSpawnTimer = 0f;
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

        private int GetZombieModePeriodicSpawnCount()
        {
            int slots = GetZombieModeNormalZombieSpawnSlots();
            if (slots <= 0)
            {
                return 0;
            }

            return Mathf.Clamp(slots, 1, ZombieModeTuning.MaxNormalZombieCount);
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

                Vector3 spawnPosition = GetNextZombieModeMapSpawnPosition();
                CharacterMainControl zombie = await TrySpawnZombieModeNormalZombieAsync(runId, spawnPosition);
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

        private Vector3 GetNextZombieModeMapSpawnPosition()
        {
            Vector3 position;
            if (TryGetNearestZombieModeMapSpawnPositionToPlayer(out position))
            {
                return position;
            }

            return GetZombieModeSpawnPosition();
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
            float remaining = ZombieModeTuning.SettlementMaxWaitSeconds;
            while (IsZombieModeRunValid(runId) && zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Settling)
            {
                if (!HasZombieModePendingPurificationStars())
                {
                    break;
                }

                if (!IsZombieModeGamePaused())
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
