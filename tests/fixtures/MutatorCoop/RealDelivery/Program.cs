using System;
using BossRush;

public static class RealDeliveryProbe
{
    private static int checks;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS RealDelivery " + name); }

    public static void Run()
    {
        checks = 0;
        KillOwner("stable"); KillOwner("A-to-B"); KillOwner("A-to-B-to-A"); KillOwner("same-asset-new-epoch");
        FrozenDispatchReceipt(); OriginalAssetReceipt();
        Console.WriteLine("MutatorCoop real delivery: " + checks + " PASS (actual module, manager, receipt, codec)");
    }

    private static void Reset()
    {
        MutatorManager.RemoveAll(); BossRushCoop.Players.Clear(); BossRushCoop.Entities.Clear(); BossRushCoop.ClearWire();
        BossRushCoop.IsConnected = true; BossRushCoop.IsHost = true;
        BossRushCoop.HasBoundScene = true; BossRushCoop.SceneToken = "same-native-Core";
        BossRushCoop.SceneGeneration = 1; BossRushCoop.RunId = "same-room";
        BossRushCoop.LocalPlayerId = "p0"; BossRushUI.Paused = false;
        BossRushCoop.BeforeLocalAssetRead = null;
        LevelManager.Instance = new LevelManager();
    }

    private static void KillOwner(string variant)
    {
        Reset();
        var owner = new ModBehaviour(); var source = new CharacterMainControl(); CharacterMainControl.Main = source;
        var player = new BossRushCoopPlayer { Id = "p0", IsLocal = true, AssetId = "asset-A", ReadyEpoch = "epoch-A1", Character = source };
        BossRushCoop.Players.Add(player);
        var module = new MutatorCoopRuntimeModule(); module.OnAwake(owner); module.OnUpdate(0, 0);
        MutatorManager.RollAndApply(source, 28, 15, "ModeF"); MutatorCoopRuntimeModule.NotifyRolled("ModeF");
        source.Health.CurrentHealth = 1;
        new CharacterMainControl().Health.ReportDead(new DamageInfo(source));
        // Change identity after the real death callback yields and before its
        // actual continuation. The native body, token and Core generation stay.
        if (variant == "A-to-B" || variant == "A-to-B-to-A") { player.AssetId = "asset-B"; player.ReadyEpoch = "epoch-B"; }
        if (variant == "A-to-B-to-A") { player.AssetId = "asset-A"; player.ReadyEpoch = "epoch-A2"; }
        if (variant == "same-asset-new-epoch") player.ReadyEpoch = "epoch-reconnected";
        Check(player.Character == source && BossRushCoop.SceneToken == "same-native-Core" && BossRushCoop.SceneGeneration == 1,
            variant + " keeps the actual source body and scene");
        owner.Advance();
        bool stable = variant == "stable";
        Check(stable ? Math.Abs(source.Health.CurrentHealth - (1 + source.Health.MaxHealth * .24f)) < .001f : source.Health.CurrentHealth == 1,
            variant + " original heal belongs only to the frozen owner incarnation");
        Check(LevelManager.Instance.ExplosionManager.Calls == (stable ? 1 : 0),
            variant + " old-owner world continuation is cancelled without changing valid explosion count");
        module.OnDestroy(); MutatorManager.RemoveAll();
    }

    [Serializable] private sealed class Reward { public int Quantity; }
    private static void FrozenDispatchReceipt()
    {
        Reset(); var owner = new ModBehaviour(); var source = new CharacterMainControl(); CharacterMainControl.Main = source;
        var player = new BossRushCoopPlayer { Id = "p0", IsLocal = true, AssetId = "asset-A", ReadyEpoch = "epoch-A1", Character = source };
        BossRushCoop.Players.Add(player);
        var module = new MutatorCoopRuntimeModule(); module.OnAwake(owner); module.OnUpdate(0, 0);
        MutatorManager.RollAndApply(source, 28, 15, "ModeF"); MutatorCoopRuntimeModule.NotifyRolled("ModeF");
        source.Health.CurrentHealth = 1; new CharacterMainControl().Health.ReportDead(new DamageInfo(source));
        BossRushCoop.BeforeLocalAssetRead = () => { player.AssetId = "asset-B"; player.ReadyEpoch = "epoch-B"; };
        owner.Advance();
        var field = typeof(MutatorCoopRuntimeModule).GetField("kills", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var delivery = (CoopPersonalDelivery<CoopMutatorKill>)field.GetValue(module);
        var queued = (System.Collections.IDictionary)delivery.GetType().GetField("pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(delivery);
        Check(player.AssetId == "asset-B" && queued.Count == 1 && source.Health.CurrentHealth == 1,
            "real Deliver metadata refresh cannot redirect validated asset-A continuation into B");
        Check(LevelManager.Instance.ExplosionManager.Calls == 0,
            "delivery-time identity refresh also cancels the old-owner explosion continuation");
        foreach (object pending in queued.Values)
        {
            var receipt = (CoopPersonalReceipt)pending.GetType().GetField("Receipt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(pending);
            Check(receipt.TargetAssetId == "asset-A", "actual DispatchKill receipt explicitly freezes asset-A at production delivery boundary");
        }
        module.OnDestroy(); MutatorManager.RemoveAll();
    }

    private static void OriginalAssetReceipt()
    {
        Reset(); var body = new CharacterMainControl(); CharacterMainControl.Main = body;
        var player = new BossRushCoopPlayer { Id = "p0", IsLocal = true, AssetId = "asset-B", ReadyEpoch = "epoch-B", Character = body };
        BossRushCoop.Players.Add(player); int committed = 0;
        var delivery = new CoopPersonalDelivery<Reward>("original-asset", reward => { committed += reward.Quantity; return true; });
        Check(delivery.Deliver("p0", "already-earned-fact", new Reward { Quantity = 5 }, "asset-A"), "explicit original asset receipt is queued");
        Check(committed == 0 && delivery.HasPendingDeliveries, "real delivery refuses to redirect earned asset-A quantity to B");
        BossRushCoop.HasBoundScene = false; BossRushCoop.SceneToken = null; delivery.Tick(2);
        Check(committed == 0 && delivery.HasPendingDeliveries, "loading and retries preserve the original asset debt");
        player.AssetId = "asset-A"; player.ReadyEpoch = "epoch-A2"; delivery.Tick(2);
        Check(committed == 5 && !delivery.HasPendingDeliveries, "permanent original asset receipt survives incarnation change and unbound scene");
        delivery.Deliver("p0", "already-earned-fact", new Reward { Quantity = 5 }, "asset-A"); delivery.Tick(2);
        Check(committed == 5, "actual original asset journal deduplicates retries"); delivery.Dispose();
    }
}
