"""Execute production faction targeting with the official Team predicate and host doubles."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ModeECombatTargeting"
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for end in range(opening, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return source[start:end + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    controller = ROOT / "Integration/DragonDescendant/DragonDescendantAbilities.cs"
    collision = ROOT / "Integration/DragonDescendant/DragonDescendantAbilities_CollisionAndIce.cs"
    battle = ROOT / "ModeE/ModeEBattle.cs"
    source = clean_source(controller.read_text(encoding="utf-8-sig"))
    generated = "using System; namespace BossRush { partial class DragonDescendantAbilities {\n"
    generated += "\n".join(member(source, signature) for signature in (
        "private bool UsesArenaOpponent()", "private bool IsPlayerAlly()", "private void RefreshPlayerReference()",
        "private CharacterMainControl ResolveArenaOpponent(bool allowMainCharacter)"))
    generated += "\n" + member(clean_source(collision.read_text(encoding="utf-8-sig")),
                              "public void OnCollisionWithPlayer(CharacterMainControl player)")
    generated += "}\nstatic class BloodhoundSetup { internal static void Apply(CharacterMainControl character, bool isModeFRun, Teams runtimeFaction, Teams modeEPlayerFaction) {\n"
    spawn = clean_source(battle.read_text(encoding="utf-8-sig"))
    start = spawn.index("var ai = character.GetComponentInChildren<AICharacterController>();")
    end = spawn.index("if (!isModeFRun)", start)
    generated += spawn[start:end] + "}}}"
    output = OUT / "Production.cs"
    output.write_text(generated, encoding="utf-8")
    official = ROOT / "鸭科夫源码/TeamSoda.Duckov.Core"
    linked = [official / "Team.cs", official / "Teams.cs"]
    paths = [output, HERE / "Program.cs"] + linked
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>'
                      + ''.join('<Compile Include="' + escape(str(p)) + '" />' for p in paths)
                      + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "sources.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                                                for p in [controller, collision, battle] + linked}, indent=2), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
