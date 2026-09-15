using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BossRush;

/// <summary>
/// 全自动实机验收的离线半边。每一节都有绿样本与能转红的红样本；断言失败不中断，全部跑完再汇总，
/// 这样一处数据漂移不会遮住后面各节。入口：python tools/run_runtime_regressions.py --filter F3AutotestJudges。
/// </summary>
internal static partial class Program
{
    private static int checks;
    private static readonly List<string> failures = new List<string>();
    private static string root;

    private static void Check(bool value, string description)
    {
        checks++;
        if (value) return;
        failures.Add(description);
        Console.WriteLine("FAIL " + description);
    }

    private static void Section(string name, Action body)
    {
        try { body(); }
        catch (Exception e)
        {
            checks++;
            failures.Add(name + " threw");
            Console.WriteLine("FAIL " + name + " threw: " + e);
        }
    }

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        root = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
        Section("step table", TableCases);
        Section("stages", StageCases);
        Section("snapshot codec", SnapshotCodecCases);
        Section("snapshot on story service", SnapshotServiceCases);
        Section("linear contrast", ContrastCases);
        Section("visibility", VisibilityCases);
        Section("ring coverage", RingCoverageCases);
        Section("results and manifest", ManifestCases);
        Section("boss corpse crate", BossDropCases);
        if (failures.Count > 0)
        {
            Console.WriteLine("FAILED: " + failures.Count + " of " + checks + " assertions");
            return 1;
        }
        Console.WriteLine("PASS: " + checks + " assertions");
        return 0;
    }

    private static string RepoPath(string relative)
    {
        return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool AnyStartsWith(IEnumerable<string> values, string prefix)
    {
        foreach (string value in values) if (value != null && value.StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool AnyContains(IEnumerable<string> values, string part)
    {
        foreach (string value in values) if (value != null && value.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    private static string Dump(IList<string> values)
    {
        if (values == null || values.Count == 0) return "(none)";
        var parts = new List<string>();
        for (int i = 0; i < values.Count && i < 12; i++) parts.Add(values[i]);
        return string.Join(" | ", parts.ToArray()) + (values.Count > 12 ? " | …+" + (values.Count - 12) : string.Empty);
    }

    private static bool Near(double actual, double expected, double tolerance)
    {
        return Math.Abs(actual - expected) <= tolerance;
    }
}
