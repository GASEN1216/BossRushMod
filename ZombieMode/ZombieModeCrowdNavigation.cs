using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 丧尸重新寻路节流。官方 TraceTarget 每 0.15 秒调一次 <see cref="AI_PathControl.MoveToPos"/>，
    /// 它会先清空当前路径、取消还没算完的请求再重新排队。尸潮把上百只丧尸同时锁定玩家，A* 队列积压超过
    /// 0.15 秒后，几乎每条路径都在算完前被下一次请求取消，丧尸只能沿上一次的移动输入直走：
    /// 一抽一抽、贴墙角、堵门（2026-11-24 玩家反馈）。
    /// 只拦本局登记过的丧尸；模式未激活时读一次属性就放行官方逻辑，其它模式与原版地图不受影响。
    /// </summary>
    [HarmonyPatch(typeof(AI_PathControl), nameof(AI_PathControl.MoveToPos), new System.Type[] { typeof(Vector3) })]
    internal static class ZombieModePathRequestThrottlePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(AI_PathControl __instance, Vector3 pos)
        {
            ModBehaviour owner = ModBehaviour.Instance;
            if (owner == null || !owner.IsZombieModeActive || __instance == null)
            {
                return true;
            }

            try
            {
                return ZombieModeCrowdNavigation.AllowPathRequest(owner, __instance, pos);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] 寻路节流异常，放行官方寻路: " + e.Message);
                return true;
            }
        }
    }

    internal static class ZombieModeCrowdNavigation
    {
        internal static bool AllowPathRequest(ModBehaviour owner, AI_PathControl pathControl, Vector3 target)
        {
            CharacterMainControl agent = pathControl.controller;
            ZombieModeEnemyRuntimeMarker marker;
            if (agent == null ||
                !owner.TryGetZombieModeKnownEnemyMarker(agent, out marker) ||
                marker == null ||
                marker.RunId != owner.ZombieModeCurrentRunId ||
                marker.DeathSettled ||
                marker.RemovedFromRuntime)
            {
                return true;
            }

            float now = owner.GetZombieModeRuntimeNow();
            Vector3 self = agent.transform.position;
            if (ShouldThrottleZombieModePathRequest(
                    marker,
                    pathControl.WaitingForPathResult,
                    pathControl.path != null,
                    pathControl.ReachedEndOfPath,
                    self,
                    target,
                    now))
            {
                return false;
            }

            marker.HasPathRequest = true;
            marker.LastPathRequestTime = now;
            marker.LastPathRequestTarget = target;
            marker.LastPathRequestOrigin = self;
            return true;
        }

        /// <summary>
        /// true = 本次不重新寻路，沿用当前路径或等在途请求算完。
        /// 在途请求不取消（超时除外）；没有路径、已走到终点、被瞬移时一律放行；
        /// 近身保持官方 0.15 秒，越远间隔越长（最多 1 秒）；目标挪动超过距离的两成（至少 2 m）时提前重算。
        /// </summary>
        internal static bool ShouldThrottleZombieModePathRequest(
            ZombieModeEnemyRuntimeMarker marker,
            bool waitingForPathResult,
            bool hasPath,
            bool reachedEndOfPath,
            Vector3 self,
            Vector3 target,
            float now)
        {
            if (marker == null || !marker.HasPathRequest || !IsFinite(self) || !IsFinite(target))
            {
                return false;
            }

            float since = now - marker.LastPathRequestTime;
            // 换局或时钟回拨时 since 为负，NaN 也落在这里。
            if (!(since >= 0f))
            {
                return false;
            }

            if (waitingForPathResult)
            {
                return since < ZombieModeTuning.PathRequestPendingTimeoutSeconds;
            }

            if (!hasPath || reachedEndOfPath)
            {
                return false;
            }

            float moved = HorizontalDistance(self, marker.LastPathRequestOrigin);
            if (moved > since * ZombieModeTuning.PathRepathTeleportSpeed + ZombieModeTuning.PathRepathTeleportSlack)
            {
                return false;
            }

            float due = since + ZombieModeTuning.PathRepathTimingSlackSeconds;
            if (due < ZombieModeTuning.PathRepathMinIntervalSeconds)
            {
                return true;
            }

            float distance = HorizontalDistance(self, target);
            float farFactor = Mathf.InverseLerp(
                ZombieModeTuning.PathRepathNearDistance,
                ZombieModeTuning.PathRepathFarDistance,
                distance);
            float interval = Mathf.Lerp(
                ZombieModeTuning.PathRepathMinIntervalSeconds,
                ZombieModeTuning.PathRepathMaxIntervalSeconds,
                farFactor);
            if (due >= interval)
            {
                return false;
            }

            float tolerance = Mathf.Max(
                ZombieModeTuning.PathRepathTargetShiftMinDistance,
                distance * ZombieModeTuning.PathRepathTargetShiftFraction);
            return HorizontalDistance(target, marker.LastPathRequestTarget) < tolerance;
        }

        /// <summary>
        /// 两点之间（抬到腰高）是否隔着官方墙体层。导航面两侧可能都可达，但隔墙挪过去就是穿墙；
        /// 读不到墙体层时不拦，退回只看可达性。刷怪散开与叠罗汉拨离共用。
        /// </summary>
        internal static bool IsBlockedByWall(Vector3 from, Vector3 to)
        {
            int wallMask;
            try
            {
                wallMask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask.value;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] 读取墙体层失败，不做隔墙检查: " + e.Message);
                return false;
            }

            if (wallMask == 0)
            {
                return false;
            }

            Vector3 lift = Vector3.up * 0.6f;
            return Physics.Linecast(from + lift, to + lift, wallMask, QueryTriggerInteraction.Ignore);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }

    internal sealed partial class ZombieModeRuntimeModule
    {
        private static readonly Vector3[] ZombieModeUnstackDirections = { new Vector3(1f, 0f, 0f), new Vector3(0.7071f, 0f, 0.7071f), new Vector3(0f, 0f, 1f), new Vector3(-0.7071f, 0f, 0.7071f), new Vector3(-1f, 0f, 0f), new Vector3(-0.7071f, 0f, -0.7071f), new Vector3(0f, 0f, -1f), new Vector3(0.7071f, 0f, -0.7071f) };
        private int zombieModeUnstackCursor;

        /// <summary>
        /// 叠罗汉兜底：站在别的角色头顶的丧尸高出子弹平面，打不到也下不来，堆在门口把整波卡住。
        /// 由每秒一次的恢复巡检逐只调用（不新增 Update）；连续两次看到脚下是别的角色，
        /// 就把它拨到下面那只旁边可达、没有隔墙的地面上，并让它立即重新寻路。
        /// </summary>
        internal void CheckZombieModeStackedEnemy(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || marker == null || marker.DeathSettled || marker.RemovedFromRuntime)
            {
                return;
            }

            try
            {
                ECM2.CharacterMovement movement = marker.CachedMovement;
                if (movement == null)
                {
                    movement = enemy.GetComponent<ECM2.CharacterMovement>();
                    marker.CachedMovement = movement;
                }

                CharacterMainControl below = movement != null ? GetZombieModeCharacterUnderfoot(movement, enemy) : null;
                if (below == null)
                {
                    marker.StackedSampleCount = 0;
                    return;
                }

                marker.StackedSampleCount++;
                if (marker.StackedSampleCount < ZombieModeTuning.StackedSamplesBeforeUnstack)
                {
                    return;
                }

                marker.StackedSampleCount = 0;
                Vector3 destination;
                if (!TryFindZombieModeUnstackPosition(below.transform.position, out destination))
                {
                    return;
                }

                movement.SetPositionAndRotation(destination, enemy.transform.rotation, false);
                marker.HasPathRequest = false;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] 叠罗汉拨离失败: " + e.Message);
            }
        }

        private static CharacterMainControl GetZombieModeCharacterUnderfoot(ECM2.CharacterMovement movement, CharacterMainControl self)
        {
            Collider ground = movement.groundCollider;
            Rigidbody body = ground != null ? ground.attachedRigidbody : null;
            if (body == null)
            {
                return null;
            }

            CharacterMainControl below = body.GetComponent<CharacterMainControl>();
            return below != null && below != self ? below : null;
        }

        private bool TryFindZombieModeUnstackPosition(Vector3 basePosition, out Vector3 destination)
        {
            destination = basePosition;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return false;
            }

            int start = zombieModeUnstackCursor;
            zombieModeUnstackCursor = (zombieModeUnstackCursor + 3) % ZombieModeUnstackDirections.Length;
            for (int i = 0; i < ZombieModeUnstackDirections.Length; i++)
            {
                Vector3 candidate = basePosition + ZombieModeUnstackDirections[(start + i) % ZombieModeUnstackDirections.Length] * ZombieModeTuning.UnstackRadius;
                Vector3 resolved;
                if (!SpawnPositionHelper.TryResolveReachableFrom(
                        candidate,
                        player.transform.position,
                        ZombieModeTuning.UnstackSnapRadius,
                        ZombieModeTuning.NavMeshLiftOffset,
                        out resolved))
                {
                    continue;
                }

                if (Mathf.Abs(resolved.y - basePosition.y) > ZombieModeTuning.UnstackMaxHeightDelta)
                {
                    continue;
                }

                if (ZombieModeCrowdNavigation.IsBlockedByWall(basePosition, resolved))
                {
                    continue;
                }

                destination = resolved;
                return true;
            }

            return false;
        }
    }
}
