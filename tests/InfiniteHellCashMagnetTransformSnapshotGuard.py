"""Guard: Infinite Hell cash magnet should snapshot transforms in the flying hot path."""

from pathlib import Path
import sys


SOURCE = Path("WavesArena/InfiniteHellCashMagnet.cs")


def fail(message: str) -> int:
    print("InfiniteHellCashMagnetTransformSnapshotGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8-sig")
    body = extract_method_body(text, "private void UpdateFlyingCashPickups(CharacterMainControl player)")
    if body is None:
        return fail("missing UpdateFlyingCashPickups body")

    for token in [
        "Transform playerTransform = player.transform;",
        "Vector3 playerPos = playerTransform.position;",
        "Transform pickupTransform = pickup.transform;",
        "Vector3 currentPos = pickupTransform.position;",
        "pickupTransform.position = newPos;",
        "DialogueBubblesManager.Show(bubbleText, playerTransform,",
    ]:
        if token not in body:
            return fail("missing transform snapshot token -> " + token)

    for forbidden in [
        "Vector3 playerPos = player.transform.position;",
        "pickup.transform.position",
        "DialogueBubblesManager.Show(bubbleText, player.transform,",
    ]:
        if forbidden in body:
            return fail("hot path still uses direct transform access -> " + forbidden)

    print("InfiniteHellCashMagnetTransformSnapshotGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
