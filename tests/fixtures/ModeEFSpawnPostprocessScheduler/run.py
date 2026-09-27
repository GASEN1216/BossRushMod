"""Link the production scheduler/profiler and extract unchanged SpawnCore DTOs."""
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ModeEFSpawnPostprocessScheduler"
OUT.mkdir(parents=True, exist_ok=True)
source = (ROOT / "Utilities/EnemySpawnCore.cs").read_text(encoding="utf-8")
contracts = OUT / "Contracts.cs"
contracts.write_text(source[:source.index("    internal sealed class EnemySpawnRuntime")] + "}\n", encoding="utf-8")
sources = [ROOT / "Utilities/ModeEFSpawnPostprocessScheduler.cs", ROOT / "Utilities/ModeEFSpawnProfiler.cs", contracts, HERE / "Program.cs"]
includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
project = OUT / "Regression.csproj"
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                   '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                   '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                   '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
raise SystemExit(subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT))
