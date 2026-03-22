using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F Boss Registration And Respawn

        /// <summary>已注册的 Mode F Boss 死亡事件句柄，确保能对称取消订阅。</summary>
        private readonly Dictionary<CharacterMainControl, UnityAction<DamageInfo>> modeFBossDeathHandlers
            = new Dictionary<CharacterMainControl, UnityAction<DamageInfo>>();
        private readonly Dictionary<CharacterMainControl, Action<DamageInfo>> modeFBossLootHandlers
            = new Dictionary<CharacterMainControl, Action<DamageInfo>>();
        private readonly HashSet<CharacterMainControl> modeFActiveBossSet = new HashSet<CharacterMainControl>();
        private readonly HashSet<int> modeFHandledBossDeathIds = new HashSet<int>();
        private int modeFPendingRespawnCount = 0;
        private int modeFRespawnInFlightCount = 0;

        /// <summary>FindSpawnPointAwayFromPlayer 候选点缓存，避免每次 new List。改为实例字段防止重入污染。</summary>
        private readonly List<Vector3> reusableSpawnCandidates = new List<Vector3>();
        private readonly List<EnemyPresetInfo> modeFRespawnBossPresetScratch = new List<EnemyPresetInfo>();

        private const int MODEF_RESPAWN_NEAREST_POINT_POOL = 10;

        private Teams ResolveModeFBossCombatTeam(Teams requestedFaction, EnemyPresetInfo preset, Vector3 spawnPos)
        {
            if (IsValidModeFCombatFaction(requestedFaction))
            {
                return requestedFaction;
            }

            Teams allocatedFaction;
            if (TryGetModeFAllocatedFactionForPosition(spawnPos, out allocatedFaction))
            {
                return allocatedFaction;
            }

            if (preset != null)
            {
                Teams presetFaction = (Teams)preset.team;
                if (IsValidModeFCombatFaction(presetFaction))
                {
                    return presetFaction;
                }
            }

            return ModeEAvailableFactions[UnityEngine.Random.Range(0, ModeEAvailableFactions.Length)];
        }

        private bool IsValidModeFCombatFaction(Teams faction)
        {
            switch (faction)
            {
                case Teams.scav:
                case Teams.usec:
                case Teams.bear:
                case Teams.lab:
                case Teams.wolf:
                    return true;
                default:
                    return false;
            }
        }

        private bool TryGetModeFAllocatedFactionForPosition(Vector3 spawnPos, out Teams faction)
        {
            faction = Teams.middle;
            if (modeESpawnAllocation == null || modeESpawnAllocation.Count <= 0)
            {
                return false;
            }

            bool found = false;
            float bestDistanceSqr = float.MaxValue;
            foreach (var kvp in modeESpawnAllocation)
            {
                if (!IsValidModeFCombatFaction(kvp.Key) || kvp.Value == null)
                {
                    continue;
                }

                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    float distanceSqr = (kvp.Value[i] - spawnPos).sqrMagnitude;
                    if (distanceSqr < bestDistanceSqr)
                    {
                        bestDistanceSqr = distanceSqr;
                        faction = kvp.Key;
                        found = true;
                    }
                }
            }

            return found;
        }

        private bool IsModeFBountyPhaseActive()
        {
            switch (modeFState.CurrentPhase)
            {
                case ModeFPhase.Bounty:
                case ModeFPhase.HuntStorm:
                case ModeFPhase.Extraction:
                    return true;
                default:
                    return false;
            }
        }

        private void EnsureModeFBossHasBaseBountyMark(CharacterMainControl boss)
        {
            if (boss == null || !modeFActive || !IsModeFBountyPhaseActive())
            {
                return;
            }

            int bossId = boss.GetInstanceID();
            int marks = 0;
            if (modeFState.BountyMarksByCharacterId.TryGetValue(bossId, out marks) && marks > 0)
            {
                return;
            }

            modeFState.BountyMarksByCharacterId[bossId] = 1;
            modeFState.InitialBountyBossIds.Add(bossId);
            Debug.Log("[ModeF] [BOUNTY] autoMark=" + GetModeFActorDisplayName(boss, false)
                + " | phase=" + modeFState.CurrentPhase);
            MarkModeFBountyLeaderDirty();
            RefreshModeFBountyLeaderIfDirty();
        }

        private void PrepareModeESharedRuntimeForModeF()
        {
            ResetModeESharedRuntimeState(clearSpawnAllocation: true, clearSpawnerCache: false, stopWarmupCoroutine: false);
        }

        private void ResetModeESharedRuntimeAfterModeF()
        {
            ResetModeESharedRuntimeState(clearSpawnAllocation: true, clearSpawnerCache: false, stopWarmupCoroutine: true);
        }

        private void RemoveModeFCharacterReference(List<CharacterMainControl> list, CharacterMainControl target)
        {
            if (list == null || object.ReferenceEquals(target, null))
            {
                return;
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (object.ReferenceEquals(list[i], target))
                {
                    list.RemoveAt(i);
                }
            }
        }

        private bool IsTrackedModeFBoss(CharacterMainControl boss)
        {
            if (object.ReferenceEquals(boss, null))
            {
                return false;
            }

            if (modeFBossDeathHandlers.ContainsKey(boss))
            {
                return true;
            }

            if (modeFActiveBossSet.Contains(boss))
            {
                return true;
            }

            for (int i = 0; i < modeFState.ActiveBosses.Count; i++)
            {
                if (object.ReferenceEquals(modeFState.ActiveBosses[i], boss))
                {
                    return true;
                }
            }

            return false;
        }

        private bool RemoveModeFBossReference(CharacterMainControl boss)
        {
            if (object.ReferenceEquals(boss, null))
            {
                return false;
            }

            modeFActiveBossSet.Remove(boss);

            bool removed = false;
            for (int i = modeFState.ActiveBosses.Count - 1; i >= 0; i--)
            {
                if (object.ReferenceEquals(modeFState.ActiveBosses[i], boss))
                {
                    modeFState.ActiveBosses.RemoveAt(i);
                    removed = true;
                }
            }

            return removed;
        }

        private void UnregisterModeFBossDeath(CharacterMainControl boss)
        {
            if (object.ReferenceEquals(boss, null))
            {
                return;
            }

            UnityAction<DamageInfo> handler = null;
            if (!modeFBossDeathHandlers.TryGetValue(boss, out handler))
            {
                return;
            }

            try
            {
                if (!(boss == null) && boss.Health != null)
                {
                    boss.Health.OnDeadEvent.RemoveListener(handler);
                }
            }
            catch { }

            modeFBossDeathHandlers.Remove(boss);
        }

        private void UnregisterModeFBossLoot(CharacterMainControl boss)
        {
            if (object.ReferenceEquals(boss, null))
            {
                return;
            }

            Action<DamageInfo> handler = null;
            if (!modeFBossLootHandlers.TryGetValue(boss, out handler))
            {
                return;
            }

            try
            {
                if (!(boss == null))
                {
                    boss.BeforeCharacterSpawnLootOnDead -= handler;
                }
            }
            catch { }

            modeFBossLootHandlers.Remove(boss);
        }

        private void RegisterModeESharedRuntimeForModeFBoss(CharacterMainControl boss, Vector3 anchorPosition)
        {
            if (boss == null)
            {
                return;
            }

            Teams faction = boss.Team;
            CleanupModeESharedRuntimeForModeFBoss(boss, faction);
            TrackModeEAliveEnemy(boss, faction);
            RegisterEnemyRecoveryAnchor(boss, anchorPosition);
            RegisterModeEEnemyToSpawnerRoot(boss);
            RegisterModeEEnemyDeath(boss);
        }

        private void CleanupModeESharedRuntimeForModeFBoss(CharacterMainControl boss, Teams? faction = null)
        {
            if (object.ReferenceEquals(boss, null))
            {
                return;
            }

            try
            {
                if (!(boss == null))
                {
                    UnregisterModeEEnemyDeath(boss);
                    UnregisterModeEEnemyLootHandler(boss);
                    RemoveModeEScalingModifiers(boss);

                    if (boss.characterPreset != null)
                    {
                        UnityEngine.Object.Destroy(boss.characterPreset);
                    }
                }
            }
            catch { }

            modeEPendingAggroTraceDistance.Remove(boss);
            UnregisterModeEEnemyFromSpawnerRoot(boss);
            UnregisterEnemyRecovery(boss);
            UntrackModeEAliveEnemy(boss, faction);
            modeFBossAiControllers.Remove(boss);
        }

        private void CleanupModeFBossRuntimeState(
            CharacterMainControl boss,
            Teams? faction = null,
            bool removeBountyMarks = true,
            bool clearLootResolutionState = true)
        {
            if (!object.ReferenceEquals(boss, null))
            {
                if (!faction.HasValue)
                {
                    try { faction = boss.Team; } catch { }
                }

                RemoveModeFBossGrowthModifiers(boss);
                modeFBossForcedTargets.Remove(boss);
                ApplyModeFBossMoveSpeedModifier(boss, 0f);
                modeFBossAiControllers.Remove(boss);
                modeFActiveBossSet.Remove(boss);
            }

            CleanupModeESharedRuntimeForModeFBoss(boss, faction);
            UnregisterModeFBossLoot(boss);
            UnregisterModeFBossDeath(boss);

            if (clearLootResolutionState)
            {
                ClearModeFBossPlunderLootState(boss);
                ClearBossRandomLootTracking(boss);
            }

            if (removeBountyMarks && !object.ReferenceEquals(boss, null))
            {
                try
                {
                    modeFState.BountyMarksByCharacterId.Remove(boss.GetInstanceID());
                }
                catch { }
            }
        }

        private void RegisterModeFBoss(CharacterMainControl boss)
        {
            try
            {
                if (boss == null || !modeFActive)
                {
                    return;
                }

                bool isNewBoss = modeFActiveBossSet.Add(boss);
                if (isNewBoss)
                {
                    modeFState.ActiveBosses.Add(boss);
                }

                RegisterBossRandomLootTracking(boss);
                EnsureModeFBossHasBaseBountyMark(boss);
                GetModeFBossAIController(boss);
                ApplyModeFPressureToBoss(boss);
                EnsureModeFBossNameTag(boss);

                Health health = boss.Health;
                UnregisterModeFBossLoot(boss);

                CharacterMainControl capturedLootBoss = boss;
                Action<DamageInfo> lootHandler = (damageInfo) =>
                {
                    if (!modeFActive || capturedLootBoss == null)
                    {
                        return;
                    }

                    CharacterMainControl killer = null;
                    try { killer = damageInfo.fromCharacter; } catch { }
                    int victimMarks = 0;
                    try { modeFState.BountyMarksByCharacterId.TryGetValue(capturedLootBoss.GetInstanceID(), out victimMarks); } catch { }
                    if (killer != null &&
                        !object.ReferenceEquals(killer, capturedLootBoss) &&
                        IsTrackedModeFBoss(killer) &&
                        victimMarks > 0)
                    {
                        TryHandleModeFBossPreLootPlunder(killer, capturedLootBoss);
                    }

                    OnModeFBossDied(capturedLootBoss, damageInfo, "BeforeSpawnLoot");
                };
                modeFBossLootHandlers[boss] = lootHandler;
                boss.BeforeCharacterSpawnLootOnDead += lootHandler;

                if (health != null)
                {
                    UnregisterModeFBossDeath(boss);

                    CharacterMainControl capturedBoss = boss;
                    UnityAction<DamageInfo> handler = null;
                    handler = (damageInfo) =>
                    {
                        UnregisterModeFBossDeath(capturedBoss);
                        OnModeFBossDied(capturedBoss, damageInfo, "OnDeadEvent");
                    };

                    modeFBossDeathHandlers[boss] = handler;
                    health.OnDeadEvent.AddListener(handler);
                }

                DevLog("[ModeF] Boss registered: " + boss.gameObject.name + " (total=" + modeFState.ActiveBosses.Count + ")");
                Debug.Log("[ModeF] [RESPAWN] register=" + GetModeFActorDisplayName(boss, false)
                    + " | team=" + boss.Team
                    + " | total=" + modeFState.ActiveBosses.Count
                    + " | pending=" + modeFPendingRespawnCount
                    + " | inflight=" + modeFRespawnInFlightCount);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] RegisterModeFBoss failed: " + e.Message);
            }
        }

        private void OnModeFBossDied(CharacterMainControl deadBoss, DamageInfo damageInfo, string sourceTag = null)
        {
            int deadBossId = 0;
            try
            {
                if (!modeFActive || deadBoss == null)
                {
                    return;
                }

                deadBossId = deadBoss.GetInstanceID();
                if (!modeFHandledBossDeathIds.Add(deadBossId))
                {
                    return;
                }

                // C1 guard: 防止死亡事件重复触发（IntegrityCheck 移除后再次触发）
                if (!RemoveModeFBossReference(deadBoss))
                {
                    modeFHandledBossDeathIds.Remove(deadBossId);
                    return;
                }

                Teams? deadBossTeam = null;
                if (!(deadBoss == null))
                {
                    try { deadBossTeam = deadBoss.Team; } catch { }
                }

                CharacterMainControl killer = null;
                try { killer = damageInfo.fromCharacter; } catch { }

                int deadBossMarks = 0;
                modeFState.BountyMarksByCharacterId.TryGetValue(deadBossId, out deadBossMarks);

                bool preserveLootResolutionState = string.Equals(sourceTag, "OnDeadEvent", StringComparison.Ordinal);
                CleanupModeFBossRuntimeState(
                    deadBoss,
                    deadBossTeam,
                    false,
                    clearLootResolutionState: !preserveLootResolutionState);

                bool killedByPlayer = killer == CharacterMainControl.Main;
                bool killedByTrackedBoss = IsTrackedModeFBoss(killer);

                DevLog("[ModeF] Boss died: " + deadBoss.gameObject.name
                    + " (source=" + (string.IsNullOrEmpty(sourceTag) ? "unknown" : sourceTag)
                    + ")"
                    + " (marks=" + deadBossMarks
                    + ", killedByPlayer=" + killedByPlayer
                    + ", killer=" + (killer != null ? killer.gameObject.name : "null") + ")");
                Debug.Log("[ModeF] [RESPAWN] death victim=" + GetModeFActorDisplayName(deadBoss, false)
                    + " | killer=" + GetModeFActorDisplayName(killer, killedByPlayer)
                    + " | marks=" + deadBossMarks
                    + " | source=" + (string.IsNullOrEmpty(sourceTag) ? "unknown" : sourceTag)
                    + " | totalAfterRemove=" + modeFState.ActiveBosses.Count);

                if (killedByPlayer)
                {
                    OnModeFBossKilledByPlayer(deadBoss);
                    if (deadBossMarks > 0)
                    {
                        AddBountyBossExtraLoot(deadBoss, deadBossMarks);
                    }
                }
                else if (killedByTrackedBoss)
                {
                    OnModeFBossKilledByBoss(killer, deadBoss);
                    if (deadBossMarks > 0)
                    {
                        DevLog("[ModeF] 悬赏额外奖励机会已随印记一并转移给胜者，不在当前死亡点兑现");
                    }
                }
                else
                {
                    modeFState.BountyMarksByCharacterId.Remove(deadBossId);
                    MarkModeFBountyLeaderDirty();
                    RefreshModeFBountyLeaderIfDirty();
                    DevLog("[ModeF] Boss died to the environment or a non-ModeF actor, bounty marks were discarded.");
                }

                QueueModeFBossRespawn();
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] OnModeFBossDied failed: " + e.Message);
                if (deadBossId != 0)
                {
                    modeFHandledBossDeathIds.Remove(deadBossId);
                }
            }
        }

        private void QueueModeFBossRespawn(int count = 1)
        {
            if (count <= 0)
            {
                return;
            }

            modeFPendingRespawnCount += count;
            Debug.Log("[ModeF] [RESPAWN] queue pending=" + modeFPendingRespawnCount
                + " | inflight=" + modeFRespawnInFlightCount
                + " | added=" + count);
            TryFulfillModeFPendingRespawns();
        }

        private void TryFulfillModeFPendingRespawns()
        {
            if (!modeFActive || modeFPendingRespawnCount <= 0 || modeFRespawnInFlightCount > 0)
            {
                return;
            }

            Debug.Log("[ModeF] [RESPAWN] dispatch pending=" + modeFPendingRespawnCount
                + " | inflight=" + modeFRespawnInFlightCount);
            if (RespawnModeFBoss())
            {
                modeFPendingRespawnCount = Mathf.Max(0, modeFPendingRespawnCount - 1);
                modeFRespawnInFlightCount += 1;
            }
        }

        private void CompleteModeFBossRespawnAttempt(bool success, bool requeueOnFailure)
        {
            modeFRespawnInFlightCount = Mathf.Max(0, modeFRespawnInFlightCount - 1);

            if (!modeFActive)
            {
                return;
            }

            if (!success && requeueOnFailure)
            {
                modeFPendingRespawnCount += 1;
                Debug.Log("[ModeF] [RESPAWN] complete success=False | requeue=True"
                    + " | pending=" + modeFPendingRespawnCount
                    + " | inflight=" + modeFRespawnInFlightCount);
                return;
            }

            if (success)
            {
                Debug.Log("[ModeF] [RESPAWN] complete success=True"
                    + " | pending=" + modeFPendingRespawnCount
                    + " | inflight=" + modeFRespawnInFlightCount);
                TryFulfillModeFPendingRespawns();
            }
        }

        private bool RespawnModeFBoss()
        {
            try
            {
                if (!modeFActive)
                {
                    return false;
                }

                InitializeEnemyPresets();
                InitializeModeDEnemyPools();
                Vector3 spawnPos = FindSpawnPointAwayFromPlayer(50f);
                EnemyPresetInfo preset = GetRandomModeFRespawnBossPreset();
                if (preset == null)
                {
                    DevLog("[ModeF] [WARNING] RespawnModeFBoss: no boss preset is available.");
                    return false;
                }

                int modeFSessionToken = modeFState.RuntimeSessionToken;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                bool selectedDragonDescendant = IsDragonDescendantPreset(preset);
                if (selectedDragonDescendant)
                {
                    modeEDragonDescendantSpawned = true;
                }

                Debug.Log("[ModeF] [RESPAWN] request preset=" + preset.displayName + " | pos=" + spawnPos);

                SpawnEnemyCore(
                    preset,
                    spawnPos,
                    true,
                    () => IsModeFSessionStillValid(modeFSessionToken, relatedScene),
                    (ctx) =>
                    {
                        bool configured = false;
                        bool requeueOnFailure = true;
                        try
                        {
                            if (ctx.character == null)
                            {
                                return;
                            }

                            EnemyPresetInfo spawnedPreset = ctx.preset;
                            if (spawnedPreset == null)
                            {
                                return;
                            }

                            SyncModeEDragonDescendantSpawnFlag(selectedDragonDescendant, spawnedPreset, "ModeF");

                            if (ctx.character.characterPreset != null)
                            {
                                CharacterRandomPreset customPreset = UnityEngine.Object.Instantiate(ctx.character.characterPreset);
                                customPreset.aiCombatFactor = 1f;
                                customPreset.showName = true;
                                customPreset.showHealthBar = true;
                                ctx.character.characterPreset = customPreset;
                            }

                            Teams spawnedTeam = ResolveModeFBossCombatTeam(Teams.middle, spawnedPreset, spawnPos);
                            SetModeFBossDisplayName(ctx.character, spawnedPreset.displayName, spawnedTeam);
                            ctx.character.SetTeam(spawnedTeam);
                            ctx.character.gameObject.name = "ModeF_" + spawnedPreset.displayName;
                            RegisterModeESharedRuntimeForModeFBoss(ctx.character, ctx.position);
                            RegisterModeFBoss(ctx.character);
                            Debug.Log("[ModeF] [RESPAWN] spawned=" + GetModeFActorDisplayName(ctx.character, false)
                                + " | team=" + spawnedTeam
                                + " | total=" + modeFState.ActiveBosses.Count);
                            configured = true;
                        }
                        catch (Exception e)
                        {
                            DevLog("[ModeF] [WARNING] Failed to configure respawned boss: " + e.Message);
                        }
                        finally
                        {
                            CompleteModeFBossRespawnAttempt(configured, requeueOnFailure);
                        }
                    },
                    () =>
                    {
                        if (selectedDragonDescendant)
                        {
                            modeEDragonDescendantSpawned = false;
                        }

                        DevLog("[ModeF] [WARNING] Failed to spawn replacement boss.");
                        Debug.Log("[ModeF] [RESPAWN] spawnFailed");
                        CompleteModeFBossRespawnAttempt(false, true);
                    },
                    1,
                    skipDragonDescendant: !selectedDragonDescendant,
                    skipDragonKing: true
                );

                DevLog("[ModeF] Replacement boss is spawning at: " + spawnPos);
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] RespawnModeFBoss failed: " + e.Message);
                return false;
            }
        }

        private Vector3 FindSpawnPointAwayFromPlayer(float minDistance)
        {
            try
            {
                List<Vector3> nearestSpawnPoints = GetNearestSpawnPoints(MODEF_RESPAWN_NEAREST_POINT_POOL);
                if (nearestSpawnPoints != null && nearestSpawnPoints.Count > 0)
                {
                    return GetSafeBossSpawnPosition(
                        nearestSpawnPoints[UnityEngine.Random.Range(0, nearestSpawnPoints.Count)]);
                }

                CharacterMainControl player = CharacterMainControl.Main;
                Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;

                Vector3[] allSpawnPoints = GetModeEFlattenedSpawnPoints();
                if (allSpawnPoints.Length > 0)
                {
                    Vector3 bestPoint = Vector3.zero;
                    float bestDistanceSqr = 0f;
                    float minDistanceSqr = minDistance * minDistance;
                    reusableSpawnCandidates.Clear();

                    for (int i = 0; i < allSpawnPoints.Length; i++)
                    {
                        Vector3 point = allSpawnPoints[i];
                        float distanceSqr = (point - playerPos).sqrMagnitude;
                        if (distanceSqr >= minDistanceSqr)
                        {
                            reusableSpawnCandidates.Add(point);
                        }

                        if (distanceSqr > bestDistanceSqr)
                        {
                            bestDistanceSqr = distanceSqr;
                            bestPoint = point;
                        }
                    }

                    if (reusableSpawnCandidates.Count > 0)
                    {
                        return GetSafeBossSpawnPosition(
                            reusableSpawnCandidates[UnityEngine.Random.Range(0, reusableSpawnCandidates.Count)]);
                    }

                    if (bestDistanceSqr > 0f)
                    {
                        DevLog("[ModeF] [WARNING] No spawn point was found beyond " + minDistance + "m, using the farthest point instead (" + Mathf.Sqrt(bestDistanceSqr).ToString("F0") + "m).");
                        return GetSafeBossSpawnPosition(bestPoint);
                    }
                }

                return GetSafeBossSpawnPosition(playerPos + new Vector3(60f, 0f, 60f));
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] FindSpawnPointAwayFromPlayer failed: " + e.Message);
                return GetSafeBossSpawnPosition(Vector3.zero);
            }
        }

        private EnemyPresetInfo GetRandomModeFRespawnBossPreset()
        {
            InitializeEnemyPresets();
            InitializeModeDEnemyPools();
            BuildModeEFactionPresetCaches();

            if (modeDBossPool == null || modeDBossPool.Count == 0)
            {
                return null;
            }

            if (modeFRespawnBossPresetScratch.Capacity < modeDBossPool.Count)
            {
                modeFRespawnBossPresetScratch.Capacity = modeDBossPool.Count;
            }

            modeFRespawnBossPresetScratch.Clear();
            for (int i = 0; i < modeDBossPool.Count; i++)
            {
                EnemyPresetInfo preset = modeDBossPool[i];
                if (preset == null || string.IsNullOrEmpty(preset.name))
                {
                    continue;
                }

                if (IsDragonKingPreset(preset))
                {
                    continue;
                }

                if (modeEDragonDescendantSpawned && IsDragonDescendantPreset(preset))
                {
                    continue;
                }

                modeFRespawnBossPresetScratch.Add(preset);
            }

            if (modeFRespawnBossPresetScratch.Count <= 0)
            {
                return null;
            }

            return modeFRespawnBossPresetScratch[UnityEngine.Random.Range(0, modeFRespawnBossPresetScratch.Count)];
        }

        private void ModeFBossIntegrityCheck()
        {
            try
            {
                if (!modeFActive)
                {
                    return;
                }

                for (int i = modeFState.ActiveBosses.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl boss = modeFState.ActiveBosses[i];
                    if (boss == null || boss.gameObject == null || boss.Health == null || boss.Health.IsDead)
                    {
                        Teams? bossTeam = null;

                        try
                        {
                            if (!(boss == null))
                            {
                                try { bossTeam = boss.Team; } catch { }
                            }
                        }
                        catch { }

                        modeFState.ActiveBosses.RemoveAt(i);
                        CleanupModeFBossRuntimeState(boss, bossTeam);
                        try
                        {
                            if (!(boss == null))
                            {
                                modeFHandledBossDeathIds.Add(boss.GetInstanceID());
                                modeFState.BountyMarksByCharacterId.Remove(boss.GetInstanceID());
                            }
                        }
                        catch { }

                        MarkModeFBountyLeaderDirty();
                        QueueModeFBossRespawn();
                    }
                }

                RefreshModeFBountyLeaderIfDirty();
                TryFulfillModeFPendingRespawns();
            }
            catch { }
        }

        #endregion
    }
}
