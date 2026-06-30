"""Guard: Mode F respawn should only compute fallback distance for the warning path."""

from pathlib import Path
import sys


SOURCE = Path("ModeF/ModeFRespawn.cs")


def fail(message: str) -> int:
    print("ModeFRespawnWarningDistanceGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private Vector3 FindSpawnPointAwayFromPlayer(")
    if body is None:
        return fail("missing FindSpawnPointAwayFromPlayer body")

    if "float resultDist = delta.magnitude;" in body:
        return fail("respawn path still computes magnitude before warning threshold")

    required = [
        "float resultDistSqr = delta.sqrMagnitude;",
        "float effectiveMinDistSqr = effectiveMinDist * effectiveMinDist;",
        "if (resultDistSqr < effectiveMinDistSqr)",
        "float resultDist = Mathf.Sqrt(resultDistSqr);",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing squared warning-distance snippet -> " + snippet)

    print("ModeFRespawnWarningDistanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
