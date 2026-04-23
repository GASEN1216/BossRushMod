"""
Guard: Phantom Witch tracked teleport telegraph should keep chase behavior active
until the actual blink commit.

Reason:
- when the player is marked by the smoke telegraph, the boss should keep chasing
  instead of freezing in place
- PauseAI should happen only for the actual teleport commit + landing attack,
  not for the whole telegraph window
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
    tracked_block = extract_block(
        text,
        "private IEnumerator ExecuteTrackedTeleportStrike(CharacterMainControl target)",
    )
    if not tracked_block:
        return fail("PhantomWitchTrackedTeleportChaseGuard: missing ExecuteTrackedTeleportStrike block")

    while_index = tracked_block.find(
        "while (Time.time - telegraphStartedAt < PhantomWitchConfig.BlinkTrackedTelegraphDuration)"
    )
    if while_index == -1:
        return fail("PhantomWitchTrackedTeleportChaseGuard: tracked teleport is missing the live telegraph loop")

    pause_index = tracked_block.find("PauseAI();")
    if pause_index == -1:
        return fail("PhantomWitchTrackedTeleportChaseGuard: tracked teleport must still pause AI for the blink commit")

    if pause_index < while_index:
        return fail("PhantomWitchTrackedTeleportChaseGuard: tracked teleport still pauses AI before the chase telegraph finishes")

    if "SetStealthMode(PhantomWitchStealthMode.SemiStealthWindup);" not in tracked_block:
        return fail("PhantomWitchTrackedTeleportChaseGuard: tracked teleport must still enter SemiStealthWindup during telegraph")

    if "TouchAttackLoopProgress();" not in tracked_block:
        return fail("PhantomWitchTrackedTeleportChaseGuard: tracked teleport must heartbeat the watchdog during telegraph")

    print("PhantomWitchTrackedTeleportChaseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
