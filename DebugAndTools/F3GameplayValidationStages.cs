// ============================================================================
// F3GameplayValidationStages.cs - 完整验收的阶段编排与用例隔离
// ============================================================================
// 模块说明：
//   把「竞技场与模式」「模式深度流程」「征程终章与音频」三个阶段的编排从主 runner
//   拆出来，主 runner 才能守住 1200 行预算（LargeFileBudgetGuard）。
//
// 隔离语义（本轮改造的核心）：
//   旧版任一模式用例或清场失败就 `yield break` 掉整个 RunSuite，报告打 CANCELLED，
//   第 4/5 阶段一个都跑不到——「一次性测完」的诉求根本无法满足。
//   现在每个模式用例走 RunIsolatedCase：异常 → 记 FAIL → 强制清场 → 继续下一个。
//   只有「连续两次清场不可恢复」（脏状态会污染后续结论）或玩家主动取消才真中止。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>场内全部用例的 ID 顺序，进不了场时用来批量记 SKIP。</summary>
        private static readonly string[] ArenaCaseIds =
        {
            "MODE_STANDARD_WAVE", "RANDOM_EVENTS_ALL",
            "MODE_D_LIFECYCLE", "MODE_D_MULTI_WAVE",
            "MODE_E_LIFECYCLE", "MODE_E_EXTRACTION",
            "MODE_F_LIFECYCLE", "MODE_F_BLOODFIRE", "MODE_F_BOUNTY", "MODE_F_EXTRACTION",
            "MODE_G_LIFECYCLE", "MODE_H_FIRST_CERTIFICATION", "MODE_H_CACHE_HIT", "MODE_H_STARTER_KITS",
            "MODE_H_ERROR_SWAP",
            "MODE_ZOMBIE_LIFECYCLE", "MODE_ZOMBIE_EXTRACTION", "BGM_OWNER_LEASES", "CAMPAIGN_FINAL_BOSS",
            "STANDARD_VICTORY_REWARD", "SCENE_CLICK_GATE",
        };

        /// <summary>
        /// 4/7 ~ 6/7 三个阶段的完整编排。每个用例独立隔离，单个红项不再拖垮整套。
        /// </summary>
        private IEnumerator RunArenaStages()
        {
            yield return RunIsolatedCase("MODE_STANDARD_WAVE", RunStandardAndRandomEvents);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_STANDARD");

            yield return RunIsolatedCase("MODE_D_LIFECYCLE", RunModeD);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_MODE_D");

            yield return RunIsolatedCase("MODE_E_LIFECYCLE", RunModeE);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_MODE_E");

            yield return RunIsolatedCase("MODE_F_LIFECYCLE", RunModeF);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_MODE_F");

            yield return RunIsolatedCase("MODE_G_LIFECYCLE", RunModeG);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_MODE_G");

            yield return RunIsolatedCase("MODE_H_FIRST_CERTIFICATION", RunModeHFirst);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_MODE_H_FIRST");

            yield return RunIsolatedCase("MODE_H_CACHE_HIT", RunModeHCached);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_MODE_H_CACHE");

            yield return RunIsolatedCase("MODE_ZOMBIE_LIFECYCLE", RunZombie);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_ZOMBIE");

            SetStage("5/7 模式深度流程");
            yield return RunModeDepthCases();

            SetStage("6/7 征程终章与音频");
            yield return EnsureArenaForCase("BGM_OWNER_LEASES");
            if (_operationSucceeded) RunSyncCaseGated("BGM_OWNER_LEASES", ValidateBgmOwnerLeases);
            else Record("BGM_OWNER_LEASES", "SKIP", 0L, string.Empty, "arena_runtime_not_ready");
            yield return RunIsolatedCase("CAMPAIGN_FINAL_BOSS", RunCampaignFinalBoss);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_CAMPAIGN_FINAL");
        }

        private IEnumerator RunModeHFirst() { return RunModeH(false); }
        private IEnumerator RunModeHCached() { return RunModeH(true); }

        /// <summary>
        /// 用例隔离壳。协程内的异常不会自动冒泡到 StartCoroutine 的调用方，
        /// 所以这里手工 MoveNext 并 catch：单个用例炸掉只记它自己的 FAIL，
        /// 强制清场后继续下一个。
        ///
        /// 注意必须透传 Current——用例内部还会 `yield return WaitSeconds(...)` 这类子协程；
        /// 丢掉 Current 会让它们永不推进（Mode H 认证就踩过这个坑，
        /// 见 ModeHCertificationCoroutineDriveGuard）。子 IEnumerator 由本壳自己压栈驱动，
        /// 这样它们抛出的异常才会落进下面那个 catch（详见循环处的注释）。
        /// </summary>
        private IEnumerator RunIsolatedCase(string caseId, Func<IEnumerator> factory)
        {
            if (ShouldAbort())
            {
                Record(caseId, "SKIP", 0L, string.Empty, DescribeAbortReason());
                yield break;
            }

            yield return EnsureArenaForCase(caseId);
            if (!_operationSucceeded)
            {
                Record(caseId, "SKIP", 0L, string.Empty, "arena_runtime_not_ready");
                yield break;
            }

            // C# 不允许在 catch 子句体里 yield（CS1631）。所以异常只在 catch 里记账，
            // 用这个标志把「需要强清」带到 catch 之外执行。
            bool needsReclaim = false;

            IEnumerator inner = null;
            try
            {
                inner = factory();
            }
            catch (Exception e)
            {
                Record(caseId, "FAIL", 0L, string.Empty, "case_factory_threw:" + e);
                needsReclaim = true;
            }

            if (needsReclaim)
            {
                yield return ForceReclaimArena();
                yield break;
            }
            if (inner == null)
            {
                Record(caseId, "FAIL", 0L, string.Empty, "case_factory_returned_null");
                yield break;
            }

            Stopwatch sw = Stopwatch.StartNew();

            // 自持迭代器栈，**不把子 IEnumerator 交给 Unity**。
            //
            // 旧写法是 `yield return inner.Current;`：Current 一旦是 IEnumerator
            // （用例里遍地都是 `yield return WaitSeconds(...)` / `yield return RunModeHErrorSwap(map)`），
            // Unity 会自己把它压栈驱动，直到子迭代器结束才回来调 inner.MoveNext。
            // 也就是说**子协程抛出的异常在物理上不会经过下面这次 MoveNext**，
            // catch 分支不可能触发：既不记 FAIL 也不强清场，脏状态直接漏进下一个用例。
            // 自己驱动子迭代器后，整条调用链上的异常都会落到同一个 catch 里。
            List<IEnumerator> stack = new List<IEnumerator>(8);
            stack.Add(inner);

            while (stack.Count > 0)
            {
                IEnumerator top = stack[stack.Count - 1];
                bool moveNext = false;
                try
                {
                    moveNext = top.MoveNext();
                }
                catch (Exception e)
                {
                    // 用例自己没记结果就炸了：补一条 FAIL，避免这一项在报告里凭空消失。
                    Record(caseId + "_UNHANDLED", "FAIL", sw.ElapsedMilliseconds,
                        string.Empty, "case_threw:" + e);
                    needsReclaim = true;
                }

                if (needsReclaim)
                {
                    yield return ForceReclaimArena();
                    yield break;
                }

                if (!moveNext)
                {
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                // 子迭代器压栈自己驱动；其余（null / WaitForSeconds / AsyncOperation…）
                // 仍旧交给 Unity，语义与原来一致。
                IEnumerator child = top.Current as IEnumerator;
                if (child != null)
                {
                    stack.Add(child);
                    continue;
                }
                yield return top.Current;
            }
        }

        /// <summary>
        /// 强化清场：安全清理 + 竞技场强制清敌 + 等几帧让异步收尾落地。
        /// 单次清场没干净多半只是 UniTask 生成/掉落结算比 2 秒窗口慢一点。
        /// </summary>
        private IEnumerator ForceReclaimArena()
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try { _host.ValidationSafeCleanup(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 强清安全清理失败: " + e.Message); }
                try { _host.ValidationForceClearArenaEnemies(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 强清清敌失败: " + e.Message); }

                // 用固定帧数而非 WaitSeconds：取消请求期间 WaitSeconds 会立刻返回，
                // 而清场恰恰是取消路径上最需要真正等一等的一步。
                for (int frame = 0; frame < 6; frame++) yield return null;
            }
        }

        /// <summary>进不了竞技场时把场内用例批量记 SKIP，报告里不留空白。</summary>
        private void SkipRemainingArenaCases(string reason)
        {
            for (int i = 0; i < ArenaCaseIds.Length; i++)
            {
                Record(ArenaCaseIds[i], "SKIP", 0L, string.Empty, reason);
            }
        }

        private string DescribeAbortReason()
        {
            if (_cancelRequested) return "player_cancelled";
            if (_fatalAbort) return "aborted_dirty_state";
            if (_suiteTimedOut) return "suite_timeout";
            return "aborted";
        }

        /// <summary>
        /// 同步用例的隔离壳（sync 版本没有子协程，直接 try/catch 即可）。
        /// 与 RunSyncCase 的差别只在于中止时记 SKIP 而不是硬跑。
        /// </summary>
        private void RunSyncCaseGated(string id, SyncValidation validation)
        {
            if (ShouldAbort())
            {
                Record(id, "SKIP", 0L, string.Empty, DescribeAbortReason());
                return;
            }
            RunSyncCase(id, validation);
        }

    }
}
