"""Guard: ZombieMode fixed reward/drop tag arrays should stay cached."""

from pathlib import Path
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
HOT_FILES = [
    Path("ZombieMode/ZombieModeEntry_StarterLoadout.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardTriggerEffects.cs"),
    Path("ZombieMode/ZombieModeDropsAndPerformance.cs"),
]

FORBIDDEN_FIXED_ARRAYS = [
    'new string[] { "Ammo" }',
    'new string[] { "Armor" }',
    'new string[] { "BodyArmor" }',
    'new string[] { "Bullet" }',
    'new string[] { "Drink" }',
    'new string[] { "Food" }',
    'new string[] { "Gun" }',
    'new string[] { "Headset" }',
    'new string[] { "Healing" }',
    'new string[] { "Helmet" }',
    'new string[] { "Medical" }',
    'new string[] { "Medic" }',
    'new string[] { "MeleeWeapon" }',
    'new string[] { "Weapon" }',
    'new string[] { "Medic", "Medical", "Healing" }',
]

REQUIRED_CACHED_TAGS = [
    "ZombieModeRewardTagAmmo",
    "ZombieModeRewardTagArmor",
    "ZombieModeRewardTagBodyArmor",
    "ZombieModeRewardTagBullet",
    "ZombieModeRewardTagDrink",
    "ZombieModeRewardTagFood",
    "ZombieModeRewardTagGun",
    "ZombieModeRewardTagHeadset",
    "ZombieModeRewardTagHealing",
    "ZombieModeRewardTagHelmet",
    "ZombieModeRewardTagMedical",
    "ZombieModeRewardTagMedic",
    "ZombieModeRewardTagMeleeWeapon",
    "ZombieModeRewardTagWeapon",
    "ZombieModeRewardTagsMedicMedicalHealing",
]


def fail(message: str) -> int:
    print("ZombieModeTagArrayAllocationGuard: FAIL - " + message)
    return 1


def main() -> int:
    if not ENTRY.exists():
        return fail("missing " + str(ENTRY))

    entry_text = ENTRY.read_text(encoding="utf-8", errors="ignore")
    for tag_name in REQUIRED_CACHED_TAGS:
        if "static readonly string[] " + tag_name not in entry_text:
            return fail("missing cached tag array: " + tag_name)

    for path in HOT_FILES:
        if not path.exists():
            return fail("missing " + str(path))
        text = path.read_text(encoding="utf-8", errors="ignore")
        for snippet in FORBIDDEN_FIXED_ARRAYS:
            if snippet in text:
                return fail(str(path) + " still allocates fixed tag array: " + snippet)

    if "ResolveZombieModeTags(requiredTags[i])" not in entry_text:
        return fail("reward candidate lookup should avoid per-tag wrapper arrays")

    if 'return new string[] { "Armor" };' in entry_text:
        return fail("BodyArmor alias should use cached alias array")
    if 'return new string[] { "Armor", "Helmat", "Helmet" };' in entry_text:
        return fail("Armor aliases should use cached alias array")
    if 'return new string[] { "Helmat", "Helmet" };' in entry_text:
        return fail("Helmet aliases should use cached alias array")

    print("ZombieModeTagArrayAllocationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
