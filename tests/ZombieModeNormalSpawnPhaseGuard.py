from pathlib import Path
import sys


SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
WAVE = Path("ZombieMode/ZombieModeWaveController.cs")
BOSS = Path("ZombieMode/ZombieModeBossController.cs")
POLLUTION_PARTS = [
    Path("ZombieMode/ZombieModePollution.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs"),
]
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REWARD_PARTS = [
    REWARDS,
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]


def read_rewards() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REWARD_PARTS)


def read_pollution() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in POLLUTION_PARTS)



def fail(message: str) -> int:
    print("ZombieModeNormalSpawnPhaseGuard: FAIL - " + message)
    return 1


def require(text: str, needle: str, message: str):
    if needle not in text:
        raise AssertionError(message + " -> " + needle)


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
    spawner = SPAWNER.read_text(encoding="utf-8")
    wave = WAVE.read_text(encoding="utf-8")
    boss = BOSS.read_text(encoding="utf-8")
    pollution = read_pollution()
    rewards = read_rewards()

    try:
        spawn_normal = extract_method(spawner, "private async UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieAsync")
        if not spawn_normal:
            return fail("cannot extract TrySpawnZombieModeNormalZombieAsync")

        require(
            spawn_normal,
            "System.Func<bool> isSpawnPhaseStillAllowed = null",
            "normal zombie async spawn helper must accept a caller phase predicate",
        )
        require(
            spawn_normal,
            "IsZombieModeNormalSpawnStillAllowed(runId, isSpawnPhaseStillAllowed)",
            "normal zombie async spawn helper must check the caller phase predicate while waiting",
        )
        require(
            spawn_normal,
            "isActiveCheck: () => IsZombieModeNormalSpawnStillAllowed(runId, isSpawnPhaseStillAllowed)",
            "SpawnEnemyCore active check must include the caller phase predicate",
        )
        require(
            spawn_normal,
            "bool phaseStillAllowed = IsZombieModeNormalSpawnStillAllowed(runId, isSpawnPhaseStillAllowed);",
            "onSpawned must reject late completions after the allowed phase changed",
        )
        require(
            spawn_normal,
            "if (!phaseStillAllowed || runtimePaused)",
            "onSpawned must reject late completions after the allowed phase changed",
        )
        require(
            spawn_normal,
            "abortedByPause = phaseStillAllowed && runtimePaused;",
            "normal zombie async spawn should retry only for runtime pause, not for phase changes",
        )

        require(
            spawner,
            "private bool IsZombieModeNormalSpawnStillAllowed(int runId, System.Func<bool> isSpawnPhaseStillAllowed)",
            "spawner must keep the run-valid and phase-valid checks in one helper",
        )

        require(
            wave,
            "() => zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat",
            "combat wave spawns must pass a combat-phase predicate",
        )
        require(
            wave,
            "() => IsZombieModeAmbientZombieSpawnPhase(zombieModeRunState.CombatPhase)",
            "ambient map spawns must pass the ambient-phase predicate through the await",
        )
        require(
            boss,
            "() => zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat",
            "splitter boss children must not spawn after combat phase leaves Combat",
        )
        require(
            pollution,
            "() => zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat",
            "elite splitting affix children must not spawn after combat phase leaves Combat",
        )
        require(
            rewards,
            "() => zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat",
            "elite squad reward spawns must keep checking Combat phase through the await",
        )
    except AssertionError as exc:
        return fail(str(exc))

    print("ZombieModeNormalSpawnPhaseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
