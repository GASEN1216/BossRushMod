using System;
using System.Collections.Generic;
using BossRush;

// 宿主边界：仅模拟事件载荷、阵营和天气；规则、内容、计时、结算与编解码均链接生产文件。
class CharacterMainControl
{
    public bool IsMainCharacter, isBossCharacter;
    public int Team;
}
static class Team { public static bool IsEnemy(int from, int to) { return from != to; } }
class Health
{
    public bool IsMainCharacterHealth;
    public CharacterMainControl Character;
    public CharacterMainControl TryGetCharacter() { return Character; }
}
struct DamageInfo { public CharacterMainControl fromCharacter; public float finalDamage; }
static class RaidUtilities
{
    public struct RaidInfo { public bool valid, dead, ended; }
    public static event Action<RaidInfo> OnNewRaid, OnRaidEnd;
    public static void Start(RaidInfo info) { OnNewRaid?.Invoke(info); }
    public static void End(RaidInfo info) { OnRaidEnd?.Invoke(info); }
}
namespace Duckov.Economy
{
    static class EconomyManager
    {
        public static event Action<long, long> OnMoneyChanged;
        public static void Change(long oldValue, long newValue) { OnMoneyChanged?.Invoke(oldValue, newValue); }
    }
}
namespace Duckov.Weathers
{
    enum Weather { Sunny, Cloudy, Rainy, Snow, Stormy_I, Stormy_II }
    class WeatherManager
    {
        public static WeatherManager Instance = new WeatherManager();
        public bool ForceWeather;
        public Weather ForceWeatherValue;
        public static Weather GetWeather(TimeSpan when) { return Weather.Rainy; }
    }
}

partial class Program
{
    static void Playability()
    {
        BountyRules();
        BountyDebts();
        ContentAndClock();
        Collection();
    }

    static void BountyRules()
    {
        var survival = DailyReportBounty.Rebuild("no_death", 1);
        Check(!DailyReportBounty.IsComplete(survival, new DailyReportStats { Raids = 1 }),
            "deploying without extracting cannot clear safe-return bounty");
        Check(DailyReportBounty.IsComplete(survival, new DailyReportStats { Extractions = 1 }),
            "cross-day raid counts on its actual extraction day");
        Check(!DailyReportBounty.IsComplete(survival, new DailyReportStats { Extractions = 1, Deaths = 1 }),
            "a later death before settlement fails the survival bounty");
        Check(!DailyReportBounty.IsComplete(survival, new DailyReportStats()), "base idling earns no survival bounty");
        Check(DailyReportBounty.IsComplete(DailyReportBounty.Rebuild("earn_money", 5000),
            new DailyReportStats { MoneyEarned = 5000, MoneySpent = 7000 }), "income bounty uses gross income as described");
        Check(DailyReportBounty.Rebuild("kills", 999) == null, "unknown target is not silently repriced");
        var seen = new HashSet<string>();
        for (int day = 1; day <= 1000; day++)
        {
            var def = DailyReportBounty.SelectForDay(123, day);
            var again = DailyReportBounty.SelectForDay(123, day);
            if (def.Id != again.Id || def.Target != again.Target || def.CashReward != again.CashReward)
                throw new Exception("non-deterministic daily selection");
            seen.Add(def.Id + "/" + def.Target);
            var stats = new DailyReportStats();
            switch (def.Kind)
            {
                case DailyReportBountyKind.Kills: stats.Kills = def.Target; break;
                case DailyReportBountyKind.BossKills: stats.BossKills = def.Target; break;
                case DailyReportBountyKind.Extractions: stats.Extractions = def.Target; break;
                case DailyReportBountyKind.EarnMoney: stats.MoneyEarned = def.Target; break;
                case DailyReportBountyKind.NoDeath: stats.Extractions = 1; break;
            }
            if (!DailyReportBounty.IsComplete(def, stats)
                || DailyReportBounty.Rebuild(def.Id, def.Target).CashReward != def.CashReward)
                throw new Exception("unreachable or unrecoverable bounty " + def.Id);
        }
        Check(seen.Count == 13, "all five bounty kinds and every distinct tier are reachable and reconstructable");
    }

    static void BountyDebts()
    {
        Reset(1, 0, 0, 1);
        DailyReportRewards.RejectCash = true;
        long expected = 0;
        for (int day = 1; day <= 3; day++)
        {
            expected += DailyReportService.GetActiveBounty().CashReward;
            DailyReportPersistence.Current.Today = new DailyReportStats
                { Kills = 120, BossKills = 5, Extractions = 4, MoneyEarned = 40000 };
            DailyReportService.DebugAdvanceGameSeconds(DailyReportTuning.GameSecondsPerDay);
            Check(DailyReportPersistence.Current.BountyDayIndex == day
                && DailyReportService.GetPendingBountyCash(DailyReportPersistence.Current) == expected,
                "unavailable cash preserves every earned bounty and still settles day " + day);
        }
        Reload();
        Check(DailyReportService.GetPendingBountyCash(DailyReportPersistence.Current) == expected,
            "all pending cash survives real JSON round-trip");
        DailyReportService.DebugAdvanceGameSeconds(DailyReportTuning.GameSecondsPerDay);
        Check(!DailyReportPersistence.Current.BountyCompleted
            && DailyReportService.GetPendingBountyCash(DailyReportPersistence.Current) == expected,
            "incomplete latest bounty does not erase older cash");
        DailyReportRewards.RejectCash = false;
        DailyReportService.TryRedeliverPendingBountyReward();
        DailyReportService.TryRedeliverPendingBountyReward();
        Check(DailyReportRewards.Cash == expected && DailyReportService.GetPendingBountyCash(DailyReportPersistence.Current) == 0,
            "all earned cash arrives once after recovery");
        Check(!DailyReportPersistence.Current.BountyRewardClaimed, "paying old debt does not mark failed latest bounty as cleared");

        Reset(1, 0, 0, 4);
        var data = DailyReportPersistence.Current;
        data.BountyCompleted = true; data.BountyKindId = "kills"; data.BountyTarget = 30; data.BountyDayIndex = 1;
        Check(DailyReportService.GetPendingBountyCash(data) == 800, "legacy debt restores original tier without rerolling seed");
        DailyReportPersistence.RejectStore = true;
        DailyReportService.DebugAdvanceGameSeconds(DailyReportTuning.GameSecondsPerDay);
        Check(DailyReportPersistence.Current.DayIndex == 4 && data.PendingBountyCash == 0 && DailyReportRewards.Cash == 0,
            "rejected settlement neither archives live debt nor pays money");
        string encoded = DailyReportCodec.Encode(data);
        Check(DailyReportCodec.Decode(encoded.Replace("\"pendingBountyCash\":0", "\"pendingBountyCash\":-1")) == null,
            "corrupt declared cash debt fails closed");
    }

    static void ContentAndClock()
    {
        Reset(1, 0, 0, 1, 0, 0);
        var first = DailyReportContent.BuildCurrentIssue();
        long seed = DailyReportPersistence.Current.BountySeed;
        var again = DailyReportContent.BuildCurrentIssue();
        Check(seed != 0 && seed == DailyReportPersistence.Current.BountySeed
            && first.TodayBountyTitle == again.TodayBountyTitle && first.FortuneLine == again.FortuneLine,
            "first-ever open freezes one seed for the whole issue");
        var data = DailyReportPersistence.Current;
        data.HasYesterday = true; data.Yesterday = new DailyReportStats { Kills = 1, BossKills = 1 };
        data.BountyCompleted = true; data.BountyKindId = "kills"; data.BountyTarget = 30; data.BountyCashReward = 800;
        var cn = DailyReportContent.BuildCurrentIssue();
        Check(cn.HeadlineBody.Contains("击杀 1 名敌人，其中 1 名是首领") && cn.BountyResultLine.Contains("待补发")
            && cn.TodayBountyCash > 0, "Chinese issue reports inclusive kill count, pending cash and today's prize");
        L10n.Chinese = false;
        var en = DailyReportContent.BuildCurrentIssue();
        Check(en.HeadlineBody.Contains("1 confirmed kills included 1 bosses") && en.BountyResultLine.Contains("pending"),
            "same stored issue is rendered in the selected language");
        L10n.Chinese = true;
        Check(new DailyReportStats { DamageTaken = 2 }.HasAnyActivity, "damage-only day is not reported as no activity");
        GameClock.Instance.clockTimeScale = 0;
        DailyReportService.Tick(1);
        Check(DailyReportService.CarrySeconds == 0, "zero official clock scale stays stopped");
        GameClock.Instance = null;
        DailyReportService.Tick(1);
        Check(DailyReportService.CarrySeconds == 0, "missing world clock cannot advance from menu");
        GameClock.Instance = new GameClock();
        DailyReportService.Tick(float.NaN);
        DailyReportService.Tick(1);
        Check(DailyReportService.CarrySeconds == 60, "invalid tick cannot poison normal timing");
        DailyReportService.NotifySlotChanged();
        DailyReportService.Tick(1);
        Check(DailyReportService.CarrySeconds == 60, "slot change resets transient clock and backoff");
    }

    static void Collection()
    {
        Reset(1, 0, 0, 1);
        var player = new CharacterMainControl { IsMainCharacter = true, Team = 1 };
        var enemy = new Health { Character = new CharacterMainControl { Team = 2, isBossCharacter = true } };
        var friend = new Health { Character = new CharacterMainControl { Team = 1 } };
        var hit = new DamageInfo { fromCharacter = player, finalDamage = 10 };
        DailyReportStatsCollector.OnGlobalDead(new Health(), hit);
        DailyReportStatsCollector.OnGlobalDead(friend, hit);
        DailyReportStatsCollector.OnGlobalDead(enemy, hit);
        DailyReportStatsCollector.OnGlobalHurt(friend, hit);
        DailyReportStatsCollector.OnGlobalHurt(enemy, hit);
        DailyReportStatsCollector.OnGlobalHurt(new Health { IsMainCharacterHealth = true }, new DamageInfo { finalDamage = 4 });
        var stats = DailyReportPersistence.Current.Today;
        Check(stats.Kills == 1 && stats.BossKills == 1 && stats.DamageDealt == 10 && stats.DamageTaken == 4,
            "only hostile characters count as kills/damage; environmental damage taken is included");
        DailyReportStatsCollector.EnsureSubscribed(); DailyReportStatsCollector.EnsureSubscribed();
        RaidUtilities.Start(new RaidUtilities.RaidInfo());
        RaidUtilities.End(new RaidUtilities.RaidInfo());
        RaidUtilities.Start(new RaidUtilities.RaidInfo { valid = true });
        RaidUtilities.End(new RaidUtilities.RaidInfo { valid = true, ended = true, dead = true });
        RaidUtilities.End(new RaidUtilities.RaidInfo { valid = true, ended = true });
        Check(stats.Raids == 1 && stats.Extractions == 1, "valid raid start and safe end are counted once per subscription");
        ModBehaviour.ModeH = true;
        DailyReportStatsCollector.OnGlobalDead(enemy, hit);
        Duckov.Economy.EconomyManager.Change(0, 50);
        RaidUtilities.End(new RaidUtilities.RaidInfo { valid = true, ended = true });
        ModBehaviour.ModeH = false;
        Check(stats.Kills == 1 && stats.MoneyEarned == 0 && stats.Extractions == 1, "Mode H remains excluded from all statistics");
        DailyReportStatsCollector.SetMoneyDeltaSuppressed(true);
        Duckov.Economy.EconomyManager.Change(0, 50);
        DailyReportStatsCollector.SetMoneyDeltaSuppressed(false);
        Duckov.Economy.EconomyManager.Change(50, 100);
        Check(stats.MoneyEarned == 50, "own cash rewards do not feed the income bounty");
        DailyReportStatsCollector.ShutdownSubscription();
        Duckov.Economy.EconomyManager.Change(100, 150);
        Check(stats.MoneyEarned == 50, "collector shutdown removes money subscription");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) DailyReportStatsCollector.OnGlobalHurt(enemy, hit);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "warmed collector hot path allocates no managed memory in isolated host");
    }
}
