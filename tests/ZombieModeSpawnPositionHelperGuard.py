"""ZombieModeSpawnPositionHelperGuard: 丧尸刷怪几何 helper 迁移必须保持编译链一致。"""

from pathlib import Path
import sys


ROOTS = [Path("ZombieMode"), Path("WavesArena"), Path("Utilities")]
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    compile_text = COMPILE.read_text(encoding="utf-8")
    if r"Utilities\SpawnPositionHelper.cs" not in compile_text:
        return fail("ZombieModeSpawnPositionHelperGuard: compile_official.bat missing Utilities\\SpawnPositionHelper.cs")

    if r"Utilities\SpawnPointGeometryHelper.cs" in compile_text:
        return fail("ZombieModeSpawnPositionHelperGuard: compile_official.bat still references old SpawnPointGeometryHelper")

    for root in ROOTS:
        for path in root.glob("*.cs"):
            text = path.read_text(encoding="utf-8")
            if "SpawnPointGeometryHelper" in text and path.name != "SpawnPositionHelper.cs":
                return fail("ZombieModeSpawnPositionHelperGuard: old helper reference remains in " + str(path))

    helper_text = Path("Utilities/SpawnPositionHelper.cs").read_text(encoding="utf-8")
    for snippet in [
        "internal static class SpawnPositionHelper",
        "internal static bool TryFindAroundPlayer(",
        "internal static bool TrySampleNavMesh(",
        "internal static bool TrySnapToGround(",
        "internal static bool PassesMinPlayerDistance(",
        "private static float GetXZDistanceSqr(",
        "int candidateIndex = (normalizedStartIndex + i) % ringCount;",
    ]:
        if snippet not in helper_text:
            return fail("ZombieModeSpawnPositionHelperGuard: SpawnPositionHelper missing API -> " + snippet)

    if "public static" in helper_text:
        return fail("ZombieModeSpawnPositionHelperGuard: SpawnPositionHelper should keep internal API surface")

    xz_selection = helper_text.split("internal static bool PassesMinPlayerDistance(", 1)[1].split(
        "internal static List<Vector3> FindMultipleSafeSpawnPoints(", 1
    )[0]
    if "delta.y = 0f;" not in xz_selection or xz_selection.count(
        "GetXZDistanceSqr(spawnPoints[i], playerPos)"
    ) != 2:
        return fail("ZombieModeSpawnPositionHelperGuard: nearest safe selection must ignore Y distance")
    arena_selection = helper_text.split("internal static List<Vector3> FindMultipleSafeSpawnPoints(", 1)[1].split(
        "internal static bool TryFindAroundPlayer(", 1
    )[0]
    if arena_selection.count("(spawnPoints[i] - playerPos).sqrMagnitude") != 2:
        return fail("ZombieModeSpawnPositionHelperGuard: arena multi-spawn selection must retain 3D distance")

    print("ZombieModeSpawnPositionHelperGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
