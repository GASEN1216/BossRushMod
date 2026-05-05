"""Guard safe-zone minimap scene binding and truly reusable Zombie Tide Beacon."""

from pathlib import Path
import re
import sys


BEACON_CONFIG = Path("Integration/Items/ZombieTideBeaconConfig.cs")
BEACON_USAGE = Path("Integration/Items/ZombieTideBeaconUsage.cs")
EXTRACTION = Path("ZombieMode/ZombieModeExtractionController.cs")


def fail(message: str) -> int:
    print("ZombieModeSafeZoneMapPoiBeaconReuseGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail(label + " -> " + snippet)
    return 0


def require_regex(text: str, pattern: str, label: str) -> int:
    if not re.search(pattern, text, re.DOTALL):
        return fail(label + " -> " + pattern)
    return 0


def main() -> int:
    beacon_config = BEACON_CONFIG.read_text(encoding="utf-8")
    beacon_usage = BEACON_USAGE.read_text(encoding="utf-8")
    extraction = EXTRACTION.read_text(encoding="utf-8")

    for snippet in [
        "public const float INFINITE_DURABILITY = 999f;",
        "item.MaxDurability = INFINITE_DURABILITY;",
        "item.Durability = INFINITE_DURABILITY;",
        "usageUtils.useDurability = false;",
        "usageUtils.durabilityUsage = 0;",
        "public static void EnsureReusableInstance(Item item)",
    ]:
        result = require(beacon_config, snippet, "beacon must use non-consuming durability mode")
        if result:
            return result

    result = require(
        beacon_usage,
        "ZombieTideBeaconConfig.EnsureReusableInstance(item);",
        "beacon use path must repair older saved beacon instances before vanilla CA_UseItem cleanup",
    )
    if result:
        return result

    for snippet in [
        "private string ResolveZombieModeSafeZoneMapSceneId()",
        "SceneInfoCollection.GetSceneID(activeScene.buildIndex)",
        "DevLog(\"[ZombieMode] 安全区地图标记已创建:",
    ]:
        result = require(extraction, snippet, "safe zone POI must bind to the active minimap scene")
        if result:
            return result

    result = require_regex(
        extraction,
        r"SimplePointOfInterest\.Create\(\s*position,\s*sceneId,\s*displayName,\s*null,\s*false\)",
        "safe zone POI should show a visible center icon as well as the area circle",
    )
    if result:
        return result

    print("ZombieModeSafeZoneMapPoiBeaconReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
