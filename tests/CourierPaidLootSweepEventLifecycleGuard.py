"""
Guard: CourierPaidLootSweepService.ResetStaticCaches must unregister static events.
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/NPCs/Courier/CourierPaidLootSweepService.cs")


def fail(message: str) -> int:
    print("CourierPaidLootSweepEventLifecycleGuard: " + message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""
    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    block = extract_block(text, "public static void ResetStaticCaches()")
    if not block:
        return fail("missing ResetStaticCaches block")

    unsubscribe = "InteractableLootbox.OnStopLoot -= OnLootboxStopLoot"
    flag_reset = "lootStopHookRegistered = false"
    generation_bump = "serviceGeneration++"
    if unsubscribe not in block:
        return fail("ResetStaticCaches must unsubscribe InteractableLootbox.OnStopLoot")
    if block.find(unsubscribe) > block.find(flag_reset):
        return fail("event unsubscribe must happen before lootStopHookRegistered=false")
    if generation_bump not in block:
        return fail("ResetStaticCaches must increment serviceGeneration to invalidate in-flight async prompts")
    if "ReleasePendingSweepResultToPlayer(true, false)" not in block:
        return fail("ResetStaticCaches must safely return pending sweep results before clearing references")
    if "ExitServiceState()" not in block:
        return fail("ResetStaticCaches must exit active service state before nulling NPC references")

    print("CourierPaidLootSweepEventLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
