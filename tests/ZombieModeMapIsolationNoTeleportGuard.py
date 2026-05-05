"""Guard ZombieMode map isolation and no extra BossRush custom-position teleport."""

from pathlib import Path
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
MAP_ISOLATION = Path("ZombieMode/ZombieModeMapIsolation.cs")


def fail(message: str) -> int:
    print("ZombieModeMapIsolationNoTeleportGuard: FAIL - " + message)
    return 1


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    entry = ENTRY.read_text(encoding="utf-8")
    map_isolation = MAP_ISOLATION.read_text(encoding="utf-8")

    wait_method = extract_method(entry, "private System.Collections.IEnumerator WaitForZombieModeTargetSceneActiveThenInitialize")
    if not wait_method:
        return fail("target-scene wait coroutine not found")
    if "TeleportPlayerToCustomPosition" in wait_method:
        return fail("ZombieMode must not call BossRush custom-position teleport after map load")

    init_method = extract_method(entry, "private bool InitializeZombieModeRunAfterMapLoaded")
    if not init_method:
        return fail("InitializeZombieModeRunAfterMapLoaded not found")

    collect_index = init_method.find("CollectZombieModeSpawnPoints(runId)")
    isolation_index = init_method.find("ApplyZombieModeMapIsolationShell(runId)")
    if collect_index < 0 or isolation_index < 0:
        return fail("initialization must collect spawn points and apply map isolation")
    if collect_index > isolation_index:
        return fail("ZombieMode must collect original spawn points before isolating original spawners")
    if "PreCacheMapSpawnerPositions();" not in init_method and "PreCacheMapSpawnerPositions();" not in map_isolation:
        return fail("ZombieMode must precache original spawner positions before isolation")

    disable_method = extract_method(map_isolation, "private void DisableZombieModeOriginalSpawners")
    if not disable_method:
        return fail("DisableZombieModeOriginalSpawners not found")
    for snippet in [
        "spawnersDisabled = false;",
        "DisableAllSpawners();",
        "已复用 BossRush 进图逻辑清理原版刷怪器",
    ]:
        if snippet not in disable_method:
            return fail("original spawner disable path must reuse BossRush entry cleanup: " + snippet)

    if "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.MapIsolation, null, record.GameObject, null);" in disable_method:
        return fail("original spawner isolation must not register spawner GameObjects into run-only destroy chain")

    for stale in [
        "OriginalSpawnerIsolationHelper.Disable(",
        "Destroy(spawner.gameObject);",
        "_cachedCreatedField.SetValue(spawner, true);",
        "GetComponentsInChildren<Light>(true)",
        "light.transform.SetParent(null);",
    ]:
        if stale in disable_method:
            return fail("ZombieMode must reuse shared BossRush spawner cleanup instead of a local copy: " + stale)

    restore_method = extract_method(map_isolation, "private void RestoreZombieModeOriginalSpawners")
    if not restore_method:
        return fail("RestoreZombieModeOriginalSpawners not found")

    print("ZombieModeMapIsolationNoTeleportGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
