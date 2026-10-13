using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>普通入场协程不能在 Mode H 已冻结意图或取得 owner 后搬动玩家。</summary>
        private bool ShouldSkipLegacySceneSetupForModeH()
        {
            return BossRushMapSelectionHelper.HasPendingModeHEntryIntent()
                || IsModeHRunInProgressSafe();
        }

        /// <summary>
        /// 是否存在任一旧模式 owner 或 pending/startup 冲突（设计提案 §17.1）。
        /// 与 Mode G 入口一样逐模式拒绝，不走会死锁的聚合判定。
        /// </summary>
        internal bool HasLegacyModeConflictForModeH(out string conflictId)
        {
            conflictId = null;
            try
            {
                if (IsActive) { conflictId = "legacy_bossrush_active"; return true; }
                if (IsBossRushArenaActive) { conflictId = "legacy_arena_active"; return true; }
                if (IsModeDActive) { conflictId = "mode_d_active"; return true; }
                if (IsModeEActive) { conflictId = "mode_e_active"; return true; }
                if (IsModeFActive) { conflictId = "mode_f_active"; return true; }
                if (IsZombieModeActive) { conflictId = "zombie_active"; return true; }
                if (IsZombieModeStartupInProgress()) { conflictId = "zombie_startup"; return true; }
                if (ModeGRuntimeGates.IsModeGEntryBlocked) { conflictId = "mode_g_blocked"; return true; }

                BossRushPendingEntryKind pendingKind = BossRushMapSelectionHelper.GetPendingEntryKind();
                if (pendingKind == BossRushPendingEntryKind.ModeG)
                {
                    conflictId = "pending_mode_g_intent";
                    return true;
                }
            }
            catch (Exception e)
            {
                conflictId = "conflict_probe_exception:" + e.GetType().Name;
                return true;
            }
            return false;
        }

        /// <summary>Mode H 入口专用：打开标准 BossRush 地图选择 UI（沿用既有船票预扣语义）。</summary>
        internal void OpenModeHMapSelection()
        {
            BossRushMapSelectionHelper.ShowBossRushMapSelection(BossRushPendingEntryKind.ModeH);
        }

        /// <summary>Mode H 入口专用：把玩家安全送回主基地（隔离失败或认证拒绝时使用）。</summary>
        internal void SafeExitFromModeH()
        {
            try
            {
                if (SceneLoader.Instance != null)
                {
                    Cysharp.Threading.Tasks.UniTaskExtensions.Forget(SceneLoader.Instance.LoadBaseScene(null, true));
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeH] [WARNING] 安全离场失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Mode H 入口（设计提案 §17.1、§25.1）。
    ///
    /// 冻结规则：
    /// - 入口是现有 BossRush 传送链的一个显式分支，不新建第二套传送器、场景宿主或 TypeID；
    /// - 只由 ModeHInteractable / Mode H 入口页显式写入 typed intent，不按背包物品自动猜测；
    /// - 复用既有一张 BossRush 船票的地图 UI 预扣、取消与退款语义；
    /// - 与 Mode E/F 的启动凭证或任一旧模式 pending/startup owner 冲突时直接拒绝并退款，
    ///   禁止静默改走 Mode E/F/G/Legacy；
    /// - <see cref="TryRefundPrepaidTicket"/> 是 Mode H 中**唯一**允许触碰玩家 Inventory 的路径，
    ///   只允许一个船票 typeId、每次入场至多一张，且禁止触碰 PlayerStorage 与玩家 ItemTreeData。
    /// </summary>
    internal static class ModeHEntry
    {
        #region 入口

        /// <summary>
        /// 尝试进入 Mode H：可用性 -> 旧模式冲突 -> 地图支持 -> 冻结 typed intent -> 打开地图选择 UI。
        /// 任何一步失败都返回 false 并给出 reasonId，不改变旧模式状态。
        /// </summary>
        internal static bool TryEnter(ModBehaviour owner, string sceneName, string sceneId, out string reasonId)
        {
            reasonId = null;
            try
            {
                if (owner == null)
                {
                    reasonId = ModeHAvailability.ReasonOwnerMissing;
                    return false;
                }

                ModeHAvailabilityStatus availability = ModeHAvailability.EvaluateNewSeason(owner, out reasonId);
                if (availability != ModeHAvailabilityStatus.Available)
                {
                    return false;
                }

                string conflictId;
                if (owner.HasLegacyModeConflictForModeH(out conflictId))
                {
                    reasonId = ModeHAvailability.ReasonOtherModeActive;
                    ModBehaviour.DevLog("[ModeH] 入口被旧模式冲突拒绝: " + conflictId);
                    return false;
                }

                ModeHSupportedMap map;
                if (!ResolveTargetMap(sceneName, out map))
                {
                    reasonId = ModeHAvailability.ReasonMapUnsupported;
                    return false;
                }
                if (!string.IsNullOrEmpty(sceneId)
                    && !string.Equals(map.SceneId, sceneId, StringComparison.Ordinal))
                {
                    reasonId = ModeHAvailability.ReasonMapUnsupported;
                    return false;
                }

                if (!ModeHPresentationAssetCache.TryPreflight())
                {
                    reasonId = ModeHAvailability.ReasonPresentationMissing;
                    return false;
                }

                if (!ModeHTicketRefund.TryPrepareEntry(owner, out reasonId)) return false;
                // 切图前原子冻结：kind + 目标 sceneName/sceneID + 单调递增 generation
                int generation = BossRushMapSelectionHelper.FreezeModeHEntryIntent(map.SceneName, map.SceneId);
                ModBehaviour.DevLog("[ModeH] 已冻结入场意图: scene=" + map.SceneName
                    + " sceneId=" + map.SceneId + " generation=" + generation);

                owner.OpenModeHMapSelection();
                return true;
            }
            catch (Exception e)
            {
                reasonId = "entry_exception:" + e.GetType().Name;
                ModBehaviour.DevLog("[ModeH] [WARNING] 入口异常: " + e.Message);
                CancelPendingEntry();
                return false;
            }
        }

        /// <summary>解析目标地图：优先使用指定场景，否则取第一张受支持地图。</summary>
        internal static bool ResolveTargetMap(string sceneName, out ModeHSupportedMap map)
        {
            map = null;
            if (!string.IsNullOrEmpty(sceneName))
            {
                return ModeHMapSupportRegistry.TryGetMap(sceneName, out map);
            }
            return ModeHMapSupportRegistry.TryGetPrimaryMap(out map);
        }

        /// <summary>
        /// 入口取消 / 认证拒绝 / 隔离失败的统一路径：
        /// 先清除预扣所有权，再退还船票，最后安全离场。
        /// </summary>
        internal static void AbortAndRefund(ModBehaviour owner, string reasonId, bool exitScene)
        {
            AbortAndRefund(owner, reasonId, exitScene, null);
        }

        /// <summary>同上；<paramref name="beforeExit"/> 在退票有了真实结果、离场之前调用（给玩家如实提示用）。</summary>
        internal static void AbortAndRefund(ModBehaviour owner, string reasonId, bool exitScene, Action<ModeHRefundOutcome> beforeExit)
        {
            ModBehaviour.DevLog("[ModeH] 入场中止并退款: " + (reasonId != null ? reasonId : "unknown"));
            ModeHRefundOutcome outcome = ModeHTicketRefund.Refund();
            if (beforeExit != null)
            {
                try { beforeExit(outcome); } catch (Exception) { /* 提示失败不得影响离场 */ }
            }
            if (exitScene && owner != null)
            {
                owner.SafeExitFromModeH();
            }
        }

        /// <summary>只清除 pending 状态，不发票（用于尚未预扣的取消）。</summary>
        internal static void CancelPendingEntry()
        {
            try
            {
                BossRushMapSelectionHelper.ClearPendingEntryFlowState();
            }
            catch (Exception)
            {
                // 清理失败不阻断后续流程
            }
        }

        #endregion

        #region 唯一退款路径（实现迁至 ModeHTicketRefund，玩家资产访问白名单第一条）

        /// <summary>退还本次入场预扣的一张船票；实现与约束见 <see cref="ModeHTicketRefund"/>。</summary>
        internal static bool TryRefundPrepaidTicket()
        {
            return ModeHTicketRefund.Refund() == ModeHRefundOutcome.Refunded;
        }

        #endregion
    }
}
