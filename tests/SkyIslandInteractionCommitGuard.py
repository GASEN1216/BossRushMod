"""天空岛交互提交的 L1 接线：一击一页、服务刷新、奖励先送达、停局先收声。

执行行为由 SkyIslandInteraction 逐字抽取生产方法跑 C# 验证；此守卫只钉调用链，
不把 UI 控件替身或字符串检查当作 Unity 实机证据。反向检查覆盖每个接线门。
"""
from pathlib import Path
import re
import sys
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
SKY = "DebugAndTools/SkyIsland/"
PANEL = SKY + "SkyIslandStoryPresentation.cs"
WORLD = SKY + "SkyIslandWorldStory.cs"
SERVICES = SKY + "SkyIslandServices.cs"
SESSION = SKY + "SkyIslandSession.cs"
GNATS = SKY + "SkyIslandGnats.cs"
RING = SKY + "SkyIslandGroundRing.cs"
PATHS = (PANEL, WORLD, SERVICES, SESSION, GNATS, RING)


def body(source, signature):
    hits = list(re.finditer(r"^([ \t]*)" + re.escape(signature), source, re.M))
    if len(hits) != 1:
        return ""
    hit = hits[0]
    opening = source.find("{", hit.end())
    end = re.search(r"^" + re.escape(hit.group(1)) + r"\}", source[opening:], re.M)
    return " ".join(source[opening:opening + end.end()].split()) if end else ""


def check(sources):
    errors = []
    src = {p: clean_source(sources[p]) for p in PATHS}

    def need(code, token, why):
        if token not in code:
            errors.append(why + " (missing " + token + ")")

    show = body(src[PANEL], "internal void Show(string title, string text, IList<Choice> choices,")
    need(show, "if (selecting) { pendingPage = new PendingPage", "按钮回调内只记录待提交页面")
    if not 0 <= show.find("if (selecting)") < show.find("CreateCanvasRoot("):
        errors.append("提交门必须在建立画布之前")
    click = body(src[PANEL], "private void BuildChoice(")
    need(click, "button.onClick.AddListener(delegate { RunChoice(select); });", "真实按钮必须经单次提交入口")
    run = body(src[PANEL], "private void RunChoice(")
    for token in ("if (!Visible || selecting || select == null) return;", "reply = select();", "page = pendingPage;",
                  "finally { selecting = false; pendingPage = null; }", "if (!Visible) return;",
                  "Show(page.Title, reply ?? page.Text, page.Choices, page.Portrait, page.Banner);",
                  "if (changedBody) RestoreBottom(anchored, bottom);"):
        need(run, token, "点击提交应有重入、关闭与收尾边界")
    need(body(src[PANEL], "public void Dispose()"), "pendingPage = null;", "关闭必须取消待提交页面")

    refreshed = body(src[WORLD], "private string Refreshed(")
    need(refreshed, "hiddenHints.Clear(); reopen(); return WithNextStep(message);", "回执须保留新页面下一步而非旧提示")
    need(body(src[WORLD], "private void ServiceChoice("), "return Refreshed(true, action());", "服务操作后按相同判据重取按钮")
    need(body(src[WORLD], "private void PuzzleChoices("), 'return Refreshed(true, feedback + "\\n\\n" + message);',
         "解题已完成但记录失败也应刷新为收录重试页")
    read_point = body(src[WORLD], "internal void ReadPoint(")
    need(read_point, "!puzzles.IsSolved(puzzle)", "已解开谜题不再展示解题按钮")
    need(read_point, "else RecordChoice(choices, key, recorded);", "解开后复用收录重试入口")

    heal = body(src[SERVICES], "private SkyIslandServiceReadiness EvaluateHeal(")
    if not 0 <= heal.find("return SkyIslandServiceReadiness.NothingToDo;") < heal.find("if (Time.time < healReadyAt)"):
        errors.append("苔药要先判无伤口，再判有需求时的冷却")
    drop = body(src[SERVICES], "internal bool DropBountyReward(")
    if not re.search(r"if \(!SkyIslandRewardCrate\.TryFindCratePosition\([^;]+out drop\)\) return false;", drop):
        errors.append("奖励落点失败必须保留委托，不得退回未经检查的交互锚点")

    hide = body(src[WORLD], "internal void Hide()")
    need(hide, "if (!session.IsReady && fieldcraft != null && fieldcraft.Gnats != null) fieldcraft.Gnats.StopBuzz();",
         "停局在发声体仍存活时收声，普通关页不误停")
    need(body(src[GNATS], "internal void StopBuzz()"), "if (!buzzing) return;", "停声必须幂等")
    for signature, invalid in (("private void OnStartedLoading(", "ready = false;"),
                               ("private void OnPlayerDied(", "ready = false;"),
                               ("internal void Close(", "returnRequested = returning = true;")):
        code = body(src[SESSION], signature)
        if not 0 <= code.find(invalid) < code.find("worldStory.Hide();"):
            errors.append("停声调用时序缺口：" + signature)
    valid = body(src[SESSION], "private bool IsSessionValid()")
    for token in ("!closed", "ready", "!returning", "!deathPending"):
        need(valid, token, "IsReady 必须反映停局事实")

    shape = body(src[RING], "internal static void SetShape(")
    need(shape, "if (line.positionCount != Segments || line.GetPosition(0).x != radius)", "圆环几何只随真实半径或拓扑变化")
    if not 0 <= shape.find("line.GetPosition(0).x != radius") < shape.find("line.SetPosition("):
        errors.append("圆环重复写顶点必须先被挡住")
    return errors


PROBES = (
    (PANEL, "if (selecting)", "if (false)", "page_defer"),
    (PANEL, "RunChoice(select);", "select();", "click_owner"),
    (PANEL, "selecting = false;", "selecting = true;", "dispatch_finally"),
    (WORLD, "return Refreshed(true, action());", "return action();", "service_refresh"),
    (WORLD, "return WithNextStep(message);", "return message;", "next_step"),
    (WORLD, 'return Refreshed(true, feedback + "\\n\\n" + message);',
     'return Refreshed(recordedNow, feedback + "\\n\\n" + message);', "puzzle_retry"),
    (SERVICES, "out drop))\n                return false;", "out drop))\n                drop = position;", "claim_placement"),
    (WORLD, "fieldcraft.Gnats.StopBuzz();", "fieldcraft.Gnats.ToString();", "early_audio_stop"),
    (SESSION, "returnRequested = returning = true;", "returnRequested = true;", "return_invalidates"),
    (RING, "line.GetPosition(0).x != radius", "true", "geometry_gate"),
)


def main():
    sources = {p: (ROOT / p).read_text(encoding="utf-8-sig") for p in PATHS}
    errors = check(sources)
    if not errors:
        for path, old, new, name in PROBES:
            if sources[path].count(old) != 1:
                errors.append("反向检查锚点不唯一：" + name)
                continue
            mutant = dict(sources)
            mutant[path] = mutant[path].replace(old, new, 1)
            if not check(mutant):
                errors.append("反向检查未转红：" + name)
    runner = (ROOT / "tools/run_runtime_regressions.py").read_text(encoding="utf-8")
    if '"SkyIslandInteraction"' not in runner:
        errors.append("交互执行回归未登记聚合入口")
    if errors:
        for error in errors:
            print("FAIL " + error)
        return 1
    print("PASS SkyIslandInteractionCommitGuard (10 reverse checks; behavior covered by SkyIslandInteraction)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
