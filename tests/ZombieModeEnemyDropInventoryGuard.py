"""ZombieModeEnemyDropInventoryGuard: enemy drops should go to player inventory first."""

from pathlib import Path
import sys


DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")


def fail(message: str) -> int:
    print("ZombieModeEnemyDropInventoryGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = DROPS.read_text(encoding="utf-8")

    if "private GameObject TryDropZombieModeItemNearPosition" not in text:
        return fail("TryDropZombieModeItemNearPosition not found")

    required_tokens = [
        "player.CharacterItem.Inventory.AddAndMerge(item, 0)",
        "item.Drop(player, true);",
        "RegisterZombieModeDropCandidate(runId, obj, highValue, bossDrop);",
    ]
    for token in required_tokens:
        if token not in text:
            return fail("missing required drop contract token -> " + token)

    forbidden_tokens = [
        "item.Drop(position + Vector3.up * 0.35f, true, UnityEngine.Random.insideUnitSphere.normalized, bossDrop ? 30f : 18f);",
    ]
    for token in forbidden_tokens:
        if token in text:
            return fail("enemy drop still falls back to corpse-position direct drop -> " + token)

    print("ZombieModeEnemyDropInventoryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
