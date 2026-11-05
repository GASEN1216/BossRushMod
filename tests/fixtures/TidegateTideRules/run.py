"""潮闸镇潮汐规则执行回归：直接编译生产 Tidegate/TidegateTideRules.cs（纯函数，无 Unity 依赖）并跑断言。"""
from pathlib import Path
import os
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/TidegateTideRules"

if __name__ == "__main__":
    if os.name != "nt":
        raise SystemExit("This fixture requires Windows .NET Framework.")
    sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).strip().splitlines()[-1]
    compiler = Path(sdk[sdk.index("[")+1:sdk.index("]")]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
    framework = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET/Framework64/v4.0.30319"
    OUT.mkdir(parents=True, exist_ok=True)
    exe = OUT / "Regression.exe"
    args = ["/nologo", "/target:exe", "/langversion:7.3", "/noconfig", "/nostdlib+", '/out:"' + str(exe) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    args += ['"' + str(p) + '"' for p in (ROOT / "Tidegate/TidegateTideRules.cs", HERE / "Program.cs")]
    response = OUT / "compile.rsp"
    response.write_text("\n".join(args), encoding="utf-8-sig")
    code = subprocess.call(["dotnet", str(compiler), "@" + str(response)], cwd=ROOT)
    if code:
        raise SystemExit(code)
    raise SystemExit(subprocess.call([str(exe)], cwd=OUT))
