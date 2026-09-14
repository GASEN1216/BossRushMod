#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationAutotestAsserts.cs - 全自动实机验收的断言求值（Dev 构建；由步骤表驱动）
// ============================================================================
// 从 F3GameplayValidationAutotestActions.cs 原样提取（新文件 1200 行预算）。步骤表里的 assert:名字[:参数…] 在这里求值，
// 名字清单只有 F3AutotestJudges.AssertNames 一份；判据尽量交给纯函数，这里只取数。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Dialogues;
using Duckov.UI;
using SodaCraft.Localizations;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        #region 断言

        private F3AutotestAssertion EvaluateAutotestAssert(string[] args)
        {
            string name = Arg(args, 0);
            string label = "assert:" + string.Join(":", args);
            try
            {
                SkyIslandSession session = SkyIslandSessionOrNull();
                SkyIslandStoryService story = session == null ? null : session.ValidationStory;
                switch (name)
                {
                    case "panel_open": return AutotestBool(label, AutotestPanelOpen(), "panel_not_open", "labels=" + AutotestRowLabels(AutotestPanelRows()));
                    case "panel_closed": return AutotestBool(label, !AutotestPanelOpen(), "panel_still_open", null);
                    case "choice_present":
                    case "choice_absent":
                    {
                        List<Button> rows = AutotestPanelRows();
                        string[] alternatives = AutotestAlternatives(Arg(args, 1));
                        bool found = false;
                        foreach (Button row in rows) if (AutotestContainsAny(AutotestRowLabel(row), alternatives)) found = true;
                        bool ok = name == "choice_present" ? found : AutotestPanelOpen() && !found;
                        return AutotestBool(label, ok, name == "choice_present" ? "choice_missing" : "choice_still_offered_or_panel_closed",
                            "panel=" + AutotestPanelOpen() + ",labels=" + AutotestRowLabels(rows));
                    }
                    case "choice_count_le":
                    {
                        int rows = AutotestPanelRows().Count;
                        return AutotestBool(label, AutotestPanelOpen() && rows <= ArgInt(args, 1, 0), "too_many_choices", "rows=" + rows);
                    }
                    case "body_contains":
                    {
                        string body = AutotestPanelBody();
                        return AutotestBool(label, body != null && AutotestContainsAny(body, AutotestAlternatives(Arg(args, 1))), "body_text_missing",
                            "body=" + AutotestShort(body, 160));
                    }
                    case "body_absent":
                    {
                        string body = AutotestPanelBody();
                        return AutotestBool(label, body != null && !AutotestContainsAny(body, AutotestAlternatives(Arg(args, 1))),
                            "body_text_present_or_panel_closed", "body=" + AutotestShort(body, 160));
                    }
                    case "alpha_le":
                    {
                        float alpha = AutotestEffectiveAlpha(Arg(args, 1));
                        return AutotestBool(label, alpha <= ArgFloat(args, 2, 0.05f), "still_visible", "alpha=" + alpha.ToString("F2", CultureInfo.InvariantCulture));
                    }
                    case "killed_ge": return AutotestBool(label, _autotest.LastKilled >= ArgInt(args, 1, 1), "too_few_kills", "killed=" + _autotest.LastKilled);
                    case "near":
                    {
                        Transform marker = session == null ? null : session.DevAutotestFind(Arg(args, 1));
                        CharacterMainControl player = CharacterMainControl.Main;
                        if (marker == null || player == null) return AutotestAssertion(label, "FAIL", "marker_or_player_missing", null);
                        float distance = Vector3.Distance(marker.position, player.transform.position);
                        return AutotestBool(label, distance <= ArgFloat(args, 2, 5f), "too_far", "distance_m=" + distance.ToString("F1", CultureInfo.InvariantCulture));
                    }
                    case "pack_delta_ge":
                    {
                        int typeId = ArgInt(args, 1, 0), before;
                        _autotest.StepStartCounts.TryGetValue(typeId, out before);
                        int now = ItemFactory.GetItemCountInInventory(typeId);
                        return AutotestBool(label, now - before >= ArgInt(args, 2, 1), "pack_gain_too_small", "before=" + before + ",now=" + now);
                    }
                    case "dialogue_active": return AutotestBool(label, DialogueManager.IsDialogueActive, "dialogue_not_active", null);
                    case "dialogue_inactive": return AutotestBool(label, !DialogueManager.IsDialogueActive && !DialogueUI.Active, "dialogue_still_active", null);
                    case "objective_contains":
                    {
                        string objective = session == null ? null : session.ValidationHudObjective;
                        return AutotestBool(label, objective != null && AutotestContainsAny(objective, AutotestAlternatives(Arg(args, 1))),
                            "objective_mismatch", "objective=" + AutotestShort(objective, 160));
                    }
                    case "caption_contains":
                    {
                        string caption = AutotestCaptionText();
                        return AutotestBool(label, caption != null && AutotestContainsAny(caption, AutotestAlternatives(Arg(args, 1))),
                            "caption_mismatch", "caption=" + AutotestShort(caption, 160));
                    }
                    case "case": return AutotestSyncCase(Arg(args, 1));
                    case "object_present": return AutotestBool(label, FindAutotestObjectByPrefix(Arg(args, 1)) != null, "object_missing", null);
                    case "object_absent": return AutotestBool(label, FindAutotestObjectByPrefix(Arg(args, 1)) == null, "object_still_present", null);
                    case "pack_count_ge":
                    {
                        int typeId = ArgInt(args, 1, 0), count = ItemFactory.GetItemCountInInventory(typeId);
                        return AutotestBool(label, count >= ArgInt(args, 2, 1), "pack_count_low", "count=" + count);
                    }
                    case "pack_delta":
                    {
                        int typeId = ArgInt(args, 1, 0), before;
                        _autotest.StepStartCounts.TryGetValue(typeId, out before);
                        int now = ItemFactory.GetItemCountInInventory(typeId);
                        return AutotestBool(label, now - before == ArgInt(args, 2, 0), "pack_delta_mismatch", "before=" + before + ",now=" + now);
                    }
                    case "crate_has":
                    {
                        int typeId = ArgInt(args, 1, 0), count = 0;
                        if (_autotest.LastLoot != null) _autotest.LastLoot.TryGetValue(typeId, out count);
                        return AutotestBool(label, count >= ArgInt(args, 2, 1), _autotest.LastLoot == null ? "no_loot_read" : "crate_missing_item", "count=" + count);
                    }
                    case "crate_has_any":
                    {
                        bool any = false;
                        foreach (string part in Arg(args, 1).Split('+'))
                        {
                            int typeId, count;
                            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out typeId) && _autotest.LastLoot != null
                                && _autotest.LastLoot.TryGetValue(typeId, out count) && count > 0) any = true;
                        }
                        return AutotestBool(label, any, "crate_missing_all_alternatives", null);
                    }
                    case "flag":
                    case "no_flag":
                    {
                        if (story == null) return AutotestAssertion(label, "SKIP", "no_live_story", null);
                        SkyIslandStoryFlag flag = (SkyIslandStoryFlag)Enum.Parse(typeof(SkyIslandStoryFlag), Arg(args, 1), false);
                        bool has = story.Current.Has(flag);
                        return AutotestBool(label, name == "flag" ? has : !has, "flag_state_mismatch", "flags=" + story.Current.flags);
                    }
                    case "note":
                        if (story == null) return AutotestAssertion(label, "SKIP", "no_live_story", null);
                        return AutotestBool(label, Array.IndexOf(story.Current.discoveredNotes, Arg(args, 1)) >= 0, "note_missing", null);
                    case "stage_data":
                    {
                        SkyIslandStoryData expected;
                        if (story == null || _autotest.CurrentStageId == null || !_autotest.StageExpectations.TryGetValue(_autotest.CurrentStageId, out expected))
                            return AutotestAssertion(label, "SKIP", "not_in_story_stage", null);
                        string metrics, reason;
                        bool ok = F3AutotestJudges.JudgeStageData(story.Current, expected, out metrics, out reason);
                        return AutotestAssertion(label, ok ? "PASS" : "FAIL", reason, metrics);
                    }
                    case "contrast_min": return AutotestMeasured(label, _autotest.LastShot == null ? null : AutotestLookup(_autotest.LastShot.Contrast, Arg(args, 1)), ArgFloat(args, 2, 4.5f));
                    case "visible_min": return AutotestMeasured(label, _autotest.LastShot == null ? null : AutotestLookup(_autotest.LastShot.Visibility, Arg(args, 1)), ArgFloat(args, 2, 0.3f));
                    case "row_contrast_min":
                    {
                        if (_autotest.LastShot == null) return AutotestAssertion(label, "SKIP", "no_shot", null);
                        int a = ArgInt(args, 1, 0), b = ArgInt(args, 2, 1);
                        List<double> rows = _autotest.LastShot.RowLuminance;
                        if (a >= rows.Count || b >= rows.Count) return AutotestAssertion(label, "SKIP", "rows_not_on_screen", "rows=" + rows.Count);
                        double ratio = F3AutotestJudges.ContrastRatio(rows[a], rows[b]);
                        return AutotestBool(label, ratio >= ArgFloat(args, 3, 3f), "row_contrast_below_min",
                            "row" + a + "_Y=" + rows[a].ToString("0.###", CultureInfo.InvariantCulture) + ",row" + b + "_Y="
                            + rows[b].ToString("0.###", CultureInfo.InvariantCulture) + ",ratio=" + ratio.ToString("0.###", CultureInfo.InvariantCulture));
                    }
                    case "overflow_none":
                    {
                        AutotestMeasure overflow = _autotest.LastShot == null ? null : _autotest.LastShot.Overflow;
                        if (overflow == null) return AutotestAssertion(label, "SKIP", "no_shot", null);
                        return AutotestAssertion(label, overflow.Result, overflow.Reason, overflow.Metrics);
                    }
                    case "profile_ok":
                        return AutotestBool(label, _autotest.FrameSampled && _autotest.LastFrameReason == null, _autotest.FrameSampled ? _autotest.LastFrameReason : "no_frame_sample", null);
                    case "quiet": return AutotestBool(label, AutotestQuiet(), "threats_nearby", null);
                    case "health_ge":
                    {
                        Health health = CharacterMainControl.Main == null ? null : CharacterMainControl.Main.Health;
                        float fraction = health == null ? -1f : health.CurrentHealth / Mathf.Max(1f, health.MaxHealth);
                        return AutotestBool(label, fraction >= ArgFloat(args, 1, 0.5f), "health_below", "fraction=" + fraction.ToString("F2", CultureInfo.InvariantCulture));
                    }
                    case "color_equals":
                    {
                        Color a, b;
                        if (!TryAutotestColorToken(Arg(args, 1), out a) || !TryAutotestColorToken(Arg(args, 2), out b))
                            return AutotestAssertion(label, "FAIL", "color_token_unknown", null);
                        bool same = Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f;
                        double white = F3AutotestJudges.LuminanceSrgb(1.0, 1.0, 1.0);
                        return AutotestBool(label, same, "color_tokens_differ", "a=" + ColorUtility.ToHtmlStringRGB(a) + ",b=" + ColorUtility.ToHtmlStringRGB(b)
                            + ",white_on_a=" + F3AutotestJudges.ContrastRatio(white, F3AutotestJudges.LuminanceSrgb(a.r, a.g, a.b)).ToString("0.##", CultureInfo.InvariantCulture));
                    }
                    case "chilled":
                    {
                        SkyIslandFieldcraft field = SkyIslandFieldcraft.Current;
                        if (field == null) return AutotestAssertion(label, "FAIL", "fieldcraft_missing", null);
                        return AutotestBool(label, field.Chilled == (Arg(args, 1) == "true"), "chill_state_mismatch", "exposure=" + field.Exposure.ToString("F2", CultureInfo.InvariantCulture));
                    }
                    case "wind_gale":
                    {
                        SkyIslandFieldcraft field = SkyIslandFieldcraft.Current;
                        if (field == null) return AutotestAssertion(label, "FAIL", "fieldcraft_missing", null);
                        // 「噬风将至 / 回响在场」的大风开关：栈道与桥上的风级由它抬到大风（StormWindPending），风级数值只进 metrics。
                        SkyIslandWindSample sample = field.WindSample;
                        return AutotestBool(label, sample.Sampled && sample.StormPending == (Arg(args, 1) == "true"),
                            sample.Sampled ? "storm_wind_state_mismatch" : "wind_not_sampled_yet",
                            "storm_pending=" + sample.StormPending + ",gale=" + sample.Gale + ",level=" + sample.Level
                            + ",on_boardwalk=" + sample.OnBoardwalk + ",on_bridge=" + sample.OnBridge);
                    }
                    case "extraction_open":
                    {
                        if (session == null) return AutotestAssertion(label, "SKIP", "no_island_session", null);
                        string which = Arg(args, 1);
                        Transform exit = which == "bell" ? session.BellExitIfUnlocked() : which == "wind" ? session.WindExitIfUnlocked()
                            : which == "star" ? session.StarExitIfUnlocked() : session.ValidationExitMarker;
                        return AutotestBool(label, (exit != null) == (Arg(args, 2) != "false"), "extraction_state_mismatch", null);
                    }
                    case "echo_starts":
                        if (session == null) return AutotestAssertion(label, "SKIP", "no_island_session", null);
                        return AutotestBool(label, session.ValidationStormEchoStarts == ArgInt(args, 1, 0), "echo_starts_mismatch", "starts=" + session.ValidationStormEchoStarts);
                    case "gnats_alive_ge":
                    {
                        SkyIslandFieldcraft field = SkyIslandFieldcraft.Current;
                        int alive = field == null || field.Gnats == null ? -1 : field.Gnats.Alive;
                        return AutotestBool(label, alive >= ArgInt(args, 1, 1), "too_few_gnats", "alive=" + alive);
                    }
                    case "language_is": return AutotestBool(label, L10n.IsChinese == (Arg(args, 1) == "zh"), "language_mismatch", LocalizationManager.CurrentLanguage.ToString());
                    case "view_open": return AutotestBool(label, View.ActiveView != null, "no_official_view", null);
                    case "log_contains":
                    {
                        string log = ReadAutotestSharedText(Application.consoleLogPath);
                        string want = string.Join(":", args, 1, Math.Max(0, args.Length - 1));
                        return AutotestBool(label, log != null && log.IndexOf(want, StringComparison.Ordinal) >= 0, "log_text_missing", "log=" + Application.consoleLogPath);
                    }
                    case "prev_log_quit": return AutotestPreviousLogQuit(label);
                    case "object_static":
                    {
                        Vector3 first;
                        GameObject now = GameObject.Find(Arg(args, 1));
                        if (!_autotest.StaticProbes.TryGetValue(Arg(args, 1), out first) || now == null)
                            return AutotestAssertion(label, "SKIP", "object_not_tracked_or_gone", null);
                        float drift = Vector3.Distance(first, now.transform.position);
                        return AutotestBool(label, drift <= ArgFloat(args, 2, 0.3f), "object_moved", "drift_m=" + drift.ToString("F2", CultureInfo.InvariantCulture));
                    }
                    default:
                        return AutotestAssertion(label, "FAIL", "assert_not_implemented", null);
                }
            }
            catch (Exception e)
            {
                return AutotestAssertion(label, "FAIL", "assert_threw:" + e.GetType().Name + ":" + e.Message, null);
            }
        }

        private F3AutotestAssertion AutotestSyncCase(string caseKey)
        {
            SyncValidation validation = AutotestCaseDelegate(caseKey);
            string label = "case:" + caseKey;
            if (validation == null) return AutotestAssertion(label, "FAIL", "case_not_mapped", null);
            string metrics = string.Empty, reason = null;
            try
            {
                bool ok = validation(out metrics, out reason);
                return AutotestAssertion(label, ok ? "PASS" : "FAIL", ok ? null : reason, metrics);
            }
            catch (SkyIslandSkipCase skip) { return AutotestAssertion(label, "SKIP", skip.Message, skip.Metrics); }
            catch (Exception e) { return AutotestAssertion(label, "FAIL", "case_threw:" + e.GetType().Name + ":" + e.Message, metrics); }
        }

        /// <summary>步骤表 <c>assert:case:SKY_…</c> 能引用的只读判据（岛内套件与基地侧），结论只进这一步的断言，不另记用例 id。</summary>
        private SyncValidation AutotestCaseDelegate(string caseKey)
        {
            switch (caseKey)
            {
                case "SKY_SESSION_READY": return ValidateSkyIslandSessionReady;
                case "SKY_GATE_STATE": return ValidateSkyIslandGateState;
                case "SKY_STORY_OBJECTIVE": return ValidateSkyIslandObjective;
                case "SKY_CHOICE_GATES": return ValidateSkyIslandChoiceGates;
                case "SKY_STORY_CODEC": return ValidateSkyIslandStoryCodec;
                case "SKY_STORY_SAVE_STATE": return ValidateSkyIslandSaveState;
                case "SKY_SERVICE_PRICING": return ValidateSkyIslandServicePricing;
                case "SKY_BOUNTY_GATING": return ValidateSkyIslandBountyGating;
                case "SKY_RESIDENTS": return ValidateSkyIslandResidents;
                case "SKY_OFFICIAL_NOTES": return ValidateSkyIslandOfficialNotes;
                case "SKY_KEEPSAKE_ITEMS": return ValidateSkyIslandKeepsakes;
                case "SKY_LETTER_PIGEON": return ValidateSkyIslandLetterPigeon;
                case "SKY_EXTRACTION_RINGS": return ValidateSkyIslandExtractionRings;
                case "SKY_EXTRACTION_RULE": return ValidateSkyIslandExtractionRule;
                case "SKY_EXTRACTION_OFFICIAL_UI": return ValidateSkyIslandOfficialCountdown;
                case "SKY_STORM_TUNING": return ValidateSkyIslandStormTuning;
                case "SKY_STORM_ECHO": return ValidateSkyIslandStormEcho;
                case "SKY_LAMPS_WIND": return ValidateSkyIslandLampsWind;
                case "SKY_GNAT_RUNTIME": return ValidateSkyIslandGnatRuntime;
                case "SKY_GATHER_NODES": return ValidateSkyIslandGatherNodes;
                case "SKY_PANEL_ART": return ValidateSkyIslandPanelArt;
                case "SKY_LOOT_BANDS": return ValidateSkyIslandLootBands;
                case "SKY_LOCALIZATION_EN": return ValidateSkyIslandEnglishText;
                case "SKY_SCENE_BASELINE": return ValidateSkyIslandSceneBaseline;
                case "SKY_OFFICIAL_NOTES_BASE": return ValidateSkyIslandOfficialNotesAtBase;
                case "SKY_KEEPSAKE_ITEMS_BASE": return ValidateSkyIslandKeepsakesAtBase;
                default: return null;
            }
        }

        private static AutotestMeasure AutotestLookup(Dictionary<string, AutotestMeasure> measures, string key)
        {
            AutotestMeasure measure;
            return measures != null && key != null && measures.TryGetValue(key, out measure) ? measure : null;
        }

        private static F3AutotestAssertion AutotestMeasured(string label, AutotestMeasure measure, float min)
        {
            if (measure == null) return AutotestAssertion(label, "SKIP", "not_measured_in_last_shot", null);
            if (measure.Result == "SKIP") return AutotestAssertion(label, "SKIP", measure.Reason, measure.Metrics);
            if (measure.Result == "FAIL" && measure.Value <= 0.0) return AutotestAssertion(label, "FAIL", measure.Reason, measure.Metrics);
            bool ok = measure.Value + 1e-9 >= min;
            return AutotestAssertion(label, ok ? "PASS" : "FAIL", ok ? null : "below_min_" + min.ToString("0.##", CultureInfo.InvariantCulture), measure.Metrics);
        }

        private static F3AutotestAssertion AutotestPreviousLogQuit(string label)
        {
            string path;
            try { path = Path.Combine(Path.GetDirectoryName(Application.consoleLogPath), "Player-prev.log"); }
            catch (Exception e) { return AutotestAssertion(label, "SKIP", "console_log_path_unavailable:" + e.GetType().Name, null); }
            string text = ReadAutotestSharedText(path);
            if (text == null) return AutotestAssertion(label, "SKIP", "no_previous_player_log", path);
            bool onIsland = text.IndexOf("[SkyIsland] ENTER_PASS", StringComparison.Ordinal) >= 0;
            bool quitClean = text.IndexOf("[SkyIsland] CLEANUP reason=application_quit", StringComparison.Ordinal) >= 0;
            bool destroyed = text.IndexOf("GameObjects can not be made active when they are being destroyed", StringComparison.Ordinal) >= 0;
            if (quitClean && !destroyed) return AutotestAssertion(label, "PASS", null, "previous_log=" + path);
            if (onIsland && destroyed && !quitClean) return AutotestAssertion(label, "FAIL", "quit_from_island_still_raises_destroyed_object_error", "previous_log=" + path);
            return AutotestAssertion(label, "SKIP", "previous_session_did_not_quit_on_the_island", "on_island=" + onIsland + ",quit_clean=" + quitClean + ",destroyed_error=" + destroyed);
        }

        private static string ReadAutotestSharedText(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (Exception) { return null; }
        }

        private string LastAutotestReportLine(string caseKey)
        {
            string text = ReadAutotestSharedText(_reportPath);
            if (text == null) return null;
            int at = text.LastIndexOf(caseKey + " | ", StringComparison.Ordinal);
            if (at < 0) return null;
            int end = text.IndexOf('\n', at);
            return AutotestShort(end < 0 ? text.Substring(at) : text.Substring(at, end - at), 600);
        }

        private static bool TryAutotestColorToken(string token, out Color color)
        {
            switch (token)
            {
                case "ZombieModeUIHelper.SuccessColor": color = ZombieModeUIHelper.SuccessColor; return true;
                case "BossRushUIColors.Success": color = BossRushUIColors.Success; return true;
                case "BossRushUIColors.Warning": color = BossRushUIColors.Warning; return true;
                default: color = default(Color); return false;
            }
        }

        private static GameObject FindAutotestObjectByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return null;
            GameObject exact = GameObject.Find(prefix);
            if (exact != null) return exact;
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
                if (t != null && t.gameObject.activeInHierarchy && t.name.StartsWith(prefix, StringComparison.Ordinal)) return t.gameObject;
            return null;
        }

        #endregion
    }
}
#endif
