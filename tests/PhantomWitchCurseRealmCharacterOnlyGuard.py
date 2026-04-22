"""
Guard: Phantom Witch scythe right-click curse realm must only damage character-backed targets.

Reason:
- latest Player.log shows external Health.get_MaxHealth patches crashing during realm/melee damage
  when a damaged Health does not have a CharacterMainControl backing it.
- the skill design says the realm affects enemies, not arbitrary damage receivers with Health.

Requirements:
- DealRealmDamage resolves `receiver.health.TryGetCharacter()` before damage
- non-character targets are skipped before `receiver.Hurt(...)`
- buff/VFX path uses the resolved character target instead of falling back to receiver.gameObject
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchScytheAction.cs")


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
    block = extract_block(text, "private void DealRealmDamage()")
    if not block:
        return fail("PhantomWitchCurseRealmCharacterOnlyGuard: missing DealRealmDamage block")

    if "CharacterMainControl realmTarget = receiver.health.TryGetCharacter();" not in block:
        return fail("PhantomWitchCurseRealmCharacterOnlyGuard: missing character target resolution before damage")

    if re.search(r"CharacterMainControl\s+realmTarget\s*=\s*receiver\.health\.TryGetCharacter\(\);\s*if\s*\(\s*realmTarget\s*==\s*null\s*\)\s*\{\s*continue\s*;\s*\}", block, re.DOTALL) is None:
        return fail("PhantomWitchCurseRealmCharacterOnlyGuard: non-character targets are not skipped before damage")

    if re.search(r"receiver\.Hurt\s*\(\s*damageInfo\s*\)", block) is None:
        return fail("PhantomWitchCurseRealmCharacterOnlyGuard: receiver.Hurt call missing")

    if re.search(r"receiver\.Hurt\s*\(\s*damageInfo\s*\)\s*;[\s\S]*PhantomWitchCurseSweatVfx\.TryAttach\s*\(\s*realmTarget\.gameObject\s*\)", block) is None:
        return fail("PhantomWitchCurseRealmCharacterOnlyGuard: buff/vfx path does not use resolved character target")

    if "receiver.gameObject" in block:
        return fail("PhantomWitchCurseRealmCharacterOnlyGuard: receiver.gameObject fallback still present in curse realm target path")

    print("PhantomWitchCurseRealmCharacterOnlyGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
