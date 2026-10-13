"""Run actual pet care, inventory reservation and asset-save methods with isolated host state."""
from pathlib import Path
import hashlib
import json
import os
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(os.environ.get("PETNEST_CARE_SOURCE_ROOT", Path(__file__).resolve().parents[3]))
HERE = Path(__file__).resolve().parent
OUT = Path(os.environ.get("PETNEST_CARE_OUTPUT", ROOT / "Build/runtime-regressions/PetNestCare"))
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source


def member(path, signature):
    code = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
    assert code.count(signature) == 1, signature
    start = code.index(signature)
    begin = code.index("{", start)
    depth = 0
    for end in range(begin, len(code)):
        depth += (code[end] == "{") - (code[end] == "}")
        if not depth:
            return code[start:end + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    care = ROOT / "PetNest/PetNestCompanionCare.cs"
    source = "using System; using System.Collections.Generic; using ItemStatsSystem; using UnityEngine; using Saves; using Duckov.Economy; using SodaCraft.Localizations; namespace BossRush {\n"
    transaction = clean_source((ROOT / "SkyIsland/SkyIslandInventoryTransaction.cs").read_text(encoding="utf-8-sig"))
    start = transaction.index("internal sealed class SkyIslandInventoryTransaction : IDisposable")
    end = transaction.index("private SkyIslandInventoryTransaction(CharacterMainControl owner)", start)
    source += transaction[start:end]
    for signature in ("private SkyIslandInventoryTransaction(CharacterMainControl owner)", "internal static bool TryReserve(",
                      "internal void Commit()", "private void Restore(", "public void Dispose()", "internal static bool HasOwner(",
                      "internal static void DestroyUnowned(", "private static void SetCount("):
        source += member("SkyIsland/SkyIslandInventoryTransaction.cs", signature) + "\n"
    source += "}\nstatic partial class PetNestService {\n"
    for signature in ("internal static bool StageCommit()", "private static bool CommitCandidate("):
        source += member("PetNest/PetNestService.cs", signature) + "\n"
    source += "}\nstatic partial class PetNestSaveCoordinator {\n"
    for signature in ("internal static void MarkBackpackAssetsChanged()", "internal static bool RequireBackpackAssetSnapshot(",
                      "internal static bool CollectPendingAssets(", "internal static bool RequestAssetFlush("):
        source += member("PetNest/PetNestSaveCoordinator.cs", signature) + "\n"
    source += "}\nstatic partial class PetNestLocalization {\n" + member("Localization/PetNestLocalization.cs", "public static void Inject()") + "\n}\n"
    source += "static class PetNestExpeditionService {\n" + member("PetNest/PetNestExpeditionService.cs", "internal static float EffectiveDeathRate(")
    source += "\nstatic float GetDeathRate(PetNestRiskTier tier){return tier==PetNestRiskTier.Desperate?.12f:0f;} }\n"
    # Exercise the exact official property used by the menu, including language override lookup.
    source += "}\npublic partial class InteractableBase {\n" + member("鸭科夫源码/TeamSoda.Duckov.Core/InteractableBase.cs", "public string InteractName") + "\n}\n"
    (OUT / "Production.cs").write_text(source, encoding="utf-8")
    production = [care, ROOT / "Common/Lifecycle/BossRushSaveCoordinatorEngine.cs", ROOT / "Common/Lifecycle/BossRushSaveFileThrottle.cs"]
    paths = production + [OUT / "Production.cs", HERE / "Stubs.cs", HERE / "Program.cs"]
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0169</NoWarn></PropertyGroup><ItemGroup>'
                       + ''.join('<Compile Include="' + escape(str(path)) + '" />' for path in paths) + '</ItemGroup></Project>', encoding="utf-8")
    referenced = production + [ROOT / path for path in ("SkyIsland/SkyIslandInventoryTransaction.cs", "PetNest/PetNestService.cs",
                 "PetNest/PetNestSaveCoordinator.cs", "Localization/PetNestLocalization.cs", "PetNest/PetNestExpeditionService.cs")]
    (OUT / "sources.json").write_text(json.dumps({str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in referenced}, indent=2), encoding="utf-8")
    build = subprocess.run(["dotnet", "build", str(project), "--configuration", "Release", "--nologo"], cwd=ROOT)
    if build.returncode:
        return build.returncode
    target = subprocess.run(["dotnet", "msbuild", str(project), "-getProperty:TargetPath", "-p:Configuration=Release", "-nologo"], cwd=ROOT, text=True, capture_output=True, check=True).stdout.strip()
    print("EXECUTE " + target + " SHA256=" + hashlib.sha256(Path(target).read_bytes()).hexdigest(), flush=True)
    return subprocess.call(["dotnet", target], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
