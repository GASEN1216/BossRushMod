"""F3 天空岛岛内验收套件的结构不变式。

这套东西的价值全在三件事上，而它们都很容易在后续改动里被悄悄破坏：

1. **它必须能在岛内跑。** 主套件的 `CheckStartGate` 从 2026 年起就显式拒绝天空岛
   （"请先退出天空岛"），天空岛因此长期零 F3 覆盖。岛内入口一旦被合并回主门、
   或者收尾阶段被改成走主套件的 `RunFinalChecks`（那会 LoadScene 回基地），
   这趟出击就会被验收自己送走，等于回到原点。
2. **它必须只读。** 岛内验收跑在玩家真实的一趟出击上。任何写剧情、搬玩家、生成敌人、
   开箱的动作都会污染玩家正在做的事，而且这种污染在报告里看不出来。
   只读也包括会话外壳：不写运行标记（那要 SavesSystem.SaveFile）、不接管无敌与回血、
   不吞玩家提示、不跑面向主套件的全宿主清理——2026-09-10 全方位审核发现这四处原先都在做。
3. **它不得把「这趟出击已经没了」记成用例失败。** 玩家中途撤离、倒下或换槽之后，
   后面的用例一律记 SKIP 并附原因。

2026-09-10 全方位审核用 22 个变异探针反向验证上一版守卫：除 3 个对照探针外，其余 19 个破坏全部让它保持全绿：
调用前加一个空格、`&& false` 废掉一道门、`if (false)` 包住用例、同名局部变量冒充静态类、
十六进制常量多写一位、观测面方法里顺手改字段……这一版全部按结构判断：
先规范空白，再切出方法体，再按完整语句匹配。

守卫钉住的是结构，不是行为：它证明不了套件跑起来是对的，只证明上面三条纪律还在。
实机 smoke 仍按 `Assets/Data/GameplayCoverage.json` 的 `M_SKY_ISLAND_*` 逐条走。
"""
import re
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
BACKSLASH = chr(92)

SUITE = "DebugAndTools/F3GameplayValidationSkyIsland.cs"
CASES = "DebugAndTools/F3GameplayValidationSkyIslandCases.cs"
SURFACE = "DebugAndTools/SkyIsland/SkyIslandSessionValidation.cs"

# `Inspect` 的 CJK 码位区间。C# 那边写成整数常量而不是字面汉字或 \u 转义，是因为后两者
# 在文件被按非 UTF-8 读写、或经过会折反斜杠的工具时会静默变形，而这条断言完全靠
# 区间成立——变形之后它会安静地永远为真，也就是最糟的假绿。这里按数值比，不按子串比：
# 子串比对下 `0x4E001` 以 `0x4E00` 开头，照样命中。
EXPECTED_CJK_RANGES = (
    ("CjkIdeograph", 0x4E00, 0x9FFF),
    ("CjkExtensionA", 0x3400, 0x4DBF),
    ("CjkPunctuation", 0x3000, 0x303F),
    ("FullWidthForms", 0xFF00, 0xFFEF),
)

# 会改变玩家这趟出击状态的生产入口（方法名）。岛内验收一条都不许调；
# 方法组转换（`Action a = session.Close;`）同样算调用。只列**确实会写**的：读属性不在此列。
MUTATING_MEMBERS = (
    ("RecordSearch", "收录见闻"), ("RecordNote", "收录来信与名册"),
    ("RecordEncounterCleared", "记清场"), ("RecordRegionVisited", "点亮区域"),
    ("BeginStoryChallenge", "开战"), ("BeginChallenge", "开战"), ("SpawnEnemy", "刷怪"),
    ("VisitNextLandmark", "搬玩家"), ("SetPosition", "搬玩家"), ("Rescue", "搬玩家"),
    ("CycleLighting", "改光色"), ("OpenMap", "打开官方地图"),
    ("DropBountyReward", "发奖励箱"), ("DropTrophy", "发战利品"),
    ("TryAccept", "动委托"), ("TryClaim", "动委托"), ("TryAbandon", "动委托"),
    ("ReportEncounterCleared", "委托记账"), ("ReportScavenged", "委托记账"), ("ReportRegionVisited", "委托记账"),
    ("Close", "结束这趟出击"), ("TryClose", "关闭剧情存档"),
    ("Talk", "打开剧情面板"), ("ReadPoint", "打开剧情面板"), ("SetVisible", "改居民可见性"),
    ("Heal", "付费服务"), ("Repair", "付费服务"), ("Meal", "付费服务"),
    ("Announce", "往玩家屏幕发提示"), ("UseCompass", "往玩家屏幕发提示"),
    # 内容批次三：采集会发物品、合成会扣材料、耗材会挂增益，打开合成台会弹面板。
    ("Harvest", "采集发物品"), ("Craft", "合成扣材料"), ("UseConsumable", "用耗材挂增益"),
    ("OpenCrafting", "打开合成面板"), ("CraftChoice", "打开合成面板"),
    # 串联：点风晶灯会写手记并扣材料，便当那一顿会挂加成。
    ("LightLamp", "点灯写手记扣材料"), ("LightChoice", "打开点灯选项"), ("PackedMeal", "吃便当挂加成"),
    ("TryRecallToDock", "搬玩家"),
    # 内容批次四（云蚋）：捧蛙卵扣材料、放生写手记、放灭蚊灯、扇蚊子、止痒会改这趟出击；推进蚊群会刷怪、叮人。
    ("TakeSpawn", "捧蛙卵扣材料"), ("ReleaseSpawn", "放生写手记"), ("SpawnChoice", "打开蛙卵选项"), ("ReleaseChoice", "打开放生选项"),
    ("DeployZapper", "放灭蚊灯"), ("SwingFan", "扇云蚋"), ("Soothe", "止痒"), ("RemedyClearsItch", "止痒"),
    ("OnProjectile", "登记弹道让云蚋躲闪"), ("Sample", "推进云蚋刷新与痒"), ("Frame", "推进云蚋飞行与叮咬"),
)

# 不以方法名出现、但同样会改状态的写法。
FORBIDDEN_PATTERNS = (
    (r"\bSavesSystem\.(?:Save|SaveFile|SaveGlobal|SetFile|DeleteCurrentSave|RestoreIndexedBackup|CollectSaveData)\b",
     "直接写档或换槽"),
    (r"\bSkyIslandRewardCrate\.(?:Create|Build|Fill)\b", "建箱"),
    (r"\btimeScale\s*=(?![=>])", "改时间流速"),
    (r"\.(?:position|localPosition|rotation)\s*=(?![=>])", "搬动场景对象"),
    (r"\.(?:Invoke|SetValue)\(", "用反射或委托调用绕过上面的名单"),
    (r"\bGameplayValidationSuppressNotifications\s*=(?![=>])", "吞掉玩家的提示条"),
    (r"\bProtectCurrentPlayer\(", "接管玩家的无敌与回血"),
    (r"\bLoadScene(?:Async)?\(", "切图"),
    (r"\bReturnToBase\(", "返航"),
    (r"\bRunFinalChecks\(", "调主套件收尾（会 LoadScene 回基地）"),
    (r"\bRunSuite\(", "调主套件编排"),
    (r"\bValidationSafeCleanup\(", "跑面向主套件的全宿主清理"),
)

# 观测面允许调用的会话方法：全部是只读的几何与计数。
READ_ONLY_HELPERS = frozenset(("ExtractionMarkerAt", "BellExitIfUnlocked", "WindExitIfUnlocked", "StarExitIfUnlocked",
                               "CountWalkableNodes"))
CALL_KEYWORDS = frozenset(("if", "for", "foreach", "while", "switch", "return", "default", "typeof", "nameof",
                           "sizeof", "catch", "using", "lock", "checked", "unchecked", "when", "new"))

# 启动门里每一道拒绝都必须是完整的 `if (...) { reason = "..."; return false; }`：
# 只找条件子串的话，`&& false` 或删掉 `return false` 都能让这道门永远不拦。
GATE_REJECTIONS = (
    (r'if\s*\(!ModBehaviour\.DevModeEnabled\)\s*\{\s*reason = "[^"]+";\s*return false;\s*\}', "仅 Dev 构建"),
    (r"SkyIslandSession session = _host\.GetComponent<SkyIslandSession>\(\);", "取当前会话"),
    (r'if\s*\(session == null\)\s*\{\s*reason = "[^"]+";\s*return false;\s*\}', "必须已经在岛上"),
    (r'if\s*\(!session\.IsReady\)\s*\{\s*reason = "[^"]+";\s*return false;\s*\}', "会话已就绪"),
    (r'if\s*\(!SkyIslandRaidLease\.IsRaidScene\(SceneManager\.GetActiveScene\(\)\)\)\s*'
     r'\{\s*reason = "[^"]+";\s*return false;\s*\}', "活动场景是天空岛"),
    (r'if\s*\(!IsDedicatedCurrentSlot\(\)\)\s*\{\s*reason = "[^"]+";\s*return false;\s*\}', "专用测试档"),
    (r'if\s*\(ZombieModeUIHelper\.ModalInputLeaseCount > 0\)\s*\{\s*reason = "[^"]+";\s*return false;\s*\}',
     "没有开着的模态面板"),
    (r'if\s*\(View\.ActiveView != null\)\s*\{\s*reason = "[^"]+";\s*return false;\s*\}', "没有开着的官方界面"),
)
CONSTANT_CONDITION = r"&&\s*(?:false|!true)\b|\|\|\s*(?:true|!false)\b|\bif\s*\(\s*(?:false|true)\s*\)"

# 编排里每条用例都必须是独立的一整条语句，并经过会话有效性外壳。
SYNC_LINE = re.compile(r'^\s*RunSkyIslandSync\("(SKY_[A-Z0-9_]+)", (ValidateSkyIsland\w+)\);\s*$')
CORO_LINE = re.compile(r'^\s*yield return RunSkyIslandCase\("(SKY_[A-Z0-9_]+)", (RunSkyIsland\w+)\);\s*$')
CASE_CALL = re.compile(r"\bRun(?:SkyIslandSync|SkyIslandCase|SyncCase)\s*\(")


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


def statement_before(source, pos):
    """pos 所在语句从开头到 pos 的文本（上一个 ; { } 之后）。"""
    start = max(source.rfind(";", 0, pos), source.rfind("{", 0, pos), source.rfind("}", 0, pos))
    return source[start + 1:pos]


def line_of(source, pos):
    start = source.rfind("\n", 0, pos) + 1
    end = source.find("\n", pos)
    return source[start:end if end >= 0 else len(source)]


def normalize(code):
    """把 `a . b (` 规范成 `a.b(`：上一版守卫按子串找 `.RecordSearch(`，调用前加一个空格就绕过去了。"""
    code = re.sub(r"\s*\.\s*(?=[A-Za-z_])", ".", code)
    return re.sub(r"(?<=[\w>\]])\s+\(", "(", code)


def blank_strings(code):
    """把字符串字面量的内容清空（引号保留）：报告文案里的 "ready=" 之类不是字段赋值。"""
    code = re.sub(r'@"(?:""|[^"])*"', '""', code)
    return re.sub(r'"(?:\\.|[^"\\\n])*"', '""', code)


def split_top_level(text):
    """按不在 <> () [] {} 里的逗号切分声明列表。"""
    parts, depth, current = [], 0, []
    for ch in text:
        if ch in "<([{":
            depth += 1
        elif ch in ">)]}":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append("".join(current))
            current = []
        else:
            current.append(ch)
    parts.append("".join(current))
    return parts


def session_fields(source):
    """解析 SkyIslandSession 的字段名（const 除外），用来判断观测面有没有写会话字段。"""
    names = set()
    for raw in source.splitlines():
        line = raw.strip()
        if not line.startswith("private ") or not line.endswith(";") or " const " in line:
            continue
        declaration = line[len("private "):-1]
        eq = declaration.find("=")
        if "(" in (declaration if eq < 0 else declaration[:eq]):
            continue
        for declarator in split_top_level(declaration):
            words = declarator.split("=", 1)[0].split()
            if words:
                names.add(words[-1])
    return names


def main():
    def read(path):
        return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))

    suite = read(SUITE)
    cases = read(CASES)
    surface = read(SURFACE)
    runner = read("DebugAndTools/F3GameplayValidationRunner.cs")
    execution = read("DebugAndTools/F3GameplayValidationExecution.cs")
    session = read("DebugAndTools/SkyIsland/SkyIslandSession.cs")
    bat = (ROOT / "compile_official.bat").read_text(encoding="utf-8", errors="ignore")
    errors = []

    def need(source, label, *tokens):
        for token in tokens:
            if token not in source:
                errors.append(label + " 缺少 " + token)

    def need_body(source, signature, label):
        body = body_of(source, signature)
        if body is None:
            errors.append("找不到 " + label + "（" + signature + "）")
            return ""
        return body

    # ---- 1. 新文件必须进编译清单（无通配符，漏了不报错）----
    for path in (SUITE, CASES, SURFACE):
        if path.replace("/", BACKSLASH) not in bat:
            errors.append("编译清单缺少 " + path)

    # ---- 2. 岛内入口存在，门的方向与主套件相反，而且每一道都真的会拦 ----
    gate_body = need_body(suite, "private bool CheckSkyIslandStartGate(out string reason)", "岛内启动门")
    for pattern, label in GATE_REJECTIONS:
        if gate_body and not re.search(pattern, gate_body):
            errors.append("岛内启动门缺少「" + label + "」这道拒绝（必须是完整的 if (...) { reason = ...; return false; }）")
    if gate_body and re.search(CONSTANT_CONDITION, gate_body):
        errors.append("岛内启动门里出现常量条件（&& false / || true / if (false)）：那道门会永远不拦")
    start_body = need_body(suite, "internal static bool TryStartSkyIsland(ModBehaviour host, out string reason)", "岛内入口")
    if start_body:
        gate_call = start_body.find("if (!_instance.CheckSkyIslandStartGate(out reason)) return false;")
        mode_on = start_body.find("_instance._skyIslandMode = true;")
        handle = start_body.find("_instance._skyIslandSceneHandle = ")
        begin = start_body.find("_instance.BeginSession(out reason)")
        if not (0 <= gate_call < mode_on < begin):
            errors.append("岛内入口必须先过启动门、再置岛内模式、最后开会话")
        if not (0 <= handle < begin):
            errors.append("开跑前没有记下天空岛场景实例句柄：会话有效性判断失去基准")

    # ---- 3. 主套件仍然拒绝在岛内启动（互斥按用例区分，不是整条删掉）----
    main_gate = need_body(runner, "private bool CheckStartGate(out string reason)", "主套件启动门")
    if main_gate and not re.search(r'if\s*\(_host\.GetComponent<SkyIslandSession>\(\) != null\)\s*'
                                   r'\{\s*reason = "[^"]+";\s*return false;\s*\}', main_gate):
        errors.append("主套件启动门不再拒绝天空岛（或只写了提示没有 return false）：它会 LoadScene 回基地，等于把这趟出击送走")

    # ---- 4. 两个阶段按模式分流；模式标志在唯一收尾路径上复位 ----
    for pattern, label in (
            (r"yield return DriveSessionPhase\(\s*_skyIslandMode \? RunSkyIslandSuite\(\) : RunSuite\(\), false\);", "主阶段"),
            (r"yield return DriveSessionPhase\(\s*_skyIslandMode \? RunSkyIslandFinalChecks\(\) : RunFinalChecks\(\), true\);",
             "收尾阶段")):
        if not re.search(pattern, execution):
            errors.append("会话" + label + "没有按 _skyIslandMode 分流")
    if len(re.findall(r"\bDriveSessionPhase\s*\(", execution)) != 3:
        errors.append("DriveSessionPhase 只许一个定义加两处按模式分流的调用：多出来的调用可能绕过分流")
    complete = need_body(execution, "private void CompleteSession()", "会话唯一收尾路径")
    if complete:
        capture = complete.find("bool skyIsland = _skyIslandMode;")
        reset = complete.find("_skyIslandMode = false;")
        dispose = complete.find("DisposeSessionStack();")
        if not (0 <= capture < reset < dispose):
            errors.append("_skyIslandMode 必须在 CompleteSession 里先记下、再复位、再释放协程栈："
                          "中途取消/异常时不复位，下次从基地启动会错走岛内编排")
    if len(re.findall(r"\b_skyIslandMode\s*=\s*false\s*;", execution)) != 1:
        errors.append("会话侧只许在 CompleteSession 这一处复位 _skyIslandMode")

    # ---- 5. 只读纪律：套件、用例与会话观测面 ----
    for label, source in (("岛内套件", suite), ("岛内用例", cases), ("会话观测面", surface)):
        flat = normalize(source)
        for name, why in MUTATING_MEMBERS:
            if re.search(r"\." + name + r"(?:\(|\s*[,;)])", flat) or re.search(r"(?<![\w.])" + name + r"\(", flat):
                errors.append(label + " 不得改变玩家这趟出击的状态（" + why + "）：" + name)
        for pattern, why in FORBIDDEN_PATTERNS:
            if re.search(pattern, flat):
                errors.append(label + " 不得" + why + "：/" + pattern + "/")
        # `TryApply` 有两个同名入口，只有一个是安全的：
        #   `SkyIslandStoryRules.TryApply` 是**纯函数**，只按输入算出候选状态并返回文案，
        #     英文完整性用例正是靠它把全部动作回执取出来扫 CJK；
        #   `SkyIslandStoryService.TryApply` 会 `store.Store(candidate)`，那是真写存档。
        # 所以不能整条禁掉，只能要求出现的每一处都是静态那个，并且不许有同名变量或别名冒充它。
        total = len(re.findall(r"\.TryApply\b", flat))
        static = len(re.findall(r"(?<![\w.])(?:global::)?(?:BossRush\.)?SkyIslandStoryRules\.TryApply\(", flat))
        if total != static:
            errors.append(label + " 只允许调用纯函数 SkyIslandStoryRules.TryApply，不得调用会写存档的 SkyIslandStoryService.TryApply")
        if re.search(r"[\w>\]]\s+SkyIslandStoryRules\s*[=;,)]|\bSkyIslandStoryRules\s*=(?![=>])", flat):
            errors.append(label + " 声明了名为 SkyIslandStoryRules 的变量或别名：它能冒充静态类骗过上一条")

    # ---- 5b. 只读纪律：会话外壳 ----
    begin_body = need_body(execution, "private bool BeginSession(out string reason)", "会话开场")
    if begin_body:
        marker_block = body_of(begin_body, "if (!_skyIslandMode)")
        if marker_block is None or "WriteRunMarker()" not in marker_block or "_runMarkerWritten = true;" not in marker_block:
            errors.append("岛内套件不得写运行标记（那要 SavesSystem.SaveFile，等于在出击途中绕过战斗落盘门写盘）："
                          "WriteRunMarker 与 _runMarkerWritten = true 必须包在 if (!_skyIslandMode) 块里")
    if len(re.findall(r"(?<!bool )\bWriteRunMarker\s*\(", execution + runner + suite + cases)) != 1:
        errors.append("WriteRunMarker 只许在会话开场按模式门控调用一次")
    for match in re.finditer(r"\bProtectCurrentPlayer\s*\(\s*\)\s*;", execution):
        if "if (!_skyIslandMode)" not in line_of(execution, match.start()):
            errors.append("岛内套件不得接管无敌与回血：ProtectCurrentPlayer 的每个调用点都必须在同一行按 !_skyIslandMode 门控")
    for match in re.finditer(r"GameplayValidationSuppressNotifications\s*=\s*true", execution):
        if "!_skyIslandMode" not in statement_before(execution, match.start()):
            errors.append("岛内套件不得吞掉玩家这趟出击里的正常提示：抑制提示条必须按 !_skyIslandMode 门控")
    if complete and not re.search(r"if \(!skyIsland && !_slotChanged && SavesSystem\.CurrentSlot == _sessionSlot\)\s*"
                                  r"\{\s*try \{ if \(_host != null\) _host\.ValidationSafeCleanup\(\); \}", complete):
        errors.append("岛内套件收尾不得跑面向主套件的全宿主清理：ValidationSafeCleanup 必须按 !skyIsland 门控")
    if len(re.findall(r"\bValidationSafeCleanup\s*\(", execution)) != 1:
        errors.append("会话侧只许在 CompleteSession 这一处调用 ValidationSafeCleanup")
    if not re.search(r"if \(_runMarkerWritten && !_slotChanged && SavesSystem\.CurrentSlot == _sessionSlot\) ClearRunMarker\(\);",
                     runner):
        errors.append("收尾只许清自己写过的运行标记：岛内套件从不写，不能为了清它再写一次盘")
    need(execution, "会话报告头",
         "read_only=true | player_invincible=false | player_health_refill=false | run_marker=false")

    # ---- 6. 判据不成立时记 SKIP，不许记 PASS ----
    skip_block = need_body(runner, "catch (SkyIslandSkipCase skip)", "SKIP 通道")
    if skip_block and ('Record(id, "SKIP"' not in skip_block or '"PASS"' in skip_block or '"FAIL"' in skip_block):
        errors.append("SkyIslandSkipCase 必须且只能记 SKIP：记 PASS 是假绿，记 FAIL 是冤枉")
    need(suite, "SKIP 信号", "internal sealed class SkyIslandSkipCase : Exception")
    english = need_body(cases, "private bool ValidateSkyIslandEnglishText(out string metrics, out string reason)", "英文完整性用例")
    if english:
        skip_at = english.find("if (L10n.IsChinese) throw new SkyIslandSkipCase(")
        if skip_at < 0:
            errors.append("中文语境下英文用例必须记 SKIP：扫不出中文残留是恒真，记 PASS 就是假绿")
        else:
            if english.count("L10n.IsChinese") != 1:
                errors.append("英文用例里 L10n.IsChinese 只许出现在记 SKIP 的那一句：别的分支可能在中文语境下提前记 PASS")
            if re.search(r"\breturn\b", english[:skip_at]):
                errors.append("英文用例在记 SKIP 之前就有 return：中文语境下会先记 PASS")
        if re.search(r"\bif\s*\(\s*(?:false|true)\s*\)", english):
            errors.append("英文用例里不得有常量条件分支")

    # ---- 7. CJK 区间必须是整数常量、数值正确，并在扫描里逐段成对使用 ----
    for name, low, high in EXPECTED_CJK_RANGES:
        for suffix, value in (("Start", low), ("End", high)):
            cm = re.search(r"\b" + name + suffix + r"\s*=\s*(0[xX][0-9A-Fa-f]+|\d+)\b", cases)
            if not cm or int(cm.group(1), 0) != value:
                errors.append("CJK 码位常量不正确或不存在：%s%s = 0x%04X" % (name, suffix, value))
    inspect = need_body(cases, "private static void Inspect(string label, string value, List<string> offenders, ref int counter)",
                        "CJK 扫描函数")
    if inspect:
        if re.search(re.escape(BACKSLASH) + r"u[0-9A-Fa-f]{4}", inspect):
            errors.append("CJK 扫描不得用 \\u 转义（跨工具复制会被折反斜杠）")
        used = set(re.findall(r"\(code >= (\w+)Start && code <= (\w+)End\)", inspect))
        expected = {(name, name) for name, _, _ in EXPECTED_CJK_RANGES}
        if used != expected:
            errors.append("CJK 扫描必须逐段成对使用 Start/End 常量，实际：%s" % sorted(used))

    # ---- 8. 编排：每条用例都是一整条语句、经过会话门；批量 SKIP 清单与实际一致 ----
    suite_body = need_body(suite, "private IEnumerator RunSkyIslandSuite()", "岛内编排")
    final_body = need_body(suite, "private IEnumerator RunSkyIslandFinalChecks()", "岛内收尾阶段")
    executed, delegates = [], []
    for index, (label, body) in enumerate((("岛内编排", suite_body), ("岛内收尾", final_body))):
        for line in body.splitlines():
            if not CASE_CALL.search(line):
                continue
            cm = SYNC_LINE.match(line) or CORO_LINE.match(line)
            if not cm:
                errors.append(label + " 的用例调用必须是独立的一整条语句（不得包进 if、不得换成 lambda、"
                              "不得绕过会话门直接 RunSyncCase）：" + line.strip())
                continue
            if index == 0:
                executed.append(cm.group(1))
            delegates.append(cm.group(2))
            if not re.search(r"private (?:bool|IEnumerator) " + re.escape(cm.group(2)) + r"\(", suite + cases):
                errors.append(label + " 引用了不存在的用例方法：" + cm.group(2))
    for kind, values in (("用例 id", executed), ("用例方法", delegates)):
        repeated = sorted({value for value in values if values.count(value) > 1})
        if repeated:
            errors.append("编排里重复的" + kind + "（同一个判据跑两遍，另一条用例就没跑）：" + ",".join(repeated))
    listing = suite.split("private static readonly string[] SkyIslandCaseIds", 1)
    if len(listing) != 2:
        errors.append("找不到 SkyIslandCaseIds")
    else:
        listed = set(listing[1].split("};", 1)[0].split('"')[1::2])
        missing = sorted(set(executed) - listed)
        extra = sorted(listed - set(executed))
        if missing:
            errors.append("SkyIslandCaseIds 漏登记（装配失败时报告会留空白）：" + ",".join(missing))
        if extra:
            errors.append("SkyIslandCaseIds 登记了编排里没有跑的用例：" + ",".join(extra))
    sync = need_body(suite, "private void RunSkyIslandSync(string id, SyncValidation validation)", "岛内同步用例外壳")
    if sync:
        order = [sync.find(token) for token in ("if (!SkyIslandSessionStillValid(out reason))", 'Record(id, "SKIP"',
                                                "return;", "RunSyncCase(id, validation);")]
        if min(order) < 0 or order != sorted(order):
            errors.append("岛内同步用例必须先确认这趟出击还在，不在就记 SKIP 并返回，之后才交给 RunSyncCase")
    coroutine = need_body(suite, "private IEnumerator RunSkyIslandCase(string caseId, Func<IEnumerator> factory)", "岛内协程用例外壳")
    if coroutine:
        gone = coroutine.find("if (!SkyIslandSessionStillValid(out gone))")
        factory = coroutine.find("factory()")
        if gone < 0 or factory < 0 or gone > factory:
            errors.append("岛内协程用例必须在创建协程之前确认这趟出击还在")
    valid = need_body(suite, "private bool SkyIslandSessionStillValid(out string reason)", "会话有效性判断")
    if valid:
        for token in ("session == null", "!session.IsReady", "session.ValidationScene.handle != _skyIslandSceneHandle"):
            if token not in valid:
                errors.append("会话有效性判断缺 " + token)
        if valid.find("return true;") < valid.find("_skyIslandSceneHandle"):
            errors.append("会话有效性判断在核对场景实例之前就 return true")

    # ---- 9. 会话侧的观测面：单独成文件、只读、与生产判定共用几何 ----
    need(surface, "会话观测面",
         "internal SkyIslandValidationSnapshot ValidationSnapshot()",
         "internal bool ValidationIsInsideExtractionAt(Vector3 position, out string markerName)",
         "internal static float ValidationExtractionRadius")
    if re.search(r"\b(?:internal|public|private|protected)\s+(?:static\s+)?[\w<>\[\],.]+\s+Validation[A-Z]\w*\s*(?:\(|\{|=>)",
                 session):
        errors.append("F3 观测面成员只许写在 SkyIslandSessionValidation.cs：会话主文件里又长出了 Validation* 成员，"
                      "下面的只读检查扫不到它")
    probe = need_body(surface, "internal bool ValidationIsInsideExtractionAt(Vector3 position, out string markerName)", "撤离几何探测")
    if probe:
        if "ExtractionMarkerAt(position)" not in probe:
            errors.append("撤离几何探测必须调用玩家判定同一个 ExtractionMarkerAt：逐字复制的第二份在生产判据改动后照样绿")
        for token in ("Vector3.Distance", "ExtractionRadius", "player", "extractionHeld", "extractionStarted",
                      "Close(", "SetPosition("):
            if token in probe:
                errors.append("撤离几何探测必须只读、且不得自带第二份距离判据：" + token)
    inside = need_body(session, "private bool IsInsideExtraction(out Transform marker)", "玩家撤离判定")
    if inside and "ExtractionMarkerAt(player.transform.position)" not in inside:
        errors.append("玩家撤离判定没有走共用的 ExtractionMarkerAt")
    geometry = need_body(session, "private Transform ExtractionMarkerAt(Vector3 position)", "撤离几何")
    # 布局 v2：码头 + 钟庭 + 两处航标广场，四个圈都用同一个半径、同一个解锁事实源。
    if geometry and (geometry.count("< ExtractionRadius") != 4 or
                     any(helper not in geometry for helper in ("BellExitIfUnlocked()", "WindExitIfUnlocked()",
                                                               "StarExitIfUnlocked()"))):
        errors.append("撤离几何必须对码头、已解锁的钟庭与两处航标广场用同一个 ExtractionRadius 判定")
    fields = session_fields(session)
    if len(fields) < 20:
        errors.append("解析 SkyIslandSession 字段失败（只解析到 %d 个）：观测面写字段的检查失效" % len(fields))
    flat_surface = re.sub(r"\bthis\.", "", normalize(blank_strings(surface)))
    for field in sorted(fields):
        name = re.escape(field)
        if (re.search(r"(?<![\w.])" + name + r"\s*(?:[-+*/%|&^]|<<|>>|\?\?)?=(?![=>])", flat_surface)
                or re.search(r"(?<![\w.])" + name + r"\s*(?:\+\+|--)", flat_surface)
                or re.search(r"(?:\+\+|--)\s*" + name + r"\b", flat_surface)
                or re.search(r"(?<![\w.])" + name + r"\.(?:Add|AddRange|Remove|RemoveAt|RemoveAll|Clear|Insert|"
                             r"Enqueue|Dequeue|Push|Pop|Sort|Reverse)\(", flat_surface)):
            errors.append("会话观测面写了会话字段：" + field)
    declared = set(re.findall(r"\b(?:internal|public|private|protected)\s+(?:static\s+)?[\w<>\[\],.]+\s+([A-Za-z_]\w*)\s*\(",
                              surface))
    without_new = re.sub(r"\bnew\s+[A-Za-z_][\w.]*(?:<[^<>]*>)?\s*(?=[(\[{])", "new ", blank_strings(surface))
    calls = set(re.findall(r"([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(", without_new))
    unexpected = sorted(calls - declared - READ_ONLY_HELPERS - CALL_KEYWORDS)
    if unexpected:
        errors.append("会话观测面只许调用只读助手（" + "/".join(sorted(READ_ONLY_HELPERS)) + "），多出了：" + ",".join(unexpected))

    if errors:
        for error in errors:
            print("  - " + error)
        print("SkyIslandValidationSuiteGuard: FAIL")
        raise SystemExit(1)
    print("SkyIslandValidationSuiteGuard: PASS (岛内入口与逐道启动门 / 主门互斥 / 分流与模式复位 / "
          "只读纪律：用例、会话外壳、观测面 / SKIP 通道 / CJK 区间 / 编排语句与用例清单 / 几何共用)")


if __name__ == "__main__":
    main()
