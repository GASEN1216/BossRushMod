using System;
using System.Collections.Generic;
using BossRush;

class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    static void Main()
    {
        const string key = "Cname_Boss_Hammer";
        var pet = new PetNestPetRecord { id = "pet-1", lineageKey = key };
        var owner = new ModBehaviour();
        owner.Pool.Add(new EnemyPresetInfo { name = key, displayName = key });
        PetNestCompanionSpawner.Presets[key] = new CharacterRandomPreset { nameKey = key };
        PetNestLineageCatalog.EnsureBuilt(owner);
        PetNestLineageInfo info;
        Check(PetNestLineageCatalog.TryGet(key, out info), "the original eligible lineage is present");
        Check(info.DisplayName == "未知血脉", "missing translation must not expose a raw or starred Cname key");
        LocalizationHelper.Texts[key] = "锤哥";
        Check(info.DisplayName == "锤哥" && PetNestService.GetPetDisplayName(pet) == "锤哥", "same prebuilt entry recovers after language data becomes ready");
        L10n.Chinese = false;
        LocalizationHelper.Texts[key] = "Hammer";
        Check(info.DisplayName == "Hammer" && PetNestService.GetPetDisplayName(pet) == "Hammer", "same entry and player name follow the current language without rebuilding");

        PetNestLineageCatalog.Invalidate();
        PetNestLineageCatalog.EnsureBuilt(new ModBehaviour());
        Check(!PetNestLineageCatalog.IsKnownLineage(key), "disabled lineage remains ineligible for hatching");
        Check(PetNestService.GetPetDisplayName(pet) == "Hammer", "existing cub names still localize when the current pool excludes their lineage");
        PetNestLineageCatalog.ResetStaticCaches();
        Check(PetNestService.GetPetDisplayName(pet) == "Hammer", "names work even before the catalog is constructed");
        pet.displayName = "My cub";
        Check(PetNestService.GetPetDisplayName(pet) == "My cub", "player nickname remains authoritative");
        pet.displayName = null;

        owner.Pool[0].displayName = "备用名字";
        LocalizationHelper.Texts.Clear();
        PetNestLineageCatalog.EnsureBuilt(owner);
        Check(PetNestLineageCatalog.TryGet(key, out info) && info.DisplayName == "备用名字", "a real pool display name remains a usable fallback");
        PetNestLineageCatalog.ResetStaticCaches();
        owner.Pool[0].displayName = "*" + key + "*";
        PetNestLineageCatalog.EnsureBuilt(owner);
        Check(PetNestLineageCatalog.TryGet(key, out info) && info.DisplayName == "Unknown bloodline", "starred pool fallback also cannot expose an identifier");
        LocalizationHelper.Throw = true;
        Check(PetNestService.GetPetDisplayName(pet) == "Unknown bloodline", "temporary official localization failure follows the normal fallback contract");
        LocalizationHelper.Throw = false;
        pet.lineageKey = DragonKingConfig.BossNameKey;
        Check(PetNestService.GetPetDisplayName(pet) == DragonKingConfig.BossNameEN, "custom lineage uses its existing English constants");
        L10n.Chinese = true;
        Check(PetNestService.GetPetDisplayName(pet) == DragonKingConfig.BossNameCN, "same custom lineage switches language without respawn");
        Check(pet.id == "pet-1" && pet.lineageKey == DragonKingConfig.BossNameKey && pet.displayName == null, "resolving names never rewrites save identities or player nicknames");
        Console.WriteLine("PetNestLineageNames: " + checks + " assertions passed");
    }
}

public partial class CharacterRandomPreset
{
    public string nameKey;
    public float elementFactor_Physics = 1, elementFactor_Fire = 1, elementFactor_Ice = 1,
        elementFactor_Poison = 1, elementFactor_Electricity = 1, elementFactor_Space = 1, elementFactor_Ghost = 1;
}

namespace SodaCraft.Localizations
{
    public static class Text
    {
        public static string ToPlainText(this string key) { return BossRush.LocalizationHelper.GetLocalizedText(key); }
    }
}

namespace BossRush
{
    internal enum ElementTypes { physics, fire, ice, poison, electricity, space, ghost }
    internal class PetNestPetRecord { internal string id, lineageKey, displayName; }
    internal class EnemyPresetInfo { internal string name, displayName; }
    internal class ModBehaviour
    {
        internal readonly List<EnemyPresetInfo> Pool = new List<EnemyPresetInfo>();
        internal List<EnemyPresetInfo> GetFilteredEnemyPresets() { return Pool; }
        internal static void DevLog(string value) { }
    }
    internal static class PetNestCompanionSpawner
    {
        internal static readonly Dictionary<string, CharacterRandomPreset> Presets = new Dictionary<string, CharacterRandomPreset>();
        internal static CharacterRandomPreset ResolveSourcePreset(string key) { CharacterRandomPreset preset; return Presets.TryGetValue(key, out preset) ? preset : null; }
    }
    internal static class LocalizationHelper
    {
        internal static readonly Dictionary<string, string> Texts = new Dictionary<string, string>();
        internal static bool Throw;
        internal static string GetLocalizedText(string key)
        {
            // Official GetPlainText returns *key* for a miss; the production helper returns key after an exception.
            if (Throw) return key;
            string text;
            return Texts.TryGetValue(key, out text) ? text : "*" + key + "*";
        }
    }
    internal static class L10n { internal static bool Chinese = true; internal static string T(string cn, string en) { return Chinese ? cn : en; } }
    internal static class DragonKingConfig { internal const string BossNameKey = "boss_dragonking", BossNameCN = "龙王", BossNameEN = "Dragon King"; }
    internal static class DragonDescendantConfig { internal const string BOSS_NAME_KEY = "boss_dragon_descendant", BOSS_NAME_CN = "龙裔", BOSS_NAME_EN = "Dragon Descendant"; }
    internal static class PhantomWitchConfig { internal const string BossNameKey = "boss_phantomwitch", BossNameCN = "幽灵女巫", BossNameEN = "Phantom Witch"; }
    internal static class SandstormChampionPoolAdapter { internal static bool IsPreset(EnemyPresetInfo info) { return false; } }
    internal static class EclipseBloomPoolAdapter { internal static bool IsPreset(EnemyPresetInfo info) { return false; } }
    internal static class PetNestTuning
    {
        internal const float DefaultCubModelScale = .6f;
        internal const string DestinationStormSea = "storm", DestinationAcidRuins = "acid", DestinationFrozenWaste = "frozen";
    }
}
