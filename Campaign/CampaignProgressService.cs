// ============================================================================
// CampaignProgressService.cs - 鸭王征程状态机核心
// ============================================================================
// 唯一负责：章节状态推导、契约接取/放弃/交付、奖励发放、token 授予、解锁链推进。
// 存档只经 CampaignPersistence 入队，物理落盘只经 CampaignSaveCoordinator。
//
// 【状态推导而非状态存储】
//   存档里只存每章的 state 整数。「哪一章可接」是**推导**出来的：
//   前一章 Completed 则本章 Available。这样调整章节表（加章、改顺序）不需要迁移存档，
//   也不会出现「存了 Available 但前置其实没过」的自相矛盾状态。
//
// 【同时只能有一个进行中的契约】
//   六章分别指向不同模式，同时接两个会让局内 HUD 与目标采集互相打架。
//   TryAcceptContract 在已有进行中契约时直接拒绝。
//
// 【交付是补偿式事务】
//   发钱 → 用独立副本把 Completed/token/线索一次入队 → 发布运行时 token。
//   入队失败会原路撤回本次奖金；token 只在入队成功后发布且自身幂等，避免
//   「钱发了但状态仍待交付」造成重复领奖，也避免写失败时后山先行解锁。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Economy;

namespace BossRush
{
    /// <summary>战役进度服务。全静态：一次会话内只有一份进度状态。</summary>
    internal static class CampaignProgressService
    {
        #region 状态

        private static bool _initialized;

        /// <summary>本会话内「目标已达成、等交付」的章节缓存；权威状态同时落盘。</summary>
        private static string _readyToDeliverChapterId;

        #endregion

        #region 初始化

        /// <summary>
        /// 幂等初始化：读存档、把已授予 token 发布给跨系统契约。
        /// 必须在后山查询解锁状态之前完成，否则后山会看到 fail-closed 的空答案。
        /// </summary>
        internal static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                CampaignSaveData data = CampaignPersistence.Current;
                CampaignPersistence.PublishTokensToUnlockContract(data);
            }
            catch (Exception e)
            {
                LogFailure("initialize", e);
            }
        }

        /// <summary>换槽 / 删档：丢弃会话态，下次访问重新从新槽推导。</summary>
        internal static void NotifySlotChanged()
        {
            _initialized = false;
            _readyToDeliverChapterId = null;
            try
            {
                CampaignObjectiveTracker.ResetSession();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 换槽复位失败: " + e.Message);
            }
        }

        #endregion

        #region 状态查询

        /// <summary>
        /// 该章当前状态。推导规则：
        /// 存档里的 Completed / ReadyToDeliver / ContractActive 均为权威状态；
        /// 会话缓存只负责加速待交付查询，否则看前置章是否已完成。
        /// </summary>
        internal static CampaignChapterState GetState(string chapterId)
        {
            try
            {
                EnsureInitialized();
                CampaignChapterDef def = CampaignContentCatalog.GetChapter(chapterId);
                if (def == null) return CampaignChapterState.Locked;

                int stored = ReadStoredState(chapterId);
                if (stored == (int)CampaignChapterState.Completed) return CampaignChapterState.Completed;
                if (stored == (int)CampaignChapterState.ReadyToDeliver)
                {
                    _readyToDeliverChapterId = chapterId;
                    return CampaignChapterState.ReadyToDeliver;
                }

                if (string.Equals(_readyToDeliverChapterId, chapterId, StringComparison.Ordinal))
                {
                    return CampaignChapterState.ReadyToDeliver;
                }

                if (stored == (int)CampaignChapterState.ContractActive) return CampaignChapterState.ContractActive;

                return IsUnlockedByPrerequisite(def)
                    ? CampaignChapterState.Available
                    : CampaignChapterState.Locked;
            }
            catch (Exception e)
            {
                LogFailure("get_state", e);
                return CampaignChapterState.Locked;
            }
        }

        /// <summary>当前进行中（或待交付）的章节 ID；没有则返回 null。</summary>
        internal static string GetActiveChapterId()
        {
            try
            {
                EnsureInitialized();
                if (!string.IsNullOrEmpty(_readyToDeliverChapterId)) return _readyToDeliverChapterId;

                CampaignSaveData data = CampaignPersistence.Current;
                if (data == null || data.chapters == null) return null;
                for (int i = 0; i < data.chapters.Length; i++)
                {
                    CampaignChapterRecord record = data.chapters[i];
                    if (record == null) continue;
                    if (record.state == (int)CampaignChapterState.ReadyToDeliver)
                    {
                        _readyToDeliverChapterId = record.chapterId;
                        return record.chapterId;
                    }
                    if (record.state == (int)CampaignChapterState.ContractActive) return record.chapterId;
                }
                return null;
            }
            catch (Exception e)
            {
                LogFailure("get_active", e);
                return null;
            }
        }

        /// <summary>当前进行中契约指向的章节定义；没有则返回 null。</summary>
        internal static CampaignChapterDef GetActiveChapterDef()
        {
            string id = GetActiveChapterId();
            if (string.IsNullOrEmpty(id)) return null;
            return CampaignContentCatalog.GetChapter(id);
        }

        /// <summary>该线索是否已解锁。</summary>
        internal static bool IsClueUnlocked(string clueId)
        {
            try
            {
                if (string.IsNullOrEmpty(clueId)) return false;
                EnsureInitialized();
                CampaignSaveData data = CampaignPersistence.Current;
                if (data == null || data.unlockedClues == null) return false;
                for (int i = 0; i < data.unlockedClues.Length; i++)
                {
                    if (string.Equals(data.unlockedClues[i], clueId, StringComparison.Ordinal)) return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsUnlockedByPrerequisite(CampaignChapterDef def)
        {
            if (def == null) return false;
            if (def.Order <= CampaignTuning.FirstChapter) return true;

            CampaignChapterDef previous = CampaignContentCatalog.GetChapterByOrder(def.Order - 1);
            if (previous == null) return false;
            return ReadStoredState(previous.ChapterId) == (int)CampaignChapterState.Completed;
        }

        private static int ReadStoredState(string chapterId)
        {
            try
            {
                CampaignSaveData data = CampaignPersistence.Current;
                if (data == null || data.chapters == null) return (int)CampaignChapterState.Locked;
                for (int i = 0; i < data.chapters.Length; i++)
                {
                    CampaignChapterRecord record = data.chapters[i];
                    if (record == null) continue;
                    if (string.Equals(record.chapterId, chapterId, StringComparison.Ordinal)) return record.state;
                }
                return (int)CampaignChapterState.Locked;
            }
            catch (Exception)
            {
                return (int)CampaignChapterState.Locked;
            }
        }

        #endregion

        #region 契约操作

        /// <summary>
        /// 接取契约。已有进行中契约、章节未解锁或已完成时拒绝并返回 false。
        /// </summary>
        internal static bool TryAcceptContract(string chapterId)
        {
            try
            {
                EnsureInitialized();
                if (GetState(chapterId) != CampaignChapterState.Available) return false;

                // 同时只能有一个：两个契约并行会让局内 HUD 与目标采集互相打架
                if (!string.IsNullOrEmpty(GetActiveChapterId())) return false;

                if (!WriteState(chapterId, CampaignChapterState.ContractActive)) return false;

                CampaignObjectiveTracker.ResetSession();
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "已接取契约: " + chapterId);
                return true;
            }
            catch (Exception e)
            {
                LogFailure("accept", e);
                return false;
            }
        }

        /// <summary>放弃当前契约。章节回到可接取状态，无惩罚。</summary>
        internal static bool TryAbandonContract()
        {
            try
            {
                EnsureInitialized();
                string active = GetActiveChapterId();
                if (string.IsNullOrEmpty(active)) return false;

                _readyToDeliverChapterId = null;
                if (!WriteState(active, CampaignChapterState.Available)) return false;

                CampaignObjectiveTracker.ResetSession();
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "已放弃契约: " + active);
                return true;
            }
            catch (Exception e)
            {
                LogFailure("abandon", e);
                return false;
            }
        }

        /// <summary>
        /// 局内目标全部达成。把 ReadyToDeliver 落盘，但仍须回公告板主动交付；
        /// 这样重启不丢进度，也不会跳过剧情对话。
        /// </summary>
        internal static void NotifyObjectivesSatisfied(string chapterId)
        {
            try
            {
                if (string.IsNullOrEmpty(chapterId)) return;
                if (!string.Equals(GetActiveChapterId(), chapterId, StringComparison.Ordinal)) return;
                if (string.Equals(_readyToDeliverChapterId, chapterId, StringComparison.Ordinal)) return;

                if (!WriteState(chapterId, CampaignChapterState.ReadyToDeliver)) return;
                _readyToDeliverChapterId = chapterId;
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "契约目标已全部达成: " + chapterId);

                CampaignChapterDef def = CampaignContentCatalog.GetChapter(chapterId);
                string title = def != null ? L10n.T(def.TitleCN, def.TitleEN) : chapterId;
                ModBehaviour.Instance?.ShowMessage(
                    L10n.T("契约目标已完成：", "Contract objectives complete: ") + title
                    + L10n.T("　回公告板交付", "  Return to the board to hand it in"));
            }
            catch (Exception e)
            {
                LogFailure("objectives_satisfied", e);
            }
        }

        /// <summary>
        /// 交付契约：发钱 → 完成状态/token/线索同 payload 入队 → 发布运行时 token。
        /// 只有 ReadyToDeliver 状态可交付。
        /// </summary>
        internal static bool TryDeliver(string chapterId)
        {
            try
            {
                EnsureInitialized();
                if (GetState(chapterId) != CampaignChapterState.ReadyToDeliver) return false;

                CampaignChapterDef def = CampaignContentCatalog.GetChapter(chapterId);
                if (def == null) return false;

                // 先发钱：失败就整体中止，让玩家可以重试交付，而不是白丢奖励
                if (def.RewardCash > 0)
                {
                    bool paid = false;
                    try
                    {
                        paid = EconomyManager.Add(def.RewardCash);
                    }
                    catch (Exception e)
                    {
                        LogFailure("reward_cash", e);
                    }
                    if (!paid)
                    {
                        ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 奖金发放失败，交付中止: " + chapterId);
                        return false;
                    }
                }

                // 先把完成状态、token 与线索写进独立副本并入队。旧实现忽略这里的失败，
                // 会让章节保持 ReadyToDeliver，玩家下一次交付再次领到现金。
                if (!WriteStateAndRewards(chapterId, CampaignChapterState.Completed, def))
                {
                    bool refunded = def.RewardCash <= 0;
                    if (def.RewardCash > 0)
                    {
                        try { refunded = EconomyManager.Pay(new Cost((long)def.RewardCash), true, true); }
                        catch (Exception e) { LogFailure("reward_rollback", e); }
                    }
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix
                        + "[ERROR] 交付状态写入失败，奖金回滚=" + refunded + ": " + chapterId);
                    Duckov.UI.NotificationText.Push(refunded
                        ? L10n.T("交付写入失败，本次奖金已撤回，可稍后重试。",
                            "Delivery could not be saved; the reward was rolled back. Try again later.")
                        : L10n.T("交付写入与奖金回滚均失败；请勿重复交付。",
                            "Delivery save and reward rollback both failed; do not submit again."));
                    return false;
                }

                // 数据已进入同一份战役 payload 后再发布运行时解锁事件；重复发布本身幂等。
                if (!string.IsNullOrEmpty(def.FacilityToken))
                {
                    CampaignFacilityUnlocks.TryGrant(def.FacilityToken);
                }

                _readyToDeliverChapterId = null;

                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "契约已交付: " + chapterId
                    + " 奖金=" + def.RewardCash);
                return true;
            }
            catch (Exception e)
            {
                LogFailure("deliver", e);
                return false;
            }
        }

        #endregion

        #region 存档写入

        private static bool WriteState(string chapterId, CampaignChapterState state)
        {
            return WriteStateAndRewards(chapterId, state, null);
        }

        /// <summary>
        /// 写章节状态（并在交付时补写 token 与线索），入队后请求落盘。
        /// 落盘本身受基地场景闸约束，战斗中只入队、回基地补写。
        /// </summary>
        private static bool WriteStateAndRewards(
            string chapterId, CampaignChapterState state, CampaignChapterDef deliveredDef)
        {
            try
            {
                CampaignSaveData current = CampaignPersistence.Current;
                if (current == null) return false;
                CampaignSaveData data = CloneSaveData(current);

                List<CampaignChapterRecord> records = new List<CampaignChapterRecord>();
                bool found = false;
                if (data.chapters != null)
                {
                    for (int i = 0; i < data.chapters.Length; i++)
                    {
                        CampaignChapterRecord record = data.chapters[i];
                        if (record == null) continue;
                        if (string.Equals(record.chapterId, chapterId, StringComparison.Ordinal))
                        {
                            record.state = (int)state;
                            found = true;
                        }
                        records.Add(record);
                    }
                }
                if (!found)
                {
                    CampaignChapterRecord added = new CampaignChapterRecord();
                    added.chapterId = chapterId;
                    added.state = (int)state;
                    records.Add(added);
                }
                data.chapters = records.ToArray();

                if (deliveredDef != null)
                {
                    if (!string.IsNullOrEmpty(deliveredDef.FacilityToken))
                    {
                        data.grantedTokens = AppendUnique(data.grantedTokens, deliveredDef.FacilityToken);
                    }
                    if (!string.IsNullOrEmpty(deliveredDef.ClueId))
                    {
                        data.unlockedClues = AppendUnique(data.unlockedClues, deliveredDef.ClueId);
                    }
                }

                if (!CampaignPersistence.Store(data)) return false;
                CampaignSaveCoordinator.RequestFlush();
                return true;
            }
            catch (Exception e)
            {
                LogFailure("write_state", e);
                return false;
            }
        }

        private static CampaignSaveData CloneSaveData(CampaignSaveData source)
        {
            CampaignSaveData copy = new CampaignSaveData();
            copy.schemaVersion = source.schemaVersion;
            copy.lastUpdatedTicks = source.lastUpdatedTicks;

            CampaignChapterRecord[] chapters = source.chapters ?? new CampaignChapterRecord[0];
            copy.chapters = new CampaignChapterRecord[chapters.Length];
            for (int i = 0; i < chapters.Length; i++)
            {
                CampaignChapterRecord record = chapters[i];
                if (record == null) continue;
                CampaignChapterRecord cloned = new CampaignChapterRecord();
                cloned.chapterId = record.chapterId;
                cloned.state = record.state;
                copy.chapters[i] = cloned;
            }
            copy.grantedTokens = source.grantedTokens != null
                ? (string[])source.grantedTokens.Clone() : new string[0];
            copy.unlockedClues = source.unlockedClues != null
                ? (string[])source.unlockedClues.Clone() : new string[0];
            return copy;
        }

        private static string[] AppendUnique(string[] source, string value)
        {
            List<string> list = new List<string>();
            if (source != null)
            {
                for (int i = 0; i < source.Length; i++)
                {
                    if (string.IsNullOrEmpty(source[i])) continue;
                    if (string.Equals(source[i], value, StringComparison.Ordinal)) return source;
                    list.Add(source[i]);
                }
            }
            list.Add(value);
            return list.ToArray();
        }

        #endregion

        #region 诊断与清理

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 进度服务 "
                    + stage + " 异常: " + e.Message);
            }
            catch (Exception)
            {
                // 日志函数自身：这里再记日志会无限递归
            }
        }

        internal static void ResetStaticCaches()
        {
            _initialized = false;
            _readyToDeliverChapterId = null;
        }

        #endregion
    }
}
