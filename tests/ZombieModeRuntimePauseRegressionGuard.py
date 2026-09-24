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


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
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
                return text[start:index + 1]
    return ""


def main() -> int:
    entry_text = Path("ZombieMode/ZombieModeEntry.cs").read_text(encoding="utf-8")
    module_text = Path("ZombieMode/ZombieModeRuntimeModule.cs").read_text(encoding="utf-8")
    host_bridge_text = Path("ZombieMode/ZombieModeMapSelection.cs").read_text(encoding="utf-8")
    try:
        require(module_text, "private float runtimePausedDuration;", "missing module-owned runtime paused-duration accumulator")
        require(module_text, "private float runtimePauseStartTime = -1f;", "missing module-owned runtime pause-start timestamp")
        require(module_text, "private int runtimePauseRunId;", "missing module-owned runtime pause run-id tracker")
    except AssertionError as exc:
        return fail(str(exc))

    entry_tick = extract_method(entry_text, "private void TickZombieMode(float deltaTime)")
    module_tick = extract_method(module_text, "internal void TickZombieMode(float deltaTime)")
    if not entry_tick or "module.TickZombieMode(deltaTime)" not in entry_tick:
        return fail("host TickZombieMode must forward the original deltaTime to RuntimeModule")
    if not module_tick:
        return fail("RuntimeModule TickZombieMode body not found")
    tick_tokens = [
        "if (!ZombieModePhaseGuards.IsRunActive(runState.LifecyclePhase))",
        "ResetZombieModeRuntimePauseClock();",
        "RefreshZombieModeRuntimePauseClock();",
        "if (IsZombieModeRuntimePaused())",
        "owner.TickZombieModeWaveControllerForRuntimeModule(deltaTime);",
        "owner.TickZombieModeDropsAndPerformanceForRuntimeModule(deltaTime);",
        "owner.TickZombieModeBossControllerForRuntimeModule(deltaTime);",
        "owner.TickZombieModeTemporaryNpcProtectionForRuntimeModule();",
        "owner.UpdateModeFFortificationHighlightsForRuntimeModule();",
        "owner.UpdateFortPlacementMode();",
        "owner.UpdateModeFRepairSelection();",
    ]
    tick_positions = [module_tick.find(token) for token in tick_tokens]
    if any(position < 0 for position in tick_positions) or tick_positions != sorted(tick_positions):
        return fail("RuntimeModule TickZombieMode must keep the active gate, pause return and controller order")
    if "private float zombieModeRuntimePausedDuration" in host_bridge_text or "zombieModeUnattachedRuntimePausedDuration" in host_bridge_text:
        return fail("pause-clock state must not remain in the host partial")

    pause_method = extract_method(module_text, "internal bool IsZombieModeRuntimePaused()")
    game_pause_method = extract_method(module_text, "internal bool IsZombieModeGamePaused()")
    refresh_method = extract_method(module_text, "internal void RefreshZombieModeRuntimePauseClock()")
    reset_method = extract_method(module_text, "internal void ResetZombieModeRuntimePauseClock()")
    clock_method = extract_method(module_text, "internal float GetZombieModeRuntimeNow()")
    try:
        require(pause_method, "ZombieModeUIHelper.IsModalInputPaused || IsZombieModeGamePaused() || CameraMode.Active", "runtime pause sources must remain modal, game menu and photo mode")
        require(game_pause_method, "PauseMenu.Instance != null && PauseMenu.Instance.Shown", "game pause must use the official pause menu state")
        require(refresh_method, "Time.unscaledTime", "pause accumulator must use unscaled time")
        require(refresh_method, "if (runtimePauseRunId != runId)", "pause clock must reset when run id changes")
        require(refresh_method, "runtimePausedDuration += Mathf.Max(0f, Time.unscaledTime - runtimePauseStartTime);", "resume must subtract paused unscaled duration")
        require(reset_method, "runtimePauseRunId = 0;", "inactive mode tick must reset clock run id")
        require(reset_method, "runtimePausedDuration = 0f;", "inactive mode tick must reset paused duration")
        require(reset_method, "runtimePauseStartTime = -1f;", "inactive mode tick must clear pause start")
        require(clock_method, "return Time.unscaledTime - pausedDuration;", "runtime clock must subtract paused duration")
        require(entry_text, "module.RefreshZombieModeRuntimePauseClock();", "host clock refresh compatibility entry must forward to module")
        require(entry_text, "module.ResetZombieModeRuntimePauseClock();", "host clock reset compatibility entry must forward to module")
        require(entry_text, "module.GetZombieModeRuntimeNow()", "host runtime clock API must forward to module")
        require(entry_text, "module.IsZombieModeRuntimePaused()", "host pause API must forward to module")
        require(entry_text, "module.IsZombieModeGamePaused()", "host game pause API must forward to module")
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
