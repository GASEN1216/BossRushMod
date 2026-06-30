"""Guard: shared NPC follow base should cache its own Transform in frame paths."""

from pathlib import Path
import sys


SOURCE = Path("Integration/Utils/NPCFollowMovementBase.cs")


def fail(message: str) -> int:
    print("NPCFollowMovementBaseTransformCacheGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str | None:
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
                return text[start : idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")

    if "private Transform cachedSelfTransform;" not in text:
        return fail("missing cached self Transform field")

    self_transform = extract_block(text, "protected Transform SelfTransform")
    if self_transform is None:
        return fail("missing SelfTransform cache accessor")

    for token in [
        "cachedSelfTransform = transform;",
        "return cachedSelfTransform;",
    ]:
        if token not in self_transform:
            return fail("SelfTransform accessor missing token -> " + token)

    required_usages = [
        ("protected virtual Vector3 GetFollowDestination", "SelfTransform.position"),
        ("protected virtual bool TryRequestFollowPath", "SelfTransform.position"),
        ("protected virtual void TeleportToFollowPosition", "SelfTransform.position = targetPosition;"),
        ("private float GetDistanceToPlayerSqr", "SelfTransform.position"),
    ]
    for signature, token in required_usages:
        block = extract_block(text, signature)
        if block is None:
            return fail("missing method -> " + signature)
        if token not in block:
            return fail(signature + " should use cached self Transform token -> " + token)

    distance_block = extract_block(text, "private float GetDistanceToPlayerSqr")
    if distance_block is None:
        return fail("missing GetDistanceToPlayerSqr")
    if "transform.position" in distance_block:
        return fail("GetDistanceToPlayerSqr should not use direct transform.position")

    print("NPCFollowMovementBaseTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
