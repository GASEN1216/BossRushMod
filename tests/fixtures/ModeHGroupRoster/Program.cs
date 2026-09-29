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
            Check(entry.Power == 1000, "custom boss default power is 1000 (owner 2026-09-29)");
        }
        Check(customs == 3, "all three custom bosses join the pool once");
        Check(ModeHGroupPool.Build(new ModBehaviour()).Count == 3, "empty host table still yields the three custom bosses");

        int minCount = int.MaxValue, maxCount = 0, worstDiff = 0, enemyFailures = 0;
        for (int seed = 0; seed < 3000; seed++)
        {
            System.Random rng = new System.Random(seed);
            ModeHGroupRoster roster = new ModeHGroupRoster();
            Check(ModeHGroupPool.TryRollAllies(rng, pool, roster), "allies roll #" + seed);
            int count = roster.Allies.Count;
            minCount = Math.Min(minCount, count);
            maxCount = Math.Max(maxCount, count);
            Check(count >= 3 && count <= 20, "ally count within 3~20 #" + seed);
            Check(CustomCapHeld(roster.Allies), "ally custom cap #" + seed);
            if (!ModeHGroupPool.TryRollEnemies(rng, pool, roster)) { enemyFailures++; continue; }
            int diff = Math.Abs(roster.AllyPower - roster.EnemyPower);
            worstDiff = Math.Max(worstDiff, diff);
            Check(diff <= ModeHGroupConfig.PowerTolerance, "power gap <= 500 #" + seed + " gap=" + diff);
            Check(roster.Enemies.Count >= 1 && roster.Enemies.Count <= ModeHGroupConfig.MaxEnemyCount, "enemy count bounds #" + seed);
            Check(CustomCapHeld(roster.Enemies), "enemy custom cap #" + seed);
            Check(!roster.Confirmed, "rolling never confirms the roster");
        }
        Check(enemyFailures == 0, "enemy roll never fails on a normal pool");
        Check(minCount == 3 && maxCount == 20, "ally counts reach both ends of 3~20 (saw " + minCount + ".." + maxCount + ")");
        Console.WriteLine("group roll: ally " + minCount + ".." + maxCount + ", worst power gap " + worstDiff);

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
