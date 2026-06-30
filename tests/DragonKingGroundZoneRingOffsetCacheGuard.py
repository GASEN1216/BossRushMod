"""
Guard: Dragon King ground-zone ring updates should reuse cached segment offsets.

Reason:
- Ground zones refresh their LineRenderer ring every pulse interval.
- The 16 segment directions are constant for the lifetime of the process.
- Rebuilding them with Quaternion.Euler during every UpdateZoneRing call wastes CPU
  without changing the visual result.

Requirement:
- UpdateZoneRing must not call Quaternion.Euler.
- Ring segment directions must be cached in a static RingUnitOffsets array.
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileZones.cs")


def fail(message: str) -> int:
    print("DragonKingGroundZoneRingOffsetCacheGuard: FAIL - " + message)
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
    update_body = extract_method_body(text, "private void UpdateZoneRing(float ringRadius)")
    if update_body is None:
        return fail("missing UpdateZoneRing body")

    if "Quaternion.Euler" in update_body:
        return fail("UpdateZoneRing still rebuilds segment rotations")

    required = [
        "private static readonly Vector3[] RingUnitOffsets = BuildRingUnitOffsets();",
        "private static Vector3[] BuildRingUnitOffsets()",
        "RingUnitOffsets[i] * ringRadius",
        "zoneRing.SetPosition(i, offset + RingHeightOffset);",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing cached ring offset snippet -> " + snippet)

    if not re.search(r"for\s*\(\s*int\s+i\s*=\s*0\s*;\s*i\s*<\s*RingUnitOffsets\.Length\s*;\s*i\+\+\s*\)", update_body):
        return fail("UpdateZoneRing does not iterate cached ring offsets")

    print("DragonKingGroundZoneRingOffsetCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
