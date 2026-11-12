"""末班编组站 Dev 演练的纯判据执行回归（2026-11-11）。

把 `DebugAndTools/F3GameplayValidationLastShiftDrill.cs` 的 `#region 纯判据` 逐字抽出来，与生产
`LastShiftYard/LastShiftTimetable.cs`（纯规则，无 Unity）一起编译执行：不启动 Unity、不碰存档。纯判据区一旦引用 Unity 或运行时对象，
这里直接失败——那一区的全部意义就是能在这里跑。判据的输入尽量由生产班次表真实推进得出（首班等人、教学停车、拉铃 14 秒、停靠车厢）。

入口：`python tools/run_runtime_regressions.py --filter LastShiftDrillJudges`。
"""
from pathlib import Path
import os
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/LastShiftDrillJudges"


def pure_region(source):
    start = source.index("#region 纯判据")
    body_start = source.index("\n", start) + 1
    end = source.index("#endregion", body_start)
    return source[body_start:end]


if __name__ == "__main__":
    if os.name != "nt":
        raise SystemExit("This fixture requires Windows .NET Framework.")
    drill = (ROOT / "DebugAndTools/F3GameplayValidationLastShiftDrill.cs").read_text(encoding="utf-8-sig")
    judges = pure_region(drill)
    for token in ("UnityEngine", "Mathf.", "GameObject", "Transform", "Vector3", "LastShiftSession", "LastShiftTrains", "Time.",
                  "CharacterMainControl", "Physics."):
        if re.search(r"(?<![\w.])" + re.escape(token), judges):
            raise SystemExit("纯判据区引用了 Unity 或运行时对象（" + token + "）：它必须能脱离游戏执行")
    OUT.mkdir(parents=True, exist_ok=True)
    generated = OUT / "Judges.cs"
    generated.write_text("using System;\nusing System.Collections.Generic;\n\nnamespace BossRush\n{\n"
                         "    internal sealed partial class F3GameplayValidationRunner\n    {\n" + judges + "    }\n}\n",
                         encoding="utf-8")
    sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).strip().splitlines()[-1]
    compiler = Path(sdk[sdk.index("[") + 1:sdk.index("]")]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
    framework = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET/Framework64/v4.0.30319"
    exe = OUT / "Regression.exe"
    args = ["/nologo", "/target:exe", "/langversion:7.3", "/define:BOSSRUSH_DEV", "/noconfig", "/nostdlib+", '/out:"' + str(exe) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    args += ['"' + str(p) + '"' for p in (ROOT / "LastShiftYard/LastShiftTimetable.cs", generated, HERE / "Program.cs")]
    response = OUT / "compile.rsp"
    response.write_text("\n".join(args), encoding="utf-8-sig")
    code = subprocess.call(["dotnet", str(compiler), "@" + str(response)], cwd=ROOT)
    if code:
        raise SystemExit(code)
    raise SystemExit(subprocess.call([str(exe)], cwd=OUT))
