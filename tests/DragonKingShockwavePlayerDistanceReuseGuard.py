"""Guard: Dragon King shockwave should compute player distance once per frame."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/DragonKingShockwaveEffect.cs")


def fail(message: str) -> int:
    print("DragonKingShockwavePlayerDistanceReuseGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private void Update()")
    if body is None:
        return fail("missing Update body")

    wave_loop = extract_method_body(body, "for (int i = 0; i < waveCount && i < waveRings.Count; i++)")
    if wave_loop is None:
        return fail("missing wave iteration loop")

    if "Vector2 playerOffset = new Vector2(playerPos.x - centerPosition.x, playerPos.z - centerPosition.z);" in wave_loop:
        return fail("Update still computes player offset inside each wave iteration")

    if "distanceToPlayerSqr" in wave_loop:
        return fail("Update still computes player distance inside each wave iteration")

    required = [
        "Vector2 playerOffset = new Vector2(playerPos.x - centerPosition.x, playerPos.z - centerPosition.z);",
        "float playerDistanceToCenterSqr = 0f;",
        "playerDistanceToCenterSqr = playerOffset.sqrMagnitude;",
        "if (waveRadiusSqr >= playerDistanceToCenterSqr)",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing per-frame player distance reuse snippet -> " + snippet)

    print("DragonKingShockwavePlayerDistanceReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
