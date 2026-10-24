"""Execute production shield ownership and the real final-damage entry without launching the game."""
from pathlib import Path
from xml.sax.saxutils import escape
import hashlib
import json
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
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
    out = ROOT / "Build/runtime-regressions/ResidualSunShield"
    out.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="source-", dir=out))
    damage_path = ROOT / "ZombieMode/ZombieModeDamageRuntime.cs"
    damage = damage_path.read_text(encoding="utf-8-sig")
    source = "using System; using UnityEngine; namespace BossRush { internal static class ZombieModeDamageRuntime {\n"
    for signature in ("internal static bool IsPlayerSideAttack(",
                      "internal static bool IsPlayerOwnHit(",
                      "internal static void ReduceFinalDamage("):
        source += method(damage, signature) + "\n"
    source += "}}\n"
    generated = invocation / "DamageBridge.cs"
    generated.write_text(source, encoding="utf-8")
    combat_path = ROOT / "Integration/EclipseBloom/EclipseBloomAbilityController.cs"
    official_path = ROOT / "鸭科夫源码/TeamSoda.Duckov.Core/CharacterMainControl.cs"
    death_source = "using System; using UnityEngine; namespace BossRush { internal sealed class EclipseBloomAbilityController : MonoBehaviour {\n"
    death_source += "internal CharacterRandomPreset originalPreset;\n"
    death_source += method(combat_path.read_text(encoding="utf-8-sig"), "internal static bool RestoreOfficialPresetBeforeDeath(")
    death_source += "}}\npublic sealed partial class CharacterMainControl {\n"
    death_source += method(official_path.read_text(encoding="utf-8-sig"), "private void OnDead(DamageInfo dmgInfo)")
    death_source += "}\n"
    death_generated = invocation / "DeathBridge.cs"
    death_generated.write_text(death_source, encoding="utf-8")
    prefix_path = ROOT / "Patches/Combat/CharacterOnDeadPatch.cs"
    sources = [generated, death_generated, prefix_path, HERE / "Program.cs", HERE / "Stubs.cs",
               HERE / "DeathPrefixCases.cs", ROOT / "Config/ConfigItemIds.cs"]
    sources += [ROOT / ("Integration/EclipseBloom/ResidualSunShield" + name + ".cs")
                for name in ("Config", "Rules", "Runtime")]
    hashes = {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
              for path in sources if path not in (generated, death_generated)}
    for path in (damage_path, combat_path, official_path):
        hashes[str(path.relative_to(ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
    (invocation / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    includes = "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
    project = invocation / "ResidualSunShield.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                       '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    code, output = run_project_fixture(project, invocation / "execution", ROOT)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
