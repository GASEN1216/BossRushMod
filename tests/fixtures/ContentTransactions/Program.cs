using System;
using System.Reflection;
using BossRush;
using Duckov.Economy;
using ItemStatsSystem;
using Saves;

class Program
{
    static int checks;
    static void Check(bool condition, string text)
    {
        if (!condition) throw new Exception("FAIL " + text);
        checks++; Console.WriteLine("PASS " + text);
    }
    static void SetPrivate(object instance, string name, object value)
    {
        var type = instance as Type ?? instance.GetType();
        var field = type.GetField(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null && instance is Type)
        {
            // 2026-09-06 (D-2): facade state now lives on the shared BossRushSlotJsonStore instance
            // held in the facade's private static "_store"; resolve through it.
            var store = type.GetField("_store", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            if (store != null) { SetPrivate(store, name, value); return; }
        }
        field.SetValue(instance is Type ? null : instance, value);
    }
    static void Reset()
    {
        CampaignPersistence.ResetStaticCaches(); CampaignSaveCoordinator.NotifySlotChanged();
        CampaignProgressService.ResetStaticCaches(); DailyReportSaveCoordinator.ResetStaticCaches();
        PetNestSaveCoordinator.ResetStaticCaches(); PetNestMuseumStats.ResetStaticCaches();
        BossRushSaveFileThrottle.ResetStaticCaches(); SavesSystem.Reset();
        EconomyManager.Instance = new EconomyManager(); EconomyManager.Money = 100000;
        EconomyManager.Adds = 0; EconomyManager.RejectAdd = EconomyManager.RejectPay = false;
        SavesSystem.Save("EconomyData", (EconomyManager.SaveData)EconomyManager.Instance.GenerateSaveData());
        LevelManager.Instance.IsBaseLevel = true; UnityEngine.Time.frameCount++;
        CharacterMainControl.Main = new CharacterMainControl { CharacterItem = new Item { Inventory = new Inventory() } };
        PlayerStorage.Inventory = new Inventory(); PlayerStorage.Loading = false;
        BossRushAchievementManager.Reset();
        PetNestExpeditionService.ResetValidationRewardBackend();
        ShowcaseService.ResetStaticCaches();
        ItemUtilities.Delivered.Clear(); RelicEggConfig.FailStamp = false;
        ItemAssetsCollection.FailInstantiate = false; UnityEngine.Random.value = 0;
        ItemAssetsCollection.MissingPrefabId = ItemAssetsCollection.InstantiateCalls = 0;
        PetNestBaseIdleSpawner.DeployNotifications = PetNestCompanionRuntime.Cleanups = 0;
        PetNestUIPages.DepartCardRequests = 0;
        BackMountainBossMorphService.Reject = BackMountainBossMorphService.Throw = false; BackMountainBossMorphService.Started = 0;
        DailyReportPersistence.Current = new DailyReportData { BountyCompleted = true, BountyKindId = "bounty", BountyDayIndex = 1 };
        DailyReportPersistence.ResetStaticCaches();
    }
    static long DiskMoney { get { return ((EconomyManager.SaveData)SavesSystem.Disk["EconomyData"]).money; } }
    static void PrepareCampaign()
    {
        SavesSystem.Save(CampaignTuning.ProgressSaveKey, UnityEngine.JsonUtility.ToJson(new CampaignSaveData
        {
            schemaVersion = 1,
            chapters = new[] { new CampaignChapterRecord { chapterId = "ch1", state = 3 } },
            grantedTokens = new string[0], unlockedClues = new string[0]
        }));
    }
    static bool CampaignClaimedOnDisk()
    {
        object raw;
        return SavesSystem.Disk.TryGetValue(CampaignTuning.ProgressSaveKey, out raw)
            && UnityEngine.JsonUtility.FromJson<CampaignSaveData>((string)raw).chapters[0].state == 4;
    }
    static void CampaignCash()
    {
        Reset();
        Check(CampaignProgressService.TryAcceptContract("ch1"), "campaign accepts available contract");
        SetPrivate(typeof(CampaignPersistence), "_storeFaulted", true);
        Check(!CampaignProgressService.NotifyObjectivesSatisfied("ch1"), "terminal event reports failed enqueue");
        CampaignObjectiveTracker.ResetSession();
        Check(!CampaignProgressService.TryAbandonContract(), "earned pending completion cannot be abandoned");
        SetPrivate(typeof(CampaignPersistence), "_storeFaulted", false);
        CampaignProgressService.RetryPendingObjectives(1f);
        Check(CampaignProgressService.GetState("ch1") == CampaignChapterState.ReadyToDeliver,
            "terminal completion retries after session cleanup without a second kill");
        Check(!CampaignProgressService.TryAbandonContract(), "ready completion cannot be discarded by stale action");

        Reset();
        CampaignProgressService.TryAcceptContract("ch1");
        SetPrivate(typeof(CampaignPersistence), "_storeFaulted", true);
        CampaignProgressService.NotifyObjectivesSatisfied("ch1");
        SetPrivate(typeof(CampaignPersistence), "_storeFaulted", false);
        CampaignProgressService.NotifySlotChanged();
        CampaignProgressService.RetryPendingObjectives(1f);
        Check(CampaignProgressService.GetState("ch1") == CampaignChapterState.ContractActive,
            "pending completion cannot follow a slot change");

        Reset(); PrepareCampaign();
        Check(CampaignProgressService.TryDeliver("ch1"), "campaign accepts delivery");
        Check(CampaignClaimedOnDisk() && DiskMoney == 104000 && SavesSystem.Writes == 1,
            "campaign completed and credited balance share physical snapshot");
        Check(!CampaignProgressService.TryDeliver("ch1") && EconomyManager.Adds == 1, "campaign delivery pays once");

        Reset(); PrepareCampaign(); EconomyManager.Instance = null;
        Check(!CampaignProgressService.TryDeliver("ch1") && EconomyManager.Money == 100000,
            "campaign absent economy leaves delivery retryable");
        EconomyManager.Instance = new EconomyManager(); SavesSystem.FailKey = "EconomyData";
        Check(CampaignProgressService.TryDeliver("ch1") && !CampaignClaimedOnDisk()
            && CampaignSaveCoordinator.HasDeferredFlush, "campaign cash collection failure retains accepted reward obligation");
        EconomyManager.Money -= 123; UnityEngine.Time.frameCount++; CampaignSaveCoordinator.Tick();
        Check(CampaignClaimedOnDisk() && DiskMoney == 103877 && EconomyManager.Adds == 1,
            "campaign retry recaptures current balance without regrant");

        Reset(); PrepareCampaign(); SavesSystem.FailPhysical = 1;
        Check(CampaignProgressService.TryDeliver("ch1") && !CampaignPersistence.HasPendingWrite
            && CampaignSaveCoordinator.HasDeferredFlush && SavesSystem.Writes == 0,
            "campaign file failure retains obligation after typed pending consumed");
        EconomyManager.Money += 321; UnityEngine.Time.frameCount++; CampaignSaveCoordinator.Tick();
        Check(CampaignClaimedOnDisk() && DiskMoney == 104321 && EconomyManager.Adds == 1,
            "campaign physical retry recaptures cash and pays once");

        Reset(); PrepareCampaign(); BossRushSaveFileThrottle.TryBeginSaveFile(false);
        Check(CampaignProgressService.TryDeliver("ch1") && SavesSystem.Writes == 0,
            "campaign same frame contention defers physical write");
        EconomyManager.Money -= 7; UnityEngine.Time.frameCount++; CampaignSaveCoordinator.Tick();
        Check(CampaignClaimedOnDisk() && DiskMoney == 103993, "campaign throttled retry keeps latest cash");

        Reset(); PrepareCampaign(); LevelManager.Instance.IsBaseLevel = false;
        CampaignPersistence.EnsureSubscribed(); CampaignProgressService.TryDeliver("ch1");
        SavesSystem.Collect(); SavesSystem.SaveFile(false);
        Check(CampaignClaimedOnDisk() && DiskMoney == 104000, "official collection includes campaign cash before claimed key");
        CampaignSaveCoordinator.NotifySlotChanged(); CampaignPersistence.ResetStaticCaches();
        SavesSystem.CurrentSlot = 1; SavesSystem.Cache.Clear(); SavesSystem.Writes = 0;
        LevelManager.Instance.IsBaseLevel = true; CampaignSaveCoordinator.RequestFlush();
        Check(SavesSystem.Writes == 0 && !SavesSystem.Cache.ContainsKey("EconomyData"), "campaign cash obligation does not cross slot reset");

        Reset(); PrepareCampaign(); var loaded = CampaignPersistence.Current;
        SetPrivate(typeof(CampaignPersistence), "_storeFaulted", true);
        Check(!CampaignProgressService.TryDeliver("ch1") && EconomyManager.Money == 100000,
            "campaign rejected candidate refunds cash and leaves delivery retryable");
        SetPrivate(typeof(CampaignPersistence), "_storeFaulted", false);
        Check(CampaignProgressService.TryDeliver("ch1") && DiskMoney == 104000,
            "campaign refunded candidate retry commits exactly one net reward");

        Reset(); PrepareCampaign(); loaded = CampaignPersistence.Current;
        SetPrivate(typeof(CampaignPersistence), "_storeFaulted", true); EconomyManager.RejectPay = true;
        Check(!CampaignProgressService.TryDeliver("ch1") && EconomyManager.Money == 104000,
            "campaign double failure retains paid latch");
        SetPrivate(typeof(CampaignPersistence), "_storeFaulted", false); EconomyManager.RejectPay = false;
        Check(CampaignProgressService.TryDeliver("ch1") && EconomyManager.Adds == 1 && DiskMoney == 104000,
            "campaign paid latch retry still captures balance without granting twice");
    }
    static void DailyCash()
    {
        Reset(); DailyReportService.TryRedeliverPendingBountyReward();
        Check((bool)SavesSystem.Disk["BountyClaimed"] && DiskMoney == 100700, "daily claimed and cash share physical snapshot");
        DailyReportService.TryRedeliverPendingBountyReward();
        Check(EconomyManager.Adds == 1 && !DailyReportStatsCollector.Suppressed, "daily reward is once and stats suppression exits");

        Reset(); EconomyManager.Instance = null; DailyReportService.TryRedeliverPendingBountyReward();
        Check(!DailyReportPersistence.Current.BountyRewardClaimed && EconomyManager.Adds == 0, "daily unavailable economy keeps reward unclaimed");
        EconomyManager.Instance = new EconomyManager(); SavesSystem.FailKey = "EconomyData";
        DailyReportService.TryRedeliverPendingBountyReward();
        Check(DailyReportSaveCoordinator.HasDeferredFlush && SavesSystem.Writes == 0 && DailyReportPersistence.HasPendingWrite,
            "daily cash collection failure retains pending claim and balance obligation");
        EconomyManager.Money -= 44; UnityEngine.Time.frameCount++; DailyReportSaveCoordinator.Tick();
        DailyReportService.TryRedeliverPendingBountyReward();
        Check((bool)SavesSystem.Disk["BountyClaimed"] && DiskMoney == 100656 && EconomyManager.Adds == 1,
            "daily collection retry captures current balance without regrant");

        Reset(); SavesSystem.FailPhysical = 1; DailyReportService.TryRedeliverPendingBountyReward();
        Check(!DailyReportPersistence.HasPendingWrite && DailyReportSaveCoordinator.HasDeferredFlush, "daily file failure persists after typed pending cleared");
        EconomyManager.Money += 5; UnityEngine.Time.frameCount++; DailyReportSaveCoordinator.Tick();
        DailyReportService.TryRedeliverPendingBountyReward();
        Check(DiskMoney == 100705 && EconomyManager.Adds == 1, "daily file retry recaptures money without regrant");

        Reset(); BossRushSaveFileThrottle.TryBeginSaveFile(false); DailyReportService.TryRedeliverPendingBountyReward();
        EconomyManager.Money -= 9; UnityEngine.Time.frameCount++; DailyReportSaveCoordinator.Tick();
        Check(DiskMoney == 100691 && EconomyManager.Adds == 1, "daily same frame throttle retains cash obligation");

        Reset(); LevelManager.Instance.IsBaseLevel = false; DailyReportService.TryRedeliverPendingBountyReward();
        DailyReportPersistence.Collect(); SavesSystem.SaveFile(false);
        Check((bool)SavesSystem.Disk["BountyClaimed"] && DiskMoney == 100700, "official collection includes daily cash before claim key");
        DailyReportSaveCoordinator.NotifySlotChanged(); DailyReportPersistence.ResetStaticCaches();
        SavesSystem.CurrentSlot = 1; SavesSystem.Cache.Clear(); SavesSystem.Writes = 0;
        LevelManager.Instance.IsBaseLevel = true; DailyReportSaveCoordinator.RequestFlush();
        Check(SavesSystem.Writes == 0 && !SavesSystem.Cache.ContainsKey("EconomyData"), "daily obligation resets with slot");
    }
    static void PrepareNest()
    {
        var bundle = PetNestCodec.CreateDefaultBundle();
        bundle.nest.soulLedger.Add(new PetNestSoulLedgerEntry { lineageKey = "test", souls = PetNestTuning.SoulsPerCondensedEgg });
        SavesSystem.Save(PetNestTuning.BundleStorageKey, PetNestCodec.EncodeBundle(bundle));
    }
    static PetNestBundleData DiskNest()
    {
        return PetNestCodec.DecodeBundle(BossRushJsonParser.ParseOrNull((string)SavesSystem.Disk[PetNestTuning.BundleStorageKey]));
    }
    static void CheckPetBundle(PetNestBundleData bundle, string message)
    {
        Check(bundle.nest.pets.Count == 1 && bundle.museum.lineages.Count == 1
            && bundle.museum.lineages[0].hatched == 1 && bundle.museum.lineages[0].shinyHatched == 1
            && bundle.museum.lineages[0].unlocked, message);
    }
    static void Condense()
    {
        PetNestHatchResult result; string error;
        Reset(); PrepareNest();
        Check(PetNestHatchService.TryCondenseAndHatch("test", out result, out error), "condense accepts one candidate");
        var disk = DiskNest();
        CheckPetBundle(disk, "condense first physical snapshot contains pet and all hatch statistics");
        Check(disk.nest.soulLedger[0].souls == 0 && disk.generation == 1 && SavesSystem.Writes == 1,
            "condense spends souls and inserts pet in exactly one Bundle generation and write");
        Check(!PetNestHatchService.TryCondenseAndHatch("test", out result, out error)
            && error == "souls_insufficient" && SavesSystem.Writes == 1, "insufficient souls cannot mutate bundle");

        Reset(); PrepareNest(); SetPrivate(PetNestPersistence.Bundle, "_storeFaulted", true);
        Check(!PetNestHatchService.TryCondenseAndHatch("test", out result, out error)
            && PetNestService.GetSouls("test") == 240 && PetNestService.PetCount == 0,
            "condense rejected writable preflight leaves souls and nest unchanged");

        Reset(); PrepareNest(); PetNestPersistence.BeginTransaction(out error);
        PetNestPersistenceAccess.Museum.lineages.Add(new PetNestLineageStats { lineageKey = "test", hatched = 1 });
        PetNestPersistence.Nest.Current.soulLedger[0].souls = 0;
        SetPrivate(PetNestPersistence.Bundle, "_storeFaulted", true);
        Check(!PetNestPersistence.CommitTransaction(out error) && PetNestService.GetSouls("test") == 240
            && PetNestPersistenceAccess.Museum.lineages.Count == 0, "actual Bundle Store rejection discards mutated soul and museum candidate");

        Reset(); PrepareNest(); SavesSystem.FailPhysical = 1;
        Check(PetNestHatchService.TryCondenseAndHatch("test", out result, out error) && SavesSystem.Writes == 0,
            "condense accepted bundle survives physical failure without partial disk state");
        UnityEngine.Time.frameCount++; PetNestSaveCoordinator.Tick();
        CheckPetBundle(DiskNest(), "condense retry persists pet and statistics once");
        Check(DiskNest().nest.soulLedger[0].souls == 0 && BossRushAchievementManager.Unlocked.Contains("petnest_first_hatch"),
            "condense retry settles soul cost and hatch achievement");
    }
    static void OfficialStickySaving()
    {
        foreach (bool daily in new[] { false, true })
        {
            Reset(); if (!daily) PrepareCampaign();
            SavesSystem.FailPhysical = 1; SavesSystem.StickSavingOnFailure = true;
            if (daily) DailyReportService.TryRedeliverPendingBountyReward();
            else CampaignProgressService.TryDeliver("ch1");
            UnityEngine.Time.frameCount++;
            if (daily) { DailyReportSaveCoordinator.Tick(); DailyReportService.TryRedeliverPendingBountyReward(); }
            else { CampaignSaveCoordinator.Tick(); CampaignProgressService.TryDeliver("ch1"); }
            Check(SavesSystem.IsSaving && SavesSystem.Writes == 0 && EconomyManager.Adds == 1
                && (daily ? DailyReportSaveCoordinator.HasDeferredFlush : CampaignSaveCoordinator.HasDeferredFlush),
                (daily ? "daily" : "campaign") + " official sticky IsSaving preserves obligation without forcing write or regrant");
            // Simulate host recovery only. Production deliberately does not alter the official latch.
            SavesSystem.IsSaving = false; UnityEngine.Time.frameCount++;
            if (daily) DailyReportSaveCoordinator.Tick(); else CampaignSaveCoordinator.Tick();
            Check(DiskMoney == (daily ? 100700 : 104000) && EconomyManager.Adds == 1,
                (daily ? "daily" : "campaign") + " retained obligation settles when host saving state recovers");
        }
    }
    static Item Egg()
    {
        var egg = new Item(); PlayerStorage.Inventory.AddAt(egg, 5); return egg;
    }
    static void Hatch()
    {
        PetNestHatchResult result; string error;
        Reset(); PrepareNest(); var egg = Egg();
        Check(PetNestHatchService.TryHatchEgg(egg, out result, out error), "egg hatching succeeds");
        Check(egg.Destroyed && (int)SavesSystem.Disk["PlayerStorage"] == 0, "egg removal shares physical snapshot with hatch");
        CheckPetBundle(DiskNest(), "egg pet and hatch statistics commit together");

        Reset(); PrepareNest(); egg = Egg(); SetPrivate(PetNestPersistence.Bundle, "_storeFaulted", true);
        Check(!PetNestHatchService.TryHatchEgg(egg, out result, out error)
            && ReferenceEquals(PlayerStorage.Inventory.Items[5], egg) && !egg.Destroyed && PetNestService.PetCount == 0,
            "rejected egg candidate restores exact egg and original slot");

        Reset(); PrepareNest(); egg = Egg(); CharacterMainControl.Main.CharacterItem.FailSaveOnce = true;
        Check(!PetNestHatchService.TryHatchEgg(egg, out result, out error) && egg.Destroyed
            && PetNestSaveCoordinator.HasDeferredFlush && SavesSystem.Writes == 0, "asset collection failure retains egg consumption obligation");
        CheckPetBundle(PetNestPersistence.Bundle.Current, "deferred hatch already includes statistics in accepted Bundle");
        Check(!BossRushAchievementManager.Unlocked.Contains("petnest_first_hatch"), "failed physical hatch does not publish achievement early");
        PetNestSaveCoordinator.Tick();
        CheckPetBundle(DiskNest(), "asset retry saves pet and hatch statistics once");
        Check((int)SavesSystem.Disk["PlayerStorage"] == 0 && BossRushAchievementManager.Unlocked.Contains("petnest_first_hatch"),
            "asset retry settles absent egg and hatch achievement");

        Reset(); PrepareNest(); egg = Egg(); SavesSystem.FailPhysical = 1;
        Check(!PetNestHatchService.TryHatchEgg(egg, out result, out error) && !PetNestPersistence.HasAnyPendingWrite,
            "egg physical failure occurs after typed pending consumption");
        PlayerStorage.Inventory.AddAt(new Item(), 8); UnityEngine.Time.frameCount++; PetNestSaveCoordinator.Tick();
        Check((int)SavesSystem.Disk["PlayerStorage"] == 1, "physical hatch retry recaptures current inventory");
        CheckPetBundle(DiskNest(), "physical hatch retry retains one hatch statistic");

        Reset(); PrepareNest(); egg = Egg(); CharacterMainControl.Main.CharacterItem.FailSaveOnce = true;
        PetNestHatchService.TryHatchEgg(egg, out result, out error); PetNestPersistence.EnsureSubscribed();
        SavesSystem.Collect(); SavesSystem.SaveFile(false); PetNestSaveCoordinator.Tick();
        Check(BossRushAchievementManager.Unlocked.Contains("petnest_first_hatch"),
            "official collection consuming typed hatch pending does not discard asset obligation or achievement retry");
    }
    static void FinishOfficialUse(RaidMealUsageBehavior behavior, Item item)
    {
        // UsageUtilities.Use rechecks each behavior; CA_UseItem.OnFinish decrements regardless.
        if (behavior.CanBeUsed(item, null)) behavior.Use(item, null);
        item.StackCount--;
    }
    static void Meals()
    {
        var behavior = new RaidMealUsageBehavior();
        foreach (int typeId in new[] { BossRushItemIds.DragonFruit, BossRushItemIds.EmberChili, BossRushItemIds.PhantomMushroom })
        foreach (int count in new[] { 1, 20 })
        {
            Reset(); var item = new Item { TypeID = typeId, StackCount = count };
            Check(behavior.CanBeUsed(item, null), "harvest starts at stack " + count);
            BackMountainBossMorphService.Reject = true; FinishOfficialUse(behavior, item);
            Check(item.StackCount == count && BackMountainBossMorphService.Started == 0,
                "resource failure preserves harvest at stack " + count);
            BackMountainBossMorphService.Reject = false; FinishOfficialUse(behavior, item);
            Check(item.StackCount == count - 1 && BackMountainBossMorphService.Started == 1,
                "morph retry starts once and official framework consumes one at stack " + count);
        }
        Reset(); var failed = new Item { TypeID = 500065 }; BackMountainBossMorphService.Reject = true;
        FinishOfficialUse(behavior, failed);
        Check(failed.StackCount == 1, "morph refusal is compensated");
        BackMountainBossMorphService.Reject = false; BackMountainBossMorphService.Throw = true; FinishOfficialUse(behavior, failed);
        Check(failed.StackCount == 1, "morph exception is compensated");
        LevelManager.Instance.IsBaseLevel = false;
        Check(behavior.CanBeUsed(failed, null), "harvest can be eaten outside base");
    }

    static PetNestExpeditionRecord PendingEgg(string id, string petId)
    {
        return new PetNestExpeditionRecord
        {
            id = id, petId = petId, settled = true, cashGranted = true,
            outcomeLootTypeIds = new System.Collections.Generic.List<int> { RelicEggConfig.TYPE_ID },
            outcomeLootCounts = new System.Collections.Generic.List<int> { 1 }
        };
    }
    static void PrepareExpeditionDebt(bool legacy, bool withPet)
    {
        PrepareNest();
        var bundle = PetNestPersistence.Bundle.Current;
        if (withPet) bundle.nest.pets.Add(new PetNestPetRecord { id = "original", lineageKey = "test", state = (int)PetNestPetState.InNest });
        var record = PendingEgg("debt", "original");
        if (!legacy) record.petLineageKey = "test";
        bundle.expedition.records.Add(record);
    }
    static void ExpeditionEggIdentity()
    {
        string error;
        Reset(); PrepareExpeditionDebt(true, true);
        Check(PetNestService.TryReleasePet("original", out error), "legacy cub can be released while reward is pending");
        var record = PetNestExpeditionService.Records[0];
        Check(record.petLineageKey == "test" && PetNestService.TryGetPet("original") == null,
            "release candidate freezes legacy reward lineage before removing original cub");
        var reloaded = PetNestCodec.DecodeBundle(BossRushJsonParser.ParseOrNull(PetNestCodec.EncodeBundle(PetNestPersistence.Bundle.Current)));
        Check(reloaded.expedition.records[0].petLineageKey == "test", "reward identity survives full bundle codec round trip");
        Check(PetNestExpeditionService.TryGrantPendingRewards() == 1
            && ItemUtilities.Delivered.Count == 1 && ItemUtilities.Delivered[0].Lineage == "test",
            "released cub pending egg still delivers original lineage");

        Reset(); PrepareExpeditionDebt(true, true);
        PetNestExpeditionRecord next;
        Check(PetNestExpeditionService.TryDepart("original", PetNestTuning.DestinationStormSea,
            PetNestRiskTier.Desperate, out next, out error) && next.petLineageKey == "test",
            "new expedition freezes lineage at departure");
        next.returnTicks = 0; next.deathRate = 1;
        Check(PetNestExpeditionService.TrySettle(next, out error)
            && PetNestService.TryGetPet("original") == null, "later expedition can kill original cub");
        PetNestExpeditionService.TryGrantPendingRewards();
        Check(ItemUtilities.Delivered.Count == 1 && ItemUtilities.Delivered[0].Lineage == "test",
            "earlier reward survives original cub death on later expedition");

        Reset(); PrepareExpeditionDebt(true, false);
        PetNestPersistence.Bundle.Current.nest.pets.Add(new PetNestPetRecord { id = "other", lineageKey = "test" });
        Check(PetNestExpeditionService.TryGrantPendingRewards() == 0 && ItemUtilities.Delivered.Count == 0,
            "unknown original lineage never guesses another cub identity");
        record = PetNestExpeditionService.Records[0];
        Check(!record.rewardsGranted && record.grantedLootUnits == 0 && record.rewardGrantAttempts == 1,
            "missing legacy identity retains reward debt and cursor");

        Reset(); PrepareExpeditionDebt(false, false); RelicEggConfig.FailStamp = true;
        Check(PetNestExpeditionService.TryGrantPendingRewards() == 0 && ItemUtilities.Delivered.Count == 0,
            "temporary lineage stamp failure retains unpaid egg");
        RelicEggConfig.FailStamp = false; PetNestExpeditionService.ResetValidationRewardBackend();
        Check(PetNestExpeditionService.TryGrantPendingRewards() == 1 && ItemUtilities.Delivered.Count == 1,
            "stamp recovery delivers pending egg exactly once");
        PetNestExpeditionService.ResetValidationRewardBackend(); PetNestExpeditionService.TryGrantPendingRewards();
        Check(ItemUtilities.Delivered.Count == 1, "reward cursor prevents repeated delivery after recovery");
    }
    static void PrepareShowcase()
    {
        SavesSystem.Save(BackMountainConfig.ShowcaseSaveKey, UnityEngine.JsonUtility.ToJson(
            new ShowcaseSaveData { schemaVersion = 1, displayedTypeIds = new[] { 100, 101, 102, 103, 104, 105, 106, 107 } }));
    }
    static void ShowcaseSnapshot()
    {
        // 2026-09-22：登记簿退役，陈列由官方柜实摆快照整体覆盖；事务纪律不变（写失败恢复、回读失败还原官方缓存、存档忙拒绝）。
        Reset(); PrepareShowcase();
        Check(ShowcaseService.SourceVersion == 1 && ShowcaseService.DisplayedCount == 8
            && Math.Abs(ShowcaseService.CalculateBonus() - .09f) < .0001f, "legacy ledger without sourceVersion loads with unchanged formula");
        Check(ShowcaseService.ApplyDisplaySnapshot(new[] { 200, 100 }, true)
            && ShowcaseService.SourceVersion == 2 && ShowcaseService.DisplayedCount == 2 && ShowcaseService.GetDisplayed()[0] == 200
            && Math.Abs(ShowcaseService.CalculateBonus() - .025f) < .0001f,
            "official snapshot replaces the legacy ledger at base and sorts by quality");
        Check(ShowcaseService.ApplyDisplaySnapshot(new[] { 200, 200, 100, 100, 300, 301, 302, 303, 304, 305, 306 }, true)
            && ShowcaseService.DisplayedCount == 8 && ShowcaseService.GetDisplayed()[0] >= 200
            && Math.Abs(ShowcaseService.CalculateBonus() - .21f) < .0001f, "snapshot dedups and keeps the best eight");
        Reset(); PrepareShowcase(); SavesSystem.FailKey = BackMountainConfig.ShowcaseSaveKey;
        Check(!ShowcaseService.ApplyDisplaySnapshot(new[] { 200 }, true)
            && ShowcaseService.GetDisplayed()[0] == 100 && ShowcaseService.DisplayedCount == 8 && ShowcaseService.SourceVersion == 1,
            "snapshot save failure preserves the original ledger and source version");
        ShowcaseService.NotifySlotChanged();
        Check(ShowcaseService.GetDisplayed()[0] == 100 && ShowcaseService.DisplayedCount == 8, "failed snapshot reload keeps original persisted record");
        SavesSystem.FailReadAfterSaveKey = BackMountainConfig.ShowcaseSaveKey;
        Check(!ShowcaseService.ApplyDisplaySnapshot(new[] { 200 }, true)
            && ShowcaseService.GetDisplayed()[0] == 100, "snapshot readback failure restores in-memory original");
        ShowcaseService.NotifySlotChanged();
        Check(ShowcaseService.GetDisplayed()[0] == 100 && ShowcaseService.DisplayedCount == 8, "snapshot readback failure restores official save cache before reload");
        SavesSystem.IsSaving = true;
        Check(!ShowcaseService.ApplyDisplaySnapshot(new[] { 200 }, true) && ShowcaseService.GetDisplayed()[0] == 100,
            "snapshot refusal while saving preserves old record");
        SavesSystem.IsSaving = false;
        Check(ShowcaseService.ApplyDisplaySnapshot(new[] { 200 }, true) && ShowcaseService.DisplayedCount == 1, "snapshot applies once saving is done");
    }
    static void CampaignGuideLifecycle()
    {
        Reset(); PrepareCampaign();
        CampaignSaveData old = CampaignPersistence.Current;
        Check(old.acceptedGuides.Length == 0 && old.experiencedGuides.Length == 0 && old.completedGuides.Length == 0,
            "old campaign save defaults optional guide fields without changing chapter");
        const string id = CampaignGuideTable.ModeH;
        Check(!CampaignPersistence.TryAdvanceGuide(id, 2) && !CampaignPersistence.TryAdvanceGuide(id, 3),
            "unaccepted guide cannot experience or deliver");
        Check(!CampaignPersistence.TryAdvanceGuide("unknown-guide", 1), "unknown guide id rejected");
        Check(CampaignPersistence.TryAdvanceGuide(id, 1), "guide acceptance persisted in campaign store");
        Check(!CampaignGuideTable.IsCompleted(id) && !CampaignGuideTable.IsExperienced(id), "acceptance does not complete objective");
        Check(!CampaignPersistence.TryAdvanceGuide(id, 3), "cannot deliver before objective");
        SetPrivate(typeof(CampaignPersistence), "_storeFaulted", true);
        Check(!CampaignPersistence.TryAdvanceGuide(id, 2) && !CampaignGuideTable.IsExperienced(id), "failed objective write preserves live facts");
        SetPrivate(typeof(CampaignPersistence), "_storeFaulted", false);
        Check(CampaignPersistence.TryAdvanceGuide(id, 2) && CampaignGuideTable.IsExperienced(id)
            && !CampaignGuideTable.IsCompleted(id), "trial ready remains separate from Jeff delivery");
        Check(CampaignPersistence.TryAdvanceGuide(id, 2) && CampaignPersistence.Current.experiencedGuides.Length == 1,
            "repeated observation is idempotent");
        CampaignSaveData copy = CampaignProgressService.CloneSaveData(CampaignPersistence.Current);
        copy.acceptedGuides[0] = "changed";
        Check(CampaignGuideTable.IsAccepted(id), "chapter transaction clone cannot mutate guide source arrays");
        Check(CampaignPersistence.TryAdvanceGuide(id, 3) && CampaignGuideTable.IsCompleted(id), "Jeff delivery completes guide");
        Check(CampaignPersistence.FlushPending(), "guide state flush succeeds");
        CampaignPersistence.ResetStaticCaches();
        Check(CampaignGuideTable.IsCompleted(id) && CampaignGuideTable.IsAccepted(id) && CampaignGuideTable.IsExperienced(id),
            "accepted objective and delivered survive save reload");
        int quest = CampaignGuideTable.FirstQuestId;
        foreach (CampaignGuideTable.Definition def in CampaignGuideTable.Definitions)
            Check(def.QuestId == quest++ && !string.IsNullOrEmpty(def.HintCN) && !string.IsNullOrEmpty(def.HintEN),
                "guide has unique contiguous quest identity and both language instructions " + def.Id);
        Check(quest - 1 == CampaignGuideTable.LastQuestId, "guide id ledger reaches final task");
    }

    static void Main()
    {
        CampaignGuideLifecycle(); CampaignCash(); DailyCash(); OfficialStickySaving(); Condense(); Hatch(); PetNestAchievements(); Meals(); ExpeditionEggIdentity(); ShowcaseSnapshot();
        ManualChromaAndDurations();
        PityGuarantees();
        PetNestLifecycleRepairs();
        Console.WriteLine("ContentTransactions: " + checks + " assertions passed");
    }

    /// 剥掉 TMP 富文本标签，只留可见文字。
    static string StripRichText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var sb = new System.Text.StringBuilder(text.Length);
        bool inTag = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    // 2026-09-22 owner：「开了十几个蛋都没有炫彩/异色，弄个保底」。走生产的凝蛋事务，
    // 随机数钉在不会自然命中的 0.99，只看保底本身。
    static void PityGuarantees()
    {
        PetNestHatchResult result; string error;
        Reset(); UnityEngine.Random.value = .99f;
        var bundle = PetNestCodec.CreateDefaultBundle();
        bundle.nest.soulLedger.Add(new PetNestSoulLedgerEntry { lineageKey = "test", souls = PetNestTuning.SoulsPerCondensedEgg * 20 });
        SavesSystem.Save(PetNestTuning.BundleStorageKey, PetNestCodec.EncodeBundle(bundle));
        for (int i = 1; i < PetNestTuning.ChromaPityHatches; i++)
        {
            Check(PetNestHatchService.TryCondenseAndHatch("test", out result, out error)
                && !PetNestChroma.HasChroma(result.Pet) && !result.Pet.shiny, "unlucky hatch " + i + " has no natural chroma");
        }
        Check(PetNestService.Nest.hatchesSinceChroma == PetNestTuning.ChromaPityHatches - 1,
            "pity counter counts every committed plain hatch");
        Check(PetNestHatchService.TryCondenseAndHatch("test", out result, out error) && PetNestChroma.HasChroma(result.Pet),
            "the tenth hatch without chroma is guaranteed chroma");
        // 同一帧里的连续提交会被写盘节流合并，这里按权威内存 + 编解码往返核对（不读节流中的磁盘副本）
        var roundTrip = PetNestCodec.DecodeNest(BossRushJsonParser.ParseOrNull(PetNestCodec.EncodeNest(PetNestService.Nest)));
        Check(PetNestService.Nest.hatchesSinceChroma == 0 && roundTrip.hatchesSinceChroma == 0
            && roundTrip.hatchesSinceShiny == PetNestTuning.ChromaPityHatches,
            "chroma pity resets while the shiny counter keeps counting, both survive a save round trip");

        while (PetNestService.PetCount < PetNestService.Capacity)
            Check(PetNestHatchService.TryCondenseAndHatch("test", out result, out error), "fill the nest");
        int chromaBefore = PetNestService.Nest.hatchesSinceChroma, shinyBefore = PetNestService.Nest.hatchesSinceShiny;
        Check(!PetNestHatchService.TryCondenseAndHatch("test", out result, out error) && error == "nest_full"
            && PetNestService.Nest.hatchesSinceChroma == chromaBefore && PetNestService.Nest.hatchesSinceShiny == shinyBefore,
            "a rejected hatch does not advance pity");

        Check(!PetNestPity.ForceShiny(PetNestTuning.ShinyPityHatches - 2) && PetNestPity.ForceShiny(PetNestTuning.ShinyPityHatches - 1)
            && PetNestPity.RemainingUntilGuaranteed(0, PetNestTuning.ShinyPityHatches) == PetNestTuning.ShinyPityHatches
            && PetNestPity.RemainingUntilGuaranteed(PetNestTuning.ShinyPityHatches + 5, PetNestTuning.ShinyPityHatches) == 1,
            "shiny pity triggers exactly on the last hatch of the window");
        var shinyPet = new PetNestPetRecord { id = "s", lineageKey = "test", shiny = true };
        var counters = new PetNestNestData { hatchesSinceChroma = 3, hatchesSinceShiny = 40 };
        PetNestPity.Advance(counters, shinyPet);
        Check(counters.hatchesSinceShiny == 0 && counters.hatchesSinceChroma == 4, "a shiny hatch resets only the shiny counter");

        var legacy = PetNestCodec.DecodeNest(BossRushJsonParser.ParseOrNull("{\"capacity\":12,\"nameSerial\":3,\"pets\":[],\"soulLedger\":[]}"));
        Check(legacy.hatchesSinceChroma == 0 && legacy.hatchesSinceShiny == 0, "old saves without pity fields start from zero");
    }

    static void ManualChromaAndDurations()
    {
        var pet = new PetNestPetRecord { id = "chroma", shiny = true, chromaA = "black", chromaB = "white" };
        string chinese = PetNestChroma.Decorate(pet, "阿花", true);
        string english = PetNestChroma.Decorate(pet, "Cub", false);
        Check(chinese.IndexOf("异色", StringComparison.Ordinal) < chinese.IndexOf("阿花", StringComparison.Ordinal),
            "shiny label precedes pet name in Chinese");
        Check(english.IndexOf("Shiny", StringComparison.Ordinal) < english.IndexOf("Cub", StringComparison.Ordinal),
            "shiny label precedes pet name in English");
        Check(pet.Clone().chromaA == "black" && pet.Clone().chromaB == "white", "cloning preserves chroma pair");
        var pairs = new System.Collections.Generic.HashSet<string>();
        for (int a = 0; a < 10; a++)
            for (int b = 0; b < 9; b++)
            {
                string x, y;
                PetNestChroma.RollPair((a + .5f) / 10, (b + .5f) / 9, out x, out y);
                Check(x != y, "chroma samples distinct colors");
                pairs.Add(string.CompareOrdinal(x, y) < 0 ? x + "/" + y : y + "/" + x);
            }
        Check(pairs.Count == 45, "all forty-five chroma pairs are reachable");
        Check(Math.Abs(PetNestExpeditionService.GetDurationHours(PetNestRiskTier.Safe) * 60 - 10) < .001
            && Math.Abs(PetNestExpeditionService.GetDurationHours(PetNestRiskTier.Rough) * 60 - 30) < .001
            && Math.Abs(PetNestExpeditionService.GetDurationHours(PetNestRiskTier.Desperate) * 60 - 60) < .001,
            "expedition durations remain 10 / 30 / 60 minutes");

        // 2026-09-20 第三轮：远征卡、翻牌卡与纪念碑显示的往往是**已经不在巢里**的崽
        // （真死结算会移除 PetRecord），颜色必须随记录固化，否则那三处只剩裸名字。
        var dead = new PetNestExpeditionRecord
        {
            id = "exp_dead", petId = "gone", petDisplayName = "阿花",
            petShiny = true, petChromaA = "black", petChromaB = "white",
            destinationId = "d", riskTier = (int)PetNestRiskTier.Desperate, outcomeDead = true,
        };
        dead.Normalize();
        Check(PetNestService.TryGetPet("gone") == null, "fixture models a pet already removed from the nest");
        // 夹具的 L10n.IsChinese 恒为 false，DescribeDecoratedPetName 走英文分支
        string deadName = PetNestExpeditionService.DescribeDecoratedPetName(dead);
        Check(deadName.IndexOf("Shiny", StringComparison.Ordinal) >= 0
            && deadName.IndexOf("阿花", StringComparison.Ordinal) >= 0
            && deadName != PetNestExpeditionService.DescribePetName(dead),
            "removed pet still shows its shiny decoration on the expedition card");
        // 搭配名是**逐字**渐变（每个字都被 <color> 包一层），所以必须先剥富文本再比
        string pairName = PetNestChroma.DescribePair("black", "white", false);
        Check(pairName == "Black-White"
            && StripRichText(deadName).IndexOf(pairName, StringComparison.Ordinal) >= 0,
            "removed pet still shows its chroma pair name");
        var clonedRecord = dead.Clone();
        Check(clonedRecord.petShiny && clonedRecord.petChromaA == "black" && clonedRecord.petChromaB == "white",
            "expedition clone preserves the frozen colors");

        // 存档往返：三格必须落盘再读回来，否则重进游戏这些颜色照样丢
        var expeditionData = new PetNestExpeditionData();
        expeditionData.Normalize();   // 容器由 Normalize 兜底，模型层没有字段初始化器
        expeditionData.records.Add(dead);
        var decodedExpedition = PetNestCodec.DecodeExpedition(
            BossRushJsonParser.ParseOrNull(PetNestCodec.EncodeExpedition(expeditionData)));
        Check(decodedExpedition.records.Count == 1 && decodedExpedition.records[0].petShiny
            && decodedExpedition.records[0].petChromaA == "black"
            && decodedExpedition.records[0].petChromaB == "white",
            "expedition colors survive a save round trip");

        var museum = new PetNestMuseumData();
        museum.Normalize();
        museum.memorials.Add(new PetNestMemorialEntry
        {
            displayName = "阿花", lineageKey = "l", destinationId = "d",
            shiny = true, chromaA = "black", chromaB = "white",
        });
        var decodedMuseum = PetNestCodec.DecodeMuseum(
            BossRushJsonParser.ParseOrNull(PetNestCodec.EncodeMuseum(museum)));
        Check(decodedMuseum.memorials.Count == 1 && decodedMuseum.memorials[0].shiny
            && decodedMuseum.memorials[0].chromaA == "black"
            && decodedMuseum.memorials[0].chromaB == "white",
            "memorial colors survive a save round trip");
        string engraved = PetNestChroma.Decorate(
            decodedMuseum.memorials[0].shiny, decodedMuseum.memorials[0].chromaA,
            decodedMuseum.memorials[0].chromaB, decodedMuseum.memorials[0].displayName, true);
        Check(engraved.IndexOf("异色", StringComparison.Ordinal) >= 0 && engraved != "阿花",
            "memorial engraving shows shiny and chroma rather than the bare name");
        Check(PetNestChroma.Decorate(false, "black", "white", "阿花", true).StartsWith("<color=#", StringComparison.Ordinal)
            && PetNestChroma.Decorate(false, null, null, "阿花", true) == "阿花",
            "chroma-only decoration gradients the name while a plain pet stays plain");

        // 老档（三格都缺）读出来必须是普通名字，不猜、不冒充
        var legacy = new PetNestExpeditionRecord { id = "exp_legacy", petId = "gone2", petDisplayName = "旧崽" };
        legacy.Normalize();
        Check(PetNestExpeditionService.DescribeDecoratedPetName(legacy) == "旧崽",
            "legacy record without colors falls back to the plain name");
    }

    static void PetNestLifecycleRepairs()
    {
        string error;
        for (int action = 0; action < 3; action++)
        {
            for (int reject = 0; reject < 2; reject++)
            {
                Reset(); PrepareNest();
                PetNestHatchResult hatch;
                Check(PetNestHatchService.TryHatchEgg(Egg(), out hatch, out error), "seat cleanup setup hatches cub");
                Check(PetNestService.TrySetDeployedPet(hatch.Pet.id, out error), "seat cleanup setup deploys cub");
                int notifications = PetNestBaseIdleSpawner.DeployNotifications;
                int cleanups = PetNestCompanionRuntime.Cleanups;
                if (reject != 0) SetPrivate(PetNestPersistence.Bundle, "_storeFaulted", true);
                PetNestExpeditionRecord record;
                bool ok = action == 0 ? PetNestService.TryRemovePet(hatch.Pet.id, out error)
                    : action == 1 ? PetNestService.TryReleasePet(hatch.Pet.id, out error)
                    : PetNestExpeditionService.TryDepart(hatch.Pet.id, PetNestTuning.DestinationStormSea,
                        PetNestRiskTier.Safe, out record, out error);
                Check(ok == (reject == 0), "seat-removing transaction reports acceptance or rejection");
                Check(PetNestBaseIdleSpawner.DeployNotifications == notifications + (ok ? 1 : 0)
                    && PetNestCompanionRuntime.Cleanups == cleanups + (ok ? 1 : 0),
                    "remove release and departure synchronize runtime exactly after successful commit");
                Check(ok ? PetNestService.Nest.deployedPetId == null
                    : PetNestService.Nest.deployedPetId == hatch.Pet.id,
                    "rejected seat transaction retains deployed cub and successful transaction clears it");
            }
        }

        Reset(); PrepareNest();
        var bundle = PetNestPersistence.Bundle.Current;
        var pet = new PetNestPetRecord { id = "old-cub", lineageKey = "test", displayName = "OldCub",
            shiny = true, chromaA = "black", chromaB = "white", state = (int)PetNestPetState.OnExpedition,
            lockedByExpeditionId = "old-trip" };
        pet.Normalize(); bundle.nest.pets.Add(pet);
        var oldTrip = new PetNestExpeditionRecord { id = "old-trip", petId = pet.id,
            petDisplayName = "OldCub", petLineageKey = "test", destinationId = PetNestTuning.DestinationStormSea,
            riskTier = (int)PetNestRiskTier.Desperate, returnTicks = 0, deathRate = 1, successRate = 0 };
        oldTrip.Normalize(); bundle.expedition.records.Add(oldTrip);
        Check(PetNestExpeditionService.TrySettle(oldTrip, out error), "legacy colored expedition settles through production death path");
        Check(PetNestService.TryGetPet(pet.id) == null, "legacy death removes original cub");
        var afterDeath = PetNestExpeditionService.Records[0];
        Check(afterDeath.petShiny && afterDeath.petChromaA == "black" && afterDeath.petChromaB == "white",
            "legacy expedition freezes known original colors before removal even with existing lineage");
        var reloaded = PetNestCodec.DecodeBundle(BossRushJsonParser.ParseOrNull(
            PetNestCodec.EncodeBundle(PetNestPersistence.Bundle.Current)));
        Check(PetNestExpeditionService.DescribeDecoratedPetName(reloaded.expedition.records[0]).Contains("Shiny")
            && StripRichText(PetNestExpeditionService.DescribeDecoratedPetName(reloaded.expedition.records[0])).Contains("Black-White"),
            "legacy death reveal retains shiny and chroma after bundle round trip");
        Check(reloaded.museum.memorials[0].shiny && reloaded.museum.memorials[0].chromaA == "black",
            "legacy death memorial agrees with reveal colors");
        Reset(); PrepareNest();
        var snapshot = PetNestPersistence.Bundle.Current;
        pet.state = (int)PetNestPetState.InNest; pet.lockedByExpeditionId = null;
        snapshot.nest.pets.Add(pet);
        snapshot.expedition.records.Add(new PetNestExpeditionRecord { id = "existing", settled = true,
            petId = pet.id, petLineageKey = "test", petChromaA = "red", petChromaB = "blue" });
        snapshot.expedition.records.Add(new PetNestExpeditionRecord { id = "orphan", settled = true,
            petId = "missing", petDisplayName = "Unknown" });
        snapshot.expedition.records.Add(new PetNestExpeditionRecord { id = "old-unrevealed", settled = true,
            petId = pet.id, petDisplayName = "OldCub" });
        Check(PetNestService.TryReleasePet(pet.id, out error), "release snapshots colors for old pending results");
        snapshot = PetNestPersistence.Bundle.Current;
        Check(snapshot.expedition.records[0].petChromaA == "red" && !snapshot.expedition.records[0].petShiny,
            "removal does not replace an existing appearance snapshot");
        Check(snapshot.expedition.records[1].petChromaA == null && !snapshot.expedition.records[1].petShiny,
            "missing original cub cannot borrow another cub appearance");
        Check(snapshot.expedition.records[2].petShiny && snapshot.expedition.records[2].petChromaA == "black",
            "release freezes known appearance before the original cub disappears");

        Reset(); PrepareNest();
        var debt = PendingEgg("missing-prefab", "gone");
        debt.outcomeLootTypeIds[0] = 123456;
        PetNestPersistence.Bundle.Current.expedition.records.Add(debt);
        ItemAssetsCollection.MissingPrefabId = 123456;
        Check(PetNestExpeditionService.TryGrantPendingRewards() == 0 && ItemUtilities.Delivered.Count == 0
            && ItemAssetsCollection.InstantiateCalls == 0, "missing prefab cannot instantiate or deliver official fallback");
        debt = PetNestExpeditionService.Records[0];
        Check(debt.grantedLootUnits == 0 && !debt.rewardsGranted, "missing prefab retains original reward cursor and debt");
        ItemAssetsCollection.MissingPrefabId = 0;
        PetNestExpeditionService.ResetValidationRewardBackend();
        Check(PetNestExpeditionService.TryGrantPendingRewards() == 1 && ItemUtilities.Delivered.Count == 1
            && !ItemUtilities.Delivered[0].IsFallback, "restored prefab completes persistent reward exactly once");
        PetNestExpeditionService.ResetValidationRewardBackend();
        Check(PetNestExpeditionService.TryGrantPendingRewards() == 0 && ItemUtilities.Delivered.Count == 1,
            "completed reward remains idempotent after prefab recovery");

        Reset(); PrepareNest();
        Check(!PetNestExpeditionService.CanDepart(null, out error), "missing cub has no departure action");
        pet = new PetNestPetRecord { id = "selectable", lineageKey = "test" }; pet.Normalize();
        PetNestPersistence.Bundle.Current.nest.pets.Add(pet);
        foreach (PetNestPetState state in new[] { PetNestPetState.InNest, PetNestPetState.Deployed,
            PetNestPetState.OnExpedition, PetNestPetState.Downed })
        {
            PetNestService.TryGetPet(pet.id).state = (int)state;
            bool expected = state == PetNestPetState.InNest || state == PetNestPetState.Deployed;
            Check(PetNestExpeditionService.CanDepart(PetNestService.TryGetPet(pet.id), out error) == expected,
                "shared departure predicate distinguishes eligible from locked and downed cubs");
            PetNestUIPages.DepartCardRequests = 0;
            PetNestUIPages.BuildExpeditionPage(null, pet.id);
            Check(PetNestUIPages.DepartCardRequests == (expected ? 1 : 0),
                "production expedition page only appends departure cards for eligible cubs");
            if (!expected)
            {
                PetNestExpeditionRecord blocked;
                Check(!PetNestExpeditionService.TryDepart(pet.id, PetNestTuning.DestinationStormSea,
                    PetNestRiskTier.Safe, out blocked, out error) && PetNestExpeditionService.Records.Count == 0,
                    "execution rejects the same locked or downed cub hidden by the page");
            }
        }
    }

    static void PetNestAchievements()
    {
        Reset(); PrepareNest(); UnityEngine.Random.value = .99f;
        PetNestHatchResult result; string error;
        Check(BossRushAchievementManager.Unlocked.Count == 0, "empty nest grants no taming achievement");
        for (int i = 1; i <= 30; i++)
        {
            var egg = new Item { Lineage = "test_" + i };
            PlayerStorage.Inventory.AddAt(egg, 5);
            UnityEngine.Time.frameCount++;
            bool failedMilestone = i == 10 || i == 30;
            if (failedMilestone) SavesSystem.FailPhysical = 1;
            bool hatched = PetNestHatchService.TryHatchEgg(egg, out result, out error);
            if (failedMilestone)
            {
                string id = "petnest_lineage_" + i;
                Check(!hatched && !BossRushAchievementManager.Unlocked.Contains(id), id + " waits for physical persistence");
                UnityEngine.Time.frameCount++; PetNestSaveCoordinator.Tick();
                Check(BossRushAchievementManager.Unlocked.Contains(id), id + " publishes after physical retry");
            }
            else Check(hatched, "real egg hatch accepted for lineage " + i);
            if (i == 1) Check(BossRushAchievementManager.Initialized && BossRushAchievementManager.Unlocked.Contains("petnest_first_hatch"),
                "first persisted egg initializes achievement catalog and unlocks first hatch");
            if (i == 9) Check(!BossRushAchievementManager.Unlocked.Contains("petnest_lineage_10"), "nine lineages cannot unlock ten");
            if (i == 29) Check(!BossRushAchievementManager.Unlocked.Contains("petnest_lineage_30"), "twenty-nine lineages cannot unlock thirty");
            Check(PetNestService.TryReleasePet(PetNestService.Pets[0].id, out error), "release through production service leaves museum progress");
        }
        Check(!BossRushAchievementManager.Unlocked.Contains("petnest_shiny") && !BossRushAchievementManager.Unlocked.Contains("petnest_memorial"),
            "normal hatches cannot unlock shiny or memorial achievements");
        PetNestMuseumStats.EvaluatePersistedAchievements(); PetNestMuseumStats.EvaluatePersistedAchievements();
        foreach (string id in new[] { "petnest_first_hatch", "petnest_lineage_10", "petnest_lineage_30" })
            Check(BossRushAchievementManager.Grants[id] == 1, id + " grants once after repeated checks and releases");

        Reset(); PrepareNest(); var shinyEgg = Egg(); SavesSystem.FailPhysical = 1;
        Check(!PetNestHatchService.TryHatchEgg(shinyEgg, out result, out error)
            && !BossRushAchievementManager.Unlocked.Contains("petnest_shiny"), "shiny waits for persisted hatch");
        UnityEngine.Time.frameCount++; PetNestSaveCoordinator.Tick();
        Check(BossRushAchievementManager.Unlocked.Contains("petnest_shiny"), "real shiny roll unlocks after save retry");
        PetNestMuseumStats.EvaluatePersistedAchievements();
        Check(BossRushAchievementManager.Grants["petnest_shiny"] == 1, "shiny achievement grants once");

        Reset(); PrepareNest(); UnityEngine.Random.value = .99f;
        Check(PetNestHatchService.TryHatchEgg(Egg(), out result, out error), "memorial test starts with a real hatched pet");
        PetNestExpeditionRecord expedition;
        UnityEngine.Time.frameCount++;
        Check(PetNestExpeditionService.TryDepart(result.Pet.id, PetNestTuning.DestinationStormSea,
            PetNestRiskTier.Desperate, out expedition, out error), "real desperate expedition starts");
        Check(!BossRushAchievementManager.Unlocked.Contains("petnest_memorial"), "departure is not a memorial");
        expedition.returnTicks = 0; UnityEngine.Random.value = 0; UnityEngine.Time.frameCount++;
        SavesSystem.FailPhysical = 1;
        PetNestExpeditionService.TrySettle(expedition, out error);
        Check(!BossRushAchievementManager.Unlocked.Contains("petnest_memorial"), "memorial candidate never unlocks before physical persistence");
        UnityEngine.Time.frameCount++; PetNestSaveCoordinator.Tick();
        Check(PetNestMuseumStats.MemorialCount == 1 && BossRushAchievementManager.Unlocked.Contains("petnest_memorial"),
            "persisted expedition death unlocks memorial achievement");
        PetNestExpeditionService.TrySettle(PetNestExpeditionService.Records[0], out error);
        PetNestMuseumStats.EvaluatePersistedAchievements();
        Check(BossRushAchievementManager.Grants["petnest_memorial"] == 1, "repeated settlement cannot grant a second memorial achievement");
    }
}
