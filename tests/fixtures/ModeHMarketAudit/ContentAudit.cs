// Production UI selection, odds and planner rules; Unity rendering and certification are host boundaries.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BossRush
{
    internal static class LocalizationHelper
    {
        internal static Dictionary<string, string> Map = new Dictionary<string, string>();
        public static void InjectLocalizations(Dictionary<string, string> map) { Map = map; }
    }

    internal static partial class ModeHContentCatalog
    {
        public static BossRushJsonValue PlayerWeights, EnemyWeights;
        public static List<ModeHScarSpec> Scars = new List<ModeHScarSpec>();
        public static List<ModeHInjurySpec> Injuries = new List<ModeHInjurySpec>();
        public static List<ModeHCommandSpec> Commands = new List<ModeHCommandSpec>();
        public static List<ModeHCommandTagMapping> CommandTagMap = new List<ModeHCommandTagMapping>();
        public static List<ModeHOddsTier> OddsTiers = new List<ModeHOddsTier>();
        public static List<ModeHOddsTestVector> OddsTestVectors = new List<ModeHOddsTestVector>();
        public static int GetWeight(BossRushJsonValue weights, string key, int fallback) { return fallback; }
        public static int GetWeightAt(BossRushJsonValue weights, string key, int index, int fallback) { return key == "kitQualityByGameQuality" ? index + 1 : fallback; }
        public static int GetArchetypeMatchup(string a, string b) { return 0; }
        internal static void LoadSelectionContent()
        {
            var doc = JsonDocument.Parse(System.IO.File.ReadAllText("Assets/Data/ModeH/Commands.json")).RootElement;
            Commands = Rows<ModeHCommandSpec>(doc, "commonCommands");
            foreach (var spec in Rows<ModeHCommandSpec>(doc, "signatureCommands")) { spec.IsSignature = true; Commands.Add(spec); }
            doc = JsonDocument.Parse(System.IO.File.ReadAllText("Assets/Data/ModeH/Scars.json")).RootElement;
            Injuries = Rows<ModeHInjurySpec>(doc, "injuries");
            // JSON uses slot, while the parsed production model calls it TargetSlot.
            foreach (var row in doc.GetProperty("injuries").EnumerateArray())
                foreach (var component in row.GetProperty("components").EnumerateArray())
                {
                    JsonElement slot;
                    if (component.TryGetProperty("slot", out slot))
                        Injuries.Find(x => x.InjuryId == row.GetProperty("injuryId").GetString()).Components
                            .Find(x => x.EffectId == component.GetProperty("effectId").GetString()).TargetSlot = slot.GetString();
                }
        }
    }

    internal static partial class ModeHCommandCompatibilityRegistry
    {
        public static string UnverifiedEffect;
        public static bool IsCommandSelectable(string key, string command) { return !string.IsNullOrEmpty(key); }
        public static ModeHCommandCompatibilityStatus GetEffectStatus(string key, string effect)
        { return effect == UnverifiedEffect ? ModeHCommandCompatibilityStatus.ReportOnly : ModeHCommandCompatibilityStatus.VerifiedBehavior; }
    }

    internal sealed class ModeHResolvedKit { public ModeHKitSpec Spec; public bool Available = true; public int ResolvedQuality = 3; }
    internal static class ModeHLoadoutKitRegistry
    {
        public static readonly List<ModeHResolvedKit> Kits = new List<ModeHResolvedKit>();
        public static ModeHResolvedKit GetKit(string id) { return Kits.Find(x => x.Spec.KitId == id); }
        public static List<string> GetStarterKitIds() { return Kits.Select(x => x.Spec.KitId).ToList(); }
        public static List<ModeHResolvedKit> GetSelectableKits(List<string> ids, string archetype, string profile)
        { return Kits.Where(x => ids.Contains(x.Spec.KitId)).ToList(); }
    }
    internal sealed class ModeHActionData { public string Label; public Action OnClick; public bool Interactable = true; public bool IsSelected; }
    internal sealed class ModeHPageContent
    {
        public string Title, Body;
        public List<string> Lines = new List<string>();
        public List<ModeHActionData> Actions = new List<ModeHActionData>(), PreparationOptions = new List<ModeHActionData>();
    }
    internal sealed partial class ModeHRuntimeModule
    {
        private bool _commandsClosed;
        private ModeHSeasonDto _season;
        private SeedOwner _runState = new SeedOwner { MatchIndex = 1, Lifecycle = ModeHLifecycle.OddsPreview };
        private string _starterDisplayName, _relayDisplayName;
        private int _selectedVirtualStake;
        private ModeHOddsQuote _currentOddsQuote;
        internal ModeHRuntimeModule(ModeHSeasonDto season) { _season = season; }
        internal bool Prepare(out string reason) { return EnsurePreparedMatchSelection(out reason); }
        internal ModeHPageContent Page() { return BuildLoadoutEditorPage(); }
        internal ModeHPageContent Preview() { var p = new ModeHPageContent(); AppendMatchPreview(p); return p; }
        internal int Score { get { return _currentOddsQuote.PlayerPublicScore; } }
        internal string Command { get { return _selectedMatchCommandId; } }
        private void RouteUiForLifecycle(ModeHLifecycle lifecycle) { }
        private string ResolveProfileDisplayName(string id) { return id ?? string.Empty; }
    }

    internal static class ContentAudit
    {
        private static int _checks;
        private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception("Content audit: " + message); }
        internal static void Run()
        {
            ModeHContentCatalog.Load(); ModeHContentCatalog.LoadSelectionContent(); ModeHLocalization.Inject();
            var t = new ModeHCombatTelemetry();
            t.BeginMatch(1, 1, "core"); t.Tick(30f);
            Check(!t.IsHighThreatCoreThreatening, "no core yet cannot cause awe cowardice");
            var core = new ModeHParticipantRef { IsEnemy = true, StableKey = "core", Character = new CharacterMainControl() };
            t.OnEnemyEntered(core); t.Tick(4f);
            Check(!t.IsHighThreatCoreThreatening, "late core counts from entry");
            t.Tick(1f); Check(t.IsHighThreatCoreThreatening, "core survived five seconds");
            t.OnParticipantDead(core, null); Check(!t.IsHighThreatCoreThreatening, "dead core is no longer threatening");
            t.BeginMatch(2, 1, null); t.Tick(10f); Check(!t.IsHighThreatCoreThreatening, "core clock resets next match");

            var plan = new ModeHMatchPlanDto { matchIndex = 1, enemyStableKeys = new List<string> { "Cname_Boss_Shot", "Cname_Prison_Boss" },
                enemyBatchIndices = new List<int> { 0, 1 }, publicSummary = new ModeHPublicSummaryDto {
                    hasHighThreatCore = true, primaryArchetypeId = "finisher", enemyCountMin = 2, enemyCountMax = 2,
                    entryScriptId = "core_last", conditionId = "narrow_cage", synergyTags = new List<string>() } };
            Check(ModeHEncounterPlanner.GetHighThreatCoreStableKey(plan) == "Cname_Prison_Boss", "core is selected by threat, not first entry");
            foreach (string choice in new[] { "hidden_quirk" })
            {
                string reason;
                Check(!ModeHEncounterPlanner.TryApplyRecon(plan, choice, out reason) && string.IsNullOrEmpty(plan.reconChoiceId), "empty mechanic cannot consume recon: " + choice);
            }
            string reconReason;
            Check(ModeHEncounterPlanner.TryApplyRecon(plan, "member_order", out reconReason) && plan.reconResult == "1-1", "real entry order is inspectable");
            Check(!ModeHEncounterPlanner.TryApplyRecon(plan, "second_equipment", out reconReason), "recon remains once per match");
            var starter = new ModeHProfileDto { profileId = "starter", stableKey = "Cname_Prison_Boss", archetypeId = "finisher", signatureCommandId = "handoff", injuryId = "armor" };
            var relay = new ModeHProfileDto { profileId = "relay", stableKey = "Cname_Boss_Shot", archetypeId = "assault", signatureCommandId = "weakness" };
            ModeHLoadoutKitRegistry.Kits.Clear();
            foreach (string slot in new[] { "Armor", "Helmet", "PrimaryWeapon", "SecondaryWeapon" })
                ModeHLoadoutKitRegistry.Kits.Add(new ModeHResolvedKit { Spec = new ModeHKitSpec { KitId = slot, NameKey = slot, DescKey = "Description " + slot, ReplaceSlot = slot } });
            var season = new ModeHSeasonDto { profiles = new List<ModeHProfileDto> { starter, relay },
                contract = new ModeHContractDto { contractMainProfileId = "starter", contractSubProfileId = "relay" },
                currentMatchPlan = plan, virtualStakeCredits = 6 };
            var runtime = new ModeHRuntimeModule(season); string error;
            Check(runtime.Prepare(out error), "preparation succeeds: " + error);
            Check(!season.matchRoster.starterKitIds.Contains("Armor") && season.matchRoster.relayKitIds.Contains("Armor"), "disabled armor removed only from injured fighter");
            Check(runtime.Command != "handoff", "starter never defaults to unreachable command");
            var commandPageOwner = runtime.Page();
            commandPageOwner.PreparationOptions[3].OnClick();
            var commandsPage = runtime.Page();
            Check(commandsPage.PreparationOptions.Count <= 6 && commandsPage.Actions.Count <= 3, "command pages stay bounded");
            Check(commandsPage.PreparationOptions.All(x => x.Label.Contains("seconds")), "each command exposes its active window");
            commandsPage.Actions.Last().OnClick();
            runtime.Page().PreparationOptions[1].OnClick();
            Check(runtime.Page().PreparationOptions.All(x => !x.Label.StartsWith("Armor") && !x.Label.StartsWith("✓ Armor")), "disabled armor has no editing button");
            runtime.Page().Actions.Last().OnClick();
            int score = runtime.Score;
            season.matchRoster.starterKitIds.Add("Armor");
            Check(runtime.Prepare(out error) && runtime.Score == score, "restored stale armor cannot improve score");
            var page = runtime.Page(); Check(page.PreparationOptions.Count == 4, "preparation has four sections");
            page.PreparationOptions[0].OnClick();
            var rosterPage = runtime.Page(); Check(rosterPage.PreparationOptions.Any(x => x.Label.Contains("Damaged Armor")), "roster shows actual injury");
            var stale = rosterPage.PreparationOptions.Last().OnClick;
            season.matchRoster = new ModeHMatchRosterDto { matchIndex = 1, matchRelayProfileId = "relay" };
            stale(); Check(season.matchRoster.matchRelayProfileId == "relay", "stale page cannot alter replacement roster");
            var preview = runtime.Preview();
            Check(preview.Lines.Any(x => x.Contains("2–2")) && preview.Lines.Any(x => x.Contains("core")), "brief shows enemy count and core warning");

            var input = new ModeHOddsPlayerInput { Starter = starter, Relay = relay };
            var quote = ModeHOddsController.BuildQuote(input, plan);
            plan.publicSummary.visibleWoundedEnemyCount = 3;
            plan.publicSummary.visibleAnomalyIds = new List<string> { "blood", "error" };
            plan.publicSummary.conditionId = "open_field";
            var after = ModeHOddsController.BuildQuote(input, plan);
            Check(quote.PlayerPublicScore == after.PlayerPublicScore && quote.EnemyPublicScore == after.EnemyPublicScore, "legacy empty-effect fields cannot alter odds");
            Console.WriteLine("Mode H content audit: " + _checks + " assertions passed; rendering and certification are stubs.");
        }
    }
}
