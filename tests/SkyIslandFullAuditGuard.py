"""天空岛全方位审核（2026-09-10）逐条修复的结构回归。

其它天空岛守卫各管一块（HUD / F3 套件纪律 / 内容 / 入口 / 生命周期），这一份补的是它们都不覆盖、
却在审核里确认过会坏的地方，每一条对应 `CODE_REVIEW_FINDINGS.md` 的一个 CR 编号：

- CR-2026-09-10-018 / -019 / -027：F3 岛内用例的**判据本身**（套件守卫只钉编排与只读纪律，钉不到判据写成恒真或必然假红）；
- CR-2026-09-10-020：桥口木牌英文溢出；
- CR-2026-09-10-021：失败提示把中文异常原文拼给英文玩家；
- CR-2026-09-10-025：每 0.5 秒与全局补丁热路径上的分配；
- CR-2026-09-10-028：英文术语与语法；
- CR-2026-09-10-022：文档与实现不一致（「蓝环」「手柄」「状态行在上方」「F6 旅程图」「未复用官方倒计时控件」）。

凡是「某句必须在某个方法里」的检查，一律先切出方法体再找；文档类检查读原文（注释也算文档）。
守卫钉的是结构，不是行为：用例判据对不对最终要在岛上跑一次 F3 才知道
（docs/制作教程/天空岛/天空岛_待人工验证清单.md 第 2.8 步）。
"""
import re
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
SKY = "DebugAndTools/SkyIsland/"


def body_of(source, signature):
    """切出以 signature 开头的块体（到配对的收尾大括号为止）。找不到返回 None。"""
    start = source.find(signature)
    if start < 0:
        return None
    open_brace = source.find("{", start)
    if open_brace < 0:
        return None
    depth = 0
    for i in range(open_brace, len(source)):
        ch = source[i]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return source[open_brace + 1:i]
    return None


def main():
    def read(path):
        return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))

    def raw(path):
        return (ROOT / path).read_text(encoding="utf-8-sig")

    cases = read("DebugAndTools/F3GameplayValidationSkyIslandCases.cs")
    suite = read("DebugAndTools/F3GameplayValidationSkyIsland.cs")
    errors = []

    def need_body(source, signature, label):
        body = body_of(source, signature)
        if body is None:
            errors.append("找不到 " + label + "（" + signature + "）")
            return ""
        return body

    def require(text, token, why):
        if text and token not in text:
            errors.append(why + "（缺 " + token + "）")

    def forbid(text, token, why):
        if text and token in text:
            errors.append(why + "（出现 " + token + "）")

    # ---- CR-2026-09-10-018 / -019 / -027：F3 岛内用例的判据 ----
    residents = need_body(cases, "private bool ValidateSkyIslandResidents(out string metrics, out string reason)", "SKY_RESIDENTS")
    forbid(residents, "FindObjectsOfType<SkyIslandResidentInteractable>",
           "SKY_RESIDENTS 又按全场景扫交互体：居民不挂在地形根下、被隐藏的折翎扫不到，有居民在岛就必然假红")
    require(residents, "residents.HasTalkInteraction(ids[i])", "SKY_RESIDENTS 必须按人逐个核对「聊聊航路」交互体")
    require(residents, 'errors.Add(ids[i] + ":no_talk_interaction")', "SKY_RESIDENTS 在岛却没有交互体时必须报红")

    separation = need_body(cases, "private bool ValidateSkyIslandInteractionSeparation(out string metrics, out string reason)",
                           "SKY_INTERACTION_SEPARATION")
    require(separation, "residentsOwner.CollectActiveInteractables(all)",
            "SKY_INTERACTION_SEPARATION 漏了居民：居民与纪念物 / 见闻点的竞争一对都算不到")
    describe = need_body(cases, "private static bool TryDescribeInteractable(InteractableBase interactable, out Vector3 center, out float extent)",
                         "交互体触发体积")
    require(describe, "interactable.GetComponents<Collider>()", "交互体包围盒只能取自身 GameObject 上的碰撞体（官方按 collider 所在对象认目标）")
    forbid(describe, "GetComponentsInChildren<Collider>", "交互体包围盒又并进了子物体碰撞体，会把半径撑大误报重叠")

    objective = need_body(cases, "private bool ValidateSkyIslandObjective(out string metrics, out string reason)", "SKY_STORY_OBJECTIVE")
    require(objective, "session.ValidationHudObjective", "SKY_STORY_OBJECTIVE 必须比对 HUD 实际显示的目标行")
    forbid(objective, "story.CurrentObjective", "SKY_STORY_OBJECTIVE 拿缓存与规则比是恒等，等于没验")

    codec = need_body(cases, "private bool ValidateSkyIslandStoryCodec(out string metrics, out string reason)", "SKY_STORY_CODEC")
    require(codec, "SameStrings(decoded.clearedEncounters, current.clearedEncounters)", "SKY_STORY_CODEC 往返必须逐项比对清场记录")
    require(codec, "SameStrings(decoded.discoveredNotes, current.discoveredNotes)", "SKY_STORY_CODEC 往返必须逐项比对见闻")
    require(codec, "declared == SkyIslandStoryRules.KnownFlags", "SKY_STORY_CODEC 必须核对枚举全集与 KnownFlags 一致")
    forbid(codec, ".Length == current.clearedEncounters.Length", "SKY_STORY_CODEC 又只比数组长度，内容被改写照样绿")

    scavenge = need_body(cases, "private bool ValidateSkyIslandScavengePlacement(out string metrics, out string reason)",
                         "SKY_SCAVENGE_PLACEMENT")
    if scavenge:
        skip = scavenge.find("if (snapshot.ScavengeBuilt == 0) throw new SkyIslandSkipCase(")
        verdict = scavenge.find("return built;")
        if not (0 <= skip < verdict):
            errors.append("SKY_SCAVENGE_PLACEMENT 一个箱都还没建时必须先记 SKIP：那时「建箱没失败」恒真")

    panel = need_body(cases, "private bool ValidateSkyIslandPanelArt(out string metrics, out string reason)", "SKY_PANEL_ART")
    require(panel, "SkyIslandUiArt.HasArt(", "SKY_PANEL_ART 必须走只读探测")
    require(panel, "if (found == 0) throw new SkyIslandSkipCase(", "SKY_PANEL_ART 一张图都没部署时必须记 SKIP")
    for token in ("SkyIslandUiArt.GetScene(", "SkyIslandUiArt.GetPortrait("):
        forbid(panel, token, "SKY_PANEL_ART 又在玩家帧上解码插图并写缓存")
    art = read(SKY + "SkyIslandUiArt.cs")
    has_art = need_body(art, "internal static bool HasArt(string assetName)", "插图只读探测")
    require(has_art, "bundle.Contains(assetName)", "插图探测只能查 bundle 目录")
    for token in ("LoadAsset", "LoadImage", "FromBundle(", "FromRawPng(", "sprites["):
        forbid(has_art, token, "插图探测不得加载资源或写缓存")
    if has_art and re.search(r"(?<![\w.])Get\(", has_art):
        errors.append("插图探测不得调用会解码并缓存的 Get")

    countdown = need_body(cases, "private bool ValidateSkyIslandOfficialCountdown(out string metrics, out string reason)",
                          "SKY_EXTRACTION_OFFICIAL_UI")
    require(countdown, "ui.gameObject.scene.handle == session.ValidationScene.handle",
            "SKY_EXTRACTION_OFFICIAL_UI 必须核对官方实例属于本岛场景（静态实例从不清空）")
    require(countdown, "ui.isActiveAndEnabled", "SKY_EXTRACTION_OFFICIAL_UI 必须核对官方实例已启用")
    require(countdown, "return instance && inIsland && enabled && bridge;", "SKY_EXTRACTION_OFFICIAL_UI 的结论必须包含场景与启用两条")

    intact = need_body(cases, "private bool ValidateSkyIslandSessionIntact(out string metrics, out string reason)", "SKY_FINAL_SESSION_INTACT")
    require(intact, "_skyIslandBaselineLeases", "SKY_FINAL_SESSION_INTACT 必须按开跑时的租约基线比")
    forbid(intact, "ModalInputLeaseCount == 0", "SKY_FINAL_SESSION_INTACT 又和 0 比，玩家自己开着的面板会被算成套件漏租约")

    english = need_body(cases, "private bool ValidateSkyIslandEnglishText(out string metrics, out string reason)", "SKY_LOCALIZATION_EN")
    require(english, "session.ValidationSearchMarkerNames()", "SKY_LOCALIZATION_EN 必须遍历本局真实见闻点")
    require(english, "SkyIslandSession.RegionLabel(", "SKY_LOCALIZATION_EN 必须覆盖 HUD 用的区域名入口")
    forbid(english, '"Search_A_02"', "SKY_LOCALIZATION_EN 又用回手写见闻点清单（会漏掉 _02 点位）")

    # 2026-09-14：判据搬进纯函数 JudgeBountyGating（隔离回归 SkyIslandValidationJudges 执行的那一份），
    # 用例本体只取数、只转交。断言跟着判据走，一条没少，并加钉「驱蚋软门 / 前三类硬门」。
    bounty = need_body(cases, "private bool ValidateSkyIslandBountyGating(out string metrics, out string reason)", "SKY_BOUNTY_GATING")
    require(bounty, "return JudgeBountyGating(", "SKY_BOUNTY_GATING 的判据必须交给纯函数 JudgeBountyGating")
    require(bounty, "session.ValidationGroundRegionCount", "SKY_BOUNTY_GATING 必须把本局索引到的区域数交给判据")
    judges = read("DebugAndTools/F3GameplayValidationSkyIslandRuntimeCases.cs")
    judge = need_body(judges, "internal static bool JudgeBountyGating(", "SKY_BOUNTY_GATING 判据")
    require(judge, "availableFor(SkyIslandBountyKind.Survey) > groundRegions",
            "SKY_BOUNTY_GATING 必须核对巡岛可完成量不超过区域数")
    require(judge, '"survey_available_exceeds_regions"', "SKY_BOUNTY_GATING 越界时必须报红")
    require(judge, "if (active == SkyIslandBountyKind.Gnats) soft =",
            "驱蚋委托是软门：白天做不完只记 metrics，硬判红就是白天必定假红")
    require(judge, 'else errors.Add("active_contract_unfinishable");', "清理 / 搜刮 / 巡视三类做不完仍必须硬判红")

    markers = need_body(cases, "private bool ValidateSkyIslandMarkers(out string metrics, out string reason)", "SKY_MARKERS")
    require(markers, "groundRegions == required.Length", "SKY_MARKERS 必须核对按地面碰撞体索引到全部区域")

    reach = need_body(suite, "private IEnumerator RunSkyIslandReachability()", "SKY_GATE_REACHABILITY")
    if reach and reach.count(':marker_missing"') < 2:
        errors.append("SKY_GATE_REACHABILITY 的撤离点与必到遭遇点标记缺失时必须记不可达，不能静默跳过")
    require(suite, 'string targetName = target == null ? "missing_target" : target.name;',
            "探路必须先取出目标名：等待期间场景卸载后再读 target.name 会对已销毁对象二次抛")
    forbid(suite, "failures.Add(target.name", "探路失败记录又直接读 target.name")
    # ---- 2026-09-14 实机：H_02 假红，以及把「路算完了」当成「走到了」 ----
    require(suite, "completed = ProbeReachedTarget(end.x, end.y, end.z, _probeTarget.x, _probeTarget.y, _probeTarget.z, out gap);",
            "探路必须按终点离目标的距离判「走到了」：只看 path.error 时，A* 把路算到门这一侧最近的点也记可达，抓不到软锁")
    if reach:
        require(reach, "string gate = ReachabilityGateFor(content.Encounters[i].Marker);",
                "锁门岛上的自动遭遇组必须按门的开闭分类：门关着时不能算必到点（09-13 补密后钟庭 H_02 的假红）")
        require(reach, "if (gate != null && (progress == null || !content.IsGateOpen(gate, progress)))",
                "锁门岛分类必须真的看门的开闭（读不到进度按关着算），不能被短路")
        require(reach, "ProbePath(seeker, origin, locked[i], mask, lockedBlocked, lockedLeaks)",
                "门关着的锁门岛遭遇点要反过来核对「确实被挡住」，走得到记进 lockedLeaks")
        require(reach, "if (unreachable.Count > 0 || lockedLeaks.Count > 0)",
                "门关着却走得到（门没切进导航图）必须记红")

    # ---- CR-2026-09-10-020：桥口木牌英文溢出 ----
    gates = read(SKY + "SkyIslandGates.cs")
    for token in ("text.enableAutoSizing = true;", "text.fontSizeMin = 2.4f;", "text.overflowMode = TextOverflowModes.Ellipsis;"):
        require(gates, token, "桥口木牌必须自动缩放并以省略号兜底（英文关闭态 6–7 行放不进 2.8 单位木板）")
    forbid(gates, "text.enableAutoSizing = false;", "桥口木牌又关掉了自动缩放")

    # ---- CR-2026-09-10-021：失败提示不得把中文异常原文拼给英文玩家 ----
    for path in (SKY + "SkyIslandSession.cs", SKY + "SkyIslandEncounters.cs"):
        text = read(path)
        if re.search(r"(?:Status|report)\s*\([^;]*\+\s*e\.Message", text) or re.search(r"Status\s*\(\s*e\.Message", text):
            errors.append(path + " 又把异常原文直接拼进玩家提示：改走 SkyIslandStoryRules.WithDetail，原文进日志")
        require(text, "SkyIslandStoryRules.WithDetail(", path + " 的失败提示必须经 WithDetail")
    rules = read(SKY + "SkyIslandStoryRules.cs")
    detail = need_body(rules, "internal static string WithDetail(string prefix, string detail)", "失败提示出口")
    if detail:
        chinese = detail.find("if (L10n.IsChinese) return prefix + detail;")
        if chinese < 0:
            errors.append("WithDetail 的中文分支必须带上原文")
        else:
            # 只看代码里的标识符：英文分支的提示文案本身写着 "(details in Player.log)"，那不是参数。
            english_branch = re.sub(r'"(?:\\.|[^"\\\n])*"', '""', detail[chinese + len("if (L10n.IsChinese) return prefix + detail;"):])
            if re.search(r"\bdetail\b", english_branch):
                errors.append("WithDetail 的英文分支不得带上中文异常原文")

    # ---- CR-2026-09-10-025：热路径分配 ----
    bridge = read(SKY + "SkyIslandSceneReferenceBridge.cs")
    is_scene = need_body(bridge, "internal static bool IsScene(Scene scene)", "天空岛场景判定")
    if is_scene:
        gate = is_scene.find("if (!scene.IsValid() || scene.buildIndex >= 0) return false;")
        path_read = is_scene.find("scene.path")
        handle = is_scene.find("scene.handle == knownSceneHandle")
        if not (0 <= gate < handle < path_read):
            errors.append("IsScene 必须先比 buildIndex、再比句柄缓存，最后才读 scene.path（它挂在全局补丁热路径上，每读一次分配一个字符串）")
    for signature in ("internal static void BeginInitialization(object owner)", "private static void OnSceneUnloaded(Scene scene)",
                      "private static void CleanupRegistration()"):
        body = need_body(bridge, signature, "场景句柄缓存作废点")
        require(body, "knownSceneHandle = 0;", "天空岛场景句柄缓存必须在 " + signature + " 里作废")
    session = read(SKY + "SkyIslandSession.cs")
    for signature in ("internal static string LandmarkLabel(string name)", "internal static string RegionLabel(string id)",
                      "private static string MainRegionCn(char region)", "private static string MainRegionEn(char region)"):
        body = need_body(session, signature, "地名取用")
        if body and re.search(r"\bnew\s*(?:string\s*)?\[", body):
            errors.append(signature + " 每次调用都新建数组：HUD 每 0.5 秒走一次，直接返回字面量即可")
    field = need_body(session, "private string FieldStatus()", "HUD 进度小标")
    if field:
        cached = field.find("return chipsText;")
        first_text = field.find("L10n.T(")
        if cached < 0 or first_text < 0 or cached > first_text:
            errors.append("FieldStatus 必须在拼字符串之前按输入计数复用上一次的结果")

    # ---- CR-2026-09-12-013：剧情体显隐是每帧两次的路径，值没变就必须整条早退（R-9 分项）----
    # 会话 Update 每帧对折翎与钟守各调一次 SetVisible，而这两个目标状态整趟只翻转个位数次。
    # 没有早退的话，每帧要做 HashSet 增删 + Dictionary 查（各一次字符串散列）再无条件 SetActive。
    # `hidden` 是唯一事实源（异步生成落地时由 SpawnOneAsync 照它补一次 SetActive），按它早退安全。
    resident_source = read(SKY + "SkyIslandResidents.cs")
    set_visible = need_body(resident_source, "internal void SetVisible(string id, bool visible)", "剧情体显隐")
    if set_visible:
        early = set_visible.find("if (hidden.Contains(id) != visible) return;")
        mutate = set_visible.find("hidden.Remove(id)")
        if early < 0:
            errors.append("SetVisible 缺少「已经是目标状态就整条早退」：它是每帧两次的路径（R-9 分项）")
        elif not (0 <= early < mutate):
            errors.append("SetVisible 的早退必须排在 HashSet 增删与 SetActive 之前，否则省不掉任何工作")
    require(resident_source, "if (hidden.Contains(id)) npc.gameObject.SetActive(false);",
            "异步生成落地时必须照 hidden 补一次显隐，否则「先 SetVisible、后生成」那条时序会漏")

    # ---- CR-2026-09-12-014：每一项居民服务都要有装置兜底 ----
    # 居民可能不在岛上：晴禾 / 苇白婚后由婚姻系统接管（PermanentDuckNpcModule 对 SkyIslandRaid 恒返回 false），
    # 眠苔与浮舟的异步生成也可能失败。合成台早就各有兜底装置，**服务**必须按同一条纪律配齐——
    # 只兜药臼不兜苔药的话，眠苔没生成出来的那一趟玩家连唯一的付费回血都没有，
    # 而星苔药膏恰恰要在她的药臼上做，等于把「回血」整条线掐断。
    world_story = read(SKY + "SkyIslandWorldStory.cs")
    read_point = need_body(world_story, "internal void ReadPoint(string key, Action recorded)", "装置面板")
    talk = need_body(world_story, "internal void Talk(string id, Transform speaker)", "居民对话面板")
    for service, resident_label, device_label in (
            ("Repair", "浮舟", "码头装置 Search_A"),
            ("Heal", "眠苔", "悬根林见闻点 Search_D_02"),
            ("Meal", "晴禾", "青穗梯田菜畦 Search_C")):
        token = "ServiceChoice(choices, L10n.T("
        in_talk = talk and token in talk and (", " + service + ")") in talk
        in_device = read_point and (", " + service + ")") in read_point
        if in_talk and not in_device:
            errors.append("%s 的服务 %s 只挂在本人身上，没有装置兜底（%s）：她/他不在的那一趟这项服务整条失联"
                          % (resident_label, service, device_label))
    # 合成台同样三处都要有兜底（这条早就成立，一并钉住，免得将来单独退化）。
    for station in ("SkyIslandCraftStation.Dock", "SkyIslandCraftStation.Stove", "SkyIslandCraftStation.Mortar"):
        require(read_point, "CraftChoice(choices, " + station + ")",
                "合成台 " + station + " 缺少装置兜底入口")

    # ---- CR-2026-09-10-028：英文术语与语法 ----
    literal = re.compile(r'"(?:\\.|[^"\\\n])*"')
    sources = sorted((ROOT / "DebugAndTools/SkyIsland").glob("*.cs")) + [
        ROOT / "DebugAndTools/F3GameplayValidationRunner.cs"]
    for path in sources:
        for value in literal.findall(clean_source(path.read_text(encoding="utf-8-sig"))):
            if re.search(r"\bSky Island(?!s)", value):
                errors.append("英文统一用 Sky Islands（群岛）：" + path.name + " " + value[:60])
                break
    main_en = need_body(session, "private static string MainRegionEn(char region)", "主岛英文名")
    require(main_en, 'default: return "Homecoming Bell Court";', "钟庭英文名统一为 Homecoming Bell Court")
    world = read(SKY + "SkyIslandWorldStory.cs")
    resident_name = need_body(world, "internal static string ResidentName(string id)", "居民名")
    require(resident_name, 'return L10n.T("无声钟守", "Silent Bell Keeper");', "居民名当标题与血条名用，英文不带小写冠词")
    forbid(world, '" left at her feet)"', "谢礼可能落在留言板旁，不写「她脚边」")
    services = read(SKY + "SkyIslandServices.cs")
    repair = need_body(services, "internal string Repair()", "整备回话")
    require(repair, "repaired == 1", "整备回话必须区分单复数")
    require(repair, '" piece. Nothing blunt lasts out on the cloud sea. (cost "', "整备回话缺单数分支")
    heal = need_body(services, "internal string Heal()", "苔药回话")
    require(heal, "wait == 1", "苔药冷却回话必须区分单复数")
    require(heal, '" second until the next dose."', "苔药冷却回话缺单数分支")
    forbid(read(SKY + "SkyIslandResidentInteractable.cs"), '"Island story · "', "居民交互名又用回 Island story")

    # ---- CR-2026-09-10-022：文档与实现一致 ----
    # 玩家文档与覆盖清单读原文；F3 面板说明读剥掉注释后的源码（注释里记录「以前写的蓝环」是正当的）；
    # 剧情面板的操作说明写在文件头注释里，也读原文。
    for path, stale, text in (
            ("WikiContent/zh/map__sky_island.md", ("码头是蓝环", "手柄玩家", "屏幕**上方**", "屏幕上方状态行"), None),
            ("WikiContent/en/map__sky_island.md", ("blue at the dock", "controller players", "at the **top** of the screen",
                                                   "status line at the top"), None),
            ("Assets/Data/GameplayCoverage.json", ("F6 旅程图", "蓝色贴地光环", "码头蓝环"), None),
            ("ArtSource/SkyIsland/OFFICIAL_SCENE_CONTRACT.md", ("未复用官方倒计时控件",), None),
            (SKY + "SkyIslandControls.cs", ("蓝环", "blue ring"), read(SKY + "SkyIslandControls.cs")),
            (SKY + "SkyIslandStoryPresentation.cs", ("手柄走官方",), None)):
        text = raw(path) if text is None else text
        for phrase in stale:
            if phrase in text:
                errors.append(path + " 仍写着与实现不符的「" + phrase + "」（撤离环是青色、官方输入资产没有手柄绑定、目标在右侧卡片、"
                              "地图是官方 M 键地图、读条已复用官方控件）")

    if errors:
        for error in errors:
            print("  - " + error)
        print("SkyIslandFullAuditGuard: FAIL")
        raise SystemExit(1)
    print("SkyIslandFullAuditGuard: PASS (F3 用例判据 / 桥口木牌 / 失败提示本地化 / 热路径分配 / "
          "剧情体显隐早退 / 服务与合成台的装置兜底 / 英文术语 / 文档一致)")


if __name__ == "__main__":
    main()
