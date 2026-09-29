using System;
using System.Collections.Generic;
using BossRush;

internal static class Program
{
    private static int _failures;

    private static void Check(bool condition, string name)
    {
        if (condition) return;
        _failures++;
        Console.WriteLine("FAIL " + name);
    }

    private static int Main()
    {
        ModBehaviour host = new ModBehaviour();
        // 十二个官方 Boss：生命 400~5200、伤害倍率 0.8~2.2，另有一个目录里查不到的（不能进池）
        for (int i = 0; i < 12; i++)
        {
            string key = "Cname_Boss_" + i;
            host.BossFilterEnemyPresets.Add(new EnemyPresetInfo { name = key, displayName = "Boss" + i, baseHealth = 400 + i * 400 });
            ModeHProductionCertification.Catalog[key] = new CharacterRandomPreset
            {
                health = 400f + i * 400f, damageMultiplier = 0.8f + (i % 8) * 0.2f,
                meleeDamageMultiplier = 1f, moveSpeedFactor = 0.9f + (i % 3) * 0.1f,
            };
        }
        host.BossFilterEnemyPresets.Add(new EnemyPresetInfo { name = "Cname_Missing", baseHealth = 900 });
        // 宿主表里登记过的自定义 Boss 不能按官方口径重复进池
        host.BossFilterEnemyPresets.Add(new EnemyPresetInfo { name = DragonKingConfig.BossNameKey, baseHealth = 800 });

        List<ModeHGroupEntry> pool = ModeHGroupPool.Build(host);
        Check(pool.Count == 15, "pool = 12 official + 3 custom (missing preset excluded)");
        int customs = 0;
        foreach (ModeHGroupEntry entry in pool)
        {
            Check(entry.Key != "Cname_Missing", "preset missing from official catalog is not spawnable");
            if (!entry.IsCustom)
            {
                Check(entry.Power >= ModeHGroupConfig.MinOfficialPower && entry.Power <= ModeHGroupConfig.MaxOfficialPower,
                    "official power clamped: " + entry.Key + "=" + entry.Power);
                continue;
            }
            customs++;
            int expected = entry.Key == DragonKingConfig.BossNameKey ? 2000
                : entry.Key == DragonDescendantConfig.BOSS_NAME_KEY ? 1500
                : entry.Key == PhantomWitchConfig.BossNameKey ? 500 : -1;
            Check(entry.Power == expected, "custom boss power (Dragon King 2000, Descendant 1500, Witch 500): " + entry.Key);
        }
        Check(customs == 3, "all three custom bosses join the pool once");
        Check(ModeHGroupPool.Build(new ModBehaviour()).Count == 3, "empty host table still yields the three custom bosses");

        float average = ModeHGroupPool.AverageOfficialPower(pool);
        int[] minPower = new int[7], maxPower = new int[7], minCount = new int[7], maxCount = new int[7];
        long[] sumPower = new long[7];
        for (int m = 1; m <= 6; m++) { minPower[m] = int.MaxValue; minCount[m] = int.MaxValue; }
        int worstDiff = 0, rollFailures = 0, duplicateRosters = 0;
        const int Seeds = 1500;
        for (int seed = 0; seed < Seeds; seed++)
        {
            for (int m = 1; m <= 6; m++)
            {
                System.Random rng = new System.Random(seed * 7 + m);
                ModeHGroupRoster roster = new ModeHGroupRoster();
                if (!ModeHGroupPool.TryRollTeams(rng, pool, m, roster)) { rollFailures++; continue; }
                int count = roster.Allies.Count;
                Check(count >= 3 && count <= 20, "blue count within 3~20 m" + m + " #" + seed);
                Check(roster.Enemies.Count >= 1 && roster.Enemies.Count <= ModeHGroupConfig.MaxEnemyCount, "red count bounds m" + m);
                int diff = Math.Abs(roster.AllyPower - roster.EnemyPower);
                worstDiff = Math.Max(worstDiff, diff);
                Check(diff <= ModeHGroupConfig.PowerTolerance, "power gap <= 500 m" + m + " #" + seed + " gap=" + diff);
                Check(CustomCapHeld(roster.Allies) && CustomCapHeld(roster.Enemies), "custom cap m" + m + " #" + seed);
                if (HasDuplicate(roster.Allies) || HasDuplicate(roster.Enemies)) duplicateRosters++;
                minPower[m] = Math.Min(minPower[m], roster.AllyPower);
                maxPower[m] = Math.Max(maxPower[m], roster.AllyPower);
                minCount[m] = Math.Min(minCount[m], count);
                maxCount[m] = Math.Max(maxCount[m], count);
                sumPower[m] += roster.AllyPower;
            }
        }
        Check(rollFailures == 0, "team roll never fails on a normal pool");
        Check(duplicateRosters > 0, "official bosses may repeat within a team");
        for (int m = 1; m <= 6; m++)
        {
            int target = ModeHGroupConfig.TargetPower(m, average);
            Console.WriteLine("m" + m + ": target " + target + " blue power " + minPower[m] + ".." + maxPower[m]
                + " avg " + (sumPower[m] / Seeds) + " count " + minCount[m] + ".." + maxCount[m]);
            if (m > 1)
            {
                Check(sumPower[m] > sumPower[m - 1], "average blue power rises every match (m" + m + ")");
                Check(minPower[m] > maxPower[m - 1] * 0.95f, "match m" + m + " is not weaker than the previous match");
            }
        }
        Check(maxCount[6] >= 16, "the last match reaches the top of the count range");
        Check(maxCount[1] <= 8, "the first match stays small");
        Console.WriteLine("group roll: worst power gap " + worstDiff);

        // 名人堂：编码往返、排名、满员淘汰
        List<ModeHHallOfFameRecordDto> records = new List<ModeHHallOfFameRecordDto>
        {
            Group("a", 3, 6, 5000, "2026-01-01"), Group("b", 5, 6, -100, "2026-01-02"),
            Group("c", 5, 6, 8000, "2026-01-03"), Legacy("old", "2025-12-01"), Group("d", 3, 6, 5000, "2025-12-31"),
        };
        int wins, matches; long net;
        Check(ModeHGroupHallOfFame.TryDecodeScore(records[2], out wins, out matches, out net)
            && wins == 5 && matches == 6 && net == 8000, "score round trip");
        Check(!ModeHGroupHallOfFame.TryDecodeScore(records[3], out wins, out matches, out net), "legacy champion record is not a group score");
        List<ModeHHallOfFameRecordDto> ranked = ModeHGroupHallOfFame.Rank(records);
        Check(ranked.Count == 4, "legacy record left out of the ranking");
        Check(ranked[0].hallOfFameId == "c" && ranked[1].hallOfFameId == "b"
            && ranked[2].hallOfFameId == "d" && ranked[3].hallOfFameId == "a", "rank by wins, then net, then earlier season");
        Check(records[ModeHGroupHallOfFame.FindEvictionIndex(records)].hallOfFameId == "a", "full hall evicts the lowest-ranked season");
        Check(ModeHGroupHallOfFame.FindEvictionIndex(new List<ModeHHallOfFameRecordDto> { Legacy("x", "1"), Legacy("y", "2") }) == 0,
            "without group records the oldest (index 0) is evicted as before");

        Console.WriteLine(_failures == 0 ? "ModeHGroupRoster: PASS" : "ModeHGroupRoster: FAIL (" + _failures + ")");
        return _failures == 0 ? 0 : 1;
    }

    private static bool CustomCapHeld(List<ModeHGroupEntry> team)
    {
        Dictionary<string, int> seen = new Dictionary<string, int>();
        foreach (ModeHGroupEntry entry in team)
        {
            if (!entry.IsCustom) continue;
            int count;
            seen.TryGetValue(entry.Key, out count);
            if (++count > ModeHGroupConfig.MaxSameCustomPerSide) return false;
            seen[entry.Key] = count;
        }
        return true;
    }

    private static bool HasDuplicate(List<ModeHGroupEntry> team)
    {
        HashSet<string> seen = new HashSet<string>();
        foreach (ModeHGroupEntry entry in team) if (!seen.Add(entry.Key)) return true;
        return false;
    }

    private static ModeHHallOfFameRecordDto Group(string id, int wins, int matches, long net, string created)
    {
        return new ModeHHallOfFameRecordDto
        {
            hallOfFameId = id, archetypeId = ModeHGroupHallOfFame.ArchetypeTag,
            quirkId = ModeHGroupHallOfFame.EncodeScore(wins, matches, net), createdUtc = created,
        };
    }

    private static ModeHHallOfFameRecordDto Legacy(string id, string created)
    {
        return new ModeHHallOfFameRecordDto { hallOfFameId = id, archetypeId = "assault", quirkId = "soft_target", createdUtc = created };
    }
}
