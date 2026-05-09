"""
Guard: death-position lootbox decoration must be processed by a shared bounded
queue, not by one physics-query coroutine per Boss death.
"""

from pathlib import Path
import sys


SOURCE = Path("Interactables/BossRushInteractables.cs")


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

    for required in (
        "LootboxDecorationRequest",
        "PendingLootboxDecorationRequests",
        "LootboxDecorationQueriesPerFrame",
        "lootboxDecorationWorkerRunning",
        "ProcessQueuedLootboxDecorations",
    ):
        if required not in text:
            return fail(f"LootboxDecorationQueueGuard: missing queued decoration invariant -> {required}")

    entry_body = extract_method_body(text, "internal static IEnumerator DecorateLootboxesNearPosition")
    if entry_body is None:
        return fail("LootboxDecorationQueueGuard: missing DecorateLootboxesNearPosition body")

    if "EnqueueLootboxDecorationRequest" not in entry_body:
        return fail("LootboxDecorationQueueGuard: DecorateLootboxesNearPosition does not enqueue requests")

    if "OverlapSphereNonAlloc" in entry_body:
        return fail("LootboxDecorationQueueGuard: DecorateLootboxesNearPosition still performs direct physics queries")

    worker_body = extract_method_body(text, "private static IEnumerator ProcessQueuedLootboxDecorations")
    if worker_body is None:
        return fail("LootboxDecorationQueueGuard: missing queued decoration worker body")

    for required in (
        "LootboxDecorationQueriesPerFrame",
        "PendingLootboxDecorationRequests.Dequeue()",
        "PendingLootboxDecorationRequests.Enqueue(request)",
    ):
        if required not in worker_body:
            return fail(f"LootboxDecorationQueueGuard: queued worker lacks -> {required}")

    process_body = extract_method_body(text, "private static bool TryProcessLootboxDecorationRequest")
    if process_body is None:
        return fail("LootboxDecorationQueueGuard: missing per-request decoration processor body")

    if "Physics.OverlapSphereNonAlloc" not in process_body:
        return fail("LootboxDecorationQueueGuard: per-request processor does not perform bounded physics query")

    print("LootboxDecorationQueueGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
