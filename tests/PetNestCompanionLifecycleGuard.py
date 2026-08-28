#!/usr/bin/env python3
"""
PetNestCompanionLifecycleGuard — 遗种巢随从局内生命周期守卫（实施计划 步骤 6）。

不变式：
- 入场/退场/清理成对且幂等：所有路径（切图、重伤、关开关、宿主销毁、静态复位）
  都走同一个 CleanupOnce 入口；
- **零常驻**：出战席位为空时连门控都不查、不分配；局内至多一次 CreateCharacterAsync；
- 异步生成必须在 await 之后重验 owner / scene generation / 玩家引用；
- 半成品 handle 必须在 finally 里回收，不得留在场上；
- 借席与容量 Modifier 与随从同寿命：入场挂、离场摘；
- 容量 Modifier 挂**玩家**的 CharacterItem（官方 PetProxy 读的是玩家 stat），
  且必须用 ModifierType.Add（PetCapcity 是格子数，不是百分比）；
- 官方 stat key 拼写必须是 PetCapcity（少一个 a），改成 PetCapacity 会静默失效；
- 清场豁免与敌对性安全网都认得随从（AGENTS.md 4.5 安全网不得误伤玩家方随从）；
- 阵营取主人实际阵营而不是硬编码 Teams.player（Mode E 会换玩家阵营）；
- 幼体必须经既有净化入口去掉自爆技能与特殊挂件。
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

GUARD = "PetNestCompanionLifecycleGuard"


def check_runtime(errors):
    text = read_petnest("PetNestCompanionRuntime.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestCompanionRuntime.cs")
        return
    code = strip_cs_comments(text)

    # 零常驻：席位为空先返回
    spawn = re.search(r"internal static void TrySpawnForScene\(ModBehaviour owner, int sceneGeneration\)[\s\S]{0,1800}?\n        \}", code)
    if spawn is None:
        errors.append("[入场] 缺少 TrySpawnForScene(ModBehaviour, int)")
    else:
        body = spawn.group(0)
        gate_pos = body.find("PetNestModeGate.IsCompanionAllowed(")
        slot_pos = body.find("PetNestService.DeployedPet")
        if slot_pos < 0:
            errors.append("[零常驻] 必须先看出战席位")
        elif gate_pos >= 0 and slot_pos > gate_pos:
            errors.append("[零常驻] 出战席位为空时不应再查模式门控")
        if "if (HasCompanion) return;" not in body:
            errors.append("[幂等] 已有随从在场时不得重复生成")
        if "if (_spawnInFlight) return;" not in body:
            errors.append("[幂等] 生成进行中不得重入")
        if 'ReasonQueryFailed' not in body:
            errors.append("[诊断] 未能带崽必须记录原因")

    # await 之后重验
    if "IsRequestStillValid(owner, player, sceneGeneration)" not in code:
        errors.append("[异步] await 之后必须重验 owner / scene generation / 玩家引用")
    valid = re.search(r"private static bool IsRequestStillValid\([\s\S]{0,900}?\n        \}", code)
    if valid is not None:
        body = valid.group(0)
        if "sceneGeneration != _sceneGeneration" not in body:
            errors.append("[异步] 重验必须比对 scene generation")
        if "current != player" not in body:
            errors.append("[异步] 重验必须比对玩家引用")

    # 半成品 handle 回收
    if not re.search(r"finally\s*\{[\s\S]{0,400}?PetNestCompanionSpawner\.CleanupOnce\(handle\);", code):
        errors.append("[回收] 生成失败路径必须在 finally 里回收半成品 handle")

    # 清理入口唯一且幂等
    cleanup = re.search(r"internal static void CleanupOnce\(\)[\s\S]{0,1600}?\n        \}", code)
    if cleanup is None:
        errors.append("[清理] 缺少统一 CleanupOnce() 入口")
    else:
        body = cleanup.group(0)
        for token, desc in [
            ("PetNestPetProxyBridge.ReleaseSeat()", "还席"),
            ("PetNestPetProxyBridge.RemoveCapacityBonus(", "摘容量 Modifier"),
            ("PetNestCompanionSpawner.CleanupOnce(_handle)", "回收随从"),
        ]:
            if token not in body:
                errors.append("[清理] CleanupOnce 缺少: " + desc)
        if "_handle = null;" not in body or "_deployedPetId = null;" not in body:
            errors.append("[清理] CleanupOnce 必须在 finally 里清空引用")

    # 借席与容量同寿命
    if "PetNestPetProxyBridge.TryBorrowSeat(" not in code:
        errors.append("[捡漏背包] 入场必须借席")
    if "PetNestPetProxyBridge.ApplyCapacityBonus(" not in code:
        errors.append("[捡漏背包] 入场必须挂容量 Modifier")

    # 入场重试窗口：绝大多数地图在 sceneLoaded 这一刻还没有任何 run 标志为真
    # （带 customSpawnPos 的竞技场要等协程、Mode D 有 0.5s 延迟、IsActive 要等开波），
    # 只采样一次的话随从在多数入场路径上永远进不了场
    if "internal static void TickSpawnRetry(" not in code:
        errors.append("[入场] 缺少入场重试入口 TickSpawnRetry（只采样一次会永远进不了场）")
    if "OpenSpawnRetryWindow()" not in code:
        errors.append("[入场] 切图必须开一个入场重试窗口")
    if "SpawnRetryWindowSeconds" not in code:
        errors.append("[入场] 重试窗口必须有时长上限")
    module = read_petnest("PetNestRuntimeModule.cs")
    if module is not None and "PetNestCompanionRuntime.TickSpawnRetry(" not in strip_cs_comments(module):
        errors.append("[入场] 宿主 tick 必须驱动入场重试")


def check_bridge(errors):
    text = read_petnest("PetNestPetProxyBridge.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestPetProxyBridge.cs")
        return
    code = strip_cs_comments(text)

    if 'internal const string PetCapacityStatKey = "PetCapcity";' not in code:
        errors.append("[stat key] 官方拼写是 PetCapcity（少一个 a），不得写成 PetCapacity")
    if "player.CharacterItem" not in code:
        errors.append("[容量] Modifier 必须挂玩家的 CharacterItem（官方 PetProxy 读的是玩家 stat）")
    if "new Modifier(ModifierType.Add, extraSlots, CapacityModifierSource)" not in code:
        errors.append("[容量] PetCapcity 是格子数，必须 ModifierType.Add 常量加")
    if "RemoveAllModifiersFrom(CapacityModifierSource)" not in code:
        errors.append("[容量] 必须走 source-tagged 整组摘除")
    apply_fn = re.search(r"internal static bool ApplyCapacityBonus\([\s\S]{0,1200}?\n        \}", code)
    if apply_fn is not None and "RemoveAllModifiersFrom(CapacityModifierSource);" not in apply_fn.group(0):
        errors.append("[容量] 挂之前必须先摘干净，避免切图重挂叠加")
    if not re.search(r"internal static void RemoveCapacityBonus\(CharacterMainControl player\)", code):
        errors.append("[容量] 缺少摘除入口 RemoveCapacityBonus")


def check_spawner(errors):
    text = read_petnest("PetNestCompanionSpawner.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestCompanionSpawner.cs")
        return
    code = strip_cs_comments(text)

    # 阵营取主人实际阵营
    if "Teams companionTeam = master != null ? master.Team : Teams.player;" not in code:
        errors.append("[阵营] 激活时必须取主人实际阵营（Mode E 会换玩家阵营）")
    if "handle.Character.SetTeam(companionTeam);" not in code:
        errors.append("[阵营] SetTeam 必须使用主人阵营变量")

    # 净化入口
    if 'owner.SanitizeBossRushZombieSpawn(handle.Character, "PetNestCompanion")' not in code:
        errors.append("[净化] 幼体必须经既有净化入口去掉自爆技能与特殊挂件")

    # 伤害归一
    if "NormalizeCombatOutput(" not in code:
        errors.append("[伤害归一] 缺少伤害归一入口")
    if "PetNestTuning.CompanionDpsShareTarget" not in code:
        errors.append("[伤害归一] 必须走 PetNestTuning.CompanionDpsShareTarget 常量")

    # 天赋与战痕必须真的挂上去，否则它们只是面板上的展示文本：
    # 两只天赋完全不同的崽进局后属性一模一样，养成与战痕惩罚等于不存在
    if "internal static void ApplyPetModifiers(" not in code:
        errors.append("[养成] 缺少 ApplyPetModifiers：天赋与战痕必须真的挂成 Modifier")
    if "ApplyPetModifiers(handle.Character, pet)" not in code:
        errors.append("[养成] TryActivate 必须应用崽的天赋与战痕")
    if "PetNestPetProxyBridge.PetCapacityStatKey" not in code:
        errors.append("[养成] PetCapcity 必须跳过（官方读的是玩家 stat，挂幼体无效）")
    if "PetNestTuning.ScarModifierCapPercent" not in code:
        errors.append("[养成] 战痕减益必须按封顶钳制后再挂")


def check_exemptions(errors):
    maintenance = read_text(repo_path("WavesArena", "WavesArenaEnemyMaintenance.cs"))
    if maintenance is None:
        errors.append("[File] 缺少 WavesArena/WavesArenaEnemyMaintenance.cs")
    else:
        mcode = strip_cs_comments(maintenance)
        if mcode.count("PetNestCompanionAgent.IsCompanionCharacter(c)") < 2:
            errors.append("[豁免] 两处清场入口都必须豁免遗种巢随从")
        if mcode.count("c.GetComponent<PetNestCompanionAgent>() != null") < 2:
            errors.append("[豁免] isPet 判定必须同时认 PetAI 与 PetNestCompanionAgent")
        # 既有 guard 依赖的字面量必须保留
        if mcode.count("bool isPet = false;") < 2:
            errors.append("[兼容] 不得删除既有的 isPet 字面量（DeathWraith guard 依赖它的顺序）")

    host = read_text(repo_path("ModBehaviour.cs"))
    if host is None:
        errors.append("[File] 缺少 ModBehaviour.cs")
    else:
        hcode = strip_cs_comments(host)
        if "PetNestCompanionAgent.IsCompanionCharacter(character)" not in hcode:
            errors.append("[安全网] 敌对性安全网必须豁免玩家方随从（AGENTS.md 4.5）")
        if "PetNestCompanionRuntime.ResetStaticCaches()" not in hcode:
            errors.append("[清理] 宿主销毁必须复位随从运行时")


def main():
    errors = []
    check_runtime(errors)
    check_bridge(errors)
    check_spawner(errors)
    check_exemptions(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
