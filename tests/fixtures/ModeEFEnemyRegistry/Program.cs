using System;
using BossRush;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        assertions++;
    }
    private static void Main()
    {
        var shared = new ModeEFEnemyRegistry();
        var first = new CharacterMainControl("first");
        var second = new CharacterMainControl("second");
        shared.TrackModeEAliveEnemy(null, Teams.wolf);
        shared.TrackModeEAliveEnemy(first, Teams.wolf);
        shared.TrackModeEAliveEnemy(second, Teams.wolf);
        shared.TrackModeEAliveEnemy(first, Teams.bear);
        Teams faction;
        Check(shared.LegacyAliveEnemies.Count == 2 && ReferenceEquals(shared.LegacyAliveEnemies[0], first), "shared tracking retains insertion order and ignores duplicates");
        Check(shared.TryGetTrackedFaction(first, out faction) && faction == Teams.wolf, "duplicate tracking retains original faction");
        Check(shared.GetFactionAliveList(Teams.bear) == null && shared.GetFactionAliveList(Teams.wolf).Count == 2, "duplicate does not create another faction membership");
        var cache = shared.GetModeEBossRegenCache();
        Check(cache.Count == 2 && ReferenceEquals(cache, shared.GetModeEBossRegenCache()), "regen cache reuses its collection");
        shared.UntrackModeEAliveEnemy(first);
        Check(!shared.IsTracked(first) && shared.LegacyAliveEnemies.Count == 1 && shared.GetFactionAliveList(Teams.wolf).Count == 1, "untracking removes global and remembered faction membership");
        Check(ReferenceEquals(cache, shared.GetModeEBossRegenCache()) && cache.Count == 1 && ReferenceEquals(cache[0], second), "untracking invalidates regen cache without replacing it");

        UnityEngine.Object.Destroy(second.gameObject);
        Check(second == null && second.Health == null, "scene destruction cascades to tracked components");
        shared.UntrackModeEAliveEnemy(second);
        Check(!shared.IsTracked(second) && shared.LegacyAliveEnemies.Count == 0 && shared.GetFactionAliveList(Teams.wolf).Count == 0, "destroyed references are removed using identity");
        shared.TrackModeEAliveEnemy(second, Teams.wolf);
        Check(shared.LegacyAliveEnemies.Count == 0, "destroyed objects cannot be tracked again");
        shared.LegacyAliveEnemies.Add(null);
        shared.RemoveNullAliveSlotAt(0);
        shared.MarkModeEBossRegenCacheDirty();
        Check(shared.GetModeEBossRegenCache().Count == 0, "integrity null-slot removal rebuilds the existing cache");

        var dying = new CharacterMainControl("dying");
        int deaths = 0;
        shared.BindDeathCallback((enemy, damage) =>
        {
            Check(ReferenceEquals(enemy, dying) && damage.Id == 42, "death callback preserves captured character and damage");
            Check(dying.Health.OnDeadEvent.Count == 0, "death handler unsubscribes before gameplay callback");
            deaths++;
        });
        shared.RegisterModeEEnemyDeath(dying);
        shared.RegisterModeEEnemyDeath(dying);
        Check(dying.Health.OnDeadEvent.Count == 1, "repeated death registration replaces its owned listener");
        dying.Health.OnDeadEvent.Invoke(new DamageInfo { Id = 42 });
        dying.Health.OnDeadEvent.Invoke(new DamageInfo { Id = 42 });
        Check(deaths == 1, "death callback runs once");
        shared.RegisterModeEEnemyDeath(dying);
        shared.UnregisterModeEEnemyDeath(dying);
        Check(dying.Health.OnDeadEvent.Count == 0, "explicit death cleanup releases only its listener");
        shared.RegisterModeEEnemyDeath(dying);
        UnityEngine.Object.Destroy(dying.gameObject);
        shared.UnregisterModeEEnemyDeath(dying);
        shared.UnregisterModeEEnemyDeath(dying);
        Check(dying == null, "destroyed death cleanup avoids Unity component access");

        var ally = new CharacterMainControl("ally");
        var policy = new ModeELootPolicy(shared) { modeEActive = true, modeEPlayerFaction = Teams.wolf };
        policy.Register(ally, Teams.wolf);
        policy.Register(ally, Teams.wolf);
        Check(ally.LootListeners == 1, "Mode E repeated loot registration replaces its owned handler");
        ally.FireLoot();
        Check(!ally.dropBoxOnDead, "Mode E allied loot policy executes through shared subscription");
        ally.dropBoxOnDead = true;
        policy.modeEActive = false;
        ally.FireLoot();
        Check(ally.dropBoxOnDead, "inactive Mode E leaves loot unchanged");
        policy.modeEActive = true;
        policy.modeEPlayerFaction = Teams.bear;
        ally.FireLoot();
        Check(ally.dropBoxOnDead, "loot policy reads the current player faction");
        policy.Register(ally, Teams.bear);
        ally.FireLoot();
        Check(!ally.dropBoxOnDead && ally.LootListeners == 1, "re-registration captures the new faction exactly once");
        shared.UnregisterModeEEnemyLootHandler(ally);
        Check(ally.LootListeners == 0, "shared cleanup detaches the real Mode E loot policy");

        shared.TrackModeEAliveEnemy(ally, Teams.bear);
        Check(shared.GetModeEBossRegenCache().Count == 1, "current run cache is populated before reset");
        shared.ClearAliveEnemies();
        shared.ClearAliveEnemySet();
        shared.ClearModeEBossRegenCache();
        shared.ClearFactionLookup();
        shared.ClearFactionAliveLists();
        shared.ClearDeathHandlers();
        shared.ClearLootHandlers();
        Check(shared.LegacyAliveEnemies.Count == 0 && !shared.IsTracked(ally) && shared.GetFactionAliveList(Teams.bear) == null, "reset clears shared tracking domains");
        Check(ReferenceEquals(cache, shared.GetModeEBossRegenCache()) && cache.Count == 0, "reset empties the same regen cache");
        shared.TrackModeEAliveEnemy(ally, Teams.bear);
        Check(shared.GetModeEBossRegenCache().Count == 1, "next run registration rebuilds after reset");
        var other = new ModeEFEnemyRegistry();
        Check(other.LegacyAliveEnemies.Count == 0 && !other.IsTracked(ally), "independent host registries cannot share tracking state");
        Console.WriteLine("PASS ModeEFEnemyRegistry: " + assertions + " assertions");
    }
}
