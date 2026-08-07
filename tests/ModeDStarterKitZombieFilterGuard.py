"""Guard shared Mode D/E/F starter medical and melee filtering."""

from pathlib import Path
import sys


SOURCE = Path("ModeD/ModeDEquipment_StarterKit.cs")


def fail(message: str) -> int:
    print("ModeDStarterKitZombieFilterGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for index in range(brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start : index + 1]
    return ""


def main() -> int:
    source = SOURCE.read_text(encoding="utf-8")
    for token in [
        'SharedStarterMedicalRequiredTags = { "Healing" }',
        'SharedStarterMeleeRequiredTags = { "MeleeWeapon" }',
        "SharedStarterMedicalFallbackIds = { 401, 402, 403 }",
        "IsZombieModeRewardCandidateAllowed",
        "GetStarterMeleePool",
        "SelectAllowedStarterFallbackItemId",
    ]:
        if token not in source:
            return fail("missing shared starter filter contract -> " + token)

    melee = extract_method(source, "private void GiveRandomMeleeWeapon(CharacterMainControl character)")
    if "GetStarterMeleePool()" not in melee or "modeDMeleePool[" in melee:
        return fail("player melee starter path must use the filtered pool")

    medical = extract_method(source, "private void GiveStarterMedical(CharacterMainControl character, int count = 1)")
    if "GetStarterMedicalPool()" not in medical:
        return fail("player medical starter path must use the filtered pool")
    if "SelectAllowedStarterFallbackItemId" not in medical:
        return fail("medical hardcoded fallback must use the same filter")

    fallback = extract_method(source, "private int SelectAllowedStarterFallbackItemId(")
    if "IsZombieModeRewardCandidateAllowed" not in fallback:
        return fail("medical fallback helper must enforce Zombie exclusions")

    print("ModeDStarterKitZombieFilterGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
