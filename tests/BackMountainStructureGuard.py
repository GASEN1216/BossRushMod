"""Guard: 竞技场后山的单实例纪律、dormant 契约与解锁读侧的「不缓存」不变式。

背景：后山是「战役 → 后山」进度线的下游，全部三个设施（菜地/展示柜/点唱机）
的可见性都取决于它怎么读战役的 token。这里守两条最容易写错的：

  1. **解锁状态不得缓存**。战役侧按契约在**读档装载 token 时不发事件**
     （那不是新授予）。若后山把查询结果缓存下来，读档后就永远停在
     「启动瞬间的答案」上——玩家上次通关解锁的菜地，重进游戏后消失。
     正确做法是每次现查（O(1) 哈希），并在每次场景加载时重新评估。

  2. **订阅必须幂等且退订**（AGENTS.md 4.6）。事件是静态的，
     重复订阅会让一次解锁触发多次注入；不退订则在 dormant 后仍会响应。

另外总开关运行时可变，关掉后必须整体 dormant：不注入作物数据、不注册建筑、
不追加点唱机曲目。关闭状态下每帧 O(1) 早返（AGENTS.md 4.12）。
"""

from pathlib import Path
import re
import sys

MODULE = Path("Integration/BackMountain/BackMountainRuntimeModule.cs")
SHOWCASE = Path("Integration/BackMountain/ShowcaseService.cs")
SHOWCASE_JUDGES = Path("Integration/BackMountain/ShowcaseDisplayJudges.cs")
SHOWCASE_SCANNER = Path("Integration/BackMountain/ShowcaseDisplayScanner.cs")
SHOWCASE_BUILDER = Path("Integration/BackMountain/ShowcaseBuildingBuilder.cs")
GARDEN_JUDGES = Path("Integration/BackMountain/GardenSiteJudges.cs")
GARDEN_SITE = Path("Integration/BackMountain/GardenConstructionSite.cs")
JUKEBOX = Path("Integration/BackMountain/JukeboxTrackInjector.cs")
BGM_TABLE = Path("Audio/BossBgmTrackTable.cs")
RAID_MEAL = Path("Integration/BackMountain/RaidMealService.cs")
RAID_MEAL_USE = Path("Integration/BackMountain/RaidMealUsageBehavior.cs")
UNLOCKS = Path("Integration/BackMountain/BackMountainUnlocks.cs")
CONFIG_CONST = Path("Integration/BackMountain/BackMountainConfig.cs")
REGISTRATION = Path("Common/Lifecycle/BossRushRuntimeModuleRegistration.cs")
SCENE = Path("Integration/BossRushIntegration_StartAndScene.cs")
# 开关接线散在 Config.cs 与提取出去的白名单文件里（同一 partial 类，
# 拆分只为 LargeFileBudgetGuard 的 1200 行预算），断言时合并来看。
CONFIG_SOURCES = [
    Path("Config/Config.cs"),
    Path("Config/ConfigModConfigKeys.cs"),
    Path("Config/ConfigBackMountain.cs"),
    Path("Config/ConfigContentSystemSwitches.cs"),
]

# 冻结契约：常量名 -> 必须保持的字面值（进存档键与官方建筑/作物 ID）
FROZEN = {
    "ShowcaseBuildingId": '"bossrush_backmountain_showcase"',
    "ShowcaseSaveKey": '"BossRush_BackMountain_Showcase_v1"',
    "RaidMealSaveKey": '"BossRush_BackMountain_RaidMeal_v1"',
    "CropIdPrefix": '"BossRush_Crop_"',
}


def fail(message):
    print("BackMountainStructureGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def main():
    for path in [MODULE, SHOWCASE, SHOWCASE_JUDGES, SHOWCASE_SCANNER, SHOWCASE_BUILDER, GARDEN_JUDGES, GARDEN_SITE,
                 JUKEBOX, BGM_TABLE, RAID_MEAL, RAID_MEAL_USE, UNLOCKS, CONFIG_CONST, REGISTRATION, SCENE] + CONFIG_SOURCES:
        if not path.is_file():
            return fail("找不到 " + path.as_posix())

    module = strip_comments(MODULE.read_text(encoding="utf-8", errors="ignore"))
    unlocks = strip_comments(UNLOCKS.read_text(encoding="utf-8", errors="ignore"))
    consts = strip_comments(CONFIG_CONST.read_text(encoding="utf-8", errors="ignore"))
    reg = strip_comments(REGISTRATION.read_text(encoding="utf-8", errors="ignore"))
    config = strip_comments("\n".join(
        path.read_text(encoding="utf-8", errors="ignore") for path in CONFIG_SOURCES))
    showcase = strip_comments(SHOWCASE.read_text(encoding="utf-8", errors="ignore"))
    showcase_scanner = strip_comments(SHOWCASE_SCANNER.read_text(encoding="utf-8", errors="ignore"))
    showcase_builder = strip_comments(SHOWCASE_BUILDER.read_text(encoding="utf-8", errors="ignore"))
    garden_judges = strip_comments(GARDEN_JUDGES.read_text(encoding="utf-8", errors="ignore"))
    garden_site = strip_comments(GARDEN_SITE.read_text(encoding="utf-8", errors="ignore"))
    jukebox = strip_comments(JUKEBOX.read_text(encoding="utf-8", errors="ignore"))
    bgm_table = strip_comments(BGM_TABLE.read_text(encoding="utf-8", errors="ignore"))
    raid_meal = strip_comments(RAID_MEAL.read_text(encoding="utf-8", errors="ignore"))
    raid_use = strip_comments(RAID_MEAL_USE.read_text(encoding="utf-8", errors="ignore"))

    # ---- 1) 单实例纪律 ----
    if not re.search(r"backMountainRuntime\s*=\s*new\s+BackMountainRuntimeModule\s*\(", reg):
        return fail(
            REGISTRATION.as_posix() + " 没有把实例先存进字段。"
            "必须「先存字段、再注册同一引用」。")
    if not re.search(r"Register\s*\(\s*backMountainRuntime\s*\)", reg):
        return fail(REGISTRATION.as_posix() + " 注册给 host 的不是字段里那份实例。")
    if not re.search(r"BackMountainRuntimeModule\s+BackMountainRuntime\s*\{\s*get", reg):
        return fail(REGISTRATION.as_posix() + " 缺少只读门面 BackMountainRuntime")

    news = []
    for path in Path(".").rglob("*.cs"):
        if any(part in {".git", "Build", "tmp", "output", "outputs", "鸭科夫源码"} for part in path.parts):
            continue
        text = strip_comments(path.read_text(encoding="utf-8", errors="ignore"))
        news += [path.as_posix()] * len(
            re.findall(r"new\s+BackMountainRuntimeModule\s*\(", text))
    if len(news) != 1:
        return fail(
            "BackMountainRuntimeModule 被 new 了 " + str(len(news)) + " 次（"
            + ", ".join(sorted(set(news))) + "）。单实例纪律要求全仓库只有一次。")

    # 注册顺序：后山 bootstrap 要订阅征程事件，征程必须先注册（host 按序回调）
    campaign_at = reg.find("campaignRuntime = new CampaignRuntimeModule")
    backmountain_at = reg.find("backMountainRuntime = new BackMountainRuntimeModule")
    if campaign_at < 0 or backmountain_at < 0 or campaign_at > backmountain_at:
        return fail(
            REGISTRATION.as_posix() + " 的注册顺序不对：后山必须排在征程之后。"
            "host 按注册顺序回调，后山 bootstrap 要订阅征程的解锁事件。")

    # ---- 2) dormant 契约 ----
    if "EnsureBootstrapped" not in module:
        return fail(MODULE.as_posix() + " 缺少幂等 bootstrap 入口 EnsureBootstrapped")
    if not re.search(r"ShutdownIfEnabledTurnedOff\s*\(", module):
        return fail(MODULE.as_posix() + " 缺少开关关闭时的 shutdown 回落。")
    if "IsBackMountainConfiguredEnabled" not in module:
        return fail(
            MODULE.as_posix() + " 没有经唯一只读入口 IsBackMountainConfiguredEnabled 读开关")

    update = re.search(
        r"public\s+override\s+void\s+OnUpdate\s*\([^)]*\)\s*\{(.*?)\n        \}",
        module, flags=re.S)
    if update and re.search(r"DevLog|Debug\.Log", update.group(1)):
        return fail(MODULE.as_posix() + " 的 OnUpdate 里有日志调用（每帧路径）。")

    # ---- 3) 场景加载时必须重新评估解锁（读档不发事件，只信事件会漏历史解锁）----
    scene = re.search(
        r"public\s+override\s+void\s+OnSceneLoaded\s*\([^)]*\)\s*\{(.*?)\n        \}",
        module, flags=re.S)
    if not scene:
        return fail(MODULE.as_posix() + " 找不到 OnSceneLoaded 方法体")
    if "RefreshFacilitiesForScene" not in scene.group(1):
        return fail(
            MODULE.as_posix() + " 的 OnSceneLoaded 没有重新评估设施解锁。"
            "战役读档灌入 token 时按契约不发事件，只靠事件会漏掉全部历史解锁——"
            "表现为「上次通关解锁的设施，重进游戏后消失」。")

    # ---- 4) 解锁读侧：不缓存 + 订阅幂等 + 退订 ----
    if not re.search(r"_subscribed", unlocks):
        return fail(UNLOCKS.as_posix() + " 缺少订阅幂等标记 _subscribed（AGENTS.md 4.6）")
    if not re.search(r"OnFacilityTokenGranted\s*-=", unlocks):
        return fail(
            UNLOCKS.as_posix() + " 没有退订战役事件。"
            "静态事件不退订会在 dormant 后继续响应，并在宿主销毁后留下死引用。")
    if not re.search(r"OnFacilityTokenGranted\s*\+=", unlocks):
        return fail(UNLOCKS.as_posix() + " 没有订阅战役事件")

    is_unlocked = re.search(
        r"internal\s+static\s+bool\s+IsFacilityUnlocked\s*\([^)]*\)\s*\{(.*?)\n        \}",
        unlocks, flags=re.S)
    if not is_unlocked:
        return fail(UNLOCKS.as_posix() + " 找不到 IsFacilityUnlocked 方法体")
    body = is_unlocked.group(1)
    if "CampaignFacilityUnlocks.IsTokenGranted" not in body:
        return fail(
            UNLOCKS.as_posix() + " 的 IsFacilityUnlocked 没有现查战役契约。"
            "解锁状态不得缓存：读档装载 token 时不发事件，缓存会永久停在启动瞬间的答案。")
    if re.search(r"_unlockedCache|_cachedUnlock|_facilityCache", unlocks):
        return fail(
            UNLOCKS.as_posix() + " 出现了解锁状态缓存字段。"
            "必须每次现查（O(1) 哈希），否则读档后的历史解锁会永久丢失。")

    # ---- 5) 冻结常量 ----
    for name, literal in FROZEN.items():
        pattern = r"\b" + name + r"\s*=\s*" + re.escape(literal)
        if not re.search(pattern, consts):
            return fail(
                CONFIG_CONST.as_posix() + " 的冻结常量 " + name + " 不再等于 " + literal
                + "。它进存档键或官方建筑/作物 ID，改名会让老档静默失联。")

    # ---- 6) 正式构建隐藏并禁用旧调试旁路，配置字段仍兼容旧文件 ----
    if not re.search(r"public bool backMountainEnabled\s*=\s*true\s*;", config):
        return fail("backMountainEnabled 必须默认 true")
    from cs_source_util import clean_source
    release = clean_source(Path("Config/ConfigBackMountain.cs").read_text(encoding="utf-8"))
    if "RegisterBackMountainModConfigOptions" in config or "BackMountainUnlockAllModConfigKeySuffix" in config:
        return fail("后山调试旁路不得再注册或进入单键白名单")
    if not re.search(r"#if BOSSRUSH_DEV\s+return config != null && config.backMountainUnlockAll;\s+#else\s+return false;\s+#endif", release):
        return fail("正式构建必须忽略历史 backMountainUnlockAll=true")

    # ---- 7) 登记/餐食写入必须可证实，失败不能静默报告成功 ----
    if not re.search(r"private static bool Store\(\)", showcase):
        return fail("展示柜 Store 必须返回 bool，让上层在写失败时回滚内存登记")
    if "showcase save readback mismatch" not in showcase:
        return fail("展示柜陈列快照必须回读核对")
    if "SavesSystem.Save<string>(BackMountainConfig.ShowcaseSaveKey, previousJson)" not in showcase:
        return fail("展示柜 Save 后回读失败必须还原官方缓存，不能只还原内存列表")

    # ---- 7a) 2026-09-22 陈列改接官方枪械展示架 / 假人：快照覆盖、写失败恢复、SCHEMA+ 不升版、扫描订阅纪律、自建柜退役 ----
    apply = re.search(r"internal\s+static\s+bool\s+ApplyDisplaySnapshot\s*\([^)]*\)\s*\{(.*?)\n        \}", showcase, flags=re.S)
    if not apply:
        return fail(SHOWCASE.as_posix() + " 缺 ApplyDisplaySnapshot：陈列必须由官方柜实摆快照整体覆盖")
    if "_displayed = previous;" not in apply.group(1) or "ShowcaseDisplayJudges.SameSnapshot(_displayed, normalized)" not in apply.group(1):
        return fail(SHOWCASE.as_posix() + " ApplyDisplaySnapshot 必须在写失败时恢复原列表，且与缓存相同时不落盘")
    if not re.search(r"private const int CurrentSchemaVersion = 1;", showcase) or '"sourceVersion"' not in showcase:
        return fail(SHOWCASE.as_posix() + " 存档必须保持 schemaVersion=1 并以可选 sourceVersion 区分登记簿 / 官方柜："
                    "升 schemaVersion 会让 EnsureLoaded 对老档永久写保护（_writeBarrier 不复位）")
    if "onSlotContentChanged += HandleSlotContentChanged" not in showcase_scanner or "onSlotContentChanged -= HandleSlotContentChanged" not in showcase_scanner:
        return fail(SHOWCASE_SCANNER.as_posix() + " 槽位事件必须用命名方法成对订阅 / 退订（AGENTS.md 4.6）")
    if "_subscribed" not in showcase_scanner:
        return fail(SHOWCASE_SCANNER.as_posix() + " 缺订阅幂等表 _subscribed")
    flush = re.search(r"internal\s+static\s+void\s+FlushIfDirty\s*\(\)\s*\{\s*if \(!_dirty\) return;", showcase_scanner)
    if not flush:
        return fail(SHOWCASE_SCANNER.as_posix() + " FlushIfDirty 首句必须是 if (!_dirty) return;（每帧 O(1) 早返）")
    init_gate = re.search(r"private\s+void\s+InitBackMountainShowcase\s*\(bool isEarlyInit\)\s*\{(.*?)\n        \}", showcase_builder, flags=re.S)
    if not init_gate or "IsFacilityUnlocked(BackMountainFacility.Showcase)" in init_gate.group(1) or "HasPendingShowcaseBuildingsInManager()" not in init_gate.group(1):
        return fail(SHOWCASE_BUILDER.as_posix() + " 自建柜已退役：注入门只能看老档是否建过（HasPendingShowcaseBuildingsInManager），不得再按解锁进建造菜单")

    # ---- 7b) 菜地工地：只读官方键、判据走纯函数、开门排在作物注入之后 ----
    if not re.search(r'ConstructionSaveKey\s*=\s*"ConstructionSite_GardenConstruction"', garden_judges):
        return fail(GARDEN_JUDGES.as_posix() + " 官方工地存档键字面值已变（只读契约）")
    for path in Path("Integration").rglob("*.cs"):
        text = strip_comments(path.read_text(encoding="utf-8", errors="ignore"))
        if 'SavesSystem.Save<bool>("ConstructionSite_' in text or "SavesSystem.Save<bool>(GardenSiteJudges.ConstructionSaveKey" in text:
            return fail(path.as_posix() + " 写了官方工地存档键：Mod 只能打开付费交互，wasBuilt / 存档全归官方（AGENTS §10）")
    if "GardenSiteJudges.ShouldOpenSite(" not in garden_site or "interactParent.SetActive(true);" not in garden_site:
        return fail(GARDEN_SITE.as_posix() + " 开门必须走 GardenSiteJudges.ShouldOpenSite 并只激活付费交互的父物体")
    refresh = re.search(r"private\s+void\s+RefreshFacilitiesForScene\s*\([^)]*\)\s*\{(.*?)\n        \}", module, flags=re.S)
    if not refresh:
        return fail(MODULE.as_posix() + " 找不到 RefreshFacilitiesForScene 方法体")
    inject_at = refresh.group(1).find("GardenSeedInjector.EnsureInjected();")
    open_at = refresh.group(1).find("GardenConstructionSite.EnsureSiteOpen(")
    if inject_at < 0 or open_at < 0 or open_at < inject_at:
        return fail(MODULE.as_posix() + " 菜地工地开门必须排在 GardenSeedInjector.EnsureInjected() 之后")
    if "GardenSeedInjector.IsInjected" not in refresh.group(1):
        return fail(MODULE.as_posix() + " 开门判据必须带 GardenSeedInjector.IsInjected（作物注入早于 Built 子树激活的结构不变式）")

    # ---- 7c) 点唱机英文曲名（CR-2026-09-18-025 回归）----
    if "L10n.T(track.musicName," not in jukebox or "track.musicNameEn" not in jukebox:
        return fail(JUKEBOX.as_posix() + " 点唱机曲名必须在取用时按语言解析（CR-2026-09-18-025 回归）")
    if "FindTrack(selector.entries, path)" not in jukebox or "selector.entries[index] = entry" not in jukebox:
        return fail(JUKEBOX.as_posix() + " 点唱机必须按音频路径原位替换：按标题判重会在切语言时重复追加并移位官方索引")
    if 'row.TryGetString("musicNameEn"' not in bgm_table or 'row.TryGetString("musicName", out entry.musicName)' not in bgm_table:
        return fail(BGM_TABLE.as_posix() + " musicNameEn 必须是可选字段、musicName 必须必填（旧表兼容）")
    if "def == null || def.IsSeed" not in raid_use:
        return fail("出击餐 CanBeUsed 必须拒绝陌生物品和种子")
    if "SavesSystem.IsSaving" in raid_use:
        return fail("出击餐不得在官方二次 CanBeUsed 门禁因存档忙而跳过 OnUse 补偿")
    if "SavesSystem.IsSaving" not in raid_meal or 'item.SetInt("Count", item.StackCount + 1, true)' not in raid_use:
        return fail("出击餐登记层必须拒绝存档忙，OnUse 失败补偿官方无条件扣量")
    if "SavesSystem.Load<int>(BackMountainConfig.RaidMealSaveKey)" not in raid_meal:
        return fail("出击餐登记与消费必须回读核对，避免同一份餐跨局重复生效")

    # 卸载接线：展示柜注入器必须在 Mod 卸载路径上被清理
    scene = strip_comments(SCENE.read_text(encoding="utf-8", errors="ignore"))
    if "CleanupBackMountainShowcase();" not in scene:
        return fail(
            SCENE.as_posix() + " 的 Mod 卸载路径缺少 CleanupBackMountainShowcase()。"
            "展示柜注入器的静态图标引用会跨卸载残留（与遗种巢/婚礼/许愿台同款接线）。")

    print("BackMountainStructureGuard: PASS（单实例 + dormant + 解锁不缓存 + 冻结常量 + 官方柜陈列 / 菜地工地 / 点唱机曲名）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
