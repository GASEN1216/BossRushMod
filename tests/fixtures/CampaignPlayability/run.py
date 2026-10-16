"""Execute the production campaign catalog and objective collection without the game."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from cs_source_util import clean_source
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build" / "campaign-playability"


def extract_methods(source, signatures):
    members = []
    for signature in signatures:
        start = source.index(signature)
        end = source.index("{", start) + 1
        depth = 1
        while depth:
            depth += (source[end] == "{") - (source[end] == "}")
            end += 1
        members.append(source[start:end].replace("async UniTask", "async Task"))
    return "\n".join(members)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    # Compile the current final-boss orchestration in its real module owner,
    # substituting only its Task carrier. Link the complete production host bridge.
    # Selected methods contain no brace literals; clean_source removes comments first.
    boss = clean_source((ROOT / "Campaign/CampaignFinalBoss.cs").read_text(encoding="utf-8-sig"))
    members = extract_methods(boss, (
        "internal bool CanStartCampaignFinalBoss()", "private bool ShouldCampaignFinalBossAltarExist()",
        "internal void StartCampaignFinalBoss()", "internal bool DebugStartCampaignFinalBossForValidation()",
        "private bool IsCampaignArenaSceneCached()", "private bool IsAnyGameplayModeActiveForCampaign()",
        "internal void TickCampaignFinalBossAltar()", "private void CreateCampaignFinalBossAltar(Vector3 position)",
        "private async UniTask StartCampaignFinalBossPrologueThenSpawnAsync(int runId)",
        "private async UniTask StartCampaignFinalBossAsync(int runId)",
        "private void OnCampaignFinalBossDead(DamageInfo damageInfo)",
        "internal void TickCampaignFinalBossYield()", "internal void CleanupCampaignFinalBoss(bool destroyBoss)",
        "private void ResetCampaignFinalBossTracking()",
        "private void CaptureCampaignFinalScope()", "private bool IsCampaignFinalScopeValid()", "private void TickCampaignFinalScope()",
    ))
    fields = '\n'.join(re.findall(r'^        private (?:CharacterMainControl|bool|int|GameObject|float|string) campaign\w+(?:, campaign\w+)*(?: = -1)?;', boss, re.M))
    properties = extract_methods(boss, (
        "internal bool IsCampaignFinalBossActive", "internal int CampaignFinalBossDeathPresentationCount",
        "internal CharacterMainControl CampaignFinalBossInstanceForValidation",
    ))
    dialogue = clean_source((ROOT / "Campaign/CampaignDialoguePlayer.cs").read_text(encoding="utf-8-sig"))
    cancellation = extract_methods(dialogue, (
        "internal static void InvalidatePlayback()", "private static CancellationToken PlaybackToken()",
    ))
    lifecycle_source = clean_source((ROOT / "Campaign/CampaignRuntimeModule.cs").read_text(encoding="utf-8-sig"))
    guide_poll_fields = re.findall(r'^        private float _guidePollRemaining(?:\s*=[^;]+)?;', lifecycle_source, re.M)
    if len(guide_poll_fields) != 1:
        raise RuntimeError("Expected the production campaign guide polling field exactly once")
    fields += "\n" + guide_poll_fields[0]
    lifecycle = extract_methods(lifecycle_source, (
        "public override void OnSceneLoaded(SceneRuntimeContext context)",
        "public override void OnDestroy()",
    ))
    extracted = OUT / "FinalBossProduction.cs"
    extracted.write_text("using System; using System.Threading; using System.Threading.Tasks; using UnityEngine; "
                         + "namespace BossRush { internal sealed partial class CampaignRuntimeModule {"
                         + fields + properties + members + lifecycle + "} internal static partial class CampaignDialoguePlayer {"
                         + cancellation + "}}", encoding="utf-8")
    entry_source = clean_source((ROOT / "WavesArena/BossRushEntryFlow.cs").read_text(encoding="utf-8-sig"))
    entry = extract_methods(entry_source, ("private IEnumerator SetupBossRushInDemoChallenge(Scene scene)",))
    entry_enum = re.search(r"private enum BossRushEntryMode\s*\{[^}]+\}", entry_source).group(0)
    signs_source = clean_source((ROOT / "UIAndSigns/UIAndSigns.cs").read_text(encoding="utf-8-sig"))
    signs = extract_methods(signs_source, (
        "internal void TryCreateArenaDifficultyEntryPoint_UIAndSigns(Vector3? customPosition)",
        "private void CreateInvisibleEntryPointForChallengeSnow(Vector3? customPosition)",
    ))
    geometry_source = clean_source((ROOT / "Utilities/SpawnPositionHelper.cs").read_text(encoding="utf-8-sig"))
    geometry = extract_methods(geometry_source, (
        "internal static bool TryFindAroundPlayer(", "internal static bool TrySampleNavMesh(",
        "private static bool TryRaycastSnapPreserveXZ(", "internal static bool PassesMinPlayerDistance(",
    ))
    geometry_constants = '\n'.join(re.findall(r'^        internal const float (?:DefaultLiftOffset|DefaultNavMeshSampleRadius|DefaultRaycastMaxDistance|DefaultRaycastOriginHeight) = [^;]+;', geometry_source, re.M))
    entry_extracted = OUT / "EntryProduction.cs"
    entry_extracted.write_text("using System; using System.Collections; using UnityEngine; using UnityEngine.SceneManagement; using UnityEngine.AI; "
                              + "namespace BossRush { public partial class ModBehaviour {" + entry_enum + entry
                              + "} internal sealed partial class UIAndSignsRuntimeModule {" + signs
                              + "} internal static partial class SpawnPositionHelper {" + geometry_constants + geometry + "}}", encoding="utf-8")
    f3_path = ROOT / "DebugAndTools/F3GameplayValidationDepth.cs"
    # Preserve the current Dev case body exactly; clocks, spawning and official Hurt are controlled boundaries.
    f3 = extract_methods(f3_path.read_text(encoding="utf-8-sig"), ("private IEnumerator RunCampaignFinalBoss()",))
    f3_extracted = OUT / "F3FinalBossProduction.cs"
    f3_extracted.write_text("using System; using System.Collections; using System.Diagnostics; using UnityEngine; "
                           + "namespace BossRush { internal sealed partial class F3GameplayValidationRunner {" + f3 + "}}", encoding="utf-8")
    # Keep only the unrelated offline prediction/channel boundaries. Campaign facts
    # execute the complete real delivery helper and codec against a recording transport.
    offline = clean_source((ROOT / "tests/fixtures/CoopOfflineStubs.cs").read_text(encoding="utf-8-sig"))
    for declaration in ("internal static partial class BossRushCoop", "internal sealed class BossRushCoopMessage", "internal sealed class CoopPersonalDelivery<T>"):
        start = offline.index(declaration)
        end = offline.index("{", start) + 1
        depth = 1
        while depth:
            depth += (offline[end] == "{") - (offline[end] == "}")
            end += 1
        offline = offline[:start] + offline[end:]
    offline_path = OUT / "OfflineBoundaries.cs"
    offline_path.write_text(offline, encoding="utf-8")
    entry_getters = OUT / "EntryEpochProduction.cs"
    getter_sources = (("ModeDRuntimeModule", "ModeD/ModeDRuntimeModule_Coop.cs", "coopEpoch"),
        ("ModeERuntimeModule", "ModeE/ModeERuntimeModule_Coop.cs", "coopEpoch"),
        ("ModeFRuntimeModule", "ModeF/ModeFRuntimeModule_Coop.cs", "coopEpoch"),
        ("CoopArenaEntryAdapter", "WavesArena/WavesArenaRuntimeModule_Coop.cs", "coopArenaEpoch"))
    getter_parts = []
    for owner_type, getter_path, field in getter_sources:
        source = clean_source((ROOT / getter_path).read_text(encoding="utf-8-sig"))
        getter = extract_methods(source, ("internal string CampaignEntryEpoch", "internal bool TryGetCoopCampaignParticipantAsset("))
        cohort, record = {"ModeDRuntimeModule": ("coopWaveParticipants", "string"),
            "ModeERuntimeModule": ("coopModeEParticipants", "CoopModeEParticipant"),
            "ModeFRuntimeModule": ("coopPlayers", "CoopModeFPlayerState"),
            "CoopArenaEntryAdapter": ("coopArenaWaveParticipants", "string")}[owner_type]
        if record != "string": getter_parts.append(extract_methods(source, ("internal sealed class " + record,)))
        value = "asset" if record == "string" else "new " + record + " { Id=id, AssetId=asset, Admitted=admitted, Declined=declined }"
        getter_parts.append("internal sealed partial class " + owner_type + " { private string " + field
            + "; private readonly Dictionary<string," + record + "> " + cohort + "=new Dictionary<string," + record + ">();"
            + " internal void SetParticipantForTest(string id,string asset,bool admitted=true,bool declined=false) { " + cohort + "[id]=" + value + "; }"
            + " internal void SetEpochForTest(string value) { " + field + "=value; } " + getter + " }")
    zombie_source = clean_source((ROOT / "ZombieMode/ZombieModeRuntimeModule_Coop.cs").read_text(encoding="utf-8-sig"))
    getter_parts.append("internal sealed class ZombieModeRuntimeModule { internal ZombieModeRunState runState;"
        + "internal readonly Dictionary<string,string> coopZombieParticipants=new Dictionary<string,string>();"
        + "internal readonly HashSet<string> coopZombieStarterReady=new HashSet<string>();"
        + extract_methods(zombie_source, ("internal bool TryGetCoopCampaignParticipantAsset(",)) + "}")
    entry_getters.write_text("using System; using System.Collections.Generic; namespace BossRush {" + "".join(getter_parts) + "}", encoding="utf-8")
    sources = [offline_path, entry_getters] + [ROOT / name for name in (
        "Campaign/CampaignModels.cs", "Campaign/CampaignTuning.cs", "Campaign/CampaignRuntimeModule_Coop.cs",
        "Utilities/CoopPersonalDelivery.cs", "Common/Coop/BossRushCoopJson.cs",
        "Campaign/CampaignQuestTable.cs", "Campaign/CampaignBaseObjectives.cs",
        "Campaign/CampaignContentCatalog.cs", "Campaign/CampaignObjectiveTracker.cs",
        "Campaign/CampaignObjectiveCollector.cs", "Campaign/CampaignFacilityUnlocks.cs",
        "Campaign/CampaignModeBridge.cs", "Campaign/CampaignNoteBridge.cs",
        "Campaign/CampaignRuntimeModuleHostBridge.cs",
        "ModeF/ModeFRuntimeModule_BountyLatch.cs",
        "Common/Data/BossRushJsonValue.cs", "Utilities/SimpleJsonHelper.cs",
        "ModeH/ModeHCanonicalDigest.cs", "ModeH/ModeHSeedStream.cs",
    )] + [HERE / "Program.cs", HERE / "Stubs.cs", HERE / "FinalBossRegression.cs", HERE / "EntryRegression.cs", HERE / "F3FinalBossRegression.cs", HERE / "CoopFactsBoundary.cs", HERE / "CoopFactsRegression.cs", HERE / "CoopAttribution.cs", extracted, entry_extracted, f3_extracted]
    production = [p for p in sources if p.is_relative_to(ROOT) and not p.is_relative_to(HERE) and p != extracted]
    production += [ROOT / "Campaign/CampaignFinalBoss.cs", ROOT / "Campaign/CampaignDialoguePlayer.cs", ROOT / "Campaign/CampaignRuntimeModule.cs"]
    production += [ROOT / "WavesArena/BossRushEntryFlow.cs", ROOT / "UIAndSigns/UIAndSigns.cs", ROOT / "Utilities/SpawnPositionHelper.cs"]
    production += [f3_path, ROOT / "Campaign/CampaignRuntimeModule_Coop.cs"]
    production += [ROOT / value[1] for value in getter_sources]
    (OUT / "production-source-sha256.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in production}, indent=2), encoding="utf-8")
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    project += '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    project += '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
    project += '</PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project += '</ItemGroup></Project>'
    path = OUT / "CampaignPlayability.csproj"
    path.write_text(project, encoding="utf-8")
    sys.path.insert(0, str(ROOT / "tools"))
    from run_runtime_regressions import run_project_fixture
    code, output = run_project_fixture(path, OUT / "execution", ROOT)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
