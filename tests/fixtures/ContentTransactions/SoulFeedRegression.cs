using System;
using System.Text.RegularExpressions;
using BossRush;
using Saves;

// 2026-11-24 玩家许愿第 5 项「献魂」：真实 PetNestService.TryFeedSouls + 真实编解码器。
// 断言：只扣同脉遗魂、2 魂 1 经验补满这一级、每个游戏日一次、满级 / 远征中 / 时钟缺失拒绝且不扣、
// 一次提交带上等级 / 印记 / 遗魂三样、新字段往返与旧档缺字段读成 0。
partial class Program
{
    static void PrepareSoulFeedNest(int souls, int level, int exp, PetNestPetState state)
    {
        Reset();
        var bundle = PetNestCodec.CreateDefaultBundle();
        bundle.nest.soulLedger.Add(new PetNestSoulLedgerEntry { lineageKey = "test", souls = souls });
        bundle.nest.soulLedger.Add(new PetNestSoulLedgerEntry { lineageKey = "other", souls = 999 });
        bundle.nest.pets.Add(new PetNestPetRecord { id = "cub", lineageKey = "test", level = level, exp = exp, state = (int)state });
        SavesSystem.Save(PetNestTuning.BundleStorageKey, PetNestCodec.EncodeBundle(bundle));
        GameClock.Instance = new GameClock();
        GameClock.Day = 7;
    }

    static void PetNestSoulFeed()
    {
        string error;
        PrepareSoulFeedNest(500, 3, 40, PetNestPetState.InNest);
        PetNestPetRecord cub = PetNestService.TryGetPet("cub");
        Check(PetNestService.SoulFeedCost(cub) == (PetNestTuning.PetExpPerLevel - 40) * PetNestTuning.SoulFeedSoulsPerExp
            && PetNestTuning.SoulFeedSoulsPerExp == 2, "soul feed costs the missing exp of this level at two souls per exp");
        Check(!PetNestService.SoulFedToday(cub) && PetNestService.SoulFeedDayStamp() == 8L, "fresh cub has not been fed on game day 7");
        int writes = SavesSystem.Writes;
        Check(PetNestService.TryFeedSouls("cub", out error), "soul feed accepts same-lineage souls");
        cub = PetNestService.TryGetPet("cub");
        Check(cub.level == 4 && cub.exp == 0, "soul feed fills exactly one level");
        Check(PetNestService.GetSouls("test") == 380 && PetNestService.GetSouls("other") == 999,
            "soul feed spends only the cub's own lineage");
        Check(cub.lastSoulFeedDay == 8L && PetNestService.SoulFedToday(cub), "soul feed stamps today's game day");
        PetNestBundleData disk = DiskNest();
        Check(SavesSystem.Writes == writes + 1 && disk.nest.pets[0].level == 4 && disk.nest.pets[0].lastSoulFeedDay == 8L
            && disk.nest.soulLedger[0].souls == 380 && disk.nest.soulLedger[1].souls == 999,
            "level, day stamp and soul cost land in one bundle write");

        Check(!PetNestService.TryFeedSouls("cub", out error) && error == "soul_fed_today"
            && PetNestService.GetSouls("test") == 380 && PetNestService.TryGetPet("cub").level == 4,
            "second offering on the same game day is refused without spending");
        GameClock.Day = 8;
        Check(!PetNestService.SoulFedToday(PetNestService.TryGetPet("cub")) && PetNestService.TryFeedSouls("cub", out error)
            && PetNestService.TryGetPet("cub").level == 5 && PetNestService.GetSouls("test") == 180,
            "the next game day takes one more offering");

        PrepareSoulFeedNest(10, 3, 40, PetNestPetState.InNest);
        Check(!PetNestService.TryFeedSouls("cub", out error) && error == "souls_insufficient"
            && PetNestService.GetSouls("test") == 10 && PetNestService.TryGetPet("cub").level == 3
            && PetNestService.TryGetPet("cub").lastSoulFeedDay == 0L, "too few same-lineage souls are refused even when another lineage is rich");

        PrepareSoulFeedNest(500, PetNestTuning.PetMaxLevel, 0, PetNestPetState.InNest);
        Check(PetNestService.SoulFeedCost(PetNestService.TryGetPet("cub")) == 0
            && !PetNestService.TryFeedSouls("cub", out error) && error == "pet_max_level" && PetNestService.GetSouls("test") == 500,
            "adult cubs take no offering");

        PrepareSoulFeedNest(500, 3, 40, PetNestPetState.OnExpedition);
        Check(!PetNestService.TryFeedSouls("cub", out error) && error == "pet_locked_by_expedition" && PetNestService.GetSouls("test") == 500,
            "cubs on an expedition take no offering");

        PrepareSoulFeedNest(500, 3, 40, PetNestPetState.InNest);
        GameClock.Instance = null;
        Check(PetNestService.SoulFeedDayStamp() == 0L && !PetNestService.TryFeedSouls("cub", out error)
            && error == "soul_feed_clock_missing" && PetNestService.GetSouls("test") == 500,
            "without a game clock the offering is refused instead of stamping day zero");
        GameClock.Instance = new GameClock();

        var bundle = PetNestCodec.CreateDefaultBundle();
        bundle.nest.pets.Add(new PetNestPetRecord { id = "x", lineageKey = "test", level = 2, lastSoulFeedDay = 42L });
        string json = PetNestCodec.EncodeBundle(bundle);
        Check(PetNestCodec.DecodeBundle(BossRushJsonParser.ParseOrNull(json)).nest.pets[0].lastSoulFeedDay == 42L,
            "codec round-trips the soul feed day stamp");
        string legacy = Regex.Replace(json, @",\s*""lastSoulFeedDay""\s*:\s*42", string.Empty);
        if (legacy == json) legacy = Regex.Replace(json, @"""lastSoulFeedDay""\s*:\s*42\s*,", string.Empty);
        PetNestBundleData old = PetNestCodec.DecodeBundle(BossRushJsonParser.ParseOrNull(legacy));
        Check(!legacy.Contains("lastSoulFeedDay") && old != null && old.nest.pets.Count == 1
            && old.nest.pets[0].level == 2 && old.nest.pets[0].lastSoulFeedDay == 0L,
            "an old save without the field loads as never fed");
    }
}
