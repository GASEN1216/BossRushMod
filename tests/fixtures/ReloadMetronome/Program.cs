using System;
using BossRush;
using Duckov.Utilities;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static CharacterMainControl player;
    private static ItemAgent_Gun gun;

    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }

    private static void Fresh()
    {
        ReloadMetronomeRuntime.Unsubscribe();
        Check(CharacterMainControl.Subscribers == 0 && SceneLoader.Subscribers == 0, "subscriptions released");
        GameObject.DestroyScene();
        player = new GameObject().Add<CharacterMainControl>();
        CharacterMainControl.Main = player;
        gun = new GameObject().Add<ItemAgent_Gun>();
        gun.Holder = player;
        player.CurrentHoldItemAgent = gun;
        LevelManager.Instance = new LevelManager();
        SceneLoader.IsSceneLoading = false;
        NewWeaponEquipState.Equipped = true;
        BossRushUI.Paused = BossRushUI.Hidden = false;
        NPCShopSystem.AffinityLevel = 5;
        ItemStatsSystem.ItemAssetsCollection.Missing = false;
        ItemStatsSystem.Inventory.DeliveryAllowed = true;
        ItemStatsSystem.Inventory.NotificationFault = 0;
        SkyIslandInventoryTransaction.OnReserve = null;
        BossRushConfirmDialog.Last = null;
        ReloadMetronomeHud.Created = 0;
        ReloadMetronomeRuntime.Subscribe();
        ReloadMetronomeRuntime.Subscribe();
        Check(CharacterMainControl.Subscribers == 2 && SceneLoader.Subscribers == 1, "subscribe is idempotent");
    }

    private static void Begin()
    { gun.Reloading = true; gun.Timer = 0f; ReloadMetronomeBeginPatch.Postfix(gun, true); }
    private static void Press(float at)
    { gun.Timer = at; ReloadMetronomeInputPatch.Prefix(player, null); }
    private static void Tick()
    { ReloadMetronomeFinishPatch.Prefix(gun, ref gun.Timer); }

    private static AimMarker CreateMarker()
    {
        var marker = new GameObject().Add<AimMarker>();
        marker.aimMarkerUI = new GameObject().transform;
        return marker;
    }

    private static void Presentation()
    {
        Fresh(); Begin();
        var marker = CreateMarker();
        ReloadMetronomeHudPatch.Postfix(marker);
        Check(ReferenceEquals(ReloadMetronomeHud.Last.Parent, marker.aimMarkerUI),
            "HUD must follow the moving crosshair UI, not the stationary component root");
        ReloadMetronomeHudPatch.Postfix(marker);
        Check(ReloadMetronomeHud.Created == 1, "repeated presentation does not duplicate HUD");
        Fresh(); Begin();
        var unavailable = new GameObject().Add<AimMarker>();
        ReloadMetronomeHudPatch.Postfix(unavailable);
        Check(ReloadMetronomeHud.Created == 0, "missing crosshair UI does not allocate an orphan HUD");
        unavailable.aimMarkerUI = new GameObject().transform;
        ReloadMetronomeHudPatch.Postfix(unavailable);
        Check(ReloadMetronomeHud.Created == 1, "crosshair UI readiness allows a later HUD attempt");
    }

    private static void Timing()
    {
        foreach (float total in new[] { 0.75f, 1f, 2f, 8f })
        {
            float start, end;
            ReloadMetronomeRules.GetWindow(total, out start, out end);
            Check(start > 0f && end < total && end - start <= 0.30001f, "bounded reaction window");
            foreach (float at in new[] { start, end })
            {
                var rule = new ReloadMetronomeRules(); rule.Begin(total);
                Check(rule.Press(at, total) == ReloadMetronomeResult.Perfect, "inclusive edge");
                Check(rule.Press(at, total) == ReloadMetronomeResult.Inactive, "no second attempt");
            }
        }
        foreach (float total in new[] { 0f, 0.74f, float.NaN, float.PositiveInfinity })
        { var rule = new ReloadMetronomeRules(); rule.Begin(total); Check(!rule.Active, "invalid duration rejected"); }
        Fresh();
        ReloadMetronomeInputPatch.Prefix(player, null);
        Begin();
        Check(ReloadMetronomeRuntime.Result == ReloadMetronomeResult.Waiting, "starting key not consumed");
        Press(1.24f);
        Check(ReloadMetronomeRuntime.Result == ReloadMetronomeResult.Perfect, "on-beat request accepted");
        Check(gun.Timer == 1.24f && gun.BulletCount == 3 && player.InventoryAmmo == 20, "request does not mutate bullets");
        Tick();
        Check(gun.Timer == 2f && gun.BulletCount == 3 && player.InventoryAmmo == 20, "only original timer advances");
        gun.Timer = 0.4f; Tick(); Check(gun.Timer == 0.4f, "completion cannot replay");
        foreach (float missed in new[] { 0.2f, 1.8f })
        {
            Begin(); Press(missed); Press(1.24f); Tick();
            Check(gun.Timer == 1.24f, "early/late spam cannot rescue attempt");
        }
        Begin(); gun.Timer = 1.24f;
        ReloadMetronomeInputPatch.Prefix(player, new ItemStatsSystem.Item());
        Check(ReloadMetronomeRuntime.Result == ReloadMetronomeResult.Waiting, "ammo selection does not consume attempt");
    }

    private static void Lifetime()
    {
        foreach (Action cancel in new Action[] {
            () => CharacterMainControl.HoldChanged(),
            () => { NewWeaponEquipState.Equipped = false; CharacterMainControl.SlotChanged(); NewWeaponEquipState.Equipped = true; CharacterMainControl.SlotChanged(); },
            () => player.Health.Die(),
            () => SceneLoader.Start(),
            () => { LevelManager.Begin(); LevelManager.End(); },
            () => ReloadMetronomeCancelPatch.Postfix(gun),
            () => ReloadMetronomeRuntime.Unsubscribe()
        })
        {
            Fresh(); Begin();
            ReloadMetronomeHudPatch.Postfix(CreateMarker());
            var visual = ReloadMetronomeHud.Last;
            Press(1.24f); cancel(); Tick();
            Check(gun.Timer == 1.24f && ReloadMetronomeRuntime.Result == ReloadMetronomeResult.Inactive, "lifecycle cancels queued completion");
            Check(visual == null, "lifecycle destroys HUD graph");
        }
        foreach (Action cancelTail in new Action[] {
            () => CharacterMainControl.HoldChanged(),
            () => { NewWeaponEquipState.Equipped = false; CharacterMainControl.SlotChanged(); },
            () => player.Health.Die(), () => SceneLoader.Start(), () => ReloadMetronomeRuntime.Unsubscribe()
        })
        {
            Fresh(); Begin(); ReloadMetronomeHudPatch.Postfix(CreateMarker());
            var tail = ReloadMetronomeHud.Last;
            Press(1.24f); Tick(); float current, total;
            Check(!ReloadMetronomeRuntime.TryGetProgress(out current, out total), "natural completion closes input session");
            Check(tail != null && tail.Finished && tail.ShownResult == ReloadMetronomeResult.Perfect, "success visual can finish after original reload");
            cancelTail(); Check(tail == null, "hard cancellation also destroys finishing visual");
        }
        Fresh(); Begin(); Press(1.24f); SceneLoader.IsSceneLoading = true; Tick();
        Check(gun.Timer == 1.24f, "loading flag fallback blocks timer");
        Fresh(); Begin(); BossRushUI.Paused = true; Press(1.24f); Tick();
        Check(ReloadMetronomeRuntime.Result == ReloadMetronomeResult.Waiting, "pause ignores timing input");
        BossRushUI.Paused = false; BossRushUI.Hidden = true; Press(1.24f);
        Check(ReloadMetronomeRuntime.Result == ReloadMetronomeResult.Waiting, "inventory ignores timing input");
        BossRushUI.Hidden = false; Press(1.24f); BossRushUI.Paused = true; Tick();
        Check(gun.Timer == 1.24f, "pause defers queued completion");
        BossRushUI.Paused = false; Tick(); Check(gun.Timer == 2f, "unpause permits original completion");
        Fresh(); NewWeaponEquipState.Equipped = false; Begin();
        ReloadMetronomeHudPatch.Postfix(CreateMarker());
        Check(ReloadMetronomeHud.Created == 0, "inactive gear creates no HUD");
        NewWeaponEquipState.Equipped = true; CharacterMainControl.SlotChanged(); Press(1.24f); Tick();
        Check(gun.Timer == 1.24f, "equipping midway cannot grant chance");
        Fresh(); gun.GunItemSetting.reloadMode = ItemSetting_Gun.ReloadModes.singleBullet; Begin();
        Check(ReloadMetronomeRuntime.Result == ReloadMetronomeResult.Inactive, "single-round reload excluded");
        gun.GunItemSetting.reloadMode = ItemSetting_Gun.ReloadModes.fullMag; gun.ReloadTime = 0.5f; Begin();
        Check(ReloadMetronomeRuntime.Result == ReloadMetronomeResult.Inactive, "very fast reload excluded");
        Fresh(); Begin();
        var npcGun = new GameObject().Add<ItemAgent_Gun>(); npcGun.Holder = new GameObject().Add<CharacterMainControl>();
        ReloadMetronomeBeginPatch.Postfix(npcGun, true);
        Press(1.24f); float otherTimer = 0.5f;
        ReloadMetronomeFinishPatch.Prefix(npcGun, ref otherTimer);
        Check(otherTimer == 0.5f, "NPC reload cannot consume player finish");
        Tick(); Check(gun.Timer == 2f, "NPC begin preserves player reload");
        Fresh(); ReloadMetronomeBeginPatch.Postfix(gun, false);
        Check(ReloadMetronomeRuntime.Result == ReloadMetronomeResult.Inactive, "failed begin cannot arm");
    }

    private static void Shop()
    {
        foreach (int scene in new[] { 12, 1 })
        {
            Fresh(); player.gameObject.scene = scene; player.Shells = 40;
            var shop = new GameObject().Add<ReloadMetronomeShopInteractable>(); shop.gameObject.scene = scene;
            shop.Open(player);
            Check(BossRushConfirmDialog.Last != null && BossRushConfirmDialog.Last.ConfirmLabel.Contains("20"), "island/home offer same shell price");
            BossRushConfirmDialog.Last.OnConfirm();
            Check(player.Shells == 20 && player.Received == 1, "one item for twenty shells");
            shop.Open(player); ItemStatsSystem.Inventory.DeliveryAllowed = false; BossRushConfirmDialog.Last.OnConfirm();
            Check(player.Shells == 20 && player.Received == 1, "full pack returns reserved shells");
            ItemStatsSystem.Inventory.DeliveryAllowed = true;
            shop.Open(player); player.Health.IsDead = true; BossRushConfirmDialog.Last.OnConfirm();
            Check(player.Shells == 20 && player.Received == 1, "death during offer prevents purchase");
        }
        Fresh(); player.Shells = 19; string message;
        Check(!ReloadMetronomeTrade.TryExchange(player, () => true, out message) && player.Received == 0 && player.Shells == 19, "insufficient shells untouched");
        player.Shells = 40; ItemStatsSystem.ItemAssetsCollection.Missing = true;
        Check(!ReloadMetronomeTrade.TryExchange(player, () => true, out message) && player.Shells == 40, "missing prefab cannot charge");
        ItemStatsSystem.ItemAssetsCollection.Missing = false;
        bool valid = true; SkyIslandInventoryTransaction.OnReserve = () => valid = false;
        Check(!ReloadMetronomeTrade.TryExchange(player, () => valid, out message) && player.Shells == 40, "scene invalidation after reserve rolls back");
        SkyIslandInventoryTransaction.OnReserve = () => { string nested; Check(!ReloadMetronomeTrade.TryExchange(player, () => true, out nested), "transaction reentry rejected"); };
        Check(ReloadMetronomeTrade.TryExchange(player, () => true, out message) && player.Shells == 20 && player.Received == 1, "outer purchase succeeds exactly once");
        for (int fault = 1; fault <= 3; fault++)
        {
            Fresh(); player.Shells = 40; ItemStatsSystem.Inventory.NotificationFault = fault;
            bool paid = ReloadMetronomeTrade.TryExchange(player, () => true, out message);
            Check(paid == (fault > 1), "inventory exception uses actual delivery receipt");
            Check(player.Shells == (fault > 1 ? 20 : 40), "failed insertion refunds, successful insertion charges once");
            Check(player.CharacterItem.Inventory.Content.Count == (fault > 1 ? 1 : 0), "no duplicate after notification failure");
        }
        Fresh(); NPCShopSystem.AffinityLevel = 4;
        var locked = new GameObject().Add<ReloadMetronomeShopInteractable>(); locked.Open(player);
        Check(BossRushConfirmDialog.Last == null, "affinity four cannot open shop");
    }

    private static void IdleHotPath()
    {
        Fresh();
        var marker = CreateMarker();
        for (int i = 0; i < 100; i++) { Tick(); ReloadMetronomeHudPatch.Postfix(marker); }
        int queries = NewWeaponEquipState.Queries;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) { Tick(); ReloadMetronomeHudPatch.Postfix(marker); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "inactive gun and HUD patches allocate no managed memory after warmup");
        Check(NewWeaponEquipState.Queries == queries && ReloadMetronomeHud.Created == 0,
            "inactive frames neither query equipment nor create HUD objects");
    }

    private static int Main()
    {
        try { Timing(); Presentation(); Lifetime(); Shop(); IdleHotPath(); Console.WriteLine("ReloadMetronome: " + checks + " checks PASS"); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { ReloadMetronomeRuntime.Unsubscribe(); GameObject.DestroyScene(); }
    }
}
