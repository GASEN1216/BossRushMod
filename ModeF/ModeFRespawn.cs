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

        /// <summary>FindSpawnPointAwayFromPlayer 候选点缓存，避免每次 new List</summary>
        private static readonly List<Vector3> reusableSpawnCandidates = new List<Vector3>();

        private void PrepareModeESharedRuntimeForModeF()
        {
            modeEPlayerFaction = Teams.player;
            modeEAliveEnemies.Clear();
            modeEAliveEnemySet.Clear();
            modeEFactionAliveMap.Clear();
            modeEFactionDeathCount.Clear();
            modeEScalingModifiers.Clear();
            modeEEnemyDeathHandlers.Clear();
            modeEEnemyLootHandlers.Clear();
            modeEPendingScalingFactions.Clear();
            modeEScalingBatchTimer = 0f;
            modeESpawnerRootRegisteredEnemies.Clear();
            CleanupModeEVirtualSpawnerRoot();
            ClearPendingBossAggroQueue();
        }

        private void ResetModeESharedRuntimeAfterModeF()
        {
            modeEPlayerFaction = Teams.player;
            modeEAliveEnemies.Clear();
            modeEAliveEnemySet.Clear();
            modeEFactionAliveMap.Clear();
            modeEFactionDeathCount.Clear();
            modeEScalingModifiers.Clear();
            modeEEnemyDeathHandlers.Clear();
            modeEEnemyLootHandlers.Clear();
            modeEPendingScalingFactions.Clear();
            modeEScalingBatchTimer = 0f;
            modeESpawnerRootRegisteredEnemies.Clear();
            modeEIntegrityTimer = 0f;
            modeESpawnAllocation = null;
            CleanupModeEVirtualSpawnerRoot();
            ClearPendingBossAggroQueue();
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

            modeEEnemyDeathHandlers.Remove(boss);
            modeEEnemyLootHandlers.Remove(boss);
            modeEScalingModifiers.Remove(boss);
            modeEPendingAggroTraceDistance.Remove(boss);
            UnregisterModeEEnemyFromSpawnerRoot(boss);
            UnregisterEnemyRecovery(boss);
            modeEAliveEnemySet.Remove(boss);
            RemoveModeFCharacterReference(modeEAliveEnemies, boss);

            if (faction.HasValue)
            {
                RemoveFromFactionAliveList(faction.Value, boss);
            }
            else
            {
                foreach (var kvp in modeEFactionAliveMap)
                {
                    RemoveFromFactionAliveList(kvp.Key, boss);
                }
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

                bool isNewBoss = !modeFState.ActiveBosses.Contains(boss);
                if (isNewBoss)
                {
                    modeFState.ActiveBosses.Add(boss);
                }

                ApplyModeFPressureToBoss(boss);

                Health health = boss.Health;
                if (health != null)
                {
                    UnregisterModeFBossDeath(boss);

                    CharacterMainControl capturedBoss = boss;
                    UnityAction<DamageInfo> handler = null;
                    handler = (damageInfo) =>
                    {
                        UnregisterModeFBossDeath(capturedBoss);
                        OnModeFBossDied(capturedBoss, damageInfo);
                    };

                    modeFBossDeathHandlers[boss] = handler;
                    health.OnDeadEvent.AddListener(handler);
                }

                DevLog("[ModeF] Boss registered: " + boss.gameObject.name + " (total=" + modeFState.ActiveBosses.Count + ")");
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] RegisterModeFBoss failed: " + e.Message);
            }
        }

        private void OnModeFBossDied(CharacterMainControl deadBoss, DamageInfo damageInfo)
        {
            try
            {
                if (!modeFActive || deadBoss == null)
                {
                    return;
                }

                // C1 guard: 防止死亡事件重复触发（IntegrityCheck 移除后再次触发）
                if (!RemoveModeFBossReference(deadBoss))
                {
                    return;
                }

                Teams? deadBossTeam = null;
                if (!(deadBoss == null))
                {
                    try { deadBossTeam = deadBoss.Team; } catch { }
                }

                CleanupModeESharedRuntimeForModeFBoss(deadBoss, deadBossTeam);
                modeFBossModifiers.Remove(deadBoss);
                modeFBossForcedTargets.Remove(deadBoss);
                ApplyModeFBossMoveSpeedModifier(deadBoss, 0f);

                CharacterMainControl killer = null;
                try { killer = damageInfo.fromCharacter; } catch { }

                int deadBossId = deadBoss.GetInstanceID();
                int deadBossMarks = 0;
                modeFState.BountyMarksByCharacterId.TryGetValue(deadBossId, out deadBossMarks);

                bool killedByPlayer = killer == CharacterMainControl.Main;
                bool killedByTrackedBoss = IsTrackedModeFBoss(killer);

                DevLog("[ModeF] Boss died: " + deadBoss.gameObject.name
                    + " (marks=" + deadBossMarks
                    + ", killedByPlayer=" + killedByPlayer
                    + ", killer=" + (killer != null ? killer.gameObject.name : "null") + ")");

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
                        AddBountyBossExtraLoot(deadBoss, deadBossMarks);
                    }
                }
                else
                {
                    modeFState.BountyMarksByCharacterId.Remove(deadBossId);
                    CheckAndBroadcastLeaderChange();
                    DevLog("[ModeF] Boss died to the environment or a non-ModeF actor, bounty marks were discarded.");
                }

                RespawnModeFBoss();
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] OnModeFBossDied failed: " + e.Message);
            }
        }

        private void RespawnModeFBoss()
        {
            try
            {
                if (!modeFActive)
                {
                    return;
                }

                Vector3 spawnPos = FindSpawnPointAwayFromPlayer(50f);
                EnemyPresetInfo preset = GetRandomBossPreset();
                if (preset == null)
                {
                    DevLog("[ModeF] [WARNING] RespawnModeFBoss: no boss preset is available.");
                    return;
                }

                int modeFSessionToken = modeFState.RuntimeSessionToken;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

                SpawnEnemyCore(
                    preset,
                    spawnPos,
                    true,
                    () => IsModeFSessionStillValid(modeFSessionToken, relatedScene),
                    (ctx) =>
                    {
                        try
                        {
                            if (ctx.character == null)
                            {
                                return;
                            }

                            if (ctx.character.characterPreset != null)
                            {
                                CharacterRandomPreset customPreset = UnityEngine.Object.Instantiate(ctx.character.characterPreset);
                                customPreset.aiCombatFactor = 1f;
                                customPreset.showName = true;
                                customPreset.showHealthBar = true;
                                ctx.character.characterPreset = customPreset;
                            }

                            ctx.character.SetTeam((Teams)preset.team);
                            ctx.character.gameObject.name = "ModeF_" + ((Teams)preset.team) + "_" + preset.displayName;
                            RegisterModeESharedRuntimeForModeFBoss(ctx.character, ctx.position);
                            RegisterModeFBoss(ctx.character);
                        }
                        catch (Exception e)
                        {
                            DevLog("[ModeF] [WARNING] Failed to configure respawned boss: " + e.Message);
                        }
                    },
                    () => DevLog("[ModeF] [WARNING] Failed to spawn replacement boss."),
                    1
                );

                DevLog("[ModeF] Replacement boss is spawning at: " + spawnPos);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] RespawnModeFBoss failed: " + e.Message);
            }
        }

        private Vector3 FindSpawnPointAwayFromPlayer(float minDistance)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;

                if (modeESpawnAllocation != null)
                {
                    Vector3 bestPoint = Vector3.zero;
                    float bestDistance = 0f;
                    reusableSpawnCandidates.Clear();

                    foreach (var kvp in modeESpawnAllocation)
                    {
                        if (kvp.Value == null)
                        {
                            continue;
                        }

                        for (int i = 0; i < kvp.Value.Count; i++)
                        {
                            Vector3 point = kvp.Value[i];
                            float distance = Vector3.Distance(point, playerPos);
                            if (distance >= minDistance)
                            {
                                reusableSpawnCandidates.Add(point);
                            }

                            if (distance > bestDistance)
                            {
                                bestDistance = distance;
                                bestPoint = point;
                            }
                        }
                    }

                    if (reusableSpawnCandidates.Count > 0)
                    {
                        return reusableSpawnCandidates[UnityEngine.Random.Range(0, reusableSpawnCandidates.Count)];
                    }

                    if (bestDistance > 0f)
                    {
                        DevLog("[ModeF] [WARNING] No spawn point was found beyond " + minDistance + "m, using the farthest point instead (" + bestDistance.ToString("F0") + "m).");
                        return bestPoint;
                    }
                }

                return playerPos + new Vector3(60f, 0f, 60f);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] FindSpawnPointAwayFromPlayer failed: " + e.Message);
                return Vector3.zero;
            }
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
                        int bossId = 0;
                        bool hasBossId = false;

                        try
                        {
                            if (!(boss == null))
                            {
                                try { bossTeam = boss.Team; } catch { }
                                try
                                {
                                    bossId = boss.GetInstanceID();
                                    hasBossId = true;
                                }
                                catch { }

                                modeFBossModifiers.Remove(boss);
                                modeFBossForcedTargets.Remove(boss);
                                ApplyModeFBossMoveSpeedModifier(boss, 0f);
                            }
                        }
                        catch { }

                        modeFState.ActiveBosses.RemoveAt(i);
                        CleanupModeESharedRuntimeForModeFBoss(boss, bossTeam);
                        UnregisterModeFBossDeath(boss);
                        if (hasBossId)
                        {
                            modeFState.BountyMarksByCharacterId.Remove(bossId);
                        }

                        RespawnModeFBoss();
                    }
                }

                CheckAndBroadcastLeaderChange();
            }
            catch { }
        }

        #endregion
    }
}
