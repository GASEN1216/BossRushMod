#!/usr/bin/env python3
"""
ModeHArenaIsolationGuard — Mode H 原图隔离守卫（设计提案 §19.2、§26.1）。

不变式：
- H intent 先让 Integration 跳过 Legacy 接管，随后由 isolation lease 冻结/回滚原图 spawner、
  清理原生敌人并核对 scene generation；
- lease 成功前不得获取 spectator lease、创建正式 UI 或生成角色；
- 获取失败按已完成步骤逆序回滚；
- 原生敌人已清理时不得在同场景回落 Legacy，必须退款并从 modeHExitPos 安全离场；
- 活动期只检查已登记 spawner 与晚到实例，不在 Update 全场扫描；
- 正常离场、技术中止、场景切换与 OnDestroy 调用同一幂等释放入口；
- 不接管 Mode E/F/G/Zombie 刷怪器，也不改写 WavesArena 波次状态。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

LEASE = os.path.join(REPO_ROOT, "ModeH", "ModeHArenaIsolationLease.cs")
MODULE = os.path.join(REPO_ROOT, "ModeH", "ModeHRuntimeModule.cs")
ENTRY = os.path.join(REPO_ROOT, "ModeH", "ModeHEntry.cs")

FORBIDDEN_LEGACY_WRITES = [
    "DisableAllSpawners(",
    "ClearEnemiesForBossRush(",
    "bossRushArenaActive",
    "currentWave",
    "modeESpawn",
]


def main():
    errors = []

    lease = read_text(LEASE)
    if lease is None:
        print("ModeHArenaIsolationGuard: FAIL (1 errors)")
        print("  - [File] 缺少 ModeH/ModeHArenaIsolationLease.cs")
        return 1
    code = strip_cs_comments(lease)

    checks = [
        (r"public bool TryAcquire\(string sceneName, int sceneGeneration, long ownerToken, out string failureReasonId\)",
         "获取入口带 scene generation 与 owner token"),
        (r"private bool FreezeNativeSpawners\(out string failureReasonId\)", "冻结原图 spawner"),
        (r"private bool ClearNativeEnemies\(out string failureReasonId\)", "清理原生敌人"),
        (r"private bool VerifyArenaBoundaries\(out string failureReasonId\)", "核对场景边界"),
        (r"_frozenOriginalCreated", "保存 spawner 原 created 状态"),
        (r"_frozenOriginalActive", "保存 spawner 原激活状态"),
        (r"private void RollbackTo\(int completedStep\)", "按已完成步骤逆序回滚"),
        (r"public void Release\(int currentSceneGeneration\)", "幂等释放入口带 generation"),
        (r"if \(_released\) return;", "释放幂等守卫"),
        (r"bool sameGeneration = currentSceneGeneration == _sceneGeneration;", "generation 变化时不写旧引用"),
        (r"public bool CheckStillIsolated\(int currentSceneGeneration, out string failureReasonId\)",
         "活动期只检查已登记 spawner"),
        (r"public bool CheckLateSpawners\(out string failureReasonId\)", "晚到 spawner 检查"),
        (r"public bool HasClearedNativeEnemies", "暴露是否已清理原生敌人（决定能否回落 Legacy）"),
        (r"isolation_scene_generation_mismatch", "scene generation 失配 fail-closed"),
        (r"private static bool ShouldPreserveNativeCharacter\(", "原图清场保留角色判定"),
        (r"character\.IsMainCharacter \|\| character\.Team == Teams\.player",
         "玩家与玩家阵营保护"),
        (r"PetNestCompanionAgent\.IsCompanionCharacter\(character\)", "遗种巢随从保护"),
        (r"GetComponentInChildren<INPCController>\(true\)", "NPC/配偶/商人保护"),
        (r"!Team\.IsEnemy\(Teams\.player, character\.Team\)", "非敌对角色保护"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Lease] 不满足: " + desc)

    # 回滚必须逆序：先恢复 spawner，再清空登记
    rollback = re.search(r"private void RollbackTo\(int completedStep\)[\s\S]{0,800}?\n        \}", code)
    if rollback:
        body = rollback.group(0)
        restore_pos = body.find("RestoreSpawners()")
        clear_pos = body.find("_frozenSpawners.Clear()")
        if restore_pos < 0 or clear_pos < 0 or restore_pos > clear_pos:
            errors.append("[Lease] 回滚必须先恢复 spawner 再清空登记")

    # 活动期不得全场扫描
    for scan in ["FindObjectsOfType", "Resources.FindObjectsOfTypeAll"]:
        still = re.search(r"public bool CheckStillIsolated\([\s\S]{0,900}?\n        \}", code)
        if still and scan in still.group(0):
            errors.append("[Lease] 活动期检查不得全场扫描: " + scan)

    # 不得写旧模式状态
    for forbidden in FORBIDDEN_LEGACY_WRITES:
        if forbidden in code:
            errors.append("[Lease] 不得触碰旧模式刷怪/波次状态: " + forbidden)

    module = read_text(MODULE)
    if module is None:
        errors.append("[File] 缺少 ModeH/ModeHRuntimeModule.cs")
    else:
        mcode = strip_cs_comments(module)
        # lease 必须先于 spectator 与生成取得
        acquire_pos = mcode.find("TryAcquire(")
        spectator_pos = mcode.find("ModeHSpectatorLease")
        if acquire_pos >= 0 and spectator_pos >= 0 and spectator_pos < acquire_pos:
            errors.append("[Order] spectator lease 不得先于 arena isolation lease 取得")

    entry = read_text(ENTRY)
    if entry is not None:
        ecode = strip_cs_comments(entry)
        if not re.search(r"internal static void AbortAndRefund\(", ecode):
            errors.append("[Exit] 缺少隔离失败的退款离场入口")
        if not re.search(r"SafeExitFromModeH\(\)", ecode):
            errors.append("[Exit] 缺少安全离场调用")

    if errors:
        print("ModeHArenaIsolationGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHArenaIsolationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
