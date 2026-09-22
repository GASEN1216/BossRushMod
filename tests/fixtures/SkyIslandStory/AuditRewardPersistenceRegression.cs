using System;
using BossRush;
using Duckov.Economy;
using Saves;

internal static class AuditRewardPersistenceRegression
{
    internal static void Run(Action<bool, string> check)
    {
        SavesSystem.Switch(9901);
        SavesSystem.Global.Clear();
        EconomyManager.Money = 100;
        var journal = new AchievementRewardJournal();
        check(journal.TryPay("one", 500, id => false), "achievement cash commits");
        check(((EconomyManager.SaveData)SavesSystem.Durable[9901]["EconomyData"]).money == 600, "cash is durable before global claimed flag");
        journal.Shutdown();
        SavesSystem.CrashReload(9901);
        journal = new AchievementRewardJournal();
        check(journal.TryPay("one", 500, id => false) && EconomyManager.Money == 600, "crash between local receipt and global flag never pays twice");
        journal.Shutdown();
        SavesSystem.Switch(9902);
        journal = new AchievementRewardJournal();
        check(!journal.TryPay("one", 500, id => false), "other slot cannot claim outstanding global intent");
        journal.Shutdown();
        SavesSystem.Switch(9901);
        journal = new AchievementRewardJournal();
        journal.Complete();
        SavesSystem.FailPhysical = true;
        check(!journal.TryPay("two", 700, id => false), "physical write failure retains receipt and retry");
        check(EconomyManager.Money == 1300, "failed flush does not erase live paid cash");
        SavesSystem.FailPhysical = false;
        SavesSystem.ClearStuckSaving();
        check(journal.TryPay("two", 700, id => false) && EconomyManager.Money == 1300, "retry flush does not repeat cash");
        journal.Complete();
        EconomyManager.RejectAdd = true;
        check(!journal.TryPay("three", 800, id => false), "Add false leaves reward unclaimed");
        EconomyManager.RejectAdd = false;
        check(journal.TryPay("three", 800, id => false) && EconomyManager.Money == 2100, "Add failure remains retryable");
        journal.Complete();
        EconomyManager.ThrowAfterAdd = true;
        check(journal.TryPay("four", 900, id => false) && EconomyManager.Money == 3000, "money observer exception uses actual receipt");
        EconomyManager.ThrowAfterAdd = false;
        journal.Complete();
        journal.Shutdown();
        SavesSystem.Switch(9904);
        SavesSystem.Global.Clear(); SavesSystem.DurableGlobal.Clear();
        EconomyManager.Money = 100;
        journal = new AchievementRewardJournal();
        SavesSystem.FailGlobalAfterCache = true;
        try { journal.TryPay("cached_intent", 400, id => false); } catch (InvalidOperationException) { }
        check(EconomyManager.Money == 100 && !SavesSystem.DurableGlobal.ContainsKey(AchievementRewardJournal.IntentKey),
            "failed global physical intent does not pay");
        SavesSystem.FailGlobalAfterCache = false;
        check(journal.TryPay("cached_intent", 400, id => false) && EconomyManager.Money == 500,
            "retry after cache-only intent can pay");
        check(SavesSystem.DurableGlobal.ContainsKey(AchievementRewardJournal.IntentKey)
            && (string)SavesSystem.DurableGlobal[AchievementRewardJournal.IntentKey] == "1|9904|cached_intent",
            "cached intent must become durable before local receipt and cash");
        journal.Complete(); journal.Shutdown();
        EconomyManager.Money = 3000;
        SavesSystem.Switch(9903);
        SavesSystem.Save(AchievementRewardJournal.ReceiptKey, "{\"schemaVersion\":1,\"receipts\":false}");
        journal = new AchievementRewardJournal();
        check(!journal.TryPay("bad", 100, id => false) && EconomyManager.Money == 3000, "bad receipts block writes and cash");
        journal.Shutdown();

        SavesSystem.Switch(9910);
        var story = new SkyIslandStoryService(); story.Open();
        string message;
        check(story.TryApply(SkyIslandStoryAction.AcceptPrelude, out message), "quest accepted");
        check(story.TryApply(SkyIslandStoryAction.RecoverPreludeInstrument, out message), "quest instrument ready");
        EconomyManager.Money = 100;
        EconomyManager.RejectAdd = true;
        check(!story.TryDeliverQuest(SkyIslandStoryAction.UnlockRoute, 5000, out message) && !story.Current.SkyIslandRouteUnlocked,
            "quest Add false preserves undelivered flag");
        EconomyManager.RejectAdd = false;
        EconomyManager.ThrowAfterAdd = true;
        check(story.TryDeliverQuest(SkyIslandStoryAction.UnlockRoute, 5000, out message), "quest accepts confirmed cash despite observer failure");
        EconomyManager.ThrowAfterAdd = false;
        story.Tick(true);
        check(((EconomyManager.SaveData)SavesSystem.Durable[9910]["EconomyData"]).money == 5100,
            "quest cash snapshot is in delivered story physical batch");
        check(SkyIslandStoryCodec.Decode((string)SavesSystem.Durable[9910][SkyIslandStoryRules.StorageKey]).SkyIslandRouteUnlocked,
            "quest delivered flag shares durable cash batch");
        check(!story.TryDeliverQuest(SkyIslandStoryAction.UnlockRoute, 5000, out message) && EconomyManager.Money == 5100,
            "repeated quest delivery does not grant cash");
        check(story.TryClose(), "quest reward owner cleans up");
        EconomyManager.Money = 100;
        SavesSystem.Global.Clear();
    }
}
