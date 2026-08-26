"""Guard: zombie global health event paths should reuse registered runtime markers."""

from pathlib import Path
import sys


RUNTIME = Path("ZombieMode/ZombieModeEnemyRuntime.cs")
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
    runtime = RUNTIME.read_text(encoding="utf-8-sig")
    waves = WAVES.read_text(encoding="utf-8-sig")

    required_runtime = [
        "private readonly System.Collections.Generic.Dictionary<int, ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkersByInstanceId",
        "internal bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl character, out ZombieModeEnemyRuntimeMarker marker)",
        "int instanceId = character.GetInstanceID();",
        "zombieModeEnemyMarkersByInstanceId.TryGetValue(instanceId, out marker)",
        "internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character, ZombieModeEnemyRuntimeMarker marker)",
        "zombieModeEnemyMarkersByInstanceId[instanceId] = marker;",
        "zombieModeEnemyMarkersByInstanceId.Remove(instanceId);",
        "zombieModeEnemyMarkersByInstanceId.Clear();",
        "RegisterZombieModeEnemyInstanceId(enemy, marker);",
    ]
    for snippet in required_runtime:
        if snippet not in runtime:
            return fail("missing marker registry snippet -> " + snippet)

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
    unregister_index = dead.find("UnregisterZombieModeEnemyInstanceId(character);")
    if (
        lethal_stealth_index < 0
        or unregister_index < 0
        or lethal_stealth_index > unregister_index
    ):
        return fail(
            "HandleZombieModeHealthDead must process lethal stealth break before marker unregister"
        )

    print("ZombieModeWaveEventMarkerCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
