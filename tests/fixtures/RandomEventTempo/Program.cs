using System;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

class Program
{
    static int checks;
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }
    static CharacterMainControl Character()
    {
        var character = new CharacterMainControl { CharacterItem = new Item() };
        foreach (string key in new[] { "WalkSpeed", "RunSpeed", "GunDamageMultiplier", "MeleeDamageMultiplier" })
            character.CharacterItem.Stats.Add(key, new Stat());
        return character;
    }
    static float Delta(CharacterMainControl character, string key) { return character.CharacterItem.GetStat(key).Delta; }
    static void Expected(CharacterMainControl character, RandomEventId id)
    {
        if (id == RandomEventId.WildChase)
            Check(Math.Abs(Delta(character, "WalkSpeed") - .25f) < .0001f && Math.Abs(Delta(character, "RunSpeed") - .25f) < .0001f, "chase changes real walk and run stats");
        else if (id == RandomEventId.MeleeCarnival)
            Check(Math.Abs(Delta(character, "MeleeDamageMultiplier") - .35f) < .0001f && Math.Abs(Delta(character, "GunDamageMultiplier") + .15f) < .0001f, "carnival applies both benefit and drawback");
        else
            Check(Math.Abs(Delta(character, "RunSpeed") + .20f) < .0001f && Math.Abs(Delta(character, "MeleeDamageMultiplier") - .20f) < .0001f, "heavy steps applies both drawback and benefit");
    }
    static void Clear(CharacterMainControl character)
    {
        foreach (Stat stat in character.CharacterItem.Stats.Values) Check(stat.Modifiers.Count == 0, "cleanup removes all owned modifiers");
    }
    static void Main()
    {
        foreach (RandomEventId id in new[] { RandomEventId.WildChase, RandomEventId.MeleeCarnival, RandomEventId.HeavySteps })
        foreach (RandomEventEndReason reason in Enum.GetValues(typeof(RandomEventEndReason)))
        {
            var evt = new RandomEventTempo(id);
            var owner = new ModBehaviour(); var player = Character(); var enemy = Character();
            CharacterMainControl.Main = player; RandomEventEffectHelpers.Player = player; owner.Enemies.Add(enemy);
            var ctx = new RandomEventContext { Owner = owner, Scope = new RuntimeScope() };
            Check(evt.OnTrigger(ctx), "registered event can really attach its effects");
            Check(evt.ConsumesRunBudget, "combat effects consume quota");
            Expected(player, id); Expected(enemy, id);
            string metrics;
            Check(evt.GetValidationOutcome(out metrics) == RandomEventValidationOutcome.Passed, "F3 sees attached effects");
            evt.OnTick(ctx, 2.1f); Expected(enemy, id);
            var reinforcement = Character(); owner.Enemies.Add(reinforcement);
            evt.OnTick(ctx, 2.1f); Expected(reinforcement, id); Expected(player, id);
            evt.OnCleanup(ctx, reason); evt.OnCleanup(ctx, reason);
            Clear(player); Clear(enemy); Clear(reinforcement);
            Check(evt.GetValidationOutcome(out metrics) == RandomEventValidationOutcome.Failed, "cleanup cannot remain falsely green");
            player.CharacterItem.Stats.Clear(); player.CharacterItem.Stats.Add("MeleeDamageMultiplier", new Stat());
            ctx.Scope = new RuntimeScope();
            Check(!evt.OnTrigger(ctx), "missing required player stat rejects partial gameplay");
            ctx.Scope.Clear("TriggerFailed"); Clear(player);
        }
        BossRushCoop.HasWorldAuthority = false;
        foreach (RandomEventId id in new[] { RandomEventId.WildChase, RandomEventId.MeleeCarnival, RandomEventId.HeavySteps })
        for (int member = 0; member < 16; member++)
        {
            var evt = new RandomEventTempo(id); var owner = new ModBehaviour();
            var player = Character(); var enemy = Character(); CharacterMainControl.Main = player;
            RandomEventEffectHelpers.Player = player; owner.Enemies.Add(enemy);
            var ctx = new RandomEventContext { Owner = owner, Scope = new RuntimeScope() };
            Check(evt.OnTrigger(ctx), "every participant receives tempo effect");
            Expected(player, id); Clear(enemy);
            evt.OnTick(ctx, 3f); Expected(player, id); Clear(enemy);
            evt.OnCleanup(ctx, RandomEventEndReason.SceneChanged); Clear(player);
        }
        foreach (bool authority in new[] { true, false })
        foreach (RandomEventId id in new[] { RandomEventId.WildChase, RandomEventId.MeleeCarnival, RandomEventId.HeavySteps })
        {
            BossRushCoop.HasWorldAuthority = authority;
            var evt = new RandomEventTempo(id); var owner = new ModBehaviour();
            var first = Character(); CharacterMainControl.Main = first;
            var ctx = new RandomEventContext { Owner = owner, Scope = new RuntimeScope() };
            Check(evt.OnTrigger(ctx), "original owner receives both temporary modifiers");
            var respawned = Character(); CharacterMainControl.Main = respawned;
            evt.OnTick(ctx, 3f); Expected(respawned, id); Clear(first);
            evt.OnTick(ctx, 3f); Expected(respawned, id);
            var previousItem = new CharacterMainControl { CharacterItem = respawned.CharacterItem };
            respawned.CharacterItem = Character().CharacterItem;
            evt.OnTick(ctx, 3f); Expected(respawned, id); Clear(previousItem);
            var incomplete = Character(); CharacterMainControl.Main = incomplete;
            string missing = id == RandomEventId.WildChase ? "RunSpeed" : id == RandomEventId.MeleeCarnival ? "GunDamageMultiplier" : "MeleeDamageMultiplier";
            incomplete.CharacterItem.Stats.Remove(missing);
            evt.OnTick(ctx, 3f); Clear(incomplete); Clear(respawned);
            incomplete.CharacterItem.Stats.Add(missing, new Stat());
            evt.OnTick(ctx, 3f); Expected(incomplete, id);
            evt.OnTick(ctx, 3f); Expected(incomplete, id);
            CharacterMainControl.Main = null;
            evt.OnTick(ctx, 3f); Clear(incomplete);
            CharacterMainControl.Main = first;
            evt.OnTick(ctx, 3f); Expected(first, id);
            evt.OnCleanup(ctx, RandomEventEndReason.RunEnded); evt.OnCleanup(ctx, RandomEventEndReason.RunEnded);
            Clear(first); Clear(respawned); Clear(previousItem); Clear(incomplete);
        }
        Console.WriteLine("RandomEventTempo: PASS (" + checks + " assertions)");
    }
}
