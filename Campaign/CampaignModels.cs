// ============================================================================
// CampaignModels.cs - 鸭王征程运行时模型与枚举（M0 骨架）
// ============================================================================
// 只放纯数据结构与枚举，不放任何行为、不碰 Unity 生命周期。
//
// 存档 DTO **不在本文件**：落盘结构在 CampaignPersistence.cs，遵循
// ModeGProfilePersistence 的纪律（[Serializable]、禁字段初始化器）。
// 本文件的类型是运行时内存态，可以有字段初始化器。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>单章在玩家侧的可见状态。</summary>
    internal enum CampaignChapterState
    {
        /// <summary>前置章未完成，杰夫的任务页上不出现。</summary>
        Locked = 0,

        /// <summary>可接取。</summary>
        Available = 1,

        /// <summary>已接取，契约生效中（局内 HUD 追踪目标）。</summary>
        ContractActive = 2,

        /// <summary>局内目标已全部达成，等玩家回基地找杰夫交付（基地侧目标另在交付时核对）。</summary>
        ReadyToDeliver = 3,

        /// <summary>已交付结算。</summary>
        Completed = 4
    }

    /// <summary>
    /// 契约目标类型白名单。数据表里的 objective.type 只能取这里的值，
    /// 由 CampaignContentCatalog 校验；未知值整表回退硬编码内容。
    /// </summary>
    internal enum CampaignObjectiveKind
    {
        /// <summary>未知/解析失败。永远视为不可完成，不静默当成已完成。</summary>
        Unknown = 0,

        /// <summary>标准竞技场通关（排除无间炼狱）。</summary>
        StandardClear = 1,

        /// <summary>指定波次结束前玩家未受任何伤害。</summary>
        NoDamageUntilWave = 2,

        /// <summary>近战击杀数达标。</summary>
        MeleeKills = 3,

        /// <summary>本局到达指定波次。</summary>
        ReachWave = 4,

        /// <summary>击杀敌方 Boss 数达标（Mode E）。</summary>
        FactionBossKills = 5,

        /// <summary>入场后存活时长达标（分钟）。</summary>
        SurviveMinutes = 6,

        /// <summary>击杀带悬赏印记的 Boss 数达标（Mode F）。</summary>
        BountyKills = 7,

        /// <summary>当前模式撤离成功。</summary>
        ModeExtract = 8,

        /// <summary>终章 Boss 击杀。</summary>
        FinalBossKill = 9,

        /// <summary>基地侧：官方菜地已建成（事实由 Integration/BackMountain 经 CampaignBaseObjectives 提供）。</summary>
        GardenBuilt = 10,

        /// <summary>基地侧：官方陈列柜 / 枪械展示架 / 假人里摆着至少一件 Mod 战利品。</summary>
        TrophyDisplayed = 11
    }

    /// <summary>
    /// 契约目标定义（内容侧，只读）。由数据表或硬编码 fallback 提供。
    /// </summary>
    internal sealed class CampaignObjectiveDef
    {
        /// <summary>目标类型。</summary>
        internal CampaignObjectiveKind Kind = CampaignObjectiveKind.Unknown;

        /// <summary>阈值语义随 Kind 变化：击杀数 / 波次号 / 分钟数。</summary>
        internal int Threshold;

        /// <summary>中文描述（HUD 与官方任务日志共用）。</summary>
        internal string DescCN = string.Empty;

        /// <summary>英文描述。</summary>
        internal string DescEN = string.Empty;

        /// <summary>
        /// 基地侧目标：不进局内追踪器、不落盘、随时可做；ReadyToDeliver 仍只看局内目标，
        /// 交付另外要求它们全真（CampaignQuestTable.CanDeliver）。
        /// </summary>
        internal bool IsBaseScope
        {
            get { return Kind == CampaignObjectiveKind.GardenBuilt || Kind == CampaignObjectiveKind.TrophyDisplayed; }
        }
    }

    /// <summary>
    /// 单个目标在本局的进度（运行时态，不落盘）。
    /// 局中失败或离场即整体丢弃，契约保持已接取状态可重试。
    /// </summary>
    internal sealed class CampaignObjectiveProgress
    {
        /// <summary>对应的目标定义。</summary>
        internal CampaignObjectiveDef Def;

        /// <summary>当前计数。语义随 Kind 变化。</summary>
        internal int Current;

        /// <summary>
        /// 是否已判定失败（例如无伤目标已被破防）。
        /// 与「未达标」区分：失败后本局不可能再完成，HUD 应显式标红。
        /// </summary>
        internal bool Failed;

        /// <summary>是否已达标。</summary>
        internal bool IsSatisfied
        {
            get
            {
                if (Failed) return false;
                if (Def == null) return false;
                if (Def.Kind == CampaignObjectiveKind.Unknown) return false;
                return Current >= Def.Threshold;
            }
        }
    }

    /// <summary>
    /// 章节定义（内容侧，只读）。字段与 Assets/Data/Campaign/Chapters.json 对应。
    /// </summary>
    internal sealed class CampaignChapterDef
    {
        /// <summary>章节 ID（如 "ch1"）。数据表内唯一。</summary>
        internal string ChapterId = string.Empty;

        /// <summary>章节序号，从 1 起且必须连续。</summary>
        internal int Order;

        /// <summary>该章契约指向的模式标识（standard/modeD/modeE/modeF/zombie/final）。</summary>
        internal string Mode = string.Empty;

        /// <summary>中文标题。</summary>
        internal string TitleCN = string.Empty;

        /// <summary>英文标题。</summary>
        internal string TitleEN = string.Empty;

        /// <summary>本章目标列表。</summary>
        internal readonly List<CampaignObjectiveDef> Objectives = new List<CampaignObjectiveDef>();

        /// <summary>交付奖励现金。</summary>
        internal int RewardCash;

        /// <summary>交付时授予的设施解锁 token（可为空）。</summary>
        internal string FacilityToken = string.Empty;

        /// <summary>本章解锁的线索 ID（可为空）。</summary>
        internal string ClueId = string.Empty;
    }
}
