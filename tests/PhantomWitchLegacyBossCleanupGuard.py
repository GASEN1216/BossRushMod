"""
Guard: remove leftover Phantom Witch boss-side legacy skill wrappers and
avoid pre-windup turn spam in the active package scheduler.

Reason:
- the current scheduler only drives the package methods
- unused legacy wrappers create extra maintenance surface and confusion
- repeated FaceTarget calls during windup make the boss look like it keeps
  twitch-turning before every package
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

    legacy_signatures = [
        "private IEnumerator ExecuteBlink()",
        "private IEnumerator ExecuteCurseAura()",
        "private IEnumerator ExecuteHeavyScytheSlash()",
        "private IEnumerator ExecuteCurseRealm()",
        "private IEnumerator ExecuteSummonMinions()",
    ]
    remaining = [signature for signature in legacy_signatures if signature in text]
    if remaining:
        return fail(
            "PhantomWitchLegacyBossCleanupGuard: legacy wrappers still present -> " + ", ".join(remaining)
        )

    requiem_block = extract_block(text, "private IEnumerator ExecuteMidrangeRequiemPackage()")
    wraith_block = extract_block(text, "private IEnumerator ExecuteWraithTrailObservePackage()")
    if not requiem_block or not wraith_block:
        return fail("PhantomWitchLegacyBossCleanupGuard: missing active package block")

    if "FaceTarget(target);" in requiem_block:
        return fail("PhantomWitchLegacyBossCleanupGuard: ExecuteMidrangeRequiemPackage still snap-faces during windup")

    if "FaceTarget(target);" in wraith_block:
        return fail("PhantomWitchLegacyBossCleanupGuard: ExecuteWraithTrailObservePackage still snap-faces during windup")

    print("PhantomWitchLegacyBossCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
