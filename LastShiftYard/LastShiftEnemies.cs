using System;
using System.Collections.Generic;
using Duckov.Utilities;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 末班编组站的刷怪 owner。2026-11-13 快节奏改版（owner「快节奏的多趟火车的激战；走到每个有宝箱的地方都有激战」）后：
    /// - 三本账（2026-11-13 owner「每趟火车来都会刷新，这样比较有激战的感觉」后分开）：
    ///   巡逻、固定岗与押运长：全趟可遇 <see cref="PopulationMin"/>–<see cref="PopulationMin"/>+6 人，同时活动 ≤ <see cref="LiveCap"/>，
    ///   流动（非固定岗）≤ <see cref="MobileCap"/>（封站后 +2）；北门、南班、押运库三处固定岗各保一人。
    ///   列车押运：不占全趟名额，每班停靠一波，同时在场 ≤ <see cref="TrainLiveCap"/>；新一波下车时，上一波里远在玩家视野外、没在交火的人跟车走了（收掉）。
    ///   守货岗（LastShiftSquads.cs）：每处有箱子的地方一队，同时在场 ≤ <see cref="GarrisonCap"/> 人、全趟 ≤ <see cref="GarrisonPopulation"/> 人；
    ///   来车给停靠那一侧打光的守货岗补岗。
    /// - 只在离玩家 25–75 m、可走、2.75 m 内没有活人的点刷；开局一批，之后补员间隔随签收柜数缩短（16 秒起，每签一柜 −3 秒，最少 8 秒）。
    /// - 来车时流动押运跟车去那一台的封签货两侧；停靠时押运车厢在车尾放下一波押运员：3 人起，每签一柜多 1 人（最多 5 人），
    ///   签过两柜起其中一人是小队长；末班车再多 1 人、守在尾梯边（玩家 25 m 内不在车尾放，改在货台放）。首班示范与直通快车不放人。
    /// - 车头正面撞到押运员：普通押运当场撞死，小队长掉六成血，押运长掉三成（一班车一人只撞一次），算玩家的。
    /// - 库房警铃召回最近 3 名押运员 40 秒；轨道车铃引附近巡逻 35 秒；维修棚蜂鸣器引最多 2 名普通巡逻 10 秒（见 LastShiftYardDevices.cs）。
    /// - 押运长守封签货：首次锁线后在停靠那一台的铅封柜后面生成，之后随道岔指向换台；
    ///   押运库那一岗由普通押运员顶。回访局 30% 押运长坐第一班停靠货车的押运车来，在停靠那一台下车。
    /// - 封站后搜站：每 10 秒把没在交火的流动押运派到玩家附近；北公路设卡（守货岗里的 checkpoint 一队）。
    /// 这些都只改官方 AI 的 <c>patrolPosition</c> / <c>MoveToPos</c>；发现玩家、交火与追击全交还官方行为树。
    /// 刷出的六步同潮闸镇：关距离激活 → 官方创建 → 导航限定本图 → 解除距离休眠 → 敌对性安全网 `SetTeam(Teams.wolf)` → 挂到世界根下。
    /// 远处安静的敌人整只停用、走近恢复（55 / 65 m 滞回）；押运长、交火中、赶往货台 / 警铃 / 铃声 / 蜂鸣器的人豁免。
    /// 坐标：作者标记已是 Unity 坐标；原型里的固定点（货台、甲板、蜂鸣器目标）按 three.js (x, z) → Unity (x, y, -z) 换算。
    /// </summary>
    internal sealed partial class LastShiftEnemies : IDisposable
    {
        private const float MinSpawn = 25f, MaxSpawn = 75f, Crowd = 2.75f, ThinkInterval = .5f;
        internal const int LiveCap = 10, MobileCap = 5, AlarmRecall = 3, PopulationMin = 40;
        /// <summary>押运车厢每班放下的人：基础、每签一柜加几人、上限；末班车另加。</summary>
        internal const int DropBase = 3, DropPerSignature = 1, DropMax = 5, DropLastTrainExtra = 1, LeaderDropFrom = 2;
        /// <summary>列车押运同时在场上限（另一本账，不占巡逻名额与全趟人数）。</summary>
        internal const int TrainLiveCap = 10;
        private const float ReinforceAfter = 16f, ReinforceHeatStep = 3f, ReinforceMin = 8f, ReinforceRetry = 2f;
        private const float AlarmSeconds = 40f, BellSeconds = 35f, BellRadius = 50f, HuntEvery = 10f, HuntSeconds = 10f;
        /// <summary>车头撞小队长、押运长时掉的血（占满血比例）；普通押运当场撞死。</summary>
        internal const float StrikeLeaderShare = .6f, StrikeChiefShare = .3f;
        internal const float ActivityWakeRadius = 55f, ActivitySleepRadius = 65f;
        internal const double ChiefRideChance = .3;
        /// <summary>押运长岗位离铅封柜中心的距离（柜子 2.35 m 宽，站在柜后约 2 m 处）。</summary>
        internal const float ChiefPostDistance = 3.2f;
        private static readonly string[] DutyPosts = { "postNorth", "postSouth", "postBond" };
        /// <summary>原型守卫跟车时围着封签货站的五个角位（相对货箱背面方向的弧度）。</summary>
        private static readonly float[] CargoAngles = { -1.45f, -.8f, .8f, 1.45f, -1.12f };

        private sealed class Spot
        {
            internal Transform Marker;
            internal string Id, Group, Type;
            internal int Quality;
        }

        private sealed class Guard
        {
            internal string Id, Group, Duty;
            internal Spot Spot;
            internal LastShiftPost Post;
            internal int Quality, Index, DwellTrip = -1;
            internal bool Boss, FromTrain, IgnoreRing, Regular;
            internal Vector3 Position, Forward, Home;
            internal bool Spawning, Failed;
            internal float RetryAfter;
            internal CharacterMainControl Character;
            internal AICharacterController Ai;
            internal List<Vector3> Route;
            internal int Wp;
            internal float HoldUntil = -1f, NextMove;
            internal int GoalKey;
            internal bool Lured;
            internal Vector3 LureTarget, LureBack;
            internal float LureUntil;
            internal int LureSerial;
            internal float BellUntil = -1f;
            internal Vector3 BellTarget;
            internal char CargoLine;
            internal Vector3 CargoGoal;
            internal bool Suspended;
            /// <summary>守货岗成员（LastShiftSquads.cs）：所属小队、圈上位次、冻着没有、冻结前的 defaultWeaponOut、错开醒来的时刻。</summary>
            internal Squad Squad;
            internal int Slot;
            internal bool Frozen, SavedWeaponOut, WakeToPlayer;
            internal float WakeDue = -1f, RestHealth;
            /// <summary>小队长的招式状态（LastShiftLeaders.cs）；普通押运为空。</summary>
            internal LeaderState Lead;
            /// <summary>末班车放下的押运员：守在押运车厢尾梯边。</summary>
            internal bool VanGuard;
            /// <summary>上一次被车头撞到是哪一班（一班车一人只撞一次，进站撞过出站不再撞）。</summary>
            internal int StruckKey = -1;
        }

        private readonly LastShiftWorld world;
        private readonly LastShiftTimetable timetable;
        private readonly ArenaPrototypeNavigation navigation;
        private readonly CharacterMainControl player;
        private readonly Func<bool> valid;
        private readonly Action<string, bool> status;
        private readonly List<Spot> spots = new List<Spot>();
        private readonly Dictionary<string, List<Vector3>> routes = new Dictionary<string, List<Vector3>>(StringComparer.Ordinal);
        private readonly List<Guard> guards = new List<Guard>();
        private readonly List<Guard> queue = new List<Guard>();
        private readonly List<Spot> picks = new List<Spot>();
        private readonly System.Random roster;
        private readonly int population;
        private readonly Spot chiefSpot;
        private CharacterRandomPreset scav;
        private TidegateLoot.PrizeCatalog spareCatalog;
        private Guard chief;
        private bool chiefRides, closed, subscribed;
        private int serial, spawned, garrisonSpawned, trainSpawned, inFlight, lureSerial, landedTrip = -1, leaderSerial;
        private float nextThink, nextReinforce, nextActivity, nextHunt, nextStrikeLine;

        /// <summary>押运长已倒下（站务记录、剧情用）。</summary>
        internal bool BossDefeated { get; private set; }
        /// <summary>押运长这趟是坐押运车来的（站务记录「击倒随车而来的押运长」）。</summary>
        internal bool BossRodeTrain { get; private set; }
        /// <summary>巡逻、固定岗与押运长已刷出几人（列车押运、守货岗另记，见 <see cref="TrainSpawned"/>、<see cref="GarrisonSpawned"/>）。</summary>
        internal int SpawnedCount { get { return spawned; } }
        internal int Population { get { return population; } }
        internal int GarrisonSpawned { get { return garrisonSpawned; } }
        internal int TrainSpawned { get { return trainSpawned; } }
        /// <summary>被车头撞倒的押运（站务与日志用）。</summary>
        internal int TrainKills { get; private set; }
        /// <summary>会话可接上搜刮 owner 的私货抽签（带着旧钢印再打倒押运长时换成的那一件）；没接时用本 owner 自带的同口径抽签。</summary>
        internal Func<int> BossSpare { get; set; }
        /// <summary>押运警戒：本趟已签几柜（会话接 <see cref="LastShiftLoot.Signatures"/>）。签得越多，押运车厢放下的人越多、补员越快。</summary>
        internal Func<int> Heat { get; set; }

        private int CurrentHeat()
        {
            if (Heat == null) return 0;
            try { return Math.Max(0, Heat()); }
            catch (Exception) { return 0; }
        }

        internal LastShiftEnemies(LastShiftWorld world, LastShiftTimetable timetable, ArenaPrototypeNavigation navigation, CharacterMainControl player,
            int seed, bool returnVisit, Func<bool> valid, HashSet<Transform> unreachable, Action<string, bool> status)
        {
            this.world = world; this.timetable = timetable; this.navigation = navigation; this.player = player;
            this.valid = valid; this.status = status ?? delegate { };
            population = PopulationMin + (seed & 0x7fffffff) % 7;
            roster = timetable.Stream("enemies#roster");
            LastShiftEnemyIdentity.InjectLabels();
            foreach (Transform marker in world.SpawnMarkers)
            {
                // 原押运长标记只提供角色档案，铅封柜旁的真实落点在生成前另核地面。
                if (marker == null || (marker.name != "bossSpawn" && unreachable != null && unreachable.Contains(marker))) continue;
                string type; int quality;
                Describe(marker.name, out type, out quality);
                spots.Add(new Spot { Marker = marker, Id = marker.name, Group = marker.parent != null ? marker.parent.name : "outer", Type = type, Quality = quality });
            }
            chiefSpot = FindSpot("bossSpawn");
            CollectRoutes();
            BuildDevices();
            BuildSquads();
            BuildLeaderProps();
            timetable.PhaseChanged += OnPhase;
            subscribed = true;
            chiefRides = returnVisit && timetable.Stream("raid#chiefRide").NextDouble() < ChiefRideChance;
            Seed();
            Debug.Log(LastShiftRaidScene.LogTag + " ENEMIES_READY spots=" + spots.Count + " population=" + population + " queued=" + queue.Count + " chiefRides=" + chiefRides);
        }

        /// <summary>刷点的岗位类型与武器档（布局 spawns 的 type / quality；标记只带名字）。</summary>
        private static void Describe(string id, out string type, out int quality)
        {
            switch (id)
            {
                case "w2": case "w3": case "e2": case "bondWatch0": case "bondWatch1": type = "guard"; quality = 2; return;
                case "escort0": type = "guard"; quality = 3; return;
                case "e3": type = "leader"; quality = 3; return;
                case "bossSpawn": type = "boss"; quality = 3; return;
                case "e1": type = "regular"; quality = 2; return;
                default: type = "regular"; quality = 1; return;
            }
        }

        // 巡逻点：Markers/Waypoints/<组>/<巡逻>_<序号>，每组一条，按序号排；吸到本图导航上。
        private void CollectRoutes()
        {
            Transform root = world.Root.transform.Find("Markers/Waypoints");
            if (root == null) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 缺少巡逻点 Markers/Waypoints，巡逻改为原地看守"); return; }
            foreach (Transform group in root)
            {
                var points = new List<KeyValuePair<int, Vector3>>();
                foreach (Transform wp in group)
                {
                    int cut = wp.name.LastIndexOf('_'), k;
                    if (cut < 0 || !int.TryParse(wp.name.Substring(cut + 1), out k)) continue;
                    Vector3 snapped;
                    points.Add(new KeyValuePair<int, Vector3>(k, navigation.TryNearestWalkable(wp.position, out snapped) ? snapped : wp.position));
                }
                points.Sort((a, b) => a.Key.CompareTo(b.Key));
                var list = new List<Vector3>(points.Count);
                foreach (var p in points) list.Add(p.Value);
                if (list.Count > 0) routes[group.name] = list;
            }
        }

        // 原型 attachActors：北门与南班各一名固定岗、外线与东西场四名流动；此刻不合规的点不补，留给后面的补员。
        // 守货岗不在这里开：TickGarrisons 按玩家走近每处有箱子的地方逐处开（另一本账）。
        private void Seed()
        {
            foreach (string post in new[] { "postNorth", "postSouth" })
                foreach (Spot spot in spots)
                    if (spot.Group == post && Eligible(spot.Marker.position)) { queue.Add(NewGuard(spot, spot.Marker.position, post)); break; }
            SeedMobile("s0", 0f, 0f); SeedMobile("w0", -1.5f, -1f); SeedMobile("w1", 1.6f, 1f); SeedMobile("e0", 0f, 0f);
        }

        private void QueueChiefAtAssignedPlatform()
        {
            if (chiefRides || chief != null || chiefSpot == null || !timetable.HasAssignedPlatform) return;
            char line = timetable.LineUsed;
            chief = NewGuard(chiefSpot, ChiefPost(line), null);
            chief.IgnoreRing = true;
            chief.CargoLine = line;
            queue.Insert(0, chief);
        }

        private void SeedMobile(string id, float dx, float dz)
        {
            Spot spot = FindSpot(id);
            if (spot == null) return;
            Vector3 at = spot.Marker.position + new Vector3(dx, 0f, -dz), snapped;
            if (!navigation.TryNearestWalkable(at, out snapped)) snapped = spot.Marker.position;
            if (Eligible(snapped)) queue.Add(NewGuard(spot, snapped, null));
        }

        private Spot FindSpot(string id)
        {
            foreach (Spot spot in spots) if (spot.Id == id) return spot;
            return null;
        }

        private Guard NewGuard(Spot spot, Vector3 position, string duty)
        {
            var g = new Guard
            {
                Id = spot.Id + "#" + serial, Spot = spot, Group = spot.Group, Duty = duty, Quality = spot.Quality, Index = serial++,
                Boss = spot.Type == "boss", Regular = spot.Type == "regular", Position = position,
                Forward = spot.Marker.forward, Post = PostFor(spot.Type, spot.Group),
            };
            List<Vector3> route;
            if (routes.TryGetValue(duty ?? spot.Group, out route) || routes.TryGetValue("outer", out route)) g.Route = route;
            return g;
        }

        private static LastShiftPost PostFor(string type, string group)
        {
            switch (type)
            {
                case "boss": return LastShiftPost.Chief;
                case "leader": return LastShiftPost.Captain;
                case "guard": return LastShiftPost.Escort;
                default: return group == "west" || group == "east" ? LastShiftPost.Inspector : LastShiftPost.Walker;
            }
        }

        // ---------------- 每帧 ----------------
        internal void Tick()
        {
            if (closed || valid == null || !valid() || player == null) return;
            float now = Time.time;
            TickTrainStrikes(now);
            TickDevices(now);
            TickActivity(now);
            TickSquads(now);
            TickLeaders(now);
            QueueChiefAtAssignedPlatform();
            if (now >= nextThink) { nextThink = now + ThinkInterval; Think(now); }
            if (timetable.YardClosed && now >= nextHunt) { nextHunt = now + HuntEvery; Hunt(now); }
            if (inFlight > 0) return;
            if (queue.Count > 0) { ServeQueue(now); return; }
            if (now >= nextReinforce) nextReinforce = now + (Replenish() ? ReinforceInterval() : ReinforceRetry);
        }

        /// <summary>补员间隔：16 秒起，每签一柜 −3 秒，最少 8 秒；封站后按最短的算。</summary>
        private float ReinforceInterval()
        {
            if (timetable.YardClosed) return ReinforceMin;
            return Mathf.Max(ReinforceMin, ReinforceAfter - ReinforceHeatStep * CurrentHeat());
        }

        private int MobileLimit { get { return MobileCap + (timetable.YardClosed ? 2 : 0); } }

        private void ServeQueue(float now)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                Guard g = queue[i];
                if (g.DwellTrip >= 0 && (timetable.Phase != LastShiftPhase.Dwell || timetable.Trip != g.DwellTrip)) { queue.RemoveAt(i--); continue; }
                if (now < g.RetryAfter) continue;
                queue.RemoveAt(i);
                // 守货岗的座位在开张时已经核过；成员彼此只隔两三米，不走流动的「2.75 m 内没人」那条。
                if (g.Squad != null) { if (GarrisonLive() >= GarrisonCap || garrisonSpawned >= GarrisonPopulation) { g.Failed = true; return; } }
                else if (!g.Boss && (!CanAdd(g.Duty, g.FromTrain) || !Eligible(g.Position))) return; // 原型 newGuard 返回空：这一名不刷。
                Spawn(g);
                return;
            }
        }

        // 原型 replenish：先补没人的固定岗，再补流动；返回这一次有没有开刷。守货岗另走 TickGarrisons。
        private bool Replenish()
        {
            if (Live() >= LiveCap || PopulationSeats() <= 0) return false;
            foreach (string post in DutyPosts)
            {
                if (PostHeld(post)) continue;
                Spot spot = PickSpot(post, true);
                if (spot == null) continue;
                Spawn(NewGuard(spot, spot.Marker.position, post));
                return true;
            }
            if (MobileLive() >= MobileLimit) return false;
            Spot mobile = PickSpot(null, false);
            if (mobile == null) return false;
            Spawn(NewGuard(mobile, mobile.Marker.position, null));
            return true;
        }

        private Spot PickSpot(string post, bool duty)
        {
            picks.Clear();
            foreach (Spot spot in spots)
            {
                bool match = duty ? spot.Group == post : Array.IndexOf(DutyPosts, spot.Group) < 0 && spot.Type != "boss";
                if (match && Eligible(spot.Marker.position)) picks.Add(spot);
            }
            return picks.Count == 0 ? null : picks[roster.Next(picks.Count)];
        }

        private static bool IsAlive(Guard g)
        {
            return g.Character != null && g.Character.Health != null && !g.Character.Health.IsDead;
        }

        private static bool Counts(Guard g) { return g.Spawning || IsAlive(g); }

        private bool ChiefPending()
        {
            return chiefSpot != null && !BossDefeated && (chief == null || (!chief.Failed && !Counts(chief)));
        }

        /// <summary>列车押运（押运车厢放下的人，押运长随车不算）：另一本账。</summary>
        private static bool TrainEscort(Guard g) { return g.FromTrain && !g.Boss; }

        private int PopulationSeats()
        {
            int flying = 0;
            foreach (Guard g in guards) if (g.Spawning && g.Squad == null && !TrainEscort(g)) flying++;
            return population - spawned - flying - (ChiefPending() ? 1 : 0);
        }

        /// <summary>巡逻、固定岗与押运长此刻在场几人（列车押运、守货岗不算，另见 <see cref="TrainLive"/>、<see cref="GarrisonLive"/>）。</summary>
        private int Live()
        {
            // 等锁线与等押运车都预留名额；生成中或已在场时由 guards 计数。
            int n = ChiefPending() ? 1 : 0;
            foreach (Guard g in guards) if (g.Squad == null && !TrainEscort(g) && Counts(g)) n++;
            return n;
        }

        /// <summary>列车押运此刻在场（含正在生成）几人。</summary>
        private int TrainLive()
        {
            int n = 0;
            foreach (Guard g in guards) if (TrainEscort(g) && Counts(g)) n++;
            return n;
        }

        /// <summary>流动巡逻此刻几人（押运车厢放下的人另算，见 <see cref="CanAdd"/>）。</summary>
        private int MobileLive()
        {
            int n = 0;
            foreach (Guard g in guards) if (g.Duty == null && !g.Boss && !g.FromTrain && Counts(g)) n++;
            return n;
        }

        private bool PostHeld(string post)
        {
            // 押运长守的是封签货（GuardCargo），押运库那一岗由普通押运员顶
            foreach (Guard g in guards) if (g.Duty == post && Counts(g)) return true;
            return false;
        }

        /// <summary>押运车厢放下的人走自己那本账（同时在场 ≤ <see cref="TrainLiveCap"/>，不占全趟人数）：每班都能下来一波，不被巡逻占满。</summary>
        private bool CanAdd(string duty, bool fromTrain = false)
        {
            if (fromTrain) return TrainLive() < TrainLiveCap;
            if (PopulationSeats() <= 0) return false;
            if (Live() >= LiveCap) return false;
            return duty != null ? !PostHeld(duty) : MobileLive() < MobileLimit;
        }

        private bool Eligible(Vector3 p)
        {
            float sqr = (p - player.transform.position).sqrMagnitude;
            if (sqr < MinSpawn * MinSpawn || sqr > MaxSpawn * MaxSpawn) return false;
            Vector3 snapped;
            if (!navigation.TryNearestWalkable(p, out snapped) || (snapped - p).sqrMagnitude > 4f) return false;
            foreach (Guard g in guards)
                if (IsAlive(g) && (g.Character.transform.position - p).sqrMagnitude < Crowd * Crowd) return false;
            return true;
        }

        // ---------------- 刷出 ----------------
        private CharacterRandomPreset SourceFor(Guard g)
        {
            if (g.Boss)
            {
                CharacterRandomPreset named = SkyIslandEnemySources.Named(LastShiftEscortChief.SourceName);
                if (named != null) return named;
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 找不到官方「路障」预设，押运长改用拾荒者底模");
            }
            if (scav == null) scav = SkyIslandEnemySources.Named("EnemyPreset_Scav") ?? SkyIslandEnemySources.ScavReference();
            return scav; // 普通点绝不退回到 Boss 池；没有官方拾荒者就明确报刷怪失败。
        }

        private async void Spawn(Guard g)
        {
            if (!guards.Contains(g)) guards.Add(g);
            g.Spawning = true;
            inFlight++;
            CharacterRandomPreset clone = null;
            CharacterMainControl created = null;
            bool retained = false;
            try
            {
                Vector3 position;
                // 小队成员只站自己那个已核过的位置（不就近挪开，否则圈就散了）。
                bool stood = g.Squad != null ? CanStand(g.Position, out position) : Footing(g.Position, out position);
                // 守货岗成员不走刷怪环（开张时站位已核），只要求不在玩家眼前冒出来。
                bool tooClose = g.Squad != null && FlatDistance(position, player.transform.position) < GarrisonSpawnMin;
                if (!stood || tooClose || (!g.IgnoreRing && !InRing(position)))
                {
                    // 押运长必到：3 秒后再试；其余这一名不刷，名额留给后面的补员。
                    if (g.Boss) { g.RetryAfter = Time.time + 3f; queue.Add(g); }
                    return;
                }
                CharacterRandomPreset source = SourceFor(g);
                if (source == null) throw new InvalidOperationException("找不到官方敌人预设");
                clone = UnityEngine.Object.Instantiate(source);
                clone.name = "BossRush_LastShift_" + g.Id;
                clone.dropBoxOnDead = true;
                clone.setActiveByPlayerDistance = false;
                LastShiftEnemyIdentity.Tune(clone, g.Post);
                if (g.Boss) LastShiftEscortChief.Tune(clone);
                else LastShiftCombatTuning.Apply(clone, g.Post);
                // 固定岗无事时只在岗位附近转，保留官方选敌、交火与追击。
                if (g.Duty != null && !g.Boss) clone.patrolRange = Mathf.Min(clone.patrolRange, g.Squad != null ? 2.5f : 3.5f);
                // 末班押运队是一伙人：同阵营，不互打。
                clone.team = Teams.scav;
                created = await clone.CreateCharacterAsync(position, g.Forward, -1, null, false);
                if (closed || valid == null || !valid() || world.Root == null) return;
                if (created == null) throw new InvalidOperationException("官方角色创建失败");
                Seeker[] seekers = created.GetComponentsInChildren<Seeker>(true);
                AICharacterController ai = created.GetComponentInChildren<AICharacterController>();
                if (seekers.Length == 0 || ai == null) throw new InvalidOperationException("角色缺少战斗 AI / 导航");
                foreach (Seeker seeker in seekers) { seeker.CancelCurrentPathRequest(); seeker.graphMask = navigation.Mask; }
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(created);
                created.SetTeam(clone.team);
                if (!Team.IsEnemy(created.Team, Teams.player)) created.SetTeam(Teams.wolf);
                created.transform.SetParent(world.Root.transform, true);
                // 东台队长、押运护卫与三种小队长按原定武器 Q3（天空岛普通档 3–4，达标的官方原装不换）；其余保留官方原装。
                if (!g.Boss && (g.Quality >= 3 || IsLeaderPost(g.Post))) SkyIslandEnemyArmory.Arm(created, SkyIslandEnemyTier.Scav);
                LastShiftEnemyIdentity.Dress(created, g.Post);
                // 克隆 preset 要活到角色销毁：官方死亡掉落、经验都还会读它。
                created.gameObject.AddComponent<TidegatePresetOwner>().Preset = clone;
                clone = null;
                g.Character = created; g.Ai = ai; g.Home = g.Boss && g.FromTrain ? ChiefPost(g.CargoLine) : position; g.GoalKey = 0;
                g.Wp = NearestWaypoint(g.Route, position);
                ai.patrolPosition = position;
                if (g.Squad != null) SquadMemberReady(g);
                if (IsLeaderPost(g.Post)) BindLeader(g);
                if (g.Boss)
                {
                    chief = g;
                    created.gameObject.AddComponent<LastShiftEscortChief>().Bind(created, ai, g.FromTrain, valid, PickSpare, OnChiefDown, world.TryGroundAt);
                    if (g.FromTrain)
                    {
                        BossRodeTrain = true;
                        status(L10n.T("押运长从押运车上跳下来，就在", "The chief escort jumps off the brake van at the ")
                            + LastShiftText.PlatformName(g.CargoLine) + L10n.T("台！", " platform!"), true);
                    }
                }
                else if (g.FromTrain && landedTrip != timetable.Trip)
                {
                    landedTrip = timetable.Trip;
                    status(L10n.T("押运车在", "The brake van drops escorts at the ") + LastShiftText.PlatformName(timetable.LineUsed)
                        + L10n.T("台放下了押运员。", " platform."), false);
                }
                retained = true;
                if (g.Squad != null) garrisonSpawned++; else if (TrainEscort(g)) trainSpawned++; else spawned++;
                Debug.Log(LastShiftRaidScene.LogTag + " ENEMY_READY id=" + g.Id + " post=" + g.Post + " duty=" + (g.Duty ?? "-") + " preset=" + source.name
                    + " team=" + created.Team + " live=" + Live() + " spawned=" + spawned + "/" + population
                    + " train=" + TrainLive() + " trainSpawned=" + trainSpawned
                    + " garrison=" + GarrisonLive() + " garrisonSpawned=" + garrisonSpawned + "/" + GarrisonPopulation);
            }
            catch (Exception e)
            {
                g.Failed = true;
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 刷怪失败 " + g.Id + "：" + e.Message);
            }
            finally
            {
                g.Spawning = false;
                if (!retained && created != null) { created.gameObject.SetActive(false); UnityEngine.Object.Destroy(created.gameObject); }
                if (clone != null) UnityEngine.Object.Destroy(clone, created != null ? .1f : 0f);
                if (!retained && !g.Boss) guards.Remove(g);
                if (!retained && g.Boss && !queue.Contains(g)) guards.Remove(g);
                inFlight--;
            }
        }

        private bool InRing(Vector3 position)
        {
            float sqr = (position - player.transform.position).sqrMagnitude;
            return sqr >= MinSpawn * MinSpawn && sqr <= MaxSpawn * MaxSpawn;
        }

        // 先吸到本图导航最近的可走面：落在设备 / 货箱实心碰撞盒里的点会让人生在盒子里（同潮闸镇）。
        private bool Footing(Vector3 near, out Vector3 position)
        {
            Vector3 snapped;
            position = near;
            if (!navigation.TryNearestWalkable(near, out snapped)) return false;
            if (CanStand(snapped, out position)) return true;
            for (int radius = 2; radius <= 8; radius += 3)
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI * .25f;
                    Vector3 probe = snapped + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius), q;
                    if (navigation.TryNearestWalkable(probe, out q) && (q - snapped).sqrMagnitude <= 81f && CanStand(q, out position)) return true;
                }
            return false;
        }

        private bool CanStand(Vector3 point, out Vector3 position)
        {
            position = point;
            float ground;
            if (!world.TryGroundAt(point, out ground)) return false;
            position.y = ground + .05f;
            return !Physics.CheckCapsule(position + Vector3.up * .45f, position + Vector3.up * 1.25f, .25f,
                GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore);
        }

        private static int NearestWaypoint(List<Vector3> route, Vector3 at)
        {
            if (route == null) return 0;
            int best = 0; float bestSqr = float.MaxValue;
            for (int i = 0; i < route.Count; i++)
            {
                float d = (route[i] - at).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = i; }
            }
            return best;
        }

        private int PickSpare()
        {
            if (BossSpare != null)
            {
                try { int id = BossSpare(); if (id > 0) return id; }
                catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 押运私货抽签失败，改用自带目录：" + e.Message); }
            }
            if (spareCatalog == null) spareCatalog = new TidegateLoot.PrizeCatalog();
            return LastShiftEscortChief.RollSpare(spareCatalog, timetable.Stream("raid#chiefSpare"));
        }

        private void OnChiefDown()
        {
            BossDefeated = true;
            Debug.Log(LastShiftRaidScene.LogTag + " CHIEF_DOWN rodeTrain=" + BossRodeTrain);
        }

        // ---------------- 列车：送押运员、送押运长 ----------------
        private void OnPhase(LastShiftPhase previous, LastShiftPhase next)
        {
            if (closed || valid == null || !valid()) return;
            try
            {
                // 末班车一进站，北公路的卡口就开始拉路障（守货岗里的 checkpoint 一队）；封站后搜站。
                if (next == LastShiftPhase.Arrival && timetable.IsLastTrain) checkpointWanted = true;
                if (next == LastShiftPhase.Clear && timetable.YardClosed) { checkpointWanted = true; nextHunt = Time.time + 3f; }
                if (next == LastShiftPhase.Arrival || (previous == LastShiftPhase.Dwell && next == LastShiftPhase.Depart))
                {
                    // 列车 owner 已封线：押运长不能沿净空时的换台路径继续横穿，停靠时的绕车路径也不能带进发车相位。
                    foreach (Guard g in guards)
                    {
                        if (!IsAlive(g) || g.Suspended) continue;
                        if (next == LastShiftPhase.Arrival && !g.Boss) continue;
                        PrepareNavigation(g.Character);
                        g.GoalKey = 0;
                    }
                    return;
                }
                if (next != LastShiftPhase.Dwell) return;
                char line = timetable.LineUsed;
                // 原型 deckPoint：西台 (-35.5, -28)、东台 (35.5, -28)（three.js）。
                float deckX = line == 'W' ? -35.5f : 35.5f;
                if (chiefRides && ChiefPending() && !queue.Contains(chief))
                {
                    chief = NewGuard(chiefSpot, Snap(Three(deckX, -24f)), null);
                    chief.IgnoreRing = true; chief.FromTrain = true; chief.DwellTrip = timetable.Trip;
                    chief.CargoLine = line;
                    queue.Insert(0, chief);
                    return; // 原型 trainArrived：押运长下车的那一班不另送押运员。
                }
                if (timetable.Training) return;
                // 这一班停到哪一侧，那一侧打光的守货岗重新有人；上一波没交过火、远在视野外的列车押运跟车走了，座位留给这一波。
                RestaffSide(line);
                int left = SendOffIdleTrainEscorts();
                Spot reference = FindSpot(line == 'W' ? "w2" : "e2");
                if (reference == null) return;
                // 押运车厢在车尾放人：离签收柜约 30 m，签收的人看不见他们刷出来，却能看见他们从车尾跑过来。
                // 玩家贴着车尾（25 m 内）时改在货台北端放；两处都不合规的那几名本班不放。
                int heat = CurrentHeat();
                int count = Math.Min(DropMax, DropBase + DropPerSignature * heat) + (timetable.IsLastTrain ? DropLastTrainExtra : 0);
                bool leader = heat >= LeaderDropFrom || timetable.IsLastTrain;
                LastShiftCar van;
                bool hasVan = timetable.TryVan(out van);
                for (int i = 0; i < count; i++)
                {
                    Vector3 at = hasVan ? VanSlot(van, line, i) : Snap(Three(deckX, -28f - 3f * i));
                    if (!Eligible(at)) at = Snap(Three(deckX, -28f - 3f * (i % 3)));
                    Guard g = NewGuard(reference, at, null);
                    g.FromTrain = true; g.DwellTrip = timetable.Trip;
                    g.VanGuard = timetable.IsLastTrain;
                    if (leader && i == 0) MakeLeader(g, NextLeaderPost());
                    queue.Add(g);
                }
                Debug.Log(LastShiftRaidScene.LogTag + " TRAIN_DROP trip=" + timetable.Trip + " heat=" + heat + " count=" + count + " leader=" + leader + " van=" + hasVan
                    + " sentOff=" + left + " trainLive=" + TrainLive());
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 列车送人失败：" + e.Message); }
        }

        /// <summary>
        /// 新一波下车前，上一波列车押运里离玩家 <see cref="ActivitySleepRadius"/> m 外（屏幕外好几屏）、没被叫走也没在交火的，跟着车走了：
        /// 整只收掉（列车押运本来就另记一本账，不退全趟名额）。来车时流动押运都醒着跟车，所以按距离判，不看是否挂起。
        /// 留下的是玩家附近、正在打的那些。返回收掉几人。
        /// </summary>
        private int SendOffIdleTrainEscorts()
        {
            int n = 0;
            Vector3 at = player.transform.position;
            for (int i = guards.Count - 1; i >= 0; i--)
            {
                Guard g = guards[i];
                if (!TrainEscort(g) || !IsAlive(g) || g.Lured || g.Spawning) continue;
                if (FlatDistance(g.Character.transform.position, at) <= ActivitySleepRadius) continue;
                if (g.Ai != null && !g.Suspended && Engaged(g, 6f)) continue;
                queue.Remove(g);
                guards.RemoveAt(i);
                DisposeLeader(g);
                g.Character.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(g.Character.gameObject);
                g.Character = null;
                n++;
            }
            return n;
        }

        /// <summary>押运车厢靠货台一侧、从车尾往车头排开的落脚点（第 <paramref name="index"/> 个，间隔 3 m，避开「2.75 m 内没有活人」）。</summary>
        private Vector3 VanSlot(LastShiftCar van, char line, int index)
        {
            double co = Math.Cos(van.Yaw), sn = Math.Sin(van.Yaw), side = line == 'W' ? -1 : 1;
            double along = van.Depth * 0.5 - LastShiftTrains.VanStepBack - 3.0 * index, across = van.Width * 0.5 + LastShiftTrains.VanStepAcross + .4;
            double x = van.X + sn * along + co * side * across, z = van.Z + co * along - sn * side * across;
            return Snap(new Vector3((float)x, 0f, -(float)z));
        }

        private static Vector3 Three(float x, float z) { return new Vector3(x, 0f, -z); }

        private Vector3 Snap(Vector3 p)
        {
            Vector3 snapped;
            return navigation.TryNearestWalkable(p, out snapped) ? snapped : p;
        }

        // ---------------- 警铃与铃声 ----------------
        /// <summary>库房警铃（打开风险柜时由会话调用，每只柜一次）：离它最近的 3 名活着的流动押运员赶回来 40 秒，再回原处。</summary>
        internal void Alarm(Vector3 at)
        {
            if (closed) return;
            Guard a = null, b = null, c = null;
            float da = float.MaxValue, db = float.MaxValue, dc = float.MaxValue;
            foreach (Guard g in guards)
            {
                if (!IsAlive(g) || g.Duty != null || g.Boss || g.Ai == null) continue;
                float d = (g.Character.transform.position - at).sqrMagnitude;
                if (d < da) { c = b; dc = db; b = a; db = da; a = g; da = d; }
                else if (d < db) { c = b; dc = db; b = g; db = d; }
                else if (d < dc) { c = g; dc = d; }
            }
            int n = 0;
            float until = Time.time + AlarmSeconds;
            foreach (Guard g in new[] { a, b, c })
            {
                if (g == null) continue;
                Lure(g, Snap(at + new Vector3((g.Index % 3 - 1) * 1.6f, 0f, -1.2f)), until);
                n++;
            }
            if (n > 0) status(L10n.T("库房警铃响了，押运员在往回跑。", "The store alarm is ringing. Escorts are running back."), true);
            SquadHearsAlarm(at);
            Debug.Log(LastShiftRaidScene.LogTag + " STORE_ALARM recalled=" + n);
        }

        /// <summary>轨道车铃：铃附近的流动巡逻过去看 35 秒（原型 bell 分支）。固定岗与押运长不动。</summary>
        internal void BellRung(Vector3 at)
        {
            if (closed) return;
            float until = Time.time + BellSeconds;
            int n = 0;
            foreach (Guard g in guards)
            {
                if (!IsAlive(g) || g.Duty != null || g.Boss || (g.Character.transform.position - at).sqrMagnitude > BellRadius * BellRadius) continue;
                g.BellUntil = until;
                g.BellTarget = Snap(at + new Vector3((g.Index % 3 - 1) * 2f, 0f, -2f));
                g.GoalKey = 0;
                n++;
            }
            SquadHearsBell(at, until);
            Debug.Log(LastShiftRaidScene.LogTag + " BELL_HEARD patrols=" + n);
        }

        private void Lure(Guard g, Vector3 target, float until)
        {
            g.Lured = true;
            g.LureTarget = target;
            g.LureBack = g.Character.transform.position;
            g.LureUntil = until;
            g.LureSerial = ++lureSerial;
            g.GoalKey = 0;
        }

        // ---------------- 巡逻与跟车 ----------------
        private bool TrainCalls { get { return timetable.TrainOnLine && (!timetable.Training || timetable.DispatchChosen); } }

        private static bool Engaged(Guard g, float seconds)
        {
            return g.Ai.searchedEnemy != null || g.Ai.isNoticing(seconds);
        }

        private void Think(float now)
        {
            bool calls = TrainCalls;
            foreach (Guard g in guards)
            {
                // 围圈小队由 TickSquads 管（冻着不动、醒了守营），不跟巡逻、货台与引人逻辑。
                if (g.Suspended || g.Ai == null || g.Squad != null || !IsAlive(g)) continue;
                // 回到原处（或回程走不通 30 秒）就恢复原任务。
                if (g.Lured && now >= g.LureUntil && (now >= g.LureUntil + 30f || (g.Character.transform.position - g.LureBack).sqrMagnitude < 2f * 2f))
                { g.Lured = false; g.GoalKey = 0; }
                if (Engaged(g, 4f)) continue;
                if (g.Boss) { GuardCargo(g, now); continue; }
                if (g.Lured)
                {
                    bool back = now >= g.LureUntil;
                    Move(g, back ? g.LureBack : g.LureTarget, (back ? 3000000 : 2000000) + g.LureSerial, now);
                }
                else if (g.Duty != null) Patrol(g, now);
                else if (now < g.BellUntil) Move(g, g.BellTarget, 4000000, now);
                else if (g.VanGuard && timetable.IsLastTrain && timetable.TrainOnLine)
                {
                    // 末班车放下的人守着押运车厢尾梯（车开动后跟着车走一段）。
                    LastShiftCar van;
                    if (timetable.TryVan(out van)) Move(g, VanSlot(van, timetable.LineUsed, g.Index % 3), 5100000 + (timetable.Phase == LastShiftPhase.Dwell ? 0 : (int)(now / 2f)), now);
                }
                else if (calls)
                {
                    char line = timetable.LineUsed;
                    if (g.CargoLine != line) { g.CargoLine = line; g.CargoGoal = CargoSlot(line, g.Index); }
                    Move(g, g.CargoGoal, line == 'W' ? 5000001 : 5000002, now);
                }
                else Patrol(g, now);
            }
        }

        private void Patrol(Guard g, float now)
        {
            List<Vector3> route = g.Route;
            if (route == null || route.Count == 0) { Move(g, g.Home, 1000000, now); return; }
            if (g.Wp >= route.Count) g.Wp = 0;
            Vector3 goal = route[g.Wp];
            if ((g.Character.transform.position - goal).sqrMagnitude <= 2.5f * 2.5f)
            {
                if (g.HoldUntil < 0f) g.HoldUntil = now + 2f + g.Index % 3;
                if (now < g.HoldUntil) return;
                g.HoldUntil = -1f;
                g.Wp = (g.Wp + 1) % route.Count;
                goal = route[g.Wp];
            }
            Move(g, goal, 1000000 + g.Wp, now);
        }

        private void Move(Guard g, Vector3 goal, int key, float now)
        {
            g.Ai.patrolPosition = goal;
            bool fresh = key != g.GoalKey;
            g.GoalKey = key;
            if ((g.Character.transform.position - goal).sqrMagnitude <= 2.5f * 2.5f) return;
            if (!fresh && (now < g.NextMove || g.Ai.WaitingForPathResult() || g.Ai.HasPath())) return;
            g.NextMove = now + 4f;
            g.Ai.MoveToPos(goal);
        }

        /// <summary>原型第六轮：押运守在封签货两端（签收位有清楚的射线），不在货箱上排网格。</summary>
        private Vector3 CargoSlot(char line, int index)
        {
            // 西台货箱面朝 +x（three.js），东台面朝 -x；角位从货箱背面量。
            float back = line == 'W' ? Mathf.PI : 0f;
            float angle = back + CargoAngles[index % CargoAngles.Length], radius = 2.6f + (index * 7 % 5) * .3f;
            return Snap(CargoCrate(line) + new Vector3(Mathf.Cos(angle) * radius, 0f, -Mathf.Sin(angle) * radius));
        }

        private Vector3 CargoCrate(char line)
        {
            foreach (Transform marker in world.LootMarkers)
                if (marker != null && marker.name == (line == 'W' ? "cargoW" : "cargoE")) return marker.position;
            return Three(line == 'W' ? -32f : 32f, -33f);
        }

        /// <summary>押运长的岗位：铅封柜正背面（签收的人站在靠股道那面，他隔着柜子守着），离柜 <see cref="ChiefPostDistance"/> m。</summary>
        private Vector3 ChiefPost(char line)
        {
            return Snap(CargoCrate(line) + new Vector3(line == 'W' ? -ChiefPostDistance : ChiefPostDistance, 0f, 0f));
        }

        /// <summary>
        /// 扳岔后尽早换岗。到停靠和离站时封线形状会改变，用新目标键让官方 AI 按当前导航重寻。
        /// </summary>
        private void GuardCargo(Guard g, float now)
        {
            if (g.CargoLine != timetable.LineUsed)
            {
                g.CargoLine = timetable.LineUsed;
                g.Home = ChiefPost(g.CargoLine);
                g.GoalKey = 0;
                Debug.Log(LastShiftRaidScene.LogTag + " CHIEF_REASSIGNED_CARGO line=" + g.CargoLine + " trip=" + timetable.Trip);
            }
            // 正常进出站的封线相同，净空与下一班预告也相同；只在形状确实变了时重寻。
            int navigationState = timetable.Phase == LastShiftPhase.Dwell ? 2 : timetable.TrainOnLine ? 1 : 0;
            Move(g, g.Home, 6000000 + navigationState * 100 + g.CargoLine, now);
        }

        // ---------------- 封站后搜站 ----------------
        /// <summary>封站后每 <see cref="HuntEvery"/> 秒：没在交火的流动押运往玩家附近赶（各自错开几米），逼人尽快走。</summary>
        private void Hunt(float now)
        {
            if (player == null || player.Health == null || player.Health.IsDead) return;
            Vector3 at = player.transform.position;
            int n = 0;
            foreach (Guard g in guards)
            {
                if (!IsAlive(g) || g.Boss || g.Duty != null || g.Squad != null || g.Ai == null || g.Suspended || Engaged(g, 4f)) continue;
                float angle = (g.Index % 6) * Mathf.PI / 3f;
                Lure(g, Snap(at + new Vector3(Mathf.Cos(angle) * 6f, 0f, Mathf.Sin(angle) * 6f)), now + HuntSeconds);
                n++;
            }
            if (n > 0) Debug.Log(LastShiftRaidScene.LogTag + " YARD_HUNT guards=" + n);
        }

        // ---------------- 车头正面撞押运 ----------------
        /// <summary>
        /// 进站、出站时车头扫过的窄带（同 <see cref="LastShiftTimetable.FrontHit"/>，判玩家的同一条）里有押运：普通押运当场撞死，
        /// 小队长掉六成血、押运长掉三成，再被推到车身外侧（一班车一人只撞一次）。伤害记在玩家名下——把追兵引上股道是玩家的打法。
        /// 只在进出站相位、每帧对活着且没挂起的人算一次，人数小、O(n)。
        /// </summary>
        private void TickTrainStrikes(float now)
        {
            LastShiftPhase phase = timetable.Phase;
            if (phase != LastShiftPhase.Arrival && phase != LastShiftPhase.Depart) return;
            int key = timetable.Trip;
            double from = timetable.SweptFromZ;
            List<LastShiftCar> cars = timetable.Cars();
            if (cars.Count == 0) return;
            LastShiftCar engine = cars[0];
            for (int i = 0; i < guards.Count; i++)
            {
                Guard g = guards[i];
                if (g.Suspended || g.StruckKey == key || !IsAlive(g)) continue;
                Vector3 p = g.Character.transform.position;
                if (!timetable.FrontHit(engine, from, p.x, -p.z, p.y)) continue;
                g.StruckKey = key;
                Strike(g, p, now);
            }
        }

        private void Strike(Guard g, Vector3 at, float now)
        {
            Health health = g.Character.Health;
            float damage = g.Boss ? health.MaxHealth * StrikeChiefShare : g.Lead != null ? health.MaxHealth * StrikeLeaderShare : health.CurrentHealth + 1f;
            CharacterMainControl credit = player != null && player.Health != null && !player.Health.IsDead ? player : null;
            Vector3 push = Vector3.zero;
            List<LastShiftCar> cars = timetable.Cars();
            if (cars.Count > 0)
            {
                LastShiftCar c = cars[0];
                double co = Math.Cos(c.Yaw), sn = Math.Sin(c.Yaw), dx = at.x - c.X, dz = -at.z - c.Z;
                double side = co * dx - sn * dz < 0 ? -1 : 1, r = c.Width * 0.5 + .7;
                push = new Vector3((float)(c.X + co * side * r + sn * (sn * dx + co * dz)), at.y, -(float)(c.Z - sn * side * r + co * (sn * dx + co * dz)));
            }
            try
            {
                health.Hurt(new DamageInfo(credit) { damageType = DamageTypes.realDamage, damageValue = damage, ignoreArmor = true,
                    fromWeaponItemID = 0, damagePoint = at + Vector3.up, damageNormal = (push - at).sqrMagnitude > .01f ? (push - at).normalized : Vector3.up });
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 车头撞押运失败 " + g.Id + "：" + e.Message); return; }
            SkyIslandImpactFx.Puff(world.Root.transform, at, .7f, 8);
            SkyIslandImpactFx.Sparks(world.Root.transform, at + Vector3.up * .8f, LastShiftChiefFx.Amber, 6);
            SkyIslandImpactFx.Shake(at, .35f);
            bool dead = health.IsDead;
            if (!dead && push != Vector3.zero) { try { g.Character.SetPosition(push); } catch (Exception) { /* 推不动就留在原地，下一班才会再撞 */ } }
            if (dead) TrainKills++;
            if (now >= nextStrikeLine) { nextStrikeLine = now + 15f; status(LastShiftText.TrainStrikeLine, false); }
            Debug.Log(LastShiftRaidScene.LogTag + " TRAIN_STRIKE id=" + g.Id + " dead=" + dead + " boss=" + g.Boss + " leader=" + (g.Lead != null) + " trip=" + timetable.Trip);
        }

        // ---------------- 远处安静的敌人挂起 ----------------
        // 官方角色除了 AI 还有物理、动画与物品更新；整只停用、走近恢复同一实体（血量装备都在），不改生成 / 死亡名额。
        private void TickActivity(float now)
        {
            if (now < nextActivity || BossRushUI.IsGamePaused()) return;
            nextActivity = now + .35f;
            Vector3 at = player.transform.position;
            bool calls = TrainCalls;
            foreach (Guard g in guards)
            {
                CharacterMainControl character = g.Character;
                if (character == null || character.Health == null || character.Health.IsDead) { g.Suspended = false; continue; }
                float radius = g.Suspended ? ActivityWakeRadius : ActivitySleepRadius;
                bool keepAwake = g.Boss || g.Lured || now < g.BellUntil || (calls && g.Duty == null)
                    || (g.Squad != null && g.Squad.State == SquadState.Roused)
                    || (g.Ai != null && !g.Frozen && (g.Ai.aimTarget != null || Engaged(g, 6f)));
                bool near = (character.transform.position - at).sqrMagnitude <= radius * radius;
                if (g.Suspended)
                {
                    if (!near && !keepAwake) continue;
                    PrepareNavigation(character);
                    SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(character);
                    character.gameObject.SetActive(true);
                    g.Suspended = false;
                    g.GoalKey = 0;
                    // 冻着的小队成员：AI 子物体一直停着（整只恢复不会把它带起来），只补回 Sleeping 与朝向。
                    if (g.Frozen) Settle(g);
                }
                else if (!near && !keepAwake && character.gameObject.activeSelf)
                {
                    PrepareNavigation(character);
                    character.gameObject.SetActive(false);
                    g.Suspended = true;
                }
            }
        }

        private void PrepareNavigation(CharacterMainControl character)
        {
            // 停用和恢复两端都清掉旧回调 / 路径，恢复前装回本图 mask。
            foreach (Seeker seeker in character.GetComponentsInChildren<Seeker>(true)) { seeker.CancelCurrentPathRequest(); seeker.graphMask = navigation.Mask; }
            foreach (AI_PathControl path in character.GetComponentsInChildren<AI_PathControl>(true)) path.StopMove();
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            if (subscribed) { timetable.PhaseChanged -= OnPhase; subscribed = false; }
            DisposeDevices();
            DisposeLeaders();
            DisposeSquads();
            foreach (Guard g in guards)
            {
                if (g.Character == null) continue;
                if (g.Character.Health != null && g.Character.Health.IsDead) continue; // 尸体归官方
                g.Character.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(g.Character.gameObject);
                g.Character = null;
            }
            guards.Clear();
            queue.Clear();
        }
    }
}
