using UnityEngine;
using UnityEngine.AI;

namespace BossRush
{
    /// <summary>
    /// 刷怪点几何工具：NavMesh 抬升 / 射线落地 / 玩家最小距离过滤 / 玩家附近回退环。
    /// 由 ModeD/E/F/Zombie 共用，避免每个模式各写一份。
    /// </summary>
    internal static class SpawnPointGeometryHelper
    {
        /// <summary>
        /// 在 candidate 附近做 NavMesh 抬升 + 射线落地，返回可用刷怪点。
        /// virtualPoint=true 表示该点为玩家附近虚拟回退（更严格的"远离玩家"过滤）。
        /// </summary>
        internal static bool TryResolve(
            Vector3 candidate,
            float navSampleRadius,
            float liftOffset,
            bool virtualPoint,
            float minPlayerDistance,
            out Vector3 resolved)
        {
            resolved = candidate;
            NavMeshHit navHit;
            if (NavMesh.SamplePosition(candidate, out navHit, navSampleRadius, NavMesh.AllAreas))
            {
                resolved = navHit.position + Vector3.up * liftOffset;
                return !virtualPoint || PassesMinPlayerDistance(resolved, minPlayerDistance);
            }

            if (virtualPoint)
            {
                return false;
            }

            if (TrySnapToGround(candidate, liftOffset, out resolved))
            {
                return PassesMinPlayerDistance(resolved, minPlayerDistance);
            }

            return false;
        }

        /// <summary>
        /// 围绕玩家生成 ringCount 个等距回退点；命中即返回。
        /// </summary>
        internal static bool TryFindAroundPlayer(
            Vector3 playerPos,
            int ringCount,
            float radius,
            float navSampleRadius,
            float liftOffset,
            float minPlayerDistance,
            out Vector3 resolved)
        {
            resolved = playerPos;
            for (int i = 0; i < ringCount; i++)
            {
                float angle = 360f * i / ringCount;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                if (TryResolve(playerPos + offset, navSampleRadius, liftOffset, true, minPlayerDistance, out resolved))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool PassesMinPlayerDistance(Vector3 point, float minPlayerDistance)
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

        private static bool TrySnapToGround(Vector3 candidate, float liftOffset, out Vector3 resolved)
        {
            resolved = candidate;
            RaycastHit hit;
            Vector3 origin = candidate + Vector3.up * 24f;
            if (Physics.Raycast(origin, Vector3.down, out hit, 80f))
            {
                resolved = hit.point + Vector3.up * liftOffset;
                return true;
            }
            return false;
        }
    }
}
