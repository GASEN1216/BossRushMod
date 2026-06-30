"""Guard: Infinite Hell cash magnet hot path must avoid per-frame allocations."""

from pathlib import Path
import re
import sys


SOURCE = Path("WavesArena/InfiniteHellCashMagnet.cs")


def fail(message: str) -> int:
    print("InfiniteHellCashMagnetNoAllocGuard: FAIL - " + message)
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

    detect_body = extract_method_body(
        text,
        "private void DetectNearbyCashPickups(Vector3 playerPos)",
    )
    if detect_body is None:
        return fail("missing DetectNearbyCashPickups body")

    if "Physics.OverlapSphereNonAlloc(" not in detect_body:
        return fail("cash pickup scan must use Physics.OverlapSphereNonAlloc")

    if re.search(r"Physics\.OverlapSphere\s*\(", detect_body):
        if "ProcessCashMagnetFallbackColliders(" not in detect_body:
            return fail("allocating OverlapSphere is only allowed behind the full-buffer fallback helper")

    if "cashMagnetColliderBuffer" not in text:
        return fail("missing reusable collider buffer")

    if "cashMagnetPickupsToRemove" not in text:
        return fail("missing reusable removal buffer")

    update_body = extract_method_body(
        text,
        "private void UpdateFlyingCashPickups(CharacterMainControl player)",
    )
    if update_body is None:
        return fail("missing UpdateFlyingCashPickups body")

    if re.search(r"new\s+List\s*<\s*InteractablePickup\s*>\s*\(", update_body):
        return fail("UpdateFlyingCashPickups must not allocate a removal list per frame")

    if "sqrMagnitude" not in update_body or "CashMagnetPickupDistanceSqr" not in text:
        return fail("pickup distance check should avoid Vector3.Distance sqrt in the hot path")

    print("InfiniteHellCashMagnetNoAllocGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
