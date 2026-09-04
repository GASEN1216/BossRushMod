#!/usr/bin/env python3
"""SaveCoordinatorRetryGuard — 落盘协调器的重试链不得被自身消费。

两个协调器都是「pending 合并成一批 -> FlushPending() 交给 SavesSystem ->
SaveFile(false) 物理落盘」。**FlushPending 一旦成功，pending 就被消费了**，
`HasPendingWrite` 随即变 false。

如果 FlushBatch 开头只用 `HasPendingWrite` 判断「有没有事要做」，那么
SaveFile 失败后置起的重试标记在下一帧会命中这个早返、直接 return true，
于是 Tick 重试与宿主销毁兜底**一起失效**——数据停在 SavesSystem 内存里从不落盘，
玩家侧表现是「进度悄悄回退」。这与两个文件里关于 deferred 重试的注释承诺相反。

因此要求：把「欠一次 SaveFile」独立成 `_saveFilePending`，早返条件必须同时看它，
且只有 SaveFile 真正成功才清除。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TARGETS = [
    (os.path.join(ROOT, "Campaign", "CampaignSaveCoordinator.cs"),
     "CampaignPersistence", "CampaignSaveCoordinator"),
    (os.path.join(ROOT, "Integration", "Codex", "CodexSaveCoordinator.cs"),
     "CodexPersistence", "CodexSaveCoordinator"),
]


def read(path):
    if not os.path.isfile(path):
        return None
    with io.open(path, "r", encoding="utf-8", errors="ignore") as fh:
        return fh.read()


def main():
    errors = []

    for path, persistence, name in TARGETS:
        src = read(path)
        if src is None:
            errors.append("[File] 缺少 " + name + ".cs")
            continue

        if "private static bool _saveFilePending;" not in src:
            errors.append(
                "[" + name + "] 必须用独立字段记录「欠一次 SaveFile」，"
                "只看 HasPendingWrite 会让重试链被自身消费")
            continue

        # 早返条件必须同时看两者
        early = re.search(
            r"if \(!" + persistence + r"\.HasPendingWrite[^)]*\)", src)
        if early is None:
            errors.append("[" + name + "] 找不到 HasPendingWrite 早返条件")
        elif "saveFileOwed" not in early.group(0):
            errors.append(
                "[" + name + "] 早返条件必须同时排除「欠一次 SaveFile」的情况")

        # FlushPending 必须只在真有 pending 时调用，且成功后置位
        m = re.search(r"if \(" + persistence + r"\.HasPendingWrite\)\s*\{", src)
        if m is None:
            errors.append(
                "[" + name + "] FlushPending 必须包在 HasPendingWrite 判断内"
                "（无 pending 但欠落盘时不应再调它）")
        if "_saveFilePending = true;" not in src:
            errors.append("[" + name + "] FlushPending 成功后必须置位 _saveFilePending")

        # 只有落盘成功才清；切档与静态复位也要清
        if src.count("_saveFilePending = false;") < 3:
            errors.append(
                "[" + name + "] _saveFilePending 至少要在「落盘成功 / 切档 / 静态复位」三处清除")

        # 清除点必须在 SaveFile 之后，而不是 FlushPending 之后
        save_idx = src.find("SavesSystem.SaveFile(false);")
        clear_idx = src.find("_saveFilePending = false;", save_idx if save_idx >= 0 else 0)
        if save_idx < 0:
            errors.append("[" + name + "] 找不到 SavesSystem.SaveFile 调用")
        elif clear_idx < 0:
            errors.append("[" + name + "] SaveFile 成功后必须清除 _saveFilePending")

    if errors:
        print("SaveCoordinatorRetryGuard: FAIL ({0} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("SaveCoordinatorRetryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
