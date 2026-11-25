"""止语钟罩护罩规则执行回归：直接编译生产 SilenceBellRules.cs 与两份纯常量 Config（无 Unity 依赖）并跑断言。"""
from pathlib import Path
import os
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/SilenceBellRules"

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
    args += ['"' + str(p) + '"' for p in (ROOT / "Integration/NewWeapons/SilenceBell/SilenceBellRules.cs",
                                          ROOT / "Integration/NewWeapons/SilenceBell/SilenceBellConfig.cs",
                                          ROOT / "Integration/NewWeapons/ReloadMetronome/ReloadMetronomeConfig.cs",
                                          HERE / "Program.cs")]
    response = OUT / "compile.rsp"
    response.write_text("\n".join(args), encoding="utf-8-sig")
    code = subprocess.call(["dotnet", str(compiler), "@" + str(response)], cwd=ROOT)
    if code:
        raise SystemExit(code)
    raise SystemExit(subprocess.call([str(exe)], cwd=OUT))
