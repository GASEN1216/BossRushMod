"""
Guard: Phantom Witch long-running attack packages must feed the AttackLoop watchdog.

Reason:
- tracked blink packages now have a multi-second telegraph and commit window
- the watchdog should recover real stalls, but must not cancel a healthy package
  just because it lasts longer than the base threshold
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

    helper_block = extract_block(text, "private void TouchAttackLoopProgress()")
    if not helper_block:
        return fail("PhantomWitchWatchdogHeartbeatGuard: missing TouchAttackLoopProgress helper")

    if "attackLoopLastTickTime = Time.time;" not in helper_block:
        return fail("PhantomWitchWatchdogHeartbeatGuard: helper must refresh attackLoopLastTickTime")

    tracked_block = extract_block(
        text,
        "private IEnumerator ExecuteTrackedTeleportStrike(CharacterMainControl target)",
    )
    if not tracked_block:
        return fail("PhantomWitchWatchdogHeartbeatGuard: missing ExecuteTrackedTeleportStrike block")

    if tracked_block.count("TouchAttackLoopProgress();") < 2:
        return fail("PhantomWitchWatchdogHeartbeatGuard: tracked teleport must heartbeat multiple times during the long package")

    teleport_block = extract_block(text, "private IEnumerator TeleportTo(Vector3 targetPos)")
    if not teleport_block:
        return fail("PhantomWitchWatchdogHeartbeatGuard: missing TeleportTo block")

    if "TouchAttackLoopProgress();" not in teleport_block:
        return fail("PhantomWitchWatchdogHeartbeatGuard: TeleportTo must heartbeat during blink commit")

    print("PhantomWitchWatchdogHeartbeatGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
