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
        "TryProcessZombieModeSafeZoneStealthBreak(runId, damageInfo, victim);",
    ]
    for snippet in hurt_required:
        if snippet not in hurt:
            return fail("HandleZombieModeHealthHurt missing preserved hot-path structure -> " + snippet)

    if hurt.find("TryProcessZombieModeSafeZoneStealthBreak(runId, damageInfo, victim);") < hurt.find("if (marker != null && marker.RunId == runId)"):
        return fail("HandleZombieModeHealthHurt must keep safe-zone stealth break after marker-gated affix handling")

    dead_required = [
        "ZombieModeEnemyRuntimeMarker marker;",
        "if (character == null || !TryGetZombieModeKnownEnemyMarker(character, out marker))",
        "if (marker == null || marker.RunId != runId)",
    ]
    for snippet in dead_required:
        if snippet not in dead:
            return fail("HandleZombieModeHealthDead missing marker-cache structure -> " + snippet)

    print("ZombieModeWaveEventMarkerCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
