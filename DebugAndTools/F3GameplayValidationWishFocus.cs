// ============================================================================
// F3GameplayValidationWishFocus.cs - 「只跑本次改动」专项：2026-11-24 许愿七项与同日尸潮 / 龙裔 / 叮当 / 配置标题
// ============================================================================
// 来由：owner 2026-11-25「能不能从全量测试里分离出来我们这次的，单单测试我们这次的。」
// 整份只在 Dev 构建里编译。用例本身全在 F3GameplayValidationWishes.cs，完整验收照旧会跑它们；这里只换一套短编排：
//   基线 10 秒 → 基地九条 → 进竞技场 → 场内六条（另有两次清场核对）→ 回基地做最终清场、泄漏与存档回读。
// 会话外壳与完整验收相同：同一道开跑门（Dev 构建、基地、专用测试档、没有活动玩法与模态）、主角无敌回血、
// 写运行标记、抑制提示条、收尾跑全宿主清理。不跑别的用例，也不接天空岛全自动后半程，几分钟就能跑完。
// 覆盖账本收窄到下面四张表（GameplayCoverageReport.Focus）：没列进来的功能整条移出本轮账本，不算「未跑」；
// 列进来的没跑到照样算未通过，SUMMARY 仍按完整验收的口径给 PASS / FAIL / INCOMPLETE。
// 以后往 F3GameplayValidationWishes.cs 加用例，要同步加进这里的表（tests/F3WishFocusGuard.py 核对两边）。
// ============================================================================

#if BOSSRUSH_DEV
using System;
using System.Collections;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>基地阶段的用例：RunWishBaseCases 记的全部 id。</summary>
        private static readonly string[] WishFocusBaseCaseIds =
        {
            "INK_SKIN_ASSETS", "ARENA_EXPEDITION_TUNING", "MUTATOR_SWARM_WEIGHT", "WISH_FOUNTAIN_RULES",
            "PETNEST_SOUL_FEED_RULES", "AFFINITY_GIFT_JOURNAL_ROUNDTRIP", "NPC_NAMEPLATE_SETTING", "MODCONFIG_OPTION_LABELS",
            "GOBLIN_POST_DIALOGUE_STAY",
        };

        /// <summary>场内用例：RunWishArenaCases 记的全部 id（含两次清场核对）。进不了竞技场时整批记 SKIP。</summary>
        private static readonly string[] WishFocusArenaCaseIds =
        {
            "ZOMBIE_CROWD_NAVIGATION", "CLEANUP_AFTER_ZOMBIE_CROWD",
            "ARENA_EXPEDITION_ORDER", "BOSS_MAP_BEACON", "EQ_SILENCE_BELL_WARD", "ARENA_EXPEDITION_CLEANUP", "CLEANUP_AFTER_EXPEDITION",
            "DRAGON_DESCENDANT_SPAWN_PLACEMENT",
        };

        /// <summary>会话外壳自己记的 id：覆盖清单、基线、进出竞技场与回基地后的最终核对（RunFinalChecks）。</summary>
        private static readonly string[] WishFocusShellCaseIds =
        {
            "COVERAGE_MANIFEST", "BASELINE_10S", "SCENE_ENTER_ARENA", "ARENA_READY",
            "SCENE_RETURN_BASE", "BASE_READY_FINAL", "FINAL_5S", "FINAL_CLEAN_STATE", "FINAL_LEAK_DELTA", "FINAL_SAVE_READBACK",
        };

        /// <summary>这次新增的人工实机项，随专项清单一起写进 .coverage.md，自动结果之外要人看的就是这些。</summary>
        private static readonly string[] WishFocusManualIds =
        {
            "M_STANDARD_03", "M_STANDARD_04", "M_STANDARD_05", "M_STANDARD_06", "M_STANDARD_07", "M_BOSS_BEACON_01",
            "M_PETNEST_SOULFEED_01", "M_AFFINITY_JOURNAL_01", "M_NPC_NAMEPLATE_01", "M_WISH_02", "M_WISH_03", "M_MUTATORS_03",
            "M_EQ_SILENCE_BELL_01", "M_EQ_SILENCE_BELL_02", "M_EQ_SILENCE_BELL_03", "M_EQ_SILENCE_BELL_04", "M_EQ_SILENCE_BELL_05",
            "M_CONFIG_UI_04",
        };

        /// <summary>F3 面板按钮：开跑就收起 F3；开不了就把原因弹给玩家（与演练按钮同口径）。</summary>
        internal static void StartWishFocusFromPanel(ModBehaviour host)
        {
            string reason;
            if (TryStartWishFocus(host, out reason)) { if (host != null) host.CloseF3BeforeDebugPanel(); return; }
            if (host != null && !string.IsNullOrEmpty(reason)) host.ShowMessage(reason);
        }

        internal static bool TryStartWishFocus(ModBehaviour host, out string reason)
        {
            reason = null;
            EnsureAttached(host);
            if (_instance == null) { reason = "验收运行器未就绪"; return false; }
            if (_instance._running) { reason = "已有验收正在运行"; return false; }
            // 与完整验收同一道开跑门：要切竞技场再回基地，所以同样只能从基地、专用测试档出发。
            if (!_instance.CheckStartGate(out reason)) return false;
            _instance._cancelRequested = false;
            _instance._fatalAbort = false;
            if (!_instance.BeginSession(out reason)) return false;
            _instance.WriteRaw("SUITE | WISH_FOCUS | read_only=false | focus=2026-11-24_wishes | base_cases=" + WishFocusBaseCaseIds.Length
                + " | arena_cases=" + WishFocusArenaCaseIds.Length + " | manual=" + WishFocusManualIds.Length);
            _instance.FocusWishCoverage();
            _instance._routine = _instance.StartCoroutine(_instance.RunWishFocusSession());
            return true;
        }

        /// <summary>覆盖账本只留本次的用例与人工项；表里有、清单里没有的 id 记到 COVERAGE_MANIFEST 的红项里。</summary>
        private void FocusWishCoverage()
        {
            if (_coverage == null) return;
            HashSet<string> automatic = new HashSet<string>(StringComparer.Ordinal);
            automatic.UnionWith(WishFocusBaseCaseIds);
            automatic.UnionWith(WishFocusArenaCaseIds);
            automatic.UnionWith(WishFocusShellCaseIds);
            List<string> missing = _coverage.Focus(automatic, new HashSet<string>(WishFocusManualIds, StringComparer.Ordinal));
            if (missing.Count > 0)
                Record("COVERAGE_MANIFEST", "FAIL", 0L, "focus=wish", "focus_ids_not_in_manifest:" + string.Join(",", missing.ToArray()));
            try { WriteCoverageSnapshot(); }
            catch (Exception e) { Record("COVERAGE_REPORT", "FAIL", 0L, string.Empty, e.ToString()); }
        }

        /// <summary>与 RunSession 同一外壳：套件换成专项，收尾照样回基地做最终核对，但不接天空岛全自动后半程。</summary>
        private IEnumerator RunWishFocusSession()
        {
            // 防止 StartCoroutine 同步结束后又把已结束的句柄赋回 _routine。
            yield return null;
            try
            {
                yield return DriveSessionPhase(RunWishFocusSuite(), false);
                if (!_slotChanged && !_hostLost && _host != null)
                {
                    _closingSession = true;
                    yield return DriveSessionPhase(RunFinalChecks(), true);
                }
            }
            finally { CompleteSession(); }
        }

        private IEnumerator RunWishFocusSuite()
        {
            SetStage("本次改动 1/2 基线与基地核对");
            yield return SamplePerformance("BASELINE_10S", 10f, true);
            CaptureLeakBaseline("WISH_FOCUS");
            yield return RunWishBaseCases();

            SetStage("本次改动 2/2 竞技场");
            yield return LoadScene(BossRushArenaSceneIDForValidation(), "SCENE_ENTER_ARENA");
            if (!_operationSucceeded)
            {
                SkipWishFocusArenaCases("arena_scene_load_failed");
                yield break;
            }
            yield return WaitRuntimeReady("ARENA_READY", SceneTimeoutSeconds);
            if (!_operationSucceeded) SkipWishFocusArenaCases("arena_runtime_not_ready");
            else yield return RunWishArenaCases();
        }

        private void SkipWishFocusArenaCases(string reason)
        {
            for (int i = 0; i < WishFocusArenaCaseIds.Length; i++) Record(WishFocusArenaCaseIds[i], "SKIP", 0L, string.Empty, reason);
        }
    }
}
#endif
