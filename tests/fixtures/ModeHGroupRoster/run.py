"""执行真实鸭王杯群战的 Boss 池 / 左右两队抽取 / 名人堂排名；只替身宿主预设表与本地化边界。"""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build" / "runtime-regressions" / "ModeHGroupRoster"
PRODUCTION = ["ModeH/ModeHGroupRoster.cs", "ModeH/ModeHGroupHallOfFame.cs", "ModeH/ModeHStateDtos.cs"]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hashes = {p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest() for p in PRODUCTION}
    (OUT / "source-hashes.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    files = [HERE / "Program.cs", HERE / "Stubs.cs"] + [ROOT / p for p in PRODUCTION]
    project = ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
               '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><RollForward>Major</RollForward>'
               '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0169;0414</NoWarn>'
               '<NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>')
    project += "".join('<Compile Include="' + escape(str(p), {'"': "&quot;"}) + '" />' for p in files)
    project += "</ItemGroup></Project>"
    (OUT / "Regression.csproj").write_text(project, encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(OUT / "Regression.csproj"),
                            "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    sys.exit(main())
