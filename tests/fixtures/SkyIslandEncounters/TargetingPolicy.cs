using System;
using BossRush;

internal static partial class Program
{
    private static void CheckEncounterTargetingPolicy()
    {
        foreach (string id in new[] { "D", "K1_Relay" })
        {
            Reset(); SkyIslandEnemyTiers.ForcedPlayer.Clear();
            using (var world = new World(id))
            {
                world.Tick();
                Check(SkyIslandEnemyTiers.ForcedPlayer.Count == GroupSize(id) &&
                    SkyIslandEnemyTiers.ForcedPlayer.TrueForAll(value => !value),
                    "automatic encounter preserves official target competition: " + id);
                Check(CharacterRandomPreset.Clones.TrueForAll(preset => !preset.setActiveByPlayerDistance),
                    "natural targeting does not re-enable distance sleep: " + id);
                foreach (var actor in CharacterRandomPreset.Created.ToArray()) Kill(actor);
                world.Tick();
                Check(world.Saved.Contains(id), "automatic faction encounter can still finish: " + id);
            }
        }
        Reset(); SkyIslandEnemyTiers.ForcedPlayer.Clear();
        using (var world = new World("Storm"))
        {
            Check(world.Encounters.BeginChallenge("Storm"), "manual story challenge can start");
            Check(SkyIslandEnemyTiers.ForcedPlayer.Count == GroupSize("Storm") &&
                SkyIslandEnemyTiers.ForcedPlayer.TrueForAll(value => value),
                "manual challenge retains forced player tracking for every position");
        }
    }
}
