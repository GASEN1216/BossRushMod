#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationLastShiftDrillCases.cs - 末班编组站 Dev 演练的各条用例（**非只读**）
// ============================================================================
// 入口、开跑门、会话外壳、站位与快进小工具、还原和纯判据在 F3GameplayValidationLastShiftDrill.cs，
// 那里头注释写的隔离纪律（不写存档、不收录剧情、不读线索、不开箱、不签收、不发物品、站位离出口 5 m / 离道 4.5 m 外、最后还原）这里同样适用。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private static readonly string[] LastShiftDrillExitOrder = { "north", "west", "east", "bell" };

        // ====================================================================
        // 1. 装配核对（只读）
        // ====================================================================

        private IEnumerator RunLastShiftDrillAssembly()
        {
            const string id = "LSY_DRILL_ASSEMBLY";
            Stopwatch sw = Stopwatch.StartNew();
            LastShiftSession session;
            string gone;
            if (!LastShiftDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            LastShiftWorld world = session.DevWorld;
            LastShiftTrains trains = session.DevTrains;
            LastShiftExtraction extraction = session.DevExtraction;
            if (session.DevLoot == null) errors.Add("loot_missing");
            if (session.DevEnemies == null) errors.Add("enemies_missing");
            if (session.DevRun == null) errors.Add("story_run_missing");
            if (session.DevNavigation == null) errors.Add("navigation_missing");
            if (world.LootMarkers.Count != 20) errors.Add("loot_markers_" + world.LootMarkers.Count);
            foreach (string interact in LastShiftDrillInteractIds)
                if (!world.Interacts.ContainsKey(interact)) errors.Add("interact_marker_missing_" + interact);
            List<string> exitIds = extraction.DevExitIds();
            foreach (string exit in LastShiftDrillExitOrder)
            {
                if (!world.Exits.ContainsKey(exit)) errors.Add("exit_marker_missing_" + exit);
                else if (!exitIds.Contains(exit)) errors.Add("extraction_exit_missing_" + exit);
                else if (!extraction.DevHasCountdown(exit)) notes.Add("official_countdown_missing_" + exit + "(hud_fallback)");
            }
            var groups = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Transform marker in world.SpawnMarkers)
            {
                string group = marker.parent != null ? marker.parent.name : "?";
                int n;
                groups.TryGetValue(group, out n);
                groups[group] = n + 1;
            }
            foreach (string group in LastShiftDrillSpawnGroups)
                if (!groups.ContainsKey(group)) errors.Add("spawn_group_missing_" + group);
            if (session.DevUnreachableSpawns > 0) errors.Add("nav_unreachable_spawns_" + session.DevUnreachableSpawns);
            int walkable = session.DevNavigation == null ? 0 : session.DevNavigation.CountWalkableNodes();
            if (walkable == 0) errors.Add("navigation_no_walkable_nodes");

            // 场景包可见网格的着色器（装配时已硬校验过一次；这里再读回一遍，只看 Visual 下的场景网格，不看运行时加的环、箭头与箱子）。
            int materials = 0;
            Transform visual = world.Root.transform.Find("Visual");
            if (visual == null) errors.Add("visual_root_missing");
            else
                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) { errors.Add("material_missing_" + renderer.name); continue; }
                        materials++;
                        Shader shader = material.shader;
                        if (shader == null || Array.IndexOf(LastShiftWorld.WorldShaders, shader.name) < 0 || !shader.isSupported)
                        {
                            errors.Add("world_shader_" + (shader == null ? "null" : shader.name) + "_on_" + renderer.name);
                            break;
                        }
                    }

            // 列车：五节车厢克隆与各自的墙层碰撞盒、示范推车、三个机关交互。
            Transform holder = world.Root.transform.Find("LastShiftTrains");
            if (holder == null) errors.Add("train_holder_missing");
            else
                for (int i = 0; i < trains.DevCarSlots; i++)
                    if (holder.Find("Car_" + i) == null) errors.Add("car_clone_missing_" + i);
            foreach (LastShiftTrains.DevBodyInfo body in trains.DevBodies())
                if (body.Layer != trains.DevWallLayer) errors.Add("car_body_layer_" + body.Model + "_" + body.Layer);
            if (((1 << trains.DevWallLayer) & GameplayDataSettings.Layers.wallLayerMask.value) == 0) errors.Add("train_wall_layer_not_in_wall_mask");
            if (trains.DevInteractCount != 3) errors.Add("train_interacts_" + trains.DevInteractCount);
            if (session.DevEnemies != null && session.DevEnemies.DevDeviceHosts != 2) errors.Add("yard_devices_" + session.DevEnemies.DevDeviceHosts);
            if (session.DevLoot != null)
            {
                int devices = 0;
                foreach (LastShiftLoot.DevBoxInfo box in session.DevLoot.DevBoxes()) if (box.Gated && box.HasDevice) devices++;
                if (devices != 5) errors.Add("gated_container_devices_" + devices);
            }

            // 室内屋顶：场景根 LastShiftIndoorRooms 下每个室内触发区都挂上官方 ShadowOnly 淡出。
            int rooms = 0, fades = 0;
            GameObject indoor = null;
            foreach (GameObject candidate in world.Root.scene.GetRootGameObjects())
                if (candidate.name == "LastShiftIndoorRooms") { indoor = candidate; break; }
            if (indoor == null) errors.Add("indoor_rooms_root_missing");
            else
            {
                rooms = indoor.transform.childCount;
                fades = indoor.GetComponentsInChildren<OcclusionFadeObject>(true).Length;
                if (rooms == 0 || fades == 0) errors.Add("indoor_roofs_not_bound_" + fades + "_of_" + rooms);
            }
            if (world.NightLamps.Count == 0) errors.Add("night_lamps_missing");

            // HUD 目标行就是列车 owner 此刻给的目标（会话半秒一刷）。
            yield return WaitSeconds(0.6f);
            if (LastShiftDrillLive(out session, out gone) && session.DevLoot != null)
            {
                string want = session.DevTrains.Objective(session.DevLoot.CargoClaimed);
                if (!string.Equals(session.DevHudObjective, want, StringComparison.Ordinal)) errors.Add("hud_objective_stale");
            }
            LastShiftTimetable t = trains.Timetable;
            notes.Add("loot_markers=" + world.LootMarkers.Count + ",spawn_markers=" + world.SpawnMarkers.Count + ",spawn_groups=" + groups.Count
                + ",pois=" + world.Pois.Count + ",walkable_nodes=" + walkable + ",world_materials=" + materials + ",indoor_rooms=" + rooms
                + ",indoor_fades=" + fades + ",night_lamps=" + world.NightLamps.Count + ",lamp_shader_count=" + Shader.GetGlobalFloat("_LastShiftLampCount").ToString("F0"));
            notes.Add("phase=" + t.Phase + ",trip=" + t.Trip + ",training=" + t.Training + ",line=" + t.Line + ",event=" + t.Event
                + ",premium=" + t.Premium + ",manifest=" + trains.Manifest + ",cart=" + trains.DevHasCart + ",runs=" + LastShiftStory.Runs);
            RecordLastShiftDrill(id, sw, errors, notes);
        }

        // ====================================================================
        // 2. 首班教学
        // ====================================================================

        private IEnumerator RunLastShiftDrillFirstTrain()
        {
            const string id = "LSY_DRILL_FIRST_TRAIN";
            Stopwatch sw = Stopwatch.StartNew();
            LastShiftSession session;
            string gone;
            if (!LastShiftDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            LastShiftTrains trains = session.DevTrains;
            LastShiftTimetable t = trains.Timetable;
            if (!t.Training)
            {
                Record(id, "SKIP", 0L, "runs=" + LastShiftStory.Runs + ",trip=" + t.Trip,
                    "这一趟没有首班教学（本槽安全撤离过、或首班已经开过去）；要测首班请换一个没撤离过编组站的测试档");
                yield break;
            }
            var errors = new List<string>();
            var notes = new List<string>();
            var skipped = new List<string>();
            notes.Add("start:phase=" + t.Phase + ",phase_time=" + t.PhaseTime.ToString("F2") + ",cart_hit=" + t.CartHit
                + ",dispatch_wait=" + t.DispatchWait + ",away=" + t.AwayFromRail.ToString("F1") + ",demo_forced=" + t.DemoForced);
            bool sawDispatchWaitStart = t.DispatchWait;

            // A. 看完示范之前道岔不给扳：走交互完成那一步，被拒、字幕说明、线路不变。
            if (!t.CartHit)
            {
                char line = t.Line;
                int mark = session.DevStatusMark;
                trains.DevThrowSwitch();
                yield return null;
                if (t.Line != line || t.DispatchChosen) errors.Add("switch_allowed_before_demo");
                if (LastShiftDrillCount(session.DevStatusSince(mark), new[] { "先在南护栏后看完首班示范", "Watch the first-train demonstration" }) != 1)
                    errors.Add("demo_refusal_caption_missing");
                if (trains.DevArrowCount != 0) errors.Add("arrows_shown_on_refusal");
            }
            else skipped.Add("switch_refused_before_demo");

            // B. 不在南护栏后：首班停表等人（离开累计到 60 秒才照常发车，只拿余量里的几秒做证据）。
            double budget = LastShiftDrillAwayBudget(t.AwayFromRail, LastShiftTimetable.DemoFallback);
            if (t.Phase == LastShiftPhase.Approach && !t.CartHit && !t.DemoForced && budget > 0)
            {
                yield return LastShiftDrillStandAwayFromRail(session, errors);
                double before = t.PhaseTime, awayBefore = t.AwayFromRail;
                double forwarded = session.DevTrains.DevFastForward(CharacterMainControl.Main, budget);
                _lastShiftDrillForwarded += forwarded;
                string why;
                if (!LastShiftDrillHeldForPlayer(before, t.PhaseTime, awayBefore, t.AwayFromRail, forwarded, out why)) errors.Add("away:" + why);
                if (t.Phase != LastShiftPhase.Approach) errors.Add("away:train_left_" + t.Phase);
                notes.Add("away:forwarded=" + forwarded.ToString("F1") + ",away_total=" + t.AwayFromRail.ToString("F1"));
            }
            else skipped.Add("waits_while_player_away(budget=" + budget.ToString("F1") + ")");

            // C. 站到南护栏后：鸣笛结束进站、撞飞示范推车（不致命）。
            if (!t.CartHit)
            {
                Vector3 cartBefore = trains.DevCartPosition;
                int mark = session.DevStatusMark;
                if (t.Phase == LastShiftPhase.Approach && !t.DemoForced) yield return LastShiftDrillStandAtRail(session, errors);
                yield return LastShiftDrillAdvanceTo(x => x.CartHit || x.DispatchWait, 60, false, errors, notes, "demo");
                if (!t.CartHit) errors.Add("cart_not_hit");
                else
                {
                    List<string> since = session.DevStatusSince(mark);
                    if (LastShiftDrillCount(since, new[] { "手推车被撞飞", "the cart is smashed aside" }) != 1) errors.Add("cart_hit_caption_missing");
                    yield return LastShiftDrillAdvance(1.3);
                    if (trains.DevHasCart && LastShiftDrillFlat(trains.DevCartPosition, cartBefore) < 1f) errors.Add("cart_not_moved");
                }
                if (t.Phase != LastShiftPhase.Arrival) errors.Add("demo_phase_" + t.Phase);
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.Health != null && player.Health.IsDead) errors.Add("player_killed_by_first_train");
            }
            else skipped.Add("cart_demo");

            // D. 撞完推车停在岔心前等调度：等多久都不动，HUD 小标写「等你扳岔」。
            if (t.Training && t.Phase == LastShiftPhase.Arrival && !t.DispatchChosen)
            {
                int mark = session.DevStatusMark;
                yield return LastShiftDrillAdvanceTo(x => x.DispatchWait, 20, false, errors, notes, "hold");
                if (!sawDispatchWaitStart
                    && LastShiftDrillCount(session.DevStatusSince(mark), new[] { "首班示范已完成", "Demonstration done" }) != 1)
                    errors.Add("dispatch_wait_caption_missing");
                yield return LastShiftDrillAdvance(5.0);
                string why;
                if (!LastShiftDrillTrainingHoldOk(t.Phase, t.DispatchWait, t.PhaseTime, t.LeadZ, out why)) errors.Add("hold:" + why);
                if (trains.DevNavState != "on" + t.LineUsed) errors.Add("hold:nav_state_" + trains.DevNavState);
                if (!LastShiftDrillContainsAny(trains.Chip(), "等你扳岔", "waiting for the switch")) errors.Add("hold:chip_missing_wait");
                string objective = trains.Objective(session.DevLoot != null && session.DevLoot.CargoClaimed);
                if (!LastShiftDrillContainsAny(objective, "扳道岔", "throw the switch")) errors.Add("hold:objective_not_switch");
            }
            else skipped.Add("dispatch_hold");
            notes.Add("end:phase=" + t.Phase + ",lead_z=" + t.LeadZ.ToString("F1") + ",dispatch_wait=" + t.DispatchWait
                + ",not_covered=" + (skipped.Count == 0 ? "-" : string.Join("/", skipped.ToArray())));
            if (errors.Count == 0 && skipped.Count > 0)
            {
                Record(id, skipped.Count >= 4 ? "SKIP" : "WARN", sw.ElapsedMilliseconds, string.Join(" | ", notes.ToArray()),
                    "开跑时首班已经走过这几步，没覆盖到：" + string.Join(",", skipped.ToArray()) + "（进站后立刻按演练、别离开出生点可全覆盖）");
                yield break;
            }
            RecordLastShiftDrill(id, sw, errors, notes);
        }

        // ====================================================================
        // 3. 道岔、条件出口与拉铃
        // ====================================================================

        private IEnumerator RunLastShiftDrillSwitchAndExits()
        {
            const string id = "LSY_DRILL_SWITCH_AND_EXITS";
            Stopwatch sw = Stopwatch.StartNew();
            LastShiftSession session;
            string gone;
            if (!LastShiftDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            LastShiftTrains trains = session.DevTrains;
            LastShiftTimetable t = trains.Timetable;
            LastShiftExtraction extraction = session.DevExtraction;

            // 先站到出生点附近的安全处（离道、离出口），再等到道岔给扳。
            yield return LastShiftDrillStand(session, session.DevWorld.PlayerSpawn.position, errors, "spawn");
            yield return LastShiftDrillAdvanceTo(LastShiftDrillCanSwitch, 400, true, errors, notes, "to_switchable");
            if (!LastShiftDrillCanSwitch(t)) { RecordLastShiftDrill(id, sw, errors, notes); yield break; }

            // 扳道岔：走交互完成那一步；改线、站场广播、8 秒地面箭头、导航按新线重刷。
            bool teaching = t.Training && t.Phase == LastShiftPhase.Arrival;
            char before = t.Line, target = before == 'W' ? 'E' : 'W';
            int mark = session.DevStatusMark;
            trains.DevThrowSwitch();
            yield return null;
            if (t.Line != target) errors.Add("switch_not_thrown_" + t.Line);
            if (!t.DispatchChosen || t.DispatchWait) errors.Add("dispatch_state_" + t.DispatchChosen + "_" + t.DispatchWait);
            if (t.LineUsed != target) errors.Add("line_used_" + t.LineUsed);
            if (trains.DevArrowCount != 9) errors.Add("dispatch_arrows_" + trains.DevArrowCount);
            if (LastShiftDrillCount(session.DevStatusSince(mark), new[] { "站场广播", "Yard tannoy" }) != 1) errors.Add("tannoy_caption_missing");
            string wantNav = t.TrainOnLine ? "on" + t.LineUsed : "clear";
            if (trains.DevNavState != wantNav) errors.Add("nav_state_" + trains.DevNavState + "_want_" + wantNav);
            notes.Add("switch:teaching=" + teaching + ",line=" + before + ">" + t.Line + ",phase=" + t.Phase);
            LastShiftDrillSampleExits(session, "after_switch", errors, notes, seen);

            // 一列车走完：进站、停靠（锁线时扳岔被拒）、停满 12 秒（东台卸完）、出站、净空，每一步核对条件出口。
            yield return LastShiftDrillAdvanceTo(x => x.Phase == LastShiftPhase.Arrival || x.Phase == LastShiftPhase.Dwell, 200, true, errors, notes, "to_arrival");
            if (t.Phase == LastShiftPhase.Arrival) LastShiftDrillSampleExits(session, "arrival", errors, notes, seen);
            yield return LastShiftDrillAdvanceTo(x => x.Phase == LastShiftPhase.Dwell, 400, true, errors, notes, "to_dwell");
            if (t.Phase == LastShiftPhase.Dwell)
            {
                LastShiftDrillSampleExits(session, "dwell", errors, notes, seen);
                char locked = t.Line;
                mark = session.DevStatusMark;
                trains.DevThrowSwitch();
                yield return null;
                if (t.Line != locked) errors.Add("switch_allowed_while_locked");
                if (LastShiftDrillCount(session.DevStatusSince(mark), new[] { "本班已锁线", "locked the route" }) != 1) errors.Add("locked_caption_missing");
                yield return LastShiftDrillAdvance(LastShiftTimetable.EastUnloadSeconds + 0.5);
                if (t.Phase == LastShiftPhase.Dwell) LastShiftDrillSampleExits(session, "dwell_unloaded", errors, notes, seen);
            }
            yield return LastShiftDrillAdvanceTo(x => x.Phase == LastShiftPhase.Depart, 200, true, errors, notes, "to_depart");
            if (t.Phase == LastShiftPhase.Depart) LastShiftDrillSampleExits(session, "depart", errors, notes, seen);
            yield return LastShiftDrillAdvanceTo(x => x.Phase == LastShiftPhase.Clear, 200, true, errors, notes, "to_clear");
            if (t.Phase == LastShiftPhase.Clear) LastShiftDrillSampleExits(session, "clear", errors, notes, seen);

            // 拉轨道车铃：走交互完成那一步；字幕、会话把铃声交给押运队（下一帧清标志）、满 14 秒接客亭才开。
            if (t.BellAt >= 0) notes.Add("bell:already_rung_before_drill_at=" + t.BellAt.ToString("F1"));
            else
            {
                mark = session.DevStatusMark;
                trains.DevRingBell();
                _lastShiftDrillBellRung = true;
                yield return null;
                yield return null;
                if (t.BellAt < 0) errors.Add("bell_not_rung");
                if (trains.Ringing) errors.Add("bell_not_handed_to_patrols");
                if (LastShiftDrillCount(session.DevStatusSince(mark), new[] { "已拉铃", "Bell rung" }) != 1) errors.Add("bell_caption_missing");
                yield return LastShiftDrillAdvance(LastShiftTimetable.BellWait - 1.0);
                trains.DevSyncExits(CharacterMainControl.Main);
                double early = t.Clock - t.BellAt;
                bool earlyOpen = extraction.IsOpen("bell");
                if (!LastShiftDrillBellOk(early, earlyOpen) || earlyOpen) errors.Add("bell_open_early_" + early.ToString("F2"));
                LastShiftDrillSampleExits(session, "bell_13s", errors, notes, seen);
                yield return LastShiftDrillAdvance(1.3);
                trains.DevSyncExits(CharacterMainControl.Main);
                double late = t.Clock - t.BellAt;
                bool lateOpen = extraction.IsOpen("bell");
                if (!LastShiftDrillBellOk(late, lateOpen) || !lateOpen) errors.Add("bell_not_open_" + late.ToString("F2"));
                LastShiftDrillSampleExits(session, "bell_14s", errors, notes, seen);
            }
            var coverage = new List<string>();
            foreach (string exit in LastShiftDrillExitOrder)
            {
                int mask;
                seen.TryGetValue(exit, out mask);
                coverage.Add(exit + "=" + ((mask & 1) != 0 ? "open" : "") + ((mask & 3) == 3 ? "+" : "") + ((mask & 2) != 0 ? "closed" : ""));
            }
            notes.Add("coverage:" + string.Join(",", coverage.ToArray()));
            RecordLastShiftDrill(id, sw, errors, notes);
        }

        private static bool LastShiftDrillCanSwitch(LastShiftTimetable t)
        {
            string reason;
            return t.CanSwitch(out reason);
        }

        /// <summary>同步一次条件出口，再逐个核对撤离 owner 的开关、贴地环与班次规则（北公路恒开）。</summary>
        private static void LastShiftDrillSampleExits(LastShiftSession session, string tag, List<string> errors, List<string> notes, Dictionary<string, int> seen)
        {
            LastShiftTrains trains = session.DevTrains;
            LastShiftExtraction extraction = session.DevExtraction;
            LastShiftTimetable t = trains.Timetable;
            trains.DevSyncExits(CharacterMainControl.Main);
            var open = new List<string>();
            foreach (string exit in LastShiftDrillExitOrder)
            {
                bool rule = t.ExitOpen(exit), owner = extraction.IsOpen(exit), ring = extraction.DevRingVisible(exit);
                string why;
                if (!LastShiftDrillExitAgrees(exit, owner, ring, rule, out why)) errors.Add(tag + ":" + why);
                int mask;
                seen.TryGetValue(exit, out mask);
                seen[exit] = mask | (owner ? 1 : 2);
                if (owner) open.Add(exit);
            }
            notes.Add(tag + ":phase=" + t.Phase + ",line=" + t.LineUsed + ",east_unloaded=" + t.EastUnloaded + ",open=" + LastShiftDrillJoinSorted(open));
        }

        // ====================================================================
        // 4. 车体碰撞盒与导航封线
        // ====================================================================

        private IEnumerator RunLastShiftDrillTrainBodies()
        {
            const string id = "LSY_DRILL_TRAIN_BODIES_AND_NAV";
            Stopwatch sw = Stopwatch.StartNew();
            LastShiftSession session;
            string gone;
            if (!LastShiftDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            ArenaPrototypeNavigation nav = session.DevNavigation;
            if (nav == null) { Record(id, "FAIL", 0L, string.Empty, "导航不可用"); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            LastShiftTrains trains = session.DevTrains;
            LastShiftTimetable t = trains.Timetable;
            yield return LastShiftDrillStand(session, session.DevWorld.PlayerSpawn.position, errors, "spawn");
            yield return LastShiftDrillAdvanceTo(x => !x.TrainOnLine && LastShiftDrillCanSwitch(x), 400, true, errors, notes, "to_clear");
            if (t.TrainOnLine) { RecordLastShiftDrill(id, sw, errors, notes); yield break; }
            // 换到另一股道：上一条用例走的是一台，这一条走另一台，两股道的封线都看到。
            char before = t.Line;
            trains.DevThrowSwitch();
            trains.DevHideArrows();
            char line = t.Line, other = line == 'W' ? 'E' : 'W';
            if (line == before) errors.Add("switch_refused_while_clear");
            Func<Vector3, Vector3, Vector3, bool> onLine = (a, b, c) => LastShiftDrillNodeInEnvelope(a, b, c, line);
            Func<Vector3, Vector3, Vector3, bool> otherOnly = (a, b, c) => LastShiftDrillNodeInEnvelope(a, b, c, other) && !LastShiftDrillNodeInEnvelope(a, b, c, line);
            int envelopeNodes, baseWalkable, otherNodes, otherBase;
            nav.DevCountNodes(onLine, out envelopeNodes, out baseWalkable);
            nav.DevCountNodes(otherOnly, out otherNodes, out otherBase);
            int baseTotal = nav.CountWalkableNodes();

            yield return LastShiftDrillAdvanceTo(x => x.Phase == LastShiftPhase.Dwell && x.LineUsed == line, 700, true, errors, notes, "to_dwell");
            int dwellWalkable = -1, otherDwell = -1, cars = 0;
            if (t.Phase == LastShiftPhase.Dwell)
            {
                Physics.SyncTransforms();
                List<LastShiftCar> list = t.Cars();
                List<LastShiftTrains.DevBodyInfo> bodies = trains.DevBodies();
                var shown = new bool[bodies.Count];
                foreach (LastShiftCar car in list)
                {
                    if (car.Z < -81 || car.Z > 84 || car.Model >= bodies.Count) continue;
                    cars++;
                    shown[car.Model] = true;
                    LastShiftTrains.DevBodyInfo body = bodies[car.Model];
                    string tag = "car" + car.Model;
                    if (!body.CarActive) errors.Add(tag + ":model_hidden");
                    if (!body.BodyActive) { errors.Add(tag + ":body_inactive"); continue; }
                    if (body.Layer != trains.DevWallLayer) errors.Add(tag + ":layer_" + body.Layer);
                    string why;
                    if (!LastShiftDrillBodyOnLine(car.X, car.Z, body.Center.x, -body.Center.z, line, out why)) errors.Add(tag + ":" + why);
                    if (Mathf.Abs(body.Size.x - (float)car.Width) > 0.01f || Mathf.Abs(body.Size.z - (float)car.Depth) > 0.01f || Mathf.Abs(body.Size.y - 3.6f) > 0.01f)
                        errors.Add(tag + ":size_" + body.Size.ToString("F2"));
                    // 碰撞盒真的进了物理场景：在盒心用墙层查得到它自己。
                    bool found = false;
                    foreach (Collider hit in Physics.OverlapBox(body.Center, body.Size * 0.25f, Quaternion.Euler(0f, body.Yaw, 0f), 1 << body.Layer, QueryTriggerInteraction.Ignore))
                        if (hit != null && hit.name == "COL_Car_" + car.Model) { found = true; break; }
                    if (!found) errors.Add(tag + ":not_in_physics");
                }
                for (int i = 0; i < bodies.Count; i++)
                    if (!shown[i] && (bodies[i].BodyActive || bodies[i].CarActive)) errors.Add("car" + i + ":active_while_not_in_train");
                if (cars == 0) errors.Add("no_cars_during_dwell");
                int dwellNodes, otherNodesDwell;
                nav.DevCountNodes(onLine, out dwellNodes, out dwellWalkable);
                nav.DevCountNodes(otherOnly, out otherNodesDwell, out otherDwell);
                if (trains.DevNavState != "on" + line) errors.Add("dwell:nav_state_" + trains.DevNavState);
            }
            yield return LastShiftDrillAdvanceTo(x => x.Phase == LastShiftPhase.Clear, 300, true, errors, notes, "to_clear_after");
            int clearNodes, clearWalkable;
            nav.DevCountNodes(onLine, out clearNodes, out clearWalkable);
            int clearTotal = nav.CountWalkableNodes();
            if (t.Phase == LastShiftPhase.Clear)
            {
                foreach (LastShiftTrains.DevBodyInfo body in trains.DevBodies())
                    if (body.BodyActive || body.CarActive) errors.Add("car" + body.Model + ":active_after_clear");
                if (trains.DevNavState != "clear") errors.Add("clear:nav_state_" + trains.DevNavState);
            }
            if (dwellWalkable >= 0)
            {
                string why;
                if (!LastShiftDrillNavOk(envelopeNodes, baseWalkable, dwellWalkable, clearWalkable, baseTotal, clearTotal, otherBase, otherDwell, out why))
                    errors.Add("nav:" + why);
            }
            notes.Add("line=" + line + ",cars=" + cars + ",heavy=" + t.IsHeavy + ",envelope_nodes=" + envelopeNodes + ",walkable_clear=" + baseWalkable
                + ",walkable_dwell=" + dwellWalkable + ",walkable_after=" + clearWalkable + ",total=" + baseTotal + ">" + clearTotal
                + ",other_line_only=" + otherNodes + ":" + otherBase + ">" + otherDwell);
            RecordLastShiftDrill(id, sw, errors, notes);
        }

        /// <summary>导航三角形在不在这股道的车体包络里：口径与列车 owner 封线一致（最低顶点 &lt; 4.75 m、重心在包络里）。</summary>
        private static bool LastShiftDrillNodeInEnvelope(Vector3 a, Vector3 b, Vector3 c, char line)
        {
            if (Mathf.Min(a.y, Mathf.Min(b.y, c.y)) >= 4.75f) return false;
            Vector3 m = (a + b + c) / 3f;
            return LastShiftTimetable.InEnvelope(m.x, -m.z, line);
        }

        // ====================================================================
        // 5. 箱柜与有条件的箱子（只读：不开、不签、不放货）
        // ====================================================================

        private IEnumerator RunLastShiftDrillLoot()
        {
            const string id = "LSY_DRILL_LOOT";
            Stopwatch sw = Stopwatch.StartNew();
            LastShiftSession session;
            string gone;
            if (!LastShiftDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            LastShiftLoot loot = session.DevLoot;
            if (loot == null) { Record(id, "FAIL", 0L, string.Empty, "搜刮 owner 不可用"); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            LastShiftTrains trains = session.DevTrains;
            LastShiftTimetable t = trains.Timetable;
            LastShiftWorld world = session.DevWorld;

            List<LastShiftLoot.DevBoxInfo> boxes = loot.DevBoxes();
            var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
            int planned = 0;
            foreach (LastShiftLoot.DevBoxInfo box in boxes)
            {
                int n;
                kinds.TryGetValue(box.Kind, out n);
                kinds[box.Kind] = n + 1;
                if (!box.Gated || box.Kind == "side")
                {
                    if (box.Planned < 0) errors.Add("not_planned_" + box.Id);
                    else planned += box.Planned;
                }
                if (box.Gated && !box.HasDevice) errors.Add("gated_without_device_" + box.Id);
                if (box.Gated && box.Built && !box.Unlocked && box.LootActive) errors.Add("gated_box_open_" + box.Id);
                if (box.Kind == "sealed" && box.Unlocked) errors.Add("sealed_unlocked_without_claim_" + box.Id);
                if (box.Opened) notes.Add("opened_before_drill_" + box.Id);
            }
            int sides = world.Interacts.ContainsKey("stamp") ? 1 : 0;
            if (boxes.Count != world.LootMarkers.Count + sides) errors.Add("boxes_" + boxes.Count + "_markers_" + world.LootMarkers.Count);
            foreach (KeyValuePair<string, int> want in new[] { new KeyValuePair<string, int>("sealed", 2), new KeyValuePair<string, int>("drop", 2),
                         new KeyValuePair<string, int>("risk", 3), new KeyValuePair<string, int>("side", sides) })
            {
                int n;
                kinds.TryGetValue(want.Key, out n);
                if (n != want.Value) errors.Add("kind_" + want.Key + "_" + n);
            }
            string risk = LastShiftDrillJoinSorted(loot.RiskContainerIds);
            if (risk != "bondCab,eastRisk,westRisk") errors.Add("risk_ids_" + risk);
            var kindNotes = new List<string>();
            foreach (KeyValuePair<string, int> kind in kinds) kindNotes.Add(kind.Key + "=" + kind.Value);
            notes.Add("boxes=" + boxes.Count + ",planned_slots=" + planned + ",kinds=" + LastShiftDrillJoinSorted(kindNotes) + ",risk=" + risk);

            // 下一列停靠车之前：站到它要停的那一台的零担箱旁（60 m 内才建箱），封签货两只都锁着。
            yield return LastShiftDrillAdvanceTo(x => x.Phase == LastShiftPhase.Approach && !x.Training, 500, true, errors, notes, "to_approach");
            if (t.Phase != LastShiftPhase.Approach) { RecordLastShiftDrill(id, sw, errors, notes); yield break; }
            char line = t.Line;
            string dropId = line == 'E' ? "dropE" : "dropW", otherDrop = line == 'E' ? "dropW" : "dropE";
            LastShiftLoot.DevBoxInfo drop = LastShiftDrillBox(loot, dropId);
            if (drop.Id == null) errors.Add("drop_box_missing_" + dropId);
            else
            {
                // 往站台外侧（离道更远的一侧）挪 4 m 站。
                Vector3 stand = drop.Position + new Vector3(line == 'E' ? 4f : -4f, 0f, 0f);
                yield return LastShiftDrillStand(session, stand, errors, "platform");
                yield return WaitSeconds(3f);
            }
            int tripBefore = t.Trip;
            foreach (LastShiftLoot.DevBoxInfo box in loot.DevBoxes())
            {
                if (box.Kind == "sealed" && (box.Unlocked || box.LootActive)) errors.Add("before_stop:sealed_open_" + box.Id);
                if (box.Kind == "drop" && box.BatchTrip == tripBefore) errors.Add("before_stop:drop_batch_early_" + box.Id);
            }

            // 停靠：只有停靠的一侧签得了（规则读班次表），零担新批卸在停靠那一台（建好了就开箱，没建好就挂着待装）。
            yield return LastShiftDrillAdvanceTo(x => x.Phase == LastShiftPhase.Dwell, 400, true, errors, notes, "to_dwell");
            if (t.Phase == LastShiftPhase.Dwell)
            {
                yield return WaitSeconds(0.8f);
                int trip = t.Trip;
                char side = t.LineUsed;
                string sideDrop = side == 'E' ? "dropE" : "dropW", farDrop = side == 'E' ? "dropW" : "dropE";
                LastShiftLoot.DevBoxInfo near = LastShiftDrillBox(loot, sideDrop), far = LastShiftDrillBox(loot, farDrop);
                if (t.IsExpress) errors.Add("dwell_on_express");
                if (near.Id != null)
                {
                    if (near.BatchTrip != trip) errors.Add("dwell:drop_batch_missing_" + sideDrop + "_trip_" + near.BatchTrip);
                    else if (near.Built && !(near.Unlocked && near.LootActive)) errors.Add("dwell:drop_not_unlocked_" + sideDrop);
                    else if (!near.Built && !near.HasPending) errors.Add("dwell:drop_batch_lost_" + sideDrop);
                }
                if (far.Id != null && far.BatchTrip == trip) errors.Add("dwell:drop_batch_on_far_side_" + farDrop);
                foreach (LastShiftLoot.DevBoxInfo box in loot.DevBoxes())
                    if (box.Kind == "sealed")
                    {
                        if (box.Unlocked) errors.Add("dwell:sealed_unlocked_by_itself_" + box.Id);
                        if (!box.DeviceActive) errors.Add("dwell:sealed_device_inactive_" + box.Id);
                    }
                bool claimable = !loot.CargoClaimed && t.Phase == LastShiftPhase.Dwell;
                notes.Add("dwell:trip=" + trip + ",side=" + side + ",claimable_side=" + (claimable ? (side == 'E' ? "cargoE" : "cargoW") : "none")
                    + ",premium=" + t.Premium + ",drop_built=" + near.Built + ",drop_unlocked=" + near.Unlocked + ",drop_pending=" + near.HasPending
                    + ",stood_line=" + line);
            }
            if (loot.DevOpenedCount != _lastShiftDrillOpenedBefore) errors.Add("containers_opened_during_case");
            if (loot.CargoClaimed) errors.Add("cargo_claimed_during_case");
            RecordLastShiftDrill(id, sw, errors, notes);
        }

        private static LastShiftLoot.DevBoxInfo LastShiftDrillBox(LastShiftLoot loot, string boxId)
        {
            foreach (LastShiftLoot.DevBoxInfo box in loot.DevBoxes()) if (box.Id == boxId) return box;
            return default(LastShiftLoot.DevBoxInfo);
        }

        // ====================================================================
        // 6. 押运队
        // ====================================================================

        private IEnumerator RunLastShiftDrillEnemies()
        {
            const string id = "LSY_DRILL_ENEMIES";
            Stopwatch sw = Stopwatch.StartNew();
            LastShiftSession session;
            string gone;
            if (!LastShiftDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            LastShiftEnemies enemies = session.DevEnemies;
            if (enemies == null) { Record(id, "FAIL", 0L, string.Empty, "刷怪 owner 不可用"); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            // 绕场一圈：刷怪要玩家在刷点 25–75 m 之间，开局一批、之后每 16 秒补一人。绕场刷出来的押运队留在场上（之后照样能打）。
            var pois = new List<Transform>(session.DevWorld.Pois);
            pois.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (Transform poi in pois)
            {
                if (ShouldAbort()) break;
                if (poi == null) continue;
                yield return LastShiftDrillStand(session, poi.position, errors, poi.name);
                float until = Time.realtimeSinceStartup + 2.5f;
                while (Time.realtimeSinceStartup < until && !ShouldAbort()) { SampleLastShiftLive(session); yield return null; }
            }
            float settle = Time.realtimeSinceStartup + 8f;
            while (enemies.DevInFlight > 0 && Time.realtimeSinceStartup < settle && !ShouldAbort()) { SampleLastShiftLive(session); yield return null; }
            SampleLastShiftLive(session);
            int wallMask = GameplayDataSettings.Layers.wallLayerMask.value;
            SkyIslandWeaponBand band = SkyIslandEnemyArmoryRules.For(SkyIslandEnemyTier.Scav);
            int alive = 0, failed = 0, suspended = 0, armed = 0, fromTrain = 0, duty = 0;
            bool chiefAlive = false, chiefBound = false;
            foreach (LastShiftEnemies.DevEnemyInfo info in enemies.DevEnemies())
            {
                if (info.Failed) { failed++; continue; }
                if (info.FromTrain) fromTrain++;
                if (info.Duty != null) duty++;
                if (!info.Alive || info.Character == null) continue;
                alive++;
                if (info.Suspended) suspended++;
                CharacterMainControl character = info.Character;
                if ((character.Team != Teams.scav && character.Team != Teams.wolf) || !Team.IsEnemy(Teams.player, character.Team))
                    errors.Add("team_" + info.Id + "_" + character.Team);
                if (!character.transform.IsChildOf(session.DevWorld.Root.transform)) errors.Add("not_under_world_root_" + info.Id);
                if (!info.Suspended)
                {
                    Vector3 position = character.transform.position;
                    if (Physics.CheckCapsule(position + Vector3.up * 0.45f, position + Vector3.up * 1.25f, 0.25f, wallMask, QueryTriggerInteraction.Ignore))
                        errors.Add("inside_wall_" + info.Id);
                }
                if (info.Boss)
                {
                    chiefAlive = true;
                    chiefBound = character.GetComponent<LastShiftEscortChief>() != null;
                    if (!chiefBound) errors.Add("chief_behaviour_missing");
                }
                else if (info.Quality >= 3)
                {
                    ItemStatsSystem.Items.Slot weapon = SkyIslandEnemyArmory.WeaponSlot(character.CharacterItem, true);
                    int quality = weapon == null || weapon.Content == null ? -1 : weapon.Content.Quality;
                    if (quality < band.Min) errors.Add("escort_weapon_quality_" + info.Id + "_" + quality);
                    else armed++;
                }
            }
            string why;
            if (!LastShiftDrillPopulationOk(enemies.Population, enemies.SpawnedCount, enemies.DevLive, _lastShiftDrillLivePeak, enemies.DevMobileLive, out why))
                errors.Add(why);
            bool chiefPreset = SkyIslandEnemySources.Named(LastShiftEscortChief.SourceName) != null;
            notes.Add("population=" + enemies.Population + ",spawned=" + enemies.SpawnedCount + ",live=" + enemies.DevLive + ",live_peak=" + _lastShiftDrillLivePeak
                + ",mobile=" + enemies.DevMobileLive + ",in_flight=" + enemies.DevInFlight + ",queued=" + enemies.DevQueued + ",alive=" + alive
                + ",suspended=" + suspended + ",failed=" + failed + ",duty=" + duty + ",from_train=" + fromTrain + ",escorts_armed_q3=" + armed);
            notes.Add("chief_preset=" + (chiefPreset ? LastShiftEscortChief.SourceName : "fallback_scav") + ",chief_rides=" + enemies.DevChiefRides
                + ",chief_alive=" + chiefAlive + ",chief_bound=" + chiefBound + ",spots=" + enemies.DevSpots + ",routes=" + enemies.DevRoutes
                + ",steam_block=" + enemies.DevSteamBlock + ",boss_defeated=" + enemies.BossDefeated);
            if (!chiefPreset) notes.Add("官方「路障」预设没找到：押运长退回拾荒者底模（WARN 级，看日志 [LastShift] 找不到官方「路障」预设）");
            if (errors.Count == 0 && (enemies.SpawnedCount == 0 || alive == 0))
            {
                Record(id, "WARN", sw.ElapsedMilliseconds, string.Join(" | ", notes.ToArray()), "绕场一圈没有观测到活着的押运员，阵营与卡墙没有样本；须人工补测");
                yield break;
            }
            if (errors.Count == 0 && !chiefPreset)
            {
                Record(id, "WARN", sw.ElapsedMilliseconds, string.Join(" | ", notes.ToArray()), "押运长底模退回拾荒者");
                yield break;
            }
            RecordLastShiftDrill(id, sw, errors, notes);
        }

        // ====================================================================
        // 7. 线索与剧情不落盘（只读：不读线索）
        // ====================================================================

        private IEnumerator RunLastShiftDrillStory()
        {
            const string id = "LSY_DRILL_STORY";
            Stopwatch sw = Stopwatch.StartNew();
            LastShiftSession session;
            string gone;
            if (!LastShiftDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            LastShiftRun run = session.DevRun;
            if (run == null) { Record(id, "FAIL", 0L, string.Empty, "本趟剧情进度不可用"); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            LastShiftWorld world = session.DevWorld;
            for (int i = 0; i < LastShiftStory.NoteCount; i++)
            {
                string marker = LastShiftStory.NoteMarkers[i];
                if (!world.Interacts.ContainsKey(marker)) errors.Add("note_marker_missing_" + marker);
                Transform host = world.Root.transform.Find("LastShiftNote_" + (i + 1));
                if (host == null) errors.Add("note_interact_missing_" + (i + 1));
                else if (host.GetComponentInChildren<LastShiftDeviceInteractable>(true) == null) errors.Add("note_interactable_component_missing_" + (i + 1));
            }
            int found = LastShiftStory.Count(run.Notes);
            string counter = run.Counter();
            if (!LastShiftDrillCounterOk(counter, LastShiftStory.Journal, found, LastShiftStory.NoteCount)) errors.Add("counter_text_" + (counter ?? "null"));
            int mark = session.DevStatusMark;
            session.DevTrains.DevReadManifest();
            yield return null;
            if (LastShiftDrillCount(session.DevStatusSince(mark), new[] { "本趟班单", "This run's manifest" }) != 1) errors.Add("manifest_caption_missing");
            if (LastShiftStory.Runs != _lastShiftDrillRunsBefore || LastShiftStory.Notes != _lastShiftDrillNotesBefore
                || LastShiftStory.Records != _lastShiftDrillRecordsBefore || LastShiftStory.Journal != _lastShiftDrillJournalBefore)
                errors.Add("story_save_changed_runs_" + LastShiftStory.Runs + "_notes_" + LastShiftStory.Notes + "_records_" + LastShiftStory.Records);
            notes.Add("saved:runs=" + LastShiftStory.Runs + ",notes=" + LastShiftStory.Notes + ",records=" + LastShiftStory.Records + ",journal=" + LastShiftStory.Journal
                + ",writable=" + LastShiftStory.Writable);
            notes.Add("this_run:notes=" + run.Notes + ",records=" + run.Records + ",counter=" + (counter ?? "-")
                + "（本趟记录只在内存里，真撤离时才写进存档；快进时开过去的直通快车会记一笔 express）");
            RecordLastShiftDrill(id, sw, errors, notes);
        }

        // ====================================================================
        // 8. 撤离读条（不读满）
        // ====================================================================

        private IEnumerator RunLastShiftDrillExtractionHold()
        {
            const string id = "LSY_DRILL_EXTRACTION_HOLD";
            Stopwatch sw = Stopwatch.StartNew();
            LastShiftSession session;
            string gone;
            if (!LastShiftDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            LastShiftExtraction extraction = session.DevExtraction;
            Transform north;
            if (!session.DevWorld.Exits.TryGetValue("north", out north) || !extraction.IsOpen("north"))
            {
                Record(id, "FAIL", 0L, string.Empty, "北公路撤离点不可用或没开");
                yield break;
            }
            var errors = new List<string>();
            var notes = new List<string>();
            if (extraction.DevCurrentExit != null) errors.Add("already_in_ring_" + extraction.DevCurrentExit);
            string why;
            if (!session.DevTeleport(north.position, out why)) { Record(id, "FAIL", 0L, string.Empty, "teleport_" + why); yield break; }
            float held = -1f;
            string current = null;
            try
            {
                // 北公路读条 5 秒；只站 1.5 秒，读到一截就走。
                float until = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < until && !ShouldAbort())
                {
                    yield return null;
                    held = extraction.DevHeld;
                    current = extraction.DevCurrentExit;
                    if (held > 3f) break;
                }
            }
            finally
            {
                // 无论如何离开撤离环：读满会真的撤离并写剧情存档。
                LastShiftDrillStepAway(session, north.position);
            }
            notes.Add("held_s=" + held.ToString("F2") + ",exit=" + current + ",hud_fallback=" + (extraction.HudFallback ?? "-"));
            if (!LastShiftDrillHoldOk(current, held, out why)) errors.Add(why);
            yield return WaitSeconds(0.6f);
            for (int attempt = 0; attempt < 3 && extraction.DevCurrentExit != null && !ShouldAbort(); attempt++)
            {
                notes.Add("step_away_retry_" + attempt);
                LastShiftDrillStepAway(session, north.position);
                yield return WaitSeconds(0.6f);
            }
            if (extraction.DevCurrentExit != null || extraction.DevHeld >= 0f) errors.Add("countdown_not_reset_after_leaving");
            RecordLastShiftDrill(id, sw, errors, notes);
        }

        /// <summary>
        /// 同步离开撤离环（finally 里用，不能 yield）：往出生点方向挪 8 m 吸到安全可走面；不行就退到出生点附近的安全可走面，
        /// 再不行直接放回出生点。返回搬没搬成。
        /// </summary>
        private static bool LastShiftDrillStepAway(LastShiftSession session, Vector3 anchor)
        {
            try
            {
                Vector3 spawn = session.DevWorld.PlayerSpawn.position;
                Vector3 direction = spawn - anchor;
                direction.y = 0f;
                if (direction.sqrMagnitude < 1f) direction = Vector3.back;
                Vector3 point;
                string why;
                if (LastShiftDrillSafePoint(session, anchor + direction.normalized * 8f, out point) && session.DevTeleport(point, out why)) return true;
                if (LastShiftDrillSafePoint(session, spawn, out point) && session.DevTeleport(point, out why)) return true;
                point = spawn;
                return session.DevTeleport(point, out why);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LastShiftDrill] 离开撤离环失败: " + e.Message);
                return false;
            }
        }
    }
}
#endif
