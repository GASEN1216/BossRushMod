"""Guard: BossRushEventBus Publish must be safe for nested publishes."""

from pathlib import Path
import sys


SOURCE = Path("Common/Events/BossRushEventBus.cs")


def fail(message: str) -> int:
    print("BossRushEventBusReentrancyGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")

    required = [
        "private static int publishDepth = 0;",
        "Delegate[] snapshot = publishDepth == 0",
        "? CopySubscribersToSharedScratch(subscribers, count)",
        ": subscribers.ToArray();",
        "publishDepth++;",
        "finally",
        "publishDepth--;",
        "if (publishDepth == 0)",
        "snapshot[i] = null;",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing reentrancy-safe publish snippet -> " + snippet)

    if "subscribers.CopyTo(publishScratch, 0);" in text and "CopySubscribersToSharedScratch" not in text:
        return fail("shared scratch copy is still inline without reentrancy guard")

    print("BossRushEventBusReentrancyGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
