"""TestNpcDevOnlySpawnGuard — 测试 NPC（testNpc，如小满）只在 Dev 构建里自动出现。

owner 2026-11-25：小满是 Dev 专用测试 NPC，只能经 F3 刷出。`testNpc` 原本只让她不进好感页签，
永久 NPC 刷新与配偶恢复都不看它，正式版玩家进基地就会刷出她。现在三处自动出现的路径都经
`PermanentDuckNpcRegistry.AllowsAutoSpawn`：

1. 判据本身：`blueprint != null && (!blueprint.testNpc || ModBehaviour.DevModeEnabled)`；
2. `PermanentDuckNpcModule.ShouldSpawnInScene` 与 `SpawnForSceneAsync` 的蓝图循环跳过不允许的蓝图；
3. `WeddingRuntimeModule.RequestPermanentSpouseRestore` 开头挡住（已婚也不例外，存档不删不改）；
4. `DuckNpcs.json` 里小满仍标着 `testNpc: true`（数据不改，门控靠代码）。

F3 的 `PermanentDuckNpcModule.ForceSpawnAtAsync` 不经这道门，Dev 构建里照常能刷。
"""
from pathlib import Path
import json
import re
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
REGISTRY = ROOT / "Integration" / "NPCs" / "DuckNpc" / "Permanent" / "PermanentDuckNpcRegistry.cs"
MODULE = ROOT / "Integration" / "NPCs" / "DuckNpc" / "Permanent" / "PermanentDuckNpcModule.cs"
BRIDGE = ROOT / "Integration" / "Wedding" / "WeddingModBehaviourBridge.cs"
DATA = ROOT / "Assets" / "Data" / "DuckNpcs.json"
GATE_CALL = "!PermanentDuckNpcRegistry.AllowsAutoSpawn(blueprint)"


def compact(text):
    return re.sub(r"\s+", "", text or "")


def body(source, marker):
    start = source.find(marker)
    if start < 0:
        return None
    start = source.index("{", start)
    depth = 0
    for end in range(start, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return source[start:end + 1]
    return None


def read(path):
    return clean_source(path.read_text(encoding="utf-8-sig"))


def main():
    errors = []

    registry = read(REGISTRY)
    gate = body(registry, "internal static bool AllowsAutoSpawn(DuckNpcBlueprint blueprint)")
    if gate is None:
        errors.append("[Registry] 缺少 PermanentDuckNpcRegistry.AllowsAutoSpawn")
    elif compact(gate) != "{returnblueprint!=null&&(!blueprint.testNpc||ModBehaviour.DevModeEnabled);}":
        errors.append("[Registry] AllowsAutoSpawn 判据被改了：应为 blueprint != null && (!blueprint.testNpc || ModBehaviour.DevModeEnabled)")

    module = read(MODULE)
    for marker, scene in (("public bool ShouldSpawnInScene(ModBehaviour mod, string sceneName)", "sceneName"),
                          ("private async UniTaskVoid SpawnForSceneAsync(PermanentSpawnRequest request)", "request.SceneName")):
        block = body(module, marker)
        if block is None:
            errors.append("[Module] 找不到 " + marker)
            continue
        want = compact("if (blueprint == null || !blueprint.AllowsScene(" + scene + ") || " + GATE_CALL + ")") + "{continue;}"
        if want not in compact(block):
            errors.append("[Module] %s 的蓝图循环必须跳过 AllowsAutoSpawn 为假的蓝图（testNpc 正式构建不自动刷）" % marker)

    bridge = read(BRIDGE)
    restore = body(bridge, "private void RequestPermanentSpouseRestore(string npcId, Vector3 position, bool following)")
    first = compact("DuckNpcBlueprint blueprint; if (PermanentDuckNpcRegistry.TryGetBlueprint(npcId, out blueprint) && "
                    + GATE_CALL + ") return;")
    if restore is None:
        errors.append("[Wedding] 找不到 RequestPermanentSpouseRestore")
    elif not compact(restore).startswith("{" + first):
        errors.append("[Wedding] RequestPermanentSpouseRestore 第一件事必须挡住不允许自动出现的测试 NPC（已婚也不例外）")

    rows = json.loads(DATA.read_text(encoding="utf-8-sig"))
    if isinstance(rows, dict):
        rows = rows.get("npcs") or rows.get("blueprints") or []
    xiaoman = [r for r in rows if isinstance(r, dict) and r.get("id") == "duck_npc_xiaoman"]
    if len(xiaoman) != 1:
        errors.append("[Data] DuckNpcs.json 里应恰好一条 duck_npc_xiaoman")
    elif xiaoman[0].get("testNpc") is not True:
        errors.append("[Data] duck_npc_xiaoman 必须保留 testNpc: true（正式构建不自动刷靠它）")

    if errors:
        print("TestNpcDevOnlySpawnGuard: FAIL")
        for e in errors:
            print("  - " + e)
        return 1
    print("TestNpcDevOnlySpawnGuard: PASS（判据、两处刷新循环、配偶恢复、小满数据标记）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
