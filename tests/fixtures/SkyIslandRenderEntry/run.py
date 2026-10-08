"""Build the whole production rendering owner and execute its actual TargetPath."""
from pathlib import Path
import hashlib
import json
import os
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = Path(os.environ.get("BOSSRUSH_FIXTURE_OUT", str(ROOT / "Build/runtime-regressions/SkyIslandRenderEntry")))
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture

source = Path(os.environ.get("BOSSRUSH_SKY_RENDER_SOURCE", str(ROOT / "SkyIsland/SkyIslandRendering.cs")))
paths = [source, HERE / "Program.cs", HERE / "Stubs.cs"]
project = OUT / "Regression.csproj"
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>'
    + ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
    + '</ItemGroup></Project>', encoding="utf-8")
(OUT / "production-sha256.json").write_text(json.dumps({str(source): hashlib.sha256(source.read_bytes()).hexdigest()}), encoding="utf-8")
code, output = run_project_fixture(project, OUT, ROOT)
print(output)
raise SystemExit(code)
