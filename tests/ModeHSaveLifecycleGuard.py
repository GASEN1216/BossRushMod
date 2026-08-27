#!/usr/bin/env python3
"""
ModeHSaveLifecycleGuard — Mode H 存档生命周期守卫（设计提案 §20.3、§26.1）。

不变式：
- OnCollectSaveData / OnSetFile / OnSaveDeleted 三个事件都有幂等订阅与对称退订；
- 订阅使用命名处理器（不能用匿名 lambda，否则无法退订）；
- OnSetFile 后立即执行 slot 风险扫描；
- 删档清空 H cache、pending、write barrier、owner、recovery 与 slotGeneration；
- SavesSystem.SaveFile 的调用点唯一，且只在 ModeHSaveFlushCoordinator 内；
- 存档收集路径不得抛出（try/catch 包裹）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
SEASON = os.path.join(MODEH_DIR, "ModeHProfilePersistence.cs")
HALL = os.path.join(MODEH_DIR, "ModeHHallOfFamePersistence.cs")
COORDINATOR = os.path.join(MODEH_DIR, "ModeHSaveFlushCoordinator.cs")
GATES = os.path.join(MODEH_DIR, "ModeHRuntimeGates.cs")

EVENTS = ["OnCollectSaveData", "OnSetFile", "OnSaveDeleted"]
HANDLERS = {
    "OnCollectSaveData": "HandleCollectSaveData",
    "OnSetFile": "HandleSetFile",
    "OnSaveDeleted": "HandleSaveDeleted",
}


def check_persistence(path, label, errors):
    text = read_text(path)
    if text is None:
        errors.append("[File] 缺少 " + os.path.relpath(path, REPO_ROOT))
        return ""
    code = strip_cs_comments(text)

    if not re.search(r"public static void EnsureSubscribed\(\)", code):
        errors.append("[{}] 缺少幂等订阅入口".format(label))
    if not re.search(r"public static void ShutdownSubscription\(\)", code):
        errors.append("[{}] 缺少退订入口".format(label))
    if not re.search(r"if \(_subscribed\) return;", code):
        errors.append("[{}] 订阅缺少幂等守卫".format(label))
    if not re.search(r"if \(!_subscribed\) return;", code):
        errors.append("[{}] 退订缺少幂等守卫".format(label))

    for event in EVENTS:
        handler = HANDLERS[event]
        if not re.search(r"SavesSystem\.{} \+= {};".format(event, handler), code):
            errors.append("[{}] 缺少命名处理器订阅: {}".format(label, event))
        if not re.search(r"SavesSystem\.{} -= {};".format(event, handler), code):
            errors.append("[{}] 缺少对称退订: {}".format(label, event))

    if re.search(r"SavesSystem\.On\w+ \+= \(", code):
        errors.append("[{}] 禁止用匿名委托订阅存档事件".format(label))

    collect = re.search(r"private static void HandleCollectSaveData\(\)[\s\S]{0,400}?\n        \}", code)
    if not collect or "try" not in collect.group(0) or "catch" not in collect.group(0):
        errors.append("[{}] 存档收集路径必须 no-throw".format(label))

    return code


def main():
    errors = []

    season_code = check_persistence(SEASON, "Season", errors)
    hall_code = check_persistence(HALL, "HallOfFame", errors)

    if season_code:
        if not re.search(r"ModeHRuntimeGates\.InitializeRiskForSlot\(_slotGeneration\);", season_code):
            errors.append("[Season] OnSetFile 后必须立即执行 slot 风险扫描")
        set_file = re.search(r"private static void HandleSetFile\(\)[\s\S]{0,900}?\n        \}", season_code)
        if set_file:
            body = set_file.group(0)
            for field in ["_cache = null;", "_pending = null;", "_writeBarrier = false;", "_slotGeneration++;"]:
                if field not in body:
                    errors.append("[Season] OnSetFile 未清空: " + field)
        deleted = re.search(r"private static void HandleSaveDeleted\(\)[\s\S]{0,900}?\n        \}", season_code)
        if deleted:
            body = deleted.group(0)
            for field in ["_cache = null;", "_pending = null;", "_slotGeneration++;",
                          "ModeHRuntimeGates.ResetForSlotChange();"]:
                if field not in body:
                    errors.append("[Season] 删档未清空: " + field)

    if hall_code:
        deleted = re.search(r"private static void HandleSaveDeleted\(\)[\s\S]{0,900}?\n        \}", hall_code)
        if deleted:
            body = deleted.group(0)
            for field in ["_cache = null;", "_pending = null;", "_writeBarrier = false;"]:
                if field not in body:
                    errors.append("[HallOfFame] 删档未清空: " + field)

    gates = read_text(GATES)
    if gates is None:
        errors.append("[File] 缺少 ModeH/ModeHRuntimeGates.cs")
    else:
        code = strip_cs_comments(gates)
        if not re.search(r"public static void ResetForSlotChange\(\)", code):
            errors.append("[Gates] 缺少槽位切换重置入口")
        reset = re.search(r"public static void ResetForSlotChange\(\)[\s\S]{0,700}?\n        \}", code)
        if reset:
            body = reset.group(0)
            for field in ["_riskScanReady = false;", "_contentReady = false;",
                          "_recoveryOnlyBlocked = false;", "_runOwnerActive = false;"]:
                if field not in body:
                    errors.append("[Gates] 槽位切换未重置: " + field)
        if not re.search(r"_slotGeneration = 0;", code):
            errors.append("[Gates] ResetStaticCaches 未清空 slotGeneration")

    # SaveFile 调用点唯一，且只在 coordinator
    save_file_files = []
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        code = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        count = len(re.findall(r"SavesSystem\.SaveFile\(", code))
        if count:
            save_file_files.append((name, count))
    if save_file_files != [("ModeHSaveFlushCoordinator.cs", 1)]:
        errors.append("[Flush] SavesSystem.SaveFile 必须只在 ModeHSaveFlushCoordinator 出现一次，实际: "
                      + str(save_file_files))

    coordinator = read_text(COORDINATOR)
    if coordinator is None:
        errors.append("[File] 缺少 ModeH/ModeHSaveFlushCoordinator.cs")
    else:
        code = strip_cs_comments(coordinator)
        if not re.search(r"if \(SavesSystem\.IsSaving\)", code):
            errors.append("[Flush] IsSaving 时必须推迟而不是强写")
        if not re.search(r"public static void ResetStaticCaches\(\)", code):
            errors.append("[Flush] 缺少静态缓存清理入口")
        if not re.search(r"public static bool TryFlushOnHostDestroy\(\)", code):
            errors.append("[Flush] 缺少宿主销毁时的尽力提交入口")

    if errors:
        print("ModeHSaveLifecycleGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHSaveLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
