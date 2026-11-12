#if BOSSRUSH_DEV
// ============================================================================
// RaidStuckProbe.cs - 自研出击地图卡脚探针（只在 Dev 构建里存在）
// ============================================================================
// 2026-11-12 玩家反馈「潮闸镇特别容易被莫名其妙的东西卡脚」。离线审计只能按台账判，
// 实机到底卡在哪要靠这里回收：主角按着方向键 0.6 秒以上、水平速度却不到 0.3 m/s，
// 记一行 `[RaidStuck] STUCK | <地图> | x,y,z | 周围碰撞体名字`，同一处 5 m 内只记一次。
// 进图第一帧另记一行 ECM2 的实际跨步高度 / 坡度上限 / 半径 / 身高（官方 prefab 里的值，源码读不到）。
// 会话持有、会话 Update 调用；正式构建里探针与调用点整段不存在。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed class RaidStuckProbe
    {
        private const float HoldSeconds = 0.6f, MinSpeed = 0.3f, DedupRadius = 5f, ProbeRadius = 0.8f;
        private const int MaxRecords = 64;
        private static readonly Collider[] Buffer = new Collider[24];

        /// <summary>本趟出击已记的卡脚处数；F3 演练报告末尾读它。</summary>
        internal static int SessionCount { get; private set; }

        private readonly string map;
        private readonly List<Vector3> recorded = new List<Vector3>();
        private float blocked;
        private bool paramsLogged;

        internal RaidStuckProbe(string map)
        {
            this.map = map;
            SessionCount = 0;
        }

        internal void Tick(CharacterMainControl player)
        {
            if (player == null) return;
            if (!paramsLogged) { paramsLogged = true; LogMovementParams(player); }
            Vector3 input = player.MoveInput; input.y = 0f;
            Vector3 velocity = player.Velocity; velocity.y = 0f;
            if (Time.timeScale <= 0f || input.sqrMagnitude < 0.25f || velocity.magnitude >= MinSpeed)
            {
                blocked = 0f;
                return;
            }
            if (blocked < 0f) return; // 这次卡住已经记过，松手或走动后才重新计时
            blocked += Time.deltaTime;
            if (blocked < HoldSeconds) return;
            blocked = -1f;
            Vector3 position = player.transform.position;
            if (recorded.Count >= MaxRecords) return;
            for (int i = 0; i < recorded.Count; i++)
                if ((recorded[i] - position).sqrMagnitude < DedupRadius * DedupRadius) return;
            recorded.Add(position);
            SessionCount = recorded.Count;
            Debug.Log("[RaidStuck] STUCK | " + map + " | " + position.x.ToString("F2") + "," + position.y.ToString("F2") + "," + position.z.ToString("F2")
                + " | input=" + input.normalized.x.ToString("F2") + "," + input.normalized.z.ToString("F2") + " | " + Nearby(player, position));
        }

        private static string Nearby(CharacterMainControl player, Vector3 position)
        {
            int count = Physics.OverlapSphereNonAlloc(position + Vector3.up * 0.6f, ProbeRadius, Buffer, ~0, QueryTriggerInteraction.Ignore);
            var names = new List<string>();
            for (int i = 0; i < count; i++)
            {
                Collider c = Buffer[i];
                if (c == null || c.transform.IsChildOf(player.transform)) continue;
                Bounds b = c.bounds;
                names.Add(c.name + "[" + LayerMask.LayerToName(c.gameObject.layer) + " top=" + b.max.y.ToString("F2") + "]");
            }
            return names.Count == 0 ? "（周围没有碰撞体）" : string.Join("; ", names.ToArray());
        }

        private void LogMovementParams(CharacterMainControl player)
        {
            ECM2.CharacterMovement movement = player.GetComponent<ECM2.CharacterMovement>();
            if (movement == null) { Debug.Log("[RaidStuck] PARAMS | " + map + " | 没找到 ECM2.CharacterMovement"); return; }
            Debug.Log("[RaidStuck] PARAMS | " + map + " | stepOffset=" + movement.stepOffset.ToString("F3") + " slopeLimit=" + movement.slopeLimit.ToString("F1")
                + " radius=" + movement.radius.ToString("F3") + " height=" + movement.height.ToString("F3"));
        }
    }
}
#endif
