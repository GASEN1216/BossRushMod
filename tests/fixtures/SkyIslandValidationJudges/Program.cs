using System;
using System.Collections.Generic;
using BossRush;

/// <summary>
/// F3 天空岛运行时用例的纯判据执行回归（2026-09-14）。
///
/// 判据函数由 run.py 从 F3GameplayValidationSkyIslandRuntimeCases.cs 逐字抽出，与生产纯规则一起编译。
/// 每条判据都喂「绿样本」（与生产内置表 / 规则一致的状态）和至少一个「红样本」（人为破坏的状态），
/// 证明判据既不恒真也不恒假。它证明不了游戏里取数的那一半（Unity 侧读场景、读官方图鉴），那一半只能实机跑 F3。
/// </summary>
internal static class Program
{
    private static int assertions;

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception("FAIL: " + message);
    }

    private static void Main()
    {
        L10n.IsChinese = true;
        BountyGating();
        ChoiceGates();
        EncounterTable();
        EncounterRuntime();
        LampsWind();
        OfficialNotes();
        Keepsakes();
        GatherNodes();
        LetterPigeon();
        Console.WriteLine("PASS: " + assertions + " assertions; F3 Sky Island runtime-case judges (pure half only, no Unity)");
    }

    private static bool Run(Func<Tuple<bool, string, string>> judge, out string metrics, out string reason)
    {
        Tuple<bool, string, string> result = judge();
        metrics = result.Item2;
        reason = result.Item3;
        return result.Item1;
    }

    // ---------------------------------------------------------------- 委托门控：驱蚋软门、前三类硬门
    private static void BountyGating()
    {
        SkyIslandBountyKind[] kinds = SkyIslandBounty.AllKinds;
        Func<SkyIslandBountyKind, int> target = kind => 3;
        var available = new Dictionary<SkyIslandBountyKind, int>();
        foreach (SkyIslandBountyKind kind in kinds) available[kind] = 5;
        Func<SkyIslandBountyKind, int> availableFor = kind => available[kind];
        string metrics, reason;

        Check(F3GameplayValidationRunner.JudgeBountyGating(kinds, target, availableFor, false, SkyIslandBountyKind.None, 0, 0, 1, 12,
            out metrics, out reason) && metrics.Contains("soft=none"), "bounty: no active contract and every kind dispatchable -> PASS");

        available[SkyIslandBountyKind.Gnats] = 0;
        Check(F3GameplayValidationRunner.JudgeBountyGating(kinds, target, availableFor, true, SkyIslandBountyKind.Gnats, 2, 12, 1, 12,
            out metrics, out reason) && metrics.Contains("soft=gnats_unfinishable_now"),
            "bounty: daytime with an unfinished gnat contract is legal (soft gate) and only shows up in metrics");

        foreach (SkyIslandBountyKind hard in new[] { SkyIslandBountyKind.Threats, SkyIslandBountyKind.Salvage, SkyIslandBountyKind.Survey })
        {
            foreach (SkyIslandBountyKind kind in kinds) available[kind] = 5;
            available[hard] = 0;
            Check(!F3GameplayValidationRunner.JudgeBountyGating(kinds, target, availableFor, true, hard, 1, 4, 1, 12,
                out metrics, out reason) && reason.Contains("active_contract_unfinishable"),
                "bounty: an unfinishable " + hard + " contract is still a hard FAIL");
        }

        foreach (SkyIslandBountyKind kind in kinds) available[kind] = 5;
        available[SkyIslandBountyKind.Survey] = 13;
        Check(!F3GameplayValidationRunner.JudgeBountyGating(kinds, target, availableFor, false, SkyIslandBountyKind.None, 0, 0, 1, 12,
            out metrics, out reason) && reason.Contains("survey_available_exceeds_regions"), "bounty: survey availability above indexed regions -> FAIL");
        available[SkyIslandBountyKind.Survey] = 5;
        Check(!F3GameplayValidationRunner.JudgeBountyGating(kinds, kind => kind == SkyIslandBountyKind.Salvage ? 0 : 3, availableFor, false,
            SkyIslandBountyKind.None, 0, 0, 1, 12, out metrics, out reason) && reason.Contains("target_not_positive"), "bounty: a zero target -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeBountyGating(kinds, target, availableFor, false, SkyIslandBountyKind.None, 0, 0,
            SkyIslandBounty.MaxRounds + 1, 12, out metrics, out reason) && reason.Contains("rounds_over_max"), "bounty: rounds over max -> FAIL");
    }

    // ---------------------------------------------------------------- 选项门
    private static void ChoiceGates()
    {
        string metrics, reason;
        string[] shape = { "Label", "Select" };
        SkyIslandStoryData fresh = SkyIslandStoryRules.CreateDefault();
        Check(F3GameplayValidationRunner.JudgeChoiceGates(fresh, -1, true, shape, out metrics, out reason),
            "choice gates: production rules on a new save are consistent (CanApply == TryApply for every action) -> PASS: " + reason);
        Check(metrics.Contains("journal_home=not_opened_this_raid") && metrics.Contains("action_pairs=" + (2 * Enum.GetValues(typeof(SkyIslandStoryAction)).Length)),
            "choice gates: both samples cover every action and an unopened journal is only noted");

        SkyIslandStoryData late = SkyIslandStoryRules.CreateDefault();
        late.flags = (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp);
        late.clearedEncounters = new[] { "D", "D_02", "G", "G_02" };
        Check(F3GameplayValidationRunner.JudgeChoiceGates(late, 2, true, shape, out metrics, out reason),
            "choice gates: a mid-story save with a two-item journal home page -> PASS: " + reason);
        Check(!metrics.Contains("applicable=0"), "choice gates: the mid-story sample really exercises applicable actions");

        Check(!F3GameplayValidationRunner.JudgeChoiceGates(fresh, 6, true, shape, out metrics, out reason) && reason.Contains("journal_home_items=6"),
            "choice gates: the old six-item flat journal home page -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeChoiceGates(fresh, -1, true, new[] { "Label", "Select", "Interactable" }, out metrics, out reason)
            && reason.Contains("choice_type_fields"), "choice gates: a choice type that can be greyed out -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeChoiceGates(fresh, -1, true, null, out metrics, out reason), "choice gates: missing type shape -> FAIL");
        Check(F3GameplayValidationRunner.JudgeChoiceGates(fresh, -1, false, shape, out metrics, out reason) && metrics.Contains("length_not_judged"),
            "choice gates: in English the brief length is not judged (English copy has its own layout checks)");
        Check(!F3GameplayValidationRunner.JudgeChoiceGates(null, -1, true, shape, out metrics, out reason) && reason.Contains("story_data_missing"),
            "choice gates: missing story data -> FAIL");
    }

    // ---------------------------------------------------------------- 遭遇内容表
    private static SkyIslandContentData CloneFallback()
    {
        SkyIslandContentData source = SkyIslandContent.CreateFallback();
        var copy = new SkyIslandEncounterDefinition[source.Encounters.Length];
        for (int i = 0; i < copy.Length; i++)
        {
            SkyIslandEncounterDefinition e = source.Encounters[i];
            copy[i] = new SkyIslandEncounterDefinition { Id = e.Id, Marker = e.Marker, Count = e.Count, Manual = e.Manual, Tier = e.Tier, Lead = e.Lead };
        }
        source.Encounters = copy;
        return source;
    }

    private static void EncounterTable()
    {
        string metrics, reason;
        Check(F3GameplayValidationRunner.JudgeEncounterTable(SkyIslandContent.CreateFallback(), out metrics, out reason)
            && metrics.Contains("table_groups=21") && metrics.Contains("table_enemies=61"), "encounters: the production table has 21 groups / 61 enemies -> PASS: " + reason);

        SkyIslandContentData dropped = CloneFallback();
        Array.Resize(ref dropped.Encounters, dropped.Encounters.Length - 1);
        Check(!F3GameplayValidationRunner.JudgeEncounterTable(dropped, out metrics, out reason) && reason.Contains("groups=20"), "encounters: one group missing -> FAIL");

        SkyIslandContentData manual = CloneFallback();
        foreach (SkyIslandEncounterDefinition e in manual.Encounters) if (e.Id == "E_03") e.Manual = true;
        Check(!F3GameplayValidationRunner.JudgeEncounterTable(manual, out metrics, out reason) && reason.Contains("E_03:manual"),
            "encounters: a round-three group turned manual -> FAIL");

        SkyIslandContentData dock = CloneFallback();
        dock.Encounters[0].Marker = "EnemySpawn_A";
        Check(!F3GameplayValidationRunner.JudgeEncounterTable(dock, out metrics, out reason) && reason.Contains("auto_group_in_safe_hub"),
            "encounters: an automatic group on the dock (A) -> FAIL");
        SkyIslandContentData market = CloneFallback();
        market.Encounters[1].Id = "B_03";
        Check(!F3GameplayValidationRunner.JudgeEncounterTable(market, out metrics, out reason) && reason.Contains("auto_group_in_safe_hub"),
            "encounters: an automatic group named for the Windchime Market (B) -> FAIL");

        SkyIslandContentData thin = CloneFallback();
        thin.Encounters[2].Count = 2;
        Check(!F3GameplayValidationRunner.JudgeEncounterTable(thin, out metrics, out reason) && reason.Contains("enemies=60"), "encounters: 60 enemies -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeEncounterTable(null, out metrics, out reason), "encounters: no table -> FAIL");
        Check(F3GameplayValidationRunner.RegionToken("Relay_K1", true) == "K1" && F3GameplayValidationRunner.RegionToken("C_02", false) == "C",
            "encounters: region token parsing");
    }

    private static void EncounterRuntime()
    {
        string metrics, reason;
        Check(F3GameplayValidationRunner.JudgeEncounterRuntime(21, 3, 12, 200, 20f, 40f, 50f, out metrics, out reason), "encounter runtime: 12 alive at the cap -> PASS");
        Check(!F3GameplayValidationRunner.JudgeEncounterRuntime(21, 3, 13, 200, 20f, 40f, 50f, out metrics, out reason) && reason.Contains("living_peak=13"),
            "encounter runtime: 13 alive -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeEncounterRuntime(16, 0, 0, 200, 10f, 12f, 50f, out metrics, out reason) && reason.Contains("groups_built=16"),
            "encounter runtime: only 16 groups built -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeEncounterRuntime(21, 8, 8, 200, 61f, 90f, 50f, out metrics, out reason) && reason.Contains("dense_p95_ms"),
            "encounter runtime: dense segment over the frame threshold -> FAIL");
        Check(F3GameplayValidationRunner.JudgeEncounterRuntime(21, 2, 2, 200, 61f, 90f, 50f, out metrics, out reason) && metrics.Contains("dense=False"),
            "encounter runtime: a slow frame outside a dense segment is only recorded");
    }

    // ---------------------------------------------------------------- 灯与风
    private static void LampsWind()
    {
        string metrics, reason;
        SkyIslandStoryData data = SkyIslandStoryRules.CreateDefault();
        var anchors = new HashSet<string>(SkyIslandLights.HearthMarkers);
        foreach (SkyIslandLight lamp in SkyIslandLights.All) anchors.Add(lamp.Marker);
        var fires = new HashSet<string>(SkyIslandLights.HearthMarkers);
        Func<string, bool> anchor = m => anchors.Contains(m);
        Func<string, bool> fire = m => fires.Contains(m);
        int lit = SkyIslandLights.LitCount(data);
        var none = new SkyIslandWindSample();

        Check(F3GameplayValidationRunner.JudgeLampsWind(data, anchor, fire, 0, lit, none, out metrics, out reason) && metrics.Contains("not_sampled_yet"),
            "lamps: a new save with only the three hearths lit -> PASS: " + reason);

        SkyIslandLight first = SkyIslandLights.All[0];
        SkyIslandStoryData oneLamp = SkyIslandStoryRules.CreateDefault();
        oneLamp.discoveredNotes = new[] { first.Id };
        Check(!F3GameplayValidationRunner.JudgeLampsWind(oneLamp, anchor, fire, 0, SkyIslandLights.LitCount(oneLamp), none, out metrics, out reason)
            && reason.Contains("saved_lit_but_dark"), "lamps: a lamp lit in the save but dark in the scene -> FAIL");
        fires.Add(first.Marker);
        Check(F3GameplayValidationRunner.JudgeLampsWind(oneLamp, anchor, fire, 0, SkyIslandLights.LitCount(oneLamp), none, out metrics, out reason),
            "lamps: lit in the save and built in the scene -> PASS");
        Check(!F3GameplayValidationRunner.JudgeLampsWind(data, anchor, fire, 0, lit, none, out metrics, out reason) && reason.Contains("dark_in_save_but_lit"),
            "lamps: a lamp standing in the scene that the save never lit -> FAIL");
        fires.Remove(first.Marker);
        anchors.Remove(SkyIslandLights.HearthMarkers[0]);
        Check(!F3GameplayValidationRunner.JudgeLampsWind(data, anchor, fire, 1, lit, none, out metrics, out reason)
            && reason.Contains("anchor_missing") && reason.Contains("missing_fire_anchors=1"), "lamps: a missing hearth anchor -> FAIL");
        anchors.Add(SkyIslandLights.HearthMarkers[0]);
        Check(!F3GameplayValidationRunner.JudgeLampsWind(data, anchor, fire, 0, lit + 1, none, out metrics, out reason) && reason.Contains("lights_lit="),
            "lamps: the owner's lamp count drifting from the save -> FAIL");

        var nightIsland = new SkyIslandWindSample { Sampled = true, Night = true, EffectiveLights = lit };
        nightIsland.Gale = SkyIslandFieldcraftRules.WindLevel(SkyIslandFieldcraftRules.NightWind(true, lit), false, false, false);
        nightIsland.Level = nightIsland.Gale;
        Check(F3GameplayValidationRunner.JudgeLampsWind(data, anchor, fire, 0, lit, nightIsland, out metrics, out reason), "wind: night on an island reads back -> PASS: " + reason);
        var bridge = new SkyIslandWindSample { Sampled = true, Night = true, OnBridge = true, EffectiveLights = lit };
        bridge.Gale = SkyIslandFieldcraftRules.WindLevel(SkyIslandFieldcraftRules.NightWind(true, lit), true, false, false);
        bridge.Level = SkyIslandFieldcraftRules.CoreEased(bridge.Gale, true);
        Check(bridge.Gale == 2 && F3GameplayValidationRunner.JudgeLampsWind(data, anchor, fire, 0, lit, bridge, out metrics, out reason),
            "wind: a night gale on a bridge eased to a breeze by the windeater core -> PASS");
        var wrongGale = nightIsland;
        wrongGale.Gale = 0;
        wrongGale.Level = 0;
        Check(!F3GameplayValidationRunner.JudgeLampsWind(data, anchor, fire, 0, lit, wrongGale, out metrics, out reason) && reason.Contains("wind_gale_readback"),
            "wind: applied gale disagreeing with the rules -> FAIL");
        var wrongLevel = nightIsland;
        wrongLevel.Level = 0;
        Check(!F3GameplayValidationRunner.JudgeLampsWind(data, anchor, fire, 0, lit, wrongLevel, out metrics, out reason) && reason.Contains("wind_level_readback"),
            "wind: applied level below the gale without the core's easing -> FAIL");
        var wrongLights = nightIsland;
        wrongLights.EffectiveLights = lit + 3;
        wrongLights.Gale = SkyIslandFieldcraftRules.WindLevel(SkyIslandFieldcraftRules.NightWind(true, lit + 3), false, false, false);
        wrongLights.Level = wrongLights.Gale;
        Check(!F3GameplayValidationRunner.JudgeLampsWind(data, anchor, fire, 0, lit, wrongLights, out metrics, out reason) && reason.Contains("wind_effective_lights"),
            "wind: the lamp count fed into the night wind is not save count minus missing anchors -> FAIL");
    }

    // ---------------------------------------------------------------- 官方图鉴镜像
    private static void OfficialNotes()
    {
        string metrics, reason;
        var keys = new List<string>();
        var ids = new List<string>();
        foreach (string[] chapter in SkyIslandJournal.Chapters)
            foreach (string id in chapter) { ids.Add(id); keys.Add(SkyIslandNoteBridge.BuildNoteKey(id)); }
        keys.Add("SomeOfficialNote");
        SkyIslandStoryData data = SkyIslandStoryRules.CreateDefault();
        data.discoveredNotes = new[] { ids[0], ids[3] };
        var unlocked = new HashSet<string> { SkyIslandNoteBridge.BuildNoteKey(ids[0]), SkyIslandNoteBridge.BuildNoteKey(ids[3]) };
        Func<string, bool> isUnlocked = key => unlocked.Contains(key);
        Func<string, string> title = key => "登云码头";

        Check(F3GameplayValidationRunner.JudgeOfficialNotes(data, keys, isUnlocked, title, "island", out metrics, out reason)
            && metrics.Contains("listed=20/20") && metrics.Contains("recorded_in_save=2") && metrics.Contains("unlocked_in_official=2"),
            "notes: 20 entries listed once, unlock state mirrors the save -> PASS: " + reason);

        var missing = new List<string>(keys);
        missing.Remove(SkyIslandNoteBridge.BuildNoteKey(ids[5]));
        Check(!F3GameplayValidationRunner.JudgeOfficialNotes(data, missing, isUnlocked, title, "base", out metrics, out reason)
            && reason.Contains("not_in_notes_list:" + ids[5]), "notes: an entry only in the lookup dictionary but not in the notes list -> FAIL");
        var duplicated = new List<string>(keys) { SkyIslandNoteBridge.BuildNoteKey(ids[1]) };
        Check(!F3GameplayValidationRunner.JudgeOfficialNotes(data, duplicated, isUnlocked, title, "base", out metrics, out reason)
            && reason.Contains("duplicated"), "notes: an entry registered twice -> FAIL");
        unlocked.Add(SkyIslandNoteBridge.BuildNoteKey(ids[7]));
        Check(!F3GameplayValidationRunner.JudgeOfficialNotes(data, keys, isUnlocked, title, "base", out metrics, out reason)
            && reason.Contains(ids[7] + ":not_recorded_but_unlocked"), "notes: official unlocked without our save recording it -> FAIL");
        unlocked.Remove(SkyIslandNoteBridge.BuildNoteKey(ids[7]));
        unlocked.Remove(SkyIslandNoteBridge.BuildNoteKey(ids[3]));
        Check(!F3GameplayValidationRunner.JudgeOfficialNotes(data, keys, isUnlocked, title, "island", out metrics, out reason)
            && reason.Contains(ids[3] + ":recorded_but_locked"), "notes: recorded in our save but still locked in the official index -> FAIL");
        unlocked.Add(SkyIslandNoteBridge.BuildNoteKey(ids[3]));
        Check(!F3GameplayValidationRunner.JudgeOfficialNotes(data, keys, isUnlocked, key => "*Note_" + key + "_Title*", "island", out metrics, out reason)
            && reason.Contains("raw_or_empty_title"), "notes: a raw localization key as title -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeOfficialNotes(data, keys, isUnlocked, key => null, "island", out metrics, out reason),
            "notes: no title at all -> FAIL");
    }

    // ---------------------------------------------------------------- 纪念品与离岛残留
    private static void Keepsakes()
    {
        string metrics, reason;
        Check(F3GameplayValidationRunner.JudgeKeepsakes(1, 1, "ok", "ok", true, false, false, false, out metrics, out reason), "keepsakes: one of each on base, nothing left behind -> PASS");
        Check(F3GameplayValidationRunner.JudgeKeepsakes(0, 0, "ok", "ok", false, true, true, true, out metrics, out reason),
            "keepsakes: on the island the fieldcraft owners are supposed to exist");
        Check(!F3GameplayValidationRunner.JudgeKeepsakes(2, 1, "ok", "ok", false, false, false, false, out metrics, out reason) && reason.Contains("homecoming_badge=2"),
            "keepsakes: a second homecoming badge -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeKeepsakes(1, 2, "ok", "ok", false, false, false, false, out metrics, out reason) && reason.Contains("windeater_core=2"),
            "keepsakes: a second windeater core -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeKeepsakes(1, 1, "same_as_clone_source_500059", "ok", false, false, false, false, out metrics, out reason)
            && reason.Contains("badge_icon"), "keepsakes: the clone source's icon -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeKeepsakes(1, 1, "ok", "icon_missing", false, false, false, false, out metrics, out reason), "keepsakes: no icon -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeKeepsakes(1, 1, "ok", "ok", true, true, false, false, out metrics, out reason) && reason.Contains("fieldcraft_owner_left_on_base"),
            "keepsakes: the fieldcraft owner survived the trip home -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeKeepsakes(1, 1, "ok", "ok", true, false, false, true, out metrics, out reason) && reason.Contains("wind_lantern_light_left_on_base"),
            "keepsakes: the wind lantern followed the player home -> FAIL");
    }

    // ---------------------------------------------------------------- 采集点
    private static void GatherNodes()
    {
        string metrics, reason;
        var empty = new List<string>();
        Check(F3GameplayValidationRunner.JudgeGatherNodes(30, 30, 4, 1, empty, empty, out metrics, out reason), "gathering: all placed, built ones glow and time right -> PASS");
        Check(!F3GameplayValidationRunner.JudgeGatherNodes(30, 29, 4, 0, empty, empty, out metrics, out reason) && reason.Contains("placed=29/30"),
            "gathering: a node silently not placed -> FAIL");
        bool skipped = false;
        try { F3GameplayValidationRunner.JudgeGatherNodes(30, 30, 0, 0, empty, empty, out metrics, out reason); }
        catch (SkyIslandSkipCase skip) { skipped = skip.Message == "no_gather_node_built_yet" && skip.Metrics.Contains("built=0"); }
        Check(skipped, "gathering: nothing built yet -> SKIP (the glow and timing checks would be vacuous)");
        Check(!F3GameplayValidationRunner.JudgeGatherNodes(30, 30, 4, 0, new List<string> { "A_Driftwood" }, empty, out metrics, out reason)
            && reason.Contains("glow_disc_missing:A_Driftwood"), "gathering: a node without its ground glow -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeGatherNodes(30, 30, 4, 0, empty, new List<string> { "A_Driftwood=0.00/2.50" }, out metrics, out reason)
            && reason.Contains("interact_time_mismatch"), "gathering: interaction time that did not reach the official field (instant harvest) -> FAIL");
    }

    // ---------------------------------------------------------------- 信鸽
    private static void LetterPigeon()
    {
        string metrics, reason;
        SkyIslandStoryData fresh = SkyIslandStoryRules.CreateDefault();
        SkyIslandLetter next = SkyIslandLetters.NextFor(fresh);
        Check(next != null && next.Requires == SkyIslandStoryFlag.None, "pigeon: a new save's next letter is an ungated one");

        Check(F3GameplayValidationRunner.JudgeLetterPigeon(fresh, next.Id, true, true, true, true, out metrics, out reason),
            "pigeon: the pigeon carries NextFor and the latch is closed -> PASS: " + reason);
        Check(F3GameplayValidationRunner.JudgeLetterPigeon(fresh, null, true, false, false, true, out metrics, out reason),
            "pigeon: latch closed but no pigeon (no clearance this raid) is legal");
        Check(F3GameplayValidationRunner.JudgeLetterPigeon(fresh, null, false, false, false, true, out metrics, out reason),
            "pigeon: latch open waiting for the next tick is legal");
        Check(!F3GameplayValidationRunner.JudgeLetterPigeon(fresh, next.Id, false, true, true, true, out metrics, out reason)
            && reason.Contains("pigeon_present_without_latch"), "pigeon: a pigeon on the field with the latch open -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeLetterPigeon(fresh, next.Id, true, false, false, true, out metrics, out reason)
            && reason.Contains("letter_without_pigeon"), "pigeon: a letter still held after the pigeon left -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeLetterPigeon(fresh, next.Id, true, true, false, true, out metrics, out reason)
            && reason.Contains("pigeon_object_not_named_after_letter"), "pigeon: the scene object is not the one for this letter -> FAIL");

        SkyIslandLetter later = null;
        for (int i = 2; i <= 12 && later == null; i++)
        {
            SkyIslandLetter candidate = SkyIslandLetters.Find("Letter_" + i.ToString("00"));
            if (candidate != null && candidate.Requires == SkyIslandStoryFlag.None && candidate.Id != next.Id) later = candidate;
        }
        Check(later != null, "pigeon: found a second ungated letter for the out-of-order sample");
        Check(!F3GameplayValidationRunner.JudgeLetterPigeon(fresh, later.Id, true, true, true, true, out metrics, out reason)
            && reason.Contains("_but_next_is_" + next.Id), "pigeon: carrying an ungated letter out of order -> FAIL");

        SkyIslandStoryData collected = SkyIslandStoryRules.CreateDefault();
        collected.discoveredNotes = new[] { next.Id };
        Check(!F3GameplayValidationRunner.JudgeLetterPigeon(collected, next.Id, true, true, true, true, out metrics, out reason)
            && reason.Contains("carrying_collected_letter"), "pigeon: carrying a letter the save already collected -> FAIL");

        SkyIslandLetter gated = null;
        for (int i = 1; i <= 12 && gated == null; i++)
        {
            SkyIslandLetter candidate = SkyIslandLetters.Find("Letter_" + i.ToString("00"));
            if (candidate != null && candidate.Requires != SkyIslandStoryFlag.None) gated = candidate;
        }
        Check(gated != null, "pigeon: found a gated letter");
        SkyIslandStoryData progressed = SkyIslandStoryRules.CreateDefault();
        progressed.flags = (int)gated.Requires;
        Check(SkyIslandLetters.NextFor(progressed) != null && SkyIslandLetters.NextFor(progressed).Requires != SkyIslandStoryFlag.None,
            "pigeon: once the story unlocks a gated letter it moves to the front");
        Check(F3GameplayValidationRunner.JudgeLetterPigeon(progressed, next.Id, true, true, true, true, out metrics, out reason),
            "pigeon: an uncollected ungated letter placed before the story unlocked a gated one is still legal (not replaced)");
        SkyIslandStoryData locked = SkyIslandStoryRules.CreateDefault();
        Check(!F3GameplayValidationRunner.JudgeLetterPigeon(locked, gated.Id, true, true, true, true, out metrics, out reason)
            && reason.Contains("carrying_locked_letter"), "pigeon: carrying a letter whose prerequisites are not met -> FAIL");
        Check(!F3GameplayValidationRunner.JudgeLetterPigeon(fresh, "Letter_99", true, true, true, true, out metrics, out reason)
            && reason.Contains("unknown_letter"), "pigeon: an unknown letter id -> FAIL");
    }
}
