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
SHOWCASE_UI = Path("Integration/BackMountain/ShowcaseUI.cs")
RAID_MEAL = Path("Integration/BackMountain/RaidMealService.cs")
RAID_MEAL_USE = Path("Integration/BackMountain/RaidMealUsageBehavior.cs")
UNLOCKS = Path("Integration/BackMountain/BackMountainUnlocks.cs")
CONFIG_CONST = Path("Integration/BackMountain/BackMountainConfig.cs")
REGISTRATION = Path("Common/Lifecycle/BossRushRuntimeModuleRegistration.cs")
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
    for path in [MODULE, SHOWCASE, SHOWCASE_UI, RAID_MEAL, RAID_MEAL_USE,
                 UNLOCKS, CONFIG_CONST, REGISTRATION] + CONFIG_SOURCES:
        if not path.is_file():
            return fail("找不到 " + path.as_posix())

    module = strip_comments(MODULE.read_text(encoding="utf-8", errors="ignore"))
    unlocks = strip_comments(UNLOCKS.read_text(encoding="utf-8", errors="ignore"))
    consts = strip_comments(CONFIG_CONST.read_text(encoding="utf-8", errors="ignore"))
    reg = strip_comments(REGISTRATION.read_text(encoding="utf-8", errors="ignore"))
    config = strip_comments("\n".join(
        path.read_text(encoding="utf-8", errors="ignore") for path in CONFIG_SOURCES))
    showcase = strip_comments(SHOWCASE.read_text(encoding="utf-8", errors="ignore"))
    showcase_ui = strip_comments(SHOWCASE_UI.read_text(encoding="utf-8", errors="ignore"))
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
        if any(part in {".git", "Build", "鸭科夫源码"} for part in path.parts):
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

    # ---- 6) 恒开契约 + 旁路旋钮仍可拨 ----
    # 后山总开关属于默认内容（恒开、不进 UI）；而 UnlockAll 是玩家偏好旋钮，
    # 必须继续注册并留在白名单里，否则玩家拨了没反应。两者性质不同，别一起撤掉。
    if not re.search(r"public bool backMountainEnabled\s*=\s*true\s*;", config):
        return fail(
            "backMountainEnabled 的默认值不再是 true。它属于默认内容，恒为开启"
            "（见 Config/ConfigContentSystemSwitches.cs 的策略说明）。")
    if "BackMountainUnlockAllModConfigKeySuffix" not in config:
        return fail(
            "IsHandledModConfigOptionKey 白名单缺少后山旁路旋钮键（查了 "
            + ", ".join(p.as_posix() for p in CONFIG_SOURCES) + "），"
            "玩家拨动「跳过战役解锁」不会即时生效。")

    # ---- 7) 登记/餐食写入必须可证实，失败不能静默报告成功 ----
    if not re.search(r"private static bool Store\(\)", showcase):
        return fail("展示柜 Store 必须返回 bool，让上层在写失败时回滚内存登记")
    if "showcase save readback mismatch" not in showcase or "_displayed.Remove(typeId)" not in showcase:
        return fail("展示柜登记必须回读核对，写失败时撤销刚加入的 TypeID")
    if "ResolveEquippedTrophy" not in showcase_ui or "ResolveHeldItem" not in showcase_ui:
        return fail("展示柜必须同时支持手持与穿戴战利品登记")
    if "SavesSystem.IsSaving" not in raid_use or "def.IsSeed" not in raid_use:
        return fail("出击餐 CanBeUsed 必须在存档忙/陌生物品/种子时拒绝消耗")
    if "SavesSystem.Load<int>(BackMountainConfig.RaidMealSaveKey)" not in raid_meal:
        return fail("出击餐登记与消费必须回读核对，避免同一份餐跨局重复生效")

    print("BackMountainStructureGuard: PASS（单实例 + dormant + 解锁不缓存 + 冻结常量）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
