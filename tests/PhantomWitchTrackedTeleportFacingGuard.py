"""
Guard: Phantom Witch tracked teleport must move meaningfully and avoid micro-turn spam.

Reason:
- tracked teleport currently uses an almost-zero target offset, which can look like a failed blink
- FaceTarget currently snaps rotation on every tiny target drift, causing constant turning motions
"""

from pathlib import Path
import re
import sys


CONFIG = Path("Integration/PhantomWitch/PhantomWitchConfig.cs")
ABILITY = Path("Integration/PhantomWitch/PhantomWitchAbilityController.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    config_text = CONFIG.read_text(encoding="utf-8")
    ability_text = ABILITY.read_text(encoding="utf-8")

    offset_match = re.search(
        r"public\s+const\s+float\s+BlinkTrackedOffsetDistance\s*=\s*([0-9.]+)f\s*;",
        config_text,
    )
    if offset_match is None:
        return fail("PhantomWitchTrackedTeleportFacingGuard: missing BlinkTrackedOffsetDistance constant")

    offset_value = float(offset_match.group(1))
    if offset_value < 1.0:
        return fail("PhantomWitchTrackedTeleportFacingGuard: BlinkTrackedOffsetDistance is still too small")

    resolve_block = extract_block(
        ability_text,
        "private Vector3 ResolveTrackedTeleportStrikePosition(CharacterMainControl target)",
    )
    if not resolve_block:
        return fail("PhantomWitchTrackedTeleportFacingGuard: missing ResolveTrackedTeleportStrikePosition block")

    if re.search(r"ResolveTeleportPosition\s*\(\s*target\s*,", resolve_block) is None:
        return fail(
            "PhantomWitchTrackedTeleportFacingGuard: tracked teleport lacks fallback to a real blink position"
        )

    if "sqrMagnitude <" not in resolve_block:
        return fail(
            "PhantomWitchTrackedTeleportFacingGuard: tracked teleport does not reject near-origin destinations"
        )

    tracked_block = extract_block(
        ability_text,
        "private IEnumerator ExecuteTrackedTeleportStrike(CharacterMainControl target)",
    )
    if not tracked_block:
        return fail("PhantomWitchTrackedTeleportFacingGuard: missing ExecuteTrackedTeleportStrike block")

    if "Vector3 lockedTeleportPos =" not in tracked_block:
        return fail("PhantomWitchTrackedTeleportFacingGuard: tracked teleport missing lockedTeleportPos state")

    face_block = extract_block(ability_text, "private void FaceTarget(CharacterMainControl target)")
    if not face_block:
        return fail("PhantomWitchTrackedTeleportFacingGuard: missing FaceTarget block")

    if "Vector3.Angle(" not in face_block:
        return fail("PhantomWitchTrackedTeleportFacingGuard: FaceTarget still snaps without angle threshold")

    if "Quaternion.LookRotation" not in face_block:
        return fail("PhantomWitchTrackedTeleportFacingGuard: FaceTarget no longer rotates toward target")

    print("PhantomWitchTrackedTeleportFacingGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
