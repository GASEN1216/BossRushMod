using System;
using ItemStatsSystem;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>一次退票尝试的真实结果；玩家提示按它说话，不再一律写「已退票」。</summary>
    internal enum ModeHRefundOutcome
    {
        /// <summary>本次入场没有预扣船票（没付过钱）。</summary>
        NotPaid,
        /// <summary>船票已回到背包或脚下。</summary>
        Refunded,
        /// <summary>付过票但这次没发出去：欠账留在本次运行里，下次进鸭王杯入口时补发。</summary>
        Owed
    }

    /// <summary>
    /// Mode H 唯一的退票实现（玩家资产访问白名单第一条，从 ModeHEntry 原样迁出）。
    /// - 只允许 GetBossRushTicketTypeId() 这一个 typeId，每次入场至多一张；
    /// - 不得触碰 PlayerStorage、玩家 ItemTreeData、仓库缓冲或任何其他 typeId；
    /// - 必须先清除预扣所有权，再实例化船票：重复触发的离场回调不会发出第二张票；
    /// - 未交付时只清理无归属的临时 Item；欠票未结清就不开放下一次付票入口。
    /// 欠账只在本次游戏运行内有效（不写存档），换存档槽不补发。
    /// </summary>
    internal static class ModeHTicketRefund
    {
        private static int _owedSlot = -1;
        private static bool _issuingRefund;

        internal static bool HasOwedRefund
        {
            get { return _owedSlot >= 0 && _owedSlot == SavesSystem.CurrentSlot; }
        }

        internal static ModeHRefundOutcome Refund()
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
            ModeHEntry.CancelPendingEntry();

            if (!hadPrepaid)
            {
                return ModeHRefundOutcome.NotPaid;
            }

            try { _owedSlot = SavesSystem.CurrentSlot; } catch (Exception) { _owedSlot = -1; }
            if (TryIssueOwedTicket()) return ModeHRefundOutcome.Refunded;
            ModBehaviour.CriticalLog("[ModeH] [WARNING] 船票退还失败，已记为欠账，下次进鸭王杯入口时补发");
            return _owedSlot >= 0 ? ModeHRefundOutcome.Owed : ModeHRefundOutcome.NotPaid;
        }

        /// <summary>
        /// 鸭王杯入口处结清上一次没收尾的票：
        /// ① 付了票、但目标图根本没加载起来（官方加载器拒绝时没有任何场景回调），意图还挂着；
        /// ② 上次退票失败留下的欠账。两者都只补一张。
        /// </summary>
        internal static bool TryPrepareEntry(ModBehaviour owner, out string reasonId)
        {
            reasonId = null;
            if (SceneLoader.IsSceneLoading || (owner.ModeHRuntime != null && owner.ModeHRuntime.IsSceneEntryPending))
            {
                reasonId = ModeHAvailability.ReasonRunOwnerActive;
                return false;
            }
            if (SettleBeforeEntry(owner)) return true;
            reasonId = ModeHAvailability.ReasonTicketRefundPending;
            return false;
        }

        internal static bool SettleBeforeEntry(ModBehaviour owner)
        {
            try
            {
                if (SceneLoader.IsSceneLoading || _issuingRefund) return false;
                ModeHRefundOutcome stale = ModeHRefundOutcome.NotPaid;
                if (BossRushMapSelectionHelper.HasPendingModeHEntryIntent()
                    && BossRushMapSelectionHelper.HasPendingPrepaidTicket())
                {
                    stale = Refund();
                }
                bool owedPaid = false;
                if (stale != ModeHRefundOutcome.Owed && HasOwedRefund && TryIssueOwedTicket())
                {
                    owedPaid = true;
                }
                if (owner != null && (stale == ModeHRefundOutcome.Refunded || owedPaid))
                {
                    owner.ShowMessage(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Unavailable_TicketOwedReturned"));
                }
                return !HasOwedRefund;
            }
            catch (Exception e)
            {
                ModBehaviour.CriticalLog("[ModeH] [WARNING] 入口结清旧船票失败: " + e.Message);
                return false;
            }
        }

        private static bool TryIssueOwedTicket()
        {
            if (_issuingRefund || !HasOwedRefund) return false;
            _issuingRefund = true;
            try
            {
                if (!IssueTicket()) return false;
                _owedSlot = -1;
                return true;
            }
            finally { _issuingRefund = false; }
        }

        internal static void ResetStaticCaches()
        {
            _owedSlot = -1;
            _issuingRefund = false;
        }

        private static bool IssueTicket()
        {
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

            SkyIslandInventoryTransaction.DestroyUnowned(item);
            return false;
        }

        /// <summary>
        /// 背包优先、满包落地；共享交付辅助按实际合堆差额修正来源，通知失败不会重复发票。
        /// </summary>
        private static bool TryCommitTicket(Item item, Inventory inventory, CharacterMainControl player)
        {
            if (item == null) return false;
            return SkyIslandInventoryTransaction.TryDeliver(item, inventory,
                remaining => TryCommitTicketRemainder(remaining, inventory, player));
        }

        private static bool TryCommitTicketRemainder(Item item, Inventory inventory, CharacterMainControl player)
        {

            if (inventory != null)
            {
                try
                {
                    inventory.AddItem(item);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeH] [WARNING] 船票写入背包异常，尝试落地: " + e.Message);
                }
                if (item.InInventory == inventory) return true;
            }

            if (player == null) return false;

            try
            {
                DuckovItemAgent pickup = item.Drop(
                    player.transform.position + Vector3.up * 0.3f,
                    true, Vector3.forward, 0f);
                if (pickup != null && item.ActiveAgent == pickup)
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
    }
}
