#!/usr/bin/env python3
"""征程首次读档与初始化必须等待官方关卡就绪；同槽已接受事实保留给退出保存。"""
from pathlib import Path
import re
import sys
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def body(path, signature):
    source = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
    assert source.count(signature) == 1, f"{path}: 签名不唯一 {signature}"
    start = source.index("{", source.index(signature)) + 1
    end, depth = start, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return re.sub(r"\s+", " ", source[start:end - 1]).strip()


def main():
    persistence = "Campaign/CampaignPersistence.cs"
    ready = body(persistence, "internal static bool IsCurrentSlotReady")
    for expression in ("!SceneLoader.IsSceneLoading", "!LevelManager.LevelInitializing", "LevelManager.LevelInited"):
        assert expression in ready, "CampaignPersistence: 首次读取缺少官方就绪门 " + expression
    load = body(persistence, "internal static CampaignSaveData LoadOrInit()")
    gate = "if (_loadedSlot != slot && !IsCurrentSlotReady) return null;"
    assert gate in load, "CampaignPersistence: 首次读当前槽必须等就绪，同槽已加载事实不得清空"
    assert load.index(gate) < load.index("_store.LoadOrInit();") < load.index("_loadedSlot = slot;"), \
        "CampaignPersistence: 读档顺序必须为就绪门、加载、成功烙印"
    for signature in ("private static void NotifySlotChangedDownstream()", "internal static void ResetStaticCaches()"):
        assert "_loadedSlot = int.MinValue;" in body(persistence, signature), \
            "CampaignPersistence: 切槽与重建必须清除读取烙印"
    assert "return IsCurrentSlotReady && _store.Store(value);" in body(persistence, "internal static bool Store("), \
        "CampaignPersistence: 关卡未就绪时不得接受新写入"
    assert "return _store.FlushPending();" in body(persistence, "internal static bool FlushPending()"), \
        "CampaignPersistence: 已接受 pending 的最终采集不能被关卡就绪门拦住"
    initialize = body("Campaign/CampaignProgressService.cs", "internal static void EnsureInitialized()")
    assert initialize.index("CampaignSaveData data = CampaignPersistence.Current;") \
        < initialize.index("if (data == null) return;") \
        < initialize.index("CampaignPersistence.PublishTokensToUnlockContract(data);") \
        < initialize.index("_initialized = true;"), "CampaignProgressService: 未读到当前槽前不得锁定初始化成功"
    bootstrap = body("Campaign/CampaignRuntimeModule.cs", "internal void EnsureBootstrapped()")
    assert bootstrap.index("CampaignSaveCoordinator.EnsureSubscribed();") \
        < bootstrap.index("if (!CampaignPersistence.IsCurrentSlotReady) return;") \
        < bootstrap.index("CampaignProgressService.EnsureInitialized();") \
        < bootstrap.index("_bootstrapped = true;"), "CampaignRuntimeModule: 先监听换槽、等就绪、再读取并发布任务"
    note = body("Campaign/CampaignNoteBridge.cs", "private static void RegisterNote(")
    assert note.index("if (!CampaignPersistence.IsCurrentSlotReady) return;") \
        < note.index("bool ours = CampaignProgressService.IsClueUnlocked(def.ClueId);"), \
        "CampaignNoteBridge: 关卡加载时不得用未读取的默认事实反锁官方线索镜像"
    print("CampaignPersistenceReadinessGuard: PASS")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (AssertionError, ValueError) as error:
        print("CampaignPersistenceReadinessGuard: FAIL: " + str(error))
        sys.exit(1)
