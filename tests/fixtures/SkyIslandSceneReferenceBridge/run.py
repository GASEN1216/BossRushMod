"""真实 Harmony + 生产天空岛桥接执行回归，Unity/场景API为隔离替身；不访问玩家存档。"""
from pathlib import Path
import os
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/SkyIslandSceneReferenceBridge"

if __name__ == "__main__":
    if os.name != "nt":
        raise SystemExit("This fixture requires Windows .NET Framework and installed Harmony.")
    harmony = Path(os.environ.get("BOSSRUSH_HARMONY_DLL",
        r"D:\software\steam\steamapps\workshop\content\3167020\3588386576\0Harmony.dll"))
    if not harmony.is_file():
        raise SystemExit("Set BOSSRUSH_HARMONY_DLL to installed 0Harmony.dll")
    sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).strip().splitlines()[-1]
    compiler = Path(sdk[sdk.index("[")+1:sdk.index("]")]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
    framework = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET/Framework64/v4.0.30319"
    OUT.mkdir(parents=True, exist_ok=True)
    shutil.copy2(harmony, OUT / "0Harmony.dll")
    exe = OUT / "Regression.exe"
    args = ["/nologo", "/target:exe", "/langversion:7.3", "/noconfig", "/nostdlib+",
            '/out:"' + str(exe) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    args += ['"' + str(p) + '"' for p in (
        ROOT / "DebugAndTools/SkyIsland/SkyIslandSceneReferenceBridge.cs", HERE / "Host.cs", HERE / "Program.cs")]
    response = OUT / "compile.rsp"
    response.write_text("\n".join(args), encoding="utf-8-sig")
    code = subprocess.call(["dotnet", str(compiler), "@" + str(response)], cwd=ROOT)
    if code:
        raise SystemExit(code)
    raise SystemExit(subprocess.call([str(exe)], cwd=OUT))
