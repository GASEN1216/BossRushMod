#!/usr/bin/env python3
"""
ModeGManagedBossLootGuard — 托管 Boss 掉落关闭守卫（规格 §20 第 17 条）。

不变式：
- preset+运行时两层关原生箱/标准掉落：
  CreateModeGPrimary 固定 RegisterStandardLootTracking=false + DropBoxOnDead=false；
  staging preset 与 runtime preset 均 dropBoxOnDead=false；
- CharacterOnDeadPatch 注册表抑制：先读静态 bool 快速早返，
  命中 IsModeGOnDeadSuppressionActive 时整段跳过两个额外掉落 handler；
  查询 no-throw、异常 fail-open=false（suppressed=false 让原 handler 继续）；
- 与 Hurt 屏障分工明确：CharacterOnDeadPatch 不是伤害屏障（不得触碰 Health.Hurt）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONTRACTS = os.path.join(REPO_ROOT, "Utilities", "ManagedBossSpawnContracts.cs")
TRANSACTION = os.path.join(REPO_ROOT, "ModeG", "ModeGSpawnTransaction.cs")
DD_ADAPTER = os.path.join(REPO_ROOT, "Integration", "DragonDescendant",
                          "DragonDescendantBoss_ModeGAdapter.cs")
ONDEAD_PATCH = os.path.join(REPO_ROOT, "Patches", "Combat", "CharacterOnDeadPatch.cs")


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def strip_comments(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def main():
    errors = []
    contracts = read(CONTRACTS, errors)
    tx = read(TRANSACTION, errors)
    dd = read(DD_ADAPTER, errors)
    patch = read(ONDEAD_PATCH, errors)

    if contracts:
        checks = [
            ("PrimaryNoLootTracking",
             r"ctx\.RegisterStandardLootTracking = false;",
             "CreateModeGPrimary 关标准掉落追踪"),
            ("PrimaryNoDropBox",
             r"ctx\.DropBoxOnDead = false;",
             "CreateModeGPrimary 关原生箱"),
            ("LegacyDefaultsOn",
             r"public bool RegisterStandardLootTracking = true;"
             r"[\s\S]*?public bool DropBoxOnDead = true;",
             "Legacy 默认值保持 true（不波及旧模式）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, contracts):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if tx:
        if "stagingPreset.dropBoxOnDead = false;" not in tx:
            errors.append("[StagingDropBox] staging preset 未关 dropBoxOnDead")

    if dd:
        if "stagingPreset.dropBoxOnDead = false;" not in dd:
            errors.append("[AdapterStagingDropBox] adapter staging preset 未关 dropBoxOnDead")
        if "runtimePreset.dropBoxOnDead = false;" not in dd:
            errors.append("[AdapterRuntimeDropBox] adapter runtime preset 未关 dropBoxOnDead")

    if patch:
        checks = [
            ("StaticBoolFirst",
             r"if \(IsModeGSuppressionArmed\(\)\)",
             "先读静态 bool 快速早返"),
            ("SuppressionQuery",
             r"ModeGRuntimeGates\.IsModeGOnDeadSuppressionActive\(deadHealth\)",
             "注册表抑制查询（staging preset/已登记 Character 引用身份）"),
            ("FailOpenFalse",
             r"suppressed = false;\s*\n\s*LogSuppressionQueryFaultLimited",
             "查询异常 fail-open=false（原 handler 继续）"),
            ("SkipBothHandlers",
             r"if \(suppressed\)[\s\S]{0,300}?return;",
             "命中抑制整段跳过两个额外掉落 handler"),
            ("HandlersIntact",
             r"FrostmourneBlueBossDropHandler\.TryHandleBlueBossDeath\(__instance\);"
             r"[\s\S]{0,120}?PhantomWitchScytheBossDropHandler\.TryHandlePhantomWitchDeath\(__instance\);",
             "两个原 handler 保留且顺序不变"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, patch):
                errors.append("[{}] 不满足: {}".format(name, desc))

        # 分工明确：OnDead patch 不是伤害屏障（不得触碰 Health.Hurt）
        code = strip_comments(patch)
        if "Health.Hurt" in code or "nameof(Health.Hurt)" in code:
            errors.append("[NotDamageBarrier] CharacterOnDeadPatch 不得引用 Health.Hurt（分工明确）")

    if errors:
        print("ModeGManagedBossLootGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGManagedBossLootGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
