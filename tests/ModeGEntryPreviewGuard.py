#!/usr/bin/env python3
"""
ModeGEntryPreviewGuard — Mode G 入口 preview 守卫（规格 §20 第 4 条）。

不变式：
- ModeGEntryPreview 不可变（sealed + readonly 字段），冻结 runSeed/runFormat/
  署名轮换/两个契约候选/scene pair/能力 revision（>=6 项）；
- ExpirySeconds 冻结 300 秒；过期 preview 不进 Starting；
- GetOrCreateModeGEntryPreview 对未过期 preview 原样复用（取消重开不刷契约候选）；
- Entry 拒绝路径不写 Legacy 存档/BossFilter（静态禁止项）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ENTRY = os.path.join(REPO_ROOT, "ModeG", "ModeGEntry.cs")

FROZEN_FIELDS = [
    "runSeed",
    "runFormat",
    "signatureRotation",
    "contractCandidateIds",
    "sceneName",
    "sceneId",
    "verificationRevision",
]


def main():
    errors = []
    if not os.path.exists(ENTRY):
        print("ModeGEntryPreviewGuard: FAIL (1 errors)")
        print("  - ModeGEntry.cs 不存在")
        return 1

    with open(ENTRY, "r", encoding="utf-8", errors="replace") as fh:
        content = fh.read()

    if not re.search(r"public sealed class ModeGEntryPreview", content):
        errors.append("[SealedPreview] ModeGEntryPreview 必须是 sealed class")

    if not re.search(r"public const double ExpirySeconds = 300\.0", content):
        errors.append("[ExpiryFrozen] ExpirySeconds 必须冻结为 300.0")

    for field in FROZEN_FIELDS:
        if not re.search(r"public readonly [\w\[\]<>]+ " + field + r"\s*;", content):
            errors.append("[FrozenField:{}] preview 缺少 readonly 冻结字段".format(field))

    if not re.search(r"public bool IsFresh\(long nowTicks\)", content):
        errors.append("[IsFresh] preview 缺少过期判定 IsFresh")

    # 过期 preview 不进 Starting
    if not re.search(r"!preview\.IsFresh\(nowTicks\)", content):
        errors.append("[ExpiredRejected] TryStartModeG 未拒绝过期 preview")

    # 取消重开不刷契约：未过期 preview 原样复用
    if not re.search(
            r"modeGEntryPreview != null && modeGEntryPreview\.IsFresh\(nowTicks\)"
            r"[\s\S]{0,200}?return modeGEntryPreview;", content):
        errors.append("[ReuseFresh] GetOrCreateModeGEntryPreview 未原样复用未过期 preview")

    # 契约候选恰好 2 个、升序（注释与结构约束）
    if "contractCandidateIds" in content and not re.search(
            r"恰好 2 个|升序", content):
        errors.append("[ContractPairDoc] 契约候选对缺少升序/恰好 2 个约束说明")

    # 拒绝路径不写 Legacy/存档/BossFilter（静态禁止项）
    for forbidden in ["BossFilter", "SaveFile", "OnSetFile", "OnCollectSaveData"]:
        if forbidden in content:
            errors.append("[NoLegacyWrite] Entry 不得触碰 {}: 出现于 ModeGEntry.cs".format(forbidden))

    if errors:
        print("ModeGEntryPreviewGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGEntryPreviewGuard: PASS")
    print("  冻结字段: {} 项".format(len(FROZEN_FIELDS)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
