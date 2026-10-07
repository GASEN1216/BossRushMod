"""Extract the production Infinite Hell preset picker and exercise its draw policy."""

from pathlib import Path
import hashlib
import json
import subprocess


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE = ROOT / "WavesArena/WavesArenaRuntimeModule_EnemyPresets.cs"
OUT = ROOT / "Build/runtime-regressions/WavesArenaPresetWeight"
OUT.mkdir(parents=True, exist_ok=True)


def method(source: str, signature: str) -> str:
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    raise ValueError(signature)


source = SOURCE.read_text(encoding="utf-8-sig")
picker = method(source, "internal EnemyPresetInfo PickRandomEnemyForInfiniteHell()")
extra_sources = {
    "DragonKingRuntimeModule": ROOT / "Integration/DragonKing/DragonKingBoss.cs",
    "DragonDescendantRuntimeModule": ROOT / "Integration/DragonDescendant/DragonDescendantBoss_RuntimeAndCleanup.cs",
    "PhantomWitchRuntimeModule": ROOT / "Integration/PhantomWitch/PhantomWitchBoss.cs",
}
recovery = ""
for module, path in extra_sources.items():
    content = path.read_text(encoding="utf-8-sig")
    boss = module.removesuffix("RuntimeModule")
    signature = "internal void Register" + boss + "Preset()"
    assert content.count(signature) == 1
    recovery += "internal sealed partial class " + module + " {" + method(content, signature)
    if boss == "PhantomWitch":
        recovery += method(content, "internal CharacterRandomPreset FindPhantomWitchBasePreset()")
    recovery += "}"
(OUT / "Generated.cs").write_text(
    "using System; using System.Collections.Generic; using UnityEngine; namespace BossRush {"
    "internal sealed partial class WavesArenaRuntimeModule {" + picker + "}" + recovery + "}",
    encoding="utf-8",
)
(OUT / "sources.json").write_text(json.dumps({
    str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
    for path in [SOURCE] + list(extra_sources.values())
}, indent=2) + "\n", encoding="utf-8")
(OUT / "Regression.csproj").write_text(
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>'
    '<ItemGroup><Compile Include="' + str(HERE / "Program.cs") + '"/>'
    '<Compile Include="' + str(HERE / "PresetRecoveryRegression.cs") + '"/>'
    '<Compile Include="' + str(OUT / "Generated.cs") + '"/></ItemGroup></Project>',
    encoding="utf-8",
)
raise SystemExit(subprocess.run(["dotnet", "run", "--project",
    str(OUT / "Regression.csproj"), "--configuration", "Release"], cwd=ROOT).returncode)
