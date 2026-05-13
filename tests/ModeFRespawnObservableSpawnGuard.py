"""Guard: Mode F replacement respawn must use observable SpawnEnemyCore completion."""

from pathlib import Path
import sys


RESPAWN = Path("ModeF/ModeFRespawn.cs")
PHASES = Path("ModeF/ModeFPhases.cs")


def fail(message: str) -> int:
    print("ModeFRespawnObservableSpawnGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def require(text: str, needle: str, message: str) -> int | None:
    if needle not in text:
        return fail(message)
    return None


def forbid(text: str, needle: str, message: str) -> int | None:
    if needle in text:
        return fail(message)
    return None


def main() -> int:
    respawn = RESPAWN.read_text(encoding="utf-8")
    phases = PHASES.read_text(encoding="utf-8")

    wrapper = extract_method_body(respawn, "private bool RespawnModeFBoss")
    if wrapper is None:
        return fail("missing RespawnModeFBoss wrapper")
    for needle, message in (
        ("RespawnModeFBossAsync().Forget();", "Mode F respawn wrapper must dispatch the observable async implementation"),
        ("return true;", "Mode F respawn wrapper must preserve dispatch success semantics"),
    ):
        result = require(wrapper, needle, message)
        if result is not None:
            return result

    async_body = extract_method_body(respawn, "private async UniTaskVoid RespawnModeFBossAsync")
    if async_body is None:
        return fail("missing RespawnModeFBossAsync")
    for needle, message in (
        ("EnemySpawnCoreResult result = await SpawnEnemyCoreInternalAsync(", "Mode F respawn must await observable spawn core completion"),
        ("CompleteModeFBossRespawnAttempt(false, true);", "Mode F respawn failures must requeue through the existing completion path"),
        ("CompleteModeFBossRespawnAttempt(configured, true);", "Mode F respawn success must complete through the existing completion path"),
        ("skipDragonDescendant: !selectedDragonDescendant", "Mode F respawn must preserve dragon descendant skip semantics"),
        ("skipDragonKing: true", "Mode F respawn must still skip dragon king"),
    ):
        result = require(async_body, needle, message)
        if result is not None:
            return result
    result = forbid(async_body, "SpawnEnemyCore(", "Mode F respawn async implementation must not use callback-based SpawnEnemyCore")
    if result is not None:
        return result

    configure_body = extract_method_body(respawn, "private bool ConfigureModeFRespawnedBoss")
    if configure_body is None:
        return fail("missing ConfigureModeFRespawnedBoss")
    for needle, message in (
        ("customPreset.aiCombatFactor = 1f;", "Mode F respawn must preserve AI combat factor normalization"),
        ("customPreset.showName = true;", "Mode F respawn must preserve boss health-bar names"),
        ("customPreset.showHealthBar = true;", "Mode F respawn must preserve boss health-bar visibility"),
        ("SetModeFBossDisplayName(ctx.character, spawnedPreset.displayName, spawnedTeam);", "Mode F respawn must preserve display name setup"),
        ("ctx.character.SetTeam(spawnedTeam);", "Mode F respawn must preserve combat team setup"),
        ("RegisterModeESharedRuntimeForModeFBoss(ctx.character, ctx.position);", "Mode F respawn must preserve shared Mode E runtime registration"),
        ("RegisterModeFBoss(ctx.character);", "Mode F respawn must preserve Mode F boss registration"),
    ):
        result = require(configure_body, needle, message)
        if result is not None:
            return result

    for text, needle, message in (
        (respawn, "modeFRespawnInFlightCount > 0", "Mode F must preserve one in-flight respawn"),
        (respawn, "modeFPendingRespawnCount = Mathf.Max(0, modeFPendingRespawnCount - 1);", "Mode F must preserve one-at-a-time pending decrement"),
        (phases, "private const float MODEF_BOSS_RETARGET_INTERVAL = 1.5f;", "Mode F target refresh interval must stay 1.5s"),
        (phases, "private const float MODEF_BOSS_INTEGRITY_CHECK_INTERVAL = 1f;", "Mode F integrity interval must stay 1s"),
        (phases, "modeFBossAiControllers.Remove(boss);", "Mode F AI controller cache must be invalidated during boss cleanup"),
    ):
        result = require(text, needle, message)
        if result is not None:
            return result

    print("ModeFRespawnObservableSpawnGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
