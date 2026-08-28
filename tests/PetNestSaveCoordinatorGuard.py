#!/usr/bin/env python3
"""
PetNestSaveCoordinatorGuard — 遗种巢落盘协调器守卫（实施计划 步骤 2）。

不变式：
- PetNestSaveCoordinator 是遗种巢**唯一**调用 SavesSystem.SaveFile 的地方，
  且整个 PetNest/ 目录只有它一处；
- 每批至多一次 SaveFile；
- SavesSystem.IsSaving 时不强写，只登记 deferred；
- deferred 重试有预算上限，超预算不静默丢弃 pending；
- 切档/删档有 NotifySlotChanged 清 deferred 状态；
- 宿主销毁有 TryFlushOnHostDestroy 且 no-throw；
- 宿主 OnDestroy 路径实际接线了落盘与静态复位。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import (  # noqa: E402
    PETNEST_DIR,
    read_petnest,
    read_text,
    repo_path,
    report,
    strip_cs_comments,
)

GUARD = "PetNestSaveCoordinatorGuard"
COORDINATOR_FILE = "PetNestSaveCoordinator.cs"


def main():
    errors = []

    text = read_petnest(COORDINATOR_FILE)
    if text is None:
        return report(GUARD, ["[File] 缺少 PetNest/" + COORDINATOR_FILE])
    code = strip_cs_comments(text)

    # 1. 唯一 SaveFile 触达点：本文件恰好一次
    save_file_calls = len(re.findall(r"SavesSystem\.SaveFile\(", code))
    if save_file_calls == 0:
        errors.append("[唯一写点] 协调器必须包含唯一的 SavesSystem.SaveFile 调用")
    elif save_file_calls > 1:
        errors.append("[唯一写点] 每批至多一次 SaveFile，当前有 " + str(save_file_calls) + " 处")

    # 2. PetNest 其余文件一律不得调 SaveFile
    for name in sorted(os.listdir(PETNEST_DIR)):
        if not name.endswith(".cs") or name == COORDINATOR_FILE:
            continue
        other = read_text(os.path.join(PETNEST_DIR, name))
        if other is None:
            continue
        if "SavesSystem.SaveFile" in strip_cs_comments(other):
            errors.append("[唯一写点] " + name + " 不得直接调用 SavesSystem.SaveFile")

    # 3. IsSaving 时不强写
    if not re.search(r"if \(SavesSystem\.IsSaving\)", code):
        errors.append("[并发] 必须在 IsSaving 时改走 deferred，不强写")
    if not re.search(r'error = "flush_deferred_is_saving";', code):
        errors.append("[并发] deferred 必须给出可诊断的原因串")

    # 4. deferred 预算
    if not re.search(r"private const int MaxDeferredRetries", code):
        errors.append("[预算] deferred 重试必须有上限常量")
    if not re.search(r'_lastError = "flush_deferred_budget_exhausted";', code):
        errors.append("[预算] 超预算必须报告失败，不得静默丢弃 pending")

    # 5. 切档 / 宿主销毁入口
    if not re.search(r"internal static void NotifySlotChanged\(\)", code):
        errors.append("[生命周期] 缺少 NotifySlotChanged() 切档清理入口")
    if not re.search(r"internal static bool TryFlushOnHostDestroy\(\)", code):
        errors.append("[生命周期] 缺少 TryFlushOnHostDestroy() 宿主销毁入口")
    destroy = re.search(r"internal static bool TryFlushOnHostDestroy\(\)[\s\S]{0,400}?\n        \}", code)
    if destroy and "catch (Exception)" not in destroy.group(0):
        errors.append("[生命周期] TryFlushOnHostDestroy 必须 no-throw")

    # 6. Tick 未 deferred 时零成本早返
    if not re.search(r"if \(!_deferredFlushPending\) return;", code):
        errors.append("[性能] Tick 未 deferred 时必须 O(1) 早返")

    # 7. 宿主 OnDestroy 实际接线
    host = read_text(repo_path("ModBehaviour.cs"))
    if host is None:
        errors.append("[File] 缺少 ModBehaviour.cs")
    else:
        hcode = strip_cs_comments(host)
        for call in ["PetNestSaveCoordinator.TryFlushOnHostDestroy()",
                     "PetNestPersistence.ResetStaticCaches()",
                     "PetNestSaveCoordinator.ResetStaticCaches()"]:
            if call not in hcode:
                errors.append("[接线] 宿主销毁路径缺少: " + call)

    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
