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

    # 潮汐读跨天连续的官方时间：只读当天时刻（24 不是 4.5 的整数倍）会让午夜从低潮一步跳回高潮。
    tide = read("Tidegate/TidegateTide.cs")
    need(body(tide, "internal static double ClockHours()"), "潮汐时钟", "TidegateTideRules.ElapsedHours(GameClock.Day, GameClock.TimeOfDay.TotalSeconds)")
    for rel in ("Tidegate/TidegateTide.cs", "Tidegate/TidegateEnemies.cs", "Tidegate/TidegateSession.cs", "Tidegate/TidegateDevices.cs",
                "Tidegate/TidegateExtraction.cs"):
        if "SkyIslandLighting.ClockHours()" in read(rel):
            errors.append(rel + " 的潮汐计时不得读当天时刻（SkyIslandLighting.ClockHours），改用 TidegateTide.ClockHours()")
    need(enemies, "淘沙帮撤回计时", "double hours = TidegateTide.ClockHours();")

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
