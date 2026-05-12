"""ZombieModeTimeAxisGuard: 丧尸模式定时逻辑统一 unscaled 时间轴。"""
from pathlib import Path
import re
import sys


def fail(msg: str) -> int:
    print(msg)
    return 1


def main() -> int:
    mod_text = Path("ModBehaviour.cs").read_text(encoding="utf-8")
    mode_runtime_text = Path("Utilities/ModeRuntimeHooks.cs").read_text(encoding="utf-8")
    zombie_runtime_text = Path("ZombieMode/ZombieModeRuntimeHooks.cs").read_text(encoding="utf-8")
    if "TickModeRuntimeGroup(Time.deltaTime, Time.unscaledDeltaTime)" not in mod_text:
        return fail("ZombieModeTimeAxisGuard: ModBehaviour.Update 未传递 Time.unscaledDeltaTime 到 mode runtime group")
    if "TickZombieModeRuntime(unscaledDeltaTime);" not in mode_runtime_text:
        return fail("ZombieModeTimeAxisGuard: mode runtime group 未使用 unscaledDeltaTime tick ZombieMode")
    if "TickZombieMode(unscaledDeltaTime);" not in zombie_runtime_text:
        return fail("ZombieModeTimeAxisGuard: TickZombieMode 调用未使用 unscaledDeltaTime")
    for text in [mod_text, mode_runtime_text, zombie_runtime_text]:
        if re.search(r"TickZombieMode(Runtime)?\(Time\.deltaTime\)", text):
            return fail("ZombieModeTimeAxisGuard: TickZombieMode 仍在使用 Time.deltaTime")
        if re.search(r"TickZombieModeRuntime\(deltaTime\)", text):
            return fail("ZombieModeTimeAxisGuard: TickZombieModeRuntime 仍在使用 scaled deltaTime")

    # ZombieMode/*.cs 内的 Time.time / Time.deltaTime 必须带 // scaled-ok 注释（仅允许 ZombiePurificationPointController.cs 的视觉旋转/寿命）
    for path in Path("ZombieMode").glob("*.cs"):
        if path.name == "ZombiePurificationPointController.cs":
            continue  # 视觉效果允许跟随暂停
        text = path.read_text(encoding="utf-8")
        for line_no, line in enumerate(text.splitlines(), 1):
            if "// scaled-ok" in line:
                continue
            if re.search(r"\bTime\.deltaTime\b", line):
                return fail("ZombieModeTimeAxisGuard: " + str(path) + ":" + str(line_no) + " 用了 Time.deltaTime（应改为 Time.unscaledDeltaTime 或加 // scaled-ok 注释）")
            if re.search(r"\bTime\.time\b(?!Sc)", line):
                return fail("ZombieModeTimeAxisGuard: " + str(path) + ":" + str(line_no) + " 用了 Time.time（应改为 Time.unscaledTime 或加 // scaled-ok 注释）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
