using System;
using System.Linq;
using BossRush;
using Saves;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Duckov.Utilities;
using Duckov.Crops;
using UnityEngine;

static class Program
{
    static int assertions;
    static BackMountainRuntimeModule module;
    static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }
    static bool Near(float a, float b) { return Math.Abs(a - b) < .0001f; }
    static Item Item(int id, int quality = 5)
    {
        var item = new GameObject().AddComponent<Item>(); item.TypeID = id; item.Quality = quality;
        ItemAssetsCollection.AddDynamicEntry(item); return item;
    }
    static CharacterMainControl Player()
    {
        var player = new GameObject().AddComponent<CharacterMainControl>();
        player.CharacterItem = Item(1);
        foreach (var key in new[] { "RunSpeed", "WalkSpeed", "GunDamageMultiplier", "MeleeDamageMultiplier", "ElementFactor_Physics" })
            player.CharacterItem.Stats[key] = new Stat { BaseValue = 1 };
        player.CharacterItem.Stats["ReloadSpeedGain"] = new Stat { BaseValue = 0 };
        player.CharacterItem.Stats["MaxHealth"] = new Stat { BaseValue = 100 };
        player.Health = new Health(player.CharacterItem); CharacterMainControl.Main = player; return player;
    }
    static void Reset()
    {
        if (module != null) module.OnDestroy(); module = null;
        RaidMealService.ResetStaticCaches(); ShowcaseService.ResetStaticCaches(); GardenSeedInjector.ResetStaticCaches();
        ShowcaseDisplayScanner.ResetStaticCaches(); GardenConstructionSite.ResetStaticCaches(); ShowcaseTrophyCatalog.ResetStaticCaches();
        CampaignBaseObjectives.Providers.Clear(); DialogueManager.IsDialogueActive = false; BossRushUI.Hidden = BossRushUI.Paused = false;
        BackMountainUnlocks.ResetStaticCaches(); BackMountainItems.ResetStaticCaches();
        SavesSystem.Reset(); ItemAssetsCollection.Prefabs.Clear(); CampaignFacilityUnlocks.Tokens.Clear();
        BossBgmCoordinator.Tracks.Clear(); UnityEngine.Object.Selector = null; UnityEngine.Object.FindCalls = 0;
        L10n.Chinese = true; ModBehaviour.Instance = new ModBehaviour(); LevelManager.Instance = new LevelManager { IsBaseLevel = true };
        LevelManager.AfterInit = false; CharacterMainControl.Main = null;
        RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo(); GameplayDataSettings.CropDatabase = new CropDatabase();
        Item(BossRushItemIds.RelicEgg); BackMountainItems.RegisterConfigurators();
    }
    static void StartModule()
    {
        module = (BackMountainRuntimeModule)Activator.CreateInstance(typeof(BackMountainRuntimeModule), true);
        module.OnAwake(ModBehaviour.Instance); module.OnStart();
    }
    static void Raid(uint id = 1)
    {
        LevelManager.Instance = new LevelManager { IsRaidMap = true };
        RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { valid = true, ID = id };
        Player(); LevelManager.Ready();
    }
    static float Stat(string key) { return CharacterMainControl.Main.CharacterItem.GetStat(key).Value; }
    static void RegistrationAndUsage()
    {
        Reset(); Player();
        BackMountainItems.ResetStaticCaches(); ItemFactory.Configurators.Clear(); BackMountainItems.RegisterConfigurators();
        Check(ItemFactory.Configurators.Count == 6, "mod reload restores cleared item factory configurators");
        foreach (var def in BackMountainItems.Definitions)
        {
            Check(BackMountainItems.EnsureRuntimeRegistration(def.TypeId), "all six items register");
            var item = ItemAssetsCollection.GetPrefab(def.TypeId);
            Check(item.Stackable && item.MaxStackCount == 20, "all seed/meal items stack");
            if (def.IsSeed) Check(item.UsageUtilities == null, "seed has no eat action");
            else Check(item.UsageUtilities != null && item.UsageUtilities.Master == item
                && item.UsageUtilities.behaviors.Count == 1, "meal bound before official use");
        }
        foreach (bool chinese in new[] { true, false })
        {
            L10n.Chinese = chinese; BackMountainItems.InjectLocalization();
            foreach (var def in BackMountainItems.Definitions)
            {
                var item = ItemAssetsCollection.GetPrefab(def.TypeId);
                Check(LocalizationHelper.Entries[item.DescriptionRaw] == (chinese ? def.DescCN : def.DescEN), "official derived description key localized");
            }
        }
        Check(!RaidMealService.RegisterMeal(BossRushItemIds.DragonSeed), "registration rejects seeds");
        Check(RaidMealService.RegisterMeal(BossRushItemIds.DragonFruit), "fruit registration");
        Check(RaidMealService.RegisterMeal(BossRushItemIds.EmberChili), "later meal replaces earlier");
        Check(!RaidMealService.RegisterMeal(int.MaxValue) && RaidMealService.ReadRegisteredMeal() == BossRushItemIds.EmberChili, "unknown registration preserves old meal");
        foreach (bool readback in new[] { false, true })
        {
            if (readback) SavesSystem.FailReadback = BackMountainConfig.RaidMealSaveKey;
            else SavesSystem.FailWrite = BackMountainConfig.RaidMealSaveKey;
            Check(!RaidMealService.RegisterMeal(BossRushItemIds.DragonFruit)
                && RaidMealService.ReadRegisteredMeal() == BossRushItemIds.EmberChili, "registration failure rolls back cache");
        }
        var meal = ItemAssetsCollection.GetPrefab(BossRushItemIds.DragonFruit);
        var use = meal.GetComponent<RaidMealUsageBehavior>();
        foreach (int count in new[] { 1, 20 })
        {
            meal.StackCount = count; SavesSystem.IsSaving = true;
            Check(use.CanBeUsed(meal, null), "transient save cannot bypass compensation");
            use.Use(meal); meal.StackCount--;
            Check(meal.StackCount == count, "official finish retains failed meal including full stack");
            SavesSystem.IsSaving = false; use.Use(meal); meal.StackCount--;
            Check(meal.StackCount == count - 1, "success consumes exactly one");
        }
        LevelManager.Instance.IsBaseLevel = false;
        Check(!use.CanBeUsed(meal, null), "cannot eat during raid");
    }
    static void MealLifecycle()
    {
        Reset(); StartModule(); RaidMealService.RegisterMeal(BossRushItemIds.EmberChili); Raid();
        Check(Near(Stat("ReloadSpeedGain"), .1f) && Near(Stat("RunSpeed"), 1.08f) && Near(Stat("WalkSpeed"), 1.08f), "chili changes all three real stats including zero-base gain");
        Check(Near(1f / (1f + Stat("ReloadSpeedGain")), 1f / 1.1f), "official reload time is actually shorter");
        Check(RaidMealService.ReadRegisteredMeal() == 0, "successful effect consumes pending record");
        module.OnSceneLoaded(new SceneRuntimeContext { SceneName = "AdditiveRegion" });
        Check(Near(Stat("RunSpeed"), 1.08f), "additive scene does not remove meal");
        LevelManager.Ready(); LevelManager.Ready();
        Check(Near(Stat("RunSpeed"), 1.08f), "repeated ready does not stack");
        var old = CharacterMainControl.Main;
        UnityEngine.Object.Destroy(old.gameObject); UnityEngine.Object.Destroy(old.CharacterItem.gameObject);
        Check(old == null && old.CharacterItem == null, "fixture destroys game object and components with Unity null semantics");
        Player(); LevelManager.Ready();
        Check(Near(Stat("RunSpeed"), 1.08f) && Near(Stat("ReloadSpeedGain"), .1f), "new character in same raid receives already consumed meal");
        RaidUtilities.End(); Check(Near(Stat("RunSpeed"), 1), "raid end removes modifiers immediately");
        Raid(2); Check(Near(Stat("RunSpeed"), 1), "meal cannot carry to next raid");
        LevelManager.Instance.IsBaseLevel = true; RaidMealService.RegisterMeal(BossRushItemIds.DragonFruit); Raid(3);
        Check(Near(Stat("GunDamageMultiplier"), 1.1f) && Near(Stat("MeleeDamageMultiplier"), 1.1f), "fruit grants both damage stats");
        SavesSystem.Switch(1); Check(Near(Stat("GunDamageMultiplier"), 1), "slot event removes active meal");
        LevelManager.Ready(); Check(Near(Stat("GunDamageMultiplier"), 1), "slot cannot inherit previous consumed meal");
        RaidMealService.RegisterMeal(BossRushItemIds.PhantomMushroom); Raid(4);
        Check(Near(Stat("ElementFactor_Physics"), .9f), "mushroom reduces physical multiplier");
        SavesSystem.CurrentSlot = 2; LevelManager.Ready();
        Check(Near(Stat("ElementFactor_Physics"), 1), "missed slot event cannot retain active meal");
        module.OnDestroy(); module = null;
        var count = ModBehaviour.Instance.Buildings; LevelManager.Ready(); CampaignFacilityUnlocks.Grant(1);
        Check(ModBehaviour.Instance.Buildings == count, "destroy unsubscribes facility and level events");
    }
    static void MealFailures()
    {
        foreach (bool readback in new[] { false, true })
        {
            Reset(); RaidMealService.RegisterMeal(BossRushItemIds.DragonFruit); Raid();
            if (readback) SavesSystem.FailReadback = BackMountainConfig.RaidMealSaveKey;
            else SavesSystem.FailWrite = BackMountainConfig.RaidMealSaveKey;
            RaidMealService.ApplyForRun();
            Check(Near(Stat("GunDamageMultiplier"), 1) && RaidMealService.ReadRegisteredMeal() == BossRushItemIds.DragonFruit, "failed settlement removes effect and restores meal");
            RaidMealService.ApplyForRun(); Check(Near(Stat("GunDamageMultiplier"), 1.1f), "settlement retry applies once");
        }
        Reset(); RaidMealService.RegisterMeal(BossRushItemIds.EmberChili); Raid();
        CharacterMainControl.Main.CharacterItem.Stats.Remove("ReloadSpeedGain");
        RaidMealService.ApplyForRun();
        Check(Near(Stat("RunSpeed"), 1) && RaidMealService.ReadRegisteredMeal() == BossRushItemIds.EmberChili, "missing stat rolls back partial meal without consuming");
        CharacterMainControl.Main.CharacterItem.Stats["ReloadSpeedGain"] = new Stat();
        RaidMealService.ApplyForRun(); Check(Near(Stat("ReloadSpeedGain"), .1f), "missing stat can recover");
        Reset(); RaidMealService.RegisterMeal(BossRushItemIds.DragonFruit); Player();
        RaidMealService.ApplyForRun(); Check(RaidMealService.ReadRegisteredMeal() != 0, "base never consumes meal");
        LevelManager.Instance.IsBaseLevel = false; RaidMealService.ApplyForRun();
        Check(RaidMealService.ReadRegisteredMeal() != 0, "non-raid level never consumes meal");
        SavesSystem.Save(BackMountainConfig.RaidMealSaveKey, int.MaxValue); Raid(); RaidMealService.ApplyForRun();
        Check(RaidMealService.ReadRegisteredMeal() == int.MaxValue, "unknown legacy record remains intact");
    }
    static void Facilities()
    {
        Reset(); StartModule();
        var selector = new GameObject().AddComponent<BaseBGMSelector>();
        selector.entries.Add(new BaseBGMSelector.Entry { musicName = "Original" });
        UnityEngine.Object.Selector = selector;
        BossBgmCoordinator.Tracks.Add(new BossBgmJukeboxEntry { musicName = "战歌", musicNameEn = "War Song", file = "mod.wav" });
        ModBehaviour.Instance.UnlockAll = true; module.OnUpdate(0, 0);
        Check(!BackMountainUnlocks.IsFacilityUnlocked((BackMountainFacility)999), "unlock bypass still rejects unknown facility");
        Check(GardenSeedInjector.IsInjected && selector.entries.Count == 2, "unlock preference immediately injects garden and jukebox");
        L10n.Chinese = false; module.OnUpdate(0, 0);
        Check(selector.entries.Count == 2 && selector.entries[1].musicName == "War Song", "language switch updates title without changing index or duplicating track");
        BossBgmTrackTable parsed; string error;
        Check(BossBgmTrackTable.TryParse("{\"version\":1,\"jukebox\":[{\"musicName\":\"旧曲\",\"file\":\"old.wav\"}]}", out parsed, out error)
            && parsed.jukebox[0].musicNameEn == null, "optional English track title preserves old table compatibility");
        int finds = UnityEngine.Object.FindCalls;
        for (int i = 0; i < 10000; i++) module.OnUpdate(.016f, .016f);
        Check(UnityEngine.Object.FindCalls == finds, "steady updates perform no object scans");
        foreach (var seed in GameplayDataSettings.CropDatabase.seedInfos)
        {
            var crop = GameplayDataSettings.CropDatabase.entries.Single(c => c.id == seed.cropIDs.Entries.Single());
            Check(crop.resultNormal == BackMountainItems.GetHarvestResultFor(seed.itemTypeID)
                && crop.resultPoor == crop.resultNormal && crop.resultGood == crop.resultNormal
                && crop.resultAmount == 2 && crop.totalGrowTicks == TimeSpan.FromMinutes(20).Ticks, "production seed maps to usable two-meal harvest");
        }
        LevelManager.Ready(); LevelManager.Ready();
        Check(GameplayDataSettings.CropDatabase.entries.Count == 3 && selector.entries.Count == 2, "facility refresh is idempotent");
        Reset(); StartModule(); SavesSystem.Save("BossRush_BackMountain_GardenRatchet_v1", true);
        LevelManager.Instance = null;
        module.OnSceneLoaded(new SceneRuntimeContext { SceneName = "Base" });
        Check(GardenSeedInjector.IsInjected, "ratchet restores crops before Garden.Start without tokens or LevelManager");
        Reset(); StartModule(); ModBehaviour.Instance.UnlockAll = true;
        GameplayDataSettings.CropDatabase.seedInfos = null; module.OnUpdate(0, 0);
        Check(!GardenSeedInjector.IsInjected, "partial crop database must not latch success");
        GameplayDataSettings.CropDatabase.seedInfos = new System.Collections.Generic.List<SeedInfo>(); LevelManager.Ready();
        Check(GardenSeedInjector.IsInjected, "level-ready retries unfinished facility injection");
        Reset(); StartModule(); ModBehaviour.Instance.UnlockAll = true; SavesSystem.IsSaving = true; module.OnUpdate(0, 0);
        Check(!GardenSeedInjector.IsInjected && GameplayDataSettings.CropDatabase.entries.Count == 0, "save busy cannot publish unprotected crop IDs");
        SavesSystem.IsSaving = false; LevelManager.Ready();
        Check(GardenSeedInjector.IsInjected && SavesSystem.Load<bool>("BossRush_BackMountain_GardenRatchet_v1"), "retry persists recovery marker before publishing crops");
        Reset(); StartModule(); CampaignFacilityUnlocks.Tokens.Add("Ch1"); LevelManager.Ready();
        Check(GardenSeedInjector.IsInjected, "historical unlock is observed without grant event");
    }
    static void Showcase()
    {
        // ---- 判据：筛选 / 归一 / 加成 / 迁移 ----
        Check(!ShowcaseDisplayJudges.ShouldCountAsTrophy(100, 4, false, false, true), "quality 4 is not a trophy");
        Check(ShowcaseDisplayJudges.ShouldCountAsTrophy(100, 5, false, false, true), "quality 5 is a trophy");
        Check(!ShowcaseDisplayJudges.ShouldCountAsTrophy(100, 8, true, false, true), "back mountain seed/meal is never a trophy");
        Check(!ShowcaseDisplayJudges.ShouldCountAsTrophy(100, 8, false, true, true), "deny list excludes function items");
        Check(!ShowcaseDisplayJudges.ShouldCountAsTrophy(100, 8, false, false, false), "unloaded prefab is skipped");
        Check(!ShowcaseDisplayJudges.ShouldCountAsTrophy(0, 8, false, false, true), "invalid id rejected");
        Func<int, int> q = id => id == 200 ? 8 : id == 100 ? 5 : id >= 300 && id < 320 ? 8 : 0;
        int[] normalized = ShowcaseDisplayJudges.NormalizeDisplaySnapshot(new[] { 100, 100, 7, 200, -1, 0 }, q, 8);
        Check(normalized.Length == 2 && normalized[0] == 200 && normalized[1] == 100, "snapshot dedups, drops invalid/low quality, sorts by quality desc");
        int[] many = ShowcaseDisplayJudges.NormalizeDisplaySnapshot(new[] { 100, 300, 301, 302, 303, 304, 305, 306, 307, 308 }, q, 8);
        Check(many.Length == 8 && Array.IndexOf(many, 100) < 0, "more than cap keeps the best eight");
        Check(ShowcaseDisplayJudges.NormalizeDisplaySnapshot(null, q, 8).Length == 0, "null snapshot is empty");
        Check(Near(ShowcaseDisplayJudges.CalculateBonusFrom(new[] { 100 }, q, 8), .005f), "one Q5 = +0.5%");
        Check(Near(ShowcaseDisplayJudges.CalculateBonusFrom(new[] { 200 }, q, 8), .02f), "one Q8 = +2%");
        Check(Near(ShowcaseDisplayJudges.CalculateBonusFrom(new[] { 300, 301, 302, 303, 304, 305, 306 }, q, 8), .14f), "seven Q8 = +14% without full-set bonus");
        Check(Near(ShowcaseDisplayJudges.CalculateBonusFrom(new[] { 300, 301, 302, 303, 304, 305, 306, 307 }, q, 8), .21f), "eight Q8 = +21% cap");
        Check(Near(ShowcaseDisplayJudges.CalculateBonusFrom(new[] { 7 }, q, 8), 0f), "Q4 and below give nothing");
        Check(ShowcaseDisplayJudges.ShouldOverwriteLegacyLedger(2, false, false), "official-display source always overwrites");
        Check(ShowcaseDisplayJudges.ShouldOverwriteLegacyLedger(1, true, true), "legacy ledger overwritten only at base with a showcase found");
        Check(!ShowcaseDisplayJudges.ShouldOverwriteLegacyLedger(1, false, true) && !ShowcaseDisplayJudges.ShouldOverwriteLegacyLedger(1, true, false),
            "legacy ledger kept outside base or without official showcase");

        // ---- 战利品名录：注册表枚举、prefab 级、幂等、排除后山自产；不补任何官方展示标签（陈列柜是废弃建筑） ----
        Reset(); Item(100, 5); Item(200, 8); Item(50, 3);
        BossRushDynamicItemRegistry.Published.Clear();
        BossRushDynamicItemRegistry.Published.AddRange(new[] { 100, 200, 50, BossRushItemIds.DragonFruit, 999 });
        ShowcaseTrophyCatalog.ResetStaticCaches(); EquipmentHelper.Tagged.Clear();
        ShowcaseTrophyCatalog.Refresh();
        Check(ShowcaseTrophyCatalog.TrophyCount == 2 && EquipmentHelper.Tagged.Count == 0, "only loaded Q5+ non-meal registry items enter the catalog, and nothing gets tagged");
        ShowcaseTrophyCatalog.Refresh();
        Check(ShowcaseTrophyCatalog.TrophyCount == 2 && EquipmentHelper.Tagged.Count == 0, "refresh is idempotent");
        Check(ShowcaseTrophyCatalog.IsShowcaseTrophy(200) && !ShowcaseTrophyCatalog.IsShowcaseTrophy(50)
            && !ShowcaseTrophyCatalog.IsShowcaseTrophy(BossRushItemIds.DragonFruit) && !ShowcaseTrophyCatalog.IsShowcaseTrophy(12345), "trophy judgement matches the catalog");

        // ---- 服务：快照覆盖、相同不落盘、写失败恢复、sourceVersion 迁移、换槽、坏档写保护 ----
        Reset(); Item(100, 5); Item(200, 8); Player(); StartModule(); CampaignFacilityUnlocks.Grant(2);
        BossRushDynamicItemRegistry.Published.Clear(); BossRushDynamicItemRegistry.Published.AddRange(new[] { 100, 200 }); ShowcaseTrophyCatalog.ResetStaticCaches();
        Check(ShowcaseService.SourceVersion == 1 && ShowcaseService.DisplayedCount == 0, "fresh slot starts as empty legacy ledger");
        Check(ShowcaseService.ApplyDisplaySnapshot(new[] { 100 }, true), "official snapshot applies at base");
        Check(ShowcaseService.SourceVersion == 2 && ShowcaseService.DisplayedCount == 1 && Near(ShowcaseService.CalculateBonus(), .005f)
            && Near(Stat("MaxHealth"), 100.5f), "snapshot records display, bumps source version and applies bonus");
        string before = SavesSystem.Load<string>(BackMountainConfig.ShowcaseSaveKey);
        Check(before.Contains("\"sourceVersion\":2"), "save carries optional sourceVersion");
        SavesSystem.FailWrite = BackMountainConfig.ShowcaseSaveKey;
        Check(ShowcaseService.ApplyDisplaySnapshot(new[] { 100 }, true) && SavesSystem.FailWrite != null, "identical snapshot does not touch the save");
        SavesSystem.FailWrite = null;
        SavesSystem.FailReadback = BackMountainConfig.ShowcaseSaveKey;
        Check(!ShowcaseService.ApplyDisplaySnapshot(new[] { 100, 200 }, true) && ShowcaseService.DisplayedCount == 1
            && SavesSystem.Load<string>(BackMountainConfig.ShowcaseSaveKey) == before, "failed write restores list and official cache");
        Check(ShowcaseService.ApplyDisplaySnapshot(new[] { 100, 200 }, true) && Near(ShowcaseService.CalculateBonus(), .025f) && Near(Stat("MaxHealth"), 102.5f),
            "retry applies both trophies immediately");
        Check(CampaignBaseObjectives.IsDone(CampaignObjectiveKind.TrophyDisplayed), "campaign trophy_displayed fact reads the cached display");
        Check(ShowcaseService.ApplyDisplaySnapshot(new int[0], true) && ShowcaseService.DisplayedCount == 0 && Near(Stat("MaxHealth"), 100), "emptying the racks removes the bonus");
        SavesSystem.Switch(1); Check(ShowcaseService.DisplayedCount == 0 && ShowcaseService.SourceVersion == 1 && Near(Stat("MaxHealth"), 100), "slot change clears display and bonus");
        // 老登记簿：非基地不覆盖、无柜不覆盖、基地有柜才覆盖
        SavesSystem.Save(BackMountainConfig.ShowcaseSaveKey, "{\"schemaVersion\":1,\"displayedTypeIds\":[200]}"); ShowcaseService.NotifySlotChanged();
        Check(ShowcaseService.SourceVersion == 1 && ShowcaseService.DisplayedCount == 1 && Near(ShowcaseService.CalculateBonus(), .02f), "legacy ledger without sourceVersion still counts");
        LevelManager.Instance.IsBaseLevel = false;
        Check(!ShowcaseService.ApplyDisplaySnapshot(new int[0], true) && ShowcaseService.DisplayedCount == 1, "legacy ledger is not overwritten outside base");
        LevelManager.Instance.IsBaseLevel = true;
        Check(!ShowcaseService.ApplyDisplaySnapshot(new int[0], false) && ShowcaseService.DisplayedCount == 1, "legacy ledger is not overwritten when no official showcase exists");
        Check(ShowcaseService.ApplyDisplaySnapshot(new[] { 100 }, true) && ShowcaseService.SourceVersion == 2 && ShowcaseService.GetDisplayed()[0] == 100, "legacy ledger migrates to official display at base");
        foreach (var raw in new[] { "{", "", "{\"schemaVersion\":2,\"displayedTypeIds\":[]}", "{\"schemaVersion\":1,\"displayedTypeIds\":[1,2,3,4,5,6,7,8,9]}",
            "{\"schemaVersion\":1,\"displayedTypeIds\":[100,100]}", "{\"schemaVersion\":1,\"sourceVersion\":\"x\",\"displayedTypeIds\":[]}" })
        {
            SavesSystem.Save(BackMountainConfig.ShowcaseSaveKey, raw); ShowcaseService.NotifySlotChanged();
            Check(!ShowcaseService.IsReadable && !ShowcaseService.ApplyDisplaySnapshot(new[] { 100 }, true)
                && SavesSystem.Load<string>(BackMountainConfig.ShowcaseSaveKey) == raw, "bad/unknown/oversized collection is never overwritten");
        }
    }
    static void GardenSite()
    {
        // ---- 判据穷举 ----
        string reason;
        Check(!GardenSiteJudges.ShouldOpenSite(false, true, true, true, true, true, false, false, out reason) && reason == "module_disabled", "disabled module never opens");
        Check(!GardenSiteJudges.ShouldOpenSite(true, false, true, true, true, true, false, false, out reason) && reason == "garden_locked", "locked garden never opens");
        Check(!GardenSiteJudges.ShouldOpenSite(true, true, false, true, true, true, false, false, out reason) && reason == "not_base_scene", "only at base");
        Check(!GardenSiteJudges.ShouldOpenSite(true, true, true, false, true, true, false, false, out reason) && reason == "crops_not_injected", "crops must be injected first");
        Check(!GardenSiteJudges.ShouldOpenSite(true, true, true, true, false, false, false, false, out reason) && reason == "site_not_found", "missing site fails closed");
        Check(!GardenSiteJudges.ShouldOpenSite(true, true, true, true, true, false, false, false, out reason) && reason == "interact_parent_not_found", "missing parent fails closed");
        Check(!GardenSiteJudges.ShouldOpenSite(true, true, true, true, true, true, true, false, out reason) && reason == "already_open", "official already open is a no-op");
        Check(!GardenSiteJudges.ShouldOpenSite(true, true, true, true, true, true, false, true, out reason) && reason == "already_built", "already built is a no-op");
        Check(GardenSiteJudges.ShouldOpenSite(true, true, true, true, true, true, false, false, out reason) && reason == null, "all conditions met opens the site");
        Check(GardenSiteJudges.IsGardenBuilt(true, false, false) && !GardenSiteJudges.IsGardenBuilt(false, false, false)
            && !GardenSiteJudges.IsGardenBuilt(false, true, false) && GardenSiteJudges.IsGardenBuilt(false, true, true), "garden built = live garden or official key true");
        Check(GardenSiteJudges.BuildCostText(5000, null, true) == "5000 金" && GardenSiteJudges.BuildCostText(0, "木头 x1", true) == "木头 x1"
            && GardenSiteJudges.BuildCostText(0, null, false) == "free" && GardenSiteJudges.BuildCostText(10, "Bolt x9", false) == "10, Bolt x9", "cost text");
        string metrics;
        Check(!GardenSiteJudges.EvaluateSiteGate(false, false, false, false, false, false, false, out metrics, out reason) && metrics.Contains("site_found=False"), "missing site is red");
        Check(GardenSiteJudges.EvaluateSiteGate(false, false, false, false, true, true, true, out metrics, out reason), "built garden without site is fine");
        Check(!GardenSiteJudges.EvaluateSiteGate(true, true, false, true, false, true, true, out metrics, out reason), "unlocked, injected but still closed is red");
        Check(GardenSiteJudges.EvaluateSiteGate(true, true, false, true, false, false, true, out metrics, out reason), "locked and closed is expected");

        // ---- 模块接线：开门排在注入之后、带解锁与注入状态、提供者登记 / 撤销、飘字延后 ----
        Reset(); StartModule(); CampaignFacilityUnlocks.Tokens.Add("Ch1"); LevelManager.Ready();
        Check(GardenConstructionSite.OpenCalls > 0 && GardenConstructionSite.LastUnlocked && GardenConstructionSite.LastInBase && GardenConstructionSite.LastCropsInjected,
            "level ready tries to open the site after crops are injected with the real unlock state");
        Check(CampaignBaseObjectives.Providers.ContainsKey(CampaignObjectiveKind.GardenBuilt) && CampaignBaseObjectives.Providers.ContainsKey(CampaignObjectiveKind.TrophyDisplayed),
            "module registers both campaign base-objective providers");
        GardenConstructionSite.Built = true;
        Check(CampaignBaseObjectives.IsDone(CampaignObjectiveKind.GardenBuilt), "garden_built fact flows through the provider");
        GardenConstructionSite.PendingNotice = "opened"; DialogueManager.IsDialogueActive = true; module.OnUpdate(0, 0);
        Check(GardenConstructionSite.PendingNotice == "opened", "notice waits while a dialogue is active");
        DialogueManager.IsDialogueActive = false; module.OnUpdate(0, 0);
        Check(GardenConstructionSite.PendingNotice == null, "notice flushes once the dialogue ends");
        int scans = ShowcaseDisplayScanner.RefreshCalls;
        for (int i = 0; i < 10000; i++) module.OnUpdate(.016f, .016f);
        Check(ShowcaseDisplayScanner.RefreshCalls == scans && GardenConstructionSite.OpenCalls <= 3, "steady updates neither rescan showcases nor retry the site");
        ModBehaviour.Instance.Enabled = false; module.OnUpdate(0, 0);
        Check(!CampaignBaseObjectives.Providers.ContainsKey(CampaignObjectiveKind.GardenBuilt) && ShowcaseDisplayScanner.ClearCalls > 0, "disabling withdraws providers and showcase subscriptions");
        ModBehaviour.Instance.Enabled = true; module.OnUpdate(0, 0);
        Check(CampaignBaseObjectives.Providers.ContainsKey(CampaignObjectiveKind.GardenBuilt), "re-enabling registers providers again");
        module.OnDestroy(); module = null;
        Check(!CampaignBaseObjectives.Providers.ContainsKey(CampaignObjectiveKind.TrophyDisplayed), "destroy withdraws providers");
    }
    static int Main()
    {
        try
        {
            RegistrationAndUsage(); MealLifecycle(); MealFailures(); Facilities(); Showcase(); GardenSite();
            if (module != null) module.OnDestroy();
            Console.WriteLine("BackMountainLifecycle PASS: " + assertions + " assertions"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
