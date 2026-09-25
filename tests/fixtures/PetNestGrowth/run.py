"""Run current growth rules and exact custom-gear replacement methods with host doubles."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/PetNestGrowth"
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source


def member(raw, signature):
    code = clean_source(raw)
    assert code.count(signature) == 1, signature
    start = code.index(signature)
    opening = code.index("{", start)
    depth = 0
    for end in range(opening, len(code)):
        depth += (code[end] == "{") - (code[end] == "}")
        if depth == 0:
            return code[start:end + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    spawner = ROOT / "PetNest/PetNestCompanionSpawner.cs"
    raw = spawner.read_text(encoding="utf-8-sig")
    methods = "\n".join(member(raw, key) for key in (
        "internal static void NeutralizeClonePreset(",
        "private static async UniTask EquipCustomBossGearAsync(",
        "private static async UniTask EquipCustomBossSlotAsync(",
        "private static bool PrepareCustomGunMagazine(",
        "private static bool StoreCustomAmmo(",
        "private static int[] ResolveCustomBossGear("))
    # Only replace the async host return type; all branches and assignments are production text.
    methods = methods.replace("async UniTask ", "async Task ")
    (OUT / "Production.cs").write_text(
        "using System; using System.Collections.Generic; using System.Threading.Tasks; "
        "namespace BossRush { static class PetNestCompanionSpawner {\n" + methods
        + "\ninternal static Task Equip(CharacterMainControl c, string key) { return EquipCustomBossGearAsync(c,key); }\n}}",
        encoding="utf-8")
    linked = [ROOT / "PetNest/PetNestGrowth.cs", ROOT / "PetNest/PetNestTuning.cs"]
    files = linked + [OUT / "Production.cs", HERE / "Program.cs"]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p)) + '" />' for p in files)
    (OUT / "Regression.csproj").write_text(project + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "sources.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in linked + [spawner]}, indent=2), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(OUT / "Regression.csproj"), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
