# -*- coding: utf-8 -*-
"""F3 验收替玩家点「点击继续」（2026-09-15）。

官方 SceneLoader.LoadBaseScene 恒传 clickToConinue=true（<LoadBaseScene>d__47 IL 实查）：基地读完后停在「点击继续」，
等点击的循环没有超时。第二轮全自动验收里 Mode F 撤离由玩法代码发起返基地，runner 只等不点，
游戏停在加载屏十分钟，后面 5 个过图用例与天空岛整段连带作废。

守的是：
1. runner 的 Update 在验收运行分支里、换槽早返之后每帧调 FeedSceneContinueClickWhenWaiting()，不管加载是谁发起的；
2. 只在官方点击接收器 pointerClickEventRecevier 激活（activeSelf）时喂 NotifyPointerClick，取不到字段时退回「加载中就喂」；
3. LoadScene 外壳不再按自己的 clickToContinue 参数决定喂不喂（返基地那条传的是 false，恰恰最需要喂），
   clicks_fed 按全局计数在本次加载前后的差值算；
4. 天空岛出发等待段不再另喂一份（一个 owner）；
5. 过图诊断带上 SceneLoader.LoadingComment，下次卡住直接看到官方停在哪个等待点。

反向检查在内存里逐条破坏，必须转红。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source  # noqa: E402

SCENES = "DebugAndTools/F3GameplayValidationScenes.cs"
EXECUTION = "DebugAndTools/F3GameplayValidationExecution.cs"
AUTOTEST = "DebugAndTools/F3GameplayValidationAutotest.cs"


def method_body(code, signature):
    start = code.find(signature)
    if start < 0:
        return None
    brace = code.find("{", start)
    if brace < 0:
        return None
    depth = 0
    for i in range(brace, len(code)):
        if code[i] == "{":
            depth += 1
        elif code[i] == "}":
            depth -= 1
            if depth == 0:
                return code[brace:i + 1]
    return None


def check(sources):
    errors = []
    scenes = sources[SCENES]
    execution = sources[EXECUTION]
    autotest = sources[AUTOTEST]

    update = method_body(execution, "private void Update()")
    if update is None:
        errors.append("找不到 runner 的 Update")
    else:
        running = update.find("if (_running)")
        slot_return = update.find("if (_slotChanged) return;")
        feed = update.find("FeedSceneContinueClickWhenWaiting();")
        not_running = update.find("else if (!_recoveryChecked")
        if not (0 <= running < slot_return < feed < not_running):
            errors.append("Update 必须在验收运行分支里、换槽早返之后每帧调 FeedSceneContinueClickWhenWaiting()")

    feeder = method_body(scenes, "private void FeedSceneContinueClickWhenWaiting()")
    if feeder is None:
        errors.append("缺少 FeedSceneContinueClickWhenWaiting")
    else:
        for token in ("SceneLoader.IsSceneLoading", "SceneClickFeedIntervalSeconds",
                      "IsSceneLoaderWaitingForClick() && FeedSceneContinueClick()", "_sceneClicksFed++"):
            if token not in feeder:
                errors.append("FeedSceneContinueClickWhenWaiting 缺少: " + token)

    waiting = method_body(scenes, "private static bool IsSceneLoaderWaitingForClick()")
    if waiting is None:
        errors.append("缺少 IsSceneLoaderWaitingForClick")
    else:
        for token in ('"pointerClickEventRecevier"', "gameObject.activeSelf"):
            if token not in waiting:
                errors.append("是否在等点击必须看官方点击接收器是否激活，缺少: " + token)
        if not re.search(r"if \(_sceneLoaderClickReceiverField == null\) return true;", waiting):
            errors.append("取不到点击接收器字段时必须退回「加载中就喂」，不能静默不喂")

    click = method_body(scenes, "private bool FeedSceneContinueClick()")
    if click is None or "SceneLoader.Instance.NotifyPointerClick(null);" not in click:
        errors.append("喂点击必须走官方 SceneLoader.NotifyPointerClick")

    load = method_body(scenes, "private IEnumerator LoadScene(")
    if load is None:
        errors.append("缺少 LoadScene 外壳")
    else:
        if "clickToContinue &&" in load or "FeedSceneContinueClick(" in load:
            errors.append("LoadScene 外壳不得按自身 clickToContinue 参数另喂点击：LoadBaseScene 恒开点击门，统一由 Update 喂")
        start = load.find("int clicksAtStart = _sceneClicksFed;")
        end = load.find("_lastSceneClicksFed = _sceneClicksFed - clicksAtStart;")
        if not (0 <= start < end):
            errors.append("clicks_fed 必须按本次加载前后的全局计数差值算")

    describe = method_body(scenes, "private static string DescribeSceneReadiness(")
    if describe is None or "SceneLoader.LoadingComment" not in describe:
        errors.append("过图诊断必须带 SceneLoader.LoadingComment（官方停在哪个等待点）")

    depart = autotest.find('AutotestLog("AUTOTEST_ISLAND_READY", "begin", null);')
    depart_end = autotest.find('AutotestLog("AUTOTEST_ISLAND_READY", "end"', max(depart, 0))
    if depart < 0 or depart_end < 0:
        errors.append("找不到天空岛出发等待段")
    elif "FeedSceneContinueClick" in autotest[depart:depart_end]:
        errors.append("天空岛出发等待段不得另喂点击（统一由 Update 喂，避免两处 owner）")
    return errors


def reverse_checks(sources):
    cases = (
        ("Update 不喂", EXECUTION, "FeedSceneContinueClickWhenWaiting();", ""),
        ("喂点击不看接收器", SCENES, "IsSceneLoaderWaitingForClick() && FeedSceneContinueClick()", "FeedSceneContinueClick()"),
        ("接收器判定改看层级激活", SCENES, "gameObject.activeSelf", "gameObject.activeInHierarchy"),
        ("取不到字段时静默不喂", SCENES, "if (_sceneLoaderClickReceiverField == null) return true;",
         "if (_sceneLoaderClickReceiverField == null) return false;"),
        ("外壳恢复按参数喂", SCENES, "_lastSceneClicksFed = _sceneClicksFed - clicksAtStart;",
         "if (clickToContinue && FeedSceneContinueClick()) _lastSceneClicksFed++;"),
        ("诊断不带加载步骤", SCENES, "SceneLoader.LoadingComment", "string.Empty"),
        ("出发段另喂", AUTOTEST, "if (loadingSince < 0f) loadingSince = Time.realtimeSinceStartup;",
         "if (loadingSince < 0f) loadingSince = Time.realtimeSinceStartup; FeedSceneContinueClick();"),
    )
    failures = []
    for name, rel, old, new in cases:
        if old not in sources[rel]:
            failures.append("反向检查「" + name + "」找不到破坏点: " + old)
            continue
        mutated = dict(sources)
        mutated[rel] = sources[rel].replace(old, new, 1)
        if not check(mutated):
            failures.append("反向检查「" + name + "」没有转红")
    return failures


def main():
    sources = {rel: clean_source((ROOT / rel).read_text(encoding="utf-8")) for rel in (SCENES, EXECUTION, AUTOTEST)}
    errors = check(sources) or reverse_checks(sources)
    if errors:
        for error in errors:
            print("FAIL: " + error)
        return 1
    print("PASS: F3 验收统一喂「点击继续」（Update 每帧、只在官方接收器激活时喂、诊断带加载步骤；反向检查 7/7 转红）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
