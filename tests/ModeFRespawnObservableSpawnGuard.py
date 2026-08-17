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

    death_body = extract_method_body(respawn, "private void OnModeFBossDied")
    if death_body is None:
        return fail("missing OnModeFBossDied")
    for needle, message in (
        ("bool replacementRequired = false;", "Mode F death handling must track an irrevocable replacement obligation"),
        ("replacementRequired = true;", "Mode F must claim replacement immediately after removing the tracked boss"),
        ("if (deadBossId != 0 && !replacementRequired)", "Mode F must not reopen a handled death after replacement became mandatory"),
        ("if (replacementRequired && modeFActive)", "Mode F must settle replacement debt from the death-handler finally block"),
        ("QueueModeFBossRespawn();", "Mode F death-handler finally block must queue the replacement"),
    ):
        result = require(death_body, needle, message)
        if result is not None:
            return result
    if death_body.count("QueueModeFBossRespawn();") != 1:
        return fail("OnModeFBossDied must settle exactly one replacement obligation")
    removal_index = death_body.find("if (!RemoveModeFBossReference(deadBoss))")
    claim_index = death_body.find("replacementRequired = true;")
    finally_index = death_body.find("finally")
    queue_index = death_body.find("QueueModeFBossRespawn();")
    if min(removal_index, claim_index, finally_index, queue_index) < 0 or not removal_index < claim_index < finally_index < queue_index:
        return fail("replacement order must be remove tracked boss -> claim debt -> finally -> queue")

    integrity_body = extract_method_body(respawn, "private void ModeFBossIntegrityCheck")
    if integrity_body is None:
        return fail("missing ModeFBossIntegrityCheck")
    for needle, message in (
        ("modeFState.ActiveBosses.RemoveAt(i);", "Mode F integrity recovery must remove each invalid tracked boss"),
        ("replacementRequired = true;", "Mode F integrity recovery must claim replacement immediately after removal"),
        ("if (replacementRequired && modeFActive)", "Mode F integrity recovery must settle replacement debt from finally"),
        ("QueueModeFBossRespawn();", "Mode F integrity recovery must queue the replacement"),
    ):
        result = require(integrity_body, needle, message)
        if result is not None:
            return result
    if integrity_body.count("QueueModeFBossRespawn();") != 1:
        return fail("ModeFBossIntegrityCheck must settle exactly one replacement per removed invalid boss")
    removal_index = integrity_body.find("modeFState.ActiveBosses.RemoveAt(i);")
    claim_index = integrity_body.find("replacementRequired = true;")
    finally_index = integrity_body.find("finally")
    queue_index = integrity_body.find("QueueModeFBossRespawn();")
    if min(removal_index, claim_index, finally_index, queue_index) < 0 or not removal_index < claim_index < finally_index < queue_index:
        return fail("integrity replacement order must be remove invalid boss -> claim debt -> finally -> queue")

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
        ("onCommit: (ctx) => ConfigureModeFRespawnedBoss(ctx, selectedDragonDescendant, spawnPos)", "Mode F respawn must commit registration inside the spawn-core activation barrier"),
        ("CompleteModeFBossRespawnAttempt(false, true);", "Mode F respawn failures must requeue through the existing completion path"),
        ("CompleteModeFBossRespawnAttempt(true, true);", "Mode F respawn success must complete through the existing completion path"),
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
