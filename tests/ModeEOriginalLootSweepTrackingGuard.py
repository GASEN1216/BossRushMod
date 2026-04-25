"""
Guard: Mode E/F 保留原生掉落箱时，也必须把箱子登记到扫箱追踪。
"""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/LootAndRewards.cs")


def fail(message: str) -> int:
    print(message)
    return 1

def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    required = "StartCoroutine(BossRushLootboxUtility.DecorateLootboxesNearPosition(this, bossMain.transform.position, true));"
    if required not in text:
        return fail("ModeEOriginalLootSweepTrackingGuard: original Mode E/F lootboxes are not registered for sweep tracking")

    print("ModeEOriginalLootSweepTrackingGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
