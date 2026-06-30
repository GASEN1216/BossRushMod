"""Guard: Phantom Witch ambient halo should avoid sqrt when camera is outside close range."""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchAmbientPresence.cs")


def fail(message: str) -> int:
    print("PhantomWitchAmbientHaloDistanceGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private float GetCloseCameraHaloBonus(Transform cameraTransform)")
    if body is None:
        return fail("missing GetCloseCameraHaloBonus body")

    if "Vector3.Distance(" in body:
        return fail("close camera bonus still uses Vector3.Distance before range rejection")

    required = [
        "float closeCameraDistanceSqr = CloseCameraDistance * CloseCameraDistance;",
        "float distanceSqr = delta.sqrMagnitude;",
        "if (distanceSqr >= closeCameraDistanceSqr)",
        "float distance = Mathf.Sqrt(distanceSqr);",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing squared close-camera snippet -> " + snippet)

    print("PhantomWitchAmbientHaloDistanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
