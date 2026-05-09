"""
Guard: Awen sweep token availability checks should use the short-lived cached
target count, while actual activation still performs a fresh target collection.
"""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/ModeEFLootboxTracker.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")

    can_use_body = extract_method_body(text, "internal bool CanUseAwenLootSweepToken(CharacterMainControl player, bool showFailureFeedback)")
    if can_use_body is None:
        return fail("AwenLootSweepCachedCanUseGuard: missing CanUseAwenLootSweepToken body")
    if "GetCurrentAwenLootSweepTargetCount()" not in can_use_body:
        return fail("AwenLootSweepCachedCanUseGuard: CanUseAwenLootSweepToken does not use cached target count")
    if "CopyFreshAwenLootSweepTargets(" in can_use_body:
        return fail("AwenLootSweepCachedCanUseGuard: CanUseAwenLootSweepToken still performs fresh O(n^2) target collection")

    activate_body = extract_method_body(text, "internal bool TryActivateAwenLootSweepToken")
    if activate_body is None:
        return fail("AwenLootSweepCachedCanUseGuard: missing TryActivateAwenLootSweepToken body")
    if "CopyFreshAwenLootSweepTargets(" not in activate_body:
        return fail("AwenLootSweepCachedCanUseGuard: activation no longer performs fresh target collection")

    print("AwenLootSweepCachedCanUseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
