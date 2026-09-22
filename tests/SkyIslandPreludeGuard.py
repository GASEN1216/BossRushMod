# -*- coding: utf-8 -*-
"""天空岛官方任务注册表守卫：Jeff 序章 + 岛上三条主线都真接官方 Quest，Mod 存档为权威，卸载不留孤儿 ID。

2026-09-16 起本守卫从「序章单任务」扩成「多任务注册表」口径（文件名保留，FIX_TRACKER 与教程都引它）：
- 序章：官方 Jeff Quest → 零号区航向仪 / 断风游猎 → 官方交付后开放船点（三层航线门）。
- 岛上：590011–590013 挂在苇白 / 浮舟 / 钟守（自定义 QuestGiverID 整数 5901–5903）名下，只在岛上接、岛上交。
- 桥：2026-09-22 起注册 / 投影 / 补丁 / 快照过滤都在共享核心 `Utilities/OfficialQuests/`（守卫 `tests/OfficialQuestProjectionGuard.py`），
  本文件的桥只是天空岛客户端：把任务表定义包成与事实无关的委托交给核心，并负责交付那一拍发钱与离岛后的会话状态。
- 存档：每条任务 Accepted + Delivered 两位（官方 history 每次都被剥掉，读档只靠这两位重建）；KnownFlags 同步；
  Codec 拒绝「交付无接取 / 交付无事实 / 未解锁航线却有任务位」。
反向探针在内存里恢复旧写法，确保每条断言真的抓得住。
"""
from pathlib import Path

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
SKY = "DebugAndTools/SkyIsland/"
PRELUDE = SKY + "SkyIslandPreludeFlow.cs"
QUEST = SKY + "SkyIslandOfficialQuestBridge.cs"
TABLE = SKY + "SkyIslandOfficialQuestTable.cs"
GIVERS = SKY + "SkyIslandOfficialQuestGivers.cs"
RUNTIME = SKY + "SkyIslandRuntimeModule.cs"
SESSION = SKY + "SkyIslandSession.cs"
SESSION_QUEST = SKY + "SkyIslandSessionQuestBridge.cs"
RESIDENTS = SKY + "SkyIslandResidents.cs"
RULES = SKY + "SkyIslandStoryRules.cs"
CODEC = SKY + "SkyIslandStoryCodec.cs"
SERVICE = SKY + "SkyIslandStoryService.cs"
START = "Integration/BossRushIntegration_StartAndScene.cs"
F3 = "DebugAndTools/F3GameplayValidationRunner.cs"
COMPILE = "compile_official.bat"
CSPROJ = "tests/fixtures/SkyIslandStory/Regression.csproj"
PATHS = (PRELUDE, QUEST, TABLE, GIVERS, RUNTIME, SESSION, SESSION_QUEST, RESIDENTS, RULES, CODEC, SERVICE, START, F3, COMPILE, CSPROJ)

QUEST_SYMBOLS = ("QuestManager", "QuestGiverID", "QuestCollection", "QuestGiverView", "Duckov.Quests")


def check(sources):
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    def ordered(text, first, second, message):
        a = text.find(first)
        b = text.find(second)
        require(a >= 0 and b >= 0 and a < b, message)

    prelude = clean_source(sources[PRELUDE])
    quest = clean_source(sources[QUEST])
    table = clean_source(sources[TABLE])
    givers = clean_source(sources[GIVERS])
    runtime = clean_source(sources[RUNTIME])
    session = clean_source(sources[SESSION])
    session_quest = clean_source(sources[SESSION_QUEST])
    residents = clean_source(sources[RESIDENTS])
    rules = clean_source(sources[RULES])
    codec = clean_source(sources[CODEC])
    service = clean_source(sources[SERVICE])
    start = clean_source(sources[START])
    f3 = clean_source(sources[F3])

    # ---- 1) 序章：官方 Jeff、零号区目标、头目复用、生命周期 ----
    for token, why in (
        ("OfficialQuestGiverLocator.TryFind(QuestGiverID.Jeff, out candidate)", "必须经共享定位器按官方公开枚举识别 Jeff，不能猜名字或层级"),
        ("officialQuest.PrepareGiver(candidate)", "找到 Jeff 后没有刷新官方任务标记"),
        ('GroundZeroScene = "Level_GroundZero_1"', "前置调查没有落在官方零号区"),
        ("SimplePointOfInterest.Create", "目标没有复用官方地图标记"),
        ('SkyIslandBossForge.TryApply(created, "K3_Relay", 0, context)', "没有复用群岛断风游猎头目"),
        ("SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(created)", "官方角色仍可能停在距离休眠"),
        ("created.SetTeam(Teams.wolf)", "前置头目缺少敌对阵营安全网"),
        ("expected == generation", "异步生成缺少场景代数门"),
        ("health.OnDeadEvent.RemoveListener(OnDead)", "头目死亡监听没有成对退订"),
        ("GiverId = (int)QuestGiverID.Jeff", "序章给予者必须是官方 Jeff"),
        ("Accept = TryAcceptOfficialQuest, Deliver = TryCompleteOfficialQuest", "序章的接取 / 交付没有交给注册表"),
        ("officialQuest.Register(BuildDefinition())", "序章没有向多任务注册表登记"),
        ("officialQuest.Unregister(SkyIslandOfficialQuestTable.PreludeQuestId)", "序章销毁时没有从注册表撤掉自己"),
        ("SkyIslandOfficialQuestStory.SetBaseSource(story)", "基地故事没有发布给任务桥当事实源"),
        ('reason = L10n.T("带着航向仪回基地找 Jeff。"', "序章交付必须回基地（岛上三条才是原地交付）"),
        # 交付物：头目的尸体箱里出，交付时收走，丢了还能在残骸处再拆一具（否则会把玩家卡在半路）
        ("SkyIslandNavInstrumentConfig.TryDropInto(boss)", "前置头目没有在建尸体箱前放进交付物"),
        ("boss.BeforeCharacterSpawnLootOnDead -= OnBeforeLoot", "交付物掉落事件没有成对退订"),
        ("SkyIslandNavInstrumentConfig.CountOwned() <= 0", "交付没有要求玩家真的带着航向仪"),
        ("SkyIslandNavInstrumentConfig.TryConsumeOne()", "交付没有收走航向仪"),
        ("SkyIslandNavInstrumentConfig.TryGiveToPlayer()", "残骸兜底没有真的发一具航向仪（箱子被毁就成死局）"),
        ("!data.SkyIslandRouteUnlocked && !holdsInstrument", "守卫的在场判据不看「手上有没有」，丢包之后不会重刷"),
        ("RequiredItemId = SkyIslandNavInstrumentConfig.TYPE_ID, RequiredItemCount = 1, RewardMoney = DeliveryMoney",
         "序章没有把交付物与奖金交给任务表"),
        ("DeliveryMoney = 5000", "序章交付奖金未冻结"),
        # 地图标记：官方按 SceneInfoCollection 的场景 ID 过滤，直接传 Unity 场景名在别的地图上会让标记整条不显示
        ("MapPointSceneResolver.Resolve(GroundZeroScene)", "地图标记的场景参数没有走共享解析"),
        ("mapMarker.IsArea = true", "目标只画一个点，零号区那张图上容易被漏看"),
    ):
        require(token in prelude, PRELUDE + "：" + why + "（缺 " + token + "）")
    departure = prelude.split("internal bool TryPrepareDeparture(", 1)[1].split("private void CloseStory()", 1)[0]
    require("SkyIslandOfficialQuestStory.SetBaseSource(null);" in departure,
            PRELUDE + " 出发前没有释放基地故事投影")
    require("private const float TickInterval = 0.25f" in prelude and "jeffAttempts = 12" in prelude and "jeffAttempts--" in prelude,
            PRELUDE + " 缺少节流 Tick 或 Jeff 有界重试")
    require("FindObjectsOfType<QuestGiver>" not in prelude, PRELUDE + " 不得自己扫描给予者：全局扫描只在 OfficialQuestGiverLocator 一处")
    require("BossActivationRange * BossActivationRange" in prelude, PRELUDE + " 必须等玩家接近目标后再创建完整角色")
    bundle_gate = prelude.split("internal void Tick()", 1)[1].split("if (!EnsureStory())", 1)[0] if "internal void Tick()" in prelude else ""
    schedule = prelude.split("internal void Schedule()", 1)[1].split("internal void OnStartedLoading()", 1)[0]
    require("bundleDeployed = SkyIslandRaidLease.IsBundleDeployed();" in schedule and "if (!bundleDeployed)" in bundle_gate,
            PRELUDE + " 缺包门必须由关卡调度采样，再在序章 Tick 中使用")
    require("SkyIslandRaidLease.IsBundleDeployed()" not in bundle_gate,
            PRELUDE + " 常驻 Tick 不应反复访问磁盘")
    require("context.BundleDeployed = SkyIslandPreludeFlow.BundleDeployed;" in givers,
            GIVERS + " 任务上下文必须复用入口 owner 的资源状态")
    ensure_story = prelude.split("private bool EnsureStory()", 1)[1].split("private bool NoConflictingMode()", 1)[0]
    require("story.IsCurrentSlot && storyReady" in ensure_story, PRELUDE + " 迁移失败的门面不能被误当作就绪")
    ordered(ensure_story, "story.EnsureRouteCompatibility(", "SkyIslandOfficialQuestStory.SetBaseSource(story);",
            PRELUDE + " 兼容检查完成前不能发布故事")
    close_story = prelude.split("private void CloseStory()", 1)[1].split("private void Report(", 1)[0]
    require("storyReady = false;" in close_story and "SkyIslandOfficialQuestStory.SetBaseSource(null);" in close_story,
            PRELUDE + " 关闭时必须复位就绪状态并释放任务事实源")
    failed_init = ensure_story.split("catch (Exception e)", 1)[1]
    require("CloseStory();" in failed_init, PRELUDE + " 初始化异常必须委托同一关闭 owner 清理")

    # ---- 2) 任务表：纯规则、稳定 ID、两位旗标、链条门 ----
    for token, why in (
        ("PreludeQuestId = 590001", "序章 Quest ID 未冻结"),
        ("BeaconQuestId = 590011", "两端航标任务 ID 未冻结"),
        ("BellCourtQuestId = 590012", "钟庭之争任务 ID 未冻结"),
        ("HomecomingQuestId = 590013", "归航钟任务 ID 未冻结"),
        ("WeibaiGiverId = 5901", "苇白给予者整数未冻结"),
        ("FuzhouGiverId = 5902", "浮舟给予者整数未冻结"),
        ("BellKeeperGiverId = 5903", "钟守给予者整数未冻结"),
        ("SkyIslandStoryRules.CanApply(data, action, out blocker)", "目标行文案没有取自绘面板同一份「还差什么」"),
        ("context.Data.Has(SkyIslandStoryFlag.BeaconQuestDelivered)", "钟庭之争没有等两端航标交付"),
        ("context.Data.BothBeacons && context.Data.BellKeeperResolved", "归航钟必须在钟庭事件解决后就地接取"),
        ("context.OnIsland && context.Data != null && context.Data.SkyIslandRouteUnlocked", "岛上任务的门没有要求人在岛上且航线已开"),
    ):
        require(token in table, TABLE + "：" + why + "（缺 " + token + "）")
    require(table.count("AcceptedFlag = SkyIslandStoryFlag.") == 3 and table.count("DeliveredFlag = SkyIslandStoryFlag.") == 3,
            TABLE + " 每条岛上任务都必须同时有 Accepted 与 Delivered 两位：官方 history 每次都被剥掉，少了 Delivered 读档后会再要你点一次完成")
    for banned in ("using UnityEngine", "using Duckov", "QuestGiverID", "QuestManager"):
        require(banned not in table, TABLE + " 出现了 " + banned + "：任务表必须无 Unity / Duckov 依赖，隔离回归才能逐字链接")
    require("SkyIslandOfficialQuestTable.cs" in sources[CSPROJ], CSPROJ + " 没有链接任务表：执行回归证不了「任务页与规则同源」")

    require("SkyIslandStoryRules.CanApply(context.Data, definition.AcceptAction, out blocker)" in table,
            TABLE + " 接取必须复用剧情规则")
    require("SkyIslandOfficialQuestTable.NextContactObjective(data)" in rules, RULES + " HUD 缺少官方任务接取 / 交付引导")
    require('LocalizationHelper.InjectLocalization("BossRush_SkyIsland_QuestGiver",' in table,
            TABLE + " 给予者交互名必须进入已有语言切换注入链")

    # ---- 3) 桥（天空岛客户端）：定义交给共享核心，事实解析、发钱、离岛状态留在这里 ----
    for token, why in (
        ("class SkyIslandOfficialQuestBridge : IDisposable, IOfficialQuestClient", "桥必须是共享投影核心的客户端"),
        ("owner.OfficialQuestRuntime.Projection", "桥必须取共享核心，不得自己 new 一份投影"),
        ("projection.Register(BuildBinding(definition))", "定义没有交给核心登记"),
        ("projection.UnregisterClient(this)", "销毁时没有整体撤销自己的定义"),
        ("CanOffer = () => SkyIslandOfficialQuestTable.CanOffer(def, SkyIslandOfficialQuestStory.Capture(host))", "可接取门没有复用任务表判据"),
        ("CanDeliver = () => SkyIslandOfficialQuestTable.CanDeliver(def, SkyIslandOfficialQuestStory.Capture(host))", "交付门没有复用任务表判据"),
        ("IsAccepted = () => HasFlag(def.AcceptedFlag)", "接取位没有读分槽故事"),
        ("IsDelivered = () => HasFlag(def.DeliveredFlag)", "交付位没有读分槽故事"),
        ("RewardPaid = () => HasFlag(def.DeliveredFlag)",
         "奖励的「已领取」不是读 Mod 交付事实：每次读档重建投影，玩家在已完成页就能再领一次钱"),
        ("PayReward = () => PayRewardOnce(def)", "交付那一拍的发钱没有交给核心调度"),
        ("if (data == null || !data.Has(def.DeliveredFlag)) return;", "发钱前没有核对交付事实已落下"),
        ("EconomyManager.Add(money)", "奖金没有走官方经济系统"),
        ("if (wasOnIsland && !onIsland) SkyIslandOfficialQuestGivers.ClearSessionState();", "离岛后没有清本趟的给予者兜底记录"),
        ("SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session)", "岛上每拍没有补装置兜底"),
        ("if (dirty) SkyIslandOfficialQuestGivers.RefreshMarkers();", "事实变化没有刷给予者标记"),
        ("Done = () => { SkyIslandStoryData data = CurrentData(); return data != null && task.Done(data); }", "目标判据没有按当前故事事实求值"),
    ):
        require(token in quest, QUEST + "：" + why + "（缺 " + token + "）")
    for banned in ("HarmonyPatch", "QuestCollection", "FilterSaveSnapshot", "manager.ActivateQuest", "new OfficialQuestProjection("):
        require(banned not in quest, QUEST + " 出现了 " + banned + "：注册 / 投影 / 补丁 / 过滤只能在共享核心（OfficialQuestProjectionGuard）")
    require("QuestReward_Money" not in quest,
            QUEST + " 用了官方 QuestReward_Money：它的「已领取」写在实例上，而投影每次加载都重建，玩家能反复领同一笔钱")

    # ---- 4) 给予者：Awake 之前写 id、官方任务页缺席不挂、装置兜底只在居民缺席且整队生成完之后 ----
    attach = givers.split("private static bool Attach(", 1)[1] if "private static bool Attach(" in givers else ""
    ordered(attach, "if (QuestGiverView.Instance == null)", "NPCInteractionGroupHelper.AddSubInteractable<QuestGiver>(",
            GIVERS + " 官方 QuestGiverView 不在场时仍会挂给予者：那会变成一个点了没反应的选项")
    setup = attach.split("NPCInteractionGroupHelper.AddSubInteractable<QuestGiver>(", 1)[1].split("});", 1)[0] if "AddSubInteractable<QuestGiver>(" in attach else ""
    require("GiverIdField.SetValue(component, (QuestGiverID)giverId);" in setup,
            GIVERS + " questGiverID 必须在 AddSubInteractable 的 setup 回调里写（官方 Awake 之前），否则头顶标记绑错给予者")
    require("component.spawnPOI = false;" in setup, GIVERS + " 岛上地图标记归 SkyIslandMapMarkers，不要官方 POI")
    for token, why in (
        ("session.ResidentsSettled", "居民还没生成完就判「谁缺席」，兜底会挂到每个装置上"),
        ("fallbackAttempts >= FallbackAttemptLimit", "装置兜底没有有界重试"),
        ("NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(device", "兜底给予者必须分组进装置，不另起同点交互体"),
        ("OfficialQuestGiverLocator.RefreshMarker(giver)", "剧情事实变了没有刷头顶标记（反射只在共享定位器一处）"),
        ("internal static void ResetStaticCaches()", "静态给予者缓存没有唯一清理 owner"),
    ):
        require(token in givers, GIVERS + "：" + why + "（缺 " + token + "）")
    fallback = givers.split("internal static void EnsureDeviceFallback(", 1)[1].split("private static bool HasAttachedGiver(", 1)[0]
    require("resident == null || HasAttachedGiver(giverId)" in fallback and "session.FindResidentQuestOwner(resident)" in fallback,
            GIVERS + " 居民存在不能代替给予者接线成功；UI 后就绪要补回居民原组")
    require("SkyIslandOfficialQuestGivers.AttachResident(interaction.transform, standaloneGroup, id);" in residents,
            RESIDENTS + " 普通居民交互路径也必须接任务给予者")
    require("GetOrCreateStandaloneInteractable" not in givers, GIVERS + " 用了独立交互体：会新增同点竞争体")
    require("SkyIslandOfficialQuestGivers.AttachResident(relationship.transform, group, id);" in residents,
            RESIDENTS + " 发任务的居民身上没有挂官方给予者")
    require("finally { spawnFinished = true; }" in residents, RESIDENTS + " 整队生成完没有落标记，兜底判「谁缺席」没有依据")
    for symbol in QUEST_SYMBOLS:
        require(symbol not in residents, RESIDENTS + " 引用了官方任务符号 " + symbol + "：居民 owner 不碰任务系统，接线只经 SkyIslandOfficialQuestGivers")
    for token, why in (
        ("IsSessionValid() ? story : null", "岛上事实源没有按会话有效性给出"),
        ("residents.SpawnFinished", "会话没有暴露居民整队生成完的状态"),
        ("GetComponent<SkyIslandSearchPoint>()", "兜底装置不是岛上的见闻点交互体"),
    ):
        require(token in session_quest, SESSION_QUEST + "：" + why + "（缺 " + token + "）")

    # ---- 5) 模块接线：桥常驻、先于岛上早退运行、销毁时清理 ----
    require("quests = new SkyIslandOfficialQuestBridge(host)" in runtime, RUNTIME + " 没有持有常驻任务桥")
    require("quests.Tick()" not in runtime,
            RUNTIME + " 不得再自己 Tick 任务桥：投影由 OfficialQuestRuntimeModule 无条件驱动（不受「岛上会话存在就早退」影响）")
    for token in ("quests.Dispose()", "SkyIslandOfficialQuestGivers.ResetStaticCaches()", "SkyIslandOfficialQuestStory.ResetStaticCaches()"):
        require(token in runtime, RUNTIME + " 销毁时缺 " + token)
    require("SkyIslandPreludeFlow.InjectLocalizations();" in start and "SkyIslandOfficialQuestTable.InjectLocalizations();" in start,
            START + " 没有注入官方任务标题与说明：任务页会显示裸 key")

    # ---- 6) 存档：新位、掩码、回填、Codec 矛盾态；schema key 不变 ----
    for token in ("PreludeAccepted = 65536", "PreludeInstrumentRecovered = 131072", "RouteUnlocked = 262144",
                  "BeaconQuestAccepted = 524288", "BeaconQuestDelivered = 1048576",
                  "BellCourtQuestAccepted = 2097152", "BellCourtQuestDelivered = 4194304",
                  "HomecomingQuestAccepted = 8388608", "HomecomingQuestDelivered = 16777216",
                  "LegacyKnownFlags = 65535", "KnownFlags = 33554431", "TryGrantLegacyRoute", "HasLegacyIslandProgress",
                  "TryBackfillIslandQuests", "case SkyIslandStoryAction.DeliverBeaconQuest:",
                  "case SkyIslandStoryAction.DeliverBellCourtQuest:", "case SkyIslandStoryAction.DeliverHomecomingQuest:"):
        require(token in rules, RULES + " 缺任务 / 兼容状态：" + token)
    require("SchemaVersion = 1" in rules and 'StorageKey = "BossRush_SkyIsland_Story_v1"' in rules,
            RULES + " 不得仅为任务投影提升 schema 或更换存档 key")
    require("PreludeInstrumentRecovered) && !data.Has(SkyIslandStoryFlag.PreludeAccepted" in codec and
            "RouteUnlocked) && !data.Has(SkyIslandStoryFlag.PreludeAccepted | SkyIslandStoryFlag.PreludeInstrumentRecovered" in codec,
            CODEC + " 没有拒绝跳过 Jeff / 航向仪的损坏状态")
    for token in ("QuestContradicts(data, SkyIslandStoryFlag.BeaconQuestAccepted, SkyIslandStoryFlag.BeaconQuestDelivered, data.BothBeacons)",
                  "QuestContradicts(data, SkyIslandStoryFlag.BellCourtQuestAccepted, SkyIslandStoryFlag.BellCourtQuestDelivered, data.BellKeeperResolved)",
                  "QuestContradicts(data, SkyIslandStoryFlag.HomecomingQuestAccepted, SkyIslandStoryFlag.HomecomingQuestDelivered, data.Has(SkyIslandStoryFlag.Ending))",
                  "AnyIslandQuestFlag(data) && !data.Has(SkyIslandStoryFlag.RouteUnlocked)"):
        require(token in codec, CODEC + " 没有拒绝岛上任务的矛盾状态：" + token)
    require("TryBackfillIslandQuests(changed ? candidate : Current" in service,
            SERVICE + " 老档没有按既有事实回填任务位：已敲钟的槽会被再问一遍「去点灯吧」")

    # ---- 7) 三层航线门与编译清单 ----
    scan_prefix = runtime.split("foreach (InteractableBase candidate", 1)[0]
    require("prelude == null || !prelude.RouteUnlocked" in scan_prefix, RUNTIME + " 未解锁仍会扫描并注入船点")
    require("prelude.TryPrepareDeparture(out reason)" in runtime, RUNTIME + " 点击船点没有二次确认并先关闭基地存档 owner")
    require("SkyIslandPreludeFlow.CanUseRoute(out reason)" in session, SESSION + " 的 CanEnter 没有航线硬门，未来调用可绕过船点")
    require("AllowsLockedSkyIslandEntry" in session and "AllowsLockedSkyIslandEntry" in f3, "全自动专用测试档的窄旁路没有保留")
    for name in ("SkyIslandPreludeFlow.cs", "SkyIslandOfficialQuestBridge.cs", "SkyIslandOfficialQuestTable.cs",
                 "SkyIslandOfficialQuestGivers.cs", "SkyIslandSessionQuestBridge.cs"):
        require(name in sources[COMPILE], COMPILE + " 未登记 " + name)
    return errors


def main():
    sources = {path: (ROOT / path).read_text(encoding="utf-8-sig") for path in PATHS}
    errors = check(sources)
    probes = (
        (PRELUDE, "story.IsCurrentSlot && storyReady", "story.IsCurrentSlot"),
        (PRELUDE, "storyReady = false;", "storyReady = true;"),
        (GIVERS, "context.BundleDeployed = SkyIslandPreludeFlow.BundleDeployed;", "context.BundleDeployed = SkyIslandRaidLease.IsBundleDeployed();"),
        (GIVERS, "resident == null || HasAttachedGiver(giverId)", "resident == null || session.HasResident(resident)"),
        (GIVERS, "session.FindResidentQuestOwner(resident)", "null"),
        (RESIDENTS, "SkyIslandOfficialQuestGivers.AttachResident(interaction.transform, standaloneGroup, id);", ""),
        (TABLE, 'LocalizationHelper.InjectLocalization("BossRush_SkyIsland_QuestGiver",', 'LocalizationHelper.InjectLocalization("unused",'),
        (TABLE, "SkyIslandStoryRules.CanApply(context.Data, definition.AcceptAction, out blocker)", "true"),
        (QUEST, "CanDeliver = () => SkyIslandOfficialQuestTable.CanDeliver(def, SkyIslandOfficialQuestStory.Capture(host))", "CanDeliver = () => true"),
        (RULES, "SkyIslandOfficialQuestTable.NextContactObjective(data)", "null"),
        (PRELUDE, "OfficialQuestGiverLocator.TryFind(QuestGiverID.Jeff, out candidate)", "TryFindJeffByNameStub(out candidate)"),
        (PRELUDE, "jeffAttempts--", ""),
        (PRELUDE, "health.OnDeadEvent.RemoveListener(OnDead)", ""),
        (PRELUDE, "SkyIslandRaidLease.IsBundleDeployed()", "true"),
        (PRELUDE, "Accept = TryAcceptOfficialQuest, Deliver = TryCompleteOfficialQuest", "Accept = null, Deliver = null"),
        (PRELUDE, "officialQuest.Unregister(SkyIslandOfficialQuestTable.PreludeQuestId)", "officialQuest.Dispose()"),
        (PRELUDE, "SkyIslandNavInstrumentConfig.TryDropInto(boss)", ""),
        (PRELUDE, "boss.BeforeCharacterSpawnLootOnDead -= OnBeforeLoot", ""),
        (PRELUDE, "SkyIslandNavInstrumentConfig.CountOwned() <= 0", "false"),
        (PRELUDE, "SkyIslandNavInstrumentConfig.TryConsumeOne()", "true"),
        (PRELUDE, "SkyIslandNavInstrumentConfig.TryGiveToPlayer()", "true"),
        (PRELUDE, "!data.SkyIslandRouteUnlocked && !holdsInstrument",
         "!data.Has(SkyIslandStoryFlag.PreludeInstrumentRecovered) && !data.SkyIslandRouteUnlocked"),
        (PRELUDE, "DeliveryMoney = 5000", "DeliveryMoney = 500"),
        (PRELUDE, "MapPointSceneResolver.Resolve(GroundZeroScene)", "GroundZeroScene"),
        (PRELUDE, "mapMarker.IsArea = true", ""),
        (QUEST, "RewardPaid = () => HasFlag(def.DeliveredFlag)", "RewardPaid = () => false"),
        (QUEST, "PayReward = () => PayRewardOnce(def)", "PayReward = null"),
        (QUEST, "if (data == null || !data.Has(def.DeliveredFlag)) return;", ""),
        (QUEST, "EconomyManager.Add(money)", "true"),
        (QUEST, "owner.OfficialQuestRuntime.Projection", "new OfficialQuestProjection(owner)"),
        (QUEST, "projection.UnregisterClient(this)", "projection.Unregister(0)"),
        (QUEST, "if (wasOnIsland && !onIsland) SkyIslandOfficialQuestGivers.ClearSessionState();", ""),
        (QUEST, "if (dirty) SkyIslandOfficialQuestGivers.RefreshMarkers();", ""),
        (TABLE, "DeliveredFlag = SkyIslandStoryFlag.HomecomingQuestDelivered", "DeliveredFlag = 0"),
        (TABLE, "context.Data.Has(SkyIslandStoryFlag.BeaconQuestDelivered)", "true"),
        (GIVERS, "GiverIdField.SetValue(component, (QuestGiverID)giverId);", ""),
        (GIVERS, "if (QuestGiverView.Instance == null)", "if (false)"),
        (GIVERS, "session.ResidentsSettled", "true"),
        (GIVERS, "component.spawnPOI = false;", ""),
        (GIVERS, "OfficialQuestGiverLocator.RefreshMarker(giver)", "RefreshMarkerStub(giver)"),
        (RESIDENTS, "SkyIslandOfficialQuestGivers.AttachResident(relationship.transform, group, id);", ""),
        (RESIDENTS, "finally { spawnFinished = true; }", "finally { }"),
        (RUNTIME, "if (prelude != null) prelude.Tick();",
         "if (quests != null) quests.Tick();\n            if (prelude != null) prelude.Tick();"),
        (RUNTIME, "prelude == null || !prelude.RouteUnlocked", "false"),
        (RUNTIME, "prelude.TryPrepareDeparture(out reason)", "true"),
        (SESSION, "SkyIslandPreludeFlow.CanUseRoute(out reason)", "true"),
        (RULES, "KnownFlags = 33554431", "KnownFlags = 524287"),
        (RULES, "case SkyIslandStoryAction.DeliverBellCourtQuest:", "case SkyIslandStoryAction.DeliverBellCourtQuest :"),
        (CODEC, "AnyIslandQuestFlag(data) && !data.Has(SkyIslandStoryFlag.RouteUnlocked)", "false"),
        (CODEC, "PreludeInstrumentRecovered) && !data.Has(SkyIslandStoryFlag.PreludeAccepted", "PreludeInstrumentRecovered) && false"),
        (SERVICE, "TryBackfillIslandQuests(changed ? candidate : Current", "TryBackfillIslandQuests(null"),
        (START, "SkyIslandOfficialQuestTable.InjectLocalizations();", ""),
        (COMPILE, "echo(DebugAndTools\\SkyIsland\\SkyIslandOfficialQuestTable.cs", ""),
        (COMPILE, "echo(DebugAndTools\\SkyIsland\\SkyIslandOfficialQuestGivers.cs", ""),
        (CSPROJ, '<Compile Include="../../../DebugAndTools/SkyIsland/SkyIslandOfficialQuestTable.cs" Link="Production/SkyIslandOfficialQuestTable.cs" />', ""),
    )
    for path, before, after in probes:
        if before not in sources[path]:
            errors.append("反向检查锚点失效：" + path + " -> " + before)
            continue
        altered = dict(sources)
        altered[path] = altered[path].replace(before, after, 1)
        if not check(altered):
            errors.append("未拦截旧写法：" + path + " -> " + before)
    if errors:
        print("SkyIslandPreludeGuard: FAIL\n  " + "\n  ".join(errors))
        return 1
    print("SkyIslandPreludeGuard: PASS（官方 Jeff Quest + 岛上三条主线挂居民 / 装置兜底 + 逐条 fail-closed + 无孤儿任务 ID + 三层航线门；%d 个反向检查）" % len(probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
