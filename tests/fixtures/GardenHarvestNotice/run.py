"""Link the production harvest notice patch with real Harmony IL types and isolated host adapters."""
from pathlib import Path
import hashlib
import json
import os
import re
import subprocess
import sys
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/garden-harvest-notice"


def find_harmony():
    configured = os.environ.get("BOSSRUSH_HARMONY_DLL")
    if configured:
        path = Path(configured)
        if not path.is_file():
            raise SystemExit("BOSSRUSH_HARMONY_DLL does not exist: " + str(path))
        return path
    response = ROOT / "Build/BossRush.rsp"
    if response.is_file():
        for value in re.findall(r'^/reference:"([^"\r\n]*0Harmony\.dll)"',
                                response.read_text(encoding="utf-8-sig"), re.MULTILINE | re.IGNORECASE):
            path = Path(value)
            if path.is_file():
                return path
    raise SystemExit("Set BOSSRUSH_HARMONY_DLL to the installed game 0Harmony.dll (no game process is needed).")


def main():
    harmony = find_harmony()
    source = ROOT / "Integration/BackMountain/GardenHarvestNoticePatch.cs"
    files = [source, HERE / "Stubs.cs", HERE / "Program.cs"]
    OUT.mkdir(parents=True, exist_ok=True)
    quote = lambda value: escape(str(value), {'"': '&quot;'})
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + quote(path) + '" />' for path in files)
    project += '<Reference Include="0Harmony"><HintPath>' + escape(str(harmony)) + '</HintPath><Private>true</Private></Reference></ItemGroup></Project>'
    target = OUT / "Regression.csproj"
    target.write_text(project, encoding="utf-8")
    hashes = {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in files}
    hashes[str(harmony)] = hashlib.sha256(harmony.read_bytes()).hexdigest()
    (OUT / "source-hashes.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(target), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    sys.exit(main())
