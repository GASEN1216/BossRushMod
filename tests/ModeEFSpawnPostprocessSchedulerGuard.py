"""Guard: Mode E/F deferred spawn postprocess must keep the shared scheduler wiring."""

from pathlib import Path
import sys


SPAWN_CORE = Path("Utilities/EnemySpawnCore.cs")
MODE_RUNTIME = Path("Utilities/ModeRuntimeHooks.cs")
MODEE_BATTLE = Path("ModeE/ModeEBattle.cs")
MODEE_STARTUP = Path("ModeE/ModeEStartup.cs")
MODEF_RESPAWN = Path("ModeF/ModeFRespawn.cs")
MODED_EQUIPMENT = Path("ModeD/ModeDEquipment.cs")


def fail(message: str) -> int:
    print("ModeEFSpawnPostprocessSchedulerGuard: FAIL - " + message)
    return 1


def require(text: str, needle: str, message: str) -> int | None:
    if needle not in text:
        return fail(message)
    return None


def main() -> int:
    spawn_core = SPAWN_CORE.read_text(encoding="utf-8")
    runtime = MODE_RUNTIME.read_text(encoding="utf-8")
    battle = MODEE_BATTLE.read_text(encoding="utf-8")
    startup = MODEE_STARTUP.read_text(encoding="utf-8")
    respawn_f = MODEF_RESPAWN.read_text(encoding="utf-8")
    equipment = MODED_EQUIPMENT.read_text(encoding="utf-8")

    for text, needle, message in (
        (spawn_core, "private const int MODE_EF_SPAWN_POSTPROCESS_SOFT_DEADLINE_FRAMES = 60;", "spawn core must preserve the shared 60-frame soft deadline"),
        (spawn_core, "private const int MODE_EF_SPAWN_POSTPROCESS_FINAL_SPRINT_FRAMES = 5;", "spawn core must preserve the final 5-frame sprint window"),
        (spawn_core, "private const float MODE_EF_SPAWN_POSTPROCESS_FRAME_BUDGET_MS = 1000f / 60f;", "spawn core must preserve the shared 60 FPS frame budget"),
        (spawn_core, "private const float MODE_EF_SPAWN_POSTPROCESS_SPRINT_FRAME_BUDGET_MS = 1000f / 30f;", "spawn core sprint path must keep a finite per-frame heavy-work budget"),
        (spawn_core, "private const int MODE_EF_SPAWN_POSTPROCESS_BASE_JOB_STEPS = 1;", "spawn core must keep round-robin single-step baseline"),
        (spawn_core, "private const int MODE_EF_SPAWN_POSTPROCESS_SPRINT_JOB_STEPS = 3;", "spawn core must preserve last-5-frame sprint step budget"),
        (spawn_core, "private const int MODE_EF_SPAWN_POSTPROCESS_MAX_STEPS_PER_TICK = 8;", "spawn core must cap per-tick postprocess work"),
        (spawn_core, "private const int MODE_EF_SPAWN_POSTPROCESS_SPRINT_MAX_STEPS_PER_TICK = 16;", "spawn core sprint path must still keep a finite per-tick budget"),
        (spawn_core, "private readonly Queue<ModeEFSpawnPostprocessJob> modeEFSpawnPostprocessQueue", "spawn core must keep the shared Mode E/F postprocess queue"),
        (spawn_core, "private void TickModeEFSpawnPostprocessScheduler()", "spawn core must expose the shared postprocess tick"),
        (spawn_core, "private UniTask<EnemySpawnCoreResult> ScheduleModeEFSpawnPostprocessAsync(", "spawn core must enqueue deferred postprocess work"),
        (spawn_core, "HasModeEFSpawnPostprocessSprintPressure(currentFrame)", "spawn core must detect when jobs enter the final sprint window"),
        (spawn_core, "GetModeEFSpawnPostprocessJobStepBudget(job, currentFrame)", "spawn core must switch to the last-5-frame sprint budget per job"),
        (spawn_core, "deadlineFrame = queuedFrame + MODE_EF_SPAWN_POSTPROCESS_SOFT_DEADLINE_FRAMES", "spawn core must stamp each deferred job with the shared 60-frame soft deadline"),
        (spawn_core, "InvokeSpawnCoreCommitCallback(job.onCommit, job.context)", "spawn core final commit must invoke the shared commit callback"),
        (spawn_core, "Func<EnemySpawnContext, bool> onCommit = null", "spawn core public/internal signatures must keep the commit callback hook"),
        (spawn_core, "return await ScheduleModeEFSpawnPostprocessAsync(", "ordinary Boss deferred path must await the shared scheduler"),
        (equipment, "private sealed class SharedModeEnemyEquipmentMaterializationPlan", "shared-mode ordinary Boss equipment plan must exist"),
        (equipment, "MaterializeNextSharedModeEnemyEquipmentPlanStep(", "shared-mode ordinary Boss hidden materialization steps must exist"),
        (runtime, "TickModeEFSpawnPostprocessScheduler();", "mode runtime tick must drive the shared postprocess scheduler"),
        (startup, "ClearModeEFSpawnPostprocessScheduler();", "Mode E/F shared reset must clear pending deferred spawn jobs"),
        (battle, "onCommit: (ctx) =>", "Mode E spawn flow must register through the final commit callback"),
        (battle, "return OnModeEEnemySpawned(ctx, capturedFaction, capturedPromoted);", "Mode E commit callback must still finish the existing runtime registration"),
        (respawn_f, "onCommit: (ctx) => ConfigureModeFRespawnedBoss(ctx, selectedDragonDescendant, spawnPos)", "Mode F respawn must register through the final commit callback"),
    ):
        result = require(text, needle, message)
        if result is not None:
            return result

    print("ModeEFSpawnPostprocessSchedulerGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
