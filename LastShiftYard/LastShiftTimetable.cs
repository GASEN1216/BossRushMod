using System;
using System.Collections.Generic;

namespace BossRush
{
    internal enum LastShiftPhase { Approach, Arrival, Dwell, Depart, Clear }
    internal enum LastShiftEvent { Normal, Slow, Heavy, Express }
    internal enum LastShiftCarKind { Engine, Cargo, Van }

    /// <summary>一节车厢此刻的位置（three.js 坐标：x 右、z 向南为正；列车从南 z = 78 进站往北开）。</summary>
    internal struct LastShiftCar
    {
        internal int Index, Model;
        internal LastShiftCarKind Kind;
        internal double X, Z, Yaw, Width, Depth;
        internal bool Extra;
    }

    /// <summary>
    /// 末班编组站的班次纯规则：相位与时长、班次事件、首班教学、道岔锁、出口条件、车厢位置与车体包络。
    /// 不碰 Unity 对象，口径逐条照 three.js 原型 `docs/design/last-shift-yard/proto/sim.mjs`（第六、七轮），夹具直接编译它跑断言。
    /// 坐标一律用 three.js 的（运行时换算成 Unity 的 (x, y, -z)）。时间是这一趟会话的游戏时间秒（菜单暂停时不走）。
    /// </summary>
    internal sealed partial class LastShiftTimetable
    {
        internal const double Approach = 20, Arrival = 26, Dwell = 47, Depart = 19, Clear = 68;
        internal const double SlowDwell = 70, ExpressArrival = 16, ExpressDepart = 12;
        /// <summary>首班在岔心前减速等调度：车头到 z = 20 时停住（原型 TRAINING_HOLD）。</summary>
        internal const double TrainingHold = (78.0 - 20.0) / 116.0 * Arrival;
        /// <summary>首班要等玩家站到南护栏后才发车；离开累计这么久仍照常发车（第七轮 owner 采纳，不会卡死）。</summary>
        internal const double DemoFallback = 60;
        internal const double BellWait = 14;
        /// <summary>东台停靠 12 秒后算卸完一班，押运门开。</summary>
        internal const double EastUnloadSeconds = 12;
        internal static readonly double[] EventOdds = { 40, 20, 25, 15 };
        /// <summary>南护栏安全观察位（布局 facilities.watch）与示范推车。</summary>
        internal const double WatchX = -10, WatchZ = 45, CartX = 0, CartZ = 38;

        private static readonly double[] CarWidths = { 3.3, 3.2, 3.2, 3.1 };
        private static readonly double[] CarDepths = { 10, 8.8, 9.2, 8.8 };

        internal LastShiftPhase Phase { get; private set; }
        internal double PhaseTime { get; private set; }
        internal double Clock { get; private set; }
        internal LastShiftEvent Event { get; private set; }
        internal int Trip { get; private set; }
        internal char Line { get; private set; }
        internal char LockedLine { get; private set; }
        internal bool Training { get; private set; }
        internal bool DispatchChosen { get; private set; }
        internal bool DispatchWait { get; private set; }
        internal bool CartHit { get; private set; }
        internal double CartHitClock { get; private set; }
        internal bool EastUnloaded { get; private set; }
        internal double BellAt { get; private set; }
        internal double AwayFromRail { get; private set; }
        internal bool DemoForced { get; private set; }
        internal char Premium { get; private set; }
        private readonly int seed;

        /// <summary>每次相位切换（老相位、新相位）；运行时用它发字幕、刷导航、开出口、送押运员。</summary>
        internal event Action<LastShiftPhase, LastShiftPhase> PhaseChanged;

        /// <param name="returnVisit">回访（不是第一趟）：跳过首班教学，从班次表的随机时刻开局。</param>
        internal LastShiftTimetable(int seed, bool returnVisit)
        {
            this.seed = seed;
            Line = 'W'; LockedLine = '\0'; Trip = 1; Event = LastShiftEvent.Normal; BellAt = -1;
            Phase = LastShiftPhase.Approach;
            var r = Stream("raid#run");
            Premium = r.NextDouble() < 0.5 ? 'W' : 'E';
            Training = !returnVisit;
            if (!returnVisit) return;
            DispatchChosen = true; CartHit = true; CartHitClock = -10;
            Line = r.NextDouble() < 0.5 ? 'W' : 'E';
            Event = RollEvent(1);
            if (r.NextDouble() < 0.5) { Phase = LastShiftPhase.Clear; PhaseTime = 8 + r.NextDouble() * 52; }
            else PhaseTime = r.NextDouble() * 14;
        }

        /// <summary>每种用途一条独立随机流（种子 + 用途），结果不随开箱、扳岔顺序变化。</summary>
        internal System.Random Stream(string tag)
        {
            unchecked
            {
                int h = seed ^ (int)0x9e3779b9;
                foreach (char c in tag) h = (h ^ c) * 16777619;
                return new System.Random(h);
            }
        }

        internal LastShiftEvent RollEvent(int trip)
        {
            double total = 0;
            foreach (double w in EventOdds) total += w;
            double x = Stream("trip#" + trip).NextDouble() * total;
            for (int i = 0; i < EventOdds.Length; i++) { if (x < EventOdds[i]) return (LastShiftEvent)i; x -= EventOdds[i]; }
            return LastShiftEvent.Express;
        }

        internal bool IsExpress { get { return Event == LastShiftEvent.Express; } }
        internal bool IsHeavy { get { return Event == LastShiftEvent.Heavy; } }
        internal char LineUsed { get { return LockedLine != '\0' ? LockedLine : Line; } }

        internal double PhaseLength(LastShiftPhase p)
        {
            switch (p)
            {
                case LastShiftPhase.Approach: return Approach;
                case LastShiftPhase.Arrival: return IsExpress ? ExpressArrival : Arrival;
                case LastShiftPhase.Dwell: return Event == LastShiftEvent.Slow ? SlowDwell : Dwell;
                case LastShiftPhase.Depart: return IsExpress ? ExpressDepart : Depart;
                default: return Clear;
            }
        }

        internal double SecondsLeft { get { return Math.Max(0, PhaseLength(Phase) - PhaseTime); } }

        /// <summary>车头所在的 z（three.js）。</summary>
        internal double LeadZ
        {
            get
            {
                switch (Phase)
                {
                    case LastShiftPhase.Approach: return 78;
                    case LastShiftPhase.Arrival: return 78 - 116 * PhaseTime / PhaseLength(LastShiftPhase.Arrival);
                    case LastShiftPhase.Dwell: return -38;
                    case LastShiftPhase.Depart: return -38 - 67 * PhaseTime / PhaseLength(LastShiftPhase.Depart);
                    default: return -105;
                }
            }
        }

        /// <summary>列车在轨道上、会挡路和撞人的相位。</summary>
        internal bool TrainOnLine { get { return Phase == LastShiftPhase.Arrival || Phase == LastShiftPhase.Dwell || Phase == LastShiftPhase.Depart; } }

        internal static bool AtSouthRail(double x, double z) { return Math.Abs(x - WatchX) <= 7 && z >= WatchZ - 1.5 && z <= WatchZ + 9; }

        /// <summary>
        /// 推进时间。<paramref name="playerX"/>/<paramref name="playerZ"/> 是玩家位置（three.js）：首班在玩家离开南护栏时停表（离开累计 60 秒后照常发车）。
        /// 返回本步里车头扫过前的 z（给正面撞击用）。
        /// </summary>
        internal double Tick(double dt, double playerX, double playerZ)
        {
            double oldZ = LeadZ;
            Clock += dt; PhaseTime += dt;
            if (Training && Phase == LastShiftPhase.Approach && !CartHit && !DemoForced && !AtSouthRail(playerX, playerZ))
            {
                PhaseTime -= dt; AwayFromRail += dt;
                if (AwayFromRail >= DemoFallback) DemoForced = true;
            }
            if (Training && !DispatchChosen && Phase == LastShiftPhase.Arrival && PhaseTime >= TrainingHold)
            {
                PhaseTime = TrainingHold;
                DispatchWait = true;
            }
            if (Phase == LastShiftPhase.Dwell && PhaseTime >= EastUnloadSeconds && LineUsed == 'E') EastUnloaded = true;
            if (PhaseTime >= PhaseLength(Phase) - 1e-7)
            {
                if (Phase == LastShiftPhase.Clear) NextTrain();
                else SetPhase(Phase == LastShiftPhase.Arrival && IsExpress ? LastShiftPhase.Depart : (LastShiftPhase)((int)Phase + 1));
            }
            return oldZ;
        }

        private void NextTrain()
        {
            Training = false; DispatchWait = false; Trip++;
            Event = RollEvent(Trip);
            SetPhase(LastShiftPhase.Approach);
        }

        private void SetPhase(LastShiftPhase next)
        {
            LastShiftPhase previous = Phase;
            Phase = next; PhaseTime = 0; DispatchWait = false;
            if (next == LastShiftPhase.Approach) { LockedLine = '\0'; EastUnloaded = false; }
            if (next == LastShiftPhase.Arrival) LockedLine = Line;
            if (next == LastShiftPhase.Dwell && LockedLine == '\0') LockedLine = Line;
            if (next == LastShiftPhase.Depart && LineUsed == 'E' && !IsExpress) EastUnloaded = true;
            Action<LastShiftPhase, LastShiftPhase> handler = PhaseChanged;
            if (handler != null) handler(previous, next);
        }

        /// <summary>道岔还给不给扳；不给时 <paramref name="reason"/> 是玩家能看懂的原因。</summary>
        internal bool CanSwitch(out string reason)
        {
            reason = null;
            if (Training && !CartHit) { reason = "demo"; return false; }
            bool teaching = Training && Phase == LastShiftPhase.Arrival && PhaseTime <= TrainingHold + 0.001;
            if (Phase != LastShiftPhase.Approach && Phase != LastShiftPhase.Clear && !teaching) { reason = "locked"; return false; }
            return true;
        }

        internal bool TrySwitch(char line, out string reason)
        {
            if (!CanSwitch(out reason)) return false;
            bool teaching = Training && Phase == LastShiftPhase.Arrival && PhaseTime <= TrainingHold + 0.001;
            Line = line; LockedLine = teaching ? line : '\0';
            DispatchChosen = true; DispatchWait = false;
            return true;
        }

        internal void MarkCartHit() { if (CartHit) return; CartHit = true; CartHitClock = Clock; }

        internal void RingBell() { if (BellAt < 0) BellAt = Clock; }

        internal bool ExitOpen(string id)
        {
            switch (id)
            {
                case "north": return true;
                case "west": return LineUsed != 'W' || Phase == LastShiftPhase.Clear;
                case "east": return EastUnloaded;
                case "bell": return BellAt >= 0 && Clock - BellAt >= BellWait;
                default: return false;
            }
        }

        // ---------------- 轨道与车厢 ----------------

        /// <summary>两股道在岔心（z = 8）以南合成一股，往北分开到 ±24 m（原型 layout.mjs 的 railX）。</summary>
        internal static double RailX(double z, char line)
        {
            double n = Math.Max(0, Math.Min(1, (8 - z) / 32));
            return (line == 'E' ? 1 : -1) * 24 * n * n * (3 - 2 * n);
        }

        private static double ArcBehind(double z, double meters, char line)
        {
            double at = z;
            for (double d = 0; d < meters;)
            {
                double slope = (RailX(at + 0.2, line) - RailX(at - 0.2, line)) / 0.4;
                double tick = Math.Min(1, meters - d);
                at += tick / Math.Sqrt(1 + slope * slope);
                d += tick;
            }
            return at;
        }

        /// <summary>按车头位置排每节车厢（车头在前，往 +z 一节节往后）；车体模型的车鼻是本地 -Z。</summary>
        internal List<LastShiftCar> Cars()
        {
            var cars = new List<LastShiftCar>(5);
            if (Phase == LastShiftPhase.Clear) return cars;
            char line = LineUsed;
            double lead = LeadZ, offset = 0;
            int[] models = IsHeavy ? new[] { 0, 1, 2, 4, 3 } : new[] { 0, 1, 2, 3 };
            for (int i = 0; i < models.Length; i++)
            {
                int m = models[i], shape = m == 4 ? 2 : m;
                double z = ArcBehind(lead, offset, line);
                int nextShape = i + 1 < models.Length ? (models[i + 1] == 4 ? 2 : models[i + 1]) : -1;
                offset += CarDepths[shape] * 0.5 + 0.65 + (nextShape >= 0 ? CarDepths[nextShape] * 0.5 : 0);
                cars.Add(new LastShiftCar
                {
                    Index = i, Model = m, Extra = m == 4,
                    Kind = m == 0 ? LastShiftCarKind.Engine : m == 3 ? LastShiftCarKind.Van : LastShiftCarKind.Cargo,
                    X = RailX(z, line), Z = z, Yaw = Math.Atan2(RailX(z + 0.25, line) - RailX(z - 0.25, line), 0.5),
                    Width = CarWidths[shape], Depth = CarDepths[shape],
                });
            }
            return cars;
        }

        /// <summary>
        /// 这一帧车头前保险杠扫过的窄带有没有碰到这一点（原型 frontHit）：只在进站、出站时算，侧擦和静止车身不算；高处（司机室、跨线桥）不算。
        /// </summary>
        internal bool FrontHit(double oldLeadZ, double x, double z, double groundY)
        {
            if (Phase != LastShiftPhase.Arrival && Phase != LastShiftPhase.Depart) return false;
            List<LastShiftCar> cars = Cars();
            if (cars.Count == 0) return false;
            LastShiftCar c = cars[0];
            double co = Math.Cos(c.Yaw), sn = Math.Sin(c.Yaw), vx = x - c.X, vz = z - c.Z;
            double across = co * vx - sn * vz, along = sn * vx + co * vz;
            if (Math.Abs(across) > c.Width * 0.5 + 0.26 || groundY > 2.7) return false;
            double moved = Math.Max(0.05, Math.Abs(c.Z - oldLeadZ) * 1.7);
            return along >= -c.Depth * 0.5 - 0.35 && along <= -c.Depth * 0.5 + moved + 0.35;
        }

        /// <summary>车体包络（原型 trainEnvelope）：离该股道中心线 3.05 m（按坡度放宽）以内；跨线桥下（桥面高 ≥ 4.75）与司机室楼面不算。</summary>
        internal static bool InEnvelope(double x, double z, char line)
        {
            if (z < -112 || z > 112) return false;
            double slope = (RailX(z + 0.1, line) - RailX(z - 0.1, line)) / 0.2;
            return Math.Abs(x - RailX(z, line)) <= 3.05 * Math.Sqrt(1 + slope * slope);
        }

        /// <summary>示范推车被撞飞后的位置（原型 sync：1.2 秒内往东北飞出、落地）。</summary>
        internal void CartPose(out double x, out double y, out double z)
        {
            x = CartX; y = 0; z = CartZ;
            if (!CartHit || CartHitClock < 0) return;
            double t = Math.Min(1.2, Clock - CartHitClock);
            x = CartX + 5 * t; z = CartZ - 2 * t; y = Math.Max(0, 4 * t - 3.5 * t * t);
        }
    }
}
