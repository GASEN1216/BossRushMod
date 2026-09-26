"""Run the actual shared registry and Mode E loot policy with controlled Unity events."""
from pathlib import Path
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ModeEFEnemyRegistry"
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / "tests"))
from ArchitectureStructureGuard import extract_method_body

source = (ROOT / "ModeE/ModeEBattle_ScalingAndRuntime.cs").read_text(encoding="utf-8-sig")
signature = "private void RegisterModeEEnemyLootHandler(CharacterMainControl enemy, Teams faction)"
body = extract_method_body(source, signature)
assert body
generated = OUT / "ModeELootPolicy.cs"
generated.write_text("using System; namespace BossRush { internal sealed partial class ModeELootPolicy {\n"
                     + signature + body + "\n}}", encoding="utf-8")
paths = [ROOT / "Utilities/ModeEFEnemyRegistry.cs", generated, HERE / "Stubs.cs", HERE / "Program.cs"]
includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
project = OUT / "Regression.csproj"
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
    '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
raise SystemExit(subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT))
