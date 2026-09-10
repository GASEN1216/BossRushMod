"""天空岛可玩性与时长评估（2026-09-10）逐条修复的结构回归。

每一条对应 `CODE_REVIEW_FINDINGS.md` 的一个 CR 编号（CR-2026-09-10-029 起）：

- 战斗里了结的三件事（战胜折翎、战胜守钟装置、击败噬风）的剧情回话，以前被会话整句丢掉，玩家只看到「航路已清理」；
- 折翎旧腰牌纪念物读不到刻字（Wiki 承诺「旧腰牌写着『航路交给你』，钟守认这份物证」）；
- 挑战开始的回执在面板收起之后才写回正文，被 `SetBodyText` 静默吞掉；
- 8 个 `Search_*_02` 见闻点共用 default 那一段文案；
- 苔药冷却走 `unscaledTime`（暂停菜单背后照走）；
- 岛上每接受一条事实就整档同步写盘一次（去抖）；
- 分段计时日志 `SKY_TIMING`（给 owner 实机回填时长模型），只能挂在事件上、不能进每帧路径；
- 折翎「挑战」按钮写明战胜后不能再和解。

凡是「某句必须在某个方法里」的检查，一律先剥注释、切出方法体、规范空白，再按完整语句找，
并尽量钉住语句之间的先后；去抖与回话的**行为**由执行回归 `tests/fixtures/SkyIslandStory` 验证，这里只钉结构。
"""
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
SKY = "DebugAndTools/SkyIsland/"


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def body_of(source, signature):
    """切出以 signature 开头的块体（到配对的收尾大括号为止）；找不到返回 None。"""
    start = source.find(signature)
    if start < 0:
        return None
    open_brace = source.find("{", start + len(signature))
    if open_brace < 0:
        return None
    depth = 0
    for i in range(open_brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[open_brace + 1:i]
    return None


def squash(text):
    """规范空白：连续空白压成一个空格，括号、点号、冒号两侧的空白去掉（`a . b (` 与 `a.b(`、`case "X": return` 与 `case "X":return` 等价）。
    源码与期望语句走同一个规范化，比较两边始终一致。"""
    text = re.sub(r"\s+", " ", text or "")
    return re.sub(r"\s*([(){};:,.\[\]])\s*", r"\1", text)


def main():
    errors = []

    def need_body(source, signature, label):
        body = body_of(source, signature)
        if body is None:
            errors.append("找不到 " + label + "（" + signature + "）")
            return ""
        return squash(body)

    def require(text, token, why):
        if text and squash(token) not in text:
            errors.append(why + "（缺 " + token + "）")

    def forbid(text, token, why):
        if text and squash(token) in text:
            errors.append(why + "（出现 " + token + "）")

    def ordered(text, tokens, why):
        if not text:
            return
        position = -1
        for token in tokens:
            found = text.find(squash(token), position + 1)
            if found < 0 or found <= position:
                errors.append(why + "（顺序或缺失：" + token + "）")
                return
            position = found

    rules_raw = (ROOT / (SKY + "SkyIslandStoryRules.cs")).read_text(encoding="utf-8-sig")
    rules = read(SKY + "SkyIslandStoryRules.cs")
    world = read(SKY + "SkyIslandWorldStory.cs")
    service = read(SKY + "SkyIslandStoryService.cs")
    services = read(SKY + "SkyIslandServices.cs")
    session = read(SKY + "SkyIslandSession.cs")

    # ---- 1. 战斗了结的回话：文案只有一份，TryApply 与场上字幕都取它 ----
    outcome = need_body(rules, "internal static string CombatOutcome(SkyIslandStoryFlag flag)", "战斗回话文案源")
    for flag in ("ZhelingDefeated", "BellKeeperDefeated", "StormSlain"):
        require(outcome, "case SkyIslandStoryFlag." + flag + ":return L10n.T(", "CombatOutcome 缺 " + flag + " 的回话")
    require(outcome, "default:return null;", "CombatOutcome 对其它旗标必须返回 null（面板动作不读战斗回话）")
    apply_body = need_body(rules, "internal static bool TryApply(SkyIslandStoryData source, SkyIslandStoryAction action,", "剧情规则")
    for action in ("ZhelingDefeated", "BellKeeperDefeated", "StormSlain"):
        block = apply_body.split("case SkyIslandStoryAction." + action + ":", 1)
        if len(block) < 2:
            errors.append("TryApply 缺 " + action + " 分支")
            continue
        block = block[1].split("case SkyIslandStoryAction.", 1)[0]
        require(block, "message = CombatOutcome(SkyIslandStoryFlag." + action + "); break;",
                "TryApply 的 " + action + " 分支必须取 CombatOutcome，文案不能再写第二份")
    # 文案只出现一次（剥注释之后数原文里的字面量；注释里可以提）。
    for phrase in ("『航路交给你。』镜水寺的路已开放", "那就让钟声，为归来的人响一次", "从今天起都少了一个理由"):
        literal_count = len(re.findall(r'"[^"\n]*' + re.escape(phrase), clean_source(rules_raw)))
        if literal_count != 1:
            errors.append("战斗回话「" + phrase + "」在规则文件里应当只有一份字面量，实际 %d 份" % literal_count)
    flags_decl = rules.split("internal static readonly SkyIslandStoryFlag[] CombatOutcomeFlags", 1)
    if len(flags_decl) < 2:
        errors.append("缺 CombatOutcomeFlags")
    else:
        declared = squash(flags_decl[1].split(";", 1)[0])
        listed = re.findall(r"SkyIslandStoryFlag\.(\w+)", declared)
        if listed != ["ZhelingDefeated", "BellKeeperDefeated", "StormSlain"]:
            errors.append("CombatOutcomeFlags 必须恰好是折翎战败 / 钟守战败 / 噬风三项，实际 " + ",".join(listed))

    tick = need_body(world, "internal void Tick()", "WorldStory.Tick")
    ordered(tick, ["if (displayedFlags == story.Current.flags) return;",
                   "int added = displayedFlags < 0 ? 0 : story.Current.flags & ~displayedFlags;",
                   "displayedFlags = story.Current.flags;",
                   "AnnounceCombatOutcomes(added);"],
            "WorldStory.Tick 必须先算新增位、再记下已显示的旗标、再读回话（进岛首帧不重播旧结果）")
    announce = need_body(world, "private void AnnounceCombatOutcomes(int added)", "战斗回话字幕")
    require(announce, "if (added == 0) return;", "没有新增位时不得读回话")
    require(announce, "SkyIslandStoryFlag[] outcomes = SkyIslandStoryRules.CombatOutcomeFlags;", "战斗回话只读 CombatOutcomeFlags")
    require(announce, "if ((added & (int)outcomes[i]) != 0) session.Announce(SkyIslandStoryRules.CombatOutcome(outcomes[i]), false);",
            "新增的战斗结果必须经会话唯一提示出口读成字幕")

    # ---- 2. 折翎旧腰牌的刻字 ----
    # 腰牌落在他那一战的锚点上：R-1 之后居民站位 = 遭遇锚点 EnemySpawn_F（SkyIslandContentPackGuard 按 World.json 核对三者同点）。
    require(tick, 'Beacon("EnemySpawn_F", L10n.T("折翎的旧腰牌", "Zheling\'s old badge"), BossRushUIColors.Accent, ZhelingBadgeText);',
            "折翎纪念物必须带自己的正文（刻字 + 物证含义），并落在他那一战的锚点上")
    badge = need_body(world, "private string ZhelingBadgeText()", "旧腰牌正文")
    require(badge, "『航路交给你。』", "旧腰牌正文缺刻字")
    require(badge, "story.Summary", "旧腰牌正文之后仍要附旅程摘要")
    beacon = need_body(world, "private void Beacon(string marker, string label, Color color, Func<string> body = null, "
                              "Func<List<SkyIslandStoryPresentation.Choice>> choices = null)", "纪念物")
    require(beacon, "presentation.Show(label, body != null ? body() : story.Summary,", "纪念物面板必须用传入的正文")
    require(beacon, "choices != null ? choices() : new List<SkyIslandStoryPresentation.Choice>(),",
            "纪念物面板的选项每次打开现取（归航船名册读过的页随存档变化）")

    # ---- 3. 挑战开始的回执不能在面板收起之后写回正文 ----
    challenge = need_body(world, "private SkyIslandStoryPresentation.Choice Challenge(string label, string id)", "挑战选项")
    ordered(challenge, ["presentation.Dispose();", 'string started = L10n.T("挑战开始", "The challenge begins");',
                        "session.Announce(started, false);", "return started;"],
            "挑战开始必须在面板收起后改走字幕")
    storm = need_body(world, "private void StormChoice(List<SkyIslandStoryPresentation.Choice> choices)", "噬风选项")
    ordered(storm, ["presentation.Dispose();", 'string opened = L10n.T("风眼张开了", "The eye of the storm opens");',
                    "session.Announce(opened, false);", "return opened;"],
            "风眼张开必须在面板收起后改走字幕")
    if re.search(r"presentation\.Dispose\(\);return L10n\.T\(", squash(world)):
        errors.append("又出现「面板收起之后直接 return 回执」的写法：回执会被 SetBodyText 静默吞掉")

    # ---- 4. 20 处见闻各有自己的标题与正文 ----
    keys = ["Search_" + c for c in "ABCDEFGH"] + ["Search_%s_02" % c for c in "ABCDEFGH"] + ["Search_S%d" % i for i in range(1, 5)]
    point_name = need_body(world, "internal static string PointName(string key)", "见闻标题")
    lore = need_body(world, "private static string Lore(string key)", "见闻正文")
    lore_texts = {}
    for key in keys:
        if point_name and 'case "%s":return L10n.T(' % key not in point_name:
            errors.append("PointName 缺 " + key + " 的专属标题（会落进 default「阅读群岛见闻」）")
        match = re.search(r'case "%s":return L10n\.T\("([^"]*)"' % re.escape(key), lore) if lore else None
        if match is None:
            errors.append("Lore 缺 " + key + " 的专属正文（会落进 default 那一段通用文案）")
        else:
            lore_texts[key] = match.group(1)
    seen = {}
    for key, text in lore_texts.items():
        if text in seen:
            errors.append("见闻 " + key + " 与 " + seen[text] + " 正文完全相同")
        seen[text] = key

    # ---- 5. 苔药冷却走游戏时间 ----
    heal = need_body(services, "internal string Heal()", "苔药")
    require(heal, "if (Time.time < healReadyAt)", "苔药冷却判断必须走游戏时间")
    require(heal, "healReadyAt = Time.time + HealCooldown;", "苔药冷却起点必须走游戏时间")
    forbid(heal, "unscaledTime", "苔药冷却又用回 unscaledTime：暂停菜单背后冷却照走")

    # ---- 6. 岛上落盘去抖 ----
    match = re.search(r"internal const float FlushDebounceSeconds\s*=\s*([0-9.]+)f;", service)
    if not match:
        errors.append("缺 FlushDebounceSeconds 常量")
    elif float(match.group(1)) != 30.0:
        errors.append("FlushDebounceSeconds 改成了 %s：去抖窗口是按「崩溃最多丢半分钟可重做事实」定的，改动要同步执行回归与文档" % match.group(1))
    tick_save = need_body(service, "internal void Tick(bool safeToFlush)", "剧情落盘 Tick")
    ordered(tick_save, ["if (!IsCurrentSlot || !safeToFlush) return;", "if (!TryRecoverFaultedStore()) return;",
                        "if (pendingSince < 0f) pendingSince = Time.unscaledTime;",
                        "if (urgentPending || coordinator.HasDeferredFlush || Time.unscaledTime - pendingSince >= FlushDebounceSeconds) coordinator.RequestFlush(out lastSaveError);",
                        "pendingSince = -1f;", "urgentPending = false;"],
            "剧情落盘 Tick 必须先过战斗门，再按「剧情动作 / 欠账重试 / 去抖到期」三选一落盘，写完才清去抖状态")
    if tick_save.count("coordinator.RequestFlush(") != 1:
        errors.append("剧情落盘 Tick 里 RequestFlush 必须恰好一处（去抖门之内）")
    for signature, urgent in (("internal bool TryApply(SkyIslandStoryAction action, out string message)", "MarkPending(true);"),
                              ("internal bool RecordEncounterCleared(string regionId)", "MarkPending(false);"),
                              ("internal bool RecordSearch(string marker, out string message)", "MarkPending(false);"),
                              ("internal bool RecordRegionVisited(string regionId)", "MarkPending(false);")):
        body = need_body(service, signature, "事实提交点")
        require(body, urgent, signature + " 接受事实后必须登记去抖状态（剧情动作登记为立即写，到访 / 清场 / 见闻登记为可去抖）")
    close = need_body(service, "internal bool TryClose()", "离岛落盘")
    require(close, "coordinator.TryFlushOnHostDestroy()", "离岛必须绕闸落盘")
    forbid(close, "FlushDebounceSeconds", "离岛落盘不得受去抖影响")
    forbid(close, "pendingSince", "离岛落盘不得受去抖影响")

    # ---- 7. 分段计时：事件上记、每帧路径上不记 ----
    log_timing = need_body(service, "internal void LogTiming(string ev, string id)", "分段计时")
    for token in ('Debug.Log("[SkyIsland] SKY_TIMING t="', "Time.realtimeSinceStartup - timingOrigin",
                  "System.Globalization.CultureInfo.InvariantCulture"):
        require(log_timing, token, "SKY_TIMING 行格式或时钟变了（清单与回填脚本按它 grep）")
    forbid(log_timing, "DevLog", "SKY_TIMING 不能走 DevLog：正式构建里会被整句剥掉")
    open_body = need_body(service, "internal void Open()", "进岛")
    ordered(open_body, ["opened = true;", "timingOrigin = Time.realtimeSinceStartup;", 'LogTiming("raid_open",'],
            "进岛必须先定计时起点再记第一行")
    require(close, 'LogTimingOnce("raid_close", null);', "离岛必须记一行计时（只记一次）")
    require(squash(body_of(world, "internal SkyIslandWorldStory(SkyIslandSession session, SkyIslandStoryService story, GameObject root)") or ""),
            'if (story != null) story.LogTiming("landed", null);', "落地时刻必须记一行计时")
    for text, label in ((tick_save, "剧情落盘 Tick"), (tick, "WorldStory.Tick"),
                        (squash(body_of(session, "private void Update()") or ""), "SkyIslandSession.Update")):
        forbid(text, "LogTiming", label + " 是每帧路径，不得记计时日志")

    # ---- 8. 折翎挑战按钮写明互斥 ----
    zheling = need_body(world, "private void ZhelingChoices(List<SkyIslandStoryPresentation.Choice> choices)", "折翎选项")
    require(zheling, 'choices.Add(Challenge(L10n.T("挑战旧航路守卫（战胜后不能再和解）", "Challenge the keeper of the old route (no reconciling once you win)"), "Zheling"));',
            "折翎挑战按钮必须写明战胜后不能再和解")

    if errors:
        for error in errors:
            print("  - " + error)
        print("SkyIslandPlaytimeFlowGuard: FAIL")
        raise SystemExit(1)
    print("SkyIslandPlaytimeFlowGuard: PASS (战斗回话 / 旧腰牌 / 挑战回执 / 20 处见闻 / 苔药时基 / 落盘去抖 / 分段计时 / 折翎互斥提示)")


if __name__ == "__main__":
    main()
