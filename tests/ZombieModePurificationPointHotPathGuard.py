"""Guard: Zombie purification point Update should keep per-star hot-path work bounded."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombiePurificationPointController.cs")


def fail(message: str) -> int:
    print("ZombieModePurificationPointHotPathGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private void Update()")
    helper = extract_method_body(text, "private static Transform GetCachedPlayerTransform()")
    if body is None:
        return fail("missing Update body")
    if helper is None:
        return fail("missing shared player transform helper")

    if "Vector3.Distance(" in body:
        return fail("Update still calls Vector3.Distance in the per-star hot path")
    if "CharacterMainControl.Main" in body:
        return fail("Update should not read CharacterMainControl.Main directly per active star")
    if text.count("CharacterMainControl.Main") != 1:
        return fail("CharacterMainControl.Main should appear exactly once in the shared helper")

    required = [
        "Transform playerTransform = GetCachedPlayerTransform();",
        "if (playerTransform != null)",
        "Vector3 playerPos = playerTransform.position;",
        "MAGNET_RADIUS_SQR",
        "PICKUP_DISTANCE_SQR",
        "distanceSqr <= MAGNET_RADIUS_SQR",
        "Mathf.Sqrt(distanceSqr)",
        "distanceToPlayer / MAGNET_RADIUS",
    ]
    for snippet in required:
        if snippet not in body and snippet not in text:
            return fail("missing squared-distance hot-path snippet -> " + snippet)

    required_helper = [
        "private static Transform cachedPlayerTransform;",
        "private static int cachedPlayerTransformFrame = -1;",
        "if (cachedPlayerTransformFrame != Time.frameCount)",
        "CharacterMainControl player = CharacterMainControl.Main;",
        "cachedPlayerTransform = player != null ? player.transform : null;",
        "cachedPlayerTransformFrame = Time.frameCount;",
        "return cachedPlayerTransform;",
    ]
    for snippet in required_helper:
        if snippet not in text:
            return fail("missing per-frame player transform cache snippet -> " + snippet)

    print("ZombieModePurificationPointHotPathGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
