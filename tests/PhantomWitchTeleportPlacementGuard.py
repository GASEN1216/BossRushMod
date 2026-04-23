"""
Guard: Phantom Witch combat teleport must snap to the tracked marker position
without relying on Character.Hide/Show.

Reason:
- tracked teleport marker and flash already lock a concrete world position
- combat teleports that depend on Hide/Show can be interrupted by external patches
- even without exceptions, SetPosition alone can drift; the boss must hard-snap to
  the tracked light position before reappearing
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchAbilityController.cs")


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
    text = SOURCE.read_text(encoding="utf-8")

    teleport_block = extract_block(text, "private IEnumerator TeleportTo(Vector3 targetPos)")
    if not teleport_block:
        return fail("PhantomWitchTeleportPlacementGuard: missing TeleportTo block")

    helper_block = extract_block(text, "private void ApplyTeleportPosition(Vector3 targetPos)")
    if not helper_block:
        return fail("PhantomWitchTeleportPlacementGuard: missing ApplyTeleportPosition helper")

    if "bossCharacter.Hide();" in teleport_block or "bossCharacter.Show();" in teleport_block:
        return fail("PhantomWitchTeleportPlacementGuard: combat teleport still depends on Character.Hide/Show")

    if "ApplyTeleportPosition(targetPos);" not in teleport_block:
        return fail("PhantomWitchTeleportPlacementGuard: TeleportTo does not use ApplyTeleportPosition")

    if "bossHealth.SetInvincible(true);" not in teleport_block or "bossHealth.SetInvincible(false);" not in teleport_block:
        return fail("PhantomWitchTeleportPlacementGuard: TeleportTo must preserve invincibility gating")

    helper_requirements = [
        ("bossCharacter.SetPosition(targetPos);", "helper must try CharacterMainControl.SetPosition first"),
        ("bossCharacter.transform.position = targetPos;", "helper must hard-snap transform.position"),
        ("currentPos - targetPos", "helper must detect SetPosition drift before hard-snap"),
    ]
    missing = [message for needle, message in helper_requirements if needle not in helper_block]
    if missing:
        return fail("PhantomWitchTeleportPlacementGuard: " + " | ".join(missing))

    print("PhantomWitchTeleportPlacementGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
