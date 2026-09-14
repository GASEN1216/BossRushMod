using System;
using BossRush;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using Saves;

/// <summary>
/// 丧尸模式入场回滚欠账账本（<see cref="ZombieModeEntryDebt"/>，CR-2026-09-11-019）的隔离执行回归。
///
/// 生产文件原样链接执行，只替身官方存档/经济/物品/通知。覆盖三条纪律：
///   1. **不吞**：经济暂不可用或资源未就绪时，钱与邀请函一定记进存档账本；
///      连账都记不下时 Refund* 返回 false，调用方据此保留自己的事务状态；
///   2. **不重复**：结账先到账再销账，销账失败只会重发、不会吞；已送达的不再欠；
///   3. **接得回来**：订阅官方「经济加载完成」幂等、成对退订，一广播就结清。
/// </summary>
internal static class Program
{
    private static int checks;

    private static void Check(bool value, string description)
    {
        checks++;
        if (!value) throw new Exception("FAIL " + description);
    }

    private static void ResetAll()
    {
        ZombieModeEntryDebt.ResetStaticCaches();
        SavesSystem.Reset();
        EconomyManager.Reset();
        NotificationText.Reset();
        ItemAssetsCollection.Reset();
        ItemUtilities.Reset();
        ModBehaviour.ResetLogs();
    }

    // ---- 1. 空账与老档 ----
    private static void EmptyLedger()
    {
        ResetAll();
        Check(ZombieModeEntryDebt.ReadCash() == 0L, "a fresh save owes no cash");
        Check(ZombieModeEntryDebt.ReadInvitations() == 0, "a fresh save owes no invitations");
        // SCHEMA+：老档没有这两个 key，结账是无操作且不碰经济。
        Check(ZombieModeEntryDebt.TrySettleCash() && ZombieModeEntryDebt.TrySettleInvitations(), "settling an empty ledger succeeds");
        Check(EconomyManager.Money == 0L && SavesSystem.Writes == 0, "settling an empty ledger writes nothing");

        // 读取路径故障也只退化成 0，绝不抛。
        SavesSystem.ThrowOnLoad = true;
        Check(ZombieModeEntryDebt.ReadCash() == 0L && ZombieModeEntryDebt.ReadInvitations() == 0, "a faulted store reads as no debt");
        SavesSystem.ThrowOnLoad = false;
    }

    // ---- 2. 现金：退得出去就直接到账 ----
    private static void CashRefundedImmediately()
    {
        ResetAll();
        Check(ZombieModeEntryDebt.RefundCash(0L), "a zero refund is already settled");
        Check(SavesSystem.Writes == 0, "a zero refund never touches the ledger");

        Check(ZombieModeEntryDebt.RefundCash(2500L), "a refund succeeds while the economy is up");
        Check(EconomyManager.Money == 2500L, "the player gets the entry fee back");
        Check(ZombieModeEntryDebt.ReadCash() == 0L, "nothing is owed after a successful refund");
        Check(NotificationText.Pushed.Contains("BossRush_ZombieMode_Notify_RefundedCash"), "the player is told about the refund");
    }

    // ---- 3. 现金：经济暂不可用 → 落账本，绝不蒸发 ----
    private static void CashFallsBackToLedger()
    {
        ResetAll();
        EconomyManager.Available = false;
        Check(ZombieModeEntryDebt.RefundCash(2500L), "a refund that cannot pay still reports settled once it is booked");
        Check(EconomyManager.Money == 0L, "nothing was paid while the economy was down");
        Check(ZombieModeEntryDebt.ReadCash() == 2500L, "the unpaid refund is booked in the ledger");

        // 第二次入场失败叠加，不是覆盖。
        Check(ZombieModeEntryDebt.RefundCash(1500L), "a second failed refund is booked too");
        Check(ZombieModeEntryDebt.ReadCash() == 4000L, "debts accumulate instead of overwriting");

        // 经济回来 → 一次付清。
        EconomyManager.Available = true;
        Check(ZombieModeEntryDebt.TrySettleCash(), "the ledger settles once the economy is back");
        Check(EconomyManager.Money == 4000L, "the whole debt reaches the player");
        Check(ZombieModeEntryDebt.ReadCash() == 0L, "the ledger is cleared after payout");
    }

    // ---- 4. 现金：Add 抛异常等同不可用；连账都记不下要如实返回 false ----
    private static void CashFaultPaths()
    {
        ResetAll();
        EconomyManager.ThrowOnAdd = true;
        Check(ZombieModeEntryDebt.RefundCash(800L), "an economy exception still books the debt");
        Check(ZombieModeEntryDebt.ReadCash() == 800L, "the debt survives an economy exception");
        EconomyManager.ThrowOnAdd = false;

        ResetAll();
        EconomyManager.Available = false;
        SavesSystem.FilePresent = false; // 没有当前存档文件：官方 Save 静默丢弃
        Check(!ZombieModeEntryDebt.RefundCash(900L), "a refund that can neither pay nor book reports failure");
        Check(ZombieModeEntryDebt.ReadCash() == 0L, "nothing was booked when the store silently dropped the write");

        ResetAll();
        EconomyManager.Available = false;
        SavesSystem.ThrowOnSave = true;
        Check(!ZombieModeEntryDebt.RefundCash(900L), "a throwing store also reports failure instead of swallowing the debt");
    }

    // ---- 5. 现金：结账时经济仍不可用 → 账目原样保留 ----
    private static void CashSettleKeepsDebtWhenDown()
    {
        ResetAll();
        EconomyManager.Available = false;
        ZombieModeEntryDebt.RefundCash(1200L);
        Check(!ZombieModeEntryDebt.TrySettleCash(), "settling fails while the economy is still down");
        Check(ZombieModeEntryDebt.ReadCash() == 1200L, "a failed settlement keeps the debt intact");

        EconomyManager.ThrowOnAdd = true;
        Check(!ZombieModeEntryDebt.TrySettleCash(), "an exception during settlement fails");
        Check(ZombieModeEntryDebt.ReadCash() == 1200L, "an exception during settlement keeps the debt intact");
        EconomyManager.ThrowOnAdd = false;
    }

    // ---- 6. 现金：先到账再销账；销账失败只重发、不吞 ----
    private static void CashPayBeforeClearing()
    {
        ResetAll();
        EconomyManager.Available = false;
        ZombieModeEntryDebt.RefundCash(700L);
        EconomyManager.Available = true;
        SavesSystem.ThrowOnSave = true;
        Check(ZombieModeEntryDebt.TrySettleCash(), "money reaching the player counts as settled even if clearing fails");
        Check(EconomyManager.Money == 700L, "the player was paid before the ledger write");
        SavesSystem.ThrowOnSave = false;
        Check(ZombieModeEntryDebt.ReadCash() == 700L, "a failed clear leaves the debt (re-pay beats swallowing)");
    }

    // ---- 7. 邀请函：资源就绪 / 未就绪 ----
    private static void InvitationRefund()
    {
        ResetAll();
        Check(ZombieModeEntryDebt.RefundInvitation(), "an invitation refund succeeds while assets are ready");
        Check(ItemUtilities.Delivered.Count == 1 && ItemUtilities.Delivered[0].TypeID == 500045, "the invitation is sent to the player");
        Check(ZombieModeEntryDebt.ReadInvitations() == 0, "nothing is owed after a successful invitation refund");

        ResetAll();
        ItemAssetsCollection.Available = false;
        Check(ZombieModeEntryDebt.RefundInvitation(), "an invitation that cannot be built is booked instead");
        Check(ItemUtilities.Delivered.Count == 0, "nothing was delivered while assets were missing");
        Check(ZombieModeEntryDebt.ReadInvitations() == 1, "the unpaid invitation is booked in the ledger");

        // 实例造出来了但送达抛异常：按已发放处理，不重复发（否则会凭空多一张）。
        ResetAll();
        ItemUtilities.ThrowOnSend = true;
        Check(ZombieModeEntryDebt.RefundInvitation(), "a delivery exception after instantiation counts as delivered");
        Check(ZombieModeEntryDebt.ReadInvitations() == 0, "a delivery exception does not book a duplicate invitation");
    }

    // ---- 8. 邀请函：逐张结账，中途失败剩下的仍在账上 ----
    private static void InvitationSettlesOneAtATime()
    {
        ResetAll();
        ItemAssetsCollection.Available = false;
        ZombieModeEntryDebt.RefundInvitation();
        ZombieModeEntryDebt.RefundInvitation();
        ZombieModeEntryDebt.RefundInvitation();
        Check(ZombieModeEntryDebt.ReadInvitations() == 3, "three failed refunds accumulate");

        Check(!ZombieModeEntryDebt.TrySettleInvitations(), "settling fails while assets are still missing");
        Check(ZombieModeEntryDebt.ReadInvitations() == 3, "a failed settlement keeps every invitation");

        ItemAssetsCollection.Available = true;
        Check(ZombieModeEntryDebt.TrySettleInvitations(), "settling succeeds once assets are ready");
        Check(ItemUtilities.Delivered.Count == 3, "every owed invitation reaches the player");
        Check(ZombieModeEntryDebt.ReadInvitations() == 0, "the invitation ledger is cleared");

        // 中途资源掉线：已发的销账，没发的留账（逐张推进的意义就在这里）。
        ResetAll();
        ItemAssetsCollection.Available = false;
        ZombieModeEntryDebt.RefundInvitation();
        ZombieModeEntryDebt.RefundInvitation();
        Check(ZombieModeEntryDebt.ReadInvitations() == 2, "two invitations are owed before the partial settlement");
        ItemAssetsCollection.Available = true;
        ItemAssetsCollection.FailAfter = 1; // 只造得出第一张
        Check(!ZombieModeEntryDebt.TrySettleInvitations(), "a partial settlement reports failure");
        Check(ItemUtilities.Delivered.Count == 1, "only the first invitation was delivered");
        Check(ZombieModeEntryDebt.ReadInvitations() == 1, "the undelivered invitation stays on the ledger");
    }

    // ---- 9. 两笔账互不影响 ----
    private static void DebtsAreIndependent()
    {
        ResetAll();
        EconomyManager.Available = false;
        ItemAssetsCollection.Available = false;
        ZombieModeEntryDebt.RefundCash(300L);
        ZombieModeEntryDebt.RefundInvitation();

        EconomyManager.Available = true; // 只有经济回来
        ZombieModeEntryDebt.TrySettleAll();
        Check(EconomyManager.Money == 300L && ZombieModeEntryDebt.ReadCash() == 0L, "cash settles on its own");
        Check(ZombieModeEntryDebt.ReadInvitations() == 1, "the invitation debt is untouched by the cash payout");

        ItemAssetsCollection.Available = true;
        ZombieModeEntryDebt.TrySettleAll();
        Check(ZombieModeEntryDebt.ReadInvitations() == 0, "the invitation settles once its assets return");
    }

    // ---- 10. 订阅：幂等、成对退订、一广播就结清 ----
    private static void SubscriptionLifecycle()
    {
        ResetAll();
        ZombieModeEntryDebt.Attach();
        ZombieModeEntryDebt.Attach();
        ZombieModeEntryDebt.Attach();
        Check(EconomyManager.SubscriberCount == 1, "attaching is idempotent");

        EconomyManager.Available = false;
        ZombieModeEntryDebt.RefundCash(4200L);
        EconomyManager.Available = true;
        EconomyManager.RaiseLoaded();
        Check(EconomyManager.Money == 4200L, "the official economy-loaded broadcast settles the ledger");
        Check(ZombieModeEntryDebt.ReadCash() == 0L, "the ledger is cleared by the broadcast");

        ZombieModeEntryDebt.ResetStaticCaches();
        Check(EconomyManager.SubscriberCount == 0, "teardown unsubscribes");
        ZombieModeEntryDebt.ResetStaticCaches();
        Check(EconomyManager.SubscriberCount == 0, "teardown is idempotent");

        // 退订之后广播不再结账（防止宿主销毁后还有回调写存档）。
        EconomyManager.Available = false;
        ZombieModeEntryDebt.RefundCash(100L);
        EconomyManager.Available = true;
        EconomyManager.RaiseLoaded();
        Check(ZombieModeEntryDebt.ReadCash() == 100L, "a detached ledger is not settled by the broadcast");
    }

    private static int Main()
    {
        try
        {
            EmptyLedger();
            CashRefundedImmediately();
            CashFallsBackToLedger();
            CashFaultPaths();
            CashSettleKeepsDebtWhenDown();
            CashPayBeforeClearing();
            InvitationRefund();
            InvitationSettlesOneAtATime();
            DebtsAreIndependent();
            SubscriptionLifecycle();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 1;
        }
        Console.WriteLine("PASS ZombieModeEntryDebt: " + checks
            + " assertions (production ledger; saves/economy/item substitutes)");
        return 0;
    }
}
