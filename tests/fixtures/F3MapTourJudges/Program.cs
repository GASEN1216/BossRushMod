using System;
using BossRush;

// F3 地图选择器逐图进场（MAP_TOUR_*）的纯判据：每一类缺陷都要单独转红，合格观测必须转绿。
// 参数：run.py 用 tools/gameplay_coverage.py 的 MAP_TOUR_* 展开结果与场景名成对传进来，核对两边用例 id 同口径。
internal static class Program
{
    private static int checks;

    private static void Check(bool value, string description)
    {
        checks++;
        if (!value) throw new Exception("FAIL " + description);
    }

    private static F3MapTourObservation Good(bool customSpawn)
    {
        return new F3MapTourObservation
        {
            ExpectedScene = "Level_GroundZero_1", ActualScene = "Level_GroundZero_1", RuntimeReady = true, ArenaActive = true,
            EntryPointPresent = true, ConfiguredPoints = 24, LoadedPoints = 24, GroundedPoints = 24, NavPoints = 24,
            HasCustomSpawn = customSpawn, PlayerToSpawnMeters = customSpawn ? 0.4f : float.NaN, PlayerGrounded = true,
            ReturnedToBase = true, ElapsedMs = 30000,
        };
    }

    private static void Red(F3MapTourObservation o, string expected, string description)
    {
        string metrics, reason;
        bool ok = F3MapTourJudges.JudgeArrival(o, out metrics, out reason);
        Check(!ok && reason != null && reason.Contains(expected), description + " -> " + (reason ?? "PASS"));
    }

    private static void Main(string[] args)
    {
        // ---- 用例 id ----
        Check(F3MapTourJudges.CaseId("Level_DemoChallenge_1") == "MAP_TOUR_LEVEL_DEMOCHALLENGE_1", "scene name upper-cased");
        Check(F3MapTourJudges.CaseId("Level_SnowMilitaryBase_ColdStorage") == "MAP_TOUR_LEVEL_SNOWMILITARYBASE_COLDSTORAGE", "underscores kept");
        Check(F3MapTourJudges.CaseId("a-b c") == "MAP_TOUR_A_B_C", "non alphanumerics become underscores");
        Check(F3MapTourJudges.CaseId(null) == "MAP_TOUR_", "missing name keeps the prefix only");
        Check(args.Length > 0 && args.Length % 2 == 0, "run.py passes scene/id pairs from tools/gameplay_coverage.py");
        for (int i = 0; i + 1 < args.Length; i += 2)
            Check(F3MapTourJudges.CaseId(args[i]) == args[i + 1], "game and tool agree on the id of " + args[i]);

        // ---- 一张图 ----
        string metrics, reason;
        Check(F3MapTourJudges.JudgeArrival(Good(true), out metrics, out reason) && reason == null, "a clean custom-spawn arrival passes");
        Check(metrics.Contains("points=24/24") && metrics.Contains("player_to_spawn_m=0.4"), "metrics carry the counts: " + metrics);
        Check(F3MapTourJudges.JudgeArrival(Good(false), out metrics, out reason), "the DEMO arena has no custom spawn and needs no distance");

        F3MapTourObservation o;
        o = Good(true); o.CollectError = "NullReferenceException"; Red(o, "collect_threw", "a throwing probe is never green");
        o = Good(true); o.ActualScene = "Level_GroundZero_Main"; Red(o, "wrong_scene", "landing in the loader scene");
        o = Good(true); o.ExpectedScene = null; Red(o, "wrong_scene", "a map without a scene name");
        o = Good(true); o.RuntimeReady = false; Red(o, "runtime_not_ready", "player or camera missing");
        o = Good(true); o.ArenaActive = false; Red(o, "arena_not_active", "BossRush never took over the map");
        o = Good(true); o.OtherMode = "ModeD"; Red(o, "unexpected_mode:ModeD", "entering drifted into another mode");
        o = Good(true); o.EntryPointPresent = false; Red(o, "entry_point_missing", "no roadsign to start from");
        o = Good(true); o.ConfiguredPoints = 0; o.LoadedPoints = 0; o.GroundedPoints = 0; o.NavPoints = 0;
        Red(o, "no_spawn_points_configured", "a map with no boss spawn points");
        o = Good(true); o.LoadedPoints = 0; Red(o, "spawn_points_not_loaded", "the map's points never loaded");
        o = Good(true); o.GroundedPoints = 23; o.PointOffenders = "5:ground"; Red(o, "spawn_points_floating", "one floating point");
        o = Good(true); o.GroundedPoints = 23; o.PointOffenders = "5:ground"; Red(o, "points=5:ground", "the offender is named");
        o = Good(true); o.NavPoints = 20; Red(o, "spawn_points_off_navmesh", "points bosses cannot walk from");
        o = Good(true); o.PlayerToSpawnMeters = 12f; Red(o, "player_not_at_custom_spawn", "teleport never happened");
        o = Good(true); o.PlayerToSpawnMeters = float.NaN; Red(o, "player_not_at_custom_spawn", "an unmeasured distance is not a pass");
        o = Good(true); o.PlayerGrounded = false; Red(o, "player_not_grounded", "player hanging in the air");
        o = Good(true); o.ReturnedToBase = false; Red(o, "return_base_failed", "stuck after the map");
        o = Good(true); o.BaseLeftover = "arena_active"; Red(o, "arena_state_left_after_return:arena_active", "arena flag leaked to base");
        o = Good(true); o.ArenaActive = false; o.EntryPointPresent = false;
        Check(!F3MapTourJudges.JudgeArrival(o, out metrics, out reason) && reason.Contains("arena_not_active") && reason.Contains("entry_point_missing"),
            "every failure is listed, not only the first");
        Check(F3MapTourJudges.MaxPlayerToSpawnMeters <= 5f && F3MapTourJudges.MaxNavDistanceMeters <= 3f, "thresholds stay tight");

        // ---- 全表 ----
        Check(!F3MapTourJudges.JudgeTour(0, 0, 0, out metrics, out reason) && reason == "map_selector_empty", "empty selector is red");
        Check(!F3MapTourJudges.JudgeTour(9, 5, 5, out metrics, out reason) && reason == "not_all_maps_ran", "a partial tour cannot stand for the table");
        Check(!F3MapTourJudges.JudgeTour(9, 8, 9, out metrics, out reason) && reason == "some_maps_failed", "one failed map is red");
        Check(F3MapTourJudges.JudgeTour(9, 9, 9, out metrics, out reason) && metrics == "maps=9,ran=9,passed=9", "a full clean tour is green");

        Console.WriteLine("F3MapTourJudges production-linked PASS assertions=" + checks);
    }
}
