"""Guard: Mode F preparation fixes must not change initial spawn configuration."""

from pathlib import Path


MODEF_ENTRY = Path("ModeF/ModeFEntry.cs")
MODEF_RESPAWN = Path("ModeF/ModeFRespawn.cs")
MODEF_PHASES = Path("ModeF/ModeFPhases.cs")
MODEE_BATTLE = Path("ModeE/ModeEBattle.cs")
MOD_BEHAVIOUR = Path("ModBehaviour.cs")
PHANTOM = Path("Integration/PhantomWitch/PhantomWitchAbilityController.cs")
PHANTOM_SCHEDULER = Path("Integration/PhantomWitch/PhantomWitchAbilityController_PackageScheduler.cs")


def fail(message):
    print("ModeFPreparationSpawnGuard: FAIL - " + message)
    raise SystemExit(1)


def require(text, needle, message):
    if needle not in text:
        fail(message)


def forbid(text, needle, message):
    if needle in text:
        fail(message)


def main():
    entry = MODEF_ENTRY.read_text(encoding="utf-8")
    respawn = MODEF_RESPAWN.read_text(encoding="utf-8")
    battle = MODEE_BATTLE.read_text(encoding="utf-8")
    mod = MOD_BEHAVIOUR.read_text(encoding="utf-8")
    phantom = PHANTOM.read_text(encoding="utf-8")
    phantom_scheduler = PHANTOM_SCHEDULER.read_text(encoding="utf-8")

    require(entry, "ModeESpawnAllBosses(modeFSessionToken, relatedScene);", "Mode F initial spawn must keep the existing Mode E spawn path")
    forbid(entry, "MODEF_INITIAL_BOSS_SPAWN_LIMIT", "Mode F fix must not add an initial spawn count cap")
    forbid(entry, "MODEF_INITIAL_BOSS_MIN_PLAYER_DISTANCE", "Mode F fix must not add an initial spawn distance rule")

    forbid(battle, "int maxSpawnTasks = 0", "ModeESpawnAllBosses must not expose a Mode F-only spawn cap")
    forbid(battle, "float minSpawnDistanceFromPlayer = 0f", "ModeESpawnAllBosses must not expose a Mode F-only player distance")
    forbid(battle, "[ModeF] 初始Boss投放上限", "Mode F fix must not log/apply an initial spawn limit")
    forbid(battle, "spawnTasks.RemoveRange(maxSpawnTasks, spawnTasks.Count - maxSpawnTasks);", "Mode F fix must not cap spawn tasks")

    require(respawn, "modeFState.CurrentPhase == ModeFPhase.Preparation", "Mode F prep must gate replacement respawns")
    require(respawn, "[ModeF] [RESPAWN] defer during preparation", "Mode F prep respawns must be deferred, not dropped")
    forbid(respawn, "[ModeF] [RESPAWN] skip during preparation", "Mode F prep respawns must not be dropped")
    require(respawn, "if (modeFState.CurrentPhase == ModeFPhase.Preparation)\n            {\n                return;\n            }\n\n            DevLog(\"[ModeF] [RESPAWN] dispatch", "Mode F pending respawn dispatch must stay blocked during preparation")
    require(
        MODEF_PHASES.read_text(encoding="utf-8"),
        "case ModeFPhase.Bounty:\n                        modeFState.PhaseDuration = MODEF_BOUNTY_DURATION;\n                        GenerateBountyList();\n                        TryFulfillModeFPendingRespawns();",
        "Deferred Mode F respawns must resume when bounty phase starts")

    require(mod, "public bool IsModeFPreparationPhase", "Mode F preparation phase must be exposed to boss AI")
    require(phantom, "inst.IsModeEActive || inst.IsModeFActive", "Phantom Witch must inspect Mode F target context")
    require(phantom, "inst.IsModeEActive || inst.IsModeFPreparationPhase", "Phantom Witch must suppress player fallback in Mode F preparation")
    require(phantom, "inst.IsModeFPreparationPhase && target == CharacterMainControl.Main", "Phantom Witch must reject direct player targets during Mode F preparation")
    require(phantom, "? null", "Phantom Witch exception fallback must not target player during Mode F preparation")
    require(phantom_scheduler, "inst.IsModeEActive || inst.IsModeFPreparationPhase", "Phantom Witch attack loop must wait during Mode F preparation instead of treating missing target as player death")

    print("ModeFPreparationSpawnGuard: PASS")


if __name__ == "__main__":
    main()
