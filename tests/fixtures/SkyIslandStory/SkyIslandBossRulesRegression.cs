using System;
using System.Collections.Generic;
using BossRush;

/// <summary>
/// 天空岛头目 / 岛主的纯规则回归（R1：残星匠首 + 瞭台观星手）。
///
/// 钉住的是 owner 拍板的口径与防换皮纪律，不是手感：
/// - 掉落：岛主每次必出一件（权重合计 100、不掉权重 0），头目三成；按均匀种子网格精确计频，不靠大数定律糊过去。
/// - 档案：挂在已有遭遇组的带队位置上，内容表档次与档案一致；五栏非空、核心招式两两不同。
/// - 档次：按官方护甲公式折算的有效血量与伤害、反应倍率随档次严格递增（Elite &lt; Chief &lt; Champion &lt; Lord &lt; Storm）。
/// - 预警：每个圈的逃圈速度 ≤ 5.5 m/s（与噬风同一条判据）。
/// - 星工两件套只让渡口工台配方的残铜片少耗一片，且不低于一片；配方原件不被改写。
/// </summary>
internal static class SkyIslandBossRulesRegression
{
    // 与 SkyIslandEnemyTiers 同口径（该文件依赖 Unity，不进这个夹具；数字由 SkyIslandContentExpansionGuard 钉住）。
    private const double EliteHealth = 2.6, ChampionHealth = 4.5, StormHealth = 13.0;
    private const double EliteDamage = 1.35, ChampionDamage = 1.55, StormDamage = 1.8;
    private const double EliteReaction = 1.3, ChampionReaction = 1.45, StormReaction = 1.7;

    internal static void Run(Action<bool, string> check)
    {
        Profiles(check);
        Drops(check);
        Tiers(check);
        Telegraphs(check);
        Gear(check);
        Crafting(check);
        Residents(check);
    }

    private static void Profiles(Action<bool, string> check)
    {
        SkyIslandBossProfile[] all = SkyIslandBossRules.Profiles;
        check(all.Length == 2, "boss: R1 ships exactly the Foreman and the Stargazer");
        SkyIslandBossProfile foreman = SkyIslandBossRules.Find("G", 0);
        SkyIslandBossProfile stargazer = SkyIslandBossRules.Find("S4", 0);
        check(foreman != null && foreman.Kind == SkyIslandBossKind.Foreman && foreman.Tier == SkyIslandEnemyTier.Lord,
            "boss: G group lead is the island lord");
        check(stargazer != null && stargazer.Kind == SkyIslandBossKind.Stargazer && stargazer.Tier == SkyIslandEnemyTier.Chief,
            "boss: S4 group lead is the chief");
        check(SkyIslandBossRules.Find("G", 1) == null && SkyIslandBossRules.Find("G_02", 0) == null && SkyIslandBossRules.Find(null, 0) == null,
            "boss: only the lead slot of the named group gets a profile");

        SkyIslandContentData content = SkyIslandContent.CreateFallback();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var verbs = new HashSet<string>(StringComparer.Ordinal);
        foreach (SkyIslandBossProfile profile in all)
        {
            SkyIslandEncounterDefinition group = Array.Find(content.Encounters, e => e.Id == profile.EncounterId);
            check(group != null && !group.Manual && group.TierFor(profile.Index) == profile.Tier,
                "boss: content table tier matches the profile for " + profile.Id);
            check(ids.Add(profile.Id) && keys.Add(profile.NameKey) && profile.NameKey.StartsWith("BossRush_SkyIsland_", StringComparison.Ordinal),
                "boss: unique id and BossRush_SkyIsland_ name key for " + profile.Id);
            check(SkyIslandBossRules.IsBossNote(profile.NoteId) && SkyIslandBossRules.FindByNote(profile.NoteId) == profile,
                "boss: first-kill note is registered for " + profile.Id);
            // 五栏设计说明是档案表上方的注释，由 SkyIslandBossEcologyGuard 解析核对（非空、核心招式两两不同）；这里钉「一种招式一个控制器」。
            check(verbs.Add(profile.Kind.ToString()), "boss: one controller kind per profile (no reskins) — " + profile.Id);
            bool wasChinese = L10n.IsChinese;
            L10n.IsChinese = false;
            string english = SkyIslandBossRules.Name(profile);
            L10n.IsChinese = true;
            string chinese = SkyIslandBossRules.Name(profile);
            L10n.IsChinese = wasChinese;
            check(!string.IsNullOrEmpty(chinese) && !string.IsNullOrEmpty(english) && chinese != english
                && chinese != SkyIslandBossRules.NameCn((SkyIslandBossKind)99) && english == SkyIslandBossRules.NameEn(profile.Kind),
                "boss: bilingual name resolves in the current language for " + profile.Id);
        }
        check(!SkyIslandBossRules.IsBossNote("Keepsake_Core") && !SkyIslandBossRules.IsBossNote(null), "boss: unrelated notes are not boss notes");

        // 严格绑定的正式内容表（Assets/Data/SkyIsland/World.json）同样认新档次名，且与内置表一致。
        string world = System.IO.File.ReadAllText(SkyIslandContent.RelativePath);
        SkyIslandContentData parsed;
        string error;
        check(SkyIslandContent.TryParse(world, out parsed, out error) && parsed.Source == "Json"
            && Array.Find(parsed.Encounters, e => e.Id == "G").Lead == SkyIslandEnemyTier.Lord
            && Array.Find(parsed.Encounters, e => e.Id == "S4").Lead == SkyIslandEnemyTier.Chief,
            "boss: the formal world table names the lord and the chief (" + error + ")");
        SkyIslandEnemyTier tier;
        check(SkyIslandContent.TryParseTier("Chief", out tier) && tier == SkyIslandEnemyTier.Chief
            && SkyIslandContent.TryParseTier("Lord", out tier) && tier == SkyIslandEnemyTier.Lord
            && !SkyIslandContent.TryParseTier("4", out tier), "boss: tiers parse from stable names, never ordinals");
        check(!SkyIslandContent.TryParse(world.Replace("\"lead\": \"Lord\"", "\"lead\": \"Elite\""), out parsed, out error),
            "boss: demoting the lord in JSON breaks the strict binding and falls back");
    }

    private static void Drops(Action<bool, string> check)
    {
        SkyIslandBossProfile foreman = SkyIslandBossRules.Find("G", 0);
        SkyIslandBossProfile stargazer = SkyIslandBossRules.Find("S4", 0);
        const int samples = 10000;

        int[] lordCounts = new int[foreman.Gear.Length];
        int lordNone = 0, total = foreman.NoDropWeight;
        foreach (SkyIslandBossGearPiece piece in foreman.Gear) total += piece.Weight;
        for (int i = 0; i < samples; i++)
        {
            int pick = SkyIslandBossRules.RollDrop(foreman, (i + 0.5) / samples);
            if (pick < 0) lordNone++;
            else lordCounts[pick]++;
        }
        check(foreman.NoDropWeight == 0 && total == 100 && lordNone == 0, "boss drop: the island lord always leaves exactly one piece");
        for (int i = 0; i < foreman.Gear.Length; i++)
            check(lordCounts[i] == foreman.Gear[i].Weight * samples / total,
                "boss drop: lord piece " + foreman.Gear[i].TypeId + " frequency is exactly its weight");

        int chiefHits = 0;
        for (int i = 0; i < samples; i++) if (SkyIslandBossRules.RollDrop(stargazer, (i + 0.5) / samples) == 0) chiefHits++;
        check(chiefHits == 3000 && Math.Abs(SkyIslandBossRules.DropChance(stargazer, 0) - 0.3) < 1e-9,
            "boss drop: the chief leaves its helm three times in ten");

        check(SkyIslandBossRules.RollDrop(foreman, -1.0) == 0 && SkyIslandBossRules.RollDrop(foreman, double.NaN) == 0
            && SkyIslandBossRules.RollDrop(foreman, 1.0) == foreman.Gear.Length - 1,
            "boss drop: out-of-range rolls clamp into the table");
        check(SkyIslandBossRules.RollDrop(stargazer, 0.999) == -1, "boss drop: the chief's no-drop band sits after its gear");
        check(SkyIslandBossRules.RollDrop(null, 0.5) == -1 && SkyIslandBossRules.DropChance(foreman, 9) == 0.0,
            "boss drop: missing profile or index never drops");
        check(SkyIslandBossRules.RollDrop(foreman, 0.42) == SkyIslandBossRules.RollDrop(foreman, 0.42), "boss drop: same roll, same piece");
    }

    private static void Tiers(Action<bool, string> check)
    {
        SkyIslandBossProfile foreman = SkyIslandBossRules.Find("G", 0);
        SkyIslandBossProfile stargazer = SkyIslandBossRules.Find("S4", 0);
        double chief = SkyIslandBossRules.EffectiveHealth(stargazer);
        double lord = SkyIslandBossRules.EffectiveHealth(foreman);
        check(EliteHealth < chief && chief < ChampionHealth && ChampionHealth < lord && lord < StormHealth,
            "boss tier: armour-adjusted health increases strictly Elite < Chief < Champion < Lord < Storm (" + chief + ", " + lord + ")");
        check(EliteDamage < stargazer.Damage && stargazer.Damage < ChampionDamage && ChampionDamage < foreman.Damage
            && foreman.Damage < StormDamage && foreman.Damage <= 3f, "boss tier: damage multipliers increase strictly and stay capped");
        check(EliteReaction < stargazer.Reaction && stargazer.Reaction < ChampionReaction && ChampionReaction < foreman.Reaction
            && foreman.Reaction < StormReaction, "boss tier: reaction speedups increase strictly");
        check(Math.Abs(SkyIslandBossRules.ArmorFactor(3.0, 2.0) - 2.0 / 3.0) < 1e-9 && SkyIslandBossRules.ArmorFactor(0.0, 2.0) == 1.0
            && SkyIslandBossRules.ArmorFactor(2.0, 5.0) == 1.0, "boss tier: armour factor mirrors the official Health.Hurt formula");
        check(foreman.Scale >= 1f && foreman.Scale < 1.9f && stargazer.Scale >= 1f, "boss tier: model scale stays below the Windeater's");
    }

    private static void Telegraphs(Action<bool, string> check)
    {
        check(SkyIslandBossRules.EscapeSpeed(SkyIslandBossRules.StarfireRadius, SkyIslandBossRules.StarfireTelegraph) <= SkyIslandBossRules.MaxEscapeSpeed,
            "boss telegraph: starfire rings can be walked out of");
        check(SkyIslandBossRules.EscapeSpeed(SkyIslandBossRules.FlareRadius, SkyIslandBossRules.MarkLockSeconds) <= SkyIslandBossRules.MaxEscapeSpeed,
            "boss telegraph: a locked flare mark can be left in time");
        check(SkyIslandBossRules.StarfireSpread > SkyIslandBossRules.StarfireRadius * 1.3f,
            "boss telegraph: side starfire rings leave a walkable gap next to the centre ring");
        check(float.IsPositiveInfinity(SkyIslandBossRules.EscapeSpeed(2f, 0f)), "boss telegraph: an instant ring is flagged as unescapable");

        float[] thresholds = SkyIslandBossRules.ForemanPhaseThresholds;
        check(SkyIslandBossRules.PhaseFor(1f, thresholds) == 0 && SkyIslandBossRules.PhaseFor(0.70f, thresholds) == 1
            && SkyIslandBossRules.PhaseFor(0.41f, thresholds) == 1 && SkyIslandBossRules.PhaseFor(0.40f, thresholds) == 2
            && SkyIslandBossRules.PhaseFor(0.15f, thresholds) == 3 && SkyIslandBossRules.PhaseFor(0f, thresholds) == 3,
            "boss phase: three pylon phases at 70% / 40% / 15%");
        int previous = 0;
        bool monotonic = true;
        for (int i = 100; i >= 0; i--)
        {
            int phase = SkyIslandBossRules.PhaseFor(i / 100f, thresholds);
            if (phase < previous) monotonic = false;
            previous = phase;
        }
        check(monotonic, "boss phase: phases only move forward as health drops");
        check(SkyIslandBossRules.PylonLifetime > 0f && SkyIslandBossRules.ShieldArmorBroken < SkyIslandBossRules.ShieldArmor
            && SkyIslandBossRules.CastsBeforeOverheat >= 2 && SkyIslandBossRules.OverheatDamageTaken > 0f,
            "boss phase: pylons expire, a broken harness halves the shield, overheat opens a damage window");
        check(SkyIslandBossRules.MarkCloseRange < SkyIslandBossRules.MarkRange && SkyIslandBossRules.FlareShots >= 1,
            "boss mark: rushing the stargazer inside close range stops the marks");
    }

    private static void Gear(Action<bool, string> check)
    {
        SkyIslandBossGearSpec[] specs = SkyIslandBossRules.GearSpecs;
        check(specs.Length == 4 && SkyIslandBossRules.AllGearTypeIds.Length == 4, "boss gear: four pieces in R1");
        var seen = new HashSet<int>();
        foreach (SkyIslandBossGearSpec spec in specs)
        {
            check(seen.Add(spec.TypeId) && Array.IndexOf(SkyIslandBossRules.AllGearTypeIds, spec.TypeId) >= 0,
                "boss gear: unique registered TypeID " + spec.TypeId);
            string suffix = spec.Slot == "Helmat" ? "_Helmet" : spec.Slot == "Armor" ? "_Armor" : spec.Slot == "Backpack" ? "_Backpack" : null;
            check(suffix != null && spec.ModelBaseName.EndsWith(suffix, StringComparison.Ordinal),
                "boss gear: bundle base name follows the EquipmentFactory slot keyword for " + spec.TypeId);
            check(spec.StatKey == "HeadArmor" || spec.StatKey == "BodyArmor" || spec.StatKey == "InventoryCapacity",
                "boss gear: stat key is an official stat for " + spec.TypeId);
            check(spec.StatValue > 0f && spec.Quality >= 1 && spec.Quality <= 7, "boss gear: positive stat and a normal quality band for " + spec.TypeId);
            check((spec.Slot == "Backpack") == (spec.Durability <= 0f), "boss gear: helmets and armour wear down, the pack does not — " + spec.TypeId);
            check(spec.IconName.StartsWith("sky_island_", StringComparison.Ordinal) && spec.LocKey.StartsWith("BossRush_SkyIsland_", StringComparison.Ordinal),
                "boss gear: icon and localization key naming for " + spec.TypeId);
            // 价值与中英名只在 SkyIslandItemRules 一处；但它不是岛上的克隆物品，不进 AllTypeIds（那张表是 500068 起连续的岛上物品族）。
            check(SkyIslandItemRules.ValueOf(spec.TypeId) > 0 && Array.IndexOf(SkyIslandItemRules.AllTypeIds, spec.TypeId) < 0
                && SkyIslandItemRules.NameCn(spec.TypeId) != "天空岛物品" && SkyIslandItemRules.NameEn(spec.TypeId) != "Sky Islands item",
                "boss gear: value and bilingual name live in SkyIslandItemRules, outside the island clone family, for " + spec.TypeId);
        }
        foreach (SkyIslandBossProfile profile in SkyIslandBossRules.Profiles)
            foreach (SkyIslandBossGearPiece piece in profile.Gear)
            {
                SkyIslandBossGearSpec spec = SkyIslandBossRules.GearSpec(piece.TypeId);
                check(spec != null && spec.Slot == piece.Slot && piece.Weight > 0, "boss gear: " + profile.Id + " wears " + piece.TypeId + " in its own slot");
            }
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

    private static void Residents(Action<bool, string> check)
    {
        SkyIslandStoryData fresh = SkyIslandStoryRules.CreateDefault();
        SkyIslandStoryData beaten = SkyIslandStoryRules.CreateDefault();
        beaten.discoveredNotes = new[] { SkyIslandBossRules.ForemanNote, SkyIslandBossRules.StargazerNote };
        check(SkyIslandBossRules.DefeatedCount(fresh) == 0 && SkyIslandBossRules.DefeatedCount(beaten) == 2
            && SkyIslandBossRules.ProgressLine(beaten).EndsWith("2/2", StringComparison.Ordinal), "boss journal: first kills count toward the overview line");
        foreach (string npc in new[] { "sky_fuzhou", "sky_weibai" })
        {
            string before = SkyIslandBossRules.ResidentLine(npc, fresh), after = SkyIslandBossRules.ResidentLine(npc, beaten);
            check(!string.IsNullOrEmpty(before) && !string.IsNullOrEmpty(after) && before != after,
                "boss residents: " + npc + " says where it is before the first kill and what the gear is for after");
        }
        check(SkyIslandBossRules.ResidentLine("sky_fuzhou", fresh).StartsWith("\n", StringComparison.Ordinal)
            && SkyIslandBossRules.ResidentLine("sky_weibai", fresh).EndsWith("\n", StringComparison.Ordinal),
            "boss residents: lines join each resident's own line breaks");
        check(SkyIslandBossRules.ResidentLine("sky_qinghe", beaten) == string.Empty, "boss residents: other residents stay quiet about the bosses");
    }

    private static int BrassIn(SkyIslandRecipe recipe)
    {
        if (recipe == null || recipe.Inputs == null) return 0;
        foreach (SkyIslandIngredient input in recipe.Inputs)
            if (input.TypeId == BossRushItemIds.SkyIslandBrassScrap) return input.Count;
        return 0;
    }
}
