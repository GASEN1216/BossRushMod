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

    /// <summary>停车时封住各节车的实体盒与胶囊净空，允许寻路绕过车头和车尾。</summary>
    internal sealed class LastShiftParkedTrain
    {
        private const double Clearance = 0.65;
        private readonly Body[] bodies;

        private struct Body
        {
            internal double X, Z, Cos, Sin, HalfWidth, HalfDepth;
        }

        internal LastShiftParkedTrain(List<LastShiftCar> cars)
        {
            bodies = new Body[cars.Count];
            for (int i = 0; i < bodies.Length; i++)
            {
                LastShiftCar car = cars[i];
                bodies[i] = new Body { X = car.X, Z = car.Z, Cos = Math.Cos(car.Yaw), Sin = Math.Sin(car.Yaw),
                    HalfWidth = car.Width * 0.5 + Clearance, HalfDepth = car.Depth * 0.5 + Clearance };
            }
        }

        internal bool Intersects(double ax, double az, double bx, double bz, double cx, double cz)
        {
            foreach (Body body in bodies)
            {
                double dx = ax - body.X, dz = az - body.Z;
                double ex = bx - body.X, ez = bz - body.Z;
                double fx = cx - body.X, fz = cz - body.Z;
                double x0 = body.Cos * dx - body.Sin * dz, z0 = body.Sin * dx + body.Cos * dz;
                double x1 = body.Cos * ex - body.Sin * ez, z1 = body.Sin * ex + body.Cos * ez;
                double x2 = body.Cos * fx - body.Sin * fz, z2 = body.Sin * fx + body.Cos * fz;
                if (Separated(x0, x1, x2, body.HalfWidth) || Separated(z0, z1, z2, body.HalfDepth)) continue;
                // 矩形两轴和三角形三条边的法向量都是分离轴；只检查重心会让长三角形伸进车体。
                if (SeparatedOnEdge(x0, z0, x1, z1, x2, z2, body)
                    || SeparatedOnEdge(x1, z1, x2, z2, x0, z0, body)
                    || SeparatedOnEdge(x2, z2, x0, z0, x1, z1, body)) continue;
                return true;
            }
            return false;
        }

        private static bool Separated(double a, double b, double c, double radius)
        {
            return (a > radius && b > radius && c > radius) || (a < -radius && b < -radius && c < -radius);
        }

        private static bool SeparatedOnEdge(double ax, double az, double bx, double bz, double cx, double cz, Body body)
        {
            double nx = bz - az, nz = ax - bx;
            double edge = nx * ax + nz * az, third = nx * cx + nz * cz;
            double radius = Math.Abs(nx) * body.HalfWidth + Math.Abs(nz) * body.HalfDepth;
            return (edge > radius && third > radius) || (edge < -radius && third < -radius);
        }
    }

    /// <summary>
    /// 末班编组站的班次纯规则：相位与时长、班次事件、首班教学、道岔锁、出口条件、车厢位置与车体包络。
    /// 不碰 Unity 对象，夹具直接编译它跑断言。坐标一律用 three.js 的（运行时换算成 Unity 的 (x, y, -z)）。
    /// 时间是这一趟会话的游戏时间秒（菜单暂停时不走）。
    /// 2026-11-13 快节奏改版（owner「做成快节奏的多趟火车的激战」）：一班从 180 秒压到 118 秒，净空只留 25 秒；
    /// 每趟第 <see cref="LastTrip"/> 班是末班车（重载、必停），它出站后站场封站（<see cref="YardClosed"/>）：不再来车，
    /// 便门、押运门、接客亭都关，北公路照开；末班车停靠与出站时押运车厢尾梯是一个会动的出口（`last`）。
    /// 加价台改成逐班抽（<see cref="PremiumFor"/>），车进站才揭晓。
    /// </summary>
    internal sealed partial class LastShiftTimetable
    {
        internal const double Approach = 15, Arrival = 22, Dwell = 40, Depart = 16, Clear = 25;
        internal const double SlowDwell = 58, ExpressArrival = 13, ExpressDepart = 10;
        /// <summary>末班车是第几班（首班教学那一趟也算第 1 班）；它出站后封站。</summary>
        internal const int LastTrip = 8;
        private const double DwellLeadZ = -38;
        /// <summary>首班的鸣笛预告只等 8 秒（owner 2026-11-11：站在护栏后干等半分钟太闷）；之后每班 15 秒。</summary>
        internal const double TrainingApproach = 8;
        /// <summary>
        /// 首班停靠 90 秒（之后每班 47 秒）：首班停在岔心等你扳道岔，选东台时车身横在司机室和东台之间，
        /// 要绕过车尾走约 226 m（导航网格离线量，西台约 121 m），47 秒第一次来的人赶不上（2026-11-11 流程走查）。
        /// </summary>
        internal const double TrainingDwell = 90;
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
        /// <summary>末班车出站后封站：不再来车、不再扳岔，条件出口全关（北公路照开）。</summary>
        internal bool YardClosed { get; private set; }
        /// <summary>本帧推进前车头所在的 z：车头正面撞击按这一帧扫过的窄带判（列车 owner 判玩家，刷怪 owner 判押运队）。</summary>
        internal double SweptFromZ { get; private set; }
        private readonly int seed;
        /// <summary>这一趟的末班是第几班：恒为 <see cref="LastTrip"/>；只有 Dev 演练快进时往后推（见 LastShiftDrillHooks.cs）。</summary>
        private int lastTrip = LastTrip;

        /// <summary>每次相位切换（老相位、新相位）；运行时用它发字幕、刷导航、开出口、送押运员。</summary>
        internal event Action<LastShiftPhase, LastShiftPhase> PhaseChanged;

        /// <param name="returnVisit">回访（不是第一趟）：跳过首班教学，从班次表的随机时刻开局。</param>
        internal LastShiftTimetable(int seed, bool returnVisit)
        {
            this.seed = seed;
            Line = 'W'; LockedLine = '\0'; Trip = 1; Event = LastShiftEvent.Normal; BellAt = -1;
            Phase = LastShiftPhase.Approach;
            SweptFromZ = LeadZ;
            var r = Stream("raid#run");
            Training = !returnVisit;
            if (!returnVisit) return;
            DispatchChosen = true; CartHit = true; CartHitClock = -10;
            Line = r.NextDouble() < 0.5 ? 'W' : 'E';
            Event = RollEvent(1);
            // 回访落在第 1 班的预告里或它之前的净空里：净空里开局时第 1 班不算（下一班记第 2 班），预告里开局能看满八班。
            if (r.NextDouble() < 0.5) { Phase = LastShiftPhase.Clear; PhaseTime = 4 + r.NextDouble() * 18; }
            else PhaseTime = r.NextDouble() * 10;
            SweptFromZ = LeadZ;
        }

        /// <summary>这一班的加价台（逐班抽，与开箱、扳岔顺序无关）：车进站才在广播里揭晓，故事收尾后余响会提前说。</summary>
        internal char PremiumFor(int trip) { return Stream("premium#" + trip).NextDouble() < 0.5 ? 'W' : 'E'; }

        /// <summary>当前这一班的加价台。</summary>
        internal char Premium { get { return PremiumFor(Trip); } }

        /// <summary>这一班已经进站（或停着、出站中）：加价台在广播里揭晓了。</summary>
        internal bool PremiumRevealed { get { return TrainOnLine && !IsExpress; } }

        internal bool IsLastTrain { get { return Trip >= lastTrip; } }

        /// <summary>下一班（净空里指还没来的那一班，其余相位就是当前这一班）的加价台与事件；封站后无意义。</summary>
        internal char UpcomingPremium { get { return Phase == LastShiftPhase.Clear && !YardClosed ? PremiumFor(Trip + 1) : Premium; } }
        internal LastShiftEvent UpcomingEvent { get { return Phase == LastShiftPhase.Clear && !YardClosed ? RollEvent(Trip + 1) : Event; } }
        internal bool UpcomingIsLast { get { return (Phase == LastShiftPhase.Clear ? Trip + 1 : Trip) >= lastTrip; } }

        /// <summary>包括当前这一班在内，封站前还会来几班（封站后为 0）。</summary>
        internal int TrainsLeft { get { return YardClosed ? 0 : Math.Max(1, lastTrip - Trip + 1); } }

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
            // 末班车总是重载、总要停靠：最长的车影、最后一次签收，押运车厢的尾梯也是最后一个出口。
            if (trip >= lastTrip) return LastShiftEvent.Heavy;
            double total = 0;
            foreach (double w in EventOdds) total += w;
            double x = Stream("trip#" + trip).NextDouble() * total;
            for (int i = 0; i < EventOdds.Length; i++) { if (x < EventOdds[i]) return (LastShiftEvent)i; x -= EventOdds[i]; }
            return LastShiftEvent.Express;
        }

        internal bool IsExpress { get { return Event == LastShiftEvent.Express; } }
        internal bool IsHeavy { get { return Event == LastShiftEvent.Heavy; } }
        internal char LineUsed { get { return LockedLine != '\0' ? LockedLine : Line; } }
        internal bool HasAssignedPlatform { get { return LockedLine != '\0' && !IsExpress && (!Training || (DispatchChosen && !TeachingSwitchWindow)); } }

        private bool TeachingSwitchWindow { get { return Training && Phase == LastShiftPhase.Arrival && PhaseTime <= TrainingHold + 0.001; } }

        internal double PhaseLength(LastShiftPhase p)
        {
            switch (p)
            {
                case LastShiftPhase.Approach: return Training ? TrainingApproach : Approach;
                case LastShiftPhase.Arrival: return IsExpress ? ExpressArrival : Arrival;
                case LastShiftPhase.Dwell: return Training ? TrainingDwell : Event == LastShiftEvent.Slow ? SlowDwell : Dwell;
                case LastShiftPhase.Depart: return IsExpress ? ExpressDepart : Depart;
                default: return Clear;
            }
        }

        internal double SecondsLeft { get { return YardClosed ? 0 : Math.Max(0, PhaseLength(Phase) - PhaseTime); } }

        /// <summary>车头所在的 z（three.js）。</summary>
        internal double LeadZ
        {
            get
            {
                switch (Phase)
                {
                    case LastShiftPhase.Approach: return 78;
                    case LastShiftPhase.Arrival: return 78 - 116 * PhaseTime / PhaseLength(LastShiftPhase.Arrival);
                    case LastShiftPhase.Dwell: return DwellLeadZ;
                    case LastShiftPhase.Depart: return DwellLeadZ - 67 * PhaseTime / PhaseLength(LastShiftPhase.Depart);
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
            SweptFromZ = oldZ;
            Clock += dt;
            if (YardClosed) return oldZ; // 封站：车不再来，只有时钟（拉铃等计时）还在走。
            PhaseTime += dt;
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
                else if (Phase == LastShiftPhase.Depart && IsLastTrain) { YardClosed = true; SetPhase(LastShiftPhase.Clear); }
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
            if (YardClosed) { reason = "closed"; return false; }
            if (Training && !CartHit) { reason = "demo"; return false; }
            if (Phase != LastShiftPhase.Approach && Phase != LastShiftPhase.Clear && !TeachingSwitchWindow) { reason = "locked"; return false; }
            return true;
        }

        internal bool TrySwitch(char line, out string reason)
        {
            if (!CanSwitch(out reason)) return false;
            Line = line; LockedLine = TeachingSwitchWindow ? line : '\0';
            DispatchChosen = true; DispatchWait = false;
            return true;
        }

        internal void MarkCartHit() { if (CartHit) return; CartHit = true; CartHitClock = Clock; }

        internal void RingBell() { if (BellAt < 0) BellAt = Clock; }

        /// <summary>
        /// 出口开没开。北公路恒开；封站后便门、押运门、接客亭都关。`last` 是末班车押运车厢的尾梯：
        /// 末班车停靠和出站时开（会跟着车走，位置由列车 owner 摆），之后随车离站。
        /// </summary>
        internal bool ExitOpen(string id)
        {
            switch (id)
            {
                case "north": return true;
                case "west": return !YardClosed && (LineUsed != 'W' || Phase == LastShiftPhase.Clear);
                case "east": return !YardClosed && EastUnloaded;
                case "bell": return !YardClosed && BellAt >= 0 && Clock - BellAt >= BellWait;
                case "last": return !YardClosed && IsLastTrain && (Phase == LastShiftPhase.Dwell || Phase == LastShiftPhase.Depart);
                default: return false;
            }
        }

        /// <summary>押运车厢（列车最后一节）此刻的位置；没有车时返回 false。</summary>
        internal bool TryVan(out LastShiftCar van)
        {
            van = default(LastShiftCar);
            if (!TrainOnLine) return false;
            List<LastShiftCar> cars = Cars();
            for (int i = cars.Count - 1; i >= 0; i--)
                if (cars[i].Kind == LastShiftCarKind.Van) { van = cars[i]; return true; }
            return false;
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
            return Phase == LastShiftPhase.Clear ? new List<LastShiftCar>() : BuildCars(LineUsed, LeadZ, IsHeavy);
        }

        internal static LastShiftParkedTrain ParkedTrain(char line, bool heavy)
        {
            return new LastShiftParkedTrain(BuildCars(line, DwellLeadZ, heavy));
        }

        private static List<LastShiftCar> BuildCars(char line, double lead, bool heavy)
        {
            var cars = new List<LastShiftCar>(5);
            double offset = 0;
            int[] models = heavy ? new[] { 0, 1, 2, 4, 3 } : new[] { 0, 1, 2, 3 };
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
            return cars.Count > 0 && FrontHit(cars[0], oldLeadZ, x, z, groundY);
        }

        /// <summary>同上，车头那节由调用方先取好（一帧里判多个人时只排一次车厢）。</summary>
        internal bool FrontHit(LastShiftCar c, double oldLeadZ, double x, double z, double groundY)
        {
            if (Phase != LastShiftPhase.Arrival && Phase != LastShiftPhase.Depart) return false;
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

        internal static bool TrainBlocksTriangle(double ax, double ay, double az, double bx, double by, double bz,
            double cx, double cy, double cz, char line, LastShiftParkedTrain parked)
        {
            if (Math.Min(ay, Math.Min(by, cy)) >= 4.75) return false;
            if (parked != null) return parked.Intersects(ax, az, bx, bz, cx, cz);
            return InEnvelope((ax + bx + cx) / 3.0, (az + bz + cz) / 3.0, line);
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
