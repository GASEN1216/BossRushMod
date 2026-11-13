using System;
using System.Collections.Generic;
using Runner = BossRush.F3GameplayValidationRunner;

namespace BossRush
{
    /// <summary>
    /// 末班编组站 Dev 演练纯判据的执行回归：站位离道、首班等人与教学停车、条件出口一致、拉铃 14 秒、车体在线、导航封线、
    /// 刷怪账目、撤离读条、线索计数、字幕计数。判据输入尽量由生产班次表（LastShiftTimetable）真实推进得到。
    /// </summary>
    internal static class Program
    {
        private static int failures;
        private const double RailX = LastShiftTimetable.WatchX, RailZ = LastShiftTimetable.WatchZ + 2, AwayX = -15.5, AwayZ = 35;

        private static void Check(bool condition, string label)
        {
            if (condition) return;
            failures++;
            Console.WriteLine("FAIL " + label);
        }

        /// <summary>按演练的口径推进：0.1 秒一步；首班车头扫过推车时记推车被撞（生产由 LastShiftTrains.Tick 的 FrontHit 判）。</summary>
        private static void Run(LastShiftTimetable t, double seconds, double px, double pz)
        {
            for (double s = 0; s < seconds - 1e-9; s += 0.1)
            {
                double old = t.Tick(0.1, px, pz);
                if (t.Training && !t.CartHit && t.FrontHit(old, LastShiftTimetable.CartX, LastShiftTimetable.CartZ, 0)) t.MarkCartHit();
            }
        }

        private static void RunUntil(LastShiftTimetable t, Func<LastShiftTimetable, bool> done, double max, double px, double pz)
        {
            for (double s = 0; s < max && !done(t); s += 0.1) Run(t, 0.1, px, pz);
        }

        private static int Main()
        {
            TrackClearance();
            FirstTrainWaitsAndHolds();
            ExitsAndBell();
            BodiesOnLine();
            Navigation();
            Population();
            LastShiftEnemies.TestDormantHearing(Check);
            ExtractionHold();
            Counter();
            Captions();
            Console.WriteLine(failures == 0 ? "LastShiftDrillJudges: ALL PASS" : "LastShiftDrillJudges: " + failures + " FAIL");
            return failures == 0 ? 0 : 1;
        }

        private static void TrackClearance()
        {
            // 南端两股道合成一股（x = 0），北端分到 ±24；南护栏观察位与运单牌旁离道够远，道心与分岔后的股道上都不行。
            Check(Runner.LastShiftDrillTrackClear(RailX, RailZ, 4.5), "watch_point_clear");
            Check(Runner.LastShiftDrillTrackClear(AwayX, AwayZ, 4.5), "manifest_point_clear");
            Check(!Runner.LastShiftDrillTrackClear(0, 30, 4.5), "south_track_blocked");
            Check(!Runner.LastShiftDrillTrackClear(-24, -40, 4.5), "west_track_blocked");
            Check(!Runner.LastShiftDrillTrackClear(24, -40, 4.5), "east_track_blocked");
            Check(Runner.LastShiftDrillTrackClear(0, -40, 4.5), "between_split_tracks_clear");
            Check(!Runner.LastShiftDrillTrackClear(-27, -33, 4.5), "west_platform_edge_too_close");
            Check(Runner.LastShiftDrillTrackClear(-36, -26, 4.5), "west_drop_stand_clear");
            // 演练的两个站位在班次表眼里确实一个在护栏后、一个不在。
            Check(LastShiftTimetable.AtSouthRail(RailX, RailZ), "rail_point_is_behind_rail");
            Check(!LastShiftTimetable.AtSouthRail(AwayX, AwayZ), "away_point_is_not_behind_rail");
        }

        private static void FirstTrainWaitsAndHolds()
        {
            Check(Runner.LastShiftDrillAwayBudget(0, 60) == 8, "away_budget_full");
            Check(Math.Abs(Runner.LastShiftDrillAwayBudget(50, 60) - 6) < 1e-9, "away_budget_partial");
            Check(Runner.LastShiftDrillAwayBudget(54, 60) == 0, "away_budget_too_small");
            Check(Runner.LastShiftDrillAwayBudget(70, 60) == 0, "away_budget_exhausted");

            var t = new LastShiftTimetable(17, false);
            string why;
            double before = t.PhaseTime, away = t.AwayFromRail;
            Run(t, 8, AwayX, AwayZ);
            Check(Runner.LastShiftDrillHeldForPlayer(before, t.PhaseTime, away, t.AwayFromRail, 8, out why), "held_for_player_away " + why);
            Check(t.Phase == LastShiftPhase.Approach, "still_approach_while_away");
            before = t.PhaseTime; away = t.AwayFromRail;
            Run(t, 5, RailX, RailZ);
            Check(!Runner.LastShiftDrillHeldForPlayer(before, t.PhaseTime, away, t.AwayFromRail, 5, out why) && why.StartsWith("phase_time_advanced", StringComparison.Ordinal),
                "not_held_behind_rail");
            Check(!Runner.LastShiftDrillHeldForPlayer(1, 1, 3, 3.5, 5, out why) && why.StartsWith("away_not_accumulated", StringComparison.Ordinal), "away_must_accumulate");

            // 站到护栏后：进站、撞推车、停在岔心前等调度；再多等也不动。
            RunUntil(t, x => x.DispatchWait, 60, RailX, RailZ);
            Check(t.CartHit, "cart_hit_before_hold");
            Check(Runner.LastShiftDrillTrainingHoldOk(t.Phase, t.DispatchWait, t.PhaseTime, t.LeadZ, out why), "training_hold " + why);
            Run(t, 5, RailX, RailZ);
            Check(Runner.LastShiftDrillTrainingHoldOk(t.Phase, t.DispatchWait, t.PhaseTime, t.LeadZ, out why), "training_hold_after_wait " + why);
            Check(!Runner.LastShiftDrillTrainingHoldOk(LastShiftPhase.Dwell, true, t.PhaseTime, t.LeadZ, out why), "hold_wrong_phase");
            Check(!Runner.LastShiftDrillTrainingHoldOk(t.Phase, false, t.PhaseTime, t.LeadZ, out why), "hold_not_waiting");
            Check(!Runner.LastShiftDrillTrainingHoldOk(t.Phase, true, t.PhaseTime + 0.1, t.LeadZ, out why), "hold_phase_time_drift");
            Check(!Runner.LastShiftDrillTrainingHoldOk(t.Phase, true, t.PhaseTime, 19, out why), "hold_lead_drift");
            string reason;
            Check(t.TrySwitch('E', out reason), "teaching_switch_allowed");
            Run(t, 1, RailX, RailZ);
            Check(!Runner.LastShiftDrillTrainingHoldOk(t.Phase, t.DispatchWait, t.PhaseTime, t.LeadZ, out why), "hold_released_after_switch");
        }

        private static void ExitsAndBell()
        {
            string why;
            Check(Runner.LastShiftDrillExitAgrees("west", true, true, true, out why), "exit_agree_open");
            Check(Runner.LastShiftDrillExitAgrees("west", false, false, false, out why), "exit_agree_closed");
            Check(!Runner.LastShiftDrillExitAgrees("west", true, true, false, out why) && why == "west_owner_open_rule_closed", "exit_owner_ahead " + why);
            Check(!Runner.LastShiftDrillExitAgrees("east", false, false, true, out why) && why == "east_owner_closed_rule_open", "exit_owner_behind " + why);
            Check(!Runner.LastShiftDrillExitAgrees("bell", true, false, true, out why) && why == "bell_ring_hidden", "exit_ring_hidden " + why);
            Check(!Runner.LastShiftDrillExitAgrees("bell", false, true, false, out why) && why == "bell_ring_shown", "exit_ring_shown " + why);

            // 拉铃：真实班次表推进，13 秒时规则关着、判据也认关着；14 秒后规则开、判据认开；反过来都不认。
            var t = new LastShiftTimetable(5, true);
            Check(Runner.LastShiftDrillBellOk(-1, t.ExitOpen("bell")), "bell_not_rung_closed");
            Check(!Runner.LastShiftDrillBellOk(-1, true), "bell_not_rung_open_rejected");
            t.RingBell();
            Run(t, 13, AwayX, AwayZ);
            double since = t.Clock - t.BellAt;
            Check(!t.ExitOpen("bell") && Runner.LastShiftDrillBellOk(since, t.ExitOpen("bell")), "bell_13s_closed");
            Check(!Runner.LastShiftDrillBellOk(since, true), "bell_13s_open_rejected");
            Run(t, 1.3, AwayX, AwayZ);
            since = t.Clock - t.BellAt;
            Check(t.ExitOpen("bell") && Runner.LastShiftDrillBellOk(since, t.ExitOpen("bell")), "bell_14s_open");
            Check(!Runner.LastShiftDrillBellOk(since, false), "bell_14s_closed_rejected");
            Check(Runner.LastShiftDrillBellOk(14.05, false) && Runner.LastShiftDrillBellOk(13.95, true), "bell_threshold_band_ignored");
            Check(!Runner.LastShiftDrillBellOk(double.NaN, false), "bell_nan_rejected");
        }

        private static void BodiesOnLine()
        {
            var t = new LastShiftTimetable(9, true);
            string reason;
            RunUntil(t, x => x.Phase == LastShiftPhase.Clear || x.Phase == LastShiftPhase.Approach, 200, AwayX, AwayZ);
            Check(t.TrySwitch(t.Line == 'W' ? 'E' : 'W', out reason) || t.Phase == LastShiftPhase.Approach, "switch_when_clear");
            char line = t.Line;
            for (int trip = 0; trip < 4 && t.Phase != LastShiftPhase.Dwell; trip++) RunUntil(t, x => x.Phase == LastShiftPhase.Dwell, 400, AwayX, AwayZ);
            Check(t.Phase == LastShiftPhase.Dwell, "reached_dwell");
            List<LastShiftCar> cars = t.Cars();
            Check(cars.Count >= 4, "cars_in_train");
            char other = line == 'W' ? 'E' : 'W';
            string why;
            foreach (LastShiftCar car in cars)
            {
                Check(Runner.LastShiftDrillBodyOnLine(car.X, car.Z, car.X, car.Z, line, out why), "body_on_line_" + car.Model + " " + why);
                Check(!Runner.LastShiftDrillBodyOnLine(car.X, car.Z, car.X + 0.1, car.Z, line, out why) && why.StartsWith("body_off_car", StringComparison.Ordinal), "body_off_car_" + car.Model);
                // 停靠时车厢都在北端分岔之后，换一股道就不在包络里。
                if (car.Z < -20) Check(!Runner.LastShiftDrillBodyOnLine(car.X, car.Z, car.X, car.Z, other, out why) && why == "body_off_line_" + other, "body_wrong_line_" + car.Model);
            }
        }

        private static void Navigation()
        {
            string why;
            Check(Runner.LastShiftDrillMovingNavOk(120, 0), "moving_line_closed");
            Check(!Runner.LastShiftDrillMovingNavOk(120, 1), "moving_line_leak_detected");
            Check(!Runner.LastShiftDrillMovingNavOk(0, 0), "moving_line_empty_detected");
            Check(Runner.LastShiftDrillNavOk(120, 110, 0, 110, 3900, 3900, 80, 80, out why), "nav_ok " + why);
            Check(!Runner.LastShiftDrillNavOk(0, 0, 0, 0, 3900, 3900, 0, 0, out why) && why.Contains("no_nav_nodes_on_line"), "nav_empty_envelope");
            Check(!Runner.LastShiftDrillNavOk(120, 0, 0, 0, 3900, 3900, 80, 80, out why) && why.Contains("line_not_walkable_when_clear"), "nav_vacuous");
            Check(!Runner.LastShiftDrillNavOk(120, 110, 3, 110, 3900, 3900, 80, 80, out why) && why.Contains("walkable_inside_envelope_during_dwell_3"), "nav_not_blocked");
            Check(!Runner.LastShiftDrillNavOk(120, 110, 0, 100, 3900, 3900, 80, 80, out why) && why.Contains("envelope_not_restored"), "nav_not_restored");
            Check(!Runner.LastShiftDrillNavOk(120, 110, 0, 110, 3900, 3890, 80, 80, out why) && why.Contains("walkable_total"), "nav_total_changed");
            Check(!Runner.LastShiftDrillNavOk(120, 110, 0, 110, 3900, 3900, 80, 60, out why) && why.Contains("other_line_changed"), "nav_other_line_blocked");
        }

        private static void Population()
        {
            string why;
            Check(Runner.LastShiftDrillPopulationOk(40, 0, 0, 0, 0, out why), "pop_min " + why);
            Check(Runner.LastShiftDrillPopulationOk(46, 46, 10, 10, 7, out why), "pop_max " + why);
            Check(!Runner.LastShiftDrillPopulationOk(39, 0, 0, 0, 0, out why), "pop_too_small");
            Check(!Runner.LastShiftDrillPopulationOk(47, 0, 0, 0, 0, out why), "pop_too_large");
            Check(!Runner.LastShiftDrillPopulationOk(42, 43, 0, 0, 0, out why), "pop_spawned_over");
            Check(!Runner.LastShiftDrillPopulationOk(42, 9, 7, 11, 0, out why) && why.Contains("live_over_cap_11"), "pop_peak_over_cap");
            Check(!Runner.LastShiftDrillPopulationOk(42, 9, 8, 8, 8, out why) && why.Contains("mobile_over_cap_8"), "pop_mobile_over_cap");
            // 围圈小队冻结：站位与朝向（2026-11-12）。
            Check(Runner.LastShiftDrillSquadHoldOk(0.05, 3, out why), "squad_hold_ok " + why);
            Check(Runner.LastShiftDrillSquadHoldOk(1.0, 35, out why), "squad_hold_boundary " + why);
            Check(!Runner.LastShiftDrillSquadHoldOk(1.4, 0, out why) && why.StartsWith("drift_"), "squad_drifted");
            Check(!Runner.LastShiftDrillSquadHoldOk(0.2, 90, out why) && why.StartsWith("facing_"), "squad_turned");
            Check(!Runner.LastShiftDrillSquadHoldOk(double.NaN, 0, out why), "squad_drift_nan");
        }

        private static void ExtractionHold()
        {
            string why;
            Check(Runner.LastShiftDrillHoldOk("north", 1.4, out why), "hold_ok " + why);
            Check(!Runner.LastShiftDrillHoldOk("west", 1.4, out why), "hold_wrong_exit");
            Check(!Runner.LastShiftDrillHoldOk(null, -1, out why), "hold_no_exit");
            Check(!Runner.LastShiftDrillHoldOk("north", 0.5, out why), "hold_not_advancing");
            Check(!Runner.LastShiftDrillHoldOk("north", 5.0, out why) && why == "countdown_filled", "hold_filled");
            Check(!Runner.LastShiftDrillHoldOk("north", double.NaN, out why), "hold_nan");
        }

        private static void Counter()
        {
            Check(Runner.LastShiftDrillCounterOk(null, true, 2, 3), "counter_hidden_after_journal");
            Check(!Runner.LastShiftDrillCounterOk("末班票 2/3", true, 2, 3), "counter_shown_after_journal_rejected");
            Check(Runner.LastShiftDrillCounterOk(null, false, 0, 3), "counter_hidden_when_none");
            Check(!Runner.LastShiftDrillCounterOk(null, false, 1, 3), "counter_missing_rejected");
            Check(Runner.LastShiftDrillCounterOk("末班票 2/3", false, 2, 3), "counter_zh");
            Check(Runner.LastShiftDrillCounterOk("Last ticket 3/3", false, 3, 3), "counter_en");
            Check(!Runner.LastShiftDrillCounterOk("末班票 1/3", false, 2, 3), "counter_wrong_count_rejected");
        }

        private static void Captions()
        {
            var lines = new List<string> { "站场广播：货车改走东线", "!本班已锁线，等列车出清后再改道。", "Yard tannoy: freight routed", "其他" };
            Check(Runner.LastShiftDrillCount(lines, new[] { "站场广播", "Yard tannoy" }) == 2, "caption_count_bilingual");
            Check(Runner.LastShiftDrillCount(lines, new[] { "本班已锁线", "locked the route" }) == 1, "caption_count_warning_prefix");
            Check(Runner.LastShiftDrillCount(null, new[] { "x" }) == 0, "caption_count_null");
            Check(Runner.LastShiftDrillContainsAny("Waiting for the switch", "waiting for the switch"), "contains_ignore_case");
            Check(!Runner.LastShiftDrillContainsAny(null, "x") && !Runner.LastShiftDrillContainsAny("abc", null), "contains_null");
            Check(Runner.LastShiftDrillJoinSorted(new[] { "westRisk", "bondCab", "eastRisk" }) == "bondCab,eastRisk,westRisk", "join_sorted");
        }
    }
}
