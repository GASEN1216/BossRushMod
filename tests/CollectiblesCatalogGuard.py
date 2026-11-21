"""CollectiblesCatalogGuard: 收集品接线不变式（2026-11-19，owner 拍板的掉落口径）。

执行回归 CollectibleLoot 覆盖表、抽签、账本、登记规划、存档与送礼；本守卫钉它测不到的接线：

1. 不进官方 / Mod 随机池：配置时先清标签、只挂 Special 与 ShowCase（ShowCase 让它能上官方陈列柜；官方随机选物都排除
   Special），子系统里不出现 Luxury / Gem / Collection；
   500119-500219 全部在掉落黑名单 JSON 与兜底数组里。
2. 不做运行时查找（owner 2026-11-11）：子系统里不扫官方物品表、不 FindObjectsOfType、不按名字找场景对象。
3. 开箱接点：OnStartLoot 只在 CollectibleLootRuntime 订阅，订阅与退订成对；物品同步实例化（不走 Async），
   置 Inspected = false，经 TryAddExtraItem 满箱只扩一格；官方场景箱靠「库存挂在 LootBoxInventoriesParent 下」识别。
4. 自研三图的首开回调各交一次，潮闸镇水下与天空岛带各自的品质上限；潮闸镇排在货单装货之后，
   末班编组站按班次装货的柜子（封签柜、铅封侧柜、落货箱）不参与。
5. 登记事务先写存档、写成功才发奖金；写成功后的步骤逐个 try，物品照常收走。
6. 注册链：配置器、按需注册、本地化、模块装配都接上；送礼系统查收集品偏好。
7. 2026-11-19 审核加固：显示品质按档次写、官方路径按箱子所在场景排除 Mod 图、售货机挂上才收工、
   登记容器不随场景卸载并在读条开始时结算、过图收起收藏册；登记容器照送礼做成单格、点「登记」按钮才登记、
   关上只退回不登记、克隆的按钮随容器销毁；收藏册关闭钮不伸出面板描边。
8. 6–8 档格子边框（owner 2026-11-19 问答）：只有 6、7、8 档有；唯一接点是 ItemDisplay.Setup(Item) 的后置补丁；
   金框层插在官方选中高亮之前（选中照样看得清）、图元不吃点击不进布局；暂停菜单停推进、格子换物品即收起；
   出货标记只在交付成功之后打；模块销毁时清理、过图清出货标记；收藏册只给已登记的卡片挂。
9. 陈列模型（2026-11-19）：唯一接点是 ItemGraphicInfo.CreateAGraphic 前置补丁，只给还没有模型的收集品按需挂；
   材质复制官方奖杯、不 Shader.Find、不在运行时解码图片；模块销毁时清理；编译脚本部署 .brcm。
10. 陈列 / 地面特效（2026-11-19）：只给 6–8 档（复用 FrameTier）模型模板挂锚点；锚点没有每帧方法，进了镜头才建特效、
   出镜头由特效根自己停用（父物体停用途中不改激活状态）；暂停停推进、unscaled 时间；只用共享特效材质、URP 粒子着色器
   不可用就不画；越高越金、不偏紫偏蓝、逐档更强、只有 8 档浮空；模块销毁时拆光并还原模型。
"""

from pathlib import Path
import json
import re
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
MODULE_DIR = ROOT / "Integration" / "Collectibles"
FIRST, LAST_ITEM, BOOK = 500119, 500218, 500219


def read(rel):
    return clean_source((ROOT / rel).read_text(encoding="utf-8-sig"))


def compact(text):
    return re.sub(r"\s+", " ", text)


def method_body(text, signature):
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    depth = 0
    for i in range(brace, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[brace:i + 1]
    return ""


def main():
    errors = []
    sources = {p.name: clean_source(p.read_text(encoding="utf-8-sig")) for p in sorted(MODULE_DIR.glob("*.cs"))}
    if "CollectibleLootRuntime.cs" not in sources or "CollectibleItems.cs" not in sources:
        print("CollectiblesCatalogGuard: FAIL - Integration/Collectibles 缺少核心文件")
        return 1
    joined = "\n".join(sources.values())

    # 1. 标签与黑名单
    configure = compact(method_body(sources["CollectibleItems.cs"], "internal static void ConfigureItem(int typeId, Item item)"))
    if "item.Tags.Clear();" not in configure or 'EquipmentHelper.AddTagToItem(item, "Special");' not in configure:
        errors.append("CollectibleItems.ConfigureItem 必须先清克隆源标签再挂 Special")
    if configure.find("item.Tags.Clear();") > configure.find('AddTagToItem(item, "Special")'):
        errors.append("CollectibleItems.ConfigureItem 清标签必须在挂 Special 之前")
    # 2026-11-19 owner：收集品能摆上官方陈列柜（12 个位全要 ShowCase）。官方所有随机选物都排除 Special、没有掉落配置按
    # ShowCase 抽（按字节读游戏资源核过），所以 ShowCase 只能和 Special 一起挂，标签只有这两个。
    if 'EquipmentHelper.AddTagToItem(item, "ShowCase");' not in configure:
        errors.append("CollectibleItems.ConfigureItem 要挂 ShowCase，收集品才能摆上官方陈列柜")
    if sorted(re.findall(r'AddTagToItem\(item, "(\w+)"\)', configure)) != ["ShowCase", "Special"]:
        errors.append("收集品只挂 Special 与 ShowCase 两个标签（Special 是官方随机选物把它挡在外面的依据）")
    if "CollectibleShowcaseModels.ClearInheritedGraphic(item);" not in configure:
        errors.append("配置收集品要清掉克隆源的 ItemGraphic，否则地上掉落和陈列柜里会长成克隆源的模型")
    if '"NotSellable"' in configure:
        errors.append("收集品可以卖，不能挂 NotSellable")
    # CR-2026-11-19-001：克隆源自带红光晕，两处配置都要按档次写显示品质。
    book_configure = compact(method_body(sources["CollectionBookItem.cs"], "internal static void ConfigureItem(Item item)"))
    for name, body, quality in (("CollectibleItems.ConfigureItem", configure, "def.Quality"), ("CollectionBookItem.ConfigureItem", book_configure, "Quality")):
        if "item.DisplayQuality = (DisplayQuality)CollectibleTuning.DisplayQualityOf(%s);" % quality not in body:
            errors.append(name + " 必须按档次写 DisplayQuality（否则沿用克隆源的红光晕）")
    for tag in ("Luxury", "Gem", "Collection"):
        if re.search(r'"%s"' % tag, joined):
            errors.append("收集品子系统不能出现标签 " + tag + "（会被官方 / 天空岛随机池抽走）")
    data = json.loads((ROOT / "Assets/Data/LootBlacklist.json").read_text(encoding="utf-8"))
    missing = [i for i in range(FIRST, BOOK + 1) if i not in data.get("itemIds", [])]
    if missing:
        errors.append("LootBlacklist.json 缺少收集品 / 收藏册: %s" % missing[:5])
    fallback = read("Config/LootBlacklistRegistry.cs")
    fallback_ids = {int(x) for x in re.findall(r"\b(5001[1-9]\d|5002[01]\d)\b", fallback)}
    gap = [i for i in range(FIRST, BOOK + 1) if i not in fallback_ids]
    if gap:
        errors.append("LootBlacklistRegistry 兜底数组缺少收集品 / 收藏册: %s" % gap[:5])

    # 2. 不做运行时查找
    for token in ("ItemAssetsCollection.Instance", "FindObjectsOfType", "FindObjectOfType", "GameObject.Find("):
        for name, text in sources.items():
            if token in text:
                errors.append("%s 不能做运行时查找：%s" % (name, token))

    # 3. 开箱接点
    runtime = compact(sources["CollectibleLootRuntime.cs"])
    for name, text in sources.items():
        if name != "CollectibleLootRuntime.cs" and "OnStartLoot +=" in text:
            errors.append(name + " 不能另订 OnStartLoot；开箱判定只有 CollectibleLootRuntime 一处")
    if runtime.count("InteractableLootbox.OnStartLoot += OnStartLoot;") != 1 or runtime.count("InteractableLootbox.OnStartLoot -= OnStartLoot;") != 1:
        errors.append("CollectibleLootRuntime 的 OnStartLoot 订阅与退订必须各一处")
    deliver = compact(method_body(sources["CollectibleLootRuntime.cs"], "private void TryDeliver("))
    for token in ("ledger.TryClaim(inventory.GetInstanceID(), inventory.hasBeenInspectedInLootBox)",
                  "CollectibleItems.InstantiateChecked(typeId)", "item.Inspected = false;",
                  "InteractableLootboxInventoryHelper.TryAddExtraItem(inventory, item)", "ledger.MarkDelivered();"):
        if token not in deliver:
            errors.append("CollectibleLootRuntime.TryDeliver 缺少：" + token)
    if "Async" in deliver or "await" in deliver:
        errors.append("开箱交付必须同步：官方找不到未搜物品会立刻结束交互")
    official = compact(method_body(sources["CollectibleLootRuntime.cs"], "private void OnStartLoot(InteractableLootbox box)"))
    for token in ("ModRaidSceneBridge.ActiveIdentity != null || box.gameObject.scene.buildIndex < 0", "box.GetComponent<LootBoxLoader>() == null",
                  "inventory.transform.parent != InteractableLootbox.LootBoxInventoriesParent",
                  "CollectibleTuning.TryResolveOfficialBoxTier(inventory.DisplayNameKey, out tier)", "IsOfficialRaid()"):
        if token not in official:
            errors.append("官方场景箱识别缺少：" + token)
    instantiate = compact(method_body(sources["CollectibleItems.cs"], "internal static Item InstantiateChecked(int typeId)"))
    if "ItemAssetsCollection.InstantiateSync(typeId)" not in instantiate or "item.TypeID != typeId" not in instantiate:
        errors.append("CollectibleItems.InstantiateChecked 必须同步实例化并回读 TypeID")

    # 4. 自研三图
    hooks = {
        "Tidegate/TidegateLoot.cs": ("CollectibleSeries.Tidegate", "CollectibleTuning.TidegateUnderwaterQualityCap"),
        "LastShiftYard/LastShiftLoot.cs": ("CollectibleSeries.LastShift", "CollectibleTuning.MaxQuality"),
        "SkyIsland/SkyIslandScavenging.cs": ("CollectibleSeries.SkyIsland", "CollectibleTuning.SkyIslandQualityCap"),
    }
    for rel, (series, cap) in hooks.items():
        body = compact(method_body(read(rel), "private void OnStartLoot("))
        if body.count("CollectibleLootRuntime.NotifyModBoxFirstOpened(") != 1 or series not in body or cap not in body:
            errors.append(rel + " 的首开回调必须交一次收集品判定（" + series + "，上限 " + cap + "）")
        elif body.find("NotifyModBoxFirstOpened") < body.find(".Opened = true;"):
            errors.append(rel + " 必须在记下首开之后才交收集品判定")
    tide = compact(method_body(read("Tidegate/TidegateLoot.cs"), "private void OnStartLoot("))
    if tide.find("NotifyModBoxFirstOpened") < tide.find("ReadDelivery(point);"):
        errors.append("潮闸镇的收集品判定必须排在货单装货之后：货单占预留的末格，先塞收集品会把它挤掉")
    yard = compact(method_body(read("LastShiftYard/LastShiftLoot.cs"), "private void OnStartLoot("))
    if 'box.Kind != "sealed" && box.Kind != "side" && box.Kind != "drop"' not in yard:
        errors.append("末班编组站的封签柜、铅封侧柜、落货箱之后还要按班次装货，不能参与收集品判定")

    # 5. 登记事务（CR-2026-11-19-002）：发奖金、提示、刷新任务都交给 CollectionRegistration.Apply，
    #    它先写存档、写成功才跑后续步骤，且后续步骤逐个 try；执行回归 CollectibleLoot 做故障注入。
    module = compact(sources["CollectiblesRuntimeModule.cs"])
    register = compact(method_body(sources["CollectiblesRuntimeModule.cs"], "private List<Item> RegisterItems(List<Item> items)"))
    if "CollectionRegistration.Apply(plan, Commit," not in register or "() => PayCash(plan.Cash)" not in register:
        errors.append("登记事务必须经 CollectionRegistration.Apply(plan, Commit, ...) 提交，奖金放在提交之后的步骤里")
    if "EconomyManager.Add" in register:
        errors.append("RegisterItems 不能绕过 Apply 直接发奖金")
    apply = compact(method_body(sources["CollectionBookPersistence.cs"], "internal static bool Apply(CollectionRegistrationPlan plan,"))
    store, loop = apply.find("stored = store(plan.Next);"), apply.find("try { afterCommit[i](); }")
    if store < 0 or loop < 0 or store > loop or "if (!stored) return false;" not in apply:
        errors.append("CollectionRegistration.Apply 必须先写存档、写成功才逐个 try 跑后续步骤")
    if "CollectionRegistration.Plan(data, typeIds)" not in register:
        errors.append("登记必须走纯规划 CollectionRegistration.Plan（执行回归覆盖它）")
    if "Writable" not in compact(method_body(sources["CollectiblesRuntimeModule.cs"], "internal bool CanRegisterNow")):
        errors.append("登记入口必须检查存档可写")

    # 6. 注册链
    wiring = {
        "Integration/Items/ItemContentRegistry.cs": "CollectibleItems.RegisterConfigurators();",
        "Integration/BossRushDynamicItemRegistry.cs": "CollectibleItems.EnsureRuntimeRegistration(typeId); } }, CollectibleTypeIds());",
        "Integration/BossRushIntegrationRuntimeModule_ContentRegistration.cs": "CollectiblesLocalization.Inject();",
        "Integration/FieldAdventures/ExplorationRuntimeRegistration.cs": "host.Register(new CollectiblesRuntimeModule());",
        "Integration/BossRushIntegrationRuntimeModule.cs": "CollectibleItems.ResetStaticCaches();",
    }
    for rel, token in wiring.items():
        if token not in compact(read(rel)):
            errors.append(rel + " 缺少接线：" + token)
    gifts = compact(read("Integration/Affinity/Systems/NPCGiftSystem.cs"))
    value = compact(method_body(read("Integration/Affinity/Systems/NPCGiftSystem.cs"), "public static int CalculateGiftValue(string npcId, Item item)"))
    reaction = compact(method_body(read("Integration/Affinity/Systems/NPCGiftSystem.cs"), "public static GiftReactionType GetGiftReactionType(string npcId, Item item)"))
    if "CollectibleTuning.LikedGiftAffinity(npcId, typeId)" not in value or "CollectibleTuning.LikedGiftAffinity(npcId, typeId) > 0" not in reaction:
        errors.append("NPCGiftSystem 的好感值与反应都要查收集品偏好（CollectibleTuning.LikedGiftAffinity）")
    elif value.find("LikedGiftAffinity") < value.find("PositiveItems.TryGetValue"):
        errors.append("收集品偏好要排在 NPC 自己的 PositiveItems 之后，已有偏好优先")
    dynamic = compact(read("Integration/BossRushDynamicItemRegistry.cs"))
    if "CollectionBookItem.EnsureRuntimeRegistration(); } }, BossRushItemIds.CollectionBook);" not in dynamic:
        errors.append("收藏册必须登记按需注册计划")
    if "OnDestroy()" not in module or "loot.Dispose();" not in module or "persistence.Dispose();" not in module:
        errors.append("CollectiblesRuntimeModule.OnDestroy 必须释放开箱订阅与存档")

    # 7. 2026-11-19 审核加固：售货机挂上才收工、登记容器不随场景卸载、过图收起收藏册
    shop = compact(method_body(sources["CollectiblesRuntimeModule.cs"], "private void TickShop()"))
    if "if (stocked) shopInjectedThisScene = true;" not in shop:
        errors.append("TickShop 只有真挂上收藏册才能置 shopInjectedThisScene（CR-2026-11-19-004）")
    scene = compact(method_body(sources["CollectiblesRuntimeModule.cs"], "public override void OnSceneLoaded(SceneRuntimeContext context)"))
    if "CollectionBookView.CloseIfOpen();" not in scene:
        errors.append("过图必须收起收藏册界面（口径同图鉴）")
    container = compact(sources["CollectionRegisterContainer.cs"])
    if "UnityEngine.Object.DontDestroyOnLoad(root);" not in container:
        errors.append("登记容器根对象必须 DontDestroyOnLoad：放进去的物品挂在它下面，不能跟着基地场景卸载")
    if container.count("SceneLoader.onStartedLoadingScene += OnStartedLoadingScene;") != 1 or \
            container.count("SceneLoader.onStartedLoadingScene -= OnStartedLoadingScene;") != 1:
        errors.append("登记容器开着时要订阅读条开始事件先结算，订阅与退订各一处")
    # 2026-11-19 owner：登记照 NPC 送礼的单格容器做——只有一格、点「登记」才登记，关上界面只退回不登记。
    if "private const int Capacity = 1;" not in container:
        errors.append("登记容器只能有一格（照送礼的单格容器）")
    clicked = compact(method_body(sources["CollectionRegisterContainer.cs"], "private void OnRegisterClicked()"))
    closing = compact(method_body(sources["CollectionRegisterContainer.cs"], "internal void CloseAndReturn(bool byPlayer)"))
    if "register(new List<Item> { item })" not in clicked or container.count("register(") != 1:
        errors.append("登记只能由「登记」按钮的点击触发，一次一件")
    if not closing or "register" in closing.replace("registeredAny", "") or "ReturnToPlayer(item);" not in closing:
        errors.append("关上登记容器只退回格子里的物品，不能再自动登记")
    if "DestroyButton();" not in compact(method_body(sources["CollectionRegisterContainer.cs"], "private void Close()")):
        errors.append("克隆的「登记」按钮挂在常驻的官方 LootView 下，关容器时必须销毁，否则普通箱子会带着它")
    on_update = compact(method_body(sources["CollectiblesRuntimeModule.cs"], "public override void OnUpdate(float deltaTime, float unscaledDeltaTime)"))
    if "container.Tick();" not in on_update:
        errors.append("登记按钮要靠模块每帧 Tick 在 LootView 摆好后克隆")
    # 2026-11-21 玩家反馈：首次登记格子没开出来、界面一关什么都没有。
    # 官方 OnStartLoot 多播链被前面的订阅者抛异常打断时 LootView 收不到；打不开也不能静默收场。
    opening = compact(method_body(sources["CollectionRegisterContainer.cs"], "internal bool Open()"))
    isolated = compact(method_body(sources["CollectionRegisterContainer.cs"], "private void RaiseStartLootIsolated(Action<InteractableLootbox> start)"))
    if "start(lootbox)" in opening or "RaiseStartLootIsolated(start);" not in opening \
            or not re.search(r"GetInvocationList\(\);.*try \{ handler\(lootbox\); \} catch \(Exception e\)", isolated):
        errors.append("登记容器要逐个调用 OnStartLoot 订阅者并各自 try：一个抛异常不能拦住排在后面的 LootView")
    if "!IsShowing(LootView.Instance) && !OpenLootViewDirectly(LootView.Instance)" not in opening:
        errors.append("OnStartLoot 之后 LootView 还没开，登记容器要直接设目标箱子再开（送礼容器同一条后备路）")
    tick = compact(method_body(sources["CollectionRegisterContainer.cs"], "internal void Tick()"))
    if "CloseAndReturn(false)" in tick or tick.count("Fail();") != 2:
        errors.append("登记格子超时没出现、按钮没建出来都要走 Fail()：告诉玩家并回到收藏册，不静默关掉")
    failed = compact(method_body(sources["CollectiblesRuntimeModule.cs"], "private void OnRegisterOpenFailed(string text)"))
    if "ShowMessage(text);" not in failed or "reopenBookPending = true;" not in failed \
            or "new CollectionRegisterContainer(RegisterItems, IsRegistered, OnRegisterClosed, OnRegisterOpenFailed)" not in compact(sources["CollectiblesRuntimeModule.cs"]):
        errors.append("登记格子没开出来时，模块要提示玩家并回到收藏册（OnRegisterOpenFailed）")
    book_refresh = compact(method_body(sources["CollectionBookView.cs"], "private void Refresh()"))
    if "module.RegisterBlockedReason" not in book_refresh or "回到基地才能登记" in book_refresh.replace('L10n.T("回到基地才能登记。", "Register at base.")', ""):
        errors.append("收藏册页脚要写真实的登记不可用原因（RegisterBlockedReason），人在基地时不能只写「回到基地才能登记」")
    if "renderedCanRegister != canRegister" not in compact(method_body(sources["CollectionBookView.cs"], "private void Update()")):
        errors.append("收藏册开着时能不能登记变了要重刷，按钮和页脚不能停在打开那一刻")
    # 关闭钮轴心在中心：中心离右沿至少半个按钮宽，否则「×」伸出面板描边（2026-11-19 实机反馈）。
    close = re.search(r'CreateButton\("CloseButton",[^;]*?new Vector2\(1f, 0\.5f\),\s*new Vector2\(([^,]+), 0f\)', sources["CollectionBookView.cs"], re.S)
    if not close or "Padding + CloseSize * 0.5f" not in close.group(1):
        errors.append("收藏册关闭钮要右沿对齐页边距（x = -(Padding + CloseSize * 0.5f)），不能伸出面板描边")

    # 8. 6–8 档格子边框
    frame_src = sources.get("CollectibleSlotFrame.cs", "")
    patch_src = compact(sources.get("CollectibleSlotFramePatch.cs", ""))
    if "[HarmonyPatch(typeof(ItemDisplay), nameof(ItemDisplay.Setup), new Type[] { typeof(Item) })]" not in patch_src \
            or "[HarmonyPostfix]" not in patch_src \
            or "CollectibleSlotFrame.BindItemDisplay(__instance, target, ___icon, ___selectionIndicator);" not in patch_src:
        errors.append("格子边框只能挂 ItemDisplay.Setup(Item) 的后置补丁，并把图标与选中高亮交给 BindItemDisplay")
    for name, text in sources.items():
        # 正则而非子串：全限定名 typeof(Duckov.UI.ItemDisplay) 也算（反向探针实测子串会漏）。
        if name != "CollectibleSlotFramePatch.cs" and re.search(r"typeof\(\s*(?:global::)?(?:[\w.]+\.)?ItemDisplay\s*\)", text):
            errors.append(name + " 不能另挂 ItemDisplay 补丁：格子边框只有 CollectibleSlotFramePatch 一处接点")
    tier = compact(method_body(frame_src, "internal static int FrameTier(int quality)"))
    for token in ("if (quality >= 8) return 3;", "if (quality == 7) return 2;", "return quality == 6 ? 1 : 0;"):
        if token not in tier:
            errors.append("FrameTier 必须只给 6、7、8 档边框（owner 拍板）：缺少 " + token)
    # owner 2026-11-19 追加：金色才是最高级。8 档主色必须是金（红满、绿高、蓝低），7 档不能比 8 档更金，任何一档都不能偏紫。
    glow_decl = re.search(r"Color\[\]\s+TierGlow\s*=\s*\{(.*?)\};", frame_src, re.S)
    glow = [tuple(float(v) for v in m) for m in
            re.findall(r"new Color\(\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*[\d.]+f\s*\)", glow_decl.group(1))] if glow_decl else []
    if len(glow) != 4:
        errors.append("TierGlow 必须按边框档写死 4 项（下标 0 不用），不在运行时查官方样式")
    else:
        r8, g8, b8 = glow[3]
        if not (r8 >= 0.95 and g8 >= 0.75 and b8 <= 0.5):
            errors.append("8 档主色必须是金色（owner：金色才是最高级）")
        if glow[2][1] > g8:
            errors.append("7 档不能比 8 档更金：档次越高越金")
        for tier, (r, g, b) in enumerate(glow[1:], 1):
            if b > r or b > g + 0.2:
                errors.append("边框第 %d 档主色偏紫 / 偏蓝（owner：紫色不够高端）" % tier)
    create = compact(method_body(frame_src, "private static CollectibleSlotFrame Create("))
    if "under.SetSiblingIndex(before.GetSiblingIndex());" not in create or "over.SetSiblingIndex(after.GetSiblingIndex() + 1);" not in create:
        errors.append("金框层要插在官方选中高亮之前、扫光层插在图标之后（否则金框盖住选中状态）")
    new_image = compact(method_body(frame_src, "private static Image NewImage("))
    if "image.raycastTarget = false;" not in new_image or "if (sprite == null) image.enabled = false;" not in new_image:
        errors.append("边框图元不能吃点击，贴图缺失时不能画成白方块")
    if "ignoreLayout = true;" not in compact(method_body(frame_src, "private static RectTransform NewRoot(")):
        errors.append("边框根物体必须不参与官方格子的布局")
    tick = compact(method_body(frame_src, "private void Tick()"))
    for token in ("CollectiblesRuntimeModule.Current == null", "display.Target != target", "BossRushUI.IsGamePaused()", "IsContentVisible()"):
        if token not in tick:
            errors.append("格子边框每帧必须先核对模块在场、格子没换物品、暂停与待搜状态：缺少 " + token)
    if "Time.deltaTime" in frame_src:
        errors.append("格子边框走 unscaled 时间（收藏册等模态界面会把 timeScale 置 0）")
    deliver_body = compact(method_body(sources["CollectibleLootRuntime.cs"], "private void TryDeliver("))
    fresh_at = deliver_body.find("CollectibleSlotFrame.MarkFreshDrop(item);")
    if fresh_at < 0 or fresh_at < deliver_body.find("ledger.MarkDelivered();"):
        errors.append("出货标记必须在交付成功（MarkDelivered）之后打")
    if "CollectibleSlotFrame.ResetStaticCaches();" not in compact(method_body(sources["CollectiblesRuntimeModule.cs"], "public override void OnDestroy()")):
        errors.append("模块销毁时必须清理格子边框（贴图、活着的边框与出货标记）")
    if "CollectibleSlotFrame.ClearFreshDrops();" not in compact(method_body(sources["CollectiblesRuntimeModule.cs"], "public override void OnSceneLoaded(SceneRuntimeContext context)")):
        errors.append("过图必须清掉没露面的出货标记")
    card = compact(method_body(sources["CollectionBookView.cs"], "private GameObject CreateCard("))
    if "if (registered && CollectibleSlotFrame.HasFrame(def.Quality))" not in card or "CollectibleSlotFrame.AttachToCard(" not in card:
        errors.append("收藏册只给已登记的 6–8 档卡片挂边框")

    # 9. 陈列模型
    models = sources.get("CollectibleShowcaseModels.cs", "")
    show_patch = compact(sources.get("CollectibleShowcasePatch.cs", ""))
    if "[HarmonyPatch(typeof(ItemGraphicInfo), nameof(ItemGraphicInfo.CreateAGraphic), new Type[] { typeof(Item), typeof(Transform), typeof(bool), typeof(bool) })]" not in show_patch \
            or "[HarmonyPrefix]" not in show_patch or "CollectibleShowcaseModels.EnsureGraphic(item);" not in show_patch:
        errors.append("收集品模型只能在 ItemGraphicInfo.CreateAGraphic 前置里按需挂（陈列柜、地上掉落都经它）")
    ensure = compact(method_body(models, "internal static void EnsureGraphic(Item item)"))
    for token in ("CollectiblesRuntimeModule.Current == null", "CollectibleCatalog.Get(item.TypeID)", "item.ItemGraphic != null"):
        if token not in ensure:
            errors.append("EnsureGraphic 必须只给还没有模型的收集品挂、模块不在时不动：缺少 " + token)
    for name, text in sources.items():
        # global:: 前缀也算（反向探针实测 typeof(global::ItemGraphicInfo) 曾漏过）。
        if name not in ("CollectibleShowcasePatch.cs",) and re.search(r"typeof\(\s*(?:global::)?(?:[\w.]+\.)?ItemGraphicInfo\s*\)", text):
            errors.append(name + " 不能另挂 ItemGraphicInfo 补丁：收集品模型只有 CollectibleShowcasePatch 一处接点")
    if "CreateAGraphic(" in models:
        errors.append("收集品模型模块自己不调 CreateAGraphic：模型只交给官方去生成")
    if "OfficialMaterialTemplateTypeId = 111;" not in compact(models):
        errors.append("模型材质复制官方奖杯（TypeID 111）的 SodaLit 材质，不在运行时 Shader.Find")
    if "Shader.Find" in models or "LoadImage" in models:
        errors.append("收集品模型不 Shader.Find、不在运行时解码图片（贴图离线压成 DXT1）")
    if "CollectibleShowcaseModels.ResetStaticCaches();" not in compact(method_body(sources["CollectiblesRuntimeModule.cs"], "public override void OnDestroy()")):
        errors.append("模块销毁时必须清理收集品模型模板、网格、贴图与材质")
    bat = (ROOT / "compile_official.bat").read_text(encoding="utf-8", errors="replace")
    if 'xcopy /Y /I "Assets\\Collectibles\\Models\\*.brcm"' not in bat:
        errors.append("compile_official.bat 要部署 Assets\\Collectibles\\Models\\*.brcm，否则游戏里读不到模型")

    # 10. 陈列 / 地面特效（6–8 档模型：光晕、符文环、光柱、金点、星芒、扫光环、点光；8 档浮空自转）
    fx_src = sources.get("CollectibleShowcaseFx.cs", "")
    if not fx_src:
        errors.append("缺少 CollectibleShowcaseFx.cs（6–8 档收集品模型的陈列 / 地面特效）")
    else:
        if "return CollectibleSlotFrame.FrameTier(quality);" not in compact(method_body(fx_src, "internal static int Tier(int quality)")):
            errors.append("陈列特效档位必须复用格子边框的 FrameTier（只有 6、7、8 档）")
        if "CollectibleShowcaseFx.Tier(quality) <= 0) return;" not in compact(method_body(fx_src, "internal static void AttachToTemplate(GameObject model, int quality)")):
            errors.append("只给 6–8 档模型模板挂特效锚点")
        if "CollectibleShowcaseFxAnchor.AttachToTemplate(model, def.Quality);" not in compact(method_body(models, "private static ItemGraphicInfo BuildModelTemplate(CollectibleDefinition def)")):
            errors.append("特效锚点要在建模型模板时挂到 Model 上（随官方 CreateAGraphic 复制到每个实例）")
        for name, text in sources.items():
            if name != "CollectibleShowcaseFx.cs" and re.search(r"AddComponent<\s*(?:global::)?(?:[\w.]+\.)?CollectibleShowcaseFx(?:Anchor)?\s*>", text):
                errors.append(name + " 不能另挂陈列特效组件：锚点只经 AttachToTemplate、特效根只经 Create")
        # §4.12：锚点没有每帧方法，没进镜头零成本；进了镜头才建；出镜头由特效根自己的 Update 停用。
        anchor_class = method_body(fx_src, "internal sealed class CollectibleShowcaseFxAnchor : MonoBehaviour")
        if not anchor_class or re.search(r"\bvoid\s+(?:Update|LateUpdate|FixedUpdate|OnGUI)\s*\(", anchor_class):
            errors.append("特效锚点不能有每帧方法：模型没进镜头时不能有任何每帧成本（§4.12）")
        set_visible = compact(method_body(fx_src, "private void SetVisible(bool visible)"))
        guard_at, create_at = set_visible.find("if (!visible) return;"), set_visible.find("fx = CollectibleShowcaseFx.Create(")
        if fx_src.count("CollectibleShowcaseFx.Create(") != 1 or guard_at < 0 or create_at < 0 or guard_at > create_at:
            errors.append("陈列特效只能在模型进了镜头时才建（SetVisible(true) 一处）")
        if "SetVisible(modelRenderer.isVisible);" not in compact(method_body(fx_src, "private void Start()")) \
                or "SetVisible(true);" not in compact(method_body(fx_src, "private void OnBecameVisible()")):
            errors.append("锚点按 Renderer.isVisible / OnBecameVisible 决定建不建特效")
        for sig in ("private void OnBecameInvisible()", "private void OnDisable()"):
            body = method_body(fx_src, sig)
            if body and ("SetActive" in body or "Show(" in body):
                errors.append(sig + " 里不能改特效根的激活状态：父物体停用 / 销毁途中同步发来，改兄弟物体会报错")
        fx_tick = compact(method_body(fx_src, "private void Tick()"))
        for token in ("CollectiblesRuntimeModule.Current == null", "!modelRenderer.isVisible", "BossRushUI.IsGamePaused()", "Time.unscaledDeltaTime"):
            if token not in fx_tick:
                errors.append("陈列特效每帧要先核对模块在场、模型仍在镜头里、暂停，并走 unscaled 时间：缺少 " + token)
        if "Time.deltaTime" in fx_src or "useUnscaledTime = true;" not in fx_src:
            errors.append("陈列特效与粒子都走 unscaled 时间（陈列柜镜头、拍照模式照样动），暂停菜单另行暂停")
        if "gameObject.SetActive(false);" not in compact(method_body(fx_src, "internal void Show(bool on)")):
            errors.append("出镜头时特效根要整棵停用（粒子、面片、灯、Update 一起停）")
        # 材质：只用共享特效材质，不改共享材质本身；URP 粒子着色器不可用就不画（owner：不做降级）。
        if "Shader.Find" in fx_src or re.search(r"new\s+Material\s*\(\s*Shader", fx_src):
            errors.append("陈列特效不 Shader.Find、不自建着色器材质，只用 BossRushFxMaterials / BossRushFxKit 的共享材质")
        if "if (!BossRushFxMaterials.SupportsAdditive)" not in compact(method_body(fx_src, "private bool Resolve()")):
            errors.append("URP 粒子着色器不可用时陈列特效整套不画（不退成半透明）")
        if any(target != "block" for target in re.findall(r"(\w+)\.SetVector\(", fx_src)):
            errors.append("陈列特效调色只写 MaterialPropertyBlock，不能改共享材质")
        if "GetComponentInParent<InteractablePickup>()" not in compact(method_body(fx_src, "private bool Resolve()")):
            errors.append("地上 / 陈列只看父链上有没有 InteractablePickup（不做场景查找）")
        # 配色：越高越金，任何一档都不偏紫偏蓝（owner：金色才是最高级）。
        for array_name in ("TierMain", "TierLight"):
            decl = re.search(r"Color\[\]\s+%s\s*=\s*\{(.*?)\};" % array_name, fx_src, re.S)
            colors = [tuple(float(v) for v in m) for m in
                      re.findall(r"new Color\(\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*[\d.]+f\s*\)", decl.group(1))] if decl else []
            if len(colors) != 4:
                errors.append(array_name + " 必须按档写死 4 项（下标 0 不用），不在运行时查官方样式")
                continue
            for index, (r, g, b) in enumerate(colors[1:], 1):
                if not (r >= g >= b):
                    errors.append("%s 第 %d 档偏紫 / 偏蓝 / 偏绿：必须是暖色（红 ≥ 绿 ≥ 蓝）" % (array_name, index))
            if not (colors[1][1] <= colors[2][1] <= colors[3][1]):
                errors.append(array_name + " 必须越高越金（绿分量逐档不减）")
            r8, g8, b8 = colors[3]
            if not (r8 >= 0.95 and g8 >= 0.75 and b8 <= 0.6):
                errors.append(array_name + " 8 档必须是金色")
        # 逐档更强，只有 8 档浮空。
        looks_decl = re.search(r"Look\[\]\s+Looks\s*=\s*\{(.*?)\};", fx_src, re.S)
        entries = re.findall(r"new Look \{(.*?)\}", looks_decl.group(1), re.S) if looks_decl else []

        def look_value(entry, field):
            m = re.search(r"\b%s = ([\w.]+?)f?\s*[,}]" % field, entry + "}")
            return m.group(1) if m else None

        if len(entries) != 3:
            errors.append("陈列特效 Looks 必须按 6、7、8 档写 3 项")
        else:
            if [look_value(e, "Float") for e in entries] != ["false", "false", "true"]:
                errors.append("只有 8 档浮空（owner：超高品质的再加上缓慢浮空）")
            for field, rising in (("MoteRate", True), ("LightIntensity", True), ("HaloMax", True), ("BeamGround", True), ("SheenPeriod", False)):
                values = [float(look_value(e, field) or "nan") for e in entries]
                ordered = values[0] < values[1] < values[2] if rising else values[0] > values[1] > values[2]
                if not ordered:
                    errors.append("陈列特效 %s 必须一档比一档强：%s" % (field, values))
            if [int(look_value(e, "Runes") or -1) for e in entries] != [0, 1, 2]:
                errors.append("符文环 6 档没有、7 档一圈、8 档两圈")
        # 清理：模块销毁时拆光、还原模型；锚点随实例销毁时注销。
        if "CollectibleShowcaseFx.ResetStaticCaches();" not in compact(method_body(models, "internal static void ResetStaticCaches()")):
            errors.append("收集品模型清理时必须先拆掉活着的陈列特效（CollectibleShowcaseFx.ResetStaticCaches）")
        fx_reset = compact(method_body(fx_src, "internal static void ResetStaticCaches()"))
        for token in ("CollectibleShowcaseFxAnchor.TeardownAll();", "Destroy(beamMaterial);", "Destroy(beamTexture);", "Destroy(flatQuad);"):
            if token not in fx_reset:
                errors.append("陈列特效 ResetStaticCaches 缺少：" + token)
        if "Teardown(false);" not in compact(method_body(fx_src, "private void OnDestroy()")):
            errors.append("锚点随实例销毁（捡起、换位、切图）时要注销并拆掉特效")
        fx_dispose = compact(method_body(fx_src, "internal void Dispose()"))
        if "model.localPosition = restPosition;" not in fx_dispose or "model.localRotation = restRotation;" not in fx_dispose:
            errors.append("拆陈列特效时要把浮空的模型放回原位")

    for error in errors:
        print("  - " + error)
    print("CollectiblesCatalogGuard: " + ("FAIL" if errors else "PASS"))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
