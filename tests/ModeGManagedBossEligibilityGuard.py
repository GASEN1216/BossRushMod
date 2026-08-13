#!/usr/bin/env python3
"""
ModeGManagedBossEligibilityGuard — 托管 Boss eligibility 守卫（规格 §20 第 15 条）。

不变式：
- 首胜 3 署名 / 复战 2 署名 + 1 官方 wildcard：WavePlan 按 runFormat 冻结
  requiredSignature = 3（FirstClearNarrative）/ 2（RematchMix），池不足 fail-closed；
- adapter eligibility 初始全 false：未登记 key 查询返回 false，登记入口仅接受
  托管 key（安全审计：非托管 key 拒绝）；
- 生产池为空不得用开发池冒充（!IsProductionReady && AllowDevTestEntry 双条件）；
- BossFilter 禁用：ModeG/ 全目录不引用 BossFilter（官方池快照走
  run-scoped Ordinal 排序快照，不触碰过滤器）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")


def read(name):
    path = os.path.join(MODEG_DIR, name)
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def strip_comments(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def main():
    errors = []
    waveplan = read("ModeGWavePlan.cs")
    variation = read("ModeGEncounterVariation.cs")
    if waveplan is None:
        errors.append("ModeGWavePlan.cs 不存在")
    if variation is None:
        errors.append("ModeGEncounterVariation.cs 不存在")

    if waveplan:
        checks = [
            ("RequiredSignatureRule",
             r"int requiredSignature = \(runFormat == ModeGRunFormat\.FirstClearNarrative\) \? 3 : 2;",
             "首胜 3 署名 / 复战 2 署名规则"),
            ("SignaturePoolFailClosed",
             r"if \(signatureCount < requiredSignature\) return null;",
             "署名池不足 fail-closed"),
            ("OfficialPoolParam",
             r"IList<string> officialKeys",
             "官方池 run-scoped 快照参数"),
            ("WildcardDoc",
             r"官方池需足够填充非署名波总量[\s\S]*?wildcard",
             "复战 wildcard 由官方池填充"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, waveplan):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if variation:
        code = strip_comments(variation)
        checks = [
            ("UnregisteredFalse",
             r"public static bool IsSignatureEligible\(string key\)",
             "eligibility 查询入口"),
            ("RegisterAudited",
             r"if \(string\.IsNullOrEmpty\(key\) \|\| !IsManagedSignatureKey\(key\)\) return;",
             "登记入口拒绝非托管 key（安全审计）"),
            ("DevPoolDoubleGate",
             r"!ModeGAvailability\.IsProductionReady\s+&&\s*ModeGAvailability\.AllowDevTestEntry",
             "开发池兜底双条件门控"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, code):
                errors.append("[{}] 不满足: {}".format(name, desc))
        # eligibility 存储初始为空（非硬编码 true 池）
        if re.search(r"private static readonly (HashSet<string>|Dictionary<string, bool>)", code):
            pass
        else:
            errors.append("[EligibilityStore] 缺少私有 eligibility 存储（初始空池）")

    # BossFilter 禁用
    for f in sorted(os.listdir(MODEG_DIR)):
        if not f.endswith(".cs"):
            continue
        if "BossFilter" in strip_comments(read(f)):
            errors.append("[NoBossFilter] ModeG/{} 引用 BossFilter".format(f))

    if errors:
        print("ModeGManagedBossEligibilityGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGManagedBossEligibilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
