#!/usr/bin/env python3
"""
ModeHExtraDeathSuppressionGuard — 额外死亡掉落抑制守卫（设计提案 §19.5、§25.2、§26.1）。

不变式：
- CharacterOnDeadPatch 的 Prefix 同时查询 Mode G 与 Mode H 的 preset / Health / Character 引用身份；
- 命中时只跳过本 Mod 的两个额外掉落 handler（霜之哀伤、幽灵女巫镰刀）；
- Prefix 保持 void，不返回 false、不跳过原版 CharacterMainControl.OnDead 与 Health.OnDead；
- 静态快门先行（未激活时零分配早返），查询异常 fail-open 并限频告警；
- Mode H 抑制表：创建前登记 preset、创建返回后补登记角色引用，
  解除顺序为“角色引用 -> preset 引用”，并提供 ResetStaticCaches；
- 不新增覆盖所有角色死亡流程的 Harmony 补丁。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

PATCH = os.path.join(REPO_ROOT, "Patches", "Combat", "CharacterOnDeadPatch.cs")
REGISTRY = os.path.join(REPO_ROOT, "ModeH", "ModeHDeathSuppressionRegistry.cs")
MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")


def main():
    errors = []

    patch = read_text(PATCH)
    if patch is None:
        print("ModeHExtraDeathSuppressionGuard: FAIL (1 errors)")
        print("  - [File] 缺少 Patches/Combat/CharacterOnDeadPatch.cs")
        return 1
    code = strip_cs_comments(patch)

    checks = [
        (r"\[HarmonyPrefix\]\s*\n\s*public static void Prefix\(CharacterMainControl __instance\)",
         "Prefix 保持 void（不得返回 false 跳过原版）"),
        (r"IsModeGSuppressionArmed\(\) \|\| ModeHDeathSuppressionRegistry\.IsSuppressionArmed",
         "静态快门同时覆盖 Mode G 与 Mode H"),
        (r"ModeGRuntimeGates\.IsModeGOnDeadSuppressionActive\(deadHealth\)",
         "保留 Mode G 引用身份查询"),
        (r"ModeHDeathSuppressionRegistry\.IsModeHOnDeadSuppressionActive\(deadHealth\)",
         "新增 Mode H 引用身份查询"),
        (r"FrostmourneBlueBossDropHandler\.TryHandleBlueBossDeath\(__instance\);",
         "未命中时仍执行霜之哀伤 handler"),
        (r"PhantomWitchScytheBossDropHandler\.TryHandlePhantomWitchDeath\(__instance\);",
         "未命中时仍执行女巫镰刀 handler"),
        (r"LogSuppressionQueryFaultLimited", "查询异常限频告警"),
        (r"suppressed = false;", "查询异常 fail-open"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Patch] 不满足: " + desc)

    if re.search(r"public static bool Prefix\(", code):
        errors.append("[Patch] Prefix 不得返回 bool（会跳过原版 OnDead）")

    registry = read_text(REGISTRY)
    if registry is None:
        errors.append("[File] 缺少 ModeH/ModeHDeathSuppressionRegistry.cs")
    else:
        rcode = strip_cs_comments(registry)
        rchecks = [
            (r"public static bool IsSuppressionArmed", "零分配静态快门"),
            (r"public static void RegisterPreset\(CharacterRandomPreset preset\)", "创建前登记 preset"),
            (r"public static void RegisterCharacter\(Health health, CharacterMainControl character\)",
             "创建返回后补登记角色引用"),
            (r"public static void UnregisterCharacter\(Health health\)", "解除角色引用"),
            (r"public static void UnregisterPreset\(CharacterRandomPreset preset\)", "解除 preset 引用"),
            (r"public static bool IsModeHOnDeadSuppressionActive\(Health deadHealth\)", "O\\(1\\) 身份查询"),
            (r"public static void ResetStaticCaches\(\)", "静态缓存清理"),
            (r"return false;", "查询异常 fail-open"),
        ]
        for pattern, desc in rchecks:
            if not re.search(pattern, rcode):
                errors.append("[Registry] 不满足: " + desc)

    # Mode H 只允许 §17.6.5 的两个 postfix，此处不得出现死亡相关 Harmony 补丁
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        c = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        if "HarmonyPatch" in c and name != "ModeHHarmonyPatches.cs":
            errors.append("[Harmony] {} 不得声明 Harmony 补丁".format(name))
        if "HarmonyPatch(typeof(Health)" in c or 'HarmonyPatch(typeof(CharacterMainControl)' in c:
            errors.append("[Harmony] {} 不得对死亡流程打补丁".format(name))

    if errors:
        print("ModeHExtraDeathSuppressionGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHExtraDeathSuppressionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
