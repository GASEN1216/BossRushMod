#!/usr/bin/env python3
"""
ModeGManagedBossEligibilityGuard — 托管 Boss eligibility 守卫（规格 §20 第 15 条）。

不变式：
- 首胜优先 3 署名 / 复战优先 2 署名 + 1 官方 wildcard；单个托管 Boss
  不可用时只以合格官方 Boss 替换对应槽；
- 至少 1 个官方 key 即可启动；少于 6 个时按 runSeed 从已有 key 确定性复制，
  每槽仍冻结一个 reserve；6 个及以上时维持同波互斥；
- adapter eligibility 初始全 false：未登记 key 查询返回 false，登记入口仅接受
  托管 key（安全审计：非托管 key 拒绝）；
- Owner 批准现有过滤 Boss 池后，池内非空 stable key 可进入资格层；实际快照仍要求
  唯一 key、排除托管 Boss、拒绝重复 preset 引用且至少 1 个；
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
    state_model = read("ModeGStateModel.cs")
    bridge = read("ModeGRuntimeBridge.cs")
    transaction = read("ModeGSpawnTransaction.cs")
    runtime = read("ModeGRuntimeModule.cs")
    entry = read("ModeGEntry.cs")
    availability = read("ModeGAvailability.cs")
    if waveplan is None:
        errors.append("ModeGWavePlan.cs 不存在")
    if variation is None:
        errors.append("ModeGEncounterVariation.cs 不存在")
    if state_model is None:
        errors.append("ModeGStateModel.cs 不存在")

    if waveplan:
        checks = [
            ("OfficialPoolReplication",
             r"public const int MinimumOfficialPoolSize =\s*"
             r"ModeGOfficialBossEligibilityRegistry\.MinimumProductionOfficialBossCount;"
             r"[\s\S]{0,500}?public const int OfficialPoolReplicationTarget =\s*"
             r"ModeGOfficialBossEligibilityRegistry\.OfficialPoolReplicationTarget;"
             r"[\s\S]{0,7000}?if \(officialCount < MinimumOfficialPoolSize\) return null;"
             r"[\s\S]{0,220}?bool allowOfficialCopies = officialCount < OfficialPoolReplicationTarget;",
             "至少 1 个官方 key；少于 6 个时开启确定性复制"),
            ("SignatureSlotFallback",
             r"if \(selected == null\)[\s\S]{0,260}?TakeNextOfficial\(",
             "单个不可用署名槽回退合格官方 key"),
            ("PerSlotReserve",
             r"public readonly string\[\] reservePresetKeys;[\s\S]{0,12000}?"
             r"keys, reserveKeys, variant\)",
             "每槽冻结对应 reserve"),
            ("OfficialPoolParam",
             r"IList<string> officialKeys",
             "官方池 run-scoped 快照参数"),
            ("WildcardDoc",
             r"RematchMix：保留 2 署名 \+ 官方 wildcard",
             "复战 wildcard 规则冻结"),
            ("SmallPoolDuplicateFallback",
             r"bool allowDuplicates\)[\s\S]{0,900}?if \(allowDuplicates\)"
             r"[\s\S]{0,500}?ModeGDeterministicRandom\.NextInt\(ref state, source\.Count\)",
             "小池只从已有 stable key 走确定性复制兜底"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, waveplan):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if runtime and not re.search(
            r"attempt == 0[\s\S]{0,120}?wave\.bossPresetKeys\[slotIndex\]"
            r"[\s\S]{0,120}?wave\.reservePresetKeys\[slotIndex\]", runtime):
        errors.append("[ReserveRuntimeWired] 第二次尝试未使用本槽冻结 reserve")

    if state_model:
        registry_checks = [
            ("OfficialRegistry", r"class ModeGOfficialBossEligibilityRegistry",
             "官方 eligibility 私有注册表"),
            ("StableRevision", r"verificationRevision[\s\S]{0,3200}?CurrentVerificationRevision",
             "记录绑定当前 verification revision"),
            ("SideEffectSummary",
             r"specialAttachmentSignature[\s\S]{0,500}?hasUnmanagedAsync"
             r"[\s\S]{0,300}?hasNativeDeathSideEffects"
             r"[\s\S]{0,300}?hasNativeLootSideEffects"
             r"[\s\S]{0,300}?hasVehicleOrShopRole"
             r"[\s\S]{0,300}?hasAdditionalOwner",
             "副作用审计摘要字段完整"),
            ("AdaptationSummary",
             r"supportsMaxHealth[\s\S]{0,300}?supportsWalkRun"
             r"[\s\S]{0,300}?supportsGunDamage"
             r"[\s\S]{0,300}?supportsMeleeDamage"
             r"[\s\S]{0,300}?supportsGunShootSpeed"
             r"[\s\S]{0,300}?usesFixedDamageController",
             "适应能力摘要字段完整"),
            ("OwnerApprovedPoolPolicy",
             r"public const bool TrustConfiguredBossPool = true;[\s\S]{0,1800}?"
             r"if \(TrustConfiguredBossPool\) return !string\.IsNullOrEmpty\(stableKey\);",
             "Owner 批准的现有过滤 Boss 池以非空 stable key 放行"),
            ("PoolSizePolicy",
             r"public const int MinimumProductionOfficialBossCount = 1;"
             r"[\s\S]{0,500}?public const int OfficialPoolReplicationTarget = 6;",
             "启动最低 1 个、完整编排目标 6 个"),
            ("DistinctStableKeys",
             r"EligibleRecordCount[\s\S]{0,700}?HasUniqueStableKey\(i\)"
             r"[\s\S]{0,1800}?private static bool HasUniqueStableKey\(int recordIndex\)",
             "最低官方池只计算唯一 stable key"),
            ("DuplicateRecordLookupRejected",
             r"TryGetEligibleRecord\(string stableKey[\s\S]{0,900}?"
             r"if \(record != null\) return false;",
             "同 stable key 重复审计记录查询必须 fail-closed"),
        ]
        for name, pattern, desc in registry_checks:
            if not re.search(pattern, state_model):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if bridge and not re.search(
            r"ModeGOfficialBossEligibilityRegistry\.IsEligible\(info\.name\)\) continue;",
            bridge):
        errors.append("[SnapshotRegistryGate] 官方快照未消费 eligibility registry")
    if bridge and not re.search(
            r"TryGetValue\(info\.name, out existingOfficial\)[\s\S]{0,260}?"
            r"!ReferenceEquals\(existingOfficial, info\)[\s\S]{0,220}?return null;", bridge):
        errors.append("[DuplicateStableKeyFailClosed] 同 key 不同 preset 引用未拒绝快照")
    if bridge and not re.search(
            r"snapshot\.officialKeys\.Count[\s\S]{0,180}?"
            r"ModeGOfficialBossEligibilityRegistry\.MinimumProductionOfficialBossCount"
            r"[\s\S]{0,220}?return null;", bridge):
        errors.append("[SnapshotMinimumInventory] run-scoped 官方快照为空时未 fail-closed")

    if entry:
        minimum_checks = len(re.findall(r"HasMinimumModeGOfficialBossPool\(\)", entry))
        if minimum_checks < 3:
            errors.append("[EntryMinimumInventory] preview/最终入口未共同要求至少 1 个唯一 Boss 池 key")

    if transaction:
        if "ModeGOfficialBossEligibilityRegistry.IsEligible(name)" not in transaction:
            errors.append("[KeyLookupRegistryGate] official key helper 未消费 eligibility registry")
        if "ModeGOfficialBossEligibilityRegistry.IsEligible(key)" not in transaction:
            errors.append("[PresetLookupRegistryGate] exact preset helper 未消费 eligibility registry")
        if not re.search(
                r"EnemyPresetInfo found = null;[\s\S]{0,420}?"
                r"!ReferenceEquals\(found, pool\[i\]\)\) return null;", transaction):
            errors.append("[PresetLookupDuplicateFailClosed] helper 仍对同 key 多引用 first-wins")

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
