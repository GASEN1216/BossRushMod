"""天空岛正式玩家入口、探索地图与手柄服务接线；不替代真实船点/UI smoke。"""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
NEWLINE = chr(10)


def main():
    def read(path):
        return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))

    runtime = read("DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs")
    session = read("DebugAndTools/SkyIsland/SkyIslandSession.cs")
    guide = read("DebugAndTools/SkyIsland/SkyIslandGuideInteractable.cs")
    registration = read("Common/Lifecycle/BossRushRuntimeModuleRegistration.cs")
    errors = []
    for source, label, tokens in (
        (runtime, "船点 owner", ["owner.IsBaseHubBoatInteractable(candidate)",
            "AddSubInteractable<SkyIslandDepartureInteractable>", "SkyIslandSession.Enter(owner, Report)",
            "LevelManager.OnAfterLevelInitialized += OnLevelReady", "LevelManager.OnAfterLevelInitialized -= OnLevelReady",
            "private bool subscribed", "attempts = 12", "attempts--", "boatGroup.Remove(departure)"]),
        (guide, "常规航路图交互", ['"Search_A_02"', '"Search_B_02"', "session.OpenMap()", "session.CycleLighting()",
            "BossRushBuildingInteractableBase", "LocalizationHelper.InjectLocalization"]),
        (session, "会话接线", ["SkyIslandGuideInteractable.Attach(", "StorySummary", "OpenMap()"]),
        # 地图走官方那一套：场景包里带 MiniMapSettings，Mod 只保留一个同样的入口。
        (session, "官方地图入口", ["MiniMapView.Show()"]),
        # CR-2026-09-08-002：撤离只能走撤离圈。地图负责指路，判定唯一留在 Session。
        (session, "撤离唯一判定", ["private const float ExtractionRadius = 2.5f", "private const float ExtractionHold = 3f",
            "private bool IsInsideExtraction(out Transform marker)", "internal Transform BellExitIfUnlocked()",
            "IsInsideExtraction(out extraction)", "extractionStarted = Time.unscaledTime",
            "ExtractionHold - (Time.unscaledTime - extractionStarted)", "extractionStarted = -1"]),
    ):
        for token in tokens:
            if token not in source:
                errors.append(f"{label} 缺少 {token}")
    # 反例断言：打开地图不得夹带任何返航/关闭动作，撤离判定必须唯一留在 Update 的撤离圈里。
    open_map = session.split("internal void OpenMap()", 1)[1].split(NEWLINE + "        }", 1)[0]
    for token in ("Close(", "returnToBase", "ReturnToBase"):
        if token in open_map:
            errors.append("打开地图不得提供绕过撤离圈的返航入口：" + token)
    if session.count("IsInsideExtraction(") != 2:
        errors.append("撤离判定必须只有一个定义和一个 Update 调用点")
    if "BellExitIfUnlocked" not in session:
        errors.append("钟庭撤离点未接入会话")
    update_body = session.split("private void Update()", 1)[1]
    if "IsInsideExtraction(out extraction)" not in update_body:
        errors.append("Update 未调用撤离判定，倒计时可能被旁路")
    if registration.count("new SkyIslandRuntimeModule()") != 1:
        errors.append("天空岛 RuntimeModule 必须唯一注册")
    # 缺场景包时不得给玩家一个必然失败的入口：入口与 CanEnter 都要先问一次资源是否部署。
    for label, text in (("船点 owner", runtime), ("会话准入", session)):
        if "SkyIslandRaidLease.IsBundleDeployed()" not in text:
            errors.append(label + " 未在缺包时提前拒绝")
    lease = read("DebugAndTools/SkyIsland/SkyIslandRaidLease.cs")
    if "internal static bool IsBundleDeployed()" not in lease:
        errors.append("缺少场景包可用性检查")

    # ---- 与原版出击/撤离手感一致（2026-09-09 审核）----
    # 进图：官方 MapSelectionView.LoadTask 用 clickToConinue: true，读条完停在「点击继续」。
    begin = lease.split("internal async void BeginLoad()", 1)[1].split("\n        }", 1)[0]
    if "clickToConinue: true" not in begin:
        errors.append("进岛必须与官方出击一致使用 clickToConinue: true，否则玩家会被直接甩进图里")
    # 撤离：官方 SceneLoaderProxy 的顺序是
    # NotifyEvacuated -> ClosureView(1s) -> 幕布换 EvacuateScreenScene -> LoadScene。
    ret = lease.split("internal async void ReturnToBase(bool evacuated)", 1)[1].split("\n        }\n", 1)[0]
    for token, why in (
        ("LevelManager.Instance.NotifyEvacuated(", "撤离必须先发官方撤离事件（顺带让主角在结算期间无敌）"),
        ("ClosureView.ShowAndReturnTask(1f)", "撤离必须显示官方撤离结算画面，原版每张图都有"),
        ("GameplayDataSettings.SceneManagement.EvacuateScreenScene", "撤离幕布必须换成官方撤离画面，而不是普通读条"),
    ):
        if token not in ret:
            errors.append(why + "：缺 " + token)
    if "clickToConinue: false" not in ret:
        errors.append("返航必须与官方 SceneLoaderProxy 一致使用 clickToConinue: false")
    # 结算画面是表现层，失败也必须继续返航，不能把玩家留在图里。
    # 按 catch 块本身判断：只在整段里找 "curtain = null" 会被声明行满足，改成 throw 也照样绿。
    if "catch (Exception e)" not in ret:
        errors.append("撤离结算必须包在 try/catch 里")
    else:
        handler = ret.split("catch (Exception e)", 1)[1].split("}", 1)[0]
        if "curtain = null;" not in handler:
            errors.append("撤离结算画面失败时必须清掉幕布并继续返航")
        if "throw" in handler:
            errors.append("撤离结算失败不得向外抛，否则玩家会被留在图里")

    # 账户门控读的是 accountAvailable，不是 SaveCharacter：
    # 后者是「是否把主角写回存档」，raid 图里同样为 true（官方合同还硬性要求 true），拿它当门控恒真。
    services = read("DebugAndTools/SkyIsland/SkyIslandServices.cs")
    if "LevelConfig.Instance.AccountAvailable" not in services:
        errors.append("岛上付费服务必须用 LevelConfig.accountAvailable 判断账户可用性")
    if "LevelConfig.SaveCharacter" in services:
        errors.append("SaveCharacter 不是账户门控，raid 图里恒为 true")
    entry_guard = runtime.split("if (departure != null || attempts <= 0", 1)[1].split("foreach (InteractableBase", 1)[0]
    if "IsBundleDeployed()" not in entry_guard:
        errors.append("缺包检查必须早于船点交互查找，否则招牌与公告仍会出现")
    # CR-2026-09-10-005：官方出击船点是 Base_SceneV2_Sub_01 的
    # Envir/Prfb_BoatBetweenBaseAndFarm/Interact，那是一张按需加载的子场景——
    # 玩家站在 Base_SceneV2 主城区时它本来就不在场，重试窗口跑空是**正常状态**。
    # 所以跑空必须分两种情况：没见过船点只发 DevLog，见过却挂不上才发 CRITICAL。
    # 必须按**结构**判断：只找 boatSeen 这个 token，会被「声明了但从不赋值」骗过
    # （那样 CRITICAL 变成死代码，真故障反而永远不报）。
    scan_tail = runtime.split("foreach (InteractableBase", 1)[1]
    if "if (attempts != 0) return;" not in scan_tail:
        errors.append("船点重试窗口跑空的分支结构已变，CR-2026-09-10-005 的判据失效")
    else:
        loop_body, tail = scan_tail.split("if (attempts != 0) return;", 1)
        if "boatSeen = true;" not in loop_body:
            errors.append("船点扫描未在命中时记录 boatSeen，跑空时无法区分「子场景没加载」与「注入失败」")
        early = tail.find("if (!boatSeen)")
        critical = tail.find('CriticalLog("sky-island-entry-missing"')
        if early < 0 or critical < 0 or early > critical:
            errors.append("没见到船点时不得发 CRITICAL：sky-island-entry-missing 必须排在 if (!boatSeen) 提前返回之后")
    for label, source in (("正式入口", runtime), ("会话", session)):
        if "DevModeEnabled" in source or "ArenaPrototypeSession.CanEnter(" in source:
            errors.append(f"{label} 仍依赖开发开关")

    # ---- 2026-09-09 复审：撤离圈与返航要与官方 CountDownArea / SceneLoaderProxy 同语义 ----
    # 官方 CountDownArea.Update 在任何 View 打开时不推进倒计时；推进分支必须带同一个门。
    advance_line = next((line for line in update_body.splitlines()
                         if "View.ActiveView == null" in line and "insideExtraction" in line), "")
    if not advance_line:
        errors.append("撤离计时必须在官方界面打开时暂停（推进分支缺 View.ActiveView == null）")
    # 官方口径是「不推进」而不是「清零」：人还在圈里、只是开着界面时，读条要冻结在原处。
    # 旧实现直接落到 extractionStarted = -1，开一下背包就把 3 秒读条清零。
    # unscaledTime 在 timeScale=0 下照走，所以冻结只能靠顺延起点。
    #
    # 必须按**结构**判断：剧情面板那一支也有同一句顺延，只在整段 Update 里找这个 token
    # 等于没断言——删掉撤离圈这一支，另一处仍然命中。
    freeze_line = next((line for line in update_body.splitlines()
                        if "insideExtraction" in line and "extractionStarted >= 0" in line), "")
    if not freeze_line:
        errors.append("官方界面打开时撤离读条必须冻结而不是清零（缺 insideExtraction 的顺延分支）")
    else:
        freeze_body = update_body.split(freeze_line, 1)[1].split("}", 1)[0]
        if "extractionStarted += Time.unscaledDeltaTime;" not in freeze_body:
            errors.append("撤离读条的冻结分支没有顺延起点，读条会在界面打开时自己走完")
    if "extractionStarted = -1" not in update_body:
        errors.append("离开撤离圈必须真正清零撤离读条")
    # 官方 SceneLoaderProxy.LoadScene 先 DisableInput 再派发；封锁源必须是岛场景内对象，
    # 挂到 DontDestroyOnLoad 的宿主上会在回基地后永久锁死输入（InputManager 只在源销毁/失活时解封）。
    if "private void BlockInputForReturn()" not in session:
        errors.append("缺少返航输入封锁 BlockInputForReturn")
    else:
        block_body = session.split("private void BlockInputForReturn()", 1)[1].split("private void DispatchReturnIfReady()", 1)[0]
        for token in ("block.transform.SetParent(root.transform, false);", "InputManager.DisableInput("):
            if token not in block_body:
                errors.append("返航输入封锁缺 " + token)
        for token in ("host.gameObject", "DontDestroyOnLoad", "SetParent(host", "SetParent(transform"):
            if token in block_body:
                errors.append("返航输入封锁不得挂在宿主或跨场景对象上：" + token)
        close_body = session.split("internal void Close(bool restorePlayer, string reason)", 1)[1].split("private void DispatchReturnIfReady()", 1)[0]
        if close_body.find("BlockInputForReturn();") < 0 or close_body.find("BlockInputForReturn();") > close_body.find("DispatchReturnIfReady();"):
            errors.append("Close 必须先封锁输入再派发返航")
        cleanup_body = session.split("private void Cleanup(string reason)", 1)[1].split("private void OnDestroy()", 1)[0]
        if "Destroy(returnInputBlock)" not in cleanup_body:
            errors.append("Cleanup 必须销毁返航输入封锁源")
    # 官方 SceneLoader 同步拒绝（已在切图 / 身份未登记）时 LoadFinished 立刻为 true 而 root 永不出现，
    # 等 root 的循环必须观察 LoadFinished 并按「未起航」清理，不能空等 120 秒再多做一次回基地加载。
    root_wait = session.split("lease.BeginLoad();", 1)[1].split("if (assemblyError != null)", 1)[0]
    if "lease.LoadFinished" not in root_wait or "loadStarted = false;" not in root_wait:
        errors.append("等待场景根的循环必须在官方加载器拒绝时立即失败并按未起航清理")
    for path in ("WikiContent/zh/map__sky_island.md", "WikiContent/en/map__sky_island.md"):
        if not (ROOT / path).exists():
            errors.append("缺少玩家操作说明：" + path)
    print("SkyIslandPlayerEntryGuard: " + ("FAIL\n" + "\n".join(errors) if errors else "PASS"))
    return bool(errors)


if __name__ == "__main__":
    raise SystemExit(main())
