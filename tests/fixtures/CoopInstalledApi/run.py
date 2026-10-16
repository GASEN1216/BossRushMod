"""Read-only installed workshop API contract and raw payload probe; never starts Unity."""
from pathlib import Path
import os
import subprocess
import hashlib
import shutil
import tempfile
import sys
import argparse
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--output')
options = parser.parse_args()
OUT = Path(options.output).resolve() if options.output else ROOT / "Build/runtime-regressions/CoopInstalledApi"
OUT.mkdir(parents=True, exist_ok=True)
project = OUT / "Regression.csproj"
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="' + escape(str(HERE / "Program.cs"), {'"': '&quot;'}) + '" /></ItemGroup></Project>', encoding="utf-8")
workshop = Path(os.environ.get("BOSSRUSH_COOP_WORKSHOP_PATH", "D:/software/steam/steamapps/workshop/content/3167020/3591341282"))
game = Path(os.environ.get("GAME_PATH", "D:/software/steam/steamapps/common/Escape from Duckov")) / "Duckov_Data/Managed"
dependency = Path(os.environ.get("BOSSRUSH_COOP_DEPENDENCY_PATH", str(workshop.parent / "3591339491")))
if not (workshop / "EscapeFromDuckovModApi.dll").is_file() or not game.is_dir():
    raise SystemExit("Installed API probe unavailable: set BOSSRUSH_COOP_WORKSHOP_PATH and GAME_PATH to DLL directories.")
result = subprocess.call(["dotnet", "build", str(project), "--configuration", "Release", "--nologo"], cwd=ROOT)
if result:
    raise SystemExit(result)
target = subprocess.check_output(["dotnet", "msbuild", str(project), "-property:Configuration=Release", "-getProperty:TargetPath"], cwd=ROOT, text=True).strip()
result = subprocess.call(["dotnet", target, str(workshop), str(game), str(dependency)], cwd=ROOT)
if result:
    raise SystemExit(result)

# Real Harmony executes the entire production Transport, with only world/UI/API
# surfaces isolated. Installed SceneNet authorization IL is also executed directly.
if os.name != "nt":
    raise SystemExit("Native scene lease execution requires Windows .NET Framework.")
harmony = Path(os.environ.get("BOSSRUSH_HARMONY_DLL", "D:/software/steam/steamapps/workshop/content/3167020/3588386576/0Harmony.dll"))
if not harmony.is_file():
    raise SystemExit("Missing BOSSRUSH_HARMONY_DLL: " + str(harmony))
fresh = Path(tempfile.mkdtemp(prefix="scene-lease-", dir=OUT))
shutil.copy2(harmony, fresh / "0Harmony.dll")
sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).strip().splitlines()[-1]
compiler = Path(sdk[sdk.index("[")+1:sdk.index("]")]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
framework = Path(os.environ.get("WINDIR", "C:/Windows")) / "Microsoft.NET/Framework64/v4.0.30319"
sources = [ROOT / "Common/Coop/BossRushCoopTransport.cs", ROOT / "Common/Coop/BossRushCoopSceneIdentity.cs",
    ROOT / "Common/Coop/BossRushCoopJson.cs", ROOT / "Common/Data/BossRushJsonValue.cs", ROOT / "Utilities/SimpleJsonHelper.cs",
    HERE / "SceneLoadLease/Stubs.cs", HERE / "SceneLoadLease/Program.cs", HERE / "SceneLoadLease/TransportWriterCases.cs"]
target = fresh / "SceneLoadLease.exe"
args = ["/nologo", "/target:exe", "/langversion:7.3", "/noconfig", "/nostdlib+", '/out:"' + str(target) + '"', '/r:"' + str(harmony) + '"']
args += ['/r:"' + str(framework / name) + '"' for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
args += ['"' + str(path) + '"' for path in sources]
response = fresh / "compile.rsp"
response.write_text("\n".join(args), encoding="utf-8-sig")
(fresh / "source-sha256.txt").write_text("\n".join(str(path) + " " + hashlib.sha256(path.read_bytes()).hexdigest() for path in sources + [harmony, workshop / "EscapeFromDuckovCoopMod.dll"]), encoding="utf-8")
result = subprocess.call(["dotnet", str(compiler), "@" + str(response)], cwd=ROOT)
if result:
    raise SystemExit(result)
print("Execute fresh TargetPath:", target, flush=True)
print("SHA-256:", hashlib.sha256(target.read_bytes()).hexdigest(), flush=True)
result = subprocess.call([str(target), str(workshop), str(game), str(dependency)], cwd=fresh)
if result:
    raise SystemExit(result)
for group in ('SpectatorRegistration', 'SceneIdentity'):
    result = subprocess.call([sys.executable, str(HERE / group / 'run.py'), '--output', str(OUT)], cwd=ROOT)
    if result:
        raise SystemExit(result)
