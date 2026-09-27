"""天空岛具名剧情对手（折翎战斗体、失控的守钟装置）的专属招式（owner 2026-09-27 批准）。

钉住：
1. 两个控制器都经 SkyIslandBossForge.BindChampionMoves 分派，遭遇 owner 在两条具名对手分支里都调了它；新文件已登记编译清单。
2. 逃圈判据：每一种预警圈的「半径 ÷ 预警」≤ SkyIslandBossRules.MaxEscapeSpeed（5.5 m/s，正常跑动可达）。
3. 预警时长一律过 TelegraphSeconds（静听耳罩）；只订自己身上的死亡事件并在 OnDestroy 退订；
   站定蓄力暂停 AI 之后，收尾与 OnDestroy 都会恢复；预警圈没画出来就不结算。
4. 手工驱动的子协程要透传 Current（根 AGENTS §4.7）。

行为与手感只能实机验；脚本末尾带内存反向检查。
"""
import re
import sys
from pathlib import Path

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent
SKY = ROOT / "DebugAndTools" / "SkyIsland"
MOVES = SKY / "SkyIslandChampionMoves.cs"
FORGE = SKY / "SkyIslandBossForge.cs"
ENCOUNTERS = SKY / "SkyIslandEncounters.cs"
RULES = SKY / "SkyIslandBossRules.cs"
BAT = ROOT / "compile_official.bat"


def squash(text):
    return re.sub(r"\s+", "", text or "")


def body_of(text, signature):
    at = text.find(signature) if text else -1
    if at < 0:
        return None
    start = text.find("{", at)
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    return None


def const(text, name):
    m = re.search(r"internal const float %s = ([0-9.]+)f;" % name, text)
    return float(m.group(1)) if m else None


def check(code):
    errors = []
    moves, forge, enc, rules, bat = code["moves"], code["forge"], code["enc"], code["rules"], code["bat"]
    if "echo(DebugAndTools\\SkyIsland\\SkyIslandChampionMoves.cs" not in bat:
        errors.append("compile_official.bat 没登记 SkyIslandChampionMoves.cs（不登记就不会编进 DLL，且不报错）")
    bind = body_of(forge, "internal static void BindChampionMoves(")
    for cid, cls in (("zheling", "SkyIslandZhelingMoves"), ("bellkeeper", "SkyIslandBellEngineMoves")):
        if bind is None or squash('case "%s": created.gameObject.AddComponent<%s>().Bind(created, context);' % (cid, cls)) not in squash(bind):
            errors.append("BindChampionMoves 要把 %s 分派给 %s" % (cid, cls))
        if squash('SkyIslandBossForge.BindChampionMoves(created, "%s", BossContext());' % cid) not in squash(enc):
            errors.append("遭遇 owner 的 %s 分支没挂专属招式" % cid)
    m = re.search(r"internal const float MaxEscapeSpeed = ([0-9.]+)f;", rules)
    limit = float(m.group(1)) if m else 5.5
    for radius, telegraph in (("CutRadius", "CutTelegraph"), ("TollRadius", "TollTelegraph"), ("EchoRadius", "EchoTelegraph")):
        r, t = const(moves, radius), const(moves, telegraph)
        if r is None or t is None or t <= 0:
            errors.append("缺常量 %s / %s" % (radius, telegraph))
        elif r / t > limit + 1e-6:
            errors.append("%s / %s = %.2f m/s，超过逃圈判据 %.1f" % (radius, telegraph, r / t, limit))
    for cls in ("SkyIslandZhelingMoves", "SkyIslandBellEngineMoves"):
        start = moves.find("internal sealed class " + cls)
        nxt = moves.find("internal sealed class ", start + 10)
        part = moves[start:nxt if nxt > 0 else len(moves)] if start >= 0 else ""
        destroy = body_of(part, "private void OnDestroy()")
        for token, message in (
            ("health.OnDeadEvent.AddListener(OnDead);", "要订自己的死亡事件"),
            ("SkyIslandBossRules.TelegraphSeconds(", "预警时长要过 TelegraphSeconds"),
            ("SkyIslandBossForge.CreateGroundRing(context.Root,", "预警圈走共用件"),
            ("SkyIslandBossForge.Detonate(boss,", "结算走共用件（buff 通道、不伤自己、自带表现）"),
        ):
            if squash(token) not in squash(part):
                errors.append(cls + " " + message)
        if destroy is None or squash("health.OnDeadEvent.RemoveListener(OnDead);") not in squash(destroy) or squash("ResumeAi();") not in squash(destroy):
            errors.append(cls + " 的 OnDestroy 要退订死亡事件并恢复 AI")
        if squash("aiControl.Pause();") in squash(part) and squash(part).count(squash("ResumeAi();")) < 2:
            errors.append(cls + " 暂停 AI 之后收尾与销毁都要恢复")
    zheling = body_of(moves, "private IEnumerator CutRoutine(Vector3 direction)")
    if zheling is None or squash("points.Add(ground);lines.Add(line);") not in squash(zheling):
        errors.append("折翎只结算成功画出预警圈的刀点")
    toll = body_of(moves, "private IEnumerator Toll(")
    if toll is None or squash("if (line == null) yield break;") not in squash(toll):
        errors.append("钟鸣圈没画出来就不能敲")
    routine = body_of(moves, "private IEnumerator TollRoutine()")
    if routine is None or squash("while (first.MoveNext()) yield return first.Current;") not in squash(routine) \
            or squash("while (echo.MoveNext()) yield return echo.Current;") not in squash(routine):
        errors.append("手工驱动子协程要透传 Current")
    return errors


def load():
    return {
        "moves": clean_source(MOVES.read_text(encoding="utf-8")),
        "forge": clean_source(FORGE.read_text(encoding="utf-8")),
        "enc": clean_source(ENCOUNTERS.read_text(encoding="utf-8")),
        "rules": clean_source(RULES.read_text(encoding="utf-8")),
        "bat": BAT.read_text(encoding="utf-8", errors="replace"),
    }


def reverse_checks(code):
    probes = [
        ("bat", "echo(DebugAndTools\\SkyIsland\\SkyIslandChampionMoves.cs", "rem removed"),
        ("enc", 'SkyIslandBossForge.BindChampionMoves(created, "bellkeeper", BossContext());', ""),
        ("moves", "internal const float EchoTelegraph = 1.6f;", "internal const float EchoTelegraph = 1.2f;"),
        ("moves", "if (line == null) yield break;", ""),
        ("moves", "while (echo.MoveNext()) yield return echo.Current;", "while (echo.MoveNext()) yield return null;"),
    ]
    failures = []
    for key, old, new in probes:
        if code[key].count(old) != 1:
            failures.append("反向检查锚点不唯一：%s %s" % (key, old))
            continue
        broken = dict(code)
        broken[key] = code[key].replace(old, new)
        if not check(broken):
            failures.append("反向检查没有转红：%s %s" % (key, old))
    return failures


def main():
    code = load()
    errors = check(code) or reverse_checks(code)
    if errors:
        for e in errors:
            print("FAIL:", e)
        sys.exit(1)
    print("PASS: SkyIslandChampionMovesGuard（分派、编译清单、逃圈判据、生命周期；5 个反向探针均转红）")


if __name__ == "__main__":
    main()
