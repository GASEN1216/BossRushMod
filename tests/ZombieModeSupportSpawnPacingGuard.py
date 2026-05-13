"""ZombieModeSupportSpawnPacingGuard: split/summon support spawns must be paced without reducing spawn count."""

from pathlib import Path


BOSS = Path("ZombieMode/ZombieModeBossController.cs")
POLLUTION_PARTS = [
    Path("ZombieMode/ZombieModePollution.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs"),
]
CLEANUP = Path("ZombieMode/ZombieModeCleanup.cs")


def fail(message: str) -> int:
    print("ZombieModeSupportSpawnPacingGuard: FAIL - " + message)
    return 1


def read_pollution() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in POLLUTION_PARTS)


def main() -> int:
    boss_text = BOSS.read_text(encoding="utf-8")
    pollution_text = read_pollution()
    cleanup_text = CLEANUP.read_text(encoding="utf-8")

    required = [
        "QueueZombieModeSmallSplitSpawn(",
        "QueueZombieModeSplitterChildSpawn(",
        "ProcessZombieModeSupportSpawnQueue(",
        "ZombieModeSupportSpawnRequestsPerFrame",
        "yield return null;",
    ]
    for token in required:
        if token not in pollution_text and token not in boss_text:
            return fail("missing paced support spawn token: " + token)

    forbidden_direct_calls = [
        "SpawnZombieModeSplitterChildAsync(runId, boss.transform.position + offset, ZombieModeTuning.SplitterBossSummonScale).Forget();",
        "SpawnZombieModeSplitterChildAsync(runId, victim.transform.position + offset, ZombieModeTuning.SplitterBossSplitChildScale).Forget();",
        "SpawnZombieModeSmallSplitAsync(runId, character.transform.position + offset).Forget();",
    ]
    combined = boss_text + "\n" + pollution_text
    for token in forbidden_direct_calls:
        if token in combined:
            return fail("support spawn still bypasses pacing queue: " + token)

    if "ClearZombieModeSupportSpawnQueue();" not in cleanup_text:
        return fail("cleanup must clear pending support spawn queue")

    print("ZombieModeSupportSpawnPacingGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
