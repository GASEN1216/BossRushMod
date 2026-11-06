"""Execute the actual F3 return method and Dev frame accumulator with controllable clocks and scene hosts."""
from pathlib import Path
import hashlib
import os
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import strip_comments

if __name__ == "__main__":
    out = ROOT / "Build/runtime-regressions/TidegateReturnAndFrames"
    out.mkdir(parents=True, exist_ok=True)
    source = strip_comments((ROOT / "DebugAndTools/F3DebugCheatMenuActions.cs").read_text("utf-8-sig"))
    sig = "private async void TeleportPlayerHomeToBaseScene()"
    start = source.index(sig)
    depth = 0
    for end in range(source.index("{", start), len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if not depth:
            break
    method = source[start:end+1]
    generated = out / "Return.cs"
    generated.write_text("using System; namespace BossRush {partial class Host {" + method + "}}", "utf-8")
    sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).splitlines()[-1]
    compiler = Path(sdk.split("[")[1].split("]")[0]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
    fx = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET/Framework64/v4.0.30319"
    exe = out / "frames.exe"
    args = ["/nologo", "/target:exe", "/langversion:7.3", "/define:BOSSRUSH_DEV", "/noconfig", "/nostdlib+", f'/out:"{exe}"']
    args += [f'/r:"{fx/name}"' for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    sources = [generated, ROOT / "Tidegate/TidegateFrameProbe.cs", HERE / "Program.cs"]
    args += [f'"{p}"' for p in sources]
    rsp = out / "compile.rsp"; rsp.write_text("\n".join(args), "utf-8-sig")
    subprocess.run(["dotnet", str(compiler), "@" + str(rsp)], check=True)
    print("production inputs SHA-256:", *(hashlib.sha256(p.read_bytes()).hexdigest() for p in sources[:2]), flush=True)
    sys.exit(subprocess.call([str(exe)]))
