using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F Boss Registration And Respawn

        /// <summary>已注册死亡事件的 Boss InstanceID，防止重复 AddListener</summary>
        private readonly HashSet<int> modeFBossDeathRegistered = new HashSet<int>();

        /// <summary>FindSpawnPointAwayFromPlayer 候选点缓存，避免每次 new List</summary>
        private static readonly List<Vector3> reusableSpawnCandidates = new List<Vector3>();

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
                int bossId = boss.GetInstanceID();
                if (health != null && !modeFBossDeathRegistered.Contains(bossId))
                {
                    modeFBossDeathRegistered.Add(bossId);
                    health.OnDeadEvent.AddListener((damageInfo) => OnModeFBossDied(boss, damageInfo));
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
                if (!modeFState.ActiveBosses.Contains(deadBoss))
                {
                    return;
                }

                modeFState.ActiveBosses.Remove(deadBoss);
                modeFBossModifiers.Remove(deadBoss);
                ApplyModeFBossMoveSpeedModifier(deadBoss, 0f);

                CharacterMainControl killer = null;
                try { killer = damageInfo.fromCharacter; } catch { }

                int deadBossId = deadBoss.GetInstanceID();
                int deadBossMarks = 0;
                modeFState.BountyMarksByCharacterId.TryGetValue(deadBossId, out deadBossMarks);

                bool killedByPlayer = killer == CharacterMainControl.Main;

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
                else if (killer != null)
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
                    DevLog("[ModeF] Boss died to the environment, bounty marks were discarded.");
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

                SpawnEnemyCore(
                    preset,
                    spawnPos,
                    true,
                    () => modeFActive,
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
                            RegisterModeFBoss(ctx.character);

                            modeEAliveEnemies.Add(ctx.character);
                            AddToFactionAliveList(ctx.character.Team, ctx.character);
                            RegisterEnemyRecoveryAnchor(ctx.character, ctx.position);
                            RegisterModeEEnemyToSpawnerRoot(ctx.character);
                            RegisterModeEEnemyDeath(ctx.character);
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
                        try
                        {
                            if (boss != null)
                            {
                                modeFBossModifiers.Remove(boss);
                            }
                        }
                        catch { }

                        modeFState.ActiveBosses.RemoveAt(i);
                    }
                }
            }
            catch { }
        }

        #endregion
    }
}
