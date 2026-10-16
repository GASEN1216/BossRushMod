using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static int passed;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
    private static object Call(object target, string name, params object[] values)
    { return target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, values); }
    private static void Main()
    {
        BossRushCoop.HasBoundScene = true; BossRushCoop.SceneToken = "mutator-scene-A";
        var owner = new ModBehaviour();
        var module = new MutatorCoopRuntimeModule(); module.OnAwake(owner);
        for (int i = 0; i < 16; i++) BossRushCoop.Players.Add(new BossRushCoopPlayer { Id = "p" + i,
            AssetId = "asset-" + i, ReadyEpoch = "owner-" + i, Character = new CharacterMainControl() });
        CharacterMainControl.Main = BossRushCoop.Players[0].Character;
        MutatorManager.RollAndApply(CharacterMainControl.Main, 28, 15, "ModeF");
        MutatorCoopRuntimeModule.NotifyRolled("ModeF");
        CoopMutatorWorld state = CoopGameplayChannel<CoopMutatorWorld>.LastPublished;
        Check(state.Active && state.Ids.Length == MutatorPool.All.Length, "host publishes every selected ID");
        Check(MutatorManager.BossRegenEnabled, "host applies boss regeneration");
        var enemy = new CharacterMainControl(); BossRushCoop.Entities["a:-18"] = enemy;
        MutatorManager.ApplyToEnemy(enemy);
        Check(enemy.CharacterItem.GetStat("WalkSpeed").Modifiers.Count > 0, "host enemy buffs execute");
        module.OnUpdate(0.3f, 0.3f); state = CoopGameplayChannel<CoopMutatorWorld>.LastPublished;
        Check(state.Enemies.Length == 1 && state.Enemies[0].Entity == "a:-18", "negative native AI ID retains scale");
        for (int i = 0; i < 16; i++)
        {
            var victim = new CharacterMainControl();
            victim.Health.ReportDead(new DamageInfo(BossRushCoop.Players[i].Character)); owner.Advance();
            Check(CoopPersonalDelivery<CoopMutatorKill>.Sent[i].Player == "p" + i, "kill is delivered to actual participant " + i);
            Check(LevelManager.Instance.ExplosionManager.Last.fromCharacter == BossRushCoop.Players[i].Character, "explosion source participant " + i);
            victim.Health.ReportDead(new DamageInfo(BossRushCoop.Players[i].Character)); owner.Advance();
        }
        Check(CoopPersonalDelivery<CoopMutatorKill>.Sent.Count == 16 && LevelManager.Instance.ExplosionManager.Calls == 16, "duplicate death does not replay heal/explosion");
        Check(LevelManager.Instance.ExplosionManager.Last.damageValue == 40 && LevelManager.Instance.ExplosionManager.Radius == 3 && LevelManager.Instance.ExplosionManager.HurtSelf, "original explosion numbers and self damage retained");
        new CharacterMainControl().Health.ReportDead(new DamageInfo(BossRushCoop.Players[1].Character) { isFromBuffOrEffect = true }); owner.Advance();
        Check(CoopPersonalDelivery<CoopMutatorKill>.Sent.Count == 16, "buff effect kills never recurse");
        BossRushCoop.Players[2].Character.Health.ReportDead(new DamageInfo(BossRushCoop.Players[1].Character)); owner.Advance();
        Check(CoopPersonalDelivery<CoopMutatorKill>.Sent.Count == 16, "participant death is not an enemy kill");
        var delayed = new CharacterMainControl(); delayed.Health.ReportDead(new DamageInfo(BossRushCoop.Players[1].Character));
        BossRushUI.Paused = true; owner.Advance(); Check(CoopPersonalDelivery<CoopMutatorKill>.Sent.Count == 16, "pause defers effects");
        BossRushUI.Paused = false; BossRushCoop.SceneGeneration++; owner.Advance();
        Check(CoopPersonalDelivery<CoopMutatorKill>.Sent.Count == 16, "scene change cancels delayed world effects");
        var replacement = new CharacterMainControl();
        delayed = new CharacterMainControl(); delayed.Health.ReportDead(new DamageInfo(BossRushCoop.Players[1].Character));
        BossRushCoop.Players[1].Character = replacement; owner.Advance();
        Check(CoopPersonalDelivery<CoopMutatorKill>.Sent.Count == 16, "replacement body cancels old scheduled kill");
        delayed = new CharacterMainControl(); delayed.Health.ReportDead(new DamageInfo(BossRushCoop.Players[1].Character));
        BossRushCoop.SceneToken = "mutator-native-next"; owner.Advance();
        Check(CoopPersonalDelivery<CoopMutatorKill>.Sent.Count == 16 && LevelManager.Instance.ExplosionManager.Calls == 16,
            "native token change cancels delayed kill and explosion even before public generation advances");
        BossRushCoop.HasBoundScene = false;
        new CharacterMainControl().Health.ReportDead(new DamageInfo(BossRushCoop.Players[1].Character)); owner.Advance();
        Check(CoopPersonalDelivery<CoopMutatorKill>.Sent.Count == 16 && LevelManager.Instance.ExplosionManager.Calls == 16,
            "unbound host cannot create a new world kill continuation");
        BossRushCoop.HasBoundScene = true;

        MutatorManager.RemoveAll(); BossRushCoop.IsHost = false; CharacterMainControl.Main = replacement;
        Call(module, "Apply", state);
        Check(MutatorManager.GetActiveMutators().Count == state.Ids.Length, "guest displays exact shared IDs");
        Check(!MutatorManager.BossRegenEnabled, "guest never enables world regeneration");
        int modifiers = replacement.CharacterItem.GetStat("WalkSpeed").Modifiers.Count;
        Check(modifiers > 0, "guest personal stats applied");
        Call(module, "Apply", state);
        Check(replacement.CharacterItem.GetStat("WalkSpeed").Modifiers.Count == modifiers, "snapshot replay never stacks personal stats");
        var guestEnemy = new CharacterMainControl(); MutatorManager.ApplyToEnemy(guestEnemy);
        Check(guestEnemy.CharacterItem.GetStat("WalkSpeed").Modifiers.Count == 0, "guest never applies enemy buffs");
        var respawn = new CharacterMainControl(); CharacterMainControl.Main = respawn;
        module.OnUpdate(0.01f, 0.01f);
        Check(replacement.CharacterItem.GetStat("WalkSpeed").Modifiers.Count == 0 && respawn.CharacterItem.GetStat("WalkSpeed").Modifiers.Count == modifiers, "respawn transfers personal stats once");
        ItemStatsSystem.Item oldItem = respawn.CharacterItem; respawn.CharacterItem = new ItemStatsSystem.Item(); module.OnUpdate(0.01f, 0.01f);
        Check(oldItem.GetStat("WalkSpeed").Modifiers.Count == 0 && respawn.CharacterItem.GetStat("WalkSpeed").Modifiers.Count == modifiers, "same body replacement Item transfers modifiers once");
        MutatorManager.RemoveAll(); module.OnUpdate(0.01f, 0.01f);
        Check(respawn.CharacterItem.GetStat("WalkSpeed").Modifiers.Count == modifiers, "local cleanup during shared run recovers same epoch");
        var nextSceneBody = new CharacterMainControl();
        BossRushCoop.HasBoundScene = false; BossRushCoop.SceneToken = null;
        CharacterMainControl.Main = nextSceneBody; module.OnUpdate(0.01f, 0.01f);
        Check(nextSceneBody.CharacterItem.GetStat("WalkSpeed").Modifiers.Count == 0, "native loading gap cannot reapply cached view before a new token exists");
        BossRushCoop.SceneToken = "mutator-scene-B"; module.OnUpdate(0.01f, 0.01f);
        Check(nextSceneBody.CharacterItem.GetStat("WalkSpeed").Modifiers.Count == 0, "cached previous-scene mutator view cannot apply to new body before binding");
        BossRushCoop.HasBoundScene = true; module.OnUpdate(0.01f, 0.01f);
        Check(nextSceneBody.CharacterItem.GetStat("WalkSpeed").Modifiers.Count == 0, "binding new native token does not revive the old cached mutator view at the same public generation");
        Call(module, "Apply", state);
        Check(nextSceneBody.CharacterItem.GetStat("WalkSpeed").Modifiers.Count == modifiers, "fresh current-scene projection restores all original personal mutators");
        CharacterMainControl.Main = respawn; Call(module, "Apply", state);
        respawn.Health.CurrentHealth = 1;
        var kill = new CoopMutatorKill { Epoch = state.Epoch, Token = BossRushCoop.SceneToken, Scene = BossRushCoop.SceneGeneration };
        Check((bool)Call(module, "ApplyKill", kill), "authoritative own kill receipt applies");
        Check(Math.Abs(respawn.Health.CurrentHealth - (1 + respawn.Health.MaxHealth * 0.24f)) < 0.001f, "original 8% and 16% healing retained");
        float hp = respawn.Health.CurrentHealth; kill.Scene--;
        Check((bool)Call(module, "ApplyKill", kill) && respawn.Health.CurrentHealth == hp, "old scene immediate effect acknowledged without healing");
        kill.Scene = BossRushCoop.SceneGeneration; kill.Token = "mutator-native-next";
        Check((bool)Call(module, "ApplyKill", kill) && respawn.Health.CurrentHealth == hp,
            "old native scene kill cannot heal the next body while public generation is unchanged");
        kill.Token = BossRushCoop.SceneToken; BossRushCoop.HasBoundScene = false;
        Check(!(bool)Call(module, "ApplyKill", kill) && respawn.Health.CurrentHealth == hp,
            "current-token personal heal waits for actual scene binding instead of being lost or applied early");
        BossRushCoop.HasBoundScene = true;
        Check((bool)Call(module, "ApplyKill", kill) && respawn.Health.CurrentHealth > hp,
            "retained current-scene heal really applies after binding");
        var lateState = new CoopMutatorWorld { Active = true, Epoch = "next", Mode = "ModeF", Ids = state.Ids };
        typeof(MutatorCoopRuntimeModule).GetField("view", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(module, lateState);
        Check(!(bool)Call(module, "ApplyKill", new CoopMutatorKill { Epoch = "next", Token = BossRushCoop.SceneToken, Scene = BossRushCoop.SceneGeneration }), "receipt before personal projection retries");
        Call(module, "Apply", lateState);
        Check((bool)Call(module, "ApplyKill", new CoopMutatorKill { Epoch = "next", Token = BossRushCoop.SceneToken, Scene = BossRushCoop.SceneGeneration }), "ready projection accepts pending receipt");
        int before = CoopPersonalDelivery<CoopMutatorKill>.Sent.Count;
        new CharacterMainControl().Health.ReportDead(new DamageInfo(respawn)); owner.Advance();
        Check(CoopPersonalDelivery<CoopMutatorKill>.Sent.Count == before, "client native deaths cannot originate effects");
        MutatorModeFlow.TryRollMutatorsForMode("ModeF", true, 1, 1, 10);
        Check(MutatorManager.GetActiveMutators().Count == state.Ids.Length, "guest cannot reroll host IDs");
        Call(module, "Apply", new CoopMutatorWorld());
        Check(!MutatorManager.IsActive && respawn.CharacterItem.GetStat("WalkSpeed").Modifiers.Count == 0, "host end clears guest stats");
        UnityEngine.Object.Destroy(respawn.gameObject);
        Check(respawn == null && respawn.Health == null, "destroyed body and components are absent");
        module.OnDestroy();
        Console.WriteLine("MutatorCoop: " + passed + " PASS");
        // The separate assembly compiles the real delivery class, so the legacy
        // Sent observation above cannot stand in for owner/receipt execution.
        string manifest = System.IO.Path.Combine(AppContext.BaseDirectory, "real-delivery.manifest");
        string[] lines = System.IO.File.ReadAllLines(manifest);
        if (lines.Length != 1) throw new Exception("Expected one actual real-delivery TargetOutputs entry");
        string[] target = lines[0].Split('|');
        if (target.Length != 2 || !System.IO.Path.IsPathFullyQualified(target[0])) throw new Exception("Invalid real-delivery TargetOutputs manifest");
        string path = System.IO.Path.GetFullPath(target[0]);
        string outputRoot = System.IO.Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!path.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path))
            throw new Exception("Real-delivery TargetOutputs must be an existing assembly inside this invocation's output: " + path);
        using (var hash = System.Security.Cryptography.SHA256.Create())
        {
            string actualHash = BitConverter.ToString(hash.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            if (!string.Equals(actualHash, target[1], StringComparison.OrdinalIgnoreCase)) throw new Exception("Real-delivery TargetOutputs SHA mismatch");
            Console.WriteLine("Execute real-delivery TargetPath: " + path + " SHA256=" + actualHash + " manifest=" + manifest);
        }
        Assembly.LoadFrom(path).GetType("RealDeliveryProbe").GetMethod("Run", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
    }
}
