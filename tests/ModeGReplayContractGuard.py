#!/usr/bin/env python3
"""
ModeGReplayContractGuard — Mode G 宿命契约守卫（规格 §20 第 8 条）。

不变式：
- 契约数量 >=8（正式首发下限）；数量 <8 时 IsProductionReady 必须 false；
- 稳定 ID 为 bit 0-31 只追加（断言 ID 集合均 <=31 且互异）；
- 四族覆盖：Adaptation/Execution/Tempo/Style 均须出现；
- 入口候选对升序、恰好 2 个、互不相同；
- pairwise token Jaccard <=0.6：静态解析 Evaluate 各契约消费的进度字段集合，
  两两计算 Jaccard；
- 纯目标组合：不新增事件/Stat/条件/奖励（禁 HarmonyPatch/存档写/物品发放）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONTRACT = os.path.join(REPO_ROOT, "ModeG", "ModeGFateContract.cs")
AVAILABILITY = os.path.join(REPO_ROOT, "ModeG", "ModeGAvailability.cs")

PROGRESS_FIELDS = [
    "distanceResolves", "ammoResolves", "attributeResolves", "lastStandCount",
    "nemesisR3FinalBlowDirect", "maxConsecutiveAxisBreaks", "resolvesPerAct",
    "distanceEchoCount", "ammoBanCount", "attributeLockCount",
    "ammoBanAvailableOnNemesisWaves",
]


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.basename(path))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def compute_jaccard(content, errors):
    """解析 Evaluate 的 switch：每个契约 case 消费的进度字段集合。"""
    tokens = {}
    for m in re.finditer(r"case (Id\w+):\s*\n((?:.*\n)*?)(?=\s*case Id|\s*default:)", content):
        contract_id = m.group(1)
        body = m.group(2)
        fields = set(f for f in PROGRESS_FIELDS if "p." + f in body)
        tokens[contract_id] = fields
    if len(tokens) < 8:
        errors.append("[JaccardParse] Evaluate 解析出的契约分支少于 8（当前 {}）".format(len(tokens)))
        return
    ids = sorted(tokens)
    for i in range(len(ids)):
        for j in range(i + 1, len(ids)):
            a, b = tokens[ids[i]], tokens[ids[j]]
            union = a | b
            if not union:
                continue
            jaccard = len(a & b) / float(len(union))
            if jaccard > 0.6:
                errors.append("[Jaccard>0.6] {} vs {} = {:.2f}".format(ids[i], ids[j], jaccard))


def main():
    errors = []
    content = read(CONTRACT, errors)
    avail = read(AVAILABILITY, errors)

    if content:
        # 契约数量下限
        m = re.search(r"public const int ContractCount = (\d+)", content)
        if not m:
            errors.append("[ContractCount] 缺少 ContractCount 常量")
        else:
            count = int(m.group(1))
            if count < 8:
                errors.append("[ContractCount<8] 正式首发不得少于 8 项（当前 {}）".format(count))
                if avail and "public const bool IsProductionReady = true" in avail:
                    errors.append("[CountVsProduction] 契约 <8 项时 IsProductionReady 必须 false")
            actual_defs = len(re.findall(r"new ContractDef\(Id", content))
            if actual_defs != count:
                errors.append("[PoolSizeMismatch] 池定义 {} 项 != ContractCount {}".format(
                    actual_defs, count))

        # 稳定 ID 只追加、<=31、互异
        ids = re.findall(r"public const int (Id\w+) = (\d+);", content)
        values = [int(v) for _, v in ids]
        if len(set(values)) != len(values):
            errors.append("[IdUnique] 契约稳定 ID 出现重复")
        if values and (min(values) < 0 or max(values) > 31):
            errors.append("[IdRange] 契约 ID 必须在 bit 0-31")

        # 四族覆盖
        for family in ["Adaptation", "Execution", "Tempo", "Style"]:
            if not re.search(r"ModeGContractFamily\." + family + r"[\s,]", content):
                errors.append("[Family:{}] 四族覆盖缺失".format(family))

        # 候选对升序互异
        if not re.search(r"if \(second >= first\) second\+\+;", content):
            errors.append("[PairDistinct] 候选对未保证互异")
        if not re.search(r"int a = Math\.Min\(first, second\);\s*\n\s*int b = Math\.Max\(first, second\);",
                         content):
            errors.append("[PairAscending] 候选对未升序输出")
        if not re.search(r"SeedDomain\(runSeed,\s*\n?\s*ModeGDeterministicRandom\.DomainConstants\.Contract",
                         content):
            errors.append("[PairDeterministic] 候选对未走 Contract domain 确定性派生")

        # Jaccard <= 0.6
        compute_jaccard(content, errors)

        # 纯目标组合禁止项
        for forbidden, label in [
                ("HarmonyPatch", "Harmony patch"),
                ("SaveFile", "存档写入"),
                ("Instantiate", "物品实例化"),
                ("AddItem", "物品发放"),
                ("UnityEngine.Random", "非冻结随机源")]:
            if forbidden in content:
                errors.append("[PureObjective] 契约不得引入 {}: 出现 {}".format(label, forbidden))

    if errors:
        print("ModeGReplayContractGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGReplayContractGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
