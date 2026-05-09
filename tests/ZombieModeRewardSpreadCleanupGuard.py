from pathlib import Path
import sys


EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    effects = EFFECTS.read_text(encoding="utf-8") if EFFECTS.exists() else ""
    if not effects:
        return fail("ZombieModeRewardSpreadCleanupGuard: missing ZombieModeRewardEffects.cs")

    required_tokens = [
        "private sealed class ZombieModeProjectileSpreadSnapshot",
        "private readonly System.Collections.Generic.Dictionary<int, ZombieModeProjectileSpreadSnapshot>",
        "CaptureZombieModeProjectileSpreadSnapshot",
        "item.GetInstanceID()",
        "RestoreZombieModeProjectileSpreadState()",
        "foreach (var pair in zombieModeProjectileSpreadSnapshots)",
        "zombieModeProjectileSpreadSnapshots.Clear();",
    ]
    for token in required_tokens:
        if token not in effects:
            return fail("ZombieModeRewardSpreadCleanupGuard: missing token -> " + token)

    banned_tokens = [
        "private int zombieModeSpreadLastHoldItemTypeId = -1;",
        "holdItem.TypeID != zombieModeSpreadLastHoldItemTypeId",
        "ContainsKey(item.TypeID)",
        "zombieModeProjectileSpreadSnapshots[item.TypeID]",
    ]
    for token in banned_tokens:
        if token in effects:
            return fail("ZombieModeRewardSpreadCleanupGuard: stale last-held-only cleanup logic still present -> " + token)

    print("ZombieModeRewardSpreadCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
