"""Guard: BossRush-owned zombie spawns must sanitize official boom attachments."""

from pathlib import Path
import sys


SANITIZER = Path("Utilities/ZombieSpawnSanitizer.cs")
FROSTMOURNE = Path("Integration/Frostmourne/FrostmourneAction.cs")
SUMMON_STAFF = Path("Integration/NewWeapons/SummonStaff/SummonStaffAction.cs")
ZOMBIE_SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")


def fail(message: str) -> int:
    print("ZombieBoomAttachmentSanitizerGuard: FAIL - " + message)
    return 1


def main() -> int:
    sanitizer = SANITIZER.read_text(encoding="utf-8-sig")
    if "AISpecialAttachment_BoomCar" not in sanitizer:
        return fail("sanitizer no longer targets AISpecialAttachment_BoomCar")
    if "Skill_Grenade" not in sanitizer:
        return fail("sanitizer no longer targets zombie self-destruction skill")
    if "ZombieModeSpecialKind.OfficialExploder" not in sanitizer:
        return fail("sanitizer no longer preserves OfficialExploder self-destruction skill")
    if "ShouldKeepBossRushZombieSelfDestructionSkill" not in sanitizer:
        return fail("missing official exploder self-destruction preservation helper")
    if "if (ShouldKeepBossRushZombieSelfDestructionSkill(character))" not in sanitizer:
        return fail("OfficialExploder no longer bypasses BoomCar sanitization")
    for debug_snippet in [
        "Debug preset (",
        "Debug preset attachment (",
        "Debug components (",
        "Debug component (",
    ]:
        if debug_snippet in sanitizer:
            return fail("temporary zombie component dump is still present -> " + debug_snippet)
    if "SanitizeBossRushZombieSpawn" not in sanitizer:
        return fail("missing SanitizeBossRushZombieSpawn helper")

    frostmourne = FROSTMOURNE.read_text(encoding="utf-8-sig")
    if 'ModBehaviour.Instance?.SanitizeBossRushZombieSpawn(zombie, "Frostmourne")' not in frostmourne:
        return fail("Frostmourne summon path lost zombie sanitization")

    summon_staff = SUMMON_STAFF.read_text(encoding="utf-8-sig")
    if 'ModBehaviour.Instance?.SanitizeBossRushZombieSpawn(ally, "SummonStaff")' not in summon_staff:
        return fail("SummonStaff summon path lost zombie sanitization")

    zombie_spawner = ZOMBIE_SPAWNER.read_text(encoding="utf-8-sig")
    for snippet in [
        'SanitizeBossRushZombieSpawn(zombie, "ZombieModeNormal")',
        'SanitizeBossRushZombieSpawn(boss, "ZombieModeBoss")',
    ]:
        if snippet not in zombie_spawner:
            return fail("ZombieMode spawn path lost sanitization -> " + snippet)

    print("ZombieBoomAttachmentSanitizerGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
