// ============================================================================
// F3GameplayValidationDeepFlows.cs - 深度玩法链与过场点击门用例
// ============================================================================
// 模块说明：
//   上一轮这几项因「产品代码全 private、无可驱动入口」如实记 SKIP，导致一键验收
//   跑完仍要人工走撤离点、打完整波次。本轮给产品代码补了 5 个 internal 验收入口
//   （DebugAdvanceModeFPhaseForValidation / DebugAwardModeFBountyMarkForValidation /
//   DebugTriggerModeFExtractionForValidation / DebugSettleModeEExtractionForValidation /
//   DebugStartSingleBossVictoryForValidation），这里把它们编排成真实机断言。
//
// 【不是后门】每个入口都只补「前提」，玩法链本身仍由产品代码跑：
//   - 撤离走官方 CountDownArea.onCountDownSucceed 事件，和玩家站进圈里同源；
//   - 赏金走 OnModeFBossKilledByPlayer 整条结算；
//   - 胜利走 currentEnemyIndex >= presetCount 的真实判定，只是把 Boss 池收窄到 1
//     （玩家自己在 Boss 池面板里就能做的事）。
//   断言取自产品状态，不取自入口的返回值本身。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>
        /// Mode F 赏金链：起模式 → 推进到 Bounty 阶段 → 结算一个悬赏 Boss
        /// → 断言玩家印记真的从 0 涨到 1。
        /// </summary>
        private IEnumerator RunModeFBounty()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                if (!_host.ValidationStartModeF() || !_host.IsModeFActive)
                {
                    Record("MODE_F_BOUNTY", "FAIL", sw.ElapsedMilliseconds,
                        "active=" + _host.IsModeFActive, "mode_f_start_rejected");
                    yield break;
                }

                yield return WaitSeconds(6f);

                string advanceReason;
                bool advanced = _host.DebugAdvanceModeFPhaseForValidation(out advanceReason);
                ModeFPhase phase = _host.ModeFCurrentPhaseForValidation;
                if (!advanced || phase != ModeFPhase.Bounty)
                {
                    Record("MODE_F_BOUNTY", "FAIL", sw.ElapsedMilliseconds,
                        "phase=" + phase + ",advanced=" + advanced,
                        advanceReason ?? "phase_not_bounty");
                    yield break;
                }

                // GenerateBountyList 在进入 Bounty 时挂上悬赏，给它一帧落地。
                yield return WaitSeconds(3f);

                int before = _host.ModeFPlayerBountyMarksForValidation;
                string awardReason;
                bool awarded = _host.DebugAwardModeFBountyMarkForValidation(out awardReason);
                yield return WaitSeconds(1f);
                int after = _host.ModeFPlayerBountyMarksForValidation;

                bool passed = awarded && after > before;
                Record("MODE_F_BOUNTY", passed ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                    "phase=" + phase + ",marks=" + before + "->" + after + ",awarded=" + awarded,
                    passed ? string.Empty : (awardReason ?? "bounty_marks_did_not_increase"));
            }
            finally
            {
                try { _host.ValidationSafeCleanup(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] Mode F 赏金清理失败: " + e.Message); }
            }
        }

        /// <summary>
        /// Mode F 撤离：推到 Extraction 阶段 → 断言撤离点真的生成
        /// → 触发官方倒计时成功事件 → 断言撤离结算走完且模式收尾。
        /// </summary>
        private IEnumerator RunModeFExtraction()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                if (!_host.ValidationStartModeF() || !_host.IsModeFActive)
                {
                    Record("MODE_F_EXTRACTION", "FAIL", sw.ElapsedMilliseconds,
                        "active=" + _host.IsModeFActive, "mode_f_start_rejected");
                    yield break;
                }

                yield return WaitSeconds(6f);

                // Preparation → Bounty → HuntStorm → Extraction：三次推进，
                // 每次都走真实阶段机。真等 540 秒会直接吃掉整套预算。
                string advanceReason = null;
                bool reached = false;
                for (int i = 0; i < 3 && !ShouldAbort(); i++)
                {
                    if (!_host.DebugAdvanceModeFPhaseForValidation(out advanceReason)) break;
                    yield return WaitSeconds(2f);
                    if (_host.ModeFCurrentPhaseForValidation == ModeFPhase.Extraction)
                    {
                        reached = true;
                        break;
                    }
                }

                ModeFPhase phase = _host.ModeFCurrentPhaseForValidation;
                if (!reached)
                {
                    Record("MODE_F_EXTRACTION", "FAIL", sw.ElapsedMilliseconds,
                        "phase=" + phase, advanceReason ?? "extraction_phase_not_reached");
                    yield break;
                }

                // SpawnFinalExtractionPoint 是异步落地的，给它时间。
                bool spawned = false;
                float deadline = Time.realtimeSinceStartup + 15f;
                while (Time.realtimeSinceStartup < deadline && !ShouldAbort())
                {
                    if (_host.ModeFExtractionPointSpawnedForValidation) { spawned = true; break; }
                    yield return null;
                }
                if (!spawned)
                {
                    Record("MODE_F_EXTRACTION", "FAIL", sw.ElapsedMilliseconds,
                        "phase=" + phase + ",spawned=false", "extraction_point_not_spawned");
                    yield break;
                }

                string triggerReason;
                int completedBefore = _host.ModeFSuccessfulExtractionCountForValidation;
                bool triggered = _host.DebugTriggerModeFExtractionForValidation(out triggerReason);

                bool resolved = _host.ModeFSuccessfulExtractionCountForValidation != completedBefore;
                bool returnedToBase = false;
                deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline && !ShouldAbort())
                {
                    if (IsRuntimeReady(BaseSceneNameForValidation())) { returnedToBase = true; break; }
                    if (!triggered || !resolved) break;
                    yield return null;
                }

                bool passed = triggered && resolved && !_host.IsModeFActive && returnedToBase;
                Record("MODE_F_EXTRACTION", passed ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                    "phase=" + phase + ",spawned=true,triggered=" + triggered
                        + ",resolved=" + resolved + ",still_active=" + _host.IsModeFActive
                        + ",base_ready=" + returnedToBase,
                    passed ? string.Empty : (triggerReason ?? (!resolved
                        ? "extraction_not_resolved" : "extraction_base_not_ready")));
            }
            finally
            {
                try { _host.ValidationSafeCleanup(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] Mode F 撤离清理失败: " + e.Message); }
            }
        }

        /// <summary>
        /// Mode E 结束收尾（该模式没有撤离点，语义见入口注释，metrics 标
        /// semantics=end_settlement）：断言阵营复位、登记敌人清空、模式关停。
        /// </summary>
        private IEnumerator RunModeEExtraction()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                if (!_host.ValidationStartModeE() || !_host.IsModeEActive)
                {
                    Record("MODE_E_EXTRACTION", "FAIL", sw.ElapsedMilliseconds,
                        "active=" + _host.IsModeEActive, "mode_e_start_rejected");
                    yield break;
                }

                yield return WaitSeconds(8f);

                string metrics;
                string reason;
                bool settled = _host.DebugSettleModeEExtractionForValidation(out metrics, out reason);
                yield return WaitSeconds(1f);

                Record("MODE_E_EXTRACTION", settled ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                    metrics, settled ? string.Empty : (reason ?? "end_settlement_failed"));
            }
            finally
            {
                try { _host.ValidationSafeCleanup(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] Mode E 收尾清理失败: " + e.Message); }
            }
        }

        /// <summary>
        /// 标准模式胜利奖励：Boss 池收窄到 1 → 开波 → 清掉那一个 Boss
        /// → 断言 OnAllEnemiesDefeated 真的走到（IsActive 归 false + 奖励箱控制器起来）。
        ///
        /// 只断言「结算链走到 + 奖励箱存在」，不钉死掉落数值——奖励走随机池，
        /// 把随机性写进断言只会得到一个随机红的用例。
        /// </summary>
        private IEnumerator RunStandardVictoryReward()
        {
            Stopwatch sw = Stopwatch.StartNew();
            Dictionary<string, bool> restoreStates = null;
            int restorePerWave = 1;
            bool started = false;
            try
            {
                string startReason;
                started = _host.DebugStartSingleBossVictoryForValidation(
                    out restoreStates, out restorePerWave, out startReason);
                if (!started)
                {
                    Record("STANDARD_VICTORY_REWARD", "FAIL", sw.ElapsedMilliseconds,
                        "active=" + _host.IsActive, startReason ?? "single_boss_start_rejected");
                    yield break;
                }

                // 官方创建期间角色就能被全场扫描找到，必须等生产路径登记 currentBoss 后再击杀。
                int spawned = 0;
                float deadline = Time.realtimeSinceStartup + 20f;
                while (Time.realtimeSinceStartup < deadline && !ShouldAbort())
                {
                    spawned = _host.ValidationGetCommittedStandardBoss() != null ? 1 : 0;
                    if (spawned > 0) break;
                    yield return null;
                }
                if (spawned <= 0)
                {
                    Record("STANDARD_VICTORY_REWARD", "FAIL", sw.ElapsedMilliseconds,
                        "spawned=0", "boss_never_committed");
                    yield break;
                }

                string killReason;
                if (!_host.ValidationKillCommittedStandardBoss(out killReason))
                {
                    Record("STANDARD_VICTORY_REWARD", "FAIL", sw.ElapsedMilliseconds,
                        "spawned=" + spawned, killReason);
                    yield break;
                }

                bool victory = false;
                deadline = Time.realtimeSinceStartup + 25f;
                while (Time.realtimeSinceStartup < deadline && !ShouldAbort())
                {
                    if (!_host.IsActive) { victory = true; break; }
                    yield return null;
                }

                // 奖励箱虚影是协程落地的，胜利后再给一段时间。
                bool crate = false;
                deadline = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < deadline && !ShouldAbort())
                {
                    if (_host.VictoryRewardCrateActiveForValidation) { crate = true; break; }
                    yield return null;
                }

                bool passed = victory && crate;
                Record("STANDARD_VICTORY_REWARD", passed ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                    "spawned=" + spawned + ",victory=" + victory + ",reward_crate=" + crate,
                    passed ? string.Empty
                        : (!victory ? "victory_settlement_not_reached" : "victory_reward_crate_missing"));
            }
            finally
            {
                try
                {
                    // 判据是「池子有没有被改」而不是「开波成不成功」：
                    // DebugStartSingleBossVictoryForValidation 收窄池子之后仍可能失败返回
                    // （narrow_failed / start_first_wave_did_not_activate / 中途抛异常），
                    // 那几条路径上 restoreStates 已非 null。按 started 判会漏还原，
                    // 把 owner 的 Boss 池永久留在「只剩一个 Boss」的状态。
                    if (restoreStates != null)
                    {
                        _host.RestoreBossPoolAfterValidation(restoreStates, restorePerWave);
                    }
                }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 还原 Boss 池失败: " + e.Message); }
                try { _host.ValidationSafeCleanup(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 胜利奖励清理失败: " + e.Message); }
            }
        }

        /// <summary>
        /// 过场「点击继续」门：走一次 clickToContinue=true 的真实入图路径
        /// （与丧尸模式选图一致），由 runner 自动喂官方 NotifyPointerClick。
        ///
        /// 这是唯一能自动抓到「玩家卡在点击继续进不去」的用例形态——
        /// 其余用例切图一律传 false，那条路径永远不会暴露这个问题。
        /// </summary>
        private IEnumerator RunSceneClickGate()
        {
            Stopwatch sw = Stopwatch.StartNew();

            yield return LoadScene(BossRushArenaSceneIDForValidation(), "SCENE_CLICK_GATE_ENTER", true);
            bool entered = _operationSucceeded;
            int clicksFed = _lastSceneClicksFed;
            string enterReason = _operationReason;

            if (entered)
            {
                yield return WaitRuntimeReady("SCENE_CLICK_GATE_READY", SceneTimeoutSeconds);
                entered = _operationSucceeded;
                if (!entered) enterReason = "runtime_ready_timeout_after_click_gate";
            }

            // 判定只认「进得去」，不拿 clicks_fed>0 当硬断言。原因是 SceneLoader.clicked
            // 的重置时机无法从反编译确认：只能看到 NotifyPointerClick 里置 true
            // （SceneLoader.cs:191）和字段声明（:274），真正等点击的 LoadScene 只是转发壳
            // （:106/150），逻辑在编译器生成的 <LoadScene>d__45.MoveNext 里、该状态机体在
            // 那份反编译中被剥离，故「每次加载是否重置」既不能证实也不能证伪。若实际不
            // 重置，本用例排在第 5 阶段、前面已切过多次图，门可能早被满足、一次都不用喂。
            // 那种情况下这轮没真正压到点击门，记 WARN 标出覆盖缺口，不冤枉健康构建。
            string outcome = !entered ? "FAIL" : (clicksFed > 0 ? "PASS" : "WARN");
            Record("SCENE_CLICK_GATE", outcome, sw.ElapsedMilliseconds,
                "click_to_continue=true,clicks_fed=" + clicksFed
                    + ",entered=" + entered + ",scene=" + SceneManager.GetActiveScene().name,
                outcome == "PASS" ? string.Empty
                    : (!entered
                        ? (enterReason ?? "click_gate_scene_load_failed")
                        : "gate_already_satisfied_click_feed_not_exercised"));
        }
    }
}
