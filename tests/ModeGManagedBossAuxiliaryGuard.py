#!/usr/bin/env python3
"""
ModeGManagedBossAuxiliaryGuard — 托管 Boss 主/辅分离守卫（规格 §20 第 16 条）。

不变式：
- 主/辅集合分离：ManagedBossRole 枚举 Primary/Auxiliary/PhaseProxy 冻结，
  SpawnContext 默认 Role=Primary；
- 激活前原子提交：契约提供 TryCommitAuxiliaryBeforeActivation（返回 true 才可激活）
  与 OnAuxiliaryReleased（仅成功提交者恰好一次）委托，且必须有消费点接线；
- 父 owner/child handle：handle 按精确 Character owner 清理（CleanupOnce 幂等），
  不盲清全局单例；
- 女巫随从上限：PhantomWitchConfig.MaxMinions=2，两处召唤入口均消费上限；
- 迟到 ticket 不写 Legacy 单例：cleanup 按 owner 退订（契约注释冻结）。
"""
import os
import re
import sys
from cs_source_util import clean_source

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONTRACTS = os.path.join(REPO_ROOT, "Utilities", "ManagedBossSpawnContracts.cs")
PW_CONFIG = os.path.join(REPO_ROOT, "Integration", "PhantomWitch", "PhantomWitchConfig.cs")
PW_SCHEDULER = os.path.join(REPO_ROOT, "Integration", "PhantomWitch",
                            "PhantomWitchAbilityController_PackageScheduler.cs")
PW_TICKS = os.path.join(REPO_ROOT, "Integration", "PhantomWitch",
                        "PhantomWitchAbilityController_RuntimeTicks.cs")
PW_ADAPTER = os.path.join(REPO_ROOT, "Integration", "PhantomWitch", "PhantomWitchBoss_ModeGAdapter.cs")
PW_MINIONS = os.path.join(REPO_ROOT, "Integration", "PhantomWitch", "PhantomWitchAbilityController_Minions.cs")


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
    contracts = read(CONTRACTS, errors)
    pw_config = read(PW_CONFIG, errors)
    pw_scheduler = read(PW_SCHEDULER, errors)
    pw_ticks = read(PW_TICKS, errors)

    if contracts:
        checks = [
            ("RoleEnum",
             r"internal enum ManagedBossRole[\s\S]*?Primary,[\s\S]*?Auxiliary,[\s\S]*?PhaseProxy",
             "ManagedBossRole 枚举 Primary/Auxiliary/PhaseProxy 冻结"),
            ("DefaultRolePrimary",
             r"public ManagedBossRole Role = ManagedBossRole\.Primary;",
             "SpawnContext 默认 Role=Primary"),
            ("AuxCommitDelegate",
             r"public Func<CharacterMainControl, ManagedBossRole, bool> TryCommitAuxiliaryBeforeActivation;",
             "激活前原子提交委托（返回 true 才可激活）"),
            ("AuxReleasedDelegate",
             r"public Action<CharacterMainControl, ManagedBossRole> OnAuxiliaryReleased;",
             "辅助释放通知委托（仅成功提交者恰好一次）"),
            ("OwnerScopedCleanup",
             r"cleanup 按精确 Character owner 退订，不盲清全局单例/共享集合",
             "handle 按精确 owner 清理（迟到 ticket 不写 Legacy 单例）"),
            ("CleanupOnceIdempotent",
             r"public void CleanupOnce\(ManagedBossCleanupReason reason\)",
             "CleanupOnce 幂等清理入口"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, contracts):
                errors.append("[{}] 不满足: {}".format(name, desc))

    # 直接核对生产消费链；扫描整个仓库会进入 Build/、官方源码与 node_modules，
    # 而任意注释里出现委托名就能假绿，既慢也不能证明激活屏障。
    adapter = clean_source(read(PW_ADAPTER, errors))
    minions = clean_source(read(PW_MINIONS, errors))
    for name, pattern, source in (
        ("AuxAdapterBinding", r"capturedController\.BindModeGAuxiliaryContract\(\s*"
         r"ctx\.TryCommitAuxiliaryBeforeActivation,\s*ctx\.OnAuxiliaryReleased\);", adapter),
        ("AuxDelegateStored", r"_modeGAuxCommit = tryCommitBeforeActivation;\s*"
         r"_modeGAuxReleased = onReleased;", minions),
        ("AuxCommitBeforeActivation", r"auxCommitted = _modeGAuxCommit\(minion, ManagedBossRole\.Auxiliary\);"
         r"[\s\S]{0,500}?if \(!auxCommitted\)\s*\{\s*CleanupSpawnedMinion\(minion\);\s*return;\s*\}"
         r"\s*_modeGCommittedMinions\.Add\(minion\);\s*\}\s*FinalizeSpawnedMinion\(minion, index\);", minions),
        ("AuxReleaseOnce", r"if \(!_modeGCommittedMinions\.Remove\(minion\)\) return;\s*"
         r"if \(_modeGAuxReleased == null\) return;\s*_modeGAuxReleased\(minion, ManagedBossRole\.Auxiliary\);", minions),
    ):
        if not re.search(pattern, source):
            errors.append("[{}] 托管辅助提交/释放生产接线缺失".format(name))

    if pw_config:
        if not re.search(r"public const int MaxMinions = 2;", pw_config):
            errors.append("[MaxMinions] PhantomWitchConfig.MaxMinions 必须为 2")

    if pw_scheduler:
        if "CountLiveMinions() >= PhantomWitchConfig.MaxMinions" not in pw_scheduler:
            errors.append("[SchedulerCap] PackageScheduler 召唤入口未消费 MaxMinions 上限")
    if pw_ticks:
        if not re.search(r"CountOccupiedMinionSlots\(\)[\s\S]{0,80}?PhantomWitchConfig\.MaxMinions",
                         pw_ticks):
            errors.append("[TicksCap] RuntimeTicks 召唤入口未消费 MaxMinions 上限")

    if errors:
        print("ModeGManagedBossAuxiliaryGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGManagedBossAuxiliaryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
