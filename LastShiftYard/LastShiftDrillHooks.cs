#if BOSSRUSH_DEV
// ============================================================================
// LastShiftDrillHooks.cs - 末班编组站 Dev 演练的观测面和驱动点（只在 Dev 构建里存在，2026-11-11）
// ============================================================================
// F3「末班编组站演练」（DebugAndTools/F3GameplayValidationLastShiftDrill*.cs）经这里读各子系统的状态、
// 快进班次、走生产路径扳道岔 / 拉铃 / 看运单牌。
//
// 纪律（tests/LastShiftDrillGuard.py 守着）：
// - 整份 #if BOSSRUSH_DEV；生产文件里的钩子点（会话字幕计数）同样包在 #if BOSSRUSH_DEV 里，正式构建一行都没有。
// - 快进班次不另做模拟：按 0.1 秒一步反复调生产 LastShiftTrains.Tick（班次推进、首班教学、撞推车、车体落位、
//   相位事件 → 刷导航 / 卸零担 / 送押运员），最后补一次零推进的 Tick 让条件出口立即同步（生产每 0.5 秒同步一次）。
// - 扳道岔、拉铃、看运单牌直接调交互完成回调对应的那个生产方法（ThrowSwitch / RingBell / ReadManifest）。
// - Dev 前缀的只读属性不改任何东西；会改状态的方法名里写明了改什么。不写存档、不碰剧情持久化、不开箱、不发物品。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class LastShiftTimetable
    {
        /// <summary>演练收尾：撤掉演练拉过的铃（接客亭下一次出口同步时关上），玩家之后还能自己再拉一次。</summary>
        internal void DevClearBell() { BellAt = -1; }

        /// <summary>
        /// 演练快进一次要拨过好几班（2026-11-13 起一趟只有 <see cref="LastTrip"/> 班）：离末班车只剩两班时把这一趟的末班往后推四班，
        /// 演练用例照旧等得到下一班，不会快进进封站。班号照常往前走（签收、撞击、送人都按班号记，不能往回拨），只改末班是第几班。
        /// </summary>
        internal bool DevKeepYardOpen()
        {
            if (YardClosed || Trip < lastTrip - 2) return false;
            lastTrip = Trip + 4;
            return true;
        }
    }

    internal sealed partial class LastShiftTrains
    {
        /// <summary>快进时每一步的游戏秒数：车头每步最多走约 0.45 m，正面撞击的扫掠窄带仍覆盖得到（同班次表夹具口径）。</summary>
        internal const float DevStep = 0.1f;

        /// <summary>
        /// 快进 <paramref name="seconds"/> 游戏秒：按 <see cref="DevStep"/> 一步反复调生产 <see cref="Tick"/>（玩家位置照实读），
        /// 被车撞死（Tick 返回 true）或已释放时提前停；最后补一次零推进的 Tick，让条件出口与撤离环立即同步。返回实际快进的秒数。
        /// </summary>
        internal double DevFastForward(CharacterMainControl player, double seconds)
        {
            double done = 0;
            while (!disposed && done < seconds - 1e-6)
            {
                float step = (float)Math.Min(DevStep, seconds - done);
                done += step;
                if (timetable.DevKeepYardOpen()) DevLastTrainDeferred++;
                if (Tick(player, step)) break;
            }
            DevSyncExits(player);
            return done;
        }

        /// <summary>演练快进时为了不进封站把末班往后推过几次（报告用）。</summary>
        internal int DevLastTrainDeferred { get; private set; }

        /// <summary>零推进走一次生产 Tick，并让这一次就把条件出口推给撤离 owner（生产每 0.5 秒推一次）。</summary>
        internal void DevSyncExits(CharacterMainControl player)
        {
            if (disposed || player == null) return;
            nextExits = 0f;
            Tick(player, 0f);
        }

        /// <summary>交互「扳道岔」读完条后的那一步（生产 <see cref="ThrowSwitch"/>，含被拒时的字幕）。</summary>
        internal void DevThrowSwitch() { ThrowSwitch(); }

        /// <summary>交互「拉轨道车铃」读完条后的那一步（生产 <see cref="RingBell"/>）。</summary>
        internal void DevRingBell() { RingBell(); }

        /// <summary>交互「看班次运单牌」读完条后的那一步（生产 <see cref="ReadManifest"/>，只发一条字幕）。</summary>
        internal void DevReadManifest() { ReadManifest(); }

        /// <summary>演练收尾：地面箭头提前收起（生产 8 秒后自己收）。</summary>
        internal void DevHideArrows() { noticeUntil = -1f; HideArrows(); }

        internal int DevArrowCount { get { int n = 0; foreach (GameObject a in arrows) if (a != null) n++; return n; } }
        internal string DevNavState { get { return navState; } }
        internal int DevWallLayer { get { return wallLayer; } }
        internal int DevInteractCount { get { int n = 0; foreach (GameObject g in interactables) if (g != null) n++; return n; } }
        internal bool DevHasCart { get { return cart != null; } }
        internal Vector3 DevCartPosition { get { return cart == null ? Vector3.zero : cart.transform.position; } }
        internal int DevCarSlots { get { return cars.Length; } }

        internal struct DevBodyInfo
        {
            internal int Model;
            internal bool CarActive, BodyActive;
            internal Vector3 Center, Size;
            internal float Yaw;
            internal int Layer;
        }

        /// <summary>每节车厢模型与它那块墙层碰撞盒此刻的状态（按模型序号）。</summary>
        internal List<DevBodyInfo> DevBodies()
        {
            var list = new List<DevBodyInfo>(cars.Length);
            for (int i = 0; i < cars.Length; i++)
            {
                BoxCollider body = bodies[i];
                list.Add(new DevBodyInfo
                {
                    Model = i,
                    CarActive = cars[i] != null && cars[i].activeSelf,
                    BodyActive = body != null && body.gameObject.activeSelf,
                    Center = body == null ? Vector3.zero : body.transform.position,
                    Size = body == null ? Vector3.zero : body.size,
                    Yaw = body == null ? 0f : body.transform.eulerAngles.y,
                    Layer = body == null ? -1 : body.gameObject.layer,
                });
            }
            return list;
        }
    }

    internal sealed partial class LastShiftExtraction
    {
        /// <summary>当前读条读了多少秒（不在环里为 -1）。</summary>
        internal float DevHeld { get { return held; } }
        internal string DevCurrentExit { get { return current == null ? null : current.Id; } }

        internal List<string> DevExitIds()
        {
            var ids = new List<string>(exits.Count);
            foreach (Exit exit in exits) ids.Add(exit.Id);
            return ids;
        }

        internal bool DevRingVisible(string id)
        {
            foreach (Exit exit in exits)
                if (exit.Id == id) return exit.Ring != null && exit.Ring.gameObject.activeSelf;
            return false;
        }

        internal bool DevHasCountdown(string id)
        {
            foreach (Exit exit in exits) if (exit.Id == id) return exit.Countdown != null;
            return false;
        }

        internal float DevRadius(string id)
        {
            foreach (Exit exit in exits) if (exit.Id == id) return exit.Radius;
            return 0f;
        }

        internal Vector3 DevAnchor(string id)
        {
            foreach (Exit exit in exits) if (exit.Id == id && exit.Anchor != null) return exit.Anchor.position;
            return Vector3.zero;
        }
    }

    internal sealed partial class LastShiftLoot
    {
        internal struct DevBoxInfo
        {
            internal string Id, Kind;
            internal bool Gated, Built, Failed, Opened, Unlocked, LootActive, HasDevice, DeviceActive, HasPending;
            internal int Planned, BatchTrip;
            internal Vector3 Position;
        }

        internal List<DevBoxInfo> DevBoxes()
        {
            var list = new List<DevBoxInfo>(boxes.Count);
            foreach (Box box in boxes)
                list.Add(new DevBoxInfo
                {
                    Id = box.Id, Kind = box.Kind, Gated = box.Gated, Built = box.Built, Failed = box.Failed, Opened = box.Opened,
                    Unlocked = box.Unlocked, LootActive = box.Loot != null && box.Loot.gameObject.activeSelf,
                    HasDevice = box.Device != null, DeviceActive = box.Device != null && box.Device.activeSelf,
                    HasPending = box.PendingBatch != null, Planned = box.Planned == null ? -1 : box.Planned.Count, BatchTrip = box.BatchTrip,
                    Position = box.Anchor == null ? Vector3.zero : box.Anchor.position,
                });
            return list;
        }

        internal int DevOpenedCount { get { int n = 0; foreach (Box box in boxes) if (box.Opened) n++; return n; } }
    }

    internal sealed partial class LastShiftEnemies
    {
        internal struct DevEnemyInfo
        {
            internal string Id, Group, Duty;
            internal LastShiftPost Post;
            internal int Quality;
            internal bool Boss, FromTrain, Regular, Spawning, Failed, Suspended, Alive;
            internal CharacterMainControl Character;
            /// <summary>围圈小队：营地 id（不是小队成员为 null）、冻着没有、站位、该朝的方向（指向圈心）。</summary>
            internal string Squad;
            internal bool Frozen;
            internal Vector3 Home, Facing;
        }

        internal List<DevEnemyInfo> DevEnemies()
        {
            var list = new List<DevEnemyInfo>(guards.Count);
            foreach (Guard g in guards)
                list.Add(new DevEnemyInfo
                {
                    Id = g.Id, Group = g.Group, Duty = g.Duty, Post = g.Post, Quality = g.Quality, Boss = g.Boss, FromTrain = g.FromTrain,
                    Regular = g.Regular, Spawning = g.Spawning, Failed = g.Failed, Suspended = g.Suspended, Alive = IsAlive(g), Character = g.Character,
                    Squad = g.Squad == null ? null : g.Squad.Site.Def.Id, Frozen = g.Frozen, Home = g.Home, Facing = g.Forward,
                });
            return list;
        }

        /// <summary>本趟守货岗（id:站得住的人数，打光的带 *、在场的带 +）、此刻在场各队的状态，与守货岗这本账。</summary>
        internal string DevSquadSummary
        {
            get
            {
                var plan = new List<string>(squadPlan.Count);
                foreach (SquadSite site in squadPlan) plan.Add(site.Def.Id + ":" + site.Spots.Count + (site.Spent ? "*" : "") + (site.Open ? "+" : ""));
                var open = new List<string>(squads.Count);
                foreach (Squad s in squads) open.Add(s.Site.Def.Id + "(" + s.State + ")");
                return "plan=" + string.Join("/", plan.ToArray()) + ",board=" + (open.Count == 0 ? "-" : string.Join("/", open.ToArray()))
                    + ",garrison=" + GarrisonLive() + "/" + GarrisonCap + ",garrisonSpawned=" + garrisonSpawned + "/" + GarrisonPopulation
                    + ",leaders=" + leaders.Count + ",trolleys=" + trolleys.Count + ",trainKills=" + TrainKills;
            }
        }

        internal int DevLive { get { return Live(); } }
        internal int DevTrainLive { get { return TrainLive(); } }
        internal int DevMobileLive { get { return MobileLive(); } }
        internal int DevInFlight { get { return inFlight; } }
        internal int DevQueued { get { return queue.Count; } }
        internal int DevSpots { get { return spots.Count; } }
        internal int DevRoutes { get { return routes.Count; } }
        internal int DevDeviceHosts { get { int n = 0; foreach (GameObject h in deviceHosts) if (h != null) n++; return n; } }
        internal bool DevChiefRides { get { return chiefRides; } }
        internal bool DevSteamBlock { get { return steamBlock != null; } }
    }

    internal sealed partial class LastShiftSession
    {
        private readonly List<string> devStatus = new List<string>();
        private int devStatusTotal;

        private void DevNoteStatus(string message, bool error)
        {
            devStatus.Add((error ? "!" : string.Empty) + message);
            devStatusTotal++;
            if (devStatus.Count > 256) devStatus.RemoveAt(0);
        }

        /// <summary>到目前为止出过几条字幕（单调递增，给 <see cref="DevStatusSince"/> 当书签）。</summary>
        internal int DevStatusMark { get { return devStatusTotal; } }

        /// <summary>书签之后出的字幕（警示色的前面带 !）。</summary>
        internal List<string> DevStatusSince(int mark)
        {
            int take = Math.Max(0, Math.Min(devStatus.Count, devStatusTotal - mark));
            return devStatus.GetRange(devStatus.Count - take, take);
        }

        internal LastShiftWorld DevWorld { get { return world; } }
        internal string DevIndoorLighting { get { return lights == null ? "indoor_fill=missing" : lights.DescribeIndoorFill(); } }
        internal LastShiftTrains DevTrains { get { return trains; } }
        internal LastShiftLoot DevLoot { get { return loot; } }
        internal LastShiftEnemies DevEnemies { get { return enemies; } }
        internal LastShiftExtraction DevExtraction { get { return extraction; } }
        internal LastShiftRun DevRun { get { return run; } }
        internal ArenaPrototypeNavigation DevNavigation { get { return navigation; } }
        internal int DevUnreachableSpawns { get { return unreachable.Count; } }
        internal string DevHudObjective { get { return hud == null ? null : hud.ObjectiveText; } }
        internal bool DevAlarmRaised { get { return alarmRaised; } }

        /// <summary>离 <paramref name="near"/> 最近的本图可走面（按此刻的导航：线上有车时车体包络已封）。</summary>
        internal bool DevSnapToWalkable(Vector3 near, out Vector3 point)
        {
            point = near;
            return navigation != null && navigation.TryNearestWalkable(near, out point);
        }

        /// <summary>演练搬主角：贴地放下，只走官方 SetPosition，不改落脚点兜底的规则。</summary>
        internal bool DevTeleport(Vector3 target, out string reason)
        {
            reason = null;
            if (!IsSessionValid()) { reason = "session_invalid"; return false; }
            if (!LastShiftWorld.PlayArea.Contains(new Vector2(target.x, target.z))) { reason = "outside_play_area"; return false; }
            float ground;
            if (!world.TryGroundAt(target, out ground)) { reason = "no_ground"; return false; }
            player.SetPosition(new Vector3(target.x, ground + 0.12f, target.z));
            return true;
        }
    }
}
#endif
