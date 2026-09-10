"""天空岛局内 HUD 的版式纪律。

owner 2026-09-10 的原话：「不要直接在玩家屏幕中上方写字，实在是太像网游了，不要有这种廉价感，
要参考主流游戏做的指引与高级感」。旧版复用试验场的 `ArenaPrototypeControls.CreateHud`——
屏幕正中偏上一块 920×90 的裸文字，常驻四行长句，而且实测装不下（中文 4 行 / 英文 6 行）。

这份守卫把改完之后的几条纪律钉住，免得哪天顺手又改回那种写法：

1. 会话不得再用试验场的 `CreateHud`（那就是屏幕正上方的裸文字）。
2. 区域大标题必须在**中线偏下**（`AreaTitleY` 为负），不在屏幕中上方。
3. 常驻卡片贴**右**边缘（锚点 x=1），左上角留给随机事件徽章与波次提示。
4. 区域大标题必须垫压暗底，而且压暗底必须是**二维**柔边（只做竖向渐变的话左右是硬边）。
5. 存档状态正常时不说话：不得再把 `SaveStatus` 无条件常驻进 HUD。
6. HUD 文本不得用 Overflow（固定框里超出的行会画到框外）。

守卫钉的是结构，不是观感：它证明不了卡片好不好看，只证明上面几条纪律还在。
观感只能实机看，见 docs/天空岛_待人工验证清单.md。
"""
import re
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
NEWLINE = chr(10)


def main():
    def read(path):
        return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))

    session = read("DebugAndTools/SkyIsland/SkyIslandSession.cs")
    hud = read("DebugAndTools/SkyIsland/SkyIslandHud.cs")
    art = read("DebugAndTools/SkyIsland/SkyIslandUiArt.cs")
    bat = (ROOT / "compile_official.bat").read_text(encoding="utf-8", errors="ignore")
    errors = []

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
    scrim = art.split("internal static Sprite GetTitleScrim()", 1)
    if len(scrim) != 2:
        errors.append("找不到压暗底生成函数")
    else:
        body = scrim[1].split(NEWLINE + "        }", 1)[0]
        wm = re.search(r"const int width\s*=\s*(\d+)", body)
        if not wm or int(wm.group(1)) <= 1:
            errors.append("压暗底宽度只有 1 列：只有竖向渐变，左右两侧会是笔直硬边")
        if "horizontal" not in body or "vertical" not in body:
            errors.append("压暗底必须是横向 × 竖向的二维柔边")

    # ---- 5. 存档状态正常时不说话 ----
    if re.search(r"\+\s*story\.SaveStatus\s*;", session):
        errors.append("存档状态又被无条件拼进 HUD 常驻文本（正常时应当一个字都不说）")
    # 必须**逐个调用点**检查门控，不能在整份文件里找子串：`!story.CanWrite` 在会话别处
    # （BeginStoryChallenge）也出现，整文件判断会被它骗过——反向验证第 7 条就是这样漏的。
    announces = [m.start() for m in re.finditer(r"hud\.Announce\(\s*story\.SaveStatus\s*\)", session)]
    if not announces:
        errors.append("找不到存档状态的提示调用点")
    for pos in announces:
        statement_start = max(session.rfind(";", 0, pos), session.rfind("{", 0, pos),
                              session.rfind("}", 0, pos))
        if "!story.CanWrite" not in session[statement_start + 1:pos]:
            errors.append("存档状态提示没有在同一条语句里按 !story.CanWrite 门控"
                          "（正常时应当一个字都不说）")

    # ---- 6. HUD 文本不用 Overflow ----
    if "TextOverflowModes.Overflow" in hud:
        errors.append("HUD 文本用了 Overflow：固定框里超出的行会直接画到卡片外")
    if "TextOverflowModes.Ellipsis" not in hud:
        errors.append("HUD 文本缺少省略号兜底")

    if errors:
        for error in errors:
            print("  - " + error)
        print("SkyIslandHudGuard: FAIL")
        raise SystemExit(1)
    print("SkyIslandHudGuard: PASS (不回退正上方裸文字 / 标题在中线偏下 / 卡片贴右 / 二维柔边压暗 / 存档静默 / 无 Overflow)")


if __name__ == "__main__":
    main()
