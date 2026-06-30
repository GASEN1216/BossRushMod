from pathlib import Path
import sys


OBJECT_CACHE = Path("Common/Infrastructure/ObjectCache.cs")
WEDDING = Path("Integration/Wedding/WeddingBuildingInjector_DataEventsAndRuntime.cs")
WISH = Path("Integration/WishFountain/WishFountainBuilder_DataEventsAndRuntime.cs")


def fail(message: str) -> int:
    print("SceneObjectTypeCacheGuard: FAIL - " + message)
    return 1


def main() -> int:
    cache_text = OBJECT_CACHE.read_text(encoding="utf-8", errors="ignore")
    wedding_text = WEDDING.read_text(encoding="utf-8", errors="ignore")
    wish_text = WISH.read_text(encoding="utf-8", errors="ignore")

    for token in [
        "_cachedObjectsByType",
        "GetSceneObjectsByType(Type type)",
        "InvalidateSceneObjectsByType(Type type)",
        "_cachedObjectsByType.Clear();",
    ]:
        if token not in cache_text:
            return fail("ObjectCache missing token -> " + token)

    if "FindObjectsOfType(buildingType) as Component[]" in wedding_text:
        return fail("wedding runtime still uses raw typed scene scan cast")
    if "UnityEngine.Object.FindObjectsOfType(buildingType)" in wedding_text:
        return fail("wedding runtime still uses raw typed scene scan")
    if "UnityEngine.Object.FindObjectsOfType(buildingType)" in wish_text:
        return fail("wish runtime still uses raw typed scene scan")

    for token in [
        "ObjectCache.GetSceneObjectsByType(buildingType)",
        "ObjectCache.InvalidateSceneObjectsByType(GetBuildingType())",
    ]:
        if token not in wedding_text:
            return fail("wedding runtime missing cache token -> " + token)

    for token in [
        "ObjectCache.GetSceneObjectsByType(buildingType)",
        "ObjectCache.InvalidateSceneObjectsByType(GetBuildingType())",
    ]:
        if token not in wish_text:
            return fail("wish runtime missing cache token -> " + token)

    print("SceneObjectTypeCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
