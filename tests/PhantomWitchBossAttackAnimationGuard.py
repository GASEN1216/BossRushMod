"""
Guard: Phantom Witch boss-side scythe attacks must explicitly force melee attack animation.

Reason:
- several boss attack packages deal damage and spawn VFX without calling the actual melee attack animation
- teleport strike also regressed by trying to use `bossCharacter.Attack()` directly after teleport
- the fix should keep custom damage logic, but restore visible scythe swing animation
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


def require_contains(block: str, needle: str, message: str) -> str | None:
    if needle not in block:
        return message
    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")

    helper_block = extract_block(text, "private void ForceScytheAttackAnimation(CharacterMainControl target)")
    if not helper_block:
        return fail("PhantomWitchBossAttackAnimationGuard: missing ForceScytheAttackAnimation helper")

    helper_missing = [
        require_contains(helper_block, "CharacterAnimationControl", "helper must sync CharacterAnimationControl"),
        require_contains(helper_block, "CurrentHoldItemAgent", "helper must read CurrentHoldItemAgent"),
        require_contains(helper_block, "ForcePlayAttackAnimation()", "helper must call ForcePlayAttackAnimation"),
        require_contains(helper_block, "ChangeHoldItem(meleeSlot.Content)", "helper must ensure the melee weapon is held"),
    ]
    helper_missing = [item for item in helper_missing if item is not None]
    if helper_missing:
        return fail("PhantomWitchBossAttackAnimationGuard: " + " | ".join(helper_missing))

    sweep_block = extract_block(text, "private IEnumerator ExecuteImmediateScytheSweep(CharacterMainControl target)")
    requiem_block = extract_block(text, "private IEnumerator ExecuteMidrangeRequiemPackage()")
    wraith_block = extract_block(text, "private IEnumerator ExecuteWraithTrailObservePackage()")
    teleport_block = extract_block(text, "private IEnumerator ExecuteTrackedTeleportStrike(CharacterMainControl target)")

    blocks = [
        ("ExecuteImmediateScytheSweep", sweep_block),
        ("ExecuteMidrangeRequiemPackage", requiem_block),
        ("ExecuteWraithTrailObservePackage", wraith_block),
    ]

    for name, block in blocks:
        if not block:
            return fail("PhantomWitchBossAttackAnimationGuard: missing block " + name)
        if "ForceScytheAttackAnimation(target);" not in block:
            return fail("PhantomWitchBossAttackAnimationGuard: " + name + " must force attack animation")

    if teleport_block == "":
        return fail("PhantomWitchBossAttackAnimationGuard: missing ExecuteTrackedTeleportStrike block")

    if "private IEnumerator ExecuteHeavyScytheSlash()" in text:
        return fail("PhantomWitchBossAttackAnimationGuard: legacy ExecuteHeavyScytheSlash wrapper should not exist anymore")

    if "ExecuteImmediateBasicAttack(" in text:
        return fail("PhantomWitchBossAttackAnimationGuard: ExecuteImmediateBasicAttack should not exist anymore")

    if "bossCharacter.Attack()" in teleport_block:
        return fail("PhantomWitchBossAttackAnimationGuard: teleport strike should not call bossCharacter.Attack() directly")

    if "yield return ExecuteImmediateScytheSweep(target);" not in teleport_block:
        return fail("PhantomWitchBossAttackAnimationGuard: teleport strike must reuse ExecuteImmediateScytheSweep")

    print("PhantomWitchBossAttackAnimationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
