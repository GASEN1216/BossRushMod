using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    public static partial class BossRushMapSelectionHelper
    {
        private sealed class PrepaidOwed { internal string Asset; internal int Slot, Count; }
        private static readonly Dictionary<string, PrepaidOwed> prepaidOwed = new Dictionary<string, PrepaidOwed>(StringComparer.Ordinal);
        private static string pendingEntryAsset;
        private static int pendingEntrySlot = -1;
        private static bool issuingPrepaidRefund;
        private static float prepaidRefundAt;
        private static void FreezeEntryAssetLease()
        {
            DeferWrongAssetPrepaidTicket();
            pendingEntryAsset = BossRushCoop.LocalAssetId;
            pendingEntrySlot = Saves.SavesSystem.CurrentSlot;
        }
        private static bool IsPendingEntryAssetCurrent()
        { return pendingEntrySlot == Saves.SavesSystem.CurrentSlot && pendingEntryAsset == BossRushCoop.LocalAssetId; }
        private static void DeferWrongAssetPrepaidTicket()
        {
            // H 的原退款 owner 已持槽身份；其它模式只在错槽清理时把未接管的旧票变成原槽欠账。
            if (!pendingPrepaidTicketForCurrentEntry || pendingEntryKind == BossRushPendingEntryKind.ModeH || IsPendingEntryAssetCurrent()) return;
            string key = pendingEntrySlot + ":" + pendingEntryAsset; PrepaidOwed owed;
            if (!prepaidOwed.TryGetValue(key, out owed))
            { owed = new PrepaidOwed { Asset = pendingEntryAsset, Slot = pendingEntrySlot }; prepaidOwed.Add(key, owed); }
            owed.Count++; pendingPrepaidTicketForCurrentEntry = false;
        }
        internal static void TickPrepaidAssetRefunds()
        {
            if (issuingPrepaidRefund || prepaidOwed.Count == 0 || Time.unscaledTime < prepaidRefundAt) return;
            prepaidRefundAt = Time.unscaledTime + 1f;
            string key = Saves.SavesSystem.CurrentSlot + ":" + BossRushCoop.LocalAssetId; PrepaidOwed owed;
            CharacterMainControl player = CharacterMainControl.Main;
            if (!prepaidOwed.TryGetValue(key, out owed) || player == null || player.CharacterItem == null || player.CharacterItem.Inventory == null) return;
            issuingPrepaidRefund = true; Item ticket = null;
            try
            {
                ticket = ItemAssetsCollection.InstantiateSync(GetBossRushTicketTypeId());
                if (ticket == null || !SkyIslandInventoryTransaction.TryDeliver(ticket, player)) return;
                owed.Count--; ticket = null;
                if (owed.Count <= 0) prepaidOwed.Remove(key);
            }
            finally { SkyIslandInventoryTransaction.DestroyUnowned(ticket); issuingPrepaidRefund = false; }
        }
        internal static void ResetStaticCaches()
        {
            ClearPendingEntryFlowState(); prepaidOwed.Clear();
            pendingEntryAsset = null; pendingEntrySlot = -1;
            issuingPrepaidRefund = false; prepaidRefundAt = 0f;
        }
    }
    internal sealed class BossRushMapEntryAssetRuntimeModule : BossRushRuntimeModuleBase
    {
        public override string ModuleName { get { return "MapEntryAsset"; } }
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime) { BossRushMapSelectionHelper.TickPrepaidAssetRefunds(); }
        public override void OnDestroy() { BossRushMapSelectionHelper.ResetStaticCaches(); }
    }
}
