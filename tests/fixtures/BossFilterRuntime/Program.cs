using System;
using System.Collections.Generic;

internal sealed class EnemyPresetInfo
{
    internal string name;
}

internal sealed class CatalogProbe
{
    internal int Refreshes;
    internal void NotifyEnemyPresetsRefreshed() { Refreshes++; }
}

internal sealed class ModBehaviour
{
    internal List<EnemyPresetInfo> BossFilterEnemyPresets = new List<EnemyPresetInfo>();
    internal List<string> Disabled = new List<string>();
    internal Dictionary<string, float> Factors = new Dictionary<string, float>();
    internal CatalogProbe PetNestRuntime = new CatalogProbe();
    internal CatalogProbe CodexRuntime = new CatalogProbe();
    internal List<string> GetBossFilterDisabledBosses() { return Disabled; }
    internal Dictionary<string, float> GetBossFilterSavedFactors() { return Factors; }
    internal static void DevLog(string message) { }
}

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        checks++;
    }

    private static void Main()
    {
        var owner = new ModBehaviour();
        owner.BossFilterEnemyPresets.Add(new EnemyPresetInfo { name = "alpha" });
        owner.BossFilterEnemyPresets.Add(new EnemyPresetInfo { name = "beta" });
        owner.Disabled.Add("beta");
        owner.Factors["alpha"] = 1.5f;
        var module = new BossFilterRuntimeModule(owner);
        module.InitializeBossPoolFilter();
        Check(module.IsBossEnabled("alpha") && !module.IsBossEnabled("beta"), "saved disabled boss");
        Check(module.IsBossEnabled("unknown"), "unknown boss defaults enabled");
        Check(module.GetBossInfiniteHellFactor("alpha") == 1.5f && module.GetBossInfiniteHellFactor("beta") == 1f,
            "saved and default factors");
        Check(owner.PetNestRuntime.Refreshes == 1 && owner.CodexRuntime.Refreshes == 1,
            "initialization refreshes both catalogs once");
        var first = module.GetFilteredEnemyPresets();
        Check(first.Count == 1 && first[0].name == "alpha", "filtered list honors disabled state");
        Check(Object.ReferenceEquals(first, module.GetFilteredEnemyPresets()), "clean cache returns same list");
        module.SetBossEnabled("beta", true);
        var second = module.GetFilteredEnemyPresets();
        Check(second.Count == 2 && !Object.ReferenceEquals(first, second), "state change invalidates cache");
        Check(owner.PetNestRuntime.Refreshes == 2 && owner.CodexRuntime.Refreshes == 2,
            "state change refreshes both catalogs");
        var anotherOwner = new ModBehaviour();
        anotherOwner.BossFilterEnemyPresets.Add(new EnemyPresetInfo { name = "gamma" });
        var another = new BossFilterRuntimeModule(anotherOwner);
        another.InitializeBossPoolFilter();
        Check(another.GetFilteredEnemyPresets().Count == 1 && module.GetFilteredEnemyPresets().Count == 2,
            "module instances keep owner state separate");
        Console.WriteLine("BossFilterRuntime regression PASS: " + checks + " checks");
    }
}
