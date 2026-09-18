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
        Reset(); Item(100, 5); Item(200, 8); Player(); StartModule(); CampaignFacilityUnlocks.Grant(2);
        Check(!ShowcaseService.TryDisplay(BossRushItemIds.DragonFruit), "raw ID cannot bypass meal exclusion");
        Check(!ShowcaseService.TryDisplay(9999), "raw unknown ID cannot create record");
        Check(ShowcaseService.TryDisplay(ItemAssetsCollection.GetPrefab(100)), "eligible trophy records without consumption");
        Check(Near(ShowcaseService.CalculateBonus(), .005f) && ItemAssetsCollection.GetPrefab(100) != null, "record leaves trophy and grants quality bonus");
        string reason; SavesSystem.FailReadback = BackMountainConfig.ShowcaseSaveKey;
        Check(!ShowcaseService.TryReplaceRecord(100, ItemAssetsCollection.GetPrefab(200), out reason), "failed replacement rejected");
        ShowcaseService.NotifySlotChanged(); Check(ShowcaseService.GetDisplayed()[0] == 100, "failed replacement remains old after reload");
        Check(ShowcaseService.TryReplaceRecord(100, ItemAssetsCollection.GetPrefab(200), out reason) && Near(ShowcaseService.CalculateBonus(), .02f), "replacement upgrades bonus");
        SavesSystem.Switch(1); Check(ShowcaseService.DisplayedCount == 0 && Near(Stat("MaxHealth"), 100), "slot change clears collection and bonus");
        foreach (var raw in new[] { "{", "", "{\"schemaVersion\":2,\"displayedTypeIds\":[]}", "{\"schemaVersion\":1,\"displayedTypeIds\":[1,2,3,4,5,6,7,8,9]}", "{\"schemaVersion\":1,\"displayedTypeIds\":[100,100]}" })
        {
            SavesSystem.Save(BackMountainConfig.ShowcaseSaveKey, raw); ShowcaseService.NotifySlotChanged();
            Check(!ShowcaseService.IsReadable && !ShowcaseService.TryDisplay(100)
                && SavesSystem.Load<string>(BackMountainConfig.ShowcaseSaveKey) == raw, "bad/unknown/oversized collection is never overwritten");
        }
    }
    static void ModalLifecycle()
    {
        Reset(); StartModule(); ShowcaseUI.Open();
        Check(ShowcaseUI.IsOpen && ZombieModeUIHelper.Leases == 1, "open acquires one modal input lease");
        var previous = ShowcaseUI.Root; ShowcaseUI.Open();
        Check(previous == null && ZombieModeUIHelper.Leases == 1, "refresh releases old lease before reacquiring");
        ShowcaseUI.NotifyDestroyed(previous);
        Check(ShowcaseUI.IsOpen && ZombieModeUIHelper.Leases == 1, "late old canvas destroy cannot close successor");
        UnityEngine.Object.Destroy(ShowcaseUI.Root);
        Check(!ShowcaseUI.IsOpen && ZombieModeUIHelper.Leases == 0, "Unity scene destruction releases input");
        ShowcaseUI.Open(); Input.Escape = true; module.OnUpdate(0, 0);
        Check(!ShowcaseUI.IsOpen && ZombieModeUIHelper.Leases == 0, "host tick drives Esc close");
        ShowcaseUI.Open(); L10n.Chinese = !L10n.Chinese; module.OnUpdate(0, 0);
        Check(ShowcaseUI.IsOpen && ZombieModeUIHelper.Leases == 1, "language refresh keeps a single lease");
        SavesSystem.Switch(3);
        Check(!ShowcaseUI.IsOpen && ZombieModeUIHelper.Leases == 0, "slot change closes modal and restores input");
        ShowcaseUI.Open(); module.OnDestroy(); module = null;
        Check(!ShowcaseUI.IsOpen && ZombieModeUIHelper.Leases == 0, "module destroy releases modal");
    }
    static int Main()
    {
        try
        {
            RegistrationAndUsage(); MealLifecycle(); MealFailures(); Facilities(); Showcase(); ModalLifecycle();
            if (module != null) module.OnDestroy();
            Console.WriteLine("BackMountainLifecycle PASS: " + assertions + " assertions"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
