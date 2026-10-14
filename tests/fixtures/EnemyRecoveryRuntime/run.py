"""Link the complete recovery monitor and both production enumeration adapters."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/EnemyRecoveryRuntime"
OUT.mkdir(parents=True, exist_ok=True)
production = [ROOT / p for p in (
    "Utilities/EnemyRecoveryMonitor.cs", "ZombieMode/ZombieModeRuntimeModule_Recovery.cs", "WavesArena/WavesArenaRuntimeModule_Recovery.cs")]
authority = ROOT / "Utilities/CoopAiPredictionHealthProtection.cs"
raw = authority.read_text(encoding="utf-8-sig")
start = raw.index("internal static bool HasEntityAuthority(CharacterMainControl character)")
opening = raw.index("{", start)
depth = 0
for end in range(opening, len(raw)):
    if raw[end] == "{": depth += 1
    elif raw[end] == "}":
        depth -= 1
        if depth == 0: break
else: raise RuntimeError("Missing production authority method end")
generated = OUT / "GeneratedAuthority.cs"
generated.write_text("namespace BossRush { internal static class CoopAiPredictionHealthProtection {" + raw[start:end + 1] + "} }", encoding="utf-8")
production.append(authority)
sources = production[:-1] + [generated, HERE / "Stubs.cs", HERE / "Program.cs"]
includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
project = OUT / "Regression.csproj"
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
    '<LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
    '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
(OUT / "production-sha256.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
    for p in production}, indent=2), encoding="utf-8")
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture
code, output = run_project_fixture(project, OUT / "runs", ROOT)
print(output)
raise SystemExit(code)
