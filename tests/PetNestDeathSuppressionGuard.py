#!/usr/bin/env python3
"""
PetNestDeathSuppressionGuard — 遗种巢死亡抑制链第三消费者守卫（实施计划 步骤 7）。

不变式：
- 零新增补丁：只在既有 CharacterOnDeadPatch 里并联第三个 registry；
- 三个 registry 的 armed 快路径与身份查询都接上了，顺序不影响语义但必须齐全；
- 命中时**只**跳过本 Mod 的两个额外掉落 handler，不得 return false、
  不得跳过或改写原版 OnDead 与 Health.OnDead；
- 查询异常 fail-open=false（让原 handler 继续），绝不打断宿主 OnDead；
- registry 是薄封装：身份来源复用随从组件的静态表，不维护第二份簿记
  （两份簿记必然失步）。
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

GUARD = "PetNestDeathSuppressionGuard"
PATCH_FILE = os.path.join("Patches", "Combat", "CharacterOnDeadPatch.cs")


def check_patch(errors):
    text = read_text(repo_path(PATCH_FILE))
    if text is None:
        errors.append("[File] 缺少 " + PATCH_FILE)
        return
    code = strip_cs_comments(text)

    # 零新增补丁
    if code.count("[HarmonyPatch(") != 1:
        errors.append("[零补丁] 该文件应恰好保留一个既有 [HarmonyPatch]，不得新增")

    # 三个 armed 快路径齐全
    for token, desc in [
        ("IsModeGSuppressionArmed()", "Mode G armed"),
        ("ModeHDeathSuppressionRegistry.IsSuppressionArmed", "Mode H armed"),
        ("PetNestDeathSuppressionRegistry.IsSuppressionArmed", "遗种巢 armed"),
    ]:
        if token not in code:
            errors.append("[快路径] 缺少 armed 判定: " + desc)

    # 三个身份查询齐全
    for token, desc in [
        ("ModeGRuntimeGates.IsModeGOnDeadSuppressionActive(deadHealth)", "Mode G 身份查询"),
        ("ModeHDeathSuppressionRegistry.IsModeHOnDeadSuppressionActive(deadHealth)", "Mode H 身份查询"),
        ("PetNestDeathSuppressionRegistry.IsPetNestOnDeadSuppressionActive(deadHealth)", "遗种巢身份查询"),
    ]:
        if token not in code:
            errors.append("[身份] 缺少身份查询: " + desc)

    # 只跳过本 Mod 的两个 handler，原版流程不动
    prefix = re.search(r"public static void Prefix\(CharacterMainControl __instance\)[\s\S]*?\n        \}", code)
    if prefix is None:
        errors.append("[链] 无法解析 OnDead 的 Prefix")
    else:
        body = prefix.group(0)
        if "FrostmourneBlueBossDropHandler.TryHandleBlueBossDeath(__instance);" not in body:
            errors.append("[链] 原有的霜之哀伤 handler 不得被移除")
        if "PhantomWitchScytheBossDropHandler.TryHandlePhantomWitchDeath(__instance);" not in body:
            errors.append("[链] 原有的女巫镰刀 handler 不得被移除")
        if "return false" in body:
            errors.append("[原版流程] Prefix 不得返回 false（那会跳过原版 OnDead）")
        if "suppressed = false;" not in body:
            errors.append("[fail-open] 查询异常必须 fail-open=false 让原 handler 继续")


def check_registry(errors):
    text = read_petnest("PetNestDeathSuppressionRegistry.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestDeathSuppressionRegistry.cs")
        return
    code = strip_cs_comments(text)

    if not re.search(r"public static bool IsSuppressionArmed", code):
        errors.append("[API] 缺少 IsSuppressionArmed 快路径")
    if not re.search(r"public static bool IsPetNestOnDeadSuppressionActive\(Health deadHealth\)", code):
        errors.append("[API] 缺少 IsPetNestOnDeadSuppressionActive(Health)")

    # 薄封装：复用随从组件的静态身份表，不维护第二份簿记
    if "PetNestCompanionAgent.IsCompanionArmed" not in code:
        errors.append("[单一真相] armed 必须复用随从组件的静态表")
    if "PetNestCompanionAgent.IsCompanionHealth(deadHealth)" not in code:
        errors.append("[单一真相] 身份查询必须复用随从组件的静态表")
    for forbidden in ["HashSet<", "Dictionary<", "RegisterPreset", "RegisterCharacter"]:
        if forbidden in code:
            errors.append("[单一真相] 不得维护第二份身份簿记: " + forbidden)

    # fail-open=false
    for fn in ["IsSuppressionArmed", "IsPetNestOnDeadSuppressionActive"]:
        block = re.search(r"public static bool " + fn + r"[\s\S]{0,600}?\n        \}", code)
        if block is not None and "catch (Exception)" not in block.group(0):
            errors.append("[fail-open] " + fn + " 必须 no-throw 且返回 false")


def main():
    errors = []
    check_patch(errors)
    check_registry(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
