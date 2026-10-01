"""2026-10-01 发布前审查 P1 的结构锁：捏脸 NPC 头顶名字的生命周期、可婚永久 NPC 的移动组件、天空岛离岛记录的去留。

1. 捏脸 NPC（永久 NPC 与天空岛居民）登记过原版名字组件后，名牌要每帧跟着角色走、停用时收起、销毁时注销
   （口径同快递员 / 护士）。旧代码只登记一次：名牌停在生成点，隐藏后不收，角色没了登记还在。
2. CR-2026-09-30-004：永久 NPC 都能结婚，婚后开跟随靠 DuckNpcMovement 走路；按人设站定（canWander=false，
   折翎、无声钟守）的以前直接 return 不挂组件，婚后跟随原地不动。现在一律挂，站定的只是平时不溜达。
3. CR-2026-09-30-002：回主菜单也会卸载岛场景，「这一趟暂不入档的永久记录」只在撤离或倒下时保留，去向是主菜单的不留。
   CR-2026-09-30-003：倒下时与撤离同口径，在官方死亡存档之前结算（只认 OnPlayerDied 方法体）。

反向验证：去掉 LateUpdate 刷新 / OnDestroy 注销、恢复 canWander 早退、删掉站定判断、把去留改回只看 raid_unloaded，各自转红（main 里内存探针，每次运行都跑）。
"""
import sys
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source

MARKER = "Integration/NPCs/DuckNpc/DuckNpcRuntimeMarker.cs"
MOVEMENT = "Integration/NPCs/DuckNpc/DuckNpcMovement.cs"
MODULE = "Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs"
DEPARTURE = "SkyIsland/SkyIslandSessionDeparture.cs"
SESSION = "SkyIsland/SkyIslandSession.cs"


def body(source, signature):
    start = source.find(signature)
    if start < 0:
        return None
    opening = source.find("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening:index + 1]
    return None


def check(src):
    errors = []
    marker = src[MARKER]
    late = body(marker, "private void LateUpdate()")
    if late is None or "NPCNameTagHelper.RefreshOriginalHealthBarName(transform);" not in late:
        errors.append("DuckNpcRuntimeMarker 必须在 LateUpdate 里刷新原版名字组件（名牌跟着角色走）")
    disable = body(marker, "private void OnDisable()")
    if disable is None or "NPCNameTagHelper.RefreshOriginalHealthBarName(transform);" not in disable:
        errors.append("DuckNpcRuntimeMarker 停用时必须刷新一次，让名牌按 activeInHierarchy 收起")
    destroy = body(marker, "private void OnDestroy()")
    if destroy is None or "NPCNameTagHelper.UnregisterOriginalHealthBarName(transform);" not in destroy:
        errors.append("DuckNpcRuntimeMarker 销毁时必须注销名字登记")

    module = src[MODULE]
    attach = body(module, "internal static void AttachPermanentParts(")
    if attach is None:
        errors.append("找不到 AttachPermanentParts")
    else:
        if "if (!blueprint.canWander)" in attach:
            errors.append("永久 NPC 不许因为 canWander=false 就不挂移动组件（婚后跟随要靠它走路）")
        add = attach.find("AddComponent<DuckNpcMovement>()")
        stay = attach.find("movement.StayHomeUnlessFollowing = !blueprint.canWander;")
        bind = attach.find("movement.Bind(npc, home, blueprint.wanderRadius);")
        if not (0 <= add < stay < bind):
            errors.append("挂移动组件后要先按 canWander 设站定、再 Bind")

    movement = src[MOVEMENT]
    wander = body(movement, "private void UpdateWander()")
    if wander is None:
        errors.append("找不到 DuckNpcMovement.UpdateWander")
    else:
        follow = wander.find("if (_followTarget != null)")
        stay = wander.find("if (StayHomeUnlessFollowing)")
        roam = wander.find("MoveToRandomPointNearHome();")
        if not (0 <= follow < stay < roam):
            errors.append("站定判断必须排在跟随之后、漫步之前：跟随照走，平时不溜达")

    departure = body(src[DEPARTURE], "private bool KeepsRaidHeldRecords(string reason)")
    if departure is None:
        errors.append("找不到 KeepsRaidHeldRecords")
    else:
        for token in ('reason != "raid_unloaded"', "returnRequested || deathPending", "!IsMainMenuScene(departureScene)"):
            if token not in departure:
                errors.append("离岛记录去留缺判据：" + token)
    session = src[SESSION]
    died = body(session, "private void OnPlayerDied(DamageInfo damage)")
    if died is None or "if (story != null) { if (moved) story.SettleRaidHeld(true); story.Tick(true); }" not in died:
        errors.append("CR-2026-09-30-003：倒下时要在官方死亡存档之前把这一趟的永久记录放进待写批次（同撤离口径）")
    loading = body(session, "private void OnStartedLoading(SceneLoadingContext context)")
    if loading is None or "departureScene = context.sceneName;" not in loading:
        errors.append("OnStartedLoading 必须记下官方要加载的离岛去向")
    return errors


def load():
    return {rel: clean_source((ROOT / rel).read_text(encoding="utf-8-sig")).replace("\r\n", "\n")
            for rel in (MARKER, MOVEMENT, MODULE, DEPARTURE, SESSION)}


def main():
    src = load()
    errors = check(src)
    probes = [
        (MARKER, "        private void LateUpdate()\n        {\n            NPCNameTagHelper.RefreshOriginalHealthBarName(transform);\n", "        private void LateUpdate()\n        {\n"),
        (MARKER, "NPCNameTagHelper.UnregisterOriginalHealthBarName(transform);", ""),
        (MODULE, "movement.StayHomeUnlessFollowing = !blueprint.canWander;", "if (!blueprint.canWander) { return; }"),
        (MOVEMENT, "if (StayHomeUnlessFollowing)", "if (false)"),
        (DEPARTURE, "!IsMainMenuScene(departureScene)", "true"),
        (SESSION, "departureScene = context.sceneName;", ""),
        (SESSION, "            if (story != null) { if (moved) story.SettleRaidHeld(true); story.Tick(true); }\n        }\n        private void DestroyEnemy()",
         "            if (story != null) story.Tick(true);\n        }\n        private void DestroyEnemy()"),
    ]
    for rel, original, mutated in probes:
        if src[rel].count(original) < 1:
            errors.append("反向探针锚点缺失：" + rel + " " + original[:40])
            continue
        probe = dict(src)
        probe[rel] = src[rel].replace(original, mutated, 1)
        if not check(probe):
            errors.append("反向探针没有转红：" + rel + " " + original[:40])
    if errors:
        print("ReleaseReviewNpcAndDepartureGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("ReleaseReviewNpcAndDepartureGuard: PASS (%d reverse probes)" % len(probes))
    return 0


if __name__ == "__main__":
    sys.exit(main())
