"""ZombieModeTimeAxisGuard: 丧尸模式定时逻辑统一 unscaled 时间轴。"""
from pathlib import Path
import re
import sys


def fail(msg: str) -> int:
    print(msg)
    return 1


def main() -> int:
    # TickZombieMode 调用必须传 unscaledDeltaTime
    mod_text = Path("ModBehaviour.cs").read_text(encoding="utf-8")
    if not re.search(r"TickZombieMode\(Time\.unscaledDeltaTime\)", mod_text):
        return fail("ZombieModeTimeAxisGuard: TickZombieMode 调用未使用 Time.unscaledDeltaTime")
    if re.search(r"TickZombieMode\(Time\.deltaTime\)", mod_text):
        return fail("ZombieModeTimeAxisGuard: TickZombieMode 仍在使用 Time.deltaTime")

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
