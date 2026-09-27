#if BOSSRUSH_DEV
// ============================================================================
// SkyIslandGnatsDrill.cs - 云蚋的 Dev 演练入口（F3 SKY_DRILL_GNAT_SWARM；正式构建里不存在）
// ============================================================================
// 从 SkyIslandGnats.cs 拆出来：主文件贴着 1200 行预算，而这一块只给 Dev 演练套件
// （DebugAndTools/F3GameplayValidationSkyIslandDrill.cs）用。整份文件包在 #if BOSSRUSH_DEV 里，
// 「正式构建里没有这些入口」一眼可见（tests/SkyIslandDrillNoPersistenceGuard.py 守着）。
//
// 这些入口会改蚊群状态（刷一群、给躲闪状态机登记合成弹道、打死一只），岛内只读验收一条都不许调
// （tests/SkyIslandValidationSuiteGuard.py 与 tests/SkyIslandReadOnlySuiteDrillIsolationGuard.py 守着）。
// 击杀只走官方 DamageReceiver.Hurt，计数仍由 Frame → Remove(killed: true) 这一条生产路径记，演练不直接记账。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandGnats
    {
        /// <summary>Dev 演练：在 <paramref name="center"/> 周围按正常刷新的距离与悬停高度放一群（受 MaxAlive 限制），返回实际放下的只数。</summary>
        internal int DevSpawnAround(Vector3 center, int count)
        {
            return DevSpawnAround(center, count, Vector3.zero, Math.PI);
        }

        /// <summary>Dev 演练：刷在 <paramref name="player"/> 瞄准方向 ±15° 里（瞄准点贴脚时退回身体朝向）。</summary>
        internal int DevSpawnAhead(CharacterMainControl player, int count)
        {
            Vector3 facing = player.GetCurrentAimPoint() - player.transform.position;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) facing = player.transform.forward;
            return DevSpawnAround(player.transform.position, count, facing, 15.0 * Math.PI / 180.0);
        }

        /// <summary>
        /// Dev 演练：同上，但只在 <paramref name="facing"/> 朝向 ±<paramref name="halfSpread"/> 弧度内放（<paramref name="facing"/> 为零向量时四周随机）。
        /// 全自动验收的可见度截图要让目标落在官方夜视扇形里：扇形外的精灵会被战争迷雾一起盖掉，读数只取决于随机方位（2026-09-25 F3）。
        /// </summary>
        internal int DevSpawnAround(Vector3 center, int count, Vector3 facing, double halfSpread)
        {
            if (!Usable) return 0;
            float now = Time.time;
            int placed = 0;
            facing.y = 0f;
            bool aimed = facing.sqrMagnitude > 1e-4f;
            double heading = aimed ? Math.Atan2(facing.z, facing.x) : 0.0;
            for (int i = 0; i < count; i++)
            {
                double bearing = aimed
                    ? heading + (random.NextDouble() * 2.0 - 1.0) * halfSpread
                    : random.NextDouble() * Math.PI * 2.0;
                float distance = SkyIslandMosquitoRules.SpawnDistance(random.NextDouble());
                Vector3 at = center + new Vector3((float)Math.Cos(bearing), 0f, (float)Math.Sin(bearing)) * distance
                    + Vector3.up * SkyIslandMosquitoRules.HoverHeight;
                if (Spawn(at, now)) placed++;
            }
            if (placed > 0) Physics.SyncTransforms();
            return placed;
        }

        /// <summary>
        /// Dev 演练：从 <paramref name="origin"/> 朝每只活蚋各打一发合成直线弹，走与开枪补丁同一个
        /// <see cref="SkyIslandGnatMotor.OnShot"/>（只是弹道是算出来的，不经过官方 Projectile）。返回登记了几只。
        /// </summary>
        internal int DevSyntheticShot(Vector3 origin, float speed, float range, float radius)
        {
            if (!Usable) return 0;
            int frame = Time.frameCount, registered = 0;
            for (int i = 0; i < gnats.Length; i++)
            {
                Gnat gnat = gnats[i];
                if (gnat == null || gnat.Leaving || gnat.Root == null || !gnat.Root.activeSelf) continue;
                Vector3 toward = gnat.Position - origin;
                if (toward.sqrMagnitude < 0.01f) continue;
                var shot = new SkyIslandGnatShot(ToVec(origin), ToVec(toward.normalized), speed, range, radius);
                gnat.Motor.OnShot(ToVec(gnat.Position), shot, frame, this);
                registered++;
            }
            return registered;
        }

        /// <summary>Dev 演练：场上活蚋的躲闪统计（最长一次冲刺、最大预算、累计起跳次数）。返回统计了几只。</summary>
        internal int DevMotorStats(out float longestDash, out float highestBudget, out int dodges)
        {
            longestDash = 0f;
            highestBudget = 0f;
            dodges = 0;
            int counted = 0;
            for (int i = 0; i < gnats.Length; i++)
            {
                Gnat gnat = gnats[i];
                if (gnat == null || gnat.Motor == null) continue;
                counted++;
                longestDash = Math.Max(longestDash, gnat.Motor.LastDashLength);
                highestBudget = Math.Max(highestBudget, gnat.Motor.Budget);
                dodges += gnat.Motor.DodgesStarted;
            }
            return counted;
        }

        /// <summary>
        /// Dev 演练：用官方 <c>DamageReceiver.Hurt</c> 打死一只——与子弹同一条伤害路径，击杀照常由 <see cref="Frame"/>
        /// 发现根物体被停用、经 <see cref="Remove"/> 记数。不直接调 Remove，也不直接给委托记账。返回是否打到了。
        /// </summary>
        internal bool DevKillOne(out Vector3 at)
        {
            at = Vector3.zero;
            if (!Usable) return false;
            for (int i = 0; i < gnats.Length; i++)
            {
                Gnat gnat = gnats[i];
                if (gnat == null || gnat.Leaving || gnat.Receiver == null || gnat.Root == null || !gnat.Root.activeSelf) continue;
                DamageInfo damage = new DamageInfo(CharacterMainControl.Main);
                damage.damageValue = SkyIslandMosquitoRules.GnatHealth * 10f;
                at = gnat.Position;
                return gnat.Receiver.Hurt(damage);
            }
            return false;
        }
    }
}
#endif
