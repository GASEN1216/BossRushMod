using System;
using System.Collections.Generic;

namespace BossRush
{
    static class LoadoutOptions
    {
        static void Check(bool condition, string reason)
        { if (!condition) throw new Exception(reason); Console.WriteLine("PASS " + reason); }

        internal static void Run()
        {
            var profile = new ModeHProfileDto { profileId = "starter", stableKey = "fixture_boss" };
            var selected = new List<string> { "replacement_HeadArmor" };
            var runtime = new ModeHRuntimeModule
            {
                _season = new ModeHSeasonDto
                {
                    profiles = new List<ModeHProfileDto> { profile },
                    unlockedKitIds = new List<string> { "replacement_HeadArmor" },
                    matchRoster = new ModeHMatchRosterDto { matchIndex = 2, matchStarterProfileId = "starter", starterKitIds = selected },
                },
                _runState = new FixtureRunState { MatchIndex = 2, Lifecycle = ModeHLifecycle.LoadoutEditing },
            };
            foreach (string slot in new[] { "PrimaryWeapon", "SecondaryWeapon", "MeleeWeapon", "HeadArmor",
                "Armor", "Backpack", "Totem1", "Totem2" })
            {
                runtime.Outfit.Add(new ModeHResolvedKit
                {
                    Available = true, ResolvedTypeId = 1, ResolvedQuality = 3,
                    Spec = new ModeHKitSpec { KitId = "original_" + slot, NameKey = "original_" + slot, ReplaceSlot = slot },
                });
            }
            ModeHLoadoutKitRegistry.Kits["replacement_HeadArmor"] = new ModeHResolvedKit
            {
                Available = true, ResolvedTypeId = 2, ResolvedQuality = 4,
                Spec = new ModeHKitSpec { KitId = "replacement_HeadArmor", NameKey = "replacement_HeadArmor", ReplaceSlot = "HeadArmor" },
            };
            ModeHPageContent page = runtime.Options(profile, selected);
            Check(page.PreparationOptions.Count == 9, "editor lists all eight original slots plus replacement");
            Check(page.PreparationOptions.FindAll(option => option.IsSelected).Count == 8,
                "every equipped slot has one selected card including replacement");
            ModeHActionData original = page.PreparationOptions.Find(option => option.Label.StartsWith("original_HeadArmor\n"));
            Check(original != null && !original.IsSelected && original.Icon != null,
                "replaced original remains visible with icon and without selected badge");
            original.OnClick();
            Check(selected.Count == 0 && runtime.SaveRequests == 1 && runtime.Routes == 1,
                "clicking original restores slot and requests persistence before refreshing");
            Check(ModeHKitPreferenceLedger.Find("fixture_boss") != null && ModeHKitPreferenceLedger.Find("fixture_boss").Count == 0,
                "restoring original gear is remembered for this fighter (empty list = full original outfit)");
            page = runtime.Options(profile, selected);
            Check(page.PreparationOptions.Find(option => option.Label.StartsWith("original_HeadArmor\n")).IsSelected,
                "restored original displays equipped on refresh");
            ModeHActionData replacement = page.PreparationOptions.Find(option => option.Label.StartsWith("replacement_HeadArmor\n"));
            replacement.OnClick();
            Check(selected.Count == 1 && selected[0] == "replacement_HeadArmor" && runtime.SaveRequests == 2,
                "replacement can be worn again and queued for season persistence");
            Check(ModeHKitPreferenceLedger.Find("fixture_boss").Contains("replacement_HeadArmor"),
                "wearing a replacement is remembered for this fighter");
            runtime._season.matchRoster = new ModeHMatchRosterDto { matchIndex = 3 };
            replacement.OnClick();
            Check(selected.Count == 1 && runtime.SaveRequests == 2,
                "old page callback cannot alter next-match equipment");
            profile.injuryId = "HeadArmor";
            page = runtime.Options(profile, selected);
            Check(page.PreparationOptions.Count == 7,
                "injury removes original and replacement from the same disabled slot");
        }
    }

    partial class ModeHRuntimeModule
    {
        internal bool _commandsClosed;
        internal FixtureRunState _runState;
        internal int SaveRequests, Routes;
        internal readonly List<ModeHResolvedKit> Outfit = new List<ModeHResolvedKit>();
        private List<ModeHResolvedKit> GetPreparedProfileOutfit(ModeHProfileDto profile) { return Outfit; }
        private ModeHProfileDto FindSeasonProfile(string id)
        { return _season == null || _season.profiles == null ? null : _season.profiles.Find(p => p.profileId == id); }
        private bool TryPersistSeason(string reason) { SaveRequests++; return true; }
        private void RouteUiForLifecycle(ModeHLifecycle lifecycle)
        { if (SaveRequests <= Routes) throw new Exception("refresh before persistence"); Routes++; }
        internal ModeHPageContent Options(ModeHProfileDto profile, List<string> selected)
        { var page = new ModeHPageContent(); AddKitOptions(page, profile, selected); return page; }
    }
    enum ModeHLifecycle { LoadoutEditing, OddsPreview }
    class FixtureRunState { internal int MatchIndex; internal ModeHLifecycle Lifecycle; }
    class ModeHPageContent
    {
        internal readonly List<ModeHActionData> PreparationOptions = new List<ModeHActionData>();
        internal int PreparationColumns;
        internal float PreparationRowHeight;
    }
    class ModeHActionData
    {
        internal string Label, SelectedBadge;
        internal bool IsSelected;
        internal Action OnClick;
        internal object Icon;
        internal int IconQuality;
    }
    class ModeHResolvedKit { internal ModeHKitSpec Spec; internal bool Available; internal int ResolvedTypeId, ResolvedQuality; }
    class ModeHKitSpec { internal string KitId, NameKey, DescKey, ReplaceSlot; }
    class ItemMetaData { internal object icon; }
    static class L10n
    {
        internal static string T(string key) { return key; }
        internal static string T(string cn, string en) { return cn; }
    }
    static class ModeHInjuryAndScarSystem
    { internal static bool InjuryDisablesKitSlot(string injury, string slot) { return injury == slot; } }
    // 选手配装偏好的存档边界（生产走 BossRushSlotJsonStore）：这里只记最后一次写入，验证「写了什么、读回什么」
    static class ModeHKitPreferenceLedger
    {
        internal static readonly Dictionary<string, List<string>> Saved = new Dictionary<string, List<string>>();
        internal static int Writes;
        internal static List<string> Find(string key)
        { List<string> v; return key != null && Saved.TryGetValue(key, out v) ? new List<string>(v) : null; }
        internal static bool Record(string key, IList<string> kits)
        { if (string.IsNullOrEmpty(key)) return false; Writes++; Saved[key] = new List<string>(kits ?? new List<string>()); return true; }
    }
    static partial class ModeHLoadoutKitRegistry
    {
        internal static readonly Dictionary<string, ModeHResolvedKit> Kits = new Dictionary<string, ModeHResolvedKit>();
        internal static ModeHResolvedKit GetKit(string id) { ModeHResolvedKit kit; return Kits.TryGetValue(id, out kit) ? kit : null; }
        internal static IEnumerable<ModeHResolvedKit> GetSelectableKits(List<string> ids, string archetype, string profile)
        { foreach (string id in ids) { ModeHResolvedKit kit = GetKit(id); if (kit != null) yield return kit; } }
    }
}
