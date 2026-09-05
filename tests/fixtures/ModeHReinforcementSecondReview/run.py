"""编译实际事务与逐字提取的增援/判胜/清理方法；Unity/生成端由 Harness 提供。"""
from pathlib import Path
import hashlib
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build" / "fix-20260905-r2" / "ModeHReinforcementSecondReview"


def method(text, signature):
    start = text.index(signature)
    opening = text.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    names = ["ModeHSpawnTransaction.cs", "ModeHRuntimeModule_CombatProfiles.cs",
             "ModeHRuntimeModule_CombatFlow.cs", "ModeHCombatControl.cs",
             "ModeHRuntimeModule_SceneFlow.cs"]
    sources = {name: (ROOT / "ModeH" / name).read_text(encoding="utf-8-sig") for name in names}
    profiles = sources["ModeHRuntimeModule_CombatProfiles.cs"]
    region = profiles[profiles.index("        #region 敌军分批入场"):profiles.rindex("        #endregion")]
    runtime = [region + "\n        #endregion"]
    for file, signature in [
        ("ModeHRuntimeModule_CombatFlow.cs", "private void ReleaseCombatRuntimeObjects()"),
        ("ModeHRuntimeModule_CombatFlow.cs", "private void TickActiveCombat(float deltaTime)"),
        ("ModeHRuntimeModule_SceneFlow.cs", "private bool IsCallbackStillValid("),
    ]:
        runtime.append(method(sources[file], signature))
    control = []
    for signature in ["public bool Tick(float deltaTime, ModeHBattleSnapshotContext snapshotContext)",
                      "public void OnEnemyBatchEntered(", "public void OnEnemyEntered(",
                      "public void SetEnemySpawningPending("]:
        control.append(method(sources["ModeHCombatControl.cs"], signature))
    generated = "using System;using System.Collections;using System.Collections.Generic;using UnityEngine;\nnamespace BossRush {\n"
    generated += "internal sealed partial class ModeHRuntimeModule {\n" + "\n".join(runtime) + "\n}\n"
    generated += "internal sealed partial class ModeHCombatControl {\n" + "\n".join(control) + "\n}\n}\n"
    (OUT / "Extracted.cs").write_text(generated, encoding="utf-8")
    (OUT / "ModeHSpawnTransaction.cs").write_text(sources["ModeHSpawnTransaction.cs"], encoding="utf-8")
    (OUT / "Harness.cs").write_text((HERE / "Harness.cs").read_text(encoding="utf-8"), encoding="utf-8")
    (OUT / "source-sha256.txt").write_text("\n".join(
        hashlib.sha256((ROOT / "ModeH" / name).read_bytes()).hexdigest() + "  ModeH/" + name
        for name in names) + "\n", encoding="utf-8")
    (OUT / "Fixture.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>0649;0414</NoWarn></PropertyGroup>
  <ItemGroup><Compile Include="Extracted.cs"/><Compile Include="ModeHSpawnTransaction.cs"/>
    <Compile Include="Harness.cs"/></ItemGroup>
</Project>''', encoding="utf-8")
    result = subprocess.run(["dotnet", "run", "--project", str(OUT / "Fixture.csproj"),
                             "--configuration", "Release", "--verbosity", "quiet"],
                            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    output = result.stdout + result.stderr
    (OUT / "execution.log").write_text(output, encoding="utf-8")
    print(output, end="")
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
