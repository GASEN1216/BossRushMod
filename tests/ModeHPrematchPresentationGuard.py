"""L1 接线：两步选人、赛前双方属性、押品图标、揭晓先于开战。实际布局/输入仍须 L3。"""
from pathlib import Path
import sys

from cs_source_util import clean_source
from ModeHOneClickFlowGuard import method_body, squeeze

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "match": "ModeH/ModeHRuntimeModule_MatchFlow.cs",
    "pages": "ModeH/ModeHRuntimeModule_MatchPages.cs",
    "flow": "ModeH/ModeHRuntimeModule_UiFlow.cs",
    "bet": "ModeH/ModeHRuntimeModule_BetFlow.cs",
    "scene": "ModeH/ModeHRuntimeModule_SceneFlow.cs",
    "ui": "ModeH/ModeHUI.cs",
    "draw": "ModeH/ModeHUIFighterDetails.cs",
    "content": "ModeH/ModeHUIPages.cs",
    "details": "ModeH/ModeHRuntimeModule_FighterPresentation.cs",
    "ledger": "ModeH/ModeHCashBetService.cs",
    "refresh_ledger": "ModeH/ModeHDraftRefreshLedger.cs",
    "module": "ModeH/ModeHRuntimeModule.cs",
    "season": "ModeH/ModeHRuntimeModule_SeasonFlow.cs",
}


def check(sources):
    src = {key: clean_source(value) for key, value in sources.items()}
    errors = []

    def body(key, signature):
        result = method_body(src[key], signature)
        if result is None:
            errors.append(FILES[key] + ": missing unique method " + signature)
        return squeeze(result)

    def need(text, token, reason):
        if token not in text:
            errors.append(reason + ": " + token)

    def ordered(text, tokens, reason):
        start = 0
        for token in tokens:
            position = text.find(token, start)
            if position < 0:
                errors.append(reason)
                return
            start = position + len(token)

    route = body("flow", "private void RouteUiForLifecycle(")
    ordered(route, ["if (TryDeferPreparedFighterPage(lifecycle)) return;", "switch (lifecycle)"],
            "赛前/选人首次绘制先分帧准备预案")
    defer = body("details", "private bool TryDeferPreparedFighterPage(")
    if "EnsurePreparedMatchSelection(" in defer:
        errors.append("分帧前不能同步准备整个阵容")
    need(defer, "if (_preparedPageRoutine != null) CancelPreparedFighterPage();", "离开准备相位立即取消协程")
    prepare = body("details", "private IEnumerator PrepareFighterPage(")
    ordered(prepare, ["yield return null;", "if (!IsPreparedPageCurrent(generation)) yield break;", "List<string> profiles"],
            "首次跨帧后读取赛季前复核 owner")
    ordered(prepare, ["PrepareOneFighterPreview(profiles[i], -1)", "yield return null;",
                      "if (!IsPreparedPageCurrent(generation)) yield break;", "ModeHMatchPlanDto plan",
                      "PrepareOneFighterPreview(null, i)", "yield return null;", "_preparedPageComplete = true;"],
            "每名选手单独让出一帧，所有预案就绪后才打开页面")
    need(prepare, "while (IsPreparedPageCurrent(generation) && BossRushUI.IsGamePaused()) yield return null;",
         "官方暂停菜单阻止预览后台继续准备")
    current = body("details", "private bool IsPreparedPageCurrent(")
    for token in ("generation == _preparedPageGeneration", "!_commandsClosed", "_season != null",
                  "_preparedPageOwner == _runState.OwnerToken", "_preparedPagePhase == _runState.Lifecycle"):
        need(current, token, "准备协程必须属于当前会话和页面")
    need(body("flow", "private void DestroyUi()"), "CancelPreparedFighterPage();", "UI 清理取消准备协程")
    cancel = body("details", "private void CancelPreparedFighterPage()")
    ordered(cancel, ["_preparedPageGeneration++;", "_preparedPageRoutine = null;", "_owner.StopCoroutine(routine);"],
            "取消先作废代次和句柄，再停止旧协程")

    pick = body("match", "private void OnDraftPick(")
    ordered(pick, ["if (string.IsNullOrEmpty(_draftPrimaryProfileId))", "_draftPrimaryProfileId = picked.profileId;",
                   "RouteUiForLifecycle(_runState.Lifecycle);", "return;", "TrySignContracts("],
            "首发选择必须先返回页面，第二次明确选接力才签约")
    refresh = body("match", "private void RefreshDraftCandidates()")
    need(src["scene"], "DraftMaxRefreshes = 3;", "刷新预算固定三次")
    need(refresh, "_draftRefreshCount >= DraftMaxRefreshes", "刷新必须核对预算")
    need(refresh, "merged.Add(locked);", "刷新保留锁定对象")
    need(refresh, "string.Equals(candidate.profileId, _draftPrimaryProfileId, StringComparison.Ordinal)", "刷新必须排除首发重复")
    # 刷新次数跟着这一季走（2026-09-25）：独立 key 按 runId 记，退出重进不能重新给满三次；
    # 先记次数再落赛季，同一次 SaveFile 一起写盘；模块销毁退订。
    ordered(refresh, ["ModeHDraftRefreshLedger.UsedFor(_runState.RunId)", "_draftRefreshCount++;",
                      "ModeHDraftRefreshLedger.Record(_runState.RunId, _draftRefreshCount);",
                      'TryPersistSeason("draft_refresh");'], "刷新先读已用次数，再记账，再落赛季")
    need(src["refresh_ledger"], "StorageKey = \"BossRush_ModeHDraftRefresh_v1\"", "刷新次数用独立 typed key（赛季 DTO 进摘要不能加字段）")
    need(src["refresh_ledger"], "string.Equals(data.RunId, runId, StringComparison.Ordinal)", "刷新次数只认本季 runId")
    need(body("module", "internal static void ResetModeHStaticCaches()"), "ModeHDraftRefreshLedger.ResetStaticCaches();",
         "模块销毁必须退订刷新次数的存档事件")
    # 首发可取消重选；五席无解且刷新用完时挂出退出，不把玩家困在选人页
    need(pick, "_draftPrimaryProfileId = null;", "再点首发必须能取消首发")
    need(pick, "_draftDeadEndRunId = _runState.RunId;", "无解时必须挂出退出本赛季")
    need(body("season", "private bool HasAnyViableDraftPair()"), "CanConstructFullSeason(contract, assignments, out reason)",
         "无解判定必须走同一条六场可行性门")
    draft = body("match", "private ModeHPageContent BuildDraftPageContent()")
    if "AppendCashBetRow(" in draft:
        errors.append("选人页不得提前下注")
    need(body("match", "private ModeHCardData BuildProfileCard("),
         "FillFighterDetails(card, GetPreparedFighterStats(profile));", "候选属性必须来自实际装配预案")

    advance = body("flow", "private void AdvanceTowardsFight()")
    ordered(advance, ["if (!_allowBriefToLoadout) return;", "EnterLoadoutEditing();"], "每场先停在赛前页等待玩家")
    if "ApplyAutoRosterDefaults();" in advance:
        errors.append("赛前确认后不能再静默替换首发")
    brief = body("pages", "private ModeHPageContent BuildBriefPageContent()")
    ordered(brief, ["EnsurePreparedMatchSelection(out prepareFailure)", "AppendMatchSides(page);", "AppendCashBetRow(page);"],
            "赛前先准备阵容/赔率，再列双方属性，最后选择押注")
    sides = body("pages", "private void AppendMatchSides(")
    for token in ("GetPreparedFighterStats(starter, roster.starterKitIds)",
                  "GetPreparedFighterStats(relay, roster.relayKitIds)", "GetPreparedEnemyStats(plan, i)",
                  "NormalizeFighterStatScales(page.PlayerFighters, page.EnemyFighters);"):
        need(sides, token, "双方必须显示与装配同源的全部人员属性")
    draw = body("draw", "private static void CreateMatchComparison(")
    need(draw, "content.PlayerFighters", "我方独立列")
    need(draw, "content.EnemyFighters", "敌方独立列")
    measurements = body("draw", "private static void CreateFighterMeasurements(")
    for token in ("stat.Text", "stat.Value / stat.Maximum", "data.Equipment[i]"):
        need(measurements, token, "属性用数字+同尺度条，所有装备有图标")
    need(body("draw", "private static void CreateFighterColumn("), "CreateScrollHost(", "双方多人清单各自可滚动")
    need(body("draw", "private static void CreateItemBetGrid("), "CreateScrollHost(", "所有押品可滚动选取，不硬截断")
    need(body("bet", "private ModeHPageContent BuildItemBetPickerPage()"), "candidate.Equipped", "穿戴押品必须标识")
    need(body("content", "private static void BuildChampionCard("), "if (data.IsSelected) AddSelectedBadge(card, data.SelectedBadge);",
         "已锁定首发有可见角标")

    lock = body("match", "private void LockLoadoutAndStartMatch()")
    ordered(lock, ["ReserveStandingCashBet();", "if (ModeHBetRevealView.IsPlaying)", "_waitingForBetReveal = true;",
                   "_ui.ClosePage();", "return;", "StartMatchSpawning();"], "押注动画在开战前完成，先关闭旧页面")
    update = body("match", "partial void OnUpdateInternal(")
    need(update, "_waitingForBetReveal && !_commandsClosed && !ModeHBetRevealView.IsPlaying", "停播且 owner 活跃才可开战")
    need(update, "&& !BossRushUI.IsGamePaused()", "暂停中不允许动画结束触发开战")
    need(update, "_runState.Lifecycle == ModeHLifecycle.LoadoutLocked", "等待后复核锁盘相位")
    need(body("ui", "private void CreateSpectatorActions("),
         "-SpectatorActionMargin - SpectatorActionSize.x * 0.5f", "观战按钮整块留在右侧屏幕内")

    need(src["ledger"], "plan.prizeItems = plan.pendingItems;", "完整奖品图标清单必须先于交付保存")
    need(body("ledger", "private static ModeHCashBetRecord Decode(string json)"),
         'root.TryGetString("prizeItems", out prizeItems)', "奖品图标列表读档恢复")
    need(body("ledger", "private static string Encode(ModeHCashBetRecord record)"),
         "SimpleJsonHelper.EscapeString(sb, record.prizeItems ?? string.Empty);", "奖品图标列表随账本落盘")
    icon = body("draw", "private static void CreateItemIcon(")
    need(icon, '"Count"', "奖品图标右下角数量")
    need(icon, "TextAlignmentOptions.BottomRight", "数量在右下")
    need(body("draw", "private static float CreateRewardIcons("), "size, true, false)", "奖品只显示图标/数量，不绘制名字")
    need(body("details", "private void AppendPrizeIcons("), "ModeHItemBetEntry.Decode(record.prizeItems)", "奖品图标必须读固定计划")
    return errors


PROBES = [
    ("flow", "if (TryDeferPreparedFighterPage(lifecycle)) return;", ""),
    ("details", "_preparedPageGeneration++;", "_preparedPageGeneration += 0;"),
    ("details", "generation == _preparedPageGeneration", "true"),
    ("match", "_draftPrimaryProfileId = picked.profileId;", "_draftPrimaryProfileId = null;"),
    ("match", "merged.Add(locked);", "merged.Clear();"),
    ("scene", "DraftMaxRefreshes = 3;", "DraftMaxRefreshes = 30;"),
    ("flow", "if (!_allowBriefToLoadout) return;", ""),
    ("match", "_waitingForBetReveal = true;", "_waitingForBetReveal = false;"),
    ("match", "&& !_commandsClosed && !ModeHBetRevealView.IsPlaying", "&& !ModeHBetRevealView.IsPlaying"),
    ("draw", "stat.Value / stat.Maximum", "1f"),
    ("draw", "TextAlignmentOptions.BottomRight", "TextAlignmentOptions.Center"),
    ("ledger", "plan.prizeItems = plan.pendingItems;", "plan.prizeItems = string.Empty;"),
    ("details", "ModeHItemBetEntry.Decode(record.prizeItems)", "ModeHItemBetEntry.Decode(record.pendingItems)"),
    ("match", "                ModeHDraftRefreshLedger.Record(_runState.RunId, _draftRefreshCount);\n", ""),
    ("match", "            _draftRefreshCount = Math.Max(_draftRefreshCount, ModeHDraftRefreshLedger.UsedFor(_runState.RunId));\n            if (_draftRefreshCount >= DraftMaxRefreshes) return;\n", ""),
    ("module", "            ModeHDraftRefreshLedger.ResetStaticCaches();\n", ""),
    ("refresh_ledger", "string.Equals(data.RunId, runId, StringComparison.Ordinal)", "true"),
    ("match", "                    _draftPrimaryProfileId = null;\n                    RouteUiForLifecycle", "                    RouteUiForLifecycle"),
    ("match", "                        _draftDeadEndRunId = _runState.RunId;\n", ""),
]


def main():
    sources = {key: (ROOT / value).read_text(encoding="utf-8-sig") for key, value in FILES.items()}
    errors = check(sources)
    for key, before, after in PROBES:
        if sources[key].count(before) != 1:
            errors.append("probe anchor: " + key + " " + before)
            continue
        mutated = dict(sources)
        mutated[key] = mutated[key].replace(before, after, 1)
        if not check(mutated):
            errors.append("probe did not fail: " + key + " " + before)
    for error in errors:
        print("FAIL " + error)
    print("ModeHPrematchPresentationGuard: " + ("FAIL" if errors else "PASS"))
    return int(bool(errors))


if __name__ == "__main__":
    sys.exit(main())
