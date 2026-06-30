"""
Guard: Dragon King ground-zone pulse updates should avoid duplicate pulse math.

Reason:
- Ground-zone radius is fixed after Initialize, so the base ring width is fixed.
- The ring pulse and light pulse use the same pulseTime phase.
- Recomputing the base width and sine in the repeated update path wastes CPU
  without changing the visual result.

Requirement:
- CreateZoneRing caches ringBaseWidth once from radius.
- Update reuses ringBaseWidth for widthMultiplier.
- Update calls Mathf.Sin(pulseTime) only once.
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileZones.cs")


def fail(message: str) -> int:
    print("DragonKingGroundZonePulseMathGuard: FAIL - " + message)
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
    create_body = extract_method_body(text, "private void CreateZoneRing(Color zoneColor)")
    update_body = extract_method_body(text, "private void Update()")
    if create_body is None:
        return fail("missing CreateZoneRing body")
    if update_body is None:
        return fail("missing Update body")

    if "private float ringBaseWidth;" not in text:
        return fail("missing cached ringBaseWidth field")

    if "ringBaseWidth = Mathf.Clamp(radius * 0.08f, 0.08f, 0.18f);" not in create_body:
        return fail("CreateZoneRing does not cache base ring width")

    if "zoneRing.widthMultiplier = ringBaseWidth;" not in create_body:
        return fail("CreateZoneRing does not apply cached base ring width")

    if update_body.count("Mathf.Sin(pulseTime)") != 1:
        return fail("Update should call Mathf.Sin(pulseTime) exactly once")

    required = [
        "float pulseSin = Mathf.Sin(pulseTime);",
        "float pulse = 1f + pulseSin * 0.08f;",
        "zoneRing.widthMultiplier = ringBaseWidth * pulse;",
        "Mathf.InverseLerp(-1f, 1f, pulseSin)",
    ]
    for snippet in required:
        if snippet not in update_body:
            return fail("missing pulse math reuse snippet -> " + snippet)

    if re.search(r"zoneRing\.widthMultiplier\s*=\s*Mathf\.Clamp\s*\(\s*radius\s*\*\s*0\.08f", update_body):
        return fail("Update still recomputes base ring width")

    print("DragonKingGroundZonePulseMathGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
