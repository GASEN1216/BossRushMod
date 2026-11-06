#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationTidegateDrillCases.cs - 潮闸镇 Dev 演练的各条用例（**非只读**）
// ============================================================================
// 入口、开跑门、会话外壳、站位与找深水的小工具、还原和纯判据在 F3GameplayValidationTidegateDrill.cs，
// 那里头注释写的隔离纪律（不写存档、不收录、不开箱、不发物品、不写官方时钟、站位离出口 5 m 外、最后还原）这里同样适用。
// 2026-11-06 一次写成后按 LargeFileBudgetGuard（新文件不超过 1200 行）原样拆出，行为逐字不变。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        // ====================================================================
        // 1. 装配核对（只读）
        // ====================================================================

        private IEnumerator RunTidegateDrillAssembly()
        {
            const string id = "TIDE_DRILL_ASSEMBLY";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            TidegateWorld world = session.DevWorld;
            if (session.DevWater == null) errors.Add("water_missing");
            if (session.DevExtraction == null) errors.Add("extraction_missing");
            if (session.DevLoot == null) errors.Add("loot_missing");
            if (session.DevEnemies == null) errors.Add("enemies_missing");
            if (session.DevDevices == null) errors.Add("devices_missing");
            if (session.DevSounds == null) errors.Add("sounds_missing");
            if (session.DevMap == null) errors.Add("map_missing");
            foreach (string exit in new[] { "road", "ferry", "drain", "lh" })
                if (!world.Exits.ContainsKey(exit)) errors.Add("exit_marker_missing_" + exit);
            if (world.Valves.Count != 3) errors.Add("valves_" + world.Valves.Count);
            if (world.Lighthouse == null) errors.Add("lighthouse_marker_missing");
            if (world.Root.transform.Find("Visual/VIS_Fx/LighthouseBeam") == null) errors.Add("lighthouse_beam_node_missing");
            foreach (string marker in TidegateDrillStoryMarkers)
                if (session.DevFindMarker(marker) == null) errors.Add("story_marker_missing_" + marker);
            foreach (string poi in TidegateDrillPoiNames)
                if (session.DevFindMarker("POI_" + poi) == null) errors.Add("poi_missing_" + poi);
            int walls = 0;
            Transform holder = world.Root.transform.Find("Collision/COL_Wall_Boundary") ?? world.Root.transform.Find("COL_Wall_Boundary");
            int wallLayerMask = GameplayDataSettings.Layers.wallLayerMask.value;
            if (holder != null)
                foreach (BoxCollider wall in holder.GetComponentsInChildren<BoxCollider>(true))
                    if (((1 << wall.gameObject.layer) & wallLayerMask) != 0) walls++;
            if (walls != 4) errors.Add("boundary_walls_" + walls);
            if (session.DevMap != null)
            {
                if (!session.DevMap.Bound) errors.Add("minimap_settings_missing");
                else if (!session.DevMap.DevOfficialInstance) errors.Add("minimap_not_official_instance");
                if (session.DevMap.DevExitPointCount != world.Exits.Count) errors.Add("map_exit_points_" + session.DevMap.DevExitPointCount);
            }
            if (session.DevWater != null)
            {
                string waterReason;
                if (!TidegateDrillWaterTopsOk(session.DevWater.DevSeaTops, session.Tide.Level, 0.03f, out waterReason))
                    errors.Add("sea_" + waterReason);
                if (!TidegateDrillWaterTopsOk(session.DevWater.DevLagoonTops, session.Tide.LagoonLevel, 0.03f, out waterReason))
                    errors.Add("lagoon_" + waterReason);
            }
            int walkable = session.DevNavigation == null ? 0 : session.DevNavigation.CountWalkableNodes();
            if (walkable == 0) errors.Add("navigation_no_walkable_nodes");
            if (session.DevUnreachableSpawns > 0) errors.Add("nav_unreachable_spawns_" + session.DevUnreachableSpawns);
            if (string.IsNullOrEmpty(session.DevHudObjective)) errors.Add("hud_objective_empty");
            if (session.DevExtraction != null)
                foreach (string exit in session.DevExtraction.DevExitIds())
                    if (!session.DevExtraction.DevHasCountdown(exit)) notes.Add("official_countdown_missing_" + exit + "(hud_fallback)");
            if (session.DevSounds != null)
                foreach (string file in TidegateSounds.DevAllFiles())
                    if (!File.Exists(Path.Combine(session.DevSounds.DevDirectory, file))) errors.Add("sfx_file_missing_" + file);
            notes.Add("loot_markers=" + world.LootMarkers.Count + ",spawn_markers=" + world.SpawnMarkers.Count + ",pois=" + world.Pois.Count
                + ",walkable_nodes=" + walkable + ",boundary_walls=" + walls + ",phase=" + session.Tide.Phase + ",level=" + session.Tide.Level.ToString("F2"));
            RecordTidegateDrill(id, sw, errors, notes);
            yield break;
        }

        // ====================================================================
        // 2. 潮汐四相与换相前提示
        // ====================================================================

        private IEnumerator RunTidegateDrillTidePhases()
        {
            const string id = "TIDE_DRILL_TIDE_PHASES";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            TidegateSounds sounds = session.DevSounds;
            int sirenBefore = sounds == null ? 0 : sounds.DevPosted(TidegateCue.Siren);
            // 先拨到高潮，站到沉船边高潮也不淹的岸上（导航此刻已封掉被淹的三角形），让附近 60 m 的箱子建起来（每 0.5 秒一只）。
            yield return TidegateDrillJumpToHigh(session, 4.0);
            Transform wreck = session.DevFindMarker("POI_沉船");
            if (wreck == null) errors.Add("poi_wreck_missing");
            else yield return TidegateDrillStand(session, wreck.position, errors, "beach");
            yield return WaitSeconds(4f);
            // 按顺序走一轮，每次只跨一个相：换相事件与换相字幕都应各出一条。
            TidegateTidePhase[] order = { TidegateTidePhase.Ebb, TidegateTidePhase.Low, TidegateTidePhase.Flood, TidegateTidePhase.High };
            foreach (TidegateTidePhase phase in order)
            {
                if (ShouldAbort()) break;
                bool plateau = phase == TidegateTidePhase.Low || phase == TidegateTidePhase.High;
                if (phase == TidegateTidePhase.Flood) yield return TidegateDrillWaitForRetreat();
                int eventMark = _tidegateDrillPhaseEvents.Count, statusMark = session.DevStatusMark;
                TidegateTide.DevJumpInto(phase, plateau ? 6.0 : 4.0);
                // 导航一秒最多重算一次、撤离环与箱子半秒一查、HUD 半秒一刷。
                yield return WaitSeconds(1.5f);
                if (!TidegateDrillLive(out session, out gone)) { errors.Add(phase + ":" + gone); break; }
                TidegateDrillCheckTide(session, phase, plateau, errors, notes);
                List<string> events = _tidegateDrillPhaseEvents.GetRange(eventMark, _tidegateDrillPhaseEvents.Count - eventMark);
                if (events.Count != 1 || !events[0].EndsWith(">" + phase, StringComparison.Ordinal))
                    errors.Add(phase + ":phase_events_" + string.Join("/", events.ToArray()));
                int captions = TidegateDrillCount(session.DevStatusSince(statusMark), TidegateDrillPhaseCaption(phase));
                if (captions != 1) errors.Add(phase + ":phase_caption_count_" + captions);
            }
            if (sounds != null && !sounds.DevUnavailable && sounds.DevPosted(TidegateCue.Siren) <= sirenBefore) errors.Add("flood_siren_not_posted");
            RecordTidegateDrill(id, sw, errors, notes);
        }

        private IEnumerator RunTidegateDrillTideNotices()
        {
            const string id = "TIDE_DRILL_TIDE_NOTICES";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            TidegateSounds sounds = session.DevSounds;
            string[] floodSoon = { "潮汐警报", "Tide alarm" }, ferryLeaving = { "渡口的船再过", "The ferry leaves in" };
            // 低潮还剩 17 秒：15 秒时拉涨潮警报（字幕 + 警报声），同一轮只一次。
            int mark = session.DevStatusMark, siren = sounds == null ? 0 : sounds.DevPosted(TidegateCue.Siren);
            TidegateTide.DevJumpBeforeEnd(TidegateTidePhase.Low, 17.0);
            float until = Time.realtimeSinceStartup + 6f;
            while (TidegateDrillCount(session.DevStatusSince(mark), floodSoon) == 0 && Time.realtimeSinceStartup < until && !ShouldAbort())
                yield return null;
            yield return WaitSeconds(2.5f);
            int floodCount = TidegateDrillCount(session.DevStatusSince(mark), floodSoon);
            if (floodCount != 1) errors.Add("flood_alarm_count_" + floodCount);
            if (sounds != null && !sounds.DevUnavailable && sounds.DevPosted(TidegateCue.Siren) <= siren) errors.Add("flood_alarm_siren_not_posted");
            // 高潮还剩 15 秒：12 秒时提醒渡口的船要走，同一轮只一次。
            yield return TidegateDrillWaitForRetreat();
            mark = session.DevStatusMark;
            TidegateTide.DevJumpBeforeEnd(TidegateTidePhase.High, 15.0);
            until = Time.realtimeSinceStartup + 6f;
            while (TidegateDrillCount(session.DevStatusSince(mark), ferryLeaving) == 0 && Time.realtimeSinceStartup < until && !ShouldAbort())
                yield return null;
            yield return WaitSeconds(2.5f);
            int ferryCount = TidegateDrillCount(session.DevStatusSince(mark), ferryLeaving);
            if (ferryCount != 1) errors.Add("ferry_leaving_count_" + ferryCount);
            notes.Add("flood_alarm=" + floodCount + ",ferry_leaving=" + ferryCount);
            RecordTidegateDrill(id, sw, errors, notes);
        }

        // ====================================================================
        // 3. 三阀一闸与剧情箱柜
        // ====================================================================

        private IEnumerator RunTidegateDrillValves()
        {
            const string id = "TIDE_DRILL_VALVES_SLUICE";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            TidegateDevices devices = session.DevDevices;
            TidegateWorld world = session.DevWorld;
            if (devices == null || world.Valves.Count != 3)
            {
                Record(id, "FAIL", 0L, "devices=" + (devices != null) + ",valves=" + world.Valves.Count, "机关 owner 不可用或阀门不是三处");
                yield break;
            }
            var errors = new List<string>();
            var notes = new List<string>();
            CharacterMainControl player = CharacterMainControl.Main;
            if (devices.DevTouched)
            {
                // 这一趟已经有人动过机关：先复位，演练从「三处都关着」开始。
                devices.DevReset();
                if (session.DevEnemies != null) session.DevEnemies.DevEndReveal();
                notes.Add("reset_before=true");
                yield return WaitSeconds(0.6f);
            }
            _tidegateDrillTouched = true;
            bool wheelCarried = TidegateInventory.Carries(player, BossRushItemIds.TidegateHandwheel);
            // 带着潮汐怀表（还没交）时目标行恒为「带着怀表回去」，目标行的几条就不判。
            bool watchObjective = TidegateInventory.Carries(player, BossRushItemIds.TidegateTideWatch) && !TidegateStory.WatchDelivered;
            notes.Add("handwheel_carried=" + wheelCarried + ",watch_objective=" + watchObjective);
            TidegateSounds sounds = session.DevSounds;
            bool soundsOk = sounds != null && !sounds.DevUnavailable;

            // 阀门 0、1：直接开；字幕 1/3、2/3；阀门声；目标行里不再有这一处。
            for (int i = 0; i < 2 && !ShouldAbort(); i++)
            {
                yield return TidegateDrillStand(session, world.Valves[i].position, errors, "valve" + i);
                int mark = session.DevStatusMark, cue = soundsOk ? sounds.DevPosted(TidegateCue.Valve) : 0;
                devices.DevTurnValve(i, false);
                yield return WaitSeconds(0.7f);
                if (!devices.DevValveOpen(i) || devices.OpenValves != i + 1) errors.Add("valve" + i + "_not_open_" + devices.OpenValves);
                if (TidegateDrillCount(session.DevStatusSince(mark), new[] { (i + 1) + "/3" }) != 1) errors.Add("valve" + i + "_caption_missing");
                if (soundsOk && sounds.DevPosted(TidegateCue.Valve) <= cue) errors.Add("valve" + i + "_sound_missing");
                string objective = session.DevObjective() ?? string.Empty;
                if (!watchObjective && objective.IndexOf(TidegateDevices.DevValveName(i), StringComparison.Ordinal) >= 0)
                    errors.Add("valve" + i + "_still_in_objective");
                if (!string.Equals(session.DevHudObjective, objective, StringComparison.Ordinal)) errors.Add("valve" + i + "_hud_objective_stale");
            }

            // 检查场景里真实的官方需求行（图标、名称、数量和携带状态），不只检查标签文本。
            int gauge = TidegateDevices.DevTideGaugeValve;
            yield return TidegateDrillStand(session, world.Valves[gauge].position, errors, "valve" + gauge);
            TidegateDeviceInteractable gaugeInteraction = devices.DevValveInteraction(gauge);
            if (gaugeInteraction == null || !gaugeInteraction.requireItem || gaugeInteraction.requireItemId != BossRushItemIds.TidegateHandwheel
                || gaugeInteraction.whenToUseRequireItem == InteractableBase.WhenToUseRequireItemTypes.None
                || gaugeInteraction.GetRequireditemIcon() == null || string.IsNullOrEmpty(gaugeInteraction.GetRequiredItemName())
                || gaugeInteraction.TryGetRequiredItem(player).Item1 != wheelCarried)
                errors.Add("gauge_official_requirement_missing_or_wrong");
            else notes.Add("gauge_requirement=" + gaugeInteraction.requireItemId + ":" + gaugeInteraction.GetRequiredItemName());
            if (!wheelCarried)
            {
                string objective = session.DevObjective() ?? string.Empty;
                if (!watchObjective && !TidegateDrillContainsAny(objective, "缺手轮", "no handwheel")) errors.Add("gauge_objective_no_dredger_hint");
                int mark = session.DevStatusMark;
                devices.DevTurnValve(gauge, false);
                yield return WaitSeconds(0.4f);
                if (devices.DevValveOpen(gauge)) errors.Add("gauge_opened_without_handwheel");
                if (TidegateDrillCount(session.DevStatusSince(mark), new[] { "手轮被人拆走", "has no handwheel" }) != 1) errors.Add("gauge_refusal_caption_missing");
            }
            else notes.Add("gauge_refusal_not_drilled(handwheel_in_pack)");

            // 旁路装上手轮（不发、不耗物品）：三处全开 → 大闸提起、闷响、字幕、4 秒后海堤增援。
            int gateCue = soundsOk ? sounds.DevPosted(TidegateCue.Gate) : 0, gateMark = session.DevStatusMark;
            devices.DevTurnValve(gauge, true);
            yield return WaitSeconds(0.7f);
            if (!devices.DevValveOpen(gauge) || !session.Tide.SluiceOpen) errors.Add("sluice_not_open");
            if (soundsOk && sounds.DevPosted(TidegateCue.Gate) <= gateCue) errors.Add("gate_sound_missing");
            if (TidegateDrillCount(session.DevStatusSince(gateMark), new[] { "大闸提起来了", "tide gate is up" }) != 1) errors.Add("sluice_caption_missing");
            if (wheelCarried && !TidegateInventory.Carries(player, BossRushItemIds.TidegateHandwheel)) errors.Add("drill_consumed_real_handwheel");

            // 泻湖放水：每秒 0.085 m，放到和外海齐平后跟着外海。
            float lagoonStart = session.Tide.LagoonLevel, seaStart = session.Tide.Level, drainFrom = Time.time;
            string draining = session.DevObjective() ?? string.Empty;
            if (!watchObjective && lagoonStart > -0.45f && lagoonStart > seaStart && !TidegateDrillContainsAny(draining, "泻湖正在放水", "lagoon is draining"))
                errors.Add("objective_not_draining");
            yield return WaitSeconds(2f);
            double expectedLagoon;
            if (!TidegateDrillLagoonDropOk(lagoonStart, session.Tide.LagoonLevel, session.Tide.Level, Time.time - drainFrom, out expectedLagoon))
                errors.Add("lagoon_drain_" + session.Tide.LagoonLevel.ToString("F2") + "_want_" + expectedLagoon.ToString("F2"));

            // 留在最后一个阀门等四秒，看近处海堤的三个槽先排队，不能贴脸落地。随后移开让他们进场。
            yield return WaitSeconds(2.5f);
            int queued = 0;
            if (session.DevEnemies != null)
                foreach (TidegateEnemies.DevEnemyInfo info in session.DevEnemies.DevEnemies())
                {
                    if (!info.Extra) continue;
                    if (!info.Started && !info.Failed) queued++;
                    if (info.Started && info.SpawnDistance >= 0f && info.SpawnDistance < 24.99f) errors.Add("seawall_arrived_too_close_" + info.Id);
                }
            notes.Add("seawall_queued_at_gauge=" + queued);
            yield return TidegateDrillStand(session, world.PlayerSpawn.position, errors, "seawall_safe_distance");
            // 海堤增援：三名、wolf 阵营、与玩家敌对。
            float until = Time.realtimeSinceStartup + 10f;
            int extrasAlive = 0;
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                extrasAlive = 0;
                if (session.DevEnemies != null)
                    foreach (TidegateEnemies.DevEnemyInfo info in session.DevEnemies.DevEnemies()) if (info.Extra && info.Alive) extrasAlive++;
                if (extrasAlive >= 3 && session.DevEnemies.DevInFlight == 0) break;
                yield return null;
            }
            if (extrasAlive != 3) errors.Add("seawall_reinforcements_" + extrasAlive);
            if (session.DevEnemies != null)
                foreach (TidegateEnemies.DevEnemyInfo info in session.DevEnemies.DevEnemies())
                    if (info.Extra && info.Alive && (info.Character.Team != Teams.wolf || !Team.IsEnemy(Teams.player, info.Character.Team)))
                        errors.Add("reinforcement_not_hostile_" + info.Id);

            // 放干到金库露出：要低潮。
            if (session.Tide.Phase != TidegateTidePhase.Low) TidegateTide.DevJumpInto(TidegateTidePhase.Low, 3.0);
            until = Time.realtimeSinceStartup + 35f;
            while (session.Tide.LagoonLevel > -0.46f && Time.realtimeSinceStartup < until && !ShouldAbort()) yield return null;
            yield return WaitSeconds(0.7f);
            if (session.Tide.LagoonLevel > -0.45f) errors.Add("vault_not_dry_lagoon_" + session.Tide.LagoonLevel.ToString("F2"));
            string dry = session.DevObjective() ?? string.Empty;
            if (!watchObjective && !TidegateDrillContainsAny(dry, "金库露出水面", "vault is above water")) errors.Add("objective_not_vault_dry");

            // 增援等泻湖放干到 -0.9 m 以下才奔金库（3 秒推一次巡逻中心）。
            until = Time.realtimeSinceStartup + 20f;
            while (session.Tide.LagoonLevel > -0.92f && Time.realtimeSinceStartup < until && !ShouldAbort()) yield return null;
            yield return WaitSeconds(3.4f);
            List<Vector3> vaultPoints;
            world.Waypoints.TryGetValue("vault", out vaultPoints);
            if (vaultPoints == null || vaultPoints.Count == 0) errors.Add("vault_waypoints_missing");
            else if (session.DevEnemies != null)
                foreach (TidegateEnemies.DevEnemyInfo info in session.DevEnemies.DevEnemies())
                {
                    if (!info.Extra || !info.Alive) continue;
                    bool atVault = false;
                    foreach (Vector3 point in vaultPoints) if (TidegateDrillFlat(point, info.Patrol) < 0.6f) atVault = true;
                    if (!atVault) errors.Add("reinforcement_not_heading_to_vault_" + info.Id);
                }
            notes.Add("lagoon=" + session.Tide.LagoonLevel.ToString("F2") + ",reinforcements=" + extrasAlive + ",objective=" + dry);
            RecordTidegateDrill(id, sw, errors, notes);
        }

        private IEnumerator RunTidegateDrillStoryContainers()
        {
            const string id = "TIDE_DRILL_STORY_CONTAINERS";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            TidegateLoot loot = session.DevLoot;
            if (loot == null) { Record(id, "FAIL", 0L, string.Empty, "搜刮 owner 不可用"); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            CharacterMainControl player = CharacterMainControl.Main;
            // 表本身：四页日志、怀表、手轮、沉船保险柜的出处。
            if (TidegateLoot.PageIndexOf("LOOT_safe_68") != 0 || TidegateLoot.PageIndexOf("LOOT_house_69") != 1
                || TidegateLoot.PageIndexOf("LOOT_house_70") != 2 || TidegateLoot.PageIndexOf("LOOT_vault_71") != 3) errors.Add("page_table_mismatch");
            if (!TidegateLoot.HasFixed("LOOT_vault_71", "tidewatch") || !TidegateLoot.HasFixed("LOOT_locker_72", "wheel")
                || !TidegateLoot.HasFixed("LOOT_safe_77", "goldbar")) errors.Add("fixed_table_mismatch");
            if (session.Tide.Phase != TidegateTidePhase.Low)
            {
                TidegateTide.DevJumpInto(TidegateTidePhase.Low, 3.0);
                yield return WaitSeconds(1.3f);
            }
            bool vaultDry = session.Tide.SluiceOpen && session.Tide.LagoonLevel <= -0.45f;
            bool carryingWatch = TidegateInventory.Carries(player, BossRushItemIds.TidegateTideWatch);
            foreach (string markerId in TidegateDrillStoryMarkers)
            {
                if (ShouldAbort()) break;
                if (markerId == "LOOT_vault_71" && !vaultDry) { errors.Add("vault_unreachable_sluice_or_lagoon"); continue; }
                Transform marker = session.DevFindMarker(markerId);
                if (marker == null) { errors.Add(markerId + "_marker_missing"); continue; }
                yield return TidegateDrillStand(session, marker.position, errors, markerId);
                float until = Time.realtimeSinceStartup + 8f;
                TidegateLoot.DevPointInfo info = loot.DevPoint(markerId, false);
                while (info != null && !info.Built && Time.realtimeSinceStartup < until && !ShouldAbort())
                {
                    yield return null;
                    info = loot.DevPoint(markerId, false);
                }
                yield return WaitSeconds(0.6f);
                info = loot.DevPoint(markerId, true);
                if (info == null || !info.Built) { errors.Add(markerId + "_not_built"); continue; }
                if (info.Failed) { errors.Add(markerId + "_build_failed"); continue; }
                if (info.Flooded || !info.BoxActive) errors.Add(markerId + "_hidden_at_low_tide");
                if (info.Opened) notes.Add(markerId + "_already_opened_this_raid");
                notes.Add(markerId + ":items=" + string.Join("/", info.Items.ConvertAll(x => x.ToString()).ToArray()));
                if (info.Items.Count == 0) errors.Add(markerId + "_empty");
                // 看正式生成的实物，不翻箱；手轮和怀表等剧情物绝不能让普通战利品的判据假绿。
                int floor = TidegateLoot.PrizeFloor(markerId, info.Kind);
                if (floor > 0)
                {
                    notes.Add(markerId + ":best_q=" + info.BestOrdinaryQuality + ",floor=" + floor
                        + ",item=" + info.BestOrdinaryId + ",unit_value=" + info.BestOrdinaryUnitValue);
                    if (info.BestOrdinaryQuality < floor) errors.Add(markerId + "_below_prize_floor");
                }
                if (markerId == "LOOT_locker_72" && !info.Items.Contains(BossRushItemIds.TidegateHandwheel)) errors.Add("locker_no_handwheel");
                if (markerId == "LOOT_vault_71")
                {
                    bool hasWatch = info.Items.Contains(BossRushItemIds.TidegateTideWatch);
                    if (!carryingWatch && !hasWatch) errors.Add("vault_no_tide_watch");
                    if (carryingWatch && hasWatch) errors.Add("vault_gave_second_watch");
                }
                if (markerId == "LOOT_safe_77" && info.Items.Count < 2) errors.Add("wreck_safe_missing_valuable");
            }
            // 演练只看不翻：剧情存档里的日志页不能变。
            TidegateStoryData story = TidegateStory.Current;
            if (story != null && _tidegateDrillPagesBefore >= 0 && story.Pages != _tidegateDrillPagesBefore) errors.Add("story_pages_changed_by_drill");
            RecordTidegateDrill(id, sw, errors, notes);
        }

        // ====================================================================
        // 4. 游泳、深水箱子与淘沙帮
        // ====================================================================

        private IEnumerator RunTidegateDrillSwim()
        {
            const string id = "TIDE_DRILL_SWIM";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            TidegateWater water = session.DevWater;
            if (water == null) { Record(id, "FAIL", 0L, string.Empty, "水体 owner 不可用"); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            CharacterMainControl player = CharacterMainControl.Main;
            yield return TidegateDrillJumpToHigh(session, 4.0);
            Transform ferry;
            session.DevWorld.Exits.TryGetValue("ferry", out ferry);
            Vector3 center = ferry != null ? ferry.position : session.DevWorld.PlayerSpawn.position;
            Vector3 deep;
            if (!TidegateDrillFindDeepWater(session, center, 1.6f, out deep))
            {
                Record(id, "FAIL", sw.ElapsedMilliseconds, "center=" + center, "栈桥渡口附近找不到 1.6 m 以上的外海深水");
                yield break;
            }
            string regionBefore = session.DevRegionPoi;
            try
            {
                water.DevSwimOverride = 1;
                water.DevCheckNow();
                string why;
                if (!session.DevTeleport(deep, true, out why)) errors.Add("teleport_water_" + why);
                else
                {
                    float until = Time.realtimeSinceStartup + 4f;
                    while (!player.Swimming && Time.realtimeSinceStartup < until && !ShouldAbort()) yield return null;
                    yield return WaitSeconds(0.8f);
                    notes.Add("light:swimming=" + player.Swimming + ",can_swim=" + water.CanSwimNow + ",stealth=" + water.Stealthy
                        + ",y=" + player.transform.position.y.ToString("F2") + ",level=" + session.Tide.Level.ToString("F2"));
                    if (!player.Swimming) errors.Add("light_not_swimming");
                    if (!water.CanSwimNow) errors.Add("light_no_can_swim");
                    if (!water.Stealthy) errors.Add("light_no_swim_stealth");
                    if (player.HeadInWater) errors.Add("light_head_under_water");
                    if (!TidegateDrillContainsAny(session.DevFieldStatus(), "游泳中", "swimming")) errors.Add("light_chip_no_swimming");
                    // 可玩区边上有墙：朝最近的那条边看过去，墙层要挡着（高潮时原海侧墙顶比水面低，CR-2026-11-05-019 补的）。
                    Vector3 here = player.transform.position;
                    Rect area = TidegateWorld.PlayArea;
                    float[] gaps = { here.x - area.xMin, area.xMax - here.x, here.z - area.yMin, area.yMax - here.z };
                    Vector3[] directions = { Vector3.left, Vector3.right, Vector3.back, Vector3.forward };
                    int side = 0;
                    for (int i = 1; i < gaps.Length; i++) if (gaps[i] < gaps[side]) side = i;
                    RaycastHit hit;
                    if (!Physics.Raycast(here + Vector3.up * 0.3f, directions[side], out hit, gaps[side] + 3f,
                        GameplayDataSettings.Layers.wallLayerMask.value, QueryTriggerInteraction.Ignore))
                        errors.Add("no_wall_towards_play_area_edge_" + side);
                    else notes.Add("edge_blocker=" + hit.collider.name + "@" + hit.distance.ToString("F1"));
                    // 游在外海：地名不换成别处（离所有地标 24 m 以外时保持上一个）。
                    float nearest;
                    session.DevWorld.NearestPoi(here, out nearest);
                    if (nearest > 24f && session.DevRegionPoi != regionBefore) errors.Add("region_changed_offshore_" + session.DevRegionPoi);

                    // 超重：撤掉游泳能力，往下沉、头没进水。
                    water.DevSwimOverride = 2;
                    water.DevCheckNow();
                    yield return WaitSeconds(0.8f);
                    if (water.CanSwimNow) errors.Add("heavy_still_can_swim");
                    if (!TidegateDrillContainsAny(session.DevFieldStatus(), "下水会沉", "will sink")) errors.Add("heavy_chip_no_sink_warning");
                    until = Time.realtimeSinceStartup + 6f;
                    while ((player.Swimming || !player.HeadInWater) && Time.realtimeSinceStartup < until && !ShouldAbort()) yield return null;
                    notes.Add("heavy:swimming=" + player.Swimming + ",head_in_water=" + player.HeadInWater + ",y=" + player.transform.position.y.ToString("F2"));
                    if (player.Swimming) errors.Add("heavy_still_swimming");
                    if (!player.HeadInWater) errors.Add("heavy_head_not_under_water");
                }
            }
            finally
            {
                water.DevSwimOverride = 0;
                water.DevCheckNow();
            }
            // 上岸：游泳状态与隐蔽都要撤。
            yield return TidegateDrillStand(session, center, errors, "ashore");
            yield return WaitSeconds(1.2f);
            if (player.Swimming) errors.Add("ashore_still_swimming");
            if (water.Stealthy) errors.Add("ashore_stealth_still_on");
            RecordTidegateDrill(id, sw, errors, notes);
        }

        private IEnumerator RunTidegateDrillFloodHint()
        {
            const string id = "TIDE_DRILL_FLOOD_HINT";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            TidegateLoot loot = session.DevLoot;
            TidegateWater water = session.DevWater;
            if (loot == null || water == null) { Record(id, "FAIL", 0L, string.Empty, "搜刮或水体 owner 不可用"); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            if (session.Tide.Phase != TidegateTidePhase.High)
            {
                yield return TidegateDrillJumpToHigh(session, 4.0);
            }
            Vector3 near;
            TidegateLoot.DevPointInfo target = TidegateDrillFloodedBox(session, loot, out near);
            if (target == null)
            {
                Transform wreck = session.DevFindMarker("POI_沉船");
                if (wreck != null) yield return TidegateDrillStand(session, wreck.position, errors, "beach");
                yield return WaitSeconds(4f);
                target = TidegateDrillFloodedBox(session, loot, out near);
            }
            if (target == null)
            {
                int flooded = 0;
                foreach (TidegateLoot.DevPointInfo point in loot.DevPoints(false)) if (point.Built && point.Flooded) flooded++;
                if (flooded == 0)
                    Record(id, "FAIL", sw.ElapsedMilliseconds, string.Empty, "高潮时滩涂 / 沉船一带没有建好且收起的箱子（箱子没建或深水里没收起）");
                else
                    // 有被淹的箱子，只是 3.2 m 内没有能让人浮着的深水（贴着船体、墙或水太浅）：这是演练找不到站位，不是缺陷，交给人工看。
                    Record(id, "SKIP", sw.ElapsedMilliseconds, "flooded_boxes=" + flooded, "被淹箱子 3.2 m 内都找不到能浮着的深水站位，深水箱子提示留给人工看");
                yield break;
            }
            int mark = session.DevStatusMark;
            string[] hint = { "泡在深水里", "under deep water" };
            try
            {
                water.DevSwimOverride = 1;
                water.DevCheckNow();
                string why;
                if (!session.DevTeleport(near, true, out why)) errors.Add("teleport_water_" + why);
                else
                {
                    yield return WaitSeconds(1.4f);
                    int first = TidegateDrillCount(session.DevStatusSince(mark), hint);
                    yield return WaitSeconds(1.6f);
                    int total = TidegateDrillCount(session.DevStatusSince(mark), hint);
                    TidegateLoot.DevPointInfo after = loot.DevPoint(target.Id, false);
                    notes.Add("box=" + target.Id + ",first=" + first + ",total=" + total + ",hinted=" + (after != null && after.FloodHinted));
                    if (first != 1 || total != 1) errors.Add("flood_hint_count_" + total);
                    if (after == null || after.BoxActive) errors.Add("flooded_box_still_interactable");
                }
            }
            finally
            {
                water.DevSwimOverride = 0;
                water.DevCheckNow();
            }
            yield return TidegateDrillStand(session, target.Position, errors, "ashore");
            RecordTidegateDrill(id, sw, errors, notes);
        }

        /// <summary>
        /// 挑一只建好、被淹、还没提示过的滩涂 / 沉船箱子，并在它 1.5–3.2 m 内找一处能让人浮着的深水（提示半径 3.5 m）。
        /// 逐只试：有的箱子贴着船体或墙，四周没有空的深水。
        /// </summary>
        private static TidegateLoot.DevPointInfo TidegateDrillFloodedBox(TidegateSession session, TidegateLoot loot, out Vector3 near)
        {
            near = Vector3.zero;
            int wallMask = GameplayDataSettings.Layers.wallLayerMask.value;
            TidegateTide tide = session.Tide;
            foreach (TidegateLoot.DevPointInfo point in loot.DevPoints(false))
            {
                if (!point.Built || point.Failed || !point.Flooded || point.FloodHinted) continue;
                if (point.Kind != "wreck" && point.Kind != "buried" && point.Id != "LOOT_safe_77") continue;
                for (float radius = 1.5f; radius <= 3.21f; radius += 0.5f)
                    for (int step = 0; step < 16; step++)
                    {
                        float angle = step * Mathf.PI * 2f / 16f;
                        Vector3 probe = new Vector3(point.Position.x + Mathf.Cos(angle) * radius, tide.Level, point.Position.z + Mathf.Sin(angle) * radius);
                        if (TidegateDrillExitWithin(session.DevWorld, probe, 6f) != null || session.DevWorld.InLagoon(probe)) continue;
                        float ground;
                        if (!session.DevWorld.TryGroundAt(probe, out ground) || tide.Level - ground < 1.25f) continue;
                        Vector3 candidate = new Vector3(probe.x, tide.Level - 0.3f, probe.z);
                        if (Physics.CheckSphere(candidate, 0.5f, wallMask, QueryTriggerInteraction.Ignore)) continue;
                        near = candidate;
                        return point;
                    }
            }
            return null;
        }

        private IEnumerator RunTidegateDrillDiggers()
        {
            const string id = "TIDE_DRILL_DIGGERS";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            TidegateEnemies enemies = session.DevEnemies;
            if (enemies == null) { Record(id, "FAIL", 0L, string.Empty, "刷怪 owner 不可用"); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            TidegateWorld world = session.DevWorld;
            // 低潮、离涨潮还早：淘沙帮该下滩。先让他们刷出来（玩家要在刷怪点 25–75 m 之间）。
            int sweepsAtStart = enemies.DevDiggerSweeps;
            TidegateTide.DevJumpInto(TidegateTidePhase.Low, 6.0);
            yield return WaitSeconds(1.3f);
            if (TidegateDrillGroup(enemies, "dig", true).Count == 0)
            {
                List<TidegateEnemies.DevEnemyInfo> all = TidegateDrillGroup(enemies, "dig", false);
                Vector3 centroid = Vector3.zero;
                foreach (TidegateEnemies.DevEnemyInfo info in all) centroid += info.Home;
                if (all.Count > 0) centroid /= all.Count;
                Transform spot = null;
                float best = float.MaxValue;
                foreach (Transform poi in world.Pois)
                {
                    float distance = TidegateDrillFlat(poi.position, centroid);
                    if (distance < 32f || distance > 62f) continue;
                    if (Math.Abs(distance - 45f) < best) { best = Math.Abs(distance - 45f); spot = poi; }
                }
                if (all.Count == 0) errors.Add("dig_spawn_slots_missing");
                else if (spot == null) errors.Add("no_poi_32_62m_from_dig_spawns");
                else
                {
                    yield return TidegateDrillStand(session, spot.position, errors, "dig_watch");
                    float until = Time.realtimeSinceStartup + 12f;
                    while (TidegateDrillGroup(enemies, "dig", true).Count == 0 && Time.realtimeSinceStartup < until && !ShouldAbort())
                        yield return null;
                    yield return TidegateDrillAwaitSpawns(6f);
                }
            }
            List<TidegateEnemies.DevEnemyInfo> diggers = TidegateDrillGroup(enemies, "dig", true);
            if (diggers.Count == 0)
            {
                int started = 0, failed = 0, vanished = 0;
                foreach (TidegateEnemies.DevEnemyInfo info in TidegateDrillGroup(enemies, "dig", false))
                {
                    if (info.Started) started++;
                    if (info.Failed) failed++;
                    else if (info.Started && !info.Alive && enemies.DevInFlight == 0) vanished++;
                }
                if (vanished > 0 && failed == 0)
                {
                    // 刷出来过、后来不在了（被打死或被潮水卷走）：这一趟里已经验不了跟潮走，不判缺陷。
                    Record(id, "SKIP", sw.ElapsedMilliseconds, "started=" + started + ",gone=" + vanished, "淘沙帮这一趟已经不在了（被打死或卷走），跟潮走留给人工看");
                    yield break;
                }
                errors.Add("dig_gang_not_spawned_started_" + started + "_failed_" + failed);
                RecordTidegateDrill(id, sw, errors, notes);
                yield break;
            }
            if (TidegateTide.RealSecondsUntilNextPhase() < 30.0 || session.Tide.Phase != TidegateTidePhase.Low)
            {
                TidegateTide.DevJumpInto(TidegateTidePhase.Low, 6.0);
                yield return WaitSeconds(0.5f);
            }
            yield return WaitSeconds(3.5f);
            List<Vector3> dig;
            world.Waypoints.TryGetValue("dig", out dig);
            foreach (TidegateEnemies.DevEnemyInfo info in TidegateDrillGroup(enemies, "dig", true))
            {
                bool ok = false;
                if (info.Kind == "boss") ok = info.SandBossBound && info.SandBossFlats && TidegateDrillFlat(info.Patrol, TidegateEnemies.DevBossDigSpot) < 0.6f;
                else if (dig != null) foreach (Vector3 point in dig) if (TidegateDrillFlat(point, info.Patrol) < 0.6f) ok = true;
                if (!ok) errors.Add("low_tide_not_digging_" + info.Id);
            }
            // 高潮：先让他们按「涨潮前 20 秒撤」回挖沙船，再拨到高潮，巡逻中心应在各自的刷怪点。
            yield return TidegateDrillJumpToHigh(session, 4.0);
            yield return WaitSeconds(2.5f);
            foreach (TidegateEnemies.DevEnemyInfo info in TidegateDrillGroup(enemies, "dig", true))
                if (TidegateDrillFlat(info.Patrol, info.Home) > 0.6f) errors.Add("high_tide_not_home_" + info.Id);
            // 固定底模、专属名和尸体回调已挂，高潮已切回船上招式。
            bool bossObserved = false;
            foreach (TidegateEnemies.DevEnemyInfo info in TidegateDrillGroup(enemies, "dig", false))
            {
                if (info.Kind != "boss" || !info.Alive) continue;
                bossObserved = true;
                if (!info.PresetIsBoss || !info.SandBossBound || info.SandBossFlats) errors.Add("sand_boss_identity_or_deck_stance");
            }
            if (!bossObserved) notes.Add("sand_boss_not_observed");
            // 被潮水卷走：挑一名不是沙老大的，挪进深水，泡满 8 秒（3 秒一查）应当消失，不掉东西。
            TidegateEnemies.DevEnemyInfo victim = null;
            foreach (TidegateEnemies.DevEnemyInfo info in TidegateDrillGroup(enemies, "dig", true)) if (info.Kind != "boss") { victim = info; break; }
            Vector3 deep;
            bool sweepVerified = enemies.DevDiggerSweeps > sweepsAtStart;
            if (sweepVerified) notes.Add("sweep_observed_before_probe");
            else if (victim == null) notes.Add("sweep_skipped_no_regular_digger");
            else if (!TidegateDrillFindDeepWater(session, victim.Position, 1.0f, out deep)) notes.Add("sweep_skipped_no_deep_water");
            else if (!enemies.DevMoveEnemy(victim.Id, deep)) notes.Add("sweep_skipped_move_failed");
            else
            {
                float deepFor = 0f, last = Time.time;
                bool swept = false, left = false;
                float until = Time.realtimeSinceStartup + 16f;
                while (Time.realtimeSinceStartup < until && !ShouldAbort())
                {
                    yield return WaitSeconds(0.25f);
                    if (enemies.DevDiggerSweeps > sweepsAtStart && enemies.DevLastSweptDigger == victim.Id) { swept = true; break; }
                    TidegateEnemies.DevEnemyInfo now = null;
                    foreach (TidegateEnemies.DevEnemyInfo info in enemies.DevEnemies()) if (info.Id == victim.Id) now = info;
                    if (now == null || !now.Alive) { notes.Add("sweep_target_died"); break; }
                    float time = Time.time;
                    if (session.Tide.IsDeepAt(now.Position)) deepFor += time - last;
                    else { left = true; deepFor = 0f; }
                    last = time;
                }
                notes.Add("sweep:" + victim.Id + ",swept=" + swept + ",deep_s=" + deepFor.ToString("F1") + ",left_deep=" + left);
                if (!swept && deepFor >= 12f) errors.Add("digger_not_swept_after_" + deepFor.ToString("F1") + "s");
                sweepVerified = enemies.DevDiggerSweeps > sweepsAtStart;
            }
            if (sweepVerified) notes.Add("swept_digger=" + enemies.DevLastSweptDigger + ",count=" + (enemies.DevDiggerSweeps - sweepsAtStart));
            notes.Add("diggers_alive=" + diggers.Count);
            if (errors.Count == 0 && (!sweepVerified || !bossObserved))
            {
                Record(id, "WARN", sw.ElapsedMilliseconds, string.Join(" | ", notes.ToArray()),
                    (!sweepVerified ? "被潮水卷走未观测到；" : string.Empty) + (!bossObserved ? "沙老大未观测到；" : string.Empty) + "未观测项目须人工补测");
                yield break;
            }
            RecordTidegateDrill(id, sw, errors, notes);
        }

        // ====================================================================
        // 5. 灯塔与来船
        // ====================================================================

        private IEnumerator RunTidegateDrillLighthouse()
        {
            const string id = "TIDE_DRILL_LIGHTHOUSE";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            TidegateDevices devices = session.DevDevices;
            TidegateEnemies enemies = session.DevEnemies;
            TidegateWorld world = session.DevWorld;
            if (devices == null || world.Lighthouse == null) { Record(id, "FAIL", 0L, string.Empty, "机关 owner 或灯塔标记不可用"); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            TidegateSounds sounds = session.DevSounds;
            bool soundsOk = sounds != null && !sounds.DevUnavailable;
            if (devices.DevLighthouseLit)
            {
                devices.DevReset();
                if (enemies != null) enemies.DevEndReveal();
                notes.Add("relit_after_reset=true");
            }
            Vector3 lighthouse = world.Lighthouse.position;
            yield return TidegateDrillStand(session, lighthouse, errors, "lighthouse");
            // 官方 OnSound 用水平距离与个体听觉判定。点灯前后同一帧对照，以免被下一声枪响覆盖。
            var traceBefore = new Dictionary<string, float>(StringComparer.Ordinal);
            var near = new HashSet<string>(StringComparer.Ordinal);
            if (enemies != null)
                foreach (TidegateEnemies.DevEnemyInfo info in enemies.DevEnemies())
                {
                    if (!info.Alive) continue;
                    traceBefore[info.Id] = info.ForceTrace;
                    if (TidegateDrillFlat(info.Position, lighthouse) < 60f * info.Hearing) near.Add(info.Id);
                }
            int cue = soundsOk ? sounds.DevPosted(TidegateCue.Lighthouse) : 0, horn = soundsOk ? sounds.DevPosted(TidegateCue.Horn) : 0;
            int mark = session.DevStatusMark;
            _tidegateDrillTouched = true;
            devices.DevLightLighthouse();
            float litAt = Time.time;
            int heard = 0;
            if (enemies != null)
                foreach (TidegateEnemies.DevEnemyInfo info in enemies.DevEnemies())
                {
                    float before;
                    if (traceBefore.TryGetValue(info.Id, out before) && Math.Abs(info.ForceTrace - before) > 0.01f)
                        errors.Add("alarm_changed_player_tracking_" + info.Id);
                    if (near.Contains(info.Id) && info.NoticeAnonymous && (info.NoticePosition - lighthouse).sqrMagnitude < 0.01f) heard++;
                }
            if (heard != near.Count) errors.Add("guard_did_not_hear_anonymous_beacon_" + heard + "_of_" + near.Count);
            notes.Add("in_earshot=" + near.Count + ",heard_beacon=" + heard);
            yield return WaitSeconds(0.8f);
            Transform beam = devices.DevBeam;
            if (!devices.DevLighthouseLit) errors.Add("not_lit");
            if (beam == null || !beam.gameObject.activeInHierarchy) errors.Add("beam_missing_or_inactive");
            if (!devices.DevBeamLightOn) errors.Add("beam_point_light_missing");
            if (!session.Tide.DevBeaconOn) errors.Add("beacon_not_in_lamp_array");
            Vector4 slot0 = session.Tide.DevLampPosition(0);
            if (beam != null && (new Vector3(slot0.x, slot0.y, slot0.z) - beam.position).sqrMagnitude > 0.05f) errors.Add("lamp_slot0_not_beacon");
            if (session.Tide.DevStaticLamps < 1) errors.Add("lamp_array_empty");
            if (soundsOk && sounds.DevPosted(TidegateCue.Lighthouse) <= cue) errors.Add("lighthouse_sound_missing");
            if (TidegateDrillCount(session.DevStatusSince(mark), new[] { "灯塔亮了", "lighthouse is lit" }) != 1) errors.Add("lit_caption_missing");
            if (beam != null)
            {
                float yaw = beam.eulerAngles.y, from = Time.time;
                yield return WaitSeconds(1f);
                double advance = TidegateDrillYawAdvance(yaw, beam.eulerAngles.y);
                notes.Add("beam_advance_deg=" + advance.ToString("F1"));
                if (!TidegateDrillYawOk(advance, Time.time - from)) errors.Add("beam_rotation_" + advance.ToString("F1"));
            }
            if (enemies != null)
            {
                if (!enemies.DevRevealActive) errors.Add("reveal_not_active");
                int alive = 0, marked = 0;
                foreach (TidegateEnemies.DevEnemyInfo info in enemies.DevEnemies())
                {
                    if (!info.Alive) continue;
                    alive++;
                    if (info.MapPoint) marked++;
                }
                notes.Add("alive=" + alive + ",on_map=" + marked);
                if (marked != alive) errors.Add("enemies_on_map_" + marked + "_of_" + alive);
            }
            // 40 秒后全图显示结束，官方强制侦测距离始终不应被点灯改变。
            while (Time.time - litAt < 40.8f && !ShouldAbort()) yield return null;
            yield return WaitSeconds(0.4f);
            if (enemies != null)
            {
                if (enemies.DevRevealActive) errors.Add("reveal_not_ended_at_40s");
                int notRestored = 0, stillMarked = 0;
                foreach (TidegateEnemies.DevEnemyInfo info in enemies.DevEnemies())
                {
                    if (!info.Alive) continue;
                    float before;
                    if (traceBefore.TryGetValue(info.Id, out before) && Math.Abs(info.ForceTrace - before) > 0.01f) notRestored++;
                    if (info.MapPoint) stillMarked++;
                }
                if (notRestored > 0) errors.Add("force_trace_not_restored_" + notRestored);
                if (stillMarked > 0) errors.Add("map_points_left_" + stillMarked);
            }
            // 45 秒来船：站到码头边（离码头撤离环 5 m 外）听汽笛。
            Transform jetty;
            if (world.Exits.TryGetValue("lh", out jetty)) yield return TidegateDrillStand(session, jetty.position, errors, "jetty");
            while (!devices.DevBoatArrived && Time.time - litAt < 48f && !ShouldAbort()) yield return null;
            float boatAfter = Time.time - litAt;
            notes.Add("boat_after_s=" + boatAfter.ToString("F1"));
            if (!devices.DevBoatArrived) errors.Add("boat_not_arrived");
            else if (!TidegateDrillBoatOnTime(boatAfter)) errors.Add("boat_time_" + boatAfter.ToString("F1"));
            yield return WaitSeconds(0.8f);
            if (session.DevExtraction != null)
            {
                if (!session.DevExtraction.DevOpenExits().Contains("lh")) errors.Add("jetty_exit_not_open");
                if (!session.DevExtraction.DevVisibleRings().Contains("lh")) errors.Add("jetty_ring_not_visible");
            }
            if (soundsOk && jetty != null && TidegateDrillFlat(CharacterMainControl.Main.transform.position, jetty.position) < 44f
                && sounds.DevPosted(TidegateCue.Horn) <= horn) errors.Add("boat_horn_missing");
            if (TidegateDrillCount(session.DevStatusSince(mark), new[] { "船到灯塔码头了", "boat is at the lighthouse jetty" }) != 1)
                errors.Add("boat_caption_missing");
            if (errors.Count == 0 && near.Count == 0)
            {
                Record(id, "WARN", sw.ElapsedMilliseconds, string.Join(" | ", notes.ToArray()), "点灯时附近没有能听到信号的敌人，声源反应未采样");
                yield break;
            }
            RecordTidegateDrill(id, sw, errors, notes);
        }

        // ====================================================================
        // 6. 撤离读条与刷怪
        // ====================================================================

        private IEnumerator RunTidegateDrillExtractionHold()
        {
            const string id = "TIDE_DRILL_EXTRACTION_HOLD";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            TidegateExtraction extraction = session.DevExtraction;
            Transform road;
            if (extraction == null || !session.DevWorld.Exits.TryGetValue("road", out road))
            {
                Record(id, "FAIL", 0L, string.Empty, "撤离 owner 或公路检查站标记不可用");
                yield break;
            }
            var errors = new List<string>();
            var notes = new List<string>();
            string why;
            if (!session.DevTeleport(road.position, false, out why)) { Record(id, "FAIL", 0L, string.Empty, "teleport_" + why); yield break; }
            float held = -1f;
            string current = null;
            try
            {
                // 公路检查站读条 10 秒；只站 1.6 秒，读到一截就走。
                float until = Time.realtimeSinceStartup + 1.6f;
                while (Time.realtimeSinceStartup < until && !ShouldAbort())
                {
                    yield return null;
                    held = extraction.DevHeld;
                    current = extraction.DevCurrentExit;
                    if (held > 6f) break;
                }
            }
            finally
            {
                // 无论如何离开撤离环：读满会真的返航。
                TidegateDrillStepAway(session, road.position);
            }
            notes.Add("held_s=" + held.ToString("F2") + ",exit=" + current);
            if (current != "road") errors.Add("countdown_not_on_road_" + current);
            if (held < 0.8f) errors.Add("countdown_not_advancing");
            yield return WaitSeconds(0.6f);
            // 还在环里就再挪：读满 10 秒会真的返航，整趟演练与还原都会被打断。
            for (int attempt = 0; attempt < 3 && extraction.DevCurrentExit != null && !ShouldAbort(); attempt++)
            {
                notes.Add("step_away_retry_" + attempt);
                TidegateDrillStepAway(session, road.position);
                yield return WaitSeconds(0.6f);
            }
            if (extraction.DevCurrentExit != null || extraction.DevHeld >= 0f) errors.Add("countdown_not_reset_after_leaving");
            RecordTidegateDrill(id, sw, errors, notes);
        }

        /// <summary>
        /// 同步离开撤离环（finally 里用，不能 yield）：往出生点方向挪 8 m 吸到安全可走面；搬不过去就退到出生点附近的安全可走面，
        /// 再不行直接放回出生点。返回搬没搬成。
        /// </summary>
        private static bool TidegateDrillStepAway(TidegateSession session, Vector3 anchor)
        {
            try
            {
                Vector3 spawn = session.DevWorld.PlayerSpawn.position;
                Vector3 direction = spawn - anchor;
                direction.y = 0f;
                if (direction.sqrMagnitude < 1f) direction = Vector3.forward;
                Vector3 point;
                string why;
                if (TidegateDrillSafePoint(session, anchor + direction.normalized * 8f, out point) && session.DevTeleport(point, false, out why)) return true;
                if (TidegateDrillSafePoint(session, spawn, out point) && session.DevTeleport(point, false, out why)) return true;
                point = spawn;
                return session.DevTeleport(point, false, out why);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[TidegateDrill] 离开撤离环失败: " + e.Message);
                return false;
            }
        }

        private IEnumerator RunTidegateDrillEnemies()
        {
            const string id = "TIDE_DRILL_ENEMIES";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            TidegateEnemies enemies = session.DevEnemies;
            if (enemies == null) { Record(id, "FAIL", 0L, string.Empty, "刷怪 owner 不可用"); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            // 绕场一圈：刷怪要玩家在刷怪点 25–75 m 之间，每 0.5 秒一只。
            // 按低潮走，避免水下点整圈都不触发；最后的覆盖判据仍逐槽核对，不以绕过场代替刷出证据。
            TidegateTide.DevJumpInto(TidegateTidePhase.Low, 6.0);
            yield return WaitSeconds(1.3f);
            foreach (string poiName in TidegateDrillTourPois)
            {
                if (ShouldAbort()) break;
                Transform poi = session.DevFindMarker(poiName);
                if (poi == null) { notes.Add("poi_missing_" + poiName); continue; }
                yield return TidegateDrillStand(session, poi.position, errors, poiName);
                yield return WaitSeconds(2.5f);
            }
            yield return TidegateDrillAwaitSpawns(8f);
            int wallMask = GameplayDataSettings.Layers.wallLayerMask.value;
            SkyIslandWeaponBand elite = SkyIslandEnemyArmoryRules.For(SkyIslandEnemyTier.Elite);
            int total = 0, started = 0, alive = 0, failed = 0, guards = 0, bosses = 0, measured = 0, expectedGuards = 0, expectedBosses = 0;
            int town = 0, docks = 0, townStarted = 0, docksStarted = 0, townPartners = 0, docksPartners = 0, arrived = 0;
            int wolves = 0, sentries = 0, sniperLoaded = 0, grenadiers = 0, thrown = 0, partnersMoved = 0;
            foreach (TidegateEnemies.DevEnemyInfo info in enemies.DevEnemies())
            {
                if (info.Extra) continue;
                total++;
                if (info.Group == "town") { town++; if (info.Started) townStarted++; if (info.Partner) townPartners++; }
                if (info.Group == "docks") { docks++; if (info.Started) docksStarted++; if (info.Partner) docksPartners++; }
                if (info.Partner && info.JoinedAfter >= 0f)
                {
                    arrived++;
                    if (info.JoinedAfter < 3.99f) errors.Add("partner_arrived_before_scout_" + info.Id);
                }
                if (info.Partner && info.PatrolAdvance >= 3f) partnersMoved++;
                if (info.Kind == "guard") expectedGuards++;
                if (info.Kind == "boss") expectedBosses++;
                if (!info.Started) continue;
                started++;
                if (info.Failed) { failed++; errors.Add("spawn_failed_" + info.Id); continue; }
                if (info.SpawnDistance >= 0f)
                {
                    measured++;
                    if (!TidegateDrillSpawnDistanceOk(info.SpawnDistance)) errors.Add("spawn_distance_" + info.Id + "_" + info.SpawnDistance.ToString("F1"));
                }
                if (!info.Alive || info.Character == null) continue;
                alive++;
                CharacterMainControl character = info.Character;
                Teams expectedTeam = info.Group == "town" || info.Group == "docks" || info.Group == "reeds" ? Teams.scav : Teams.wolf;
                if (character.Team != expectedTeam || !Team.IsEnemy(Teams.player, character.Team)) errors.Add("district_team_" + info.Id + "_" + character.Team);
                Vector3 position = character.transform.position;
                if (Physics.CheckCapsule(position + Vector3.up * 0.45f, position + Vector3.up * 1.25f, 0.25f, wallMask, QueryTriggerInteraction.Ignore))
                    errors.Add("inside_wall_" + info.Id);
                if (info.Group != "dig" && session.Tide.IsDeepAt(position)) errors.Add("in_deep_water_" + info.Id);
                if (info.Role == TidegateEnemyRole.Hound) wolves++;
                if (info.Role == TidegateEnemyRole.Gunner)
                {
                    TidegateGunner bomber = character.GetComponent<TidegateGunner>();
                    if (bomber != null) { grenadiers++; thrown += bomber.Throws; }
                }
                if (info.Kind == "guard")
                {
                    guards++;
                    ItemStatsSystem.Items.Slot weapon = SkyIslandEnemyArmory.WeaponSlot(character.CharacterItem, true);
                    int quality = weapon == null || weapon.Content == null ? -1 : weapon.Content.Quality;
                    if (info.Role == TidegateEnemyRole.Marksman && weapon != null && weapon.Content != null
                        && weapon.Content.TypeID == TidegateEnemyActors.LookoutRifleId)
                    {
                        sentries++;
                        var gun = weapon.Content.GetComponent<ItemSetting_Gun>();
                        var loaded = gun != null ? gun.GetCurrentLoadedBullet() : null;
                        if (loaded != null && loaded.TypeID == TidegateEnemyActors.LookoutAmmoId) sniperLoaded++;
                        else if (loaded != null) errors.Add("wrong_lookout_round_" + loaded.TypeID);
                    }
                    else if (quality < elite.Min) errors.Add("guard_weapon_quality_" + info.Id + "_" + quality);
                }
                if (info.Kind == "boss")
                {
                    bosses++;
                    if (!info.PresetIsBoss || !info.SandBossBound) errors.Add("sand_boss_identity_or_drop_missing");
                }
            }
            // 与作者布局的候选点数分别报告：不能只把选中的槽当作分母而默许一组守备整个消失。
            int authored = session.DevWorld.SpawnMarkers.Count;
            if (authored == 25 && session.DevUnreachableSpawns == 0
                && (total != 27 || town != 9 || docks != 6 || townPartners != 4 || docksPartners != 3 || expectedGuards != 4 || expectedBosses != 1))
                errors.Add("encounter_roster_expected_27_town9_docks6_4guards_1boss");
            notes.Add("town_started=" + townStarted + "/" + town + ",docks_started=" + docksStarted + "/" + docks
                + ",partners_arrived=" + arrived + "/" + (townPartners + docksPartners) + ",partners_advanced_3m=" + partnersMoved);
            notes.Add("scav_wolves=" + wolves + ",lookout_rifle=" + sentries + ",lookout_loaded=" + sniperLoaded
                + ",gunners_armed=" + grenadiers + ",grenades_thrown=" + thrown);
            notes.Add("markers=" + authored + ",slots=" + total + ",started=" + started + ",alive=" + alive + ",failed=" + failed + ",guards=" + guards + ",bosses=" + bosses
                + ",expected_guards=" + expectedGuards + ",expected_bosses=" + expectedBosses
                + ",distance_measured=" + measured + ",unreachable=" + session.DevUnreachableSpawns);
            if (started == 0) errors.Add("nothing_spawned");
            string coverageReason;
            if (errors.Count == 0 && !TidegateDrillEnemyCoverageOk(total, started, measured, expectedGuards, guards, expectedBosses, bosses, alive, out coverageReason))
            {
                Record(id, "WARN", sw.ElapsedMilliseconds, string.Join(" | ", notes.ToArray()), coverageReason + ";未观测项目须人工补测");
                yield break;
            }
            if (errors.Count == 0 && (wolves < 2 || sentries < 1 || sniperLoaded < 1 || grenadiers < 1 || partnersMoved < 1))
            {
                Record(id, "WARN", sw.ElapsedMilliseconds, string.Join(" | ", notes.ToArray()), "部分岗位、狙击弹或结伴巡逻的实际位移未采样；请看分项数");
                yield break;
            }
            RecordTidegateDrill(id, sw, errors, notes);
        }

        // ====================================================================
        // 7. 地名、海浪、浮标钟与提示音
        // ====================================================================

        private IEnumerator RunTidegateDrillRegionSounds()
        {
            const string id = "TIDE_DRILL_REGION_SOUNDS";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            TidegateSounds sounds = session.DevSounds;
            TidegateWorld world = session.DevWorld;
            bool soundsOk = sounds != null && !sounds.DevUnavailable;
            if (sounds == null) errors.Add("sounds_missing");
            else if (sounds.DevUnavailable) errors.Add("sounds_unavailable(see [Tidegate] 音效不可用 in Player.log)");
            CharacterMainControl player = CharacterMainControl.Main;
            foreach (string poiName in new[] { "POI_码头", "POI_老镇", "POI_第三水厂", "POI_灯塔岬" })
            {
                if (ShouldAbort()) break;
                Transform poi = session.DevFindMarker(poiName);
                if (poi == null) { errors.Add("poi_missing_" + poiName); continue; }
                yield return TidegateDrillStand(session, poi.position, errors, poiName);
                yield return WaitSeconds(0.8f);
                float distance = TidegateDrillFlat(player.transform.position, poi.position);
                if (distance <= 20f && session.DevRegionPoi != poiName) errors.Add("region_at_" + poiName + "_shows_" + session.DevRegionPoi);
                if (poiName == "POI_码头" && soundsOk)
                {
                    yield return WaitSeconds(0.6f);
                    if (sounds.DevAmbientLoopsPlaying < 1) errors.Add("waves_not_playing_at_dock");
                }
            }
            // 离岸：找离四处海浪发声点都最远的地标，44 m 外海浪要停。
            Transform inland = null;
            float farthest = 0f;
            foreach (Transform poi in world.Pois)
            {
                float nearestShore = float.MaxValue;
                foreach (string shore in TidegateDrillShorePois)
                {
                    Transform marker = session.DevFindMarker(shore);
                    if (marker != null) nearestShore = Mathf.Min(nearestShore, TidegateDrillFlat(marker.position, poi.position));
                }
                if (nearestShore != float.MaxValue && nearestShore > farthest) { farthest = nearestShore; inland = poi; }
            }
            if (inland != null && farthest >= 50f)
            {
                yield return TidegateDrillStand(session, inland.position, errors, "inland");
                yield return WaitSeconds(0.8f);
                if (soundsOk && sounds.DevAmbientLoopsPlaying != 0) errors.Add("waves_still_playing_inland_" + inland.name);
                notes.Add("inland=" + inland.name + "@" + farthest.ToString("F0") + "m");
            }
            else notes.Add("no_inland_poi_beyond_50m(best=" + farthest.ToString("F0") + ")");
            // 浮标钟：栈桥边每 14–20 秒一声；前面游泳那条在栈桥边待过，多半已经响过。
            Transform ferry;
            if (soundsOk && world.Exits.TryGetValue("ferry", out ferry) && sounds.DevPostedFile(TidegateSounds.DevBellFile) == 0)
            {
                yield return TidegateDrillStand(session, ferry.position, errors, "bell");
                float until = Time.realtimeSinceStartup + 22f;
                while (sounds.DevPostedFile(TidegateSounds.DevBellFile) == 0 && Time.realtimeSinceStartup < until && !ShouldAbort()) yield return null;
            }
            if (soundsOk)
            {
                if (sounds.DevPostedFile(TidegateSounds.DevBellFile) == 0) errors.Add("buoy_bell_never_rang");
                foreach (TidegateCue cue in new[] { TidegateCue.Siren, TidegateCue.Gate, TidegateCue.Valve, TidegateCue.Lighthouse, TidegateCue.Horn })
                    if (sounds.DevPosted(cue) < 1) errors.Add("cue_never_posted_" + cue);
                notes.Add("siren=" + sounds.DevPosted(TidegateCue.Siren) + ",gate=" + sounds.DevPosted(TidegateCue.Gate) + ",valve="
                    + sounds.DevPosted(TidegateCue.Valve) + ",lighthouse=" + sounds.DevPosted(TidegateCue.Lighthouse) + ",horn="
                    + sounds.DevPosted(TidegateCue.Horn) + ",bell=" + sounds.DevPostedFile(TidegateSounds.DevBellFile));
            }
            RecordTidegateDrill(id, sw, errors, notes);
        }
    }
}
#endif
