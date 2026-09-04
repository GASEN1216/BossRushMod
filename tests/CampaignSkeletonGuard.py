"""Guard: 鸭王征程的单实例纪律、dormant 契约与跨系统解锁契约的冻结面。

背景：征程是「战役 → 后山」这条进度线的上游。它出错的方式不是崩溃，
而是静默给错答案——后山据此显示或隐藏设施，玩家看到的是「奖励没发」。
所以这里守的重点不是模块骨架（那套范式 Mode H / 遗种巢已经固化），
而是解锁契约的三条易碎不变式：

  1. **冻结常量不得漂移**：ProgressSaveKey / FacilityTokenPrefix /
     BoardBuildingId / NoteKeyPrefix 进存档键、跨系统 token 与官方笔记本地化键。
     改名 = 老档的章节进度与解锁凭证全部失联，且不报错。

  2. **fail-closed**：查询侧在「尚未装载存档」时必须返回未解锁。
     反过来（默认已解锁）会让后山在读档前抢跑，把设施提前放出来。

  3. **换槽必须复位**：LoadGrantedTokens 整体替换而非追加，且有
     ResetForSlotReload。少了它，A 档通关的解锁会泄漏到 B 档
     （遗种巢踩过同类坑，见 PetNestPersistenceAccess.ResetCachesForSlotReload）。

另外总开关运行时可变（ModConfig 单键回调），所以模块要能关掉即 dormant、
打开即当帧复活，且关闭状态下每帧 O(1) 早返（AGENTS.md 4.12）。
"""

from pathlib import Path
import re
import sys

MODULE = Path("Campaign/CampaignRuntimeModule.cs")
PROGRESS = Path("Campaign/CampaignProgressService.cs")
UNLOCKS = Path("Campaign/CampaignFacilityUnlocks.cs")
TUNING = Path("Campaign/CampaignTuning.cs")
REGISTRATION = Path("Common/Lifecycle/BossRushRuntimeModuleRegistration.cs")
SCENE = Path("Integration/BossRushIntegration_StartAndScene.cs")
PLAYER = Path("Campaign/CampaignDialoguePlayer.cs")
FINAL_BOSS = Path("Campaign/CampaignFinalBoss.cs")
# 开关接线散在 Config.cs 与提取出去的白名单文件里（同一 partial 类，
# 拆分只为 LargeFileBudgetGuard 的 1200 行预算），断言时合并来看。
CONFIG_SOURCES = [
    Path("Config/Config.cs"),
    Path("Config/ConfigModConfigKeys.cs"),
    Path("Config/ConfigCampaign.cs"),
    Path("Config/ConfigContentSystemSwitches.cs"),
]

# 冻结契约：常量名 -> 必须保持的字面值。改这里之前先想清楚老档怎么办。
FROZEN = {
    "ProgressSaveKey": '"BossRush_Campaign_Progress_v1"',
    "FacilityTokenPrefix": '"BossRush_Campaign_Unlock_Ch"',
    "BoardBuildingId": '"bossrush_campaign_board"',
    "NoteKeyPrefix": '"BossRushCampaign_"',
}


def fail(message):
    print("CampaignSkeletonGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def main():
    for path in [MODULE, PROGRESS, UNLOCKS, TUNING, REGISTRATION,
                 SCENE, PLAYER, FINAL_BOSS] + CONFIG_SOURCES:
        if not path.is_file():
            return fail("找不到 " + path.as_posix())

    module = strip_comments(MODULE.read_text(encoding="utf-8", errors="ignore"))
    progress = strip_comments(PROGRESS.read_text(encoding="utf-8", errors="ignore"))
    unlocks = strip_comments(UNLOCKS.read_text(encoding="utf-8", errors="ignore"))
    tuning = strip_comments(TUNING.read_text(encoding="utf-8", errors="ignore"))
    reg = strip_comments(REGISTRATION.read_text(encoding="utf-8", errors="ignore"))
    config = strip_comments("\n".join(
        path.read_text(encoding="utf-8", errors="ignore") for path in CONFIG_SOURCES))

    # ---- 1) 单实例纪律：先存字段再注册同一引用 ----
    if not re.search(r"campaignRuntime\s*=\s*new\s+CampaignRuntimeModule\s*\(", reg):
        return fail(
            REGISTRATION.as_posix() + " 没有把实例先存进字段。"
            "必须「先存字段、再注册同一引用」，否则契约状态会分裂。")
    if not re.search(r"Register\s*\(\s*campaignRuntime\s*\)", reg):
        return fail(
            REGISTRATION.as_posix() + " 注册给 host 的不是字段里那份实例。")
    if not re.search(r"CampaignRuntimeModule\s+CampaignRuntime\s*\{\s*get", reg):
        return fail(REGISTRATION.as_posix() + " 缺少只读门面 CampaignRuntime")

    news = []
    for path in Path(".").rglob("*.cs"):
        if any(part in {".git", "Build", "鸭科夫源码"} for part in path.parts):
            continue
        text = strip_comments(path.read_text(encoding="utf-8", errors="ignore"))
        news += [path.as_posix()] * len(
            re.findall(r"new\s+CampaignRuntimeModule\s*\(", text))
    if len(news) != 1:
        return fail(
            "CampaignRuntimeModule 被 new 了 " + str(len(news)) + " 次（"
            + ", ".join(sorted(set(news))) + "）。单实例纪律要求全仓库只有一次。")

    # ---- 2) dormant 契约 ----
    if "EnsureBootstrapped" not in module:
        return fail(MODULE.as_posix() + " 缺少幂等 bootstrap 入口 EnsureBootstrapped")
    if not re.search(r"ShutdownIfEnabledTurnedOff\s*\(", module):
        return fail(
            MODULE.as_posix() + " 缺少开关关闭时的 shutdown 回落。"
            "总开关运行时可关，关掉后必须整体 dormant。")
    if "IsCampaignConfiguredEnabled" not in module:
        return fail(
            MODULE.as_posix() + " 没有经唯一只读入口 IsCampaignConfiguredEnabled 读开关")
    # 关掉开关必须同时把解锁契约复位，否则后山还查得到 token，设施继续可见
    if "ResetForSlotReload" not in module:
        return fail(
            MODULE.as_posix() + " 的 dormant 回落没有复位解锁契约。"
            "不复位的话，关掉征程后后山仍能查到 token，设施照常可见。")

    update = re.search(
        r"public\s+override\s+void\s+OnUpdate\s*\([^)]*\)\s*\{(.*?)\n        \}",
        module, flags=re.S)
    if update and re.search(r"DevLog|Debug\.Log", update.group(1)):
        return fail(
            MODULE.as_posix() + " 的 OnUpdate 里有日志调用。"
            "这是每帧路径，日志会在长局里刷爆 Player.log。")

    # ---- 3) 冻结常量 ----
    for name, literal in FROZEN.items():
        pattern = r"\b" + name + r"\s*=\s*" + re.escape(literal)
        if not re.search(pattern, tuning):
            return fail(
                TUNING.as_posix() + " 的冻结常量 " + name + " 不再等于 " + literal
                + "。它进存档键/跨系统 token/官方笔记本地化键，改名会让老档静默失联；"
                "确需变更必须走 BREAKING 流程并拿 owner 确认（AGENTS.md 第 6、10 节）。")

    # ---- 4) 解锁契约的 fail-closed 与换槽复位 ----
    for token in ("IsTokenGranted", "GetGrantedTokens", "TryGrant",
                  "LoadGrantedTokens", "ResetForSlotReload", "OnFacilityTokenGranted"):
        if token not in unlocks:
            return fail(UNLOCKS.as_posix() + " 缺少契约成员 " + token)

    is_granted = re.search(
        r"internal\s+static\s+bool\s+IsTokenGranted\s*\([^)]*\)\s*\{(.*?)\n        \}",
        unlocks, flags=re.S)
    if not is_granted:
        return fail(UNLOCKS.as_posix() + " 找不到 IsTokenGranted 方法体")
    if not re.search(r"if\s*\(\s*!\s*_loaded\s*\)\s*return\s+false", is_granted.group(1)):
        return fail(
            UNLOCKS.as_posix() + " 的 IsTokenGranted 没有 fail-closed 前置。"
            "尚未装载存档时必须返回未解锁，否则后山会在读档前抢跑放出设施。")

    load = re.search(
        r"internal\s+static\s+void\s+LoadGrantedTokens\s*\([^)]*\)\s*\{(.*?)\n        \}",
        unlocks, flags=re.S)
    if not load:
        return fail(UNLOCKS.as_posix() + " 找不到 LoadGrantedTokens 方法体")
    if "_granted.Clear()" not in load.group(1):
        return fail(
            UNLOCKS.as_posix() + " 的 LoadGrantedTokens 没有先清空再装载。"
            "必须整体替换而非追加，否则换槽时 A 档的解锁会泄漏到 B 档。")

    # 读档装载不得发事件：那不是新授予，后山靠自身全量查询拿历史解锁
    if re.search(r"LoadGrantedTokens[\s\S]{0,600}?OnFacilityTokenGranted", unlocks):
        return fail(
            UNLOCKS.as_posix() + " 的 LoadGrantedTokens 疑似触发了授予事件。"
            "读档回放不是新授予，发事件会让后山在每次读档时重复播放解锁提示。")

    # ---- 5) 恒开契约（owner 2026-08-30 定，见 Config/ConfigContentSystemSwitches.cs）----
    # 征程属于默认内容：字段默认 true、不注册进 ModConfig UI、被强制拉回 true。
    # 前两条与「被强制拉回」由 ModConfigOptionChangeGuard 的 CONTENT_SYSTEM_SWITCHES 统一断言；
    # 这里只钉住本系统自己那半边——别让人顺手把默认值改回 false。
    if not re.search(r"public bool campaignEnabled\s*=\s*true\s*;", config):
        return fail(
            "campaignEnabled 的默认值不再是 true。它属于默认内容，恒为开启；"
            "改回 false 会把老玩家永久关在系统外面且毫无提示"
            "（见 Config/ConfigContentSystemSwitches.cs 的策略说明）。")
    if re.search(r"RegisterCampaignModConfigOption\s*\(", config):
        return fail(
            "征程开关又被注册进 ModConfig UI 了。它属于默认内容，不该暴露；"
            "若确要放出，请同时更新 ModConfigOptionChangeGuard 的 CONTENT_SYSTEM_SWITCHES "
            "并登记回 IsHandledModConfigOptionKey 白名单。")

    # ---- 6) 待交付必须落盘；交付写失败不得重复发钱/提前发布 token ----
    if "CampaignChapterState.ReadyToDeliver" not in progress or not re.search(
            r"WriteState\(chapterId,\s*CampaignChapterState\.ReadyToDeliver\)", progress):
        return fail("目标完成必须把 ReadyToDeliver 落盘，重启后仍能回公告板交付")
    store_at = progress.find("WriteStateAndRewards(chapterId, CampaignChapterState.Completed, def)")
    token_at = progress.find("CampaignFacilityUnlocks.TryGrant(def.FacilityToken)", store_at)
    if store_at < 0 or token_at < store_at:
        return fail("交付必须先写完成 payload，再发布后山 token；写失败不能提前解锁")
    if "reward_rollback" not in progress or "CloneSaveData" not in progress:
        return fail("交付写失败必须撤回本次奖金，且不得直接改写当前缓存对象")

    # ---- 7) 卸载接线：建筑注入器必须在 Mod 卸载路径上被清理 ----
    scene = strip_comments(SCENE.read_text(encoding="utf-8", errors="ignore"))
    if "CleanupCampaignBoardBuilding();" not in scene:
        return fail(
            SCENE.as_posix() + " 的 Mod 卸载路径缺少 CleanupCampaignBoardBuilding()。"
            "公告板注入器的静态图标引用会跨卸载残留（与遗种巢/婚礼/许愿台同款接线）。")

    # ---- 8) 终章冠军独白：立绘必须真的被用上，且宿主不得与中间人共用 ----
    player = strip_comments(PLAYER.read_text(encoding="utf-8", errors="ignore"))
    boss = strip_comments(FINAL_BOSS.read_text(encoding="utf-8", errors="ignore"))
    if "CampaignAssetCache.GetChampionPortrait()" not in player:
        return fail(
            "冠军立绘（Assets/ui/Campaign/campaign_portrait_champion.png）又没有调用方了。"
            "它是已随包发布的美术资产，零调用 = 玩家永远看不到。")
    if "_championActorHost" not in player:
        return fail(
            "冠军对话 actor 必须有独立宿主 GameObject。DialogueActorFactory 的缓存按 "
            "GameObject 索引，Create 命中缓存时会忽略传入的 actorId/nameKey/portrait，"
            "与中间人共用宿主会让冠军顶着中间人的名字和立绘说话。")
    if "PlayFinalBossPrologueAsync" not in boss:
        return fail("决战开战路径没有播冠军独白。终章是玩家唯一一次听冠军说话的地方。")
    if "StartCampaignFinalBossAsync(runId).Forget();" not in boss:
        return fail(
            "F3 验收路径（DebugStartCampaignFinalBossForValidation）必须绕过独白直连生成，"
            "否则对话要等玩家点击才 resolve，RunCampaignFinalBoss 会一路等到超时记 spawn_timeout。")

    print("CampaignSkeletonGuard: PASS（单实例 + dormant + 冻结常量 + 契约 fail-closed + 卸载接线 + 终章独白）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
