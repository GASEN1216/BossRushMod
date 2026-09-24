"""Compile and run the production TickModeRuntimeGroup with tracing stubs."""
from pathlib import Path
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build" / "mode-runtime-dispatch"
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source


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
    OUT.mkdir(parents=True, exist_ok=True)
    source = clean_source((ROOT / "Utilities/ModeRuntimeHooks.cs").read_text(encoding="utf-8-sig"))
    member = extract_method(source, "internal bool TickModeRuntimeGroup(float deltaTime, float unscaledDeltaTime)")
    production = OUT / "Production.cs"
    production.write_text("namespace BossRush { public partial class ModBehaviour {" + member + "}}", encoding="utf-8")
    sources = [production, HERE / "Program.cs"]
    includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project = OUT / "ModeRuntimeDispatch.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>'
                       '<ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
