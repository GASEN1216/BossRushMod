"""PerformanceTierAdjusterGuard: 通用性能层级判定的纯函数 invariant。"""

from pathlib import Path
import sys


HELPER = Path("Utilities/PerformanceTierAdjuster.cs")
ZOMBIE_DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    helper = HELPER.read_text(encoding="utf-8")
    drops = ZOMBIE_DROPS.read_text(encoding="utf-8")

    for snippet in [
        "internal static class PerformanceTierAdjuster",
        "internal enum Tier",
        "Normal = 0",
        "ExtremeProtect = 3",
        "internal struct Thresholds",
        "internal static Tier Evaluate(",
    ]:
        if snippet not in helper:
            return fail("PerformanceTierAdjusterGuard: helper missing -> " + snippet)

    if "PerformanceTierAdjuster.Evaluate(" not in drops:
        return fail("PerformanceTierAdjusterGuard: ZombieMode 必须复用 PerformanceTierAdjuster.Evaluate")

    if "count >= ZombieModeTuning.PerfTierExtreme" in drops:
        return fail("PerformanceTierAdjusterGuard: ZombieMode 仍保留旧的阶梯式 if/else，未走 helper")

    print("PerformanceTierAdjusterGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
