using System;
using System.Collections.Generic;
using BossRush;

/// <summary>2. 剧情阶段：纯规则推进真实表的 story 阶段，逐阶段抽查旗标 / 清场 / 灯，并做编解码往返。</summary>
internal static partial class Program
{
    private static Dictionary<string, SkyIslandStoryData> ExpectedStages(F3AutotestTable table, List<string> errors, out bool ok)
    {
        var perStage = new List<SkyIslandStoryData>();
        ok = F3AutotestJudges.SimulateStages(table, perStage, errors);
        var byId = new Dictionary<string, SkyIslandStoryData>(StringComparer.Ordinal);
        int index = 0;
        foreach (F3AutotestStage stage in table.Stages)
        {
            if (stage.When != "story") continue;
            if (index < perStage.Count) byId[stage.Id] = perStage[index];
            index++;
        }
        if (index != perStage.Count) errors.Add("fixture:per_stage_count " + perStage.Count + " != story stages " + index);
        return byId;
    }

    private static SkyIslandStoryData Expect(Dictionary<string, SkyIslandStoryData> byId, string id)
    {
        SkyIslandStoryData data;
        if (!byId.TryGetValue(id, out data) || data == null) throw new InvalidOperationException("no expected data for stage " + id);
        return data;
    }

    private static bool Judge(SkyIslandStoryData actual, SkyIslandStoryData expected, out string reason)
    {
        string metrics;
        return F3AutotestJudges.JudgeStageData(actual, expected, out metrics, out reason);
    }

    private static void StageCases()
    {
        var errors = new List<string>();
        bool ok;
        F3AutotestTable table = FreshTable();
        Dictionary<string, SkyIslandStoryData> stages = ExpectedStages(table, errors, out ok);
        Check(ok && errors.Count == 0, "real story stages simulate from an empty save: " + Dump(errors));
        Check(stages.Count == 6, "every story stage has expected data (" + stages.Count + ")");

        string diff;
        Check(F3AutotestJudges.SameStory(Expect(stages, "new_save"), SkyIslandStoryRules.CreateDefault(), out diff), "new_save is the default story: " + diff);
        SkyIslandStoryData beacons = Expect(stages, "both_beacons");
        Check(beacons.Has(SkyIslandStoryFlag.WindBeacon) && beacons.Has(SkyIslandStoryFlag.StarLamp) && !beacons.Has(SkyIslandStoryFlag.Ending),
            "both_beacons: wind beacon and star lamp flags set, no ending");
        foreach (string id in new[] { "D", "D_02", "G", "G_02" }) Check(beacons.EncounterCleared(id), "both_beacons clears " + id);
        SkyIslandStoryData storm = Expect(stages, "storm_slain");
        Check(storm.Has(SkyIslandStoryFlag.StormSlain) && storm.BothBeacons && !storm.Has(SkyIslandStoryFlag.Ending), "storm_slain: StormSlain on top of both beacons");
        Check(Expect(stages, "bell_ready").BellKeeperResolved && Expect(stages, "bell_ready").StormResolved, "bell_ready: bell keeper resolved, storm kept");
        SkyIslandStoryData ending = Expect(stages, "ending");
        Check(ending.Has(SkyIslandStoryFlag.Ending), "ending: Ending flag set");
        SkyIslandStoryData lamps = Expect(stages, "ending_lamps");
        Check(lamps.Has(SkyIslandStoryFlag.Ending) && SkyIslandLights.LampsLit(lamps) == 7 && SkyIslandLights.AllLit(lamps)
            && SkyIslandLights.LitCount(lamps) == SkyIslandLights.Target, "ending_lamps: seven windcrystal lamps lit, all ten lights count");
        foreach (SkyIslandLight light in SkyIslandLights.All) Check(SkyIslandLights.Lit(lamps, light.Id), "ending_lamps lights " + light.Id);

        SkyIslandStoryData previous = null;
        foreach (F3AutotestStage stage in table.Stages)
        {
            if (stage.When != "story") continue;
            SkyIslandStoryData expected = Expect(stages, stage.Id);
            SkyIslandStoryData decoded = SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(expected));
            string forward, backward, codec;
            Check(decoded != null && Judge(decoded, expected, out forward) && Judge(expected, decoded, out backward),
                "stage " + stage.Id + " survives encode/decode for JudgeStageData");
            Check(F3AutotestJudges.CodecRoundTrip(expected, out codec), "stage " + stage.Id + " CodecRoundTrip: " + codec);
            if (previous != null) Check(Judge(expected, previous, out forward), "stage " + stage.Id + " keeps everything the previous stage reached: " + forward);
            previous = expected;
        }

        StageRedSamples(lamps);
    }

    private static void StageRedSamples(SkyIslandStoryData lamps)
    {
        // 敲钟挪到双航标之前（新档阶段）：规则拒绝，后面的结局阶段也就没有 Ending。
        F3AutotestTable moved = FreshTable();
        StoryStage(moved, "ending").Apply.Remove("story:RingHomecomingBell");
        StoryStage(moved, "new_save").Apply.Add("story:RingHomecomingBell");
        var errors = new List<string>();
        bool ok;
        Dictionary<string, SkyIslandStoryData> movedStages = ExpectedStages(moved, errors, out ok);
        Check(!ok && AnyStartsWith(errors, "new_save:story:RingHomecomingBell:"), "red: ringing the bell before both beacons is rejected: " + Dump(errors));
        string reason;
        SkyIslandStoryData movedEnding;
        Check(movedStages.TryGetValue("ending", out movedEnding) && !Judge(movedEnding, lamps, out reason) && reason.Contains("flags:16384"),
            "red: the ending stage then misses the Ending flag");

        F3AutotestTable early = FreshTable();
        StoryStage(early, "new_save").Apply.Insert(0, "clear:D");
        errors.Clear();
        ExpectedStages(early, errors, out ok);
        Check(!ok && errors.Contains("new_save:op_before_reset:clear:D"), "red: an op before reset is rejected: " + Dump(errors));

        SkyIslandStoryData noFlag = lamps.Copy();
        noFlag.flags &= ~(int)SkyIslandStoryFlag.StormSlain;
        Check(!Judge(noFlag, lamps, out reason) && reason == "stage_not_reached:flags:32768", "red: one missing flag -> " + reason);
        SkyIslandStoryData noClear = lamps.Copy();
        noClear.clearedEncounters = Array.FindAll(noClear.clearedEncounters, id => id != "G_02");
        Check(!Judge(noClear, lamps, out reason) && reason == "stage_not_reached:cleared:G_02", "red: one missing clear -> " + reason);
        SkyIslandStoryData noNote = lamps.Copy();
        noNote.discoveredNotes = Array.FindAll(noNote.discoveredNotes, id => id != "Light_S4");
        Check(!Judge(noNote, lamps, out reason) && reason == "stage_not_reached:note:Light_S4", "red: one missing note -> " + reason);
        Check(!Judge(null, lamps, out reason) && reason == "stage_data_missing" && !Judge(lamps, null, out reason), "red: missing data never passes");

        // 绿样本：岛上这一趟自己多记的到访、清场、手记不影响「包含期望」。
        SkyIslandStoryData superset = lamps.Copy();
        superset.visitedRegions |= 1 | 256;
        superset.clearedEncounters = new List<string>(superset.clearedEncounters) { "B" }.ToArray();
        superset.discoveredNotes = new List<string>(superset.discoveredNotes) { "Letter_01" }.ToArray();
        string metrics;
        Check(F3AutotestJudges.JudgeStageData(superset, lamps, out metrics, out reason) && reason == null && metrics.Contains("extra_flags=0"),
            "green: extra visits, clears and notes still reach the stage: " + metrics);
        SkyIslandStoryData bothBeacons = SkyIslandStoryRules.CreateDefault();
        bothBeacons.flags = (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp);
        F3AutotestJudges.JudgeStageData(lamps, bothBeacons, out metrics, out reason);
        Check(metrics.Contains("extra_flags=" + (lamps.flags & ~bothBeacons.flags)), "metrics report flags beyond the expectation: " + metrics);

        SkyIslandStoryData corrupt = SkyIslandStoryRules.CreateDefault();
        corrupt.flags = (int)SkyIslandStoryFlag.Ending;
        Check(!F3AutotestJudges.CodecRoundTrip(corrupt, out reason) && reason == "codec_decode_rejected", "red: an ending without beacons is rejected by the codec: " + reason);
    }
}
