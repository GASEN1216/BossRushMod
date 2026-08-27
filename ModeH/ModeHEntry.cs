using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
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
            ModBehaviour.DevLog("[ModeH] 入场中止并退款: " + (reasonId != null ? reasonId : "unknown"));
            TryRefundPrepaidTicket();
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

        #region 唯一退款路径（玩家资产访问白名单第一条）

        /// <summary>
        /// 退还本次入场预扣的一张 BossRush 船票。
        ///
        /// 这是 Mode H 中唯一允许触碰 CharacterMainControl.Main.CharacterItem.Inventory 的代码路径：
        /// - 只允许 GetBossRushTicketTypeId() 这一个 typeId，每次入场至多一张；
        /// - 不得触碰 PlayerStorage、玩家 ItemTreeData、仓库缓冲或任何其他 typeId；
        /// - 必须先 ClearPendingEntryFlowState() 清除预扣所有权，防止重复回调发出两张票；
        /// - 背包满时允许落地 fallback；发票失败时 DestroyTree() 清理临时 Item 并只记日志，
        ///   不阻断离场（设计提案 §17.1）。
        /// </summary>
        internal static bool TryRefundPrepaidTicket()
        {
            bool hadPrepaid;
            try
            {
                hadPrepaid = BossRushMapSelectionHelper.HasPendingPrepaidTicket();
            }
            catch (Exception)
            {
                hadPrepaid = false;
            }

            // 先清预扣所有权，再发票：重复触发的离场回调不会发出第二张票
            CancelPendingEntry();

            if (!hadPrepaid)
            {
                return false;
            }

            int typeId;
            try
            {
                typeId = BossRushMapSelectionHelper.GetBossRushTicketTypeId();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 船票 typeId 解析失败: " + e.Message);
                return false;
            }
            if (typeId <= 0)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 船票 typeId 非法，放弃退款");
                return false;
            }

            Item item = null;
            try
            {
                item = ItemAssetsCollection.InstantiateSync(typeId);
                CharacterMainControl player = CharacterMainControl.Main;
                Inventory inventory = player != null && player.CharacterItem != null
                    ? player.CharacterItem.Inventory
                    : null;

                if (item != null && TryCommitTicket(item, inventory, player))
                {
                    ModBehaviour.DevLog("[ModeH] 已退还入场船票");
                    return true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 入场船票退还异常: " + e.Message);
            }

            try
            {
                if (item != null) item.DestroyTree();
            }
            catch (Exception cleanupException)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 退款失败后的物品清理异常: " + cleanupException.Message);
            }
            return false;
        }

        /// <summary>
        /// 背包优先、满包落地。与 Mode G 的提交形状一致，但由 Mode H 自行实现，
        /// 不复用 ModeGRewardTransaction 对象，也不回退到 PlayerStorage。
        /// </summary>
        private static bool TryCommitTicket(Item item, Inventory inventory, CharacterMainControl player)
        {
            if (item == null) return false;

            if (inventory != null)
            {
                try
                {
                    if (inventory.AddAndMerge(item, 0)) return true;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeH] [WARNING] 船票写入背包异常，尝试落地: " + e.Message);
                }
            }

            if (player == null) return false;

            try
            {
                DuckovItemAgent pickup = item.Drop(
                    player.transform.position + Vector3.up * 0.3f,
                    true, Vector3.forward, 0f);
                if (pickup != null)
                {
                    ModBehaviour.DevLog("[ModeH] 背包已满，船票已掉落在玩家脚下");
                    return true;
                }
            }
            catch (Exception dropException)
            {
                try
                {
                    if (item != null && item.ActiveAgent != null)
                    {
                        ModBehaviour.DevLog("[ModeH] 船票掉落回调异常，但拾取代理已生成: " + dropException.Message);
                        return true;
                    }
                }
                catch (Exception)
                {
                    // 归属核对失败按未提交处理
                }
                ModBehaviour.DevLog("[ModeH] [WARNING] 船票地面交付失败: " + dropException.Message);
            }
            return false;
        }

        #endregion
    }
}
