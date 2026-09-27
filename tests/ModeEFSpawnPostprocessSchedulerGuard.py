"""Guard: Mode E/F deferred spawn postprocess must keep the shared scheduler wiring."""

from pathlib import Path
import sys
from cs_source_util import clean_source


SPAWN_CORE = Path("Utilities/EnemySpawnCore.cs")
MODE_RUNTIME = Path("Utilities/ModeRuntimeHooks.cs")
MODEE_BATTLE = Path("Utilities/ModeEFEnemySpawnRuntime.cs")
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
    host = clean_source(Path("Utilities/EnemySpawnHostBridge.cs").read_text(encoding="utf-8"))
    scheduler = clean_source(Path("Utilities/ModeEFSpawnPostprocessScheduler.cs").read_text(encoding="utf-8"))
    registration = clean_source(Path("ModBehaviourRuntimeModules.cs").read_text(encoding="utf-8"))
    spawn_core = clean_source(SPAWN_CORE.read_text(encoding="utf-8")) + "\n" + scheduler
    for text, statement in (
        (registration, "BindSpawnPostprocessServices();"),
        (host, "spawnPostprocess.BindServices(modeDItemPool.MaterializeNextSharedModeEnemyEquipmentPlanStep,"),
        (host, "spawnPostprocess.ClearModeEFSpawnPostprocessScheduler();"),
        (host, "spawnPostprocess.TickModeEFSpawnPostprocessScheduler();"),
        (scheduler, "this.materializeEquipmentStep = materializeEquipmentStep;"),
        (scheduler, "this.applyBossStatMultiplier = applyBossStatMultiplier;"),
        (scheduler, "this.registerBossLoot = registerBossLoot;"),
        (scheduler, "this.cleanupEquipmentPlan = cleanupEquipmentPlan;"),
        (scheduler, "this.clearBossLoot = clearBossLoot;"),
    ):
        if statement not in text:
            return fail("shared scheduler wiring missing -> " + statement)
    if host.count("new ModeEFSpawnPostprocessScheduler()") != 1 or "Queue<ModeEFSpawnPostprocessJob>" in host:
        return fail("host must delegate to exactly one scheduler owner")
    runtime = clean_source(MODE_RUNTIME.read_text(encoding="utf-8"))
    battle = clean_source(MODEE_BATTLE.read_text(encoding="utf-8"))
    startup = clean_source(MODEE_STARTUP.read_text(encoding="utf-8"))
    respawn_f = clean_source(MODEF_RESPAWN.read_text(encoding="utf-8"))
    equipment = clean_source(MODED_EQUIPMENT.read_text(encoding="utf-8"))

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
        (spawn_core, "internal void TickModeEFSpawnPostprocessScheduler()", "spawn core must expose the shared postprocess tick"),
        (spawn_core, "internal UniTask<EnemySpawnCoreResult> ScheduleModeEFSpawnPostprocessAsync(", "spawn core must enqueue deferred postprocess work"),
        (spawn_core, "HasModeEFSpawnPostprocessSprintPressure(currentFrame)", "spawn core must detect when jobs enter the final sprint window"),
        (spawn_core, "GetModeEFSpawnPostprocessJobStepBudget(job, currentFrame)", "spawn core must switch to the last-5-frame sprint budget per job"),
        (spawn_core, "deadlineFrame = queuedFrame + MODE_EF_SPAWN_POSTPROCESS_SOFT_DEADLINE_FRAMES", "spawn core must stamp each deferred job with the shared 60-frame soft deadline"),
        (spawn_core, "InvokeSpawnCoreCommitCallback(job.onCommit, job.context)", "spawn core final commit must invoke the shared commit callback"),
        (spawn_core, "Func<EnemySpawnContext, bool> onCommit = null", "spawn core public/internal signatures must keep the commit callback hook"),
        (spawn_core, "return await spawnPostprocess.ScheduleModeEFSpawnPostprocessAsync(", "ordinary Boss deferred path must await the shared scheduler"),
        (spawn_core, "public EnemySpawnCoreOptions options;", "postprocess job must carry the SpawnCore options for gate parity"),
        (spawn_core, "EnemySpawnCoreOptions options)", "scheduler signature must take options explicitly (no default) so future defer call sites cannot silently drop hold semantics"),
        (spawn_core, "options = options,", "scheduler enqueue must forward options into the job"),
        (spawn_core, "if (job.options != null && job.options.HoldForExternalCommit)\n            {\n                if (character.Health != null) character.Health.SetInvincible(true);\n                character.gameObject.SetActive(false);", "deferred finalize must freeze (SetInvincible + SetActive(false)) when HoldForExternalCommit is set, mirroring the sync path"),
        (spawn_core, "job.options == null || job.options.ApplySharedMutators", "deferred finalize must gate shared mutators on options, mirroring the sync path"),
        (spawn_core, "job.options == null || !job.options.HoldForExternalCommit", "deferred finalize must skip the legacy commit callback when HoldForExternalCommit is set, mirroring the sync path"),
        (equipment, "internal sealed class SharedModeEnemyEquipmentMaterializationPlan", "shared-mode ordinary Boss equipment plan must exist in the item pool service"),
        (equipment, "internal sealed partial class ModeDItemPool", "shared equipment implementation must belong to the item pool service"),
        (spawn_core, "using SharedModeEnemyEquipmentMaterializationPlan = BossRush.ModeDItemPool.SharedModeEnemyEquipmentMaterializationPlan;", "spawn scheduler must use the service-owned equipment plan"),
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
