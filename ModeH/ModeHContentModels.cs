// Mode H 静态内容模型（设计提案 §23.2）。
// 只放来自 Assets/Data/ModeH/*.json 的只读模型类；加载、签名核对与解析
// 实现在 ModeHContentCatalog.cs，拆分只为遵守单文件 1200 行预算。
using System.Collections.Generic;

namespace BossRush
{
    #region 内容模型（只读，来自 Assets/Data/ModeH/*.json）

    /// <summary>单条口令 effect 的调制描述（Commands.json / Scars.json 共用）。</summary>
    public sealed class ModeHEffectSpec
    {
        /// <summary>effectId，命名固定为 &lt;commandId&gt;.&lt;controlPointId&gt;。</summary>
        public string EffectId;
        /// <summary>控制点 ID，必须落在 §17.6.2 白名单内。</summary>
        public string ControlPointId;
        /// <summary>操作类别：multiply / multiply_capped / set_bool / set_value / add_seconds_milli /
        /// set_marker_window_end / set_marker_past / fire_* / self_settled*。</summary>
        public string Op;
        /// <summary>千分之一整数倍率。</summary>
        public int MultiplierMilli;
        /// <summary>千分之一整数上限。</summary>
        public int CapMilli;
        /// <summary>千分之一整数绝对值。</summary>
        public int ValueMilli;
        /// <summary>千分之一秒增量。</summary>
        public int AddMilli;
        /// <summary>布尔目标值。</summary>
        public bool BoolValue;
        /// <summary>是否由 Mode H 自结算（对任何 stable key 恒为 VerifiedBehavior）。</summary>
        public bool SelfSettled;
        /// <summary>窗口结束时是否还原（nextReleaseSkillTimeMarker 固定 false）。</summary>
        public bool Restore;
        /// <summary>收益/代价角色（战痕使用）。</summary>
        public string Role;
        /// <summary>生效条件标签（战痕/伤病使用）。</summary>
        public string AppliesWhen;
        /// <summary>目标口令 ID（self_settled_command_scale 使用）。</summary>
        public string TargetCommandId;
        /// <summary>目标装备槽（self_settled_kit_slot_disabled 使用）。</summary>
        public string TargetSlot;
        /// <summary>局部窗口秒数（0 表示沿用条目窗口）。</summary>
        public int WindowSeconds;
    }

    /// <summary>一条口令定义。</summary>
    public sealed class ModeHCommandSpec
    {
        /// <summary>口令稳定 ID。</summary>
        public string CommandId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>说明 key。</summary>
        public string DescKey;
        /// <summary>基础意图。</summary>
        public string Intent;
        /// <summary>是否为招牌口令。</summary>
        public bool IsSignature;
        /// <summary>招牌口令所属原型。</summary>
        public string ArchetypeId;
        /// <summary>要求敌方同时在场数量下限（0 表示无要求）。</summary>
        public int RequiresEnemyCountAtLeast;
        /// <summary>是否只有接力者实际登场后才有效（handoff）。</summary>
        public bool RequiresRelayEntered;
        /// <summary>逐 effect 调制。</summary>
        public List<ModeHEffectSpec> Effects;
    }

    /// <summary>一条伤病定义。</summary>
    public sealed class ModeHInjurySpec
    {
        /// <summary>伤病稳定 ID。</summary>
        public string InjuryId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>说明 key。</summary>
        public string DescKey;
        /// <summary>作用域：whole_match / triggered_once / self_settled。</summary>
        public string Scope;
        /// <summary>触发生命比例（千分之一整数，0 表示不适用）。</summary>
        public int TriggerHealthFractionMilli;
        /// <summary>要求敌方同时在场数量下限。</summary>
        public int RequiresEnemyCountAtLeast;
        /// <summary>逐分量调制。</summary>
        public List<ModeHEffectSpec> Components;
    }

    /// <summary>一条战痕定义。</summary>
    public sealed class ModeHScarSpec
    {
        /// <summary>战痕稳定 ID。</summary>
        public string ScarId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>说明 key。</summary>
        public string DescKey;
        /// <summary>触发条件 ID。</summary>
        public string Trigger;
        /// <summary>窗口秒数（0 表示条件型常驻判断）。</summary>
        public int WindowSeconds;
        /// <summary>兼容原型。</summary>
        public List<string> CompatibleArchetypeIds;
        /// <summary>收益赔率标签。</summary>
        public string BenefitTag;
        /// <summary>代价赔率标签。</summary>
        public string CostTag;
        /// <summary>收益赔率分。</summary>
        public int BenefitOdds;
        /// <summary>代价赔率分。</summary>
        public int CostOdds;
        /// <summary>逐分量调制（收益与代价必须同时可用）。</summary>
        public List<ModeHEffectSpec> Components;
    }

    /// <summary>Boss 档案模板。</summary>
    public sealed class ModeHProfileTemplate
    {
        /// <summary>模板稳定 ID。</summary>
        public string ProfileTemplateId;
        /// <summary>官方预设 nameKey（EnemyPresetInfo.name）。</summary>
        public string StableKey;
        /// <summary>显示名 key。</summary>
        public string DisplayNameKey;
        /// <summary>传闻 key。</summary>
        public string RumorKey;
        /// <summary>公开原型。</summary>
        public string ArchetypeId;
        /// <summary>固有底色。</summary>
        public string TemperamentId;
        /// <summary>普通怪癖（与异常互斥）。</summary>
        public string QuirkId;
        /// <summary>公开异常（与怪癖互斥）。</summary>
        public string AnomalyId;
        /// <summary>招牌口令。</summary>
        public string SignatureCommandId;
        /// <summary>看台表演模式。</summary>
        public string StandInPatternId;
        /// <summary>是否进入生产目录。</summary>
        public bool ProductionCandidate;
        /// <summary>生产目录顺序（唯一）。</summary>
        public int ProductionOrder;
        /// <summary>单体威胁评分。</summary>
        public int ThreatScore;
        /// <summary>能力标签（供计划 veto 使用）。</summary>
        public List<string> CapabilityTags;
    }

    /// <summary>虚拟整备套装定义（静态部分；typeId 解析在运行时完成）。</summary>
    public sealed class ModeHKitSpec
    {
        /// <summary>套装稳定 ID。</summary>
        public string KitId;
        /// <summary>是否 starter。</summary>
        public bool IsStarterKit;
        /// <summary>starter 顺序。</summary>
        public int StarterOrder;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>说明 key。</summary>
        public string DescKey;
        /// <summary>替换槽位。</summary>
        public string ReplaceSlot;
        /// <summary>固定官方 typeId（0 表示按 tag 解析）。</summary>
        public int TypeId;
        /// <summary>解析标签。</summary>
        public List<string> ResolveTags;
        /// <summary>解析品质下界。</summary>
        public int ResolveMinQuality;
        /// <summary>解析品质上界。</summary>
        public int ResolveMaxQuality;
        /// <summary>解析序号（候选按 typeId 升序后取该下标）。</summary>
        public int ResolveOrdinal;
        /// <summary>声明品质（1..8）。</summary>
        public int GameQuality;
        /// <summary>固定弹药 typeId（0 表示按口径解析）。</summary>
        public int AmmoTypeId;
        /// <summary>冻结弹药数量。</summary>
        public int AmmoCount;
        /// <summary>是否按枪械口径解析弹药。</summary>
        public bool ResolveAmmoByCaliber;
        /// <summary>兼容原型。</summary>
        public List<string> CompatibleArchetypeIds;
        /// <summary>兼容 profile 模板。</summary>
        public List<string> CompatibleProfileIds;
        /// <summary>公开克制标签。</summary>
        public List<string> PublicTags;
    }

    /// <summary>单场威胁走廊。</summary>
    public sealed class ModeHMatchCorridor
    {
        /// <summary>比赛编号。</summary>
        public int MatchIndex;
        /// <summary>总威胁预算。</summary>
        public int ThreatBudget;
        /// <summary>同时在场上限。</summary>
        public int SimultaneousCap;
        /// <summary>最低填充百分比（防止后期计划过弱）。</summary>
        public int MinFillPercent;
        /// <summary>可用编制骨架。</summary>
        public List<string> SkeletonIds;
    }

    /// <summary>编制骨架。</summary>
    public sealed class ModeHSkeletonSpec
    {
        /// <summary>骨架 ID。</summary>
        public string SkeletonId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>最少单位数。</summary>
        public int MinUnits;
        /// <summary>最多单位数。</summary>
        public int MaxUnits;
        /// <summary>公开标签。</summary>
        public List<string> PublicTags;
        /// <summary>是否含已公开的高威胁核心。</summary>
        public bool HasHighThreatCore;
        /// <summary>带伤单位数。</summary>
        public int WoundedUnits;
        /// <summary>是否需要落选回响核心。</summary>
        public bool RequiresEchoReturn;
    }

    /// <summary>进场剧本。</summary>
    public sealed class ModeHEntryScriptSpec
    {
        /// <summary>剧本 ID。</summary>
        public string EntryScriptId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>公开提示 key。</summary>
        public string HintKey;
        /// <summary>分批入场人数序列。</summary>
        public List<int> BatchPattern;
        /// <summary>公开标签。</summary>
        public List<string> PublicTags;
        /// <summary>核心是否压轴。</summary>
        public bool CoreEntersLast;
        /// <summary>是否保留一个未知席位。</summary>
        public bool HiddenSeat;
    }

    /// <summary>擂台条件。</summary>
    public sealed class ModeHArenaConditionSpec
    {
        /// <summary>条件 ID。</summary>
        public string ConditionId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>公开标签。</summary>
        public List<string> PublicTags;
        /// <summary>受益原型。</summary>
        public List<string> FavoredArchetypeIds;
        /// <summary>受损原型。</summary>
        public List<string> DisfavoredArchetypeIds;
    }

    /// <summary>协同类别（威胁预算上浮与公开摘要共用）。</summary>
    public sealed class ModeHSynergyCategory
    {
        /// <summary>类别 ID：heal / summon / control。</summary>
        public string CategoryId;
        /// <summary>公开标签（进入 publicSummary.synergyTags）。</summary>
        public string PublicTag;
        /// <summary>命中时的预算上浮百分比。</summary>
        public int BudgetShare;
    }

    /// <summary>侦察类别（§17.5 四类，每场至多消费一条）。</summary>
    public sealed class ModeHReconChoiceSpec
    {
        /// <summary>侦察稳定 ID。</summary>
        public string ReconChoiceId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>被揭示的公开摘要字段名。</summary>
        public string RevealField;
    }

    /// <summary>原型能力矩阵条目（roster-level veto 使用）。</summary>
    public sealed class ModeHArchetypeCapability
    {
        /// <summary>原型 ID。</summary>
        public string ArchetypeId;
        /// <summary>主要手段标签。</summary>
        public List<string> PrimaryAnswers;
        /// <summary>会被硬封死的敌方能力标签。</summary>
        public List<string> HardLockedBy;
    }

    /// <summary>赔率档位。</summary>
    public sealed class ModeHOddsTier
    {
        /// <summary>净赔率倍数。</summary>
        public int Odds;
        /// <summary>公开分差下界。</summary>
        public int MinPublicEdge;
        /// <summary>公开分差上界。</summary>
        public int MaxPublicEdge;
        /// <summary>盘口称呼 key。</summary>
        public string ToneKey;
    }

    /// <summary>口令与公开标签的相合/冲突映射。</summary>
    public sealed class ModeHCommandTagMapping
    {
        /// <summary>口令 ID。</summary>
        public string CommandId;
        /// <summary>相合标签。</summary>
        public List<string> AlignedTags;
        /// <summary>冲突标签。</summary>
        public List<string> ConflictedTags;
    }

    /// <summary>赔率公开分差测试向量。</summary>
    public sealed class ModeHOddsTestVector
    {
        /// <summary>向量 ID。</summary>
        public string VectorId;
        /// <summary>玩家公开分。</summary>
        public int PlayerPublicScore;
        /// <summary>敌方公开分。</summary>
        public int EnemyPublicScore;
        /// <summary>期望赔率档。</summary>
        public int ExpectedOdds;
    }

    #endregion
}
