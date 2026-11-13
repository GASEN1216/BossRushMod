using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 守货岗（2026-11-13 owner「玩家走到每个有宝箱的地方都有激战」；前身是 2026-11-12 的围圈小队）。归刷怪 owner，另记一本账：
    /// - 每处有箱子的地方一队（卸货棚、检修库、维修棚、押运库、押运场、东班房、北桥下、东北退件），没箱子的两处歇脚地（职工院、中场备料）每趟各一半概率；
    ///   末班车进站后北公路设卡（checkpoint）。坐标是作者布局（three.js）里摆件之间的空地，换算 Unity (x, y, -z)；
    ///   进图时逐个吸到导航、查脚下与墙层胶囊，站不住的位置本趟不用（日志 SQUAD_SITE），不足 3 个的整处不用。离线核对：`tests/LastShiftSquadPlacementPropertyTest.py`。
    /// - 姿态：风险柜与押运库（Vault）和卡口醒着站岗；没箱子的歇脚地围坐；其余有箱子的地方每趟按种子一半站岗一半围坐。
    ///   重要的几处由小队长带队（信号工长 / 调车长 / 押运班长，招式见 LastShiftLeaders.cs）。
    /// - 开张：玩家走到圈心 29–71 m（卡口 26 m 起）才刷，离得近的先开；同时在场 ≤ <see cref="GarrisonCap"/> 人、全趟 ≤ <see cref="GarrisonPopulation"/> 人。
    ///   没交过手、玩家走远到 85 m 外的那一队收岗（人收走、名额退回、这处以后还会再开），座位留给玩家正走向的地方；打光了的那处要等
    ///   下一班车停到这一侧才补岗（<see cref="RestaffSide"/>，2026-11-13 owner「每趟火车来都会刷新」）。
    /// - 围坐（冻结）：官方 AI 挂在角色子物体上，整只子物体停用——行为树、寻路、AI 的 Update 与官方听觉订阅一起停；再 SetSleeping(true)，
    ///   收枪、清移动输入、朝圈心定向。醒来时重新启用子物体、还原 defaultWeaponOut、把玩家交给官方选敌，之后全归官方行为树。
    /// - 醒：玩家进 11 m 且在某人正前方 ±60° 内有视线（墙层与检修蒸汽都挡），或贴到 3.5 m 内有视线；任一人受伤或倒下；
    ///   官方 AISound（玩家脚步、枪声、嘎嘎、手雷落地）按官方半径 × 本人 hearingAbility 传到，枪声另限 30 m；库房警铃在 30 m 内响；信号工长点名。
    ///   一人醒全队醒，按位次错开 0.3 s，发现的那个喊一声。站岗的队伍醒着，官方 AI 自己发现玩家；交上火就算醒。
    /// - 和其它引人逻辑：库房警铃、维修棚蜂鸣器、跟车押运都不拉守货岗（Duty 非空，现成代码天然跳过）；
    ///   只有轨道车铃（铃附近 50 m）会把围坐的整队叫起来过去看 35 秒，没看到人就走回原位重新坐下。远处整只停用照旧（55 / 65 m）。
    /// </summary>
    internal sealed partial class LastShiftEnemies
    {
        internal const int SquadMin = 3, GarrisonCap = 10, GarrisonPopulation = 80;
        internal const float SquadSight = 11f, SquadBump = 3.5f, SquadGunHearing = 30f, SquadAlarmRadius = 30f, SquadChat = 17f;
        internal const float SquadStagger = .3f, GarrisonOpenMin = 29f, GarrisonOpenMax = 71f, CheckpointOpenMin = 26f, GarrisonStandDown = 85f;
        /// <summary>守货岗成员逐个生成时离玩家至少这么远（屏幕约 28 × 20 m，18 m 外看不见冒出来）；站位开张时已核过，不再走 25–75 m 刷怪环。</summary>
        internal const float GarrisonSpawnMin = 18f;
        /// <summary>一个人都没刷成的那处，隔这么久再试（不算打光）。</summary>
        private const float GarrisonRetry = 10f;
        /// <summary>没箱子的歇脚地每趟开张的概率；有箱子的普通地点站岗（而不是围坐）的概率。</summary>
        internal const double CampChance = .5, GuardPostureChance = .5;
        /// <summary>正前方视锥半角 60°（cos）。</summary>
        private const float SquadCone = .5f;

        internal enum SquadState { Dormant, Guarding, Roused, Alert }

        /// <summary>Loot：有箱子，每趟都开、姿态按种子；Vault：风险柜 / 押运库，每趟都开、站岗；Camp：没箱子的歇脚地，一半概率、围坐；Checkpoint：末班车进站后北公路设卡。</summary>
        internal enum GarrisonKind { Loot, Vault, Camp, Checkpoint }

        private sealed class SquadDef
        {
            internal readonly string Id, IdleZh, IdleEn;
            internal readonly float FocusX, FocusZ;
            internal readonly float[] Slots;
            internal readonly LastShiftPost[] Posts;
            internal readonly int Quality;
            internal readonly GarrisonKind Kind;
            internal SquadDef(string id, float fx, float fz, float[] slots, LastShiftPost[] posts, int quality, string zh, string en, GarrisonKind kind)
            { Id = id; FocusX = fx; FocusZ = fz; Slots = slots; Posts = posts; Quality = quality; IdleZh = zh; IdleEn = en; Kind = kind; }
        }

        private sealed class SquadSite
        {
            internal SquadDef Def;
            internal Vector3 Focus;
            internal readonly List<Vector3> Spots = new List<Vector3>(4);
            /// <summary>Spent：本趟打光了不再开；Open：此刻有一队在。</summary>
            internal bool Spent, Open;
            internal SquadState Posture;
            internal float RetryAt;
        }

        private sealed class Squad
        {
            internal SquadSite Site;
            internal readonly List<Guard> Members = new List<Guard>(4);
            internal SquadState State;
            internal bool Chatted, Heard, HeardPlayer, AlertToPlayer, Fought;
            internal Teams PendingTeam;
            internal float PendingHearing;
            internal float QuietUntil, NextThink;
            internal int Serial, Spawned;
        }

        private const LastShiftPost Walk = LastShiftPost.Walker, Check = LastShiftPost.Inspector, Escorted = LastShiftPost.Escort;
        private const LastShiftPost Lamp = LastShiftPost.Signalman, Shunt = LastShiftPost.Shunter, Sarge = LastShiftPost.Sergeant;

        // three.js (x, z) 坐标对；圈心是摆件本身或摆件围出来的那块空地。离线核对见 tests/LastShiftSquadPlacementPropertyTest.py。
        // 小队长总在第一个位次（Posts[0]）。
        private static readonly SquadDef[] SquadDefs =
        {
            // 夜班职工小院：饭桌（-47, 32）和长凳（-47, 34）四边各站一人。没有箱子。
            new SquadDef("canteen", -47f, 32.6f, new[] { -47.8f, 30.1f, -44.4f, 32.2f, -49.6f, 32.2f, -46.2f, 35.2f }, new[] { Walk, Check, Walk, Walk }, 1,
                "饭都凉了，车还没来。", "The food's gone cold and the train's still not here.", GarrisonKind.Camp),
            // 中场换轨备料：钢轨束、枕木垛、扣件托盘三面围着的空地，道班在这儿抽烟。没有箱子。
            new SquadDef("relay", 16.9f, 23.4f, new[] { 15.4f, 23.6f, 17.4f, 21.9f, 18.4f, 23.9f }, new[] { Walk, Walk, Walk }, 1,
                "再抽一根就上道。", "One more smoke, then back on the track.", GarrisonKind.Camp),
            // 东北退件堆场：退件垛和空托盘西侧，挨着东线风箱（风险柜 eastRisk）。押运班长带队。
            new SquadDef("returns", 65.2f, -44.6f, new[] { 63.6f, -44.4f, 65f, -43f, 66.7f, -44.9f, 65.4f, -46.1f }, new[] { Sarge, Escorted, Check, Escorted }, 2,
                "没人认领的，先点清楚再说。", "Nobody's claimed this lot. Count it properly first.", GarrisonKind.Vault),
            // 北桥下排水检修点：围着桥下那件遗失货件分东西，离公路补给柜十来米。
            new SquadDef("lost", -16f, -43f, new[] { -13.6f, -42.9f, -15.8f, -40.6f, -15.6f, -45.4f }, new[] { Walk, Check, Walk }, 1,
                "失物？谁捡着算谁的。", "Lost property? Finders keepers.", GarrisonKind.Loot),
            // 西场煤灰过磅间：雨棚下、磅砣台和地磅表头之间烤手，挨着维修棚的钳工箱。信号工长带队。
            new SquadDef("coal", -63.3f, -9.3f, new[] { -64.8f, -9.4f, -63.1f, -7.8f, -61.8f, -9.6f }, new[] { Lamp, Check, Walk }, 1,
                "炉灰还热乎，手伸过来烤烤。", "The ash is still warm. Get your hands over here.", GarrisonKind.Loot),
            // 西台卸货棚：补给柜和工具箱之间的空地，离西线不远。调车长带队。
            new SquadDef("westDeck", -42f, -36f, new[] { -40.5f, -36f, -42f, -34.5f, -43.5f, -36f, -42f, -37.5f }, new[] { Shunt, Escorted, Walk, Check }, 2,
                "卸货棚的工具谁动过？", "Who's been at the tools in the unloading shed?", GarrisonKind.Loot),
            // 检修库：库外那片空地守着库里的货柜（风险柜 westRisk）。押运班长带队。
            new SquadDef("westStore", -56f, -30f, new[] { -54.5f, -30f, -56.8f, -31.3f, -55f, -31.7f, -54.8f, -27.8f }, new[] { Sarge, Escorted, Escorted, Check }, 2,
                "检修库那柜子别碰，押运长点过数。", "Leave the cabinet in the inspection shed alone. The chief counted it.", GarrisonKind.Vault),
            // 东台：随车药箱南边的空地，离东线十几米（与西台卸货棚对称）。调车长带队。
            new SquadDef("eastDeck", 40f, -41f, new[] { 41.5f, -41f, 40f, -39.5f, 38.7f, -41.8f, 40f, -43f }, new[] { Shunt, Escorted, Check, Walk }, 2,
                "东台的随车药箱谁打开过？", "Who's been into the east platform's first-aid box?", GarrisonKind.Loot),
            // 押运库门前：库内货柜（风险柜 bondCab）与铅封侧柜。信号工长带队。
            new SquadDef("bond", 51f, -17.5f, new[] { 52.3f, -16.8f, 50.2f, -16.2f, 49f, -17.5f, 51f, -20f }, new[] { Lamp, Escorted, Escorted, Escorted }, 2,
                "库里的货一件都不能少。", "Not one piece goes missing from the store.", GarrisonKind.Vault),
            // 押运场：工具箱旁边，几个人拿空推车打赌。调车长带队。
            new SquadDef("escortYard", 49f, 4.5f, new[] { 50.5f, 4.5f, 49f, 6f, 47.5f, 4.5f, 49f, 3f }, new[] { Shunt, Escorted, Walk, Walk }, 1,
                "推车再溜一回，看谁先躲开。", "Let one more cart roll. See who jumps first.", GarrisonKind.Loot),
            // 东班房外：补给柜往南、接客亭药箱往北的中间。信号工长带队。
            new SquadDef("eastCrew", 41f, 28f, new[] { 42.5f, 28f, 41f, 29.5f, 39.5f, 28f, 41f, 26.5f }, new[] { Lamp, Walk, Check, Walk }, 1,
                "东班房的灯又闪了。", "The east crew-room light's flickering again.", GarrisonKind.Loot),
            // 北公路卡口：北跨线桥中间楼梯东侧、桥面底下的路面，离常开撤离点约 12 m，末班车进站后才拉路障。押运班长带队。
            // 2026-11-13 实机日志：原圈心 (0, -45) 四个站位全压在中间楼梯（x ±2.5、z -46.2..-29）的实心台阶里，运行时全部站不住、整处不开。
            new SquadDef("checkpoint", 4.5f, -46f, new[] { 5.8f, -45.2f, 3.8f, -44.7f, 3.7f, -47.3f, 5.5f, -47.7f }, new[] { Sarge, Escorted, Escorted, Escorted }, 2,
                "末班走了，谁也别想出站。", "The last train's gone. Nobody leaves the yard.", GarrisonKind.Checkpoint),
        };

        private readonly List<SquadSite> squadPlan = new List<SquadSite>(SquadDefs.Length);
        private readonly List<Squad> squads = new List<Squad>(4);
        private int squadSerial;
        private float nextGarrison;
        private bool soundSubscribed, checkpointWanted;
        private int sightMask = -1, fowMask;

        // ---------------- 进图：核对站位、排本趟的守货岗 ----------------
        private void BuildSquads()
        {
            System.Random pick = timetable.Stream("enemies#squads");
            foreach (SquadDef def in SquadDefs)
            {
                var site = new SquadSite { Def = def, Focus = Three(def.FocusX, def.FocusZ) };
                float ground;
                if (world.TryGroundAt(site.Focus, out ground)) site.Focus.y = ground;
                for (int i = 0; i + 1 < def.Slots.Length; i += 2)
                {
                    Vector3 want = Three(def.Slots[i], def.Slots[i + 1]), snapped, stand;
                    Vector3 flat = Vector3.zero;
                    bool onNav = navigation.TryNearestWalkable(want, out snapped);
                    if (onNav) { flat = snapped - want; flat.y = 0f; }
                    if (!onNav || flat.sqrMagnitude > .6f * .6f || !CanStand(snapped, out stand))
                    {
                        Debug.LogWarning(LastShiftRaidScene.LogTag + " SQUAD_SITE " + def.Id + " 第 " + (i / 2) + " 个站位站不住，本趟不用 at=" + want);
                        continue;
                    }
                    site.Spots.Add(stand);
                }
                // 每处都按固定顺序各抽一次（是否开、什么姿态），结果只由种子决定。
                double open = pick.NextDouble(), posture = pick.NextDouble();
                if (site.Spots.Count < SquadMin)
                {
                    Debug.LogWarning(LastShiftRaidScene.LogTag + " SQUAD_SITE " + def.Id + " 站得住的位置不足 " + SquadMin + "，整处不用");
                    continue;
                }
                if (def.Kind == GarrisonKind.Camp && open >= CampChance) continue;
                site.Posture = def.Kind == GarrisonKind.Camp ? SquadState.Dormant
                    : def.Kind == GarrisonKind.Loot && posture >= GuardPostureChance ? SquadState.Dormant : SquadState.Guarding;
                squadPlan.Add(site);
            }
            if (!soundSubscribed && squadPlan.Count > 0) { AIMainBrain.OnSoundSpawned += OnYardSound; soundSubscribed = true; }
            var ids = new List<string>(squadPlan.Count);
            foreach (SquadSite site in squadPlan) ids.Add(site.Def.Id + ":" + site.Spots.Count + ":" + site.Posture);
            Debug.Log(LastShiftRaidScene.LogTag + " SQUAD_PLAN " + string.Join(",", ids.ToArray()));
        }

        private void DisposeSquads()
        {
            if (soundSubscribed) { AIMainBrain.OnSoundSpawned -= OnYardSound; soundSubscribed = false; }
            squads.Clear();
            squadPlan.Clear();
        }

        /// <summary>守货岗此刻在场（含正在生成）的人数。</summary>
        private int GarrisonLive()
        {
            int n = 0;
            foreach (Guard g in guards) if (g.Squad != null && Counts(g)) n++;
            return n;
        }

        private int QueuedGarrison()
        {
            int n = 0;
            foreach (Guard g in queue) if (g.Squad != null) n++;
            return n;
        }

        // ---------------- 开张与收岗（0.5 秒一次） ----------------
        private void TickGarrisons(float now)
        {
            Vector3 at = player.transform.position;
            for (int i = squads.Count - 1; i >= 0; i--)
            {
                Squad s = squads[i];
                if (s.Fought || s.Site.Def.Kind == GarrisonKind.Checkpoint || (s.State != SquadState.Dormant && s.State != SquadState.Guarding)) continue;
                if (FlatDistance(s.Site.Focus, at) > GarrisonStandDown && !Busy(s)) StandDown(s, i);
            }
            // 一次只开一处：上一处的人还在排队就等。
            if (QueuedGarrison() > 0) return;
            int seats = GarrisonCap - GarrisonLive();
            if (seats < SquadMin) return;
            SquadSite best = null;
            float bestDistance = float.MaxValue;
            foreach (SquadSite site in squadPlan)
            {
                if (site.Spent || site.Open || now < site.RetryAt) continue;
                bool checkpoint = site.Def.Kind == GarrisonKind.Checkpoint;
                if (checkpoint && !checkpointWanted) continue;
                float d = FlatDistance(site.Focus, at);
                // 卡口也有上限：玩家远在南边时等他往北公路走近了再拉路障（刷得出、也看得见）。
                if (d < (checkpoint ? CheckpointOpenMin : GarrisonOpenMin) || d > GarrisonOpenMax) continue;
                if (garrisonSpawned + Math.Min(site.Spots.Count, seats) > GarrisonPopulation) continue;
                // 卡口优先：末班车一进站就拉路障。其余离得近的先开。
                if (checkpoint) d = -1f;
                if (d < bestDistance) { bestDistance = d; best = site; }
            }
            if (best != null) OpenGarrison(best, Math.Min(best.Spots.Count, seats));
        }

        /// <summary>
        /// 来车补岗：这一班停到哪一侧（西台 / 东台），那一侧打光了的守货岗（有箱子的地方，不含歇脚地与卡口）重新可以开张；
        /// 照常等玩家走到 29–71 m 才到岗，所以玩家站在那里时不会当面冒人，签完货往回走时才撞上。返回补了几处。
        /// </summary>
        private int RestaffSide(char line)
        {
            int n = 0;
            var names = new List<string>(4);
            foreach (SquadSite site in squadPlan)
            {
                if (!site.Spent || site.Open) continue;
                if (site.Def.Kind == GarrisonKind.Camp || site.Def.Kind == GarrisonKind.Checkpoint) continue;
                if ((line == 'W') != (site.Def.FocusX < 0f)) continue;
                site.Spent = false;
                site.RetryAt = 0f;
                names.Add(site.Def.Id);
                n++;
            }
            if (n == 0) return 0;
            status(L10n.T("押运车厢下来一队人，往" + LastShiftText.PlatformName(line) + "边的货棚补岗去了。",
                "A squad climbs off the brake van and heads for the empty posts on the " + LastShiftText.LineName(line) + " side."), false);
            Debug.Log(LastShiftRaidScene.LogTag + " SQUAD_RESTAFF line=" + line + " sites=" + string.Join(",", names.ToArray()));
            return n;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            Vector3 d = a - b;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>队里还有人在排队或正在生成：这时不收岗（异步生成中途销毁不干净）。</summary>
        private bool Busy(Squad s)
        {
            foreach (Guard m in s.Members) if (m.Spawning || queue.Contains(m)) return true;
            return false;
        }

        private void OpenGarrison(SquadSite site, int count)
        {
            var s = new Squad { Site = site, State = site.Posture, Serial = squadSerial++ };
            site.Open = true;
            LastShiftPost[] posts = site.Def.Posts;
            for (int i = 0; i < count; i++)
            {
                Vector3 spot = site.Spots[i], face = site.Focus - spot;
                face.y = 0f;
                // 围坐朝圈心；站岗背对圈心朝外看。
                if (site.Posture == SquadState.Guarding) face = -face;
                var g = new Guard
                {
                    Id = "squad_" + site.Def.Id + "#" + serial, Group = "squad", Duty = "squad:" + site.Def.Id, Quality = site.Def.Quality,
                    Index = serial++, Post = posts[i % posts.Length], Position = spot, Home = spot,
                    Forward = face.sqrMagnitude > .01f ? face.normalized : Vector3.forward, Squad = s, Slot = i,
                    IgnoreRing = true,
                };
                s.Members.Add(g);
                queue.Add(g);
            }
            CharacterRandomPreset source = SourceFor(s.Members[0]);
            s.PendingHearing = source != null ? source.hearingAbility : 0f;
            s.PendingTeam = Team.IsEnemy(Teams.scav, Teams.player) ? Teams.scav : Teams.wolf;
            squads.Add(s);
            Debug.Log(LastShiftRaidScene.LogTag + " SQUAD_BOARD site=" + site.Def.Id + " members=" + count + " posture=" + s.State
                + " garrison=" + GarrisonLive() + " garrisonSpawned=" + garrisonSpawned + "/" + GarrisonPopulation);
        }

        /// <summary>没交过手、玩家走远了的那一队收岗：人收走、名额退回，这处以后还会再开（玩家看不见，85 m 远在屏幕外好几屏）。</summary>
        private void StandDown(Squad s, int index)
        {
            int refunded = 0;
            foreach (Guard m in s.Members)
            {
                queue.Remove(m);
                guards.Remove(m);
                DisposeLeader(m);
                if (m.Character == null) continue;
                if (m.Character.Health != null && !m.Character.Health.IsDead) refunded++;
                m.Character.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(m.Character.gameObject);
                m.Character = null;
            }
            garrisonSpawned = Math.Max(0, garrisonSpawned - refunded);
            s.Site.Open = false;
            squads.RemoveAt(index);
            Debug.Log(LastShiftRaidScene.LogTag + " SQUAD_STAND_DOWN site=" + s.Site.Def.Id + " refunded=" + refunded);
        }

        /// <summary>刷出后：围坐的冻在原位，站岗的醒着守在原位附近，已经醒过的直接交给官方 AI。</summary>
        private void SquadMemberReady(Guard g)
        {
            g.Squad.Spawned++;
            g.SavedWeaponOut = g.Ai.defaultWeaponOut;
            g.Ai.patrolPosition = g.Home;
            if (g.Character.Health != null) g.RestHealth = g.Character.Health.CurrentHealth;
            switch (g.Squad.State)
            {
                case SquadState.Dormant: Freeze(g); break;
                case SquadState.Alert: Wake(g, g.Squad.AlertToPlayer); break;
                default: Wake(g, false); break;
            }
        }

        // ---------------- 冻结与唤醒 ----------------
        private void Freeze(Guard g)
        {
            CharacterMainControl c = g.Character;
            AICharacterController ai = g.Ai;
            if (c == null || ai == null) return;
            if (!g.Frozen) g.SavedWeaponOut = ai.defaultWeaponOut;
            g.Frozen = true;
            if (c.Health != null) g.RestHealth = c.Health.CurrentHealth;
            try
            {
                PrepareNavigation(c);
                ai.searchedEnemy = null;
                ai.aimTarget = null;
                ai.noticed = false;
                ai.defaultWeaponOut = false;
                ai.PutBackWeapon();
                ai.patrolPosition = g.Home;
                c.SetMoveInput(Vector3.zero);
                c.SetRunInput(false);
                c.Trigger(false, false, false);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 小队冻结收枪失败 " + g.Id + "：" + e.Message); }
            // 官方 AI 子物体整只停用：行为树、寻路与 AI 自己的 Update 都不跑（口径同远处整只停用）。挂在角色根上时退到只停组件。
            if (ai.gameObject != c.gameObject) ai.gameObject.SetActive(false);
            else ai.enabled = false;
            Settle(g);
        }

        /// <summary>冻住时的姿态：官方 Sleeping（IsAiming 恒假，不按旧瞄准点转身）+ 朝圈心。远处整只停用恢复后也走这里。</summary>
        private static void Settle(Guard g)
        {
            CharacterMainControl c = g.Character;
            if (c == null) return;
            try
            {
                c.SetAimPoint(c.transform.position + g.Forward * 6f);
                if (c.movementControl != null) c.movementControl.ForceTurnTo(g.Forward);
            }
            catch (Exception e) { ModBehaviour.DevLog(LastShiftRaidScene.LogTag + " 小队朝向失败 " + g.Id + "：" + e.Message); }
            c.SetSleeping(true);
        }

        private void Wake(Guard g, bool toPlayer)
        {
            CharacterMainControl c = g.Character;
            AICharacterController ai = g.Ai;
            g.WakeDue = -1f;
            if (c == null || ai == null) return;
            bool was = g.Frozen;
            g.Frozen = false;
            c.SetSleeping(false);
            if (!ai.gameObject.activeSelf) ai.gameObject.SetActive(true);
            ai.enabled = true;
            if (was) ai.defaultWeaponOut = g.SavedWeaponOut;
            foreach (Seeker seeker in c.GetComponentsInChildren<Seeker>(true)) seeker.graphMask = navigation.Mask;
            ai.patrolPosition = g.Home;
            g.GoalKey = 0;
            if (toPlayer) PointAtPlayer(ai);
        }

        /// <summary>官方选敌的口径（forceTracePlayer 与带队共享目标都是直接写 searchedEnemy）；之后交火、追击、丢目标全归官方。</summary>
        private void PointAtPlayer(AICharacterController ai)
        {
            if (ai == null || player == null || player.Health == null || player.Health.IsDead || player.mainDamageReceiver == null) return;
            ai.searchedEnemy = player.mainDamageReceiver;
            ai.SetNoticedToTarget(player.mainDamageReceiver);
            ai.noticed = true;
        }

        /// <summary>一人醒全队醒：发现的那个先动并喊一声，其余按位次错开 <see cref="SquadStagger"/>。</summary>
        private void AlertSquad(Squad s, float now, bool toPlayer, Guard spotter, string why)
        {
            if (s.State == SquadState.Alert) return;
            s.State = SquadState.Alert;
            s.Fought = true;
            s.AlertToPlayer = toPlayer;
            int order = 1;
            foreach (Guard m in s.Members)
            {
                if (!IsAlive(m)) continue;
                // 发现的那个、以及醒着的（站岗、被铃声叫起来的），立刻交给官方选敌；其余睡着的按位次错开醒。
                if (m == spotter || !m.Frozen) { Wake(m, toPlayer); continue; }
                m.WakeDue = now + SquadStagger * order++;
                m.WakeToPlayer = toPlayer;
            }
            Guard voice = spotter != null && IsAlive(spotter) ? spotter : FirstAwake(s);
            if (voice != null) Bark(voice, WakeLine(s.Serial));
            Debug.Log(LastShiftRaidScene.LogTag + " SQUAD_ALERT site=" + s.Site.Def.Id + " why=" + why);
        }

        private static Guard FirstAwake(Squad s)
        {
            foreach (Guard m in s.Members) if (IsAlive(m)) return m;
            return null;
        }

        // ---------------- 每 0.2–0.5 秒 ----------------
        private void TickSquads(float now)
        {
            if (now >= nextGarrison) { nextGarrison = now + .5f; TickGarrisons(now); }
            Vector3 at = player.transform.position;
            for (int i = squads.Count - 1; i >= 0; i--)
            {
                Squad s = squads[i];
                if (now < s.NextThink) continue;
                float focusSqr = (s.Site.Focus - at).sqrMagnitude;
                s.NextThink = now + (focusSqr < 40f * 40f ? .2f : .5f);
                bool any = false;
                foreach (Guard m in s.Members) if (Counts(m)) { any = true; break; }
                if (!any) foreach (Guard q in queue) if (q.Squad == s) { any = true; break; }
                if (!any)
                {
                    squads.RemoveAt(i);
                    s.Site.Open = false;
                    // 真打光了才算这处本趟没人；一个都没刷成（玩家贴得太近）就过一会儿再开。
                    if (s.Spawned > 0) s.Site.Spent = true;
                    else s.Site.RetryAt = now + GarrisonRetry;
                    Debug.Log(LastShiftRaidScene.LogTag + (s.Spawned > 0 ? " SQUAD_CLEARED site=" : " SQUAD_NEVER_SPAWNED site=") + s.Site.Def.Id);
                    continue;
                }
                switch (s.State)
                {
                    case SquadState.Dormant: TickDormant(s, now, at, focusSqr); break;
                    case SquadState.Guarding: TickGuarding(s, now); break;
                    case SquadState.Roused: TickRoused(s, now, at); break;
                    default: TickAlert(s, now); break;
                }
            }
        }

        private void TickDormant(Squad s, float now, Vector3 at, float focusSqr)
        {
            if (s.Heard) { s.Heard = false; AlertSquad(s, now, s.HeardPlayer, null, "heard"); return; }
            foreach (Guard m in s.Members)
            {
                CharacterMainControl c = m.Character;
                if (ReferenceEquals(c, null) || m.Spawning) continue; // 还没刷出来（或这一个没刷成）
                // 同伴倒下：官方死亡后角色可能已被销毁（Unity 判空为真），也算倒下。
                if (c == null || c.Health == null || c.Health.IsDead) { AlertSquad(s, now, true, null, "mate_down"); return; }
                if (!c.gameObject.activeInHierarchy) continue;
                if (m.Frozen && c.Health.CurrentHealth < m.RestHealth - .5f) { AlertSquad(s, now, true, m, "hurt"); return; }
                if (m.Frozen && Sees(m, at)) { AlertSquad(s, now, true, m, "seen"); return; }
            }
            if (!s.Chatted && focusSqr <= SquadChat * SquadChat)
            {
                Guard voice = FirstAwake(s);
                if (voice != null && voice.Character.gameObject.activeInHierarchy)
                {
                    s.Chatted = true;
                    Bark(voice, L10n.T(s.Site.Def.IdleZh, s.Site.Def.IdleEn));
                }
            }
        }

        /// <summary>站岗：醒着守在原位 5 m 内，官方 AI 自己发现玩家；有人受伤、倒下或交上火就整队转为醒着追打。</summary>
        private void TickGuarding(Squad s, float now)
        {
            foreach (Guard m in s.Members)
            {
                CharacterMainControl c = m.Character;
                if (ReferenceEquals(c, null) || m.Spawning) continue;
                if (c == null || c.Health == null || c.Health.IsDead) { AlertSquad(s, now, true, null, "mate_down"); return; }
                if (m.Ai == null || m.Suspended) continue;
                bool hurt = c.Health.CurrentHealth < m.RestHealth - .5f;
                if (hurt || Engaged(m, 2f))
                {
                    bool toPlayer = hurt || (player.mainDamageReceiver != null && m.Ai.searchedEnemy == player.mainDamageReceiver);
                    AlertSquad(s, now, toPlayer, m, "guard_contact");
                    return;
                }
                KeepHome(m, now, 4400000);
            }
            if (!s.Chatted && (s.Site.Focus - player.transform.position).sqrMagnitude <= SquadChat * SquadChat)
            {
                Guard voice = FirstAwake(s);
                if (voice != null && voice.Character.gameObject.activeInHierarchy)
                {
                    s.Chatted = true;
                    Bark(voice, L10n.T(s.Site.Def.IdleZh, s.Site.Def.IdleEn));
                }
            }
        }

        private void KeepHome(Guard m, float now, int key)
        {
            Vector3 off = m.Character.transform.position - m.Home;
            off.y = 0f;
            if (off.sqrMagnitude > 5f * 5f) Move(m, m.Home, key + m.Slot, now);
            else m.Ai.patrolPosition = m.Home;
        }

        private void TickRoused(Squad s, float now, Vector3 at)
        {
            bool home = now >= s.QuietUntil, overdue = now >= s.QuietUntil + 30f;
            foreach (Guard m in s.Members)
            {
                if (!IsAlive(m) || m.Ai == null || m.Spawning) continue;
                bool hurt = m.Character.Health.CurrentHealth < m.RestHealth - .5f;
                if (hurt || Engaged(m, 2f))
                {
                    bool toPlayer = hurt || (player.mainDamageReceiver != null && m.Ai.searchedEnemy == player.mainDamageReceiver);
                    AlertSquad(s, now, toPlayer, m, "roused_contact");
                    return;
                }
                if (m.Suspended) continue;
                if (now < m.BellUntil) { Move(m, m.BellTarget, 4100000 + m.Slot, now); home = false; continue; }
                Move(m, m.Home, 4200000 + m.Slot, now);
                Vector3 off = m.Character.transform.position - m.Home;
                off.y = 0f;
                if (off.sqrMagnitude > 2.6f * 2.6f) home = false;
            }
            if (!home && !overdue) return;
            // 都走回来了（或卡在路上 30 秒）、一直没看到人：重新坐下。离玩家远就挪回原位（屏幕约 28 × 20 m，18 m 外看不见这一下），近的就地朝圈心坐。
            Vector3 toPlayer3 = s.Site.Focus - at;
            toPlayer3.y = 0f;
            bool far = toPlayer3.sqrMagnitude > 18f * 18f;
            foreach (Guard m in s.Members)
            {
                if (!IsAlive(m) || m.Ai == null) continue;
                if (far) m.Character.SetPosition(m.Home);
                else
                {
                    m.Home = m.Character.transform.position;
                    Vector3 face = s.Site.Focus - m.Home;
                    face.y = 0f;
                    if (face.sqrMagnitude > .01f) m.Forward = face.normalized;
                }
                m.BellUntil = -1f;
                Freeze(m);
            }
            s.State = SquadState.Dormant;
            Guard voice = FirstAwake(s);
            if (voice != null) Bark(voice, L10n.T("没人。接着歇。", "Nobody. Back to it."));
            Debug.Log(LastShiftRaidScene.LogTag + " SQUAD_RESETTLED site=" + s.Site.Def.Id + " snapped=" + far);
        }

        private void TickAlert(Squad s, float now)
        {
            foreach (Guard m in s.Members)
            {
                if (!IsAlive(m) || m.Ai == null || m.Spawning) continue;
                if (m.Frozen)
                {
                    if (m.WakeDue >= 0f && now >= m.WakeDue) Wake(m, m.WakeToPlayer);
                    continue;
                }
                if (m.Suspended || Engaged(m, 6f)) continue;
                // 打过一场的守货岗：不交火时回原位 5 m 内，官方巡逻就在附近转。
                KeepHome(m, now, 4300000);
            }
        }

        /// <summary>看见：11 m 内、本人正前方 ±60°，或 3.5 m 内任意方向；中间不能有墙层（不看触发体）或检修蒸汽（FowBlock 触发体）。</summary>
        private bool Sees(Guard m, Vector3 at)
        {
            Vector3 feet = m.Character.transform.position, flat = at - feet;
            flat.y = 0f;
            float sqr = flat.sqrMagnitude;
            if (sqr > SquadSight * SquadSight) return false;
            if (sqr > SquadBump * SquadBump && Vector3.Dot(m.Forward, flat / Mathf.Sqrt(sqr)) < SquadCone) return false;
            if (sightMask < 0)
            {
                sightMask = GameplayDataSettings.Layers.wallLayerMask.value;
                fowMask = GameplayDataSettings.Layers.fowBlockLayers.value;
            }
            Vector3 eye = feet + Vector3.up * 1.5f, target = at + Vector3.up * 1.1f;
            return !Physics.Linecast(eye, target, sightMask, QueryTriggerInteraction.Ignore)
                && !Physics.Linecast(eye, target, fowMask, QueryTriggerInteraction.Collide);
        }

        #region 休眠听觉

        /// <summary>
        /// 官方 AISound 的听觉口径（AICharacterController.OnSound）：同阵营的脚步与枪声不算，按官方半径 × 本人 hearingAbility 判距；
        /// 枪声另限 <see cref="SquadGunHearing"/>。待生成成员用已核验的站位和预设；醒着的 AI 由官方自己接收声音。每队围坐的分别判。
        /// </summary>
        private void OnYardSound(AISound sound)
        {
            if (closed) return;
            for (int i = 0; i < squads.Count; i++) HearSound(squads[i], sound);
        }

        private void HearSound(Squad s, AISound sound)
        {
            if (s == null || s.State != SquadState.Dormant || s.Heard) return;
            bool ignoresFriendly = sound.soundType == SoundTypes.unknowNoise || sound.soundType == SoundTypes.combatSound;
            foreach (Guard m in s.Members)
            {
                CharacterMainControl c = m.Character;
                bool pending = ReferenceEquals(c, null) && !m.Failed && (m.Spawning || queue.Contains(m));
                if (!pending && (c == null || m.Ai == null || c.Health == null || c.Health.IsDead || !c.gameObject.activeInHierarchy)) continue;
                Teams team = pending ? s.PendingTeam : c.Team;
                if (ignoresFriendly && sound.fromTeam == team) continue;
                Vector3 d = sound.pos - (pending ? m.Position : c.transform.position);
                d.y = 0f;
                float range = sound.radius * (pending ? s.PendingHearing : m.Ai.hearingAbility);
                if (sound.soundType == SoundTypes.combatSound) range = Mathf.Min(range, SquadGunHearing);
                if (range <= 0f || d.sqrMagnitude >= range * range) continue;
                s.Heard = true;
                s.HeardPlayer = player != null && (sound.fromCharacter == player || sound.fromTeam == Teams.player);
                return;
            }
        }

        #endregion

        // ---------------- 警铃、铃声与点名 ----------------
        /// <summary>库房警铃就在旁边响：附近没醒的守货岗醒来（不拉走——他们不在岗，警铃召回的是押运员）。</summary>
        private void SquadHearsAlarm(Vector3 at)
        {
            foreach (Squad s in squads.ToArray())
            {
                if (s.State == SquadState.Alert) continue;
                if (FlatDistance(s.Site.Focus, at) <= SquadAlarmRadius) AlertSquad(s, Time.time, true, null, "store_alarm");
            }
        }

        /// <summary>信号工长点名：<paramref name="radius"/> 内没醒的守货岗全醒、直扑玩家。返回叫醒了几队。</summary>
        private int SquadsHearCall(Vector3 at, float radius)
        {
            int n = 0;
            foreach (Squad s in squads.ToArray())
            {
                if (s.State == SquadState.Alert || FlatDistance(s.Site.Focus, at) > radius) continue;
                AlertSquad(s, Time.time, true, null, "signal_call");
                n++;
            }
            return n;
        }

        /// <summary>轨道车铃：营地在铃 50 m 内、整队还围坐着，就一起起身过去看 35 秒，再走回来坐下。</summary>
        private void SquadHearsBell(Vector3 at, float until)
        {
            foreach (Squad s in squads)
            {
                if (s.State != SquadState.Dormant || FlatDistance(s.Site.Focus, at) > BellRadius) continue;
                s.State = SquadState.Roused;
                s.QuietUntil = until + 4f;
                int n = 0;
                foreach (Guard m in s.Members)
                {
                    if (!IsAlive(m) || m.Ai == null) continue;
                    Wake(m, false);
                    m.BellUntil = until;
                    m.BellTarget = Snap(at + new Vector3((m.Slot % 3 - 1) * 1.8f, 0f, -2.5f - m.Slot / 3 * 1.5f));
                    n++;
                }
                Guard voice = FirstAwake(s);
                if (voice != null) Bark(voice, L10n.T("铃响了？过去看看。", "Was that the bell? Let's go and look."));
                Debug.Log(LastShiftRaidScene.LogTag + " SQUAD_BELL site=" + s.Site.Def.Id + " members=" + n);
            }
        }

        private static string WakeLine(int serial)
        {
            switch (serial % 3)
            {
                case 0: return L10n.T("有人！", "Someone's here!");
                case 1: return L10n.T("那边！抄家伙！", "Over there! Grab your guns!");
                default: return L10n.T("谁？站住！", "Who's that? Stop right there!");
            }
        }

        private static void Bark(Guard g, string text)
        {
            if (g == null || g.Character == null) return;
            try { DialogueBubblesManager.Show(text, g.Character.transform, 2.2f, false, false, -1f, 1.2f).Forget(); }
            catch (Exception e) { ModBehaviour.DevLog(LastShiftRaidScene.LogTag + " 小队喊话气泡失败：" + e.Message); }
        }
    }
}
