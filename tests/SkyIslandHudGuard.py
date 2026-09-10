"""天空岛局内指引（HUD / 字幕 / 撤离读条 / 世界提示字 / 剧情面板操作）的版式与接线纪律。

owner 2026-09-10 的原话：「不要直接在玩家屏幕中上方写字，实在是太像网游了，不要有这种廉价感，
要参考主流游戏做的指引与高级感」，随后又要求「全面审核一遍确保没问题」。旧版复用试验场的
`ArenaPrototypeControls.CreateHud`——屏幕正中偏上一块 920×90 的裸文字，常驻四行长句，而且实测装不下。

这份守卫把改完之后的纪律钉住，免得哪天顺手又改回去：

1. 会话不得再用试验场的 `CreateHud`（那就是屏幕正上方的裸文字）。
2. 区域大标题必须在**中线偏下**（`AreaTitleY` 为负），不在屏幕中上方。
3. 常驻卡片贴**右**边缘（锚点 x=1），左上角留给随机事件徽章与波次提示。
4. 区域大标题必须垫压暗底，而且压暗底必须是**二维**柔边（只做竖向渐变的话左右是硬边）。
5. 存档状态正常时不说话：只在 `!story.CanWrite` 时常驻一行，且绝不走一次性字幕。
6. HUD 文本不得用 Overflow（固定框里超出的行会画到框外）。
7. 跟随官方 HUD 显隐：照抄 `HUDManager.ShouldDisplay` 的条件，且会话在各处提前 return 之前驱动 HUD。
8. 区域大标题一趟一区只出一次、落地提示只跟第一次；地名切换有到访半径做滞回。
9. 对外提示只走一个出口：岛上就绪后走字幕，否则才交给 report，二者互斥。
10. 撤离读条复用官方 `EvacuationCountdownUI`，代理组件必须禁用（不参与判定），返航与清理时收起。
11. 剧情面板开着时隐藏官方 HUD，令牌在 Dispose 里成对注销。
12. 剧情面板支持手柄（官方 UIInputManager），订阅与退订成对。
13. 世界提示字走近才浮现，不再用警示黄常亮。
14. 清场提示不得把遭遇的内部 id 念给玩家。

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
    bat = (ROOT / "compile_official.bat").read_text(encoding="utf-8", errors="ignore")
    errors = []

    def need_body(source, signature, label):
        body = body_of(source, signature)
        if body is None:
            errors.append("找不到 " + label + "（" + signature + "）")
            return ""
        return body

    # ---- 0. 进编译清单 ----
    if "DebugAndTools" + chr(92) + "SkyIsland" + chr(92) + "SkyIslandHud.cs" not in bat:
        errors.append("编译清单缺少 SkyIslandHud.cs")

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
            errors.append("常驻卡片的锚点/轴心不在右上角（左上角留给随机事件徽章与波次提示）")

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

    # ---- 7. 跟随官方 HUD 显隐 ----
    hidden = need_body(hud, "private static bool OfficialHudHidden()", "官方 HUD 显隐判定")
    for token in ("View.ActiveView != null", "DialogueUI.Active", "CustomFaceUI.ActiveView != null",
                  "CameraMode.Active"):
        if hidden and token not in hidden:
            errors.append("HUD 显隐没有照抄官方 HUDManager.ShouldDisplay 的条件：缺 " + token)
    tick = need_body(hud, "internal void Tick(float unscaledDelta, bool suppressed)", "HUD 每帧驱动")
    if tick and "OfficialHudHidden()" not in tick:
        errors.append("HUD 的 Tick 没有按官方 HUD 显隐让位：会浮在官方地图与背包上面")
    if tick and "suppressed" not in tick.split("OfficialHudHidden()", 1)[0]:
        errors.append("HUD 的 Tick 没有把会话侧的隐藏条件并进去")
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

    # ---- 8. 区域大标题一趟一区只出一次，落地提示只跟第一次 ----
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
        if "sqrMagnitude <" not in statement_before(session, pos):
            errors.append("区域名切换没有到访半径门控：站在两个地标中垂线附近会每半秒翻一次")

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

    # ---- 11. 剧情面板隐藏官方 HUD，令牌成对 ----
    if "HUDManager.RegisterHideToken(" not in panel:
        errors.append("剧情面板没有隐藏官方 HUD（官方对话界面同口径）")
    dispose = need_body(panel, "public void Dispose()", "剧情面板 Dispose")
    if dispose and "HUDManager.UnregisterHideToken(hideToken)" not in dispose:
        errors.append("剧情面板 Dispose 没有注销 HUD 隐藏令牌：官方 HUD 会一直不回来")

    # ---- 12. 剧情面板支持手柄，订阅成对 ----
    subscribe = need_body(panel, "private void SubscribeInput()", "剧情面板订阅 UI 输入")
    unsubscribe = need_body(panel, "private void UnsubscribeInput()", "剧情面板退订 UI 输入")
    for event_name in ("OnNavigate", "OnConfirm", "OnCancel"):
        if subscribe and "UIInputManager." + event_name + " +=" not in subscribe:
            errors.append("剧情面板没有订阅官方 " + event_name + "：手柄玩家打开面板后既选不了也关不掉")
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

    if errors:
        for error in errors:
            print("  - " + error)
        print("SkyIslandHudGuard: FAIL")
        raise SystemExit(1)
    print("SkyIslandHudGuard: PASS (14 条：不回退正上方裸文字 / 标题与字幕在中线下 / 卡片贴右 / 二维柔边 / "
          "存档静默 / 无 Overflow / 跟随官方 HUD 显隐 / 一区一标题 / 单一出口 / 官方撤离读条 / "
          "面板隐藏官方 HUD / 手柄 / 走近才浮现 / 不念内部 id)")


if __name__ == "__main__":
    main()
