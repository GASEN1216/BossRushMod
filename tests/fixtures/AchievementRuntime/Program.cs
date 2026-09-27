using System;
using System.Linq;
using BossRush;
using Duckov.Economy;
using Saves;
using UnityEngine;

internal static class Program
{
    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static ModBehaviour New(bool initialize = true)
    {
        Assert(Health.HurtSubscribers == 0 && LevelManager.Subscribers == 0 && InteractablePickup.Subscribers == 0
            && BossRushEventBus.Count == 0 && SavesSystem.CollectSubscribers == 0 && SavesSystem.SlotSubscribers == 0,
            "Previous owner must release all global subscriptions");
        Probe.Trace.Clear();
        Probe.Unlocks.Clear();
        Probe.HotkeyReads = 0;
        Probe.ThrowManagerInitialization = false;
        AchievementTracker.HasTakenDamage = AchievementTracker.HasUsedHealItem = AchievementTracker.HasPickedUpItem = false;
        AchievementTracker.TotalBossKills = AchievementTracker.TotalDragonKingKills = AchievementTracker.TotalClears = 0;
        AchievementTracker.ArenaEnterTime = 10;
        AchievementTracker.Elapsed = 60;
        ModeGRuntimeGates.DamageWindow = false;
        CharacterMainControl.Main = new CharacterMainControl("player");
        Input.Pressed = false;
        Duckov.UI.View.ActiveView = null;
        var host = new ModBehaviour();
        host.modeDRuntime = new ModeDRuntimeModule();
        host.wavesArenaRuntime = new WavesArenaRuntimeModule { BossesPerWave = 1 };
        host.runtimeModuleHost = new FixtureModuleHost();
        host.config = new FixtureConfig { Hotkey = (int)KeyCode.P };
        host.BindProductionAchievementForFixture();
        Assert(ReferenceEquals(host.achievementRuntime, host.runtimeModuleHost.Registered), "Production binding must register the same achievement owner");
        host.achievementRuntime.OnAwake(host);
        Assert(string.Join(",", Probe.Trace) == "register" && Probe.HotkeyReads == 0,
            "Registration and OnAwake must bind queries without initializing or sampling gameplay state");
        Probe.Trace.Clear();
        if (initialize) host.InitializeAchievementRuntime();
        return host;
    }

    private static void InitializationAndCleanup()
    {
        var host = New();
        Assert(string.Join(",", Probe.Trace) == "manager-initialize,popup-ensure,pickup-subscribe,level-subscribe,health-subscribe,view-ensure,popup-ensure,bus-subscribe,hurt-subscribe",
            "Initialization must retain original manager, two popup requests and subscription order");
        BossRushEventBus.Publish(new BossRushAchievementUnlockedEvent());
        Assert(Probe.Trace.Last() == "popup-show", "Unlock notifications must reach the production popup callback");
        host.achievementRuntime.SubscribeMedalStockEvents();
        host.achievementRuntime.SubscribeMedalStockEvents();
        Assert(SavesSystem.CollectSubscribers == 1 && SavesSystem.SlotSubscribers == 1, "Medal stock subscriptions must have one module owner");
        Probe.Trace.Clear();
        host.CleanupProductionAchievementSlotForFixture();
        Assert(string.Join(",", Probe.Trace) == "hurt-unsubscribe,bus-unsubscribe,level-unsubscribe,pickup-unsubscribe,health-unsubscribe,view-shutdown,popup-shutdown,manager-reset,icons-reset",
            "Actual achievement unsubscription must precede manager and icon reset at the original host slot");
        Assert(SavesSystem.CollectSubscribers == 1 && SavesSystem.SlotSubscribers == 1,
            "Achievement cleanup must leave medal subscription timing to the original Integration slot");
        host.achievementRuntime.UnsubscribeMedalStockEvents();
        Probe.Trace.Clear();
        host.achievementRuntime.OnDestroy();
        host.achievementRuntime.OnDestroy();
        Assert(Probe.Trace.Count == 0, "Registry destruction must not repeat completed cleanup or medal unsubscription");

        host = New(false);
        Probe.ThrowManagerInitialization = true;
        host.InitializeAchievementRuntime();
        Assert(string.Join(",", Probe.Trace) == "manager-initialize,view-ensure,popup-ensure,bus-subscribe,hurt-subscribe",
            "Inner initialization failure must retain the original outer view and event initialization path");
        host.achievementRuntime.OnDestroy();
    }

    private static void SessionsAndKills()
    {
        var host = New();
        var boss = new CharacterMainControl("dragon_king");
        Assert(host.KillForFixture(boss) && !host.KillForFixture(boss) && AchievementTracker.TotalBossKills == 1,
            "Boss reference callbacks must count once per legacy session");
        host.BeginSessionForFixture();
        Assert(host.KillForFixture(boss) && AchievementTracker.TotalBossKills == 2,
            "Legacy session reset must clear counted boss references");
        host.ClearForFixture();
        Assert(Probe.Unlocks.Contains("easy_clear") && Probe.Unlocks.Contains("speedrun_1min") && Probe.Unlocks.Contains("speedrun_5min"),
            "Clear achievements must consume current difficulty and the existing speedrun thresholds");
        host.wavesArenaRuntime.BossesPerWave = 3;
        Probe.Unlocks.Clear();
        host.ClearForFixture();
        Assert(Probe.Unlocks.Contains("normal_clear") && !Probe.Unlocks.Contains("easy_clear"), "Difficulty query must remain live after binding");
        AchievementTracker.HasUsedHealItem = true;
        AchievementTracker.HasPickedUpItem = true;
        AchievementTracker.HasTakenDamage = true;
        float startTime = AchievementTracker.ArenaEnterTime;
        host.BeginModeGAchievementSession();
        Assert(!AchievementTracker.HasTakenDamage && AchievementTracker.HasUsedHealItem && AchievementTracker.HasPickedUpItem
            && AchievementTracker.ArenaEnterTime == startTime, "Mode G reset must only clear flawless state without resetting the legacy session");
        AchievementTracker.HasTakenDamage = true;
        Probe.Unlocks.Clear();
        int kills = AchievementTracker.TotalBossKills;
        host.ReportModeGBossKillAchievement(7, "DragonKing", true);
        host.ReportModeGBossKillAchievement(7, "DragonKing", true);
        Assert(AchievementTracker.TotalBossKills == kills + 1 && Probe.Unlocks.Contains("kill_dragon_king_flawless"),
            "Mode G reports must deduplicate triplets and consume the supplied flawless snapshot");
        host.ReportModeGBossKillAchievement(7, "DragonKing", false);
        host.ReportModeGBossKillAchievement(7, "DragonDescendant", false);
        Assert(AchievementTracker.TotalBossKills == kills + 3, "Mode G deduplication must retain boss type and snapshot in its key");
        host.EndModeGAchievementSession();
        host.ReportModeGBossKillAchievement(8, "DragonKing", true);
        Assert(AchievementTracker.TotalBossKills == kills + 3, "Closed Mode G sessions must reject delayed reports");
        host.achievementRuntime.OnDestroy();
    }

    private static void PlayerEventsAndHotkey()
    {
        var host = New();
        Health player = CharacterMainControl.Main.Health;
        Health.Hurt(player, 1);
        Assert(!AchievementTracker.HasTakenDamage, "Inactive modes must not consume damage events");
        ModeGRuntimeGates.DamageWindow = true;
        Health.Hurt(new CharacterMainControl("other").Health, 2);
        Assert(!AchievementTracker.HasTakenDamage, "Non-player health must not consume the achievement damage window");
        Health.Hurt(player, 2);
        Assert(AchievementTracker.HasTakenDamage, "Mode G damage window must track the observed main player");
        InteractablePickup.Pick(CharacterMainControl.Main);
        Assert(!AchievementTracker.HasPickedUpItem, "Pickup tracking must remain Mode D gated");
        host.modeDRuntime.IsActive = true;
        InteractablePickup.Pick(CharacterMainControl.Main);
        Assert(AchievementTracker.HasPickedUpItem, "Mode D query must remain live for pickup events");
        player.CurrentHealth = 80;
        player.OnHealthChange.Invoke(player);
        Assert(!AchievementTracker.HasUsedHealItem, "Healing tracking must remain infinite hell gated");
        host.wavesArenaRuntime.InfiniteHellMode = true;
        player.OnHealthChange.Invoke(player);
        player.CurrentHealth = 90;
        player.OnHealthChange.Invoke(player);
        Assert(AchievementTracker.HasUsedHealItem, "Observed health increases must record healing after the original baseline update");
        CharacterMainControl previous = CharacterMainControl.Main;
        CharacterMainControl.Main = new CharacterMainControl("replacement");
        LevelManager.Rebind();
        LevelManager.Rebind();
        Assert(player.OnHealthChange.Count == 0 && CharacterMainControl.Main.Health.OnHealthChange.Count == 1,
            "Live player replacement must detach the old health and bind the new health once");
        UnityEngine.Object.Destroy(CharacterMainControl.Main.gameObject);
        Assert(CharacterMainControl.Main.Health == null, "Scene object destruction must cascade into its health component");
        CharacterMainControl.Main = new CharacterMainControl("after scene");
        LevelManager.Rebind();
        Assert(CharacterMainControl.Main.Health.OnHealthChange.Count == 1, "Destroyed scene health must not prevent rebinding the next player");
        Input.Pressed = true;
        Probe.Trace.Clear();
        host.achievementRuntime.OnUpdate(1, 2);
        Assert(Input.LastKey == KeyCode.P && Probe.HotkeyReads == 2 && Probe.Trace.SequenceEqual(new[] { "toggle" }),
            "Configured hotkey must preserve both original reads and toggle when no view is active");
        Duckov.UI.View.ActiveView = new Duckov.UI.View();
        Probe.Trace.Clear();
        host.achievementRuntime.OnUpdate(1, 2);
        Assert(Probe.Trace.Count == 0, "Active official views must suppress the achievement hotkey");
        host.config = null;
        Duckov.UI.View.ActiveView = null;
        host.achievementRuntime.OnUpdate(1, 2);
        Assert(Input.LastKey == KeyCode.L, "Missing configuration must preserve the L fallback");
        host.achievementRuntime.OnDestroy();
    }

    private static StockShop Shop() { return new StockShop(); }

    private static void MedalStock()
    {
        var host = New();
        host.achievementRuntime.SubscribeMedalStockEvents();
        SavesSystem.ThrowRead = SavesSystem.ThrowWrite = false;
        SavesSystem.Data.Clear();
        SavesSystem.Data[AchievementMedalConfig.STOCK_SAVE_KEY] = 4;
        SavesSystem.SwitchSlot();
        ItemStatsSystem.ItemAssetsCollection.Prefab = new ItemStatsSystem.Item { RawValue = 20 };
        var shop = Shop();
        Assert(host.TryInjectAchievementMedalIntoShop(shop), "Eligible shop must receive its first medal entry");
        var entry = shop.entries.Single();
        Assert(entry.CurrentStock == 4 && entry.Definition.priceFactor == 0.05f && entry.Show,
            "Medal injection must preserve saved stock, unit price and visibility");
        Assert(!host.TryInjectAchievementMedalIntoShop(shop) && shop.entries.Count == 1, "Repeated shop injection must reuse the existing medal entry");
        entry.CurrentStock = 2;
        SavesSystem.Collect();
        Assert(SavesSystem.Data[AchievementMedalConfig.STOCK_SAVE_KEY] == 2, "Save event must persist the current injected entry stock");
        SavesSystem.Data[AchievementMedalConfig.STOCK_SAVE_KEY] = 8;
        SavesSystem.SwitchSlot();
        shop = Shop();
        host.TryInjectAchievementMedalIntoShop(shop);
        Assert(shop.entries.Single().CurrentStock == 8, "Slot switch must discard cached stock and entry identity before the next injection");
        SavesSystem.ThrowWrite = true;
        shop.entries.Single().CurrentStock = 3;
        SavesSystem.Collect();
        Assert(SavesSystem.Data[AchievementMedalConfig.STOCK_SAVE_KEY] == 8, "Failed stock save must preserve the previously persisted value");
        SavesSystem.ThrowWrite = false;
        SavesSystem.SwitchSlot();
        SavesSystem.ThrowRead = true;
        shop = Shop();
        host.TryInjectAchievementMedalIntoShop(shop);
        Assert(shop.entries.Single().CurrentStock == AchievementMedalConfig.DEFAULT_MAX_STOCK, "Read failure must retain the original default stock fallback");
        SavesSystem.ThrowRead = false;
        var skipped = Shop();
        skipped.Eligible = false;
        Assert(!host.TryInjectAchievementMedalIntoShop(skipped) && skipped.entries.Count == 0, "Shop policy must be queried before injection");
        ObjectCache.Shops = new[] { Shop() };
        host.InjectMedalsForFixture("non-base");
        Assert(ObjectCache.Shops[0].entries.Count == 0, "Non-base scenes must not scan and inject medal shops");
        host.InjectMedalsForFixture("Base_SceneV2");
        Assert(ObjectCache.Shops[0].entries.Count == 1, "Original base scene must inject through the production scan path");
        host.achievementRuntime.UnsubscribeMedalStockEvents();
        host.achievementRuntime.OnDestroy();
    }

    public static void Main()
    {
        InitializationAndCleanup();
        SessionsAndKills();
        PlayerEventsAndHotkey();
        MedalStock();
        Console.WriteLine("PASS AchievementRuntime");
    }
}
