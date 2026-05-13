"""Guard ZombieMode entry from starting regular GroundZero BossRush setup."""

from pathlib import Path
import sys


INTEGRATION_PARTS = [
    Path("Integration/BossRushIntegration.cs"),
    Path("Integration/BossRushIntegration_StartAndScene.cs"),
    Path("Integration/BossRushIntegration_TravelAndSetup.cs"),
    Path("Integration/BossRushIntegration_MapObjectsAndDragonBreath.cs"),
]


def fail(message: str) -> int:
    print("ZombieModeGroundZeroBossRushIsolationGuard: FAIL - " + message)
    return 1


def read_boss_rush_integration() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in INTEGRATION_PARTS)


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    text = read_boss_rush_integration()

    scene_loaded = extract_method(text, "private void OnSceneLoaded_Integration")
    if not scene_loaded:
        return fail("OnSceneLoaded_Integration not found")

    zombie_handle = scene_loaded.find("TryHandleZombieModePendingMapSceneLoaded")
    if zombie_handle < 0:
        return fail("ZombieMode pending-scene handler must run in scene-loaded flow")

    teleport = extract_method(text, "private System.Collections.IEnumerator TeleportPlayerToCustomPosition")
    if not teleport:
        return fail("TeleportPlayerToCustomPosition not found")

    bossrush_schedule = teleport.find("StartCoroutine(SetupBossRushInGroundZero")
    if bossrush_schedule < 0:
        return fail("GroundZero setup scheduling not found in custom-position teleport flow")

    for snippet in [
        "IsZombieModeStartupInProgress()",
        "ZombieModeMapSelectionHelper.HasPendingZombieEntry",
        "IsZombieModeActive",
        "跳过 BossRush GroundZero 初始化",
    ]:
        if snippet not in teleport:
            return fail("TeleportPlayerToCustomPosition must guard ZombieMode before GroundZero setup: " + snippet)

    first_teleport_guard = teleport.find("IsZombieModeStartupInProgress()")
    if first_teleport_guard < 0 or first_teleport_guard > bossrush_schedule:
        return fail("ZombieMode teleport guard must run before GroundZero setup scheduling")

    setup = extract_method(text, "private System.Collections.IEnumerator SetupBossRushInGroundZero")
    if not setup:
        return fail("SetupBossRushInGroundZero not found")

    required = [
        "IsZombieModeStartupInProgress()",
        "ZombieModeMapSelectionHelper.HasPendingZombieEntry",
        "IsZombieModeActive",
        "yield break;",
    ]
    for snippet in required:
        if snippet not in setup:
            return fail("SetupBossRushInGroundZero must guard ZombieMode state: " + snippet)

    first_guard = setup.find("IsZombieModeStartupInProgress()")
    first_mutation = min(
        position
        for position in [
            setup.find("spawnersDisabled = false;"),
            setup.find("bossRushArenaActive = true"),
            setup.find("SpawnBossRushMapObjects"),
            setup.find("DisableAllSpawners"),
        ]
        if position >= 0
    )
    if first_guard > first_mutation:
        return fail("ZombieMode guard must run before BossRush state mutation")

    print("ZombieModeGroundZeroBossRushIsolationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
