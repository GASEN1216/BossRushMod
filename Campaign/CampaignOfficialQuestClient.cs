// ============================================================================
// CampaignOfficialQuestClient.cs - 鸭王征程接官方任务投影核心的客户端
// ============================================================================
// 六章各投影成一条官方 Quest（见 CampaignQuestTable），挂在官方 Jeff 名下：
//   官方可接取页 ← CanOffer；接受 → CampaignProgressService.TryAcceptContract；
//   官方任务日志的目标行 ← 本局进度 / 基地侧事实；官方「完成任务」按钮 → TryDeliver。
// 发钱、发 token、写线索仍归 TryDeliver 的补偿式事务（PayReward = null）；
// 官方奖励行只是展示（RewardMoney = def.RewardCash，同一字段源），「已领取」读 Completed。
//
// 整个 Campaign/ 目录不出现任何 Duckov.Quests 符号：注册、投影、补丁、快照过滤、
// 给予者查找全在 Utilities/OfficialQuests/（tests/CampaignSkeletonGuard.py）。
//
// 【交付后的表现顺序】官方 TryComplete 成功 → 官方完成面板（异步）→ 面板关掉后播杰夫对话
//   （BossRushUI.IsOfficialHudHidden 覆盖 View.ActiveView 与 DialogueUI.Active）→ 对话结束后弹解锁飘字。
//   漏播不丢线索：CampaignNoteBridge 每次进基地按存档自愈。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>征程的官方任务客户端。宿主：CampaignRuntimeModule（唯一 owner）。</summary>
    internal sealed class CampaignOfficialQuestClient : IOfficialQuestClient
    {
        private const string QuestLogTag = "[CampaignQuest]";
        private const float NoticeDelayAfterDialogue = 1f;

        private readonly CampaignRuntimeModule _module;
        private OfficialQuestProjection _projection;
        private bool _registered;
        private CampaignChapterDef _pendingDialogue;
        private string _pendingNotice;
        private float _noticeReadyAt;

        internal CampaignOfficialQuestClient(CampaignRuntimeModule module)
        {
            _module = module;
        }

        internal bool IsRegistered { get { return _registered; } }

        /// <summary>把六章登记进共享核心。幂等；核心缺席时只记一次日志。</summary>
        internal void RegisterAll(OfficialQuestProjection projection)
        {
            if (_registered) return;
            if (projection == null)
            {
                ModBehaviour.CriticalLog("campaign-quest-core-missing",
                    QuestLogTag + " [ERROR] 官方任务投影核心未就绪，征程任务不会出现在杰夫的任务页。");
                return;
            }
            _projection = projection;
            IList<CampaignChapterDef> chapters = CampaignContentCatalog.Chapters;
            for (int i = 0; i < chapters.Count; i++)
            {
                CampaignChapterDef def = chapters[i];
                if (def == null) continue;
                OfficialQuestBinding binding = BuildBinding(def);
                if (binding != null) projection.Register(binding);
            }
            _registered = true;
        }

        /// <summary>关闭开关 / 宿主销毁：整体撤销（核心先冻结所有权再清投影）。</summary>
        internal void UnregisterAll()
        {
            if (_registered && _projection != null) _projection.UnregisterClient(this);
            _registered = false;
            ClearPending();
        }

        internal void ClearPending()
        {
            _pendingDialogue = null;
            _pendingNotice = null;
        }

        #region IOfficialQuestClient

        public string LogTag { get { return QuestLogTag; } }

        /// <summary>征程 bootstrap 完成且当前槽存档已读出；否则核心对本客户端不清不建。</summary>
        public bool Ready
        {
            get
            {
                try { return _module != null && _module.IsBootstrapped && _module.IsEnabled && CampaignPersistence.Current != null; }
                catch (Exception) { return false; }
            }
        }

        public int Slot
        {
            get
            {
                try { return Saves.SavesSystem.CurrentSlot; }
                catch (Exception) { return -1; }
            }
        }

        public void BeginTick()
        {
        }

        public void EndTick(bool dirty)
        {
            if (dirty) RefreshMarkers();
            if (_pendingDialogue != null)
            {
                if (BossRushUI.IsOfficialHudHidden() || BossRushUI.IsGamePaused()) return;
                CampaignChapterDef def = _pendingDialogue;
                _pendingDialogue = null;
                CampaignDialoguePlayer.PlayChapterDelivered(def);
                _pendingNotice = CampaignContentCatalog.GetDeliveredNotice(def.ChapterId);
                _noticeReadyAt = Time.unscaledTime + NoticeDelayAfterDialogue;
                return;
            }
            if (_pendingNotice != null && Time.unscaledTime >= _noticeReadyAt
                && !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused())
            {
                string notice = _pendingNotice;
                _pendingNotice = null;
                try { Duckov.UI.NotificationText.Push(notice); }
                catch (Exception e) { ModBehaviour.DevLog(QuestLogTag + " [WARNING] 解锁提示推送失败: " + e.Message); }
            }
        }

        /// <summary>官方只在任务列表变化时自己刷标记；我们的事实变化（交付、目标达成）不经那些事件。</summary>
        public void RefreshMarkers()
        {
            OfficialQuestGiverLocator.RefreshMarkerFor(CampaignQuestTable.JeffGiverId);
        }

        #endregion

        #region 定义 → 绑定

        private OfficialQuestBinding BuildBinding(CampaignChapterDef def)
        {
            int questId = CampaignQuestTable.QuestIdForOrder(def.Order);
            if (questId <= 0) return null;
            string chapterId = def.ChapterId;
            var binding = new OfficialQuestBinding
            {
                QuestId = questId,
                GiverId = CampaignQuestTable.JeffGiverId,
                ObjectName = CampaignQuestTable.ObjectName(questId),
                NameKey = CampaignQuestTable.NameKey(chapterId),
                DescriptionKey = CampaignQuestTable.DescriptionKey(chapterId),
                RewardMoney = def.RewardCash,
                CanOffer = () => CampaignQuestTable.CanOffer(State(chapterId), CanWrite(), AnotherActive(chapterId)),
                CanDeliver = () => CampaignQuestTable.CanDeliver(State(chapterId), CanWrite(), CampaignBaseObjectives.AllDone(def)),
                DeliverBlocked = () => DescribeBlocked(def),
                IsAccepted = () => IsAcceptedState(State(chapterId)),
                IsDelivered = () => State(chapterId) == CampaignChapterState.Completed,
                RewardPaid = () => State(chapterId) == CampaignChapterState.Completed,
                Accept = (out string message) => Accept(chapterId, out message),
                Deliver = (out string message) => Deliver(def, out message),
                PayReward = null,
                StateStamp = () => CampaignQuestTable.ComputeStateStamp(State(chapterId), Armed(chapterId),
                    CampaignObjectiveTracker.Progress, CampaignBaseObjectives.DoneBits(def)),
                Client = this,
            };
            binding.Tasks = new OfficialQuestTaskBinding[def.Objectives.Count];
            for (int i = 0; i < def.Objectives.Count; i++)
            {
                CampaignObjectiveDef objective = def.Objectives[i];
                binding.Tasks[i] = new OfficialQuestTaskBinding
                {
                    TaskId = i + 1,
                    Done = () => IsDone(chapterId, objective),
                    Description = () => Describe(chapterId, objective),
                    ExtraHint = () => CampaignQuestTable.DescribeObjectiveHint(objective, def.Mode, IsSettled(State(chapterId))),
                };
            }
            return binding;
        }

        private static CampaignChapterState State(string chapterId)
        {
            return CampaignProgressService.GetState(chapterId);
        }

        private static bool IsSettled(CampaignChapterState state)
        {
            return state == CampaignChapterState.ReadyToDeliver || state == CampaignChapterState.Completed;
        }

        private static bool IsAcceptedState(CampaignChapterState state)
        {
            return state == CampaignChapterState.ContractActive || IsSettled(state);
        }

        private static bool CanWrite()
        {
            return !CampaignPersistence.HasWriteBarrier && !CampaignPersistence.IsStoreFaulted;
        }

        private static bool AnotherActive(string chapterId)
        {
            string active = CampaignProgressService.GetActiveChapterId();
            return !string.IsNullOrEmpty(active) && !string.Equals(active, chapterId, StringComparison.Ordinal);
        }

        private static bool Armed(string chapterId)
        {
            return string.Equals(CampaignObjectiveTracker.ArmedChapterId, chapterId, StringComparison.Ordinal);
        }

        private static bool IsDone(string chapterId, CampaignObjectiveDef objective)
        {
            CampaignChapterState state = State(chapterId);
            bool armed = Armed(chapterId);
            CampaignObjectiveProgress progress = armed ? CampaignQuestTable.FindProgress(CampaignObjectiveTracker.Progress, objective) : null;
            bool baseFact = objective.IsBaseScope && CampaignBaseObjectives.IsDone(objective.Kind);
            return CampaignQuestTable.IsObjectiveDone(objective, state, armed, progress, baseFact);
        }

        private static string Describe(string chapterId, CampaignObjectiveDef objective)
        {
            CampaignChapterState state = State(chapterId);
            bool armed = Armed(chapterId);
            CampaignObjectiveProgress progress = armed ? CampaignQuestTable.FindProgress(CampaignObjectiveTracker.Progress, objective) : null;
            bool baseFact = objective.IsBaseScope && CampaignBaseObjectives.IsDone(objective.Kind);
            return CampaignQuestTable.DescribeObjective(objective, progress, IsSettled(state), baseFact);
        }

        private static string DescribeBlocked(CampaignChapterDef def)
        {
            CampaignChapterState state = State(def.ChapterId);
            if (state != CampaignChapterState.ReadyToDeliver)
                return L10n.T("目标还没做完，做完再来找我。", "The objectives aren't done yet. Come back when they are.");
            CampaignObjectiveDef pending = CampaignBaseObjectives.FirstPending(def);
            if (pending != null)
                return L10n.T("还差一件：", "One thing left: ") + L10n.T(pending.DescCN, pending.DescEN);
            if (!CanWrite())
                return L10n.T("进度暂时无法保存，稍后再来找我。", "Progress can't be saved right now. Come back in a moment.");
            return null;
        }

        private static bool Accept(string chapterId, out string message)
        {
            if (CampaignProgressService.TryAcceptContract(chapterId)) { message = null; return true; }
            message = L10n.T("现在接不了这份契约：同时只能进行一份，或者进度暂时无法保存。",
                "Can't take this contract now: only one at a time, or progress can't be saved yet.");
            return false;
        }

        private bool Deliver(CampaignChapterDef def, out string message)
        {
            if (!CampaignProgressService.TryDeliver(def.ChapterId))
            {
                message = L10n.T("契约还没结算完，稍后再来找我。", "The contract isn't settled yet. Come back in a moment.");
                return false;
            }
            _pendingDialogue = def;
            message = null;
            return true;
        }

        #endregion
    }
}
