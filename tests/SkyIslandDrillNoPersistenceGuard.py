# -*- coding: utf-8 -*-
"""天空岛 Dev 演练套件：只在 Dev 构建、不写存档、改过的东西一定还原（2026-09-14）。

演练（`DebugAndTools/F3GameplayValidationSkyIslandDrill.cs`、`DebugAndTools/SkyIsland/SkyIslandGnatsDrill.cs`）
为了看到「只读验收看不到的判据」会动这趟出击：强制夜里、刷云蚋、打死一只、把主角血量压到叮咬下限附近、弹官方对话。
它能留在仓库里，靠的是下面这些约束，每一条都容易在后续改动里悄悄丢掉：

1. **只在 Dev 构建里存在**：两份文件整份包在 `#if BOSSRUSH_DEV` 里；F3 面板上的按钮与它的处理函数也在 `#if` 里。
2. **不写存档、不收录**：不调任何写存档、收录见闻 / 来信、记清场、发物品、点灯、合成、捧放蛙卵、动委托、
   改官方图鉴的入口；击杀只走官方伤害路径，不直接给委托记账。
3. **沿用专用测试档门**：入口先过 `CheckSkyIslandStartGate`（Dev + 专用测试档 + 人在岛上 + 没开模态），再置演练标志、再开会话。
4. **报告头如实写 read_only=false**，演练标志只在会话唯一收尾路径 `CompleteSession` 复位。
5. **改过的东西在 finally 里还原**：强制夜里复位到开跑前的值、压低的血量还回去、静态受伤事件退订。

守卫钉结构，证明不了演练跑起来是对的——那要在 Dev 构建里按一次演练按钮读报告（本轮没有，见报告第一节）。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source  # noqa: E402

DRILL = "DebugAndTools/F3GameplayValidationSkyIslandDrill.cs"
GNATS_DRILL = "DebugAndTools/SkyIsland/SkyIslandGnatsDrill.cs"
RUNNER = "DebugAndTools/F3GameplayValidationRunner.cs"
EXECUTION = "DebugAndTools/F3GameplayValidationExecution.cs"
BAT = "compile_official.bat"
BACKSLASH = chr(92)

# 演练里不许出现的写入口（清洗注释后按正则找）。每一条都写明为什么。
FORBIDDEN = (
    (r"\bSavesSystem\.(?:Save|SaveFile|SaveGlobal|SetFile|DeleteCurrentSave|RestoreIndexedBackup|CollectSaveData)\b", "直接写档或换槽"),
    (r"\.(?:RecordSearch|RecordNote|RemoveNote|RecordEncounterCleared|RecordRegionVisited|RequireAssetSnapshot)\(", "写群岛记录"),
    (r"(?<!SkyIslandStoryRules)\.TryApply\(", "推进剧情旗标（只有纯函数 SkyIslandStoryRules.TryApply 允许）"),
    (r"\bSkyIslandNoteBridge\.", "改官方图鉴镜像"),
    (r"\bNoteIndex\.SetNote", "写官方图鉴"),
    (r"\b(?:TryGive|GrantKeepsakes)\(", "发物品"),
    (r"\b(?:LightLamp|Craft|Harvest|TakeSpawn|ReleaseSpawn|DeployZapper|SwingFan|UseConsumable)\(", "改岛上进度或扣材料"),
    (r"\b(?:ReportGnatCulled|ReportEncounterCleared|ReportScavenged|ReportRegionVisited|TryAccept|TryClaim|TryAbandon)\(",
     "直接给委托记账或动委托（击杀计数必须由生产的 Remove(killed: true) 自己记）"),
    (r"\.Store\(", "落剧情存档"),
    (r"\b(?:BossRushSaveFileThrottle|WriteRunMarker|ProtectCurrentPlayer|ValidationSafeCleanup)\b", "写运行标记、接管无敌或跑主套件清理"),
    (r"\bLoadScene(?:Async)?\(|\bReturnToBase\(", "切图或返航"),
    (r"\bRuntimeStatModifierTracker\.", "给主角挂或摘增益"),
)


def directive_frames(raw):
    """逐行给出 (行号, 这一行是否处在 #if BOSSRUSH_DEV 的真分支里)。"""
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
    drill_raw, gnats_raw = raw.get(DRILL), raw.get(GNATS_DRILL)
    if drill_raw is None or gnats_raw is None:
        return ["找不到演练文件"]
    drill, gnats = clean_source(drill_raw), clean_source(gnats_raw)

    # ---- 1. 只在 Dev 构建里存在 ----
    for rel, text in ((DRILL, drill_raw), (GNATS_DRILL, gnats_raw)):
        if not wrapped_whole_file(text):
            errors.append(rel + " 没有整份包在 #if BOSSRUSH_DEV … #endif 里：正式构建里会出现会改状态的演练入口")
        if rel.replace("/", BACKSLASH) not in raw[BAT]:
            errors.append("编译清单缺少 " + rel + "（无通配符，漏了不报错，Dev 构建里演练按钮点了会编不过）")
    for number, code, in_dev in directive_frames(raw[RUNNER]):
        if ("StartSkyIslandDrillFromF3" in code or "TryStartSkyIslandDrill" in code) and not in_dev:
            errors.append(RUNNER + ":%d 在 #if BOSSRUSH_DEV 之外引用了演练入口（正式构建的 F3 面板不许有这个按钮）" % number)
    if "StartSkyIslandDrillFromF3" not in raw[RUNNER]:
        errors.append("F3 面板上没有演练按钮（独立入口）")

    # ---- 2. 不写存档、不收录 ----
    for rel, code in ((DRILL, drill), (GNATS_DRILL, gnats)):
        for pattern, why in FORBIDDEN:
            if re.search(pattern, code):
                errors.append(rel + " 不得" + why + "：/" + pattern + "/")

    # ---- 3. 专用测试档门在前，演练标志在后，最后才开会话 ----
    start = body_of(drill, "internal static bool TryStartSkyIslandDrill(ModBehaviour host, out string reason)")
    if start is None:
        errors.append("找不到演练入口 TryStartSkyIslandDrill")
    else:
        gate = start.find("if (!_instance.CheckSkyIslandStartGate(out reason)) return false;")
        flag = start.find("_instance._skyIslandDrill = true;")
        mode = start.find("_instance._skyIslandMode = true;")
        begin = start.find("_instance.BeginSession(out reason)")
        if not (0 <= gate < flag < begin and 0 <= gate < mode < begin):
            errors.append("演练入口必须先过岛内启动门（Dev + 专用测试档 + 在岛上 + 没开模态），再置岛内模式与演练标志，最后开会话")
    if len(re.findall(r"\b_skyIslandDrill\s*=\s*true\s*;", drill + raw[EXECUTION] + raw[RUNNER])) != 1:
        errors.append("_skyIslandDrill 只许在演练入口置位一次")

    # ---- 4. 报告头如实写 read_only=false；复位只在 CompleteSession ----
    execution = clean_source(raw[EXECUTION])
    if not re.search(r'_skyIslandDrill\s*\?\s*"SESSION \| slot=" \+ _sessionSlot \+ " \| assisted=false \| read_only=false \| drill=true',
                     execution):
        errors.append("会话报告头没有按 _skyIslandDrill 如实写 read_only=false / drill=true")
    complete = body_of(execution, "private void CompleteSession()") or ""
    if "_skyIslandDrill = false;" not in complete or len(re.findall(r"\b_skyIslandDrill\s*=\s*false\s*;", execution)) != 1:
        errors.append("演练标志必须且只能在会话唯一收尾路径 CompleteSession 复位")

    # ---- 5. 改过的东西在 finally 里还原 ----
    gnat_case = body_of(drill, "private IEnumerator RunSkyIslandDrillGnats()") or ""
    capture = gnat_case.find("bool previousForceNight = SkyIslandNight.DevForceNight;")
    force = gnat_case.find("SkyIslandNight.DevForceNight = true;")
    try_at = gnat_case.rfind("try", 0, force) if force >= 0 else -1
    finally_body = body_of(gnat_case, "finally", force) if force >= 0 else None
    if capture < 0 or force < 0 or try_at < capture or finally_body is None:
        errors.append("强制夜里必须先记下开跑前的值、再在 try 里打开")
    else:
        if "SkyIslandNight.DevForceNight = previousForceNight;" not in finally_body:
            errors.append("强制夜里没有在 finally 里复位到开跑前的值：演练中途异常或取消会把整趟变成永夜")
        if "Health.OnHurt -= onHurt;" not in finally_body or "Health.OnHurt += onHurt;" not in gnat_case:
            errors.append("演练订阅的官方静态受伤事件没有在 finally 里退订：静态事件会一直挂着这个闭包")
        if not re.search(r"if \(healthLowered[^)]*\)\s*health\.SetHealth\(", finally_body):
            errors.append("压低的主角血量没有在 finally 里还回去")
    if len(re.findall(r"\bDevForceNight\s*=(?!=)", drill)) != 2:
        errors.append("演练里只许有「打开」与「finally 复位」两处写强制夜里")
    dialogue = body_of(drill, "private IEnumerator RunSkyIslandDrillDialogue()") or ""
    dialogue_finally = body_of(dialogue, "finally") if dialogue else None
    if dialogue_finally is None or "first.Cancel();" not in dialogue_finally or "second.Cancel();" not in dialogue_finally:
        errors.append("演练弹出的官方对话没有在 finally 里取消：中途异常会留下一个关不掉的对话框")
    return errors


def main():
    raw = {}
    for rel in (DRILL, GNATS_DRILL, RUNNER, EXECUTION, BAT):
        path = ROOT / rel
        raw[rel] = path.read_text(encoding="utf-8-sig", errors="ignore") if path.is_file() else None
    if any(value is None for value in raw.values()):
        print("SkyIslandDrillNoPersistenceGuard: FAIL - 缺文件：" + ", ".join(k for k, v in raw.items() if v is None))
        return 1
    errors = check(raw)

    probes = (
        (DRILL, "#if BOSSRUSH_DEV\n", "", "拆掉整份文件的 #if"),
        (DRILL, "SkyIslandNight.DevForceNight = previousForceNight;", "", "finally 不复位强制夜里"),
        (DRILL, "Health.OnHurt -= onHurt;", "", "finally 不退订受伤事件"),
        (DRILL, "if (!_instance.CheckSkyIslandStartGate(out reason)) return false;", "", "绕过专用测试档门"),
        (DRILL, "int counterBefore = session.Bounty.Counter(SkyIslandBountyKind.Gnats);",
         "int counterBefore = session.Bounty.Counter(SkyIslandBountyKind.Gnats); session.Bounty.ReportGnatCulled();",
         "直接给委托记账"),
        (DRILL, "Record(\"SKY_DRILL_GNAT_SWARM\", \"FAIL\", 0L,", "SavesSystem.SaveFile(false); Record(\"SKY_DRILL_GNAT_SWARM\", \"FAIL\", 0L,",
         "写存档"),
        (EXECUTION, "read_only=false | drill=true", "read_only=true | drill=true", "报告头谎报只读"),
        (RUNNER, "#if BOSSRUSH_DEV\n            // 演练", "            // 演练", "按钮挪出 #if"),
        (BAT, "echo(DebugAndTools" + BACKSLASH + "F3GameplayValidationSkyIslandDrill.cs", "", "编译清单漏登记"),
    )
    for rel, before, after, label in probes:
        text = raw[rel].replace("\r\n", "\n")
        if before not in text:
            errors.append("反向检查锚点失效（%s）：%s" % (label, before[:60]))
            continue
        altered = dict(raw)
        altered[rel] = text.replace(before, after, 1)
        if not check(altered):
            errors.append("反向检查失效：%s 之后守卫仍然全绿" % label)

    if errors:
        print("SkyIslandDrillNoPersistenceGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("SkyIslandDrillNoPersistenceGuard: PASS（整份 #if BOSSRUSH_DEV / 不写存档不收录 / 专用测试档门 / "
          "read_only=false 报告头 / finally 还原强制夜里、血量、事件与对话；%d 个反向检查）" % len(probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
