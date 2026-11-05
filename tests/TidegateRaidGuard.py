"""潮闸镇独立出击的结构不变式：身份登记走多图共用桥、官方加载 / 撤离顺序、合同与注入先后、
返航封输入挂在本图、刷怪安全网、撤离读条冻结、与天空岛 / 试验场 / F3 互斥。"""
import re
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(rel):
    return clean_source((ROOT / rel).read_text(encoding="utf-8-sig"))


def body(source, signature):
    if signature not in source:
        return None
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for i in range(opening, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[opening:i + 1]
    return None


def main():
    errors = []

    def need(text, label, *tokens):
        for token in tokens:
            if text is None or token not in text:
                errors.append(label + " 缺少 " + token)

    module = read("Tidegate/TidegateRuntimeModule.cs")
    scene = read("Tidegate/TidegateRaidScene.cs")
    session = read("Tidegate/TidegateSession.cs")
    lease = read("Utilities/RaidScenes/ModRaidLease.cs")
    enemies = read("Tidegate/TidegateEnemies.cs")
    extraction = read("Tidegate/TidegateExtraction.cs")
    world = read("Tidegate/TidegateWorld.cs")

    # 身份只登记、不自己装补丁：官方加载状态机的转译补丁全仓一份（SkyIslandSceneReferenceBridgeGuard 另查）。
    need(module, "模块生命周期", "ModRaidSceneBridge.Register(TidegateRaidScene.Identity)", "ModRaidSceneBridge.Unregister(TidegateRaidScene.Identity)")
    if "new Harmony(" in module + scene + session:
        errors.append("潮闸镇不得自己装 Harmony 补丁，场景身份只经 ModRaidSceneBridge 登记")
    need(scene, "场景身份", 'SceneId = "BossRush_Tidegate"', 'ScenePath = "Assets/Tidegate/TidegateRaid.unity"',
         'BundleRelativePath = "Assets/arenas/tidegate_raid"', 'ServicesRootName = "TidegateLevel"', 'WorldRootName = "TidegateWorld"')

    # 进图与原版一致：读条停在「点击继续」；撤离照官方 SceneLoaderProxy 顺序。
    begin = body(lease, "internal async void BeginLoad()")
    need(begin, "进图", "clickToConinue: true")
    ret = body(lease, "internal async void ReturnToBase(bool evacuated)")
    need(ret, "撤离返航", "LevelManager.Instance.NotifyEvacuated(", "ClosureView.ShowAndReturnTask(1f)",
         "GameplayDataSettings.SceneManagement.EvacuateScreenScene", "clickToConinue: false")
    if ret and "catch (Exception e)" in ret:
        handler = ret.split("catch (Exception e)", 1)[1].split("}", 1)[0]
        if "curtain = null;" not in handler or "throw" in handler:
            errors.append("撤离结算画面失败时必须清掉幕布并继续返航")

    # 合同：天气 / 起始 Buff 注入早于激活前合同，合同早于激活服务根（CR-2026-09-10-002 / CR-2026-09-08-005）。
    loaded = body(lease, "private void OnSceneLoaded(Scene scene, LoadSceneMode mode)") or ""
    order = [loaded.find("config.timeOfDayConfig = timeOfDay"), loaded.find("ModRaidOfficialContract.VerifyBeforeActivation("),
             loaded.find("services.SetActive(true)")]
    if min(order) < 0 or not order[0] < order[1] < order[2]:
        errors.append("租约必须先注入天气与起始 Buff、再过激活前合同、最后激活服务根")
    if "MarkInitialized();" not in (body(lease, "private void TryRelease()") or ""):
        errors.append("释放路径必须归还初始化令牌")
    need(session, "会话合同", "ModRaidOfficialContract.Verify(profile, entryScene, out contractError)", "lease.MarkInitialized()",
         "lease.Abort(", "LevelManager.RegisterWaitForInitialization(this)", "LevelManager.UnregisterWaitForInitialization(this)")

    # 返航封输入：封锁源挂在本图世界根下，随场景卸载失效；挂到常驻宿主会让回基地后输入永久锁死。
    block = body(session, "private void BlockInputForReturn()")
    need(block, "返航封输入", "block.transform.SetParent(root.transform, false)", "InputManager.DisableInput(block)")
    if block and "host" in block:
        errors.append("返航输入封锁源不得挂在 Mod 宿主上")

    # 撤离读条：官方界面打开时冻结不清零（同官方 CountDownArea）。
    if "View.ActiveView == null ? Time.deltaTime : 0f" not in session:
        errors.append("撤离读条必须在官方界面打开时冻结")
    need(extraction, "撤离判定", "SkyIslandExtractionCountdown(", 'Add("road"', "delegate { return true; }")

    # 刷怪安全网（根 AGENTS §4.5）与本图导航隔离、距离休眠解除（contracts §7.1）。
    need(enemies, "刷怪", "created.SetTeam(Teams.wolf)", "SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(created)",
         "seeker.graphMask = navigation.Mask", "clone.setActiveByPlayerDistance = false", "MinSpawnDistance = 25f")

    # 着色器不降级：资源不对就硬失败（owner 定）。
    need(world, "着色器", "!shader.isSupported", "throw new InvalidOperationException(\"当前显卡不支持潮闸镇着色器")
    if re.search(r"SodaCraft/|FindCompatibleShader", world):
        errors.append("潮闸镇不得退回官方兼容着色器")

    # 互斥：天空岛、试验场、完整 F3 与潮闸镇互相认得。
    need(session, "潮闸镇准入", "GetComponent<SkyIslandSession>() != null", "GetComponent<ArenaPrototypeSession>() != null",
         "F3GameplayValidationRunner.IsRunning")
    need(read("SkyIsland/SkyIslandSession.cs"), "天空岛准入", "GetComponent<TidegateSession>() != null")
    need(read("DebugAndTools/ArenaPrototype/ArenaPrototypeSession.cs"), "试验场准入", "GetComponent<TidegateSession>() != null")
    need(read("DebugAndTools/F3GameplayValidationRunner.cs"), "完整验收准入", "GetComponent<TidegateSession>() != null")

    # 游泳复用官方：Water 层水体 + CanSwim 修饰；修饰只属于这一趟，离图必须摘，否则跟着主角回基地。
    water = read("Tidegate/TidegateWater.cs")
    need(water, "水体与游泳", 'LayerMask.NameToLayer("Water")', "box.isTrigger = true", '"CanSwim"')
    need(body(water, "public void Dispose()"), "游泳修饰清理", "RuntimeStatModifierTracker.RemoveAll(swimRecords")
    need(session, "会话清理", "water.Dispose()", "devices.Dispose()", "loot.Dispose()", "enemies.Dispose()", "tide.Dispose()")
    # 机关读条：官方私有字段，AddComponent 出来恒为 0（contracts §7.1），写进去再读回。
    need(read("Tidegate/TidegateDevices.cs"), "机关读条", 'SetHiddenMember(this, "interactTime", seconds)', "InteractTime - seconds")

    # 剧情线：读过闸伯的信才挂竹筏与闸伯；信只送一次；只回收自己生成的闸伯（婚姻等别的 owner 在别处生成的不归这里）。
    story = read("Tidegate/TidegateStory.cs")
    need(module, "竹筏入口门控", "!TidegateStory.RouteUnlocked", "TidegateStory.TryDeliverLetter()", "TidegateStory.Bind(null)")
    deliver = body(story, "internal static void TryDeliverLetter()") or ""
    if "data.LetterDelivered" not in deliver or deliver.find("CourierService.QuickDeliverItems") > deliver.find("d.LetterDelivered = true"):
        errors.append("湿包裹必须先确认送达、再记「已送」，且已送过就不再送")
    need(story, "剧情存档", 'StorageKey = "BossRush_Tidegate_Story_v1"', "new BossRushSlotJsonStore<TidegateStoryData>")
    zhabo = read("Tidegate/TidegateZhabo.cs")
    despawn = body(zhabo, "internal static void Despawn()") or ""
    if "owned" not in despawn or "GetInstance(BlueprintId) == npc" not in despawn:
        errors.append("闸伯只能回收本模块生成的那一个")
    notes = read("Tidegate/TidegateNoteBridge.cs")
    if notes.find("notes.Add(note)") < 0 or notes.find("notes.Add(note)") > notes.find("NoteIndex.SetNoteDynamic(note)"):
        errors.append("官方笔记图鉴要先写 notes 列表再 SetNoteDynamic（只写字典界面列不出来）")

    # 2026-11-05 审查修复（三路独立审查）——
    # 丢信：送过没读、信又哪儿都找不到时补寄（每游戏日一次），信不可出售；否则这个档永远开不了航线。
    need(deliver, "补寄闸伯的信", "if (data == null || data.LetterRead", "TidegateInventory.OwnsAnywhere(BossRushItemIds.TidegateLetter) != false",
         "day == resendDay")
    need(read("Integration/Tidegate/TidegateItems.cs"), "信不可出售",
         'if (typeId == BossRushItemIds.TidegateLetter) EquipmentHelper.AddTagToItem(item, "NotSellable");')
    # 谢礼在基地：背包满了进仓库，不落地。
    need(body(story, "internal static bool GiveToPlayerOrStorage(int typeId)"), "交表谢礼", "ItemUtilities.SendToPlayer(item, false, true)")
    # 婚后配偶：交给婚姻接管并挪回主场景，不回收（口径同天空岛居民）。
    release = body(zhabo, "internal static void ReleaseIfMarried()") or ""
    need(release, "闸伯婚后交接", "AffinityManager.IsMarriedToPlayer(BlueprintId)", "owned = null;", "MultiSceneCore.MoveToMainScene(npc.gameObject)")
    if despawn.find("ReleaseIfMarried();") < 0 or despawn.find("ReleaseIfMarried();") > despawn.find("DuckNpcSpawner.Despawn(npc)"):
        errors.append("闸伯回收前必须先把已婚的交给婚姻接管")
    need(module, "闸伯婚后交接时机", "TidegateZhabo.ReleaseIfMarried();")
    # 图鉴镜像：剧情存档读不了时不拿默认值收回条目。
    need(body(notes, "internal static void Tick()"), "图鉴镜像写屏障", "!TidegateStory.Writable")
    # 刷怪：先吸到本图导航可走面，再贴地；Boss 不卷走；不给常驻透视锁定；灯塔引怪限时还原。
    spawn = body(enemies, "private async void Spawn(Slot slot)") or ""
    snap = spawn.find("navigation.TryNearestWalkable(position, out snapped)")
    if snap < 0 or snap > spawn.find("clone.CreateCharacterAsync("):
        errors.append("刷怪必须先吸到本图导航可走面（有刷怪点落在实心碰撞盒里）")
    if re.search(r"forceTracePlayerDistance\s*=\s*\d", enemies):
        errors.append("刷怪不得写死 forceTracePlayerDistance（官方 AI 在这个距离内无视视线锁定，等于透视）")
    need(body(enemies, "private void TickTide()"), "沙老大不卷走", 'slot.Kind != "boss" && !diggersOut')
    need(body(enemies, "private void EndLure()"), "灯塔引怪还原", "slot.Ai.forceTracePlayerDistance = slot.LureOriginal;")
    need(enemies, "灯塔引怪限时", "lureUntil = -1f; EndLure();")
    need(read("Tidegate/TidegateDevices.cs"), "灯塔引怪时长", "enemies.Lure(world.Lighthouse.position, LureRadius, RevealSeconds)")
    # 箱子：只有收起要等玩家走开，水退了马上放出。
    need(read("Tidegate/TidegateLoot.cs"), "箱子放出不等人", "if (flooded != point.Flooded && (!flooded || distance > KeepNearRange * KeepNearRange))")
    # 导航：只写变了的节点（A* setter 每写一次都交分层图重算）。
    walk = body(read("DebugAndTools/ArenaPrototype/ArenaPrototypeNavigation.cs"), "internal void SetWalkable(Func<Vector3, Vector3, Vector3, bool> walkable)")
    need(walk, "导航只写变化", "if (node.Walkable == next) return;", "if (changed) context.SetGraphDirty(graph);")
    # 撤离读条兜底：官方读条不在场时 HUD 文字读秒。
    need(session, "撤离读秒兜底", "hud.SetExtraction(extraction.HudFallback)")
    need(read("Tidegate/TidegateExtraction.cs"), "撤离读秒兜底", "bool shown = current.Countdown != null && current.Countdown.Show(held);")
    # 返航刷新基地环境光：撤离返航时会话组件到基地就绪后才销毁，要看「已收尾」而不是「组件在不在」（天空岛同修）。
    need(body(module, "private void OnLevelReady()"), "返航环境光", "if (session != null && !session.IsClosed) return;")
    need(body(read("SkyIsland/SkyIslandRuntimeModule.cs"), "private void OnLevelReady()"), "天空岛返航环境光", "(session == null || session.IsClosed)")

    # 2026-11-05 生产水准复审（CR-2026-11-05-017～028）——
    rules = read("Tidegate/TidegateTideRules.cs")
    loot = read("Tidegate/TidegateLoot.cs")
    devices = read("Tidegate/TidegateDevices.cs")
    tide_rt = read("Tidegate/TidegateTide.cs")
    update = body(session, "private void Update()") or ""
    cleanup = body(session, "private void Cleanup(string reason)") or ""
    build = body(session, "private IEnumerator Build()") or ""
    # 017 换相有字幕与警报：会话订阅并退订潮汐换相；涨潮警示色加警报；换相前提示每轮一次（15 秒涨潮警报、12 秒渡口要走）。
    need(build, "换相提示订阅", "tide.PhaseChanged += OnTidePhaseChanged;")
    need(cleanup, "换相提示退订", "tide.PhaseChanged -= OnTidePhaseChanged;")
    phase_changed = body(session, "private void OnTidePhaseChanged(TidegateTidePhase previous, TidegateTidePhase next)") or ""
    need(phase_changed, "换相字幕", "noticeShown = TidegateTideNotice.None;", "case TidegateTidePhase.Flood:", "sounds.Play(TidegateCue.Siren, null)")
    flood_case = phase_changed.split("case TidegateTidePhase.Flood:", 1)[-1].split("break;", 1)[0]
    if "), true);" not in flood_case:
        errors.append("涨潮字幕必须用警示色（Status 第二个参数 true）")
    need(body(session, "private void TickTideNotices()"), "换相前提示", "TidegateTide.Notice()", "notice == noticeShown")
    need(update, "换相前提示时机", "TickTideNotices();")
    need(rules, "提示时长", "FloodWarningSeconds = 15.0", "FerryLeavingSeconds = 12.0")
    # 018 目标行只放按进度变化的引导；每秒在变的潮位、倒计时放小标行。
    need(update, "目标行", "hud.SetObjective(Objective());", "hud.SetChips(FieldStatus());")
    objective = body(session, "private string Objective()") or ""
    if not objective or re.search(r"tide\.Chip\(|RealSecondsUntilNextPhase|\.Level\.ToString", objective):
        errors.append("HUD 目标行不得放潮位读数或倒计时（字一变就挂「目标更新」）")
    need(body(session, "private string FieldStatus()"), "小标行潮汐", "tide.Chip(carryingWatch)")
    # 019 可玩区四边补墙层碰撞（海侧原墙顶面低于高潮水位）；越界兜底按可玩区。
    need(body(world, "internal void ApplyLayers()"), "边界墙", "AddBoundary(wallLayer);")
    need(body(world, "private void AddBoundary(int wallLayer)"), "边界墙四边", '"North"', '"South"', '"East"', '"West"')
    need(world, "边界墙高度", "BoundaryBottom = -12f, BoundaryTop = 6f", "Rect.MinMaxRect(-90f, -70f, 90f, 70f)")
    need(update, "越界兜底", "TidegateWorld.OutsidePlayArea(position)")
    if "160f" in update:
        errors.append("越界兜底不得再用旧的 ±160 / ±150 m 范围")
    # 020 灯塔点亮后灯头进灯数组（自研着色器不吃延迟光源）；入夜手电每帧一格。
    need(body(devices, "private void LightBeam()"), "灯塔照地", "tide.SetBeacon(beam.position, BeaconRange, BeaconColor)")
    need(body(tide_rt, "private void PublishLamps()"), "灯塔灯位", "if (beaconOn)", "MaxLamps - 1 - count")
    need(body(tide_rt, "private void TickFlashlight()"), "手电", "player.FlashLight", "night >= FlashlightMinNight")
    need(body(tide_rt, "internal void Tick(float deltaTime)"), "手电每帧", "TickFlashlight();")
    # 021 声音：会话持有、每帧推进、清理时先停循环再销毁；提示音接到换相与机关；部署自检认得 Tidegate 目录。
    sounds_src = read("Tidegate/TidegateSounds.cs")
    need(build, "音效装配", "sounds = new TidegateSounds(world)")
    need(update, "音效推进", "sounds.Tick(position)")
    need(cleanup, "音效清理", "sounds.Dispose()")
    need(sounds_src, "音效目录", '"Assets", "Sounds", "Tidegate"', '"PostCustomSFX"', '"StopAll"')
    dispose_sounds = body(sounds_src, "public void Dispose()") or ""
    if dispose_sounds.find("StopAll(null, go);") < 0 or dispose_sounds.find("StopAll(null, go);") > dispose_sounds.find("Destroy(go)"):
        errors.append("潮闸镇音效必须先 StopAll 再销毁发声体（循环声不随 GameObject 停）")
    need(devices, "机关音效", "TidegateCue.Valve", "TidegateCue.Gate", "TidegateCue.Lighthouse", "TidegateCue.Horn")
    if "SetBonus SkyIsland Tidegate items lottery" not in (ROOT / "compile_official.bat").read_text(encoding="utf-8", errors="replace"):
        errors.append("compile_official.bat 的声音目录自检要包含 Tidegate")
    # 024 交互反馈：被淹的箱柜走近提示一次；缺手轮写进交互名；括号随语言；读信写不进存档时不说竹筏出现了。
    need(body(loot, "internal void Tick(CharacterMainControl player)"), "被淹箱子提示",
         "if (point.Flooded && !point.FloodHinted && distance <= FloodHintRange * FloodHintRange && announce != null)")
    need(body(devices, "private static string ValveLabel(int index)"), "阀门交互名", 'L10n.T("拧开阀门（", "Open the valve (")',
         'L10n.T(" · 缺手轮", " · handwheel missing")', 'L10n.T("）", ")")')
    need(body(story, "internal static void ReadLetter()"), "读信写入", "bool stored = !first || Mutate(", ": stored")
    # 025 地名按到访半径与滞回；「带着怀表」HUD、交表与金库同一判据（整棵物品树）。
    need(body(session, "private void UpdateRegion(Vector3 position)"), "地名滞回", "distance <= RegionRadius",
         "distance + RegionSwitchMargin < current")
    need(session, "地名半径", "RegionRadius = 24f, RegionSwitchMargin = 4f",
         "carryingWatch = TidegateInventory.Carries(player, BossRushItemIds.TidegateTideWatch)")
    need(body(story, "internal static bool TryDeliverWatch(CharacterMainControl player, out string reason)"), "交表判据",
         "TidegateInventory.FindCarried(player, BossRushItemIds.TidegateTideWatch)")
    need(module, "交表选项判据", "TidegateInventory.Carries(CharacterMainControl.Main, BossRushItemIds.TidegateTideWatch)")
    # 026 剧情箱柜一张表（布局 fixed 标签由导航检查脚本核对）；沉船保险柜放一件金库档值钱物；增援计数；守卫配枪；游泳隐蔽。
    for row in ('{ "LOOT_safe_68", "page1" }', '{ "LOOT_house_69", "page2" }', '{ "LOOT_house_70", "page3" }',
                '{ "LOOT_vault_71", "page4" }', '{ "LOOT_vault_71", "tidewatch" }', '{ "LOOT_locker_72", "wheel" }',
                '{ "LOOT_safe_77", "goldbar" }'):
        need(loot, "剧情箱柜表", row)
    need(body(loot, "internal static int PageIndexOf(string pointId)"), "日志页出处", 'HasFixed(pointId, "page" + (page + 1))')
    need(body(loot, "private void Build(Point point, CharacterMainControl player)"), "沉船保险柜",
         'if (HasFixed(point.Id, "goldbar"))', "SkyIslandLootTier.Starworks")
    nav_tool = (ROOT / "tools/build_tidegate_navigation.py").read_text(encoding="utf-8")
    if "story = story_containers(grid.layout)" not in nav_tool or "'Tidegate' / 'TidegateLoot.cs'" not in nav_tool:
        errors.append("导航检查脚本要核对剧情箱柜表与布局 fixed 标签")
    need(enemies, "刷怪计数", "if (inFlight > 0 || Time.time < nextTick) return;", "inFlight++;", "inFlight--;")
    if re.search(r"\bspawning\b", enemies):
        errors.append("刷怪节流不得退回布尔（海堤增援三连会提前清零）")
    need(spawn, "守卫配枪", 'if (slot.Kind == "guard") SkyIslandEnemyArmory.Arm(created, SkyIslandEnemyTier.Elite);')
    need(water, "游泳隐蔽", "ApplyStealth(player.Swimming);", '"VisableDistanceFactor"', "SwimVisibilityMultiplier = -0.5f",
         "ModifierType.PercentageMultiply")
    need(body(water, "public void Dispose()"), "游泳隐蔽清理", "RuntimeStatModifierTracker.RemoveAll(stealthRecords")
    # 028 物资池与武器池趁读条预热（导航扫完、放行官方初始化之前）。
    warm_at, ready_at = build.find("SkyIslandLootPools.Prewarm()"), build.find("navigationReady = true;")
    if warm_at < 0 or ready_at < 0 or warm_at > ready_at:
        errors.append("潮闸镇要在放行官方初始化之前预热物资池（SkyIslandLootPools.Prewarm）")

    # 潮汐读跨天连续的官方时间：只读当天时刻（24 不是 4.5 的整数倍）会让午夜从低潮一步跳回高潮。
    tide = read("Tidegate/TidegateTide.cs")
    need(body(tide, "internal static double ClockHours()"), "潮汐时钟", "TidegateTideRules.ElapsedHours(GameClock.Day, GameClock.TimeOfDay.TotalSeconds)")
    for rel in ("Tidegate/TidegateTide.cs", "Tidegate/TidegateEnemies.cs", "Tidegate/TidegateSession.cs", "Tidegate/TidegateDevices.cs",
                "Tidegate/TidegateExtraction.cs"):
        if "SkyIslandLighting.ClockHours()" in read(rel):
            errors.append(rel + " 的潮汐计时不得读当天时刻（SkyIslandLighting.ClockHours），改用 TidegateTide.ClockHours()")
    need(body(enemies, "private bool DiggersOut()"), "淘沙帮撤回计时", "TidegateTide.RealSecondsUntilNextPhase()")
    need(body(tide, "internal static double RealSecondsUntilNextPhase()"), "换相倒计时", "TidegateTideRules.RealSecondsUntilNextPhase(ClockHours(),")

    # 竹筏用作者工程的正式模型：包缺了就不开入口，不拿几何体顶替；着色器同大地图口径；包跟着筏子卸载。
    raft = read("Tidegate/TidegateEntryRaft.cs")
    need(raft, "竹筏模型", 'BundleRelativePath = "Assets/arenas/tidegate_entry"', 'PrefabPath = "Assets/Tidegate/Entry/TidegateRaft.prefab"',
         "CheckShaders(model)", "!shader.isSupported", 'Find("LanternLight")')
    if re.search(r"CreatePrimitive|FieldAdventureVisualAssets|SodaCraft/", raft or ""):
        errors.append("竹筏不得退回几何体或借用别的模块的材质")
    need(body(raft, "internal sealed class TidegateEntryBundleLease"), "竹筏包租约", "bundle.Unload(true)", "private void OnDestroy() { Release(); }")
    need(module, "竹筏入口", "!TidegateEntryRaft.IsBundleDeployed()", "TidegateEntryRaft.Release(raft)")

    # 部署：场景包与竹筏包随正式构建进 Mod 目录，否则入口永远不出现。
    bat = (ROOT / "compile_official.bat").read_text(encoding="utf-8", errors="replace")
    for bundle_name in ("tidegate_raid", "tidegate_entry"):
        if 'copy /Y "Assets\\arenas\\' + bundle_name + '"' not in bat:
            errors.append("compile_official.bat 未部署 " + bundle_name + " 包")

    print("TidegateRaidGuard: " + ("FAIL\n" + "\n".join(errors) if errors else "PASS"))
    return bool(errors)


if __name__ == "__main__":
    raise SystemExit(main())
