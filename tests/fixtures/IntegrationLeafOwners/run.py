"""Execute production nurse, ticket, mutator and managed-character leaf owners."""
from pathlib import Path
import hashlib
import json
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/IntegrationLeafOwners"

import sys
sys.path.insert(0, str(ROOT / "tests"))
from integration_host_source import materialize_host


def block(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    linked = [ROOT / path for path in (
        "Integration/NPCs/Nurse/NurseNPC.cs",
        "Integration/Mutators/MutatorModeFlow.cs",
        "Integration/ModeGManagedCharacterService.cs",
    )]
    linked += [materialize_host(ROOT, OUT / "NurseHost.cs", "NurseNPCRuntimeModuleHostBridge", "using UnityEngine;"),
               materialize_host(ROOT, OUT / "MutatorHost.cs", "MutatorRuntimeBridge")]
    registry_path = ROOT / "Integration/BossRushDynamicItemRegistry.cs"
    init_path = ROOT / "Integration/BossRushIntegrationRuntimeModule_Initialization.cs"
    host_path = ROOT / "Integration/BossRushIntegration.cs"
    registry = block(registry_path.read_text(encoding="utf-8-sig"), "internal sealed partial class IntegrationRuntimeModule")
    initializer = block(init_path.read_text(encoding="utf-8-sig"), "internal void InitializeDynamicItems_Integration()")
    bridges = "\n".join(block(host_path.read_text(encoding="utf-8-sig"), "internal bool " + name + "()") for name in (
        "EnsureItemContentConfiguratorsRegisteredForDynamicRegistry", "EnsureBossRushTicketItemRegisteredForDynamicRegistry",
        "EnsureBirthdayCakeItemRegisteredForDynamicRegistry", "EnsureAdventureJournalItemRegisteredForDynamicRegistry"))
    generated = OUT / "TicketProduction.cs"
    generated.write_text("using System; using System.IO; using UnityEngine; using ItemStatsSystem; using BossRush.Utils; namespace BossRush {\n" + registry + "\ninternal sealed partial class IntegrationRuntimeModule {\n" + initializer + "\n}\npublic partial class ModBehaviour {\n" + bridges + "\n}}", encoding="utf-8")
    sources = linked + [generated, HERE / "Program.cs", HERE / "Stubs.cs"]
    includes = "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>' +
                       includes + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "production-sha256.json").write_text(json.dumps({path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
                                                          for path in linked + [registry_path, init_path, host_path]}, indent=2), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
