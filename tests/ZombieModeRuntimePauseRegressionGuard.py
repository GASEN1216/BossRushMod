"""ZombieModeRuntimePauseRegressionGuard: pause must freeze ZombieMode runtime clocks."""
from pathlib import Path
import re
import sys


POLLUTION_PARTS = [
    Path("ZombieMode/ZombieModePollution.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs"),
]


def fail(message: str) -> int:
    print("ZombieModeRuntimePauseRegressionGuard: FAIL - " + message)
    return 1


def require(text: str, needle: str, message: str):
    if needle not in text:
        raise AssertionError(message + " -> " + needle)


def read_pollution() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in POLLUTION_PARTS)


def main() -> int:
    entry_text = Path("ZombieMode/ZombieModeEntry.cs").read_text(encoding="utf-8")
    try:
        require(entry_text, "private float zombieModeRuntimePausedDuration", "missing runtime paused-duration accumulator")
        require(entry_text, "private float zombieModeRuntimePauseStartTime", "missing runtime pause-start timestamp")
        require(entry_text, "private int zombieModeRuntimePauseRunId", "missing runtime pause run-id tracker")
        require(entry_text, "private void RefreshZombieModeRuntimePauseClock()", "missing runtime pause clock refresh")
        require(entry_text, "internal float GetZombieModeRuntimeNow()", "missing runtime pause-adjusted clock")
        require(entry_text, "RefreshZombieModeRuntimePauseClock();", "TickZombieMode must refresh runtime pause clock")
        require(entry_text, "return Time.unscaledTime - pausedDuration;", "runtime clock must subtract paused duration")
    except AssertionError as exc:
        return fail(str(exc))

    boss_text = Path("ZombieMode/ZombieModeBossController.cs").read_text(encoding="utf-8")
    try:
        require(boss_text, "float now = GetZombieModeRuntimeNow();", "boss controller must use pause-adjusted runtime clock")
        require(boss_text, "instance.Lifecycle.LastReachableTime = GetZombieModeRuntimeNow();", "boss lifecycle timestamps must use runtime clock")
        require(boss_text, "instance.Lifecycle.LastHurtTime = GetZombieModeRuntimeNow();", "boss hurt timestamp must use runtime clock")
        require(boss_text, "hunter.FrenzyEndTime = GetZombieModeRuntimeNow() + ZombieModeTuning.HunterFrenzyDurationSeconds;", "hunter frenzy must freeze during pause")
    except AssertionError as exc:
        return fail(str(exc))

    pollution_text = read_pollution()
    try:
        require(pollution_text, "if (inst.IsZombieModeRuntimePaused())", "standalone pollution runtimes must gate on pause")
        require(pollution_text, "float now = inst.GetZombieModeRuntimeNow();", "pollution runtimes must use pause-adjusted runtime clock")
        require(pollution_text, "if (now > marker.AdaptiveReductionEndTime)", "adaptive affix expiry must use runtime clock")
        require(pollution_text, "marker.AdaptiveReductionEndTime = GetZombieModeRuntimeNow() + ZombieModeTuning.AdaptiveAffixDurationSeconds;", "adaptive affix duration must freeze during pause")
    except AssertionError as exc:
        return fail(str(exc))

    drop_text = Path("ZombieMode/ZombieModeDropsAndPerformance.cs").read_text(encoding="utf-8")
    try:
        require(drop_text, "float now = GetZombieModeRuntimeNow();", "drop/performance tick must use runtime clock")
        require(drop_text, "candidate.SpawnTime = GetZombieModeRuntimeNow();", "drop expiry must use runtime clock at spawn")
        require(drop_text, "bool timeExpired = now - candidate.SpawnTime >= ZombieModeTuning.DropCleanupAgeSeconds;", "drop expiry must use pause-adjusted elapsed time")
    except AssertionError as exc:
        return fail(str(exc))

    cleanup_text = Path("ZombieMode/ZombieModeCleanup.cs").read_text(encoding="utf-8")
    try:
        require(cleanup_text, "private async UniTask<bool> WaitForZombieModeRuntimeResumeAsync(int runId)", "missing shared async-spawn pause wait helper")
        require(cleanup_text, "while (IsZombieModeRunValid(runId) && IsZombieModeRuntimePaused())", "async-spawn pause wait must hold while runtime is paused")
        require(cleanup_text, "await UniTask.Yield();", "async-spawn pause wait must yield without advancing gameplay")
    except AssertionError as exc:
        return fail(str(exc))

    spawner_text = Path("ZombieMode/ZombieModeSpawner.cs").read_text(encoding="utf-8")
    try:
        require(spawner_text, "private async UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieAsync", "normal zombie async spawn must be awaitable")
        require(spawner_text, "private async UniTask<CharacterMainControl> TrySpawnZombieModeBossAsync", "boss async spawn must be awaitable")
        require(spawner_text, "await WaitForZombieModeRuntimeResumeAsync(runId)", "async spawns must wait for ZombieMode runtime pause")
        require(spawner_text, "abortedByPause", "async spawns must abort and retry if pause starts while SpawnEnemyCore is mid-flight")
    except AssertionError as exc:
        return fail(str(exc))

    star_text = Path("ZombieMode/ZombiePurificationPointController.cs").read_text(encoding="utf-8")
    if "inst != null && inst.IsZombieModeRuntimePaused()" not in star_text:
        return fail("purification star magnet/auto-collect must stop while runtime is paused")

    print("ZombieModeRuntimePauseRegressionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
