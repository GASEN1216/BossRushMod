using System.Collections.Generic;
using Duckov.Utilities;
using Pathfinding;
using UnityEngine;
using UnityEngine.AI;

namespace BossRush
{
    /// <summary>
    /// 野外趣事落点的「玩家真走得到」判据（2026-10-25）。只看导航连通不够：
    /// - 官方导航图常越过地图边的空气墙铺到外面，墙外的点与场内同属一个连通区，签收台曾因此刷到地图外；
    /// - A* 报完成不等于走到了：目标不可达时会停在出发侧最近点照报完成。
    /// 因此真算一条路，要求终点确实落在目标上、绕路不离谱，再沿路每一段用玩家 ECM2 的碰撞层扫一遍，
    /// 挡住玩家的墙（包括导航图没烘进去的空气墙）都会让这个落点作废。只在生成落点时调用，不进每帧。
    /// </summary>
    internal static class FieldAdventureRoute
    {
        private static readonly object ClaimOwner = new object();
        private const float ArrivalTolerance = 1.2f;
        private const float ArrivalHeightTolerance = 1.5f;
        private const float DetourFactor = 3f;
        private const float DetourSlack = 15f;
        // 扫掠用细胶囊：空气墙是薄面，细胶囊照样扫得到；用玩家原半径会在 A* 贴墙拐角处擦边误判。
        private const float SweepRadius = 0.15f;

        internal static bool IsWalkable(Vector3 from, Vector3 to)
        {
            try
            {
                AstarPath astar = AstarPath.active;
                if (astar != null) return !astar.isScanning && IsAstarRouteWalkable(from, to);

                // 场景没有 A* 时与 SpawnPositionHelper 同口径退到 Unity NavMesh 完整路径。
                NavMeshPath navPath = new NavMeshPath();
                if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, navPath) || navPath.status != NavMeshPathStatus.PathComplete)
                    return false;
                return IsRouteAcceptable(new List<Vector3>(navPath.corners), from, to);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAstarRouteWalkable(Vector3 from, Vector3 to)
        {
            ABPath path = null;
            bool claimed = false;
            try
            {
                // Claim 防止返回队列在读取 vectorPath 前把路径交回池，finally 必须释放。
                path = ABPath.Construct(from, to, null);
                path.Claim(ClaimOwner);
                claimed = true;
                AstarPath.StartPath(path);
                path.BlockUntilCalculated();
                if (path.error || path.CompleteState != PathCompleteState.Complete
                    || path.vectorPath == null || path.vectorPath.Count < 2) return false;
                return IsRouteAcceptable(path.vectorPath, from, to);
            }
            finally
            {
                if (path != null && claimed) path.Release(ClaimOwner);
            }
        }

        private static bool IsRouteAcceptable(List<Vector3> corners, Vector3 from, Vector3 to)
        {
            if (corners == null || corners.Count < 2) return false;
            Vector3 gap = corners[corners.Count - 1] - to;
            if (Mathf.Abs(gap.y) > ArrivalHeightTolerance) return false;
            gap.y = 0f;
            if (gap.sqrMagnitude > ArrivalTolerance * ArrivalTolerance) return false;

            float length = 0f;
            for (int i = 1; i < corners.Count; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            if (length > Vector3.Distance(from, to) * DetourFactor + DetourSlack) return false;
            return IsRouteClear(corners);
        }

        private static bool IsRouteClear(List<Vector3> corners)
        {
            float height = 1f, step = 0.25f;
            int mask = GameplayDataSettings.Layers.wallLayerMask;
            CharacterMainControl player = CharacterMainControl.Main;
            ECM2.CharacterMovement movement = player != null ? player.GetComponent<ECM2.CharacterMovement>() : null;
            if (movement != null)
            {
                height = Mathf.Clamp(movement.height, 0.6f, 3f);
                step = Mathf.Clamp(movement.stepOffset, 0f, 0.6f);
                // 地面层不扫：路线贴着地形走，起伏处会擦到地面误判；挡路的是墙与空气墙。
                mask = movement.collisionLayers & ~GameplayDataSettings.Layers.groundLayerMask;
            }
            if (mask == 0) return true;

            // 胶囊底抬过跨步高度，避开台阶和小起伏；顶到角色头顶。
            float bottom = step + SweepRadius + 0.1f;
            float top = Mathf.Max(bottom, step + height - SweepRadius);
            for (int i = 1; i < corners.Count; i++)
            {
                Vector3 a = corners[i - 1], b = corners[i];
                Vector3 direction = b - a;
                float distance = direction.magnitude;
                if (distance < 0.01f) continue;
                if (Physics.CapsuleCast(a + Vector3.up * bottom, a + Vector3.up * top, SweepRadius, direction / distance,
                        distance, mask, QueryTriggerInteraction.Ignore)) return false;
            }
            return true;
        }
    }
}
