"""CR-2026-09-05-020: real Harmony patches in an isolated Windows .NET Framework process.

Requires the same installed Harmony and Windows .NET SDK as the official build.
Override BOSSRUSH_HARMONY_DLL to run against another compatible local installation.
No game process or player save is accessed; all outputs stay under Build/.
"""
from pathlib import Path
import hashlib
import os
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/harmony-binding-second-review-fixture"

if __name__ == "__main__":
    if os.name != "nt":
        raise SystemExit("This execution fixture requires Windows .NET Framework and the installed game Harmony.")
    harmony = Path(os.environ.get("BOSSRUSH_HARMONY_DLL",
        r"D:\software\steam\steamapps\workshop\content\3167020\3588386576\0Harmony.dll"))
    if not harmony.is_file():
        raise SystemExit("Set BOSSRUSH_HARMONY_DLL to the installed 0Harmony.dll: " + str(harmony))
    sdk_lines = subprocess.check_output(["dotnet", "--list-sdks"], text=True).strip().splitlines()
    sdk = sdk_lines[-1]
    compiler = Path(sdk[sdk.index("[") + 1:sdk.index("]")]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
    framework = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET/Framework64/v4.0.30319"
    OUT.mkdir(parents=True, exist_ok=True)
    shutil.copy2(harmony, OUT / "0Harmony.dll")
    source = ROOT / "Common/Infrastructure/HarmonyBindingSelfCheck.cs"
    exe = OUT / "HarmonyBinding.exe"
    args = ["/nologo", "/target:exe", "/langversion:7.3", "/noconfig", "/nostdlib+",
            '/out:"' + str(exe) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    args += ['"' + str(source) + '"', '"' + str(HERE / "Program.cs") + '"']
    response = OUT / "compile.rsp"
    response.write_text("\n".join(args), encoding="utf-8-sig")
    (OUT / "source.sha256.txt").write_text(
        "\n".join(hashlib.sha256(p.read_bytes()).hexdigest() + "  " + str(p) for p in (source, harmony)), encoding="utf-8")
    result = subprocess.call(["dotnet", str(compiler), "@" + str(response)], cwd=ROOT)
    if result:
        raise SystemExit(result)
    raise SystemExit(subprocess.call([str(exe)], cwd=OUT))
