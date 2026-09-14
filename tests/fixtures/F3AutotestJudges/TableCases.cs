using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BossRush;

/// <summary>1. 步骤表：真实 Assets/Data/SkyIslandAutotest.json 解析与结构校验，外加逐条红样本。</summary>
internal static partial class Program
{
    private static string tableJson;

    private static F3AutotestTable FreshTable()
    {
        var errors = new List<string>();
        F3AutotestTable table = F3AutotestJudges.ParseTable(tableJson, errors);
        if (table == null || errors.Count > 0) throw new InvalidOperationException("real step table no longer parses: " + Dump(errors));
        return table;
    }

    private static List<string> Validate(F3AutotestTable table, ICollection<string> cases, ICollection<string> checklist, ICollection<string> offline)
    {
        var errors = new List<string>();
        F3AutotestJudges.ValidateTable(table, cases, checklist, offline, errors);
        return errors;
    }

    private static F3AutotestStep Step(F3AutotestTable table, string id)
    {
        F3AutotestStep step = F3AutotestJudges.FindStep(table, id);
        if (step == null) throw new InvalidOperationException("step missing from real table: " + id);
        return step;
    }

    private static F3AutotestCoverageRow Row(F3AutotestTable table, string checklist)
    {
        foreach (F3AutotestCoverageRow row in table.Coverage) if (row.Checklist == checklist) return row;
        throw new InvalidOperationException("coverage row missing from real table: " + checklist);
    }

    private static F3AutotestStage StoryStage(F3AutotestTable table, string id)
    {
        F3AutotestStage stage = table.FindStage(id);
        if (stage == null || stage.When != "story") throw new InvalidOperationException("story stage missing: " + id);
        return stage;
    }

    /// <summary>已知用例 id：GameplayCoverage.json 全部 feature 的 automatic ∪ AutotestCaseDelegate 的 case 字面量。</summary>
    private static HashSet<string> KnownCases()
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        BossRushJsonValue coverage = BossRushJsonParser.ParseOrNull(File.ReadAllText(RepoPath("Assets/Data/GameplayCoverage.json")));
        Check(coverage != null, "GameplayCoverage.json parses");
        List<BossRushJsonValue> features;
        if (coverage != null && coverage.TryGetArray("features", out features))
        {
            foreach (BossRushJsonValue feature in features)
            {
                List<string> automatic;
                if (feature.TryGetStringList("automatic", out automatic)) known.UnionWith(automatic);
            }
        }
        int fromCoverage = known.Count;
        string asserts = File.ReadAllText(RepoPath("DebugAndTools/F3GameplayValidationAutotestAsserts.cs"));
        int start = asserts.IndexOf("private SyncValidation AutotestCaseDelegate", StringComparison.Ordinal);
        int end = start < 0 ? -1 : asserts.IndexOf("default: return null;", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "AutotestCaseDelegate body located in F3GameplayValidationAutotestAsserts.cs");
        int delegateCases = 0;
        if (start >= 0 && end > start)
        {
            foreach (Match match in Regex.Matches(asserts.Substring(start, end - start), "case\\s+\"([^\"]+)\""))
            {
                delegateCases++;
                known.Add(match.Groups[1].Value);
            }
        }
        Check(fromCoverage > 20 && delegateCases > 10 && known.Contains("SKY_STORY_CODEC") && known.Contains("SKY_KEEPSAKE_ITEMS_BASE"),
            "known case set is not vacuous (coverage=" + fromCoverage + ", delegate=" + delegateCases + ")");
        return known;
    }

    private static void TableCases()
    {
        tableJson = File.ReadAllText(RepoPath("Assets/Data/SkyIslandAutotest.json"));
        var parseErrors = new List<string>();
        F3AutotestTable table = F3AutotestJudges.ParseTable(tableJson, parseErrors);
        Check(table != null && parseErrors.Count == 0, "real step table parses: " + Dump(parseErrors));
        if (table == null) return;
        Check(table.Version == 1, "step table version is 1");
        // 解析没有静默丢行：步骤行带 "stage"、覆盖行的 "checklist" 是字符串（步骤里是数组）。
        Check(table.Steps.Count == Regex.Matches(tableJson, "\"stage\"\\s*:").Count && table.Steps.Count > 40,
            "every step row parsed (" + table.Steps.Count + ")");
        Check(table.Coverage.Count == Regex.Matches(tableJson, "\"checklist\"\\s*:\\s*\"").Count && table.Coverage.Count > 100,
            "every coverage row parsed (" + table.Coverage.Count + ")");
        Check(table.Stages.Count == Regex.Matches(tableJson, "\"when\"\\s*:").Count, "every stage row parsed");
        Check(Step(table, "SKY_AUTO_REAL_DOCK_PANEL").Actions.Count > 10 && Step(table, "SKY_AUTO_REAL_DOCK_PANEL").Checklist.Contains("2.15.37"),
            "step actions and checklist lists parsed");

        List<string> nullKnown = Validate(table, null, null, null);
        Check(nullKnown.Count == 0, "real table validates with null known sets: " + Dump(nullKnown));

        HashSet<string> cases = KnownCases();
        var offline = new HashSet<string>(StringComparer.Ordinal);
        var missingOffline = new List<string>();
        foreach (F3AutotestCoverageRow row in table.Coverage)
            foreach (string evidence in row.Evidence)
            {
                if (!evidence.StartsWith(F3AutotestJudges.OfflineEvidencePrefix, StringComparison.Ordinal)) continue;
                string path = evidence.Substring(F3AutotestJudges.OfflineEvidencePrefix.Length);
                if (File.Exists(RepoPath(path)) || Directory.Exists(RepoPath(path))) offline.Add(path);
                else if (!missingOffline.Contains(path)) missingOffline.Add(path);
            }
        Check(offline.Count > 5, "offline evidence set is not vacuous (" + offline.Count + ")");
        Check(missingOffline.Count == 0, "every offline: evidence path exists in the repo, missing=" + Dump(missingOffline));
        List<string> realKnown = Validate(table, cases, null, offline);
        Check(realKnown.Count == 0, "real table validates against known cases and existing offline evidence: " + Dump(realKnown));
        var checklist = new HashSet<string>(StringComparer.Ordinal);
        foreach (F3AutotestCoverageRow row in table.Coverage) checklist.Add(row.Checklist);
        List<string> withChecklist = Validate(table, cases, checklist, offline);
        Check(withChecklist.Count == 0, "real table validates with checklist = coverage ids: " + Dump(withChecklist));

        TableChecklistIdCases();
        TableRedSamples(cases, checklist, offline);
    }

    private static void TableChecklistIdCases()
    {
        foreach (string good in new[] { "2.10.1", "2.15.7a", "2.16.D5", "2.14.30", "M_SKY_ISLAND_01", "M_SKY_ISLAND_14" })
            Check(F3AutotestJudges.IsChecklistId(good), "checklist id accepted: " + good);
        foreach (string bad in new[] { "2.10", "3.10.1", "2.10.D1", "2.15.7ab", "2.15.a", "M_SKY_ISLAND_1", "M_SKY_ISLAND_00", "M_SKY_ISLAND_1A", "", null })
            Check(!F3AutotestJudges.IsChecklistId(bad), "checklist id rejected: " + (bad ?? "null"));
    }

    /// <summary>改表对象（或 JSON 文本）后，期望的那条错误必须出现；基线里没有它。</summary>
    private static void Red(string name, Action<F3AutotestTable> mutate, string expectedPrefix,
        ICollection<string> cases, ICollection<string> checklist, ICollection<string> offline)
    {
        F3AutotestTable baseline = FreshTable();
        Check(!AnyStartsWith(Validate(baseline, cases, checklist, offline), expectedPrefix), "red baseline lacks " + expectedPrefix + " (" + name + ")");
        F3AutotestTable table = FreshTable();
        mutate(table);
        List<string> errors = Validate(table, cases, checklist, offline);
        Check(AnyStartsWith(errors, expectedPrefix), "red: " + name + " -> " + expectedPrefix + " got " + Dump(errors));
    }

    private static void TableRedSamples(HashSet<string> cases, HashSet<string> checklist, HashSet<string> offline)
    {
        Red("unknown verb", t => Step(t, "SKY_AUTO_REAL_LANTERN").Actions.Add("teleprot:PlayerSpawn:0:0"), "step_action_unknown:SKY_AUTO_REAL_LANTERN", cases, null, offline);
        Red("unknown assert name", t => Step(t, "SKY_AUTO_REAL_LANTERN").Actions.Add("assert:no_such_assert"), "step_assert_unknown:SKY_AUTO_REAL_LANTERN", cases, null, offline);
        Red("assert:case unknown id", t => Step(t, "SKY_AUTO_STORM_KEEPSAKES").Actions.Add("assert:case:SKY_NOT_A_CASE"), "step_assert_case_unknown:SKY_AUTO_STORM_KEEPSAKES", cases, null, offline);
        Red("assert:case without id", t => Step(t, "SKY_AUTO_STORM_KEEPSAKES").Actions.Add("assert:case"), "step_assert_case_unknown:SKY_AUTO_STORM_KEEPSAKES", null, null, null);
        // 只剩不自带判定的动作（瞬移、等待）：没有断言也没有截图。
        Red("step without assert or shot", t =>
        {
            F3AutotestStep step = Step(t, "SKY_AUTO_REAL_LANTERN");
            step.Actions.Clear();
            step.Actions.Add("teleport:Search_A:1:0");
            step.Actions.Add("wait_real:1");
        }, "step_without_assert_or_shot:SKY_AUTO_REAL_LANTERN", cases, null, offline);
        // 绿样本：自带判定的动作（use_buff）单独成步不算「无断言」。
        F3AutotestTable asserting = FreshTable();
        F3AutotestStep charm = Step(asserting, "SKY_AUTO_REAL_LANTERN");
        charm.Actions.Clear();
        charm.Actions.Add("use_buff:Charm");
        List<string> assertingErrors = Validate(asserting, cases, null, offline);
        Check(!AnyStartsWith(assertingErrors, "step_without_assert_or_shot:") && assertingErrors.Count == 0,
            "green: a step with only use_buff:Charm counts as asserting: " + Dump(assertingErrors));
        Red("shot-class step without shot", t =>
        {
            F3AutotestStep step = Step(t, "SKY_AUTO_BEACONS_MAP");
            step.Actions.Clear();
            step.Actions.Add("open_map");
            step.Actions.Add("assert:view_open");
            step.Actions.Add("close_view");
        }, "shot_step_without_shot:SKY_AUTO_BEACONS_MAP", cases, null, offline);
        Red("malformed checklist id on a step", t => Step(t, "SKY_AUTO_REAL_CHARM").Checklist.Add("2.11"), "step_checklist_id_malformed:SKY_AUTO_REAL_CHARM=2.11", cases, null, offline);
        Red("malformed checklist id on coverage", t => Row(t, "2.11.12").Checklist = "2.11.12xy", "coverage_id_malformed:2.11.12xy", cases, null, offline);
        Red("unknown checklist id with knownChecklist", t => Step(t, "SKY_AUTO_REAL_CHARM").Checklist.Add("2.11.99"), "step_checklist_id_unknown:SKY_AUTO_REAL_CHARM=2.11.99", cases, checklist, offline);
        Red("duplicate coverage row", t =>
        {
            var copy = new F3AutotestCoverageRow { Checklist = "2.11.12", Class = "auto" };
            copy.Evidence.Add("SKY_AUTO_REAL_CHARM");
            t.Coverage.Add(copy);
        }, "coverage_id_duplicate:2.11.12", cases, null, offline);
        Red("manual reason without feel/sound/fun", t => Row(t, "2.13.6").Reason = "要站到桥上看一眼", "coverage_manual_reason_must_be_feel_sound_or_fun:2.13.6", cases, null, offline);
        Red("offline evidence on a shot row", t => Row(t, "2.10.3").Evidence.Add("offline:tests/fixtures/SkyIslandStory"), "coverage_offline_evidence_only_on_auto_rows:2.10.3", cases, null, offline);
        Red("shot row whose steps take no shot", t =>
        {
            F3AutotestCoverageRow row = Row(t, "2.10.8");
            row.Evidence.Clear();
            row.Evidence.Add("SKY_AUTO_REAL_CHARM");
        }, "coverage_shot_row_without_shot_step:2.10.8", cases, null, offline);
        Red("first story stage does not start with reset", t => StoryStage(t, "new_save").Apply.Insert(0, "clear:D"), "first_story_stage_must_start_with_reset:new_save", cases, null, offline);
        Red("lamp that is not registered", t => StoryStage(t, "ending_lamps").Apply.Add("lamp:Light_Z9"), "stage_op_invalid:ending_lamps:lamp_not_registered:Light_Z9", cases, null, offline);
        Red("story action that does not exist", t => StoryStage(t, "ending").Apply.Add("story:RingTheWrongBell"), "stage_op_invalid:ending:story_action_unknown:RingTheWrongBell", cases, null, offline);
        Red("budget over 600 seconds", t => Step(t, "SKY_AUTO_REAL_CHARM").BudgetSeconds = 601f, "step_budget_out_of_range:SKY_AUTO_REAL_CHARM", cases, null, offline);
        Red("step id without SKY_AUTO_ prefix", t => Step(t, "SKY_AUTO_REAL_CHARM").Id = "SKY_REAL_CHARM", "step_id_invalid_or_duplicate:SKY_REAL_CHARM", cases, null, offline);
        Red("offline evidence path unknown", t => Row(t, "2.11.4").Evidence.Add("offline:tests/NoSuchGuard.py"), "coverage_evidence_offline_unknown:2.11.4", cases, null, offline);

        // JSON 文本层：版本、缺覆盖表、读不动。
        var errors = new List<string>();
        F3AutotestJudges.ParseTable(tableJson.Replace("\"version\": 1", "\"version\": 2"), errors);
        Check(errors.Contains("step_table_version_must_be_1"), "red: table version 2 rejected: " + Dump(errors));
        errors.Clear();
        F3AutotestJudges.ParseTable(tableJson.Replace("\"coverage\":", "\"coverage_moved\":"), errors);
        Check(errors.Contains("step_table_missing_coverage"), "red: missing coverage array rejected: " + Dump(errors));
        errors.Clear();
        Check(F3AutotestJudges.ParseTable(tableJson.Substring(0, tableJson.Length / 2), errors) == null && AnyStartsWith(errors, "step_table_unparsable:"),
            "red: truncated table JSON is unparsable: " + Dump(errors));
    }
}
