using System;
using System.Reflection;
using BossRush;
using Duckov.Economy;
using EscapeFromDuckovCoopMod;
using UnityEngine;

// The receipt and its existing H/non-H refund owners are production sources.
// Cancellation runs inside the official ConsumeItems notification after the real
// ticket debit. No payment, native readiness, prepaid flag or refund is invented.
internal static class ReceiptCases
{
    private static BossRushMapSelectionHelper.CoopMapPaymentReceipt Receipt()
    {
        object selection = typeof(BossRushCoopMapPaymentCompatibility)
            .GetField("selection", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Probe.Owner);
        return (BossRushMapSelectionHelper.CoopMapPaymentReceipt)selection.GetType()
            .GetField("Receipt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(selection);
    }

    private static void CancelDuringDebit(Action afterCancel)
    {
        Action callback = () =>
        {
            SceneNet.Instance.CancelVote();
            if (afterCancel != null) afterCancel();
        };
        ItemUtilities.OnPlayerItemOperation += callback;
        try { Probe.StartVote(); Probe.Commit(); }
        finally { ItemUtilities.OnPlayerItemOperation -= callback; }
    }

    private static void TickRefund()
    {
        BossRushCoopMapPaymentCompatibility.TickRefunds();
        Time.unscaledTime += 2f;
        BossRushMapSelectionHelper.TickPrepaidAssetRefunds();
    }

    internal static void Run()
    {
        Probe.Reset();
        var receipt = Receipt();
        Probe.Check(receipt != null && receipt.IsCurrent(), "receipt freezes the real selected entry");
        Cost cost = Probe.Entry.Cost;
        Probe.Entry.Cost = new Cost { money = cost.money, items = new[] { cost.items[0] } };
        Probe.Check(!receipt.IsCurrent(), "replacement equal-valued Cost array is a different selection");
        Probe.StartVote();
        Probe.Check(!SceneNet.Instance.sceneVoteActive && EconomyManager.Attempts == 0,
            "native owner's old intent cannot vote with the replacement array");

        Probe.Reset(); receipt = Receipt();
        cost = Probe.Entry.Cost; cost.money = 1; Probe.Entry.Cost = cost;
        Probe.Check(!receipt.IsCurrent(), "money mutation invalidates the frozen zero-money ticket");
        Probe.Reset(); receipt = Receipt();
        Probe.Entry.SceneID = "another-map";
        Probe.Check(!receipt.IsCurrent(), "retargeted UI cannot debit the old destination");
        Probe.Reset(); receipt = Receipt();
        UnityEngine.Object.Destroy(Probe.Entry);
        Probe.Check(!receipt.IsCurrent(), "destroyed Unity entry cannot authorize payment");
        Probe.Reset(); receipt = Receipt();
        Probe.Entry.ConditionsSatisfied = false;
        Probe.Check(!receipt.IsCurrent(), "changed real entry conditions invalidate payment");
        Probe.Check(BossRushMapSelectionHelper.CaptureCoopMapPaymentReceipt(Probe.Entry, 0) == null,
            "invalid entry conditions are refused while capturing");
        Probe.Reset(); receipt = Receipt();
        Probe.Entry.Cost.items[0].amount = 2;
        Probe.Check(!receipt.IsCurrent(), "mutating the captured cost array cannot increase the ticket debit");
        Probe.Reset(); receipt = Receipt();
        string asset = BossRushCoop.Asset; BossRushCoop.Asset = "receipt-other-asset";
        Probe.Check(!receipt.IsCurrent(), "receipt cannot follow a same-slot asset replacement");
        BossRushCoop.Asset = asset; Saves.SavesSystem.CurrentSlot = 2;
        Probe.Check(!receipt.IsCurrent(), "receipt cannot follow a slot replacement");

        Probe.Reset(); receipt = Receipt();
        Probe.StartVote(); Probe.Commit();
        Probe.Check(Probe.TicketCount == 2 && CoopTool.Begins == 1, "real payment precedes the one BeginLoad");
        Probe.Check(!receipt.Refund(), "published normal receipt belongs exclusively to the existing mode owner");
        Probe.Check(Probe.TicketCount == 2, "manual duplicate refund request cannot mint a published ticket");

        Probe.Reset(); receipt = Receipt();
        CancelDuringDebit(null);
        Probe.Check(EconomyManager.Attempts == 1 && CoopTool.Begins == 0, "post-debit cancellation blocks native BeginLoad");
        Probe.Check(Probe.TicketCount == 2, "normal cancellation transfers a debt before attempting delivery");
        TickRefund();
        Probe.Check(Probe.TicketCount == 3, "original asset receives its sole cancelled normal ticket");
        Probe.Check(receipt.Refund(), "transferred refund receipt is idempotent");
        TickRefund();
        Probe.Check(Probe.TicketCount == 3 && EconomyManager.Attempts == 1, "repeated refund ticks neither pay nor mint again");

        Probe.Reset(); receipt = Receipt(); asset = BossRushCoop.Asset;
        CancelDuringDebit(() =>
        {
            BossRushCoop.Asset = "receipt-away";
            BossRushMapSelectionHelper.ClearPendingEntryFlowState();
            BossRushMapSelectionHelper.ClearPendingMapEntry();
        });
        TickRefund();
        Probe.Check(Probe.TicketCount == 2 && !receipt.Refund(), "unpublished debit cannot be refunded to a replacement asset");
        BossRushCoop.Asset = asset;
        TickRefund();
        Probe.Check(Probe.TicketCount == 3 && receipt.Refund(), "returning original asset adopts the one retained refund");
        TickRefund(); Probe.Check(Probe.TicketCount == 3, "returned-asset refund cannot repeat");

        Probe.Reset(); receipt = Receipt();
        CancelDuringDebit(() =>
        {
            Saves.SavesSystem.CurrentSlot = 2;
            BossRushMapSelectionHelper.ClearPendingEntryFlowState();
            BossRushMapSelectionHelper.ClearPendingMapEntry();
        });
        TickRefund();
        Probe.Check(Probe.TicketCount == 2 && !receipt.Refund(), "cancelled debit stays with its original slot");
        Saves.SavesSystem.CurrentSlot = 1;
        TickRefund();
        Probe.Check(Probe.TicketCount == 3, "switching back delivers only the original slot's ticket");

        Probe.Reset(BossRushPendingEntryKind.ModeH); receipt = Receipt();
        CancelDuringDebit(null);
        Probe.Check(EconomyManager.Attempts == 1 && CoopTool.Begins == 0 && Probe.TicketCount == 3,
            "known paid but unpublished H cancellation uses the real H refund owner");
        Probe.Check(receipt.Refund(), "completed H refund receipt remains idempotent");
        TickRefund(); Probe.Check(Probe.TicketCount == 3, "H cancellation cannot refund twice");

        Probe.Reset(BossRushPendingEntryKind.ModeH); receipt = Receipt(); asset = BossRushCoop.Asset;
        CancelDuringDebit(() =>
        {
            BossRushCoop.Asset = "h-receipt-away";
            BossRushMapSelectionHelper.ClearPendingEntryFlowState();
            BossRushMapSelectionHelper.ClearPendingMapEntry();
        });
        TickRefund();
        Probe.Check(Probe.TicketCount == 2 && !receipt.Refund(), "H refund cannot transfer the debit to another asset");
        BossRushCoop.Asset = asset;
        TickRefund();
        Probe.Check(Probe.TicketCount == 3 && receipt.Refund(), "H refund restores the frozen generation on its original asset only");

        Probe.Reset(BossRushPendingEntryKind.ModeH); receipt = Receipt();
        Probe.StartVote(); Probe.Commit();
        Probe.Check(Probe.TicketCount == 2 && !receipt.Refund(), "published H receipt is never reconstructed for cancellation");
    }
}
