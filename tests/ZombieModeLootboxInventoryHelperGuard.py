"""ZombieModeLootboxInventoryHelperGuard: lootbox local-inventory wiring should reuse a shared helper."""

from pathlib import Path
import sys


DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
HELPER = Path("Utilities/InteractableLootboxInventoryHelper.cs")
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print("ZombieModeLootboxInventoryHelperGuard: FAIL - " + message)
    return 1


def main() -> int:
    drops = DROPS.read_text(encoding="utf-8")
    helper = HELPER.read_text(encoding="utf-8") if HELPER.exists() else ""
    compile_text = COMPILE.read_text(encoding="utf-8")

    if "InteractableLootboxInventoryHelper.EnsureLocalInventory(lootbox" not in drops:
        return fail("ZombieMode must use shared InteractableLootboxInventoryHelper")

    forbidden_tokens = [
        "\"CreateLocalInventory\"",
        "\"inventoryReference\"",
        "GetField(",
        "GetMethod(",
    ]
    for token in forbidden_tokens:
        if token in drops:
            return fail("ZombieMode lootbox wiring still contains local reflection -> " + token)

    if "internal static class InteractableLootboxInventoryHelper" not in helper:
        return fail("shared lootbox inventory helper missing")

    if "Utilities\\InteractableLootboxInventoryHelper.cs ^" not in compile_text:
        return fail("compile_official.bat must include Utilities\\InteractableLootboxInventoryHelper.cs")

    print("ZombieModeLootboxInventoryHelperGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
