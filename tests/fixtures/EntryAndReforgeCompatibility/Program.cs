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
        // 2026-11-25 下调：整数预制体 5 的上下限为 [3, 7]（±50% 取整）
        var row = ReforgeUIManager.Reveal("RecoilScaleH", 5, 3, -2);
        Check(row[0] && row[1], "recoil lower bound is beneficial and gets best-bound reveal");
        row = ReforgeUIManager.Reveal("RecoilScaleH", 5, 7, 2);
        Check(!row[0] && !row[1], "worse recoil upper bound is never celebrated");
        row = ReforgeUIManager.Reveal("Damage", 5, 7, 2);
        Check(row[0] && row[1], "damage upper bound keeps existing feedback");
        Check(ReforgeUIManager.Bound("RecoilScaleH", 5, 3, -2).Contains("best>Min"), "recoil Min label uses best-bound colour");
        Check(ReforgeUIManager.Bound("RecoilScaleH", 5, 7, 2).Contains("neutral>Max"), "recoil Max label is neutral");
        Check(ReforgeUIManager.Bound("Damage", 5, 7, 2).Contains("best>Max"), "damage Max label keeps best-bound colour");
        ReforgeCaps();
    }
    private static void Bounds(string key, float prefab, float min, float max)
    {
        float lo, hi;
        ReforgeSystem.GetPropertyValueBounds(key, prefab, out lo, out hi);
        Check(Mathf.Abs(lo - min) < 0.0001f && Mathf.Abs(hi - max) < 0.0001f,
            "bounds " + key + "@" + prefab + " = [" + lo + ", " + hi + "], expected [" + min + ", " + max + "]");
    }
    private static void ReforgeCaps()
    {
        Bounds("Damage", 10f, 5f, 15f);
        Bounds("Damage", 5f, 3f, 7f);
        Bounds("Damage", 1f, 0.5f, 1.5f);
        Bounds("Damage", 0.2f, 0.1f, 0.3f);
        Bounds("Damage", -4f, -6f, -2f);
        Bounds("Damage", 0f, -0.25f, 0.25f);
        Bounds("BodyArmor", 3f, 2f, 4f);
        Bounds("BodyArmor", 0.5f, 0f, 1.5f);
        Check(Mathf.Abs(ReforgeSystem.ClampToReforgeBounds("Damage", 10f, 20f) - 15f) < 0.0001f, "legacy 200% value clamps to the 150% cap");
        Check(Mathf.Abs(ReforgeSystem.ClampToReforgeBounds("RecoilScaleV", 10f, 3f) - 5f) < 0.0001f, "legacy 30% recoil clamps to the 50% floor");
        Check(Mathf.Abs(ReforgeSystem.ClampToReforgeBounds("Damage", 10f, 12f) - 12f) < 0.0001f, "in-range legacy value is untouched");

        // 每次以预制体原值为基准重抽：同一随机序列反复重铸不能越刷越高（旧实现会累加到 200%）
        foreach (var type in new[] { PropertyType.Stat, PropertyType.Modifier, PropertyType.Variable })
        foreach (var prefabValue in new[] { 10f, 1f, 0.3f, 0f })
        {
            var item = Make("Damage", prefabValue, type); item.Prefab = Make("Damage", prefabValue, type);
            float first = ReforgeSystem.Reforge(item, 100, "test", 1f).ModifiedStats[0].NewValue;
            float lo, hi;
            ReforgeSystem.GetPropertyValueBounds("Damage", prefabValue, out lo, out hi);
            for (int n = 0; n < 30; n++)
            {
                var info = ReforgeSystem.Reforge(item, 100, "test", 1f).ModifiedStats[0];
                Check(info.NewValue <= hi + 0.0001f && info.NewValue >= lo - 0.0001f, "rolled value stays in bounds");
                // 抽到与当前值相同会被推开一个最小单位（整数 1、小数 0.01），只允许这一格的来回
                Check(Mathf.Abs(info.NewValue - first) <= (prefabValue >= 2f ? 1.0101f : 0.0101f),"reforge must re-roll from the prefab value, not accumulate: " + prefabValue + " " + type);
            }
        }
    }
    private static void Entry()
    {
        IntegrationRuntimeModule.TicketId = 0;
        Check(BossRushMapSelectionHelper.GetBossRushTicketTypeId() == BossRushItemIds.BossRushTicket,
            "map entry uses the registered content ID before ticket registration finishes");
        IntegrationRuntimeModule.ThrowTicketQuery = true;
        Check(BossRushMapSelectionHelper.GetBossRushTicketTypeId() == BossRushItemIds.BossRushTicket,
            "map entry never charges the old template when registration query fails");
        IntegrationRuntimeModule.ThrowTicketQuery = false;
        IntegrationRuntimeModule.TicketId = 501234;
        Check(BossRushMapSelectionHelper.GetBossRushTicketTypeId() == 501234,
            "live registration remains the authoritative ticket ID");
        IntegrationRuntimeModule.TicketId = 0;
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
