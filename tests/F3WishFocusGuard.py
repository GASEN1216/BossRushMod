# -*- coding: utf-8 -*-
"""F3「只跑本次改动」专项（2026-11-25）：Dev 独占、与完整验收同门同外壳、用例表与许愿用例文件对得上。

owner 要能只跑 2026-11-24 这一轮的 F3 用例，不跑全量。专项（`DebugAndTools/F3GameplayValidationWishFocus.cs`）只换编排，
用例本身仍在 `F3GameplayValidationWishes.cs`，完整验收照旧会跑它们。能这样留着，靠下面这些约束：

1. **只在 Dev 构建里存在**：专项文件与配置标题用例文件整份包在 `#if BOSSRUSH_DEV` 里并登记编译清单；F3 面板按钮在 `#if` 里；
   覆盖账本的 `Focus` 与 ModConfig 标题快照也只在 Dev 构建里；`check_dll_identifiers` 登记了入口名（正式构建里必须没有）。
2. **开跑门与外壳同完整验收**：先 `CheckStartGate`（基地、专用测试档、没有活动玩法与模态）再开会话；会话照样回基地跑
   `RunFinalChecks`，`finally` 里 `CompleteSession`；不接天空岛全自动后半程。
3. **用例表跟着用例文件走**：许愿用例文件里记的每个 id 都在专项的基地表或场内表里（两表不重叠），外壳表正好是
   覆盖清单、基线、进出竞技场与 `RunFinalChecks` 记的那些；表里每个 id 都在 `GameplayCoverage.json` 里。
   以后往许愿用例文件加用例忘了加进专项表，这里转红。
4. **覆盖账本收窄而不是放水**：`Focus` 只删表外的自动项与人工项、删空功能，并把清单里找不到的 id 交回去记红；
   专项开跑后立刻收窄，SUMMARY 仍按 `AutomaticNotPassed` 给 INCOMPLETE。
5. **配置标题用例切回原语言**：`MODCONFIG_OPTION_LABELS` 切语言在 `try` 里，切回原语言在 `finally` 里，按官方
   `ToPlainText` 同一口径取标题。

守卫钉结构，证明不了专项跑起来是对的——那要 owner 在 Dev 构建里按一次按钮，AI 读报告。
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
sys.path.insert(0, str(ROOT / "tools"))
from cs_source_util import clean_source  # noqa: E402
from gameplay_coverage import case_ids_in  # noqa: E402

FOCUS = "DebugAndTools/F3GameplayValidationWishFocus.cs"
WISHES = "DebugAndTools/F3GameplayValidationWishes.cs"
LABEL_CASE = "DebugAndTools/F3GameplayValidationModConfigLabels.cs"
RUNNER = "DebugAndTools/F3GameplayValidationRunner.cs"
COVERAGE = "DebugAndTools/F3GameplayValidationCoverage.cs"
LABELS = "Localization/ModConfigOptionLocalization.cs"
IDENTIFIERS = "tools/check_dll_identifiers.py"
MANIFEST = "Assets/Data/GameplayCoverage.json"
BAT = "compile_official.bat"
BACKSLASH = chr(92)
# 会话外壳的诊断分支 id，不进覆盖清单（与 tools/gameplay_coverage.py 的 required_automatic_ids 同口径）。
DIAGNOSTIC_IDS = {"RUN_MARKER", "COVERAGE_REPORT", "SUITE_EXECUTION", "RUNTIME_ERRORS", "EXTERNAL_ERRORS", "LOG_DIAGNOSTICS"}


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
    body = [line.strip() for line in raw.splitlines() if line.strip() and not line.strip().startswith("//")]
    if not body or body[0] != "#if BOSSRUSH_DEV" or body[-1] != "#endif":
        return False
    depth = 0
    for index, code in enumerate(body):
        if code.startswith("#if"):
            depth += 1
        elif code.startswith("#endif"):
            depth -= 1
            if depth == 0 and index != len(body) - 1:
                return False
    return depth == 0


def body_of(source, signature):
    at = source.find(signature)
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


def ordered(errors, text, tokens, label):
    if text is None:
        errors.append("找不到 " + label)
        return
    at = 0
    for token in tokens:
        found = text.find(token, at)
        if found < 0:
            errors.append("%s 缺少或顺序不对：%s" % (label, token))
            return
        at = found + len(token)


def string_array(source, name):
    match = re.search(r"\b" + re.escape(name) + r"\s*=\s*\{(.*?)\};", source, re.S)
    return None if match is None else re.findall(r'"([A-Z0-9_]+)"', match.group(1))


def in_dev_only(errors, raw, rel, tokens):
    for number, code, in_dev in directive_frames(raw):
        for token in tokens:
            if token in code and not in_dev:
                errors.append("%s:%d 在 #if BOSSRUSH_DEV 之外引用了 %s（正式构建里没有它）" % (rel, number, token))


def check(raw):
    errors = []
    clean = {rel: clean_source(text) for rel, text in raw.items() if rel.endswith(".cs")}
    focus, wishes, runner = clean[FOCUS], clean[WISHES], clean[RUNNER]

    # ---- 1. Dev 独占 ----
    for rel in (FOCUS, LABEL_CASE):
        if not wrapped_whole_file(raw[rel]):
            errors.append(rel + " 没有整份包在 #if BOSSRUSH_DEV … #endif 里")
        if "echo(" + rel.replace("/", BACKSLASH) + "\n" not in raw[BAT]:
            errors.append("编译清单缺少 " + rel + "（无通配符，漏了不报错，Dev 构建里会编不过）")
    if "() => F3GameplayValidationRunner.StartWishFocusFromPanel(this));" not in runner:
        errors.append("F3 面板没有「只跑本次改动」按钮（经 StartWishFocusFromPanel 开跑）")
    in_dev_only(errors, raw[RUNNER], RUNNER, ("StartWishFocusFromPanel", "TryStartWishFocus"))
    in_dev_only(errors, raw[COVERAGE], COVERAGE, ("internal List<string> Focus(",))
    in_dev_only(errors, raw[LABELS], LABELS, ("SnapshotForValidation",))
    if '"TryStartWishFocus",' not in raw[IDENTIFIERS]:
        errors.append("tools/check_dll_identifiers.py 没有登记 TryStartWishFocus：正式构建混进专项入口时查不出")

    # ---- 2. 开跑门与外壳 ----
    ordered(errors, body_of(focus, "internal static bool TryStartWishFocus(ModBehaviour host, out string reason)"),
            ("EnsureAttached(host);", "_instance._running", "_instance.CheckStartGate(out reason)", "_instance.BeginSession(out reason)",
             '"SUITE | WISH_FOCUS | read_only=false', "_instance.FocusWishCoverage();",
             "_instance.StartCoroutine(_instance.RunWishFocusSession())"), "TryStartWishFocus（门 → 会话 → 报告头 → 收窄账本 → 开跑）")
    panel = body_of(focus, "internal static void StartWishFocusFromPanel(ModBehaviour host)")
    ordered(errors, panel, ("TryStartWishFocus(host, out reason)", "host.CloseF3BeforeDebugPanel()", "host.ShowMessage(reason)"),
            "StartWishFocusFromPanel（开跑收起 F3，失败弹原因）")
    session = body_of(focus, "private IEnumerator RunWishFocusSession()")
    ordered(errors, session, ("yield return null;", "try", "DriveSessionPhase(RunWishFocusSuite(), false)", "_closingSession = true;",
                              "DriveSessionPhase(RunFinalChecks(), true)", "finally { CompleteSession(); }"),
            "RunWishFocusSession（专项 → 回基地最终核对 → CompleteSession）")
    if session is not None and re.search(r"Autotest|SkyIsland", session):
        errors.append("RunWishFocusSession 不许接天空岛全自动后半程或岛内编排")
    ordered(errors, body_of(focus, "private IEnumerator RunWishFocusSuite()"),
            ('SamplePerformance("BASELINE_10S", 10f, true)', 'CaptureLeakBaseline("WISH_FOCUS")', "yield return RunWishBaseCases();",
             'LoadScene(BossRushArenaSceneIDForValidation(), "SCENE_ENTER_ARENA")', 'SkipWishFocusArenaCases("arena_scene_load_failed")',
             'WaitRuntimeReady("ARENA_READY", SceneTimeoutSeconds)', 'SkipWishFocusArenaCases("arena_runtime_not_ready")',
             "yield return RunWishArenaCases();"), "RunWishFocusSuite（基线 → 基地 → 进竞技场 → 场内）")

    # ---- 3. 用例表 ----
    tables = {name: string_array(focus, name) for name in
              ("WishFocusBaseCaseIds", "WishFocusArenaCaseIds", "WishFocusShellCaseIds", "WishFocusManualIds")}
    for name, values in tables.items():
        if not values:
            errors.append("专项缺少用例表 " + name)
            tables[name] = []
        elif len(values) != len(set(values)):
            errors.append(name + " 里有重复 id")
    base, arena = set(tables["WishFocusBaseCaseIds"]), set(tables["WishFocusArenaCaseIds"])
    shell, manual = set(tables["WishFocusShellCaseIds"]), set(tables["WishFocusManualIds"])
    if base & arena:
        errors.append("基地表与场内表重叠：" + ", ".join(sorted(base & arena)))
    recorded = case_ids_in(wishes) - DIAGNOSTIC_IDS
    if recorded != base | arena:
        errors.append("专项用例表与许愿用例文件对不上：用例文件有、表里没有 %s；表里有、用例文件没有 %s"
                      % (sorted(recorded - base - arena), sorted((base | arena) - recorded)))
    direct_base = case_ids_in(body_of(wishes, "private IEnumerator RunWishBaseCases()") or "")
    direct_arena = case_ids_in(body_of(wishes, "private IEnumerator RunWishArenaCases()") or "")
    if not direct_base or not direct_base <= base:
        errors.append("RunWishBaseCases 直接跑的用例必须都在基地表里：缺 %s" % sorted(direct_base - base))
    if not direct_arena or not direct_arena <= arena:
        errors.append("RunWishArenaCases 直接跑的用例必须都在场内表里：缺 %s" % sorted(direct_arena - arena))
    final_checks = body_of(runner, "private IEnumerator RunFinalChecks()") or ""
    expected_shell = ({"COVERAGE_MANIFEST", "SCENE_ENTER_ARENA", "SCENE_RETURN_BASE"} | case_ids_in(final_checks)
                      | (case_ids_in(focus) - base - arena)) - DIAGNOSTIC_IDS
    if '"SCENE_RETURN_BASE"' not in final_checks or "FINAL_SAVE_READBACK" not in final_checks:
        errors.append("RunFinalChecks 的回基地与最终核对 id 变了，专项外壳表要跟着核对")
    if shell != expected_shell:
        errors.append("专项外壳表与实际记的外壳 id 对不上：缺 %s，多 %s" % (sorted(expected_shell - shell), sorted(shell - expected_shell)))
    try:
        data = json.loads(raw[MANIFEST])
        automatic = {case for feature in data["features"] for case in feature["automatic"]}
        manual_ids = {case["id"] for feature in data["features"] for case in feature["manual"]}
    except (ValueError, KeyError, TypeError) as error:
        errors.append("读不了覆盖清单：%s" % error)
        automatic, manual_ids = set(), set()
    if (base | arena | shell) - automatic:
        errors.append("专项自动 id 不在覆盖清单的自动项里：%s" % sorted((base | arena | shell) - automatic))
    if manual - manual_ids:
        errors.append("专项人工 id 不在覆盖清单的人工项里：%s" % sorted(manual - manual_ids))

    # ---- 4. 覆盖账本收窄 ----
    ordered(errors, body_of(clean[COVERAGE], "internal List<string> Focus(ICollection<string> automaticIds, ICollection<string> manualIds)"),
            ("feature.Automatic.RemoveAll(id => !automaticIds.Contains(id));", "feature.Manual.RemoveAll(test => !manualIds.Contains(test.Id));",
             "if (feature.Automatic.Count + feature.Manual.Count == 0) _features.RemoveAt(i);",
             "if (!seen.Contains(id)) missing.Add(id);", "return missing;"), "GameplayCoverageReport.Focus（只删表外项并交回找不到的 id）")
    ordered(errors, body_of(focus, "private void FocusWishCoverage()"),
            ("_coverage.Focus(automatic,", "if (missing.Count > 0)", 'Record("COVERAGE_MANIFEST", "FAIL"', "WriteCoverageSnapshot();"),
            "FocusWishCoverage（收窄后找不到的 id 记红并重写清单）")
    if "_coverage.AutomaticNotPassed > 0) status = \"INCOMPLETE\"" not in runner:
        errors.append("Finish 不再按 AutomaticNotPassed 给 INCOMPLETE：专项收窄后可能把没跑到的用例算成总 PASS")

    # ---- 5. 配置标题用例 ----
    label_source = clean[LABEL_CASE]
    label_case = body_of(label_source, "private static bool ValidateModConfigOptionLabels(out string metrics, out string reason)")
    ordered(errors, label_case, ("ModConfigOptionLocalization.SnapshotForValidation()", "throw new SkyIslandSkipCase(",
                                 "try", "LocalizationManager.SetLanguage(originalChinese ? SystemLanguage.English : SystemLanguage.ChineseSimplified);",
                                 "finally", "LocalizationManager.SetLanguage(original);", "LocalizationManager.CurrentLanguage == original"),
            "ValidateModConfigOptionLabels（快照 → 切语言 → finally 切回原语言 → 核对切回）")
    if label_case is not None and not re.search(r"finally\s*\{\s*if \(LocalizationManager\.CurrentLanguage != original\) LocalizationManager\.SetLanguage\(original\);\s*\}", label_case):
        errors.append("切回原语言必须写在 finally 里：切语言后核对抛异常时玩家的语言偏好会留在另一种语言")
    if "(ModConfigOptionLocalization.OfficialLabelKeyPrefix + labels[i].Key).ToPlainText()" not in (body_of(label_source, "private static int CountWrongModConfigLabels(") or ""):
        errors.append("配置标题要按官方组件同一口径取（Options_ 前缀 + 配置键，ToPlainText）")
    if 'RunSyncCase("MODCONFIG_OPTION_LABELS", ValidateModConfigOptionLabels);' not in (body_of(wishes, "private IEnumerator RunWishBaseCases()") or ""):
        errors.append("RunWishBaseCases 没有跑 MODCONFIG_OPTION_LABELS")
    return errors


def main():
    raw = {}
    for rel in (FOCUS, WISHES, LABEL_CASE, RUNNER, COVERAGE, LABELS, IDENTIFIERS, MANIFEST, BAT):
        path = ROOT / rel
        raw[rel] = path.read_text(encoding="utf-8-sig", errors="ignore").replace("\r\n", "\n") if path.is_file() else None
    missing = [rel for rel, value in raw.items() if value is None]
    if missing:
        print("F3WishFocusGuard: FAIL - 缺文件：" + ", ".join(sorted(missing)))
        return 1
    errors = check(raw)

    probes = (
        (FOCUS, "#if BOSSRUSH_DEV\n", "", "拆掉专项文件的 #if"),
        (BAT, "echo(DebugAndTools" + BACKSLASH + "F3GameplayValidationWishFocus.cs\n", "", "编译清单漏登记专项文件"),
        (LABEL_CASE, "#if BOSSRUSH_DEV\n", "", "拆掉配置标题用例文件的 #if"),
        (BAT, "echo(DebugAndTools" + BACKSLASH + "F3GameplayValidationModConfigLabels.cs\n", "", "编译清单漏登记配置标题用例文件"),
        (RUNNER, "                BossRushUIColors.Warning, () => F3GameplayValidationRunner.StartWishFocusFromPanel(this));\n#endif\n",
         "#endif\n                BossRushUIColors.Warning, () => F3GameplayValidationRunner.StartWishFocusFromPanel(this));\n", "按钮挪出 #if"),
        (FOCUS, "            if (!_instance.CheckStartGate(out reason)) return false;\n", "", "专项不过开跑门"),
        (FOCUS, "                    yield return DriveSessionPhase(RunFinalChecks(), true);\n", "", "专项不回基地做最终核对"),
        (FOCUS, "                    yield return DriveSessionPhase(RunFinalChecks(), true);\n",
         "                    yield return DriveSessionPhase(RunFinalChecks(), true);\n                    yield return RunSkyIslandAutotestLeg();\n",
         "专项接上天空岛全自动后半程"),
        (FOCUS, '"PETNEST_SOUL_FEED_RULES", ', "", "基地表漏一条用例"),
        (FOCUS, '"DRAGON_DESCENDANT_SPAWN_PLACEMENT",\n', '"DRAGON_DESCENDANT_SPAWN_PLACEMENT", "BOSS_MAP_BEACON",\n', "场内表重复 / 与用例文件对不上"),
        (FOCUS, '"FINAL_SAVE_READBACK",', '', "外壳表漏掉最终存档回读"),
        (FOCUS, '"M_CONFIG_UI_04",', '"M_CONFIG_UI_99",', "人工 id 写错"),
        (FOCUS, "            _instance.FocusWishCoverage();\n", "", "开跑后不收窄覆盖账本"),
        (COVERAGE, "                feature.Manual.RemoveAll(test => !manualIds.Contains(test.Id));\n", "", "收窄不删表外人工项"),
        (LABEL_CASE, "            finally\n            {\n                if (LocalizationManager.CurrentLanguage != original) LocalizationManager.SetLanguage(original);\n            }\n",
         "            finally { }\n            if (LocalizationManager.CurrentLanguage != original) LocalizationManager.SetLanguage(original);\n",
         "切回原语言挪出 finally"),
        (WISHES, '            RunSyncCase("MODCONFIG_OPTION_LABELS", ValidateModConfigOptionLabels);\n', "", "基地阶段不跑配置标题用例"),
        (LABELS, "#if BOSSRUSH_DEV\n", "", "标题快照挪出 Dev 构建"),
        (IDENTIFIERS, '    "TryStartWishFocus",\n', "", "DLL 标识表漏登记专项入口"),
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
        print("F3WishFocusGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("F3WishFocusGuard: PASS（Dev 独占 / 开跑门与外壳同完整验收、不接岛上后半程 / 用例表与许愿用例文件及覆盖清单对得上 / "
          "账本收窄 / 配置标题用例切回原语言；%d 个反向检查）" % len(probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
