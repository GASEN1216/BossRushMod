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
    /// 围圈小队（owner 2026-11-12「几个小队围成一圈，不让他们走动，玩家发现他们了才恢复 AI」）。归刷怪 owner：
    /// - 五处现成的摆件旁（职工院饭桌、中场换轨备料、东北退件堆场、北桥下失物、西场过磅间），一趟按种子挑三处，3–4 人面朝圈心站着，不巡逻。
    ///   坐标是作者布局（three.js，`yard-details-data.mjs` 的 yardDetailClusters）里摆件之间的空地，换算 Unity (x, y, -z)；
    ///   进图时逐个吸到导航、查脚下与墙层胶囊，站不住的位置本趟不用（日志 SQUAD_SITE），不足 3 个的整处不用。
    /// - 冻结：官方 AI 挂在角色子物体上（AICharacterController.Init 把自己挂到角色下），整只子物体停用——行为树 owner、寻路、
    ///   AI 的 Update 与官方听觉订阅一起停（口径同远处整只停用，只是停到 AI 这一层）；再 SetSleeping(true)（官方 IsAiming 恒假，不转向），
    ///   收枪、清移动输入、朝圈心定向。身体、受伤与尸体照常。醒来时重新启用子物体、还原 defaultWeaponOut、把玩家交给官方选敌，之后全归官方行为树。
    /// - 醒：玩家进 11 m 且在某人正前方 ±60° 内有视线（墙层与检修蒸汽都挡），或贴到 3.5 m 内有视线；任一人受伤或倒下；
    ///   官方 AISound（玩家脚步、枪声、嘎嘎、手雷落地）按官方半径 × 本人 hearingAbility 传到，枪声另限 30 m（附近开枪）；库房警铃在 30 m 内响。
    ///   一人醒全队醒，按位次错开 0.3 s，发现的那个喊一声。
    /// - 名额：小队算进同时活动 8 人与全趟人数，不算流动（Duty 写 squad:&lt;id&gt;）。场上同时只有一队；没有小队在场时给它留 3 个座位
    ///   （流动与列车送来的押运员少刷 3 人），营地被打空 40 秒后下一处才可能开张。只在离玩家 25–75 m 时开张，免得当面冒出来。
    /// - 和其它引人逻辑：库房警铃、维修棚蜂鸣器、跟车押运都不拉小队（它们不在岗、不是押运队；Duty 非空，这几处现成代码天然跳过）；
    ///   只有轨道车铃（铃附近 50 m）会把睡着的整队叫起来过去看 35 秒，没看到人就走回原位重新坐下——这是玩家调开营地的唯一手段。
    ///   远处整只停用照旧（55 / 65 m）；恢复时 AI 子物体仍停着，只补回 SetSleeping 与朝向。
    /// </summary>
    internal sealed partial class LastShiftEnemies
    {
        internal const int SquadMin = 3, SquadsPerRun = 3;
        internal const float SquadSight = 11f, SquadBump = 3.5f, SquadGunHearing = 30f, SquadAlarmRadius = 30f, SquadChat = 17f;
        internal const float SquadBreak = 40f, SquadStagger = .3f;
        /// <summary>正前方视锥半角 60°（cos）。</summary>
        private const float SquadCone = .5f;

        internal enum SquadState { Dormant, Roused, Alert }

        private sealed class SquadDef
        {
            internal readonly string Id, IdleZh, IdleEn;
            internal readonly float FocusX, FocusZ;
            internal readonly float[] Slots;
            internal readonly LastShiftPost[] Posts;
            internal readonly int Quality;
            internal SquadDef(string id, float fx, float fz, float[] slots, LastShiftPost[] posts, int quality, string zh, string en)
            { Id = id; FocusX = fx; FocusZ = fz; Slots = slots; Posts = posts; Quality = quality; IdleZh = zh; IdleEn = en; }
        }

        private sealed class SquadSite
        {
            internal SquadDef Def;
            internal Vector3 Focus;
            internal readonly List<Vector3> Spots = new List<Vector3>(4);
            internal bool Spent;
        }

        private sealed class Squad
        {
            internal SquadSite Site;
            internal readonly List<Guard> Members = new List<Guard>(4);
            internal SquadState State;
            internal bool Chatted, Heard, HeardPlayer;
            internal float QuietUntil;
            internal int Serial;
        }

        private const LastShiftPost Walk = LastShiftPost.Walker, Check = LastShiftPost.Inspector, Escorted = LastShiftPost.Escort;

        // three.js (x, z) 坐标对；圈心是摆件本身或摆件围出来的那块空地。离线核对见 tests/LastShiftSquadPlacementPropertyTest.py。
        private static readonly SquadDef[] SquadDefs =
        {
            // 夜班职工小院：饭桌（-47, 32）和长凳（-47, 34）四边各站一人。
            new SquadDef("canteen", -47f, 32.6f, new[] { -47.8f, 30.1f, -44.4f, 32.2f, -49.6f, 32.2f, -46.2f, 35.2f }, new[] { Walk, Check, Walk, Walk }, 1,
                "饭都凉了，车还没来。", "The food's gone cold and the train's still not here."),
            // 中场换轨备料：钢轨束、枕木垛、扣件托盘三面围着的空地，道班在这儿抽烟。
            new SquadDef("relay", 16.9f, 23.4f, new[] { 15.4f, 23.6f, 17.4f, 21.9f, 18.4f, 23.9f }, new[] { Walk, Walk, Walk }, 1,
                "再抽一根就上道。", "One more smoke, then back on the track."),
            // 东北退件堆场：退件垛和空托盘西侧，挨着东线风箱守货。
            new SquadDef("returns", 65.2f, -44.6f, new[] { 63.6f, -44.4f, 65f, -43f, 66.7f, -44.9f, 65.4f, -46.1f }, new[] { Escorted, Escorted, Check, Escorted }, 2,
                "没人认领的，先点清楚再说。", "Nobody's claimed this lot. Count it properly first."),
            // 北桥下排水检修点：围着桥下那件遗失货件分东西。
            new SquadDef("lost", -16f, -43f, new[] { -13.6f, -42.9f, -15.8f, -40.6f, -15.6f, -45.4f }, new[] { Walk, Check, Walk }, 1,
                "失物？谁捡着算谁的。", "Lost property? Finders keepers."),
            // 西场煤灰过磅间：雨棚下、磅砣台和地磅表头之间烤手。
            new SquadDef("coal", -63.3f, -9.3f, new[] { -64.8f, -9.4f, -63.1f, -7.8f, -61.8f, -9.6f }, new[] { Walk, Check, Walk }, 1,
                "炉灰还热乎，手伸过来烤烤。", "The ash is still warm. Get your hands over here."),
        };

        private readonly List<SquadSite> squadPlan = new List<SquadSite>(SquadsPerRun);
        private readonly List<SquadSite> squadPicks = new List<SquadSite>(SquadsPerRun);
        private Squad squad;
        private int squadSerial;
        private float nextSquad, nextSquadThink;
        private bool soundSubscribed;
        private int sightMask = -1, fowMask;

        // ---------------- 进图：核对营地、挑本趟三处 ----------------
        private void BuildSquads()
        {
            var valid = new List<SquadSite>(SquadDefs.Length);
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
                if (site.Spots.Count >= SquadMin) valid.Add(site);
                else Debug.LogWarning(LastShiftRaidScene.LogTag + " SQUAD_SITE " + def.Id + " 站得住的位置不足 " + SquadMin + "，整处不用");
            }
            System.Random pick = timetable.Stream("enemies#squads");
            while (valid.Count > 0 && squadPlan.Count < SquadsPerRun)
            {
                int k = pick.Next(valid.Count);
                squadPlan.Add(valid[k]);
                valid.RemoveAt(k);
            }
            if (!soundSubscribed && squadPlan.Count > 0) { AIMainBrain.OnSoundSpawned += OnYardSound; soundSubscribed = true; }
            var ids = new List<string>(squadPlan.Count);
            foreach (SquadSite site in squadPlan) ids.Add(site.Def.Id + ":" + site.Spots.Count);
            Debug.Log(LastShiftRaidScene.LogTag + " SQUAD_PLAN " + string.Join(",", ids.ToArray()));
        }

        private void DisposeSquads()
        {
            if (soundSubscribed) { AIMainBrain.OnSoundSpawned -= OnYardSound; soundSubscribed = false; }
            squad = null;
            squadPlan.Clear();
        }

        /// <summary>没有小队在场、还有营地和人数可开时，流动与列车送来的押运员给它留出的座位。</summary>
        private int PendingSquadSeats()
        {
            if (squad != null || closed || population - spawned - inFlight < SquadMin) return 0;
            foreach (SquadSite site in squadPlan) if (!site.Spent) return SquadMin;
            return 0;
        }

        /// <summary>开张一处营地：离玩家 25–75 m、座位与全趟人数够 3 人；成员排进刷怪队列（<paramref name="pendingSeats"/> 是开局已排队还没刷出的人）。</summary>
        private bool TryBoardSquad(int pendingSeats)
        {
            if (squad != null || closed || Time.time < nextSquad) return false;
            int seats = Math.Min(LiveCap - Live() - pendingSeats, population - spawned - inFlight - pendingSeats);
            if (seats < SquadMin) return false;
            Vector3 at = player.transform.position;
            squadPicks.Clear();
            foreach (SquadSite site in squadPlan)
            {
                if (site.Spent) continue;
                Vector3 offset = site.Focus - at;
                offset.y = 0f;
                float sqr = offset.sqrMagnitude;
                // 圈心离刷怪环内外沿各留 4 m：成员逐个刷出（每人一帧以上）时都还在 25–75 m 里，不当面冒出来。
                if (sqr >= (MinSpawn + 4f) * (MinSpawn + 4f) && sqr <= (MaxSpawn - 4f) * (MaxSpawn - 4f)) squadPicks.Add(site);
            }
            if (squadPicks.Count == 0) return false;
            SquadSite chosen = squadPicks[roster.Next(squadPicks.Count)];
            chosen.Spent = true;
            var s = new Squad { Site = chosen, State = SquadState.Dormant, Serial = squadSerial++ };
            int count = Math.Min(chosen.Spots.Count, seats);
            for (int i = 0; i < count; i++)
            {
                Vector3 spot = chosen.Spots[i], face = chosen.Focus - spot;
                face.y = 0f;
                LastShiftPost[] posts = chosen.Def.Posts;
                var g = new Guard
                {
                    Id = "squad_" + chosen.Def.Id + "#" + serial, Group = "squad", Duty = "squad:" + chosen.Def.Id, Quality = chosen.Def.Quality,
                    Index = serial++, Post = posts[i % posts.Length], Position = spot, Home = spot,
                    Forward = face.sqrMagnitude > .01f ? face.normalized : Vector3.forward, Squad = s, Slot = i,
                };
                s.Members.Add(g);
                queue.Add(g);
            }
            squad = s;
            Debug.Log(LastShiftRaidScene.LogTag + " SQUAD_BOARD site=" + chosen.Def.Id + " members=" + count + " live=" + Live() + " spawned=" + spawned + "/" + population);
            return true;
        }

        /// <summary>刷出后：冻在原位（或这队已经醒了就直接交给官方 AI）。</summary>
        private void SquadMemberReady(Guard g)
        {
            g.SavedWeaponOut = g.Ai.defaultWeaponOut;
            g.Ai.patrolPosition = g.Home;
            if (g.Squad.State == SquadState.Dormant) Freeze(g);
            else Wake(g, g.Squad.State == SquadState.Alert);
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
            if (toPlayer && player != null && player.Health != null && !player.Health.IsDead && player.mainDamageReceiver != null)
            {
                // 官方选敌的口径（forceTracePlayer 与带队共享目标都是直接写 searchedEnemy）；之后交火、追击、丢目标全归官方。
                ai.searchedEnemy = player.mainDamageReceiver;
                ai.SetNoticedToTarget(player.mainDamageReceiver);
                ai.noticed = true;
            }
        }

        /// <summary>一人醒全队醒：发现的那个先动并喊一声，其余按位次错开 <see cref="SquadStagger"/>。</summary>
        private void AlertSquad(Squad s, float now, bool toPlayer, Guard spotter, string why)
        {
            if (s.State == SquadState.Alert) return;
            s.State = SquadState.Alert;
            int order = 1;
            foreach (Guard m in s.Members)
            {
                if (!IsAlive(m)) continue;
                // 发现的那个、以及被铃声叫起来已经醒着的，立刻交给官方选敌；其余睡着的按位次错开醒。
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
            Squad s = squad;
            if (s == null || now < nextSquadThink) return;
            Vector3 at = player.transform.position;
            Vector3 toFocus = s.Site.Focus - at;
            toFocus.y = 0f;
            nextSquadThink = now + (toFocus.sqrMagnitude < 40f * 40f ? .2f : .5f);
            bool any = false;
            foreach (Guard m in s.Members) if (Counts(m)) { any = true; break; }
            if (!any) foreach (Guard q in queue) if (q.Squad == s) { any = true; break; }
            if (!any)
            {
                squad = null;
                nextSquad = now + SquadBreak;
                Debug.Log(LastShiftRaidScene.LogTag + " SQUAD_CLEARED site=" + s.Site.Def.Id);
                return;
            }
            switch (s.State)
            {
                case SquadState.Dormant: TickDormant(s, now, at, toFocus.sqrMagnitude); break;
                case SquadState.Roused: TickRoused(s, now, at); break;
                default: TickAlert(s, now); break;
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
                // 打过一场的守营：不交火时回营地 5 m 内，官方巡逻就在营地附近转。
                Vector3 off = m.Character.transform.position - m.Home;
                off.y = 0f;
                if (off.sqrMagnitude > 5f * 5f) Move(m, m.Home, 4300000 + m.Slot, now);
                else m.Ai.patrolPosition = m.Home;
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

        /// <summary>
        /// 官方 AISound 的听觉口径（AICharacterController.OnSound）：同阵营的脚步与枪声不算，按官方半径 × 本人 hearingAbility 判距；
        /// 枪声另限 <see cref="SquadGunHearing"/>。只记一笔，交给下一拍处理，回调里不做别的。
        /// </summary>
        private void OnYardSound(AISound sound)
        {
            Squad s = squad;
            if (closed || s == null || s.State != SquadState.Dormant || s.Heard) return;
            Vector3 gap = sound.pos - s.Site.Focus;
            gap.y = 0f;
            float reach = sound.radius + 4f;
            if (gap.sqrMagnitude > reach * reach) return;
            foreach (Guard m in s.Members)
            {
                CharacterMainControl c = m.Character;
                if (c == null || m.Ai == null || c.Health == null || c.Health.IsDead || !c.gameObject.activeInHierarchy) continue;
                if (sound.soundType != SoundTypes.grenadeDropSound && sound.fromTeam == c.Team) return;
                Vector3 d = sound.pos - c.transform.position;
                d.y = 0f;
                float range = sound.radius * m.Ai.hearingAbility;
                if (sound.soundType == SoundTypes.combatSound) range = Mathf.Min(range, SquadGunHearing);
                if (d.sqrMagnitude >= range * range) continue;
                s.Heard = true;
                s.HeardPlayer = player != null && (sound.fromCharacter == player || sound.fromTeam == Teams.player);
                return;
            }
        }

        // ---------------- 警铃与铃声 ----------------
        /// <summary>库房警铃就在营地旁边响：睡着的整队醒来（不拉走——他们不在岗，警铃召回的是押运员）。</summary>
        private void SquadHearsAlarm(Vector3 at)
        {
            Squad s = squad;
            if (s == null || s.State == SquadState.Alert) return;
            Vector3 gap = s.Site.Focus - at;
            gap.y = 0f;
            if (gap.sqrMagnitude <= SquadAlarmRadius * SquadAlarmRadius) AlertSquad(s, Time.time, true, null, "store_alarm");
        }

        /// <summary>轨道车铃：营地在铃 50 m 内、整队还睡着，就一起起身过去看 35 秒，再走回来坐下。</summary>
        private void SquadHearsBell(Vector3 at, float until)
        {
            Squad s = squad;
            if (s == null || s.State != SquadState.Dormant) return;
            Vector3 gap = s.Site.Focus - at;
            gap.y = 0f;
            if (gap.sqrMagnitude > BellRadius * BellRadius) return;
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
