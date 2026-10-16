"""Campaign personal facts consume original entry leases without rearming another run."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def body(path, signature):
    source = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
    assert source.count(signature) == 1, signature
    start = source.index("{", source.index(signature))
    depth = 0
    for end in range(start, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if not depth:
            return source[start:end + 1]
    raise AssertionError(signature)


def main():
    coop = "Campaign/CampaignRuntimeModule_Coop.cs"
    tracker = "Campaign/CampaignObjectiveTracker.cs"
    bridge = "Campaign/CampaignModeBridge.cs"
    runtime = "Campaign/CampaignRuntimeModule.cs"
    checks = [
        ("actual world scope comes from original mode entry", coop, "private void RefreshCoopCampaignSource(",
         r"string scope = ResolveCoopCampaignEntry\(mode\);[\s\S]*?coopCampaignScope = scope;"),
        ("scope includes actual entry and mode", coop, "private string ResolveCoopCampaignEntry(",
         r"return string.IsNullOrEmpty\(entry\) \? null : mode \+ \":\" \+ entry;"),
        ("tracking requires current actual token", coop, "private bool IsCoopCampaignEntryCurrent(",
         r"!BossRushCoop.HasBoundScene[\s\S]*?return token == BossRushCoop.SceneToken;"),
        ("new original entry retires only its tracker", coop, "private bool IsCoopCampaignTrackingCurrent(",
         r"IsCoopCampaignEntryCurrent\(scope\)[\s\S]*?CampaignObjectiveTracker.IsExtractionLeaseCurrent\(previous\)\) CampaignObjectiveTracker.ResetSession\(\);"),
        ("return entry restores original session", coop, "private bool IsCoopCampaignTrackingCurrent(",
         r"coopCampaignLeases.TryGetValue\(key, out original\)\)\s*return CampaignObjectiveTracker.RestoreExtractionLease\(original\);"),
        ("personal capture resolves real entry before arming", tracker, "internal static ExtractionLease CaptureExtractionLease(",
         r"if \(!CampaignRuntimeModule.PrepareCoopCampaignEntry\(mode\)\) return null;\s*EnsureArmedFor\(mode\);"),
        ("each lease shares exact original clock", tracker, "internal static ExtractionLease CaptureExtractionLease(",
         r"Objectives = _progress.ToArray\(\), Clock = _clock"),
        ("restore retains original objective objects and clock", tracker, "internal static bool RestoreExtractionLease(",
         r"_clock = value.Clock;\s*_progress.AddRange\(value.Objectives\);"),
        ("restore checks original asset and contract", tracker, "internal static bool RestoreExtractionLease(",
         r"value.Asset != BossRushCoop.LocalAssetId\) return false;[\s\S]*?revision != value.ContractRevision"),
        ("successful original fact shares notification latch", tracker, "internal static bool ReportCoopFactLease(",
         r"if \(!CampaignProgressService.NotifyObjectivesSatisfied\(value.Chapter\)\) return false;\s*if \(value.Clock != null\) value.Clock.Notified = true;"),
        ("original entry captures contract progress once", coop, "private void CaptureCoopCampaignTracking(",
         r"coopCampaignLeases.ContainsKey\(key\)[\s\S]*?CampaignObjectiveTracker.CaptureExtractionLease\(mode\)[\s\S]*?coopCampaignLeases.Add\(key, lease\)"),
        ("world projection rejects unbound token", coop, "private void ApplyCoopCampaignWorld(",
         r"!BossRushCoop.HasBoundScene[\s\S]*?state.SceneToken != BossRushCoop.SceneToken\) return;"),
        ("same scene new scope retires only its current tracker", coop, "private void ApplyCoopCampaignWorld(",
         r"CampaignObjectiveTracker.IsExtractionLeaseCurrent\(previous\)\) CampaignObjectiveTracker.ResetSession\(\);"),
        ("source freezes original admitted recipients and asset", coop, "private bool DeliverCoopCampaignFact(",
         r"if \(TryGetCoopCampaignParticipant\(fact.Mode, scope, player.Id, out asset\)\)[\s\S]*?coopFacts.Deliver\(player.Id, scope \+ \":\" \+ id, fact, asset\)"),
        ("kill fact uses actual owner identity", coop, "internal static bool ReportCoopCampaignKill(",
         r"\}, BossRushCoop.GetPlayerId\(killer\)\);"),
        ("kill targets only its living owner", coop, "private bool DeliverCoopCampaignFact(",
         r'if \(fact.Kind == "kill" && \(string.IsNullOrEmpty\(recipient\) \|\| player.Id != recipient \|\| !player.IsAlive\)\) continue;'),
        ("new slot cannot earn for old participant", coop, "private bool DeliverCoopCampaignFact(",
         r'if \(fact.Kind == "kill" && asset != player.AssetId\) continue;'),
        ("local tracking requires actual admitted asset", coop, "private bool IsCoopCampaignTrackingCurrent(",
         r"if \(!TryGetCoopCampaignParticipant\(mode, scope, BossRushCoop.LocalPlayerId, out participantAsset\)\s*\|\| participantAsset != BossRushCoop.LocalAssetId\)\s*\{\s*CampaignObjectiveTracker.ResetSession\(\);\s*return false;"),
        ("final freezes actual entrants before gameplay", coop, "private void BeginCoopCampaignFinalEntry(",
         r"coopCampaignFinalEntry = campaignFinalBossRunId;[\s\S]*?if \(player.IsConnected && player.IsAlive && !string.IsNullOrEmpty\(player.AssetId\)\)\s*coopCampaignFinalParticipants\[player.Id\] = player.AssetId;"),
        ("collector resolves actual summoned and hired owner", "Campaign/CampaignObjectiveCollector.cs", "private static CharacterMainControl ResolveKillOwner(",
         r"AttributeModeEHiredBossKillToOwner\(ref info\);[\s\S]*?FrostmourneAction.TryGetSummonOwner\(actor, out owner\) \|\| SummonStaffAction.TryGetSummonOwner\(actor, out owner\)"),
        ("pet kill belongs to its master", "Campaign/CampaignObjectiveCollector.cs", "private static CharacterMainControl ResolveKillOwner(",
         r"owner = companion != null \? companion.Master : null;"),
        ("receipt consumes only frozen original lease", coop, "private bool ApplyCoopCampaignFact(",
         r"CoopCampaignLeaseKey\(fact.RunId, fact.SceneToken, fact.Scope, BossRushCoop.LocalAssetId\)[\s\S]*?CampaignObjectiveTracker.ReportCoopFactLease\(lease, fact\)"),
        ("asset gate before original fact write", tracker, "internal static bool ReportCoopFactLease(",
         r"if \(value.Asset != BossRushCoop.LocalAssetId\) return false;"),
        ("original contract revision gate", tracker, "internal static bool ReportCoopFactLease(",
         r"revision != value.ContractRevision \|\| CampaignProgressService.GetActiveChapterId\(\) != value.Chapter"),
        ("original fact dedup before counters", tracker, "internal static bool ReportCoopFactLease(",
         r"if \(value.Facts.Add\(fact.FactId\)\)[\s\S]*?value.BountyFactKills\+\+"),
        ("world and personal evidence use maximum", tracker, "internal static bool ReportCoopFactLease(",
         r"item.Current = Math.Max\(item.Current, Math.Min\(reached, item.Def.Threshold\)\);"),
        ("original persistence failure retains receipt", tracker, "internal static bool ReportCoopFactLease(",
         r"if \(!CampaignProgressService.NotifyObjectivesSatisfied\(value.Chapter\)\) return false;"),
        ("bridge requires current scope before arming", bridge, "internal void TickCampaignModeBridge(",
         r"if \(!IsCoopCampaignTrackingCurrent\(mode\)\) return;[\s\S]*?CampaignObjectiveTracker.EnsureArmedFor\(mode\);\s*CaptureCoopCampaignTracking\(mode\);"),
        ("dead host preserves actual surviving world", bridge, "internal void TickCampaignModeBridge(",
         r"if \(string.IsNullOrEmpty\(mode\)\)[\s\S]*?if \(BossRushCoop.IsHost\) RefreshCoopCampaignSource\(ResolveCampaignWorldMode\(\)\);"),
        ("normal final entry freezes before async", "Campaign/CampaignFinalBoss.cs", "internal void StartCampaignFinalBoss()",
         r"int runId = \+\+campaignFinalBossRunId;[\s\S]*?BeginCoopCampaignFinalEntry\(\);\s*StartCampaignFinalBossPrologueThenSpawnAsync\(runId\).Forget\(\);"),
        ("Dev final entry freezes before async", "Campaign/CampaignFinalBoss.cs", "internal bool DebugStartCampaignFinalBossForValidation()",
         r"int runId = \+\+campaignFinalBossRunId;\s*BeginCoopCampaignFinalEntry\(\);\s*StartCampaignFinalBossAsync\(runId\).Forget\(\);"),
        ("scene change resets projection only", runtime, "public override void OnSceneLoaded(",
         r"CampaignObjectiveTracker.ResetSession\(\);\s*ResetCoopCampaignProjection\(\);"),
        ("runtime owner releases retained leases", runtime, "public override void OnDestroy()",
         r"CleanupCoopCampaignFacts\(\);"),
    ]
    errors = []
    for label, path, signature, pattern in checks:
        if not re.search(pattern, body(path, signature)):
            errors.append(label + ": " + path)
    receive = body(coop, "private bool ApplyCoopCampaignFact(")
    if "EnsureArmedFor" in receive or "fact.SceneGeneration != BossRushCoop.SceneGeneration" in receive:
        errors.append("receipt cannot rearm or discard earned original fact by current generation")
    capture = body(coop, "private void CaptureCoopCampaignTracking(")
    if "coopCampaignMode != mode" in capture or "coopCampaignScope" in capture:
        errors.append("real entry cannot depend on first Campaign world snapshot")
    for path, field in (("ModeD/ModeDRuntimeModule_Coop.cs", "coopEpoch"), ("ModeE/ModeERuntimeModule_Coop.cs", "coopEpoch"),
                        ("ModeF/ModeFRuntimeModule_Coop.cs", "coopEpoch"), ("WavesArena/WavesArenaRuntimeModule_Coop.cs", "coopArenaEpoch")):
        if not re.fullmatch(r"\{\s*get\s*\{\s*return " + field + r";\s*\}\s*\}", body(path, "internal string CampaignEntryEpoch")):
            errors.append("real original mode epoch getter: " + path)
    fixture = (ROOT / "tests/fixtures/CampaignPlayability/run.py").read_text(encoding="utf-8-sig")
    for path in (coop, "Utilities/CoopPersonalDelivery.cs", "Common/Coop/BossRushCoopJson.cs"):
        if '"' + path + '"' not in fixture:
            errors.append("complete production fixture link: " + path)
    for error in errors:
        print(error)
    print("CampaignCoopFactLeaseGuard: " + ("FAIL" if errors else "PASS"))
    return bool(errors)


if __name__ == "__main__":
    raise SystemExit(main())
