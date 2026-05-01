from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
BOSS = Path("ZombieMode/ZombieModeBossController.cs")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModePhase5BossGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    if not BOSS.exists():
        return fail("ZombieModePhase5BossGuard: missing ZombieModeBossController.cs")

    models = MODELS.read_text(encoding="utf-8")
    spawner = SPAWNER.read_text(encoding="utf-8")
    boss = BOSS.read_text(encoding="utf-8")
    entry = ENTRY.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    compile_text = COMPILE.read_text(encoding="utf-8")

    for snippet in [
        "public Vector3 LastKnownPosition;",
        "public float LastReachableTime;",
        "public float LastHurtTime;",
        "public bool RuntimeRegistered;",
        "public float NextSkillTime;",
        "public int SkillSequence;",
    ]:
        result = require(models, snippet, "boss instance state")
        if result:
            return result

    for snippet in [
        "ApplyZombieModeBossTuning(boss, kind)",
        "RegisterZombieModeBossRuntime(runId, boss, kind)",
        "SpawnZombieModeWaveAsync(runId, effectiveSpawnPointCount, false)",
        "35f",
        "18f",
        "25f",
        "28f",
        "26f",
        "Random.Range(min, max + 1)",
    ]:
        result = require(spawner + waves, snippet, "boss spawn/tuning")
        if result:
            return result

    for snippet in [
        "TickZombieModeBossController(deltaTime)",
        "HandleZombieModeBossHurt(runId, marker, victim)",
        "HandleZombieModeBossDeathEffects(runId, marker, character)",
    ]:
        source = entry + waves
        result = require(source, snippet, "boss integration")
        if result:
            return result

    for snippet in [
        "private void RegisterZombieModeBossRuntime(int runId, CharacterMainControl boss, ZombieModeBossKind kind)",
        "private void TickZombieModeBossController(float deltaTime)",
        "ZombieModeTuning.BossStuckTimeoutSeconds",
        "TeleportZombieModeBossNearPlayer",
        "TryExecuteZombieModeBossSkill",
        "GetZombieModeBossSkillCooldown",
        "ApplyZombieModeBossShieldPulse",
        "StartZombieModeTelegraphedAreaDamage",
        "HandleZombieModeBossHurt",
        "HandleZombieModeBossDeathEffects",
        "ZombieModeBossKind.Splitter",
        "ZombieModeBossKind.Corruptor",
        "ZombieModeBossKind.Titan",
        "ZombieModeBossKind.Hunter",
        "ZombieModeBossKind.Shielder",
    ]:
        result = require(boss, snippet, "boss controller implementation")
        if result:
            return result

    if "ZombieMode\\ZombieModeBossController.cs" not in compile_text:
        return fail("ZombieModePhase5BossGuard: missing compile entry")

    print("ZombieModePhase5BossGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
