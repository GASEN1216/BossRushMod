using System;
using BossRush;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    private static Item Make(string key, float value, PropertyType type)
    {
        var item = new Item();
        if (type == PropertyType.Stat) item.Stats.Add(new Stat { Key = key, BaseValue = value });
        else if (type == PropertyType.Modifier) item.Modifiers.Add(new ModifierDescription { Key = key, Value = value });
        else item.Variables.Add(new CustomData { Key = key, Value = value });
        return item;
    }
    private static void Reforge()
    {
        StatInfoDatabase.Values["ReloadTime"] = Polarity.Negative;
        foreach (var key in new[] { "RecoilScaleV", "RecoilScaleH", "ReloadTime", "Damage", "RecoilControl", "UnknownModStat" })
        foreach (var type in new[] { PropertyType.Stat, PropertyType.Modifier, PropertyType.Variable })
        foreach (var value in new[] { 0.35f, 5f, -0.2f })
        foreach (var chance in new[] { 0f, 1f })
        {
            var item = Make(key, value, type); item.Prefab = Make(key, value, type);
            var result = ReforgeSystem.Reforge(item, 100, "test", chance);
            bool lower = key == "RecoilScaleV" || key == "RecoilScaleH" || key == "ReloadTime";
            Check(result.Success && result.HasAppliedChanges && result.ModifiedStats.Count == 1, "actual Reforge must apply a result");
            var diff = result.ModifiedStats[0].AdjustmentFactor;
            Check((diff < 0) == (lower == (chance == 1)), "benefit/penalty direction: " + key);
            Check(ReforgeSystem.IsBeneficialChange(key, diff) == (chance == 1), "UI agrees with applied result");
            item.Locked.Add(key);
            Check(!ReforgeSystem.Reforge(item, 100, "test", chance).Success, "locked property remains untouched");
        }
        StatInfoDatabase.Throw = true;
        Check(ReforgeSystem.GetBeneficialValueDirection("RecoilScaleV") == -1, "vertical recoil fallback");
        Check(ReforgeSystem.GetBeneficialValueDirection("RecoilScaleH") == -1, "horizontal recoil fallback");
        Check(ReforgeSystem.GetBeneficialValueDirection("RecoilControl") == 1, "no recoil substring inversion");
        Check(ReforgeSystem.GetBeneficialValueDirection(null) == 1, "unknown key preserves legacy direction");
        StatInfoDatabase.Throw = false;
        var row = ReforgeUIManager.Reveal("RecoilScaleH", 5, 1.5f, -3.5f);
        Check(row[0] && row[1], "recoil lower bound is beneficial and gets best-bound reveal");
        row = ReforgeUIManager.Reveal("RecoilScaleH", 5, 10, 5);
        Check(!row[0] && !row[1], "worse recoil upper bound is never celebrated");
        row = ReforgeUIManager.Reveal("Damage", 5, 10, 5);
        Check(row[0] && row[1], "damage upper bound keeps existing feedback");
        Check(ReforgeUIManager.Bound("RecoilScaleH", 5, 1.5f, -3.5f).Contains("best>Min"), "recoil Min label uses best-bound colour");
        Check(ReforgeUIManager.Bound("RecoilScaleH", 5, 10, 5).Contains("neutral>Max"), "recoil Max label is neutral");
        Check(ReforgeUIManager.Bound("Damage", 5, 10, 5).Contains("best>Max"), "damage Max label keeps best-bound colour");
    }
    private static void Entry()
    {
        BossRushMapSelectionHelper.SetPendingMapEntryIndex(-1);
        BossRushMapEntrySelectionPatch.Select(new MapSelectionEntry());
        Check(!BossRushMapSelectionHelper.IsPendingTargetScene("arena"), "vanilla entry cannot arm BossRush");
        var entry = new MapSelectionEntry { Marker = new BossRushMapEntryClickHandler { entryIndex = 0 } };
        entry.Marker.Entry = entry;
        entry.Cost.Enough = false; BossRushMapEntrySelectionPatch.Select(entry); entry.Marker.OnPointerClick(null);
        Check(!BossRushMapSelectionHelper.IsPendingTargetScene("arena"), "insufficient ticket does not select a target");
        entry.Cost.Enough = true; entry.ConditionsSatisfied = false;
        BossRushMapEntrySelectionPatch.Select(entry); entry.Marker.OnPointerClick(null);
        Check(!BossRushMapSelectionHelper.IsPendingTargetScene("arena"), "unmet conditions reject both callbacks");
        entry.ConditionsSatisfied = true; BossRushMapEntrySelectionPatch.Select(entry); entry.Marker.OnPointerClick(null);
        Check(BossRushMapSelectionHelper.IsPendingTargetScene("arena"), "prefix selects before official load starts");
        Check(!BossRushMapSelectionHelper.IsPendingTargetScene("other_arena"), "other arena cannot consume selected target");
        SceneInfoCollection.Entries = new System.Collections.Generic.List<SceneInfoEntry>();
        SceneManager.Active = new Scene { name = "base", handle = 1, buildIndex = 0 };
        var aux = new Scene { name = "mod_resource", handle = 2, buildIndex = -1 };
        var host = new ModBehaviour();
        host.Load(aux, LoadSceneMode.Additive, true, false);
        host.Load(aux, LoadSceneMode.Additive, false, true);
        Check(host.Cleanups == 0 && host.Callbacks == 0, "auxiliary callback cannot clear pending or active arena");
        foreach (string name in new[] { "arena", "main", "MainMenu", "LoadingScreen", "Base_SceneV2" })
        {
            var scene = aux; scene.name = name;
            host.Load(scene, LoadSceneMode.Additive, true, false);
        }
        Check(host.Cleanups == 5 && host.Callbacks == 5, "target, main, menu, curtain and base still dispatch");
        var gameplay = aux; gameplay.Roots = new[] { new GameObject { HasLevel = true } };
        host.Load(gameplay, LoadSceneMode.Additive, true, false);
        gameplay.Roots[0].HasLevel = false; gameplay.Roots[0].HasCore = true;
        host.Load(gameplay, LoadSceneMode.Additive, true, false);
        host.Load(aux, LoadSceneMode.Single, true, false);
        host.Load(aux, LoadSceneMode.Additive, false, false);
        SceneManager.Active = aux; host.Load(aux, LoadSceneMode.Additive, true, false);
        Check(host.Cleanups == 10, "real level owners, single loads, non-arena and active scenes are preserved");
        SceneManager.Active = new Scene { handle = 1 };
        SceneInfoCollection.Entries.Add(new SceneInfoEntry { SceneReference = new SceneReference { Name = aux.name } });
        host.Load(aux, LoadSceneMode.Additive, true, false);
        Check(host.Cleanups == 11, "registered third-party raid without local level owner is not a resource scene");
        SceneInfoCollection.Entries = null; host.Load(aux, LoadSceneMode.Additive, true, false);
        Check(host.Cleanups == 12, "missing scene registry preserves existing lifecycle");
    }
    private static void Main() { Reforge(); Entry(); checks += InitialSpawnRegression.Run().GetAwaiter().GetResult(); Console.WriteLine("PASS: " + checks + " production-linked assertions"); }
}
