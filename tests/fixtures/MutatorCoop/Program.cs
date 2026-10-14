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
        var owner = new ModBehaviour();
        var module = new MutatorCoopRuntimeModule(); module.OnAwake(owner);
        for (int i = 0; i < 16; i++) BossRushCoop.Players.Add(new BossRushCoopPlayer { Id = "p" + i, Character = new CharacterMainControl() });
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
        respawn.Health.CurrentHealth = 1;
        var kill = new CoopMutatorKill { Epoch = state.Epoch, Scene = BossRushCoop.SceneGeneration };
        Check((bool)Call(module, "ApplyKill", kill), "authoritative own kill receipt applies");
        Check(Math.Abs(respawn.Health.CurrentHealth - (1 + respawn.Health.MaxHealth * 0.24f)) < 0.001f, "original 8% and 16% healing retained");
        float hp = respawn.Health.CurrentHealth; kill.Scene--;
        Check((bool)Call(module, "ApplyKill", kill) && respawn.Health.CurrentHealth == hp, "old scene immediate effect acknowledged without healing");
        var lateState = new CoopMutatorWorld { Active = true, Epoch = "next", Mode = "ModeF", Ids = state.Ids };
        typeof(MutatorCoopRuntimeModule).GetField("view", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(module, lateState);
        Check(!(bool)Call(module, "ApplyKill", new CoopMutatorKill { Epoch = "next", Scene = BossRushCoop.SceneGeneration }), "receipt before personal projection retries");
        Call(module, "Apply", lateState);
        Check((bool)Call(module, "ApplyKill", new CoopMutatorKill { Epoch = "next", Scene = BossRushCoop.SceneGeneration }), "ready projection accepts pending receipt");
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
    }
}
