# -*- coding: utf-8 -*-
"""末班编组站 Dev 演练：只在 Dev 构建、不写存档不收录剧情、快进走生产 Tick、改过的东西一定还原（2026-11-11）。

演练（`DebugAndTools/F3GameplayValidationLastShiftDrill*.cs`，钩子 `LastShiftYard/LastShiftDrillHooks.cs`）在玩家这一趟
末班编组站出击里快进班次、扳道岔、拉铃、把主角搬来搬去、绕场刷出押运队。它能留在仓库里，靠的是下面这些约束（照潮闸镇演练）：

1. **只在 Dev 构建里存在**：三份文件整份包在 `#if BOSSRUSH_DEV` 里并登记编译清单；F3 面板按钮与处理函数在 `#if` 里；
   生产文件里的钩子点（会话字幕计数、导航只读计数）都在 `#if BOSSRUSH_DEV` 里。
2. **演练不写存档、不收录剧情、不读线索、不开箱、不签收、不发物品、不写官方时钟、不切图、不收会话**（禁用清单）。
3. **快进不是另一套模拟**：钩子只许反复调生产 `LastShiftTrains.Tick`，不许直接推班次表（`timetable.Tick(`）。
4. **入口先过 CheckLastShiftStartGate，再置 _lastShiftDrill，最后开会话**；标志只在 CompleteSession 复位，复位前先记下给收尾用。
5. **会话外壳按 _lastShiftDrill 不写运行标记、不抑制提示条、收尾不跑主套件清理**；报告头如实写 read_only=false / drill=true。
6. **还原**：撤铃、收箭头、同步出口、主角送回开跑处、剧情存档快照比对；CompleteSession 里另有同步兜底；撤离读条那条在 finally 里离开撤离环。
7. **站位离出口 5 m 外、离两股道 4.5 m 外**：站进开着的撤离环读满会真的撤离并写剧情存档，站在道上会被车推走。
8. 只读套件不引用演练；纯判据有执行回归（`LastShiftDrillJudges`）。

守卫钉结构，证明不了演练跑起来是对的——那要 owner 在 Dev 构建里按一次演练按钮，AI 读报告。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source  # noqa: E402

DRILL = "DebugAndTools/F3GameplayValidationLastShiftDrill.cs"
CASES = "DebugAndTools/F3GameplayValidationLastShiftDrillCases.cs"
HOOKS = "LastShiftYard/LastShiftDrillHooks.cs"
RUNNER = "DebugAndTools/F3GameplayValidationRunner.cs"
EXECUTION = "DebugAndTools/F3GameplayValidationExecution.cs"
SESSION = "LastShiftYard/LastShiftSession.cs"
TRAINS = "LastShiftYard/LastShiftTrains.cs"
NAVIGATION = "DebugAndTools/ArenaPrototype/ArenaPrototypeNavigation.cs"
REGRESSIONS = "tools/run_runtime_regressions.py"
BAT = "compile_official.bat"
READ_ONLY = ("DebugAndTools/F3GameplayValidationSkyIsland.cs", "DebugAndTools/F3GameplayValidationSkyIslandCases.cs",
             "DebugAndTools/F3GameplayValidationSkyIslandRuntimeCases.cs")
DEV_FILES = (DRILL, CASES, HOOKS)
PRODUCTION_HOOKED = (SESSION, TRAINS, NAVIGATION, "LastShiftYard/LastShiftLoot.cs", "LastShiftYard/LastShiftExtraction.cs",
                     "LastShiftYard/LastShiftTimetable.cs", "LastShiftYard/LastShiftEnemies.cs", "LastShiftYard/LastShiftYardDevices.cs")
BACKSLASH = chr(92)

# 生产文件里只许出现在 #if BOSSRUSH_DEV 真分支里的钩子标识。
HOOK_TOKENS = re.compile(r"\b(?:DevNoteStatus|DevCountNodes|DevFastForward|DevSyncExits|DevThrowSwitch|DevRingBell|DevReadManifest"
                         r"|DevClearBell|DevHideArrows|DevTeleport|DevBoxes|DevEnemies|DevStatusSince|DevStatusMark)\b")

# 演练（两份文件）与钩子里不许出现的入口（清洗注释后按正则找）。每一条都写明为什么。
FORBIDDEN = (
    (r"\bSavesSystem\.(?:Save|SaveFile|SaveGlobal|SetFile|DeleteCurrentSave|RestoreIndexedBackup|CollectSaveData)\b", "直接写档或换槽"),
    (r"\bLastShiftStory\.(?:CommitRun|TryHandIn|TryShowNotice|Bind|Tick)\b|\bLastShiftStoryPersistence\b|\bCommitStory\b",
     "推进或写剧情存档"),
    (r"\b(?:LastShiftNoteBridge|NoteIndex)\.", "改官方笔记图鉴"),
    (r"\b(?:ItemAssetsCollection|ItemUtilities)\.|\b(?:GiveToPlayerOrStorage|SendToPlayer|AddOfficialStack|GiveStub)\(|\bTidegateInventory\.Consume\(",
     "发物品或扣物品"),
    (r"\bOnStartLoot\b|\bStartLoot\(|\.Interact\(|\b(?:ClaimCargo|OpenSideCabinet|OnDevice|Unlock|ApplyDrop)\(",
     "翻开箱子、签收封签货或开侧柜（会放货、扣钢印、记签收）"),
    (r"\b(?:run|DevRun)\.Record\(|\bRead\(\s*\w+\s*\)\s*;", "往本趟剧情记录里记东西或读线索（读齐三条还会发票根）"),
    (r"\bGameClock\.|\bclockTimeScale\b|\bTime\.timeScale\s*=", "写官方时钟或时间流速（等车只许走 DevFastForward）"),
    (r"\bLoadScene(?:Async)?\(|\bReturnToBase\(|\.Close\(|\bCleanup\(", "切图、返航或收掉会话"),
    (r"\b(?:WriteRunMarker|ValidationSafeCleanup|BossRushSaveFileThrottle)\b", "写运行标记或跑主套件清理"),
)

READ_ONLY_FORBIDDEN = re.compile(r"\b(?:TryStartLastShiftDrill|RunLastShiftDrill\w*|_lastShiftDrill\w*|LastShiftDrill\w*|DevFastForward"
                                 r"|DevThrowSwitch|DevRingBell|DevClearBell)\b")


def directive_frames(raw):
    """逐行给出 (行号, 去注释的代码, 这一行是否处在 #if BOSSRUSH_DEV 的真分支里)。"""
    stack = []
    result = []
    for number, line in enumerate(raw.splitlines(), 1):
        code = line.split("//", 1)[0].strip()
        if code.startswith("#if"):
            stack.append("BOSSRUSH_DEV" in code and "!" not in code)
        elif code.startswith("#else") and stack:
            stack[-1] = not stack[-1]
        elif code.startswith("#endif") and stack:
            stack.pop()
        result.append((number, code, any(stack)))
    return result


def wrapped_whole_file(raw):
    lines = [line.strip() for line in raw.splitlines() if line.strip()]
    if not lines or lines[0] != "#if BOSSRUSH_DEV" or lines[-1] != "#endif":
        return False
    depth = 0
    body = [line for line in raw.splitlines() if line.strip()]
    for index, line in enumerate(body):
        code = line.strip()
        if code.startswith("#if"):
            depth += 1
        elif code.startswith("#endif"):
            depth -= 1
            if depth == 0 and index != len(body) - 1:
                return False
    return depth == 0


def body_of(source, signature, start=0):
    at = source.find(signature, start)
    if at < 0:
        return None
    brace = source.find("{", at)
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:i]
    return None


def check(raw):
    errors = []
    clean = {rel: clean_source(text) for rel, text in raw.items() if rel.endswith(".cs")}

    # ---- 1. 只在 Dev 构建里存在 ----
    for rel in DEV_FILES:
        if not wrapped_whole_file(raw[rel]):
            errors.append(rel + " 没有整份包在 #if BOSSRUSH_DEV … #endif 里：正式构建里会出现会改状态的演练或钩子")
        if "echo(" + rel.replace("/", BACKSLASH) not in raw[BAT]:
            errors.append("编译清单缺少 " + rel + "（无通配符，漏了不报错，Dev 构建里按钮点了会编不过）")
    for number, code, in_dev in directive_frames(raw[RUNNER]):
        if ("StartLastShiftDrillFromF3" in code or "TryStartLastShiftDrill" in code) and not in_dev:
            errors.append(RUNNER + ":%d 在 #if BOSSRUSH_DEV 之外引用了末班编组站演练入口（正式构建的 F3 面板不许有这个按钮）" % number)
    if "StartLastShiftDrillFromF3" not in clean[RUNNER] or "BossRushUIColors.Warning, StartLastShiftDrillFromF3);" not in clean[RUNNER]:
        errors.append("F3 验收页上没有末班编组站演练按钮（独立入口，警示色）")
    handler = body_of(clean[RUNNER], "private void StartLastShiftDrillFromF3()") or ""
    if "F3GameplayValidationRunner.TryStartLastShiftDrill(this, out reason)" not in handler:
        errors.append("演练按钮的处理函数必须经 TryStartLastShiftDrill（开跑门）启动")
    for rel in PRODUCTION_HOOKED:
        for number, code, in_dev in directive_frames(raw[rel]):
            if HOOK_TOKENS.search(code) and not in_dev:
                errors.append(rel + ":%d 的 Dev 钩子在 #if BOSSRUSH_DEV 之外：%s" % (number, code[:80]))
    status = body_of(clean[SESSION], "private void Status(string message, bool error)") or ""
    if "DevNoteStatus(message, error);" not in status:
        errors.append("会话字幕没有接上 Dev 计数（演练按字幕核对提示）")

    # ---- 2. 演练与钩子不写存档、不收录、不开箱、不发物品、不写时钟、不切图 ----
    for rel in (DRILL, CASES, HOOKS):
        for pattern, why in FORBIDDEN:
            if re.search(pattern, clean[rel]):
                errors.append(rel + " 不得" + why + "：/" + pattern + "/")
    for rel in READ_ONLY:
        if READ_ONLY_FORBIDDEN.search(clean[rel]):
            errors.append(rel + " 是只读套件，不得引用末班编组站演练或它的钩子")

    # ---- 3. 快进走生产 Tick，不另做模拟 ----
    forward = body_of(clean[HOOKS], "internal double DevFastForward(CharacterMainControl player, double seconds)") or ""
    if "if (Tick(player, step)) break;" not in forward or "DevSyncExits(player);" not in forward:
        errors.append("DevFastForward 必须逐步调生产 Tick（被撞死即停）并在最后同步出口")
    sync = body_of(clean[HOOKS], "internal void DevSyncExits(CharacterMainControl player)") or ""
    if "nextExits = 0f;" not in sync or "Tick(player, 0f);" not in sync:
        errors.append("DevSyncExits 要靠零推进的生产 Tick 推送出口（不直接改撤离 owner）")
    if re.search(r"\btimetable\.Tick\(|\.Timetable\.Tick\(|\bSetOpen\(", clean[HOOKS] + clean[DRILL] + clean[CASES]):
        errors.append("演练与钩子不得直接推班次表或直接开关出口：只许经生产 LastShiftTrains.Tick")
    for name, target in (("DevThrowSwitch", "ThrowSwitch();"), ("DevRingBell", "RingBell();"), ("DevReadManifest", "ReadManifest();")):
        hook = body_of(clean[HOOKS], "internal void " + name + "()") or ""
        if target not in hook:
            errors.append(name + " 必须调交互完成回调对应的生产方法 " + target)

    # ---- 4. 入口顺序与标志 ----
    start = body_of(clean[DRILL], "internal static bool TryStartLastShiftDrill(ModBehaviour host, out string reason)") or ""
    gate = start.find("if (!_instance.CheckLastShiftStartGate(out reason)) return false;")
    flag = start.find("_instance._lastShiftDrill = true;")
    begin = start.find("_instance.BeginSession(out reason)")
    if not (0 <= gate < flag < begin):
        errors.append("演练入口必须先过 CheckLastShiftStartGate（Dev + 专用测试档 + 在编组站 + 没开模态），再置 _lastShiftDrill，最后开会话")
    start_gate = body_of(clean[DRILL], "private bool CheckLastShiftStartGate(out string reason)") or ""
    for token in ("IsDedicatedCurrentSlot()", "ModBehaviour.DevModeEnabled", "GetComponent<LastShiftSession>()", "LastShiftRaidScene.IsScene(",
                  "ZombieModeUIHelper.ModalInputLeaseCount != 0", "session.DevLoot.CargoClaimed"):
        if token not in start_gate:
            errors.append("末班编组站演练开跑门缺少 " + token)
    sets = sum(len(re.findall(r"\b_lastShiftDrill\s*=\s*true\s*;", clean[rel])) for rel in (DRILL, CASES, EXECUTION, RUNNER))
    if sets != 1:
        errors.append("_lastShiftDrill 只许在演练入口置位一次（现在 %d 处）" % sets)
    execution = clean[EXECUTION]
    complete = body_of(execution, "private void CompleteSession()") or ""
    capture, reset = complete.find("bool lastShift = _lastShiftDrill;"), complete.find("_lastShiftDrill = false;")
    if not (0 <= capture < reset) or len(re.findall(r"\b_lastShiftDrill\s*=\s*false\s*;", execution)) != 1:
        errors.append("_lastShiftDrill 必须且只能在 CompleteSession 里先记下、再复位")
    fallback_frames = [in_dev for _, code, in_dev in directive_frames(raw[EXECUTION]) if "FinishLastShiftDrillRestoreSynchronously" in code]
    if fallback_frames != [True] or "if (lastShift) FinishLastShiftDrillRestoreSynchronously();" not in complete:
        errors.append("CompleteSession 要在 #if BOSSRUSH_DEV 里按 lastShift 同步兜底还原一次（取消、异常、超时也走到）")
    if not re.search(r"if \(!skyIsland && !tidegate && !lastShift && !_slotChanged && SavesSystem\.CurrentSlot == _sessionSlot\)", complete):
        errors.append("末班编组站演练收尾不得跑面向主套件的全宿主清理（ValidationSafeCleanup 按 !lastShift 门控）")

    # ---- 5. 会话外壳 ----
    begin_body = body_of(execution, "private bool BeginSession(out string reason)") or ""
    marker = body_of(begin_body, "if (!_skyIslandMode && !_tidegateDrill && !_lastShiftDrill)")
    if marker is None or "WriteRunMarker()" not in marker:
        errors.append("末班编组站演练不得写运行标记：WriteRunMarker 必须包在 if (... && !_lastShiftDrill) 块里")
    if not re.search(r'_lastShiftDrill\s*\?\s*"SESSION \| slot=" \+ _sessionSlot \+ " \| assisted=true \| read_only=false \| drill=true \| '
                     r'map=last_shift_yard \| save_writes=false \| story_commit=false', execution):
        errors.append("会话报告头没有按 _lastShiftDrill 如实写 read_only=false / drill=true / save_writes=false / story_commit=false")
    if not re.search(r"if \(_host != null && !_skyIslandMode && !_tidegateDrill && !_lastShiftDrill\) _host\.GameplayValidationSuppressNotifications = true;",
                     execution):
        errors.append("末班编组站演练不得吞掉这一趟的正常提示（抑制提示条按 !_lastShiftDrill 门控）")
    if '"SUITE | LAST_SHIFT_DRILL | read_only=false | drill=true | scene="' not in clean[DRILL]:
        errors.append("套件头没有如实写 read_only=false / drill=true")

    # ---- 6. 还原 ----
    restore = body_of(clean[DRILL], "private List<string> RestoreLastShiftDrillState(out string metrics)") or ""
    for token in ("if (_lastShiftDrillBellRung) t.DevClearBell();", "trains.DevHideArrows();", "trains.DevSyncExits(player);",
                  "LastShiftDrillSafePoint(session, _lastShiftDrillStart, out back)", "session.DevTeleport(back, out why)",
                  "LastShiftStory.Runs != _lastShiftDrillRunsBefore", "LastShiftStory.Records != _lastShiftDrillRecordsBefore",
                  "_lastShiftDrillRestored = true;"):
        if token not in restore:
            errors.append("还原缺少 " + token)
    suite = body_of(clean[DRILL], "private IEnumerator RunLastShiftDrillSuite()") or ""
    if "RecordLastShiftDrillRestore();" not in suite or suite.rfind("RecordLastShiftDrillRestore();") < suite.rfind("RunLastShiftDrillCase("):
        errors.append("套件最后一步必须是还原（RecordLastShiftDrillRestore）")
    fallback = body_of(clean[DRILL], "private void FinishLastShiftDrillRestoreSynchronously()") or ""
    if "if (_lastShiftDrillRestored) return;" not in fallback or "RestoreLastShiftDrillState(" not in fallback:
        errors.append("同步兜底要先看还原跑过没有，没跑过才还原")
    ring = body_of(clean[CASES], "private IEnumerator RunLastShiftDrillSwitchAndExits()") or ""
    if "_lastShiftDrillBellRung = true;" not in ring or ring.find("_lastShiftDrillBellRung = true;") < ring.find("trains.DevRingBell();"):
        errors.append("拉过铃要立刻记下（还原按它撤铃）")
    hold = body_of(clean[CASES], "private IEnumerator RunLastShiftDrillExtractionHold()") or ""
    hold_finally = body_of(hold, "finally") if hold else None
    if hold_finally is None or "LastShiftDrillStepAway(" not in hold_finally:
        errors.append("撤离读条那条必须在 finally 里离开撤离环：读满会真的撤离并写剧情存档")
    if not re.search(r"Time\.realtimeSinceStartup \+ 1\.5f", hold) or "if (held > 3f) break;" not in hold:
        errors.append("撤离读条只许站 1.5 秒、读到 3 秒必走（北公路 5 秒读满）")

    # ---- 7. 站位离出口与道 ----
    safe = body_of(clean[DRILL], "private static bool LastShiftDrillSafePoint(LastShiftSession session, Vector3 near, out Vector3 point)") or ""
    if "LastShiftDrillExitWithin(session.DevWorld, snapped, 5f) != null" not in safe:
        errors.append("安全站位要离任何出口 5 m 以上（撤离环最大 3 m）")
    if "LastShiftDrillTrackClear(snapped.x, -snapped.z, LastShiftDrillTrackMargin)" not in safe:
        errors.append("安全站位要离两股道中心线 LastShiftDrillTrackMargin 以上（站在道上会被车推走）")
    margin = re.search(r"private const double LastShiftDrillTrackMargin = ([0-9.]+);", clean[DRILL])
    if not margin or float(margin.group(1)) < 4.0:
        errors.append("离道余量不得小于 4 m（车体半宽 1.65 + 推挤 0.24 + 余量）")
    for signature in ("private IEnumerator LastShiftDrillStand(", "private static bool LastShiftDrillStepAway("):
        source = clean[DRILL] if "LastShiftDrillStand(" in signature else clean[CASES]
        if "LastShiftDrillSafePoint(" not in (body_of(source, signature) or ""):
            errors.append(signature.split()[-1] + " 必须走 LastShiftDrillSafePoint")
    rail = body_of(clean[DRILL], "private IEnumerator LastShiftDrillStandAtRail(") or ""
    if "LastShiftDrillExitWithin(session.DevWorld, point, 5f) == null" not in rail or "LastShiftDrillTrackClear(point.x, -point.z, LastShiftDrillTrackMargin)" not in rail:
        errors.append("站到南护栏后也要离出口 5 m、离道 4.5 m")
    # 用例里直接搬主角只许去北公路读条（DevTeleport(north.position, ...)）或离开撤离环（point 来自 LastShiftDrillSafePoint）。
    snaps = len(re.findall(r"\bDevTeleport\(", clean[CASES]))
    allowed = len(re.findall(r"\bDevTeleport\((?:north\.position|point), ", clean[CASES]))
    if snaps != allowed:
        errors.append("用例里直接搬主角只许去北公路读条或离开撤离环（其余一律 LastShiftDrillStand）：%d 处里有 %d 处不合规" % (snaps, snaps - allowed))
    drill_snaps = len(re.findall(r"\bDevTeleport\(", clean[DRILL]))
    drill_allowed = len(re.findall(r"\bDevTeleport\((?:point|back), ", clean[DRILL]))
    if drill_snaps != drill_allowed:
        errors.append("演练外壳里搬主角只许去安全点（point / back）")

    # ---- 8. 纯判据执行回归 ----
    if "#region 纯判据" not in raw[DRILL]:
        errors.append("演练纯判据区缺失（tests/fixtures/LastShiftDrillJudges 抽它执行）")
    if '"LastShiftDrillJudges"' not in raw[REGRESSIONS]:
        errors.append("执行回归聚合器没有登记 LastShiftDrillJudges")
    return errors


def main():
    raw = {}
    rels = set(DEV_FILES + PRODUCTION_HOOKED + READ_ONLY + (RUNNER, EXECUTION, REGRESSIONS, BAT))
    for rel in rels:
        path = ROOT / rel
        raw[rel] = path.read_text(encoding="utf-8-sig", errors="ignore").replace("\r\n", "\n") if path.is_file() else None
    missing = [rel for rel, value in raw.items() if value is None]
    if missing:
        print("LastShiftDrillGuard: FAIL - 缺文件：" + ", ".join(sorted(missing)))
        return 1
    errors = check(raw)

    probes = (
        (DRILL, "#if BOSSRUSH_DEV\n", "", "拆掉演练文件的 #if"),
        (CASES, "#if BOSSRUSH_DEV\n", "", "拆掉用例文件的 #if"),
        (HOOKS, "#if BOSSRUSH_DEV\n", "", "拆掉钩子文件的 #if"),
        (BAT, "echo(DebugAndTools" + BACKSLASH + "F3GameplayValidationLastShiftDrillCases.cs\n", "", "编译清单漏登记用例文件"),
        (BAT, "echo(LastShiftYard" + BACKSLASH + "LastShiftDrillHooks.cs\n", "", "编译清单漏登记钩子文件"),
        (RUNNER, "        private void StartFullGameplayValidationFromF3()\n        {\n",
         "        private void StartFullGameplayValidationFromF3()\n        {\n            StartLastShiftDrillFromF3();\n", "演练入口挪出 #if"),
        (RUNNER, "            if (F3GameplayValidationRunner.TryStartLastShiftDrill(this, out reason))",
         "            if (F3GameplayValidationRunner.TryStartTidegateDrill(this, out reason))", "按钮处理函数不再经演练开跑门"),
        (SESSION, "#if BOSSRUSH_DEV\n            // Dev 演练按字幕核对", "            // Dev 演练按字幕核对", "字幕钩子挪出 #if"),
        (NAVIGATION, "#if BOSSRUSH_DEV\n        /// <summary>\n        /// Dev 演练只读计数", "        /// <summary>\n        /// Dev 演练只读计数",
         "导航计数钩子挪出 #if"),
        (HOOKS, "                if (Tick(player, step)) break;", "                timetable.Tick(step, 0, 0);", "快进另做模拟"),
        (HOOKS, "            nextExits = 0f;\n            Tick(player, 0f);", "            extraction.SetOpen(\"bell\", true);", "直接开出口"),
        (CASES, "trains.DevRingBell();", "trains.DevRingBell(); LastShiftStory.CommitRun(0, 0);", "演练写剧情存档"),
        (CASES, "trains.DevRingBell();", "trains.DevRingBell(); SavesSystem.SaveFile(false);", "演练写档"),
        (CASES, "trains.DevRingBell();", "trains.DevRingBell(); GameClock.Instance.clockTimeScale = 600f;", "演练改官方时钟"),
        (CASES, "session.DevTrains.DevReadManifest();", "session.DevTrains.DevReadManifest(); run.Record(\"red\");", "演练往本趟记录记东西"),
        (CASES, "LastShiftLoot loot = session.DevLoot;", "LastShiftLoot loot = session.DevLoot; loot.ClaimCargo(null);", "演练签收封签货"),
        (CASES, "yield return LastShiftDrillStand(session, poi.position, errors, poi.name);",
         "string skip; session.DevTeleport(poi.position, out skip);", "绕过安全站位直接搬主角"),
        (DRILL, "if (!_instance.CheckLastShiftStartGate(out reason)) return false;", "", "绕过开跑门"),
        (DRILL, "            if (!IsDedicatedCurrentSlot()) { reason = \"当前槽不是专用测试档，请回基地先点标记按钮\"; return false; }\n", "", "开跑门不看专用测试档"),
        (DRILL, "if (_lastShiftDrillBellRung) t.DevClearBell();", "", "还原不撤铃"),
        (DRILL, "if (_lastShiftDrillRestored) return;", "", "兜底不看还原跑过没有"),
        (DRILL, "LastShiftDrillExitWithin(session.DevWorld, snapped, 5f) != null", "LastShiftDrillExitWithin(session.DevWorld, snapped, 1f) != null",
         "站位贴着撤离环"),
        (DRILL, "private const double LastShiftDrillTrackMargin = 4.5;", "private const double LastShiftDrillTrackMargin = 1.5;", "站位贴着道"),
        (CASES, "                LastShiftDrillStepAway(session, north.position);\n            }\n", "            }\n", "撤离读条不离开撤离环"),
        (EXECUTION, "if (lastShift) FinishLastShiftDrillRestoreSynchronously();", "", "收尾不兜底还原"),
        (EXECUTION, "read_only=false | drill=true | map=last_shift_yard", "read_only=true | drill=true | map=last_shift_yard", "报告头谎报只读"),
        (EXECUTION, "if (!_skyIslandMode && !_tidegateDrill && !_lastShiftDrill)\n", "if (!_skyIslandMode && !_tidegateDrill)\n", "演练写运行标记"),
        (EXECUTION, "if (!skyIsland && !tidegate && !lastShift && !_slotChanged", "if (!skyIsland && !tidegate && !_slotChanged", "演练收尾跑主套件清理"),
        (REGRESSIONS, '"LastShiftDrillJudges", ', "", "纯判据回归没登记"),
        (READ_ONLY[0], "namespace BossRush\n{\n", "namespace BossRush\n{\n    // x\n    internal static class ZzProbe { static void P() { F3GameplayValidationRunner.TryStartLastShiftDrill(null, out string r); } }\n",
         "只读套件引用演练入口"),
    )
    for rel, before, after, label in probes:
        text = raw[rel]
        if before not in text:
            errors.append("反向检查锚点失效（%s）：%s" % (label, before[:60]))
            continue
        altered = dict(raw)
        altered[rel] = text.replace(before, after, 1)
        if not check(altered):
            errors.append("反向检查失效：%s 之后守卫仍然全绿" % label)

    if errors:
        print("LastShiftDrillGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("LastShiftDrillGuard: PASS（整份 #if BOSSRUSH_DEV 与生产钩子门控 / 不写存档不收录不读线索不开箱不签收不发物品不写时钟 / "
          "快进走生产 Tick / 开跑门与标志 / 会话外壳 / 还原与兜底 / 站位离出口与道 / 纯判据回归；%d 个反向检查）" % len(probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
