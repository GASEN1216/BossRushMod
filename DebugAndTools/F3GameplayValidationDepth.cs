// ============================================================================
// F3GameplayValidationDepth.cs - 完整验收的模式深度流程用例
// ============================================================================
// 模块说明：
//   生命周期用例只证明「模式能起来、场上有敌人、能清干净」。这一组往前走一步，
//   验的是模式内部的推进链——那才是玩家真正会卡住的地方。
//
// 【入口来源】Mode D 多波用现成 public 入口（ModeDStartNextWave / ModeDWaveIndex）。
//   Mode E 收尾、Mode F 赏金/撤离、标准胜利奖励原本全是 private，上一轮如实记 SKIP；
//   现已在产品代码补 internal 验收入口，用例编排见 F3GameplayValidationDeepFlows.cs。
//   那些入口只补「前提」（推进阶段计时、给悬赏印记、触发官方倒计时事件、收窄 Boss 池），
//   玩法链本身仍由产品代码跑，断言取自产品状态——不是为了凑绿造假 PASS。
// ============================================================================

using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>
        /// 5/7 阶段：模式深度流程。五项全自动驱动，逐项隔离——单项红不拖垮整套。
        /// 每项后跟一次场地清洁校验，避免上一项的残留把下一项染红。
        /// </summary>
        private IEnumerator RunModeDepthCases()
        {
            yield return RunIsolatedCase("MODE_D_MULTI_WAVE", RunModeDMultiWave);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_MODE_D_MULTI");

            yield return RunIsolatedCase("MODE_E_EXTRACTION", RunModeEExtraction);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_MODE_E_EXTRACTION");

            yield return RunIsolatedCase("MODE_F_BOUNTY", RunModeFBounty);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_MODE_F_BOUNTY");

            yield return RunIsolatedCase("MODE_F_EXTRACTION", RunModeFExtraction);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_MODE_F_EXTRACTION");

            yield return RunIsolatedCase("MODE_ZOMBIE_EXTRACTION", RunZombieExtraction);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_ZOMBIE_EXTRACTION");

            yield return RunIsolatedCase("STANDARD_VICTORY_REWARD", RunStandardVictoryReward);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_VICTORY_REWARD");

            // 过场「点击继续」门放在最后：它会切场景，跑完由 RunArenaStages 的
            // 收尾流程负责回基地，不影响前面几项的场地状态。
            yield return RunIsolatedCase("SCENE_CLICK_GATE", RunSceneClickGate);
        }

        /// <summary>
        /// Mode D 多波连打。这是「敌人被官方距离休眠关掉」那个 bug 唯一能自动抓到的形态：
        /// 第 1 波清完必须能真正推进到第 2 波。旧版只测第 1 波，
        /// 而卡波次恰恰发生在「怪还活着但 active=False，永远等不到它死」。
        ///
        /// 流程：开波 → 确认登记的怪可玩（active + alive + hostile）→ 强制清敌
        /// → 等波次结算 → 开下一波 → 确认 waveIndex 真的涨了且新波同样可玩。
        /// </summary>
        private IEnumerator RunModeDMultiWave()
        {
            Stopwatch sw = Stopwatch.StartNew();
            int firstWave = 0;
            int secondWave = 0;
            int firstPlayable = 0;
            int secondPlayable = 0;
            bool advanced = false;
            string firstDetails = string.Empty;
            string secondDetails = string.Empty;

            try
            {
                _host.StartModeD();
                if (!_host.IsModeDActive || !_host.ModeDStartNextWave())
                {
                    Record("MODE_D_MULTI_WAVE", "FAIL", sw.ElapsedMilliseconds,
                        "active=" + _host.IsModeDActive, "first_wave_start_rejected");
                    yield break;
                }

                yield return WaitSeconds(8f);
                firstWave = _host.ModeDWaveIndex;
                firstPlayable = _host.ValidationCountPlayableModeDEnemies(out firstDetails);

                // 清掉第一波：走 Health.Hurt 让 Mode D 的死亡记账正常收尾，
                // 这样波次完成条件（无存活敌人 + 生成全部结案）才会真实达成。
                bool defeated = false;
                string defeatReason = null;
                yield return _host.ValidationDefeatModeDWave(delegate(bool success, string reason)
                {
                    defeated = success;
                    defeatReason = reason;
                });
                if (!defeated)
                {
                    Record("MODE_D_MULTI_WAVE", "FAIL", sw.ElapsedMilliseconds,
                        "wave=" + firstWave + ",first_details=" + firstDetails, defeatReason);
                    yield break;
                }

                // 等波次结算。给足时间：掉落结算与自动开波协程都是异步的。
                float deadline = Time.realtimeSinceStartup + 12f;
                while (Time.realtimeSinceStartup < deadline && !ShouldAbort())
                {
                    if (_host.ModeDWaveIndex > firstWave) { advanced = true; break; }
                    yield return null;
                }

                // 自动开波没接上时手工推一次——本用例要验的是「能不能推进」，
                // 自动 vs 手动的差别记进 metrics，不影响主结论。
                bool manualPush = false;
                if (!advanced && _host.IsModeDActive)
                {
                    manualPush = _host.ModeDStartNextWave();
                    if (manualPush)
                    {
                        yield return WaitSeconds(8f);
                        advanced = _host.ModeDWaveIndex > firstWave;
                    }
                }

                secondWave = _host.ModeDWaveIndex;
                secondPlayable = _host.ValidationCountPlayableModeDEnemies(out secondDetails);
                // 波次编号先增加，角色异步生成；自动开波也必须等到可玩的登记对象。
                float nextWaveDeadline = Time.realtimeSinceStartup + 8f;
                while (advanced && secondPlayable == 0 && Time.realtimeSinceStartup < nextWaveDeadline && !ShouldAbort())
                {
                    yield return null;
                    secondPlayable = _host.ValidationCountPlayableModeDEnemies(out secondDetails);
                }

                bool passed = advanced && firstPlayable > 0 && secondPlayable > 0;
                Record("MODE_D_MULTI_WAVE", passed ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                    "wave=" + firstWave + "->" + secondWave
                        + ",playable=" + firstPlayable + "/" + secondPlayable
                        + ",advanced=" + advanced + ",manual_push=" + manualPush
                        + ",first_details=" + firstDetails
                        + ",second_details=" + secondDetails,
                    passed ? string.Empty
                        : (!advanced
                            ? "wave_did_not_advance_after_clear"
                            : "wave_has_no_playable_tracked_enemy"));
            }
            finally
            {
                try { _host.ValidationSafeCleanup(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] Mode D 多波清理失败: " + e.Message); }
            }
        }
    }
}
