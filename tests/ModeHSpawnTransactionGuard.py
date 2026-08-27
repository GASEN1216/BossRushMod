#!/usr/bin/env python3
"""
ModeHSpawnTransactionGuard — Mode H 生成事务守卫（设计提案 §19.3、§26.1）。

不变式：
- 每帧最多创建一个角色；上限 3 敌军 + 1 选手（+1 既有玩家看台身体）；
- 创建**之前**登记 clone preset 到额外死亡掉落抑制表；
- 在 staging 点创建，返回后的第一个同步步骤登记 Health/Character 并 inactive + invincible；
- runtime clone 与角色同寿命，回收顺序为“角色引用 -> preset 引用 -> 销毁 clone”；
- clone 上固定 aiCombatFactor=1f、dropBoxOnDead=false、目标 team；
- 一律传 group=null、isLeader=false（避免 leader/成员双向同步 searchedEnemy 污染点火）；
- 失败整批逆序回收；
- Mode H 不调用也不修改 Utilities/EnemySpawnCore.cs。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
BRIDGE = os.path.join(MODEH_DIR, "ModeHSpawnBridge.cs")
TRANSACTION = os.path.join(MODEH_DIR, "ModeHSpawnTransaction.cs")
CONFIG = os.path.join(MODEH_DIR, "ModeHConfig.cs")


def main():
    errors = []

    bridge = read_text(BRIDGE)
    if bridge is None:
        print("ModeHSpawnTransactionGuard: FAIL (1 errors)")
        print("  - [File] 缺少 ModeH/ModeHSpawnBridge.cs")
        return 1
    code = strip_cs_comments(bridge)

    checks = [
        (r"UnityEngine\.Object\.Instantiate\(auditedPreset\)", "克隆已审计 preset"),
        (r"clone\.aiCombatFactor = 1f;", "clone 固定 aiCombatFactor=1f"),
        (r"clone\.dropBoxOnDead = false;", "clone 固定 dropBoxOnDead=false"),
        (r"clone\.team = team;", "clone 固定目标阵营"),
        (r"ModeHDeathSuppressionRegistry\.RegisterPreset\(clone\);", "创建前登记抑制表"),
        (r"CreateCharacterAsync\(\s*stagingPos, Vector3\.forward, relatedScene, null, false\)",
         "在 staging 创建且 group=null / isLeader=false"),
        (r"ModeHDeathSuppressionRegistry\.RegisterCharacter\(handle\.Health, character\);",
         "创建返回后补登记角色引用"),
        (r"handle\.Health\.SetInvincible\(true\);", "返回后立即无敌"),
        (r"character\.gameObject\.SetActive\(false\);", "返回后立即 inactive"),
        (r"ai\.forceTracePlayerDistance = 0f;", "清零强制追踪玩家"),
        (r"ai\.searchedEnemy = null;", "清空 searchedEnemy"),
        (r"ai\.noticed = false;", "清空 noticed"),
        (r"internal static void Recycle\(ModeHSpawnHandle handle\)", "统一回收入口"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Bridge] 不满足: " + desc)

    # 登记顺序：RegisterPreset 必须早于 CreateCharacterAsync
    register_pos = code.find("ModeHDeathSuppressionRegistry.RegisterPreset(clone)")
    create_pos = code.find("CreateCharacterAsync(")
    if register_pos < 0 or create_pos < 0 or register_pos > create_pos:
        errors.append("[Bridge] clone preset 必须在 CreateCharacterAsync 调用之前登记")

    # 回收顺序：角色引用 -> preset 引用 -> 销毁 clone
    recycle = re.search(r"internal static void Recycle\(ModeHSpawnHandle handle\)[\s\S]*?\n        \}", code)
    if recycle:
        body = recycle.group(0)
        char_pos = body.find("UnregisterCharacter")
        preset_pos = body.find("UnregisterPreset")
        destroy_pos = body.find("DestroyClone")
        if not (0 <= char_pos < preset_pos < destroy_pos):
            errors.append("[Bridge] 回收顺序必须是 角色引用 -> preset 引用 -> 销毁 clone")

    transaction = read_text(TRANSACTION)
    if transaction is None:
        errors.append("[File] 缺少 ModeH/ModeHSpawnTransaction.cs")
    else:
        tcode = strip_cs_comments(transaction)
        tchecks = [
            (r"ModeHConfig\.MaxConcurrentEnemyInstances", "引用敌军上限常量"),
            (r"ModeHConfig\.MaxConcurrentFighterInstances", "引用选手上限常量"),
            (r"public void RollbackAll\(\)", "整批回收入口"),
            (r"for \(int i = _fighterHandles\.Count - 1; i >= 0; i--\)", "逆序回收选手"),
            (r"for \(int i = _enemyHandles\.Count - 1; i >= 0; i--\)", "逆序回收敌军"),
            (r"public bool VerifyTeamsStableNextFrame\(", "下一帧阵营稳定核对"),
            (r"public void Cancel\(\)", "场景切换立即取消"),
            (r"yield return null;", "分帧生成"),
        ]
        for pattern, desc in tchecks:
            if not re.search(pattern, tcode):
                errors.append("[Transaction] 不满足: " + desc)

        # 每帧最多一个：批次循环里必须有等待
        batch = re.search(r"public IEnumerator SpawnBatch\([\s\S]*?\n        \}", tcode)
        if batch and batch.group(0).count("yield return null;") < 2:
            errors.append("[Transaction] 分帧生成必须在每次创建后让出一帧")

    # Mode H 不得调用或修改共享生成核心
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        c = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        for forbidden in ["EnemySpawnCore", "HoldForExternalCommit", "EnemySpawnCoreOptions"]:
            if forbidden in c:
                errors.append("[SpawnCore] {} 不得引用共享生成核心: {}".format(name, forbidden))

    config = read_text(CONFIG)
    if config:
        if not re.search(r"public const int MaxConcurrentEnemyInstances = 3;", config):
            errors.append("[Config] 敌军同时在场上限未冻结为 3")
        if not re.search(r"public const int MaxConcurrentFighterInstances = 1;", config):
            errors.append("[Config] 选手同时在场上限未冻结为 1")
        if not re.search(r"public const int MaxSpawnPerFrame = 1;", config):
            errors.append("[Config] 每帧生成上限未冻结为 1")

    if errors:
        print("ModeHSpawnTransactionGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHSpawnTransactionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
