"""Guard: Dragon King shockwave should fetch cached ring unit points once per update."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/DragonKingShockwaveEffect.cs")


def fail(message: str) -> int:
    print("DragonKingShockwaveRingUnitReuseGuard: FAIL - " + message)
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
    update_ring = extract_method_body(text, "private void UpdateRingPositions(WaveRing wave, Vector2[] unitPoints)")
    if update_body is None:
        return fail("missing Update body")
    if update_ring is None:
        return fail("missing UpdateRingPositions overload with unitPoints")

    old_update_ring = extract_method_body(text, "private void UpdateRingPositions(WaveRing wave)")
    if old_update_ring is not None and "GetCachedRingUnitPoints()" in old_update_ring:
        return fail("UpdateRingPositions still fetches cached unit points per ring")

    required = [
        "Vector2[] ringUnitPoints = GetCachedRingUnitPoints();",
        "UpdateRingPositions(wave, ringUnitPoints);",
        "UpdateRingPositions(wave, GetCachedRingUnitPoints());",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing ring unit reuse snippet -> " + snippet)

    if "UpdateRingPositions(wave);" in text:
        return fail("call sites still use the no-cache UpdateRingPositions overload")

    print("DragonKingShockwaveRingUnitReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
