"""
Guard: GetAllMapConfigs must preserve JSON-declared map order.

Runtime map data is JSON-only. The baseline nine maps carry sortOrder values
matching the old UI order; registry-only maps may append with larger sortOrder.
"""

from pathlib import Path
import json
import sys


SOURCE = Path("ModBehaviour.cs")
REGISTRY = Path("Common/MapConfig/MapSpawnPointRegistry.cs")
SPAWN_POINTS_DIR = Path("Assets/SpawnPoints")

EXPECTED_BASELINE_ORDER = [
    "Level_DemoChallenge_1",
    "Level_ChallengeSnow",
    "Level_GroundZero_1",
    "Level_HiddenWarehouse",
    "Level_Farm_01",
    "Level_JLab_1",
    "Level_StormZone_B0",
    "Level_SnowMilitaryBase",
    "Level_SnowMilitaryBase_ColdStorage",
]


def fail(message: str) -> int:
    print("MapSpawnRegistryOrderGuard: " + message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""
    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    block = extract_block(text, "public static BossRushMapConfig[] GetAllMapConfigs()")
    if not block:
        return fail("missing GetAllMapConfigs block")

    if "LegacyMapSpawnPointFallback" in block:
        return fail("GetAllMapConfigs still depends on LegacyMapSpawnPointFallback")

    if "return _mapSpawnRegistry.All().ToArray();" not in block:
        return fail("GetAllMapConfigs must return registry ordering directly")

    registry_text = REGISTRY.read_text(encoding="utf-8")
    if "_orderedConfigs" not in registry_text or "sortOrder" not in registry_text:
        return fail("MapSpawnPointRegistry must maintain ordered configs by sortOrder")

    observed = []
    for json_file in sorted(SPAWN_POINTS_DIR.glob("*.json")):
        data = json.loads(json_file.read_text(encoding="utf-8"))
        if "sortOrder" not in data:
            return fail(f"{json_file} is missing sortOrder")
        observed.append((int(data["sortOrder"]), data.get("sceneName", "")))

    observed.sort()
    baseline = [scene for _, scene in observed[:len(EXPECTED_BASELINE_ORDER)]]
    if baseline != EXPECTED_BASELINE_ORDER:
        return fail("baseline JSON sortOrder does not match the established map UI order")

    print("MapSpawnRegistryOrderGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
