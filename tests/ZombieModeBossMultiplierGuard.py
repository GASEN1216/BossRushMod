"""ZombieModeBossMultiplierGuard: 禁止 Health.defaultMaxHealth 反射写。"""
from pathlib import Path
import sys


def fail(msg: str) -> int:
    print(msg)
    return 1


def main() -> int:
    for path in Path("ZombieMode").glob("*.cs"):
        text = path.read_text(encoding="utf-8")
        if "defaultMaxHealth" in text:
            return fail("ZombieModeBossMultiplierGuard: " + str(path) + " 仍在引用私有字段 defaultMaxHealth")
        if "GetField(typeof(Health)" in text:
            return fail("ZombieModeBossMultiplierGuard: " + str(path) + " 仍在反射 Health 字段")
    return 0


if __name__ == "__main__":
    sys.exit(main())
