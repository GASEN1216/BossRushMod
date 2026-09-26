"""Link complete sweep runtime and the production host bridge."""
from pathlib import Path
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/AwenLootSweepRuntime"
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / "tests"))
from integration_host_source import host_region

# The independent enum/target stay linked in their original production file.
# Extract the complete production host region, including its readonly initializer.
host = OUT / "AwenLootSweepHost.cs"
host.write_text("using System;\nusing System.Collections.Generic;\n"
                "using Duckov.UI.DialogueBubbles;\nusing UnityEngine;\n"
                "namespace BossRush { public partial class ModBehaviour : Duckov.Modding.ModBehaviour {\n"
                + host_region(ROOT, "ModeEFLootboxTracker", "LootAndRewards/LootAndRewards.cs")
                + "\n}}\n", encoding="utf-8")
sources = [ROOT / "LootAndRewards/AwenLootSweepRuntime.cs", ROOT / "LootAndRewards/ModeEFLootboxTracker.cs",
           host, HERE / "Program.cs", HERE / "Stubs.cs"]
includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
project = OUT / "Regression.csproj"
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                   '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                   '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                   '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
raise SystemExit(subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT))
