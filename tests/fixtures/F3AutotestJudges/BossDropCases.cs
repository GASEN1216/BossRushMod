using System.Collections.Generic;
using BossRush;

/// <summary>
/// 头目 / 岛主真实击杀后的尸体箱（动作 loot_boss → F3AutotestJudges.JudgeBossDrop）。
/// 专属装备表与「岛主必出一件」都从生产档案表读，不在这里抄 TypeID；每条红样本对应一种真实会发生的坏法。
/// </summary>
internal static partial class Program
{
    private static void BossDropCases()
    {
        SkyIslandBossProfile lord = null, chief = null;
        foreach (SkyIslandBossProfile profile in SkyIslandBossRules.Profiles)
        {
            if (profile.Kind == SkyIslandBossKind.Foreman) lord = profile;
            if (profile.Kind == SkyIslandBossKind.Stargazer) chief = profile;
        }
        Check(lord != null && lord.Gear.Length == 3 && lord.NoDropWeight == 0, "foreman profile: three pieces, always drops one");
        Check(chief != null && chief.Gear.Length == 1 && chief.NoDropWeight > 0, "stargazer profile: one piece, may drop nothing");
        if (lord == null || chief == null) return;
        int[] lordGear = GearIds(lord), chiefGear = GearIds(chief);
        int kept = lordGear[1], other = lordGear[0];
        const int Rifle = 101, Ammo = 102;
        string metrics, reason;

        Check(F3AutotestJudges.JudgeBossDrop(Crate(kept, 1, Rifle, 1, Ammo, 30), lordGear, kept, "slot", true, out metrics, out reason) && reason == null,
            "green: lord kept the chosen worn piece plus vanilla loot: " + metrics);
        Check(F3AutotestJudges.JudgeBossDrop(Crate(kept, 1, Rifle, 1), lordGear, kept, "inventory", true, out metrics, out reason) && reason == null,
            "green: chosen piece was topped up into the pack instead: " + metrics);
        Check(F3AutotestJudges.JudgeBossDrop(Crate(Rifle, 1, Ammo, 12), chiefGear, -1, "no_drop", false, out metrics, out reason) && reason == null,
            "green: chief rolled the empty weight and dropped only vanilla loot: " + metrics);

        F3AutotestJudges.JudgeBossDrop(Crate(lordGear[0], 1, lordGear[1], 1, lordGear[2], 1, Rifle, 1), lordGear, -1, "alive", true, out metrics, out reason);
        Check(reason == "drop_not_resolved:alive", "red: death event never fired, the whole worn set went into the crate: " + reason);
        F3AutotestJudges.JudgeBossDrop(Crate(lordGear[0], 1, lordGear[1], 1, lordGear[2], 1, Rifle, 1), lordGear, kept, "slot", true, out metrics, out reason);
        Check(reason == "gear_in_box_3_expected_1", "red: resolved but the unchosen pieces were not removed: " + reason);
        F3AutotestJudges.JudgeBossDrop(Crate(other, 1, Rifle, 1), lordGear, kept, "slot", true, out metrics, out reason);
        Check(reason == "chosen_piece_not_in_box", "red: one piece in the crate but not the chosen one: " + reason);
        F3AutotestJudges.JudgeBossDrop(Crate(kept, 2, Rifle, 1), lordGear, kept, "slot", true, out metrics, out reason);
        Check(reason == "gear_in_box_2_expected_1", "red: chosen piece duplicated: " + reason);
        F3AutotestJudges.JudgeBossDrop(Crate(kept, 1), lordGear, kept, "slot", true, out metrics, out reason);
        Check(reason == "vanilla_loot_missing", "red: vanilla loot vanished with the unchosen pieces: " + reason);
        F3AutotestJudges.JudgeBossDrop(Crate(Rifle, 1), lordGear, -1, "no_drop", true, out metrics, out reason);
        Check(reason == "lord_dropped_no_gear", "red: a lord must always leave one piece: " + reason);
        F3AutotestJudges.JudgeBossDrop(Crate(chiefGear[0], 1, Rifle, 1), chiefGear, -1, "no_drop", false, out metrics, out reason);
        Check(reason == "gear_in_box_1_expected_0", "red: chief rolled empty but the helmet still dropped: " + reason);
        F3AutotestJudges.JudgeBossDrop(Crate(Rifle, 1), lordGear, -1, "slot", true, out metrics, out reason);
        Check(reason == "outcome_chosen_mismatch", "red: outcome says a piece was kept but nothing was chosen: " + reason);
        foreach (string broken in new[] { "error", "missing", "character_item_missing" })
        {
            F3AutotestJudges.JudgeBossDrop(Crate(Rifle, 1), lordGear, kept, broken, true, out metrics, out reason);
            Check(reason == "drop_not_resolved:" + broken, "red: loot resolution reported " + broken + ": " + reason);
        }
        Check(!F3AutotestJudges.JudgeBossDrop(null, lordGear, kept, "slot", true, out metrics, out reason) && reason == "crate_not_read",
            "red: no crate read at all: " + reason);
        Check(metrics != null && metrics.Contains("gear_in_box=0") && metrics.Contains("outcome=slot"), "metrics still reported without a crate: " + metrics);
    }

    private static int[] GearIds(SkyIslandBossProfile profile)
    {
        int[] ids = new int[profile.Gear.Length];
        for (int i = 0; i < ids.Length; i++) ids[i] = profile.Gear[i].TypeId;
        return ids;
    }

    private static Dictionary<int, int> Crate(params int[] typeIdCountPairs)
    {
        var crate = new Dictionary<int, int>();
        for (int i = 0; i + 1 < typeIdCountPairs.Length; i += 2)
        {
            int existing;
            crate.TryGetValue(typeIdCountPairs[i], out existing);
            crate[typeIdCountPairs[i]] = existing + typeIdCountPairs[i + 1];
        }
        return crate;
    }
}
