"""Execute the actual independent-spouse world and factory, using in-memory Unity/API substitutes."""
from pathlib import Path
from xml.sax.saxutils import escape
import hashlib
import json
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/CoopSpouses"

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    production = [ROOT / p for p in (
        "Integration/NPCs/Common/CoopNpcWorld.cs",
        "Integration/NPCs/Common/CoopNpcWorld_Spouses.cs",
    )]
    files = production + [HERE / p for p in ("Stubs.cs", "Async.cs", "Program.cs")]
    files += [ROOT / p for p in ("Common/Coop/BossRushCoopJson.cs", "Common/Data/BossRushJsonValue.cs", "Utilities/SimpleJsonHelper.cs")]
    (OUT / "source-hashes.json").write_text(json.dumps({p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in production}, indent=2), encoding="utf-8")
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414;0162</NoWarn></PropertyGroup><ItemGroup>' + ''.join('<Compile Include="' + escape(str(p)) + '" />' for p in files) + '</ItemGroup></Project>', encoding="utf-8")
    sys.path.insert(0, str(ROOT / "tools"))
    from run_runtime_regressions import run_project_fixture
    result, output = run_project_fixture(project, OUT, ROOT)
    print(output)
    return result

if __name__ == "__main__":
    raise SystemExit(main())
