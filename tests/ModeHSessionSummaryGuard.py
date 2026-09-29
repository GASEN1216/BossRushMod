"""
ModeHSessionSummaryGuard — 鸭王杯「本场总结」的接线守卫（2026-09-29 owner）。

owner 要求：Mode H 结束后、回到基地前弹出一张展示本场收获 / 失去的总结（押钱输赢、押物品得失、赛季奖励、
得到 / 失去的物品），以实际结算数据为准；有明确的继续按钮，关掉后再返回基地；不能挡住官方返回流程或卡住。

本守卫钉住：
  - 关停时先组装总结（赛季状态还在）、再关停，记账随后清空；
  - 回基地的两条路（赛季结束 / 观战退出）都经 ModeHSessionSummary.Show(…, owner.SafeExitFromModeH)，
    没弹出来就照旧直接 SafeExitFromModeH（不挡离场）；
  - 总结页只有一颗主按钮，ESC 等于点它；按钮先收页面再离场；场景被别的途径切走时收起且不再离场；
  - F3 自动验收在跑、这一趟什么也没发生时不弹；
  - 记账来源：结算 / 技术中止 / 名人堂（状态投影）、锁盘前与原样退回时抄押注账本；读档对账不进总结；
  - Mod 销毁时收页面、退订场景事件。
只证明接线与顺序，不证明观感：页面排版、按钮手感与实际离场要实机看。
每条断言都有内置变异探针，探针必须让 check() 转红，否则本守卫自己判失败。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from cs_source_util import clean_source  # noqa: E402
from ModeHOneClickFlowGuard import method_body  # noqa: E402

FILES = {
    "module": "ModeH/ModeHRuntimeModule.cs",
    "ui_flow": "ModeH/ModeHRuntimeModule_UiFlow.cs",
    "bet": "ModeH/ModeHRuntimeModule_BetFlow.cs",
    "summary": "ModeH/ModeHSessionSummary.cs",
}


def read(rel):
    with open(os.path.join(REPO_ROOT, rel), "r", encoding="utf-8-sig") as handle:
        return handle.read().replace("\r\n", "\n")


def squeeze(text):
    return re.sub(r"\s+", " ", text or "")


def check(sources):
    errors = []
    src = dict((key, clean_source(value)) for key, value in sources.items())

    def body(key, signature):
        text = method_body(src[key], signature)
        if text is None:
            errors.append("[%s] 找不到唯一的方法 %s" % (FILES[key], signature))
            return ""
        return squeeze(text)

    def need(text, token, why):
        if token not in text:
            errors.append(why + "（缺少 %r）" % token)

    def ordered(text, tokens, why):
        positions = [text.find(t) for t in tokens]
        if -1 in positions or positions != sorted(positions):
            errors.append(why + "（顺序 %r）" % (positions,))

    # ---- 关停：先组装、清账、再关停；回基地两条路都先弹总结，弹不出来照旧离场 ----
    shutdown = body("module", "internal void ShutdownRuntime(ModeHExitReason reason, string reasonId)")
    ordered(shutdown, ["sessionSummary = BuildSessionSummaryContent(reason, reasonId);",
                       "ModeHSessionSummary.Discard();", "ShutdownRuntimeInternal(reason, reasonId);",
                       "ModeHSessionSummary.Show(sessionSummary,"],
            "总结必须在关停清掉赛季状态之前组装，关停之后再显示")
    need(shutdown, "owner.SafeExitFromModeH)) owner.SafeExitFromModeH();",
         "总结页的按钮回调是 SafeExitFromModeH；没弹出来必须照旧直接离场")
    if shutdown.count("SafeExitFromModeH()") != 1:
        errors.append("回基地只能在「总结没弹出来」时直接调用一次 SafeExitFromModeH（弹出来的由按钮回调离场）")
    need(body("module", "internal static void ResetModeHStaticCaches()"), "ModeHSessionSummary.ResetStaticCaches();",
         "Mod 销毁必须收起总结页并退订场景事件")
    for signature in ("public override void OnAwake(ModBehaviour owner)", "private void RestoreForSlotChange()"):
        ordered(body("module", signature), ["ReconcileCashBetOnRestore();", "ModeHSessionSummary.Discard();"],
                "读档 / 换槽时的押注对账不属于任何一趟，不能进下一次总结（%s）" % signature)

    # ---- 组装：F3 自动验收与空记账不弹 ----
    build = body("ui_flow", "private ModeHPageContent BuildSessionSummaryContent(ModeHExitReason reason, string reasonId)")
    need(build, "if (F3GameplayValidationRunner.IsRunning) return null;", "F3 自动验收在跑时不弹模态总结（会挡住后续用例）")
    need(build, "if (!ModeHSessionSummary.HasFacts && openMatch < 0) return null;", "这一趟什么都没发生时不弹")
    need(build, "DescribeSummaryBet(page, ModeHSessionSummary.FindBet(key)", "每场的押注结果取自抄下的账本快照")
    need(build, "record.status == ModeHCashBetService.StatusRefunded", "原样退回单列")
    need(build, "operation.selectedRewardKitId", "赛季奖励（解锁的整备）取自奖励 operation")

    observe = body("ui_flow", "private void ObserveLifecycleForSummary(ModeHLifecycle lifecycle)")
    need(observe, "ModeHSessionSummary.NoteSettled(_runState.RunId, _runState.MatchIndex);", "结算相位记下出了结算的场次")
    need(observe, "ModeHSessionSummary.NoteInterrupted(_runState.RunId, _runState.MatchIndex);", "技术中止记下被打断的场次")
    need(observe, "ModeHSessionSummary.NoteHallOfFame();", "名人堂记进总结")
    route = body("ui_flow", "private void RouteUiForLifecycle(ModeHLifecycle lifecycle)")
    ordered(route, ["ObserveLifecycleForSummary(lifecycle);", "if (_deferPageRoutes && IsPageLifecycle(lifecycle)) return;"],
            "记账必须在自动流程早退之前（自动链里的结算相位不建页，但结算事实照样要记）")
    ordered(body("bet", "private void ReserveStandingCashBet()"),
            ["ModeHSessionSummary.NoteBet(ModeHCashBetService.Current);", "ModeHCashBetRecord carried = CarriedBetForCurrentMatch();"],
            "账本只留最新一条：新一场押注之前先把上一场的结果抄进总结")
    ordered(body("bet", "private void RefundCashBet(string context)"),
            ["ModeHCashBetService.TryRefund(context, out refunded)", "ModeHSessionSummary.NoteBet(ModeHCashBetService.Current);"],
            "原样退回之后抄一份账本进总结")

    # ---- 页面：一颗主按钮、ESC 等于它、先收页再离场、切场景不二次离场 ----
    show = body("summary", "internal static bool Show(ModeHPageContent content, string buttonLabel, Action onClosed)")
    ordered(show, ["content.Actions.Clear();", "IsPrimary = true,", "IsCancel = true,", "OnClick = Finish,",
                   "_ui.OpenPage(ModeHPage.Settlement,", "SceneManager.activeSceneChanged += OnActiveSceneChanged;"],
            "总结页只有一颗主按钮（ESC 等于它），版式复用结算页，并监听场景切换")
    need(show, "return false;", "建页失败必须返回 false，让调用方照旧离场")
    ordered(body("summary", "private static void Finish()"), ["_onClosed = null;", "Close();", "callback();"],
            "按钮：回调只执行一次，先收页面（释放输入租约）再离场")
    scene = body("summary", "private static void OnActiveSceneChanged(Scene from, Scene to)")
    ordered(scene, ["_onClosed = null;", "Close();"], "场景被别的途径切走：收起页面且不再离场")
    need(body("summary", "private static void Close()"), "SceneManager.activeSceneChanged -= OnActiveSceneChanged;",
         "收页面时退订场景事件")
    need(body("summary", "internal static void ResetStaticCaches()"), "SceneManager.activeSceneChanged -= OnActiveSceneChanged;",
         "销毁路径退订场景事件")
    return errors


PROBES = [
    ("module", "            ModeHSessionSummary.Discard();\n\n            ShutdownRuntimeInternal(reason, reasonId);",
     "            ShutdownRuntimeInternal(reason, reasonId);\n            ModeHSessionSummary.Discard();"),
    ("module", "owner.SafeExitFromModeH))\n                        owner.SafeExitFromModeH();",
     "null))\n                        owner.SafeExitFromModeH();"),
    ("module", "            ModeHSessionSummary.ResetStaticCaches();\n", ""),
    ("ui_flow", "            if (F3GameplayValidationRunner.IsRunning) return null;\n", ""),
    ("ui_flow", "            ObserveLifecycleForSummary(lifecycle);\n", ""),
    ("ui_flow", "                        ModeHSessionSummary.NoteInterrupted(_runState.RunId, _runState.MatchIndex);\n", ""),
    ("bet", "            ModeHSessionSummary.NoteBet(ModeHCashBetService.Current); // 本场总结的「原样退回」\n", ""),
    ("summary", "                    IsCancel = true, // ESC = 返回基地 / 关闭：这一页只有这一个去处\n", ""),
    ("summary", "            _onClosed = null;\n            Close();\n        }\n\n        private static void Close()",
     "            Close();\n        }\n\n        private static void Close()"),
]


def main():
    sources = dict((key, read(rel)) for key, rel in FILES.items())
    errors = check(sources)
    for key, before, after in PROBES:
        if sources[key].count(before) != 1:
            errors.append("[probe] 变异锚点不唯一或失效: %s: %r" % (FILES[key], before[:70]))
            continue
        mutated = dict(sources)
        mutated[key] = sources[key].replace(before, after, 1)
        if not check(mutated):
            errors.append("[probe] 变异没被抓住: %s: %r" % (FILES[key], before[:70]))
    if errors:
        print("ModeHSessionSummaryGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("ModeHSessionSummaryGuard: PASS (%d probes)" % len(PROBES))
    return 0


if __name__ == "__main__":
    sys.exit(main())
