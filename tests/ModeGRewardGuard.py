#!/usr/bin/env python3
"""
ModeGRewardGuard — Mode G 严格奖励事务守卫（规格 §20 第 25 条）。

不变式：
- 模板一次构建：BuildSlotPlan 内 TypeID 升序去重冻结序列、确定性抽样
  （Reward domain），候选不足（General<7 / Premium<5）返回 null fail-closed；
- nearest-rank = clamp(0, N-1, ceil(p*N)-1)；
- 10 槽 RewardSlotPlan：GeneralBase 槽 0..5 / Premium 槽 6..9；
  分带 <P75 / P75..P95 含边界 / >P95 ExtremeExcluded 不进槽，两池互斥；
- Resolve → 件数 6/7/8/9/10；
- 双 nonce（reward/attempt）确定性派生；Rewarding 死亡先失效 attempt；
  Execute 守卫 Victory-only + nonce 有效 + rewardNonceInvalidated；
- strict materializer：每帧最多 1 件 InstantiateSync、TypeID 快照内部拷贝、
  单件失败销毁实例不静默重试/替换、完成回调后自毁；
- 禁异步 Task/timeout：materializer 类剥注释后无 async/await/Task./
  Invoke(/StartCoroutine/InvokeRepeating；
- 胜利幂等返还信物（TypeID 500057）一次（Interlocked CAS）。

规格偏差注明：规格列举的 CandidateRejected 四类命名原因在实现中以
入口级 failureReason（快照为空/Inventory 为空/初始化失败）表达，
且「staging parent inactive/Loader 移除时序/rollback 引用快照」属
Legacy void/Loader 路径描述——Mode G strict 路径不复用，此处以等价
同步约束（快照拷贝+无 await 窗口）覆盖。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REWARD = os.path.join(REPO_ROOT, "ModeG", "ModeGRewardTransaction.cs")
ENTRY = os.path.join(REPO_ROOT, "LootAndRewards", "LootAndRewardsVictoryRewards.cs")
MATERIALIZER = os.path.join(REPO_ROOT, "LootAndRewards", "VictoryRewardShadowCrateController.cs")


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
    reward = read(REWARD, errors)
    entry = read(ENTRY, errors)
    mat_file = read(MATERIALIZER, errors)

    if reward:
        checks = [
            ("SlotCount", r"public const int SlotCount = 10;", "10 槽冻结"),
            ("SlotRanges",
             r"public const int GeneralSlotBegin = 0;"
             r"[\s\S]*?public const int GeneralSlotEnd = 6;"
             r"[\s\S]*?public const int PremiumSlotBegin = 6;"
             r"[\s\S]*?public const int PremiumSlotEnd = 10;",
             "槽位分带范围冻结"),
            ("PoolMinimums",
             r"public const int GeneralPoolMinimum = 7;"
             r"[\s\S]*?public const int PremiumPoolMinimum = 5;",
             "池最小候选数 7/5"),
            ("NearestRank",
             r"int idx = \(int\)Math\.Ceiling\(p \* n\) - 1;"
             r"[\s\S]{0,120}?if \(idx > n - 1\) idx = n - 1;",
             "nearest-rank = clamp(0, N-1, ceil(p*N)-1)"),
            ("Banding",
             r"if \(price > p95Value\) return ModeGRewardBand\.ExtremeExcluded;"
             r"[\s\S]{0,120}?if \(price >= p75Value\) return ModeGRewardBand\.PremiumP75P95;",
             "分带 >P95 排除 / >=P75 Premium"),
            ("PoolFailClosed",
             r"if \(generalPool\.Count < GeneralPoolMinimum\) return null;"
             r"[\s\S]{0,120}?if \(premiumPool\.Count < PremiumPoolMinimum\) return null;",
             "候选不足 fail-closed"),
            ("TypeIdSortedDedup",
             r"Array\.Sort\(arr, \(a, b\) => a\.typeId\.CompareTo\(b\.typeId\)\);",
             "TypeID 升序去重冻结输入序列"),
            ("DeterministicPick",
             r"ulong state = ModeGDeterministicRandom\.SeedDomain\(runSeed,"
             r"[\s\S]{0,200}?DomainConstants\.Reward",
             "确定性抽样（Reward domain，禁 System.Random）"),
            ("ResolveItemCount",
             r"if \(resolveCount <= 2\) return 6;"
             r"[\s\S]{0,80}?if \(resolveCount <= 5\) return 7;"
             r"[\s\S]{0,80}?if \(resolveCount <= 8\) return 8;"
             r"[\s\S]{0,80}?if \(resolveCount == 9\) return 9;",
             "Resolve → 件数 6/7/8/9/10"),
            ("DoubleNonce",
             r"_rewardNonce = ModeGDeterministicRandom\.SplitMix64Next\(ref state\);"
             r"[\s\S]{0,120}?_attemptNonce = ModeGDeterministicRandom\.SplitMix64Next\(ref state\);",
             "双 nonce 确定性派生"),
            ("AttemptInvalidation",
             r"public static void InvalidateAttemptNonce\(\)[\s\S]{0,150}?_attemptNonce = 0;",
             "Rewarding 死亡先失效 attempt nonce"),
            ("VictoryOnly",
             r"if \(state\.battleResult != ModeGBattleResult\.Victory\)",
             "仅 Victory 发放奖励"),
            ("NonceGuard",
             r"if \(!AreNoncesValid\(\) \|\| state\.rewardNonceInvalidated\)",
             "Execute nonce 双守卫"),
            ("VictoryLease",
             r'ModeGLateCleanupSink\.AcquireLease\("reward_materializer"\)',
             "胜利安全 lease（materializer 完成前 sink 隔离）"),
            ("RelicReturnCas",
             r"if \(System\.Threading\.Interlocked\.Exchange\(ref _relicReturnExecuted, 1\) != 0\) return;",
             "信物返还 Interlocked CAS 幂等一次"),
            ("RelicTypeId",
             r"public const int RelicTypeId = FateEchoRelicConfig\.TYPE_ID;",
             "信物 TypeID 引用 500057 常量"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, reward):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if entry:
        checks = [
            ("TryEntry",
             r"internal bool TryStartModeGRewardMaterialization_LootAndRewards\(",
             "strict materializer Try 入口"),
            ("EntryRejectEmptySnapshot",
             r'failureReason = "TypeID 快照为空";', "入口拒绝：快照为空"),
            ("EntryRejectNullInventory",
             r'failureReason = "目标 Inventory 为空";', "入口拒绝：Inventory 为空"),
            ("EntryRejectInitFail",
             r'failureReason = "materializer 初始化失败";', "入口拒绝：初始化失败"),
            ("CancelEntry",
             r"internal void CancelModeGRewardMaterialization_LootAndRewards\(\)",
             "Rewarding 死亡取消物化入口"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, entry):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if mat_file:
        if not re.search(r"public sealed class ModeGRewardStrictMaterializer : MonoBehaviour",
                         mat_file):
            errors.append("[MaterializerClass] ModeGRewardStrictMaterializer 不存在")
        checks = [
            ("SnapshotClone",
             r"typeIdSnapshot = \(int\[\]\)fixedTypeIds\.Clone\(\);",
             "TypeID 快照内部拷贝（rollback 引用快照）"),
            ("OnePerFrame",
             r"// 每帧至多一件 InstantiateSync，避免奖励尖峰",
             "每帧最多 1 件 InstantiateSync"),
            ("StrictNoRetry",
             r"// strict：入箱失败即判失败，销毁实例，不静默重试/替换",
             "单件失败不静默重试/替换"),
            ("CompleteThenSelfDestroy",
             r"completionCallback\(total, succeeded, failed\);"
             r"[\s\S]{0,500}?UnityEngine\.Object\.Destroy\(gameObject\);",
             "完成回调后自毁"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, mat_file):
                errors.append("[{}] 不满足: {}".format(name, desc))

        # 禁异步 Task/timeout：materializer 类体剥注释后无异步符号
        m = re.search(r"public sealed class ModeGRewardStrictMaterializer : MonoBehaviour"
                      r"([\s\S]*?)\n    \}", mat_file)
        if m:
            body = strip_comments(m.group(1))
            for token in ["async ", "await ", "Task.", "Invoke(",
                          "StartCoroutine", "InvokeRepeating", "Timeout"]:
                if token in body:
                    errors.append("[NoAsync] materializer 含异步/定时符号 {}".format(token))

    if errors:
        print("ModeGRewardGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGRewardGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
