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
    ))
    fields = '\n'.join(re.findall(r'^        private (?:CharacterMainControl|bool|int|GameObject|float) campaign\w+(?: = -1)?;', boss, re.M))
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
    sources = [ROOT / name for name in (
        "Campaign/CampaignModels.cs", "Campaign/CampaignTuning.cs",
        "Campaign/CampaignQuestTable.cs", "Campaign/CampaignBaseObjectives.cs",
        "Campaign/CampaignContentCatalog.cs", "Campaign/CampaignObjectiveTracker.cs",
        "Campaign/CampaignObjectiveCollector.cs", "Campaign/CampaignFacilityUnlocks.cs",
        "Campaign/CampaignModeBridge.cs", "Campaign/CampaignNoteBridge.cs",
        "Campaign/CampaignRuntimeModuleHostBridge.cs",
        "ModeF/ModeFRuntimeModule_BountyLatch.cs",
        "Common/Data/BossRushJsonValue.cs", "Utilities/SimpleJsonHelper.cs",
        "ModeH/ModeHCanonicalDigest.cs", "ModeH/ModeHSeedStream.cs",
    )] + [HERE / "Program.cs", HERE / "Stubs.cs", HERE / "FinalBossRegression.cs", extracted]
    production = [p for p in sources if p.is_relative_to(ROOT) and not p.is_relative_to(HERE) and p != extracted]
    production += [ROOT / "Campaign/CampaignFinalBoss.cs", ROOT / "Campaign/CampaignDialoguePlayer.cs", ROOT / "Campaign/CampaignRuntimeModule.cs"]
    (OUT / "production-source-sha256.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in production}, indent=2), encoding="utf-8")
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    project += '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    project += '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
    project += '</PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project += '</ItemGroup></Project>'
    path = OUT / "CampaignPlayability.csproj"
    path.write_text(project, encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(path), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
