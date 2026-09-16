using System;
using System.Collections.Generic;
using BossRush;

/// <summary>
/// 天空岛头目 / 岛主的纯规则回归（R1 残星匠首、瞭台观星手；R2–R4 悬根猎首、截信人、穗镰、听雨人、蚋笛翁、镜中客、断风三游猎）。
///
/// 钉住的是 owner 拍板的口径与防换皮纪律，不是手感：
/// - 掉落：岛主每次必出一件（权重合计 100、不掉权重 0），头目有不掉权重；按均匀种子网格精确计频，不靠大数定律糊过去。
/// - 档案：挂在已有遭遇组的带队位置上，内容表档次与档案一致；一种招式一个控制器（断风游猎是同一家族的三个变体）；
///   夜限定只有蚋笛翁与镜中客，换阵营只有断风游猎；五栏非空、核心招式两两不同由守卫解析。
/// - 档次：按官方护甲公式折算的有效血量与伤害、反应倍率随档次严格递增（Elite &lt; Chief &lt; Champion &lt; Lord &lt; Storm）。
/// - 预警：每一个会伤人或减速的圈，逃圈速度 ≤ 5.5 m/s（与噬风同一条判据）；静听耳罩只会让预警更长。
/// - 招式几何（绊索、倒影换位、冲步、逃点、洞口、镜池）与套装效果（翻箱、割草、走桥、偷窃白名单）都是纯函数，这里逐条算。
/// - 星工两件套只让渡口工台配方的残铜片少耗一片，且不低于一片；配方原件不被改写。
/// </summary>
internal static class SkyIslandBossRulesRegression
{
    // 与 SkyIslandEnemyTiers 同口径（该文件依赖 Unity，不进这个夹具；数字由 SkyIslandContentExpansionGuard 钉住）。
    private const double EliteHealth = 2.6, ChampionHealth = 4.5, StormHealth = 13.0;
    private const double EliteDamage = 1.35, ChampionDamage = 1.55, StormDamage = 1.8;
    private const double EliteReaction = 1.3, ChampionReaction = 1.45, StormReaction = 1.7;

    private sealed class Expected
    {
        internal string Encounter;
        internal SkyIslandBossKind Kind;
        internal SkyIslandEnemyTier Tier;
        internal int Variant;
        internal bool Night, Rival;
    }

    private static readonly Expected[] roster =
    {
        Row("G", SkyIslandBossKind.Foreman, SkyIslandEnemyTier.Lord, 0, false, false),
        Row("S4", SkyIslandBossKind.Stargazer, SkyIslandEnemyTier.Chief, 0, false, false),
        Row("D", SkyIslandBossKind.RootHunter, SkyIslandEnemyTier.Lord, 0, false, false),
        Row("S2", SkyIslandBossKind.Waylayer, SkyIslandEnemyTier.Chief, 0, false, false),
        Row("C", SkyIslandBossKind.Sickle, SkyIslandEnemyTier.Lord, 0, false, false),
        Row("S3", SkyIslandBossKind.Listener, SkyIslandEnemyTier.Chief, 0, false, false),
        Row("S1", SkyIslandBossKind.Piper, SkyIslandEnemyTier.Chief, 0, true, false),
        Row("F", SkyIslandBossKind.Mirror, SkyIslandEnemyTier.Chief, 0, true, false),
        Row("K1_Relay", SkyIslandBossKind.Windhunter, SkyIslandEnemyTier.Chief, SkyIslandBossRules.WindhunterChaser, false, true),
        Row("K2_Relay", SkyIslandBossKind.Windhunter, SkyIslandEnemyTier.Chief, SkyIslandBossRules.WindhunterStalker, false, true),
        Row("K3_Relay", SkyIslandBossKind.Windhunter, SkyIslandEnemyTier.Chief, SkyIslandBossRules.WindhunterWarden, false, true),
    };

    internal static void Run(Action<bool, string> check)
    {
        Profiles(check);
        Drops(check);
        Tiers(check);
        Telegraphs(check);
        Geometry(check);
        Gear(check);
        Crafting(check);
        Sets(check);
        Residents(check);
    }

    private static void Profiles(Action<bool, string> check)
    {
        SkyIslandBossProfile[] all = SkyIslandBossRules.Profiles;
        check(all.Length == roster.Length, "boss: the roster has the R1 pair plus the seven R2–R4 bosses (" + all.Length + ")");
        foreach (Expected row in roster)
        {
            SkyIslandBossProfile profile = SkyIslandBossRules.Find(row.Encounter, 0);
            check(profile != null && profile.Kind == row.Kind && profile.Tier == row.Tier && profile.Variant == row.Variant
                && profile.NightOnly == row.Night && profile.RivalFaction == row.Rival,
                "boss: " + row.Encounter + " group lead is " + row.Kind + " (" + row.Tier + ", variant " + row.Variant + ")");
            check(SkyIslandBossRules.LeadIsNightOnly(row.Encounter) == row.Night && SkyIslandBossRules.IsRivalFaction(row.Encounter) == row.Rival,
                "boss: night-only and rival-faction lookups agree with the profile for " + row.Encounter);
        }
        check(SkyIslandBossRules.Find("G", 1) == null && SkyIslandBossRules.Find("G_02", 0) == null && SkyIslandBossRules.Find(null, 0) == null
            && SkyIslandBossRules.Find("E_03", 0) == null && !SkyIslandBossRules.LeadIsNightOnly("G") && !SkyIslandBossRules.IsRivalFaction(null),
            "boss: only the lead slot of the named groups gets a profile; elite-led groups stay ordinary");

        SkyIslandContentData content = SkyIslandContent.CreateFallback();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var verbs = new HashSet<string>(StringComparer.Ordinal);
        var kinds = new Dictionary<SkyIslandBossKind, int>();
        foreach (SkyIslandBossProfile profile in all)
        {
            SkyIslandEncounterDefinition group = Array.Find(content.Encounters, e => e.Id == profile.EncounterId);
            check(group != null && !group.Manual && group.TierFor(profile.Index) == profile.Tier,
                "boss: content table tier matches the profile for " + profile.Id);
            check(ids.Add(profile.Id) && keys.Add(profile.NameKey) && profile.NameKey.StartsWith("BossRush_SkyIsland_", StringComparison.Ordinal),
                "boss: unique id and BossRush_SkyIsland_ name key for " + profile.Id);
            check(SkyIslandBossRules.IsBossNote(profile.NoteId) && SkyIslandBossRules.FindByNote(profile.NoteId) == profile
                && SkyIslandBossRules.FindById(profile.Id) == profile && SkyIslandBossRules.FindById(profile.Id.ToLowerInvariant()) == profile,
                "boss: first-kill note and id lookups are registered for " + profile.Id);
            // 五栏设计说明是档案表上方的注释，由 SkyIslandBossEcologyGuard 解析核对（非空、核心招式两两不同）；这里钉「一种招式一个控制器」：
            // 同一个控制器只许由同一家族的不同变体共用（断风游猎），变体号必须两两不同且非零。
            check(verbs.Add(profile.Kind + "#" + profile.Variant), "boss: one controller kind per profile (no reskins) — " + profile.Id);
            int seen;
            kinds.TryGetValue(profile.Kind, out seen);
            kinds[profile.Kind] = seen + 1;
            bool wasChinese = L10n.IsChinese;
            L10n.IsChinese = false;
            string english = SkyIslandBossRules.Name(profile);
            L10n.IsChinese = true;
            string chinese = SkyIslandBossRules.Name(profile);
            L10n.IsChinese = wasChinese;
            check(!string.IsNullOrEmpty(chinese) && !string.IsNullOrEmpty(english) && chinese != english
                && chinese != SkyIslandBossRules.NameCn((SkyIslandBossKind)99)
                && english == SkyIslandBossRules.NameEn(profile.Kind) + SkyIslandBossRules.VariantEn(profile.Variant),
                "boss: bilingual name resolves in the current language for " + profile.Id);
            check(profile.FaceId == "skyboss_" + profile.Id.ToLowerInvariant().Replace("windhunter", "windhunter_") && BossFaceBlueprintOk(profile.FaceId),
                "boss: signature face blueprint (json face, never spawned as an NPC) for " + profile.Id);
        }
        foreach (SkyIslandBossProfile profile in all)
            check(kinds[profile.Kind] == 1 ? profile.Variant == 0 : profile.Variant > 0,
                "boss: a shared controller is only for numbered variants of one family — " + profile.Id);
        check(!SkyIslandBossRules.IsBossNote("Keepsake_Core") && !SkyIslandBossRules.IsBossNote(null) && SkyIslandBossRules.FindById("nobody") == null,
            "boss: unrelated notes and ids are not bosses");

        // 严格绑定的正式内容表（Assets/Data/SkyIsland/World.json）同样认新档次名，且与内置表一致。
        string world = System.IO.File.ReadAllText(SkyIslandContent.RelativePath);
        SkyIslandContentData parsed;
        string error;
        bool ok = SkyIslandContent.TryParse(world, out parsed, out error) && parsed.Source == "Json";
        check(ok, "boss: the formal world table parses (" + error + ")");
        if (ok)
            foreach (Expected row in roster)
                check(Array.Find(parsed.Encounters, e => e.Id == row.Encounter).Lead == row.Tier,
                    "boss: the formal world table names the " + row.Tier + " of " + row.Encounter);
        SkyIslandEnemyTier tier;
        check(SkyIslandContent.TryParseTier("Chief", out tier) && tier == SkyIslandEnemyTier.Chief
            && SkyIslandContent.TryParseTier("Lord", out tier) && tier == SkyIslandEnemyTier.Lord
            && !SkyIslandContent.TryParseTier("4", out tier), "boss: tiers parse from stable names, never ordinals");
        check(!SkyIslandContent.TryParse(world.Replace("\"lead\": \"Lord\"", "\"lead\": \"Elite\""), out parsed, out error),
            "boss: demoting a lord in JSON breaks the strict binding and falls back");
    }

    private static void Drops(Action<bool, string> check)
    {
        const int samples = 10000;
        foreach (SkyIslandBossProfile profile in SkyIslandBossRules.Profiles)
        {
            int[] counts = new int[profile.Gear.Length];
            int none = 0, total = profile.NoDropWeight;
            foreach (SkyIslandBossGearPiece piece in profile.Gear) total += piece.Weight;
            for (int i = 0; i < samples; i++)
            {
                int pick = SkyIslandBossRules.RollDrop(profile, (i + 0.5) / samples);
                if (pick < 0) none++;
                else counts[pick]++;
            }
            check(total == 100, "boss drop: weights of " + profile.Id + " add up to 100");
            if (profile.Tier == SkyIslandEnemyTier.Lord)
                check(profile.NoDropWeight == 0 && none == 0, "boss drop: the island lord " + profile.Id + " always leaves exactly one piece");
            else
                check(profile.NoDropWeight > 0 && none == profile.NoDropWeight * samples / total,
                    "boss drop: the chief " + profile.Id + " sometimes leaves nothing, exactly at its no-drop weight");
            for (int i = 0; i < profile.Gear.Length; i++)
                check(counts[i] == profile.Gear[i].Weight * samples / total
                    && Math.Abs(SkyIslandBossRules.DropChance(profile, i) - profile.Gear[i].Weight / (double)total) < 1e-9,
                    "boss drop: " + profile.Id + " piece " + profile.Gear[i].TypeId + " frequency is exactly its weight");
        }

        SkyIslandBossProfile foreman = SkyIslandBossRules.Find("G", 0);
        SkyIslandBossProfile stargazer = SkyIslandBossRules.Find("S4", 0);
        check(SkyIslandBossRules.RollDrop(foreman, -1.0) == 0 && SkyIslandBossRules.RollDrop(foreman, double.NaN) == 0
            && SkyIslandBossRules.RollDrop(foreman, 1.0) == foreman.Gear.Length - 1,
            "boss drop: out-of-range rolls clamp into the table");
        check(SkyIslandBossRules.RollDrop(stargazer, 0.999) == -1, "boss drop: the chief's no-drop band sits after its gear");
        check(SkyIslandBossRules.RollDrop(null, 0.5) == -1 && SkyIslandBossRules.DropChance(foreman, 9) == 0.0,
            "boss drop: missing profile or index never drops");
        check(SkyIslandBossRules.RollDrop(foreman, 0.42) == SkyIslandBossRules.RollDrop(foreman, 0.42), "boss drop: same roll, same piece");
        check(SkyIslandBossRules.RollDrop(foreman, 0.5) == 1, "boss drop: the drill's fixed roll 0.5 lands on a lord's second piece");
    }

    private static void Tiers(Action<bool, string> check)
    {
        foreach (SkyIslandBossProfile profile in SkyIslandBossRules.Profiles)
        {
            double effective = SkyIslandBossRules.EffectiveHealth(profile);
            if (profile.Tier == SkyIslandEnemyTier.Chief)
            {
                check(EliteHealth < effective && effective < ChampionHealth,
                    "boss tier: chief " + profile.Id + " armour-adjusted health sits between Elite and Champion (" + effective + ")");
                check(EliteDamage < profile.Damage && profile.Damage < ChampionDamage && EliteReaction < profile.Reaction && profile.Reaction < ChampionReaction,
                    "boss tier: chief " + profile.Id + " damage and reaction sit between Elite and Champion");
            }
            else
            {
                check(profile.Tier == SkyIslandEnemyTier.Lord && ChampionHealth < effective && effective < StormHealth,
                    "boss tier: lord " + profile.Id + " armour-adjusted health sits between Champion and Storm (" + effective + ")");
                check(ChampionDamage < profile.Damage && profile.Damage < StormDamage && profile.Damage <= 3f
                    && ChampionReaction < profile.Reaction && profile.Reaction < StormReaction,
                    "boss tier: lord " + profile.Id + " damage and reaction sit between Champion and Storm, capped");
            }
            check(profile.Scale >= 1f && profile.Scale < 1.9f, "boss tier: model scale of " + profile.Id + " stays below the Windeater's");
        }
        check(Math.Abs(SkyIslandBossRules.ArmorFactor(3.0, 2.0) - 2.0 / 3.0) < 1e-9 && SkyIslandBossRules.ArmorFactor(0.0, 2.0) == 1.0
            && SkyIslandBossRules.ArmorFactor(2.0, 5.0) == 1.0, "boss tier: armour factor mirrors the official Health.Hurt formula");
    }

    private static void Telegraphs(Action<bool, string> check)
    {
        string[] names = SkyIslandBossRules.TelegraphRingNames;
        check(names.Length == 9 && new HashSet<string>(names).Count == names.Length
            && float.IsPositiveInfinity(SkyIslandBossRules.TelegraphRingEscapeSpeed(names.Length)),
            "boss telegraph: every damaging or slowing ring is listed once");
        for (int i = 0; i < names.Length; i++)
        {
            float speed = SkyIslandBossRules.TelegraphRingEscapeSpeed(i);
            check(speed > 0f && speed <= SkyIslandBossRules.MaxEscapeSpeed, "boss telegraph: the " + names[i] + " ring can be walked out of (" + speed + " m/s)");
        }
        check(float.IsPositiveInfinity(SkyIslandBossRules.EscapeSpeed(2f, 0f)), "boss telegraph: an instant ring is flagged as unescapable");
        check(SkyIslandBossRules.TelegraphSeconds(1f, false) == 1f && SkyIslandBossRules.TelegraphSeconds(1f, true) > 1f,
            "boss telegraph: the rainhush earmuffs only ever lengthen a warning");
        check(SkyIslandBossRules.StarfireSpread > SkyIslandBossRules.StarfireRadius * 1.3f,
            "boss telegraph: side starfire rings leave a walkable gap next to the centre ring");
        check(SkyIslandBossRules.AmbushTelegraphMaskBroken > SkyIslandBossRules.AmbushTelegraph
            && SkyIslandBossRules.SweepTelegraphBroken > SkyIslandBossRules.SweepTelegraph
            && SkyIslandBossRules.LungeTelegraphFor(SkyIslandBossRules.WindhunterChaser, true) == 2f * SkyIslandBossRules.LungeTelegraphFor(SkyIslandBossRules.WindhunterChaser, false)
            && SkyIslandBossRules.LungeTelegraphFor(SkyIslandBossRules.WindhunterWarden, false) > SkyIslandBossRules.LungeTelegraphFor(SkyIslandBossRules.WindhunterChaser, false)
            && SkyIslandBossRules.LungeTelegraphFor(SkyIslandBossRules.WindhunterStalker, false) == SkyIslandBossRules.LungeTelegraph,
            "boss gear link: breaking the mask, raincoat or Galebreaker piece lengthens the warning; the Warden reads slowest");

        float[] foreman = SkyIslandBossRules.ForemanPhaseThresholds;
        check(SkyIslandBossRules.PhaseFor(1f, foreman) == 0 && SkyIslandBossRules.PhaseFor(0.70f, foreman) == 1
            && SkyIslandBossRules.PhaseFor(0.41f, foreman) == 1 && SkyIslandBossRules.PhaseFor(0.40f, foreman) == 2
            && SkyIslandBossRules.PhaseFor(0.15f, foreman) == 3 && SkyIslandBossRules.PhaseFor(0f, foreman) == 3,
            "boss phase: three pylon phases at 70% / 40% / 15%");
        check(SkyIslandBossRules.PhaseFor(0.76f, SkyIslandBossRules.RootHunterAmbushThresholds) == 0
            && SkyIslandBossRules.PhaseFor(0.75f, SkyIslandBossRules.RootHunterAmbushThresholds) == 1
            && SkyIslandBossRules.PhaseFor(0.45f, SkyIslandBossRules.RootHunterAmbushThresholds) == 2,
            "boss phase: two root-hollow ambushes at 75% / 45%");
        check(SkyIslandBossRules.PhaseFor(0.80f, SkyIslandBossRules.SicklePhaseThresholds) == 1
            && SkyIslandBossRules.PhaseFor(0.50f, SkyIslandBossRules.SicklePhaseThresholds) == 2
            && SkyIslandBossRules.PhaseFor(0.25f, SkyIslandBossRules.SicklePhaseThresholds) == 3,
            "boss phase: three sluice openings at 80% / 50% / 25%");
        foreach (float[] thresholds in new[] { foreman, SkyIslandBossRules.RootHunterAmbushThresholds, SkyIslandBossRules.SicklePhaseThresholds })
        {
            int previous = 0;
            bool monotonic = true;
            for (int i = 100; i >= 0; i--)
            {
                int phase = SkyIslandBossRules.PhaseFor(i / 100f, thresholds);
                if (phase < previous) monotonic = false;
                previous = phase;
            }
            check(monotonic, "boss phase: phases only move forward as health drops");
        }
        check(SkyIslandBossRules.PylonLifetime > 0f && SkyIslandBossRules.ShieldArmorBroken < SkyIslandBossRules.ShieldArmor
            && SkyIslandBossRules.CastsBeforeOverheat >= 2 && SkyIslandBossRules.OverheatDamageTaken > 0f,
            "boss phase: pylons expire, a broken harness halves the shield, overheat opens a damage window");
        check(SkyIslandBossRules.MarkCloseRange < SkyIslandBossRules.MarkRange && SkyIslandBossRules.FlareShots >= 1,
            "boss mark: rushing the stargazer inside close range stops the marks");
        check(SkyIslandBossRules.SnareStakeHealth > 0f && SkyIslandBossRules.SnareLifetime > SkyIslandBossRules.SnareArmSeconds
            && SkyIslandBossRules.SnareSlowFor(false) < SkyIslandBossRules.SnareSlowFor(true) && SkyIslandBossRules.SnareSlowFor(true) < 0f
            && Math.Abs(SkyIslandBossRules.SnareSlowFor(true) * 2f - SkyIslandBossRules.SnareSlowFor(false)) < 1e-6f && SkyIslandBossRules.SnareSlowFor(false) > -1f,
            "boss snare: stakes can be cut, the wire arms before it bites, a shot-through cuirass halves the slow");
        check(SkyIslandBossRules.SnatchReach > SkyIslandBossRules.SnatchRange && SkyIslandBossRules.SnatchMax >= 1
            && SkyIslandBossRules.WaylayerDropBelow > 0f && SkyIslandBossRules.WaylayerDropBelow < 1f && SkyIslandBossRules.FleeSpeedBonus > 0f,
            "boss snatch: stepping back out of reach escapes, snatches are capped, low health drops the loot");
        check(SkyIslandBossRules.MudPatchesHatBroken < SkyIslandBossRules.MudPatches && SkyIslandBossRules.MudSlow < 0f && SkyIslandBossRules.MudSlow > -1f
            && SkyIslandBossRules.MudSeconds > SkyIslandBossRules.MudTelegraph && SkyIslandBossRules.MudSpread > SkyIslandBossRules.MudRadius,
            "boss sluice: a shot-through hat floods fewer patches; mud slows but never roots; side patches leave a dry gap");
        check(SkyIslandBossRules.ShotsPerRockfallFor(true) == 2 * SkyIslandBossRules.ShotsPerRockfallFor(false) && SkyIslandBossRules.ShotsPerRockfallFor(false) > 1
            && SkyIslandBossRules.RockfallCooldown > 0f, "boss rockfall: your own earmuffs halve how often gunfire brings rocks down");
        check(SkyIslandBossRules.FluteInterruptDamage > 0f && SkyIslandBossRules.FluteGnatsMin >= 1 && SkyIslandBossRules.FluteGnatsMax >= SkyIslandBossRules.FluteGnatsMin
            && SkyIslandBossRules.FluteLureSeconds > SkyIslandBossRules.FluteChannel, "boss flute: the tune can be broken off and lures for longer than it takes to play");
        check(SkyIslandBossRules.SwapMinDistance < SkyIslandBossRules.SwapMaxDistance && SkyIslandBossRules.DecoySeconds > 0f && SkyIslandBossRules.DecoyStagger > 0f,
            "boss mirror: flips land within a bounded band; a shattered reflection staggers it");
        check(SkyIslandBossRules.SwapMinDistance > SkyIslandBossRules.SwapRadius && SkyIslandBossRules.SwapMinDistance < 2f * SkyIslandBossRules.SwapRadius + 1f,
            "boss mirror: the ring never lights under your feet, but backing away while shooting walks into it");
        check(SkyIslandBossRules.LungeMinRange < SkyIslandBossRules.LungeMaxRange && SkyIslandBossRules.LungeStandOff > SkyIslandBossRules.LungeRadius * 0.5f
            && SkyIslandBossRules.StalkerPackBreakBelow > 0f && SkyIslandBossRules.StalkerPackBreakBelow < 1f,
            "boss lunge: a lunge needs room to start and stops short of you");
    }

    private static string duckNpcs;

    /// <summary>头目的专属脸蓝图在 DuckNpcs.json 里：json 脸（跨版本长相不漂）、scenes 为空（不会被当成 NPC 刷出来）。</summary>
    private static bool BossFaceBlueprintOk(string id)
    {
        if (duckNpcs == null) duckNpcs = System.IO.File.ReadAllText("Assets/Data/DuckNpcs.json");
        int at = duckNpcs.IndexOf(@"""id"": """ + id + @"""", StringComparison.Ordinal);
        if (at < 0) return false;
        int next = duckNpcs.IndexOf(@"""id"": """, at + 1, StringComparison.Ordinal);
        string entry = next < 0 ? duckNpcs.Substring(at) : duckNpcs.Substring(at, next - at);
        return entry.Contains(@"""faceMode"": ""json""") && entry.Contains(@"""faceJson"": ""{") && entry.Contains(@"""scenes"": []");
    }

    private static void Geometry(Action<bool, string> check)
    {
        check(SkyIslandBossRules.SnareTripped(-4f, 0f, 4f, 0f, 0f, -3f, 0f, 3f), "boss snare: stepping across the wire trips it");
        check(!SkyIslandBossRules.SnareTripped(-4f, 0f, 4f, 0f, 0f, -3f, 0f, -2f), "boss snare: stepping toward the wire without reaching it does not");
        check(SkyIslandBossRules.SnareTripped(-4f, 0f, 4f, 0f, 1f, 3f, 1f, 0.5f), "boss snare: stopping on the wire trips it");
        check(!SkyIslandBossRules.SnareTripped(-4f, 0f, 4f, 0f, 6f, -3f, 6f, 3f), "boss snare: walking round the end of the wire is safe");
        check(Math.Abs(SkyIslandBossRules.DistanceToSegment(3f, 4f, 0f, 0f, 0f, 0f) - 5f) < 1e-4f
            && Math.Abs(SkyIslandBossRules.DistanceToSegment(0f, 2f, -4f, 0f, 4f, 0f) - 2f) < 1e-4f, "boss snare: point-to-wire distance");

        float x, z;
        check(SkyIslandBossRules.MirrorAcross(0f, 0f, 0f, -10f, out x, out z) && Math.Abs(x) < 1e-4f && Math.Abs(z - 10f) < 1e-4f,
            "boss mirror: it flips to the same distance behind you");
        check(SkyIslandBossRules.MirrorAcross(0f, 0f, 0f, -2f, out x, out z) && Math.Abs(z - SkyIslandBossRules.SwapMinDistance) < 1e-4f
            && SkyIslandBossRules.MirrorAcross(0f, 0f, 0f, -40f, out x, out z) && Math.Abs(z - SkyIslandBossRules.SwapMaxDistance) < 1e-4f,
            "boss mirror: the flip distance is clamped to its band");
        check(!SkyIslandBossRules.MirrorAcross(1f, 1f, 1f, 1f, out x, out z), "boss mirror: standing on top of it gives no direction to flip");
        check(SkyIslandBossRules.NearMirrorPool(SkyIslandBossRules.PoolCenterX, SkyIslandBossRules.PoolCenterZ)
            && !SkyIslandBossRules.NearMirrorPool(SkyIslandBossRules.PoolCenterX + SkyIslandBossRules.PoolRange + 1f, SkyIslandBossRules.PoolCenterZ),
            "boss mirror: it only flips near the mirror pool");

        SkyIslandBossRules.LungeLanding(0f, 0f, 0f, 10f, false, out x, out z);
        check(Math.Abs(x) < 1e-4f && Math.Abs(z - (10f - SkyIslandBossRules.LungeStandOff)) < 1e-4f, "boss lunge: the first step stops in front of you");
        SkyIslandBossRules.LungeLanding(0f, 0f, 0f, 10f, true, out x, out z);
        check(Math.Abs(z - (10f + SkyIslandBossRules.LungeStandOff)) < 1e-4f, "boss lunge: the Chaser's second step lands behind you");
        SkyIslandBossRules.LungeLanding(2f, 3f, 2f, 3f, false, out x, out z);
        check(x == 2f && z == 3f, "boss lunge: no direction, no lunge");

        check(SkyIslandBossRules.ShouldDisengage(false, SkyIslandBossRules.CloseQuartersSeconds, true)
            && !SkyIslandBossRules.ShouldDisengage(false, SkyIslandBossRules.CloseQuartersSeconds - 0.1f, true),
            "boss lunge: a Galebreaker only backs off after it has been in your face for the full count");
        check(!SkyIslandBossRules.ShouldDisengage(true, 99f, true) && !SkyIslandBossRules.ShouldDisengage(false, 99f, false),
            "boss lunge: it never backs off mid-lunge or before the cooldown is up");
        check(SkyIslandBossRules.DisengageRange > SkyIslandBossRules.LungeMinRange
            && SkyIslandBossRules.DisengageRange < SkyIslandBossRules.LungeMaxRange,
            "boss lunge: backing off lands inside the lunge band, so the next line comes straight away");

        check(SkyIslandBossRules.FartherPoint(0f, 0f, 10f, 0f, 0f, 20f) == 1 && SkyIslandBossRules.FartherPoint(0f, 0f, 10f, 0f, 0f, 10f) == 0,
            "boss snatch: it flees to the escape point farther from you");
        check(SkyIslandBossRules.InCave(SkyIslandBossRules.CaveCenterX, SkyIslandBossRules.CaveCenterZ)
            && !SkyIslandBossRules.InCave(SkyIslandBossRules.CaveCenterX, SkyIslandBossRules.CaveCenterZ + SkyIslandBossRules.CaveRange + 1f),
            "boss rockfall: only gunfire in the cave mouth counts");

        foreach (int id in SkyIslandItemRules.StealableTypeIds)
            check(SkyIslandBossRules.StealScore(id) == SkyIslandItemRules.ValueOf(id) && SkyIslandBossRules.StealScore(id) > 0,
                "boss snatch: island supply " + id + " can be snatched, most valuable first");
        check(SkyIslandBossRules.StealScore(BossRushItemIds.SkyIslandCloudmossFiber) < 0 && SkyIslandBossRules.StealScore(BossRushItemIds.SkyIslandHomecomingBadge) < 0
            && SkyIslandBossRules.StealScore(BossRushItemIds.SkyIslandStarbrassVisorHelm) < 0 && SkyIslandBossRules.StealScore(12345) < 0,
            "boss snatch: materials, keepsakes, boss gear and official items are never snatched");
        check(SkyIslandBossRules.StealScore(BossRushItemIds.SkyIslandQinglanCharm) > SkyIslandBossRules.StealScore(BossRushItemIds.SkyIslandHomecomingBento),
            "boss snatch: a charm goes before a bento");
    }

    private static void Gear(Action<bool, string> check)
    {
        SkyIslandBossGearSpec[] specs = SkyIslandBossRules.GearSpecs;
        int[] ids = SkyIslandBossRules.AllGearTypeIds;
        check(specs.Length == 17 && ids.Length == 17, "boss gear: four R1 pieces and thirteen R2–R4 pieces");
        for (int i = 1; i < ids.Length; i++) check(ids[i] > ids[i - 1], "boss gear: AllGearTypeIds stays in increasing TypeID order at " + ids[i]);
        var seen = new HashSet<int>();
        foreach (SkyIslandBossGearSpec spec in specs)
        {
            check(seen.Add(spec.TypeId) && Array.IndexOf(ids, spec.TypeId) >= 0, "boss gear: unique registered TypeID " + spec.TypeId);
            string suffix = spec.Slot == "Helmat" ? "_Helmet" : spec.Slot == "Armor" ? "_Armor" : spec.Slot == "Backpack" ? "_Backpack"
                : spec.Slot == "FaceMask" ? "_FaceMask" : spec.Slot == "Headset" ? "_Headset" : null;
            check(suffix != null && spec.ModelBaseName.EndsWith(suffix, StringComparison.Ordinal),
                "boss gear: bundle base name follows the EquipmentFactory slot keyword for " + spec.TypeId);
            string stat = spec.Slot == "Armor" ? "BodyArmor" : spec.Slot == "Backpack" ? "InventoryCapacity" : spec.Slot == "Headset" ? "HearingAbility" : "HeadArmor";
            check(spec.StatKey == stat, "boss gear: the official stat matches the slot for " + spec.TypeId + " (" + spec.StatKey + ")");
            check(spec.StatValue > 0f && spec.Quality >= 1 && spec.Quality <= 7, "boss gear: positive stat and a normal quality band for " + spec.TypeId);
            check((spec.Slot == "Backpack") == (spec.Durability <= 0f), "boss gear: everything but a pack wears down — " + spec.TypeId);
            check(spec.IconName.StartsWith("sky_island_", StringComparison.Ordinal) && spec.LocKey.StartsWith("BossRush_SkyIsland_", StringComparison.Ordinal),
                "boss gear: icon and localization key naming for " + spec.TypeId);
            // 价值与中英名只在 SkyIslandItemRules 一处；但它不是岛上的克隆物品，不进 AllTypeIds（那张表是 500068 起连续的岛上物品族）。
            check(SkyIslandItemRules.ValueOf(spec.TypeId) > 0 && Array.IndexOf(SkyIslandItemRules.AllTypeIds, spec.TypeId) < 0
                && SkyIslandItemRules.NameCn(spec.TypeId) != "天空岛物品" && SkyIslandItemRules.NameEn(spec.TypeId) != "Sky Islands item",
                "boss gear: value and bilingual name live in SkyIslandItemRules, outside the island clone family, for " + spec.TypeId);
            int wearers = 0;
            foreach (SkyIslandBossProfile profile in SkyIslandBossRules.Profiles)
                foreach (SkyIslandBossGearPiece piece in profile.Gear) if (piece.TypeId == spec.TypeId) wearers++;
            check(wearers == 1, "boss gear: exactly one boss wears " + spec.TypeId + " (there is a source, and only one)");
        }
        foreach (SkyIslandBossProfile profile in SkyIslandBossRules.Profiles)
        {
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (SkyIslandBossGearPiece piece in profile.Gear)
            {
                SkyIslandBossGearSpec spec = SkyIslandBossRules.GearSpec(piece.TypeId);
                check(spec != null && spec.Slot == piece.Slot && piece.Weight > 0 && slots.Add(piece.Slot),
                    "boss gear: " + profile.Id + " wears " + piece.TypeId + " in its own slot");
            }
        }
        check(SameSet(SkyIslandBossRules.RootweaveSet, SkyIslandBossRules.Find("D", 0)) && SameSet(SkyIslandBossRules.SickleSet, SkyIslandBossRules.Find("C", 0)),
            "boss gear: the rootweave and straw-cloak sets are exactly what their lords wear");
        var windbreak = new HashSet<int>();
        foreach (string encounter in new[] { "K1_Relay", "K2_Relay", "K3_Relay" })
        {
            SkyIslandBossProfile ranger = SkyIslandBossRules.Find(encounter, 0);
            check(ranger.Gear.Length == 1 && Array.IndexOf(SkyIslandBossRules.WindbreakSet, ranger.Gear[0].TypeId) >= 0 && windbreak.Add(ranger.Gear[0].TypeId),
                "boss gear: each Galebreaker Ranger wears a different piece of the set — " + encounter);
        }
    }

    private static bool SameSet(int[] set, SkyIslandBossProfile profile)
    {
        if (set == null || profile == null || set.Length != profile.Gear.Length) return false;
        foreach (SkyIslandBossGearPiece piece in profile.Gear) if (Array.IndexOf(set, piece.TypeId) < 0) return false;
        return true;
    }

    private static void Crafting(Action<bool, string> check)
    {
        int helm = BossRushItemIds.SkyIslandStarbrassVisorHelm, harness = BossRushItemIds.SkyIslandStarfurnaceHarness,
            pack = BossRushItemIds.SkyIslandStarfurnacePack, lens = BossRushItemIds.SkyIslandStargazerLensHelm;
        check(SkyIslandBossRules.StarworksPiecesWorn(helm, harness, pack) == 3 && SkyIslandBossRules.StarworksPiecesWorn(lens, harness, 0) == 1
            && SkyIslandBossRules.StarworksPiecesWorn(0, 0, 0) == 0, "boss set: only the Foreman's three pieces count as Starworks");
        check(SkyIslandBossRules.BrassScrapCost(3, 2) == 2 && SkyIslandBossRules.BrassScrapCost(4, 3) == 3
            && SkyIslandBossRules.BrassScrapCost(3, 1) == 3 && SkyIslandBossRules.BrassScrapCost(1, 3) == 1
            && SkyIslandBossRules.BrassScrapCost(0, 3) == 0, "boss set: two pieces save exactly one brass scrap, never below one");

        // 合成台接线：同一个配方按穿着件数给出「这位穿戴者的配方」，Craft 与合成面板共用它。
        SkyIslandRecipe charm = Array.Find(SkyIslandFieldcraftRules.Recipes, r => r.OutputTypeId == BossRushItemIds.SkyIslandQinglanCharm);
        check(charm != null && charm.Station == SkyIslandCraftStation.Dock && BrassIn(charm) == 3, "boss set: the charm recipe needs three brass scrap at the dock");
        SkyIslandRecipe discounted = SkyIslandFieldcraftRules.ForWearer(charm, 2);
        check(discounted != null && discounted != charm && discounted.Id == charm.Id && discounted.OutputTypeId == charm.OutputTypeId
            && BrassIn(discounted) == 2 && BrassIn(charm) == 3, "boss set: two Starworks pieces take one brass scrap off a copy, never the shared recipe");
        check(SkyIslandFieldcraftRules.ForWearer(charm, 1) == charm && SkyIslandFieldcraftRules.ForWearer(null, 3) == null,
            "boss set: one piece or no recipe changes nothing");
        foreach (SkyIslandRecipe recipe in SkyIslandFieldcraftRules.Recipes)
        {
            if (recipe.Station == SkyIslandCraftStation.Dock && BrassIn(recipe) > 1) continue;
            check(SkyIslandFieldcraftRules.ForWearer(recipe, 3) == recipe, "boss set: recipes without spare brass at the dock stay untouched — " + recipe.Id);
        }
    }

    private static void Sets(Action<bool, string> check)
    {
        int mask = BossRushItemIds.SkyIslandRootweaveMask, cuirass = BossRushItemIds.SkyIslandVinewovenCuirass, quiver = BossRushItemIds.SkyIslandHangrootQuiver;
        int hat = BossRushItemIds.SkyIslandGreenearStrawHat, raincoat = BossRushItemIds.SkyIslandStrawRaincoat;
        check(SkyIslandBossRules.SetPiecesWorn(SkyIslandBossRules.RootweaveSet, hat, cuirass, quiver, mask, 0) == 3
            && SkyIslandBossRules.SetPiecesWorn(SkyIslandBossRules.SickleSet, hat, cuirass, quiver, mask, 0) == 1
            && SkyIslandBossRules.SetPiecesWorn(SkyIslandBossRules.SickleSet, hat, raincoat, 0, 0, 0) == 2
            && SkyIslandBossRules.SetPiecesWorn(null, hat, raincoat, 0, 0, 0) == 0 && SkyIslandBossRules.SetPiecesWorn(SkyIslandBossRules.WindbreakSet, 0, 0, 0, 0, 0) == 0,
            "boss set: pieces are counted across the five slots, per set");
        check(SkyIslandBossRules.HunterIslandExtraRoll(0.3, 2) == 0.15 && SkyIslandBossRules.HunterIslandExtraRoll(0.3, 1) == 0.3,
            "boss set: two rootweave pieces halve the island-goods roll");
        check(SkyIslandItemRules.IslandExtraFor(SkyIslandLootTier.Supply, 0.2) == 0
            && SkyIslandItemRules.IslandExtraFor(SkyIslandLootTier.Supply, SkyIslandBossRules.HunterIslandExtraRoll(0.2, 2)) == BossRushItemIds.SkyIslandHomecomingBento,
            "boss set: a halved roll really doubles the chance of island goods in a crate");
        check(SkyIslandBossRules.GrassBonus(2) == 1 && SkyIslandBossRules.GrassBonus(3) == 1 && SkyIslandBossRules.GrassBonus(1) == 0,
            "boss set: two straw-cloak pieces cut one more greenear sheaf");
        check(SkyIslandBossRules.BridgeSpeedBonus(2, true) == SkyIslandBossRules.WindbreakBridgeSpeed && SkyIslandBossRules.BridgeSpeedBonus(2, false) == 0f
            && SkyIslandBossRules.BridgeSpeedBonus(1, true) == 0f && SkyIslandBossRules.WindbreakBridgeSpeed > 0f && SkyIslandBossRules.WindbreakBridgeSpeed < 0.5f,
            "boss set: two Galebreaker pieces speed you up only on bridges and relay platforms");
    }

    private static void Residents(Action<bool, string> check)
    {
        SkyIslandStoryData fresh = SkyIslandStoryRules.CreateDefault();
        SkyIslandStoryData beaten = SkyIslandStoryRules.CreateDefault();
        var notes = new List<string>();
        foreach (SkyIslandBossProfile profile in SkyIslandBossRules.Profiles) notes.Add(profile.NoteId);
        beaten.discoveredNotes = notes.ToArray();
        SkyIslandStoryData partial = SkyIslandStoryRules.CreateDefault();
        partial.discoveredNotes = new[] { SkyIslandBossRules.ForemanNote, SkyIslandBossRules.StargazerNote, SkyIslandBossRules.SickleNote, SkyIslandBossRules.PiperNote };
        check(SkyIslandBossRules.DefeatedCount(fresh) == 0 && SkyIslandBossRules.DefeatedCount(beaten) == SkyIslandBossRules.Profiles.Length
            && SkyIslandBossRules.ProgressLine(beaten).EndsWith(SkyIslandBossRules.Profiles.Length + "/" + SkyIslandBossRules.Profiles.Length, StringComparison.Ordinal),
            "boss journal: first kills count toward the overview line");
        foreach (string npc in new[] { "sky_fuzhou", "sky_weibai", "sky_qinghe", "sky_miantai" })
        {
            string before = SkyIslandBossRules.ResidentLine(npc, fresh), middle = SkyIslandBossRules.ResidentLine(npc, partial),
                after = SkyIslandBossRules.ResidentLine(npc, beaten);
            check(!string.IsNullOrEmpty(before) && !string.IsNullOrEmpty(middle) && !string.IsNullOrEmpty(after) && before != after && middle != after,
                "boss residents: " + npc + " talks about the next boss still standing, then about the gear once all theirs are down");
            bool leading = npc != "sky_weibai";
            foreach (string line in new[] { before, middle, after })
                check(leading ? line.StartsWith("\n", StringComparison.Ordinal) : line.EndsWith("\n", StringComparison.Ordinal),
                    "boss residents: " + npc + " lines join the resident's own line breaks");
        }
        check(SkyIslandBossRules.ResidentLine("sky_fuzhou", partial) != SkyIslandBossRules.ResidentLine("sky_fuzhou", fresh),
            "boss residents: with the Foreman down, Fuzhou moves on to the Waylayer");
        check(SkyIslandBossRules.ResidentLine("sky_bellkeeper", beaten) == string.Empty && SkyIslandBossRules.ResidentLine("sky_zheling", fresh) == string.Empty,
            "boss residents: the named opponents stay quiet about the bosses");
    }

    private static int BrassIn(SkyIslandRecipe recipe)
    {
        if (recipe == null || recipe.Inputs == null) return 0;
        foreach (SkyIslandIngredient input in recipe.Inputs)
            if (input.TypeId == BossRushItemIds.SkyIslandBrassScrap) return input.Count;
        return 0;
    }

    private static Expected Row(string encounter, SkyIslandBossKind kind, SkyIslandEnemyTier tier, int variant, bool night, bool rival)
    {
        return new Expected { Encounter = encounter, Kind = kind, Tier = tier, Variant = variant, Night = night, Rival = rival };
    }
}
