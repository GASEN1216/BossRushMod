"""Link real Tidegate persistence, note mirror and Dev reset without game/save I/O."""
from pathlib import Path
import hashlib
import json
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/TidegateStoryTransactions"
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for i in range(opening, len(source)):
        depth += (source[i] == "{") - (source[i] == "}")
        if depth == 0:
            return source[start:i + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hooks = ROOT / "Tidegate/TidegateDrillHooks.cs"
    # Extract the complete owned partial, preserving all Dev reset production code.
    raw = hooks.read_text(encoding="utf-8-sig")
    partial = member(raw, "internal static partial class TidegateStory")
    shared = ROOT / "Utilities/OfficialQuests/OfficialQuestItems.cs"
    source = clean_source(shared.read_text(encoding="utf-8-sig"))
    extracted = "using System; using System.Collections.Generic; using ItemStatsSystem; using ItemStatsSystem.Data; using Saves; using UnityEngine; namespace BossRush {\n"
    extracted += partial + "\ninternal static class OfficialQuestItems {\n"
    for signature in ("internal static bool CanCollectAssets(bool inboxOnly)", "internal static Func<bool> AssetCollector(bool inboxOnly)"):
        extracted += member(source, signature) + "\n"
    extracted += "}\ninternal sealed partial class TidegateDevices {\n"
    devices = ROOT / "Tidegate/TidegateDevices.cs"
    device_source = devices.read_text(encoding="utf-8-sig")
    for signature in ("internal int OpenValves", "private void TurnValve(int index)", "private bool DevBypassHandwheel()", "private static string ValveName(int index)"):
        extracted += member(device_source, signature) + "\n"
    extracted += "}}\n"
    generated = OUT / "Extracted.cs"
    generated.write_text(extracted, encoding="utf-8")
    paths = [ROOT / p for p in (
        "Tidegate/TidegateStory.cs", "Tidegate/TidegateNoteBridge.cs",
        "Common/Lifecycle/BossRushSlotJsonStore.cs", "Common/Lifecycle/BossRushSaveCoordinatorEngine.cs",
        "Common/Lifecycle/BossRushSaveFileThrottle.cs", "Common/Data/BossRushJsonValue.cs",
        "Utilities/SimpleJsonHelper.cs", "Config/ConfigItemIds.cs")]
    linked = paths + [generated, HERE / "Program.cs", HERE / "Stubs.cs", HERE / "ValveCases.cs"]
    project = OUT / "TidegateStoryTransactions.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0067</NoWarn>'
        '</PropertyGroup><ItemGroup>' + ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '"/>' for p in linked)
        + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "production-sha256.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in paths + [hooks, shared, devices]}, indent=2) + "\n", encoding="utf-8")
    sys.path.insert(0, str(ROOT / "tools"))
    from run_runtime_regressions import run_project_fixture
    code, output = run_project_fixture(project, OUT, ROOT)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
