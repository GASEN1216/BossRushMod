#if BOSSRUSH_DEV
// 全自动验收的纯数据模型；与 AutotestJudges 一起由隔离回归编译，不依赖游戏对象。
using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>步骤表里的一个阶段。<see cref="When"/> 决定它在编排里什么时候跑；只有 story 阶段带剧情推进动作。</summary>
    internal sealed class F3AutotestStage
    {
        internal string Id, Title, When;
        internal readonly List<string> Apply = new List<string>();
    }

    /// <summary>步骤表里的一步：在某个阶段、某个位置做一串动作，至少一条断言或一张截图。</summary>
    internal sealed class F3AutotestStep
    {
        internal string Id, Title, Stage, Location, Class;
        internal float BudgetSeconds;
        internal readonly List<string> Checklist = new List<string>();
        internal readonly List<string> Actions = new List<string>();
    }

    /// <summary>清单一行的归类：自动断言 / 截图给 AI 看 / 只能人工（写理由）。</summary>
    internal sealed class F3AutotestCoverageRow
    {
        internal string Checklist, Class, Reason;
        internal readonly List<string> Evidence = new List<string>();
    }

    internal sealed class F3AutotestTable
    {
        internal int Version;
        internal readonly List<F3AutotestStage> Stages = new List<F3AutotestStage>();
        internal readonly List<F3AutotestStep> Steps = new List<F3AutotestStep>();
        internal readonly List<F3AutotestCoverageRow> Coverage = new List<F3AutotestCoverageRow>();

        internal F3AutotestStage FindStage(string id)
        {
            for (int i = 0; i < Stages.Count; i++) if (string.Equals(Stages[i].Id, id, StringComparison.Ordinal)) return Stages[i];
            return null;
        }
    }

    internal enum F3AutotestOpKind { Reset, ClearEncounter, StoryAction, RecordNote, LightLamp }

    /// <summary>
    /// 剧情阶段的一条推进动作。只有这五种：清空、记清场、规则动作、手记、点风晶灯——都是生产侧的合法入口
    /// （点灯走 SkyIslandFieldcraft.LightLamp：先发够材料再点，场景里的灯与手记一起落下，与玩家在装置上点灯是同一条代码）。
    /// </summary>
    internal struct F3AutotestStageOp
    {
        internal F3AutotestOpKind Kind;
        internal string Argument;
        internal SkyIslandStoryAction Action;
    }

    internal sealed class F3AutotestAssertion
    {
        internal string Name, Result, Reason, Metrics;
    }

    internal sealed class F3AutotestShot
    {
        internal string Name, File, Kind, Encoding, Metrics;
        internal long Bytes;
    }

    /// <summary>manifest 里的一步：步骤表里的静态信息 + 这一轮实际跑出来的结果。</summary>
    internal sealed class F3AutotestStepRecord
    {
        internal string Id, Title, Stage, Location, Class, Language, Result, Reason, StartedUtc;
        internal long DurationMs;
        internal readonly List<string> Checklist = new List<string>();
        internal readonly List<string> Actions = new List<string>();
        internal readonly List<F3AutotestAssertion> Assertions = new List<F3AutotestAssertion>();
        internal readonly List<F3AutotestShot> Shots = new List<F3AutotestShot>();
        internal readonly List<string> Notes = new List<string>();
    }

    /// <summary>一轮自动验收的元信息：写进 manifest 头与 summary 顶部。</summary>
    internal sealed class F3AutotestRunInfo
    {
        internal string RunId, Mvid, Language, AltLanguage, StartedUtc, EndedUtc, Status, ReportLog;
        internal int Slot;
        internal string RestoreStory = "NOT_RUN", RestoreDetail = string.Empty, ItemLedger = string.Empty, MoneyLedger = string.Empty;
        internal string EnvironmentRestore = "NOT_RUN";
        internal long ShotBytes;
        internal int ShotCount, ShotsDegraded, ShotsSkipped;
    }

    /// <summary>
    /// 开跑前的快照（纯记录）：剧情原文逐字、官方图鉴里已点亮的天空岛条目、天空岛物品件数、钱、语言、强制夜里、timeScale。
    /// 写进存档键 BossRush_Validation_AutotestSnapshot_v1；游戏中途崩了，下次回基地据它还原，所以编码必须往返不变形。
    /// </summary>
    internal sealed class F3AutotestSnapshotRecord
    {
        internal string RunId, Raw, Language;
        internal int Slot;
        internal bool RawExists, ForceNight, BufferCountsIncluded;
        internal long Money;
        internal float TimeScale = 1f;
        internal readonly List<string> OfficialUnlocked = new List<string>();
        internal readonly Dictionary<int, int> Items = new Dictionary<int, int>();
    }

}
#endif
