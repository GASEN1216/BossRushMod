"""Execute the three production equipment owners and their original host entry points."""
from pathlib import Path
import hashlib
import json
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/EquipmentBootstrapOwners"

import sys
sys.path.insert(0, str(ROOT / "tests"))
from integration_host_source import materialize_host


def block(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    linked = [
        ROOT / "Integration/ReverseScale/ReverseScaleFactory.cs",
        ROOT / "Integration/ReverseScale/ReverseScaleBootstrap.cs",
        materialize_host(ROOT, OUT / "ReverseHost.cs", "ReverseScaleRuntimeModuleHostBridge", "using UnityEngine; using UnityEngine.SceneManagement;"),
        ROOT / "Integration/ReverseScale/ReverseScaleConfig.cs",
        ROOT / "Common/Equipment/AbilitySystemHelper.cs",
        ROOT / "Common/Equipment/EquipmentAbilityConfig.cs",
    ]
    extracted = []
    generated = []
    for folder, bootstrap, bridge, module, label, wait in (
        ("PhantomWitch", "PhantomWitchScytheBootstrap", "PhantomWitchRuntimeModuleHostBridge", "PhantomWitchRuntimeModule", "PhantomWitchScythe", "PhantomWitchScytheSharedWait05sForRuntime"),
        ("DragonKing", "Weapons/FenHuangHalberdBootstrap", "DragonKingRuntimeModuleHostBridge", "DragonKingRuntimeModule", "FenHuangHalberd", "FenHuangHalberdSharedWait05sForRuntime"),
    ):
        path = ROOT / ("Integration/" + folder + "/" + bootstrap + ".cs")
        bridge_path = ROOT / "Integration/IntegrationHostCompatibility.cs"
        extracted += [path, bridge_path]
        body = block(path.read_text(encoding="utf-8-sig"), "internal sealed partial class " + module)
        bridge_source = bridge_path.read_text(encoding="utf-8-sig")
        methods = [block(bridge_source, "private void " + verb + label + tail) for verb, tail in (
            ("Initialize", "System()"), ("Setup", "ForScene(UnityEngine.SceneManagement.Scene scene)"), ("Cleanup", "System()"))]
        methods.append(block(bridge_source, "internal static WaitForSeconds " + wait))
        output = OUT / (module + ".cs")
        output.write_text("using System; using System.Collections; using UnityEngine; using UnityEngine.SceneManagement; namespace BossRush {\n" +
                          body + "\npublic partial class ModBehaviour {\n" + "\n".join(methods) + "\n}}", encoding="utf-8")
        generated.append(output)
    includes = "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />'
                       for path in linked + generated + [HERE / "Program.cs", HERE / "Stubs.cs"])
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>' +
                       includes + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "production-sha256.json").write_text(json.dumps({path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
                                                          for path in linked + extracted}, indent=2), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
