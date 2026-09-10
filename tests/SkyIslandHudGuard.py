"""天空岛局内指引（HUD / 字幕 / 撤离读条 / 世界提示字 / 剧情面板操作）的版式与接线纪律。

owner 2026-09-10 的原话：「不要直接在玩家屏幕中上方写字，实在是太像网游了，不要有这种廉价感，
要参考主流游戏做的指引与高级感」，随后又要求「全面审核一遍确保没问题」。旧版复用试验场的
`ArenaPrototypeControls.CreateHud`——屏幕正中偏上一块 920×90 的裸文字，常驻四行长句，而且实测装不下。

这份守卫把改完之后的纪律钉住，免得哪天顺手又改回去：

1. 会话不得再用试验场的 `CreateHud`（那就是屏幕正上方的裸文字）。
2. 区域大标题必须在**中线偏下**（`AreaTitleY` 为负），不在屏幕中上方。
3. 常驻卡片贴**右**边缘（锚点 x=1），左上角留给官方时间显示与随机事件徽章。
4. 区域大标题必须垫压暗底，而且压暗底必须是**二维**柔边（只做竖向渐变的话左右是硬边）。
5. 存档状态正常时不说话：只在 `!story.CanWrite` 时常驻一行，且绝不走一次性字幕。
6. HUD 文本不得用 Overflow（固定框里超出的行会画到框外）。
7. 跟随官方 HUD 显隐：条件收在 `BossRushUI.IsOfficialHudHidden`（照抄 `HUDManager.ShouldDisplay`），
   SkyIslandHud 与 CampaignHud 共用一份；暂停菜单开着时 HUD 不推进；会话在各处提前 return 之前驱动 HUD。
8. 区域大标题一趟一区只出一次、落地提示只跟第一次；区域名只来自「脚下那块地」。
9. 对外提示只走一个出口：岛上就绪后走字幕，否则才交给 report，二者互斥。
10. 撤离读条复用官方 `EvacuationCountdownUI`，代理组件必须禁用（不参与判定），返航与清理时收起；
    离开撤离圈时 HUD 文字读秒当场撤掉。
11. 剧情面板开着时隐藏官方 HUD，令牌在 Dispose 里按**引用**成对注销。
12. 剧情面板支持键盘导航（官方 `UIInputManager`：W/S、Enter、Esc），订阅与退订成对。
    官方输入资产只有键鼠方案、没有手柄绑定，别再把它写成「原生手柄支持」。
13. 世界提示字走近才浮现，不再用警示黄常亮。
14. 清场提示不得把遭遇的内部 id 念给玩家。

2026-09-10 全方位审核追加：

15. 临场提示分级：Boss 机制警示插队、打断正在播的普通字幕、队满不先丢它（规则只写在 SkyIslandCaptionQueue，
    由隔离回归 tests/fixtures/SkyIslandHudPolicy 执行）。
16. 中下方纵向避让按常量复算：字幕底边在官方交互读条（ActionProgress_Slider）之上，
    区域大标题的操作提示行（含上浮起点）在字幕上沿之上。
17. 右上角卡片排在官方「操作说明」提示栈（IndicatorHUD）的实际下沿之下，CampaignHud 同口径。
18. 玩家倒下、开始切图时立刻收起官方撤离读条（原版 CountDownArea 在人倒下时同样中止读条）。
19. 剧情面板导航按边沿走一步（官方 UI_Navigate 一按派发两条同向事件），选项回执引起的重开保留当前项。
20. 世界提示字不得每帧量距离、不得按 1% 阈值反复重建 TMP 网格（AGENTS 4.12）。

凡是「某句代码必须在某个方法里」的检查，一律先切出方法体再找，不在整份文件里找子串：
同一句话常常在别处也出现（比如 `extractionCountdown.Hide()` 在 Update 与 Close 里各有一处），
整文件判断会被另一处骗过——上一版守卫的第 7 条反向验证就是这样漏的。

守卫钉的是结构，不是观感：它证明不了卡片好不好看，只证明上面几条纪律还在。
观感只能实机看，见 docs/制作教程/天空岛/天空岛_待人工验证清单.md。
"""
import re
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
NEWLINE = chr(10)
BACKSLASH = chr(92)

# 本库 HUD 画布是 1920×1080 Expand：比 16:9 更宽的屏幕画布高度仍是 1080，更窄的（16:10、4:3）画布更高。
# 所以「距底边」最紧的情形就是 1080 高，屏幕中心到底边 540 个单位。
CANVAS_HALF_HEIGHT = 540.0
# 官方底部 HUD 堆叠的最高点：交互读条 ActionProgress_Slider 的顶边，折成本库画布单位。
# UnityPy 读 resources.assets 的 LevelManager/HUDCanvas 实测（官方 2560×1440 参考 × 0.75）。
# 两边都按短边缩放，这个换算与屏幕比例无关。
MEASURED_OFFICIAL_BOTTOM_STACK_TOP = 243.0


def body_of(source, signature):
    """切出以 signature 开头的方法体（到同缩进的收尾大括号为止）。找不到返回 None。"""
    start = source.find(signature)
    if start < 0:
        return None
    open_brace = source.find("{", start)
    if open_brace < 0:
        return None
    depth = 0
    for i in range(open_brace, len(source)):
        ch = source[i]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return source[open_brace + 1:i]
    return None


def statement_before(source, pos):
    """pos 所在语句从开头到 pos 的文本（上一个 ; { } 之后）。"""
    start = max(source.rfind(";", 0, pos), source.rfind("{", 0, pos), source.rfind("}", 0, pos))
    return source[start + 1:pos]


def statement_from(source, pos):
    """pos 起到本语句结束（第一个 ;）的文本。"""
    end = source.find(";", pos)
    return source[pos:end if end >= 0 else len(source)]


def line_with(source, token):
    """source 里第一处含 token 的整行；没有返回空串。"""
    return next((line for line in source.splitlines() if token in line), "")


def main():
    def read(path):
        return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))

    session = read("DebugAndTools/SkyIsland/SkyIslandSession.cs")
    hud = read("DebugAndTools/SkyIsland/SkyIslandHud.cs")
    art = read("DebugAndTools/SkyIsland/SkyIslandUiArt.cs")
    ring = read("DebugAndTools/SkyIsland/SkyIslandGroundRing.cs")
    panel = read("DebugAndTools/SkyIsland/SkyIslandStoryPresentation.cs")
    module = read("DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs")
    encounters = read("DebugAndTools/SkyIsland/SkyIslandEncounters.cs")
    storm = read("DebugAndTools/SkyIsland/SkyIslandStormBoss.cs")
    caption_queue = read("DebugAndTools/SkyIsland/SkyIslandCaptionQueue.cs")
    shared_ui = read("Common/UI/BossRushUI.cs")
    campaign = read("Campaign/CampaignHud.cs")
    regressions = (ROOT / "tools/run_runtime_regressions.py").read_text(encoding="utf-8")
    bat = (ROOT / "compile_official.bat").read_text(encoding="utf-8", errors="ignore")
    errors = []

    def need_body(source, signature, label):
        body = body_of(source, signature)
        if body is None:
            errors.append("找不到 " + label + "（" + signature + "）")
            return ""
        return body

    # ---- 0. 进编译清单 ----
    for name in ("SkyIslandHud.cs", "SkyIslandCaptionQueue.cs"):
        if "DebugAndTools" + BACKSLASH + "SkyIsland" + BACKSLASH + name not in bat:
            errors.append("编译清单缺少 " + name)

    # ---- 1. 不得回退到试验场那块屏幕正上方的裸文字 ----
    if "ArenaPrototypeControls.CreateHud" in session:
        errors.append("会话又用回了 ArenaPrototypeControls.CreateHud（屏幕正上方的裸文字）")
    if "new SkyIslandHud(" not in session:
        errors.append("会话没有创建 SkyIslandHud")
    if re.search(r"\bhud\.text\s*=", session):
        errors.append("会话仍在直接往 hud.text 里塞整段文本（那就是旧的常驻四行写法）")

    # ---- 2. 区域大标题在中线偏下 ----
    m = re.search(r"AreaTitleY\s*=\s*(-?[0-9.]+)f", hud)
    if not m:
        errors.append("找不到区域大标题的 y 常量 AreaTitleY")
    elif float(m.group(1)) >= 0:
        errors.append("区域大标题 AreaTitleY=%s 不在中线偏下：屏幕中上方正是要避开的位置" % m.group(1))
    if "new Vector2(0f, AreaTitleY)" not in hud:
        errors.append("区域大标题没有真的使用 AreaTitleY（常量在、调用点却写了字面量）")
    m = re.search(r"CaptionY\s*=\s*(-?[0-9.]+)f", hud)
    if not m or float(m.group(1)) >= 0:
        errors.append("字幕必须在中线下方（CaptionY 为负），不得回到屏幕上半部")

    # ---- 3. 常驻卡片贴右边缘 ----
    card = hud.split('ZombieModeUIHelper.CreateRect("SkyIslandTracker"', 1)
    if len(card) != 2:
        errors.append("找不到常驻卡片 SkyIslandTracker 的建造点")
    else:
        head = card[1].split(";", 1)[0]
        if head.count("new Vector2(1f, 1f)") < 3:
            errors.append("常驻卡片的锚点/轴心不在右上角（左上角是官方时间显示与随机事件徽章）")

    # ---- 4. 区域大标题必须垫二维柔边压暗底 ----
    if "SkyIslandUiArt.GetTitleScrim()" not in hud:
        errors.append("区域大标题没有垫压暗底：岛上抬头是高亮云海，浅色字直接压上去读不出来")
    scrim = body_of(art, "internal static Sprite GetTitleScrim()")
    if scrim is None:
        errors.append("找不到压暗底生成函数")
    else:
        wm = re.search(r"const int width\s*=\s*(\d+)", scrim)
        if not wm or int(wm.group(1)) <= 1:
            errors.append("压暗底宽度只有 1 列：只有竖向渐变，左右两侧会是笔直硬边")
        if "horizontal" not in scrim or "vertical" not in scrim:
            errors.append("压暗底必须是横向 × 竖向的二维柔边")

    # ---- 5. 存档状态正常时不说话，且不走一次性字幕 ----
    if re.search(r"\+\s*story\.SaveStatus\s*;", session):
        errors.append("存档状态又被无条件拼进 HUD 常驻文本（正常时应当一个字都不说）")
    if re.search(r"Caption\(\s*story\.SaveStatus", session):
        errors.append("存档状态不得走一次性字幕：每半秒重播会挤掉 Boss 机制提示与战斗门控原因")
    if "hud.Announce(" in session:
        errors.append("HUD 已没有 Announce：临场提示走 Caption，持续问题走 SetStatus")
    status_calls = [mm.start() for mm in re.finditer(r"hud\.SetStatus\(", session)]
    save_calls = [pos for pos in status_calls if "story.SaveStatus" in statement_from(session, pos)]
    if not save_calls:
        errors.append("找不到存档状态的常驻状态行调用点（hud.SetStatus(... story.SaveStatus ...)）")
    for pos in save_calls:
        # 门控可以写在 if 里，也可以写在参数的三元里：两段都算同一条语句。
        if "!story.CanWrite" not in statement_before(session, pos) + statement_from(session, pos):
            errors.append("存档状态行没有在同一条语句里按 !story.CanWrite 门控（正常时应当一个字都不说）")

    # ---- 6. HUD 文本不用 Overflow ----
    if "TextOverflowModes.Overflow" in hud:
        errors.append("HUD 文本用了 Overflow：固定框里超出的行会直接画到卡片外")
    if "TextOverflowModes.Ellipsis" not in hud:
        errors.append("HUD 文本缺少省略号兜底")

    # ---- 7. 跟随官方 HUD 显隐（判定收在 BossRushUI，两块自绘 HUD 共用）----
    # 各抄一份迟早漂移：2026-09-10 审核时 CampaignHud 就是完全没跟随的那一份，
    # 它在 HudOverlay（1200）上，压在 sortingOrder 100 的官方背包、地图与对话上面。
    hidden = need_body(shared_ui, "internal static bool IsOfficialHudHidden()", "官方 HUD 显隐判定")
    for token in ("View.ActiveView != null", "DialogueUI.Active", "CustomFaceUI.ActiveView != null",
                  "CameraMode.Active"):
        if hidden and token not in hidden:
            errors.append("HUD 显隐没有照抄官方 HUDManager.ShouldDisplay 的条件：缺 " + token)
    if re.search(r"\bbool\s+OfficialHudHidden\s*\(", hud):
        errors.append("SkyIslandHud 又私抄了一份官方 HUD 显隐判定：改用 BossRushUI.IsOfficialHudHidden，两份条件迟早漂移")
    tick = need_body(hud, "internal void Tick(float unscaledDelta, bool suppressed)", "HUD 每帧驱动")
    hidden_at = tick.find("BossRushUI.IsOfficialHudHidden()")
    if tick and hidden_at < 0:
        errors.append("HUD 的 Tick 没有按官方 HUD 显隐让位：会浮在官方地图与背包上面")
    elif tick and "suppressed" not in statement_before(tick, hidden_at):
        errors.append("HUD 的 Tick 没有把会话侧的隐藏条件并进同一个判定")
    # 暂停菜单（GameManager.Paused）不是 View、不让官方 HUD 隐藏，但它的画布 sortingOrder 10000 整个盖在上面；
    # 本 HUD 的淡变走 unscaled 时间，不停下来的话一条 Boss 机制提示会在暂停菜单背后播完。
    paused = need_body(shared_ui, "internal static bool IsGamePaused()", "官方暂停判定")
    if paused and "GameManager.Paused" not in paused:
        errors.append("暂停判定没有读官方 GameManager.Paused")
    pause_line = line_with(tick, "BossRushUI.IsGamePaused()")
    if tick and ("hidden" not in pause_line or "return;" not in pause_line):
        errors.append("HUD 的 Tick 没有在隐藏或暂停时提前 return：开着地图或暂停菜单时大标题与字幕会在背后播完")
    elif tick:
        gate_at = tick.find(pause_line)
        for step in ("TickBanner(", "TickCaption("):
            at = tick.find(step)
            if at < 0 or at < gate_at:
                errors.append("隐藏/暂停的提前 return 必须排在 " + step + " 之前")
    update = need_body(session, "private void Update()", "会话 Update")
    drive = update.find("hud.Tick(Time.unscaledDeltaTime, HudSuppressed())")
    early = update.find("if (returnRequested)")
    if drive < 0:
        errors.append("会话没有把 HudSuppressed() 传给 HUD")
    elif early < 0 or drive > early:
        errors.append("HUD 必须在 returnRequested / !ready 的提前 return 之前驱动，否则返航黑幕上仍挂着卡片")
    suppressed = need_body(session, "private bool HudSuppressed()", "会话侧 HUD 隐藏条件")
    for token in ("!ready", "returnRequested", "deathPending", "worldStory.Visible"):
        if suppressed and token not in suppressed:
            errors.append("会话侧 HUD 隐藏条件缺 " + token)
    campaign_tick = need_body(campaign, "internal static void Tick()", "契约追踪条每帧驱动")
    visible_at = campaign_tick.find("bool visible = !BossRushUI.IsOfficialHudHidden();")
    if campaign_tick and visible_at < 0:
        errors.append("CampaignHud 没有跟随官方 HUD 显隐：HudOverlay（1200）会压在 sortingOrder 100 的背包、地图与对话上面")
    elif campaign_tick and "_canvas.enabled = visible;" not in campaign_tick[visible_at:]:
        errors.append("CampaignHud 算出了官方 HUD 显隐却没有真的开关画布")

    # ---- 8. 区域大标题一趟一区只出一次；区域名只来自脚下那块地 ----
    banner = need_body(hud, "private void TickBanner(float delta)", "区域大标题驱动")
    gate = banner.find("titled.Add(next)")
    start = banner.find("StartBanner(next)")
    if gate < 0 or start < 0 or gate > start:
        errors.append("区域大标题必须先过 titled.Add 再 StartBanner：同一个区域一趟只出一次")
    if banner and "BannerMinGap" not in banner:
        errors.append("区域大标题缺最小间隔：连着走过两个地标会连弹两次")
    start_body = need_body(hud, "private void StartBanner(string title)", "区域大标题开播")
    if start_body and "landingHint = null;" not in start_body:
        errors.append("落地提示没有在第一次大标题之后清空：之后每个区域都会再念一遍操作说明")
    region_calls = [mm.start() for mm in re.finditer(r"hud\.SetRegion\(", session)]
    if not region_calls:
        errors.append("会话没有写区域名")
    for pos in region_calls:
        statement = statement_before(session, pos) + statement_from(session, pos)
        if "standingRegion != null" not in statement or "RegionLabel(standingRegion)" not in statement:
            errors.append("区域名必须来自脚下那块地（standingRegion）：按离哪个地标近切换，走在两个地标之间会来回翻，"
                          "隔着桥还会提前显示对岸（见 tests/SkyIslandRegionResolutionPropertyTest.py）")
    writes = [mm.start() for mm in re.finditer(r"\bstandingRegion\s*=(?![=>])", session)]
    if len(writes) != 1:
        errors.append("standingRegion 必须只有一个写入点（地面射线命中的碰撞体查表），实际 %d 处" % len(writes))
    elif "groundRegions.TryGetValue(hit.collider, out region)" not in statement_before(session, writes[0]):
        errors.append("standingRegion 只能由地面射线命中的碰撞体查 groundRegions 得到")

    # ---- 9. 对外提示只走一个出口 ----
    status = need_body(session, "private void Status(string message, bool error)", "会话提示出口")
    if status:
        if "hud.Caption(message, error)" not in status:
            errors.append("岛上就绪后的提示必须走 HUD 字幕")
        if "else if (report != null) report(message, error);" not in status:
            errors.append("字幕与 report 必须互斥：两处同时出现就是同一句话说两遍，官方 toast 又只停 1.2 秒")

    # ---- 10. 撤离读条复用官方控件 ----
    for token, why in (("new SkyIslandExtractionCountdown(", "会话没有创建官方撤离读条桥"),
                       ("extractionCountdown.Show(", "撤离读秒没有优先交给官方读条")):
        if token not in session:
            errors.append(why)
    countdown = need_body(ring, "internal SkyIslandExtractionCountdown(Transform parent, float holdSeconds)",
                          "官方撤离读条桥构造")
    if countdown and "area.enabled = false;" not in countdown:
        errors.append("代理 CountDownArea 必须禁用：否则它会按官方口径自己计时甚至自己判成功")
    show = need_body(ring, "internal bool Show(float heldSeconds)", "官方撤离读条驱动")
    if show and "EvacuationCountdownUI.Request(" not in show:
        errors.append("官方撤离读条没有 Request")
    hide = need_body(ring, "internal void Hide()", "官方撤离读条收起")
    if hide and "EvacuationCountdownUI.Release(" not in hide:
        errors.append("官方撤离读条没有 Release")
    close = need_body(session, "internal void Close(bool restorePlayer, string reason)", "会话 Close")
    if close and "extractionCountdown.Hide()" not in close:
        errors.append("返航派发时必须收起官方读条，否则它停在 00:00 一直挂到黑幕落下")
    cleanup = need_body(session, "private void Cleanup(string reason)", "会话 Cleanup")
    if cleanup and "extractionCountdown.Dispose()" not in cleanup:
        errors.append("会话清理必须销毁官方读条桥")
    # 官方读条不可用时读秒退回卡片里的文字行：离开圈子那一刻当场撤掉，不等下一次 0.5 秒刷新。
    leave = re.search(r"if \(extractionHeld >= 0 && hud != null\) hud\.SetExtraction\(null\);", update)
    reset = update.find("extractionHeld = -1;")
    if not leave or reset < 0 or leave.start() > reset:
        errors.append("离开撤离圈时 HUD 文字读秒没有当场撤掉（要在清零 extractionHeld 之前判断刚才是否在读条）")

    # ---- 11. 剧情面板隐藏官方 HUD，令牌按引用成对注销 ----
    if "HUDManager.RegisterHideToken(" not in panel:
        errors.append("剧情面板没有隐藏官方 HUD（官方对话界面同口径）")
    dispose = need_body(panel, "public void Dispose()", "剧情面板 Dispose")
    unregister = dispose.find("HUDManager.UnregisterHideToken(hideToken)")
    if dispose and unregister < 0:
        errors.append("剧情面板 Dispose 没有注销 HUD 隐藏令牌：官方 HUD 会一直不回来")
    elif dispose and "!ReferenceEquals(hideToken, null)" not in dispose[:unregister]:
        errors.append("隐藏令牌必须按引用判空再注销：画布随场景卸载先被销毁时 Unity 的 != null 为假，令牌会漏注销")
    panel_show = need_body(panel, "internal void Show(string title, string text, IList<Choice> choices,", "剧情面板打开")
    if panel_show and "HUDManager.UnregisterHideToken(previousHideToken)" in panel_show \
            and "!ReferenceEquals(previousHideToken, null)" not in panel_show:
        errors.append("重开面板时旧令牌必须按引用判空再注销")
    if re.search(r"hidetoken\s*!=\s*null", (dispose + panel_show).lower()):
        errors.append("隐藏令牌不得用 Unity 的 != null 判空：已销毁的令牌会被当成空跳过注销")

    # ---- 12. 剧情面板支持键盘导航，订阅成对 ----
    subscribe = need_body(panel, "private void SubscribeInput()", "剧情面板订阅 UI 输入")
    unsubscribe = need_body(panel, "private void UnsubscribeInput()", "剧情面板退订 UI 输入")
    for event_name in ("OnNavigate", "OnConfirm", "OnCancel"):
        if subscribe and "UIInputManager." + event_name + " +=" not in subscribe:
            errors.append("剧情面板没有订阅官方 " + event_name + "：键盘玩家（W/S、Enter、Esc）打开面板后既选不了也关不掉")
        if unsubscribe and "UIInputManager." + event_name + " -=" not in unsubscribe:
            errors.append("剧情面板没有退订官方 " + event_name + "：面板关掉后静态事件仍挂着它")
    if dispose and "UnsubscribeInput()" not in dispose:
        errors.append("剧情面板 Dispose 没有退订 UI 输入")

    # ---- 13. 世界提示字走近才浮现，不用警示黄常亮 ----
    memorial = need_body(panel, "internal static GameObject Create(", "纪念物建造")
    sign = need_body(module, "private void CreateSign(Transform boat)", "船点招牌建造")
    for label, body in (("纪念物提示字", memorial), ("船点招牌", sign)):
        if not body:
            continue
        if "SkyIslandProximityLabel.Attach(" not in body:
            errors.append(label + "没有走近才浮现：常驻浮空字就是网游式头顶标语")
        if "BossRushUIColors.WarningText" in body:
            errors.append(label + "又用回了警示黄：它不是警告")

    # ---- 14. 清场提示不念内部 id ----
    # 名字由会话经构造参数注入：遭遇 owner 不认识地标，也不反向依赖会话
    # （它的回归夹具里根本没有会话类，反向依赖一加上，夹具就编不过）。
    if re.search(r"\+\s*encounter\.Id\s*,\s*(true|false)\s*\)", encounters):
        errors.append("清场提示把遭遇内部 id（C_02 / S1）直接念给了玩家")
    if "describe(encounter.Id)" not in encounters:
        errors.append("清场提示没有经过会话注入的 describe 转成地标名或对手名")
    if "SkyIslandSession." in encounters:
        errors.append("遭遇 owner 不得反向依赖 SkyIslandSession：会话事实一律经构造参数注入")
    construct = session.find("new SkyIslandEncounters(")
    if construct < 0 or "EncounterLabel" not in statement_from(session, construct):
        errors.append("会话创建遭遇 owner 时没有把 EncounterLabel 注入进去")

    # ---- 15. 临场提示分级：Boss 机制警示插队，队满不先丢它 ----
    # 噬风说完相位台词 1.4 秒后圈内吃满伤害。旧写法一律排队、队满丢最旧：一条「航路已清理」正在播时，
    # 「离开它脚下的那一圈」要等上一条停满再淡出，玩家读到时第一波已经炸完；再来三条普通字幕它还会被挤掉。
    caption = need_body(hud, "internal void Caption(string value, bool warning)", "HUD 字幕入口")
    if caption and "captions.Admit(value, warning," not in caption:
        errors.append("字幕入口没有走 SkyIslandCaptionQueue 的分级规则（去重 / 警示插队 / 队满先丢普通字幕）")
    if caption and "captionCutAge = captionAge;" not in caption:
        errors.append("警示到来时没有打断正在播的普通字幕：噬风的预警窗口只有 1.4 秒")
    if re.search(r"\bQueue\s*<", hud):
        errors.append("SkyIslandHud 又自己维护了一条字幕队列：排队规则只许写在 SkyIslandCaptionQueue（隔离回归执行的那一份）")
    admit = need_body(caption_queue,
                      "internal Admission Admit(string text, bool warning, string showing, bool showingWarning, out bool preempt)",
                      "字幕分级规则")
    if admit and "FirstNormalIndex()" not in admit:
        errors.append("队满时必须先丢普通字幕，不得按到达顺序丢最旧的（那会把 Boss 机制提示挤掉）")
    if '"SkyIslandHudPolicy"' not in regressions or not (ROOT / "tests/fixtures/SkyIslandHudPolicy/Program.cs").exists():
        errors.append("字幕分级规则的隔离回归 SkyIslandHudPolicy 没有登记（tools/run_runtime_regressions.py）")
    phase = need_body(storm, "private void EnterPhase()", "噬风相位切换")
    callouts = re.findall(r'Announce\("[^"]*",\s*"[^"]*",\s*(true|false)\)', phase)
    if phase and (len(callouts) != 3 or any(flag != "true" for flag in callouts)):
        errors.append("噬风的三条相位台词是机制提示，必须走警示通道（Announce 第三个参数为 true），实际 %r" % callouts)
    storm_announce = need_body(storm, "private void Announce(string cn, string en, bool urgent)", "噬风提示出口")
    if storm_announce and "report(L10n.T(cn, en), urgent)" not in storm_announce:
        errors.append("噬风提示出口没有把 urgent 传给字幕的 warning")

    # ---- 16. 中下方纵向避让（按常量复算）----
    consts = {}
    for name in ("CaptionY", "CaptionMaxHeight", "OfficialBottomStackTop", "AreaTitleY", "AreaTitleRise",
                 "BannerHintY", "BannerHintHeight"):
        cm = re.search(r"const float " + name + r"\s*=\s*(-?[0-9.]+)f\s*;", hud)
        if cm:
            consts[name] = float(cm.group(1))
        else:
            errors.append("找不到版式常量 " + name)
    if len(consts) == 7:
        if consts["OfficialBottomStackTop"] < MEASURED_OFFICIAL_BOTTOM_STACK_TOP:
            errors.append("OfficialBottomStackTop=%.0f 低于实测的官方交互读条顶边 %.0f"
                          % (consts["OfficialBottomStackTop"], MEASURED_OFFICIAL_BOTTOM_STACK_TOP))
        caption_bottom = CANVAS_HALF_HEIGHT + consts["CaptionY"]
        if caption_bottom < consts["OfficialBottomStackTop"]:
            errors.append("字幕底边距底 %.0f，压到官方底部堆叠（顶边 %.0f，搜箱开门时的交互读条）上"
                          % (caption_bottom, consts["OfficialBottomStackTop"]))
        caption_top = consts["CaptionY"] + consts["CaptionMaxHeight"]
        hint_lowest = (consts["AreaTitleY"] - consts["AreaTitleRise"] + consts["BannerHintY"]
                       - consts["BannerHintHeight"] / 2.0)
        if hint_lowest <= caption_top:
            errors.append("区域大标题的操作提示行最低处 y=%.0f 压到字幕上沿 y=%.0f（两者会同时出现）"
                          % (hint_lowest, caption_top))
    caption_root = hud.split('ZombieModeUIHelper.CreateRect("SkyIslandCaption"', 1)
    if len(caption_root) != 2:
        errors.append("找不到字幕根 SkyIslandCaption 的建造点")
    elif not re.search(r"new Vector2\(0f, CaptionY\),\s*new Vector2\(CaptionWidth, [0-9.]+f\),\s*"
                       r"new Vector2\(0\.5f, 0f\)\)\s*$", caption_root[1].split(";", 1)[0]):
        errors.append("字幕根必须以 CaptionY 定位、轴心在底边（向上长）：轴心在中心时两行字幕会往下压住交互读条")
    start_caption = need_body(hud, "private void StartCaption(string text, bool warning)", "字幕开播")
    if start_caption and "CaptionMaxHeight" not in start_caption:
        errors.append("字幕高度没有封顶两行：再长的一句会往上顶进区域大标题")
    if "new Vector2(0f, AreaTitleY - AreaTitleRise)" not in hud or "* AreaTitleRise" not in banner:
        errors.append("大标题的上浮不再以 AreaTitleRise 为幅度：守卫的纵向避让复算失效")
    if "BannerHintY, BannerHintHeight)" not in hud:
        errors.append("提示行没有使用 BannerHintY / BannerHintHeight 常量：守卫的纵向避让复算失效")

    # ---- 17. 右上角卡片排在官方「操作说明」提示栈下面 ----
    anchor = need_body(hud, "private void ApplyCardAnchor()", "卡片避让官方提示栈")
    if anchor and "BossRushUI.GetTopRightHudTop(canvas)" not in anchor:
        errors.append("卡片顶边没有排在官方右上角提示栈下面：玩家展开 11 行按键提示后卡片被整块盖住")
    # Tick 里有两处 ApplyCardAnchor：定时重排与契约武装切换。只找子串的话删掉定时那一处照样绿，
    # 而契约状态不变时卡片就再也不跟官方提示栈走了。必须钉定时块本身。
    if tick and not re.search(r"layoutTimer = CardLayoutInterval;\s*ApplyCardAnchor\(\);", tick):
        errors.append("HUD 的 Tick 没有按 CardLayoutInterval 定时重排卡片顶边（官方提示栈随玩家展开/收起而变高变矮）")
    top = need_body(shared_ui, "internal static float GetTopRightHudTop(Canvas canvas)", "官方右上角提示栈下沿")
    if top:
        for token in ("FindObjectOfType<global::IndicatorHUD>()", "GetWorldCorners(", "canvas.scaleFactor",
                      "TopRightHudTopMin"):
            if token not in top:
                errors.append("官方提示栈下沿的换算缺 " + token)
        throttle = top.find("Time.unscaledTime < nextIndicatorSearch")
        search = top.find("FindObjectOfType<")
        if throttle < 0 or search < 0 or throttle > search:
            errors.append("找官方提示栈必须节流：FindObjectOfType 是全场景遍历，丢了实例时每帧找一遍就是每帧轮询（AGENTS 4.12）")
    if campaign_tick and "BossRushUI.GetTopRightHudTop(_canvas)" not in campaign_tick:
        errors.append("CampaignHud 的顶边没有排在官方提示栈下面（与 SkyIslandHud 同口径）")
    campaign_reset = need_body(campaign, "internal static void ResetStaticCaches()", "契约追踪条静态缓存复位")
    if campaign_reset and "_nextAnchorCheck = 0f;" not in campaign_reset:
        errors.append("CampaignHud 的静态节流计时没有在 ResetStaticCaches 里复位")
    shared_reset = need_body(shared_ui, "public static void ResetStaticCaches()", "BossRushUI 静态缓存复位")
    if shared_reset and ("officialIndicator = null;" not in shared_reset or "nextIndicatorSearch = 0f;" not in shared_reset):
        errors.append("BossRushUI 缓存的官方提示栈实例与找实例节流没有在 ResetStaticCaches 里放掉")

    # ---- 18. 倒下、开始切图时收起官方撤离读条 ----
    for signature, label in (("private void OnPlayerDied(DamageInfo damage)", "玩家倒下"),
                             ("private void OnStartedLoading(SceneLoadingContext context)", "开始切图")):
        body = need_body(session, signature, label + "回调")
        if body and "extractionCountdown.Hide()" not in body:
            errors.append(label + "时没有收起官方撤离读条：ready 落下后 Update 不再走撤离分支，读条会留在屏幕上自己读到 00:00")

    # ---- 19. 剧情面板：导航按边沿走一步，重开保留当前项 ----
    navigate = need_body(panel, "private void OnNavigate(global::UIInputEventData data)", "剧情面板导航")
    if navigate:
        held = navigate.find("if (navigateHeld == step) return;")
        select = navigate.find("Select(")
        if held < 0 or select < 0 or held > select:
            errors.append("导航必须按边沿走一步：官方 UI_Navigate 按一下会派发 started 与 performed 两条同向事件，逐条走一步就是一按跳两格")
        if "navigateHeld = 0;" not in navigate:
            errors.append("导航回到中位时没有重新武装：松开再按会走不动")
    if dispose and "navigateHeld = 0;" not in dispose:
        errors.append("剧情面板 Dispose 没有复位导航按住状态")
    if panel_show and ("int previousSelected = reopening ? selected : -1;" not in panel_show
                       or "if (previousSelected >= 0) Select(previousSelected);" not in panel_show):
        errors.append("选项回执引起的重开丢了键盘当前项：连着按 Enter 的玩家每次都被打回第一项")

    # ---- 20. 世界提示字不每帧量距离、不反复重建 TMP ----
    late = need_body(hud, "private void LateUpdate()", "世界提示字每帧驱动")
    if late:
        wait = late.find("Time.unscaledTime < nextCheck")
        measure = late.find("Vector3.Distance(")
        if wait < 0 or measure < 0 or wait > measure:
            errors.append("世界提示字完全隐形且离得远时仍每帧量距离（AGENTS 4.12）：量距离之前必须先按 nextCheck 推迟")
        # 量化与写入门是两件事：只找 AlphaStep 子串的话，删掉量化、只留下 AlphaStep * 0.5f 的写入门照样绿。
        if not re.search(r"alpha = Mathf\.Round\([^;]*/ AlphaStep\) \* AlphaStep;", late):
            errors.append("世界提示字的透明度没有按 AlphaStep 量化：TMP 的 alpha 每改一次就重建整块网格")
        if "< AlphaStep * 0.5f" not in late:
            errors.append("世界提示字的写入门没有按量化档位判断：同一档内仍会反复重建")
        if "0.01f" in late:
            errors.append("世界提示字又用回 1% 阈值：走过一次淡变带要重建上百次 TMP 网格")

    if errors:
        for error in errors:
            print("  - " + error)
        print("SkyIslandHudGuard: FAIL")
        raise SystemExit(1)
    print("SkyIslandHudGuard: PASS (20 条：不回退正上方裸文字 / 标题与字幕在中线下 / 卡片贴右 / 二维柔边 / "
          "存档静默 / 无 Overflow / 跟随官方 HUD 显隐与暂停 / 一区一标题且区域来自脚下 / 单一出口 / 官方撤离读条 / "
          "令牌按引用注销 / 键盘导航 / 走近才浮现 / 不念内部 id / 警示插队 / 纵向避让复算 / 避让官方提示栈 / "
          "倒下与切图收读条 / 边沿导航与保留当前项 / 提示字不每帧轮询)")


if __name__ == "__main__":
    main()
