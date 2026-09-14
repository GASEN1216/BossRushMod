using System;
using System.Collections.Generic;
using BossRush;

/// <summary>6. 一步的结论、截图编码分档、manifest / summary 结构、整轮状态。</summary>
internal static partial class Program
{
    private static F3AutotestAssertion Assertion(string name, string result, string reason, string metrics)
    {
        return new F3AutotestAssertion { Name = name, Result = result, Reason = reason, Metrics = metrics };
    }

    private static F3AutotestStepRecord StepRecord(string id, string result, string reason, string checklist, params F3AutotestAssertion[] assertions)
    {
        var record = new F3AutotestStepRecord
        {
            Id = id, Title = "标题 " + id, Stage = "real", Location = "Search_A", Class = "auto", Language = "zh",
            Result = result, Reason = reason, StartedUtc = "2026-09-14T10:15:00Z", DurationMs = 1500
        };
        record.Checklist.Add(checklist);
        record.Actions.Add("assert:case:SKY_STORY_CODEC");
        record.Assertions.AddRange(assertions);
        return record;
    }

    private static List<F3AutotestStepRecord> Records(params string[] results)
    {
        var list = new List<F3AutotestStepRecord>();
        for (int i = 0; i < results.Length; i++) list.Add(new F3AutotestStepRecord { Id = "SKY_AUTO_" + i, Result = results[i] });
        return list;
    }

    private static F3AutotestCoverageRow CoverageRow(string checklist, string cls, string reason, params string[] evidence)
    {
        var row = new F3AutotestCoverageRow { Checklist = checklist, Class = cls, Reason = reason };
        row.Evidence.AddRange(evidence);
        return row;
    }

    private static string JsonText(BossRushJsonValue obj, string name)
    {
        BossRushJsonValue value = obj == null ? null : obj.GetProperty(name);
        return value == null || value.Kind != BossRushJsonKind.String ? null : value.StringValue;
    }

    private static bool SameList(BossRushJsonValue obj, string name, IList<string> expected)
    {
        List<string> values;
        if (obj == null || !obj.TryGetStringList(name, out values) || values.Count != expected.Count) return false;
        for (int i = 0; i < values.Count; i++) if (values[i] != expected[i]) return false;
        return true;
    }

    private static void ManifestCases()
    {
        ResultCases();
        var pass = StepRecord("SKY_AUTO_A", "PASS", null, "2.10.1", Assertion("case:SKY_STORY_CODEC", "PASS", null, "a=1"));
        pass.Shots.Add(new F3AutotestShot { Name = "hud", File = "shots/hud.png", Kind = "ui", Encoding = "png", Bytes = 12345, Metrics = "contrast=4.8" });
        pass.Notes.Add("备注 \"引号\"\n第二行");
        var fail = StepRecord("SKY_AUTO_B", "FAIL", "assert_failed", "2.10.2", Assertion("contrast_min:X", "FAIL", "contrast_below_min", "measured=2.1|min=4.5"));
        var skip = StepRecord("SKY_AUTO_C", "SKIP", "all_skipped", "2.10.5", Assertion("visible_min:ring", "SKIP", "target_not_on_screen", null));
        var steps = new List<F3AutotestStepRecord> { pass, fail, skip };
        var table = new F3AutotestTable { Version = 1 };
        table.Coverage.Add(CoverageRow("2.10.1", "auto", null, "SKY_AUTO_A", "SKY_CASE_OK"));
        table.Coverage.Add(CoverageRow("2.10.2", "shot", "AI 看截图", "SKY_AUTO_B"));
        table.Coverage.Add(CoverageRow("2.10.3", "manual", "手感 | 声音"));
        table.Coverage.Add(CoverageRow("2.10.4", "auto", null, "SKY_AUTO_A", "SKY_AUTO_MISSING"));
        table.Coverage.Add(CoverageRow("2.10.5", "auto", null, "SKY_AUTO_C"));
        table.Coverage.Add(CoverageRow("2.10.6", "auto", null, "SKY_CASE_BAD"));
        string[] expectedStatus = { "PASS", "FAIL", "MANUAL", "NOT_RUN", "SKIP", "FAIL" };
        var outcomes = new Dictionary<string, string> { { "SKY_CASE_OK", "PASS" }, { "SKY_CASE_BAD", "FAIL" } };
        var info = new F3AutotestRunInfo
        {
            RunId = "autotest-1", Slot = 3, Mvid = "0f3c9d2e", Language = "zh", AltLanguage = "en", StartedUtc = "2026-09-14T10:00:00Z",
            EndedUtc = "2026-09-14T11:00:00Z", ReportLog = "BossRushValidation_1.log", RestoreStory = "PASS", RestoreDetail = "readback ok",
            ItemLedger = "reclaimed 3", MoneyLedger = "+0", EnvironmentRestore = "PASS", ShotBytes = 5000000000L, ShotCount = 1
        };
        info.Status = F3AutotestJudges.RunStatus(true, false, false, steps, 3);
        Check(info.Status == "FAIL", "run with a red step is FAIL");

        BossRushJsonValue root;
        string error;
        string manifest = F3AutotestJudges.RenderManifest(info, steps, table, outcomes);
        Check(BossRushJsonParser.TryParse(manifest, out root, out error), "manifest parses back: " + error);
        if (root == null) return;
        Check(JsonText(root, "schema") == F3AutotestJudges.ManifestSchema, "manifest schema is " + F3AutotestJudges.ManifestSchema);
        var header = new Dictionary<string, string>
        {
            { "runId", info.RunId }, { "mvid", info.Mvid }, { "language", info.Language }, { "altLanguage", info.AltLanguage },
            { "startedUtc", info.StartedUtc }, { "endedUtc", info.EndedUtc }, { "status", "FAIL" }, { "reportLog", info.ReportLog },
            { "steamScreenshots", F3AutotestJudges.SteamScreenshotNote }
        };
        foreach (KeyValuePair<string, string> field in header) Check(JsonText(root, field.Key) == field.Value, "manifest header " + field.Key);
        int slot;
        Check(root.TryGetInt("slot", out slot) && slot == 3, "manifest header slot");
        BossRushJsonValue restore = root.GetObject("restore"), shots = root.GetObject("shots");
        Check(restore != null && JsonText(restore, "story") == "PASS" && JsonText(restore, "detail") == "readback ok" && JsonText(restore, "environment") == "PASS"
            && JsonText(restore, "items") == "reclaimed 3" && JsonText(restore, "money") == "+0", "manifest restore block");
        long bytes, budget;
        int count;
        Check(shots != null && shots.TryGetLong("bytes", out bytes) && bytes == 5000000000L && shots.TryGetLong("budgetBytes", out budget)
            && budget == F3AutotestJudges.ShotBudgetBytes && shots.TryGetInt("count", out count) && count == 1, "manifest shots block");

        List<BossRushJsonValue> stepRows, coverageRows;
        Check(root.TryGetArray("steps", out stepRows) && stepRows.Count == steps.Count, "manifest has one row per step");
        for (int i = 0; stepRows != null && i < stepRows.Count && i < steps.Count; i++) ManifestStepMatches(stepRows[i], steps[i]);
        Check(root.TryGetArray("coverage", out coverageRows) && coverageRows.Count == table.Coverage.Count, "manifest has one row per coverage line");
        for (int i = 0; coverageRows != null && i < coverageRows.Count && i < table.Coverage.Count; i++)
        {
            F3AutotestCoverageRow row = table.Coverage[i];
            BossRushJsonValue json = coverageRows[i];
            Check(JsonText(json, "checklist") == row.Checklist && JsonText(json, "class") == row.Class && JsonText(json, "reason") == row.Reason
                && SameList(json, "evidence", row.Evidence), "coverage row " + row.Checklist + " fields");
            Check(JsonText(json, "status") == expectedStatus[i], "coverage row " + row.Checklist + " status " + expectedStatus[i] + " got " + JsonText(json, "status"));
        }
        BossRushJsonValue noTable = BossRushJsonParser.ParseOrNull(F3AutotestJudges.RenderManifest(info, steps.GetRange(0, 2), null, null));
        Check(noTable != null && noTable.GetArray("coverage").Count == 0 && noTable.GetArray("steps").Count == 2, "manifest without a table has no coverage rows and follows the step list");

        SummaryCases(info, steps, table, outcomes);
    }

    private static void ManifestStepMatches(BossRushJsonValue json, F3AutotestStepRecord step)
    {
        long duration;
        Check(JsonText(json, "id") == step.Id && JsonText(json, "title") == step.Title && JsonText(json, "result") == step.Result
            && JsonText(json, "reason") == step.Reason && JsonText(json, "stage") == step.Stage && JsonText(json, "location") == step.Location
            && JsonText(json, "class") == step.Class && JsonText(json, "language") == step.Language && JsonText(json, "startedUtc") == step.StartedUtc
            && json.TryGetLong("durationMs", out duration) && duration == step.DurationMs, "manifest step " + step.Id + " header fields");
        Check(SameList(json, "checklist", step.Checklist) && SameList(json, "actions", step.Actions) && SameList(json, "notes", step.Notes), "manifest step " + step.Id + " lists");
        List<BossRushJsonValue> assertions = json.GetArray("assertions"), shots = json.GetArray("shots");
        bool same = assertions.Count == step.Assertions.Count && shots.Count == step.Shots.Count;
        for (int i = 0; same && i < assertions.Count; i++)
            same = JsonText(assertions[i], "name") == step.Assertions[i].Name && JsonText(assertions[i], "result") == step.Assertions[i].Result
                && JsonText(assertions[i], "reason") == step.Assertions[i].Reason && JsonText(assertions[i], "metrics") == step.Assertions[i].Metrics;
        for (int i = 0; same && i < shots.Count; i++)
        {
            long bytes;
            same = JsonText(shots[i], "name") == step.Shots[i].Name && JsonText(shots[i], "file") == step.Shots[i].File && JsonText(shots[i], "kind") == step.Shots[i].Kind
                && JsonText(shots[i], "encoding") == step.Shots[i].Encoding && shots[i].TryGetLong("bytes", out bytes) && bytes == step.Shots[i].Bytes
                && JsonText(shots[i], "metrics") == step.Shots[i].Metrics;
        }
        Check(same, "manifest step " + step.Id + " assertions and shots");
    }

    private static void SummaryCases(F3AutotestRunInfo info, List<F3AutotestStepRecord> steps, F3AutotestTable table, Dictionary<string, string> outcomes)
    {
        const string heading = "# 全自动实机验收 autotest-1";
        string ok = F3AutotestJudges.RenderSummary(info, steps, table, outcomes);
        Check(ok.StartsWith(heading, StringComparison.Ordinal) && !ok.Contains("❌") && !ok.Contains("⚠️"), "restore PASS: summary opens with the heading, no restore banner");
        Check(ok.Contains("PASS=1 FAIL=1 SKIP=1") && ok.Contains("- `SKY_AUTO_B`（2.10.2）：assert_failed")
            && ok.Contains("  - contrast_min:X：contrast_below_min | measured=2.1|min=4.5") && ok.Contains("- 用例 `SKY_CASE_BAD` FAIL"), "summary lists counts and red items");
        Check(ok.Contains("自动断言 4 行 · 截图待 AI 看 1 行 · 只能人工 1 行。") && ok.Contains("| 2.10.4 | 自动断言 | NOT_RUN |")
            && ok.Contains("| 2.10.3 | 只能人工 | MANUAL | 手感 / 声音 |"), "summary coverage table with statuses and escaped pipes");

        info.RestoreStory = "FAIL";
        info.RestoreDetail = "story_readback_mismatch";
        string failed = F3AutotestJudges.RenderSummary(info, steps, table, outcomes);
        int banner = failed.IndexOf("> ❌", StringComparison.Ordinal), title = failed.IndexOf(heading, StringComparison.Ordinal);
        Check(banner == 0 && title > banner && failed.Contains("FAIL**（story_readback_mismatch）") && failed.Contains("BossRush_Validation_AutotestSnapshot_v1"),
            "red: restore FAIL puts the red banner first, before the heading");
        info.RestoreStory = "PENDING_NEXT_BASE";
        Check(F3AutotestJudges.RenderSummary(info, steps, table, outcomes).StartsWith("> ❌", StringComparison.Ordinal), "restore PENDING also raises the banner");
        info.RestoreStory = "NOT_NEEDED";
        info.EnvironmentRestore = "FAIL:language";
        string env = F3AutotestJudges.RenderSummary(info, steps, table, outcomes);
        Check(!env.Contains("❌") && env.StartsWith("> ⚠️ 环境还原", StringComparison.Ordinal) && env.IndexOf(heading, StringComparison.Ordinal) > 0,
            "environment restore failure is a warning above the heading, restore NOT_NEEDED has no red banner");
        info.RestoreStory = "PASS";
        info.EnvironmentRestore = "NOT_NEEDED";
        string green = F3AutotestJudges.RenderSummary(info, Records("PASS", "SKIP"), null, null);
        Check(green.StartsWith(heading, StringComparison.Ordinal) && green.Contains("- 无"), "no red steps or cases: red section says none");

        foreach (string attention in new[] { "FAIL", "NOT_RUN", "PENDING", "PENDING_NEXT_BASE" })
            Check(F3AutotestJudges.RestoreNeedsAttention(attention), "restore needs attention: " + attention);
        foreach (string quiet in new[] { "PASS", "NOT_NEEDED", "", null })
            Check(!F3AutotestJudges.RestoreNeedsAttention(quiet), "restore needs no attention: " + (quiet ?? "null"));
        Check(new F3AutotestRunInfo().RestoreStory == "NOT_RUN" && F3AutotestJudges.RestoreNeedsAttention(new F3AutotestRunInfo().RestoreStory),
            "a run that never reached restore is flagged by default");

        Check(F3AutotestJudges.RunStatus(false, true, true, Records("FAIL"), 1) == "RESTORE_FAILED", "run status: restore failure outranks everything");
        Check(F3AutotestJudges.RunStatus(false, false, false, Records("PASS"), 1) == "RESTORE_FAILED", "run status: restore failure on an otherwise green run");
        Check(F3AutotestJudges.RunStatus(true, true, true, Records("FAIL"), 1) == "CANCELLED", "run status: cancelled outranks aborted and red");
        Check(F3AutotestJudges.RunStatus(true, false, true, Records("FAIL"), 1) == "ABORTED", "run status: aborted outranks red");
        Check(F3AutotestJudges.RunStatus(true, false, false, Records("PASS", "FAIL"), 5) == "FAIL", "run status: red outranks incomplete");
        Check(F3AutotestJudges.RunStatus(true, false, false, Records("PASS", "PASS"), 3) == "INCOMPLETE", "run status: missing steps are INCOMPLETE");
        Check(F3AutotestJudges.RunStatus(true, false, false, Records("PASS", "SKIP", "PASS"), 3) == "PASS", "run status: all steps present and none red is PASS");
        Check(F3AutotestJudges.RunStatus(true, false, false, Records("SKIP", "SKIP"), 2) == "INCOMPLETE", "run status: every step skipped is INCOMPLETE, not PASS");
    }

    private static void ResultCases()
    {
        F3AutotestAssertion p = Assertion("a", "PASS", null, null), f = Assertion("b", "FAIL", "x", null), s = Assertion("c", "SKIP", "y", null);
        Check(F3AutotestJudges.AggregateResult(new[] { p }, 0, true) == "FAIL", "step result: an errored step is FAIL even with passing assertions");
        Check(F3AutotestJudges.AggregateResult(new[] { p, f, s }, 2, false) == "FAIL", "step result: any FAIL assertion is FAIL");
        Check(F3AutotestJudges.AggregateResult(new[] { s, p }, 0, false) == "PASS", "step result: a PASS assertion is PASS");
        Check(F3AutotestJudges.AggregateResult(new[] { s, s }, 0, false) == "SKIP", "step result: only SKIP and no screenshot is SKIP");
        Check(F3AutotestJudges.AggregateResult(new F3AutotestAssertion[0], 0, false) == "SKIP", "step result: nothing evaluated is SKIP");
        Check(F3AutotestJudges.AggregateResult(new F3AutotestAssertion[0], 1, false) == "PASS", "step result: a written screenshot alone is PASS");
        Check(F3AutotestJudges.AggregateResult(new[] { s }, 1, false) == "SKIP", "step result: SKIP assertions plus a written screenshot stay SKIP (a shot must not hide an unjudged check)");

        // 清单行状态：离线证据这一轮在游戏里看不到结论，不参与取最差；只有离线证据的行是 OFFLINE。
        var offlineOnly = new F3AutotestCoverageRow { Checklist = "2.11.4", Class = "auto" };
        offlineOnly.Evidence.Add("offline:tests/SkyIslandFieldcraftGuard.py");
        Check(F3AutotestJudges.CoverageStatus(offlineOnly, new System.Collections.Generic.List<F3AutotestStepRecord>(), null) == "OFFLINE",
            "coverage: a row backed only by offline evidence is OFFLINE, not NOT_RUN");
        var mixed = new F3AutotestCoverageRow { Checklist = "2.11.11", Class = "auto" };
        mixed.Evidence.Add("SKY_AUTO_REAL_LANTERN");
        mixed.Evidence.Add("offline:tests/SkyIslandFieldcraftGuard.py");
        var lanternPassed = new System.Collections.Generic.List<F3AutotestStepRecord> { new F3AutotestStepRecord { Id = "SKY_AUTO_REAL_LANTERN", Result = "PASS" } };
        Check(F3AutotestJudges.CoverageStatus(mixed, lanternPassed, null) == "PASS", "coverage: offline evidence does not drag an observed PASS down to NOT_RUN");
        Check(F3AutotestJudges.CoverageStatus(mixed, new System.Collections.Generic.List<F3AutotestStepRecord>(), null) == "NOT_RUN",
            "red: the observed step that never ran is still NOT_RUN");

        // note: 推进动作与生产 RecordNote 同一份登记表：登记过的信收得下，没登记的在读表时就拒。
        F3AutotestStageOp noteOp;
        string noteError, noteReason;
        SkyIslandStoryData noted;
        Check(F3AutotestJudges.TryParseStageOp("note:Letter_05", out noteOp, out noteError)
            && F3AutotestJudges.ApplyOp(SkyIslandStoryRules.CreateDefault(), noteOp, out noted, out noteReason)
            && System.Array.IndexOf(noted.discoveredNotes, "Letter_05") >= 0, "stage op: a registered letter note parses and applies");
        Check(!F3AutotestJudges.TryParseStageOp("note:Letter_99", out noteOp, out noteError) && noteError == "note_not_registered:Letter_99",
            "red: an unregistered note id is rejected when the table is read -> " + noteError);

        long budget = F3AutotestJudges.ShotBudgetBytes;
        Check(budget == 300L * 1024L * 1024L, "shot budget is 300 MiB");
        Check(F3AutotestJudges.ChooseEncoding(0, true, 0) == "skip" && F3AutotestJudges.ChooseEncoding(0, false, -1) == "skip", "encoding: no budget writes nothing");
        Check(F3AutotestJudges.ChooseEncoding(0, true, budget) == "png" && F3AutotestJudges.ChooseEncoding(0, false, budget) == "jpg", "encoding: empty budget, UI png / world jpg");
        Check(F3AutotestJudges.ChooseEncoding(budget * 6 / 10 - 1, true, budget) == "png", "encoding: just under 60% UI stays png");
        Check(F3AutotestJudges.ChooseEncoding(budget * 6 / 10, true, budget) == "jpg" && F3AutotestJudges.ChooseEncoding(budget * 6 / 10, false, budget) == "jpg", "encoding: at 60% everything is jpg");
        Check(F3AutotestJudges.ChooseEncoding(budget * 9 / 10 - 1, true, budget) == "jpg", "encoding: just under 90% is jpg");
        Check(F3AutotestJudges.ChooseEncoding(budget * 9 / 10, true, budget) == "jpg_half" && F3AutotestJudges.ChooseEncoding(budget - 1, false, budget) == "jpg_half", "encoding: 90% up to full is jpg_half");
        Check(F3AutotestJudges.ChooseEncoding(budget, true, budget) == "skip" && F3AutotestJudges.ChooseEncoding(budget + 1, false, budget) == "skip", "encoding: full budget only analyses");

        Check(F3AutotestJudges.LogLine("SKY_AUTO_A", "end", "PASS") == "[AUTOTEST] step=SKY_AUTO_A end result=PASS"
            && F3AutotestJudges.LogLine(null, "begin", null) == "[AUTOTEST] step=? begin", "log line format");

        string metrics, reason;
        var recorded = new HashSet<string> { "a", "b" };
        var none = new HashSet<string>();
        Check(F3AutotestJudges.JudgeOfficialNotesAfterAutotest(recorded, new HashSet<string> { "a", "b", "c" }, new HashSet<string> { "c" }, none, out metrics, out reason),
            "official notes: entries lit before the run are explained");
        Check(F3AutotestJudges.JudgeOfficialNotesAfterAutotest(recorded, new HashSet<string> { "a", "b", "d" }, none, new HashSet<string> { "d" }, out metrics, out reason),
            "official notes: entries lit during the run are explained");
        Check(!F3AutotestJudges.JudgeOfficialNotesAfterAutotest(recorded, new HashSet<string> { "a", "b", "d" }, none, none, out metrics, out reason)
            && reason == "official_mirror_mismatch_after_restore" && metrics.Contains("unexplained_extra=d"), "red: an unexplained official entry");
        Check(!F3AutotestJudges.JudgeOfficialNotesAfterAutotest(recorded, new HashSet<string> { "a" }, none, none, out metrics, out reason) && metrics.Contains("missing=b"),
            "red: a recorded entry missing from the official index");
    }
}
