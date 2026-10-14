"""Execute current Mode F personal ownership, ordering, extraction and snapshot methods."""
from pathlib import Path
import hashlib
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ModeFCoop"
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source

SOURCE = ROOT / "ModeF/ModeFRuntimeModule_Coop.cs"
source = clean_source(SOURCE.read_text(encoding="utf-8-sig"))

def block(signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]

OUT.mkdir(parents=True, exist_ok=True)
code = ["using System; using System.Collections.Generic; using UnityEngine; using ItemStatsSystem; namespace BossRush {"]
code += [block(signature) for signature in ("internal sealed class CoopModeFPlayerState", "internal sealed class CoopModeFState", "internal sealed class CoopModeFPersonal")]
code += ["internal partial class ModeFRuntimeModule {"]
code += [block(signature) for signature in (
    "private void RetireLostCoopProjection(", "private sealed class CoopModeFRewardDebt", "private sealed class CoopModeFLocalLedger",
    "private void PreserveCoopModeFLocalLedger(", "private bool IsCoopModeFLocalAdmitted(", "private bool TryStoreCoopModeFRewards(",
    "private static bool IsCoopModeFRewardBuffered(",
    "private void BeginCoopModeF(", "private void TickCoopModeF(", "private void ApplyCoopModeF(",
    "private bool ApplyCoopModeFPersonal(", "private void DeliverCoopModeFKill(",
    "private int GetCoopModeFPlayerMarks(", "private static bool IsCoopModeFParticipant(",
    "private bool CoopModeFAnyParticipantAlive(", "private void CompleteCoopModeFExtraction(", "private void ExecuteCoopModeF(")]
code += ["} internal partial class ModeERuntimeModule {"]
f_source = source
source = clean_source((ROOT / "ModeE/ModeERuntimeModule_Coop.cs").read_text(encoding="utf-8-sig"))
code += [block(signature) for signature in ("internal bool CommitCoopModeEntry(", "private void TickCoopEntryRefunds(", "internal bool ReopenCoopEntryDecision(", "private void ExecuteCoopModeE(", "private bool IsCoopModeEParticipant(")]
code += ["} internal partial class ModeFRuntimeModule {"]
source = clean_source((ROOT / "ModeF/ModeFFortifications_RepairRewardsCleanup.cs").read_text(encoding="utf-8-sig"))
code += [block("private static List<int> GetModeFKillRewardTypeIds(")]
source = clean_source((ROOT / "ModeF/ModeFExtraction.cs").read_text(encoding="utf-8-sig"))
code += [block("internal void OnModeFExtractionSuccess(")]
code += ["}}"]
generated = OUT / "Production.cs"
generated.write_text("\n".join(code), encoding="utf-8")
(OUT / "production-sha256.txt").write_text("\n".join(str(path) + " " + hashlib.sha256(path.read_bytes()).hexdigest() for path in (SOURCE, ROOT / "ModeE/ModeERuntimeModule_Coop.cs")), encoding="utf-8")
paths = [generated, HERE / "Stubs.cs", HERE / "Program.cs", ROOT / "Common/Coop/BossRushCoopJson.cs", ROOT / "Common/Data/BossRushJsonValue.cs", ROOT / "Utilities/SimpleJsonHelper.cs"]
project = OUT / "Regression.csproj"
includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture
code, output = run_project_fixture(project, OUT / "runs", ROOT)
print(output)
raise SystemExit(code)
