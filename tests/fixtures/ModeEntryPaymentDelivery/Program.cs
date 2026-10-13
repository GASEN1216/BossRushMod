using System;
using System.Linq;
using BossRush;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using Saves;

internal static class Probe
{
    internal static bool FailCreate, RejectDrop, ThrowAfterDrop, ThrowMessage;
    internal static int Created, Dropped, TicketMaxStack = 1;
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Reset()
    {
        FailCreate = RejectDrop = ThrowAfterDrop = ThrowMessage = false;
        Created = Dropped = 0; TicketMaxStack = 1; SavesSystem.CurrentSlot = 1;
        SceneLoader.IsSceneLoading = false; ItemAssetsCollection.Last = null;
        ModBehaviour.Instance = new ModBehaviour();
        BossRushMapSelectionHelper.ClearPendingEntryFlowState(); ModeHTicketRefund.ResetStaticCaches();
        Item character = new Item { IsCharacter = true };
        character.Inventory = new Inventory { Master = character };
        CharacterMainControl.Main = new CharacterMainControl { CharacterItem = character };
        PlayerStorage.IncomingItemBuffer.Clear();
    }
    private static Inventory Pack { get { return CharacterMainControl.Main.CharacterItem.Inventory; } }
    private static int TicketCount { get { return Pack.Content.Where(x => x != null && x.TypeID == 500001).Sum(x => x.StackCount) + Dropped; } }
    private static MapSelectionEntry SelectedEntry()
    {
        string reason;
        Check(ModeHEntry.TryEnter(ModBehaviour.Instance, "arena", "main", out reason), "normal entry opens: " + reason);
        var entry = new MapSelectionEntry { Marker = new BossRushMapEntryClickHandler { entryIndex = 0 },
            Cost = new Cost { items = new[] { new Cost.ItemEntry { id = 500001, amount = 1 } } } };
        BossRushMapEntrySelectionPatch.RecordSelection(entry);
        return entry;
    }
    private static void PaidEntry()
    { var entry = SelectedEntry(); BossRushMapSelectionHelper.NotePaidCost(entry.Cost); Check(BossRushMapSelectionHelper.HasPendingPrepaidTicket(), "selected entry cost owns receipt"); }
    private static void Starter()
    {
        Reset(); CharacterMainControl.Main.CharacterItem.RejectPlug = true; Pack.Reject = true;
        Check(!new ModeDItemPool().GivePlayerStarterKit(), "CR-004 rejected slot and inventory cannot count as successful starter kit");
        Check(ItemAssetsCollection.Last == null, "failed unowned starter weapon is destroyed");
        Reset(); CharacterMainControl.Main.CharacterItem.ThrowAfterPlug = true;
        Check(new ModeDItemPool().GivePlayerStarterKit(), "equipped weapon survives throwing slot notification");
        Check(ItemAssetsCollection.Last.GetCharacterItem() == CharacterMainControl.Main.CharacterItem, "success means weapon on target player");
        Reset(); CharacterMainControl.Main.CharacterItem.RejectPlug = true;
        Pack.AfterAdd = item => { throw new Exception("inventory notification"); };
        Check(new ModeDItemPool().GivePlayerStarterKit() && Pack.Content.Count(x => x != null) == 1, "inventory commit followed by exception is one successful delivery");
    }
    private static void Debt()
    {
        Reset(); PaidEntry(); FailCreate = true;
        Check(ModeHTicketRefund.Refund() == ModeHRefundOutcome.Owed, "failed first refund remains owed");
        int opens = ModBehaviour.Instance.Opens; string reason;
        Check(!ModeHEntry.TryEnter(ModBehaviour.Instance, "arena", "main", out reason), "CR-003 unresolved refund blocks next paid entry");
        Check(ModBehaviour.Instance.Opens == opens && ModeHTicketRefund.HasOwedRefund, "blocked entry preserves exactly one obligation and cannot open payment UI");
        FailCreate = false; int startCreates = Created;
        bool reentered = true;
        Pack.AfterAdd = item => { reentered = ModeHEntry.TryEnter(ModBehaviour.Instance, "arena", "main", out reason); };
        Check(ModeHEntry.TryEnter(ModBehaviour.Instance, "arena", "main", out reason), "settled ticket permits retry");
        Check(!reentered && Created == startCreates + 1 && TicketCount == 1 && !ModeHTicketRefund.HasOwedRefund, "refund notification cannot recursively issue owed ticket");
        Check(ModeHAvailability.GetReasonLocalizationKey("modeh_ticket_refund_pending").EndsWith("Unavailable_TicketRefundOwed"), "blocked entry uses existing owed-ticket notice");
    }
    private static void RefundDelivery()
    {
        Reset(); PaidEntry(); Pack.AfterAdd = item => { throw new Exception("after add"); };
        Check(ModeHTicketRefund.Refund() == ModeHRefundOutcome.Refunded && TicketCount == 1 && Dropped == 0, "default nonstackable ticket: throwing add notification does not also drop");
        Check(ModeHTicketRefund.Refund() == ModeHRefundOutcome.NotPaid && TicketCount == 1, "duplicate failure callback does not issue again");
        Reset(); PaidEntry(); Pack.Reject = true; ThrowAfterDrop = true;
        Check(ModeHTicketRefund.Refund() == ModeHRefundOutcome.Refunded && Dropped == 1, "throwing pickup notification recognizes actual ground agent");
        Reset(); PaidEntry(); Pack.Reject = true; RejectDrop = true;
        Check(ModeHTicketRefund.Refund() == ModeHRefundOutcome.Owed && ItemAssetsCollection.Last == null, "both destinations reject: retain owed state and destroy only unowned temporary");
        Pack.Reject = RejectDrop = false;
        Check(ModeHEntry.TryEnter(ModBehaviour.Instance, "arena", "main", out _), "later retry settles failed ground delivery");
        Check(TicketCount == 1, "failed attempt plus retry refunds exactly one ticket");
        Reset(); PaidEntry(); FailCreate = true;
        Check(ModeHTicketRefund.Refund() == ModeHRefundOutcome.Owed, "create failure leaves an owed ticket for notice test");
        FailCreate = false; ThrowMessage = true;
        // Settlement clears the bill after actual delivery, before notifying the UI.
        Check(!ModeHEntry.TryEnter(ModBehaviour.Instance, "arena", "main", out _) && TicketCount == 1 && !ModeHTicketRefund.HasOwedRefund,
            "throwing settlement notice cannot resurrect the paid refund obligation");
        ThrowMessage = false; Check(ModeHEntry.TryEnter(ModBehaviour.Instance, "arena", "main", out _), "notice failure permits a later retry without reissuing");
        Check(TicketCount == 1 && PlayerStorage.IncomingItemBuffer.Count == 0, "no warehouse delivery or repeated ticket");
    }
    private static void StackRefund()
    {
        Reset(); TicketMaxStack = 99; var target = new Item { TypeID = 500001, MaxStackCount = 99 };
        Pack.AddItem(target); target.OnCount = item => { throw new Exception("target write notification"); }; PaidEntry();
        Check(ModeHTicketRefund.Refund() == ModeHRefundOutcome.Refunded && TicketCount == 2 && Dropped == 0,
            "CR-011 compatibility: stackable ticket target notification cannot leave source as a second refund");
    }
    private static void Receipt()
    {
        Reset(); var entry = SelectedEntry();
        BossRushMapSelectionHelper.NotePaidCost(new Cost { items = new[] { new Cost.ItemEntry { id = 500001, amount = 1 } } });
        Check(!BossRushMapSelectionHelper.HasPendingPrepaidTicket(), "unrelated same-value cost does not own selected entry");
        BossRushMapSelectionHelper.MarkTargetSceneLoadStarted();
        Check(!BossRushMapSelectionHelper.HasPendingPrepaidTicket(), "target scene without cost receipt cannot infer H payment");
        BossRushMapSelectionHelper.NotePaidCost(entry.Cost);
        Check(BossRushMapSelectionHelper.HasPendingPrepaidTicket(), "actual selected cost binds payment before target callback");
        SceneLoader.IsSceneLoading = false;
        Check(ModeHEntry.TryEnter(ModBehaviour.Instance, "arena", "main", out _) && TicketCount == 1, "loader rejection with no target callback settles actual paid ticket at next entry");
        Reset(); entry = SelectedEntry(); BossRushMapSelectionHelper.NotePaidCost(entry.Cost); SavesSystem.CurrentSlot = 2;
        Check(ModeHTicketRefund.Refund() == ModeHRefundOutcome.NotPaid && TicketCount == 0, "old-slot receipt cannot issue a ticket into new slot");
        Reset(); SelectedEntry(); BossRushMapSelectionHelper.Fallback();
        Check(!ModBehaviour.Instance.Planned && BossRushMapSelectionHelper.SelectedIndex == -1 && ModBehaviour.Instance.DirectStarts == 0, "H fallback cancels map/planned and cannot start normal entry");
        Reset(); SelectedEntry(); BossRushMapSelectionHelper.CancelUnstartedMapSelection();
        Check(!ModBehaviour.Instance.Planned && !BossRushMapSelectionHelper.HasPendingModeHEntryIntent(), "unpaid close clears selected intent and plan");
        Reset(); PaidEntry(); BossRushMapSelectionHelper.CancelUnstartedMapSelection();
        Check(BossRushMapSelectionHelper.HasPendingPrepaidTicket(), "UI close after payment does not throw away receipt");
        Reset(); ModBehaviour.Instance.ModeHRuntime = new ModeHRuntimeModule { IsSceneEntryPending = true };
        Check(!ModeHEntry.TryEnter(ModBehaviour.Instance, "arena", "main", out _) && ModBehaviour.Instance.Opens == 0, "pending readiness request cannot be mistaken for stale paid entry");
        Reset(); BossRushMapSelectionHelper.MarkEntryFlowFromMapSelectionUi(BossRushPendingEntryKind.None);
        BossRushMapSelectionHelper.MarkTargetSceneLoadStarted();
        Check(BossRushMapSelectionHelper.HasPendingPrepaidTicket(), "legacy target callback retains existing receipt behavior");
    }
    private static void Main()
    {
        string only = Environment.GetEnvironmentVariable("BOSSRUSH_ENTRY_CASE");
        if (string.IsNullOrEmpty(only) || only == "starter") Starter();
        if (string.IsNullOrEmpty(only) || only == "debt") Debt();
        if (string.IsNullOrEmpty(only) || only == "refund") RefundDelivery();
        if (string.IsNullOrEmpty(only) || only == "stack") StackRefund();
        if (string.IsNullOrEmpty(only) || only == "receipt") Receipt();
        Console.WriteLine("PASS: " + checks + " assertions; real D starter, H entry/receipt/refund, shared inventory transaction and official Combine; L2 only");
    }
}
