using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ModeDRuntimeModule
    {
        private static readonly List<Vector3> reusableVector3List = new List<Vector3>();

        internal int GetModeDWaveBossCount(int waveIndex, int totalEnemies)
        {
            if (waveIndex <= 5)
            {
                // 第1-5波：全小怪
                return 0;
            }
            else if (waveIndex <= 10)
            {
                // 第6-10波：1个Boss（或按比例）
                return Mathf.Min(1, totalEnemies);
            }
            else if (waveIndex <= 15)
            {
                // 第11-15波：2个Boss（或按比例）
                return Mathf.Min(2, totalEnemies);
            }
            else
            {
                // 第16+波：全Boss
                return totalEnemies;
            }
        }

        internal EnemyPresetInfo GetRandomBossPreset()
        {
            List<EnemyPresetInfo> filteredBossPool = owner.GetFilteredEnemyPresets();
            if (filteredBossPool == null || filteredBossPool.Count == 0)
            {
                ModBehaviour.DevLog("[ModeD] Boss池为空（过滤后）");
                return null;
            }

            // 第6-10波（首次出Boss的波次）：过滤掉强力Boss
            // Mode E 不使用波次系统，直接走完整池
            if (!owner.IsModeEActive && !owner.IsModeFActive && modeDWaveIndex <= 10)
            {
                // 使用复用缓存避免 GC
                presetFilterCache.Clear();
                for (int i = 0; i < filteredBossPool.Count; i++)
                {
                    EnemyPresetInfo boss = filteredBossPool[i];
                    if (boss == null || string.IsNullOrEmpty(boss.name))
                    {
                        continue;
                    }

                    // 排除强力 Boss
                    if (WavesArenaRuntimeModule.EarlyWaveExcludedBosses.Contains(boss.name))
                    {
                        continue;
                    }

                    presetFilterCache.Add(boss);
                }

                if (presetFilterCache.Count > 0)
                {
                    ModBehaviour.DevLog("[ModeD] 前期波次Boss过滤：可用Boss数=" + presetFilterCache.Count + "/" + filteredBossPool.Count);
                    return presetFilterCache[UnityEngine.Random.Range(0, presetFilterCache.Count)];
                }

                // 如果过滤后没有可用Boss，回退到完整池
                ModBehaviour.DevLog("[ModeD] 前期波次过滤后无可用Boss，使用过滤后的完整池");
            }

            return filteredBossPool[UnityEngine.Random.Range(0, filteredBossPool.Count)];
        }

        internal EnemyPresetInfo GetRandomMinionPreset()
        {
            if (modeDMinionPool == null || modeDMinionPool.Count == 0)
            {
                ModBehaviour.DevLog("[ModeD] 小怪池为空，使用Boss池替代");
                return GetRandomBossPreset();
            }

            // 第 1~2 波：只刷血量最低的敌人（拾荒者等最普通的鸭鸭敌人）
            // 同时过滤掉幽灵和"???"名字的敌人
            if (modeDWaveIndex <= 2)
            {
                // 第一步：从原始池过滤掉幽灵和"???"名字的敌人
                presetFilterCache.Clear();
                for (int i = 0; i < modeDMinionPool.Count; i++)
                {
                    EnemyPresetInfo info = modeDMinionPool[i];
                    if (info == null) continue;
                    if (info.name == "Cname_Ghost") continue; // 排除幽灵

                    string name = info.displayName;
                    if (name == null) name = string.Empty;
                    if (name == "???" || name == "？？？") continue; // 排除"???"名字的敌人

                    presetFilterCache.Add(info);
                }

                if (presetFilterCache.Count > 0)
                {
                    // 找出血量最低值
                    float minHealth = float.MaxValue;
                    for (int i = 0; i < presetFilterCache.Count; i++)
                    {
                        if (presetFilterCache[i].baseHealth < minHealth)
                        {
                            minHealth = presetFilterCache[i].baseHealth;
                        }
                    }

                    // 筛选出血量最低的敌人（允许 10% 的误差范围，以包含同级别的敌人）
                    float healthThreshold = minHealth * 1.1f;
                    presetFilterCache2.Clear();
                    for (int i = 0; i < presetFilterCache.Count; i++)
                    {
                        if (presetFilterCache[i].baseHealth <= healthThreshold)
                        {
                            presetFilterCache2.Add(presetFilterCache[i]);
                        }
                    }

                    if (presetFilterCache2.Count > 0)
                    {
                        ModBehaviour.DevLog("[ModeD] 前两波限制：只刷血量最低的敌人，可选数量=" + presetFilterCache2.Count + ", 血量阈值=" + healthThreshold);
                        return presetFilterCache2[UnityEngine.Random.Range(0, presetFilterCache2.Count)];
                    }
                    else
                    {
                        // 没有符合血量条件的，使用过滤后的池
                        return presetFilterCache[UnityEngine.Random.Range(0, presetFilterCache.Count)];
                    }
                }
                // 过滤后为空，使用原始池
                return modeDMinionPool[UnityEngine.Random.Range(0, modeDMinionPool.Count)];
            }
            // 第 3~5 波：过滤掉幽灵和"???"名字的敌人，但不限制血量
            else if (modeDWaveIndex <= 5)
            {
                presetFilterCache.Clear();
                for (int i = 0; i < modeDMinionPool.Count; i++)
                {
                    EnemyPresetInfo info = modeDMinionPool[i];
                    if (info == null) continue;
                    if (info.name == "Cname_Ghost") continue; // 排除幽灵

                    string name = info.displayName;
                    if (name == null) name = string.Empty;
                    if (name == "???" || name == "？？？") continue; // 过滤掉"???"名字的小怪

                    presetFilterCache.Add(info);
                }

                if (presetFilterCache.Count > 0)
                {
                    return presetFilterCache[UnityEngine.Random.Range(0, presetFilterCache.Count)];
                }
                // 过滤后为空，使用原始池
                return modeDMinionPool[UnityEngine.Random.Range(0, modeDMinionPool.Count)];
            }
            // 第 6~10 波：只过滤掉幽灵
            else if (modeDWaveIndex <= 10)
            {
                presetFilterCache.Clear();
                for (int i = 0; i < modeDMinionPool.Count; i++)
                {
                    EnemyPresetInfo info = modeDMinionPool[i];
                    if (info == null) continue;
                    if (info.name == "Cname_Ghost") continue; // 排除幽灵
                    presetFilterCache.Add(info);
                }
                if (presetFilterCache.Count > 0)
                {
                    return presetFilterCache[UnityEngine.Random.Range(0, presetFilterCache.Count)];
                }
                // 过滤后为空，使用原始池
                return modeDMinionPool[UnityEngine.Random.Range(0, modeDMinionPool.Count)];
            }

            // 第 11+ 波：使用完整池
            return modeDMinionPool[UnityEngine.Random.Range(0, modeDMinionPool.Count)];
        }

        internal List<Vector3> ShuffleSpawnPoints(Vector3[] spawnPoints)
        {
            reusableVector3List.Clear();
            reusableVector3List.AddRange(spawnPoints);
            for (int i = reusableVector3List.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                Vector3 temp = reusableVector3List[i];
                reusableVector3List[i] = reusableVector3List[j];
                reusableVector3List[j] = temp;
            }
            return reusableVector3List;
        }

        internal Vector3[] GenerateFallbackSpawnPointsAroundPlayer(Vector3 playerPos, int pointCount = 10, float minRadius = 8f, float maxRadius = 15f)
        {
            return SpawnPositionHelper.BuildRingPoints(playerPos, pointCount, minRadius, maxRadius);
        }
    }
}
