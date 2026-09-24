"""Execute the production Birthday Cake delay coroutine with a tiny Unity wait adapter."""
from pathlib import Path
import hashlib
import subprocess
import xml.sax.saxutils as xml

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/BirthdayCakeGift"


def extract_method(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    source_path = ROOT / "Integration/BossRushIntegrationRuntimeModule_BirthdayCake.cs"
    source = source_path.read_text(encoding="utf-8")
    signature = "internal System.Collections.IEnumerator DelayedBirthdayCakeGift()"
    method = extract_method(source, signature)
    program_path = HERE / "Program.cs"
    program = program_path.read_text(encoding="utf-8")
    marker = "    // The regression runner inserts the production DelayedBirthdayCakeGift method here.\n"
    if program.count(marker) != 1:
        raise SystemExit("fixture insertion marker must occur exactly once")

    OUT.mkdir(parents=True, exist_ok=True)
    production = "using System.Collections;\n" + program.replace(marker, method + "\n")
    production_path = OUT / "Production.cs"
    production_path.write_text(production, encoding="utf-8")
    (OUT / "source-sha256.txt").write_text(hashlib.sha256(source_path.read_bytes()).hexdigest(), encoding="utf-8")

    project = (
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    )
    project += ''.join(
        '<Compile Include="' + xml.escape(str(path), {'"': '&quot;'}) + '" />'
        for path in (production_path,)
    )
    project += '</ItemGroup></Project>'
    project_path = OUT / "Probe.csproj"
    project_path.write_text(project, encoding="utf-8")

    build = subprocess.run(
        ["dotnet", "build", str(project_path), "--configuration", "Release",
         "--output", str(OUT / "bin"), "-p:BaseIntermediateOutputPath=" + str(OUT / "obj") + "/"],
        cwd=ROOT,
    )
    if build.returncode:
        return build.returncode
    return subprocess.call(["dotnet", str(OUT / "bin/Probe.dll")], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())