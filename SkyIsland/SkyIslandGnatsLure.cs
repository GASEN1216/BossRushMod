// ============================================================================
// SkyIslandGnatsLure.cs - 蚋笛翁的笛声：把云蚋招过来、往玩家身上扑（头目 R3）
// ============================================================================
// 从 SkyIslandGnats.cs 拆出来单独放（主文件临近 1200 行预算）：主文件只在 Sample 的「附近有敌人就散群」、
// Cruise 的「被灭蚊灯引走」各多一处判断，另在 Steer 里让苔纱面罩挡掉躲闪。
// 这里不造成伤害、没有玩家可见文案（笛声字幕在蚋笛翁的控制器里）；刷新走主文件的私有 Spawn，不动它的装配顺序。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandGnats
    {
        /// <summary>笛声招来的云蚋落在玩家身边这么远的一圈上（米），再自己飞过来：不凭空贴脸刷出。</summary>
        private const float LureSpawnDistance = 6f;

        private float hostileLureUntil = -1f;

        /// <summary>
        /// 笛声还在不在：在的这几秒，附近有敌人也不散群（吹笛的本身就是敌人），所有云蚋都往玩家身上扑、不理灭蚊灯。
        /// </summary>
        internal bool HostileLureActive { get { return hostileLureUntil > 0f && Time.time < hostileLureUntil; } }

        /// <summary>吹一次笛：<paramref name="seconds"/> 秒内压过灭蚊灯、敌人在附近也不散群。蚊群不可用时返回 false。</summary>
        internal bool SetHostileLure(float seconds)
        {
            if (!Usable || seconds <= 0f) return false;
            hostileLureUntil = Time.time + seconds;
            return true;
        }

        /// <summary>夜里在 <paramref name="player"/> 身边招 <paramref name="count"/> 只（受同时存活上限约束），返回实际刷出几只。白天不招。</summary>
        internal int LureSwarm(Vector3 player, int count)
        {
            if (!Usable || !NightNow || count <= 0) return 0;
            int room = SkyIslandMosquitoRules.RoomFor(alive, count);
            if (room <= 0) return 0;
            float now = Time.time;
            double bearing = random.NextDouble() * Math.PI * 2.0;
            Vector3 center = player + new Vector3((float)Math.Cos(bearing), 0f, (float)Math.Sin(bearing)) * LureSpawnDistance +
                Vector3.up * SkyIslandMosquitoRules.HoverHeight;
            int spawned = 0;
            for (int k = 0; k < room; k++)
            {
                Vector3 at = center + new Vector3((float)(random.NextDouble() * 2.4 - 1.2), (float)(random.NextDouble() * 0.4 - 0.2),
                    (float)(random.NextDouble() * 2.4 - 1.2));
                if (Spawn(at, now)) spawned++;
            }
            if (spawned > 0) Debug.Log("[SkyIslandGnats] LURE count=" + spawned + " alive=" + alive);
            return spawned;
        }

        /// <summary>Cruise 用：笛声压着的这几秒不找灭蚊灯（逐帧路径，只读两个字段）。</summary>
        private bool LureOverridesZappers(float now)
        {
            return hostileLureUntil > 0f && now < hostileLureUntil;
        }
    }
}
