#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationAutotestActions.cs - 全自动实机验收的动作库与断言（Dev 构建；由步骤表驱动）
// ============================================================================
// 步骤表（Assets/Data/SkyIslandAutotest.json）里每一步是一串「动词:参数」。这里只做分派，动作都走生产入口或官方公开 API：
//   teleport       会话 DevAutotestTeleport（先核地面再 SetPosition，同航徽回码头），按真实时间等相机与光照稳定
//   interact       官方 CharacterMainControl.Interact，走真实读条，等面板或对话出现
//   choose / hover 剧情面板选项行 Button.onClick / PointerEnter（与鼠标点击同一条路径，不反射私有 UI）
//   dialogue_*     官方 DialogueUI.Confirm 与 DialogueUIChoice.OnPointerClick（同 Dev 演练）
//   clear_nearby   以玩家为伤害来源走官方 Health.Hurt，让死亡、清场与委托记账真实跑一遍
//   echo_hurt      同上压到相位阈值以下，等第一次预警圈出现、截图后再击杀
//   night / frame  DevForceNight；帧时间与 SkyIslandFrameProfile 分项计时（与 SKY_PERF_* 同一套）
//   shot / burst   截图与进程内像素检查（F3GameplayValidationAutotestCapture.cs）
//   assert         断言，结果进 manifest；求值在 F3GameplayValidationAutotestAsserts.cs，判据尽量交给纯函数（F3AutotestJudges）
// 面板开着时 timeScale = 0，所有等待一律用真实时间；每步结束收起面板与官方界面，免得下一步的交互读条永远走不完。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Dialogues;
using Duckov.UI;
using ItemStatsSystem;
using SodaCraft.Localizations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        #region 阶段与步骤

        private IEnumerator RunAutotestStages(string when)
        {
            if (_autotest == null || _autotest.Table == null) yield break;
            bool any = false;
            foreach (F3AutotestStage stage in _autotest.Table.Stages) if (stage.When == when) any = true;
            if (!any) yield break;
            bool switched = false;
            if (when == "alt_language")
            {
                switched = SwitchAutotestLanguage();
                Record("AUTOTEST_LANGUAGE_SWITCH", switched ? "PASS" : "FAIL", 0L, "alt=" + _autotest.Info.AltLanguage,
                    switched ? string.Empty : "language_switch_failed");
                if (switched) yield return WaitAutotestReal(2f);
            }
            foreach (F3AutotestStage stage in _autotest.Table.Stages)
            {
                if (stage.When != when) continue;
                _autotest.CurrentStageId = stage.Id;
                yield return RunAutotestStage(stage);
            }
            _autotest.CurrentStageId = null;
            if (switched)
            {
                string detail;
                bool restored = RestoreAutotestLanguage(out detail);
                Record("AUTOTEST_LANGUAGE_RESTORE", restored ? "PASS" : "FAIL", 0L, detail, restored ? string.Empty : "language_not_restored");
                yield return WaitAutotestReal(1.5f);
            }
        }

        private IEnumerator RunAutotestStage(F3AutotestStage stage)
        {
            foreach (F3AutotestStep step in _autotest.Table.Steps)
            {
                if (step.Stage != stage.Id) continue;
                yield return RunAutotestStep(step, stage);
            }
            WriteAutotestReport();
        }

        private string AutotestStepSkipReason(F3AutotestStage stage)
        {
            if (ShouldAbort()) return DescribeAbortReason();
            if (stage.When == "base_before" || stage.When == "base_after")
                return IsBaseScene() && SkyIslandSessionOrNull() == null ? null : "not_in_base";
            string gone;
            if (!SkyIslandSessionStillValid(out gone)) return gone;
            // 岛上的步骤会搬玩家、发物品、点选项写剧情：每一步开跑前再过一次写入门（槽位没换、快照已落盘）。
            string gate;
            if (!AutotestWriteAllowed(out gate)) return gate;
            if (stage.When == "story" && !_autotest.StagesApplied.Contains(stage.Id)) return "stage_not_applied";
            return null;
        }

        private IEnumerator RunAutotestStep(F3AutotestStep step, F3AutotestStage stage)
        {
            var record = new F3AutotestStepRecord
            {
                Id = step.Id, Title = step.Title, Stage = stage.Id, Location = step.Location, Class = step.Class,
                Language = LocalizationManager.CurrentLanguage.ToString(), StartedUtc = DateTime.UtcNow.ToString("O")
            };
            record.Checklist.AddRange(step.Checklist);
            record.Actions.AddRange(step.Actions);
            _autotest.Records.Add(record);
            Stopwatch sw = Stopwatch.StartNew();
            AutotestLog(step.Id, "begin", null);
            string skip = AutotestStepSkipReason(stage);
            if (skip != null)
            {
                record.Result = "SKIP";
                record.Reason = skip;
            }
            else
            {
                _autotest.StepFailed = false;
                _autotest.StepStartCounts = CountAutotestPack();
                _autotest.LastShot = null;
                _autotest.LastLoot = null;
                _autotest.FrameSampled = false;
                _autotest.LastFrameReason = null;
                _autotest.LastKilled = 0;
                _autotest.StaticProbes.Clear();
                ValidationCoroutineStack stack = new ValidationCoroutineStack(RunAutotestActions(record, step));
                string error = null;
                try
                {
                    while (!ShouldAbort())
                    {
                        object current;
                        Exception thrown;
                        bool more = TryStep(stack, out current, out thrown);
                        if (thrown != null) { error = "action_threw:" + thrown.GetType().Name + ":" + thrown.Message; break; }
                        if (sw.Elapsed.TotalSeconds > step.BudgetSeconds)
                        {
                            error = "step_budget_exceeded_" + step.BudgetSeconds.ToString("F0", CultureInfo.InvariantCulture) + "s";
                            break;
                        }
                        if (!more) break;
                        yield return current;
                    }
                }
                finally
                {
                    try { stack.Dispose(); }
                    catch (Exception e) { record.Notes.Add("dispose_threw:" + e.GetType().Name); }
                }
                int written = 0;
                foreach (F3AutotestShot shot in record.Shots) if (!string.IsNullOrEmpty(shot.File)) written++;
                if (error == null && ShouldAbort() && record.Assertions.Count == 0 && written == 0)
                {
                    record.Result = "SKIP";
                    record.Reason = DescribeAbortReason();
                }
                else
                {
                    record.Result = F3AutotestJudges.AggregateResult(record.Assertions, written, error != null);
                    record.Reason = error ?? FirstAutotestProblem(record);
                }
                // 步骤之间不留模态：剧情面板与官方对话把时间压到 0，下一步的交互读条会永远走不完。
                CloseAutotestPanels();
            }
            record.DurationMs = sw.ElapsedMilliseconds;
            Record(step.Id, record.Result, record.DurationMs, AutotestRecordMetrics(record), record.Reason ?? string.Empty);
            AutotestLog(step.Id, "end", record.Result);
        }

        private IEnumerator RunAutotestActions(F3AutotestStepRecord record, F3AutotestStep step)
        {
            for (int i = 0; i < step.Actions.Count; i++)
            {
                string action = step.Actions[i];
                string verb = F3AutotestJudges.VerbOf(action);
                string[] args = F3AutotestJudges.ArgsOf(action);
                if (_autotest.StepFailed)
                {
                    if (verb == "assert") record.Assertions.Add(AutotestAssertion("assert:" + string.Join(":", args), "SKIP", "earlier_action_failed", null));
                    else record.Notes.Add("skipped_after_failure:" + action);
                    continue;
                }
                if (verb == "assert")
                {
                    record.Assertions.Add(EvaluateAutotestAssert(args));
                    continue;
                }
                if (verb == "shot")
                {
                    yield return CaptureAutotestShot(record, Arg(args, 0), Arg(args, 1), Arg(args, 2),
                        AutotestProbeArgs(step, i, "contrast_min"), AutotestProbeArgs(step, i, "visible_min"));
                    continue;
                }
                if (verb == "burst")
                {
                    int count = Mathf.Clamp(ArgInt(args, 2, 3), 1, 12);
                    float interval = Mathf.Clamp(ArgFloat(args, 3, 0.1f), 0f, 5f);
                    for (int n = 0; n < count && !ShouldAbort(); n++)
                    {
                        yield return CaptureAutotestShot(record, Arg(args, 0) + "_" + n, Arg(args, 1), null, null, null);
                        if (interval > 0f) yield return WaitAutotestReal(interval);
                    }
                    continue;
                }
                IEnumerator run = RunAutotestVerb(record, verb, args);
                if (run != null) yield return run;
            }
        }

        /// <summary>这一张截图之后、下一张截图之前的断言里，<paramref name="assertName"/> 要取样的路径 / 目标：截图时只取这些，免得逐字逐像素分析整屏。</summary>
        private static List<string> AutotestProbeArgs(F3AutotestStep step, int shotIndex, string assertName)
        {
            var result = new List<string>();
            for (int i = shotIndex + 1; i < step.Actions.Count; i++)
            {
                string verb = F3AutotestJudges.VerbOf(step.Actions[i]);
                if (verb == "shot" || verb == "burst") break;
                if (verb != "assert") continue;
                string[] args = F3AutotestJudges.ArgsOf(step.Actions[i]);
                if (args.Length >= 2 && args[0] == assertName && !result.Contains(args[1])) result.Add(args[1]);
            }
            return result;
        }

        private IEnumerator RunAutotestVerb(F3AutotestStepRecord record, string verb, string[] args)
        {
            switch (verb)
            {
                case "teleport": return AutotestTeleport(record, args);
                case "teleport_view": return AutotestTeleportView(record, args);
                case "ring_replay": AutotestRingReplay(record, args); return null;
                case "wait_real": return WaitAutotestReal(ArgFloat(args, 0, 1f));
                case "interact": return AutotestInteract(record, args);
                case "wait_panel": return AutotestWaitUntil(record, "wait_panel", ArgFloat(args, 0, 3f), AutotestPanelOpen, ArgBool(args, 1, true));
                case "wait_dialogue": return AutotestWaitUntil(record, "wait_dialogue", ArgFloat(args, 0, 3f), AutotestDialogueOpen, ArgBool(args, 1, true));
                case "wait_dialogue_typed": return AutotestWaitDialogueTyped(record, ArgFloat(args, 0, 10f));
                case "dialogue_advance": return AutotestDialogueAdvance(record, args);
                case "dialogue_choose": return AutotestDialogueChoose(record, args);
                case "choose": return AutotestChoose(record, args, false);
                case "choose_label": return AutotestChoose(record, args, true);
                case "hover": return AutotestHover(record, args);
                case "close_panel": CloseAutotestPanels(); return WaitAutotestReal(0.4f);
                case "clear_nearby": return AutotestClearNearby(record, args, true);
                case "kill_nearby": return AutotestClearNearby(record, args, false);
                case "wait_quiet": return AutotestWaitUntil(record, "wait_quiet", ArgFloat(args, 0, 6f), AutotestQuiet, ArgBool(args, 1, false));
                case "invincible": AutotestInvincible(record, args); return null;
                case "night": AutotestNight(record, args); return WaitAutotestReal(ArgFloat(args, 1, 2.5f));
                case "give": AutotestGive(record, args, false); return WaitAutotestReal(0.3f);
                case "give_if_missing": AutotestGive(record, args, true); return WaitAutotestReal(0.3f);
                case "use_buff": AutotestUseBuff(record, args); return WaitAutotestReal(0.6f);
                case "use_item": AutotestUseItem(record, args); return WaitAutotestReal(0.6f);
                case "use_compass": AutotestUseCompass(record); return WaitAutotestReal(0.5f);
                case "click_close": AutotestClickClose(record); return WaitAutotestReal(0.5f);
                case "set_health": AutotestSetHealth(record, args); return null;
                case "spawn_gnats": AutotestSpawnGnats(record, args); return WaitAutotestReal(ArgFloat(args, 1, 2f));
                case "echo_hurt": return AutotestBossHurt(record, "storm", ArgFloat(args, 0, 0.75f), ArgFloat(args, 1, 15f));
                case "boss_hurt": return AutotestBossHurt(record, Arg(args, 0), ArgFloat(args, 1, 0.65f), ArgFloat(args, 2, 15f));
                case "wait_boss": return AutotestWaitBoss(record, args);
                case "wait_object": return AutotestWaitObject(record, args);
                case "wait_alpha": return AutotestWaitAlpha(record, args);
                case "caption": AutotestCaption(record, args); return WaitAutotestReal(0.3f);
                case "wait_caption": return AutotestWaitCaption(record, args);
                case "frame": return AutotestFrameSample(record, args);
                case "loot": return AutotestLoot(record, args);
                case "puzzle_solve": return AutotestPuzzleSolve(record, args);
                case "open_map": return AutotestOpenMap(record);
                case "close_view": AutotestCloseView(); return WaitAutotestReal(0.6f);
                case "open_modeg_confirm": AutotestOpenModeG(record); return WaitAutotestReal(1f);
                case "close_modeg_confirm": ModeGInteractable.CloseActiveConfirmation(); return WaitAutotestReal(0.3f);
                case "reachability": return AutotestCoroutineCase(record, "SKY_GATE_REACHABILITY", RunSkyIslandReachability);
                case "encounter_cap": return AutotestCoroutineCase(record, "SKY_ENCOUNTER_CAP", RunSkyIslandEncounterCap);
                default:
                    AutotestFail(record, "action:" + verb, "verb_not_implemented", null, true);
                    return null;
            }
        }

        #endregion

        #region 动作

        private IEnumerator WaitAutotestReal(float seconds)
        {
            float until = Time.realtimeSinceStartup + Mathf.Max(0f, seconds);
            while (Time.realtimeSinceStartup < until && !ShouldAbort()) yield return null;
        }

        private IEnumerator AutotestWaitUntil(F3AutotestStepRecord record, string label, float seconds, Func<bool> condition, bool critical)
        {
            float until = Time.realtimeSinceStartup + Mathf.Max(0.1f, seconds);
            bool met = false;
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                met = condition();
                if (met) break;
                yield return null;
            }
            if (!met) met = condition();
            if (!met) AutotestFail(record, "action:" + label, "timeout_" + seconds.ToString("F1", CultureInfo.InvariantCulture) + "s", null, critical);
        }

        private IEnumerator AutotestTeleport(F3AutotestStepRecord record, string[] args)
        {
            string target = Arg(args, 0);
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { AutotestFail(record, "action:teleport", "no_island_session", target, true); yield break; }
            CloseAutotestPanels();
            string reason;
            Transform marker = session.DevAutotestFind(target);
            if (!session.DevAutotestTeleport(marker, ArgFloat(args, 1, 0f), ArgFloat(args, 2, 0f), out reason))
            {
                AutotestFail(record, "action:teleport", reason, target, true);
                yield break;
            }
            record.Notes.Add("teleport=" + target);
            yield return WaitAutotestReal(ArgFloat(args, 3, 1.5f));
        }

        private IEnumerator AutotestInteract(F3AutotestStepRecord record, string[] args)
        {
            string target = string.Join(":", args);
            InteractableBase interactable = ResolveAutotestInteractable(target);
            if (interactable == null && (target.StartsWith("resident:", StringComparison.Ordinal) || target == "pigeon"))
            {
                // 居民不在岛上不是缺陷（已婚离岛由婚姻系统接管，SKY_RESIDENTS 分辨两种情况）；这一趟没有信鸽也不是（SKY_LETTER_PIGEON 判）。
                // 这一步记 SKIP，后面的动作不再做。
                record.Assertions.Add(AutotestAssertion("action:interact", "SKIP", "target_not_present_this_raid:" + target, null));
                _autotest.StepFailed = true;
                yield break;
            }
            if (interactable == null) { AutotestFail(record, "action:interact", "target_not_found", target, true); yield break; }
            CloseAutotestPanels();
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) { AutotestFail(record, "action:interact", "player_missing", target, true); yield break; }
            bool threw = false;
            try { player.Interact(interactable); }
            catch (Exception e)
            {
                threw = true;
                AutotestFail(record, "action:interact", "interact_threw:" + e.GetType().Name, target, true);
            }
            if (threw) yield break;
            bool started = false;
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until && interactable != null)
            {
                if (interactable.Interacting) { started = true; break; }
                if (AutotestPanelOpen() || DialogueManager.IsDialogueActive) break;
                yield return null;
            }
            float interactTime = interactable == null ? 0f : interactable.InteractTime;
            until = Time.realtimeSinceStartup + interactTime + 5f;
            while (interactable != null && interactable.Interacting && Time.realtimeSinceStartup < until) yield return null;
            record.Notes.Add("interact=" + target + ",started=" + started + ",interact_time=" + interactTime.ToString("F2", CultureInfo.InvariantCulture));
            yield return WaitAutotestReal(0.6f);
        }

        private InteractableBase ResolveAutotestInteractable(string target)
        {
            if (target == "departure") return FindAutotestDeparture();
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null || string.IsNullOrEmpty(target)) return null;
            if (target.StartsWith("resident:", StringComparison.Ordinal))
            {
                // 按居民 id 找：交互名在居民生成时按当时的语言写死，换语言复拍后按名字匹配找不到人。
                string npcId = target.Substring("resident:".Length);
                foreach (SkyIslandResidentInteractable candidate in UnityEngine.Object.FindObjectsOfType<SkyIslandResidentInteractable>())
                    if (candidate != null && candidate.isActiveAndEnabled && candidate.NpcId == npcId) return candidate;
                return null;
            }
            Transform point;
            if (target.StartsWith("gather:", StringComparison.Ordinal)) point = session.DevAutotestFind("SkyIslandGather_" + target.Substring("gather:".Length));
            else if (target.StartsWith("completed:", StringComparison.Ordinal)) point = session.DevAutotestFind(target.Substring("completed:".Length) + "_Completed");
            else if (target == "pigeon") point = FindAutotestWorldChild("SkyIslandPigeon_");
            else point = session.DevAutotestFind(target);
            if (point == null) return null;
            InteractableBase direct = point.GetComponent<InteractableBase>();
            return direct != null ? direct : point.GetComponentInChildren<InteractableBase>();
        }

        private Transform FindAutotestWorldChild(string prefix)
        {
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            if (root == null) return null;
            foreach (Transform child in root.transform)
                if (child != null && child.gameObject.activeInHierarchy && child.name.StartsWith(prefix, StringComparison.Ordinal)) return child;
            return null;
        }

        private IEnumerator AutotestDialogueAdvance(F3AutotestStepRecord record, string[] args)
        {
            float until = Time.realtimeSinceStartup + ArgFloat(args, 0, 25f);
            int confirms = 0;
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                if (!DialogueManager.IsDialogueActive) break;
                if (AutotestDialogueChoices().Count > 0) break;
                if (DialogueUI.instance != null)
                {
                    DialogueUI.instance.Confirm();
                    confirms++;
                }
                yield return WaitAutotestReal(0.45f);
            }
            record.Notes.Add("dialogue_confirms=" + confirms + ",active=" + DialogueManager.IsDialogueActive
                + ",choices=" + AutotestDialogueChoices().Count);
        }

        private static List<DialogueUIChoice> AutotestDialogueChoices()
        {
            var choices = new List<DialogueUIChoice>();
            if (DialogueUI.instance == null) return choices;
            foreach (DialogueUIChoice choice in DialogueUI.instance.GetComponentsInChildren<DialogueUIChoice>(false))
                if (choice != null) choices.Add(choice);
            return choices;
        }

        /// <summary>找不到官方选项时把对话框各层的显隐记进报告（首轮实测：选项不渲染、也找不到，只知道对话「在进行」）。</summary>
        private static string DescribeAutotestDialogueUi()
        {
            DialogueUI ui = DialogueUI.instance;
            if (ui == null) return "dialogue_ui=null";
            var parts = new List<string> { "manager_active=" + DialogueManager.IsDialogueActive, "ui_active=" + DialogueUI.Active };
            bool? waitingNow = OfficialDialogueWaitingForChoice();
            parts.Add("waiting_for_choice=" + (waitingNow.HasValue ? waitingNow.Value.ToString() : "unknown"));
            bool? shownNow = OfficialDialogueLineShown();
            parts.Add("line_shown=" + (shownNow.HasValue ? shownNow.Value.ToString() : "unknown"));
            TMP_Text lineText = OfficialDialogueText();
            if (lineText != null)
                parts.Add("visible=" + lineText.maxVisibleCharacters + "/" + (lineText.textInfo == null ? 0 : lineText.textInfo.characterCount)
                    + ",line=" + AutotestShort(lineText.text, 40));
            foreach (string name in new[] { "mainFadeGroup", "textAreaFadeGroup", "choiceListFadeGroup" })
            {
                Duckov.UI.Animations.FadeGroup group = null;
                try { group = typeof(DialogueUI).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(ui) as Duckov.UI.Animations.FadeGroup; }
                catch (Exception) { }
                parts.Add(group == null ? name + "=null" : name + "=shown:" + group.IsShown + "/hiding:" + group.IsHidingInProgress
                    + "/active:" + group.gameObject.activeInHierarchy);
            }
            int active = 0, inactive = 0;
            foreach (DialogueUIChoice choice in Resources.FindObjectsOfTypeAll<DialogueUIChoice>())
            {
                if (choice == null || !choice.gameObject.scene.IsValid()) continue;
                if (choice.gameObject.activeInHierarchy) active++; else inactive++;
            }
            parts.Add("choices_active=" + active + ",choices_inactive=" + inactive);
            return string.Join(",", parts.ToArray());
        }

        private IEnumerator AutotestDialogueChoose(F3AutotestStepRecord record, string[] args)
        {
            int index = ArgInt(args, 0, 0);
            DialogueUIChoice target = null;
            bool? waiting = null;
            float until = Time.realtimeSinceStartup + ArgFloat(args, 1, 3f);
            // 官方先激活并淡入选项，淡入完才进 WaitForChoice 把 confirmedChoice 清成 -1：见到选项就点会被清掉、对话一直挂着
            // （2026-09-15 第三轮：浮舟、晴禾、折翎、英文苇白都点丢了）。等官方真在等玩家选再点。
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                target = null;
                foreach (DialogueUIChoice choice in AutotestDialogueChoices()) if (choice.Index == index) target = choice;
                waiting = OfficialDialogueWaitingForChoice();
                if (target != null && waiting != false) break;
                yield return null;
            }
            if (target == null) { AutotestFail(record, "action:dialogue_choose", "official_choice_not_found:" + index, DescribeAutotestDialogueUi(), true); yield break; }
            if (waiting == false) { AutotestFail(record, "action:dialogue_choose", "official_not_waiting_for_choice:" + index, DescribeAutotestDialogueUi(), true); yield break; }
            // 点完要确认被官方吃掉（waitingForChoice 落回 false）：1 秒内没吃掉就再点，最多 3 次。字段取不到时只点一次（旧做法）。
            int clicks = 0;
            do
            {
                target.OnPointerClick(null);
                clicks++;
                if (waiting == null) break;
                float consumedUntil = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < consumedUntil && OfficialDialogueWaitingForChoice() == true) yield return null;
            }
            while (clicks < 3 && OfficialDialogueWaitingForChoice() == true && !ShouldAbort());
            record.Notes.Add("dialogue_choice=" + index + ",clicks=" + clicks);
            if (waiting != null && OfficialDialogueWaitingForChoice() == true)
            { AutotestFail(record, "action:dialogue_choose", "choice_not_consumed:" + index, DescribeAutotestDialogueUi(), true); yield break; }
            yield return WaitAutotestReal(0.6f);
        }

        private IEnumerator AutotestChoose(F3AutotestStepRecord record, string[] args, bool byLabel)
        {
            string label = byLabel ? "action:choose_label" : "action:choose";
            List<Button> rows = AutotestPanelRows();
            int index = -1;
            if (byLabel)
            {
                string[] alternatives = AutotestAlternatives(Arg(args, 0));
                for (int i = 0; i < rows.Count && index < 0; i++)
                    if (AutotestContainsAny(AutotestRowLabel(rows[i]), alternatives)) index = i;
            }
            else index = ArgInt(args, 0, -1);
            if (index < 0 || index >= rows.Count)
            {
                // 第三个参数 soft：这一行挂不挂取决于现场（例如交火中面板根本打不开），找不到记 SKIP，后面的动作照做。
                if (Arg(args, 2) == "soft")
                {
                    record.Assertions.Add(AutotestAssertion(label, "SKIP", "choice_not_offered:" + Arg(args, 0),
                        "rows=" + rows.Count + ",labels=" + AutotestRowLabels(rows)));
                    yield break;
                }
                AutotestFail(record, label, "choice_not_found:" + Arg(args, 0), "rows=" + rows.Count + ",labels=" + AutotestRowLabels(rows), true);
                yield break;
            }
            string chosen = AutotestRowLabel(rows[index]);
            bool threw = false;
            try { rows[index].onClick.Invoke(); }
            catch (Exception e)
            {
                threw = true;
                AutotestFail(record, label, "click_threw:" + e.GetType().Name + ":" + e.Message, chosen, true);
            }
            if (threw) yield break;
            record.Notes.Add("chose=" + index + ":" + AutotestShort(chosen, 60));
            yield return WaitAutotestReal(ArgFloat(args, 1, 0.7f));
        }

        private IEnumerator AutotestHover(F3AutotestStepRecord record, string[] args)
        {
            List<Button> rows = AutotestPanelRows();
            int index = ArgInt(args, 0, 0);
            if (index < 0 || index >= rows.Count) { AutotestFail(record, "action:hover", "row_not_found:" + index, "rows=" + rows.Count, true); yield break; }
            ExecuteEvents.Execute(rows[index].gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            record.Notes.Add("hover=" + index);
            yield return WaitAutotestReal(0.4f);
        }

        private IEnumerator AutotestClearNearby(F3AutotestStepRecord record, string[] args, bool untilQuiet)
        {
            float radius = ArgFloat(args, 0, 45f);
            float until = Time.realtimeSinceStartup + ArgFloat(args, 1, untilQuiet ? 25f : 2f);
            int killed = 0, passes = 0;
            string gate;
            if (!AutotestWriteAllowed(out gate)) { AutotestFail(record, "action:clear_nearby", gate, null, true); yield break; }
            SetAutotestInvincible(true);
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                killed += KillAutotestHostilesWithin(radius);
                passes++;
                yield return WaitAutotestReal(0.8f);
                if (!untilQuiet) break;
                if (AutotestQuiet() && CountAutotestHostilesWithin(radius) == 0) break;
            }
            bool quiet = AutotestQuiet();
            _autotest.LastKilled = killed;
            record.Notes.Add("killed=" + killed + ",passes=" + passes + ",quiet=" + quiet + ",radius=" + radius);
            if (untilQuiet && !quiet) AutotestFail(record, "action:clear_nearby", "threats_remain_within_story_quiet_radius", "killed=" + killed, false);
        }

        private static int CountAutotestHostilesWithin(float radius)
        {
            int count = 0;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return 0;
            foreach (CharacterMainControl character in UnityEngine.Object.FindObjectsOfType<CharacterMainControl>())
                if (IsAutotestHostileWithin(character, player, radius)) count++;
            return count;
        }

        private static bool IsAutotestHostileWithin(CharacterMainControl character, CharacterMainControl player, float radius)
        {
            if (character == null || character == player || character.Health == null || character.Health.IsDead) return false;
            if (PetNestCompanionAgent.IsCompanionCharacter(character) || !Team.IsEnemy(Teams.player, character.Team)) return false;
            return Vector3.Distance(character.transform.position, player.transform.position) <= radius;
        }

        /// <summary>以玩家为伤害来源走官方 Health.Hurt：死亡事件、遭遇清场、委托记账与掉落照常结算。</summary>
        private static int KillAutotestHostilesWithin(float radius)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return 0;
            int killed = 0;
            foreach (CharacterMainControl character in UnityEngine.Object.FindObjectsOfType<CharacterMainControl>())
            {
                if (!IsAutotestHostileWithin(character, player, radius)) continue;
                try
                {
                    DamageInfo damage = new DamageInfo(player);
                    damage.damageValue = character.Health.MaxHealth * 20f;
                    damage.ignoreArmor = true;
                    damage.toDamageReceiver = character.mainDamageReceiver;
                    damage.damagePoint = character.transform.position;
                    character.Health.SetInvincible(false);
                    character.Health.Hurt(damage);
                    if (character.Health.IsDead) killed++;
                }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 自动验收击杀失败: " + e.Message); }
            }
            return killed;
        }

        private void AutotestInvincible(F3AutotestStepRecord record, string[] args)
        {
            string mode = Arg(args, 0);
            if (mode == "restore")
            {
                foreach (KeyValuePair<Health, bool> pair in _autotest.InvincibleOriginal) if (pair.Key != null) pair.Key.SetInvincible(pair.Value);
                _autotest.InvincibleOriginal.Clear();
            }
            else SetAutotestInvincible(mode != "off");
            record.Notes.Add("invincible=" + mode);
        }

        private void AutotestNight(F3AutotestStepRecord record, string[] args)
        {
            bool original = _autotest.Snapshot != null && _autotest.Snapshot.ForceNight;
            // on 强制夜里、off 关掉、restore 回到开跑前的值（快照里记着）。
            string mode = Arg(args, 0);
            SkyIslandNight.DevForceNight = mode == "on" || (mode == "restore" && original);
            record.Notes.Add("force_night=" + SkyIslandNight.DevForceNight);
        }

        /// <summary><c>give:TypeID:件数</c> 发进背包；<c>give_if_missing</c> 只补到背包顶层有这么多件（不重复发纪念品）。</summary>
        private void AutotestGive(F3AutotestStepRecord record, string[] args, bool onlyMissing)
        {
            int typeId = ArgInt(args, 0, 0), count = Mathf.Clamp(ArgInt(args, 1, 1), 1, 50);
            if (onlyMissing) count -= ItemFactory.GetItemCountInInventory(typeId);
            if (count <= 0) { record.Notes.Add("give_skipped_already_in_pack=" + typeId); return; }
            string reason;
            bool ok = GiveAutotestItems(typeId, count, out reason);
            record.Notes.Add("give=" + typeId + "x" + count + (ok ? string.Empty : "(" + reason + ")"));
            if (!ok) AutotestFail(record, "action:give", reason, null, true);
        }

        /// <summary><c>use_buff:效果[:false]</c> 走局内 owner 的 UseConsumable（与耗材的使用行为同一个入口）；第二个参数 false 表示期望被拒（护符不叠加、航徽每趟一次）。</summary>
        private void AutotestUseBuff(F3AutotestStepRecord record, string[] args)
        {
            SkyIslandFieldcraft field = SkyIslandFieldcraft.Current;
            SkyIslandFieldBuff buff;
            try { buff = (SkyIslandFieldBuff)Enum.Parse(typeof(SkyIslandFieldBuff), Arg(args, 0), false); }
            catch (Exception) { AutotestFail(record, "action:use_buff", "buff_unknown:" + Arg(args, 0), null, true); return; }
            bool expected = ArgBool(args, 1, true);
            CharacterMainControl player = CharacterMainControl.Main;
            Health health = player == null ? null : player.Health;
            float maxBefore = health == null ? -1f : health.MaxHealth;
            bool used = field != null && field.UseConsumable(buff);
            record.Assertions.Add(AutotestAssertion("action:use_buff:" + buff + (expected ? string.Empty : ":expect_rejected"),
                used == expected ? "PASS" : "FAIL", used == expected ? null : (used ? "use_accepted_but_rejection_expected" : "use_rejected"),
                "fieldcraft=" + (field != null) + ",used=" + used + ",max_health=" + maxBefore.ToString("F1", CultureInfo.InvariantCulture)
                + "->" + (health == null ? -1f : health.MaxHealth).ToString("F1", CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// <c>use_item:TypeID[:heal]</c>：取背包顶层这件物品，按官方 UsageUtilities 的口径逐个问使用行为「能不能用」、能用的调 Use
        /// （不经 CA_UseItem，所以不扣堆叠）。第二个参数 heal 表示期望生命上涨。每个行为的可用性与生命前后写进 metrics。
        /// </summary>
        private void AutotestUseItem(F3AutotestStepRecord record, string[] args)
        {
            int typeId = ArgInt(args, 0, 0);
            CharacterMainControl player = CharacterMainControl.Main;
            Item item = FindAutotestPackItem(typeId);
            if (player == null || item == null) { AutotestFail(record, "action:use_item", "item_not_in_pack:" + typeId, null, true); return; }
            UsageUtilities usage = item.GetComponent<UsageUtilities>();
            if (usage == null || usage.behaviors == null) { AutotestFail(record, "action:use_item", "no_usage_behaviors:" + typeId, null, true); return; }
            Health health = player.Health;
            float before = health == null ? -1f : health.CurrentHealth;
            var parts = new List<string>();
            int used = 0;
            foreach (UsageBehavior behavior in new List<UsageBehavior>(usage.behaviors))
            {
                if (behavior == null) continue;
                bool can;
                try { can = behavior.CanBeUsed(item, player); }
                catch (Exception e) { parts.Add(behavior.GetType().Name + "=can_threw:" + e.GetType().Name); continue; }
                parts.Add(behavior.GetType().Name + "=" + can);
                if (!can) continue;
                try { behavior.Use(item, player); used++; }
                catch (Exception e) { parts.Add(behavior.GetType().Name + "=use_threw:" + e.GetType().Name); }
            }
            float after = health == null ? -1f : health.CurrentHealth;
            bool wantHeal = Arg(args, 1) == "heal";
            bool ok = !wantHeal || after > before + 0.01f;
            record.Assertions.Add(AutotestAssertion("action:use_item:" + typeId + (wantHeal ? ":heal" : string.Empty), ok ? "PASS" : "FAIL",
                ok ? null : "no_heal_observed", "behaviors=" + string.Join("+", parts.ToArray()) + ",used=" + used
                + ",health=" + before.ToString("F1", CultureInfo.InvariantCulture) + "->" + after.ToString("F1", CultureInfo.InvariantCulture)));
        }

        private static Item FindAutotestPackItem(int typeId)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Inventory inventory = player == null || player.CharacterItem == null ? null : player.CharacterItem.Inventory;
            if (inventory == null || inventory.Content == null) return null;
            foreach (Item item in inventory.Content) if (item != null && item.TypeID == typeId) return item;
            return null;
        }

        private void AutotestUseCompass(F3AutotestStepRecord record)
        {
            SkyIslandSession session = SkyIslandSessionOrNull();
            bool used = session != null && session.UseCompass();
            record.Assertions.Add(AutotestAssertion("action:use_compass", used ? "PASS" : "FAIL", used ? null : "compass_reading_unavailable", null));
        }

        /// <summary>点剧情面板右上角的 ESC 键帽（面板里除选项行之外唯一的按钮）：与鼠标点它、按 ESC 同一个 Dispose。</summary>
        private void AutotestClickClose(F3AutotestStepRecord record)
        {
            GameObject root = AutotestPanelRoot();
            if (root == null) { AutotestFail(record, "action:click_close", "panel_not_open", null, true); return; }
            foreach (Button button in root.GetComponentsInChildren<Button>(false))
            {
                if (button == null || button.gameObject.name == "Choice") continue;
                button.onClick.Invoke();
                record.Notes.Add("clicked_close=" + button.gameObject.name);
                return;
            }
            AutotestFail(record, "action:click_close", "close_button_not_found", null, true);
        }

        private void AutotestSetHealth(F3AutotestStepRecord record, string[] args)
        {
            string gate;
            if (!AutotestWriteAllowed(out gate)) { AutotestFail(record, "action:set_health", gate, null, true); return; }
            SetAutotestHealthFraction(ArgFloat(args, 0, 1f));
            record.Notes.Add("health_fraction=" + ArgFloat(args, 0, 1f).ToString("F2", CultureInfo.InvariantCulture));
        }

        private void AutotestSpawnGnats(F3AutotestStepRecord record, string[] args)
        {
            SkyIslandFieldcraft field = SkyIslandFieldcraft.Current;
            SkyIslandGnats swarm = field == null ? null : field.Gnats;
            CharacterMainControl player = CharacterMainControl.Main;
            if (swarm == null || !swarm.Usable || player == null) { AutotestFail(record, "action:spawn_gnats", "swarm_unusable", null, false); return; }
            int placed = swarm.DevSpawnAround(player.transform.position, ArgInt(args, 0, SkyIslandMosquitoRules.MaxAlive));
            record.Notes.Add("gnats_spawned=" + placed + ",alive=" + swarm.Alive);
        }

        /// <summary>
        /// <c>boss_hurt:种类:血线:秒数</c>（<c>echo_hurt:血线:秒数</c> 是噬风的旧写法）：以玩家为伤害来源把最近的这类 Boss 压到血线以下，
        /// 让相位机制（噬风预警圈、残星匠首供能桩）按真实判定触发，之后再截图、击杀。
        /// </summary>
        private IEnumerator AutotestBossHurt(F3AutotestStepRecord record, string kind, float requestedFraction, float seconds)
        {
            float fraction = Mathf.Clamp(requestedFraction, 0.05f, 0.99f);
            string label = "action:boss_hurt:" + kind;
            CharacterMainControl boss = null;
            float until = Time.realtimeSinceStartup + seconds;
            while (boss == null && Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                boss = FindAutotestBoss(kind, AutotestBossSearchRadius);
                if (boss == null) yield return WaitAutotestReal(0.3f);
            }
            if (boss == null) { AutotestFail(record, label, "boss_not_found:" + kind, null, true); yield break; }
            CharacterMainControl player = CharacterMainControl.Main;
            for (int attempt = 0; attempt < 5 && boss != null && boss.Health != null && !boss.Health.IsDead; attempt++)
            {
                float max = boss.Health.MaxHealth, current = boss.Health.CurrentHealth;
                if (current <= max * fraction) break;
                try
                {
                    DamageInfo damage = new DamageInfo(player);
                    damage.damageValue = current - max * fraction + 1f;
                    damage.ignoreArmor = true;
                    damage.toDamageReceiver = boss.mainDamageReceiver;
                    damage.damagePoint = boss.transform.position;
                    boss.Health.Hurt(damage);
                }
                catch (Exception e) { record.Notes.Add("echo_hurt_threw:" + e.GetType().Name); break; }
                yield return WaitAutotestReal(0.3f);
            }
            float after = boss == null || boss.Health == null ? -1f : boss.Health.CurrentHealth / Mathf.Max(1f, boss.Health.MaxHealth);
            record.Notes.Add("storm_health_fraction=" + after.ToString("F2", CultureInfo.InvariantCulture));
            if (after < 0f || after > fraction + 0.02f)
                // 不致命：没压到血线时相位机制不会触发，后面的断言照样记红，但击杀与截图还要跑，metrics 里的护盾与血量才看得到。
                AutotestFail(record, label, "fraction_not_reached", "fraction=" + after.ToString("F2", CultureInfo.InvariantCulture), false);
        }

        private IEnumerator AutotestWaitObject(F3AutotestStepRecord record, string[] args)
        {
            string name = Arg(args, 0);
            float until = Time.realtimeSinceStartup + ArgFloat(args, 1, 5f);
            GameObject found = null;
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                found = GameObject.Find(name);
                if (found != null) break;
                yield return null;
            }
            if (found == null) { AutotestFail(record, "action:wait_object", "object_not_seen:" + name, null, ArgBool(args, 2, true)); yield break; }
            _autotest.StaticProbes[name] = found.transform.position;
            record.Notes.Add("object_seen=" + name + "@" + found.transform.position);
        }

        private IEnumerator AutotestWaitAlpha(F3AutotestStepRecord record, string[] args)
        {
            string name = Arg(args, 0);
            float min = ArgFloat(args, 1, 0.9f);
            float until = Time.realtimeSinceStartup + ArgFloat(args, 2, 5f);
            float alpha = 0f;
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                alpha = AutotestEffectiveAlpha(name);
                if (alpha >= min) break;
                yield return null;
            }
            record.Notes.Add("alpha(" + name + ")=" + alpha.ToString("F2", CultureInfo.InvariantCulture));
            if (alpha < min) AutotestFail(record, "action:wait_alpha", "alpha_below_min:" + name, "alpha=" + alpha.ToString("F2", CultureInfo.InvariantCulture), ArgBool(args, 3, true));
        }

        private static float AutotestEffectiveAlpha(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            if (go == null) return 0f;
            float alpha = 1f;
            foreach (CanvasGroup group in go.GetComponentsInParent<CanvasGroup>()) alpha *= group.alpha;
            return alpha;
        }

        private void AutotestCaption(F3AutotestStepRecord record, string[] args)
        {
            SkyIslandSession session = SkyIslandSessionOrNull();
            string key = Arg(args, 0), text;
            switch (key)
            {
                case "storm_outcome": text = SkyIslandStoryRules.CombatOutcome(SkyIslandStoryFlag.StormSlain); break;
                case "echo_opened": text = SkyIslandStormEchoRules.Opened; break;
                default: text = null; break;
            }
            if (session == null || text == null)
            {
                AutotestFail(record, "action:caption", session == null ? "no_island_session" : "caption_key_unknown:" + key, null, true);
                return;
            }
            session.Announce(text, ArgBool(args, 1, false));
            record.Notes.Add("caption=" + key);
        }

        private IEnumerator AutotestWaitCaption(F3AutotestStepRecord record, string[] args)
        {
            string[] alternatives = AutotestAlternatives(Arg(args, 0));
            float until = Time.realtimeSinceStartup + ArgFloat(args, 1, 5f);
            string seen = null;
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                string text = AutotestCaptionText();
                if (text != null && AutotestEffectiveAlpha("SkyIslandCaption") > 0.5f && (alternatives.Length == 0 || AutotestContainsAny(text, alternatives)))
                {
                    seen = text;
                    break;
                }
                yield return null;
            }
            // 第三个参数 optional：没等到记 SKIP（这一趟不一定发生，例如没有信鸽），不记 FAIL。
            bool optional = Arg(args, 2) == "optional";
            record.Assertions.Add(AutotestAssertion("action:wait_caption", seen != null ? "PASS" : optional ? "SKIP" : "FAIL",
                seen != null ? null : "caption_not_seen", "want=" + Arg(args, 0) + ",last=" + AutotestShort(AutotestCaptionText(), 120)));
        }

        private static string AutotestCaptionText()
        {
            GameObject caption = GameObject.Find("SkyIslandCaption");
            TextMeshProUGUI text = caption == null ? null : caption.GetComponentInChildren<TextMeshProUGUI>(false);
            return text == null ? null : text.text;
        }

        private IEnumerator AutotestFrameSample(F3AutotestStepRecord record, string[] args)
        {
            float seconds = Mathf.Clamp(ArgFloat(args, 0, 5f), 1f, 30f);
            // 录制窗口只由 F3 运行时用例开（SkyIslandFrameProfileGuard）：岛上这一段 _skyIslandMode 为真，这里借它的开窗入口。
            BeginSkyIslandFrameProfile();
            var frames = new List<float>(1024);
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                float ms = Time.unscaledDeltaTime * 1000f;
                if (ms > 0f) frames.Add(ms);
                yield return null;
            }
            frames.Sort();
            float max;
            float p95 = FrameP95(frames, out max);
            string profileReason;
            string profile = SkyIslandFrameProfileMetrics(out profileReason);
            SkyIslandSession session = SkyIslandSessionOrNull();
            _autotest.FrameSampled = true;
            _autotest.LastFrameReason = profileReason;
            record.Assertions.Add(AutotestAssertion("frame", profileReason == null ? "PASS" : "FAIL", profileReason,
                "samples=" + frames.Count + ",p95_ms=" + p95.ToString("F2", CultureInfo.InvariantCulture)
                + ",max_ms=" + max.ToString("F2", CultureInfo.InvariantCulture)
                + ",living_enemies=" + (session == null ? -1 : session.ValidationLivingEnemies)
                + ",night=" + SkyIslandNight.IsNight(SkyIslandLighting.ClockHours()) + profile));
        }

        private IEnumerator AutotestLoot(F3AutotestStepRecord record, string[] args)
        {
            string prefix = Arg(args, 0);
            float until = Time.realtimeSinceStartup + ArgFloat(args, 1, 8f);
            InteractableLootbox box = null;
            while (box == null && Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                box = NearestAutotestLootbox(prefix);
                if (box == null) yield return WaitAutotestReal(0.3f);
            }
            if (box == null) { AutotestFail(record, "action:loot", "crate_not_found:" + prefix, null, true); yield break; }
            var counts = new Dictionary<int, int>();
            var parts = new List<string>();
            try
            {
                if (box.Inventory != null && box.Inventory.Content != null)
                {
                    foreach (Item item in box.Inventory.Content)
                    {
                        if (item == null) continue;
                        int units = item.Stackable ? Math.Max(1, item.StackCount) : 1, existing;
                        counts.TryGetValue(item.TypeID, out existing);
                        counts[item.TypeID] = existing + units;
                    }
                }
            }
            catch (Exception e) { record.Notes.Add("loot_read_threw:" + e.GetType().Name); }
            foreach (KeyValuePair<int, int> pair in counts) parts.Add(pair.Key + "x" + pair.Value);
            _autotest.LastLoot = counts;
            record.Assertions.Add(AutotestAssertion("action:loot", counts.Count > 0 ? "PASS" : "FAIL", counts.Count > 0 ? null : "crate_empty",
                "crate=" + box.gameObject.name + ",contents=" + string.Join("+", parts.ToArray())));
        }

        private static InteractableLootbox NearestAutotestLootbox(string prefix)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            InteractableLootbox best = null;
            float bestDistance = float.MaxValue;
            foreach (InteractableLootbox box in UnityEngine.Object.FindObjectsOfType<InteractableLootbox>())
            {
                if (box == null) continue;
                Transform parent = box.transform.parent;
                bool named = box.gameObject.name.StartsWith(prefix, StringComparison.Ordinal)
                    || (parent != null && parent.name.StartsWith(prefix, StringComparison.Ordinal));
                if (!named) continue;
                float distance = player == null ? 0f : Vector3.Distance(player.transform.position, box.transform.position);
                if (distance < bestDistance) { bestDistance = distance; best = box; }
            }
            return best;
        }

        /// <summary>
        /// 解秘境谜题：按生产谜题表（SkyIslandPuzzles）点正确选项。<c>puzzle_solve:标记:1</c> 表示第一步先故意选错两次，
        /// 断言第一次给提示、第二次点破、面板停在原步。解开后断言剧情旗标已写。
        /// </summary>
        private IEnumerator AutotestPuzzleSolve(F3AutotestStepRecord record, string[] args)
        {
            string marker = Arg(args, 0);
            bool wrongFirst = Arg(args, 1) == "1";
            SkyIslandPuzzle puzzle = SkyIslandPuzzles.For(marker);
            if (puzzle == null) { AutotestFail(record, "action:puzzle_solve", "puzzle_unknown:" + marker, null, true); yield break; }
            for (int index = 0; index < puzzle.Steps.Length && !ShouldAbort(); index++)
            {
                SkyIslandPuzzleStep step = puzzle.Steps[index];
                if (wrongFirst && index == 0 && step.Options.Length > 1)
                {
                    int wrong = step.Answer == 0 ? 1 : 0;
                    for (int miss = 0; miss < 2; miss++)
                    {
                        if (!PressAutotestRowByLabel(step.Options[wrong].Label)) { AutotestFail(record, "action:puzzle_solve", "wrong_option_row_missing", step.Options[wrong].Label + " | " + DescribeAutotestPanelRows(), true); yield break; }
                        yield return WaitAutotestReal(0.6f);
                        string body = AutotestPanelBody();
                        string expected = miss == 0 ? step.Hint : step.Reveal;
                        // 取提示原文的前 10 个字比对；AutotestShort 截断时会补「…」，原文里没有，拿它比永远找不到。
                        bool shown = body != null && expected != null && body.IndexOf(expected.Substring(0, Math.Min(10, expected.Length)), StringComparison.Ordinal) >= 0;
                        record.Assertions.Add(AutotestAssertion("puzzle_wrong_" + (miss + 1), AutotestPanelOpen() && shown ? "PASS" : "FAIL",
                            AutotestPanelOpen() && shown ? null : "feedback_missing_or_panel_closed", "body=" + AutotestShort(body, 80)));
                    }
                }
                if (!PressAutotestRowByLabel(step.Options[step.Answer].Label))
                {
                    AutotestFail(record, "action:puzzle_solve", "answer_row_missing:step" + index, step.Options[step.Answer].Label + " | " + DescribeAutotestPanelRows(), true);
                    yield break;
                }
                yield return WaitAutotestReal(0.8f);
            }
            SkyIslandSession session = SkyIslandSessionOrNull();
            SkyIslandStoryService story = session == null ? null : session.ValidationStory;
            bool flagged = story != null && story.Current.Has(puzzle.Flag);
            record.Assertions.Add(AutotestAssertion("puzzle_solved_flag:" + puzzle.Flag, flagged ? "PASS" : "FAIL", flagged ? null : "flag_not_written", marker));
        }

        private static string DescribeAutotestPanelRows()
        {
            List<Button> rows = AutotestPanelRows();
            return "panel=" + AutotestPanelOpen() + ",rows=" + rows.Count + ",labels=" + AutotestRowLabels(rows);
        }

        private static bool PressAutotestRowByLabel(string label)
        {
            foreach (Button row in AutotestPanelRows())
            {
                if (AutotestRowLabel(row).IndexOf(label, StringComparison.Ordinal) < 0) continue;
                row.onClick.Invoke();
                return true;
            }
            return false;
        }

        private IEnumerator AutotestOpenMap(F3AutotestStepRecord record)
        {
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { AutotestFail(record, "action:open_map", "no_island_session", null, true); yield break; }
            CloseAutotestPanels();
            session.OpenMap();
            float until = Time.realtimeSinceStartup + 3f;
            while (View.ActiveView == null && Time.realtimeSinceStartup < until) yield return null;
            record.Notes.Add("map_view=" + (View.ActiveView == null ? "none" : View.ActiveView.GetType().Name));
            if (View.ActiveView == null) AutotestFail(record, "action:open_map", "official_map_view_not_open", null, false);
            yield return WaitAutotestReal(1.5f);
        }

        private static void AutotestCloseView()
        {
            try { if (View.ActiveView != null) View.ActiveView.Close(); }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 关闭官方界面失败: " + e.Message); }
        }

        private void AutotestOpenModeG(F3AutotestStepRecord record)
        {
            bool opened = false;
            string error = null;
            try { opened = ModeGInteractable.TryOpenConfirmation(_host); }
            catch (Exception e) { error = e.GetType().Name; }
            record.Assertions.Add(AutotestAssertion("action:open_modeg_confirm", opened ? "PASS" : "SKIP",
                opened ? null : (error ?? "confirmation_not_available_in_this_scene"), "scene=" + SceneManager.GetActiveScene().name));
        }

        private void CloseAutotestPanels()
        {
            try { if (_host != null) SkyIslandSession.HideMapBeforeF3(_host); }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 收起剧情面板失败: " + e.Message); }
            AutotestCloseView();
            try { ModeGInteractable.CloseActiveConfirmation(); }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 收起 Mode G 确认页失败: " + e.Message); }
        }

        private IEnumerator AutotestCoroutineCase(F3AutotestStepRecord record, string caseKey, Func<IEnumerator> factory)
        {
            int failed = _failedIds.Count, skipped = _skippedIds.Count, passed = _passed;
            yield return RunSkyIslandCase(caseKey, factory);
            string result = _failedIds.Count > failed ? "FAIL" : _skippedIds.Count > skipped ? "SKIP" : _passed > passed ? "PASS" : "FAIL";
            record.Assertions.Add(AutotestAssertion("case:" + caseKey, result, result == "PASS" ? null : "see_report_line",
                LastAutotestReportLine(caseKey)));
        }

        #endregion

        #region 小工具

        private static bool AutotestPanelOpen() { return AutotestPanelRoot() != null; }
        private static bool AutotestDialogueOpen() { return DialogueManager.IsDialogueActive; }

        private bool AutotestQuiet()
        {
            SkyIslandSession session = SkyIslandSessionOrNull();
            string reason;
            return session != null && session.CanOpenStoryPanel(out reason);
        }

        private static GameObject AutotestPanelRoot() { return GameObject.Find("SkyIslandStory"); }

        private static List<Button> AutotestPanelRows()
        {
            var rows = new List<Button>();
            GameObject root = AutotestPanelRoot();
            if (root == null) return rows;
            foreach (Button button in root.GetComponentsInChildren<Button>(false))
                if (button != null && button.gameObject.name == "Choice") rows.Add(button);
            return rows;
        }

        private static string AutotestRowLabel(Button row)
        {
            string best = string.Empty;
            if (row == null) return best;
            foreach (TextMeshProUGUI text in row.GetComponentsInChildren<TextMeshProUGUI>(false))
            {
                string value = text == null ? null : AutotestPlainText(text.text);
                if (string.IsNullOrEmpty(value)) continue;
                // 行首的数字键帽（1…9）不是选项文字：单字选项「西」与键帽「2」一样长，按长度取会取到键帽（首轮实测瞭台谜题因此按不到）。
                if (IsAutotestKeycap(value)) continue;
                if (value.Length > best.Length) best = value;
            }
            return best;
        }

        private static bool IsAutotestKeycap(string value)
        {
            if (value.Length > 2) return false;
            foreach (char c in value) if (c < '0' || c > '9') return false;
            return true;
        }

        private static string AutotestRowLabels(List<Button> rows)
        {
            var labels = new List<string>();
            foreach (Button row in rows) labels.Add(AutotestShort(AutotestRowLabel(row), 40));
            return labels.Count == 0 ? "none" : string.Join(" / ", labels.ToArray());
        }

        private static string AutotestPanelBody()
        {
            GameObject root = AutotestPanelRoot();
            if (root == null) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(false))
            {
                if (t.name != "Body") continue;
                TextMeshProUGUI text = t.GetComponentInChildren<TextMeshProUGUI>(false);
                if (text != null) return AutotestPlainText(text.text);
            }
            return null;
        }

        private static string[] AutotestAlternatives(string value)
        {
            return string.IsNullOrEmpty(value) ? new string[0] : value.Split('|');
        }

        private static bool AutotestContainsAny(string text, string[] alternatives)
        {
            if (text == null) return false;
            foreach (string alternative in alternatives)
                if (!string.IsNullOrEmpty(alternative) && text.IndexOf(alternative, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string AutotestShort(string value, int max)
        {
            if (value == null) return "null";
            value = value.Replace('\n', ' ').Replace('\r', ' ');
            return value.Length <= max ? value : value.Substring(0, max) + "…";
        }

        private static string Arg(string[] args, int index) { return args != null && index < args.Length ? args[index] : string.Empty; }

        private static int ArgInt(string[] args, int index, int fallback)
        {
            int value;
            return int.TryParse(Arg(args, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static float ArgFloat(string[] args, int index, float fallback)
        {
            float value;
            return float.TryParse(Arg(args, index), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static bool ArgBool(string[] args, int index, bool fallback)
        {
            string value = Arg(args, index);
            return value == "true" ? true : value == "false" ? false : fallback;
        }

        private static F3AutotestAssertion AutotestAssertion(string name, string result, string reason, string metrics)
        {
            return new F3AutotestAssertion { Name = name, Result = result, Reason = reason, Metrics = metrics };
        }

        private static F3AutotestAssertion AutotestBool(string name, bool ok, string reason, string metrics)
        {
            return AutotestAssertion(name, ok ? "PASS" : "FAIL", ok ? null : reason, metrics);
        }

        /// <summary>动作失败：记一条 FAIL 断言；<paramref name="critical"/> 为真时这一步后面的动作不再执行（断言记 SKIP）。</summary>
        private void AutotestFail(F3AutotestStepRecord record, string name, string reason, string metrics, bool critical)
        {
            record.Assertions.Add(AutotestAssertion(name, "FAIL", reason, metrics));
            if (critical) _autotest.StepFailed = true;
        }

        private static string FirstAutotestProblem(F3AutotestStepRecord record)
        {
            foreach (F3AutotestAssertion assertion in record.Assertions)
                if (assertion.Result == "FAIL") return assertion.Name + ":" + assertion.Reason;
            if (record.Result == "SKIP")
                foreach (F3AutotestAssertion assertion in record.Assertions)
                    if (assertion.Result == "SKIP") return assertion.Name + ":" + assertion.Reason;
            return null;
        }

        private static string AutotestRecordMetrics(F3AutotestStepRecord record)
        {
            int pass = 0, fail = 0, skip = 0;
            foreach (F3AutotestAssertion assertion in record.Assertions)
            {
                if (assertion.Result == "PASS") pass++;
                else if (assertion.Result == "FAIL") fail++;
                else skip++;
            }
            var files = new List<string>();
            foreach (F3AutotestShot shot in record.Shots) if (!string.IsNullOrEmpty(shot.File)) files.Add(shot.File);
            return "stage=" + record.Stage + ",checklist=" + string.Join("+", record.Checklist.ToArray()) + ",asserts=" + pass + "/" + fail + "/" + skip
                + ",shots=" + (files.Count == 0 ? "none" : string.Join("+", files.ToArray()));
        }

        #endregion
    }
}
#endif
