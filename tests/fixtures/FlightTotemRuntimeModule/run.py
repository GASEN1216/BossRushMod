"""Link the FlightTotem owner and its real initialization/coroutine helpers."""
from pathlib import Path
import hashlib
import json
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/FlightTotemRuntimeModule"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    production = [
        ROOT / "Integration/FlightTotem/FlightTotemBootstrap.cs",
        ROOT / "Integration/FlightTotem/FlightTotemFactory.cs",
        ROOT / "Integration/FlightTotem/FlightConfig.cs",
        ROOT / "Common/Equipment/EquipmentAbilityConfig.cs",
        ROOT / "Common/Equipment/AbilitySystemHelper.cs",
    ]
    sources = production + [HERE / "Program.cs", HERE / "Stubs.cs"]
    includes = "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
                       '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "production-sha256.json").write_text(json.dumps({
        path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in production
    }, indent=2), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
