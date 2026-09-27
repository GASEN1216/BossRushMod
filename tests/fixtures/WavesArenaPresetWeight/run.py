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
(OUT / "Generated.cs").write_text(
    "using System; using System.Collections.Generic; using UnityEngine; namespace BossRush {"
    "internal sealed partial class WavesArenaRuntimeModule {" + picker + "}}",
    encoding="utf-8",
)
(OUT / "sources.json").write_text(json.dumps({
    str(SOURCE.relative_to(ROOT)): hashlib.sha256(SOURCE.read_bytes()).hexdigest()
}, indent=2) + "\n", encoding="utf-8")
(OUT / "Regression.csproj").write_text(
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>'
    '<ItemGroup><Compile Include="' + str(HERE / "Program.cs") + '"/>'
    '<Compile Include="' + str(OUT / "Generated.cs") + '"/></ItemGroup></Project>',
    encoding="utf-8",
)
raise SystemExit(subprocess.run(["dotnet", "run", "--project",
    str(OUT / "Regression.csproj"), "--configuration", "Release"], cwd=ROOT).returncode)
