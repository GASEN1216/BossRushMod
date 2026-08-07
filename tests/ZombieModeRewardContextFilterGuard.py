"""Guard the ZombieMode-only medical/melee candidate filters."""

from pathlib import Path
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")


def fail(message: str) -> int:
    print("ZombieModeRewardContextFilterGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = ENTRY.read_text(encoding="utf-8")
    required = [
        "ZombieModeMedicalExcludedTypeIds = { 1243, 1244, 1245, 1246 }",
        "ZombieModeMeleeExcludedTypeIds = { 1, 305, 343, 1095, 1096 }",
        'HasZombieModeRequestedTag(requiredTags, "Medic")',
        'HasZombieModeRequestedTag(requiredTags, "Medical")',
        'HasZombieModeRequestedTag(requiredTags, "Healing")',
        'HasZombieModeRequestedTag(requiredTags, "MeleeWeapon")',
        "GameplayDataSettings.Tags.AdvancedDebuffMode",
        'string.Equals(tag.name, "AdvancedDebuffMode"',
        "EnsureZombieModeOpaqueFilterDiagnosticsLogged(requiredTags);",
        "GetZombieModeRewardCandidateIds(requiredTags, fallbackQuality, fallbackQuality)",
        "for (int fallbackQuality = minQuality - 1; fallbackQuality >= 0; fallbackQuality--)",
        "AddZombieModeRewardCandidates(candidates, requiredTags)",
    ]
    for token in required:
        if token not in text:
            return fail("missing context-filter invariant -> " + token)

    fallback_start = text.index("for (int fallbackQuality")
    fallback_end = text.index("if (candidates == null || candidates.Length <= 0)", fallback_start)
    fallback = text[fallback_start:fallback_end]
    if "FindRandomItemTypeByTags(null" in fallback or "requiredTags = null" in fallback:
        return fail("filtered fallback must retain the requested tags")

    if "ZombieModeMedicalExcludedTypeIds" not in text[text.index("private bool IsZombieModeRewardCandidateAllowed"):]:
        return fail("medical IDs are not scoped to the candidate allowance check")
    if "ZombieModeMeleeExcludedTypeIds" not in text[text.index("private bool IsZombieModeRewardCandidateAllowed"):]:
        return fail("melee IDs are not scoped to the candidate allowance check")

    print("ZombieModeRewardContextFilterGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
