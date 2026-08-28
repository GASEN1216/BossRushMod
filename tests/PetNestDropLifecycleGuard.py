#!/usr/bin/env python3
"""
PetNestDropLifecycleGuard — 遗种巢掉落生命周期守卫（实施计划 步骤 4）。

不变式：
- 掉落挂接点只有一处：LootAndRewards.RegisterBossRandomLootTracking 体内并联
  PetNestDropService.TryTrack；清理侧并联 ClearTracking，注册/退订成对；
- 零新增 Harmony 补丁：掉落服务不得出现 HarmonyPatch / new Harmony；
- per-character 事件订阅必须幂等（先退旧再挂新）且注册失败回滚（AGENTS.md 4.6）；
- 开关关闭 / 未 bootstrap 时不注册任何 handler（dormant）；
- 血脉不在目录里一律 fail-closed：既不掉蛋也不记遗魂；
- 遗魂公式镜像官方 SoulCollector 的 MaxHealth/除数 口径且有下限；
- 高频记账走 StageCommit（只入队不落盘），不得每次击杀都 SaveFile；
- 掉落服务不得直接触碰 PetNestPersistence（分层）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import (  # noqa: E402
    read_petnest,
    read_text,
    repo_path,
    report,
    strip_cs_comments,
)

GUARD = "PetNestDropLifecycleGuard"


def check_service(errors):
    text = read_petnest("PetNestDropService.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestDropService.cs")
        return
    code = strip_cs_comments(text)

    # 零新增补丁
    for forbidden in ["HarmonyPatch", "new Harmony(", "HarmonyPrefix", "HarmonyPostfix"]:
        if forbidden in code:
            errors.append("[零补丁] 掉落服务不得新增 Harmony 补丁: " + forbidden)

    # 注册 / 退订成对
    if not re.search(r"internal static void TryTrack\(ModBehaviour owner, CharacterMainControl character\)", code):
        errors.append("[成对] 缺少 TryTrack(ModBehaviour, CharacterMainControl)")
    if not re.search(r"internal static void ClearTracking\(CharacterMainControl character\)", code):
        errors.append("[成对] 缺少 ClearTracking(CharacterMainControl)")
    if "BeforeCharacterSpawnLootOnDead +=" not in code:
        errors.append("[成对] 缺少 per-character 事件订阅")
    if "BeforeCharacterSpawnLootOnDead -=" not in code:
        errors.append("[成对] 缺少 per-character 事件退订")

    # 幂等：先退旧再挂新
    track = re.search(r"internal static void TryTrack\([\s\S]{0,2000}?\n        \}", code)
    if track is None:
        errors.append("[幂等] 无法解析 TryTrack 函数体")
    else:
        body = track.group(0)
        if "ClearTracking(character);" not in body:
            errors.append("[幂等] TryTrack 必须先退订旧 handler 再挂新的")
        if "if (!IsEnabled(owner)) return;" not in body:
            errors.append("[dormant] 开关关闭时不得注册任何 handler")
        if "PetNestLineageCatalog.IsKnownLineage(lineageKey)" not in body:
            errors.append("[fail-closed] 血脉不在目录里必须直接返回")
        if "_hooks.Remove(character);" not in body:
            errors.append("[回滚] 注册失败必须回滚追踪状态")

    # dormant 判据要求 bootstrap 完成
    if "runtime.IsEnabled && runtime.IsBootstrapped" not in code:
        errors.append("[dormant] 必须同时要求开关开启与 bootstrap 完成")

    # 遗魂公式
    if "PetNestTuning.SoulDropHealthDivisor" not in code:
        errors.append("[遗魂] 计量必须走 PetNestTuning.SoulDropHealthDivisor 常量")
    if "PetNestTuning.MinSoulDropPerKill" not in code:
        errors.append("[遗魂] 计量必须有下限常量")
    if "health.MaxHealth" not in code:
        errors.append("[遗魂] 计量必须镜像官方 MaxHealth 口径")

    # 高频记账只入队
    if "PetNestService.AddSouls(lineageKey, souls, false)" not in code:
        errors.append("[性能] 遗魂记账必须走 commit=false 的入队路径")
    if "PetNestService.StageCommit()" not in code:
        errors.append("[性能] 遗魂记账后必须 StageCommit（只入队不落盘）")
    if "PetNestSaveCoordinator" in code:
        errors.append("[性能] 掉落热路径不得直接请求落盘")

    # 分层
    if "PetNestPersistence." in code:
        errors.append("[分层] 掉落服务不得直接访问 PetNestPersistence，必须走 Service")

    # 蛋掉落走概率常量
    if "PetNestTuning.EggDropChance" not in code:
        errors.append("[掉落] 蛋掉落概率必须走 PetNestTuning.EggDropChance 常量")


def check_hook_site(errors):
    text = read_text(repo_path("LootAndRewards", "LootAndRewards.cs"))
    if text is None:
        errors.append("[File] 缺少 LootAndRewards/LootAndRewards.cs")
        return
    code = strip_cs_comments(text)

    if code.count("PetNestDropService.TryTrack(this, character);") != 1:
        errors.append("[挂接] RegisterBossRandomLootTracking 必须且只能并联一次 TryTrack")
    if code.count("PetNestDropService.ClearTracking(character);") != 1:
        errors.append("[挂接] ClearBossRandomLootTracking 必须且只能并联一次 ClearTracking")

    # 挂接必须在正确的函数体内
    register = re.search(r"private void RegisterBossRandomLootTracking\([\s\S]{0,3000}?\n        \}", code)
    if register is None or "PetNestDropService.TryTrack(this, character);" not in register.group(0):
        errors.append("[挂接] TryTrack 必须在 RegisterBossRandomLootTracking 体内")
    clear = re.search(r"private void ClearBossRandomLootTracking\([\s\S]{0,3000}?\n        \}", code)
    if clear is None or "PetNestDropService.ClearTracking(character);" not in clear.group(0):
        errors.append("[挂接] ClearTracking 必须在 ClearBossRandomLootTracking 体内")


def main():
    errors = []
    check_service(errors)
    check_hook_site(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
