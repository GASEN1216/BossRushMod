"""ZombieModeEnemyDropPickupBubbleGuard: inventory pickup should show a quality-colored player bubble."""

from pathlib import Path
import sys


DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")


def fail(message: str) -> int:
    print("ZombieModeEnemyDropPickupBubbleGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = DROPS.read_text(encoding="utf-8")

    required_tokens = [
        "TryShowZombieModeInventoryPickupPopText(player, itemName, itemQuality);",
        "player.PopText(message, -1f);",
        "搜到了<color=",
        "Found <color=",
        "GetZombieModeDropQualityColorHex",
    ]
    for token in required_tokens:
        if token not in text:
            return fail("missing pickup-bubble contract token -> " + token)

    print("ZombieModeEnemyDropPickupBubbleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
