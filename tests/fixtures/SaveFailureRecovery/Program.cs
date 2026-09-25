using System;
using System.Collections.Generic;
using BossRush;
using Duckov.Economy;
using ItemStatsSystem;
using Saves;

class Program
{
    const string BetKey = "BossRush_ModeHCashBet_v1";
    const string ItemKey = "Item/MainCharacterItemData";
    static int checks;
    static Inventory Bag { get { return CharacterMainControl.Main.CharacterItem.Inventory; } }
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        checks++; Console.WriteLine("PASS " + name);
    }
    static void Reset()
    {
        SavesSystem.IsSaving = true;
        ModeHCashBetService.ResetStaticCaches(); ModeHItemBetStake.ResetStaticCaches();
        DailyReportSaveCoordinator.ResetStaticCaches(); DailyReportService.ResetStaticCaches();
        if (CharacterMainControl.Main != null) CharacterMainControl.Main.CharacterItem.DestroyTree();
        SavesSystem.Reset(); BossRushSaveFileThrottle.ResetStaticCaches();
        UnityEngine.Time.frameCount++; UnityEngine.Time.realtimeSinceStartup = 0;
        LevelManager.Instance = new LevelManager { IsBaseLevel = true };
        GameClock.Instance = new GameClock { clockTimeScale = 60 };
        EconomyManager.Instance = new EconomyManager(); EconomyManager.Money = 100000;
        EconomyManager.Reject = EconomyManager.ThrowAfter = false;
        ItemUtilities.ThrowBefore = ItemUtilities.ThrowAfter = false;
        ItemUtilities.Deliveries = 0; ItemUtilities.FailAfter = -1; ItemUtilities.OnDelivery = null;
        Item.FailSnapshot = false; ItemAssetsCollection.Available = true;
        ModeHRewardItemPool.Created.Clear(); ModBehaviour.DevModeEnabled = false;
        Item root = new Item { TypeID = 1, DisplayName = "character" };
        root.Inventory = new Inventory { Owner = root, Capacity = 20 };
        CharacterMainControl.Main = new CharacterMainControl { CharacterItem = root };
    }
    static Item Add(int type = 101, int value = 1000, int count = 1)
    {
        Item item = new Item { TypeID = type, DisplayName = "stake " + type, Value = value, StackCount = count };
        Check(Bag.AddItem(item), "fixture item fits in backpack");
        return item;
    }
    static List<ModeHItemBetEntry> Reserve(params Item[] items)
    {
        // 玩家页面会先读账本，夹具同样在锁物品前绑定本槽。
        var current = ModeHCashBetService.Current;
        string reason;
        foreach (Item item in items) Check(ModeHItemBetStake.Toggle(item.GetInstanceID(), out reason), "selection accepted");
        long value; List<ModeHItemBetEntry> entries;
        Check(ModeHItemBetStake.TryLock(out value, out entries, out reason), "stake locked with stable identities");
        Check(ModeHCashBetService.TryReserveItems("run", 1, 5, value, entries, out reason), "item reservation accepted");
        ModeHItemBetStake.ClearSelection();
        Check(SavesSystem.Disk.ContainsKey(ItemKey), "reservation snapshots item identities with ledger");
        return entries;
    }
    static void Settle(bool won) { new ModeHRuntimeModule(42).Settle(won); }
    static void Tick()
    {
        UnityEngine.Time.realtimeSinceStartup += 2; UnityEngine.Time.frameCount++;
        ModeHCashBetService.Tick();
    }
    static BossRushJsonValue SavedBet()
    {
        return BossRushJsonParser.ParseOrNull((string)SavesSystem.Disk[BetKey]);
    }
    static void Restart()
    {
        // 模拟进程被杀：禁止 Shutdown 最后补盘，并且必经旧角色/组件销毁、从磁盘重建新对象。
        SavesSystem.IsSaving = true;
        ModeHCashBetService.ResetStaticCaches(); ModeHItemBetStake.ResetStaticCaches();
        DailyReportSaveCoordinator.ResetStaticCaches(); DailyReportService.ResetStaticCaches();
        Item old = CharacterMainControl.Main.CharacterItem;
        old.DestroyTree(); Check(old == null, "scene teardown destroys item component through GameObject");
        foreach (Item item in ModeHRewardItemPool.Created) if (item != null) item.DestroyTree();
        SavesSystem.Cache = new Dictionary<string, object>(SavesSystem.Disk);
        CharacterMainControl.Main = new CharacterMainControl { CharacterItem = ((Snapshot)SavesSystem.Disk[ItemKey]).Restore() };
        object cash;
        if (SavesSystem.Disk.TryGetValue("EconomyData", out cash)) EconomyManager.Money = ((EconomyManager.SaveData)cash).money;
        else EconomyManager.Money = 100000;
        SavesSystem.IsSaving = false; SavesSystem.FailAt = 0; Item.FailSnapshot = false;
        ItemUtilities.ThrowBefore = ItemUtilities.ThrowAfter = false; ItemUtilities.FailAfter = -1;
        BossRushSaveFileThrottle.ResetStaticCaches();
        var loaded = ModeHCashBetService.Current;
    }

    static void DailyRollover()
    {
        Reset(); DailyReportPersistence.EnsureSubscribed(); DailyReportPersistence.LoadOrInit();
        var data = DailyReportCodec.CreateDefault();
        data.DayIndex = data.LastSignedDayIndex = data.PeriodSignedCount = data.Streak = data.TotalSignedDays = 7;
        data.PeriodIndex = 1;
        Check(DailyReportPersistence.Store(data), "daily baseline accepted");
        DailyReportService.Tick(0.001f);
        SavesSystem.FailAt = SavesSystem.Attempts + 1;
        DailyReportService.DebugAdvanceGameSeconds(DailyReportTuning.GameSecondsPerDay);
        Check(SavesSystem.IsSaving && DailyReportPersistence.Current.DayIndex == 8
            && DailyReportService.CarrySeconds < 1, "IO failure consumes the accepted day exactly once");
        UnityEngine.Time.realtimeSinceStartup = 60; DailyReportService.Tick(0.016f);
        Check(DailyReportPersistence.Current.DayIndex == 8 && DailyReportPersistence.Current.PeriodSignedCount == 7
            && DailyReportService.RolloverCount == 1, "retry interval does not invent day nine or reset streak");
        SavesSystem.IsSaving = false; SavesSystem.FailAt = 0; UnityEngine.Time.frameCount++;
        SavesSystem.Collect(); DailyReportSaveCoordinator.Tick();
        var saved = DailyReportCodec.Decode((string)SavesSystem.Disk[DailyReportTuning.StorageKey]);
        Check(saved.DayIndex == 8 && Math.Abs(saved.CarrySeconds - DailyReportService.CarrySeconds) < 0.00001,
            "official collect and coordinator retry persist matching day and remainder");

        Reset(); DailyReportPersistence.EnsureSubscribed(); DailyReportPersistence.LoadOrInit();
        ModBehaviour.DevModeEnabled = true; DailyReportPersistence.SetValidationRejectStore(true);
        DailyReportService.DebugAdvanceGameSeconds(DailyReportTuning.GameSecondsPerDay);
        Check(DailyReportPersistence.Current.DayIndex == 1 && DailyReportService.CarrySeconds >= DailyReportTuning.GameSecondsPerDay
            && !DailyReportService.HasPendingIssueBanner, "rejected candidate keeps elapsed time and suppresses banner");
        DailyReportPersistence.SetValidationRejectStore(false);
        UnityEngine.Time.realtimeSinceStartup = 60; DailyReportService.Tick(0.001f);
        Check(DailyReportPersistence.Current.DayIndex == 2 && DailyReportService.RolloverCount == 1, "rejected candidate retries once after recovery");

        Reset(); DailyReportPersistence.EnsureSubscribed(); DailyReportPersistence.LoadOrInit();
        SavesSystem.FailAt = 1;
        Check(DailyReportService.SignInAndClaim().Outcome == DailyReportSignInOutcome.PersistBlocked,
            "claim UI still reports hard write failure");
    }

    static void WinAndRetries()
    {
        Reset(); Reserve(Add()); Settle(true);
        Check(ModeHCashBetService.Current.status == ModeHCashBetService.StatusSettled && Bag.Count == 2,
            "successful prize delivery settles the bet");
        Check(SavedBet().GetInt("status", -1) == ModeHCashBetService.StatusSettled
            && ((Snapshot)SavesSystem.Disk[ItemKey]).Children.Count == 2, "settled receipt includes the delivered inventory");
        long cash = EconomyManager.Money; Restart(); Tick(); Settle(true);
        Check(Bag.Count == 2 && EconomyManager.Money == cash && ModeHCashBetService.Current.tierBets[5] == 1,
            "reloaded settled bet grants and counts nothing twice");
        int writes = SavesSystem.Writes;
        for (int i = 0; i < 100; i++) Tick();
        Check(SavesSystem.Writes == writes, "idle settled bet does not snapshot or save each frame");

        Reset(); Reserve(Add()); ItemUtilities.ThrowBefore = true; Settle(true);
        var pending = ModeHCashBetService.Current;
        Check(pending.status == ModeHCashBetService.StatusReserved && pending.itemSettlement == 1
            && ModeHItemBetEntry.Decode(pending.pendingItems).Count == 1 && ItemUtilities.Deliveries == 0,
            "failed send retains a fixed prize obligation");
        Check(ModeHRewardItemPool.Created.TrueForAll(item => item == null), "undelivered temporary prizes have a cleanup owner");
        long ignored; string failure;
        Check(!ModeHCashBetService.TryRefund("abandon", out ignored)
            && !ModeHCashBetService.TryReserve("next", 2, 3, 1000, out failure), "pending delivery cannot be erased by abandon or another bet");
        string plan = pending.pendingItems; Restart();
        Check(ModeHCashBetService.Current.pendingItems == plan, "restart preserves exact prize identities and types");
        Tick();
        Check(Bag.Count == 2 && ModeHCashBetService.Current.status == ModeHCashBetService.StatusSettled,
            "pending prize is delivered after restart without a season owner");

        Reset(); Reserve(Add()); ItemUtilities.ThrowAfter = true; Settle(true);
        Check(Bag.Count == 2 && ItemUtilities.Deliveries == 1
            && ModeHCashBetService.Current.status == ModeHCashBetService.StatusSettled,
            "post-delivery notification exception is recognized as delivered");

        Reset(); Reserve(Add()); Bag.Capacity = Bag.Count; Settle(true);
        int created = ModeHRewardItemPool.Created.Count; Tick(); Tick();
        Check(Bag.Count == 1 && ModeHRewardItemPool.Created.Count == created
            && ModeHCashBetService.Current.status == ModeHCashBetService.StatusReserved,
            "full backpack retains debt without creating orphan instances on every retry");
        Bag.Capacity++; Tick();
        Check(Bag.Count == 2 && ModeHCashBetService.Current.status == ModeHCashBetService.StatusSettled, "freeing backpack space completes delivery");

        Reset(); Reserve(Add(101), Add(102), Add(103)); ItemUtilities.FailAfter = 1; Settle(true);
        Check(Bag.Count == 4 && ModeHItemBetEntry.Decode(ModeHCashBetService.Current.pendingItems).Count == 2,
            "partial delivery saves only remaining prize obligations");
        Restart(); Tick();
        Check(Bag.Count == 6 && ModeHCashBetService.Current.tierBets[5] == 1, "partial delivery recovery never repeats first prize");
    }

    static void CrashBoundaries()
    {
        for (int failureStep = 1; failureStep <= 3; failureStep++)
        {
            Reset(); Reserve(Add()); SavesSystem.FailAt = SavesSystem.Attempts + failureStep; Settle(true);
            Check(SavesSystem.IsSaving, "injected physical failure at item settlement stage " + failureStep);
            Restart(); Settle(true); Tick();
            Check(Bag.Count == 2 && ModeHCashBetService.Current.status == ModeHCashBetService.StatusSettled
                && ModeHCashBetService.Current.tierBets[5] == 1
                && EconomyManager.Money == 100000 + ModeHCashBetService.Current.prizeCash,
                "crash at plan, inventory or money save recovers exactly once: " + failureStep);
        }

        Reset(); Reserve(Add()); Item.FailSnapshot = true; Settle(true);
        Check(Bag.Count == 2 && ModeHCashBetService.Current.pendingItems.Length == 0
            && SavedBet().GetString("pendingItems", "").Length > 0,
            "failed asset collection cannot publish an acknowledged prize");
        Item.FailSnapshot = false; Tick(); Tick();
        Check(Bag.Count == 2 && ModeHCashBetService.Current.status == ModeHCashBetService.StatusSettled,
            "asset snapshot retry does not deliver twice");

        Reset(); Reserve(Add());
        ItemUtilities.OnDelivery = () => SavesSystem.Collect();
        Settle(true);
        Check(Bag.Count == 2 && ModeHCashBetService.Current.status == ModeHCashBetService.StatusSettled,
            "reentrant official collect during transfer cannot publish staged ledger");
        ItemUtilities.OnDelivery = null;

        Reset(); Reserve(Add()); ItemUtilities.ThrowBefore = true; Settle(true);
        SavesSystem.ChangeSlot(2); ItemUtilities.ThrowBefore = false; Tick();
        Check(ModeHCashBetService.Current.status == ModeHCashBetService.StatusNone && !ModeHItemBetStake.HasLocked
            && Bag.Count == 1, "slot switch cancels old runtime identities and delivery retries");
    }

    static void LossAndIdentity()
    {
        Reset(); Item other = Add(), selected = Add(); var entries = Reserve(selected);
        Restart(); Item restoredOther = Bag[0], restoredSelected = Bag[1]; Settle(false);
        Check(restoredOther != null && restoredSelected == null && Bag.Count == 1 && EconomyManager.Money == 100000,
            "same-type recovery forfeits only the selected persistent identity");
        Check(((Snapshot)SavesSystem.Disk[ItemKey]).Children.Count == 1, "loss saves removed stake with settled ledger");

        Reset(); selected = Add(); Reserve(selected); SavesSystem.IsSaving = true; Settle(false);
        Check(selected != null && Bag.Count == 1 && ModeHCashBetService.Current.itemSettlement == 0,
            "save-busy loss never removes stake before transaction can start");
        SavesSystem.IsSaving = false; SavesSystem.FailAt = SavesSystem.Attempts + 2; Settle(false);
        Check(selected == null && SavesSystem.IsSaving, "loss asset-save failure reached");
        Restart(); Settle(false); Tick();
        Check(Bag.Count == 0 && EconomyManager.Money == 100000 && ModeHCashBetService.Current.tierBets[5] == 1,
            "loss crash replays matching disk inventory without charging missing value twice");

        Reset(); selected = Add(count: 4); Reserve(selected); selected.StackCount = 2;
        // 保存玩家已使用的数量，然后模拟读档；身份仍属于原先那一组。
        CharacterMainControl.Main.CharacterItem.Save("MainCharacterItemData"); SavesSystem.SaveFile(false);
        Restart(); Settle(false);
        Check(Bag.Count == 0 && ModeHCashBetService.Current.charged == 1000 && EconomyManager.Money == 99000,
            "reduced stack recovery removes remaining units and charges only consumed units");

        Reset(); selected = Add(count: 2); Reserve(selected); selected.StackCount = 5; Settle(false);
        Check(selected != null && selected.StackCount == 3 && ModeHCashBetService.Current.charged == 0,
            "merged extra units stay with the player");

        Reset(); selected = Add(); other = Add(); entries = Reserve(selected);
        ModeHItemBetStake.ResetStaticCaches(); other.SetString(ModeHItemBetStake.IdentityKey, entries[0].Identity, true);
        ModeHItemBetStake.RebindFromLedger(entries);
        Check(ModeHItemBetStake.ForfeitLocked() == entries[0].Value && selected != null && other != null,
            "duplicate identity fails closed instead of selecting an arbitrary item");

        Reset(); selected = Add(); other = Add(); entries = new List<ModeHItemBetEntry> {
            new ModeHItemBetEntry { TypeId = selected.TypeID, Count = 1, Value = 500, Quality = 3, Name = selected.DisplayName } };
        ModeHItemBetStake.RebindFromLedger(entries);
        Check(ModeHItemBetStake.ForfeitLocked() == 500 && Bag.Count == 2,
            "legacy ledger without identity uses missing-item compensation and destroys no guessed item");
    }

    static void Schema()
    {
        Reset();
        SavesSystem.Cache[BetKey] = "{\"schemaVersion\":1,\"runId\":\"old\",\"matchIndex\":1,\"odds\":3,\"status\":1,\"amount\":\"500\",\"kind\":1,\"items\":\"101|1|500|3|old\"}";
        Check(ModeHCashBetService.Current.runId == "old" && ModeHCashBetService.Current.itemSettlement == 0
            && ModeHItemBetEntry.Decode(ModeHCashBetService.Current.items)[0].Identity == string.Empty,
            "literal v1 save defaults new recovery fields without inventing identities");
        long refund; Check(ModeHCashBetService.TryRefund("old", out refund), "v1 remains writable through compatible upgrade");
        Check(SavedBet().GetInt("schemaVersion", -1) == 3, "normal write upgrades recovery schema to v3");
        Reset(); SavesSystem.Cache[BetKey] = "{\"schemaVersion\":4}";
        string reason;
        Check(!ModeHCashBetService.TryReserve("run", 1, 3, 1000, out reason)
            && (string)SavesSystem.Cache[BetKey] == "{\"schemaVersion\":4}", "unknown future schema is never overwritten");
    }

    static int Main()
    {
        try
        {
            DailyRollover(); WinAndRetries(); CrashBoundaries(); LossAndIdentity(); Schema();
            Console.WriteLine("SaveFailureRecovery: PASS " + checks + " assertions (real services, stores and coordinators; in-memory host)");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
