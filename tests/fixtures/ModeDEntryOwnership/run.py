"""Link real Mode D runtime/lifecycle and extract the exact public entry bridges."""
from pathlib import Path
import hashlib
import json
import os
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = Path(os.environ.get("BOSSRUSH_FIXTURE_OUT", str(ROOT / "Build/runtime-regressions/ModeDEntryOwnership")))


def member(source, signature):
    assert source.count(signature) == 1, "missing or duplicate production signature: " + signature
    start = source.index(signature)
    pos = source.index("{", start)
    depth = 0
    while pos < len(source):
        if source.startswith("//", pos):
            end = source.find("\n", pos)
            pos = len(source) if end < 0 else end
            continue
        if source.startswith("/*", pos):
            end = source.find("*/", pos + 2)
            assert end >= 0, "unclosed comment"
            pos = end + 2
            continue
        verbatim = source.startswith('@"', pos)
        if verbatim or source[pos] in ('"', "'"):
            quote = '"' if verbatim else source[pos]
            pos += 2 if verbatim else 1
            while pos < len(source):
                if not verbatim and source[pos] == "\\":
                    pos += 2
                    continue
                if source[pos] == quote:
                    if verbatim and source.startswith('""', pos):
                        pos += 2
                        continue
                    pos += 1
                    break
                pos += 1
            continue
        depth += (source[pos] == "{") - (source[pos] == "}")
        pos += 1
        if depth == 0:
            return source[start:pos]
    raise ValueError("unclosed production member: " + signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    host = ROOT / "ModeD/ModeD.cs"
    source = host.read_text(encoding="utf-8-sig")
    signatures = ["public bool TryStartModeD()", "public void StartModeD()", "public bool IsPlayerNaked()",
                  "private int GetBossRushTicketTypeId()",
                  "private bool IsPlayerNakedWithAllowedItems(string logTag, int allowedTypeIdA, int allowedTypeIdB, bool allowFactionFlags)"]
    generated = OUT / "HostBridges.cs"
    generated.write_text("namespace BossRush { public partial class ModBehaviour {\n"
                         + "\n".join(member(source, s) for s in signatures) + "\n} }\n", encoding="utf-8")
    production = [ROOT / "ModeD/ModeDRuntimeModule.cs", ROOT / "ModeD/ModeDRuntimeModule_Lifecycle.cs"]
    paths = production + [generated, HERE / "Program.cs", HERE / "Stubs.cs"]
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414;0067</NoWarn>'
                       '</PropertyGroup><ItemGroup>'
                       + ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
                       + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "production-sha256.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                                                           for p in production + [host]}, indent=2), encoding="utf-8")
    result = subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)
    entry = (ROOT / "WavesArena/BossRushEntryFlow.cs").read_text(encoding="utf-8-sig")
    inventory_bridge = OUT / "InventoryEntryBridges.cs"
    inventory_bridge.write_text("using System; using UnityEngine.SceneManagement; using ItemStatsSystem; namespace BossRush { public partial class ModBehaviour {\n"
        + "\n".join(member(source, s) for s in ("public bool IsPlayerNaked()", "private int GetBossRushTicketTypeId()"))
        + "\n" + "\n".join(member(entry, s) for s in ("private enum BossRushEntryMode", "private BossRushEntryMode DetermineBossRushEntryMode(string context)"))
        + "\n} }", encoding="utf-8")
    inventory_paths = [ROOT / "Utilities/ModeEntryInventory.cs", ROOT / "Config/ConfigItemIds.cs", inventory_bridge, HERE / "InventoryEntryRegression.cs"]
    inventory_project = OUT / "InventoryEntry.csproj"
    inventory_project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
        '</PropertyGroup><ItemGroup>'
        + ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in inventory_paths)
        + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "inventory-production-sha256.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in [host, ROOT / "WavesArena/BossRushEntryFlow.cs", ROOT / "Utilities/ModeEntryInventory.cs", ROOT / "Config/ConfigItemIds.cs"]}, indent=2), encoding="utf-8")
    inventory_result = subprocess.call(["dotnet", "run", "--project", str(inventory_project), "--configuration", "Release"], cwd=ROOT)
    return inventory_result or result


if __name__ == "__main__":
    raise SystemExit(main())
