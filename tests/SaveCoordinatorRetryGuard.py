#!/usr/bin/env python3
"""SaveCoordinatorRetryGuard — 落盘协调器的重试链不得被自身消费。

协调器都是「pending 合并成一批 -> FlushPending() 交给 SavesSystem ->
SaveFile(false) 物理落盘」。**FlushPending 一旦成功，pending 就被消费了**，
`HasPendingWrite` 随即变 false。

如果 FlushBatch 开头只用 `HasPendingWrite` 判断「有没有事要做」，那么
SaveFile 失败后置起的重试标记在下一帧会命中这个早返、直接 return true，
于是 Tick 重试与宿主销毁兜底**一起失效**——数据停在 SavesSystem 内存里从不落盘，
玩家侧表现是「进度悄悄回退」。

因此要求：把「欠一次 SaveFile」独立成 `_saveFilePending`，早返条件必须同时看它，
且只有 SaveFile 真正成功才清除。

2026-09-06（D-2）起状态机只有一份：Common/Lifecycle/BossRushSaveCoordinatorEngine.cs。
征程 / 图鉴 / 日报 / 遗种巢四个协调器退化为门面，各自持有一个引擎实例；
本守卫把不变式钉在引擎上，并断言四个门面确实经引擎落盘、目录内零处 SaveFile 直调。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

ENGINE = os.path.join(ROOT, "Common", "Lifecycle", "BossRushSaveCoordinatorEngine.cs")

# (门面文件, 子系统目录, 协调器名)
FACADES = [
    (os.path.join(ROOT, "Campaign", "CampaignSaveCoordinator.cs"),
     os.path.join(ROOT, "Campaign"), "CampaignSaveCoordinator"),
    (os.path.join(ROOT, "Integration", "Codex", "CodexSaveCoordinator.cs"),
     os.path.join(ROOT, "Integration", "Codex"), "CodexSaveCoordinator"),
    (os.path.join(ROOT, "Integration", "DailyReport", "DailyReportSaveCoordinator.cs"),
     os.path.join(ROOT, "Integration", "DailyReport"), "DailyReportSaveCoordinator"),
    (os.path.join(ROOT, "PetNest", "PetNestSaveCoordinator.cs"),
     os.path.join(ROOT, "PetNest"), "PetNestSaveCoordinator"),
]


def read(path):
    if not os.path.isfile(path):
        return None
    with io.open(path, "r", encoding="utf-8", errors="ignore") as fh:
        return fh.read()


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def check_engine(errors):
    src = read(ENGINE)
    if src is None:
        errors.append("[File] 缺少 Common/Lifecycle/BossRushSaveCoordinatorEngine.cs")
        return
    code = strip_comments(src)

    if "private bool _saveFilePending;" not in code:
        errors.append("[Engine] 必须用独立字段记录「欠一次 SaveFile」，只看 HasPendingWrite 会让重试链被自身消费")
        return

    # 早返条件必须同时看 typed pending、欠账位与快照义务
    early = re.search(r"if \(!_source\.HasPendingWrite[^)]*\)", code)
    if early is None:
        errors.append("[Engine] 找不到 HasPendingWrite 早返条件")
    else:
        if "saveFileOwed" not in early.group(0):
            errors.append("[Engine] 早返条件必须同时排除「欠一次 SaveFile」的情况")
        if "HasSnapshotObligation" not in early.group(0):
            errors.append("[Engine] 早返条件必须同时排除「欠一次快照采集」的情况（现金 / 实物义务不得被 pending 消费掉）")

    # FlushPending 必须只在真有 pending 时调用，且成功后置位
    if re.search(r"if \(_source\.HasPendingWrite\)\s*\{", code) is None:
        errors.append("[Engine] FlushPending 必须包在 HasPendingWrite 判断内（无 pending 但欠落盘时不应再调它）")
    if "_saveFilePending = true;" not in code:
        errors.append("[Engine] FlushPending 成功后必须置位 _saveFilePending")

    # 只有落盘成功才清；切档（Reset 复用切档）也要清
    if code.count("_saveFilePending = false;") < 2:
        errors.append("[Engine] _saveFilePending 至少要在「落盘成功 / 切档」两处清除")
    reset = re.search(r"internal void Reset\(\)\s*\{[\s\S]*?\n        \}", code)
    if reset is None or "NotifySlotChanged();" not in reset.group(0):
        errors.append("[Engine] Reset() 必须复用 NotifySlotChanged() 清欠账位")

    # 唯一物理写点，且清除点必须在 SaveFile 之后
    if code.count("SavesSystem.SaveFile(") != 1:
        errors.append("[Engine] 引擎里 SaveFile 必须有且只有一次调用（每批至多一次物理落盘）")
    save_idx = code.find("SavesSystem.SaveFile(false);")
    clear_idx = code.find("_saveFilePending = false;", save_idx if save_idx >= 0 else 0)
    if save_idx < 0:
        errors.append("[Engine] 找不到 SavesSystem.SaveFile 调用")
    elif clear_idx < 0:
        errors.append("[Engine] SaveFile 成功后必须清除 _saveFilePending")

    # 快照义务只在物理落盘成功后才交还数据源清除
    if code.find("_source.OnPhysicalSaveSucceeded();") < save_idx:
        errors.append("[Engine] OnPhysicalSaveSucceeded 必须在 SaveFile 成功之后才回调")


def check_facades(errors):
    for path, directory, name in FACADES:
        src = read(path)
        if src is None:
            errors.append("[File] 缺少 " + name + ".cs")
            continue
        code = strip_comments(src)
        if "BossRushSaveCoordinatorEngine" not in code:
            errors.append("[" + name + "] 必须经共享引擎 BossRushSaveCoordinatorEngine 落盘，不得自带第二份状态机")
        if "IBossRushSaveBatchSource" not in code:
            errors.append("[" + name + "] 必须实现 IBossRushSaveBatchSource 数据源")
        if "SavesSystem.SaveFile(" in code:
            errors.append("[" + name + "] 门面不得直接调用 SavesSystem.SaveFile（唯一写点在引擎）")

        # 子系统目录内零处直调
        for fn in sorted(os.listdir(directory)):
            if not fn.endswith(".cs"):
                continue
            other = read(os.path.join(directory, fn))
            if other is None:
                continue
            if "SavesSystem.SaveFile(" in strip_comments(other):
                errors.append("[" + name + "] " + fn + " 不得直接调用 SavesSystem.SaveFile")


def main():
    errors = []
    check_engine(errors)
    check_facades(errors)

    if errors:
        print("SaveCoordinatorRetryGuard: FAIL ({0} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("SaveCoordinatorRetryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
