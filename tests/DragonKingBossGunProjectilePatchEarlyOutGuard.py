"""
Guard: 龙皇铳 Projectile.UpdateMoveAndCheck 补丁对非龙皇铳子弹必须先便宜早返。

官方 Projectile.Update 每帧调 UpdateMoveAndCheck，补丁前缀对全场每颗子弹（官方、其它 Mod）每帧都会进来。
以前前缀第一句就是 GetComponent<DragonKingBossGunProjectileAgent>()（2026-10-01 发布前审查 P3）。

不变式：
1. 前缀在 GetComponent 之前先判 `DragonKingBossGunProjectileAgent.HasAnyActiveRuntimeAgent`，为假时
   `__state = null; return true;` 原样放行。
2. 计数只在 Agent.Initialize（紧跟 projectile / profile 赋值之后）与 OnDisable（清空 projectile / profile 之后）
   两处随 IsActiveForRuntime 同步，且 ClearStaticCaches 不清零计数（清零会让仍在飞的子弹被漏判）。
"""

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source

PATCH_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs")
AGENT_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")


def fail(message: str) -> int:
    print("DragonKingBossGunProjectilePatchEarlyOutGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str, start_at: int = 0) -> str | None:
    start = text.find(signature, start_at)
    if start < 0:
        return None
    brace = text.find("{", start)
    if brace < 0:
        return None
    depth = 0
    for idx in range(brace, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace : idx + 1]
    return None


def norm(text: str) -> str:
    return " ".join(text.split())


def main() -> int:
    for path in (PATCH_SOURCE, AGENT_SOURCE):
        if not path.exists():
            return fail(f"missing {path}")
    patch_text = clean_source(PATCH_SOURCE.read_text(encoding="utf-8-sig"))
    agent_text = clean_source(AGENT_SOURCE.read_text(encoding="utf-8-sig"))

    patch_class_at = patch_text.find('[HarmonyPatch(typeof(Projectile), "UpdateMoveAndCheck")]')
    if patch_class_at < 0:
        return fail("missing Projectile.UpdateMoveAndCheck patch")
    prefix = extract_block(
        patch_text,
        "private static bool Prefix(Projectile __instance, out DragonKingBossGunProjectileAgent __state)",
        patch_class_at,
    )
    if prefix is None:
        return fail("missing UpdateMoveAndCheck Prefix body")
    prefix_n = norm(prefix)
    early_out = "if (!DragonKingBossGunProjectileAgent.HasAnyActiveRuntimeAgent) { __state = null; return true; }"
    lookup = "GetComponent<DragonKingBossGunProjectileAgent>()"
    if early_out not in prefix_n:
        return fail("Prefix lacks cheap early-out on HasAnyActiveRuntimeAgent")
    if lookup not in prefix_n:
        return fail("Prefix lacks the agent lookup after the early-out")
    if prefix_n.find(early_out) > prefix_n.find(lookup):
        return fail("Prefix must early-out before GetComponent<DragonKingBossGunProjectileAgent>()")
    if not prefix_n.startswith("{ " + early_out):
        return fail("early-out must be the first statement of the Prefix")

    agent_n = norm(agent_text)
    for required in (
        "private static int activeRuntimeAgentCount;",
        "internal static bool HasAnyActiveRuntimeAgent { get { return activeRuntimeAgentCount > 0; } }",
    ):
        if required not in agent_n:
            return fail(f"agent lacks -> {required}")

    sync = extract_block(agent_text, "private void SyncActiveRuntimeAgentCount()")
    if sync is None:
        return fail("missing SyncActiveRuntimeAgentCount body")
    sync_n = norm(sync)
    for required in (
        "bool active = IsActiveForRuntime;",
        "if (active == countedAsActiveRuntime) { return; }",
        "activeRuntimeAgentCount += active ? 1 : -1;",
    ):
        if required not in sync_n:
            return fail(f"SyncActiveRuntimeAgentCount lacks -> {required}")

    init = extract_block(agent_text, "public void Initialize(Projectile projectileInstance")
    if init is None:
        return fail("missing Agent.Initialize body")
    if "projectile = projectileInstance; sourceGun = gunAgent; profile = shotProfile; SyncActiveRuntimeAgentCount();" not in norm(init):
        return fail("Initialize must sync the counter right after assigning projectile / profile")

    disable = extract_block(agent_text, "private void OnDisable()")
    if disable is None:
        return fail("missing Agent.OnDisable body")
    if "projectile = null; sourceGun = null; profile = null; SyncActiveRuntimeAgentCount();" not in norm(disable):
        return fail("OnDisable must sync the counter right after clearing projectile / profile")

    clear = extract_block(agent_text, "internal static void ClearStaticCaches()")
    if clear is None:
        return fail("missing ClearStaticCaches body")
    if "activeRuntimeAgentCount" in clear:
        return fail("ClearStaticCaches must not reset activeRuntimeAgentCount")

    writes = agent_n.count("activeRuntimeAgentCount =") + agent_n.count("activeRuntimeAgentCount +=")
    if writes != 2:
        return fail(f"activeRuntimeAgentCount must only be written inside SyncActiveRuntimeAgentCount (found {writes} writes)")
    if agent_n.count("SyncActiveRuntimeAgentCount();") != 2:
        return fail("SyncActiveRuntimeAgentCount must be called exactly from Initialize and OnDisable")

    print("DragonKingBossGunProjectilePatchEarlyOutGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
