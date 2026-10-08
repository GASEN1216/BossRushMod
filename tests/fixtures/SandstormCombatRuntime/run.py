"""Execute production fight iterator and official simple-health pipeline with host adapters."""
from pathlib import Path
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture


def method(path, signature):
    text = (ROOT / path).read_text(encoding="utf-8-sig")
    start = text.index(signature)
    end = text.index("{", start) + 1
    depth = 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


def main():
    out = ROOT / "Build" / "sandstorm-combat-runtime"
    out.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="source-", dir=out))
    controller = "Integration/SandstormChampion/SandstormChampionAbilityController.cs"
    generated = "using System; using System.Collections; using System.Collections.Generic; using UnityEngine; using UnityEngine.Events; namespace BossRush { partial class SandstormChampionController {\n"
    for signature in ("private void TickFight()", "private IEnumerator WaitForFightSeconds(",
                      "private IEnumerator RunFight()", "private IEnumerator Fight()", "private IEnumerator OrbitAndOrbs("):
        generated += method(controller, signature) + "\n"
    generated += "}\ninternal static class SandstormChampionVolume {\n"
    generated += method("Integration/SandstormChampion/SandstormChampionVolume.cs", "internal static float SampleHeightDensity(")
    generated += "}\npartial class SandstormOrb {\n"
    hazards = "Integration/SandstormChampion/SandstormChampionHazards.cs"
    # Keep production construction and exact receiver wiring; omit rendering-only emitter setup.
    generated += method(hazards, "internal static SandstormOrb Spawn(") + "\n"
    init = method(hazards, "private void Init(SandstormChampionController owner, Vector3 direction,")
    start = init.index('int layer = LayerMask.NameToLayer("DamageReceiver");')
    end = init.index("_subscribed = true;") + len("_subscribed = true;")
    generated += "private void Init(SandstormChampionController owner, Vector3 direction, float speedScale, bool homing) { _owner = owner; _bubble = homing;\n" + init[start:end] + "}\n"
    generated += method(hazards, "private void OnShotDown(") + "\n}}"
    (run / "Production.cs").write_text(generated, encoding="utf-8")
    official = ROOT / "鸭科夫源码/TeamSoda.Duckov.Core"
    # Decompiled source requires an unambiguous Random alias; keep its method bodies unchanged.
    (run / "HealthSimpleBase.cs").write_text("using Random = UnityEngine.Random;\n" + (official / "HealthSimpleBase.cs").read_text(encoding="utf-8-sig"), encoding="utf-8")
    files = [run / "Production.cs", Path(__file__).with_name("Program.cs"),
             ROOT / "Integration/SandstormChampion/SandstormChampionAttackPattern.cs",
             official / "DamageReceiver.cs", run / "HealthSimpleBase.cs"]
    from xml.sax.saxutils import escape
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p)) + '" />' for p in files)
    project += '</ItemGroup></Project>'
    (run / "Combat.csproj").write_text(project, encoding="utf-8")
    code, output = run_project_fixture(run / "Combat.csproj", run / "execution", ROOT)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
