"""Compile current H room coordination methods and the complete personal ledger."""
from pathlib import Path
import hashlib
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
import argparse
parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path, default=ROOT / "Build/runtime-regressions/ModeHCoop")
OUT = parser.parse_args().output.resolve()
OUT.mkdir(parents=True, exist_ok=True)
source = ROOT / "ModeH/ModeHRuntimeModule_Coop.cs"

def block(signature):
    text = source.read_text(encoding="utf-8-sig")
    start = text.index(signature)
    opening = text.index("{", start)
    depth, end = 1, opening + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]

def other(path,signature):
    text=(ROOT/path).read_text(encoding="utf-8-sig")
    assert text.count(signature)==1,signature
    start=text.index(signature);opening=text.index("{",start);depth=0
    for i in range(opening,len(text)):
        depth+=(text[i]=="{")-(text[i]=="}")
        if depth==0:return text[start:i+1]
    raise AssertionError(signature)

code = "using System; using System.Collections.Generic; using System.Threading.Tasks; using UnityEngine; namespace BossRush {\n"
code += "[Serializable] " + block("internal sealed class ModeHCoopResult")
code += block("internal sealed class ModeHCoopFighter") + block("internal sealed class ModeHCoopWorld")
code += "partial class ModeHRuntimeModule {\n"
for signature in ("private bool IsCoopSkipped(", "private string[] CaptureCoopSkipped(", "private void StartCoopWithConfirmedPlayers(", "private void QueueCoopSkippedRefunds(", "private string CurrentCoopRound(", "private void RefreshCoopRound(",
                  "private void TickCoop(", "private void ExecuteCoopCommand(",
                  "private bool RequestCoopStart(", "private bool CoopRosterLocked",
                  "private bool RequestCoopReroll(", "private bool CoopParticipantsSceneReady(", "private bool HasPendingCoopParticipantResults(",
                  "private string FundedAsset(", "private bool IsCoopFunded(", "private void RefreshCoopParticipantAssets(", "private static bool Contains("):
    code += block(signature) + "\n"
code += block("private void TryPrepareCoopBet(") + "\n"
for signature in ("private bool IsCoopProjectionSceneCurrent", "private void ReleaseCoopProjection()", "private void BindCoopWorldAsset()", "private bool IsCoopWorldAuthorityCurrent", "private bool CheckCoopWorldAuthority()", "private bool IsCoopWorldPersonalAssetCurrent", "private bool CanUseCoopNativePersonalSeason", "private bool RetainCoopWorldOnSlotChange()", "private bool ApplyCoopResult(", "private void EnsureCoopHostSpectator()"):
    code += block(signature) + "\n"
code += other("ModeH/ModeHRuntimeModule_GroupFlow.cs", "private void StartGroupMatch()").replace("private void StartGroupMatch()", "private void ActualStartGroupMatch()") + "\n"
code += other("ModeH/ModeHRuntimeModule_GroupFlow.cs", "private bool IsGroupRosterReadyForCurrentMatch()") + "\n"
code += other("ModeH/ModeHRuntimeModule_UiFlow.cs", "private bool TryPersistSeason(string reasonId)") + "\n"
code += other("ModeH/ModeHRuntimeModule.cs", "internal bool HasActiveRun") + "\n"
code += other("ModeH/ModeHRuntimeModule.cs", "internal void RequestExit(") + "\n"
code += other("ModeH/ModeHRuntimeModule.cs", "internal bool TryTransition(") + "\n"
code += other("ModeH/ModeHRuntimeModule_SceneFlow.cs", "private bool IsCallbackStillValid(") + "\n"
code += other("ModeH/ModeHRuntimeModule_SceneFlow.cs", "partial void ShutdownRuntimeInternal(") + "\n"
code += other("ModeH/ModeHRuntimeModule_SceneFlow.cs", "private void TryReturnRealStakeOnAbort(") + "\n"
code += other("ModeH/ModeHRuntimeModule_Recovery.cs", "private bool IsSeasonResumeRequestCurrent(") + "\n"
code += other("ModeH/ModeHRuntimeModule_Recovery.cs", "private void BeginSeasonResumeScene()") + "\n"
code += other("ModeH/ModeHRuntimeModule_Recovery.cs", "private void CancelSeasonResume()") + "\n"
code += other("ModeH/ModeHRuntimeModule_GroupFlow.cs", "private sealed class GroupCustomTicket") + "\n"
code += other("ModeH/ModeHRuntimeModule_GroupFlow.cs", "private async UniTask PrepareGroupCustomUnitAsync(").replace("async UniTask", "async Task") + "\n"
code += block("private void PublishCoopResult(") + "\n"
code += other("ModeH/ModeHRuntimeModule.cs", "private void RestoreForSlotChange()") + "\n"
code += other("ModeH/ModeHRuntimeModule_UiFlow.cs", "private bool TryPersistSeason(string reasonId, bool requireDurable)") + "\n"
code += "}}"
(OUT / "Production.cs").write_text(code, encoding="utf-8")
sources = [HERE / "Program.cs", HERE / "WorldOwner.cs", HERE / "CachedScene.cs", OUT / "Production.cs", ROOT / "ModeH/ModeHCoopLedger.cs", ROOT / "Common/Coop/BossRushCoopJson.cs", ROOT / "Common/Data/BossRushJsonValue.cs", ROOT / "Utilities/SimpleJsonHelper.cs"]
project = OUT / "Regression.csproj"
includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
(OUT / "source-hashes.txt").write_text("\n".join(str(p) + " " + hashlib.sha256(p.read_bytes()).hexdigest() for p in [source, ROOT / "ModeH/ModeHCoopLedger.cs", ROOT / "ModeH/ModeHRuntimeModule.cs", ROOT / "ModeH/ModeHRuntimeModule_SceneFlow.cs", ROOT / "ModeH/ModeHRuntimeModule_Recovery.cs", ROOT / "ModeH/ModeHRuntimeModule_GroupFlow.cs"]), encoding="utf-8")
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture
code, output = run_project_fixture(project, OUT / "runs", ROOT)
print(output)
raise SystemExit(code)
