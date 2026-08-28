#!/usr/bin/env python3
"""
PetNestLethalClampGuard — 遗种巢致死钳制链第四消费者守卫（实施计划 步骤 7）。

不变式：
- **零新增 Harmony 补丁**：只在既有 BossLethalHealthProtectionPatch 里加消费者，
  PetNest/ 目录下不得出现任何 [HarmonyPatch] / new Harmony；
- 第四消费者形态与前三个一致：`private static bool TryClampXxx(Health, ref float)`，
  接在链尾，命中钳血并返回 true；
- 先读静态 armed bool 快速早返，未带崽时热路径零分配；
- 钳制只钳血 + 登记退场：**不得**在 Hurt 调用栈里销毁角色、写存档或改场景状态；
- 退场由 PetNestDownedHandler 在宿主 tick 执行，并接进了运行时模块的 OnUpdate；
- 战痕落档有上限，溢出合并为旧伤计数，不无限增长；
- 战痕减益有叠加封顶。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import (  # noqa: E402
    PETNEST_DIR,
    read_petnest,
    read_text,
    repo_path,
    report,
    strip_cs_comments,
)

GUARD = "PetNestLethalClampGuard"
PATCH_FILE = os.path.join("Patches", "Combat", "BossLethalHealthProtectionPatch.cs")

# 在 Hurt 调用栈里绝不能做的事
FORBIDDEN_IN_CLAMP = [
    "Destroy(",
    "SetActive(",
    "Commit(",
    "StageCommit(",
    "SavesSystem",
    "CleanupOnce(",
]


def check_patch(errors):
    text = read_text(repo_path(PATCH_FILE))
    if text is None:
        errors.append("[File] 缺少 " + PATCH_FILE)
        return
    code = strip_cs_comments(text)

    # 零新增补丁：补丁属性数量不得增长（两个既有补丁类）
    if code.count("[HarmonyPatch(") != 2:
        errors.append("[零补丁] 该文件应恰好保留两个既有 [HarmonyPatch]，不得新增")

    # 第四消费者形态
    if not re.search(r"private static bool TryClampPetNestCompanion\(Health health, ref float value\)", code):
        errors.append("[消费者] 缺少第四消费者 TryClampPetNestCompanion(Health, ref float)")

    # 接在链尾，且前三个消费者仍在
    prefix = re.search(r"private static void Prefix\(Health __instance, ref float value\)[\s\S]{0,1200}?\n        \}", code)
    if prefix is None:
        errors.append("[链] 无法解析 CurrentHealth setter 的 Prefix")
    else:
        body = prefix.group(0)
        order = []
        for name in ["TryClampReverseScale", "TryClampDragonKing",
                     "TryClampDragonDescendant", "TryClampPetNestCompanion"]:
            pos = body.find(name)
            if pos < 0:
                errors.append("[链] Prefix 缺少消费者: " + name)
            order.append(pos)
        if all(p >= 0 for p in order) and order != sorted(order):
            errors.append("[链] 第四消费者必须接在既有三个之后，不得插队")

    # 静态 armed 早返
    clamp = re.search(r"private static bool TryClampPetNestCompanion\([\s\S]{0,900}?\n        \}", code)
    if clamp is None:
        errors.append("[消费者] 无法解析第四消费者函数体")
    else:
        body = clamp.group(0)
        if "IsPetNestCompanionClampArmed()" not in body:
            errors.append("[性能] 必须先读静态 armed bool 快速早返")
        if "PetNestCompanionAgent.IsCompanionHealth(health)" not in body:
            errors.append("[身份] 必须做 O(1) 引用身份比较")
        if "value = 1f;" not in body:
            errors.append("[重伤不死] 必须把致死血量钳到 1")
        if "PetNestDownedHandler.NotifyLethalClamped(" not in body:
            errors.append("[退场] 必须登记待处理退场")
        for forbidden in FORBIDDEN_IN_CLAMP:
            if forbidden in body:
                errors.append("[时序] Hurt 调用栈里不得执行: " + forbidden)

    armed = re.search(r"private static bool IsPetNestCompanionClampArmed\(\)[\s\S]{0,400}?\n        \}", code)
    if armed is not None and "catch (Exception)" not in armed.group(0):
        errors.append("[fail-closed] armed 查询必须 no-throw")

    # 不得为记录凶手而改动既有 Hurt Prefix 的签名
    # （ReverseScaleLethalProtectionGuard / ModeGPerformanceGuard 都盯着这行字面量）
    if "private static bool Prefix(Health __instance, ref bool __result, ref bool __state)" not in code:
        errors.append("[兼容] 不得改动 Health.Hurt Prefix 的既有签名")
    if "DamageInfo damageInfo" in code:
        errors.append("[兼容] 不得给 Hurt Prefix 加 DamageInfo 参数，凶手走 OnHurt 订阅")


def check_no_new_patch_in_petnest(errors):
    for name in sorted(os.listdir(PETNEST_DIR)):
        if not name.endswith(".cs"):
            continue
        text = read_text(os.path.join(PETNEST_DIR, name))
        if text is None:
            continue
        code = strip_cs_comments(text)
        for forbidden in ["[HarmonyPatch", "new Harmony(", "[HarmonyPrefix", "[HarmonyPostfix"]:
            if forbidden in code:
                errors.append("[零补丁] " + name + " 不得新增 Harmony 补丁: " + forbidden)


def check_handler(errors):
    text = read_petnest("PetNestDownedHandler.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestDownedHandler.cs")
        return
    code = strip_cs_comments(text)

    # 登记与执行分离
    notify = re.search(r"internal static void NotifyLethalClamped\(Health health\)[\s\S]{0,900}?\n        \}", code)
    if notify is None:
        errors.append("[登记] 缺少 NotifyLethalClamped(Health)")
    else:
        body = notify.group(0)
        if "health.SetInvincible(true)" not in body:
            errors.append("[短无敌] 钳血后必须同帧上短无敌，挡住连击")
        for forbidden in ["Destroy(", "CleanupOnce(", "SavesSystem"]:
            if forbidden in body:
                errors.append("[时序] 登记路径不得执行: " + forbidden)

    tick = re.search(r"internal static void Tick\(\)[\s\S]{0,1400}?\n        \}", code)
    if tick is None:
        errors.append("[执行] 缺少 Tick() 执行入口")
    else:
        body = tick.group(0)
        if "if (!_downedPending) return;" not in body:
            errors.append("[性能] Tick 无待办时必须 O(1) 早返")
        if "PetNestCompanionRuntime.NotifyDowned()" not in body:
            errors.append("[执行] Tick 必须走统一的退场清理入口")

    # 战痕上限与封顶
    if "PetNestTuning.MaxScarsPerPet" not in code:
        errors.append("[存档体积] 战痕必须有每崽上限")
    if "pet.mergedOldScarCount++" not in code:
        errors.append("[存档体积] 溢出的战痕必须合并为旧伤计数，不得静默丢弃")
    if "PetNestTuning.ScarModifierCapPercent" not in code:
        errors.append("[数值] 战痕减益必须有叠加封顶")
    if "PetNestTuning.ScarModifierPercent" not in code:
        errors.append("[数值] 战痕减益必须走常量")

    # 凶手记录：只在随从在场期间订阅官方 OnHurt（全场热路径，不得常驻订阅）
    if "Health.OnHurt += HandleAnyHurt;" not in code:
        errors.append("[战痕] 缺少 Health.OnHurt 订阅（凶手来源）")
    if "Health.OnHurt -= HandleAnyHurt;" not in code:
        errors.append("[事件纪律] Health.OnHurt 订阅必须成对退订")
    if "if (_hurtSubscribed) return;" not in code:
        errors.append("[事件纪律] OnHurt 订阅必须有防重复 bool")
    handler = re.search(
        r"private static void HandleAnyHurt\(Health health, DamageInfo damageInfo\)[\s\S]{0,700}?\n        \}",
        code)
    if handler is None:
        errors.append("[战痕] 缺少 OnHurt 回调 HandleAnyHurt")
    elif "if (!PetNestCompanionAgent.IsCompanionHealth(health)) return;" not in handler.group(0):
        errors.append("[性能] OnHurt 回调必须对非随从零分配早返")

    runtime = read_petnest("PetNestCompanionRuntime.cs")
    if runtime is not None:
        rcode = strip_cs_comments(runtime)
        if "PetNestDownedHandler.EnsureHurtSubscribed()" not in rcode:
            errors.append("[接线] 随从入场必须订阅 OnHurt")
        if "PetNestDownedHandler.ShutdownHurtSubscription()" not in rcode:
            errors.append("[接线] 随从离场必须退订 OnHurt")

    # 接进宿主 tick
    module = read_petnest("PetNestRuntimeModule.cs")
    if module is None:
        errors.append("[File] 缺少 PetNest/PetNestRuntimeModule.cs")
    elif "PetNestDownedHandler.Tick();" not in strip_cs_comments(module):
        errors.append("[接线] 运行时模块 OnUpdate 必须驱动 PetNestDownedHandler.Tick()")


def main():
    errors = []
    check_patch(errors)
    check_no_new_patch_in_petnest(errors)
    check_handler(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
