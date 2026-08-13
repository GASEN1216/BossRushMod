#!/usr/bin/env python3
"""
ModeGProfilePersistenceGuard — 个人记录持久化守卫（规格 §20 第 23 条）。

不变式：
- 独立 v1 key：BossRush_ModeG_Profile_v1（与宿敌 key 分离）；
- battleResultToken 幂等防重：同一 token 不重复记账；
- contractStreakBreakToken 规则：仅有效 ManualExit 清 streak
  （ClearContractStreakOnManualExit），胜利时 IncrementContractStreak 递增；
- Victory 后保留语义：胜利只递增统计/刷新最佳时间，不清零历史；
- 不从 profile 发物品/货币/加成：文件剥注释后无 Inventory/AddItem/
  GiveMoney/Currency 等发放符号（纯展示/匹配数据）；
- StoreFaulted 单向故障与 Store fail-closed。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROFILE = os.path.join(REPO_ROOT, "ModeG", "ModeGProfilePersistence.cs")
NEMESIS = os.path.join(REPO_ROOT, "ModeG", "ModeGNemesisPersistence.cs")
CLEANUP = os.path.join(REPO_ROOT, "ModeG", "ModeGCleanupController.cs")


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
    profile = read(PROFILE, errors)
    nemesis = read(NEMESIS, errors)
    cleanup = read(CLEANUP, errors)

    if profile:
        checks = [
            ("StorageKey",
             r'public const string StorageKey = "BossRush_ModeG_Profile_v1";',
             "独立 v1 key 冻结"),
            ("TokenIdempotent",
             r"if \(token\.Length > 0 && dto\.lastBattleResultToken == token\) return;",
             "battleResultToken 幂等防重"),
            ("TokenField",
             r"public string lastBattleResultToken;",
             "lastBattleResultToken DTO 字段"),
            ("VictoryOnlyIncrement",
             r"if \(result == ModeGBattleResult\.Victory\)\s*\{\s*"
             r"copy\.totalVictories\+\+;",
             "Victory 分支只递增统计（保留语义）"),
            ("StreakClearEntry",
             r"public static void ClearContractStreakOnManualExit\(\)",
             "ManualExit 清契约连胜入口"),
            ("StreakIncrement",
             r"public static void IncrementContractStreak\(\)",
             "契约达成递增连胜入口"),
            ("StoreFailClosed",
             r"if \(_storeFaulted\) return false;",
             "Store 对 StoreFaulted fail-closed"),
            ("KeyExisitsFirst",
             r"if \(SavesSystem\.KeyExisits\(StorageKey\)\)"
             r"[\s\S]{0,200}?SavesSystem\.Load<ProfileDto>\(StorageKey\);",
             "KeyExisits 前置分类再 Load"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, profile):
                errors.append("[{}] 不满足: {}".format(name, desc))

        # Victory 后不清零历史：RecordRun 体不得将统计字段置 0
        m = re.search(r"public static void RecordRun\([\s\S]*?\n        \}", profile)
        if m:
            body = strip_comments(m.group(0))
            if re.search(r"copy\.(totalRuns|totalVictories|totalDefeats|totalBossKills"
                         r"|bestWaveReached|totalNemesisDefeated)\s*=\s*0", body):
                errors.append("[VictoryKeepHistory] RecordRun 将历史统计清零（Victory 后应保留）")
        else:
            errors.append("[RecordRun] RecordRun 方法未找到")

        # 不从 profile 发物品/货币/加成
        code = strip_comments(profile)
        for token in ["Inventory", "AddItem", "GiveMoney", "AddMoney",
                      "AddCurrency", "GiveCurrency", "AddBuff"]:
            if token in code:
                errors.append("[NoGrant] profile 持久化文件含发放符号 {}".format(token))

    # 独立 key：与宿敌 key 不同
    if profile and nemesis:
        pk = re.search(r'public const string StorageKey = "([^"]+)";', profile)
        nk = re.search(r'public const string StorageKey = "([^"]+)";', nemesis)
        if pk and nk and pk.group(1) == nk.group(1):
            errors.append("[IndependentKey] profile 与宿敌共用存档 key")

    # contractStreakBreakToken 消费点：ManualExit 清 streak
    if cleanup:
        if "ClearContractStreakOnManualExit" not in cleanup and \
                "TryConsumeContractStreakBreakToken" not in cleanup:
            errors.append("[StreakBreakConsumed] CleanupController 未消费契约连胜清除 token")

    if errors:
        print("ModeGProfilePersistenceGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGProfilePersistenceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
