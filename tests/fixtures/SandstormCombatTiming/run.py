"""Execute production dash control, minion windups and complete ribbon implementation."""
from pathlib import Path
import sys
import tempfile
import hashlib
import json
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture


def method(text, signature):
    assert text.count(signature) == 1, signature
    start = text.index(signature)
    end = text.index("{", start) + 1
    depth = 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


def main():
    out = ROOT / "Build/sandstorm-combat-timing"
    out.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="source-", dir=out))
    source = (ROOT / "Integration/SandstormChampion/SandstormChampionAbilityController.cs").read_text(encoding="utf-8-sig")
    generated = "using System; using System.Collections; using System.Collections.Generic; using UnityEngine; namespace BossRush { partial class SandstormChampionController {\n"
    for signature in ("private void Update()", "private void TickFight()", "private IEnumerator RunFight()",
                      "private IEnumerator Dashes(", "private bool PhaseDashStep(", "private void PhaseTo(",
                      "private void AimDash(", "private IEnumerator WaitForFightSeconds(",
                      "private static float DistanceToSegment("):
        generated += method(source, signature) + "\n"
    # 原方法只改入口名，便于与 Update 的调用计数替身同时验证真实速度采样。
    generated += method(source,"private void TrackPlayerVelocity()").replace("TrackPlayerVelocity()","SamplePlayerVelocity()",1)+"\n"
    generated += "}}"
    (run / "Production.cs").write_text(generated, encoding="utf-8")
    (run / "production-sha256.json").write_text(json.dumps({"Integration/SandstormChampion/SandstormChampionAbilityController.cs":hashlib.sha256((ROOT/"Integration/SandstormChampion/SandstormChampionAbilityController.cs").read_bytes()).hexdigest()},indent=2),encoding="utf-8")
    files = [run / "Production.cs", Path(__file__).with_name("Program.cs"), Path(__file__).with_name("Stubs.cs"),
             ROOT / "Integration/SandstormChampion/SandstormChampionConfig.cs",
             ROOT / "Integration/SandstormChampion/SandstormChampionMinionArts.cs",
             ROOT / "Integration/NewWeapons/AstralStaff/AstralStaffFxShapes.cs"]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(file)) + '" />' for file in files)
    project += '</ItemGroup></Project>'
    (run / "Timing.csproj").write_text(project, encoding="utf-8")
    code, output = run_project_fixture(run / "Timing.csproj", run / "execution", ROOT)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
