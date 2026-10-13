"""Execute real lock/unlock clicks and NPC reward delivery with the shared inventory transaction."""
from pathlib import Path
import hashlib
import json
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ReforgeAssetTransactions"
sys.path.insert(0, str(ROOT / "tests/fixtures/EntryAndReforgeCompatibility"))
from run import extract, HASHES
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    ui = "Integration/Reforge/ReforgeUIManager.cs"
    npc = "Integration/NPCs/Goblin/GoblinNPCReward.cs"
    code = ["using System; using UnityEngine; using ItemStatsSystem; namespace BossRush { public partial class Row {"]
    code += [extract(ui, s) for s in ("public void OnPointerClick(", "private void HandleUnlockClick(")]
    if "private bool TryChangeLockWithMaterial(" in (ROOT / ui).read_text(encoding="utf-8-sig"):
        code += [extract(ui, "private bool TryChangeLockWithMaterial(")]
    code += ["} public partial class GoblinNPCController {"]
    code += [extract(npc, s) for s in ("public bool TryGiveRewardItem(", "private bool DropRewardItemAtNpc(", "private void ShowRewardBanner(")]
    code += ["} public static partial class ItemFactory {", extract("Integration/ItemFactory.cs", "public static bool ConsumeItem("), "}}"]
    generated = OUT / "Extracted.cs"
    generated.write_text("\n".join(code), encoding="utf-8")
    files = [generated, HERE / "Stubs.cs", HERE / "Program.cs",
             ROOT / "tests/fixtures/SkyIslandDelivery/Stubs.cs",
             ROOT / "Integration/Reforge/PropertyLockSystem.cs",
             ROOT / "SkyIsland/SkyIslandInventoryTransaction.cs"]
    hashes = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in files[1:]}
    (OUT / "source-hashes.json").write_text(json.dumps({"methods": HASHES, "files": hashes}, indent=2), encoding="utf-8")
    project = OUT / "ReforgeAssetTransactions.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                      '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                      '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
                      + ''.join('<Compile Include="' + escape(str(p)) + '"/>' for p in files)
                      + '</ItemGroup></Project>', encoding="utf-8")
    code, output = run_project_fixture(project, OUT, ROOT)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
