"""Execute the exact arena entry owner, complete DEMO setup and real Mode D activation."""
from pathlib import Path
import hashlib
import json
import os
import sys
from xml.sax.saxutils import escape

ROOT = Path(os.environ.get("BOSSRUSH_FIXTURE_SOURCE_ROOT", Path(__file__).resolve().parents[3]))
HERE = Path(__file__).resolve().parent
OUT = Path(os.environ.get("BOSSRUSH_FIXTURE_OUT", ROOT / "Build/runtime-regressions/LegacyArenaEntry"))
sys.path.insert(0, str(ROOT / "tests/fixtures/ModeDEntryOwnership"))
from run import member
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    entry = ROOT / "WavesArena/BossRushEntryFlow.cs"
    host = ROOT / "Integration/BossRushIntegration.cs"
    map_objects = ROOT / "Integration/BossRushIntegrationRuntimeModule_MapObjects.cs"
    lifecycle = ROOT / "Integration/BossRushIntegrationRuntimeModule.cs"
    mode_d = ROOT / "ModeD/ModeD.cs"
    generated = OUT / "EntryBridges.cs"
    source = entry.read_text(encoding="utf-8-sig")
    code = "using System; using System.Collections; using UnityEngine; using UnityEngine.SceneManagement; using ItemStatsSystem; namespace BossRush { public partial class ModBehaviour {\n"
    code += "\n".join(member(source, s) for s in (
        "private enum BossRushEntryMode", "private BossRushEntryMode DetermineBossRushEntryMode(string context)",
        "private IEnumerator SetupBossRushInDemoChallenge(Scene scene)"))
    code += member(host.read_text(encoding="utf-8-sig"), "internal void StartBossRushDemoChallengeSetupForScene(Scene scene)")
    code += "\n".join(member(mode_d.read_text(encoding="utf-8-sig"), s) for s in (
        "public bool TryStartModeD()", "public void StartModeD()"))
    code += "} internal sealed partial class IntegrationRuntimeModule {\n"
    code += member(map_objects.read_text(encoding="utf-8-sig"), "internal System.Collections.IEnumerator WaitForLevelInitializedThenSetup_Integration(Scene scene)")
    code += "\n".join(member(lifecycle.read_text(encoding="utf-8-sig"), s) for s in (
        "public override void OnAwake(ModBehaviour owner)", "public override void OnDestroy()",
        "internal bool ReadSceneLoaderDoneWithWarning(string context)", "internal bool ReadMainExistsWithWarning(string context)",
        "internal bool ReadCameraExistsWithWarning(string context)", "internal bool ReadLevelInitedWithWarning(string context)"))
    generated.write_text(code + "}}", encoding="utf-8")
    production = [ROOT / "Integration/BossRushIntegrationRuntimeModule_ArenaEntry.cs",
                  ROOT / "ModeD/ModeDRuntimeModule.cs", ROOT / "ModeD/ModeDRuntimeModule_Lifecycle.cs"]
    paths = production + [generated, HERE / "Program.cs", HERE / "Stubs.cs"]
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414;0067</NoWarn>'
                       '</PropertyGroup><ItemGroup>'
                       + ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
                       + '</ItemGroup></Project>', encoding="utf-8")
    evidence = production + [entry, host, map_objects, lifecycle, mode_d]
    (OUT / "production-sha256.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                                                            for p in evidence}, indent=2), encoding="utf-8")
    result, output = run_project_fixture(project, OUT, ROOT)
    print(output)
    return result


if __name__ == "__main__":
    raise SystemExit(main())
