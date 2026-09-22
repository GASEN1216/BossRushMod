#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationAutotestJudges.cs - 全自动实机验收的纯判据（Dev 构建）
// ============================================================================
// 为什么判据与 Models 独立成文件、而且不许引用 Unity：
//   全自动实机验收（F3GameplayValidationAutotest*.cs）在游戏里跑，AI 事后读结果目录下结论。能离线证明的部分——
//   步骤表读得对不对、剧情阶段按规则推进后编解码往返是否不变形、线性色彩空间的对比度与可见度怎么算、
//   manifest / summary 的结构——全部收在这里，由 tests/fixtures/F3AutotestJudges 整份编译执行，
//   游戏侧只负责取像素、读场景、调生产入口。这一区一旦引用 Unity，执行回归就编不过。
//
// 纪律：
// - 整份 #if BOSSRUSH_DEV：正式构建的 DLL 里没有这些标识（tools/check_dll_identifiers.py 构建后实查）。
// - 步骤表是数据（Assets/Data/SkyIslandAutotest.json），这里只认它的形状；动作与断言的名字只有这里一份，
//   游戏侧的分派与守卫 tests/SkyIslandAutotestTableGuard.py 都读这两张名单。
// - 对比度一律在线性光里合成（游戏是 Linear 色彩空间，2026-09-14 UI 审核 F-01）：像素先按 sRGB 反解到线性，
//   再算相对亮度 Y 与 WCAG 比值。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BossRush
{
    internal static class F3AutotestJudges
    {
        internal const string TableFile = "SkyIslandAutotest.json";
        internal const string ManifestSchema = "bossrush-autotest-manifest/1";
        /// <summary>单轮截图磁盘预算（≤300 MB）。UI 截图 PNG；超过六成改 JPG，超过九成降采样，满了只分析不落盘。</summary>
        internal const long ShotBudgetBytes = 300L * 1024L * 1024L;
        internal const string SteamScreenshotNote =
            "not_used: Steam 截图依赖 overlay 已初始化、文件落在 Steam userdata 而不在结果目录、进程内拿不到像素做分析；与官方拍照模式一致改用 UnityEngine.ScreenCapture";

        internal static readonly string[] StageWhens = { "base_before", "landing", "real", "story", "alt_language", "base_after" };

        /// <summary>动作动词。游戏侧分派（F3GameplayValidationAutotestActions.cs）与步骤表守卫都只认这一份。</summary>
        internal static readonly string[] ActionVerbs =
        {
            "teleport", "wait_real", "interact", "wait_panel", "wait_dialogue", "wait_dialogue_typed", "dialogue_advance", "dialogue_choose",
            "choose", "choose_label", "hover", "close_panel", "clear_nearby", "kill_nearby", "wait_quiet", "invincible",
            "night", "give", "give_if_missing", "use_buff", "use_item", "use_compass", "set_health", "spawn_gnats", "echo_hurt",
            "wait_object", "wait_alpha", "caption", "wait_caption", "frame", "loot", "puzzle_solve", "open_map", "close_view",
            "click_close", "open_modeg_confirm", "close_modeg_confirm", "reachability", "encounter_cap", "wait_boss", "boss_hurt",
            "loot_boss", "approach_boss", "feed_shots", "reset_encounter", "teleport_view", "ring_replay", "shot", "burst", "assert",
        };

        /// <summary>
        /// 自带判定的动作：执行时一定往这一步记一条 PASS / FAIL / SKIP（用耗材、罗盘读数、等字幕、帧时间采样、读箱子、解谜、
        /// 两条岛内用例、开 Mode G 确认页）。「每步至少一条断言或一张截图」把它们算作断言。只在失败时才记的动作（瞬移、交互）不在此列。
        /// </summary>
        internal static readonly string[] AssertingVerbs =
        {
            "use_buff", "use_item", "use_compass", "wait_caption", "frame", "loot", "puzzle_solve", "reachability", "encounter_cap",
            "open_modeg_confirm", "wait_boss", "loot_boss",
        };

        /// <summary>断言名。<c>assert:名字[:参数…]</c>。</summary>
        internal static readonly string[] AssertNames =
        {
            "panel_open", "panel_closed", "choice_present", "choice_absent", "choice_count_le", "body_contains", "body_absent",
            "dialogue_active", "dialogue_inactive", "dialogue_line_contains", "objective_contains", "caption_contains", "case", "object_present",
            "object_absent", "pack_count_ge", "pack_delta", "pack_delta_ge", "crate_has", "crate_has_any", "flag", "no_flag", "note",
            "alpha_le", "killed_ge", "near",
            "stage_data", "contrast_min", "row_contrast_min", "overflow_none", "visible_min", "profile_ok", "quiet",
            "health_ge", "color_equals", "chilled", "wind_gale", "extraction_open", "echo_starts", "gnats_alive_ge",
            "language_is", "view_open", "log_contains", "prev_log_quit", "object_static", "boss_alive",
        };

        /// <summary>
        /// 覆盖表里「自动断言」行允许引用的离线证据前缀：<c>offline:tests/某守卫.py</c>、<c>offline:tests/fixtures/某夹具</c>、
        /// <c>offline:tools/某脚本.py</c>。只有进程内造不出来的前提（跨出击、退游戏进程、改键）才用它，守卫核对路径真的存在。
        /// </summary>
        internal const string OfflineEvidencePrefix = "offline:";

        internal static readonly string[] CoverageClasses = { "auto", "shot", "manual" };

        /// <summary>「只能人工」只留手感、声音、好不好玩：理由里必须写明是其中哪一类。</summary>
        internal static readonly string[] ManualReasonKeywords = { "手感", "声音", "好不好玩" };

        #region 步骤表

        internal static F3AutotestTable ParseTable(string json, List<string> errors)
        {
            BossRushJsonValue root;
            string error;
            if (!BossRushJsonParser.TryParse(json, out root, out error) || root == null)
            {
                errors.Add("step_table_unparsable:" + error);
                return null;
            }
            var table = new F3AutotestTable();
            if (!root.TryGetInt("version", out table.Version) || table.Version != 1) errors.Add("step_table_version_must_be_1");
            List<BossRushJsonValue> rows;
            if (!root.TryGetArray("stages", out rows) || rows.Count == 0) errors.Add("step_table_missing_stages");
            else
            {
                foreach (BossRushJsonValue row in rows)
                {
                    var stage = new F3AutotestStage { Id = Str(row, "id"), Title = Str(row, "title"), When = Str(row, "when") };
                    List<string> apply;
                    if (row.TryGetStringList("apply", out apply)) stage.Apply.AddRange(apply);
                    table.Stages.Add(stage);
                }
            }
            if (!root.TryGetArray("steps", out rows) || rows.Count == 0) errors.Add("step_table_missing_steps");
            else
            {
                foreach (BossRushJsonValue row in rows)
                {
                    var step = new F3AutotestStep
                    {
                        Id = Str(row, "id"), Title = Str(row, "title"), Stage = Str(row, "stage"),
                        Location = Str(row, "location"), Class = Str(row, "class"),
                        BudgetSeconds = row.GetFloat("budget", 60f)
                    };
                    List<string> values;
                    if (row.TryGetStringList("checklist", out values)) step.Checklist.AddRange(values);
                    if (row.TryGetStringList("actions", out values)) step.Actions.AddRange(values);
                    table.Steps.Add(step);
                }
            }
            if (root.TryGetArray("coverage", out rows))
            {
                foreach (BossRushJsonValue row in rows)
                {
                    var coverage = new F3AutotestCoverageRow { Checklist = Str(row, "checklist"), Class = Str(row, "class"), Reason = Str(row, "reason") };
                    List<string> values;
                    if (row.TryGetStringList("evidence", out values)) coverage.Evidence.AddRange(values);
                    table.Coverage.Add(coverage);
                }
            }
            else errors.Add("step_table_missing_coverage");
            return table;
        }

        private static string Str(BossRushJsonValue row, string key)
        {
            string value;
            return row != null && row.TryGetString(key, out value) ? value : null;
        }

        internal static string VerbOf(string action)
        {
            if (string.IsNullOrEmpty(action)) return string.Empty;
            int colon = action.IndexOf(':');
            return colon < 0 ? action : action.Substring(0, colon);
        }

        /// <summary>动作参数（冒号切开，不含动词）。<c>choose_label:中文|English</c> 这种参数里不许再出现冒号。</summary>
        internal static string[] ArgsOf(string action)
        {
            if (string.IsNullOrEmpty(action)) return new string[0];
            string[] parts = action.Split(':');
            var result = new string[Math.Max(0, parts.Length - 1)];
            Array.Copy(parts, 1, result, 0, result.Length);
            return result;
        }

        internal static bool IsChecklistId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (id.StartsWith("M_SKY_ISLAND_", StringComparison.Ordinal))
            {
                string tail = id.Substring("M_SKY_ISLAND_".Length);
                int n;
                return tail.Length == 2 && int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 1;
            }
            string[] parts = id.Split('.');
            if (parts.Length != 3 || parts[0] != "2") return false;
            int section;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out section)) return false;
            string last = parts[2];
            if (last.Length == 2 && last[0] == 'D' && last[1] >= '1' && last[1] <= '9') return section == 16;
            int digits = 0;
            while (digits < last.Length && char.IsDigit(last[digits])) digits++;
            return digits > 0 && (digits == last.Length || (digits == last.Length - 1 && last[digits] >= 'a' && last[digits] <= 'z'));
        }

        /// <summary>
        /// 步骤表的结构校验。<paramref name="knownCases"/> 是 F3 用例 id（SKY_* 等，覆盖清单里的自动项），
        /// <paramref name="knownChecklist"/> 为 null 时不核对清单编号是否真的存在（清单是 local-only 文档，缺席时守卫记 PARTIAL）。
        /// </summary>
        internal static void ValidateTable(F3AutotestTable table, ICollection<string> knownCases, ICollection<string> knownChecklist,
            ICollection<string> knownOffline, List<string> errors)
        {
            if (table == null) { errors.Add("table_null"); return; }
            var stageIds = new HashSet<string>(StringComparer.Ordinal);
            bool firstStory = true;
            foreach (F3AutotestStage stage in table.Stages)
            {
                if (string.IsNullOrEmpty(stage.Id) || !stageIds.Add(stage.Id)) errors.Add("stage_id_missing_or_duplicate:" + stage.Id);
                if (Array.IndexOf(StageWhens, stage.When) < 0) errors.Add("stage_when_unknown:" + stage.Id + "=" + stage.When);
                if (stage.When != "story" && stage.Apply.Count > 0) errors.Add("stage_apply_only_on_story:" + stage.Id);
                if (stage.When == "story")
                {
                    if (firstStory && (stage.Apply.Count == 0 || stage.Apply[0] != "reset"))
                        errors.Add("first_story_stage_must_start_with_reset:" + stage.Id);
                    firstStory = false;
                    foreach (string op in stage.Apply)
                    {
                        F3AutotestStageOp parsed;
                        string opError;
                        if (!TryParseStageOp(op, out parsed, out opError)) errors.Add("stage_op_invalid:" + stage.Id + ":" + opError);
                    }
                }
            }
            if (firstStory) errors.Add("no_story_stage");
            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (F3AutotestStep step in table.Steps)
            {
                string label = step.Id ?? "(no id)";
                if (string.IsNullOrEmpty(step.Id) || !step.Id.StartsWith("SKY_AUTO_", StringComparison.Ordinal) || !stepIds.Add(step.Id))
                    errors.Add("step_id_invalid_or_duplicate:" + label);
                if (string.IsNullOrEmpty(step.Title)) errors.Add("step_title_missing:" + label);
                if (step.Stage == null || !stageIds.Contains(step.Stage)) errors.Add("step_stage_unknown:" + label + "=" + step.Stage);
                if (step.Class != "auto" && step.Class != "shot") errors.Add("step_class_must_be_auto_or_shot:" + label);
                if (step.BudgetSeconds <= 0f || step.BudgetSeconds > 600f) errors.Add("step_budget_out_of_range:" + label);
                if (step.Checklist.Count == 0) errors.Add("step_without_checklist:" + label);
                foreach (string id in step.Checklist)
                {
                    if (!IsChecklistId(id)) errors.Add("step_checklist_id_malformed:" + label + "=" + id);
                    else if (knownChecklist != null && !knownChecklist.Contains(id)) errors.Add("step_checklist_id_unknown:" + label + "=" + id);
                }
                int asserts = 0, shots = 0;
                foreach (string action in step.Actions)
                {
                    string verb = VerbOf(action);
                    if (Array.IndexOf(ActionVerbs, verb) < 0) { errors.Add("step_action_unknown:" + label + "=" + action); continue; }
                    string[] args = ArgsOf(action);
                    if (verb == "assert")
                    {
                        asserts++;
                        if (args.Length == 0 || Array.IndexOf(AssertNames, args[0]) < 0) errors.Add("step_assert_unknown:" + label + "=" + action);
                        else if (args[0] == "case" && (args.Length < 2 || (knownCases != null && !knownCases.Contains(args[1]))))
                            errors.Add("step_assert_case_unknown:" + label + "=" + action);
                    }
                    else if (verb == "shot" || verb == "burst")
                    {
                        shots++;
                        if (args.Length < 2 || (args[1] != "ui" && args[1] != "world")) errors.Add("step_shot_kind_invalid:" + label + "=" + action);
                    }
                    else if (Array.IndexOf(AssertingVerbs, verb) >= 0) asserts++;
                }
                if (asserts + shots == 0) errors.Add("step_without_assert_or_shot:" + label);
                if (step.Class == "shot" && shots == 0) errors.Add("shot_step_without_shot:" + label);
            }
            foreach (F3AutotestCoverageRow row in table.Coverage)
            {
                string label = row.Checklist ?? "(no id)";
                if (!IsChecklistId(row.Checklist)) errors.Add("coverage_id_malformed:" + label);
                else if (!covered.Add(row.Checklist)) errors.Add("coverage_id_duplicate:" + label);
                else if (knownChecklist != null && !knownChecklist.Contains(row.Checklist)) errors.Add("coverage_id_unknown:" + label);
                if (Array.IndexOf(CoverageClasses, row.Class) < 0) { errors.Add("coverage_class_invalid:" + label); continue; }
                if (row.Class == "manual")
                {
                    if (!MentionsManualReason(row.Reason)) errors.Add("coverage_manual_reason_must_be_feel_sound_or_fun:" + label);
                    continue;
                }
                if (row.Evidence.Count == 0) errors.Add("coverage_without_evidence:" + label);
                foreach (string evidence in row.Evidence)
                {
                    bool isStep = evidence.StartsWith("SKY_AUTO_", StringComparison.Ordinal);
                    bool isOffline = evidence.StartsWith(OfflineEvidencePrefix, StringComparison.Ordinal);
                    if (isStep && !stepIds.Contains(evidence)) errors.Add("coverage_evidence_step_unknown:" + label + "=" + evidence);
                    else if (isOffline)
                    {
                        if (row.Class != "auto") errors.Add("coverage_offline_evidence_only_on_auto_rows:" + label);
                        else if (knownOffline != null && !knownOffline.Contains(evidence.Substring(OfflineEvidencePrefix.Length)))
                            errors.Add("coverage_evidence_offline_unknown:" + label + "=" + evidence);
                    }
                    else if (!isStep && knownCases != null && !knownCases.Contains(evidence))
                        errors.Add("coverage_evidence_case_unknown:" + label + "=" + evidence);
                }
                if (row.Class == "shot")
                {
                    bool hasShot = false;
                    foreach (string evidence in row.Evidence)
                    {
                        F3AutotestStep step = FindStep(table, evidence);
                        if (step != null && CountShots(step) > 0) hasShot = true;
                    }
                    if (!hasShot) errors.Add("coverage_shot_row_without_shot_step:" + label);
                }
            }
            if (knownChecklist != null)
            {
                foreach (string id in knownChecklist)
                    if (!covered.Contains(id)) errors.Add("coverage_row_missing:" + id);
            }
            foreach (F3AutotestStep step in table.Steps)
                foreach (string id in step.Checklist)
                    if (IsChecklistId(id) && !covered.Contains(id)) errors.Add("step_checklist_not_in_coverage:" + step.Id + "=" + id);
        }

        internal static bool MentionsManualReason(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return false;
            for (int i = 0; i < ManualReasonKeywords.Length; i++) if (reason.IndexOf(ManualReasonKeywords[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        internal static F3AutotestStep FindStep(F3AutotestTable table, string id)
        {
            if (table == null) return null;
            for (int i = 0; i < table.Steps.Count; i++) if (string.Equals(table.Steps[i].Id, id, StringComparison.Ordinal)) return table.Steps[i];
            return null;
        }

        internal static int CountShots(F3AutotestStep step)
        {
            int shots = 0;
            foreach (string action in step.Actions)
            {
                string verb = VerbOf(action);
                if (verb == "shot" || verb == "burst") shots++;
            }
            return shots;
        }

        #endregion

        #region 剧情阶段（只走合法动作）

        internal static bool TryParseStageOp(string text, out F3AutotestStageOp op, out string error)
        {
            op = default(F3AutotestStageOp);
            error = null;
            if (text == "reset") { op.Kind = F3AutotestOpKind.Reset; return true; }
            string verb = VerbOf(text);
            string[] args = ArgsOf(text);
            if (args.Length != 1 || string.IsNullOrEmpty(args[0])) { error = "op_argument_missing:" + text; return false; }
            op.Argument = args[0];
            switch (verb)
            {
                case "clear":
                    op.Kind = F3AutotestOpKind.ClearEncounter;
                    return true;
                case "story":
                    op.Kind = F3AutotestOpKind.StoryAction;
                    try
                    {
                        op.Action = (SkyIslandStoryAction)Enum.Parse(typeof(SkyIslandStoryAction), args[0], false);
                        if (!Enum.IsDefined(typeof(SkyIslandStoryAction), op.Action)) { error = "story_action_unknown:" + args[0]; return false; }
                        return true;
                    }
                    catch (ArgumentException) { error = "story_action_unknown:" + args[0]; return false; }
                case "note":
                    op.Kind = F3AutotestOpKind.RecordNote;
                    if (!IsRecordableNote(args[0])) { error = "note_not_registered:" + args[0]; return false; }
                    return true;
                case "lamp":
                    op.Kind = F3AutotestOpKind.LightLamp;
                    if (SkyIslandLights.Find(args[0]) == null) { error = "lamp_not_registered:" + args[0]; return false; }
                    return true;
                default:
                    error = "op_verb_unknown:" + text;
                    return false;
            }
        }

        /// <summary>
        /// 纯规则里推一遍所有 story 阶段：从 <see cref="SkyIslandStoryRules.CreateDefault"/> 起，清场直接记、规则动作走纯函数
        /// <see cref="SkyIslandStoryRules.TryApply"/>、手记只收灯（<see cref="SkyIslandLights.Find"/>）。每个阶段结束做一次编解码往返。
        /// 游戏侧开跑前先跑这一遍：规则上走不通的阶段根本不该拿到存档上去试。返回每个阶段结束时的期望数据（按阶段顺序）。
        /// </summary>
        internal static bool SimulateStages(F3AutotestTable table, List<SkyIslandStoryData> perStage, List<string> errors)
        {
            SkyIslandStoryData data = null;
            bool ok = true;
            foreach (F3AutotestStage stage in table.Stages)
            {
                if (stage.When != "story") continue;
                foreach (string text in stage.Apply)
                {
                    F3AutotestStageOp op;
                    string error;
                    if (!TryParseStageOp(text, out op, out error)) { errors.Add(stage.Id + ":" + error); ok = false; continue; }
                    if (op.Kind == F3AutotestOpKind.Reset) { data = SkyIslandStoryRules.CreateDefault(); continue; }
                    if (data == null) { errors.Add(stage.Id + ":op_before_reset:" + text); ok = false; continue; }
                    string reason;
                    SkyIslandStoryData next;
                    if (!ApplyOp(data, op, out next, out reason)) { errors.Add(stage.Id + ":" + text + ":" + reason); ok = false; continue; }
                    data = next;
                }
                if (data == null) { errors.Add(stage.Id + ":no_data"); ok = false; continue; }
                string codec;
                if (!CodecRoundTrip(data, out codec)) { errors.Add(stage.Id + ":" + codec); ok = false; }
                if (perStage != null) perStage.Add(data.Copy());
            }
            return ok;
        }

        /// <summary>一条合法动作在纯数据上的效果（与游戏侧 SkyIslandStoryService 的同名入口同口径）。</summary>
        internal static bool ApplyOp(SkyIslandStoryData source, F3AutotestStageOp op, out SkyIslandStoryData result, out string reason)
        {
            result = null;
            reason = null;
            switch (op.Kind)
            {
                case F3AutotestOpKind.Reset:
                    result = SkyIslandStoryRules.CreateDefault();
                    return true;
                case F3AutotestOpKind.ClearEncounter:
                    result = source.Copy();
                    if (!result.EncounterCleared(op.Argument))
                    {
                        var cleared = new List<string>(result.clearedEncounters);
                        cleared.Add(op.Argument);
                        result.clearedEncounters = cleared.ToArray();
                    }
                    return true;
                case F3AutotestOpKind.StoryAction:
                    return SkyIslandStoryRules.TryApply(source, op.Action, out result, out reason);
                case F3AutotestOpKind.RecordNote:
                case F3AutotestOpKind.LightLamp:
                    if (op.Kind == F3AutotestOpKind.LightLamp ? SkyIslandLights.Find(op.Argument) == null : !IsRecordableNote(op.Argument))
                    {
                        reason = (op.Kind == F3AutotestOpKind.LightLamp ? "lamp_not_registered:" : "note_not_registered:") + op.Argument;
                        return false;
                    }
                    result = source.Copy();
                    if (Array.IndexOf(result.discoveredNotes, op.Argument) < 0)
                    {
                        var notes = new List<string>(result.discoveredNotes);
                        notes.Add(op.Argument);
                        result.discoveredNotes = notes.ToArray();
                    }
                    return true;
                default:
                    reason = "op_kind_unknown";
                    return false;
            }
        }

        /// <summary>
        /// 手记收得下的 id：与 SkyIslandStoryService.RecordNote 的登记表同口径（信鸽来信、船员名册、纪念品、风晶灯、蛙卵、头目 / 岛主首杀）。
        /// </summary>
        internal static bool IsRecordableNote(string id)
        {
            return !string.IsNullOrEmpty(id) && (SkyIslandLetters.Find(id) != null || SkyIslandCrew.IndexOf(id) >= 0
                || SkyIslandItemRules.FindKeepsake(id) != null || SkyIslandLights.Find(id) != null || SkyIslandMosquitoRules.IsFrogNote(id)
                || SkyIslandBossRules.IsBossNote(id));
        }

        internal static bool CodecRoundTrip(SkyIslandStoryData data, out string reason)
        {
            reason = null;
            string encoded = SkyIslandStoryCodec.Encode(data);
            if (encoded == null) { reason = "codec_encode_null"; return false; }
            SkyIslandStoryData decoded = SkyIslandStoryCodec.Decode(encoded);
            if (decoded == null) { reason = "codec_decode_rejected"; return false; }
            string diff;
            if (!SameStory(data, decoded, out diff)) { reason = "codec_roundtrip_changed:" + diff; return false; }
            return true;
        }

        internal static bool SameStory(SkyIslandStoryData a, SkyIslandStoryData b, out string diff)
        {
            diff = null;
            if (a == null || b == null) { diff = "null"; return a == null && b == null; }
            if (a.flags != b.flags) { diff = "flags " + a.flags + "!=" + b.flags; return false; }
            if (a.visitedRegions != b.visitedRegions) { diff = "regions " + a.visitedRegions + "!=" + b.visitedRegions; return false; }
            if (!SameList(a.clearedEncounters, b.clearedEncounters)) { diff = "cleared"; return false; }
            if (!SameList(a.discoveredNotes, b.discoveredNotes)) { diff = "notes"; return false; }
            return true;
        }

        private static bool SameList(string[] a, string[] b)
        {
            a = a ?? new string[0];
            b = b ?? new string[0];
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>
        /// 游戏侧「阶段到位」的判据：实际数据的剧情位、清场与手记必须**包含**期望（岛上这一趟自己也会记到访、清场、纪念品手记，
        /// 岛上步骤也会合法地多写旗标，例如解开谜题、战胜折翎，所以不要求相等）。多出来的剧情位只写进 metrics 的 extra_flags 给 AI 看，不判红。
        /// </summary>
        internal static bool JudgeStageData(SkyIslandStoryData actual, SkyIslandStoryData expected, out string metrics, out string reason)
        {
            reason = null;
            if (actual == null || expected == null)
            {
                metrics = "actual=" + (actual != null) + ",expected=" + (expected != null);
                reason = "stage_data_missing";
                return false;
            }
            var missing = new List<string>();
            if ((actual.flags & expected.flags) != expected.flags) missing.Add("flags:" + (expected.flags & ~actual.flags));
            foreach (string id in expected.clearedEncounters ?? new string[0])
                if (!actual.EncounterCleared(id)) missing.Add("cleared:" + id);
            foreach (string id in expected.discoveredNotes ?? new string[0])
                if (Array.IndexOf(actual.discoveredNotes ?? new string[0], id) < 0) missing.Add("note:" + id);
            int extraFlags = actual.flags & ~expected.flags;
            metrics = "flags=" + actual.flags + ",expected_flags=" + expected.flags + ",extra_flags=" + extraFlags
                + ",notes=" + (actual.discoveredNotes == null ? 0 : actual.discoveredNotes.Length)
                + ",cleared=" + (actual.clearedEncounters == null ? 0 : actual.clearedEncounters.Length);
            if (missing.Count > 0) reason = "stage_not_reached:" + string.Join("+", missing.ToArray());
            return missing.Count == 0;
        }

        #endregion

        #region 快照编码（崩溃恢复靠它往返不变形）

        internal const int SnapshotVersion = 1;

        internal static string EncodeSnapshot(F3AutotestSnapshotRecord snapshot)
        {
            var w = new BossRushJsonWriter();
            w.BeginObject().Int("version", SnapshotVersion).Str("runId", snapshot.RunId).Int("slot", snapshot.Slot).Bool("rawExists", snapshot.RawExists)
                .Str("raw", snapshot.Raw).Long("money", snapshot.Money).Str("language", snapshot.Language)
                .Bool("forceNight", snapshot.ForceNight).Num("timeScale", snapshot.TimeScale)
                .Bool("bufferCountsIncluded", snapshot.BufferCountsIncluded);
            w.BeginArray("officialUnlocked");
            foreach (string key in snapshot.OfficialUnlocked) w.ItemStr(key);
            w.EndArray().BeginArray("items");
            foreach (KeyValuePair<int, int> pair in snapshot.Items)
                w.BeginObject().Int("typeId", pair.Key).Int("count", pair.Value).EndObject();
            return w.EndArray().EndObject().ToString();
        }

        /// <summary>版本不对、JSON 读不动时返回 null：调用方不拿它还原，也不拿当前状态覆盖它。</summary>
        internal static F3AutotestSnapshotRecord DecodeSnapshot(string json)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(json);
            int version;
            if (root == null || !root.TryGetInt("version", out version) || version != SnapshotVersion) return null;
            var snapshot = new F3AutotestSnapshotRecord
            {
                RunId = root.GetString("runId", string.Empty),
                Slot = root.GetInt("slot", -1),
                RawExists = root.GetBool("rawExists", false),
                Money = root.GetLong("money", 0L),
                Language = root.GetString("language", string.Empty),
                ForceNight = root.GetBool("forceNight", false),
                TimeScale = root.GetFloat("timeScale", 1f),
                BufferCountsIncluded = root.GetBool("bufferCountsIncluded", false),
            };
            string raw;
            snapshot.Raw = root.TryGetString("raw", out raw) ? raw : null;
            List<string> keys;
            if (root.TryGetStringList("officialUnlocked", out keys)) snapshot.OfficialUnlocked.AddRange(keys);
            List<BossRushJsonValue> items;
            if (root.TryGetArray("items", out items))
            {
                foreach (BossRushJsonValue item in items)
                {
                    int typeId, count;
                    if (item != null && item.TryGetInt("typeId", out typeId) && item.TryGetInt("count", out count)) snapshot.Items[typeId] = count;
                }
            }
            return snapshot;
        }

        /// <summary>快照里的剧情：原文存在就按生产编解码读（读不动返回 null，不拿它还原）；原文不存在是新档，按默认值。</summary>
        internal static SkyIslandStoryData SnapshotStory(F3AutotestSnapshotRecord snapshot)
        {
            if (snapshot == null) return null;
            return snapshot.RawExists ? SkyIslandStoryCodec.Decode(snapshot.Raw) : SkyIslandStoryRules.CreateDefault();
        }

        #endregion

        #region 线性色彩空间：对比度与可见度

        /// <summary>sRGB 编码值（0..1）→ 线性光。</summary>
        internal static double SrgbToLinear(double c)
        {
            if (double.IsNaN(c)) return 0.0;
            c = Math.Max(0.0, Math.Min(1.0, c));
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        /// <summary>相对亮度 Y（输入是线性光三通道）。</summary>
        internal static double LuminanceLinear(double r, double g, double b)
        {
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        /// <summary>相对亮度 Y（输入是 sRGB 编码三通道，0..1）。截图像素走这一个。</summary>
        internal static double LuminanceSrgb(double r, double g, double b)
        {
            return LuminanceLinear(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b));
        }

        internal static double ContrastRatio(double a, double b)
        {
            double hi = Math.Max(a, b), lo = Math.Min(a, b);
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>
        /// 声明色（sRGB + alpha）在线性光里压到背景（线性三通道）上之后的亮度。游戏是 Linear 色彩空间，
        /// UI 的 alpha 混合发生在线性光里；旧守卫在 sRGB 值上混合，把大标题眉题算成 5.95:1、线性模型只有 2.13:1（审核 F-01）。
        /// </summary>
        internal static double CompositeLuminance(double fgR, double fgG, double fgB, double alpha, double bgLinR, double bgLinG, double bgLinB)
        {
            alpha = Math.Max(0.0, Math.Min(1.0, alpha));
            double r = alpha * SrgbToLinear(fgR) + (1.0 - alpha) * bgLinR;
            double g = alpha * SrgbToLinear(fgG) + (1.0 - alpha) * bgLinG;
            double b = alpha * SrgbToLinear(fgB) + (1.0 - alpha) * bgLinB;
            return LuminanceLinear(r, g, b);
        }

        internal static double Median(double[] values)
        {
            if (values == null || values.Length == 0) return double.NaN;
            var copy = (double[])values.Clone();
            Array.Sort(copy);
            int mid = copy.Length / 2;
            return copy.Length % 2 == 1 ? copy[mid] : 0.5 * (copy[mid - 1] + copy[mid]);
        }

        /// <summary>
        /// 截图取色的文字对比度。背景取元素矩形**外圈**像素亮度的中位数；文字取矩形内离背景最远的那一截（前 12%，至少 4 个）的均值——
        /// 字形边缘的抗锯齿像素介于两者之间，全取平均会把对比度算低。<paramref name="declaredY"/> 是声明色在线性光里压到背景上的理论值，
        /// 只进 metrics 对照，判据用实测值。样本太少（元素出屏、被裁掉）记 SKIP，不记 PASS。
        /// </summary>
        internal static string JudgeTextContrast(double[] ringY, double[] insideY, double declaredY, double minRatio,
            out double measured, out string metrics, out string reason)
        {
            measured = 0.0;
            reason = null;
            int ring = ringY == null ? 0 : ringY.Length, inside = insideY == null ? 0 : insideY.Length;
            if (ring < 8 || inside < 8)
            {
                metrics = "ring_samples=" + ring + ",inside_samples=" + inside;
                reason = "too_few_samples";
                return "SKIP";
            }
            double background = Median(ringY);
            double text = MeanOfFarthest(insideY, background, 0.12);
            measured = ContrastRatio(text, background);
            double declared = double.IsNaN(declaredY) ? double.NaN : ContrastRatio(declaredY, background);
            metrics = "bg_Y=" + F(background) + ",text_Y=" + F(text) + ",measured=" + F(measured)
                + ",declared=" + (double.IsNaN(declared) ? "n/a" : F(declared)) + ",min=" + F(minRatio)
                + ",ring_samples=" + ring + ",inside_samples=" + inside;
            if (measured + 1e-9 < minRatio) { reason = "contrast_below_min"; return "FAIL"; }
            return "PASS";
        }

        /// <summary>投影框至少要有这么大一块落在截图里才判：大半在画面外是拍法问题（第五轮 A2 11 m 两张只剩上沿一条）。</summary>
        internal const double WorldProbeMinOnScreen = 0.5;
        /// <summary>投影框长边不到这么多像素不判：810×540 下云蚋投影框 17–28 px、虫子本身只有 3–5 px，量出来的是地面花纹（第六轮 28 px 那张量成 0.03）。</summary>
        internal const double WorldProbeMinPixels = 32.0;
        /// <summary>
        /// 采集光斑的色度偏移达到它也算看得见：白天暖色光斑压在砂岩与花丛上主要靠色相。第五轮实测（线性 RGB 色度坐标）：
        /// A1 11 m 肉眼可见而亮度 Weber 只有 0.004、色度 0.18；有光斑的四张 0.18–0.51，同图挪到空地的对照 ≤ 0.08。
        /// </summary>
        internal const double GlowMinChromaShift = 0.12;

        /// <summary>亮度 Weber 达标，或（给了色度门槛时）色度偏移达标，都算看得见。截图判据与 visible_min 断言共用这一条。</summary>
        internal static bool VisibilityMet(double weber, double minWeber, double chromaShift, double minChroma)
        {
            if (weber + 1e-9 >= minWeber) return true;
            return !double.IsNaN(chromaShift) && !double.IsNaN(minChroma) && chromaShift + 1e-9 >= minChroma;
        }

        /// <summary>
        /// 物体相对邻域的色度偏移：线性 RGB 换成 r/(r+g+b) 一类色度坐标，邻域逐通道取中位数作参照，物体里离参照最远的前 15%（至少 4 个）求平均距离。
        /// 口径同亮度的「前 15%」：光斑只占投影框一小块，整块平均会被地面稀释。<paramref name="objectRgb"/> 与 <paramref name="neighborRgb"/> 是 r,g,b 连排；样本不够返回 NaN。
        /// </summary>
        internal static double ChromaShift(double[] objectRgb, double[] neighborRgb)
        {
            int objects = objectRgb == null ? 0 : objectRgb.Length / 3, neighbors = neighborRgb == null ? 0 : neighborRgb.Length / 3;
            if (objects < 4 || neighbors < 8) return double.NaN;
            var nr = new double[neighbors];
            var ng = new double[neighbors];
            var nb = new double[neighbors];
            for (int i = 0; i < neighbors; i++)
            {
                double sum = neighborRgb[i * 3] + neighborRgb[i * 3 + 1] + neighborRgb[i * 3 + 2] + 0.03;
                nr[i] = neighborRgb[i * 3] / sum;
                ng[i] = neighborRgb[i * 3 + 1] / sum;
                nb[i] = neighborRgb[i * 3 + 2] / sum;
            }
            double cr = Median(nr), cg = Median(ng), cb = Median(nb);
            var distance = new double[objects];
            for (int i = 0; i < objects; i++)
            {
                double sum = objectRgb[i * 3] + objectRgb[i * 3 + 1] + objectRgb[i * 3 + 2] + 0.03;
                double dr = objectRgb[i * 3] / sum - cr, dg = objectRgb[i * 3 + 1] / sum - cg, db = objectRgb[i * 3 + 2] / sum - cb;
                distance[i] = Math.Sqrt(dr * dr + dg * dg + db * db);
            }
            return MeanOfFarthest(distance, 0.0, 0.15);
        }

        /// <summary>
        /// <paramref name="values"/> 里离 <paramref name="reference"/> 最远的前 <paramref name="share"/>（至少 4 个）求平均：
        /// 字形、地面圆环、光斑、云蚋在取样框里只占一小块，整块取平均会被背景稀释。文字对比度、世界可见度、色度偏移共用。
        /// </summary>
        private static double MeanOfFarthest(double[] values, double reference, double share)
        {
            var distance = new double[values.Length];
            var order = new int[values.Length];
            for (int i = 0; i < values.Length; i++) { distance[i] = -Math.Abs(values[i] - reference); order[i] = i; }
            Array.Sort(distance, order);
            int take = Math.Min(values.Length, Math.Max(4, (int)Math.Ceiling(values.Length * share)));
            double sum = 0.0;
            for (int i = 0; i < take; i++) sum += values[order[i]];
            return sum / take;
        }

        /// <summary>
        /// 世界物体在屏幕上的可见度：物体投影矩形内的平均亮度与外扩邻域平均亮度之比，按 Weber 口径 |Lo−Ln| / (Ln + 0.05)，
        /// 同时给 WCAG 比值作参考。物体不在屏幕里（样本太少）记 SKIP。数值只能说明「和周围亮度差多少」，好不好认仍要看截图。
        /// </summary>
        /// <param name="onScreenFraction">投影框落在截图里的面积占比。</param>
        /// <param name="targetPixels">投影框长边的像素数。</param>
        /// <param name="chromaShift">物体相对邻域的色度偏移（<see cref="ChromaShift"/>）；NaN 表示不看色度。</param>
        /// <param name="minChroma">色度门槛（光斑一类靠色相的目标）；NaN 表示只看亮度。</param>
        internal static string JudgeWorldVisibility(double[] objectY, double[] neighborY, double minWeber,
            double onScreenFraction, double targetPixels, double chromaShift, double minChroma,
            out double weber, out string metrics, out string reason)
        {
            weber = 0.0;
            reason = null;
            int objects = objectY == null ? 0 : objectY.Length, neighbors = neighborY == null ? 0 : neighborY.Length;
            string frame = ",on_screen=" + F(onScreenFraction) + ",target_px=" + F(targetPixels);
            if (onScreenFraction < WorldProbeMinOnScreen || targetPixels < WorldProbeMinPixels)
            {
                metrics = "object_samples=" + objects + ",neighbor_samples=" + neighbors + frame;
                reason = onScreenFraction < WorldProbeMinOnScreen ? "target_mostly_off_screen" : "target_too_small_on_screen";
                return "SKIP";
            }
            if (objects < 4 || neighbors < 8)
            {
                metrics = "object_samples=" + objects + ",neighbor_samples=" + neighbors;
                reason = "target_not_on_screen";
                return "SKIP";
            }
            double n = Median(neighborY);
            // 物体像素里离邻域最远的那一截（前 15%，至少 4 个）：地面圆环、光斑、云蚋在投影矩形里只占一小部分，整块取平均会被地面稀释。
            double o = MeanOfFarthest(objectY, n, 0.15);
            weber = Math.Abs(o - n) / (n + 0.05);
            metrics = "object_Y=" + F(o) + ",neighbor_Y=" + F(n) + ",weber=" + F(weber) + ",wcag=" + F(ContrastRatio(o, n))
                + ",min_weber=" + F(minWeber) + ",object_samples=" + objects + ",neighbor_samples=" + neighbors + frame
                + (double.IsNaN(chromaShift) ? string.Empty : ",chroma=" + F(chromaShift) + ",min_chroma=" + F(minChroma));
            if (!VisibilityMet(weber, minWeber, chromaShift, minChroma)) { reason = "visibility_below_min"; return "FAIL"; }
            return "PASS";
        }

        /// <summary>环带在屏幕上至少要有这么多段才判覆盖率；少了说明圈大半在画面外，是拍法问题。</summary>
        internal const int RingCoverageMinSamples = 16;

        /// <summary>
        /// 地面圆环（撤离环、噬风预警圈）沿环带的可见覆盖率。旧口径（<see cref="JudgeWorldVisibility"/>）取投影框里离邻域最远的 15% 像素，
        /// 环被广场台面盖得只剩碎弧时，量到的是框里日照的石面，照样 PASS（2026-09-15 第四轮截图）。
        /// 每段给环带中心线上的线性 RGB 与紧挨环带内外两侧的平均线性 RGB（<paramref name="bandRgb"/> / <paramref name="nearRgb"/> 按 r,g,b 平铺）：
        /// 环带相对邻域朝环的声明颜色偏过去（投影 ≥ <paramref name="minShift"/>、夹角余弦 ≥ 0.6）才算这一段看得见。
        /// 覆盖率 = 看得见的段数 / 在屏幕上的段数，由断言按步骤表阈值判；环色与邻域几乎同色的段算看不见。
        /// </summary>
        internal static string JudgeRingCoverage(double[] bandRgb, double[] nearRgb, double ringR, double ringG, double ringB,
            double minShift, out double coverage, out string metrics, out string reason)
        {
            coverage = 0.0;
            reason = null;
            int samples = bandRgb == null || nearRgb == null ? 0 : Math.Min(bandRgb.Length, nearRgb.Length) / 3;
            if (samples < RingCoverageMinSamples)
            {
                metrics = "mode=ring_coverage,samples=" + samples;
                reason = "ring_mostly_off_screen";
                return "SKIP";
            }
            int visible = 0;
            for (int i = 0; i < samples; i++)
            {
                int k = i * 3;
                double dr = bandRgb[k] - nearRgb[k], dg = bandRgb[k + 1] - nearRgb[k + 1], db = bandRgb[k + 2] - nearRgb[k + 2];
                double rr = ringR - nearRgb[k], rg = ringG - nearRgb[k + 1], rb = ringB - nearRgb[k + 2];
                double ringLength = Math.Sqrt(rr * rr + rg * rg + rb * rb);
                double shiftLength = Math.Sqrt(dr * dr + dg * dg + db * db);
                if (ringLength < 1e-6 || shiftLength < 1e-9) continue;
                double toward = (dr * rr + dg * rg + db * rb) / ringLength;
                if (toward + 1e-12 >= minShift && toward / shiftLength >= 0.6) visible++;
            }
            coverage = visible / (double)samples;
            metrics = "mode=ring_coverage,samples=" + samples + ",visible=" + visible + ",coverage=" + F(coverage) + ",min_shift=" + F(minShift);
            return "PASS";
        }

        /// <summary>
        /// 文字溢出：画到框外的（overflowMode 为 Overflow 而且内容超框）判红；被省略号截断的只列出来，不判红——
        /// 省略号是版式最后一道兜底（清单 2.8.12 / 2.8.14 明文接受），截断得多不多交给 AI 看截图。
        /// 例外 <paramref name="truncatedEarly"/>：设计上有行数余量、却没排满就被截断的（字幕封顶两行，只排出一行就省略号），
        /// 说明框高算小了，判红（2026-09-15 第五轮 7 条长字幕只剩一行，一直自动绿）。
        /// </summary>
        internal static string JudgeTextOverflow(IList<string> overflowing, IList<string> truncated, IList<string> truncatedEarly, int inspected,
            out string metrics, out string reason)
        {
            reason = null;
            metrics = "inspected=" + inspected + ",overflowing=" + (overflowing == null ? 0 : overflowing.Count)
                + ",truncated=" + (truncated == null ? 0 : truncated.Count)
                + ",truncated_early=" + (truncatedEarly == null ? 0 : truncatedEarly.Count)
                + (overflowing != null && overflowing.Count > 0 ? ",overflow_list=" + Join(overflowing, 8) : string.Empty)
                + (truncated != null && truncated.Count > 0 ? ",truncated_list=" + Join(truncated, 8) : string.Empty)
                + (truncatedEarly != null && truncatedEarly.Count > 0 ? ",truncated_early_list=" + Join(truncatedEarly, 8) : string.Empty);
            if (inspected == 0) { reason = "no_visible_text"; return "SKIP"; }
            if (overflowing != null && overflowing.Count > 0) { reason = "text_draws_outside_its_box"; return "FAIL"; }
            if (truncatedEarly != null && truncatedEarly.Count > 0) { reason = "text_truncated_before_its_line_budget"; return "FAIL"; }
            return "PASS";
        }

        #endregion

        #region 头目 / 岛主的尸体箱（配装即掉落的实机核对）

        /// <summary>
        /// <c>loot_boss</c> 读到的官方尸体箱是否符合「每次都穿全套，死后只留其中一件」（owner 2026-09-14 拍板）：
        /// 死亡结算真的跑了（<paramref name="outcome"/> 是 slot / inventory / no_drop，不是 alive / error / missing）；
        /// 箱里的专属装备恰好是抽中的那一件，没抽中时一件都没有，岛主（<paramref name="mustDrop"/>）不许没抽中；原版掉落至少一件。
        /// 建箱前事件没接上时，官方会把 Boss 身上的全套收进箱子——这里按件数判红。演练不走死亡，证不到这一条。
        /// </summary>
        internal static bool JudgeBossDrop(IDictionary<int, int> crate, int[] gearTypeIds, int chosenTypeId, string outcome, bool mustDrop,
            out string metrics, out string reason)
        {
            int gearInBox = 0, chosenInBox = 0, vanilla = 0;
            if (crate != null)
            {
                foreach (KeyValuePair<int, int> pair in crate)
                {
                    if (pair.Value <= 0) continue;
                    if (gearTypeIds == null || Array.IndexOf(gearTypeIds, pair.Key) < 0) { vanilla += pair.Value; continue; }
                    gearInBox += pair.Value;
                    if (pair.Key == chosenTypeId) chosenInBox += pair.Value;
                }
            }
            bool resolved = outcome == "slot" || outcome == "inventory" || outcome == "no_drop";
            int expectedGear = chosenTypeId > 0 ? 1 : 0;
            metrics = "outcome=" + (outcome ?? "null") + ",chosen=" + chosenTypeId + ",gear_in_box=" + gearInBox
                + ",vanilla_items=" + vanilla + ",must_drop=" + (mustDrop ? "true" : "false");
            if (crate == null) reason = "crate_not_read";
            else if (!resolved) reason = "drop_not_resolved:" + (outcome ?? "null");
            else if ((outcome == "no_drop") != (chosenTypeId <= 0)) reason = "outcome_chosen_mismatch";
            else if (mustDrop && chosenTypeId <= 0) reason = "lord_dropped_no_gear";
            else if (gearInBox != expectedGear) reason = "gear_in_box_" + gearInBox + "_expected_" + expectedGear;
            else if (chosenInBox != expectedGear) reason = "chosen_piece_not_in_box";
            else if (vanilla <= 0) reason = "vanilla_loot_missing";
            else reason = null;
            return reason == null;
        }

        #endregion

        #region 结果、磁盘预算与报告

        /// <summary>
        /// 一步的结论：任一断言 FAIL 即 FAIL；有断言 PASS 即 PASS；有断言但全是 SKIP 记 SKIP——截图照样进 manifest 给 AI 看，
        /// 但不能拿「截到了图」把没判成的检查算成通过（SKIP 不能吞缺陷）；一条断言都没有、只截了图的记 PASS。
        /// </summary>
        internal static string AggregateResult(IList<F3AutotestAssertion> assertions, int shotsWritten, bool errored)
        {
            if (errored) return "FAIL";
            bool anyPass = false;
            foreach (F3AutotestAssertion assertion in assertions)
            {
                if (assertion.Result == "FAIL") return "FAIL";
                if (assertion.Result == "PASS") anyPass = true;
            }
            if (anyPass) return "PASS";
            return assertions.Count == 0 && shotsWritten > 0 ? "PASS" : "SKIP";
        }

        /// <summary>截图落盘编码：UI 用 PNG、世界用 JPG；用量过六成一律 JPG，过九成降采样一半，满了只分析不落盘。</summary>
        internal static string ChooseEncoding(long usedBytes, bool ui, long budget)
        {
            if (budget <= 0 || usedBytes >= budget) return "skip";
            if (usedBytes >= budget * 9 / 10) return "jpg_half";
            if (usedBytes >= budget * 6 / 10) return "jpg";
            return ui ? "png" : "jpg";
        }

        internal static string LogLine(string stepId, string phase, string result)
        {
            return "[AUTOTEST] step=" + (stepId ?? "?") + " " + phase + (string.IsNullOrEmpty(result) ? string.Empty : " result=" + result);
        }

        /// <summary>
        /// 回基地、还原之后的官方图鉴镜像核对。官方 NoteIndex 是官方存档（`NoteIndexData`），只增不减，自动验收还原不了它：
        /// 开跑前已点亮、或这一轮阶段推进时点亮的条目，还原后在我们的存档里可能没有记录。这两类记进 metrics 并**扣掉**；
        /// 扣掉之后仍然「官方点亮而存档没有」或「存档有而官方没点亮」的才判红。
        /// </summary>
        internal static bool JudgeOfficialNotesAfterAutotest(ICollection<string> recorded, ICollection<string> unlockedNow,
            ICollection<string> unlockedBefore, ICollection<string> unlockedDuringRun, out string metrics, out string reason)
        {
            reason = null;
            var extra = new List<string>();
            var missing = new List<string>();
            foreach (string key in unlockedNow)
                if (!recorded.Contains(key) && !unlockedBefore.Contains(key) && !unlockedDuringRun.Contains(key)) extra.Add(key);
            foreach (string key in recorded)
                if (!unlockedNow.Contains(key)) missing.Add(key);
            metrics = "recorded=" + recorded.Count + ",unlocked_now=" + unlockedNow.Count + ",unlocked_before=" + unlockedBefore.Count
                + ",unlocked_during_run=" + unlockedDuringRun.Count + ",unexplained_extra=" + Join(extra, 8) + ",missing=" + Join(missing, 8);
            if (extra.Count > 0 || missing.Count > 0) reason = "official_mirror_mismatch_after_restore";
            return reason == null;
        }

        internal static string Join(IList<string> values, int max)
        {
            if (values == null || values.Count == 0) return "none";
            var parts = new List<string>();
            for (int i = 0; i < values.Count && i < max; i++) parts.Add(values[i]);
            return string.Join("+", parts.ToArray()) + (values.Count > max ? "+…" + (values.Count - max) : string.Empty);
        }

        private static string F(double value)
        {
            return double.IsNaN(value) ? "nan" : value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>manifest.json：一轮的全部步骤、断言、截图与清单覆盖。字段名冻结（tools/autotest_review.py 读它）。</summary>
        internal static string RenderManifest(F3AutotestRunInfo info, IList<F3AutotestStepRecord> steps, F3AutotestTable table,
            IDictionary<string, string> caseOutcomes)
        {
            var w = new BossRushJsonWriter();
            w.BeginObject().Str("schema", ManifestSchema).Str("runId", info.RunId).Int("slot", info.Slot).Str("mvid", info.Mvid)
                .Str("language", info.Language).Str("altLanguage", info.AltLanguage).Str("startedUtc", info.StartedUtc)
                .Str("endedUtc", info.EndedUtc).Str("status", info.Status).Str("reportLog", info.ReportLog)
                .Str("steamScreenshots", SteamScreenshotNote);
            w.BeginObject("restore").Str("story", info.RestoreStory).Str("detail", info.RestoreDetail)
                .Str("environment", info.EnvironmentRestore).Str("items", info.ItemLedger).Str("money", info.MoneyLedger).EndObject();
            w.BeginObject("shots").Long("bytes", info.ShotBytes).Long("budgetBytes", ShotBudgetBytes).Int("count", info.ShotCount)
                .Int("degraded", info.ShotsDegraded).Int("skipped", info.ShotsSkipped).EndObject();
            w.BeginArray("steps");
            foreach (F3AutotestStepRecord step in steps)
            {
                w.BeginObject().Str("id", step.Id).Str("title", step.Title).Str("stage", step.Stage).Str("location", step.Location)
                    .Str("class", step.Class).Str("language", step.Language).Str("result", step.Result).Str("reason", step.Reason)
                    .Str("startedUtc", step.StartedUtc).Long("durationMs", step.DurationMs);
                StringArray(w, "checklist", step.Checklist);
                StringArray(w, "actions", step.Actions);
                w.BeginArray("assertions");
                foreach (F3AutotestAssertion a in step.Assertions)
                    w.BeginObject().Str("name", a.Name).Str("result", a.Result).Str("reason", a.Reason).Str("metrics", a.Metrics).EndObject();
                w.EndArray().BeginArray("shots");
                foreach (F3AutotestShot s in step.Shots)
                    w.BeginObject().Str("name", s.Name).Str("file", s.File).Str("kind", s.Kind).Str("encoding", s.Encoding)
                        .Long("bytes", s.Bytes).Str("metrics", s.Metrics).EndObject();
                w.EndArray();
                StringArray(w, "notes", step.Notes);
                w.EndObject();
            }
            w.EndArray().BeginArray("coverage");
            if (table != null)
            {
                foreach (F3AutotestCoverageRow row in table.Coverage)
                {
                    w.BeginObject().Str("checklist", row.Checklist).Str("class", row.Class).Str("reason", row.Reason)
                        .Str("status", CoverageStatus(row, steps, caseOutcomes));
                    StringArray(w, "evidence", row.Evidence);
                    w.EndObject();
                }
            }
            w.EndArray().EndObject();
            return w.ToString();
        }

        private static void StringArray(BossRushJsonWriter w, string name, IList<string> values)
        {
            w.BeginArray(name);
            if (values != null) foreach (string value in values) w.ItemStr(value);
            w.EndArray();
        }

        /// <summary>
        /// 一行清单这一轮的状态：人工行恒为 MANUAL；自动 / 截图行取游戏里观测得到的证据里最差的一个（FAIL &gt; NOT_RUN &gt; SKIP &gt; PASS）。
        /// 离线证据（守卫、执行回归）这一轮在游戏里看不到结论，不参与取最差；只有离线证据的行记 OFFLINE。
        /// </summary>
        internal static string CoverageStatus(F3AutotestCoverageRow row, IList<F3AutotestStepRecord> steps, IDictionary<string, string> caseOutcomes)
        {
            if (row.Class == "manual") return "MANUAL";
            int worst = 0;
            bool observed = false;
            foreach (string evidence in row.Evidence)
            {
                if (evidence.StartsWith(OfflineEvidencePrefix, StringComparison.Ordinal)) continue;
                observed = true;
                string outcome = null;
                if (evidence.StartsWith("SKY_AUTO_", StringComparison.Ordinal))
                {
                    foreach (F3AutotestStepRecord step in steps) if (step.Id == evidence) outcome = step.Result;
                }
                else if (caseOutcomes != null) caseOutcomes.TryGetValue(evidence, out outcome);
                int rank = outcome == "FAIL" ? 3 : outcome == null ? 2 : outcome == "SKIP" ? 1 : 0;
                if (rank > worst) worst = rank;
            }
            if (!observed) return "OFFLINE";
            return worst == 3 ? "FAIL" : worst == 2 ? "NOT_RUN" : worst == 1 ? "SKIP" : "PASS";
        }

        /// <summary>summary.md：还原失败在最顶上标红；然后是总数、红项、清单覆盖表（自动 / 截图 / 人工）与人工行理由。</summary>
        internal static string RenderSummary(F3AutotestRunInfo info, IList<F3AutotestStepRecord> steps, F3AutotestTable table,
            IDictionary<string, string> caseOutcomes)
        {
            var text = new StringBuilder();
            if (RestoreNeedsAttention(info.RestoreStory))
            {
                text.AppendLine("> ❌ **测试档天空岛剧情还原状态：" + info.RestoreStory + "**（" + info.RestoreDetail + "）");
                text.AppendLine("> 处理：不要在这个槽上继续玩。回到基地后重启游戏读这个槽，运行器会按存档里的快照键 `BossRush_Validation_AutotestSnapshot_v1` 自动还原；");
                text.AppendLine("> 仍失败时把本目录的 `story_snapshot.json` 交给 AI，按其中 `raw` 字段手工写回 `BossRush_SkyIsland_Story_v1`。");
                text.AppendLine();
            }
            if (info.EnvironmentRestore != "PASS" && info.EnvironmentRestore != "NOT_NEEDED")
                text.AppendLine("> ⚠️ 环境还原（语言 / 强制夜里 / 时间流速 / 无敌）：" + info.EnvironmentRestore).AppendLine();
            int pass = 0, fail = 0, skip = 0;
            foreach (F3AutotestStepRecord step in steps)
            {
                if (step.Result == "PASS") pass++;
                else if (step.Result == "FAIL") fail++;
                else skip++;
            }
            text.AppendLine("# 全自动实机验收 " + info.RunId);
            text.AppendLine();
            text.AppendLine("- 状态：**" + info.Status + "**；步骤 PASS=" + pass + " FAIL=" + fail + " SKIP=" + skip + "；槽位 " + info.Slot + "；DLL MVID " + info.Mvid);
            text.AppendLine("- 语言：" + info.Language + " → 复拍 " + info.AltLanguage + "；开始 " + info.StartedUtc + "，结束 " + info.EndedUtc);
            text.AppendLine("- 截图：" + info.ShotCount + " 张，" + (info.ShotBytes / (1024 * 1024)) + " MB / 预算 " + (ShotBudgetBytes / (1024 * 1024))
                + " MB（降级 " + info.ShotsDegraded + "、超预算只分析不落盘 " + info.ShotsSkipped + "）；Steam 截图不用，理由见 manifest。");
            text.AppendLine("- 物品记账：" + info.ItemLedger + "；金钱：" + info.MoneyLedger);
            text.AppendLine("- 主套件与岛内用例逐项结果：" + info.ReportLog);
            text.AppendLine();
            text.AppendLine("## 红项");
            text.AppendLine();
            bool anyRed = false;
            foreach (F3AutotestStepRecord step in steps)
            {
                if (step.Result != "FAIL") continue;
                anyRed = true;
                text.AppendLine("- `" + step.Id + "`（" + string.Join(", ", step.Checklist.ToArray()) + "）：" + step.Reason);
                foreach (F3AutotestAssertion a in step.Assertions)
                    if (a.Result == "FAIL") text.AppendLine("  - " + a.Name + "：" + a.Reason + " | " + a.Metrics);
            }
            if (caseOutcomes != null)
                foreach (KeyValuePair<string, string> pair in caseOutcomes)
                    if (pair.Value == "FAIL") { anyRed = true; text.AppendLine("- 用例 `" + pair.Key + "` FAIL（详见 .log）"); }
            if (!anyRed) text.AppendLine("- 无");
            text.AppendLine();
            text.AppendLine("## 清单覆盖");
            text.AppendLine();
            if (table != null)
            {
                int auto = 0, shot = 0, manual = 0;
                foreach (F3AutotestCoverageRow row in table.Coverage)
                {
                    if (row.Class == "auto") auto++;
                    else if (row.Class == "shot") shot++;
                    else manual++;
                }
                text.AppendLine("自动断言 " + auto + " 行 · 截图待 AI 看 " + shot + " 行 · 只能人工 " + manual + " 行。");
                text.AppendLine();
                text.AppendLine("| 清单 | 归类 | 本轮 | 证据 / 理由 |");
                text.AppendLine("| --- | --- | --- | --- |");
                foreach (F3AutotestCoverageRow row in table.Coverage)
                {
                    string status = CoverageStatus(row, steps, caseOutcomes);
                    string detail = row.Class == "manual" ? row.Reason : string.Join(", ", row.Evidence.ToArray());
                    text.AppendLine("| " + row.Checklist + " | " + ClassLabel(row.Class) + " | " + status + " | " + (detail ?? string.Empty).Replace("|", "/") + " |");
                }
            }
            text.AppendLine();
            text.AppendLine("## 审阅");
            text.AppendLine();
            text.AppendLine("`python tools/autotest_review.py <本目录>`：按清单分组生成缩略图总览、红项汇总与上一轮同 id 对比。");
            return text.ToString();
        }

        /// <summary>还原状态要不要在 summary 顶上标红：失败、或只能等下次回基地由存档里的快照键恢复。没写过存档（NOT_NEEDED）不标。</summary>
        internal static bool RestoreNeedsAttention(string restoreStory)
        {
            return restoreStory == "FAIL" || restoreStory == "NOT_RUN"
                || (restoreStory != null && restoreStory.StartsWith("PENDING", StringComparison.Ordinal));
        }

        private static string ClassLabel(string value)
        {
            return value == "auto" ? "自动断言" : value == "shot" ? "截图待 AI 看" : "只能人工";
        }

        /// <summary>整轮状态：还原失败 &gt; 取消 / 中止 &gt; 有红 &gt; 有没跑到的、或一步都没通过（全是 SKIP）&gt; PASS。</summary>
        internal static string RunStatus(bool restoreOk, bool cancelled, bool aborted, IList<F3AutotestStepRecord> steps, int expectedSteps)
        {
            if (!restoreOk) return "RESTORE_FAILED";
            if (cancelled) return "CANCELLED";
            if (aborted) return "ABORTED";
            bool anyPass = false;
            foreach (F3AutotestStepRecord step in steps)
            {
                if (step.Result == "FAIL") return "FAIL";
                if (step.Result == "PASS") anyPass = true;
            }
            return steps.Count < expectedSteps || (steps.Count > 0 && !anyPass) ? "INCOMPLETE" : "PASS";
        }

        #endregion
    }
}
#endif
