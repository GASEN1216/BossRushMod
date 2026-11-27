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
        // 飞行图腾的加速时间与体力消耗越低越好，官方极性表里没有这些键（替身返回中性）
        foreach (string lowerBetter in new[] { "Flight_AccelerationTime", "Flight_StartupStamina", "Flight_FlightStaminaDrain", "Flight_GlidingStaminaDrain" })
        {
            Check(ReforgeSystem.GetBeneficialValueDirection(lowerBetter) == -1, "flight lower-is-better " + lowerBetter);
        }
        Check(ReforgeSystem.GetBeneficialValueDirection("Flight_MaxUpwardSpeed") == 1, "flight upward speed stays higher-is-better");
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
                // 只验「从原值重抽」：每轮清掉保底计数，免得保底在固定种子下把这一轮直接顶到 Max
                item.Variables.RemoveAll(d => d.Key.StartsWith("RF_PITY_"));
                var info = ReforgeSystem.Reforge(item, 100, "test", 1f).ModifiedStats[0];
                Check(info.NewValue <= hi + 0.0001f && info.NewValue >= lo - 0.0001f, "rolled value stays in bounds");
                // 抽到与当前值相同会被推开一个最小单位（整数 1、小数 0.01），只允许这一格的来回
                Check(Mathf.Abs(info.NewValue - first) <= (prefabValue >= 2f ? 1.0101f : 0.0101f),"reforge must re-roll from the prefab value, not accumulate: " + prefabValue + " " + type);
            }
        }
        BoundSnap();
        Pity();
    }
    private static int FindSeed(float lo, float hi, double pityDrawMin = 0.01)
    {
        // 夹具 FinalProbability 恒为 0.5；按生产 RollMagnitude 实算第一次抽到的幅度挑种子，
        // 紧随其后的第二次是保底抽签，要求不小于 pityDrawMin（新物品保底只有 0.01%，默认 0.01 足够避开）
        for (int seed = 0; seed < 2000000; seed++)
        {
            var random = new System.Random(seed);
            float mag = ReforgeSystem.RollMagnitude(0.5f, random);
            if (mag >= lo && mag < hi && random.NextDouble() >= pityDrawMin) return seed;
        }
        throw new Exception("no seed for magnitude window [" + lo + ", " + hi + ") with pity draw >= " + pityDrawMin);
    }
    private static float RollOnce(string key, float prefabValue, PropertyType type, float chance, float current)
    {
        var item = Make(key, current, type); item.Prefab = Make(key, prefabValue, type);
        var result = ReforgeSystem.Reforge(item, 100, "test", chance);
        Check(result.Success && result.ModifiedStats.Count == 1, "reforge succeeds: " + key + "@" + prefabValue);
        return result.ModifiedStats[0].NewValue;
    }
    private static void BoundSnap()
    {
        // 2026-11-27：从预制体原值重抽后上限是连续分布的端点，顶档幅度必须取到边界，Max/Min 才抽得到
        int top = FindSeed(0.95f, 1.01f), near = FindSeed(0.90f, 0.95f);
        try
        {
            foreach (var type in new[] { PropertyType.Stat, PropertyType.Modifier, PropertyType.Variable })
            {
                ReforgeSystem.FixtureSeed = top;
                // 0.35 的上限 0.525 是三位小数，舍入到两位只有 0.52：顶档必须原样取上限
                float v = RollOnce("Damage", 0.35f, type, 1f, 0.35f);
                Check(ReforgeSystem.IsValueAtUpperBound("Damage", 0.35f, v), "top band reaches Max on a 3-decimal cap: " + v + " " + type);
                Check(ReforgeUIManager.Bound("Damage", 0.35f, v, v - 0.35f).Contains("best>Max"), "Max label shows for the snapped cap");
                Check(ReforgeUIManager.Reveal("Damage", 0.35f, v, v - 0.35f)[1], "snapped cap gets the best-bound reveal");
                v = RollOnce("Damage", 0.35f, type, 0f, 0.35f);
                Check(ReforgeSystem.IsValueAtLowerBound("Damage", 0.35f, v), "top band reaches Min: " + v);
                Check(ReforgeUIManager.Bound("Damage", 0.35f, v, v - 0.35f).Contains("neutral>Min"), "worse Min keeps the neutral label");
                Check(Mathf.Abs(RollOnce("Damage", 30f, type, 1f, 30f) - 45f) < 0.0001f, "integer cap 45 for prefab 30");
                Check(Mathf.Abs(RollOnce("Damage", 1.2f, type, 1f, 1.2f) - 1.8f) < 0.0001f, "decimal cap 1.8 for prefab 1.2");
                float recoil = RollOnce("RecoilScaleV", 1.2f, type, 1f, 1.2f);
                Check(ReforgeSystem.IsValueAtLowerBound("RecoilScaleV", 1.2f, recoil), "beneficial recoil snaps to its floor: " + recoil);
                Check(ReforgeUIManager.Bound("RecoilScaleV", 1.2f, recoil, recoil - 1.2f).Contains("best>Min"), "recoil floor is the best bound");
                Check(Mathf.Abs(RollOnce("Damage", 0f, type, 1f, 0f) - 0.25f) < 0.0001f, "zero prefab snaps to +0.25");
                Check(Mathf.Abs(RollOnce("BodyArmor", 3f, type, 1f, 3f) - 4f) < 0.0001f, "armor snaps to prefab +1");
                // 读档夹到上限的旧装备再抽到顶档：保持 Max，不被「必定变化」推开
                float cap = ReforgeSystem.ClampToReforgeBounds("Damage", 0.35f, 1f);
                v = RollOnce("Damage", 0.35f, type, 1f, cap);
                Check(Mathf.Abs(v - cap) < 0.0001f && ReforgeSystem.IsValueAtUpperBound("Damage", 0.35f, v), "an item already at Max stays at Max");

                ReforgeSystem.FixtureSeed = near;
                v = RollOnce("Damage", 0.35f, type, 1f, 0.35f);
                Check(v > 0.35f && !ReforgeSystem.IsValueAtUpperBound("Damage", 0.35f, v), "below the top band is an ordinary roll: " + v);
                v = RollOnce("Damage", 30f, type, 1f, 30f);
                Check(v > 30f && !ReforgeSystem.IsValueAtUpperBound("Damage", 30f, v), "integer below the top band stays under the cap: " + v);
            }
        }
        finally
        {
            ReforgeSystem.FixtureSeed = 123;
        }
    }
    private static float PityValue(Item item, string key, PropertyType type)
    {
        var entry = item.Variables.GetEntry(ReforgeSystem.GetPityKey(key, type));
        return entry == null ? -1f : entry.Value;
    }
    private static ReforgeResult ReforgeOnce(Item item, float chance)
    {
        var result = ReforgeSystem.Reforge(item, 100, "test", chance);
        Check(result.Success, "reforge succeeds");
        return result;
    }
    private static void Pity()
    {
        // 2026-11-27 顶格保底：第 k 次连续未顶格额外 (k/100)^2，第 100 次必中
        Check(ReforgeSystem.PITY_GUARANTEE_REFORGES == 100, "pity guarantees within 100 reforges");
        Check(ReforgeSystem.GetPityKey("Damage", PropertyType.Stat) == "RF_PITY_STAT_Damage", "stat pity key");
        Check(ReforgeSystem.GetPityKey("Damage", PropertyType.Modifier) == "RF_PITY_MOD_Damage", "modifier pity key");
        Check(ReforgeSystem.GetPityKey("Damage", PropertyType.Variable) == "RF_PITY_VAR_Damage", "variable pity key");
        foreach (var type in new[] { PropertyType.Stat, PropertyType.Modifier, PropertyType.Variable })
        {
            string key = ReforgeSystem.GetPityKey("Damage", type);
            Check(ReforgeSystem.IsRuntimeTrackingVariableKey(key), "pity counter never enters the reforge pool: " + key);
            foreach (var prefix in new[] { "RF_MOD_", "RF_STAT_", "RF_VAR_", "RF_LOCK_" })
                Check(!key.StartsWith(prefix), "pity counter is not parsed as reforge delta or lock data: " + key);
        }
        Check(Math.Abs(ReforgeSystem.GetPityChance(0) - 0.0001f) < 1e-7f, "first reforge adds only 0.01%");
        Check(Math.Abs(ReforgeSystem.GetPityChance(49) - 0.25f) < 1e-6f, "50th reforge adds 25%");
        Check(ReforgeSystem.GetPityChance(99) == 1f && ReforgeSystem.GetPityChance(500) == 1f, "100th reforge is guaranteed");
        Check(ReforgeSystem.GetPityChance(-3) == ReforgeSystem.GetPityChance(0), "negative counts clamp to the first step");
        for (int k = 1; k < 100; k++)
            Check(ReforgeSystem.GetPityChance(k) > ReforgeSystem.GetPityChance(k - 1), "pity chance grows every reforge");

        // 旧存档没有计数条目、脏值：一律按 0 起算，上限 99
        var old = Make("Damage", 10f, PropertyType.Stat);
        string statKey = ReforgeSystem.GetPityKey("Damage", PropertyType.Stat);
        Check(ReforgeSystem.GetPityCount(old, statKey) == 0, "old saves without a counter start from 0");
        Check(ReforgeSystem.GetPityCount(null, statKey) == 0, "null item reads 0");
        foreach (var dirty in new[] { float.NaN, float.PositiveInfinity, -5f })
        {
            old.SetFloat(statKey, dirty);
            Check(ReforgeSystem.GetPityCount(old, statKey) == 0, "dirty counter reads 0: " + dirty);
        }
        old.SetFloat(statKey, 1e9f); Check(ReforgeSystem.GetPityCount(old, statKey) == 99, "oversized counter caps at 99");
        old.SetFloat(statKey, 12.4f); Check(ReforgeSystem.GetPityCount(old, statKey) == 12, "counter rounds to an integer");

        int miss = FindSeed(0.30f, 0.60f, 0.99), top = FindSeed(0.95f, 1.01f);
        try
        {
            foreach (var type in new[] { PropertyType.Stat, PropertyType.Modifier, PropertyType.Variable })
            {
                // 没顶格逐次 +1；计数条目不在详情里显示，也不被当成可重铸属性
                ReforgeSystem.FixtureSeed = miss;
                var item = Make("Damage", 10f, type); item.Prefab = Make("Damage", 10f, type);
                for (int n = 1; n <= 5; n++)
                {
                    var r = ReforgeOnce(item, 1f);
                    Check(r.ModifiedStats.Count == 1, "pity counter is never rerolled: " + type);
                    Check(!ReforgeSystem.IsValueAtUpperBound("Damage", 10f, r.ModifiedStats[0].NewValue), "ordinary roll stays below Max");
                    Check(PityValue(item, "Damage", type) == n, "miss increments the counter: " + n + " " + type);
                }
                Check(!item.Variables.GetEntry(ReforgeSystem.GetPityKey("Damage", type)).Display, "counter is hidden from item details");

                // 第 99 次（98% 概率）这颗种子没中，计数到 99；第 100 次哪怕倾向拉到负面也必取 Max，并清零
                item.SetFloat(ReforgeSystem.GetPityKey("Damage", type), 98f);
                var r99 = ReforgeOnce(item, 1f);
                Check(!ReforgeSystem.IsValueAtUpperBound("Damage", 10f, r99.ModifiedStats[0].NewValue) && PityValue(item, "Damage", type) == 99, "99th can still miss");
                var r100 = ReforgeOnce(item, 0f);
                Check(Math.Abs(r100.ModifiedStats[0].NewValue - 15f) < 0.0001f, "100th reforge lands on Max regardless of tendency: " + r100.ModifiedStats[0].NewValue);
                Check(PityValue(item, "Damage", type) == 0, "pity hit resets the counter");
                Check(ReforgeUIManager.Reveal("Damage", 10f, 15f, 5f)[1], "pity Max gets the same best-bound reveal");

                // 后坐力越低越好：保底给 Min
                var recoil = Make("RecoilScaleV", 1.2f, type); recoil.Prefab = Make("RecoilScaleV", 1.2f, type);
                recoil.SetFloat(ReforgeSystem.GetPityKey("RecoilScaleV", type), 99f);
                float rv = ReforgeOnce(recoil, 1f).ModifiedStats[0].NewValue;
                Check(ReforgeSystem.IsValueAtLowerBound("RecoilScaleV", 1.2f, rv), "recoil pity lands on Min: " + rv);
                Check(PityValue(recoil, "RecoilScaleV", type) == 0, "recoil pity resets");

                // 自然顶到有利边界也清零；顶到不利边界（倾向负面的 Min）照常 +1
                ReforgeSystem.FixtureSeed = top;
                item.SetFloat(ReforgeSystem.GetPityKey("Damage", type), 40f);
                Check(ReforgeSystem.IsValueAtUpperBound("Damage", 10f, ReforgeOnce(item, 1f).ModifiedStats[0].NewValue), "natural top band reaches Max");
                Check(PityValue(item, "Damage", type) == 0, "natural Max resets the counter");
                item.SetFloat(ReforgeSystem.GetPityKey("Damage", type), 40f);
                Check(ReforgeSystem.IsValueAtLowerBound("Damage", 10f, ReforgeOnce(item, 0f).ModifiedStats[0].NewValue), "harmful top band reaches Min");
                Check(PityValue(item, "Damage", type) == 41, "harmful bound is not a pity success");

                // 从没记过数的属性顶格：不新建条目
                var fresh = Make("Damage", 10f, type); fresh.Prefab = Make("Damage", 10f, type);
                ReforgeOnce(fresh, 1f);
                Check(PityValue(fresh, "Damage", type) == -1f, "a first-try Max does not create an empty counter");

                // 冷淬液固定的属性不重抽，计数冻结；其余属性照常计
                ReforgeSystem.FixtureSeed = miss;
                var pair = Make("Damage", 10f, type); pair.Prefab = Make("Damage", 10f, type);
                if (type == PropertyType.Stat) { pair.Stats.Add(new Stat { Key = "Speed", BaseValue = 4f }); pair.Prefab.Stats.Add(new Stat { Key = "Speed", BaseValue = 4f }); }
                else if (type == PropertyType.Modifier) { pair.Modifiers.Add(new ModifierDescription { Key = "Speed", Value = 4f }); pair.Prefab.Modifiers.Add(new ModifierDescription { Key = "Speed", Value = 4f }); }
                else { pair.Variables.Add(new CustomData { Key = "Speed", Value = 4f }); pair.Prefab.Variables.Add(new CustomData { Key = "Speed", Value = 4f }); }
                pair.SetFloat(ReforgeSystem.GetPityKey("Damage", type), 30f);
                pair.Locked.Add("Damage");
                var locked = ReforgeOnce(pair, 1f);
                Check(locked.ModifiedStats.Count == 1 && locked.ModifiedStats[0].StatKey == "Speed", "locked property is not rerolled");
                Check(PityValue(pair, "Damage", type) == 30f, "locked property's counter stays frozen");
                Check(PityValue(pair, "Speed", type) == 1f, "unlocked property keeps counting");
            }

            // 同键的两条 Modifier 共用一个计数，一次重铸只 +1（种子要让两条都落在普通幅度、保底都不中）
            int dupSeed = -1;
            for (int seed = 0; seed < 2000000 && dupSeed < 0; seed++)
            {
                var random = new System.Random(seed);
                float m1 = ReforgeSystem.RollMagnitude(0.5f, random); double p1 = random.NextDouble();
                float m2 = ReforgeSystem.RollMagnitude(0.5f, random); double p2 = random.NextDouble();
                if (m1 >= 0.3f && m1 < 0.6f && m2 >= 0.3f && m2 < 0.6f && p1 >= 0.01 && p2 >= 0.01) dupSeed = seed;
            }
            Check(dupSeed >= 0, "seed for duplicate modifiers");
            ReforgeSystem.FixtureSeed = dupSeed;
            var dup = Make("Damage", 10f, PropertyType.Modifier); dup.Prefab = Make("Damage", 10f, PropertyType.Modifier);
            dup.Modifiers.Add(new ModifierDescription { Key = "Damage", Value = 10f });
            Check(ReforgeOnce(dup, 1f).ModifiedStats.Count == 2, "both duplicate modifiers reroll");
            Check(PityValue(dup, "Damage", PropertyType.Modifier) == 1f, "duplicate keys share one counter");

            // 生产路径实跑分布：倾向负面时自然只会顶到不利边界，有利边界全靠保底；间隔不超过 100、均值约 27
            var dist = Make("Damage", 10f, PropertyType.Stat); dist.Prefab = Make("Damage", 10f, PropertyType.Stat);
            var seeds = new System.Random(20261127);
            int gap = 0, maxGap = 0, hits = 0; long total = 0;
            for (int n = 0; n < 60000; n++)
            {
                ReforgeSystem.FixtureSeed = seeds.Next();
                gap++;
                if (ReforgeSystem.IsValueAtUpperBound("Damage", 10f, ReforgeOnce(dist, 0.3f).ModifiedStats[0].NewValue))
                {
                    hits++; total += gap; maxGap = Math.Max(maxGap, gap); gap = 0;
                }
            }
            double mean = (double)total / hits;
            Check(hits > 0 && maxGap <= 100, "best bound always arrives within 100 reforges: " + maxGap);
            Check(mean > 25.0 && mean < 30.0, "pity-only mean gap matches the quadratic curve (~27.4): " + mean);
            Console.WriteLine("pity distribution: hits=" + hits + " mean=" + mean.ToString("F2") + " maxGap=" + maxGap);
        }
        finally
        {
            ReforgeSystem.FixtureSeed = 123;
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
