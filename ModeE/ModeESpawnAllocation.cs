// ============================================================================
// ModeESpawnAllocation.cs - 刷怪点扫描与阵营分配算法
// ============================================================================
// 模块说明：
//   负责将刷怪点平均分配给所有参战阵营，
//   以及将玩家传送到其阵营的刷怪区域或独狼安全位置。
//
// 刷怪点来源优先级：
//   1. BossRushMapConfig.modeESpawnPoints（自定义 Mode E 专用刷怪点）
//   2. 原地图 CharacterSpawnerRoot 的 Points 组件（兜底）
//   3. 基于玩家位置生成的备用点（最终兜底）
//
// 分配算法：
//   1. 获取刷怪点
//   2. 按距离玩家由近到远排序
//   3. 间隔过滤（每个点与已选点距离 >= 10m）
//   4. 轮询分配：spawnPoints[i] → factions[i % factionCount]
//   5. 余数刷怪点依次分配，确保差异不超过 1
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode E 刷怪点分配模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode E 刷怪点分配数据

        /// <summary>阵营 → 刷怪点列表的映射</summary>
        private Dictionary<Teams, List<Vector3>> modeESpawnAllocation;

        /// <summary>缓存的原地图刷怪点位置（在 DisableAllSpawners 销毁 spawner 之前扫描并缓存）</summary>
        private Vector3[] modeECachedSpawnerPositions;

        #endregion

        #region Mode E 刷怪点分配方法

        /// <summary>刷怪点之间的最小间隔距离（米）</summary>
        private const float MODE_E_SPAWN_MIN_DISTANCE = 10f;

        /// <summary>
        /// 将刷怪点按间隔过滤后，循环依次分配给各阵营
        /// 优先使用 BossRushMapConfig.modeESpawnPoints 自定义刷怪点，
        /// 无自定义配置时兜底使用原地图 CharacterSpawnerRoot 位置
        /// 玩家阵营优先分配距离玩家最近的刷怪点
        /// 特殊：爷的营旗（player阵营）不参与分配，所有刷怪点分给5个NPC阵营
        /// </summary>
        private void AllocateSpawnPoints()
        {
            try
            {
                DevLog("[ModeE] 开始分配刷怪点（轮询模式）...");

                // 初始化分配映射（始终为5个NPC阵营分配）
                modeESpawnAllocation = new Dictionary<Teams, List<Vector3>>();
                for (int i = 0; i < ModeEAvailableFactions.Length; i++)
                {
                    modeESpawnAllocation[ModeEAvailableFactions[i]] = new List<Vector3>();
                }

                // 优先使用当前地图配置的 Mode E 专用刷怪点
                Vector3[] spawnPoints = null;
                BossRushMapConfig mapConfig = GetCurrentMapConfig();
                if (mapConfig != null && mapConfig.modeESpawnPoints != null && mapConfig.modeESpawnPoints.Length > 0)
                {
                    spawnPoints = mapConfig.modeESpawnPoints;
                    DevLog("[ModeE] 使用地图配置的 Mode E 专用刷怪点，数量: " + spawnPoints.Length);
                }

                // 兜底：扫描原地图的 CharacterSpawnerRoot 位置
                if (spawnPoints == null || spawnPoints.Length == 0)
                {
                    spawnPoints = ScanMapSpawnerPositions();
                    DevLog("[ModeE] 无自定义刷怪点配置，兜底使用原地图 spawner 位置");
                }

                // 最终兜底：基于玩家位置生成备用点
                if (spawnPoints == null || spawnPoints.Length == 0)
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null)
                    {
                        spawnPoints = GenerateFallbackSpawnPointsAroundPlayer(player.transform.position);
                        DevLog("[ModeE] 使用玩家位置生成的备用刷怪点");
                    }
                    else
                    {
                        DevLog("[ModeE] [ERROR] 无刷怪点且无法获取玩家位置");
                        return;
                    }
                }

                int factionCount = ModeEAvailableFactions.Length;
                DevLog("[ModeE] 原始刷怪点数量: " + spawnPoints.Length + ", 阵营数: " + factionCount);

                // ========== 第1步：获取玩家位置 ==========
                Vector3 playerPos = Vector3.zero;
                CharacterMainControl playerRef = CharacterMainControl.Main;
                if (playerRef != null) playerPos = playerRef.transform.position;

                // ========== 第2步：按距离玩家由近到远排序所有刷怪点 ==========
                Vector3[] sorted = new Vector3[spawnPoints.Length];
                Array.Copy(spawnPoints, sorted, spawnPoints.Length);
                Array.Sort(sorted, (a, b) =>
                {
                    float distA = Vector3.SqrMagnitude(a - playerPos);
                    float distB = Vector3.SqrMagnitude(b - playerPos);
                    return distA.CompareTo(distB);
                });

                // ========== 第3步：间隔过滤（每个点与已选点距离 >= 10m） ==========
                List<Vector3> filtered = new List<Vector3>();
                for (int i = 0; i < sorted.Length; i++)
                {
                    bool tooClose = false;
                    for (int j = 0; j < filtered.Count; j++)
                    {
                        if (Vector3.Distance(sorted[i], filtered[j]) < MODE_E_SPAWN_MIN_DISTANCE)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (!tooClose)
                    {
                        filtered.Add(sorted[i]);
                    }
                }

                DevLog("[ModeE] 间隔过滤后剩余刷怪点: " + filtered.Count);

                // ========== 第4步：构建阵营分配顺序 ==========
                // 爷的营旗（player阵营）：不参与分配，所有刷怪点均匀分给5个NPC阵营
                // 普通营旗：玩家阵营排第一，优先获得距玩家最近的刷怪点
                bool isPlayerFaction = (modeEPlayerFaction == Teams.player);

                Teams[] orderedFactions;
                if (isPlayerFaction)
                {
                    // 爷的营旗：直接使用原始阵营顺序，不需要优先排序
                    orderedFactions = new Teams[factionCount];
                    Array.Copy(ModeEAvailableFactions, orderedFactions, factionCount);
                    DevLog("[ModeE] 爷的营旗模式：所有刷怪点分配给 " + factionCount + " 个NPC阵营");
                }
                else
                {
                    // 普通营旗：玩家阵营排第一
                    int playerFactionIdx = -1;
                    for (int i = 0; i < ModeEAvailableFactions.Length; i++)
                    {
                        if (ModeEAvailableFactions[i] == modeEPlayerFaction)
                        {
                            playerFactionIdx = i;
                            break;
                        }
                    }

                    orderedFactions = new Teams[factionCount];
                    if (playerFactionIdx >= 0)
                    {
                        orderedFactions[0] = ModeEAvailableFactions[playerFactionIdx];
                        int idx = 1;
                        for (int i = 0; i < factionCount; i++)
                        {
                            if (i != playerFactionIdx)
                            {
                                orderedFactions[idx++] = ModeEAvailableFactions[i];
                            }
                        }
                    }
                    else
                    {
                        Array.Copy(ModeEAvailableFactions, orderedFactions, factionCount);
                    }
                }

                // ========== 第5步：轮询分配（循环依次分配给各阵营） ==========
                // filtered[0] 距玩家最近 → 分配给 orderedFactions[0]（玩家阵营或第一个NPC阵营）
                for (int i = 0; i < filtered.Count; i++)
                {
                    Teams faction = orderedFactions[i % factionCount];
                    modeESpawnAllocation[faction].Add(filtered[i]);
                }

                // 输出分配结果日志
                foreach (var kvp in modeESpawnAllocation)
                {
                    DevLog("[ModeE] 阵营 " + kvp.Key + " 分配 " + kvp.Value.Count + " 个刷怪点");
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] AllocateSpawnPoints 失败: " + e.Message + "\n" + e.StackTrace);
            }
        }



        /// <summary>
        /// 爷的营旗专用：将玩家传送到安全位置
        /// 优先使用 BossRushMapConfig.modeEPlayerSpawnPos 自定义落点，
        /// 无自定义配置时兜底从地图出生点中选出离所有Boss最远的点
        /// </summary>
        private void TeleportPlayerToSafePosition()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    DevLog("[ModeE] [WARNING] TeleportPlayerToSafePosition: 玩家为 null");
                    return;
                }

                Vector3 bestPos = player.transform.position;

                // 优先使用地图配置的 Mode E 独狼玩家落点
                BossRushMapConfig mapConfig = GetCurrentMapConfig();
                if (mapConfig != null && mapConfig.modeEPlayerSpawnPos.HasValue)
                {
                    bestPos = mapConfig.modeEPlayerSpawnPos.Value;
                    DevLog("[ModeE] 使用地图配置的 Mode E 独狼玩家落点: " + bestPos);
                }
                else
                {
                    // 兜底：从地图出生点中选出离所有Boss最远的点
                    DevLog("[ModeE] 无自定义独狼落点配置，兜底使用远离Boss的安全位置");

                    // 收集所有已分配给NPC阵营的刷怪点（即Boss会出现的位置）
                    List<Vector3> bossSpawnPoints = new List<Vector3>();
                    if (modeESpawnAllocation != null)
                    {
                        foreach (var kvp in modeESpawnAllocation)
                        {
                            bossSpawnPoints.AddRange(kvp.Value);
                        }
                    }

                    if (bossSpawnPoints.Count == 0)
                    {
                        DevLog("[ModeE] [WARNING] TeleportPlayerToSafePosition: 无刷怪点数据，跳过传送");
                        return;
                    }

                    float bestMinDist = 0f;

                    if (modeECachedSpawnerPositions != null && modeECachedSpawnerPositions.Length > 0)
                    {
                        for (int i = 0; i < modeECachedSpawnerPositions.Length; i++)
                        {
                            Vector3 candidate = modeECachedSpawnerPositions[i];

                            // 计算该候选点到最近Boss刷怪点的距离
                            float minDist = float.MaxValue;
                            for (int j = 0; j < bossSpawnPoints.Count; j++)
                            {
                                float dist = Vector3.Distance(candidate, bossSpawnPoints[j]);
                                if (dist < minDist) minDist = dist;
                            }

                            // 选"离最近Boss最远"的候选点
                            if (minDist > bestMinDist)
                            {
                                bestMinDist = minDist;
                                bestPos = candidate;
                            }
                        }

                        DevLog("[ModeE] 从 " + modeECachedSpawnerPositions.Length + " 个地图出生点中选出安全位置，距最近Boss " + bestMinDist.ToString("F1") + "m");
                    }
                    else
                    {
                        DevLog("[ModeE] [WARNING] 地图出生点缓存为空，玩家保持原位");
                    }
                }

                // 用 NavMesh 采样微调，确保落点精确在可行走区域上
                UnityEngine.AI.NavMeshHit navHit;
                if (UnityEngine.AI.NavMesh.SamplePosition(bestPos, out navHit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    bestPos = navHit.position;
                }

                // 执行传送
                player.transform.position = bestPos;
                DevLog("[ModeE] 爷的营旗：玩家已传送到安全位置 " + bestPos);

                ShowMessage(L10n.T(
                    "你已被传送到安全区域，准备好迎战所有阵营的Boss吧！",
                    "You've been teleported to a safe zone. Prepare to fight all faction bosses!"
                ));
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] TeleportPlayerToSafePosition 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 获取指定阵营的刷怪点列表
        /// </summary>
        private List<Vector3> GetFactionSpawnPoints(Teams faction)
        {
            if (modeESpawnAllocation != null)
            {
                List<Vector3> points;
                if (modeESpawnAllocation.TryGetValue(faction, out points))
                {
                    return points;
                }
            }
            return new List<Vector3>();
        }



        /// <summary>
        /// 预扫描并缓存原地图实际敌人出生点位置（必须在 DisableAllSpawners 之前调用）
        /// 由 OnSceneLoaded_Integration 在场景加载时立即调用
        /// 
        /// 关键修复：从 RandomCharacterSpawner 的 Points 组件提取实际出生点坐标，
        /// 而非 CharacterSpawnerRoot.transform.position（那只是 spawner 根对象位置，不是敌人出生点）
        /// </summary>
        public void PreCacheMapSpawnerPositions()
        {
            try
            {
                CharacterSpawnerRoot[] spawners = UnityEngine.Object.FindObjectsOfType<CharacterSpawnerRoot>();
                if (spawners == null || spawners.Length == 0)
                {
                    modeECachedSpawnerPositions = null;
                    DevLog("[ModeE] PreCacheMapSpawnerPositions: 未找到任何 CharacterSpawnerRoot");
                    return;
                }

                List<Vector3> positions = new List<Vector3>();
                for (int i = 0; i < spawners.Length; i++)
                {
                    if (spawners[i] == null) continue;

                    // 从 CharacterSpawnerRoot 的子组件获取 Points（实际出生点）
                    // RandomCharacterSpawner 上挂载了 Points 组件，包含所有实际出生点坐标
                    Points pointsComponent = spawners[i].GetComponentInChildren<Points>();
                    if (pointsComponent != null && pointsComponent.points != null && pointsComponent.points.Count > 0)
                    {
                        for (int j = 0; j < pointsComponent.points.Count; j++)
                        {
                            // GetPoint 会将本地坐标转换为世界坐标（如果不是 worldSpace 模式）
                            Vector3 worldPoint = pointsComponent.GetPoint(j);
                            if (worldPoint != Vector3.zero)
                            {
                                positions.Add(worldPoint);
                            }
                        }
                        DevLog("[ModeE] PreCacheMapSpawnerPositions: spawner[" + i + "] 提取了 " + pointsComponent.points.Count + " 个实际出生点");
                    }
                    else
                    {
                        // 兜底：如果没有 Points 组件，使用 spawner 根对象位置
                        if (spawners[i].transform != null)
                        {
                            positions.Add(spawners[i].transform.position);
                            DevLog("[ModeE] PreCacheMapSpawnerPositions: spawner[" + i + "] 无 Points 组件，使用根对象位置兜底");
                        }
                    }
                }

                modeECachedSpawnerPositions = positions.Count > 0 ? positions.ToArray() : null;
                DevLog("[ModeE] PreCacheMapSpawnerPositions: 缓存了 " + positions.Count + " 个实际出生点");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] PreCacheMapSpawnerPositions 失败: " + e.Message);
                modeECachedSpawnerPositions = null;
            }
        }

        /// <summary>
        /// 获取原地图实际出生点位置（优先使用缓存，缓存为空时实时扫描）
        /// </summary>
        private Vector3[] ScanMapSpawnerPositions()
        {
            // 优先使用预缓存的位置（在 DisableAllSpawners 销毁 spawner 之前缓存的）
            if (modeECachedSpawnerPositions != null && modeECachedSpawnerPositions.Length > 0)
            {
                DevLog("[ModeE] ScanMapSpawnerPositions: 使用预缓存的 " + modeECachedSpawnerPositions.Length + " 个出生点");
                return modeECachedSpawnerPositions;
            }

            // 兜底：实时扫描（如果 spawner 尚未被销毁）
            try
            {
                CharacterSpawnerRoot[] spawners = UnityEngine.Object.FindObjectsOfType<CharacterSpawnerRoot>();
                if (spawners == null || spawners.Length == 0)
                {
                    DevLog("[ModeE] ScanMapSpawnerPositions: 未找到任何 CharacterSpawnerRoot（可能已被销毁）");
                    return null;
                }

                List<Vector3> positions = new List<Vector3>();
                for (int i = 0; i < spawners.Length; i++)
                {
                    if (spawners[i] == null) continue;

                    // 从 Points 组件提取实际出生点
                    Points pointsComponent = spawners[i].GetComponentInChildren<Points>();
                    if (pointsComponent != null && pointsComponent.points != null && pointsComponent.points.Count > 0)
                    {
                        for (int j = 0; j < pointsComponent.points.Count; j++)
                        {
                            Vector3 worldPoint = pointsComponent.GetPoint(j);
                            if (worldPoint != Vector3.zero)
                            {
                                positions.Add(worldPoint);
                            }
                        }
                    }
                    else if (spawners[i].transform != null)
                    {
                        // 兜底：使用 spawner 根对象位置
                        positions.Add(spawners[i].transform.position);
                    }
                }

                DevLog("[ModeE] ScanMapSpawnerPositions: 实时扫描到 " + positions.Count + " 个出生点");
                return positions.Count > 0 ? positions.ToArray() : null;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ScanMapSpawnerPositions 失败: " + e.Message);
                return null;
            }
        }

        #endregion
    }
}
