#!/usr/bin/env python3
"""
PetNestMuseumStatsGuard — 遗种博物馆统计与基地闲逛崽守卫（实施计划 步骤 12）。

不变式：
- **按角色实例去重**：同一只 Boss 的死亡可能被多条路径看到（掉落 handler /
  死亡补丁 / 波次统计），不去重会把一次击杀记成三次。去重用
  HashSet<CharacterMainControl> 引用集合（照 AchievementTriggers 先例）；
- 去重集合必须在切图时清空，否则跨局累积死引用；
- 高频写只入队（StageMuseum），不逐次落盘；
- 首次孵化解锁该血脉图鉴页；
- 基地闲逛崽：**只在基地场景**、上限 MaxBaseIdleCompanions、**分帧生成**、
  离开基地全清；不借席、不挂容量 Modifier。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import read_petnest, report, strip_cs_comments  # noqa: E402

GUARD = "PetNestMuseumStatsGuard"


def check_stats(errors):
    text = read_petnest("PetNestMuseumStats.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestMuseumStats.cs")
        return
    code = strip_cs_comments(text)

    # 实例去重
    if "HashSet<CharacterMainControl> _countedBossKills" not in code:
        errors.append("[去重] 击杀统计必须用 HashSet<CharacterMainControl> 引用集合去重")
    kill = re.search(r"internal static void RecordKill\(CharacterMainControl boss, string lineageKey\)[\s\S]{0,900}?\n        \}", code)
    if kill is None:
        errors.append("[统计] 缺少 RecordKill(CharacterMainControl, string)")
    else:
        body = kill.group(0)
        if "_countedBossKills.Contains(boss)" not in body:
            errors.append("[去重] RecordKill 必须先查去重集合")
        if "_countedBossKills.Add(boss)" not in body:
            errors.append("[去重] RecordKill 必须登记已计角色")
        if "PetNestPersistenceAccess.StageMuseum()" not in body:
            errors.append("[性能] 高频统计只能入队，不得逐次落盘")

    if "internal static void ClearCountedKills()" not in code:
        errors.append("[去重] 缺少 ClearCountedKills() 跨局清理入口")

    # 四类统计齐全
    for fn in ["RecordKill", "RecordHatch", "RecordExpedition", "RecordLevel"]:
        if fn not in code:
            errors.append("[统计] 缺少入口: " + fn)

    # 首次孵化解锁
    hatch = re.search(r"internal static void RecordHatch\(PetNestPetRecord pet\)[\s\S]{0,900}?\n        \}", code)
    if hatch is not None and "stats.unlocked = true;" not in hatch.group(0):
        errors.append("[图鉴] 首次孵化必须解锁该血脉图鉴页")

    # 不得逐次落盘
    if "PetNestSaveCoordinator" in code:
        errors.append("[性能] 统计层不得直接请求落盘")

    # 接线：切图清去重集合
    module = read_petnest("PetNestRuntimeModule.cs")
    if module is None:
        errors.append("[File] 缺少 PetNest/PetNestRuntimeModule.cs")
    elif "PetNestMuseumStats.ClearCountedKills();" not in strip_cs_comments(module):
        errors.append("[去重] 切图必须清空跨局去重集合")

    drop = read_petnest("PetNestDropService.cs")
    if drop is not None and "PetNestMuseumStats.RecordKill(boss, lineageKey);" not in strip_cs_comments(drop):
        errors.append("[接线] 掉落路径必须记一次血脉击杀")

    hatch_service = read_petnest("PetNestHatchService.cs")
    if hatch_service is not None:
        hcode = strip_cs_comments(hatch_service)
        if hcode.count("PetNestMuseumStats.RecordHatch(pet);") < 2:
            errors.append("[接线] 孵化与凝蛋两条路径都必须记孵化")

    expedition = read_petnest("PetNestExpeditionService.cs")
    if expedition is not None and "PetNestMuseumStats.RecordExpedition(pet);" not in strip_cs_comments(expedition):
        errors.append("[接线] 远征出发必须记一次远征")


def check_idle_spawner(errors):
    text = read_petnest("PetNestBaseIdleSpawner.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestBaseIdleSpawner.cs")
        return
    code = strip_cs_comments(text)

    # 只在基地
    refresh = re.search(r"internal static void RefreshForScene\(ModBehaviour owner, int sceneGeneration, bool isBaseScene\)[\s\S]{0,900}?\n        \}", code)
    if refresh is None:
        errors.append("[门控] 缺少 RefreshForScene(ModBehaviour, int, bool)")
    else:
        body = refresh.group(0)
        if "if (!isBaseScene)" not in body:
            errors.append("[门控] 非基地场景必须直接清空并返回")
        if "CleanupAll();" not in body:
            errors.append("[门控] 非基地场景必须清空闲逛崽")

    # 上限与分帧
    if "PetNestTuning.MaxBaseIdleCompanions" not in code:
        errors.append("[上限] 闲逛崽数量必须走 MaxBaseIdleCompanions 常量")
    if "PetNestTuning.BaseIdleSpawnIntervalSeconds" not in code:
        errors.append("[分帧] 生成必须按间隔分帧，不得同帧连开多次创建")
    if "await UniTask.Delay(" not in code:
        errors.append("[分帧] 缺少分帧等待")

    # await 后重验
    if "if (sceneGeneration != _sceneGeneration)" not in code:
        errors.append("[异步] await 之后必须重验场景代数")

    # 闲逛崽不碰捡漏背包链路
    for forbidden in ["TryBorrowSeat", "ApplyCapacityBonus", "PetNestPetProxyBridge"]:
        if forbidden in code:
            errors.append("[边界] 闲逛崽不得借席或挂容量 Modifier: " + forbidden)

    if "internal static void CleanupAll()" not in code:
        errors.append("[清理] 缺少 CleanupAll()")

    module = read_petnest("PetNestRuntimeModule.cs")
    if module is not None:
        mcode = strip_cs_comments(module)
        if "PetNestBaseIdleSpawner.RefreshForScene(_owner, _sceneGeneration, true)" not in mcode:
            errors.append("[接线] 回基地必须铺闲逛崽")
        if "PetNestBaseIdleSpawner.RefreshForScene(_owner, _sceneGeneration, false)" not in mcode:
            errors.append("[接线] 离开基地必须清空闲逛崽")


def main():
    errors = []
    check_stats(errors)
    check_idle_spawner(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
