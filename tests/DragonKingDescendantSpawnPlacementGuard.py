"""DragonKingDescendantSpawnPlacementGuard: 孩儿护我召唤的龙裔不能落进墙里（2026-11-24 鸭王杯玩家反馈）。

鸭王杯 / Mode E / 宿命回响不用场景刷怪点，龙裔落在龙皇起飞点附近。这个后备落点必须：
- 以起飞点为圆心找导航面上与起飞点连通、同层的位置（不是世界 +Z 固定偏移）；
- 起飞点到候选点、到吸附后的落点之间都没有官方墙体层；
- 全部失败时落在起飞点本身。
"""

from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402


CHILD = Path("Integration/DragonKing/DragonKingAbilityController_ChildProtection.cs")


def fail(message: str) -> int:
    print("DragonKingDescendantSpawnPlacementGuard: FAIL - " + message)
    return 1


def method_body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0 or source.find(signature, start + 1) >= 0:
        return ""
    opening = source.index("{", start)
    depth, index = 1, opening + 1
    while depth:
        depth += (source[index] == "{") - (source[index] == "}")
        index += 1
    return source[opening:index]


def main() -> int:
    child = clean_source(CHILD.read_text(encoding="utf-8-sig"))
    spawn_point = method_body(child, "private Vector3 GetRandomSpawnPoint()")
    if not spawn_point:
        return fail("GetRandomSpawnPoint missing")
    if re.search(r"Vector3\.forward\s*\*", spawn_point):
        return fail("fallback must not use a fixed world-forward offset (lands inside walls in narrow arenas)")
    if "return ResolveDescendantSpawnNear(ground);" not in spawn_point or "preFlyPosition" not in spawn_point:
        return fail("fallback must resolve around the pre-flight ground position")

    resolver = method_body(child, "internal static Vector3 ResolveDescendantSpawnNear(Vector3 ground)")
    if not resolver:
        return fail("ResolveDescendantSpawnNear missing")
    for token in (
        "GameplayDataSettings.Layers.wallLayerMask",
        "SpawnPositionHelper.TryResolveReachableFrom(candidate, ground,",
        "DescendantSpawnMaxHeightDelta",
        "Physics.Linecast(ground + lift, candidate + lift, wallMask, QueryTriggerInteraction.Ignore)",
        "Physics.Linecast(ground + lift, resolved + lift, wallMask, QueryTriggerInteraction.Ignore)",
        "return ground;",
    ):
        if token not in resolver:
            return fail("descendant placement lost a safety check -> " + token)

    print("DragonKingDescendantSpawnPlacementGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
