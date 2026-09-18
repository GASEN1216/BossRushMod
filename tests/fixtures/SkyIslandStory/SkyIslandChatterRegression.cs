using System;
using System.Collections.Generic;
using BossRush;

/// <summary>
/// 天空岛头顶气泡话语表的纯规则回归（`SkyIslandChatterLines` 原样执行）。
///
/// 守卫（tests/SkyIslandChatterGuard.py）核对的是**文本本身**：谁有台词、有没有人设、长不长、重不重。
/// 这里核对的是**规则**——离线能证、实机看不出来的那几条：
/// - `Pick` 挑出来的下标一定在池子里，而且**永远不与上一句重复**（否则站在苇白跟前会听见她连说两遍同一句）；
/// - 只有一句的池子照样给得出下标（无声钟守那种极短池子不能挑出 -1）；
/// - 居民台词随剧情进度换池子：结局前后说的不是同一批话；
/// - 每位头目 / 岛主的三个时刻、两个小兵共享库的三个时刻都取得到话，**断风三变体互不相同**（防换皮）；
/// - 噬风没有台词，取到的池子是空的——`HasLines` 为 false，调用方整条早退。
/// </summary>
internal static class SkyIslandChatterRegression
{
    internal static void Run(Action<bool, string> check)
    {
        PickNeverRepeats(check);
        ResidentPoolsFollowStory(check);
        EverySpeakerHasLines(check);
        SilentOnesStaySilent(check);
    }

    /// <summary>`Pick` 是纯函数：逐个种子复算，不靠大数定律。</summary>
    private static void PickNeverRepeats(Action<bool, string> check)
    {
        check(SkyIslandChatterLines.Pick(0, -1, 7) < 0, "chatter pick: an empty pool yields no line");
        check(SkyIslandChatterLines.Pick(1, -1, 7) == 0 && SkyIslandChatterLines.Pick(1, 0, 7) == 0,
            "chatter pick: a one-line pool always yields that line, even right after saying it");

        bool inRange = true, repeated = false, covered = true;
        for (int count = 2; count <= 12; count++)
        {
            for (int last = -1; last < count; last++)
            {
                var hit = new HashSet<int>();
                for (int seed = 0; seed < 512; seed++)
                {
                    int index = SkyIslandChatterLines.Pick(count, last, seed);
                    if (index < 0 || index >= count) inRange = false;
                    if (last >= 0 && index == last) repeated = true;
                    hit.Add(index);
                }
                // 除去上一句之外的每一条都抽得到：否则池子里有几句永远说不出口。
                int expected = last >= 0 && last < count ? count - 1 : count;
                if (hit.Count != expected) covered = false;
            }
        }
        check(inRange, "chatter pick: the index always lands inside the pool");
        check(!repeated, "chatter pick: never repeats the line just spoken");
        check(covered, "chatter pick: every other line in the pool is reachable");
    }

    private static void ResidentPoolsFollowStory(Action<bool, string> check)
    {
        SkyIslandStoryData fresh = SkyIslandStoryRules.CreateDefault();
        SkyIslandStoryData ending = SkyIslandStoryRules.CreateDefault();
        ending.flags |= (int)SkyIslandStoryFlag.Ending;
        SkyIslandStoryData reconciled = SkyIslandStoryRules.CreateDefault();
        reconciled.flags |= (int)SkyIslandStoryFlag.ZhelingReconciled;

        foreach (string npcId in new[] { "sky_qinghe", "sky_weibai", "sky_fuzhou" })
        {
            string[] before = SkyIslandChatterLines.Resident(npcId, fresh);
            string[] after = SkyIslandChatterLines.Resident(npcId, ending);
            check(SkyIslandChatterLines.HasLines(before) && SkyIslandChatterLines.HasLines(after),
                "chatter residents: " + npcId + " has idle lines before and after the ending");
            check(!Same(before, after),
                "chatter residents: " + npcId + " talks about different things once the bell has rung");
        }
        check(!Same(SkyIslandChatterLines.Resident("sky_zheling", fresh),
                SkyIslandChatterLines.Resident("sky_zheling", reconciled)),
            "chatter residents: Zheling eases up after reconciling");
        // 眠苔不随进度换池子（她只关心伤口），但必须有话；存档还没读出来的那一帧（null）也要有话。
        check(SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Resident("sky_miantai", null)),
            "chatter residents: Miantai still has lines before the save is read");
        check(!SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Resident("sky_nobody", fresh)),
            "chatter residents: an unknown id stays silent instead of borrowing someone else's voice");

        // 【为什么这一段必须在回归里，不能只靠守卫】守卫是按源码里的 `case` 聚合数条数的，
        // 一位居民的两个剧情分支加起来够数就绿；某个分支只剩两三句照样藏得住，
        // 而玩家在那个分支上站一会儿就会听见同一句来回倒。这里按**每个分支实际返回的数组**判。
        SkyIslandStoryData[] states =
        {
            null, fresh, ending, reconciled,
            WithFlags(SkyIslandStoryFlag.ZhelingReconciled | SkyIslandStoryFlag.Ending)
        };
        string[] talkers = { "sky_qinghe", "sky_weibai", "sky_fuzhou", "sky_miantai", "sky_zheling" };
        for (int s = 0; s < states.Length; s++)
            for (int i = 0; i < talkers.Length; i++)
            {
                int count = SkyIslandChatterLines.Resident(talkers[i], states[s]).Length;
                check(count >= 4, "chatter residents: " + talkers[i] + " has " + count
                    + " lines in story state " + s + " (every branch needs at least four, or it loops audibly)");
            }
        // 无声钟守是唯一的例外：钟声与省略号，三条就够，也不该更多。
        for (int s = 0; s < states.Length; s++)
        {
            int count = SkyIslandChatterLines.Resident("sky_bellkeeper", states[s]).Length;
            check(count >= 3, "chatter residents: the Silent Bell Keeper still has his chimes in story state " + s);
        }
    }

    private static SkyIslandStoryData WithFlags(SkyIslandStoryFlag flags)
    {
        SkyIslandStoryData data = SkyIslandStoryRules.CreateDefault();
        data.flags |= (int)flags;
        return data;
    }

    private static void EverySpeakerHasLines(Action<bool, string> check)
    {
        SkyIslandChatterMoment[] mobMoments =
        {
            SkyIslandChatterMoment.Idle, SkyIslandChatterMoment.Noticed, SkyIslandChatterMoment.AllyDown
        };
        foreach (SkyIslandChatterMoment moment in mobMoments)
        {
            check(SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Mob(false, moment)),
                "chatter mobs: cloud-edge scavengers speak at " + moment);
            check(SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Mob(true, moment)),
                "chatter mobs: Galebreaker Rangers speak at " + moment);
            check(!Same(SkyIslandChatterLines.Mob(false, moment), SkyIslandChatterLines.Mob(true, moment)),
                "chatter mobs: the two factions do not share a pool at " + moment);
        }
        // 两个共享库在战斗时刻之外没有别的时刻（头目专用的 Wounded / Down 不该漏进小兵库）。
        check(!SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Mob(false, SkyIslandChatterMoment.Wounded))
            && !SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Mob(false, SkyIslandChatterMoment.Down)),
            "chatter mobs: grunts have no boss-only moments");

        SkyIslandChatterMoment[] bossMoments =
        {
            SkyIslandChatterMoment.Noticed, SkyIslandChatterMoment.Wounded, SkyIslandChatterMoment.Down
        };
        foreach (SkyIslandBossProfile profile in SkyIslandBossRules.Profiles)
        {
            foreach (SkyIslandChatterMoment moment in bossMoments)
                check(SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Boss(profile.Kind, profile.Variant, moment)),
                    "chatter bosses: " + profile.Id + " speaks at " + moment);
        }
        // 断风游猎三个变体共用一个控制器，但绝不共用台词（防换皮，口径同五栏的「核心招式两两不同」）。
        for (int moment = 0; moment < bossMoments.Length; moment++)
        {
            string[] chaser = SkyIslandChatterLines.Boss(SkyIslandBossKind.Windhunter, 1, bossMoments[moment]);
            string[] stalker = SkyIslandChatterLines.Boss(SkyIslandBossKind.Windhunter, 2, bossMoments[moment]);
            string[] warden = SkyIslandChatterLines.Boss(SkyIslandBossKind.Windhunter, 3, bossMoments[moment]);
            check(!Same(chaser, stalker) && !Same(stalker, warden) && !Same(chaser, warden),
                "chatter bosses: the three Galebreaker Rangers say different things at " + bossMoments[moment]);
        }
        foreach (string champion in new[] { "zheling", "bellkeeper" })
            foreach (SkyIslandChatterMoment moment in bossMoments)
                check(SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Champion(champion, moment)),
                    "chatter champions: " + champion + " answers at " + moment);
    }

    /// <summary>按人设不说话的：噬风（一股风）与任何没登记的说话者。</summary>
    private static void SilentOnesStaySilent(Action<bool, string> check)
    {
        check(!SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Champion("storm", SkyIslandChatterMoment.Noticed))
            && !SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Champion("storm", SkyIslandChatterMoment.Down)),
            "chatter: the Windeater is a gust of wind and never speaks");
        check(!SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Boss(SkyIslandBossKind.Foreman, 0, SkyIslandChatterMoment.AllyDown)),
            "chatter: bosses do not borrow the grunts' ally-down line");
        check(!SkyIslandChatterLines.HasLines(SkyIslandChatterLines.Champion(null, SkyIslandChatterMoment.Noticed)),
            "chatter: a null champion id stays silent instead of throwing");
    }

    private static bool Same(string[] left, string[] right)
    {
        if (left == null || right == null) return left == right;
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal)) return false;
        return true;
    }
}
