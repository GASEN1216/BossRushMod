"""Guard: Mode F bounty radar should reuse camera basis per refresh."""

from pathlib import Path
import sys


SOURCE = Path("ModeF/ModeFUI_BountyRadarAndHealthBars.cs")


def fail(message: str) -> int:
    print("ModeFBountyRadarBasisReuseGuard: FAIL - " + message)
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
    update_ui = extract_method_body(text, "private void UpdateModeFBountyRadarUI()")
    update_entry = extract_method_body(text, "private void UpdateModeFBountyRadarEntry(")
    direction = extract_method_body(text, "private static Vector2 GetModeFBountyRadarDirection(")
    basis = extract_method_body(text, "private static void GetModeFBountyRadarBasis(")
    if update_ui is None:
        return fail("missing UpdateModeFBountyRadarUI body")
    if update_entry is None:
        return fail("missing UpdateModeFBountyRadarEntry body")
    if direction is None:
        return fail("missing GetModeFBountyRadarDirection body")
    if basis is None:
        return fail("missing GetModeFBountyRadarBasis body")

    if "radarCamera.transform" in update_entry:
        return fail("entry update still reads camera transform per target")

    if "forward.Normalize();" in direction or "right.Normalize();" in direction:
        return fail("direction helper still normalizes camera basis per target")

    required_update = [
        "Vector3 radarForward;",
        "Vector3 radarRight;",
        "GetModeFBountyRadarBasis(radarCamera.transform, out radarForward, out radarRight);",
        "radarForward,",
        "radarRight);",
    ]
    for snippet in required_update:
        if snippet not in update_ui:
            return fail("missing per-refresh basis snippet -> " + snippet)

    required_entry_signature = [
        "Vector3 radarForward,",
        "Vector3 radarRight)",
    ]
    for snippet in required_entry_signature:
        if snippet not in text:
            return fail("missing entry basis parameter snippet -> " + snippet)

    required_entry_body = [
        "Vector2 direction = GetModeFBountyRadarDirection(playerPos, targetPos, radarForward, radarRight);",
    ]
    for snippet in required_entry_body:
        if snippet not in update_entry:
            return fail("missing entry basis reuse snippet -> " + snippet)

    required_basis = [
        "radarForward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;",
        "radarForward.Normalize();",
        "radarRight = cameraTransform != null ? cameraTransform.right : Vector3.right;",
        "radarRight.Normalize();",
    ]
    for snippet in required_basis:
        if snippet not in basis:
            return fail("missing basis helper snippet -> " + snippet)

    required_direction = [
        "Vector3.Dot(toTarget, radarRight)",
        "Vector3.Dot(toTarget, radarForward)",
        "float invDirectionMagnitude = 1f / Mathf.Sqrt(directionSqr);",
    ]
    for snippet in required_direction:
        if snippet not in direction:
            return fail("missing direction reuse snippet -> " + snippet)

    print("ModeFBountyRadarBasisReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
