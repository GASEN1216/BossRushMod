using System;
using System.Collections.Generic;
using Runner = BossRush.F3GameplayValidationRunner;

namespace BossRush
{
    /// <summary>潮闸镇 Dev 演练纯判据的执行回归：拨潮相的落点、出口开关、泻湖放水、光束转速、刷怪距离、来船时间、字幕计数。</summary>
    internal static class Program
    {
        private const double Scale = 60.0;
        private static int failures;

        private static void Check(bool condition, string label)
        {
            if (condition) return;
            failures++;
            Console.WriteLine("FAIL " + label);
        }

        private static double RealToHours(double seconds) { return seconds * Scale / 3600.0; }

        private static int Main()
        {
            // 相位边界（游戏小时）：退潮 0、低潮 0.5、涨潮 2.5、高潮 3、一轮 4.5；每一相的起点与终点前一点都还在这一相里。
            Check(Runner.TidegateDrillPhaseStart(TidegateTidePhase.Ebb) == 0.0, "ebb_start");
            Check(Math.Abs(Runner.TidegateDrillPhaseStart(TidegateTidePhase.Low) - 0.5) < 1e-9, "low_start");
            Check(Math.Abs(Runner.TidegateDrillPhaseStart(TidegateTidePhase.Flood) - 2.5) < 1e-9, "flood_start");
            Check(Math.Abs(Runner.TidegateDrillPhaseStart(TidegateTidePhase.High) - 3.0) < 1e-9, "high_start");
            Check(Math.Abs(Runner.TidegateDrillPhaseEnd(TidegateTidePhase.High) - TidegateTideRules.CycleHours) < 1e-9, "high_end");
            foreach (TidegateTidePhase phase in Enum.GetValues(typeof(TidegateTidePhase)))
            {
                double start = Runner.TidegateDrillPhaseStart(phase), end = Runner.TidegateDrillPhaseEnd(phase);
                Check(end > start, "phase_span_" + phase);
                Check(TidegateTideRules.PhaseAt(start + 1e-4) == phase, "phase_at_start_" + phase);
                Check(TidegateTideRules.PhaseAt(end - 1e-4) == phase, "phase_at_end_" + phase);
            }

            // 只往前拨：落点在当前之前就绕到下一轮；结果在 [0, 一轮)，加上去正好落在目标的轮内位置。
            Check(Math.Abs(Runner.TidegateDrillForwardHours(1.0, 3.0) - 2.0) < 1e-9, "forward_simple");
            Check(Math.Abs(Runner.TidegateDrillForwardHours(4.0, 0.5) - 1.0) < 1e-9, "forward_wrap");
            Check(Runner.TidegateDrillForwardHours(2.0, 2.0) == 0.0, "forward_zero");
            var random = new Random(20261106);
            for (int i = 0; i < 4000; i++)
            {
                double from = random.NextDouble() * TidegateTideRules.CycleHours, to = random.NextDouble() * TidegateTideRules.CycleHours;
                double delta = Runner.TidegateDrillForwardHours(from, to);
                Check(delta >= 0.0 && delta < TidegateTideRules.CycleHours, "forward_range_" + i);
                double landed = TidegateTideRules.CycleOffset(from + delta), target = TidegateTideRules.CycleOffset(to);
                double gap = Math.Abs(landed - target);
                Check(gap < 1e-6 || Math.Abs(gap - TidegateTideRules.CycleHours) < 1e-6, "forward_lands_" + i);
            }

            // 拨到「某相开始后 4 现实秒」「某相结束前 15 现实秒」：从任意跨天时刻出发都落在那一相里，剩余秒数对得上。
            for (int day = 0; day < 6; day++)
            {
                double now = TidegateTideRules.ElapsedHours(day, 12345.0 + day * 777.0);
                foreach (TidegateTidePhase phase in Enum.GetValues(typeof(TidegateTidePhase)))
                {
                    double into = now + Runner.TidegateDrillForwardHours(TidegateTideRules.CycleOffset(now),
                        Runner.TidegateDrillPhaseStart(phase) + RealToHours(4.0));
                    Check(TidegateTideRules.PhaseAt(into) == phase, "jump_into_" + phase + "_" + day);
                    double before = now + Runner.TidegateDrillForwardHours(TidegateTideRules.CycleOffset(now),
                        Runner.TidegateDrillPhaseEnd(phase) - RealToHours(15.0));
                    Check(TidegateTideRules.PhaseAt(before) == phase, "jump_before_end_" + phase + "_" + day);
                    Check(Math.Abs(TidegateTideRules.RealSecondsUntilNextPhase(before, Scale) - 15.0) < 1e-3, "jump_before_end_seconds_" + phase + "_" + day);
                }
            }

            // 演练拨进「低潮还剩 17 秒」：先没有提示，2 秒后到 15 秒出涨潮警报；「高潮还剩 15 秒」：3 秒后到 12 秒出渡口要走。
            double low17 = Runner.TidegateDrillPhaseEnd(TidegateTidePhase.Low) - RealToHours(17.0);
            Check(TidegateTideRules.NoticeAt(low17, Scale) == TidegateTideNotice.None, "flood_alarm_not_yet");
            Check(TidegateTideRules.NoticeAt(low17 + RealToHours(2.1), Scale) == TidegateTideNotice.FloodSoon, "flood_alarm_after_two_seconds");
            double high15 = Runner.TidegateDrillPhaseEnd(TidegateTidePhase.High) - RealToHours(15.0);
            Check(TidegateTideRules.NoticeAt(high15, Scale) == TidegateTideNotice.None, "ferry_notice_not_yet");
            Check(TidegateTideRules.NoticeAt(high15 + RealToHours(3.1), Scale) == TidegateTideNotice.FerryLeaving, "ferry_notice_after_three_seconds");

            // 出口：公路常驻；渡口只在高潮；暗渠出口水深 < 0.25 m；灯塔码头要来过船。
            Check(Runner.TidegateDrillExpectedExits(TidegateTidePhase.High, 1.57, false) == "ferry,road", "exits_high");
            Check(Runner.TidegateDrillExpectedExits(TidegateTidePhase.Low, -0.38, false) == "drain,road", "exits_low");
            Check(Runner.TidegateDrillExpectedExits(TidegateTidePhase.Ebb, 0.24, true) == "drain,lh,road", "exits_ebb_boat");
            Check(Runner.TidegateDrillExpectedExits(TidegateTidePhase.Flood, 0.25, false) == "road", "exits_flood_drain_threshold");
            Check(Runner.TidegateDrillExpectedExits(TidegateTidePhase.Flood, double.NaN, false) == "road", "exits_no_drain_anchor");
            Check(Runner.TidegateDrillJoinSorted(new[] { "road", "drain", "ferry" }) == "drain,ferry,road", "join_sorted");

            // 泻湖每秒放 0.085 m，到外海潮位就停；没在放水要判出来。
            double expected;
            // 放水速度是 float 常量（0.085f），换成 double 有 1e-9 量级的误差。
            Check(Runner.TidegateDrillLagoonDropOk(0.62, 0.45, -1.5, 2.0, out expected) && Math.Abs(expected - 0.45) < 1e-6, "lagoon_drop_two_seconds");
            Check(!Runner.TidegateDrillLagoonDropOk(0.62, 0.62, -1.5, 2.0, out expected), "lagoon_not_draining_detected");
            Check(Runner.TidegateDrillLagoonDropOk(-1.45, -1.5, -1.5, 2.0, out expected) && expected == -1.5, "lagoon_stops_at_sea");

            // 光束：欧拉角按 360 绕回；约 30°/秒。
            Check(Math.Abs(Runner.TidegateDrillYawAdvance(350.0, 20.0) - 30.0) < 1e-9, "yaw_wrap");
            Check(Math.Abs(Runner.TidegateDrillYawAdvance(10.0, 40.0) - 30.0) < 1e-9, "yaw_plain");
            Check(Runner.TidegateDrillYawOk(30.0, 1.0) && Runner.TidegateDrillYawOk(31.0, 1.05), "yaw_rate_ok");
            Check(!Runner.TidegateDrillYawOk(0.0, 1.0) && !Runner.TidegateDrillYawOk(90.0, 1.0) && !Runner.TidegateDrillYawOk(30.0, 0.0), "yaw_rate_bad");

            // 刷怪在玩家 25–75 m 之间；灯塔点亮后 45 秒来船。
            Check(Runner.TidegateDrillSpawnDistanceOk(25.0) && Runner.TidegateDrillSpawnDistanceOk(74.9), "spawn_distance_ok");
            Check(!Runner.TidegateDrillSpawnDistanceOk(10.0) && !Runner.TidegateDrillSpawnDistanceOk(80.0), "spawn_distance_bad");
            Check(Runner.TidegateDrillBoatOnTime(45.2) && Runner.TidegateDrillBoatOnTime(44.0), "boat_on_time");
            Check(!Runner.TidegateDrillBoatOnTime(40.0) && !Runner.TidegateDrillBoatOnTime(50.0), "boat_late_or_early");

            // 缺角色或取数时绝不能 PASS：有一名普通敌人不等于守卫 / Boss 均已观测。
            string coverage;
            Check(Runner.TidegateDrillEnemyCoverageOk(5, 5, 5, 2, 2, 1, 1, 5, out coverage), "enemy_complete_observations");
            Check(!Runner.TidegateDrillEnemyCoverageOk(5, 1, 1, 2, 0, 1, 0, 1, out coverage), "enemy_only_scav_is_incomplete");
            Check(!Runner.TidegateDrillEnemyCoverageOk(5, 5, 5, 2, 0, 1, 1, 3, out coverage), "enemy_missing_live_guards");
            Check(!Runner.TidegateDrillEnemyCoverageOk(5, 5, 5, 2, 2, 1, 0, 4, out coverage), "enemy_missing_live_boss");
            Check(!Runner.TidegateDrillEnemyCoverageOk(5, 5, 4, 2, 2, 1, 1, 5, out coverage), "enemy_missing_distance_sample");
            Check(!Runner.TidegateDrillEnemyCoverageOk(1, 1, 1, 0, 0, 0, 0, 1, out coverage), "enemy_missing_guard_boss_slots");

            // 缺水盒、非首盒不同步与 NaN 均会红；原先 Math.Abs(NaN) > 容差恒为 false。
            string waterReason;
            Check(Runner.TidegateDrillWaterTopsOk(new[] { 0.45f, 0.45f }, 0.45, 0.03, out waterReason), "water_all_volumes_synchronized");
            Check(!Runner.TidegateDrillWaterTopsOk(new float[0], 0.45, 0.03, out waterReason), "water_missing_volume");
            Check(!Runner.TidegateDrillWaterTopsOk(new[] { 0.45f, -1.5f }, 0.45, 0.03, out waterReason), "water_second_volume_stale");
            Check(!Runner.TidegateDrillWaterTopsOk(new[] { float.NaN }, 0.45, 0.03, out waterReason), "water_nan_top");
            Check(!Runner.TidegateDrillWaterTopsOk(new[] { float.PositiveInfinity }, 0.45, 0.03, out waterReason), "water_infinite_top");
            Check(!Runner.TidegateDrillWaterTopsOk(new[] { 0.45f }, double.NaN, 0.03, out waterReason), "water_nan_expected");

            // 箱柜状态（2026-11-07 实机误报）：上锁的领班柜在干地上本来就关着；没锁的干箱必须能开，被淹的必须收起；埋货模型跟着淹没显隐。
            Check(Runner.TidegateDrillLootStateOk(false, false, true, false, null), "loot_locked_dry_closed");
            Check(!Runner.TidegateDrillLootStateOk(false, false, true, true, null), "loot_locked_but_open");
            Check(Runner.TidegateDrillLootStateOk(false, false, false, true, null), "loot_dry_open");
            Check(!Runner.TidegateDrillLootStateOk(false, false, false, false, null), "loot_dry_hidden");
            Check(Runner.TidegateDrillLootStateOk(true, true, false, false, null), "loot_flooded_hidden");
            Check(Runner.TidegateDrillLootStateOk(true, true, true, false, null), "loot_flooded_locked_hidden");
            Check(!Runner.TidegateDrillLootStateOk(true, true, false, true, null), "loot_flooded_but_open");
            Check(!Runner.TidegateDrillLootStateOk(true, false, false, false, null), "loot_flood_flag_disagrees_with_depth");
            Check(Runner.TidegateDrillLootStateOk(false, false, false, true, true), "buried_dry_visible");
            Check(!Runner.TidegateDrillLootStateOk(false, false, false, true, false), "buried_dry_model_missing");
            Check(Runner.TidegateDrillLootStateOk(true, true, false, false, false), "buried_flooded_model_hidden");
            Check(!Runner.TidegateDrillLootStateOk(true, true, false, false, true), "buried_flooded_model_still_shown");
            Check(!Runner.TidegateDrillLevelOk(double.NaN, 0.45, 0.03)
                && !Runner.TidegateDrillLevelOk(0.45, double.PositiveInfinity, 0.03), "shader_levels_require_finite_values");
            foreach (string failure in TidegateEnemies.CheckLateSpawnCancellation()) Check(false, failure);
            foreach (string failure in TidegateEnemies.CheckRaidVariety()) Check(false, failure);
            foreach (string failure in TidegateEnemies.CheckSquadPatrol()) Check(false, failure);
            // 三轮真实吊装曲线，极长帧也不能跳过落区预告。死亡边沿与物体接地只重合一帧。
            var crane = new TidegateCraneCycle();
            int impacts = 0, warnedFrames = 0;
            bool warnedBefore = false;
            for (int i = 0; i < 4000; i++)
            {
                bool hit = crane.Step(i % 53 == 0 ? 100f : 0.016f);
                if (!crane.Trained) Check(!hit && !crane.Down && crane.Height >= 2f, "brake rehearsal reached player");
                if (crane.Warns) { warnedFrames++; warnedBefore = true; }
                if (!hit) continue;
                Check(warnedBefore && warnedFrames >= 28 && crane.Down && crane.Height < 0.001f, "crane_warns_then_contacts_ground");
                warnedBefore = false; warnedFrames = 0; impacts++;
            }
            Check(impacts >= 2 && impacts <= 4, "crane_single_strike_after_rehearsal");
            Check(TidegateCraneCycle.UnderLoad(0.65f, 0f, 0.80f) && !TidegateCraneCycle.UnderLoad(0.651f, 0f, 0f)
                && !TidegateCraneCycle.UnderLoad(0f, 0f, -0.801f) && !TidegateCraneCycle.UnderLoad(0f, 1.51f, 0f)
                && !TidegateCraneCycle.UnderLoad(0f, -0.56f, 0f), "crane_footprint_and_vertical_bounds");
            float paused = crane.Elapsed;
            Check(!crane.Step(float.NaN) && !crane.Step(float.PositiveInfinity) && !crane.Step(-1f) && crane.Elapsed == paused, "crane_invalid_delta_holds");

            // 字幕计数：中英两种语言都认，警示色前缀「!」不影响；换相字幕关键词对得上会话里的原文。
            var lines = new List<string> { "潮汐警报：12 秒后涨潮，滩涂上的往高处走。", "!Tide alarm: the flood starts in 12 s. Get off the flats.", "低潮：滩涂、沉船货舱和排水暗渠都露出来了，淘沙帮下滩了。" };
            Check(Runner.TidegateDrillCount(lines, new[] { "潮汐警报", "Tide alarm" }) == 2, "count_both_languages");
            Check(Runner.TidegateDrillCount(lines, Runner.TidegateDrillPhaseCaption(TidegateTidePhase.Low)) == 1, "phase_caption_low");
            Check(Runner.TidegateDrillCount(new List<string> { "High tide: a boat is at the ferry pier. You can leave from there." },
                Runner.TidegateDrillPhaseCaption(TidegateTidePhase.High)) == 1, "phase_caption_high_english");
            Check(Runner.TidegateDrillCount(null, new[] { "x" }) == 0, "count_null_lines");
            Check(!Runner.TidegateDrillContainsAny(null, "x") && !Runner.TidegateDrillContainsAny("abc"), "contains_any_edges");

            Console.WriteLine(failures == 0 ? "PASS TidegateDrillJudges" : "FAIL TidegateDrillJudges failures=" + failures);
            return failures == 0 ? 0 : 1;
        }
    }
}
