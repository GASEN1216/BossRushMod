// ============================================================================
// F3GameplayValidationDepth.cs - 完整验收的模式深度流程用例
// ============================================================================
// 模块说明：
//   生命周期用例只证明「模式能起来、场上有敌人、能清干净」。这一组往前走一步，
//   验的是模式内部的推进链——那才是玩家真正会卡住的地方。
//
// 【只用现成入口】本文件不给产品代码开测试后门。经核查：
//   - Mode D 多波连打可驱动：ModeDStartNextWave / ModeDWaveIndex 都是 public。
//   - Mode E 撤离结算（ModeELifecycle）与 Mode F 赏金链、撤离（ModeFBounty /
//     ModeFExtraction / ModeFPhases）**全部是 private 且无 Debug/Validation 入口**，
//     只能靠真人走到撤离点交互。这两项如实记 SKIP 并写明「需人工」，
//     不伪造 PASS，也不为了凑绿去加 internal 后门（那等于把验收本身变成谎言）。
//   - 标准模式「波次完成→胜利奖励」同理：胜利判定挂在 Boss 死亡链上，
//     LootAndRewardsVictoryRewards 只暴露生成入口，不暴露可断言的结算查询。
// ============================================================================

using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>5/7 阶段：模式深度流程。当前只有 Mode D 多波可全自动驱动。</summary>
        private IEnumerator RunModeDepthCases()
        {
            yield return RunIsolatedCase("MODE_D_MULTI_WAVE", RunModeDMultiWave);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_MODE_D_MULTI");

            // 无 code-drivable 入口的项：如实记 SKIP。报告里留痕比留空白诚实，
            // 也比硬凑一个恒绿断言有用——后者会让人误以为这条链被测过了。
            Record("MODE_E_EXTRACTION", "SKIP", 0L, "drivable=false",
                "Mode E 撤离结算无 code-drivable 入口（ModeELifecycle 均为 private），需人工走撤离点验证");
            Record("MODE_F_BOUNTY", "SKIP", 0L, "drivable=false",
                "Mode F 赏金链无 code-drivable 入口（ModeFBounty 均为 private），需人工击杀赏金 Boss 验证");
            Record("MODE_F_EXTRACTION", "SKIP", 0L, "drivable=false",
                "Mode F 撤离无 code-drivable 入口（ModeFExtraction 均为 private），需人工走撤离点验证");
            Record("STANDARD_VICTORY_REWARD", "SKIP", 0L, "drivable=false",
                "标准模式胜利奖励结算需打完全部波次，无可断言的结算查询入口，需人工验证");
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

                // 清掉第一波：走 Health.Kill 让 Mode D 的死亡记账正常收尾，
                // 这样波次完成条件（无存活敌人 + 生成全部结案）才会真实达成。
                _host.ValidationForceClearArenaEnemies();

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
