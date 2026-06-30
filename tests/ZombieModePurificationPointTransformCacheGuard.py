"""Guard: Zombie purification point Update should use a cached Transform."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombiePurificationPointController.cs")


def fail(message: str) -> int:
    print("ZombieModePurificationPointTransformCacheGuard: FAIL - " + message)
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
    update_body = extract_method_body(text, "private void Update()")
    if update_body is None:
        return fail("missing Update body")

    required = [
        "private Transform cachedTransform;",
        "private void Awake()",
        "cachedTransform = transform;",
        "Transform pointTransform = cachedTransform;",
        "pointTransform.localScale = baseScale * pulse;",
        "pointTransform.Rotate(Vector3.up, 120f * Time.deltaTime, Space.World);",
        "Vector3 currentPosition = pointTransform.position;",
        "pointTransform.position = currentPosition;",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing cached Transform pattern -> " + snippet)

    forbidden = [
        "transform.localScale",
        "transform.Rotate(",
        "transform.position",
    ]
    for snippet in forbidden:
        if snippet in update_body:
            return fail("Update still uses direct Transform property access -> " + snippet)

    print("ZombieModePurificationPointTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
