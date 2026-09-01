// ============================================================================
// F3GameplayValidationLeaks.cs - 完整验收的泄漏与性能专项
// ============================================================================
// 模块说明：
//   跑完一整套模式之后，「场上看起来干净」不等于「没泄漏」。真实泄漏藏在那些
//   跨模式常驻的静态登记表里：死亡钩子没退订、参赛者没摘、诊断对象没解绑、
//   模态租约没还。这些都不会让 ValidationTryGetArenaCleanState 变红，
//   但会在连打几局之后表现为帧率下降、事件重复触发或存档写入放大。
//
//   做法是取套件开始时的一组静态计数作为基线，跑完全部模式后取一次差值。
//   只断言「应当回到基线」的项——容量类缓存（图鉴目录、地图登记表）本身
//   就该常驻，不算泄漏，所以不进断言集，只作为观测值记进 metrics。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>基线快照：key = 计数名，value = 套件开始时的值。</summary>
        private readonly Dictionary<string, int> _leakBaseline =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>基线是否已采集（未采集时差值断言只能记 SKIP，不能凭空判红）。</summary>
        private bool _leakBaselineCaptured;

        private void ResetLeakBaselines()
        {
            _leakBaseline.Clear();
            _leakBaselineCaptured = false;
        }

        /// <summary>
        /// 采集当前的一组静态计数。每项都单独 try/catch：
        /// 任一子系统尚未初始化不应让整个基线采集失败。
        /// </summary>
        private static void CollectLeakCounters(Dictionary<string, int> into)
        {
            AddCounter(into, "modal_leases", delegate { return ZombieModeUIHelper.ModalInputLeaseCount; });
            AddCounter(into, "bgm_owners", delegate { return BossBgmCoordinator.ActiveOwnerLeaseCount; });
            AddCounter(into, "modeh_participants", delegate { return ModeHEventRouter.ParticipantCount; });
            AddCounter(into, "modeh_diagnostics", delegate { return ModeHEventRouter.DiagnosticCount; });
            AddCounter(into, "affix_active", delegate { return AffixRuntimeService.ActiveAffixCount; });
            AddCounter(into, "affix_stone_hooks", delegate { return AffixForgeStoneDropService.TrackedCount; });
            AddCounter(into, "petnest_drop_hooks", delegate { return PetNestDropService.TrackedCount; });
            AddCounter(into, "petnest_idle_spawns", delegate { return PetNestBaseIdleSpawner.ActiveCount; });
            AddCounter(into, "petnest_downed", delegate { return PetNestDownedHandler.DownedCount; });
            AddCounter(into, "codex_fight_hooks", delegate { return CodexKillCollector.TrackedFightCount; });
        }

        private static void AddCounter(Dictionary<string, int> into, string key, Func<int> read)
        {
            try { into[key] = read(); }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Validation] 泄漏计数 " + key + " 读取失败: " + e.Message);
            }
        }

        /// <summary>套件开始时采基线。失败只记日志，差值用例会因此记 SKIP。</summary>
        private void CaptureLeakBaseline(string label)
        {
            try
            {
                _leakBaseline.Clear();
                CollectLeakCounters(_leakBaseline);
                _leakBaselineCaptured = _leakBaseline.Count > 0;
                WriteRaw("LEAK_BASELINE | " + label + " | " + DescribeCounters(_leakBaseline));
            }
            catch (Exception e)
            {
                _leakBaselineCaptured = false;
                ModBehaviour.DevLog("[Validation] 泄漏基线采集失败: " + e.Message);
            }
        }

        /// <summary>
        /// 跑完全部模式后核对静态登记表是否回到基线。
        /// 所有入选项的语义都是「一局结束后必须归零/回落」，涨了就是没退订。
        /// </summary>
        private bool ValidateSuiteLeakDelta(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;

            if (!_leakBaselineCaptured)
            {
                metrics = "baseline_captured=false";
                reason = "未取到泄漏基线，本项无法判定（需人工复测）";
                return false;
            }

            Dictionary<string, int> now = new Dictionary<string, int>(StringComparer.Ordinal);
            CollectLeakCounters(now);

            List<string> grew = new List<string>();
            StringBuilder detail = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in _leakBaseline)
            {
                int after;
                if (!now.TryGetValue(pair.Key, out after)) continue;
                int delta = after - pair.Value;
                if (detail.Length > 0) detail.Append(',');
                detail.Append(pair.Key).Append('=').Append(pair.Value)
                    .Append("->").Append(after);
                if (delta > 0) grew.Add(pair.Key + "+" + delta);
            }

            long memoryDelta = _finalMemory - _baselineMemory;
            metrics = detail.ToString()
                + ",memory_delta=" + memoryDelta
                + ",baseline_p95_ms=" + _baselineP95Ms.ToString("F2")
                + ",final_p95_ms=" + _finalP95Ms.ToString("F2");

            if (grew.Count > 0)
            {
                reason = "跨模式静态登记表未回落（疑似未退订）: "
                    + string.Join(",", grew.ToArray());
                return false;
            }

            // p95 退化单独报 WARN 而不是 FAIL：一次实机采样受后台进程影响很大，
            // 拿它判红会让整套结论变成噪声。真正的硬断言是上面那组计数。
            if (_baselineP95Ms > 0f && _finalP95Ms > _baselineP95Ms * 1.75f)
            {
                _warnings++;
                WriteRaw("PERFORMANCE_WARNING | final_p95_regressed | baseline="
                    + _baselineP95Ms.ToString("F2") + " final=" + _finalP95Ms.ToString("F2"));
            }
            return true;
        }

        private static string DescribeCounters(Dictionary<string, int> counters)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in counters)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(pair.Key).Append('=').Append(pair.Value);
            }
            return sb.ToString();
        }
    }
}
