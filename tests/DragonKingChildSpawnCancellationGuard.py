#!/usr/bin/env python3
"""
DragonKingChildSpawnCancellationGuard — 龙王孩儿护我生成取消守卫（规格 §20 第 18 条）。

不变式：
- 10 秒超时：龙裔异步生成等待循环 waitTime < 10f，超时不得无限挂起；
- 父失效与 late result 撤销/定点清理：生成失败（spawnedDescendant == null）
  走 TriggerLinkedDeath 联动死亡并 yield break；
- 子亡父随：OnDescendantDeath 触发 TriggerLinkedDeath；
- 仅 Mode G 传播 linked kill credit：契约侧 CreateModeGPrimary 冻结
  PreserveLinkedKillAttribution=true（Mode G 主 Boss 保留联动击杀来源），
  且该开关必须有 Mode G 消费点（把联动击杀来源归因给玩家）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CHILD_PROTECTION = os.path.join(REPO_ROOT, "Integration", "DragonKing",
                                "DragonKingAbilityController_ChildProtection.cs")
CONTRACTS = os.path.join(REPO_ROOT, "Utilities", "ManagedBossSpawnContracts.cs")


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []
    cp = read(CHILD_PROTECTION, errors)
    contracts = read(CONTRACTS, errors)

    if cp:
        checks = [
            ("TenSecondTimeout",
             r"while \(!spawnCompleted && waitTime < 10f\)",
             "异步生成等待 10 秒超时（不得无限挂起）"),
            ("TimeoutIncrement",
             r"waitTime \+= Time\.deltaTime;",
             "超时计时按帧推进"),
            ("FailureLinkedDeath",
             r"if \(spawnedDescendant == null\)[\s\S]{0,300}?TriggerLinkedDeath\(\);"
             r"[\s\S]{0,80}?yield break;",
             "生成失败触发联动死亡并终止协程（late result 撤销路径）"),
            ("ChildDeathLinked",
             r"private void OnDescendantDeath\(DamageInfo damageInfo\)"
             r"[\s\S]{0,300}?TriggerLinkedDeath\(\);",
             "子亡父随：OnDescendantDeath 触发联动死亡"),
            ("LinkedDeathImpl",
             r"private void TriggerLinkedDeath\(\)",
             "TriggerLinkedDeath 实现存在"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, cp):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if contracts:
        if not re.search(r"ctx\.PreserveLinkedKillAttribution = true;", contracts):
            errors.append("[AttributionFlag] CreateModeGPrimary 未冻结 PreserveLinkedKillAttribution=true")
        if not re.search(r"public bool PreserveLinkedKillAttribution;", contracts):
            errors.append("[AttributionField] 契约缺少 PreserveLinkedKillAttribution 开关")

    # 仅 Mode G 传播 linked kill credit：开关必须有 Mode G 消费点
    if contracts:
        consumer_found = False
        modeg_dir = os.path.join(REPO_ROOT, "ModeG")
        integration_dir = os.path.join(REPO_ROOT, "Integration")
        for base in (modeg_dir, integration_dir):
            for dirpath, _, filenames in os.walk(base):
                for f in filenames:
                    if not f.endswith(".cs"):
                        continue
                    try:
                        with open(os.path.join(dirpath, f), "r",
                                  encoding="utf-8", errors="replace") as fh:
                            text = fh.read()
                    except OSError:
                        continue
                    if "PreserveLinkedKillAttribution" in text:
                        consumer_found = True
                        break
                if consumer_found:
                    break
            if consumer_found:
                break
        if not consumer_found:
            errors.append("[AttributionConsumed] PreserveLinkedKillAttribution 无 Mode G/adapter 消费点，"
                          "linked kill credit 未传播（联动击杀来源无法归因给玩家）")

    if errors:
        print("DragonKingChildSpawnCancellationGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("DragonKingChildSpawnCancellationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
