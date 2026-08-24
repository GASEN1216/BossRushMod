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
- 普通奖励与信物共用分阶段可靠交付：背包/仓库异常以后验归属、
  缓冲计数或实例销毁确认是否已提交，禁止外层异常跳过 fallback；
- 胜利幂等返还信物（TypeID 500057）一次（Interlocked CAS），
  地面 fallback 必须验证拾取代理。

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
SPAWN = os.path.join(REPO_ROOT, "ModeG", "ModeGSpawnTransaction.cs")
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
    spawn = read(SPAWN, errors)
    entry = read(ENTRY, errors)
    mat_file = read(MATERIALIZER, errors)

    if reward:
        checks = [
            ("HighQualityCandidateGate",
             r"meta\.id != typeId \|\| meta\.quality < 5 \|\| meta\.quality > 8",
             "Mode G 奖励候选只接受 Q5-Q8，不改变 Legacy 候选池"),
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
             r"if \(System\.Threading\.Interlocked\.Exchange\(ref _relicReturnExecuted, 1\) != 0\) return true;",
             "信物返还 Interlocked CAS 幂等一次"),
            ("RelicTypeId",
             r"public const int RelicTypeId = FateEchoRelicConfig\.TYPE_ID;",
             "信物 TypeID 引用 500057 常量"),
            ("ReliableDeliveryHelper",
             r"internal static bool TryCommitItemToInventoryOrStorage\("
             r"[\s\S]{0,500}?inventory\.AddAndMerge\(item, 0\)"
             r"[\s\S]{0,900}?ItemUtilities\.SendToPlayerStorage\(item, true\)",
             "背包与仓库缓冲使用独立可靠交付边界"),
            ("InventoryPostcondition",
             r"private static bool IsCommittedToInventory\("
             r"[\s\S]{0,500}?ReferenceEquals\(item\.InInventory, inventory\)",
             "背包回调异常后验证实际 Inventory 归属/实例消费"),
            ("StoragePostcondition",
             r"int bufferCountBefore = GetIncomingBufferCount\(\);"
             r"[\s\S]{0,900}?DidStorageBufferAcceptItem\(item, bufferCountBefore\)",
             "仓库回调异常后调用提交后验检查"),
            ("StoragePostconditionEvidence",
             r"private static bool DidStorageBufferAcceptItem\("
             r"[\s\S]{0,500}?buffer\.Count > bufferCountBefore"
             r"[\s\S]{0,300}?return item == null;",
             "仓库后验检查验证缓冲计数或实例销毁"),
            ("RelicReliableDelivery",
             r"TryCommitItemWithGroundFallback\(\s*relic,\s*inventory,",
             "信物使用统一关键物品可靠交付入口"),
            ("GroundFallbackPostcondition",
             r"internal static bool TryCommitItemWithGroundFallback\("
             r"[\s\S]{0,700}?TryCommitItemToInventoryOrStorage\(item, inventory, itemLabel\)"
             r"[\s\S]{0,700}?DuckovItemAgent pickup = item\.Drop\("
             r"[\s\S]{0,900}?item\.ActiveAgent != null",
             "关键物品以实际拾取代理验证地面 fallback"),
            ("RelicFailureRollback",
             r"relic\.DestroyTree\(\);[\s\S]{0,400}?"
             r"信物返还失败后的实例清理异常[\s\S]{0,240}?"
             r"Interlocked\.Exchange\(ref _relicReturnExecuted, 0\);\s*return false;",
             "信物全部交付失败时销毁临时实例并重开幂等闸"),
        ]
        for name, pattern, desc in checks:
            haystack = spawn if name == "HighQualityCandidateGate" else reward
            if not re.search(pattern, haystack):
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
            ("SharedReliableDelivery",
             r"ModeGRewardTransaction\.TryCommitItemToInventoryOrStorage\("
             r"[\s\S]{0,120}?item, targetInventory,",
             "strict materializer 复用分阶段可靠交付"),
            ("FailedInstanceCleanup",
             r"if \(!committed\)\s*\{[\s\S]{0,180}?item\.DestroyTree\(\);",
             "可靠交付全部失败后销毁未提交实例"),
            ("StrictNoRetry",
             r"if \(!committed\) failedCount\+\+;",
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
