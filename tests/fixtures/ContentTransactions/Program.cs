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
        BossRushAchievementManager.Unlocked.Clear();
        PetNestExpeditionService.ResetValidationRewardBackend();
        ShowcaseService.ResetStaticCaches();
        ItemUtilities.Delivered.Clear(); RelicEggConfig.FailStamp = false;
        ItemAssetsCollection.FailInstantiate = false; UnityEngine.Random.value = 0;
        RaidMealService.Reject = RaidMealService.Throw = false; RaidMealService.Registered = 0;
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
        foreach (int count in new[] { 1, 20 })
        {
            Reset(); var item = new Item { TypeID = 500065, StackCount = count };
            Check(behavior.CanBeUsed(item, null), "meal starts at stack " + count);
            SavesSystem.IsSaving = true; FinishOfficialUse(behavior, item);
            Check(item.StackCount == count && RaidMealService.Registered == 0,
                "saving begins before official second gate; meal preserved at stack " + count);
            SavesSystem.IsSaving = false; FinishOfficialUse(behavior, item);
            Check(item.StackCount == count - 1 && RaidMealService.Registered == 1,
                "meal retry registers once and official framework consumes one at stack " + count);
        }
        Reset(); var failed = new Item { TypeID = 500065 }; RaidMealService.Reject = true;
        FinishOfficialUse(behavior, failed);
        Check(failed.StackCount == 1, "meal registration refusal is compensated");
        RaidMealService.Reject = false; RaidMealService.Throw = true; FinishOfficialUse(behavior, failed);
        Check(failed.StackCount == 1, "meal registration exception is compensated");
        LevelManager.Instance.IsBaseLevel = false;
        Check(!behavior.CanBeUsed(failed, null), "non-base meal remains unavailable at action start");
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
    static void ShowcaseReplacement()
    {
        string error;
        Reset(); PrepareShowcase();
        var replacement = new Item { TypeID = 200, Quality = 8 };
        Check(!ShowcaseService.CanDisplay(replacement, out error)
            && ShowcaseService.CanReplaceRecord(100, replacement, out error), "full showcase permits replacing existing slot without requiring empty slot");
        Check(ShowcaseService.TryReplaceRecord(100, replacement, out error)
            && ShowcaseService.DisplayedCount == 8 && ShowcaseService.GetDisplayed()[0] == 200
            && Math.Abs(ShowcaseService.CalculateBonus() - .105f) < .0001f,
            "Q5 trophy upgrades to Q8 in same slot using unchanged health formula");
        Check(!ShowcaseService.TryReplaceRecord(101, replacement, out error)
            && !ShowcaseService.TryReplaceRecord(101, new Item { TypeID = 500065, Quality = 8 }, out error)
            && !ShowcaseService.TryReplaceRecord(101, new Item { TypeID = 201, Quality = 4 }, out error),
            "replacement still rejects duplicate trophies, homegrown items and low quality");
        Reset(); PrepareShowcase(); SavesSystem.FailKey = BackMountainConfig.ShowcaseSaveKey;
        Check(!ShowcaseService.TryReplaceRecord(100, replacement, out error)
            && ShowcaseService.GetDisplayed()[0] == 100 && ShowcaseService.DisplayedCount == 8,
            "replacement save failure preserves original record and position");
        ShowcaseService.NotifySlotChanged();
        Check(ShowcaseService.GetDisplayed()[0] == 100, "failed replacement reload keeps original persisted record");
        SavesSystem.FailReadAfterSaveKey = BackMountainConfig.ShowcaseSaveKey;
        Check(!ShowcaseService.TryReplaceRecord(100, replacement, out error)
            && ShowcaseService.GetDisplayed()[0] == 100, "replacement readback failure restores in-memory original");
        ShowcaseService.NotifySlotChanged();
        Check(ShowcaseService.GetDisplayed()[0] == 100, "replacement readback failure restores official save cache before reload");
        SavesSystem.IsSaving = true;
        Check(!ShowcaseService.TryRemoveRecord(100) && ShowcaseService.GetDisplayed()[0] == 100,
            "remove refusal while saving also preserves old record");
        SavesSystem.IsSaving = false;
        Check(ShowcaseService.TryRemoveRecord(100) && ShowcaseService.DisplayedCount == 7,
            "explicit remove frees slot for later recording");
    }
    static void Main()
    {
        CampaignCash(); DailyCash(); OfficialStickySaving(); Condense(); Hatch(); Meals(); ExpeditionEggIdentity(); ShowcaseReplacement();
        Console.WriteLine("ContentTransactions: " + checks + " assertions passed");
    }
}
