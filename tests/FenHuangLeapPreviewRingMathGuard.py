"""
Guard: FenHuang leap preview ring should reuse constant ring and pulse math.

Reason:
- The landing-preview ring uses a fixed number of segments and only changes
  position/radius/color while previewing.
- Rebuilding segment rotations for each preview refresh and calculating the
  same sine twice per frame adds avoidable visual-update CPU work.

Requirement:
- UpdateLandingRing must use cached MarkerUnitOffsets.
- UpdateLandingRing must not call Quaternion.Euler.
- Update must call Mathf.Sin(pulseTime) only once and reuse pulseSin for the
  ring pulse, ban icon pulse, and marker light pulse.
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/DragonKing/Weapons/FenHuangHalberdAbilityManager.cs")


def fail(message: str) -> int:
    print("FenHuangLeapPreviewRingMathGuard: FAIL - " + message)
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
    ring_body = extract_method_body(text, "private void UpdateLandingRing(Vector3 landingPoint, bool valid)")
    if update_body is None:
        return fail("missing FenHuangLeapPreview.Update body")
    if ring_body is None:
        return fail("missing UpdateLandingRing body")

    if "Quaternion.Euler" in ring_body:
        return fail("UpdateLandingRing still rebuilds marker rotations")

    if update_body.count("Mathf.Sin(pulseTime)") != 1:
        return fail("Update should call Mathf.Sin(pulseTime) exactly once")

    required_text = [
        "private static readonly Vector3 MarkerHeightOffset = Vector3.up * 0.05f;",
        "private static readonly Vector3[] MarkerUnitOffsets = BuildMarkerUnitOffsets();",
        "private static Vector3[] BuildMarkerUnitOffsets()",
    ]
    for snippet in required_text:
        if snippet not in text:
            return fail("missing cached marker offset snippet -> " + snippet)

    required_update = [
        "float pulseSin = Mathf.Sin(pulseTime);",
        "float pulse = 1f + pulseSin * 0.08f;",
        "markerLight.intensity = 2.2f + pulseSin * 0.35f;",
    ]
    for snippet in required_update:
        if snippet not in update_body:
            return fail("missing pulse reuse snippet -> " + snippet)

    required_ring = [
        "landingRing.positionCount = MarkerUnitOffsets.Length;",
        "Vector3 offset = MarkerUnitOffsets[i] * radius;",
        "landingRing.SetPosition(i, landingPoint + offset + MarkerHeightOffset);",
    ]
    for snippet in required_ring:
        if snippet not in ring_body:
            return fail("missing cached marker ring snippet -> " + snippet)

    if not re.search(r"for\s*\(\s*int\s+i\s*=\s*0\s*;\s*i\s*<\s*MarkerUnitOffsets\.Length\s*;\s*i\+\+\s*\)", ring_body):
        return fail("UpdateLandingRing does not iterate cached marker offsets")

    print("FenHuangLeapPreviewRingMathGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
