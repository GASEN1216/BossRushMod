from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
RUNTIME = Path("ZombieMode/ZombieModeEnemyRuntime.cs")
POLLUTION = Path("ZombieMode/ZombieModePollution.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModePhase3PollutionGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    if not POLLUTION.exists():
        return fail("ZombieModePhase3PollutionGuard: missing ZombieModePollution.cs")

    models = MODELS.read_text(encoding="utf-8")
    runtime = RUNTIME.read_text(encoding="utf-8")
    pollution = POLLUTION.read_text(encoding="utf-8")
    spawner = SPAWNER.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    compile_text = COMPILE.read_text(encoding="utf-8")

    for snippet in [
        "public enum ZombieModeEnemyKind",
        "Normal",
        "Special",
        "Elite",
        "public enum ZombieModeSpecialKind",
        "Sprinter",
        "Exploder",
        "Plague",
        "Summoner",
        "Harasser",
        "public enum ZombieModeEliteAffix",
        "Swift",
        "Frenzied",
        "Tough",
        "Stalwart",
        "Regenerating",
        "Burst",
        "Plague",
        "Commander",
        "ToxicAura",
        "Splitting",
        "Shielded",
        "Adaptive",
    ]:
        result = require(models, snippet, "model contract")
        if result:
            return result

    for snippet in [
        "public ZombieModeEnemyKind EnemyKind;",
        "public ZombieModeSpecialKind SpecialKind;",
        "public readonly System.Collections.Generic.List<ZombieModeEliteAffix> EliteAffixes",
        "public float HealthMultiplier",
        "public float DamageMultiplier",
        "public float MoveSpeedMultiplier",
        "CalculateZombieModeEnemyPurificationPoints(isBoss, enemyKind)",
    ]:
        result = require(runtime, snippet, "runtime marker contract")
        if result:
            return result

    for snippet in [
        "private ZombieModeEnemyKind RollZombieModeEnemyKind()",
        "GetZombieModeSpecialChancePercent",
        "GetZombieModeEliteChancePercent",
        "private ZombieModeSpecialKind RollZombieModeSpecialKind()",
        "private List<ZombieModeEliteAffix> RollZombieModeEliteAffixes()",
        "GetZombieModeAffixUnlockTier",
        "IsZombieModeAffixCombinationAllowed",
        "private void ApplyZombieModeEnemyTuning(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)",
        "ApplyZombieModeEliteAffixTuning",
        "EnsureZombieModeThreatRuntime",
        "TryExecuteZombieModeEnemyRuntimeSkill",
        "ZombieModeThreatRuntime",
        "StartZombieModeTelegraphedAreaDamage",
        "GetZombieModeEliteAffixDisplayName",
        "ZombieModeTuning.StalwartRangedDamageMultiplier",
    ]:
        result = require(pollution, snippet, "pollution implementation")
        if result:
            return result

    for snippet in [
        "RollZombieModeEnemyKind()",
        "RollZombieModeSpecialKind()",
        "RollZombieModeEliteAffixes()",
        "RegisterZombieModeEnemyRuntimeShell(runId, zombie, false, ZombieModeBossKind.Titan, -1, enemyKind, specialKind, eliteAffixes)",
        "ApplyZombieModeEnemyTuning(zombie, marker)",
    ]:
        result = require(spawner, snippet, "spawner integration")
        if result:
            return result

    for snippet in [
        "marker.EnemyKind == ZombieModeEnemyKind.Elite",
        "HandleZombieModeEliteDeathEffects(runId, marker, character)",
        "HandleZombieModeSpecialDeathEffects(runId, marker, character)",
    ]:
        result = require(waves, snippet, "death effects integration")
        if result:
            return result

    if "ZombieMode\\ZombieModePollution.cs" not in compile_text:
        return fail("ZombieModePhase3PollutionGuard: missing compile entry")

    print("ZombieModePhase3PollutionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
