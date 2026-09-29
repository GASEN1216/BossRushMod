// ============================================================================
// SpawnPositionHelper.cs - 共享刷怪点几何工具
// ============================================================================
// 模块说明：
//   统一所有模式的刷怪点 Y 轴修正、玩家最小距离过滤、玩家附近回退环。
//   取代 WavesArena 内的私有 GetSafeBossSpawnPosition / FindNearestSafeSpawnPoint
//   以及旧的 SpawnPointGeometryHelper（NavMesh 优先 + 无 groundLayerMask）。
//
//   设计要点（来自 WavesArena 现有注释）：
//     - 默认 Raycast 优先（带 groundLayerMask），NavMesh 兜底，再 +0.5m 兜底。
//       目的：避免 NavMesh.SamplePosition 把敌人吸到非预设点（屋顶、楼梯下、
//       墙体内的 NavMesh）。
//     - 仅修正 Y 轴，保持 raw 的 XZ 不变。
//     - 玩家最小距离判断只算 XZ（忽略 Y）。
//     - 玩家附近回退环（virtual point）是不同语义：raw 是几何构造点而非预设点，
//       此时用 NavMesh 优先，并接受 NavMesh 给出的完整 XYZ。
// ============================================================================

using System.Collections.Generic;
using Pathfinding;
using UnityEngine;
using UnityEngine.AI;

namespace BossRush
{
    internal static class SpawnPositionHelper
    {
        internal const float DefaultLiftOffset = 0.15f;
        /// <summary>锚点（通常是玩家）吸附到导航面的最大水平距离。</summary>
        internal const float ReachableAnchorSnapDistance = 3f;
        /// <summary>吸附前后允许的最大高度差；超过视为另一层楼 / 屋顶。</summary>
        internal const float ReachableVerticalTolerance = 2f;

        /// <summary>
        /// 把 raw 吸附到可走导航面，并证明它与 anchor（通常是玩家）在同一连通区。
        /// 官方 AI 走 A* Pathfinding（AstarPath），官方关卡不烘焙 Unity NavMesh：
        /// 只查 NavMesh 会把所有官方刷怪点判为不可达（丧尸模式 Demo 地图收集为 0 的根因）。
        /// 因此 A* 图优先，用 PathUtilities.IsPathPossible 判连通（不发起寻路、不改写图）；
        /// 场景没有 A* 时才退到 Unity NavMesh 完整路径；两者都没有判不可达，不用 Raycast 冒充可达。
        /// 成功时采纳导航面的完整 XYZ 并抬高 liftOffset。
        /// </summary>
        internal static bool TryResolveReachableFrom(
            Vector3 rawPosition,
            Vector3 anchorPosition,
            float snapDistance,
            float liftOffset,
            out Vector3 resolved,
            NavMeshPath navMeshPathBuffer = null)
        {
            resolved = Vector3.zero;
            try
            {
                AstarPath astar = AstarPath.active;
                if (astar != null)
                {
                    if (astar.isScanning)
                    {
                        return false;
                    }

                    NNInfo target = astar.GetNearest(rawPosition, NNConstraint.Walkable);
                    NNInfo anchor = astar.GetNearest(anchorPosition, NNConstraint.Walkable);
                    if (!IsNearWalkableNode(target, rawPosition, snapDistance) ||
                        !IsNearWalkableNode(anchor, anchorPosition, ReachableAnchorSnapDistance) ||
                        !PathUtilities.IsPathPossible(target.node, anchor.node))
                    {
                        return false;
                    }

                    resolved = target.position + Vector3.up * liftOffset;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            try
            {
                NavMeshHit targetHit;
                NavMeshHit anchorHit;
                NavMeshPath path = navMeshPathBuffer ?? new NavMeshPath();
                if (!NavMesh.SamplePosition(rawPosition, out targetHit, snapDistance, NavMesh.AllAreas) ||
                    !NavMesh.SamplePosition(anchorPosition, out anchorHit, ReachableAnchorSnapDistance, NavMesh.AllAreas) ||
                    Mathf.Abs(targetHit.position.y - rawPosition.y) > ReachableVerticalTolerance ||
                    Mathf.Abs(anchorHit.position.y - anchorPosition.y) > ReachableVerticalTolerance ||
                    !NavMesh.CalculatePath(targetHit.position, anchorHit.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                {
                    return false;
                }

                resolved = targetHit.position + Vector3.up * liftOffset;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsNearWalkableNode(NNInfo info, Vector3 rawPosition, float maxHorizontalDistance)
        {
            if (info.node == null || !info.node.Walkable)
            {
                return false;
            }

            Vector3 delta = info.position - rawPosition;
            if (Mathf.Abs(delta.y) > ReachableVerticalTolerance)
            {
                return false;
            }

            delta.y = 0f;
            return delta.sqrMagnitude <= maxHorizontalDistance * maxHorizontalDistance;
        }
        internal const float DefaultNavMeshSampleRadius = 5f;
        internal const float DefaultRaycastMaxDistance = 5f;
        internal const float DefaultRaycastOriginHeight = 1f;
        internal const float DefaultSafeDistance = 15f;

        /// <summary>
        /// 修正 raw 位置的 Y 轴：Raycast(groundLayerMask) 优先 → NavMesh 兜底 → +0.5m 兜底。
        /// 总是返回一个可用位置；用于已知大致正确的预设点（如关卡刷怪点、撤离点）。
        /// </summary>
        internal static Vector3 SnapToGround(
            Vector3 rawPosition,
            float liftOffset = DefaultLiftOffset,
            float navMeshSampleRadius = DefaultNavMeshSampleRadius)
        {
            Vector3 resolved;
            if (TryRaycastSnapPreserveXZ(rawPosition, liftOffset, out resolved))
            {
                return resolved;
            }
            if (TryNavMeshSnapPreserveXZ(rawPosition, navMeshSampleRadius, liftOffset, out resolved))
            {
                return resolved;
            }
            return rawPosition + Vector3.up * 0.5f;
        }

        /// <summary>
        /// 严格版本的 SnapToGround：Raycast / NavMesh 都失败时返回 false（不做 +0.5m 兜底）。
        /// 用于"无地面则丢弃"场景，如丧尸候选刷怪点收集。
        /// </summary>
        internal static bool TrySnapToGround(
            Vector3 rawPosition,
            out Vector3 resolved,
            float liftOffset = DefaultLiftOffset,
            float navMeshSampleRadius = DefaultNavMeshSampleRadius)
        {
            if (TryRaycastSnapPreserveXZ(rawPosition, liftOffset, out resolved))
            {
                return true;
            }
            return TryNavMeshSnapPreserveXZ(rawPosition, navMeshSampleRadius, liftOffset, out resolved);
        }

        /// <summary>
        /// NavMesh.SamplePosition 优先（采纳 NavMesh 的完整 XYZ），Raycast 兜底（仅 Y）。
        /// 用于"在 raw 周围找到一个 NavMesh 可达点"场景，如玩家附近虚拟回退环。
        /// </summary>
        internal static bool TrySampleNavMesh(
            Vector3 rawPosition,
            out Vector3 resolved,
            float liftOffset = DefaultLiftOffset,
            float navMeshSampleRadius = DefaultNavMeshSampleRadius)
        {
            resolved = rawPosition;
            try
            {
                NavMeshHit navHit;
                if (NavMesh.SamplePosition(rawPosition, out navHit, navMeshSampleRadius, NavMesh.AllAreas))
                {
                    resolved = navHit.position + Vector3.up * liftOffset;
                    return true;
                }
            }
            catch { }
            return TryRaycastSnapPreserveXZ(rawPosition, liftOffset, out resolved);
        }

        /// <summary>
        /// XZ 距离判断（忽略 Y）。当前主角不存在或 minPlayerDistance &lt;=0 时视为通过。
        /// </summary>
        internal static bool PassesMinPlayerDistance(Vector3 point, float minPlayerDistance)
        {
            if (minPlayerDistance <= 0f)
            {
                return true;
            }
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return true;
            }
            Vector3 delta = point - player.transform.position;
            delta.y = 0f;
            return delta.sqrMagnitude >= minPlayerDistance * minPlayerDistance;
        }

        /// <summary>
        /// 从候选点中取最近但不在 minPlayerDistance 内的点；都在内则取最远。所选点经过 SnapToGround 修正。
        /// </summary>
        internal static Vector3 FindNearestSafeSpawnPoint(
            Vector3[] spawnPoints,
            Vector3 playerPos,
            float minPlayerDistance = DefaultSafeDistance,
            float liftOffset = DefaultLiftOffset)
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return SnapToGround(playerPos + new Vector3(minPlayerDistance, 0f, minPlayerDistance), liftOffset);
            }

            float minDistanceSqr = minPlayerDistance * minPlayerDistance;
            Vector3 bestSafe = Vector3.zero;
            float bestSafeDistSqr = float.MaxValue;
            bool foundSafe = false;

            Vector3 farthest = spawnPoints[0];
            float farthestDistSqr = 0f;

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                float distSqr = GetXZDistanceSqr(spawnPoints[i], playerPos);
                if (distSqr > farthestDistSqr)
                {
                    farthestDistSqr = distSqr;
                    farthest = spawnPoints[i];
                }
                if (distSqr >= minDistanceSqr && distSqr < bestSafeDistSqr)
                {
                    bestSafeDistSqr = distSqr;
                    bestSafe = spawnPoints[i];
                    foundSafe = true;
                }
            }

            return SnapToGround(foundSafe ? bestSafe : farthest, liftOffset);
        }

        /// <summary>
        /// FindNearestSafeSpawnPoint 的 List 版本。
        /// </summary>
        internal static Vector3 FindNearestSafeSpawnPoint(
            List<Vector3> spawnPoints,
            Vector3 playerPos,
            float minPlayerDistance = DefaultSafeDistance,
            float liftOffset = DefaultLiftOffset)
        {
            if (spawnPoints == null || spawnPoints.Count == 0)
            {
                return SnapToGround(playerPos + new Vector3(minPlayerDistance, 0f, minPlayerDistance), liftOffset);
            }

            float minDistanceSqr = minPlayerDistance * minPlayerDistance;
            Vector3 bestSafe = Vector3.zero;
            float bestSafeDistSqr = float.MaxValue;
            bool foundSafe = false;

            Vector3 farthest = spawnPoints[0];
            float farthestDistSqr = 0f;

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                float distSqr = GetXZDistanceSqr(spawnPoints[i], playerPos);
                if (distSqr > farthestDistSqr)
                {
                    farthestDistSqr = distSqr;
                    farthest = spawnPoints[i];
                }
                if (distSqr >= minDistanceSqr && distSqr < bestSafeDistSqr)
                {
                    bestSafeDistSqr = distSqr;
                    bestSafe = spawnPoints[i];
                    foundSafe = true;
                }
            }

            return SnapToGround(foundSafe ? bestSafe : farthest, liftOffset);
        }

        /// <summary>
        /// 从刷怪点数组中选取多个不在安全距离内的点，按距玩家由近到远排序
        /// <para>用于多Boss同波生成时分配不重复的安全刷怪位置</para>
        /// </summary>
        internal static List<Vector3> FindMultipleSafeSpawnPoints(int count, Vector3[] spawnPoints, Vector3 playerPos, float minPlayerDistance)
        {
            var result = new List<Vector3>(count);
            if (spawnPoints == null || spawnPoints.Length == 0 || count <= 0)
            {
                return result;
            }

            // 收集所有安全距离外的点，按距玩家由近到远排序
            var safeCandidates = new List<(int idx, Vector3 pos, float distSqr)>();
            var allByDistance = new List<(int idx, Vector3 pos, float distSqr)>(spawnPoints.Length);

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                float distSqr = (spawnPoints[i] - playerPos).sqrMagnitude;
                allByDistance.Add((i, spawnPoints[i], distSqr));
                if (distSqr >= minPlayerDistance * minPlayerDistance)
                {
                    safeCandidates.Add((i, spawnPoints[i], distSqr));
                }
            }

            // 按距离由近到远排序
            safeCandidates.Sort((a, b) => a.distSqr.CompareTo(b.distSqr));

            var usedIndices = new HashSet<int>();

            // 优先使用安全距离外的点
            for (int i = 0; i < safeCandidates.Count && result.Count < count; i++)
            {
                result.Add(SnapToGround(safeCandidates[i].pos));
                usedIndices.Add(safeCandidates[i].idx);
            }

            // 不够的话，从未使用的点中按距离由远到近补充
            if (result.Count < count)
            {
                allByDistance.Sort((a, b) => b.distSqr.CompareTo(a.distSqr));
                for (int i = 0; i < allByDistance.Count && result.Count < count; i++)
                {
                    if (!usedIndices.Contains(allByDistance[i].idx))
                    {
                        result.Add(SnapToGround(allByDistance[i].pos));
                        usedIndices.Add(allByDistance[i].idx);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 从刷怪点列表中选取多个不在安全距离内的点（List版本）
        /// </summary>
        internal static List<Vector3> FindMultipleSafeSpawnPoints(int count, List<Vector3> spawnPoints, Vector3 playerPos, float minPlayerDistance)
        {
            var result = new List<Vector3>(count);
            if (spawnPoints == null || spawnPoints.Count == 0 || count <= 0)
            {
                return result;
            }

            var safeCandidates = new List<(int idx, Vector3 pos, float distSqr)>();
            var allByDistance = new List<(int idx, Vector3 pos, float distSqr)>(spawnPoints.Count);

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                float distSqr = (spawnPoints[i] - playerPos).sqrMagnitude;
                allByDistance.Add((i, spawnPoints[i], distSqr));
                if (distSqr >= minPlayerDistance * minPlayerDistance)
                {
                    safeCandidates.Add((i, spawnPoints[i], distSqr));
                }
            }

            safeCandidates.Sort((a, b) => a.distSqr.CompareTo(b.distSqr));

            var usedIndices = new HashSet<int>();

            for (int i = 0; i < safeCandidates.Count && result.Count < count; i++)
            {
                result.Add(SnapToGround(safeCandidates[i].pos));
                usedIndices.Add(safeCandidates[i].idx);
            }

            if (result.Count < count)
            {
                allByDistance.Sort((a, b) => b.distSqr.CompareTo(a.distSqr));
                for (int i = 0; i < allByDistance.Count && result.Count < count; i++)
                {
                    if (!usedIndices.Contains(allByDistance[i].idx))
                    {
                        result.Add(SnapToGround(allByDistance[i].pos));
                        usedIndices.Add(allByDistance[i].idx);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 在 player 周围生成 ringCount 个等距候选点；首个能 NavMesh 落地 + 通过 minPlayerDistance 的点即返回。
        /// 用于场景没有任何预设刷怪点时的兜底。
        /// </summary>
        internal static bool TryFindAroundPlayer(
            Vector3 playerPos,
            int ringCount,
            float radius,
            out Vector3 resolved,
            float liftOffset = DefaultLiftOffset,
            float minPlayerDistance = 0f,
            float navMeshSampleRadius = DefaultNavMeshSampleRadius,
            int startIndex = 0)
        {
            resolved = playerPos;
            if (ringCount <= 0 || radius <= 0f)
            {
                return false;
            }

            int normalizedStartIndex = startIndex % ringCount;
            if (normalizedStartIndex < 0)
            {
                normalizedStartIndex += ringCount;
            }

            for (int i = 0; i < ringCount; i++)
            {
                int candidateIndex = (normalizedStartIndex + i) % ringCount;
                float angle = 360f * candidateIndex / ringCount;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                Vector3 candidate = playerPos + offset;
                Vector3 sampled;
                if (!TrySampleNavMesh(candidate, out sampled, liftOffset, navMeshSampleRadius))
                {
                    continue;
                }
                if (!PassesMinPlayerDistance(sampled, minPlayerDistance))
                {
                    continue;
                }
                resolved = sampled;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 在 player 周围生成 count 个等距候选点（半径 [minRadius, maxRadius] 随机），
        /// 每点 SnapToGround 修正 Y 后返回。失败的点仍保留 raw（不丢点），便于
        /// EnemyRecovery 等系统在没有任何预设点时拿到完整候选数组。
        /// </summary>
        internal static Vector3[] BuildRingPoints(
            Vector3 playerPos,
            int count,
            float minRadius,
            float maxRadius,
            float liftOffset = DefaultLiftOffset)
        {
            if (count <= 0)
            {
                return new Vector3[0];
            }

            Vector3[] points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = (360f / count) * i;
                float radius = Random.Range(minRadius, maxRadius);
                float rad = angle * Mathf.Deg2Rad;
                Vector3 raw = new Vector3(
                    playerPos.x + Mathf.Cos(rad) * radius,
                    playerPos.y,
                    playerPos.z + Mathf.Sin(rad) * radius);
                points[i] = SnapToGround(raw, liftOffset);
            }
            return points;
        }

        private static bool TryRaycastSnapPreserveXZ(Vector3 rawPosition, float liftOffset, out Vector3 resolved)
        {
            resolved = rawPosition;
            try
            {
                LayerMask groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                Vector3 origin = rawPosition + Vector3.up * DefaultRaycastOriginHeight;
                RaycastHit hit;
                if (Physics.Raycast(origin, Vector3.down, out hit, DefaultRaycastMaxDistance, groundMask))
                {
                    resolved = new Vector3(rawPosition.x, hit.point.y + liftOffset, rawPosition.z);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static float GetXZDistanceSqr(Vector3 a, Vector3 b)
        {
            Vector3 delta = a - b;
            delta.y = 0f;
            return delta.sqrMagnitude;
        }

        private static bool TryNavMeshSnapPreserveXZ(Vector3 rawPosition, float sampleRadius, float liftOffset, out Vector3 resolved)
        {
            resolved = rawPosition;
            try
            {
                NavMeshHit navHit;
                if (NavMesh.SamplePosition(rawPosition, out navHit, sampleRadius, NavMesh.AllAreas))
                {
                    resolved = new Vector3(rawPosition.x, navHit.position.y + liftOffset, rawPosition.z);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
