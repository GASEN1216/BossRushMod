"""Guard: wave settlement cleanup and boss spawn recovery stay reachable and NavMesh-safe."""

from pathlib import Path
import sys


DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
BOSS = Path("ZombieMode/ZombieModeBossController.cs")
TUNING = Path("ZombieMode/ZombieModeTuning.cs")


def fail(message: str) -> int:
    print("ZombieModeWaveCleanupAndBossSpawnGuard: FAIL - " + message)
    return 1


def main() -> int:
    drops = DROPS.read_text(encoding="utf-8-sig")
    waves = WAVES.read_text(encoding="utf-8-sig")
    spawner = SPAWNER.read_text(encoding="utf-8-sig")
    boss = BOSS.read_text(encoding="utf-8-sig")
    tuning = TUNING.read_text(encoding="utf-8-sig")

    for token in [
        "CleanupZombieModeExpiredDropCandidates(bool forceWaveCleanup)",
        "forceWaveCleanup ||",
        "candidate.BossDrop ||",
        "!forceWaveCleanup && candidate.HighValue",
        "candidate.GameObject.GetComponent<Item>()",
        "ownedItem.InInventory != null || ownedItem.PluggedIntoSlot != null",
        "RemoveZombieModeRunOnlyObjectRecord(candidate.GameObject);",
        "PruneZombieModeUnknownRunOnlyRecords();",
    ]:
        if token not in drops:
            return fail("wave cleanup contract missing -> " + token)
    cleanup_token = "CleanupZombieModeExpiredDropCandidates(true);"
    if cleanup_token not in waves:
        return fail("next wave start does not force ordinary drop cleanup")
    start_index = waves.index("private void StartZombieModeWave")
    complete_index = waves.index("private void CompleteZombieModeWave")
    if waves.find(cleanup_token, start_index, complete_index) < 0:
        return fail("ordinary drop cleanup is not at next wave start")
    if waves.find(cleanup_token, complete_index) >= 0:
        return fail("ordinary drop cleanup still runs at wave settlement")

    for token in [
        "TryResolveZombieModeSpawnPoint(candidate, zombieModeRunState.SpawnPoints[index].VirtualPoint",
        "return GetZombieModeSpawnPosition();",
    ]:
        if token not in spawner:
            return fail("boss spawn point safety missing -> " + token)

    for token in [
        "boss.SetPosition(target);",
        "navAgent.Warp(target)",
        "rb.velocity = Vector3.zero",
        "instance.Lifecycle.LastReachableTime = GetZombieModeRuntimeNow();",
        "if (now - instance.Lifecycle.LastReachableTime >= ZombieModeTuning.BossStuckTimeoutSeconds)",
    ]:
        if token not in boss:
            return fail("boss stuck recovery missing -> " + token)
    hurt_start = boss.find("private void HandleZombieModeBossHurt(")
    if hurt_start < 0:
        return fail("boss hurt handler missing")
    hurt_end = boss.find("private ", hurt_start + 20)
    hurt_body = boss[hurt_start:hurt_end if hurt_end > hurt_start else len(boss)]
    if "LastReachableTime =" in hurt_body or "LastKnownPosition =" in hurt_body:
        return fail("boss hurt events must not postpone stuck recovery")
    if "BossStuckTimeoutSeconds = 12f" not in tuning:
        return fail("boss stuck timeout must remain responsive")

    print("ZombieModeWaveCleanupAndBossSpawnGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
