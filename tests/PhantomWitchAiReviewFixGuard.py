"""
Guard the concrete Phantom Witch AI review fixes.

This source-level guard locks the three high-value static review fixes:
- boss curse realm damage de-duplicates multi-collider targets
- boss curse realm uses the controller's current weapon type id contract
- dual-minion pair-fill accounts for pending async spawns
"""

from pathlib import Path
import re
import sys


ABILITY = Path("Integration/PhantomWitch/PhantomWitchAbilityController.cs")
PHANTOM_WITCH_ABILITY_SOURCES = [
    ABILITY,
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_PackageScheduler.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_StealthAndAttacks.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_Minions.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_RuntimeTicks.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_PhaseAndLifecycle.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_MovementAndDamage.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_CleanupAndTelemetry.cs"),
]


def read_phantom_witch_ability_sources() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in PHANTOM_WITCH_ABILITY_SOURCES)
REALM = Path("Integration/PhantomWitch/PhantomWitchBossCurseRealmRuntime.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, pattern: str, description: str, flags: int = 0) -> str | None:
    if re.search(pattern, text, flags) is None:
        return description
    return None


def main() -> int:
    ability_text = read_phantom_witch_ability_sources()
    realm_text = REALM.read_text(encoding="utf-8")

    missing = [
        require(realm_text, r"HashSet<int>\s+processedReceiverIds", "boss realm missing processedReceiverIds"),
        require(realm_text, r"processedReceiverIds\.Clear\s*\(", "boss realm missing processedReceiverIds.Clear"),
        require(realm_text, r"!processedReceiverIds\.Add\s*\(", "boss realm missing duplicate receiver gate"),
        require(realm_text, r"weaponTypeId", "boss realm missing cached weaponTypeId"),
        require(realm_text, r"GetCurrentWeaponTypeIdForRealmRuntime\s*\(", "boss realm not using controller weapon id contract"),
        require(ability_text, r"pendingMinionRoles", "ability missing pending minion role reservation"),
        require(ability_text, r"pendingMinionRoles\.Add\s*\(", "ability missing pending role reservation before spawn"),
        require(ability_text, r"pendingMinionRoles\.Remove\s*\(", "ability missing pending role release"),
        require(ability_text, r"CountOccupiedMinionSlots\s*\(", "ability missing occupied slot helper"),
    ]

    missing = [item for item in missing if item is not None]
    if missing:
        return fail("PhantomWitchAiReviewFixGuard: FAIL | " + " | ".join(missing))

    print("PhantomWitchAiReviewFixGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
