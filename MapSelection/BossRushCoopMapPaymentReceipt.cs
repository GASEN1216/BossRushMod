using System;
using Duckov.Economy;
using Duckov.UI;

namespace BossRush
{
    public static partial class BossRushMapSelectionHelper
    {
        // The native compatibility owner proves payment. This context only
        // publishes that receipt to the existing mode or transfers an unpublished
        // confirmed payment to its original asset's existing refund owner.
        internal sealed class CoopMapPaymentReceipt
        {
            internal readonly MapSelectionEntry Entry;
            internal readonly Cost Cost;
            internal readonly string SceneId, Asset;
            internal readonly int Slot;
            internal readonly BossRushPendingEntryKind Kind;
            private readonly int index, ticketId, modeHGeneration, modeHSlot;
            private readonly string sceneName, modeHTargetName, modeHTargetId;
            private bool published, refundTransferred, refundAttempted;

            internal CoopMapPaymentReceipt(MapSelectionEntry entry, int selectedIndex, BossRushMapConfig map)
            {
                Entry = entry; Cost = entry.Cost; index = selectedIndex;
                SceneId = map.sceneID; sceneName = map.sceneName;
                Asset = pendingEntryAsset; Slot = pendingEntrySlot; Kind = pendingEntryKind;
                ticketId = Cost.items[0].id;
                modeHTargetName = pendingModeHTargetSceneName;
                modeHTargetId = pendingModeHTargetSceneId;
                modeHGeneration = pendingModeHSceneGeneration; modeHSlot = pendingModeHSlot;
            }

            private bool OriginalAssetCurrent()
            {
                return Slot == Saves.SavesSystem.CurrentSlot
                    && string.Equals(Asset, BossRushCoop.LocalAssetId, StringComparison.Ordinal);
            }

            private bool FrozenCostIntact()
            {
                return Cost.money == 0L && Cost.items != null && Cost.items.Length == 1
                    && Cost.items[0].id == ticketId && Cost.items[0].amount == 1L;
            }

            internal bool IsCurrent()
            {
                if (refundTransferred || refundAttempted || !OriginalAssetCurrent() || !FrozenCostIntact()
                    || Entry == null || !string.Equals(Entry.SceneID, SceneId, StringComparison.Ordinal)
                    || Entry.Cost.money != Cost.money || !ReferenceEquals(Entry.Cost.items, Cost.items)
                    || !Entry.ConditionsSatisfied
                    || pendingEntryFlowSource != BossRushEntryFlowSource.MapSelectionUi
                    || pendingEntrySlot != Slot || !string.Equals(pendingEntryAsset, Asset, StringComparison.Ordinal)
                    || pendingEntryKind != Kind || pendingMapEntryIndex != index) return false;
                BossRushMapConfig[] maps = ModBehaviour.GetAllMapConfigs();
                if (maps == null || index < 0 || index >= maps.Length || maps[index] == null
                    || !string.Equals(maps[index].sceneID, SceneId, StringComparison.Ordinal)
                    || !string.Equals(maps[index].sceneName, sceneName, StringComparison.Ordinal)) return false;
                if (Kind == BossRushPendingEntryKind.ModeH)
                    return pendingModeHSlot == modeHSlot && pendingModeHSceneGeneration == modeHGeneration
                        && string.Equals(pendingModeHTargetSceneName, modeHTargetName, StringComparison.Ordinal)
                        && string.Equals(pendingModeHTargetSceneId, modeHTargetId, StringComparison.Ordinal)
                        && ReferenceEquals(pendingModeHCostItems, Cost.items);
                return true;
            }

            internal bool NotePaid()
            {
                if (!IsCurrent()) return false;
                if (published) return pendingPrepaidTicketForCurrentEntry;
                // The owner calls this only after the exact native Pay has
                // completed and while it owns the receipt publication scope.
                if (!BossRushCoopMapPaymentCompatibility.PublishingReceipt) return false;
                if (Kind == BossRushPendingEntryKind.ModeH) NotePaidCost(Cost);
                else pendingPrepaidTicketForCurrentEntry = true;
                published = pendingPrepaidTicketForCurrentEntry;
                return published;
            }

            internal bool Refund()
            {
                if (refundTransferred) return true;
                // Published receipts already belong to the existing mode/asset
                // path. Reconstructing them here would create a second refund.
                if (published || refundAttempted || !OriginalAssetCurrent() || !FrozenCostIntact()) return false;
                bool sameFlow = IsCurrent();
                if (pendingPrepaidTicketForCurrentEntry
                    || !sameFlow && (pendingEntryFlowSource != BossRushEntryFlowSource.None
                        || pendingMapEntryIndex >= 0 || pendingEntryKind != BossRushPendingEntryKind.None)) return false;
                ModBehaviour owner = ModBehaviour.Instance;
                if (owner != null && owner.IsAnyBossRushLikeModeActive()
                    || ModBehaviour.IsModeHRunInProgressSafe()) return false;

                // Only an unpublished, known paid receipt can reach here. Its
                // former UI may have closed while its original asset was away.
                pendingEntryAsset = Asset; pendingEntrySlot = Slot;
                pendingEntryFlowSource = BossRushEntryFlowSource.MapSelectionUi;
                pendingMapEntryIndex = index; pendingEntryKind = Kind;
                pendingInitialSpawn = false;
                pendingModeHTargetSceneName = modeHTargetName;
                pendingModeHTargetSceneId = modeHTargetId;
                pendingModeHSceneGeneration = modeHGeneration; pendingModeHSlot = modeHSlot;
                pendingModeHCostItems = Cost.items;
                if (Kind == BossRushPendingEntryKind.ModeH)
                {
                    pendingPrepaidTicketForCurrentEntry = true;
                    // Set before the old owner invokes any inventory notification;
                    // an exception with an unknown outcome must not be replayed.
                    refundAttempted = true;
                    ModeHEntry.AbortAndRefund(owner, "coop_native_vote_cancelled_before_publication", false,
                        outcome => refundTransferred = outcome == ModeHRefundOutcome.Refunded || outcome == ModeHRefundOutcome.Owed);
                }
                else
                {
                    string key = Slot + ":" + Asset; PrepaidOwed owed;
                    if (!prepaidOwed.TryGetValue(key, out owed))
                    {
                        owed = new PrepaidOwed { Asset = Asset, Slot = Slot };
                        prepaidOwed.Add(key, owed);
                    }
                    owed.Count++;
                    refundTransferred = true;
                    // The original asset queue now owns the sole obligation.
                    // No delivery runs in this call or in a cancellation callback.
                    pendingPrepaidTicketForCurrentEntry = false;
                    ClearPendingEntryFlowState();
                    ClearPendingMapEntry();
                    SetBossRushArenaPlanned(false);
                }
                return refundTransferred;
            }
        }

        internal static CoopMapPaymentReceipt CaptureCoopMapPaymentReceipt(MapSelectionEntry entry, int index)
        {
            if (!BossRushCoop.IsHost || entry == null || !IsPendingEntryAssetCurrent()
                || string.IsNullOrEmpty(pendingEntryAsset)
                || pendingEntryFlowSource != BossRushEntryFlowSource.MapSelectionUi
                || pendingPrepaidTicketForCurrentEntry || pendingMapEntryIndex != index) return null;
            var marker = entry.GetComponent<BossRushMapEntryClickHandler>();
            BossRushMapConfig[] maps = ModBehaviour.GetAllMapConfigs();
            if (marker == null || marker.entryIndex != index || maps == null || index < 0 || index >= maps.Length
                || maps[index] == null || !string.Equals(entry.SceneID, maps[index].sceneID, StringComparison.Ordinal)) return null;
            Cost cost = entry.Cost;
            if (cost.money != 0L || cost.items == null || cost.items.Length != 1
                || cost.items[0].id != GetBossRushTicketTypeId() || cost.items[0].amount != 1L) return null;
            if (pendingEntryKind == BossRushPendingEntryKind.ModeH
                && (pendingModeHSceneGeneration <= 0 || pendingModeHSlot != Saves.SavesSystem.CurrentSlot
                    || !string.Equals(pendingModeHTargetSceneName, maps[index].sceneName, StringComparison.Ordinal)
                    || !string.Equals(pendingModeHTargetSceneId, maps[index].sceneID, StringComparison.Ordinal)
                    || !ReferenceEquals(pendingModeHCostItems, cost.items))) return null;
            var receipt = new CoopMapPaymentReceipt(entry, index, maps[index]);
            return receipt.IsCurrent() ? receipt : null;
        }
    }
}
