#!/usr/bin/env python3
"""
ModeGMapSupportGuard — Mode G 支持地图注册表守卫（规格 §20 第 3 条）。

不变式：
- SupportedMap 不可变条目含 exact sceneName+sceneID+非空 verificationRevision；
- IsSupported 必须 exact 匹配 scene pair（单点通过不得冒充三元组）；
- preview 冻结的 scene pair 来自注册表 Verified 记录（TryGetPrimaryVerifiedPair）；
- 注册表 revision 取自 ModeGAvailability.CurrentVerificationRevision；
- 规格要求：每条记录须有 death behavior / 双语风险 key / 冻结安全三元组
  （缺失即 FAIL——实现补齐前不放宽）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")


def read_file(name):
    path = os.path.join(MODEG_DIR, name)
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []

    registry = read_file("ModeGMapSupportRegistry.cs")
    entry = read_file("ModeGEntry.cs")
    if registry is None:
        errors.append("ModeGMapSupportRegistry.cs 不存在")
    if entry is None:
        errors.append("ModeGEntry.cs 不存在")

    if registry is not None:
        checks = [
            ("ImmutableEntry",
             r"public struct SupportedMap",
             "SupportedMap 条目类型存在"),
            ("BaseSceneName",
             r"public readonly string baseSceneName",
             "exact 运行时场景名字段"),
            ("CombatSceneId",
             r"public readonly string combatSceneName",
             "exact 加载用场景 ID 字段"),
            ("RevisionField",
             r"public readonly string verificationRevision",
             "验证 revision 字段"),
            ("RevisionFromAvailability",
             r"ModeGAvailability\.CurrentVerificationRevision",
             "revision 取自 Availability 冻结值"),
            ("ExactPairMatch",
             r"string\.Equals\(_supportedMaps\[i\]\.baseSceneName, baseScene, StringComparison\.Ordinal\)"
             r"[\s\S]*?string\.Equals\(_supportedMaps\[i\]\.combatSceneName, combatScene, StringComparison\.Ordinal\)",
             "IsSupported 必须 sceneName+sceneID 双字段 exact Ordinal 匹配（禁单点冒充三元组）"),
            ("ReadOnlyRegistry",
             r"private static readonly SupportedMap\[\] _supportedMaps",
             "注册表只读（首版不开放运行时 Register）"),
            ("PrimaryPairQuery",
             r"public static bool TryGetPrimaryVerifiedPair\(out string sceneName, out string sceneId\)",
             "preview 冻结用 Verified pair 查询存在"),
            # ---- 规格 §20 第 3 条要求的记录字段（实现缺失即 FAIL）----
            ("DeathBehaviorField",
             r"deathBehavior|DeathBehavior",
             "每条记录须有 death behavior 字段（规格 §20 第 3 条）"),
            ("RiskKeyField",
             r"[Rr]iskKey|riskLocKey|风险",
             "每条记录须有双语死亡风险 key（规格 §20 第 3 条）"),
            ("SafetyTriadField",
             r"[Ss]afety[Tt]riad|安全三元组",
             "每条记录须冻结安全三元组（规格 §20 第 3 条）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, registry):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if entry is not None:
        if "ModeGMapSupportRegistry.TryGetPrimaryVerifiedPair" not in entry:
            errors.append("[EntryFreezesVerifiedPair] preview 未从注册表冻结 Verified scene pair")
        if "verificationRevision" not in entry:
            errors.append("[EntryFreezesRevision] preview 未冻结 verificationRevision")

    if errors:
        print("ModeGMapSupportGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGMapSupportGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
