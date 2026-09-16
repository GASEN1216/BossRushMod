# -*- coding: utf-8 -*-
"""全自动实机验收的编排纪律（2026-09-14）。

F3「自动验收 + 完整待测清单」在 Dev 构建里跑完主套件之后，带着专用测试档去天空岛：瞬移、交互、刷怪、换语言、
把剧情清空成新档按阶段推进，最后还原。它比 Dev 演练更进一步——会写测试档的天空岛剧情与背包。能被接受，全靠：

1. 只在 Dev 构建里存在：每个文件整份 #if BOSSRUSH_DEV；宿主文件里的钩子也都在 #if BOSSRUSH_DEV 里；
2. 任何写入先过 AutotestWriteAllowed（Dev + 专用测试档 + 这一轮正在跑 + 槽位没换 + 快照已落盘）；
   存档写入口（DevAutotest*）第一句就过门；直接落盘的方法自己过门，或写明「只经带门入口写 / 只被带门方法调用」；
3. 快照先于第一次写入；还原两道——收尾阶段（_closingSession，取消打断不了）与 CompleteSession 的同步兜底——
   都复位语言、强制夜里、无敌、血量与时间流速；中途崩溃后回基地按快照键恢复；
4. 岛内只读套件与 Dev 演练的文件不引用编排的任何符号；
5. 截图前确认 F3 菜单等调试浮层不在屏幕上，帧末用 UnityEngine.ScreenCapture 取像素（不用 Steam 截图）；
   编译清单登记全部文件与 ScreenCaptureModule 引用。

编译挡不住这些：Dev 构建照样编得过，而这些代码恰恰只在 Dev 构建里跑。反向检查在内存里逐条破坏，必须转红。
正式构建的 DLL 里确实查不到这些标识，由 tools/check_dll_identifiers.py 在构建之后实查（守卫只管源码）。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import strip_comments  # noqa: E402

AUTOTEST = "DebugAndTools/F3GameplayValidationAutotest.cs"
ACTIONS = "DebugAndTools/F3GameplayValidationAutotestActions.cs"
CAPTURE = "DebugAndTools/F3GameplayValidationAutotestCapture.cs"
STORY = "DebugAndTools/F3GameplayValidationAutotestStory.cs"
EXECUTION = "DebugAndTools/F3GameplayValidationExecution.cs"
BAT = "compile_official.bat"

AUTOTEST_FILES = (
    "DebugAndTools/F3GameplayValidationAutotestJudges.cs",
    AUTOTEST,
    ACTIONS,
    "DebugAndTools/F3GameplayValidationAutotestAsserts.cs",
    "DebugAndTools/F3GameplayValidationAutotestBosses.cs",
    CAPTURE,
    STORY,
    "DebugAndTools/F3GameplayValidationAutotestReport.cs",
    "DebugAndTools/SkyIsland/SkyIslandSessionAutotest.cs",
    "DebugAndTools/SkyIsland/SkyIslandStoryServiceAutotest.cs",
)
# 岛内只读套件（与 SkyIslandReadOnlySuiteDrillIsolationGuard 同一份）与 Dev 演练：都不许引用编排。
ISOLATED = (
    "DebugAndTools/F3GameplayValidationSkyIsland.cs",
    "DebugAndTools/F3GameplayValidationSkyIslandCases.cs",
    "DebugAndTools/F3GameplayValidationSkyIslandRuntimeCases.cs",
    "DebugAndTools/SkyIsland/SkyIslandSessionValidation.cs",
    "DebugAndTools/F3GameplayValidationSkyIslandDrill.cs",
    "DebugAndTools/SkyIsland/SkyIslandGnatsDrill.cs",
)
SYMBOL = re.compile(r"\b\w*Autotest\w*\b|\b_autotest\w*\b|SKY_AUTO_")

# 存档写入口：第一句必须是写入门。
GATED_ENTRIES = {
    "DebugAndTools/SkyIsland/SkyIslandStoryServiceAutotest.cs": ("DevAutotestReplace", "DevAutotestFlush"),
    "DebugAndTools/SkyIsland/SkyIslandSessionAutotest.cs": ("DevAutotestTeleport", "DevAutotestResetEncounter", "DevAutotestReturnToBase"),
}
READ_ONLY_ENTRIES = {
    "DevAutotestOpen": "只烙印槽位、订阅并读档，不写",
    "DevAutotestFind": "只按名字取地形根下的标记",
}
# 直接改存档内容（剧情、背包、快照键）的调用。
PERSIST_CALLS = ("SavesSystem.Save<", "SavesSystem.SaveFile(", "SendToPlayerCharacterInventory(", "DevAutotestReplace(",
                 "DevAutotestFlush(", ".RecordEncounterCleared(", ".TryApply(", ".RecordNote(", ".LightLamp(",
                 ".RemoveItem(", "StackCount -=")
GATE_TOKENS = ("AutotestWriteAllowed(out", "IsDedicatedCurrentSlot()")
# 自身不过门的写入方法：只经带门入口写。
VIA_ENTRY = {
    "RestoreAutotestStoryOnIsland": "只经 DevAutotestReplace 写（入口第一句过门）",
    "RestoreAutotestStoryAtBase": "只经 DevAutotestReplace 写（入口第一句过门）",
}
# 自身不过门的写入方法：只被这些方法调用。
VIA_CALLER = {
    "LightAutotestLamp": ("ApplyAutotestStage",),
    "RemoveFromInventory": ("RemoveOwnedItems", "RemoveFromInventory"),
    "RemoveOwnedItems": ("ReclaimAutotestItems",),
}
# 必须自己过写入门的方法。
MUST_GATE = {
    STORY: ("TakeAutotestSnapshotCore", "ApplyAutotestStage", "GiveAutotestItems", "ClearAutotestSnapshotKey", "ReclaimAutotestItems"),
    ACTIONS: ("AutotestStepSkipReason", "AutotestClearNearby", "AutotestSetHealth"),
}

METHOD = re.compile(r"(?m)^[ \t]*(?:(?:private|internal|public|protected|static|sealed|override|virtual)\s+)+[\w<>\[\],.?]+\s+(\w+)\s*\(")


def blank_strings(code):
    """字符串与字符字面量的内容换成空格（保留引号与长度），给括号配对用。"""
    out = list(code)
    i, n = 0, len(code)
    while i < n:
        ch = code[i]
        if ch == "@" and i + 1 < n and code[i + 1] == '"':
            i += 2
            while i < n:
                if code[i] == '"':
                    if i + 1 < n and code[i + 1] == '"':
                        out[i] = out[i + 1] = " "
                        i += 2
                        continue
                    i += 1
                    break
                if code[i] != "\n":
                    out[i] = " "
                i += 1
            continue
        if ch in "\"'":
            quote = ch
            i += 1
            while i < n:
                c = code[i]
                if c == "\\" and i + 1 < n:
                    out[i] = " "
                    if code[i + 1] != "\n":
                        out[i + 1] = " "
                    i += 2
                    continue
                if c == quote:
                    i += 1
                    break
                if c == "\n":
                    break
                out[i] = " "
                i += 1
            continue
        i += 1
    return "".join(out)


def methods(blank):
    """方法名 → [(声明起点, 方法体 '{' 位置, 方法体结束位置)]。"""
    result = {}
    for match in METHOD.finditer(blank):
        i, depth = match.end(), 1
        while i < len(blank) and depth:
            depth += (blank[i] == "(") - (blank[i] == ")")
            i += 1
        j = i
        while j < len(blank) and blank[j] in " \t\r\n":
            j += 1
        if j >= len(blank) or blank[j] != "{":
            continue
        k, depth = j + 1, 1
        while k < len(blank) and depth:
            depth += (blank[k] == "{") - (blank[k] == "}")
            k += 1
        result.setdefault(match.group(1), []).append((match.start(), j, k))
    return result


class Source:
    def __init__(self, raw):
        self.raw = raw
        self.clean = strip_comments(raw)
        self.blank = blank_strings(self.clean)
        self.methods = methods(self.blank)

    def body(self, name):
        spans = self.methods.get(name)
        if not spans:
            return None
        _, start, end = spans[0]
        return self.clean[start:end]

    def enclosing(self, pos):
        best = None
        for name, spans in self.methods.items():
            for _, start, end in spans:
                if start <= pos < end and (best is None or end - start < best[2] - best[1]):
                    best = (name, start, end)
        return best[0] if best else None


def dev_mask(raw):
    """逐行：是否处在 #if BOSSRUSH_DEV 的真分支里（#else 分支不算）。预处理指令行本身记为 True（不检查）。"""
    stack, mask = [], []
    for line in raw.split("\n"):
        text = line.strip()
        if text.startswith("#if"):
            stack.append("dev" if re.match(r"#if\s+BOSSRUSH_DEV\b", text) else "other")
            mask.append(True)
        elif text.startswith("#elif") or text.startswith("#else"):
            if stack and stack[-1] == "dev":
                stack[-1] = "dev_else"
            mask.append(True)
        elif text.startswith("#endif"):
            if stack:
                stack.pop()
            mask.append(True)
        else:
            mask.append("dev" in stack)
    return mask


def ordered(errors, text, tokens, why):
    if text is None:
        errors.append("找不到方法体：" + why)
        return
    at = 0
    for token in tokens:
        found = text.find(token, at)
        if found < 0:
            errors.append("%s：缺少或次序不对「%s」" % (why, token))
            return
        at = found + len(token)


def check(src):
    errors = []
    parsed = {rel: Source(raw) for rel, raw in src.items() if rel.endswith(".cs")}

    # 1. 整份 Dev，新文件要登记进本守卫
    for rel in AUTOTEST_FILES:
        raw = src.get(rel)
        if raw is None:
            errors.append("找不到自动验收文件 " + rel)
            continue
        lines = [line.strip() for line in raw.replace("\r", "").split("\n")]
        body = [i for i, line in enumerate(lines) if line]
        if not body or lines[body[0]] != "#if BOSSRUSH_DEV" or lines[body[-1]] != "#endif":
            errors.append(rel + " 必须整份包在 #if BOSSRUSH_DEV … #endif 里（第一行与最后一行）")
            continue
        depth = 0
        for i in body:
            if lines[i].startswith("#if"):
                depth += 1
            elif lines[i].startswith("#endif"):
                depth -= 1
                if depth <= 0 and i != body[-1]:
                    errors.append("%s 第 %d 行就闭合了最外层的 #if BOSSRUSH_DEV：之后的代码会进正式构建" % (rel, i + 1))
                    break
    for extra in src.get("__autotest_glob__", ()):
        if extra not in AUTOTEST_FILES:
            errors.append("新的自动验收文件 %s 没登记进本守卫的 AUTOTEST_FILES（整份 Dev 与编译清单都要核）" % extra)

    # 2. 编译清单
    bat = src.get(BAT, "")
    for rel in AUTOTEST_FILES:
        if "echo(" + rel.replace("/", "\\") + "\r\n" not in bat:
            errors.append("compile_official.bat 没登记 " + rel + "（不登记就不进 DLL，也不报错）")
    if "echo(/reference:UnityEngine.ScreenCaptureModule.dll\r\n" not in bat:
        errors.append("compile_official.bat 缺 /reference:UnityEngine.ScreenCaptureModule.dll（截图走 UnityEngine.ScreenCapture）")
    if bat.count("\n") != bat.count("\r\n"):
        errors.append("compile_official.bat 必须保持 CRLF（换成 LF 后 cmd 会报与代码无关的怪错）")

    # 3. 宿主文件里的钩子都在 #if BOSSRUSH_DEV 里
    for rel, source in parsed.items():
        if rel in AUTOTEST_FILES:
            continue
        mask = dev_mask(source.raw)
        for number, line in enumerate(source.clean.split("\n")):
            if number < len(mask) and mask[number]:
                continue
            hit = SYMBOL.search(line)
            if hit:
                errors.append("%s:%d 在 #if BOSSRUSH_DEV 之外引用了自动验收符号 %s（正式构建里没有它）" % (rel, number + 1, hit.group(0)))
    execution = parsed.get(EXECUTION)
    if execution is None:
        errors.append("找不到 " + EXECUTION)
    else:
        for method, token, why in (("RunSession", "RunSkyIslandAutotestLeg()", "主套件回到基地之后接着跑全自动后半程"),
                                   ("CompleteSession", "FinishAutotestRestoreSynchronously();", "CompleteSession 的同步还原兜底"),
                                   ("Update", "RecoverInterruptedAutotestIfNeeded();", "中途崩溃之后回基地按快照键恢复")):
            text = execution.body(method)
            if text is None or token not in text:
                errors.append("%s 的 %s 里缺少 %s（%s）" % (EXECUTION, method, token, why))

    # 4. 只读套件与演练不引用编排
    for rel in ISOLATED:
        source = parsed.get(rel)
        if source is None:
            errors.append("找不到只读 / 演练文件 " + rel)
            continue
        hits = sorted(set(m.group(0) for m in SYMBOL.finditer(source.clean)))
        if hits:
            errors.append("%s 引用了全自动验收的符号：%s（只读套件 read_only=true 的报告里不能混进会写存档的步骤；演练也不许借道）"
                          % (rel, ", ".join(hits)))

    # 5. 写入门
    for rel, names in GATED_ENTRIES.items():
        source = parsed.get(rel)
        if source is None:
            errors.append("找不到 " + rel)
            continue
        for declared in sorted(n for n in source.methods if n.startswith("DevAutotest")):
            if declared not in names and declared not in READ_ONLY_ENTRIES:
                errors.append("%s 新增了入口 %s：写入口登记进 GATED_ENTRIES（第一句过写入门），只读入口登记进 READ_ONLY_ENTRIES 并写理由"
                              % (rel, declared))
        for name in names:
            text = source.body(name)
            first = text[1:].lstrip() if text else ""
            if not first.startswith("if (!F3GameplayValidationRunner.AutotestWriteAllowed(out"):
                errors.append("%s 的 %s 第一句必须是 F3GameplayValidationRunner.AutotestWriteAllowed（专用测试档门）" % (rel, name))
    autotest = parsed.get(AUTOTEST)
    gate = autotest.body("AutotestWriteAllowed") if autotest else None
    ordered(errors, gate, ("ModBehaviour.DevModeEnabled", "IsDedicatedCurrentSlot()", "_autotestRecovering", "_autotest.Active",
                           "_slotChanged", "SnapshotPersisted", "Snapshotting"),
            "AutotestWriteAllowed 必须依次核 Dev 构建、专用测试档、崩溃恢复例外、这一轮正在跑、槽位没换、快照已落盘")
    for rel, names in MUST_GATE.items():
        source = parsed.get(rel)
        for name in names:
            text = source.body(name) if source else None
            if text is None or not any(token in text for token in GATE_TOKENS):
                errors.append("%s 的 %s 必须自己先过 AutotestWriteAllowed" % (rel, name))
    runner_files = [rel for rel in AUTOTEST_FILES if rel.startswith("DebugAndTools/F3GameplayValidationAutotest") and not rel.endswith("Judges.cs")]
    callers = {}
    for rel in runner_files:
        source = parsed.get(rel)
        if source is None:
            continue
        for name in VIA_CALLER:
            for hit in re.finditer(r"\b" + name + r"\s*\(", source.blank):
                owner = source.enclosing(hit.start())
                declaration = any(start <= hit.start() < body for start, body, _ in source.methods.get(name, ()))
                if not declaration:
                    callers.setdefault(name, set()).add(owner)
        for name, spans in source.methods.items():
            for _, start, end in spans:
                text = source.blank[start:end]
                calls = [call for call in PERSIST_CALLS if call in text]
                if not calls or any(token in text for token in GATE_TOKENS):
                    continue
                if name in VIA_ENTRY:
                    if [c for c in calls if c not in ("DevAutotestReplace(", "DevAutotestFlush(")]:
                        errors.append("%s 的 %s 登记为只经带门入口写，却直接调用了 %s" % (rel, name, ", ".join(calls)))
                    continue
                if name in VIA_CALLER:
                    continue
                errors.append("%s 的 %s 直接调用了 %s，却不先过 AutotestWriteAllowed（确属只经带门入口写或只被带门方法调用的，登记进 VIA_ENTRY / VIA_CALLER 写明理由）"
                              % (rel, name, ", ".join(calls)))
    for name, allowed in VIA_CALLER.items():
        for owner in sorted(c or "(方法体外)" for c in callers.get(name, ())):
            if owner not in allowed:
                errors.append("%s 只许被 %s 调用（它自己不过写入门），现在 %s 也在调" % (name, " / ".join(allowed), owner))

    # 6. 快照先于写入；还原两道
    story = parsed.get(STORY)
    ordered(errors, autotest.body("AutotestLegDepartAndReal") if autotest else None,
            ("TakeAutotestSnapshot(", "if (!snapshotOk) yield break;", "yield return AutotestDepart();", 'RunAutotestStages("landing")'),
            "快照必须先于第一次写入（出发、瞬移、剧情阶段），取不到快照就不出发")
    leg = autotest.body("RunSkyIslandAutotestLeg") if autotest else None
    ordered(errors, leg, ("DriveSessionPhase(AutotestLegDepartAndReal(), false)", "DriveSessionPhase(AutotestLegStoryStages(), false)",
                          "DriveSessionPhase(AutotestLegAltAndExtract(), false)"), "后半程三段按顺序跑")
    if leg is None or not re.search(r"_closingSession = true;\s*yield return DriveSessionPhase\(AutotestLegRestore\(\), true\);", leg):
        errors.append("还原段必须紧跟 _closingSession = true 以收尾阶段跑（取消打断不了还原）")
    if autotest and len(re.findall(r"\bDriveSessionPhase\s*\(", autotest.blank)) != 4:
        errors.append(AUTOTEST + " 里 DriveSessionPhase 只许四处（三段主阶段 + 一段还原）：多出来的阶段可能绕开还原")
    snapshot = story.body("TakeAutotestSnapshot") if story else None
    if snapshot is None or not re.search(r"_autotest\.Snapshotting = true;\s*try \{ return TakeAutotestSnapshotCore\(ref metrics, out reason\); \}\s*finally \{ _autotest\.Snapshotting = false; \}", snapshot):
        errors.append("TakeAutotestSnapshot 必须在 try/finally 里置位、复位 Snapshotting（写入门只为取快照那一次破例）")
    for name in ("AutotestLegRestore", "FinishAutotestRestoreSynchronously"):
        text = autotest.body(name) if autotest else None
        if text is None or "RestoreAutotestEnvironment(out" not in text:
            errors.append("%s 必须复位环境（RestoreAutotestEnvironment：语言、强制夜里、无敌、血量、时间流速）" % name)
    env = story.body("RestoreAutotestEnvironment") if story else None
    for token, why in (("RestoreAutotestLanguage(out", "语言"), ("SkyIslandNight.DevForceNight = snapshot.ForceNight", "强制夜里"),
                       (".SetInvincible(pair.Value)", "临时无敌"), (".SetHealth(pair.Value)", "临时压低的血量"),
                       ("Time.timeScale = target", "时间流速")):
        if env is None or token not in env:
            errors.append("RestoreAutotestEnvironment 没有复位%s（缺 %s）" % (why, token))
    language = story.body("RestoreAutotestLanguage") if story else None
    if language is None or "LocalizationManager.SetLanguage(target)" not in language:
        errors.append("RestoreAutotestLanguage 必须用官方 LocalizationManager.SetLanguage 把语言拨回快照值")
    ordered(errors, story.body("TryRecoverAutotestSnapshot") if story else None,
            ("IsDedicatedCurrentSlot()", "_autotestRecovering = true;", "RestoreAutotestStoryAtBase(", "LocalizationManager.SetLanguage(snapshot.Language)",
             "SkyIslandNight.DevForceNight = snapshot.ForceNight", "finally { _autotestRecovering = false; }"),
            "崩溃恢复只在专用测试档上、按快照写回剧情并复位语言与强制夜里，恢复标志在 finally 里复位")

    # 7. 截图
    capture = parsed.get(CAPTURE)
    ordered(errors, capture.body("CaptureAutotestShot") if capture else None,
            ("EnsureAutotestOverlaysHidden(out blocking)", "yield return new WaitForEndOfFrame();", "EnsureAutotestOverlaysHidden(out blocking)",
             "ScreenCapture.CaptureScreenshotAsTexture()"),
            "截图前（等帧末之前与之后各一次）确认调试浮层不在屏幕上，再在帧末取像素")
    if capture is None or '"F3DebugCheatMenu"' not in capture.clean:
        errors.append("截图前要确认不在屏幕上的浮层里必须有 F3 菜单（F3DebugCheatMenu）")
    for rel in AUTOTEST_FILES:
        source = parsed.get(rel)
        if source and ("ScreenCapture.CaptureScreenshot(" in source.blank or "SteamScreenshots" in source.blank):
            errors.append(rel + " 不许用写文件版 ScreenCapture.CaptureScreenshot 或 Steam 截图（进程内拿不到像素、文件不在结果目录）")
    return errors


def load():
    src = {}
    for rel in AUTOTEST_FILES + ISOLATED:
        path = ROOT / rel
        if path.is_file():
            src[rel] = path.read_bytes().decode("utf-8-sig")
    for path in list((ROOT / "DebugAndTools").rglob("*.cs")) + list(ROOT.glob("*.cs")):
        rel = path.relative_to(ROOT).as_posix()
        if rel in src:
            continue
        raw = path.read_bytes().decode("utf-8-sig", errors="replace")
        if "utotest" in raw or "SKY_AUTO_" in raw:
            src[rel] = raw
    src["__autotest_glob__"] = tuple(sorted(
        p.relative_to(ROOT).as_posix()
        for p in list((ROOT / "DebugAndTools").glob("F3GameplayValidationAutotest*.cs")) + list((ROOT / "DebugAndTools/SkyIsland").glob("*Autotest*.cs"))))
    src[BAT] = (ROOT / BAT).read_bytes().decode("utf-8-sig")
    return src


def sub_once(pattern, replacement):
    def apply(text):
        return re.sub(pattern, replacement, text, count=1, flags=re.S)
    return apply


PROBES = (
    (ACTIONS, sub_once(r"\A#if BOSSRUSH_DEV\r?\n", ""), "去掉整份 Dev 包裹"),
    (AUTOTEST, sub_once(r"\nnamespace BossRush", "\n#endif\nnamespace BossRush"), "最外层 #if 提前闭合"),
    (BAT, sub_once(r"echo\(DebugAndTools\\F3GameplayValidationAutotestAsserts\.cs\r\n", ""), "编译清单漏登记一个文件"),
    (BAT, sub_once(r"echo\(/reference:UnityEngine\.ScreenCaptureModule\.dll\r\n", ""), "编译清单缺 ScreenCaptureModule 引用"),
    (EXECUTION, sub_once(r"(\n[ \t]*if \(!_skyIslandMode\) yield return RunSkyIslandAutotestLeg\(\);)(\r?\n)#endif", r"\2#endif\1"), "钩子挪出 #if BOSSRUSH_DEV"),
    (EXECUTION, sub_once(r"FinishAutotestRestoreSynchronously\(\);", ""), "CompleteSession 去掉同步还原兜底"),
    ("DebugAndTools/F3GameplayValidationSkyIslandRuntimeCases.cs", sub_once(r"\Z", "\nclass AutotestProbe { string p = F3AutotestJudges.TableFile; }\n"), "只读套件引用编排"),
    ("DebugAndTools/SkyIsland/SkyIslandStoryServiceAutotest.cs", sub_once(r"if \(!F3GameplayValidationRunner\.AutotestWriteAllowed\(out error\)\) return false;", ""), "存档写入口去掉写入门"),
    ("DebugAndTools/SkyIsland/SkyIslandSessionAutotest.cs", sub_once(r"(\n[ \t]*internal bool DevAutotestReturnToBase)", r"\n        internal bool DevAutotestWarp(float x) { return true; }\1"), "新增未登记的写入口"),
    (AUTOTEST, sub_once(r"if \(!IsDedicatedCurrentSlot\(\)\) \{ reason = \"dedicated_test_slot_required\"; return false; \}", ""), "写入门不核专用测试档"),
    (AUTOTEST, sub_once(r"if \(!runner\._autotest\.SnapshotPersisted && !runner\._autotest\.Snapshotting\)[^\n]*\n", "\n"), "写入门不核快照已落盘"),
    (STORY, sub_once(r"(GiveAutotestItems\(int typeId, int count, out string reason\)\s*\{)\s*if \(!AutotestWriteAllowed\(out reason\)\) return false;", r"\1 reason = null;"), "发物品不过写入门"),
    (STORY, sub_once(r"string gate;\s*if \(!AutotestWriteAllowed\(out gate\)\) return \"not_allowed:\" \+ gate;", ""), "收回物品不过写入门"),
    (ACTIONS, sub_once(r"string gate;\s*if \(!AutotestWriteAllowed\(out gate\)\) return gate;", ""), "岛上步骤开跑前不再过写入门"),
    (AUTOTEST, sub_once(r"if \(!snapshotOk\) yield break;", ""), "取不到快照照样出发"),
    (AUTOTEST, sub_once(r"_closingSession = true;", ""), "还原段不以收尾阶段跑"),
    (STORY, sub_once(r"try \{ SkyIslandNight\.DevForceNight = snapshot\.ForceNight; \}", "try { }"), "还原不复位强制夜里"),
    (STORY, sub_once(r"if \(!IsDedicatedCurrentSlot\(\)\) \{ detail = \"snapshot_on_non_dedicated_slot\"; return false; \}", ""), "崩溃恢复不核专用测试档"),
    (CAPTURE, sub_once(r"(yield return new WaitForEndOfFrame\(\);\s*)if \(!EnsureAutotestOverlaysHidden\(out blocking\)\)", r"\1if (false)"), "帧末之后不再确认浮层已隐藏"),
    (CAPTURE, sub_once(r"ScreenCapture\.CaptureScreenshotAsTexture\(\)", "ScreenCapture.CaptureScreenshot(\"x.png\")"), "改用写文件版截图"),
)


def main():
    src = load()
    errors = check(src)
    probes_ok = 0
    for rel, mutate, label in PROBES:
        if rel not in src:
            errors.append("反向检查的目标文件不在：" + rel)
            continue
        mutated = dict(src)
        mutated[rel] = mutate(src[rel])
        if mutated[rel] == src[rel]:
            errors.append("反向检查没能改动源码（守卫的锚点过期了）：" + label)
            continue
        if not check(mutated):
            errors.append("反向检查失效，破坏之后仍然全绿：" + label)
        else:
            probes_ok += 1
    if errors:
        print("F3AutotestOrchestratorGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("F3AutotestOrchestratorGuard: PASS（%d 个文件整份 Dev / 编译清单与截图引用 / 钩子都在 Dev 区块 / 只读与演练隔离 / "
          "写入门与快照先行 / 两道还原 / 截图前隐藏浮层；%d 条反向检查全部转红）" % (len(AUTOTEST_FILES), probes_ok))
    return 0


if __name__ == "__main__":
    sys.exit(main())
