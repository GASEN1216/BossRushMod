"""Native map votes require one real host payment and its original asset receipt."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def body(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index("{", source.index(signature))
    depth = 0
    for end in range(start, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return re.sub(r"\s+", " ", source[start + 1:end]).strip()
    raise AssertionError("unclosed " + signature)


def need(source, signature, *tokens):
    result = body(source, signature)
    for token in tokens:
        assert token in result, signature + ": " + token
    return result


def main():
    receipt = read("MapSelection/BossRushCoopMapPaymentReceipt.cs")
    payment = read("Common/Coop/BossRushCoopMapPaymentCompatibility.cs")
    helper = read("MapSelection/BossRushMapSelectionHelper.cs")
    lease = read("MapSelection/BossRushMapEntryAssetLease.cs")
    transport = read("Common/Coop/BossRushCoopTransport.cs")
    need(receipt, "private bool OriginalAssetCurrent()", "Slot == Saves.SavesSystem.CurrentSlot", "string.Equals(Asset, BossRushCoop.LocalAssetId, StringComparison.Ordinal)")
    need(receipt, "private bool FrozenCostIntact()", "Cost.money == 0L", "Cost.items.Length == 1", "Cost.items[0].id == ticketId", "Cost.items[0].amount == 1L")
    need(receipt, "internal bool IsCurrent()", "Entry == null", "string.Equals(Entry.SceneID, SceneId, StringComparison.Ordinal)", "Entry.Cost.money != Cost.money", "!ReferenceEquals(Entry.Cost.items, Cost.items)", "!Entry.ConditionsSatisfied", "pendingEntrySlot != Slot", "pendingEntryKind != Kind", "pendingMapEntryIndex != index", "maps[index].sceneID, SceneId", "maps[index].sceneName, sceneName", "pendingModeHSceneGeneration == modeHGeneration", "ReferenceEquals(pendingModeHCostItems, Cost.items)")
    capture = need(receipt, "internal static CoopMapPaymentReceipt CaptureCoopMapPaymentReceipt(", "!BossRushCoop.IsHost", "!IsPendingEntryAssetCurrent()", "pendingPrepaidTicketForCurrentEntry", "marker.entryIndex != index", "entry.SceneID, maps[index].sceneID", "cost.money != 0L", "cost.items.Length != 1", "cost.items[0].id != GetBossRushTicketTypeId()", "cost.items[0].amount != 1L", "return receipt.IsCurrent() ? receipt : null")
    assert capture.index("cost.items.Length != 1") < capture.index("new CoopMapPaymentReceipt("), "freeze only the exact single-ticket entry"
    note = need(receipt, "internal bool NotePaid()", "if (!IsCurrent()) return false", "if (published) return pendingPrepaidTicketForCurrentEntry", "!BossRushCoopMapPaymentCompatibility.PublishingReceipt", "NotePaidCost(Cost)", "published = pendingPrepaidTicketForCurrentEntry")
    assert note.index("!BossRushCoopMapPaymentCompatibility.PublishingReceipt") < note.index("NotePaidCost(Cost)"), "old mode owner receives only a proven payment"
    refund = need(receipt, "internal bool Refund()", "if (refundTransferred) return true", "published || refundAttempted || !OriginalAssetCurrent() || !FrozenCostIntact()", "pendingPrepaidTicketForCurrentEntry", "pendingEntryAsset = Asset", "pendingEntrySlot = Slot", "refundAttempted = true", "ModeHEntry.AbortAndRefund(", "ModeHRefundOutcome.Refunded || outcome == ModeHRefundOutcome.Owed", "owed.Count++", "refundTransferred = true", "ClearPendingEntryFlowState()")
    assert refund.index("if (published || refundAttempted") < refund.index("pendingEntryAsset = Asset"), "a published receipt must never be reconstructed for another refund"
    assert refund.index("refundAttempted = true") < refund.index("ModeHEntry.AbortAndRefund("), "notification exceptions cannot replay the H refund"
    assert "Cost.Pay(" not in receipt and "InstantiateSync(" not in refund, "receipt publication cannot debit or deliver items itself"
    broadcast = need(payment, "private static bool BeforeBroadcast(", "Phase.Attempted", "value.Receipt.Cost.Pay(true, true)", "Phase.Unknown", "!value.ReceiptSeen", "value.CancelRequested", "!owner.Current(value, true)", "PublishingReceipt = true", "value.Receipt.NotePaid()", "Phase.Committed", "finally { PublishingReceipt = false; }")
    assert 'if (!value.VoteBound) { owner.Reject(value, "unbound_selection"); return false; }' in broadcast, "a captured entry without a real native vote cannot broadcast for free"
    assert broadcast.index("value.State = Phase.Attempted") < broadcast.index(".Pay(true, true)"), "synchronous payment callbacks must see Attempted"
    assert broadcast.index("!paid && !value.ReceiptSeen") < broadcast.index("value.Receipt.NotePaid()") < broadcast.index("value.State = Phase.Committed"), "native BeginLoad is allowed only after actual payment and publication"
    preflight = "owner.Current(value, true) && value.Receipt.Cost.Enough && owner.Current(value, true)"
    assert preflight in broadcast and broadcast.index("value.State = Phase.Checking") < broadcast.index(preflight), "funds checks and their callbacks cannot reenter or replace the lease"
    need(payment, "private void OnCostPaid(", "value.State == Phase.Attempted", "ReferenceEquals(cost.items, value.Receipt.Cost.items)", "cost.items[0].amount == 1L", "value.ReceiptSeen = true")
    vote = need(payment, "private static bool BeforeVote(", "value.State == Phase.Committed && !SourceRetired(value)", "Retain(value)", "owner.selection = null")
    assert vote.index("Retain(value)") < vote.index("owner.selection = null"), "another native vote cannot erase ambiguous published consumption"
    dispose = need(payment, "public void Dispose()", "interrupted = selection", "selection.State == Phase.Committed && !SourceRetired(selection)", "Retain(selection)", "EconomyManager.OnCostPaid -= OnCostPaid", "BossRushCoop.SessionChanged -= OnInvalidated", "SceneManager.sceneUnloaded -= OnUnloaded")
    assert dispose.index("Retain(selection)") < dispose.index("active = null"), "transport rebind retains the paid source lease"
    need(payment, "internal static void TickRefunds()", "value.State == Phase.Committed && SourceRetired(value)", "value.State != Phase.Cancelled", "value.Refunding", "value.Receipt.Refund()")
    need(payment, "internal static void RejectSelected(", "!owner.live() || !owner.host()", "owner.ownClick = marker != null && marker.entryIndex >= 0", "cost.money == 0L", "cost.items.Length == 1", "cost.items[0].id == BossRushMapSelectionHelper.GetBossRushTicketTypeId()", "cost.items[0].amount == 1L", "owner.refusedScene = owner.ownClick ? entry.SceneID : null")
    need(payment, "private static bool BeforeHostLoad(", "if (!value.VoteBound) return false", "value.State != Phase.Committed", "value.HostLoadClaimed", "!owner.Current(value, false)", "value.HostLoadClaimed = true")
    need(helper, "public static void MarkTargetSceneLoadStarted()", "pendingEntryKind == BossRushPendingEntryKind.ModeH || BossRushCoop.IsConnected")
    need(helper, "internal static void NotePaidCost(", "BossRushCoop.IsConnected && !BossRushCoopMapPaymentCompatibility.PublishingReceipt")
    selection = need(helper, "internal static void RecordSelection(", "BossRushCoopMapPaymentCompatibility.RejectSelected(mapSelectionEntry)", "if (!BossRushCoopMapPaymentCompatibility.CanRecordSelection()) return", "BossRushCoopMapPaymentCompatibility.CaptureSelected(mapSelectionEntry, marker.entryIndex)")
    assert selection.index("RejectSelected(mapSelectionEntry)") < selection.index("CanRecordSelection()"), "refused selections still bind the real clicked destination"
    allow = need(payment, "internal static bool AllowEntryClick(", "entry.GetComponent<BossRushMapEntryClickHandler>() == null) return true", "owner != null && owner.live() && !owner.host()", "BossRushCoop.IsConnected && !BossRushCoop.IsHost", 'L10n.T("请由房主选择 BossRush 地图，再参与准备。", "Ask the host to select the BossRush map, then ready up.")', "return false")
    assert allow.index("GetComponent<BossRushMapEntryClickHandler>()") < allow.index("bool guest"), "ordinary native entries retain their original click path"
    for signature, parameter in (("internal static bool BeforeNotify(", "mapSelectionEntry"), ("internal static bool BeforePointer(", "__instance")):
        entry = need(helper, signature, "if (!BossRushCoopMapPaymentCompatibility.AllowEntryClick(" + parameter + ")) return false", "RecordSelection(" + parameter + ")", "return true")
        assert entry.index("AllowEntryClick(") < entry.index("RecordSelection("), "guest clicks are rejected before recording any owned selection"
    owned = need(helper, "internal static bool HasCoopOwnedMapTarget(", "bossRushEntryObjects", "obj.activeInHierarchy", "obj.GetComponent<BossRushMapEntryClickHandler>()", "obj.GetComponent<MapSelectionEntry>()", "entry.SceneID == sceneId")
    assert "GetAllMapConfigs" not in owned, "remote ownership uses actual visible entry objects, never all configured destinations"
    need(payment, "private static bool BeforeRemoteVote(", "owner.requestTarget.GetValue(__1)", 'owner.requestSource.GetValue(__1) != "OnPointerClick"', "!BossRushMapSelectionHelper.HasCoopOwnedMapTarget(id)) return true", "value != null && value.VoteBound && value.State == Phase.Captured", "value.Receipt.SceneId == id && owner.Current(value, true)")
    need(payment, "internal void Install()", 'rpc.GetMethod("HandleSceneVoteRequest", Flags)', 'requestType.GetField("TargetSceneId", Flags)', 'requestType.GetField("LocationName", Flags)', 'harmony.Patch(requested, prefix: new HarmonyMethod(typeof(BossRushCoopMapPaymentCompatibility), "BeforeRemoteVote") { priority = Priority.First })')
    pointer_patch = '[HarmonyPatch(typeof(MapSelectionEntry), "OnPointerClick", new Type[] { typeof(PointerEventData) })]'
    assert helper.count(pointer_patch) == 1, "installed native entry-click interception needs its own production capture"
    pointer_header = helper[helper.index(pointer_patch):helper.index("internal static bool BeforePointer(")]
    assert "[HarmonyPrefix, HarmonyPriority(Priority.First)]" in pointer_header, "capture must precede the installed native click prefix"
    notify_patch = '[HarmonyPatch(typeof(MapSelectionView), "NotifyEntryClicked", new Type[] { typeof(MapSelectionEntry), typeof(PointerEventData) })]'
    notify_header = helper[helper.index(notify_patch):helper.index("internal static bool BeforeNotify(")]
    assert "[HarmonyPrefix, HarmonyPriority(Priority.First)]" in notify_header, "notify capture must precede the installed native authority prefix"
    need(helper, "internal static void CancelUnstartedMapSelection()", "BossRushCoopMapPaymentCompatibility.OnMapClosed()")
    assert "mapPaymentCompatibility.Install();" in transport and "mapPaymentCompatibility.Dispose();" in transport, "transport owns native payment patch lifetime"
    need(lease, "public override void OnUpdate(", "BossRushCoopMapPaymentCompatibility.TickRefunds()")
    print("CoopMapPaymentGuard: PASS")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("CoopMapPaymentGuard: FAIL - " + str(error))
        raise SystemExit(1)
