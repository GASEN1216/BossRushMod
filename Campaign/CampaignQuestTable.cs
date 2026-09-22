// ============================================================================
// CampaignQuestTable.cs - 鸭王征程接官方任务系统的纯规则表
// ============================================================================
// 六章各投影成一条官方 Quest（590101–590106，给予者官方 Jeff = 1，2026-09-22 owner 授权）。
// 本文件**无 Unity / Duckov 依赖**：ID 映射、可接取 / 可交付 / 目标完成的判据、目标行文案
// 全是纯函数，tests/fixtures/CampaignPlayability 直接链接穷举。
//
// 权威仍是 CampaignProgressService（BossRush_Campaign_Progress_v1）：
//   Available ↔ 官方可接取页；接受 → TryAcceptContract；Task 行 ← 本局目标进度 / 基地侧事实；
//   官方「完成任务」按钮 → TryDeliver。判据与 TryAcceptContract / TryDeliver 的前置同源，
//   保证「能不能挂」与「点了会不会被拒」是同一份口径（AGENTS §4.14）。
//
// 【基地侧目标】garden_built / trophy_displayed 不进局内追踪器、不落盘、随时可做：
//   ReadyToDeliver 仍只由局内目标同局达成触发；交付另外要求基地侧目标全真。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>征程官方任务表：ID 映射与纯判据。</summary>
    internal static class CampaignQuestTable
    {
        #region 常量（冻结：进 docs/contracts.md §7.1）

        /// <summary>ch1..ch6 = 590101..590106；BossRush 保留段，下一可用 590107。</summary>
        internal const int QuestIdBase = 590100;

        /// <summary>(int)QuestGiverID.Jeff。本文件不引用官方枚举。</summary>
        internal const int JeffGiverId = 1;

        private const string ObjectNamePrefix = "BossRush_Campaign_Quest_";
        private const string QuestKeyPrefix = "BossRush_Campaign_";

        #endregion

        #region ID 映射

        internal static int QuestIdForOrder(int order)
        {
            if (order < CampaignTuning.FirstChapter || order >= CampaignTuning.FirstChapter + CampaignTuning.ChapterCount) return 0;
            return QuestIdBase + order;
        }

        /// <summary>越界返回 0。</summary>
        internal static int OrderForQuestId(int questId)
        {
            int order = questId - QuestIdBase;
            if (order < CampaignTuning.FirstChapter || order >= CampaignTuning.FirstChapter + CampaignTuning.ChapterCount) return 0;
            return order;
        }

        internal static string ObjectName(int questId) { return ObjectNamePrefix + questId; }

        internal static string NameKey(string chapterId) { return QuestKeyPrefix + chapterId + "_Name"; }

        internal static string DescriptionKey(string chapterId) { return QuestKeyPrefix + chapterId + "_Description"; }

        #endregion

        #region 纯判据

        /// <summary>可接取：与 TryAcceptContract 的两条前置同源（Available、没有别的契约在进行）。</summary>
        internal static bool CanOffer(CampaignChapterState state, bool canWrite, bool anotherActive)
        {
            return state == CampaignChapterState.Available && canWrite && !anotherActive;
        }

        /// <summary>可交付：局内目标已同局达成（ReadyToDeliver）、能写盘、基地侧目标全真。</summary>
        internal static bool CanDeliver(CampaignChapterState state, bool canWrite, bool baseObjectivesDone)
        {
            return state == CampaignChapterState.ReadyToDeliver && canWrite && baseObjectivesDone;
        }

        /// <summary>
        /// 一条目标是否完成（官方 Task.CheckFinished）。
        /// 已结算章（ReadyToDeliver / Completed）里的局内目标恒真；
        /// 基地侧目标读 baseFact（Completed 时恒真）；局内目标只在 ContractActive 且本局武装时看进度。
        /// </summary>
        internal static bool IsObjectiveDone(CampaignObjectiveDef def, CampaignChapterState state,
            bool armedForChapter, CampaignObjectiveProgress progress, bool baseFact)
        {
            if (def == null) return false;
            if (state == CampaignChapterState.Completed) return true;
            if (def.IsBaseScope) return baseFact;
            if (state == CampaignChapterState.ReadyToDeliver) return true;
            if (state != CampaignChapterState.ContractActive) return false;
            return armedForChapter && progress != null && progress.Def == def && progress.IsSatisfied;
        }

        /// <summary>按定义引用在本局进度里找对应项；基地侧目标不进追踪器，恒 null。</summary>
        internal static CampaignObjectiveProgress FindProgress(IList<CampaignObjectiveProgress> progress, CampaignObjectiveDef def)
        {
            if (progress == null || def == null) return null;
            for (int i = 0; i < progress.Count; i++)
                if (progress[i] != null && progress[i].Def == def) return progress[i];
            return null;
        }

        /// <summary>
        /// 官方任务日志的目标行（由退役的公告板 BuildDetailText 原样提取）。
        /// settled = 章已结算；progress == null 且未结算 = 读档后本局未武装，只写目标本身、不带数字。
        /// </summary>
        internal static string DescribeObjective(CampaignObjectiveDef def, CampaignObjectiveProgress progress, bool settled, bool baseFact)
        {
            if (def == null) return string.Empty;
            string text = L10n.T(def.DescCN, def.DescEN);
            if (def.IsBaseScope)
            {
                text = L10n.T("基地：", "Base: ") + text;
                return settled || baseFact ? text + L10n.T("（已达成）", " (done)") : text;
            }
            if (settled) return text + L10n.T("（已达成）", " (done)");
            if (progress == null || progress.Def != def) return text;
            if (progress.Failed) return text + L10n.T("（本局已失败）", " (failed this run)");
            if (def.Threshold > 1) return text + " (" + progress.Current + "/" + def.Threshold + ")";
            if (progress.IsSatisfied) return text + L10n.T("（已达成）", " (done)");
            return text;
        }

        /// <summary>目标行下方的一句提示：局内目标写去哪个模式，基地侧目标写「随时可做」。</summary>
        internal static string DescribeObjectiveHint(CampaignObjectiveDef def, string mode, bool settled)
        {
            if (def == null || settled) return null;
            if (def.IsBaseScope)
                return L10n.T("在基地随时可做，不用和局内目标同一局完成。", "Do this at base any time. It does not need to happen in the same run.");
            return L10n.T("前往：", "Go to: ") + GetModeDisplayName(mode);
        }

        internal static string GetModeDisplayName(string mode)
        {
            switch (mode)
            {
                case CampaignContentCatalog.ModeStandard:
                    return L10n.T("标准竞技场", "Standard Arena");
                case CampaignContentCatalog.ModeModeD:
                    return L10n.T("白手起家", "From Scratch");
                case CampaignContentCatalog.ModeModeE:
                    return L10n.T("划地为营", "Faction War");
                case CampaignContentCatalog.ModeModeF:
                    return L10n.T("血猎追击", "Blood Hunt");
                case CampaignContentCatalog.ModeZombie:
                    return L10n.T("末日丧尸", "Zombie Mode");
                case CampaignContentCatalog.ModeFinal:
                    return L10n.T("竞技场决战", "Arena Showdown");
                default:
                    return mode ?? string.Empty;
            }
        }

        /// <summary>事实指纹：状态、本局各目标计数 / 失败位、基地侧目标位。变化时刷给予者标记。</summary>
        internal static int ComputeStateStamp(CampaignChapterState state, bool armedForChapter,
            IList<CampaignObjectiveProgress> progress, int baseBits)
        {
            unchecked
            {
                int hash = ((int)state << 8) ^ baseBits;
                if (armedForChapter && progress != null)
                {
                    for (int i = 0; i < progress.Count; i++)
                    {
                        CampaignObjectiveProgress item = progress[i];
                        hash = hash * 31 + (item == null ? 0 : item.Current * 2 + (item.Failed ? 1 : 0));
                    }
                }
                return hash;
            }
        }

        #endregion
    }
}
