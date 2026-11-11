using System;
using System.Collections.Generic;
using Duckov.Utilities;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 末班编组站的刷怪 owner（第二期）。规则逐条照 three.js 原型 `docs/design/last-shift-yard/proto/sim.mjs`（第六轮）：
    /// - 全趟可遇 24–30 人（含押运长与列车送来的押运员），同时活动 ≤ 8，流动（非固定岗）≤ 5；北门、南班、押运库三处固定岗各保一人。
    /// - 只在离玩家 25–75 m、可走、2.75 m 内没有活人的点刷（原型 eligible）；开局一批，之后每 16 秒（没刷成 2 秒）补一人，先补固定岗。
    /// - 来车（进站 / 停靠 / 出站）时流动押运跟车去那一台的封签货两侧；停靠时押运车在那一台放下最多 2 名押运员（玩家 25 m 内不放）。
    /// - 库房警铃召回最近 3 名押运员 40 秒；轨道车铃引附近巡逻 35 秒；维修棚蜂鸣器引最多 2 名普通巡逻 10 秒（见 LastShiftYardDevices.cs）。
    /// - 回访局 30% 押运长不在库前，坐第一班停靠货车的押运车来，在停靠那一台下车。
    /// 这些都只改官方 AI 的 <c>patrolPosition</c> / <c>MoveToPos</c>；发现玩家、交火与追击全交还官方行为树。
    /// 刷出的六步同潮闸镇：关距离激活 → 官方创建 → 导航限定本图 → 解除距离休眠 → 敌对性安全网 `SetTeam(Teams.wolf)` → 挂到世界根下。
    /// 远处安静的敌人整只停用、走近恢复（55 / 65 m 滞回）；押运长、交火中、赶往货台 / 警铃 / 铃声 / 蜂鸣器的人豁免。
    /// 坐标：作者标记已是 Unity 坐标；原型里的固定点（货台、甲板、蜂鸣器目标）按 three.js (x, z) → Unity (x, y, -z) 换算。
    /// </summary>
    internal sealed partial class LastShiftEnemies : IDisposable
    {
        private const float MinSpawn = 25f, MaxSpawn = 75f, Crowd = 2.75f, ThinkInterval = .5f;
        internal const int LiveCap = 8, MobileCap = 5, AlarmRecall = 3;
        private const float ReinforceAfter = 16f, ReinforceRetry = 2f, AlarmSeconds = 40f, BellSeconds = 35f, BellRadius = 50f;
        internal const float ActivityWakeRadius = 55f, ActivitySleepRadius = 65f;
        internal const double ChiefRideChance = .3;
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
        private CharacterRandomPreset scav;
        private TidegateLoot.PrizeCatalog spareCatalog;
        private Guard chief;
        private bool chiefRides, closed, subscribed;
        private int serial, spawned, inFlight, lureSerial, landedTrip = -1;
        private float nextThink, nextReinforce, nextActivity;

        /// <summary>押运长已倒下（站务记录、剧情用）。</summary>
        internal bool BossDefeated { get; private set; }
        /// <summary>押运长这趟是坐押运车来的（站务记录「击倒随车而来的押运长」）。</summary>
        internal bool BossRodeTrain { get; private set; }
        internal int SpawnedCount { get { return spawned; } }
        internal int Population { get { return population; } }
        /// <summary>会话可接上搜刮 owner 的私货抽签（带着旧钢印再打倒押运长时换成的那一件）；没接时用本 owner 自带的同口径抽签。</summary>
        internal Func<int> BossSpare { get; set; }

        internal LastShiftEnemies(LastShiftWorld world, LastShiftTimetable timetable, ArenaPrototypeNavigation navigation, CharacterMainControl player,
            int seed, bool returnVisit, Func<bool> valid, HashSet<Transform> unreachable, Action<string, bool> status)
        {
            this.world = world; this.timetable = timetable; this.navigation = navigation; this.player = player;
            this.valid = valid; this.status = status ?? delegate { };
            population = 24 + (seed & 0x7fffffff) % 7;
            roster = timetable.Stream("enemies#roster");
            LastShiftEnemyIdentity.InjectLabels();
            foreach (Transform marker in world.SpawnMarkers)
            {
                // 进图时导航探路够不着的点本趟不刷（会话已记日志）。
                if (marker == null || (unreachable != null && unreachable.Contains(marker))) continue;
                string type; int quality;
                Describe(marker.name, out type, out quality);
                spots.Add(new Spot { Marker = marker, Id = marker.name, Group = marker.parent != null ? marker.parent.name : "outer", Type = type, Quality = quality });
            }
            CollectRoutes();
            BuildDevices();
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

        // 原型 attachActors：押运长（随车局除外）、北门与南班各一名固定岗、外线与东西场四名流动；此刻不合规的点不补，留给后面的补员。
        private void Seed()
        {
            Spot boss = FindSpot("bossSpawn");
            if (!chiefRides && boss != null) { chief = NewGuard(boss, boss.Marker.position, null); chief.IgnoreRing = true; queue.Add(chief); }
            foreach (string post in new[] { "postNorth", "postSouth" })
                foreach (Spot spot in spots)
                    if (spot.Group == post && Eligible(spot.Marker.position)) { queue.Add(NewGuard(spot, spot.Marker.position, post)); break; }
            SeedMobile("s0", 0f, 0f); SeedMobile("w0", -1.5f, -1f); SeedMobile("w1", 1.6f, 1f); SeedMobile("e0", 0f, 0f);
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
            TickDevices(now);
            TickActivity(now);
            if (now >= nextThink) { nextThink = now + ThinkInterval; Think(now); }
            if (inFlight > 0) return;
            if (queue.Count > 0) { ServeQueue(now); return; }
            if (now >= nextReinforce) nextReinforce = now + (Replenish() ? ReinforceAfter : ReinforceRetry);
        }

        private void ServeQueue(float now)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                Guard g = queue[i];
                if (g.DwellTrip >= 0 && (timetable.Phase != LastShiftPhase.Dwell || timetable.Trip != g.DwellTrip)) { queue.RemoveAt(i--); continue; }
                if (now < g.RetryAfter) continue;
                queue.RemoveAt(i);
                if (!g.Boss && (!CanAdd(g.Duty) || !Eligible(g.Position))) return; // 原型 newGuard 返回空：这一名不刷。
                Spawn(g);
                return;
            }
        }

        // 原型 replenish：先补没人的固定岗，再补流动；返回这一次有没有开刷。
        private bool Replenish()
        {
            if (Live() >= LiveCap || spawned + inFlight >= population) return false;
            foreach (string post in DutyPosts)
            {
                if (PostHeld(post)) continue;
                Spot spot = PickSpot(post, true);
                if (spot == null) continue;
                Spawn(NewGuard(spot, spot.Marker.position, post));
                return true;
            }
            if (MobileLive() >= MobileCap) return false;
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

        private int Live()
        {
            int n = chiefRides && !BossDefeated && (chief == null || (!chief.Failed && !Counts(chief))) ? 1 : 0; // 随车的押运长也占一个活动名额
            foreach (Guard g in guards) if (Counts(g)) n++;
            return n;
        }

        private int MobileLive()
        {
            int n = 0;
            foreach (Guard g in guards) if (g.Duty == null && !g.Boss && Counts(g)) n++;
            return n;
        }

        private bool PostHeld(string post)
        {
            // 押运长守在库前时就是押运库那一岗；坐车来的不算，库前另补一名。
            if (post == "postBond" && chief != null && !chief.FromTrain && Counts(chief)) return true;
            foreach (Guard g in guards) if (g.Duty == post && Counts(g)) return true;
            return false;
        }

        private bool CanAdd(string duty)
        {
            if (Live() >= LiveCap || spawned + inFlight >= population) return false;
            return duty != null ? !PostHeld(duty) : MobileLive() < MobileCap;
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
                if (!Footing(g.Position, out position) || (!g.IgnoreRing && !InRing(position)))
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
                if (g.Duty != null && !g.Boss) clone.patrolRange = Mathf.Min(clone.patrolRange, 3.5f);
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
                // 东台队长与押运护卫按原定武器 Q3（天空岛普通档 3–4，达标的官方原装不换）；其余保留官方原装。
                if (!g.Boss && g.Quality >= 3) SkyIslandEnemyArmory.Arm(created, SkyIslandEnemyTier.Scav);
                LastShiftEnemyIdentity.Dress(created, g.Post);
                // 克隆 preset 要活到角色销毁：官方死亡掉落、经验都还会读它。
                created.gameObject.AddComponent<TidegatePresetOwner>().Preset = clone;
                clone = null;
                g.Character = created; g.Ai = ai; g.Home = position; g.GoalKey = 0;
                g.Wp = NearestWaypoint(g.Route, position);
                ai.patrolPosition = position;
                if (g.Boss)
                {
                    chief = g;
                    created.gameObject.AddComponent<LastShiftEscortChief>().Bind(created, ai, g.FromTrain, valid, PickSpare, OnChiefDown);
                    if (g.FromTrain)
                    {
                        BossRodeTrain = true;
                        status(L10n.T("押运长从押运车上跳下来，就在", "The chief escort jumps off the brake van at the ")
                            + LastShiftText.PlatformName(timetable.LineUsed) + L10n.T("台！", " platform!"), true);
                    }
                }
                else if (g.FromTrain && landedTrip != timetable.Trip)
                {
                    landedTrip = timetable.Trip;
                    status(L10n.T("押运车在", "The brake van drops escorts at the ") + LastShiftText.PlatformName(timetable.LineUsed)
                        + L10n.T("台放下了押运员。", " platform."), false);
                }
                retained = true;
                spawned++;
                Debug.Log(LastShiftRaidScene.LogTag + " ENEMY_READY id=" + g.Id + " post=" + g.Post + " duty=" + (g.Duty ?? "-") + " preset=" + source.name
                    + " team=" + created.Team + " live=" + Live() + " spawned=" + spawned + "/" + population);
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
            if (closed || next != LastShiftPhase.Dwell || valid == null || !valid()) return;
            try
            {
                char line = timetable.LineUsed;
                // 原型 deckPoint：西台 (-35.5, -28)、东台 (35.5, -28)（three.js）。
                float deckX = line == 'W' ? -35.5f : 35.5f;
                if (chiefRides && !BossDefeated && (chief == null || (!chief.Failed && !Counts(chief) && !queue.Contains(chief))))
                {
                    Spot spot = FindSpot("bossSpawn");
                    if (spot == null) return;
                    chief = NewGuard(spot, Snap(Three(deckX, -24f)), null);
                    chief.IgnoreRing = true; chief.FromTrain = true; chief.DwellTrip = timetable.Trip;
                    queue.Add(chief);
                    return; // 原型 trainArrived：押运长下车的那一班不另送押运员。
                }
                if (timetable.Training) return;
                Spot reference = FindSpot(line == 'W' ? "w2" : "e2");
                if (reference == null) return;
                for (int i = 0; i < 2; i++)
                {
                    Guard g = NewGuard(reference, Snap(Three(deckX, -28f - 2.2f * i)), null);
                    g.FromTrain = true; g.DwellTrip = timetable.Trip;
                    queue.Add(g);
                }
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 列车送人失败：" + e.Message); }
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
            if (n > 0) status(L10n.T("库房警铃响了：", "The store alarm is ringing: ") + n + L10n.T(" 名押运员正往回赶。", " escorts are hurrying back."), true);
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
                if (g.Suspended || g.Ai == null || !IsAlive(g)) continue;
                // 回到原处（或回程走不通 30 秒）就恢复原任务。
                if (g.Lured && now >= g.LureUntil && (now >= g.LureUntil + 30f || (g.Character.transform.position - g.LureBack).sqrMagnitude < 2f * 2f))
                { g.Lured = false; g.GoalKey = 0; }
                if (Engaged(g, 4f)) continue;
                if (g.Boss) { g.Ai.patrolPosition = g.Home; continue; }
                if (g.Lured)
                {
                    bool back = now >= g.LureUntil;
                    Move(g, back ? g.LureBack : g.LureTarget, (back ? 3000000 : 2000000) + g.LureSerial, now);
                }
                else if (g.Duty != null) Patrol(g, now);
                else if (now < g.BellUntil) Move(g, g.BellTarget, 4000000, now);
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
            Vector3 crate = Three(line == 'W' ? -32f : 32f, -33f);
            foreach (Transform marker in world.LootMarkers)
                if (marker != null && marker.name == (line == 'W' ? "cargoW" : "cargoE")) { crate = marker.position; break; }
            // 西台货箱面朝 +x（three.js），东台面朝 -x；角位从货箱背面量。
            float back = line == 'W' ? Mathf.PI : 0f;
            float angle = back + CargoAngles[index % CargoAngles.Length], radius = 2.6f + (index * 7 % 5) * .3f;
            return Snap(crate + new Vector3(Mathf.Cos(angle) * radius, 0f, -Mathf.Sin(angle) * radius));
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
                    || (g.Ai != null && (g.Ai.aimTarget != null || Engaged(g, 6f)));
                bool near = (character.transform.position - at).sqrMagnitude <= radius * radius;
                if (g.Suspended)
                {
                    if (!near && !keepAwake) continue;
                    PrepareNavigation(character);
                    SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(character);
                    character.gameObject.SetActive(true);
                    g.Suspended = false;
                    g.GoalKey = 0;
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
