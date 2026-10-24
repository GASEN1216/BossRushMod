"""Execute the real fog binding and extracted equipment entry methods without Unity."""
from pathlib import Path
import hashlib
import importlib.util
import json
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ModEntityFogVisibility"
PRODUCTION = (
    "Utilities/ModEntityFogVisibility.cs",
    "Integration/EquipmentFactory_ItemProcessing.cs",
    "Integration/DragonKing/Weapons/FenHuangHalberdWeaponConfig.cs",
    "Integration/PhantomWitch/PhantomWitchScytheWeaponConfig.cs",
)


def member(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    cursor = opening + 1
    while depth:
        depth += (source[cursor] == "{") - (source[cursor] == "}")
        cursor += 1
    return source[start:cursor]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    source = (ROOT / PRODUCTION[1]).read_text(encoding="utf-8-sig")
    parts = ["using UnityEngine; namespace BossRush { internal static class EquipmentVisualFixture {",
             member(source, "private static void EnsureModelSubVisuals("),
             "internal static void Ensure(GameObject go) { EnsureModelSubVisuals(go); } }"]
    for name, path in zip(("HalberdVisualFixture", "ScytheVisualFixture"), PRODUCTION[2:]):
        source = (ROOT / path).read_text(encoding="utf-8-sig")
        parts += ["internal static class " + name + " {",
                  member(source, "internal static void PrepareRuntimeHoldAgentVisual("),
                  member(source, "private static void EnableRuntimeRenderers("),
                  "private static void DisableMotionBlur(GameObject go) { VisualTrace.Events.Add(\"motion\"); }",
                  "private static void FixModelGraphics(GameObject go) { VisualTrace.Events.Add(\"graphics\"); go.layer = 0; } }"]
    parts.append("}")
    generated = OUT / "EquipmentMethods.cs"
    generated.write_text("\n".join(parts), encoding="utf-8")
    files = [ROOT / PRODUCTION[0], HERE / "Stubs.cs", HERE / "Program.cs", generated]
    includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in files)
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                      '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                      '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                      '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "source-hashes.json").write_text(json.dumps({p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest()
                                                     for p in PRODUCTION}, indent=2), encoding="utf-8")
    spec = importlib.util.spec_from_file_location("runtime_runner", ROOT / "tools/run_runtime_regressions.py")
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    code, output = runner.run_project_fixture(project, OUT / "execution", ROOT)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
