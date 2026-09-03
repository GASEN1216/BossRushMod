using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BossRush
{
    /// <summary>用例结果之外的覆盖账本。未执行和人工场景永远不能由自动 PASS 抹掉。</summary>
    internal sealed partial class F3GameplayValidationRunner
    {
        private GameplayCoverageReport _coverage;

        private bool InitializeCoverage(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            _coverage = null;
            try
            {
                string path = Path.Combine(ModBehaviour.GetModPath(), "Assets", "Data", "GameplayCoverage.json");
                _coverage = GameplayCoverageReport.Parse(File.ReadAllText(path));
                _coverage.Expand("ITEM_FACTORY_*", Array.ConvertAll(
                    BossRushDynamicItemRegistry.GetPublishedTypeIds(), id => "ITEM_FACTORY_" + id));
                List<string> events = new List<string>();
                foreach (RandomEventId id in Enum.GetValues(typeof(RandomEventId)))
                    if (id != RandomEventId.None) events.Add("RANDOM_EVENT_" + id.ToString().ToUpperInvariant());
                _coverage.Expand("RANDOM_EVENT_*", events);
                // 在任何切图之前落盘；强退导致没有 SUMMARY 时仍然有完整待测清单。
                WriteCoverageSnapshot();
                metrics = "features=" + _coverage.FeatureCount + ",manual=" + _coverage.ManualCount;
                return true;
            }
            catch (Exception e) { reason = e.ToString(); return false; }
        }

        private void WriteCoverageSnapshot()
        {
            if (_coverage == null) return;
            File.WriteAllText(Path.ChangeExtension(_reportPath, ".coverage.md"),
                _coverage.Render(_runId, _reportPath), Encoding.UTF8);
        }

        private string FinishCoverage()
        {
            if (_coverage == null) return "ERROR";
            WriteCoverageSnapshot();
            string state = _coverage.HasPending ? "INCOMPLETE" : "COMPLETE";
            WriteRaw("COVERAGE | " + state + " | auto_not_passed=" + _coverage.AutomaticNotPassed
                + " | manual_pending=" + _coverage.ManualCount
                + " | checklist=" + Path.ChangeExtension(_reportPath, ".coverage.md"));
            return state;
        }
    }

    /// <summary>不依赖 Unity 的报告模型；由 token parser 严格读取随包发布的覆盖清单。</summary>
    internal sealed class GameplayCoverageReport
    {
        private sealed class Feature
        {
            internal string Id, Title;
            internal List<string> Automatic;
            internal readonly List<ManualCase> Manual = new List<ManualCase>();
        }
        private sealed class ManualCase { internal string Id, Steps, Expected; }
        private readonly List<Feature> _features = new List<Feature>();
        private readonly Dictionary<string, string> _outcomes = new Dictionary<string, string>(StringComparer.Ordinal);
        internal int FeatureCount { get { return _features.Count; } }
        internal int ManualCount
        {
            get { int count = 0; foreach (Feature feature in _features) count += feature.Manual.Count; return count; }
        }
        internal int AutomaticNotPassed
        {
            get
            {
                HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);
                foreach (Feature feature in _features)
                    foreach (string id in feature.Automatic)
                        if (Outcome(id) != "PASS") pending.Add(id);
                return pending.Count;
            }
        }
        internal bool HasPending { get { return ManualCount > 0 || AutomaticNotPassed > 0; } }

        internal static GameplayCoverageReport Parse(string json)
        {
            ModeHJsonValue root;
            string error;
            if (!ModeHJsonParser.TryParse(json, out root, out error)) throw new FormatException(error);
            int version;
            List<ModeHJsonValue> rows;
            if (root == null || !root.TryGetInt("version", out version) || version != 1
                || !root.TryGetArray("features", out rows) || rows.Count == 0)
                throw new FormatException("coverage_manifest_invalid");
            GameplayCoverageReport report = new GameplayCoverageReport();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModeHJsonValue row in rows)
            {
                Feature feature = new Feature();
                feature.Id = Required(row, "id");
                feature.Title = Required(row, "title");
                List<ModeHJsonValue> manual;
                if (!ids.Add(feature.Id) || !row.TryGetStringList("automatic", out feature.Automatic)
                    || !row.TryGetArray("manual", out manual)) throw new FormatException("invalid_feature:" + feature.Id);
                foreach (ModeHJsonValue item in manual)
                {
                    ManualCase test = new ManualCase { Id = Required(item, "id"), Steps = Required(item, "steps"),
                        Expected = Required(item, "expected") };
                    if (!ids.Add(test.Id)) throw new FormatException("duplicate_case:" + test.Id);
                    feature.Manual.Add(test);
                }
                if (feature.Automatic.Count + feature.Manual.Count == 0)
                    throw new FormatException("uncovered_feature:" + feature.Id);
                report._features.Add(feature);
            }
            return report;
        }

        private static string Required(ModeHJsonValue row, string key)
        {
            string value;
            if (row == null || !row.TryGetString(key, out value) || string.IsNullOrWhiteSpace(value))
                throw new FormatException("coverage_missing:" + key);
            return value;
        }

        internal void Expand(string group, IEnumerable<string> members)
        {
            List<string> ids = new List<string>(members);
            if (ids.Count == 0) throw new FormatException("coverage_empty_group:" + group);
            foreach (Feature feature in _features)
            {
                if (!feature.Automatic.Remove(group)) continue;
                feature.Automatic.AddRange(ids);
            }
        }

        internal void Record(string id, string outcome)
        {
            string previous;
            // 同一用例重试成功也不能覆盖本轮已经观察到的失败。
            if (_outcomes.TryGetValue(id, out previous) && previous == "FAIL") return;
            _outcomes[id] = outcome;
        }

        private string Outcome(string id)
        {
            string outcome;
            return _outcomes.TryGetValue(id, out outcome) ? outcome : "NOT_RUN";
        }

        internal string Render(string runId, string reportPath)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("# 游戏内功能覆盖与人工验收清单");
            text.AppendLine("\n运行：" + runId + "  \n自动日志：" + reportPath);
            text.AppendLine("\n快照 UTC：" + DateTime.UtcNow.ToString("O") + "。按阶段更新；若强退，以自动日志中的逐项结果为准。");
            text.AppendLine("\n自动结果只证明列出的断言。MANUAL_PENDING 需要真实操作与证据，不能据自动 PASS 勾选。");
            text.AppendLine("\n自动尚未通过：" + AutomaticNotPassed + "；人工待验：" + ManualCount + "。");
            text.AppendLine("\n人工前提：Dev 构建、专用测试档；记录 DLL 版本、地图、语言、槽位和日志/截图。资产与中断恢复用可丢弃的测试档副本。每个场景都记录结果与证据；此文件由 F3 覆写，人工记录另存副本。");
            foreach (Feature feature in _features)
            {
                text.AppendLine("\n## " + feature.Id + " · " + feature.Title + "\n");
                foreach (string id in feature.Automatic) text.AppendLine("- 自动 `" + id + "`：**" + Outcome(id) + "**");
                foreach (ManualCase test in feature.Manual)
                {
                    text.AppendLine("\n- [ ] `" + test.Id + "` **MANUAL_PENDING**");
                    text.AppendLine("  - 操作：" + test.Steps);
                    text.AppendLine("  - 预期：" + test.Expected);
                    text.AppendLine("  - 结果 / 证据：待填");
                }
            }
            return text.ToString();
        }
    }
}
