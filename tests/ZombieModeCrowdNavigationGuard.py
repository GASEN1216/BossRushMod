"""ZombieModeCrowdNavigationGuard: 尸潮寻路节流与叠罗汉拨离的结构不变式（2026-11-24 玩家反馈）。

- Harmony 前缀挂在官方 AI_PathControl.MoveToPos(Vector3) 上，模式未激活时第一句就放行（§4.12 关闭时 O(1)）；
  判定异常一律放行官方寻路，不能让 Mod 异常把丧尸寻路整体卡死。
- 只拦本局登记、未结算、未移除的丧尸；放行后才记录请求时刻、目标与出发点。
- 叠罗汉源头预防：登记时给丧尸挂共享的 ECM2 碰撞行为回调，别的角色不可站、不可跨上、不可停靠、不随其移动；
  回调在 ECM2 每帧移动里执行，不记日志、不分配、不查场景，不覆盖别的系统已挂的回调。
- 叠罗汉兜底检测复用每秒一次的恢复巡检，不新增 Update；拨离走 ECM2 SetPositionAndRotation 同步控制器，
  并在可达性之外做隔墙检查。
- 刷怪落点散开与拨离共用同一个隔墙判定。
"""

from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402


CROWD = Path("ZombieMode/ZombieModeCrowdNavigation.cs")
RECOVERY = Path("ZombieMode/ZombieModeRuntimeModule_Recovery.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
SAFE_ZONE = Path("ZombieMode/ZombieModeSafeZoneController.cs")
ENEMY_RUNTIME = Path("ZombieMode/ZombieModeRuntimeModule_EnemyRuntime.cs")


def fail(message: str) -> int:
    print("ZombieModeCrowdNavigationGuard: FAIL - " + message)
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


def compact(text: str) -> str:
    return re.sub(r"\s+", "", text)


def main() -> int:
    crowd = clean_source(CROWD.read_text(encoding="utf-8-sig"))

    patch_attribute = "[HarmonyPatch(typeof(AI_PathControl), nameof(AI_PathControl.MoveToPos), new System.Type[] { typeof(Vector3) })]"
    if compact(patch_attribute) not in compact(crowd):
        return fail("path throttle must patch the official AI_PathControl.MoveToPos(Vector3)")

    prefix = method_body(crowd, "private static bool Prefix(AI_PathControl __instance, Vector3 pos)")
    if not prefix:
        return fail("path throttle prefix missing")
    gate = "ModBehaviour owner = ModBehaviour.Instance; if (owner == null || !owner.IsZombieModeActive || __instance == null) { return true; }"
    if not compact(prefix).startswith(compact("{" + gate)):
        return fail("prefix must early-return to official pathing before any work when Zombie mode is inactive")
    if compact("try { return ZombieModeCrowdNavigation.AllowPathRequest(owner, __instance, pos); }") not in compact(prefix):
        return fail("prefix must delegate the decision inside try")
    catch_block = prefix[prefix.find("catch"):]
    if "return true;" not in catch_block:
        return fail("decision exceptions must fall back to official pathing")

    allow = method_body(crowd, "internal static bool AllowPathRequest(ModBehaviour owner, AI_PathControl pathControl, Vector3 target)")
    if not allow:
        return fail("AllowPathRequest missing")
    for token in ("owner.TryGetZombieModeKnownEnemyMarker(agent, out marker)",
                  "marker.RunId != owner.ZombieModeCurrentRunId",
                  "marker.DeathSettled",
                  "marker.RemovedFromRuntime",
                  "pathControl.WaitingForPathResult",
                  "pathControl.ReachedEndOfPath"):
        if token not in allow:
            return fail("throttle must only gate current-run live zombies with official path state -> " + token)
    record_at = allow.find("marker.LastPathRequestTime = now;")
    decide_at = allow.find("ShouldThrottleZombieModePathRequest(")
    if record_at < 0 or decide_at < 0 or record_at < decide_at:
        return fail("request bookkeeping must happen only after the throttle allows the request")

    decision = method_body(crowd, "internal static bool ShouldThrottleZombieModePathRequest(")
    for token in ("ZombieModeTuning.PathRequestPendingTimeoutSeconds",
                  "if (!hasPath || reachedEndOfPath)",
                  "ZombieModeTuning.PathRepathTeleportSpeed",
                  "ZombieModeTuning.PathRepathTimingSlackSeconds",
                  "ZombieModeTuning.PathRepathMaxIntervalSeconds"):
        if token not in decision:
            return fail("throttle decision lost a release condition -> " + token)

    for forbidden in ("void Update(", "void LateUpdate(", "void FixedUpdate(", "FindObjectsOfType", "FindObjectOfType"):
        if forbidden in crowd:
            return fail("crowd navigation must not add per-frame loops or scene scans -> " + forbidden)

    recovery = clean_source(RECOVERY.read_text(encoding="utf-8-sig"))
    sweep = method_body(recovery, "internal void MonitorZombieModeEnemyRecovery(CharacterMainControl player)")
    if compact("enemyRecoveryMonitor.MonitorEnemyRecovery(enemy, player, marker); CheckZombieModeStackedEnemy(enemy, marker);") not in compact(sweep):
        return fail("stacked-zombie check must ride the existing 1 s recovery sweep right after shared recovery")

    unstack = method_body(crowd, "internal void CheckZombieModeStackedEnemy(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)")
    for token in ("ZombieModeTuning.StackedSamplesBeforeUnstack",
                  "movement.SetPositionAndRotation(destination, enemy.transform.rotation, false);",
                  "marker.HasPathRequest = false;"):
        if token not in unstack:
            return fail("unstack must debounce, move through ECM2 and force a re-path -> " + token)
    if "transform.position =" in unstack:
        return fail("unstack must not write transform.position directly (desyncs the official controller)")
    finder = method_body(crowd, "private bool TryFindZombieModeUnstackPosition(Vector3 basePosition, out Vector3 destination)")
    for token in ("SpawnPositionHelper.TryResolveReachableFrom(", "ZombieModeTuning.UnstackMaxHeightDelta",
                  "ZombieModeCrowdNavigation.IsBlockedByWall(basePosition, resolved)"):
        if token not in finder:
            return fail("unstack destination must be reachable, same-floor and wall-free -> " + token)

    # 叠罗汉源头预防：登记时给丧尸挂 ECM2 碰撞行为，别的角色不可站 / 跨上 / 停靠 / 跟随（2026-11-24 复查补）。
    callback_decl = "internal static readonly ECM2.CharacterMovement.CollisionBehaviourCallback ZombieCollisionBehaviour = ResolveZombieCollisionBehaviour;"
    if compact(callback_decl) not in compact(crowd):
        return fail("stacking prevention must share one static ECM2 collision callback (no per-zombie allocation)")
    const_at = crowd.find("private const ECM2.CollisionBehaviour OtherCharacterCollisionBehaviour =")
    const_decl = crowd[const_at:crowd.find(";", const_at)] if const_at >= 0 else ""
    for flag in ("NotWalkable", "CanNotStepOn", "CanNotPerchOn", "CanNotRideOn"):
        if "ECM2.CollisionBehaviour." + flag not in const_decl:
            return fail("other characters must not be standable / steppable / perchable / rideable for zombies -> " + flag)
    resolve = method_body(crowd, "private static ECM2.CollisionBehaviour ResolveZombieCollisionBehaviour(Collider collider)")
    for token in ("collider.attachedRigidbody", "body.TryGetComponent(out other)",
                  "OtherCharacterCollisionBehaviour", "ECM2.CollisionBehaviour.Default", "catch"):
        if token not in resolve:
            return fail("collision callback must detect ECM2 characters by rigidbody and fall back to Default -> " + token)
    for forbidden in ("DevLog", "new ", "GetComponent<", "FindObject", "Physics."):
        if forbidden in resolve:
            return fail("collision callback runs inside ECM2 per-frame movement; no logging, allocation or scene queries -> " + forbidden)
    apply_crowd = method_body(crowd, "internal void ApplyZombieModeCrowdMovement(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)")
    for token in ("marker.HasPathRequest = false;", "marker.StackedSampleCount = 0;", "marker.CachedMovement = movement;",
                  "movement.collisionBehaviourCallback == null",
                  "movement.collisionBehaviourCallback = ZombieModeCrowdNavigation.ZombieCollisionBehaviour;"):
        if token not in apply_crowd:
            return fail("registration must reset crowd state and install the callback without overwriting a foreign one -> " + token)
    enemy_runtime = clean_source(ENEMY_RUNTIME.read_text(encoding="utf-8-sig"))
    register = method_body(enemy_runtime, "internal ZombieModeEnemyRuntimeMarker RegisterZombieModeEnemyRuntimeShell(")
    if compact("RegisterZombieModeEnemyInstanceId(enemy, marker); ApplyZombieModeCrowdMovement(enemy, marker);") not in compact(register):
        return fail("every registered zombie (normal, special, boss) must get the crowd collision behaviour")

    spawner = clean_source(SPAWNER.read_text(encoding="utf-8-sig"))
    spread = method_body(spawner, "private bool TryFindZombieModeSpreadSpawnPlacement(Vector3 center, out Vector3 resolved)")
    for token in ("ZombieModeTuning.SpawnSpreadMaxQueries", "zombieModeSpawnReachabilityPath",
                  "SpawnPositionHelper.PassesMinPlayerDistance(sampled, minPlayerDistance)",
                  "ZombieModeCrowdNavigation.IsBlockedByWall(center, sampled)"):
        if token not in spread:
            return fail("spawn spread must stay bounded, reachable, player-safe and wall-free -> " + token)

    safe_zone = clean_source(SAFE_ZONE.read_text(encoding="utf-8-sig"))
    if "TryGetZombieModeSpreadRecoveryPosition(out resolved)" not in safe_zone or "TryGetZombieModeReliableSpawnPosition(out resolved)" in safe_zone:
        return fail("safe-zone fallback ejection must use the spread recovery position (several zombies eject at once)")

    print("ZombieModeCrowdNavigationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
