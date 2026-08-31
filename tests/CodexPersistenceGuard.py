"""Guard: 鸭皇图鉴（Integration/Codex/）的存档管线不变式。

背景：图鉴是**只增不减的收藏进度**，一次错误覆盖等于抹掉玩家几十小时的记录，
而且症状要等玩家下次打开面板才显现，人工冒烟基本抓不到。因此存档侧的
fail-closed 纪律必须由结构守卫钉死。

守卫内容：
  1. 存档整存：SavesSystem.Save<string> + KeyExisits 前置分类，不用 typed Save<T>
     （ES3 会把 assembly-qualified 类型名写进档，mod 程序集改名就读不回来）。
  2. schemaVersion 前置校验 + 写屏障 fail-closed：未知版本 / 不可读 payload
     只读不覆盖，且 Store 必须真的被写屏障挡住。
  3. 写入后回读核对（readback mismatch）。
  4. 槽位烙印：ShutdownSubscription 会退订 OnSetFile，此后在主菜单换档没有任何
     回调，缓存不校验 SavesSystem.CurrentSlot 就会把上一个槽的图鉴写进新档
     （与日报 CR-2026-08-29-017 同面）。
  5. 槽位漂移/切档/删档必须同时复位协调器、采集器与目录三个下游，
     否则会出现「数据换了、去重集与目录没换」。
  6. SavesSystem.SaveFile 只能出现在 CodexSaveCoordinator，且每批至多一次；
     采集器/编解码/目录/面板一律只能入队。
  7. 入队之后必须显式 RequestFlush()：_deferredFlushPending 只在 FlushBatch 里
     置位，而 Tick() 在它为 false 时 O(1) 早返——少了这一行，整条「非基地推迟、
     回基地补写」的链路都是死代码，图鉴只能靠官方存盘顺带带走。
  8. 存档事件订阅必须成对退订且用命名方法（AGENTS.md 4.6，禁 lambda）。
  9. 新增 .cs 必须进编译清单（AGENTS.md 4.1）。图鉴的清单由主控合并两个实现
     agent 的返回值一次加全，因此这里做的是**全有或全无**断言：一条都没登记
     时只告警（等待主控接线），登记了一部分就是真 bug（源码存在但不编译且不报错）。
"""

from pathlib import Path
import re
import sys


CODEX_DIR = Path("Integration/Codex")

TUNING = CODEX_DIR / "CodexTuning.cs"
MODELS = CODEX_DIR / "CodexModels.cs"
CODEC = CODEX_DIR / "CodexCodec.cs"
PERSISTENCE = CODEX_DIR / "CodexPersistence.cs"
COORDINATOR = CODEX_DIR / "CodexSaveCoordinator.cs"
CATALOG = CODEX_DIR / "CodexBossCatalog.cs"
COLLECTOR = CODEX_DIR / "CodexKillCollector.cs"
MILESTONES = CODEX_DIR / "CodexMilestones.cs"
PORTRAITS = CODEX_DIR / "CodexPortraitCache.cs"
VIEW = CODEX_DIR / "CodexView.cs"
VIEW_GRID = CODEX_DIR / "CodexView_Grid.cs"
BOOK_ITEM = CODEX_DIR / "CodexBookItem.cs"
RUNTIME_MODULE = CODEX_DIR / "CodexRuntimeModule.cs"
CONFIG = Path("Config/ConfigCodex.cs")
LOCALIZATION = Path("Localization/CodexLocalization.cs")

COMPILE_LIST = Path("compile_official.bat")

REQUIRED_SOURCES = [
    TUNING, MODELS, CODEC, PERSISTENCE, COORDINATOR, CATALOG, COLLECTOR,
    MILESTONES, PORTRAITS, VIEW, VIEW_GRID, BOOK_ITEM, RUNTIME_MODULE,
    CONFIG, LOCALIZATION,
]

# 除协调器之外，任何图鉴源文件都不得出现物理落盘调用
NO_SAVEFILE_SOURCES = [
    CODEC, PERSISTENCE, CATALOG, COLLECTOR, MILESTONES,
    PORTRAITS, VIEW, VIEW_GRID, RUNTIME_MODULE,
]


def fail(message):
    print("CodexPersistenceGuard: FAIL - " + message)
    return 1


def warn(message):
    print("CodexPersistenceGuard: WARN - " + message)


def strip_comments(text):
    """去掉 // 行注释与 /* */ 块注释。

    本 guard 断言的是**代码**，不是散文：图鉴源码的文件头会成段解释
    「SaveFile 只能出现在协调器」，不剥注释就会把这些解释误判成违规。
    """
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def main():
    for path in REQUIRED_SOURCES:
        if not path.exists():
            return fail("缺少源文件 " + path.as_posix())

    persistence = PERSISTENCE.read_text(encoding="utf-8")
    persistence_code = strip_comments(persistence)
    coordinator = COORDINATOR.read_text(encoding="utf-8")
    coordinator_code = strip_comments(coordinator)
    collector_code = strip_comments(COLLECTOR.read_text(encoding="utf-8"))
    book_code = strip_comments(BOOK_ITEM.read_text(encoding="utf-8"))
    tuning = TUNING.read_text(encoding="utf-8")

    # ---- 1) 存档 key 与 schema 版本是冻结的兼容面 ----
    if 'StorageKey = "BossRush_Codex_v1"' not in tuning:
        return fail(
            "存档 key 必须是 BossRush_Codex_v1（已冻结的存档兼容面，AGENTS.md §5）")
    if "CurrentSchemaVersion = 1" not in tuning:
        return fail("schemaVersion 必须是 1；升版必须同时写迁移路径")

    # ---- 2) 整存 + 前置分类 + 版本校验 + 写屏障 + 回读核对 ----
    for needle, message in (
        ("SavesSystem.Save<string>", "必须 Save<string> 整存 JSON，不用 typed Save<T>"),
        ("SavesSystem.KeyExisits", "必须用 KeyExisits 前置分类新档与老档（官方拼写少一个 t）"),
        ("ReadSchemaVersion", "必须在解码前校验 schemaVersion"),
        ("_writeBarrier = true", "未知版本 / 不可读 payload 必须进写屏障"),
        ("readback mismatch", "写入后必须回读核对"),
    ):
        if needle not in persistence_code:
            return fail(message + " -> " + needle)

    if not re.search(
            r"version\s*!=\s*CodexTuning\.CurrentSchemaVersion", persistence_code):
        return fail(
            "schemaVersion 不等于当前版本时必须 fail-closed（高低版本一律只读不覆盖）")

    # 写屏障必须真的挡住写入，而不只是记一个标志
    if "if (HasWriteBarrier) return false;" not in persistence_code:
        return fail(
            "Store 必须在写屏障时拒写，否则会用空图鉴覆盖掉读不动的存档")

    # ---- 3) 槽位烙印（跨档写污染） ----
    if "SavesSystem.CurrentSlot" not in persistence_code:
        return fail(
            "存档缓存必须记录 SavesSystem.CurrentSlot：ShutdownSubscription 会退订 "
            "OnSetFile，此后在主菜单换档没有任何回调，缓存不校验槽位就会把上一个槽的"
            "图鉴 JSON 写进新档")
    if not re.search(r"_cacheSlot\s*==\s*slot", persistence_code):
        return fail(
            "LoadOrInit 命中缓存时必须比对槽位烙印（_cacheSlot == slot），"
            "不一致要自失效并从新槽重载")

    # ---- 4) 切档 / 删档 / 槽位漂移必须复位三个下游 ----
    for downstream in (
        "CodexSaveCoordinator.NotifySlotChanged",
        "CodexKillCollector.NotifySlotChanged",
        "CodexBossCatalog.NotifySlotChanged",
    ):
        if downstream not in persistence_code:
            return fail(
                "槽位复位必须同时通知 " + downstream
                + "，否则新槽会继承上一个槽的去重集与目录快照")

    # ---- 5) SaveFile 只能在协调器里，且每批至多一次 ----
    for path in NO_SAVEFILE_SOURCES:
        text = strip_comments(path.read_text(encoding="utf-8"))
        if "SaveFile(" in text:
            return fail(
                path.as_posix() + " 不得直接调 SavesSystem.SaveFile，"
                "物理落盘唯一入口是 CodexSaveCoordinator")
        if "SaveGlobal(" in text:
            return fail(
                path.as_posix() + " 不得写全局存档：图鉴是跟槽位的收藏进度，"
                "SaveGlobal 会让所有存档共用一份图鉴")

    if coordinator_code.count("SavesSystem.SaveFile(") != 1:
        return fail("协调器里 SaveFile 必须有且只有一次调用（每批至多一次物理落盘）")

    if "SavesSystem.IsSaving" not in coordinator_code:
        return fail("协调器必须在 IsSaving 时改走 deferred，不得强写")

    if "IsBaseLevelSafe" not in coordinator_code:
        return fail(
            "协调器必须有基地场景闸：SaveFile 会做备份拷贝 + 整档同步写盘，"
            "而图鉴的写入点必然落在交火帧上")

    if "bypassSceneGate" not in coordinator_code:
        return fail(
            "宿主销毁 / 关停必须能绕过场景闸落盘（bypassSceneGate），"
            "否则退出游戏时非基地的 pending 会整批丢掉")

    # ---- 6) 入队之后必须请求落盘，否则 deferred 重试是死代码 ----
    if "CodexSaveCoordinator.RequestFlush" not in collector_code:
        return fail(
            "CodexKillCollector 入队（CodexPersistence.Store）之后必须调 "
            "CodexSaveCoordinator.RequestFlush()：_deferredFlushPending 只在 FlushBatch "
            "里置位，Tick() 在它为 false 时 O(1) 早返，少了这一行整条「非基地推迟、"
            "回基地补写」链路都不会被点着")

    # ---- 7) 存档事件订阅成对退订且用命名方法 ----
    adds = re.findall(r"(SavesSystem\.\w+)\s*\+=\s*(\w+)", persistence_code)
    removes = re.findall(r"(SavesSystem\.\w+)\s*-=\s*(\w+)", persistence_code)
    if not adds:
        return fail("CodexPersistence 必须订阅官方存档事件（OnCollectSaveData/OnSetFile/OnSaveDeleted）")
    if sorted(adds) != sorted(removes):
        return fail(
            "CodexPersistence 的存档事件订阅与退订不成对：订阅 "
            + str(sorted(adds)) + "，退订 " + str(sorted(removes)))
    for event, handler in adds:
        if not handler.startswith("Handle"):
            return fail(
                event + " 必须用命名方法订阅（AGENTS.md 4.6），当前是 " + handler)
    if re.search(r"SavesSystem\.\w+\s*\+=\s*(delegate|\()", persistence_code):
        return fail("静态存档事件禁止用 lambda / 匿名委托订阅，退订时退不掉")

    if "_subscribed" not in persistence_code:
        return fail("存档订阅必须有布尔幂等守卫 _subscribed（AGENTS.md 4.6）")

    # ---- 8) 图鉴实体不可倒卖，商店价格与库存存档必须保持可用 ----
    if 'AddTagToItem(item, "NotSellable")' not in book_code:
        return fail("鸭皇图鉴必须打官方 NotSellable 标签，避免买入后倒卖套利")
    if not re.search(r"float\s+priceFactor\s*=\s*1f", book_code):
        return fail("图鉴 StockShop.priceFactor 必须为 1；写 1/rawValue 会把 4000 金售价压成 1 金")
    if "else if (cachedCodexBookStock >= 0)" not in book_code:
        return fail("商店尚未注入时，存档收集必须保留已读取的售罄库存")

    # ---- 8) 条目上限必须 fail-closed，不得挤掉老条目 ----
    models_code = strip_comments(MODELS.read_text(encoding="utf-8"))
    if not re.search(
            r"Entries\.Count\s*>=\s*CodexTuning\.MaxEntries\)\s*return\s+null",
            models_code):
        return fail(
            "GetOrCreate 达到 MaxEntries 必须返回 null（fail-closed），"
            "绝不能挤掉老条目——老条目就是玩家的收藏进度")

    # ---- 9) 编译清单：全有或全无 ----
    compile_list = COMPILE_LIST.read_text(encoding="utf-8", errors="ignore")
    registered = []
    missing = []
    for path in REQUIRED_SOURCES:
        entry = path.as_posix().replace("/", "\\")
        (registered if entry in compile_list else missing).append(entry)

    if registered and missing:
        return fail(
            "编译清单登记不完整（AGENTS.md 4.1：源码存在但不编译且不报错）。"
            "已登记 " + str(len(registered)) + " 条，缺失: " + ", ".join(missing))
    if not registered:
        warn(
            "图鉴源文件尚未登记进 compile_official.bat（等待主控合并两个实现 agent 的"
            "清单一次加全）。登记后本 guard 会自动转为强制断言。")

    print("CodexPersistenceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
