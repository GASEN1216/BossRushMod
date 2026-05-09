"""PerformanceTierAdjusterGuard: ZombieMode must not scale gameplay by performance tier."""

from pathlib import Path
import sys


HELPER = Path("Utilities/PerformanceTierAdjuster.cs")
COMPILE_SCRIPT = Path("compile_official.bat")
PRODUCTION_ROOTS = [Path("ZombieMode"), Path("Utilities")]


def fail(message: str) -> int:
    print("PerformanceTierAdjusterGuard: FAIL - " + message)
    return 1


def main() -> int:
    if HELPER.exists():
        return fail("PerformanceTierAdjuster helper should be removed")

    compile_text = COMPILE_SCRIPT.read_text(encoding="utf-8", errors="ignore")
    if "PerformanceTierAdjuster.cs" in compile_text:
        return fail("compile list still references PerformanceTierAdjuster.cs")

    for root in PRODUCTION_ROOTS:
        if not root.exists():
            continue
        for path in root.rglob("*.cs"):
            text = path.read_text(encoding="utf-8", errors="ignore")
            if "PerformanceTierAdjuster" in text:
                return fail("production code still references PerformanceTierAdjuster -> " + str(path))

    print("PerformanceTierAdjusterGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
