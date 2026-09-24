"""Run production release eligibility, compatibility and opponent/bell handover methods."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ModeHPlayerFlow"
sys.path.insert(0, str(ROOT / "tests"))
from ModeHOneClickFlowGuard import method_body


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hashes = {}

    def extract(path, signature):
        source = ROOT / path
        hashes[path] = hashlib.sha256(source.read_bytes()).hexdigest()
        result = method_body(source.read_text(encoding="utf-8-sig"), signature)
        if result is None:
            raise AssertionError("Missing unique production method: " + signature)
        return result

    cert = "ModeH/ModeHProductionCertification.cs"
    methods = [extract(cert, signature) for signature in (
        "internal bool TryUseReleaseCatalog()", "private static bool IsReleaseControlPointAvailable(",
        "internal static bool PassesStaticAudit(", "private ModeHProductionCertificationDto BuildReport()",
        "private bool EvaluateThreshold(", "private List<ModeHCommandCertificationStatusDto> BuildCommandStatuses(",
        "private void AppendEntryStatus(")]
    code = "using System; using System.Collections; using System.Collections.Generic; using UnityEngine; namespace BossRush { partial class ReleaseCatalog {\n"
    code += "\n".join(methods) + "\n} partial class ArenaWake {\n"
    code += extract("ModeH/ModeHCombatControl.cs", "private static void WakeArenaOpponent(")
    code += "\n} partial class BellLease {\n"
    code += extract("ModeH/ModeHSpectatorLease.cs", "public void StartAcceptingBell()")
    code += extract("ModeH/ModeHSpectatorLease.cs", "public void StopAcceptingBell()")
    code += "\n} partial class CommandDescriptions {\n"
    code += extract("ModeH/ModeHRuntimeModule_LoadoutEditing.cs", "private static string DescribeCommand(")
    code += extract("ModeH/ModeHRuntimeModule_LoadoutEditing.cs", "private static string DescribeControlPoint(") + "\n}}"
    scene = "ModeH/ModeHRuntimeModule_SceneFlow.cs"
    runner = "DebugAndTools/F3GameplayValidationRunner.cs"
    code += "\nnamespace BossRush { partial class ModeHRuntimeModule {\n"
    code += "\n".join(extract(scene, signature) for signature in (
        "private void StartCertification()", "internal bool CanRunCertificationFromF3(",
        "internal bool StartCertificationFromF3(", "private IEnumerator DriveCertification(",
        "private void RestorePlayerFlowAfterCertification(", "private void CancelSetupFromDiagnostics()",
        "private bool IsCallbackStillValid(", "private static string DescribeCertificationProgress("))
    code += "\n} partial class ModBehaviour {\n" + extract(runner, "private void StartModeHCertificationFromF3()")
    code += "\n} static partial class F3GameplayValidationRunner {\n" + extract(runner, "internal static bool CanRunModeHCertification(") + "\n}}"
    generated = OUT / "ProductionMethods.cs"
    generated.write_text(code, encoding="utf-8")
    files = [HERE / "Program.cs", HERE / "FlowHarness.cs", generated] + [ROOT / p for p in (
        "Common/Data/BossRushJsonValue.cs", "Common/Data/JsonDataRegistry.cs", "Utilities/SimpleJsonHelper.cs",
        "ModeH/ModeHConfig.cs", "ModeH/ModeHStateModel.cs", "ModeH/ModeHStateDtos.cs", "ModeH/ModeHContentModels.cs",
        "ModeH/ModeHCanonicalDigest.cs", "ModeH/ModeHContentCatalog.cs", "ModeH/ModeHContentCatalogParsers.cs",
        "ModeH/ModeHProfileRegistry.cs", "ModeH/ModeHCommandCompatibilityRegistry.cs", "DebugAndTools/ValidationCoroutineStack.cs")]
    includes = "\n".join('<Compile Include="%s" />' % escape(str(p)) for p in files)
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><AssemblyName>Assembly-CSharp</AssemblyName>'
        '<LangVersion>7.3</LangVersion><RollForward>Major</RollForward><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
        '<NoWarn>0649</NoWarn><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>'
        + includes + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "source-hashes.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release", "--", str(ROOT)], cwd=ROOT)


if __name__ == "__main__":
    sys.exit(main())
