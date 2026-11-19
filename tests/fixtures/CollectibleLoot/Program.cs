using System;
using System.Collections.Generic;
using BossRush;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(label);
    }

    private static void Main()
    {
        CatalogShape();
        ValuesFollowOfficialBands();
        LocalizationCoversEveryItem();
        AppearanceRatesAndQualityWeights();
        JackpotRarity();
        LocalSeriesStaysOnItsMap();
        QualityCapsHold();
        RaidLedger();
        PersistenceRoundTrip();
        RegistrationPlan();
        RegistrationTransaction();
        NewItemsFirst();
        DisplayQualityFollowsTier();
        SetRewardsBeatSelling();
        GiftPreferences();
        OfficialBoxTiers();
        Console.WriteLine("CollectibleLoot: PASS (" + checks + " checks)");
    }

    // ---------------- 表 ----------------

    private static void CatalogShape()
    {
        Check(CollectibleCatalog.Count == 100, "100 collectibles");
        var keys = new HashSet<string>();
        var perQuality = new int[9];
        var perSeries = new int[CollectibleCatalog.SeriesCount];
        for (int i = 0; i < CollectibleCatalog.Count; i++)
        {
            CollectibleDefinition def = CollectibleCatalog.At(i);
            Check(def.TypeId == BossRushItemIds.CollectibleFirst + i, "TypeIDs are contiguous from 500119: " + def.Key);
            Check(CollectibleCatalog.Get(def.TypeId) == def, "Get by TypeID returns the same row: " + def.Key);
            Check(keys.Add(def.Key), "unique key: " + def.Key);
            Check(def.LocKey == "BossRush_Collectible_" + def.Key, "loc key follows the key");
            Check(def.Quality >= 1 && def.Quality <= 8, "quality 1..8");
            perQuality[def.Quality]++;
            perSeries[(int)def.Series]++;
            CollectibleSeriesInfo info = CollectibleCatalog.GetSeries(def.Series);
            if (def.Quality >= 7) Check(def.Series == CollectibleSeries.Treasures, "tier 7/8 only in the treasure group: " + def.Key);
            if (def.Series == CollectibleSeries.Treasures) Check(!info.CountsForSet && !info.Local, "treasures are global and outside sets");
        }
        Check(BossRushItemIds.CollectibleLast == BossRushItemIds.CollectibleFirst + 99, "CollectibleLast is the 100th id");
        Check(BossRushItemIds.CollectionBook == BossRushItemIds.CollectibleLast + 1, "album follows the last collectible");
        int[] expected = { 0, 24, 22, 18, 14, 10, 6, 5, 1 };
        for (int q = 1; q <= 8; q++) Check(perQuality[q] == expected[q], "tier " + q + " count is " + expected[q] + ", got " + perQuality[q]);
        for (int s = 0; s < CollectibleCatalog.SeriesCount; s++)
        {
            CollectibleSeriesInfo info = CollectibleCatalog.GetSeries((CollectibleSeries)s);
            int size = info.Local ? 8 : (info.CountsForSet ? 11 : 6);
            Check(perSeries[s] == size, info.NameEN + " has " + size + " items, got " + perSeries[s]);
        }
        Check(CollectibleCatalog.Get(BossRushItemIds.CollectibleFirst - 1) == null && CollectibleCatalog.Get(BossRushItemIds.CollectionBook) == null,
            "neighbours of the range are not collectibles");
    }

    private static void ValuesFollowOfficialBands()
    {
        // 官方收藏品 / 奢侈品同档区间（tests/fixtures/TidegateTreasure/catalog.txt，2026-11-07）；7、8 档 owner 授权自定。
        int[] min = { 0, 780, 1195, 1369, 1200, 4580, 12000, 300000, 21593219 };
        int[] max = { 0, 1700, 2400, 3100, 7819, 15631, 56000, 800000, int.MaxValue };
        for (int i = 0; i < CollectibleCatalog.Count; i++)
        {
            CollectibleDefinition def = CollectibleCatalog.At(i);
            Check(def.Value >= min[def.Quality] && def.Value <= max[def.Quality], def.Key + " value " + def.Value + " inside tier " + def.Quality + " band");
            Check(def.Weight > 0f && def.Weight <= 20f, def.Key + " has a sane weight");
        }
    }

    private static void LocalizationCoversEveryItem()
    {
        LocalizationHelper.Injected.Clear();
        CollectiblesLocalization.Inject();
        Check(LocalizationHelper.Injected.Count == CollectibleCatalog.Count * 2 + 2, "every item has a name and description, plus the album");
        foreach (bool chinese in new[] { true, false })
        {
            L10n.IsChinese = chinese;
            for (int i = 0; i < CollectibleCatalog.Count; i++)
            {
                CollectibleDefinition def = CollectibleCatalog.At(i);
                Check(!string.IsNullOrEmpty(CollectiblesLocalization.Name(def)), "name for " + def.Key);
                Check(!string.IsNullOrEmpty(CollectiblesLocalization.Description(def)), "description for " + def.Key);
            }
        }
        L10n.IsChinese = true;
    }

    // ---------------- 抽签 ----------------

    private static int[] Sample(CollectibleBoxTier tier, CollectibleSeries? local, int cap, int rolls, int seed, out int spawned, List<int> picks = null)
    {
        var random = new Random(seed);
        Func<double> next = () => random.NextDouble();
        var perQuality = new int[9];
        spawned = 0;
        for (int i = 0; i < rolls; i++)
        {
            int typeId = CollectibleLootRoller.Roll(tier, local, cap, 1f, next);
            if (typeId == 0) continue;
            spawned++;
            CollectibleDefinition def = CollectibleCatalog.Get(typeId);
            Check(def != null, "roll returns a real collectible");
            perQuality[def.Quality]++;
            if (picks != null) picks.Add(typeId);
        }
        return perQuality;
    }

    private static void AppearanceRatesAndQualityWeights()
    {
        const int rolls = 4000000;
        int spawned;
        int[] normal = Sample(CollectibleBoxTier.Normal, CollectibleSeries.GroundZero, 8, rolls, 11, out spawned);
        double rate = spawned / (double)rolls;
        Check(Math.Abs(rate - 0.04) < 0.001, "normal crate appearance near 4%, got " + rate);
        for (int q = 1; q <= 6; q++)
        {
            double share = normal[q] / (double)spawned;
            Check(Math.Abs(share - CollectibleTuning.NormalQualityWeights[q] / 100.0) < 0.006, "normal tier " + q + " share " + share);
        }
        Check(normal[8] == 0, "tier 8 never comes from a normal crate");

        int[] high = Sample(CollectibleBoxTier.High, CollectibleSeries.GroundZero, 8, rolls, 12, out spawned);
        rate = spawned / (double)rolls;
        Check(Math.Abs(rate - 0.12) < 0.0015, "high crate appearance near 12%, got " + rate);
        for (int q = 1; q <= 6; q++)
        {
            double share = high[q] / (double)spawned;
            Check(Math.Abs(share - CollectibleTuning.HighQualityWeights[q] / 100.0) < 0.006, "high tier " + q + " share " + share);
        }
    }

    private static void JackpotRarity()
    {
        // 每箱绝对概率：8 档高价值箱 0.006%（皇冠 0.03% 的五分之一），7 档高价值箱 0.03%、普通箱 0.006%。
        const int rolls = 8000000;
        int spawned;
        int[] high = Sample(CollectibleBoxTier.High, CollectibleSeries.Farm, 8, rolls, 21, out spawned);
        double q8 = high[8] / (double)rolls, q7 = high[7] / (double)rolls;
        Check(q8 > 0.00004 && q8 < 0.00008, "tier 8 per high crate near 0.006%, got " + q8);
        Check(q7 > 0.00025 && q7 < 0.00035, "tier 7 per high crate near 0.03%, got " + q7);
        Check(q8 < 0.0003 / 3, "tier 8 is clearly rarer than the crown's 0.03%");
        int[] normal = Sample(CollectibleBoxTier.Normal, CollectibleSeries.Farm, 8, rolls, 22, out spawned);
        double n7 = normal[7] / (double)rolls;
        Check(n7 > 0.00004 && n7 < 0.00008, "tier 7 per normal crate near 0.006%, got " + n7);
        Check(normal[8] == 0, "no tier 8 in normal crates");
        Check(q7 > n7 * 3, "high-value crates are where tier 7 shows up");
    }

    private static void LocalSeriesStaysOnItsMap()
    {
        var picks = new List<int>();
        int spawned;
        Sample(CollectibleBoxTier.High, CollectibleSeries.Tidegate, 8, 600000, 31, out spawned, picks);
        int localQ1 = 0, q1 = 0;
        foreach (int typeId in picks)
        {
            CollectibleDefinition def = CollectibleCatalog.Get(typeId);
            CollectibleSeriesInfo info = CollectibleCatalog.GetSeries(def.Series);
            Check(!info.Local || def.Series == CollectibleSeries.Tidegate, "only Tidegate's local series on Tidegate: " + def.Key);
            if (def.Quality == 1) { q1++; if (info.Local) localQ1++; }
        }
        double share = localQ1 / (double)q1;
        Check(Math.Abs(share - CollectibleTuning.LocalSeriesShare) < 0.02, "local share near 60% when both pools exist, got " + share);

        picks.Clear();
        Sample(CollectibleBoxTier.High, null, 8, 300000, 32, out spawned, picks);
        foreach (int typeId in picks)
            Check(!CollectibleCatalog.GetSeries(CollectibleCatalog.Get(typeId).Series).Local, "a map without a local series only yields global items");
    }

    private static void QualityCapsHold()
    {
        int spawned;
        int[] tide = Sample(CollectibleBoxTier.High, CollectibleSeries.Tidegate, CollectibleTuning.TidegateUnderwaterQualityCap, 600000, 41, out spawned);
        for (int q = 5; q <= 8; q++) Check(tide[q] == 0, "Tidegate underwater cap keeps tier " + q + " out");
        Check(Math.Abs(spawned / 600000.0 - 0.12) < 0.003, "the cap lowers quality, not the appearance rate");
        int[] sky = Sample(CollectibleBoxTier.High, CollectibleSeries.SkyIsland, CollectibleTuning.SkyIslandQualityCap, 600000, 42, out spawned);
        for (int q = 7; q <= 8; q++) Check(sky[q] == 0, "Sky Island cap keeps tier " + q + " out");
        for (int i = 0; i < CollectibleCatalog.Count; i++)
        {
            CollectibleDefinition def = CollectibleCatalog.At(i);
            if (def.Quality <= CollectibleTuning.SkyIslandQualityCap) Check(def.Value <= 100000, "everything under the Sky Island cap is worth at most 100k: " + def.Key);
        }
    }

    // ---------------- 账本 / 存档 / 送礼 / 箱子档次 ----------------

    private static void RaidLedger()
    {
        var ledger = new CollectibleRaidLedger();
        Check(!ledger.TryClaim(1, true), "an already searched crate is not judged");
        Check(ledger.TryClaim(2, false), "first open is judged");
        Check(!ledger.TryClaim(2, false), "re-opening the same crate is not judged again");
        for (int i = 0; i < CollectibleTuning.PerRaidCap; i++) ledger.MarkDelivered();
        Check(ledger.CapReached && !ledger.TryClaim(3, false), "at most 3 collectibles per raid");
        ledger.Reset();
        Check(ledger.Delivered == 0 && ledger.TryClaim(2, false), "a new raid starts clean");
    }

    private static void PersistenceRoundTrip()
    {
        var data = new CollectionBookData { JeffAccepted = true, FullPaid = false };
        data.Registered.Add(500150);
        data.Registered.Add(500119);
        data.PaidSeries.Add((int)CollectibleSeries.Tidegate);
        string json = CollectionBookPersistence.Encode(data);
        CollectionBookData back = CollectionBookPersistence.Decode(json);
        Check(back != null && back.Registered.SetEquals(data.Registered) && back.PaidSeries.SetEquals(data.PaidSeries)
            && back.JeffAccepted && !back.JeffDone && !back.FullPaid, "encode/decode round trip");
        Check(CollectionBookPersistence.ReadSchemaVersion(json) == 1, "schema 1");
        Check(json.IndexOf("[500119,500150]", StringComparison.Ordinal) >= 0 || json.IndexOf("500119", StringComparison.Ordinal) < json.IndexOf("500150", StringComparison.Ordinal),
            "registered ids are written sorted");
        Check(CollectionBookPersistence.Decode("{\"schemaVersion\":1,\"registered\":[500118]}") == null, "out-of-range id rejects the save");
        Check(CollectionBookPersistence.Decode("{\"schemaVersion\":1,\"registered\":[500119,500119]}") == null, "duplicate id rejects the save");
        Check(CollectionBookPersistence.Decode("{\"schemaVersion\":1,\"paidSeries\":[12]}") == null, "unknown series rejects the save");
        Check(CollectionBookPersistence.Decode("{\"schemaVersion\":2}") == null, "unknown schema rejects the save");
        CollectionBookData empty = CollectionBookPersistence.Decode("{\"schemaVersion\":1}");
        Check(empty != null && empty.Registered.Count == 0 && !empty.BookUnlocked, "missing keys read as nothing happened");
        Check(data.BookUnlocked, "accepting Jeff's quest unlocks the album in the shop");
        CollectionBookData copy = data.Clone();
        copy.Registered.Add(500200);
        Check(!data.Registered.Contains(500200), "Clone is a value copy");
        Check(data.RegisteredInSeries(CollectibleSeries.GroundZero) == 1, "count per series");
    }

    private static List<int> SeriesIds(CollectibleSeries series)
    {
        var ids = new List<int>();
        for (int i = 0; i < CollectibleCatalog.Count; i++)
        {
            CollectibleDefinition def = CollectibleCatalog.At(i);
            if (def.Series == series) ids.Add(def.TypeId);
        }
        return ids;
    }

    private static void RegistrationPlan()
    {
        var start = new CollectionBookData();
        // 一件普通收集品、一件重复、一件非收集品、一个空格
        CollectionRegistrationPlan plan = CollectionRegistration.Plan(start, new List<int> { 500119, 500119, 12, 0 });
        Check(plan.HasWork && plan.AcceptedIndices.Count == 1 && plan.AcceptedIndices[0] == 0, "only the first copy of a new collectible is taken");
        Check(plan.Cash == 0 && plan.Completed.Count == 0 && !plan.FullNow, "no set completed yet");
        Check(start.Registered.Count == 0, "planning never mutates the current facts");

        CollectionRegistrationPlan again = CollectionRegistration.Plan(plan.Next, new List<int> { 500119 });
        Check(!again.HasWork, "an already logged collectible is returned");

        List<int> ground = SeriesIds(CollectibleSeries.GroundZero);
        CollectionRegistrationPlan set = CollectionRegistration.Plan(plan.Next, ground);
        Check(set.AcceptedIndices.Count == ground.Count - 1, "the rest of the Ground Zero set is taken");
        Check(set.Completed.Count == 1 && set.Completed[0] == CollectibleSeries.GroundZero && set.Cash == CollectibleTuning.LocalSeriesCash,
            "completing a local set pays its cash once");
        CollectionRegistrationPlan twice = CollectionRegistration.Plan(set.Next, new List<int> { SeriesIds(CollectibleSeries.Warehouse)[0] });
        Check(twice.Cash == 0 && twice.Completed.Count == 0, "a paid set never pays again");

        // 集齐全部系列（珍品不需要）：最后一件触发全集大奖
        var all = new List<int>();
        for (int s = 0; s < CollectibleCatalog.SeriesCount; s++)
        {
            if (!CollectibleCatalog.GetSeries((CollectibleSeries)s).CountsForSet) continue;
            all.AddRange(SeriesIds((CollectibleSeries)s));
        }
        CollectionRegistrationPlan full = CollectionRegistration.Plan(new CollectionBookData(), all);
        long expected = CollectibleTuning.AllSeriesCash;
        for (int s = 0; s < CollectibleCatalog.SeriesCount; s++) expected += CollectibleTuning.SeriesCash((CollectibleSeries)s);
        Check(full.FullNow && full.Next.FullPaid && full.Cash == expected, "whole album pays every set plus the grand prize once");
        Check(full.Completed.Count == 11, "eleven sets count toward the album");
        CollectionRegistrationPlan treasure = CollectionRegistration.Plan(full.Next, SeriesIds(CollectibleSeries.Treasures));
        Check(treasure.HasWork && treasure.Cash == 0 && !treasure.FullNow, "treasures are logged but pay nothing");
    }

    // CR-2026-11-19-002：写成功之后哪一步抛错都照常收走、奖金只发一次；写失败一步都不跑。
    private static void RegistrationTransaction()
    {
        List<int> ground = SeriesIds(CollectibleSeries.GroundZero);
        CollectionRegistrationPlan plan = CollectionRegistration.Plan(new CollectionBookData(), ground);
        Check(plan.Cash == CollectibleTuning.LocalSeriesCash, "the fixture plan completes one set");
        int stores = 0, pays = 0, notices = 0, warnings = 0;
        CollectionBookData saved = null;
        bool committed = CollectionRegistration.Apply(plan, next => { stores++; saved = next; return true; }, w => warnings++,
            () => pays++,
            () => { throw new InvalidOperationException("official task refresh failed"); },
            () => notices++);
        Check(committed && stores == 1 && saved == plan.Next, "a successful write commits the plan once");
        Check(pays == 1 && notices == 1 && warnings == 1, "a throwing step is logged and the remaining steps still run once");

        stores = pays = notices = warnings = 0;
        committed = CollectionRegistration.Apply(plan, next => { stores++; return false; }, w => warnings++, () => pays++, () => notices++);
        Check(!committed && stores == 1 && pays == 0 && notices == 0, "a failed write returns everything and pays nothing");

        committed = CollectionRegistration.Apply(plan, next => { throw new InvalidOperationException("disk"); }, w => warnings++, () => pays++);
        Check(!committed && pays == 0 && warnings == 1, "a throwing write counts as not committed");

        CollectionRegistrationPlan empty = CollectionRegistration.Plan(plan.Next, new List<int> { ground[0] });
        stores = 0;
        Check(!CollectionRegistration.Apply(empty, next => { stores++; return true; }, null, () => pays++) && stores == 0,
            "nothing new to log never touches the save");
    }

    // CR-2026-11-19-007：新件优先只换挑哪一件，不改出现率与品质分布。
    private static void NewItemsFirst()
    {
        const int rolls = 600000;
        var random = new Random(51);
        Func<double> next = () => random.NextDouble();
        // 零号区系列和全图系列只剩一件 1 档没登记：一半机会直接挑它，另一半照常从全部候选里挑。
        var q1 = new List<int>();
        for (int i = 0; i < CollectibleCatalog.Count; i++)
        {
            CollectibleDefinition def = CollectibleCatalog.At(i);
            CollectibleSeriesInfo info = CollectibleCatalog.GetSeries(def.Series);
            if (def.Quality == 1 && (def.Series == CollectibleSeries.GroundZero || !info.Local)) q1.Add(def.TypeId);
        }
        int missing = q1[q1.Count - 1];
        Func<int, bool> registered = id => id != missing;
        int hits = 0, picks = 0;
        for (int i = 0; i < 200000; i++)
        {
            int id = CollectibleLootRoller.PickInQuality(1, CollectibleSeries.GroundZero, next, registered);
            Check(CollectibleCatalog.Get(id).Quality == 1, "new-first never leaves the rolled tier");
            picks++;
            if (id == missing) hits++;
        }
        double share = hits / (double)picks;
        Check(share > CollectibleTuning.NewItemPreference && share < CollectibleTuning.NewItemPreference + 0.15,
            "the last missing piece shows up a bit more than half the time, got " + share);

        // 全登记过：照常挑，不会卡住或返回 0。
        Check(CollectibleLootRoller.PickInQuality(1, CollectibleSeries.GroundZero, next, id => true) > 0, "a fully logged tier still yields a piece");

        // 出现率与品质分布照旧。
        int spawned = 0;
        var perQuality = new int[9];
        for (int i = 0; i < rolls; i++)
        {
            int id = CollectibleLootRoller.Roll(CollectibleBoxTier.High, CollectibleSeries.GroundZero, 8, 1f, next, registered);
            if (id == 0) continue;
            spawned++;
            perQuality[CollectibleCatalog.Get(id).Quality]++;
        }
        Check(Math.Abs(spawned / (double)rolls - 0.12) < 0.0025, "new-first keeps the 12% appearance rate");
        for (int q = 1; q <= 6; q++)
            Check(Math.Abs(perQuality[q] / (double)spawned - CollectibleTuning.HighQualityWeights[q] / 100.0) < 0.008,
                "new-first keeps the tier " + q + " share");
    }

    // CR-2026-11-19-001：物品格光晕跟着档次，官方枚举 1–8 一一对应。
    private static void DisplayQualityFollowsTier()
    {
        for (int q = 1; q <= CollectibleTuning.MaxQuality; q++)
            Check(CollectibleTuning.DisplayQualityOf(q) == q, "display quality follows tier " + q);
        Check(CollectibleTuning.DisplayQualityOf(0) == 1 && CollectibleTuning.DisplayQualityOf(99) == CollectibleTuning.MaxQuality,
            "out-of-range tiers clamp into the official enum");
    }

    // CR-2026-11-19-007：登记会收走物品，集齐奖金不能比整套卖掉还少。
    private static void SetRewardsBeatSelling()
    {
        for (int s = 0; s < CollectibleCatalog.SeriesCount; s++)
        {
            var series = (CollectibleSeries)s;
            CollectibleSeriesInfo info = CollectibleCatalog.GetSeries(series);
            if (!info.CountsForSet) continue;
            long value = 0;
            foreach (int id in SeriesIds(series)) value += CollectibleCatalog.Get(id).Value;
            Check(CollectibleTuning.SeriesCash(series) >= value, info.NameEN + " set cash " + CollectibleTuning.SeriesCash(series) + " is below its sale value " + value);
        }
        CollectibleTuning.SeriesBonus arena = CollectibleTuning.BonusOf(CollectibleSeries.Arena);
        Check(arena.Stat == "GunCritDamageGain", "the player's gun crits only on headshots, so the Arena bonus must raise headshot damage");
    }

    private static void GiftPreferences()
    {
        int liked = 0;
        for (int i = 0; i < CollectibleCatalog.Count; i++)
        {
            CollectibleDefinition def = CollectibleCatalog.At(i);
            int value = CollectibleTuning.LikedGiftAffinity("goblin_dingdang", def.TypeId);
            bool likes = Array.IndexOf(def.LikedBy, "goblin_dingdang") >= 0;
            Check(likes == (value > 0), "Dingdang's liking matches LikedBy for " + def.Key);
            if (likes) { liked++; Check(value == CollectibleTuning.GiftAffinity(def.Quality), "affinity follows quality for " + def.Key); }
        }
        Check(liked >= 5, "Dingdang has several favourites");
        int zha = 0;
        for (int i = 0; i < CollectibleCatalog.Count; i++)
            if (CollectibleTuning.LikedGiftAffinity("tidegate_zhabo", CollectibleCatalog.At(i).TypeId) > 0) zha++;
        Check(zha == 8, "Old Zha likes the whole Tidegate series");
        for (int i = 0; i < CollectibleCatalog.Count; i++)
            Check(CollectibleTuning.LikedGiftAffinity("lastshift_yuxiang", CollectibleCatalog.At(i).TypeId) == 0, "Yu Xiang has no affinity system and likes nothing");
        Check(CollectibleTuning.LikedGiftAffinity("goblin_dingdang", 500001) == 0, "non-collectibles are never liked through this path");
        Check(CollectibleTuning.LikedGiftAffinity(null, 500200) == 0, "no NPC id, no liking");
        Check(CollectibleTuning.GiftAffinity(8) > CollectibleTuning.GiftAffinity(1), "rarer gifts are worth more");
    }

    private static void OfficialBoxTiers()
    {
        CollectibleBoxTier tier;
        Check(CollectibleTuning.TryResolveOfficialBoxTier("UI_LootBox_Lux", out tier) && tier == CollectibleBoxTier.High, "safes are high value");
        Check(CollectibleTuning.TryResolveOfficialBoxTier("UI_LootBox_WeaponEquip_2", out tier) && tier == CollectibleBoxTier.High, "weapon crates are high value");
        Check(CollectibleTuning.TryResolveOfficialBoxTier("UI_LootBox_Normal", out tier) && tier == CollectibleBoxTier.Normal, "plain crates are normal");
        Check(CollectibleTuning.TryResolveOfficialBoxTier("UI_LootBox_SomethingNew", out tier) && tier == CollectibleBoxTier.Normal, "unknown keys count as normal");
        foreach (string excluded in new[] { "UI_LootBox_Safe", "UI_LootBox_DeliverBox", "UI_LootBox_Deliver", "UI_LootBox_Starter", "UI_LootBox_Quest2201", "UI_LootBox_GunFire_2002" })
            Check(!CollectibleTuning.TryResolveOfficialBoxTier(excluded, out tier), "excluded: " + excluded);
        CollectibleSeries series;
        Check(CollectibleTuning.TryResolveOfficialSeries("Level_SnowMilitaryBase_Main", out series) && series == CollectibleSeries.TestArea37, "Test Area 37 maps to its series");
        Check(!CollectibleTuning.TryResolveOfficialSeries("Base", out series), "the base has no local series");
        for (int s = 0; s < CollectibleCatalog.SeriesCount; s++)
        {
            CollectibleSeriesInfo info = CollectibleCatalog.GetSeries((CollectibleSeries)s);
            Check(info.CountsForSet == (CollectibleTuning.BonusOf((CollectibleSeries)s) != null), "every set has exactly one bonus: " + info.NameEN);
            Check(info.CountsForSet == (CollectibleTuning.SeriesCash((CollectibleSeries)s) > 0), "every set pays cash: " + info.NameEN);
        }
    }
}
