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
        return fail("ZombieMode must collect original spawn points before destroying original spawners")

    disable_method = extract_method(map_isolation, "private void DisableZombieModeOriginalSpawners")
    if not disable_method:
        return fail("DisableZombieModeOriginalSpawners not found")
    for snippet in [
        'typeof(CharacterSpawnerRoot).GetField("created"',
        "_cachedCreatedField.SetValue(spawner, true);",
        "GetComponentsInChildren<Light>(true)",
        "light.transform.SetParent(null);",
        "Destroy(spawner.gameObject);",
        "已禁用并销毁原版刷怪器",
    ]:
        if snippet not in disable_method:
            return fail("original spawner disable path must match BossRush-style hard isolation: " + snippet)

    if "disabledObject.SetActive(false);" in disable_method:
        return fail("original spawners must be destroyed, not only SetActive(false)")
    for stale in [
        "spawner.gameObject.scene != scene",
        "!spawner.gameObject.activeSelf",
    ]:
        if stale in disable_method:
            return fail("original spawner destruction must not skip by scene or activeSelf like the previous broken path: " + stale)

    print("ZombieModeMapIsolationNoTeleportGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
