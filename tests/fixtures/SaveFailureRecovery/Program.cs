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
        EconomyManager.Cash = 0; EconomyManager.PayCalls = 0;
        EconomyManager.Reject = EconomyManager.ThrowAfter = false;
        ItemUtilities.ThrowBefore = ItemUtilities.ThrowAfter = false;
        ItemUtilities.Deliveries = 0; ItemUtilities.FailAfter = -1; ItemUtilities.OnDelivery = null;
        Item.FailSnapshot = false; ItemAssetsCollection.Available = true;
        ModeHRewardItemPool.Created.Clear(); ModBehaviour.DevModeEnabled = false;
        ModeHProfilePersistence.Saved = null;
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
        Check(!SavesSystem.IsSaving && SavesSystem.Attempts == SavesSystem.FailAt && DailyReportPersistence.Current.DayIndex == 8
            && DailyReportService.CarrySeconds < 1, "IO failure consumes the accepted day exactly once");
        UnityEngine.Time.realtimeSinceStartup = 60; DailyReportService.Tick(0.016f);
        Check(DailyReportPersistence.Current.DayIndex == 8 && DailyReportPersistence.Current.PeriodSignedCount == 7
            && DailyReportService.RolloverCount == 1, "retry interval does not invent day nine or reset streak");
        SavesSystem.FailAt = 0; UnityEngine.Time.frameCount++;
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
            Check(!SavesSystem.IsSaving && SavesSystem.Attempts >= SavesSystem.FailAt && SavesSystem.Writes == SavesSystem.FailAt - 1,
                "injected physical failure releases only its own saving latch at item settlement stage " + failureStep);
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
        Check(selected == null && !SavesSystem.IsSaving && SavesSystem.Attempts >= SavesSystem.FailAt
            && SavesSystem.Writes == SavesSystem.FailAt - 1,
            "loss asset-save failure reached and releases its own saving latch");
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

        Reset(); selected = Add(count: 2); Reserve(selected); selected.StackCount = 5;
        selected.ThrowAfterStackChange = true; Settle(false); Tick();
        Check(selected != null && selected.StackCount == 3 && ModeHCashBetService.Current.charged == 0
            && EconomyManager.Money == 100000,
            "stack notification failure after forfeiture never also charges cash");

        Reset(); selected = Add(count: 2); Reserve(selected); selected.StackCount = 5;
        selected.ThrowBeforeStackChange = true; Settle(false);
        Check(selected != null && selected.StackCount == 5 && ModeHCashBetService.Current.charged == 1000
            && EconomyManager.Money == 99000,
            "failed stack mutation compensates only units actually left with the player");

        Reset(); selected = Add(); Reserve(selected); selected.ThrowAfterDetach = true; Settle(false); Tick();
        Check(selected == null && Bag.Count == 0 && ModeHCashBetService.Current.charged == 0
            && EconomyManager.Money == 100000,
            "detach notification failure still cleans removed stake and never double charges");

        Reset(); selected = Add(count: 4); Reserve(selected); selected.StackCount = 2;
        selected.ThrowAfterDetach = true; Settle(false);
        Check(selected == null && Bag.Count == 0 && ModeHCashBetService.Current.charged == 1000
            && EconomyManager.Money == 99000,
            "partial stack detach failure charges only units consumed before settlement");

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

    static void NestedStakeOwnership()
    {
        Reset();
        var current = ModeHCashBetService.Current;
        Item container = Add(201, 1000);
        container.Inventory = new Inventory { Owner = container, Capacity = 10 };
        Item child = Add(202, 800);
        string reason;
        Check(ModeHItemBetStake.Toggle(container.GetInstanceID(), out reason)
            && ModeHItemBetStake.Toggle(child.GetInstanceID(), out reason), "separate roots can be selected together");
        Check(container.Inventory.AddItem(child), "selected item can move inside a selected container before locking");
        Check(ModeHItemBetStake.SelectedCount == 1 && ModeHItemBetStake.ListCandidates().Count == 1,
            "nested selection is pruned to the same roots shown by the picker");
        long value; List<ModeHItemBetEntry> entries;
        Check(ModeHItemBetStake.TryLock(out value, out entries, out reason)
            && entries.Count == 1 && value == ModeHItemBetStake.ValueOf(container),
            "a container and its contents are valued only once at lock");

        for (int reverse = 0; reverse <= 1; reverse++)
        {
            Reset();
            container = Add(201, 1000);
            container.Inventory = new Inventory { Owner = container, Capacity = 10 };
            child = Add(202, 800);
            Reserve(reverse == 0 ? new[] { container, child } : new[] { child, container });
            Check(container.Inventory.AddItem(child), "locked items can become nested while watching the match");
            long before = EconomyManager.Money;
            Settle(false);
            Check(Bag.Count == 0 && container == null && child == null,
                "both independently staked items are forfeited after becoming nested");
            Check(ModeHCashBetService.Current.charged == 0 && EconomyManager.Money == before,
                "nested forfeiture does not charge cash for an already confiscated child, order=" + reverse);
            Restart(); Tick();
            Check(Bag.Count == 0 && EconomyManager.Money == before && ModeHCashBetService.Current.tierBets[5] == 1,
                "nested loss persists once and does not charge or restore items on restart");
        }
    }

    static void WarehouseNotifications()
    {
        Reset(); Item item = Add(); ModeHInventoryPersistenceBridge.TestInventory = Bag;
        var snapshot = new ModeHItemTreeSnapshotDto { sourcePosition = 0, Expected = item };
        string reason;
        Bag.ThrowAfterRemove = true;
        Check(ReferenceEquals(ModeHInventoryPersistenceBridge.TryDetachAt(snapshot, out reason), item)
            && Bag.Count == 0 && item.InInventory == null,
            "warehouse removal notification preserves escrow ownership of the removed item");
        Bag.ThrowAfterAdd = true;
        Check(ModeHInventoryPersistenceBridge.TryAddAtEmpty(item, 0, out reason) && ReferenceEquals(Bag[0], item),
            "warehouse insertion notification acknowledges the delivered item");
        Bag.ThrowBeforeRemove = true;
        Check(ModeHInventoryPersistenceBridge.TryDetachAt(snapshot, out reason) == null && ReferenceEquals(Bag[0], item),
            "warehouse removal failure before mutation leaves the original item in place");
        Check(!ModeHInventoryPersistenceBridge.TryAddAtEmpty(new Item(), 0, out reason) && ReferenceEquals(Bag[0], item),
            "warehouse occupied slot remains protected after notification recovery");
    }

    static void CashRestoreReadiness()
    {
        for (int lateStart = 0; lateStart <= 1; lateStart++)
        {
            Reset(); string reason;
            Check(ModeHCashBetService.TryReserve("run", 1, 5, 1000, out reason), "cash stake reserved for restored result");
            var season = new ModeHSeasonDto { runState = new ModeHRunStateDto { runId = "run" },
                matchReports = new List<ModeHMatchReportDto> { new ModeHMatchReportDto { matchIndex = 1, winner = (int)ModeHMatchOutcome.PlayerVictory } } };
            var runtime = new ModeHRuntimeModule(42); runtime.Configure(season);
            EconomyManager.Instance = null; runtime.Reconcile();
            Check(ModeHCashBetService.Current.status == ModeHCashBetService.StatusReserved,
                "menu restore waits for the real wallet");
            EconomyManager.Instance = new EconomyManager(); runtime.LevelReady = true;
            if (lateStart == 0) runtime.OnReady(); else runtime.OnStart();
            Check(ModeHCashBetService.Current.status == ModeHCashBetService.StatusSettled
                && EconomyManager.Money == 99000 + ModeHCashBetService.ComputePayout(1000, 5),
                "restored cash win reconciles when level is ready, late start=" + lateStart);
            long paid = EconomyManager.Money; runtime.OnReady(); runtime.OnStart();
            Check(EconomyManager.Money == paid && ModeHCashBetService.Current.tierBets[5] == 1,
                "repeated readiness callbacks never pay the result twice");
        }

        Reset(); string failure;
        Check(ModeHCashBetService.TryReserve("finished", 1, 5, 1000, out failure), "cash stake reserved for completed season");
        ModeHProfilePersistence.Saved = new ModeHSeasonDto { runState = new ModeHRunStateDto { runId = "finished" },
            matchReports = new List<ModeHMatchReportDto> { new ModeHMatchReportDto { matchIndex = 1, winner = (int)ModeHMatchOutcome.PlayerVictory } } };
        var ended = new ModeHRuntimeModule(42); ended.Configure(null); ended.OnReady();
        Check(ModeHCashBetService.Current.status == ModeHCashBetService.StatusSettled
            && EconomyManager.Money == 99000 + ModeHCashBetService.ComputePayout(1000, 5),
            "ended season keeps its recorded win instead of refunding only the stake");
        SavesSystem.ChangeSlot(2); ModeHProfilePersistence.Saved = null;
        EconomyManager.Money = 23000; ended.OnReady();
        Check(EconomyManager.Money == 23000 && ModeHCashBetService.Current.status == ModeHCashBetService.StatusNone,
            "new slot readiness cannot inherit the previous slot bet");
    }

    static void CashBetSurvivesSpectatorExit()
    {
        // 看台退出 / 挂起送回基地：内存 owner 已清空、本场没有战报，但磁盘上同一季仍可续。
        // 回到基地的关卡就绪对账不能把押注退掉——押注跟着这一场走，重打时沿用。
        int[] resumable = { (int)ModeHLifecycle.MatchFighting, (int)ModeHLifecycle.Suspended };
        foreach (int lifecycle in resumable)
        {
            Reset(); string reason;
            Check(ModeHCashBetService.TryReserve("run", 1, 5, 1000, out reason), "reserve bet before spectator exit");
            long afterStake = EconomyManager.Money;
            var season = new ModeHSeasonDto { runState = new ModeHRunStateDto { runId = "run", lifecycle = lifecycle },
                matchReports = new List<ModeHMatchReportDto>() };
            ModeHProfilePersistence.Saved = season;
            var runtime = new ModeHRuntimeModule(42); runtime.Configure(season); runtime.DropRunOwner();
            runtime.LevelReady = true; runtime.OnReady();
            Check(ModeHCashBetService.Current.status == ModeHCashBetService.StatusReserved && EconomyManager.Money == afterStake,
                "resumable season keeps its pending bet after the run owner is dropped, lifecycle=" + lifecycle);
        }

        // 反面：磁盘上是别的赛季或已结束的赛季时仍原样退回
        foreach (string variant in new[] { "other_run", "season_ended" })
        {
            Reset(); string reason;
            Check(ModeHCashBetService.TryReserve("run", 1, 5, 1000, out reason), "reserve bet before refund check");
            long beforeStake = EconomyManager.Money + 1000;
            ModeHProfilePersistence.Saved = new ModeHSeasonDto { runState = new ModeHRunStateDto {
                    runId = variant == "other_run" ? "another" : "run",
                    lifecycle = variant == "other_run" ? (int)ModeHLifecycle.MatchFighting : (int)ModeHLifecycle.SeasonEnded },
                matchReports = new List<ModeHMatchReportDto>() };
            var runtime = new ModeHRuntimeModule(42); runtime.Configure(null);
            runtime.LevelReady = true; runtime.OnReady();
            Check(ModeHCashBetService.Current.status == ModeHCashBetService.StatusRefunded && EconomyManager.Money == beforeStake,
                "bet without a resumable owner season is refunded: " + variant);
        }
    }

    static void StartedBetForfeit()
    {
        // 2026-09-29 owner 拍板：开战后放弃赛季 / 换季不能白退押注，按输结清；没开战的照旧原样退回
        Reset(); string reason;
        Check(ModeHCashBetService.TryReserve("run", 1, 5, 1000, out reason), "reserve bet before combat");
        long afterStake = EconomyManager.Money;
        Check(!ModeHCashBetService.TryMarkCombatStarted("run", 2) && !ModeHCashBetService.TryMarkCombatStarted("other", 1),
            "combat mark only applies to the reserved bet of the same run and match");
        Check(ModeHCashBetService.Current.combatStarted == 0, "mismatched mark leaves the bet unmarked");
        Check(ModeHCashBetService.TryMarkCombatStarted("run", 1) && ModeHCashBetService.Current.combatStarted == 1,
            "combat start marks the pending bet");
        Check(!ModeHCashBetService.TryMarkCombatStarted("run", 1), "combat mark is written at most once");
        var season = new ModeHSeasonDto { runState = new ModeHRunStateDto { runId = "run", lifecycle = (int)ModeHLifecycle.Suspended },
            matchReports = new List<ModeHMatchReportDto>() };
        var runtime = new ModeHRuntimeModule(42); runtime.Configure(season);
        Check(runtime.ResolveBeforeAbandon(), "started bet resolves before abandonment");
        ModeHCashBetRecord settled = ModeHCashBetService.Current;
        Check(settled.status == ModeHCashBetService.StatusSettled && settled.payout == 0 && EconomyManager.Money == afterStake,
            "abandoning after combat started settles the bet as lost instead of refunding it");
        ModeHCashBetService.ResetStaticCaches();
        ModeHCashBetRecord reloaded = ModeHCashBetService.Current;
        Check(reloaded.status == ModeHCashBetService.StatusSettled && reloaded.combatStarted == 1,
            "combat mark survives the journal round trip");
        Check(ModeHCashBetService.TryReserve("run", 2, 5, 1000, out reason) && ModeHCashBetService.Current.combatStarted == 0,
            "a new reservation starts unmarked");

        // 换季：上一季挂着的已开战押注按输结清
        Reset();
        Check(ModeHCashBetService.TryReserve("old", 3, 5, 1000, out reason), "reserve bet in the old season");
        afterStake = EconomyManager.Money;
        Check(ModeHCashBetService.TryMarkCombatStarted("old", 3), "old season bet reached combat");
        ModeHProfilePersistence.Saved = new ModeHSeasonDto { runState = new ModeHRunStateDto { runId = "new", lifecycle = (int)ModeHLifecycle.MatchFighting },
            matchReports = new List<ModeHMatchReportDto>() };
        var fresh = new ModeHRuntimeModule(42); fresh.Configure(ModeHProfilePersistence.Saved);
        fresh.LevelReady = true; fresh.OnReady();
        Check(ModeHCashBetService.Current.status == ModeHCashBetService.StatusSettled && ModeHCashBetService.Current.payout == 0
            && EconomyManager.Money == afterStake, "started bet of a superseded season is settled as lost");
    }

    static void AbandonBetResolution()
    {
        for (int outcome = 0; outcome <= 2; outcome++)
        {
            Reset(); string reason;
            Check(ModeHCashBetService.TryReserve("run", 1, 5, 1000, out reason), "reserve bet before abandonment");
            var season = new ModeHSeasonDto { runState = new ModeHRunStateDto { runId = "run" },
                matchReports = new List<ModeHMatchReportDto>() };
            if (outcome > 0) season.matchReports.Add(new ModeHMatchReportDto { matchIndex = 1,
                winner = outcome == 1 ? (int)ModeHMatchOutcome.PlayerVictory : (int)ModeHMatchOutcome.PlayerDefeat });
            var runtime = new ModeHRuntimeModule(42); runtime.Configure(season);
            EconomyManager.Instance = null;
            Check(!runtime.ResolveBeforeAbandon() && ModeHCashBetService.Current.status == ModeHCashBetService.StatusReserved,
                "missing wallet preserves outstanding bet and prevents abandonment");
            EconomyManager.Instance = new EconomyManager();
            Check(runtime.ResolveBeforeAbandon(), "bet resolves before abandonment after wallet recovery");
            long expected = outcome == 0 ? 100000L : outcome == 1 ? 99000L + ModeHCashBetService.ComputePayout(1000, 5) : 99000L;
            Check(EconomyManager.Money == expected && ModeHCashBetService.Current.status ==
                (outcome == 0 ? ModeHCashBetService.StatusRefunded : ModeHCashBetService.StatusSettled),
                "abandon refunds only undecided bets and honors recorded win or loss, outcome=" + outcome);
            Check(runtime.ResolveBeforeAbandon() && EconomyManager.Money == expected,
                "repeated abandonment never repeats a payout or refund");
        }

        Reset(); Reserve(Add()); Bag.Capacity = Bag.Count;
        var wonSeason = new ModeHSeasonDto { runState = new ModeHRunStateDto { runId = "run" },
            matchReports = new List<ModeHMatchReportDto> { new ModeHMatchReportDto { matchIndex = 1,
                winner = (int)ModeHMatchOutcome.PlayerVictory } } };
        var pending = new ModeHRuntimeModule(42); pending.Configure(wonSeason);
        Check(!pending.ResolveBeforeAbandon() && ModeHCashBetService.Current.itemSettlement == 1
            && ModeHCashBetService.Current.status == ModeHCashBetService.StatusReserved,
            "full backpack keeps decided item prize debt instead of refunding or abandoning it");
        Bag.Capacity += 5;
        Check(pending.ResolveBeforeAbandon() && ModeHCashBetService.Current.status == ModeHCashBetService.StatusSettled,
            "freeing backpack space completes the original prize before abandonment");

        Reset(); string failure;
        Check(ModeHCashBetService.TryReserve("run", 1, 5, 1000, out failure), "reserve overflow boundary bet");
        EconomyManager.Money = long.MaxValue - 1000;
        var overflow = new ModeHRuntimeModule(42); overflow.Configure(wonSeason);
        Check(!overflow.ResolveBeforeAbandon() && ModeHCashBetService.Current.status == ModeHCashBetService.StatusReserved
            && EconomyManager.Money == long.MaxValue - 1000,
            "a blocked recorded payout cannot be silently replaced with a smaller principal refund");
    }

    static void BetSideAndSeasonNet()
    {
        // 2026-10-01 审查：群战押哪边随押注记进账本（沿用押注时恢复）；名人堂「净赚」按季累计，跨会话不丢、同一场只计一次
        Reset(); string reason; long payout;
        Check(ModeHCashBetService.TryReserve("run", 1, 5, 1000, out reason, ModeHCashBetService.BetSideRed)
            && ModeHCashBetService.Current.betSide == ModeHCashBetService.BetSideRed, "group bet side recorded at reservation");
        ModeHCashBetService.ResetStaticCaches();
        Check(ModeHCashBetService.ReservedFor("run", 1).betSide == ModeHCashBetService.BetSideRed, "bet side survives the journal round trip");
        Check(ModeHCashBetService.TrySettle("run", 1, true, 0L, 0L, string.Empty, out payout) && payout > 1000, "first match won");
        long first = payout - 1000;
        ModeHCashBetRecord afterFirst = ModeHCashBetService.Current;
        Check(afterFirst.netRunId == "run" && afterFirst.runNet == first && afterFirst.netMatchMask == (1 << 1),
            "settlement accumulates the season net in the ledger");
        ModeHCashBetService.ResetStaticCaches(); // 换一趟游戏会话：这一趟的会话快照不在了，账本还在
        Check(ModeHCashBetService.TryReserve("run", 2, 3, 1000, out reason)
            && ModeHCashBetService.Current.betSide == ModeHCashBetService.BetSideNone, "single-duel reservation records no side");
        Check(ModeHCashBetService.TrySettle("run", 2, false, 0L, 0L, string.Empty, out payout), "second match lost");
        ModeHCashBetService.ResetStaticCaches();
        ModeHCashBetRecord afterSecond = ModeHCashBetService.Current;
        Check(afterSecond.runNet == first - 1000 && afterSecond.netMatchMask == ((1 << 1) | (1 << 2)),
            "season net spans game sessions");
        Check(!ModeHCashBetService.TrySettle("run", 2, true, 0L, 0L, string.Empty, out payout)
            && ModeHCashBetService.Current.runNet == first - 1000, "a settled match is never counted twice");
        Check(ModeHCashBetService.TryReserve("next", 1, 5, 1000, out reason)
            && ModeHCashBetService.TrySettle("next", 1, false, 0L, 0L, string.Empty, out payout)
            && ModeHCashBetService.Current.netRunId == "next" && ModeHCashBetService.Current.runNet == -1000
            && ModeHCashBetService.Current.netMatchMask == (1 << 1), "a new season starts its own net");
        // 修复前写的账本没有这几个字段：缺省为未记录方向 / 空累计
        Reset();
        SavesSystem.Cache[BetKey] = "{\"schemaVersion\":3,\"runId\":\"old\",\"matchIndex\":2,\"odds\":4,\"status\":1,\"amount\":\"500\",\"kind\":0}";
        Check(ModeHCashBetService.Current.betSide == ModeHCashBetService.BetSideNone && ModeHCashBetService.Current.netRunId == string.Empty
            && ModeHCashBetService.Current.runNet == 0 && ModeHCashBetService.Current.netMatchMask == 0,
            "legacy ledger defaults the new optional fields");
    }

    static void CustomCashAmount()
    {
        Reset();
        Check(ModeHCashBetService.StandingAmount == 0, "custom cash choice starts with no bet");
        ModeHCashBetService.SelectCustomStandingAmount(12599);
        Check(ModeHCashBetService.StandingAmount == 12599 && ModeHCashBetService.IsCustomStandingBet,
            "custom choice keeps integer cash without a 100-cash minimum step");
        ModeHCashBetService.SelectCustomStandingAmount(long.MaxValue, 5);
        Check(ModeHCashBetService.StandingAmount == 32608, "custom maximum follows balance and actual payout ratio beyond 20000");
        ModeHCashBetService.SelectCustomStandingAmount(long.MinValue);
        Check(ModeHCashBetService.StandingAmount == 0, "negative custom amount becomes no bet without overflow");
        ModeHCashBetService.SelectCustomStandingAmount(12300);
        string reason; long payout;
        Check(ModeHCashBetService.TryReserve("custom", 1, 3, ModeHCashBetService.StandingAmount, out reason),
            "custom amount reserves through the existing transaction");
        Check(ModeHCashBetService.Current.amount == 12300 && EconomyManager.Money == 87700,
            "custom amount is the actual deduction and stored principal");
        ModeHCashBetService.SelectCustomStandingAmount(20000);
        Check(ModeHCashBetService.Current.amount == 12300, "editing next-match stake cannot rewrite a reserved stake");
        long expected = ModeHCashBetService.ComputePayout(12300, 3);
        Check(ModeHCashBetService.TrySettle("custom", 1, true, 0, 0, string.Empty, out payout) && payout == expected,
            "custom stake uses unchanged payout rules");
        ModeHCashBetService.ResetStaticCaches();
        Check(ModeHCashBetService.StandingAmount == 0, "custom selector resets on runtime cleanup");
    }

    static void SeedCashStats(long bets, long wins)
    {
        SavesSystem.Cache[BetKey] = "{\"schemaVersion\":3,\"tierBets\":\"0,0,0,0,0," + bets
            + "\",\"tierWins\":\"0,0,0,0,0," + wins + "\"}";
    }

    static void ReloadCashJournal()
    {
        SavesSystem.IsSaving = true;
        ModeHCashBetService.ResetStaticCaches();
        SavesSystem.Cache = new Dictionary<string, object>(SavesSystem.Disk);
        EconomyManager.Money = ((EconomyManager.SaveData)SavesSystem.Disk["EconomyData"]).money;
        SavesSystem.IsSaving = false;
        BossRushSaveFileThrottle.ResetStaticCaches();
    }

    static void LargeCashBets()
    {
        Reset(); SeedCashStats(1000, 460);
        Check(ModeHCashBetService.GetMaximumStandingAmount(100000, 5) == 50000,
            "100000 wallet with x2 return permits exactly 50000 stake");
        ModeHCashBetService.SelectCustomStandingAmount(long.MaxValue, 5);
        string reason; long payout;
        Check(ModeHCashBetService.StandingAmount == 50000
            && ModeHCashBetService.TryReserve("large", 1, 5, 50000, out reason, ModeHCashBetService.BetSideRed)
            && EconomyManager.Money == 50000, "maximum stake deducts principal once, not principal times payout multiplier");
        Check(!ModeHCashBetService.TryReserve("large", 1, 1, 1000, out reason, ModeHCashBetService.BetSideBlue)
            && ModeHCashBetService.Current.betSide == ModeHCashBetService.BetSideRed,
            "relocking or selecting another team cannot replace the reserved direction or debit again");
        string saved = (string)SavesSystem.Disk[BetKey];
        SavesSystem.Disk[BetKey] = saved.Replace("0,0,0,0,0,460", "0,0,0,0,0,990");
        ReloadCashJournal();
        Check(ModeHCashBetService.Current.payoutNumerator == 920 && ModeHCashBetService.Current.payoutDenominator == 460
            && ModeHCashBetService.ResolveAssumedWinPermille(5) == 990,
            "reloaded reservation retains exact quote despite changed calibration");
        Check(ModeHRuntimeModule.FormatPayoutMultiplier(ModeHCashBetService.Current) == "x2.00"
            && ModeHRuntimeModule.FormatPayoutMultiplier(5) == "x0.93",
            "carried-bet display uses the frozen multiplier while new bets show current calibration");
        Check(!ModeHCashBetService.TrySettle("other", 1, true, 0, 0, "", out payout)
            && ModeHCashBetService.TrySettle("large", 1, true, 0, 0, "", out payout)
            && payout == 100000 && EconomyManager.Money == 150000,
            "win uses frozen payout and original match identity");
        Check(!ModeHCashBetService.TrySettle("large", 1, true, 0, 0, "", out payout)
            && EconomyManager.Money == 150000, "frozen payout is delivered at most once");

        Reset(); SeedCashStats(1000, 460);
        ModeHCashBetService.SelectCustomStandingAmount(50000, 5);
        EconomyManager.Money = 90000;
        Check(!ModeHCashBetService.TryReserve("changed", 1, 5, ModeHCashBetService.StandingAmount, out reason)
            && reason == "cash_bet_limit_changed" && EconomyManager.Money == 90000,
            "lock rereads changed wallet instead of silently deducting an outdated maximum");
        Reset(); ModeHCashBetService.SelectCustomStandingAmount(90000, 1);
        Check(!ModeHCashBetService.TryReserve("odds_changed", 1, 5, ModeHCashBetService.StandingAmount, out reason)
            && reason == "cash_bet_limit_changed" && EconomyManager.Money == 100000,
            "lock recomputes limit after payout odds change");

        foreach (long wallet in new[] { 0L, 1L, 2L, 9L, 99L, 16777217L, long.MaxValue / 4, long.MaxValue })
        {
            Reset(); EconomyManager.Money = wallet;
            long maximum = ModeHCashBetService.GetMaximumStandingAmount(wallet, 5);
            Check(maximum >= 0 && maximum <= wallet && ModeHCashBetService.ComputePayout(maximum, 5) <= wallet,
                "stake cap and payout remain within wallet across integer boundaries: " + wallet);
            Check(ModeHCashBetService.AmountAtProgress(maximum, 0) == 0
                && ModeHCashBetService.AmountAtProgress(maximum, 10000) == maximum,
                "normalized slider endpoints preserve exact long amounts: " + wallet);
            long previous = 0; bool monotonic = true;
            for (int step = 1; step <= 10000; step++)
            {
                long current = ModeHCashBetService.AmountAtProgress(maximum, step);
                if (current < previous || current > maximum) monotonic = false;
                previous = current;
            }
            Check(monotonic, "normalized slider cannot wrap or exceed maximum: " + wallet);
            if (wallet == long.MaxValue)
                Check(!ModeHCashBetService.TryReserve("overflow", 1, 5, maximum, out reason)
                    && reason == "cash_bet_overflow" && EconomyManager.Money == wallet,
                    "unrepresentable winning wallet is rejected before debit or journal mutation");
            else if (maximum > 0)
            {
                Check(ModeHCashBetService.TryReserve("boundary", 1, 5, maximum, out reason)
                    && EconomyManager.Money == wallet - maximum, "integer stake debit remains exact: " + wallet);
                Check(ModeHCashBetService.TrySettle("boundary", 1, false, 0, 0, "", out payout)
                    && payout == 0 && EconomyManager.Money == wallet - maximum, "loss never deducts stake a second time");
            }
        }
        Reset(); SeedCashStats(long.MaxValue, long.MaxValue); EconomyManager.Money = long.MaxValue;
        Check(ModeHCashBetService.ResolveAssumedWinPermille(5) == 990
            && ModeHCashBetService.GetMaximumStandingAmount(long.MaxValue, 5) == long.MaxValue,
            "calibrated multiplier below one permits the whole wallet without overflow");
        Check(ModeHCashBetService.TryReserve("whole", 1, 5, long.MaxValue, out reason)
            && EconomyManager.Money == 0
            && ModeHCashBetService.TrySettle("whole", 1, true, 0, 0, "", out payout)
            && EconomyManager.Money == payout && ModeHCashBetService.Current.tierBets[5] == long.MaxValue,
            "whole-long wallet can settle below-one multiplier and saturated stats do not wrap");
        Check(!ModeHCashBetService.TryComputePayout(long.MaxValue, 1, out payout), "impossible payout reports overflow");
        Check(ModeHItemBetEntry.PrizeQuality(new List<ModeHItemBetEntry> {
            new ModeHItemBetEntry { Value = long.MaxValue, Quality = 3 },
            new ModeHItemBetEntry { Value = long.MaxValue, Quality = 8 } }) == 6,
            "large weighted item values retain correct prize quality");
        Check(ModeHRuntimeModule.FormatSignedMoney(long.MinValue) == "-9,223,372,036,854,775,808"
            && ModeHRuntimeModule.FormatSignedMoney(long.MaxValue) == "+9,223,372,036,854,775,807",
            "session and hall-of-fame net formatting supports both signed long endpoints");
        foreach (string field in new[] { "amount", "payout", "charged", "prizeCash", "missingValue", "runNet" })
        {
            Reset(); string corrupt = "{\"schemaVersion\":3,\"" + field + "\":\"92233720368547758080\"}";
            SavesSystem.Cache[BetKey] = corrupt;
            Check(!ModeHCashBetService.TryReserve("bad", 1, 5, 1000, out reason)
                && EconomyManager.Money == 100000 && (string)SavesSystem.Cache[BetKey] == corrupt,
                "declared invalid amount creates write barrier instead of becoming zero: " + field);
        }
    }

    static void SmallBetsAndFrozenRecovery()
    {
        Reset(); EconomyManager.Money = 9;
        long maximum = ModeHCashBetService.GetMaximumStandingAmount(9, 5);
        string reason; long payout;
        Check(maximum == 2 && ModeHCashBetService.TryReserve("small", 1, 5, maximum, out reason)
            && EconomyManager.Money == 7, "small wallet can stake its exact two-cash maximum");
        Check(ModeHCashBetService.TrySettle("small", 1, true, 0, 0, "", out payout)
            && payout == 0 && EconomyManager.Money == 7 && ModeHCashBetService.Current.settlementResult == 1
            && ModeHCashBetService.IsWinningRecord(ModeHCashBetService.Current),
            "winning tiny bet retains original ten-cash rounding without being reported as a loss");
        ReloadCashJournal();
        Check(ModeHCashBetService.IsWinningRecord(ModeHCashBetService.Current)
            && ModeHCashBetService.Current.tierWins[5] == 1,
            "zero-return victory and calibration survive journal recovery");
        Check(ModeHCashBetService.TryReserve("small", 2, 5, 1, out reason)
            && ModeHCashBetService.Current.settlementResult == 0
            && ModeHCashBetService.TrySettle("small", 2, false, 0, 0, "", out payout)
            && ModeHCashBetService.Current.settlementResult == 2
            && !ModeHCashBetService.IsWinningRecord(ModeHCashBetService.Current),
            "next reservation clears the previous outcome and records a real tiny-bet loss");

        for (int schema = 1; schema <= 3; schema++)
        {
            Reset(); EconomyManager.Money = 99500;
            SavesSystem.Cache[BetKey] = "{\"schemaVersion\":" + schema
                + ",\"runId\":\"old\",\"matchIndex\":1,\"odds\":3,\"status\":1,\"amount\":\"500\",\"kind\":0}";
            Check(ModeHCashBetService.Current.payoutNumerator == 0 && ModeHCashBetService.Current.payoutDenominator == 0
                && ModeHCashBetService.TrySettle("old", 1, true, 0, 0, "", out payout)
                && payout == 830 && EconomyManager.Money == 100330,
                "legacy reserved cash bet still settles once without frozen fields, schema=" + schema);
        }

        Reset(); SeedCashStats(1000, 460); Reserve(Add());
        string saved = (string)SavesSystem.Disk[BetKey];
        SavesSystem.Disk[BetKey] = saved.Replace("0,0,0,0,0,460", "0,0,0,0,0,990");
        Restart(); Settle(true);
        Check(ModeHCashBetService.Current.payout == 1000 && ModeHCashBetService.Current.itemSettlement == 1
            && ModeHCashBetService.Current.payoutDenominator == 460,
            "item prize plan after restart also honors the exact reserved payout fraction");

        Reset();
        Check(ModeHCashBetService.TryReserve("capacity", 1, 5, 1000, out reason), "reserve cash before wallet fills");
        EconomyManager.Money = long.MaxValue;
        Check(!ModeHCashBetService.TrySettle("capacity", 1, true, 0, 0, "", out payout)
            && ModeHCashBetService.Current.status == ModeHCashBetService.StatusReserved
            && EconomyManager.Money == long.MaxValue,
            "later wallet growth cannot overflow, lose or mark a blocked payout complete");
        EconomyManager.Money -= 10000;
        Check(ModeHCashBetService.TrySettle("capacity", 1, true, 0, 0, "", out payout)
            && payout == 3060 && EconomyManager.Money == long.MaxValue - 6940,
            "freeing wallet capacity completes the original exact payout");

        foreach (string fields in new[] { "\"payoutNumerator\":920", "\"payoutNumerator\":920,\"payoutDenominator\":0",
            "\"payoutNumerator\":\"bad\",\"payoutDenominator\":460", "\"settlementResult\":99" })
        {
            Reset(); string corrupt = "{\"schemaVersion\":3," + fields + "}";
            SavesSystem.Cache[BetKey] = corrupt;
            Check(!ModeHCashBetService.TryReserve("bad", 1, 5, 1000, out reason)
                && EconomyManager.Money == 100000 && (string)SavesSystem.Cache[BetKey] == corrupt,
                "malformed declared quote or outcome keeps the journal write barrier");
        }
    }

    static void OfficialCashPrecheckOverflow()
    {
        Reset(); SeedCashStats(long.MaxValue, long.MaxValue);
        EconomyManager.Money = long.MaxValue; EconomyManager.Cash = 1;
        string reason; long payout;
        Check(!EconomyManager.Pay(new Cost(1000), true, false) && EconomyManager.Money == long.MaxValue,
            "official cost precheck overflows wallet plus cash even with cash payment disabled");
        int calls = EconomyManager.PayCalls;
        Check(ModeHCashBetService.TryReserve("cash-overflow", 1, 5, long.MaxValue, out reason)
            && EconomyManager.Money == 0 && EconomyManager.Cash == 1 && EconomyManager.PayCalls == calls,
            "whole-wallet stake bypasses overflowing official precheck without consuming cash items");
        Check(ModeHCashBetService.TrySettle("cash-overflow", 1, true, 0, 0, "", out payout)
            && EconomyManager.Money == payout && EconomyManager.Cash == 1,
            "overflow-boundary stake settles its frozen return once");
        Check(!ModeHCashBetService.TrySettle("cash-overflow", 1, true, 0, 0, "", out payout),
            "overflow-boundary payment cannot settle twice");

        Reset(); EconomyManager.Cash = 100;
        Check(ModeHCashBetService.TryReserve("normal-cash", 1, 5, 1000, out reason)
            && EconomyManager.Money == 99000 && EconomyManager.Cash == 100 && EconomyManager.PayCalls == 1,
            "ordinary bank debit retains official payment notifications and leaves cash items alone");
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
            DailyRollover(); WinAndRetries(); CrashBoundaries(); LossAndIdentity(); NestedStakeOwnership(); WarehouseNotifications(); CashRestoreReadiness(); CashBetSurvivesSpectatorExit(); StartedBetForfeit(); AbandonBetResolution(); BetSideAndSeasonNet(); CustomCashAmount(); LargeCashBets(); SmallBetsAndFrozenRecovery(); OfficialCashPrecheckOverflow(); Schema();
            Console.WriteLine("SaveFailureRecovery: PASS " + checks + " assertions (real services, stores and coordinators; in-memory host)");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
