"""
ModeHOneClickFlowGuard — Mode H「选完人进入赛前参数页、确认后开打、场间最多按一个键、镜头跟着选手」的结构守卫。

背景（2026-09-23，owner 人工实测第 6 条）：
  「鸭王杯我一进去就在跑什么契约什么的，跑完了后才选择什么什么武将而且还要选一堆东西，
   能不能只弄一个选择武将的页面选完后就开始……选完后页面是这个样子完全看不到有斗蛐蛐啊，
   而且下面那个拍铃铛按钮也太丑了吧。」

改法（行为与理由写在各方法注释里）：
  - 入口页是唯一的选人页：一次点击 = 签主将 + 自动配接力 + 推到赛前 MatchBrief；
  - 赛前参数与押注页由玩家点「开打」后才进入整备和锁盘；其余自动链中间相位不建页面（_deferPageRoutes）；
  - 默认值：不下虚拟注、不押真实物品（兜底页上勾过的押品格也要清掉）、默认阵容与配装、招牌口令；
  - 结算页的战痕 / 整备奖励按默认值自动处理（复用玩家点按钮走的同一批方法），只剩一个「下一场」；
  - 转会窗口保留，两个按钮都经自动链；
  - 交战期间官方镜头对准当前选手（只 SetTarget，不转移控制权），结束 / 离场 / 释放时还原；
  - 拍铃卡挂在左上状态卡下面，不再压住底部快捷栏；单行字框高够一行；
  - 加载页不把内部 reasonId 显示给玩家；HUD 敌人行不再借用「人数区间」标签。

本守卫只证明「接线与顺序在」，不证明手感：镜头跟随、迷雾放宽与页面观感要实机看。
每条断言都有一条内置变异探针，探针必须让 check() 转红，否则本守卫自己判失败。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from cs_source_util import clean_source  # noqa: E402

FILES = {
    "match": "ModeH/ModeHRuntimeModule_MatchFlow.cs",
    "ui_flow": "ModeH/ModeHRuntimeModule_UiFlow.cs",
    "season": "ModeH/ModeHRuntimeModule_SeasonFlow.cs",
    "combat": "ModeH/ModeHRuntimeModule_CombatFlow.cs",
    "scene": "ModeH/ModeHRuntimeModule_SceneFlow.cs",
    "lease": "ModeH/ModeHSpectatorLease.cs",
    "hud": "ModeH/ModeHUI.cs",
    "pages": "ModeH/ModeHUIPages.cs",
}

# 会弹模态页的相位；自动链只允许压这些，入场 / 交战 / 恢复 / 挂起必须照常路由
PAGE_LIFECYCLES = {
    "Drafting", "RosterLocked", "MatchBrief", "LoadoutEditing", "OddsPreview",
    "MatchSettling", "Intermission", "TransferWindow", "HallOfFame",
}


def read(rel):
    with open(os.path.join(REPO_ROOT, rel), "r", encoding="utf-8-sig") as handle:
        return handle.read()


def method_body(code, signature):
    """按签名切出方法体（跳过字符串 / 字符字面量里的花括号）；签名必须恰好出现一次。"""
    if code.count(signature) != 1:
        return None
    start = code.index(signature)
    i = code.index("{", start)
    depth = 0
    n = len(code)
    while i < n:
        ch = code[i]
        if ch == '"':
            i += 1
            while i < n and code[i] != '"':
                i += 2 if code[i] == "\\" else 1
        elif ch == "'":
            i += 1
            while i < n and code[i] != "'":
                i += 2 if code[i] == "\\" else 1
        elif ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return code[start:i + 1]
        i += 1
    return None


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

    # ---- 选人：一次点击就进自动链 ----
    pick = body("match", "private void OnDraftPick(")
    need(pick, 'RunAutoAdvance("champion_picked", delegate',
         "选人点击必须直接进自动开打链（不再停在名单 / 看盘 / 赔率页）")
    draft = body("match", "private ModeHPageContent BuildDraftPageContent()")
    need(draft, "page.ShowRealStakeNotice = true;", "入口页仍须固定披露真实押品风险（§22.1）")
    need(draft, "page.CompactRiskNotice = true;", "选人页的风险披露放页脚，不占顶部红条")
    card = body("match", "private ModeHCardData BuildProfileCard(")
    need(card, "card.PortraitKey = profile.stableKey;", "选人卡立绘键必须是 stableKey（= 图鉴条目键）")
    need(card, "card.Body = ResolveFighterPlainDescription(profile);", "选人卡正文必须是白话说明")

    # ---- 镜头：每帧同步，交战以外对回玩家 ----
    update = body("match", "partial void OnUpdateInternal(")
    need(update, "_spectatorLease.SyncCameraTarget(", "每帧驱动必须同步观战镜头")
    need(update, "IsCombatLifecycle(_runState.Lifecycle)", "镜头只在交战相位对准选手")

    # ---- 路由：只压页面相位 ----
    route = body("ui_flow", "private void RouteUiForLifecycle(ModeHLifecycle lifecycle)")
    ordered(route, ["if (_deferPageRoutes && IsPageLifecycle(lifecycle)) return;", "switch (lifecycle)"],
            "自动链只能在进 switch 之前按页面相位早退")
    intermission = route[route.find("case ModeHLifecycle.Intermission:"):] if "case ModeHLifecycle.Intermission:" in route else ""
    ordered(intermission, ["case ModeHLifecycle.Intermission:", "ApplySettlementDefaults();",
                           "OpenPage(ModeHPage.Settlement, BuildSettlementPageContent());"],
            "幕间先按默认值处理战痕 / 整备，再建结算页")
    page_body = method_body(src["ui_flow"], "private static bool IsPageLifecycle(ModeHLifecycle lifecycle)")
    if page_body is None:
        errors.append("[UiFlow] 找不到 IsPageLifecycle")
    else:
        listed = set(re.findall(r"case ModeHLifecycle\.(\w+):", page_body))
        if listed != PAGE_LIFECYCLES:
            errors.append("IsPageLifecycle 必须恰好是会弹模态页的九个相位；多压了入场 / 交战 / 恢复会卡死，"
                          "少了会闪中间页（多 %r，少 %r）" % (sorted(listed - PAGE_LIFECYCLES),
                                                          sorted(PAGE_LIFECYCLES - listed)))

    auto = body("ui_flow", "private void RunAutoAdvance(string reasonId, Action firstStep)")
    ordered(auto, ["_deferPageRoutes = true;", "firstStep();", "AdvanceTowardsFight();",
                   "if (outer) _deferPageRoutes = false;", "RouteUiForLifecycle(_runState.Lifecycle);"],
            "自动链：先挡页、做首步、推到开打、放开、最后按停下的相位路由一次")
    need(auto, "IsPageLifecycle(_runState.Lifecycle)", "链尾只补路由页面相位（入场与恢复已在链中路由过）")

    advance = body("ui_flow", "private void AdvanceTowardsFight()")
    ordered(advance, ["OpenFirstMatchBrief();", "EnsureMatchPlan();", "EnterLoadoutEditing();",
                      "_selectedVirtualStake = 0;", "ModeHRealStakeService.ClearSelection();",
                      "LockLoadoutAndStartMatch();"],
            "自动链必须走原有命令方法，并在锁盘前清零虚拟注、清空真实押品选择")
    need(advance, "if (!_allowBriefToLoadout) return;", "自动链到达 MatchBrief 后必须先停下，不能跳过赛前参数与押注页")
    brief_start = body("ui_flow", "private void StartMatchFromBrief()")
    need(brief_start, "_allowBriefToLoadout = true;", "只有赛前页的开打按钮可以继续进入整备")
    need(brief_start, 'RunAutoAdvance("brief_start", null);', "赛前页开打必须复用统一自动链")

    defaults = body("ui_flow", "private void ApplySettlementDefaults()")
    need(defaults, "ResolveScarOffer(", "战痕默认处理必须复用结算页按钮的同一方法（身份围栏与落盘屏障）")
    ordered(defaults, ["ModeHSeasonRewardService.TrySelectKit(", 'TryPersistSeason("reward_auto_selected");'],
            "整备奖励默认领取后必须落盘（按批节流即可：没落盘就崩，读档后原样再领一次，结果相同）")
    if "CompleteSettlementAndRoute" in defaults:
        errors.append("默认处理不得自己归档并跳走：玩家还要看一眼结算页")
    decorate = body("ui_flow", "private void DecorateSettlementPage(ModeHPageContent page)")
    need(decorate, 'RunAutoAdvance("next_match", archive)', "结算页的「下一场」必须经自动链直接开打")

    transfer = body("season", "private ModeHPageContent BuildTransferPageContent()")
    for token in ('RunAutoAdvance("transfer_no_offer"', 'RunAutoAdvance("transfer_accept"',
                  'RunAutoAdvance("transfer_keep"'):
        need(transfer, token, "转会窗口的每个出口都要一键直达下一场")

    # ---- 观战租约：镜头只 SetTarget，不转移控制权；每条收尾都还原 ----
    sync = body("lease", "public void SyncCameraTarget(CharacterMainControl fighter, bool matchLive)")
    need(sync, "camera.SetTarget(fighter)", "观战镜头必须用官方 GameCamera.SetTarget 对准选手")
    for forbidden in ("SetControllingCharacter", "ControlOtherCharacter"):
        if forbidden in sync:
            errors.append("观战镜头不得转移控制权（%s 会把选手交给玩家、关掉它的 AI）" % forbidden)
    ordered(sync, ["if (!matchLive)", "RestoreCameraTarget();", "if (fighter == null) return;"],
            "离开交战相位先还原；交战中选手为空时保持镜头不动")
    restore_vision = body("lease", "private void RestoreSpectatorVision()")
    need(restore_vision, "_allVisionField.SetValue(fog, false)", "放宽的战争迷雾必须还原")
    need(restore_vision, "!_originalAllVision", "只还原本租约改过的值")
    release_combat = body("combat", "private void ReleaseCombatRuntimeObjects()")
    need(release_combat, "_spectatorLease.RestoreCameraTarget();", "回收选手前先把镜头还给玩家")

    # ---- HUD 与加载页 ----
    tick = body("hud", "public void TickHud(")
    need(tick, '"Hud_EnemiesLeft"', "HUD 敌人行是「场上敌人」")
    if '"Summary_EnemyCount"' in tick:
        errors.append("HUD 敌人行不得再借用看盘页「人数区间」的标签")
    bell = body("hud", "private void CreateBellButton(Action onRingBell)")
    need(bell, '"ModeH_Bell", _hudRoot.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),',
         "拍铃卡挂左上（旧版底部正中压住官方快捷栏）")
    need(bell, "StatusSize.y + BellCardGap", "拍铃卡排在状态卡正下方")
    need(bell, "ZombieModeUIHelper", "拍铃卡走共享 UI 库")
    progress = body("scene", "private static string DescribeCertificationProgress(ModeHCertificationResult result)")
    if "FailureReasonId" in progress:
        errors.append("加载页不得把内部 reasonId 显示给玩家")
    need(progress, '"Diag_Progress"', "加载页进度行用白话模板")

    pages = src["pages"]
    build = body("pages", "public static void Build(")
    ordered(build, ["case ModeHPage.Entry:", "case ModeHPage.Transfer:",
                    "CreateChampionCards(surface, panelSize, content, cursorY);"],
            "入口页与转会页用选人卡渲染")
    portrait = body("pages", "private static void CreatePortrait(")
    ordered(portrait, ["CodexPortraitCache.GetPortrait(data.PortraitKey)",
                       "CodexPortraitCache.GetOfficialIcon(data.PortraitKey)",
                       "ModeHPresentationAssetCache.GetEmblemSprite()", '"Initial"'],
            "立绘链与图鉴同源：图鉴立绘 → 官方图标 → 模式徽记淡显 → 名字首字（2026-09-23 UB-13：首字与名字重复，像占位）")

    # 单行字框：高度至少 1.45×字号 + 4，否则 TMP Ellipsis 会把整串清空
    def const_value(text, name):
        match = re.search(r"const float " + name + r" = ([\d.]+)f;", text)
        return float(match.group(1)) if match else None

    def font_of(text, pattern):
        match = re.search(pattern, text)
        return float(match.group(1)) if match else None

    single_lines = [
        ("hud", "BellTitleHeight", r'CreateBellText\(card\.transform, "Title", [^,]+, ([\d.]+)f', 1),
        ("hud", "BellSubtitleHeight", r'CreateBellText\(card\.transform, "Subtitle", [^,]+, ([\d.]+)f', 1),
        ("pages", "ChampionNameHeight", r'CreateChampionText\(card, "Name", [^,]+, ([\d.]+)f', 1),
        ("pages", "ChampionRoleHeight", r'CreateChampionText\(card, "Role", [^,]+, ([\d.]+)f', 1),
        ("compact", "CompactNoticeHeight", r'"RealStakeRiskNotice"\),\s*([\d.]+)f', 2),
    ]
    compact = method_body(src["pages"], "private static void CreateCompactRiskNotice(") or ""
    for key, const, pattern, lines in single_lines:
        text = compact if key == "compact" else src[key]
        height = const_value(src["pages"] if key == "compact" else src[key], const)
        font = font_of(text, pattern)
        if height is None or font is None:
            errors.append("[%s] 读不到 %s 或它的字号" % (FILES.get(key, FILES["pages"]), const))
        elif height < lines * 1.45 * font + 4:
            errors.append("%s=%.0f 装不下 %d 行 %.0f 号字（至少 %.1f）" % (
                const, height, lines, font, lines * 1.45 * font + 4))
    if pages.count("GetActionBandReserve(panelSize, content)") < 4:
        errors.append("选人卡也必须按共用动作带让位（GetActionBandReserve）")
    return errors


PROBES = [
    ("match", 'RunAutoAdvance("champion_picked", delegate', 'RunDetached("champion_picked", delegate'),
    ("match", "card.PortraitKey = profile.stableKey;", "card.PortraitKey = profile.profileId;"),
    ("match", "_spectatorLease.SyncCameraTarget(", "_spectatorLease.GetHashCode("),
    ("ui_flow", "if (_deferPageRoutes && IsPageLifecycle(lifecycle)) return;", "if (_deferPageRoutes) return;"),
    ("ui_flow", "                case ModeHLifecycle.HallOfFame:\n                    return true;",
     "                case ModeHLifecycle.HallOfFame:\n                case ModeHLifecycle.MatchSpawning:\n"
     "                    return true;"),
    ("ui_flow", "            ModeHRealStakeService.ClearSelection();\n", ""),
    ("ui_flow", 'TryPersistSeason("reward_auto_selected");', 'MarkSeasonDirty();'),
    ("ui_flow", 'RunAutoAdvance("next_match", archive)', "archive()"),
    ("season", 'RunAutoAdvance("transfer_keep",', 'RunDetached("transfer_keep",'),
    ("lease", "camera.SetTarget(fighter)", "LevelManager.Instance.SetControllingCharacter(fighter)"),
    ("lease", "_allVisionField.SetValue(fog, false)", "_allVisionField.GetValue(fog)"),
    ("combat", "_spectatorLease.RestoreCameraTarget();", "_spectatorLease.StopAcceptingBell();"),
    ("hud", '"Hud_EnemiesLeft"', '"Summary_EnemyCount"'),
    ("hud", "internal const float BellTitleHeight = 34f;", "internal const float BellTitleHeight = 28f;"),
    ("pages", "sprite = ModeHPresentationAssetCache.GetEmblemSprite();", "sprite = null;"),
    ("scene", "            if (result == null || result.TotalKeys <= 0) return string.Empty;\n",
     "            if (result == null || result.TotalKeys <= 0) return string.Empty;\n"
     "            if (result.FailureReasonId != null) return result.FailureReasonId;\n"),
    ("pages", "CodexPortraitCache.GetPortrait(data.PortraitKey)", "null"),
]


def main():
    sources = {}
    for key, rel in FILES.items():
        sources[key] = read(rel).replace("\r\n", "\n")
    errors = check(sources)

    for key, before, after in PROBES:
        if sources[key].count(before) != 1:
            errors.append("[probe] 变异锚点不唯一或失效: %s: %r" % (FILES[key], before[:60]))
            continue
        mutated = dict(sources)
        mutated[key] = sources[key].replace(before, after, 1)
        if not check(mutated):
            errors.append("[probe] 变异没被抓住: %s: %r" % (FILES[key], before[:60]))

    if errors:
        print("ModeHOneClickFlowGuard: FAIL (%d errors)" % len(errors))
        for error in errors:
            print("  - " + error)
        return 1
    print("ModeHOneClickFlowGuard: PASS（选人一键开打、自动链只压页面相位、默认不押、结算 / 转会一键下一场、"
          "镜头只 SetTarget 且每条收尾都还原、拍铃卡离开快捷栏、单行字框够高；%d 条变异探针全部转红）" % len(PROBES))
    return 0


if __name__ == "__main__":
    sys.exit(main())
