"""ZombieModePurificationPointPrefabCacheGuard: SoulCube prefab lookup must be prewarmed and cached, including cache misses."""

from pathlib import Path
import re
import sys


CONTROLLER = Path("ZombieMode/ZombiePurificationPointController.cs")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
RUNTIME_MODULE = Path("ZombieMode/ZombieModeRuntimeModule.cs")
HOST_BRIDGE = Path("ZombieMode/ZombieModeMapSelection.cs")


def fail(message: str) -> int:
    print("ZombieModePurificationPointPrefabCacheGuard: FAIL - " + message)
    return 1


def extract_method(text: str, method_name: str) -> str:
    match = re.search(r"\b" + re.escape(method_name) + r"\s*\([^)]*\)\s*\{", text)
    if match is None:
        return ""

    depth = 0
    for index in range(match.end() - 1, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[match.start():index + 1]
    return ""


def main() -> int:
    controller = CONTROLLER.read_text(encoding="utf-8")
    entry = ENTRY.read_text(encoding="utf-8")
    runtime_module = RUNTIME_MODULE.read_text(encoding="utf-8")
    host_bridge = HOST_BRIDGE.read_text(encoding="utf-8")

    try_get = extract_method(controller, "TryGetSoulCubePrefab")
    if not try_get:
        return fail("TryGetSoulCubePrefab not found")
    if "s_soulCubePrefabSearched && s_cachedSoulCubePrefab != null" in try_get:
        return fail("cache miss must stop repeated global Resources scans")
    for token in [
        "if (s_soulCubePrefabSearched)",
        "return s_cachedSoulCubePrefab;",
        "Resources.FindObjectsOfTypeAll<SoulCollector>()",
        "Resources.FindObjectsOfTypeAll<SoulCube>()",
    ]:
        if token not in try_get:
            return fail("SoulCube lookup missing token -> " + token)

    first_scan = min(
        index for index in [
            try_get.find("Resources.FindObjectsOfTypeAll<SoulCollector>()"),
            try_get.find("Resources.FindObjectsOfTypeAll<SoulCube>()"),
        ] if index >= 0
    )
    if try_get.find("if (s_soulCubePrefabSearched)") > first_scan:
        return fail("cache gate must run before any Resources scan")

    if "s_soulCubePrefabSearched = false;" in controller:
        return fail("ZombieMode runs must not force a fresh SoulCube global scan")

    for token in [
        "private static void PrewarmSoulCubePrefabCache()",
        "internal void PrepareSoulCubePrefabCacheForZombieRun()",
        "PrewarmSoulCubePrefabCache();",
    ]:
        if token not in controller:
            return fail("SoulCube cache prewarm missing token -> " + token)

    if "PrepareSoulCubePrefabCacheForZombieRun();" not in runtime_module:
        return fail("RuntimeModule initialization must request the SoulCube cache prewarm")
    if "PrepareSoulCubePrefabCacheForZombieRun();" not in host_bridge:
        return fail("host compatibility bridge must retain the SoulCube cache prewarm call")

    print("ZombieModePurificationPointPrefabCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
