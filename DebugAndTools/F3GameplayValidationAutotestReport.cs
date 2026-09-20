#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationAutotestReport.cs - 全自动实机验收的结果目录（Dev 构建）
// ============================================================================
// 结果目录 persistentDataPath/BossRushTestReports/<runId>/：
//   manifest.json  每步的 id、清单编号、剧情阶段、位置、动作、断言、metrics、截图文件名（F3AutotestJudges.RenderManifest）；
//   summary.md     还原状态在最顶上、红项在前、清单覆盖表（F3AutotestJudges.RenderSummary）；
//   shots/         截图（UI PNG、世界 JPG，单轮 ≤300 MB）；
//   story_snapshot.json  开跑前剧情存档的快照（还原失败时人工写回用）。
// 主套件与岛内用例的逐项结果仍在同级的 BossRushValidation_<runId>.log / .coverage.md，这里只按 id 读回它们的结论。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SodaCraft.Localizations;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private static readonly UTF8Encoding AutotestUtf8 = new UTF8Encoding(false);

        private void InitAutotestRunDirectory()
        {
            string dir = Path.Combine(Application.persistentDataPath, "BossRushTestReports", _runId);
            Directory.CreateDirectory(Path.Combine(dir, "shots"));
            _autotest.RunDir = dir;
            _autotest.ShotsDir = Path.Combine(dir, "shots");
            F3AutotestRunInfo info = _autotest.Info;
            info.RunId = _runId;
            info.Slot = _sessionSlot;
            info.Mvid = typeof(ModBehaviour).Module.ModuleVersionId.ToString();
            info.Language = LocalizationManager.CurrentLanguage.ToString();
            info.StartedUtc = DateTime.UtcNow.ToString("O");
            info.ReportLog = _reportPath;
            info.Status = "RUNNING";
            WriteRaw("AUTOTEST | result_dir=" + dir);
        }

        /// <summary>每步在 Player.log 写 [AUTOTEST] step=… begin / end result=…，便于和报告对时。</summary>
        private static void AutotestLog(string stepId, string phase, string result)
        {
            UnityEngine.Debug.Log(F3AutotestJudges.LogLine(stepId, phase, result));
        }

        /// <summary>按 id 读回本轮 .log 里主套件与岛内用例的结论（FAIL 不被后来的 PASS 覆盖，与覆盖账本同口径）。</summary>
        private Dictionary<string, string> ReadAutotestCaseOutcomes()
        {
            var outcomes = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (string.IsNullOrEmpty(_reportPath) || !File.Exists(_reportPath)) return outcomes;
                using (var stream = new FileStream(_reportPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        int first = line.IndexOf(" | ", StringComparison.Ordinal);
                        if (first <= 0) continue;
                        int second = line.IndexOf(" | ", first + 3, StringComparison.Ordinal);
                        if (second <= first) continue;
                        string id = line.Substring(0, first);
                        string outcome = line.Substring(first + 3, second - first - 3);
                        if (id.StartsWith("SKY_AUTO_", StringComparison.Ordinal)) continue;
                        if (outcome != "PASS" && outcome != "FAIL" && outcome != "SKIP" && outcome != "WARN") continue;
                        bool upper = true;
                        for (int i = 0; i < id.Length && upper; i++)
                            upper = (id[i] >= 'A' && id[i] <= 'Z') || (id[i] >= '0' && id[i] <= '9') || id[i] == '_';
                        if (!upper) continue;
                        string previous;
                        if (outcomes.TryGetValue(id, out previous) && previous == "FAIL") continue;
                        outcomes[id] = outcome;
                    }
                }
            }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 读回用例结论失败: " + e.Message); }
            return outcomes;
        }

        /// <summary>写 manifest.json 与 summary.md。阶段之间也写一次：强退时目录里仍有到那一刻为止的结果。</summary>
        private void WriteAutotestReport()
        {
            if (_autotest == null || string.IsNullOrEmpty(_autotest.RunDir)) return;
            try
            {
                _autotest.Info.EndedUtc = DateTime.UtcNow.ToString("O");
                _autotest.Info.ShotBytes = _autotest.ShotBytes;
                Dictionary<string, string> outcomes = ReadAutotestCaseOutcomes();
                File.WriteAllText(Path.Combine(_autotest.RunDir, "manifest.json"),
                    AddResourcePerformanceManifest(F3AutotestJudges.RenderManifest(_autotest.Info, _autotest.Records, _autotest.Table, outcomes)), AutotestUtf8);
                File.WriteAllText(Path.Combine(_autotest.RunDir, "summary.md"),
                    F3AutotestJudges.RenderSummary(_autotest.Info, _autotest.Records, _autotest.Table, outcomes) + ResourcePerformanceSummary(), AutotestUtf8);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[BossRushValidation] 全自动验收结果目录写入失败: " + e);
            }
        }

        private void WriteAutotestTextFile(string name, string content)
        {
            if (_autotest == null || string.IsNullOrEmpty(_autotest.RunDir)) return;
            try { File.WriteAllText(Path.Combine(_autotest.RunDir, name), content ?? string.Empty, AutotestUtf8); }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 写结果目录文件失败 " + name + ": " + e.Message); }
        }
    }
}
#endif
