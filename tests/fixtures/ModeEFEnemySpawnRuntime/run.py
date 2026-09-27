"""Execute the complete E/F shared spawn owner with controlled Unity/time/core boundaries."""
from pathlib import Path
import hashlib
import json
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ModeEFEnemySpawnRuntime"
OUT.mkdir(parents=True, exist_ok=True)
production = ROOT / "Utilities/ModeEFEnemySpawnRuntime.cs"
generated = OUT / production.name
generated.write_text(production.read_text(encoding="utf-8-sig")
                     .replace("using Cysharp.Threading.Tasks;", "using Cysharp.Threading.Tasks;\nusing System.Threading.Tasks;")
                     .replace("UniTaskVoid", "Task"), encoding="utf-8")
sources = [generated, HERE / "Stubs.cs", HERE / "Program.cs"]
includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
project = OUT / "Regression.csproj"
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
    '<NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
(OUT / "production-sha256.json").write_text(json.dumps({str(production.relative_to(ROOT)): hashlib.sha256(production.read_bytes()).hexdigest()}, indent=2), encoding="utf-8")
raise SystemExit(subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT))
