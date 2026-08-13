#!/usr/bin/env python3
"""
ModeG 结构守卫。
验证 Mode G 核心文件存在、compile_official.bat 注册完整、关键不变式满足。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")
COMPILE_BAT = os.path.join(REPO_ROOT, "compile_official.bat")

# 必须存在的 Mode G 核心文件
REQUIRED_FILES = [
    "ModeGEntry.cs",
    "ModeGAvailability.cs",
    "ModeGDeterministicRandom.cs",
    "ModeGStateModel.cs",
    "ModeGRunState.cs",
    "ModeGWavePlan.cs",
    "ModeGAdaptiveCombat.cs",
    "ModeGCombatTelemetry.cs",
    "ModeGDeathRouting.cs",
    "ModeGNemesisPersistence.cs",
    "ModeGProfilePersistence.cs",
    "ModeGRewardTransaction.cs",
    "ModeGMapSupportRegistry.cs",
    "ModeGFateContract.cs",
    "ModeGEncounterVariation.cs",
    "ModeGPresentationAssetCache.cs",
    "ModeGSpawnTransaction.cs",
    "ModeGCleanupController.cs",
    "ModeGRuntimeModule.cs",
    "ModeGRuntimeLifecycle.cs",
    "ModeGRuntimeBridge.cs",
    "ModeGHUD.cs",
    "ModeGRecapPanel.cs",
    "ModeGInteractable.cs",
]

# compile_official.bat 中必须注册的文件
REQUIRED_COMPILE_ENTRIES = ["ModeG\\" + f for f in REQUIRED_FILES]
REQUIRED_COMPILE_ENTRIES += [
    "Utilities\\ManagedBossSpawnContracts.cs",
    "Integration\\DragonDescendant\\DragonDescendantBoss_ModeGAdapter.cs",
    "Integration\\DragonKing\\DragonKingBoss_ModeGAdapter.cs",
    "Integration\\PhantomWitch\\PhantomWitchBoss_ModeGAdapter.cs",
    "Integration\\Items\\FateEchoRelicConfig.cs",
]

# 关键不变式
INVARIANTS = {
    "IsProductionReady_true": (
        "ModeGAvailability.cs",
        r"public const bool IsProductionReady = true",
        "正式入口已开放，IsProductionReady 必须为 true",
    ),
    "FnvOffsetBasis": (
        "ModeGAvailability.cs",
        r"14695981039346656037UL",
        "FNV-1a 64 offset basis 冻结值",
    ),
    "FnvPrime": (
        "ModeGAvailability.cs",
        r"1099511628211UL",
        "FNV-1a 64 prime 冻结值",
    ),
    "SplitMix64Increment": (
        "ModeGAvailability.cs",
        r"0x9E3779B97F4A7C15UL",
        "SplitMix64 增量冻结值",
    ),
    "MaxAttemptsPerSlot": (
        "ModeGSpawnTransaction.cs",
        r"public const int MaxAttemptsPerSlot = 2",
        "生成槽位最多两次尝试",
    ),
    "AttributeLockDamageMultiplier": (
        "ModeGAdaptiveCombat.cs",
        r"public const float AttributeLockValue = -0\.25f",
        "属性封锁 PercentageAdd -0.25",
    ),
    "NineWaves": (
        "ModeGWavePlan.cs",
        r"WaveSlot\[\] waves;.*?// 固定 9 个|new WaveSlot\[9\]",
        "九波固定结构",
    ),
    "StrictRewardVictoryOnly": (
        "ModeGRewardTransaction.cs",
        r"battleResult != ModeGBattleResult\.Victory",
        "严格奖励只在 Victory 后发放",
    ),
    "CasGuardBattleResult": (
        "ModeGRunState.cs",
        r"TryLockBattleResult",
        "BattleResult CAS 守卫存在",
    ),
    "CasGuardLifecycle": (
        "ModeGRunState.cs",
        r"TryAdvanceLifecycle",
        "Lifecycle CAS 守卫存在",
    ),
    "LateCleanupSink": (
        "ModeGCleanupController.cs",
        r"LateCleanupSink",
        "late-cleanup sink 存在",
    ),
    "EmergencyShutdown": (
        "ModeGCleanupController.cs",
        r"EmergencyShutdown",
        "紧急关闭存在",
    ),
    "SchemaVersionNemesis": (
        "ModeGNemesisPersistence.cs",
        r'BossRush_ModeG_NemesisRecord_v1.*?schemaVersion == 0',
        "宿敌存档 v1 key 与默认 schema 0",
    ),
    "SchemaVersionProfile": (
        "ModeGProfilePersistence.cs",
        r'BossRush_ModeG_Profile_v1.*?schemaVersion == 0',
        "个人记录 v1 key 与默认 schema 0",
    ),
    "ContractPoolSize": (
        "ModeGFateContract.cs",
        r"new ContractDef\(",
        "宿命契约池定义存在",
    ),
    "DomainSalts": (
        "ModeGDeterministicRandom.cs",
        r"DomainSalts",
        "八个 domain stream salt 存在",
    ),
}

REPO_INVARIANTS = {
    "ManagedPrepareBeforeActivate": (
        "ModeG/ModeGSpawnTransaction.cs",
        r"UniTask<ManagedBossPrepareResult> DispatchModeGManagedBossSpawnAsync",
        "dispatcher 返回 prepared handle，不提前激活",
    ),
    "BatchActivation": (
        "ModeG/ModeGRuntimeModule.cs",
        r"pendingActivationHandles.*?ActivateOnce",
        "全部槽位结案后批量激活",
    ),
    "StartupRefund": (
        "ModeG/ModeGRuntimeModule.cs",
        r"RefundStartupPaymentOnTechnicalFailure.*?SpawnExhausted.*?TechnicalIntegrityLoss",
        "首波技术失败返还入场物品",
    ),
    "ProductionEligibilityNoDevFallback": (
        "ModeG/ModeGEncounterVariation.cs",
        r"!ModeGAvailability\.IsProductionReady\s*&&\s*ModeGAvailability\.AllowDevTestEntry",
        "生产池不得使用开发署名兜底",
    ),
    "DemoEntryFailClosed": (
        "WavesArena/BossRushEntryFlow.cs",
        r"entryMode == BossRushEntryMode\.ModeG.*?bool startedModeG = TryStartModeG\(\).*?ClearPendingEntryFlowState\(\);\s*yield break;",
        "DEMO Mode G 分支成功失败均清状态并终止",
    ),
    "GroundZeroEntryFailClosed": (
        "Integration/BossRushIntegration_TravelAndSetup.cs",
        r"entryMode == BossRushEntryMode\.ModeG.*?bool startedModeG = TryStartModeG\(\).*?ClearPendingEntryFlowState\(\);\s*yield break;",
        "GroundZero Mode G 分支成功失败均清状态并终止",
    ),
}


def check_files():
    """检查所有必需文件存在"""
    errors = []
    for f in REQUIRED_FILES:
        path = os.path.join(MODEG_DIR, f)
        if not os.path.exists(path):
            errors.append(f"缺失文件: ModeG/{f}")
    return errors


def check_compile_bat():
    """检查 compile_official.bat 注册"""
    errors = []
    if not os.path.exists(COMPILE_BAT):
        errors.append("compile_official.bat 不存在")
        return errors

    with open(COMPILE_BAT, "r", encoding="utf-8", errors="replace") as fh:
        content = fh.read()

    for entry in REQUIRED_COMPILE_ENTRIES:
        if entry not in content:
            errors.append(f"compile_official.bat 缺失注册: {entry}")

    return errors


def check_invariants():
    """检查关键不变式"""
    errors = []
    for name, (filename, pattern, desc) in INVARIANTS.items():
        filepath = os.path.join(MODEG_DIR, filename)
        if not os.path.exists(filepath):
            errors.append(f"[{name}] 文件不存在: {filename}")
            continue

        with open(filepath, "r", encoding="utf-8", errors="replace") as fh:
            content = fh.read()

        if not re.search(pattern, content, re.DOTALL):
            errors.append(f"[{name}] 不变式不满足 ({desc}): {filename}")

    return errors


def check_repo_invariants():
    errors = []
    for name, (relative_path, pattern, desc) in REPO_INVARIANTS.items():
        filepath = os.path.join(REPO_ROOT, *relative_path.split("/"))
        if not os.path.exists(filepath):
            errors.append(f"[{name}] 文件不存在: {relative_path}")
            continue
        with open(filepath, "r", encoding="utf-8", errors="replace") as fh:
            content = fh.read()
        if not re.search(pattern, content, re.DOTALL):
            errors.append(f"[{name}] 不变式不满足 ({desc}): {relative_path}")
    return errors


def check_typeid():
    """检查 TypeID 500057 使用"""
    errors = []
    relic_config = os.path.join(REPO_ROOT, "Integration", "Items", "FateEchoRelicConfig.cs")
    if not os.path.exists(relic_config):
        errors.append("FateEchoRelicConfig.cs 不存在")
        return errors

    with open(relic_config, "r", encoding="utf-8", errors="replace") as fh:
        content = fh.read()

    if "500057" not in content:
        errors.append("FateEchoRelicConfig.cs 缺少 TypeID 500057")

    return errors


def main():
    all_errors = []

    all_errors.extend(check_files())
    all_errors.extend(check_compile_bat())
    all_errors.extend(check_invariants())
    all_errors.extend(check_repo_invariants())
    all_errors.extend(check_typeid())

    if all_errors:
        print(f"ModeGStructureGuard: FAIL ({len(all_errors)} errors)")
        for e in all_errors:
            print(f"  - {e}")
        sys.exit(1)
    else:
        print("ModeGStructureGuard: PASS")
        print(f"  文件: {len(REQUIRED_FILES)} 个")
        print(f"  编译注册: {len(REQUIRED_COMPILE_ENTRIES)} 个")
        print(f"  不变式: {len(INVARIANTS)} 个")
        print(f"  跨文件不变式: {len(REPO_INVARIANTS)} 个")
        sys.exit(0)


if __name__ == "__main__":
    main()
