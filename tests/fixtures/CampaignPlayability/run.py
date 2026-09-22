"""Execute the production campaign catalog and objective collection without the game."""
from pathlib import Path
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
    # Compile the current final-boss orchestration, substituting only its Task carrier.
    # Selected methods contain no brace literals; clean_source removes comments first.
    boss = clean_source((ROOT / "Campaign/CampaignFinalBoss.cs").read_text(encoding="utf-8-sig"))
    members = extract_methods(boss, (
        "internal bool CanStartCampaignFinalBoss()", "private bool ShouldCampaignFinalBossAltarExist()",
        "private async UniTask StartCampaignFinalBossPrologueThenSpawnAsync(int runId)",
        "private async UniTask StartCampaignFinalBossAsync(int runId)",
        "private void OnCampaignFinalBossDead(DamageInfo damageInfo)",
        "internal void TickCampaignFinalBossYield()", "internal void CleanupCampaignFinalBoss(bool destroyBoss)",
        "private void ResetCampaignFinalBossTracking()",
    ))
    dialogue = clean_source((ROOT / "Campaign/CampaignDialoguePlayer.cs").read_text(encoding="utf-8-sig"))
    cancellation = extract_methods(dialogue, (
        "internal static void InvalidatePlayback()", "private static CancellationToken PlaybackToken()",
    ))
    extracted = OUT / "FinalBossProduction.cs"
    extracted.write_text("using System; using System.Threading; using System.Threading.Tasks; using UnityEngine; "
                         + "namespace BossRush { public partial class ModBehaviour {"
                         + members + "} internal static partial class CampaignDialoguePlayer {"
                         + cancellation + "}}", encoding="utf-8")
    sources = [ROOT / name for name in (
        "Campaign/CampaignModels.cs", "Campaign/CampaignTuning.cs",
        "Campaign/CampaignQuestTable.cs", "Campaign/CampaignBaseObjectives.cs",
        "Campaign/CampaignContentCatalog.cs", "Campaign/CampaignObjectiveTracker.cs",
        "Campaign/CampaignObjectiveCollector.cs", "Campaign/CampaignFacilityUnlocks.cs",
        "Campaign/CampaignModeBridge.cs", "Campaign/CampaignNoteBridge.cs",
        "Common/Data/BossRushJsonValue.cs", "Utilities/SimpleJsonHelper.cs",
        "ModeH/ModeHCanonicalDigest.cs", "ModeH/ModeHSeedStream.cs",
    )] + [HERE / "Program.cs", HERE / "Stubs.cs", HERE / "FinalBossRegression.cs", extracted]
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
