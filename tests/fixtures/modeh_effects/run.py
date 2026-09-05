#!/usr/bin/env python3
"""Compile actual effect classes and verbatim combat entry methods with host stubs.

Outputs stay under Build; this does not load Unity, player saves or deploy a DLL.
"""
from pathlib import Path
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
FIXTURE = Path(__file__).resolve().parent
BUILD = ROOT / "Build/fix-20260905/modeh-effects-fixture"


def method(source, signature):
    start = source.index(signature)
    brace = source.index("{", start)
    depth = 1
    end = brace + 1
    while depth:
        if source[end] == "{":
            depth += 1
        elif source[end] == "}":
            depth -= 1
        end += 1
    return source[start:end]


def prepare(build, mutations=None):
    build.mkdir(parents=True, exist_ok=True)
    mutations = mutations or {}
    paths_by_name = {}
    source_by_name = {}
    for name in ("ModeHContentModels.cs", "ModeHInjuryAndScarSystem.cs",
                 "ModeHCommandAdapters.cs", "ModeHCommandController.cs", "ModeHCombatControl.cs"):
        path = ROOT / "ModeH" / name
        source = path.read_text(encoding="utf-8-sig")
        for old, new in mutations.get(name, ()):
            if old not in source:
                raise AssertionError("missing mutation anchor in " + name)
            source = source.replace(old, new, 1)
        source_by_name[name] = source
        if name in mutations:
            path = build / name
            path.write_text(source, encoding="utf-8")
        paths_by_name[name] = path
    source = source_by_name["ModeHCombatControl.cs"]
    methods = [method(source, "public bool OnFighterEntered("),
               method(source, "public bool TryRingBell(ModeHBattleSnapshotContext")]
    (build / "CombatMethods.cs").write_text(
        "namespace BossRush { internal sealed partial class ModeHCombatControl {\n"
        + "\n".join(methods) + "\n} }\n", encoding="utf-8")
    paths = [FIXTURE / "Program.cs", FIXTURE / "Stubs.cs", build / "CombatMethods.cs"]
    paths += [paths_by_name[name] for name in (
        "ModeHContentModels.cs", "ModeHInjuryAndScarSystem.cs",
        "ModeHCommandAdapters.cs", "ModeHCommandController.cs")]
    project = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>7.3</LangVersion>
    <NoWarn>0649</NoWarn><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup>
"""
    project += "\n".join('    <Compile Include="%s" />' % escape(str(p)) for p in paths)
    project += '\n    <None Include="%s" Link="Scars.json" CopyToOutputDirectory="PreserveNewest" />' % escape(str(ROOT / "Assets/Data/ModeH/Scars.json"))
    project += "\n  </ItemGroup>\n</Project>\n"
    csproj = build / "Effects.csproj"
    csproj.write_text(project, encoding="utf-8")
    return csproj


def main():
    csproj = prepare(BUILD)
    result = subprocess.run(["dotnet", "run", "--project", str(csproj), "--configuration", "Release"],
                            cwd=ROOT, text=True, encoding="utf-8", errors="replace")
    if result.returncode or "--verify-regressions" not in sys.argv:
        return result.returncode
    mutations = {
        "unowned-scar": {"ModeHInjuryAndScarSystem.cs": [("if (!_ownedScarIds.Contains(scarId))", "if (false)")]},
        "expired-adapter": {
            "ModeHInjuryAndScarSystem.cs": [("window.Adapter.Restore();\n                    _activeWindows.RemoveAt(i);", "_activeWindows.RemoveAt(i);")],
            "ModeHCommandAdapters.cs": [("_windowRemaining -= deltaTime;\n            if (_windowRemaining <= 0f)\n            {\n                Restore();\n                return;\n            }", "_windowRemaining -= deltaTime;")]},
        "global-command-scale": {"ModeHInjuryAndScarSystem.cs": [("modulation.TargetCommandId = component.TargetCommandId;", "modulation.TargetCommandId = null;")]},
        "permanent-command-scale": {"ModeHInjuryAndScarSystem.cs": [("_commandModulations[i].RemainingSeconds -= deltaTime;", "")]},
        "empty-initial-context": {"ModeHInjuryAndScarSystem.cs": [("_sharedFireContext = fireContext;", "_sharedFireContext = null;")]},
        "late-bell-scale": {"ModeHCombatControl.cs": [("GetCommandScaleForBell(_commandController.LockedCommandId)", "GetCommandScale(_commandController.LockedCommandId)")]},
    }
    for name, changes in mutations.items():
        project = prepare(BUILD / name, changes)
        mutant = subprocess.run(["dotnet", "run", "--project", str(project), "--configuration", "Release"],
                                cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
        output = mutant.stdout + mutant.stderr
        (project.parent / "result.log").write_text(output, encoding="utf-8")
        if mutant.returncode == 0 or "System.Exception: FAIL:" not in output:
            print("FAIL reverse regression did not reach an assertion: " + name)
            print(output)
            return 1
        failure = next(line for line in output.splitlines() if "System.Exception: FAIL:" in line)
        print("PASS reverse regression rejected " + name + ": " + failure.split("FAIL:", 1)[1].strip())
    print("Mode H effects reverse regressions: %d behavioral mutations rejected." % len(mutations))
    return 0


if __name__ == "__main__":
    sys.exit(main())
