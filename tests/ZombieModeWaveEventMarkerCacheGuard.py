"""Guard: zombie global health event paths should reuse registered runtime markers."""

from pathlib import Path
import sys
from cs_source_util import clean_source


RUNTIME = Path("ZombieMode/ZombieModeEnemyRuntime.cs")
MODULE = Path("ZombieMode/ZombieModeRuntimeModule_EnemyRuntime.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")


def fail(message: str) -> int:
    print("ZombieModeWaveEventMarkerCacheGuard: FAIL - " + message)
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
                return text[brace_start:idx + 1]

    return None


def main() -> int:
    runtime = clean_source(RUNTIME.read_text(encoding="utf-8-sig"))
    module = clean_source(MODULE.read_text(encoding="utf-8-sig"))
    waves = clean_source(WAVES.read_text(encoding="utf-8-sig"))

    required_module = [
        "private readonly HashSet<int> zombieModeEnemyInstanceIds",
        "private readonly Dictionary<int, ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkersByInstanceId",
        "internal bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl character, out ZombieModeEnemyRuntimeMarker marker)",
        "int instanceId = character.GetInstanceID();",
        "zombieModeEnemyMarkersByInstanceId.TryGetValue(instanceId, out marker)",
        "internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character, ZombieModeEnemyRuntimeMarker marker)",
        "zombieModeEnemyMarkersByInstanceId[instanceId] = marker;",
        "zombieModeEnemyMarkersByInstanceId.Remove(instanceId);",
        "zombieModeEnemyMarkersByInstanceId.Clear();",
        "RegisterZombieModeEnemyInstanceId(enemy, marker);",
    ]
    for snippet in required_module:
        if snippet not in module:
            return fail("missing RuntimeModule marker registry snippet -> " + snippet)

    for field in [
        "private readonly System.Collections.Generic.HashSet<int> zombieModeEnemyInstanceIds",
        "private readonly System.Collections.Generic.Dictionary<int, ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkersByInstanceId",
    ]:
        if field in runtime:
            return fail("marker indexes must have one owner in ZombieModeRuntimeModule -> " + field)

    for method, forward in [
        ("IsZombieModeKnownEnemy", "module.IsZombieModeKnownEnemy(character)"),
        ("TryGetZombieModeKnownEnemyMarker", "module.TryGetZombieModeKnownEnemyMarker(character, out marker)"),
        ("RegisterZombieModeEnemyInstanceId", "module.RegisterZombieModeEnemyInstanceId(character, marker)"),
        ("UnregisterZombieModeEnemyInstanceId", "module.UnregisterZombieModeEnemyInstanceId(character)"),
        ("ClearZombieModeEnemyInstanceIds", "module.ClearZombieModeEnemyInstanceIds()"),
        ("RegisterZombieModeEnemyRuntimeShell", "module.RegisterZombieModeEnemyRuntimeShell("),
    ]:
        if forward not in runtime:
            return fail("host compatibility bridge must forward marker state to RuntimeModule -> " + method)

    register = extract_method_body(module, "internal ZombieModeEnemyRuntimeMarker RegisterZombieModeEnemyRuntimeShell(")
    if register is None:
        return fail("missing RuntimeModule RegisterZombieModeEnemyRuntimeShell body")
    gate = register.find("if (!IsZombieModeRunValid(runId) || enemy == null || enemy.gameObject == null)")
    get_component = register.find("enemy.gameObject.GetComponent<ZombieModeEnemyRuntimeMarker>()")
    add_component = register.find("enemy.gameObject.AddComponent<ZombieModeEnemyRuntimeMarker>()")
    cache_register = register.find("RegisterZombieModeEnemyInstanceId(enemy, marker);")
    run_only_register = register.find("RegisterZombieModeRunOnlyObject(")
    if min(gate, get_component, add_component, cache_register, run_only_register) < 0 or not (
        gate < get_component < add_component < cache_register < run_only_register
    ):
        return fail("RuntimeModule marker registration must keep RunId gate, component setup, cache, then RunOnly registration order")

    hurt = extract_method_body(waves, "private void HandleZombieModeHealthHurt(")
    if hurt is None:
        return fail("missing HandleZombieModeHealthHurt body")
    dead = extract_method_body(waves, "private void HandleZombieModeHealthDead(")
    if dead is None:
        return fail("missing HandleZombieModeHealthDead body")

    for name, body in [
        ("HandleZombieModeHealthHurt", hurt),
        ("HandleZombieModeHealthDead", dead),
    ]:
        if "GetComponent<ZombieModeEnemyRuntimeMarker>()" in body:
            return fail(name + " still performs direct marker GetComponent on the global event hot path")
        if "TryGetZombieModeKnownEnemyMarker(" not in body:
            return fail(name + " does not reuse registered marker cache")
        if "IsZombieModeKnownEnemy(" in body:
            return fail(name + " still performs a separate known-enemy lookup before marker cache lookup")

    hurt_required = [
        "ZombieModeEnemyRuntimeMarker marker;",
        "if (victim == null || !TryGetZombieModeKnownEnemyMarker(victim, out marker))",
        "if (marker != null && marker.RunId == runId)",
        "TryHandleZombieModeSafeZonePlayerAttack(runId, damageInfo, victim);",
    ]
    for snippet in hurt_required:
        if snippet not in hurt:
            return fail("HandleZombieModeHealthHurt missing preserved hot-path structure -> " + snippet)

    stealth_break_index = hurt.find("TryHandleZombieModeSafeZonePlayerAttack(runId, damageInfo, victim);")
    marker_lookup_index = hurt.find("TryGetZombieModeKnownEnemyMarker(victim, out marker)")
    if stealth_break_index < 0 or marker_lookup_index < 0 or stealth_break_index < marker_lookup_index:
        return fail("HandleZombieModeHealthHurt must require the registered zombie marker before stealth break")

    dead_required = [
        "ZombieModeEnemyRuntimeMarker marker;",
        "if (character == null || !TryGetZombieModeKnownEnemyMarker(character, out marker))",
        "if (marker == null || marker.RunId != runId)",
    ]
    for snippet in dead_required:
        if snippet not in dead:
            return fail("HandleZombieModeHealthDead missing marker-cache structure -> " + snippet)

    lethal_stealth_index = dead.find(
        "TryHandleZombieModeSafeZonePlayerAttack(runId, damageInfo, character);"
    )
    settled_index = dead.find("marker.DeathSettled = true;")
    unregister_index = dead.find("UnregisterZombieModeEnemyInstanceId(character);")
    stars_index = dead.find("SpawnZombieModeDeathStars(runId, character.transform.position, pointValue, starCount);")
    drop_index = min(
        (idx for idx in [dead.find("TrySpawnZombieModeBossDrop(runId, marker, character.transform.position);"),
                         dead.find("TrySpawnZombieModeEnemyDrop(runId, marker, character.transform.position);")] if idx >= 0),
        default=-1,
    )
    if (
        lethal_stealth_index < 0
        or settled_index < 0
        or unregister_index < 0
        or stars_index < 0
        or drop_index < 0
        or not (lethal_stealth_index < settled_index < unregister_index < stars_index < drop_index)
    ):
        return fail(
            "HandleZombieModeHealthDead must preserve lethal stealth handling, settlement, index removal, death stars, and drop order"
        )

    print("ZombieModeWaveEventMarkerCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
