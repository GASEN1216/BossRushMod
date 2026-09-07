"""静态布局回归：容器边界、阅读区避让、逻辑像素与重复乘色。

从生产源码读取几何参数；不声称模拟了 Unity/TMP 排版。字体、滚动与实际观感需实机。
反向检查仅在内存中恢复旧的错误参数，确保断言确实能抓住本轮修复。
"""
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parent.parent
PATHS = [
    "Common/UI/BossRushUI.cs", "ZombieMode/ZombieModeUIHelper.cs",
    "PetNest/PetNestUI.cs", "ModeH/ModeHUI.cs", "ModeH/ModeHUIPages.cs",
    "Campaign/CampaignBoardView.cs", "Integration/BackMountain/ShowcaseUI.cs",
    "Integration/UI/ImageViewerUI.cs", "Achievement/SteamAchievementPopup.cs",
    "Achievement/AchievementView.cs", "Integration/Codex/CodexView.cs",
    "Common/Effects/RingParticleEffect.cs", "ModeH/ModeHRecoveryPanel.cs",
    "Integration/Bonus/FrostMistEffect.cs", "Integration/Affinity/AffinityUIManager.cs",
]
VECTOR = re.compile(r"new Vector2\(\s*(-?\d+(?:\.\d+)?)f?\s*,\s*(-?\d+(?:\.\d+)?)f?\s*\)")
COLOR = re.compile(
    r"Color (\w+) = new Color\("
    r"\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f")
FILLED = re.compile(r"(\w+)\.type = Image\.Type\.Filled;")
TINT_CHANNEL = re.compile(r"colors\.\w+Color = new Color\(\s*([\d.]+)f")
LIGHT_THRESHOLD = re.compile(r"LightBackgroundLuminance = ([\d.]+)f")

# 会被当作按钮底色用的设计 token，全部要离亮底阈值足够远。
BUTTON_TOKENS = ("Accent", "Success", "Warning", "Danger", "Surface", "SurfaceRaised",
                 "Disabled", "RarityLegendary")
# 阈值余量下限。低于这个值说明某个 token 已经挤到判定刀刃上，
# 下一次配色微调就会把标签静默从白翻黑。
TOKEN_MARGIN = 0.20


def vectors(text):
    return [tuple(map(float, m)) for m in VECTOR.findall(text)]


def to_linear(channel):
    """Unity Mathf.GammaToLinearSpace 的 sRGB 分段函数。"""
    if channel <= 0.04045:
        return channel / 12.92
    if channel < 1.0:
        return ((channel + 0.055) / 1.055) ** 2.4
    return channel ** 2.2


def relative_luminance(rgb):
    r, g, b = (to_linear(c) for c in rgb)
    return r * 0.2126 + g * 0.7152 + b * 0.0722


def filled_without_sprite(text):
    """Image.Type.Filled 且附近没给 sprite —— fillAmount 会被 Unity 完全忽略。"""
    missing = []
    for match in FILLED.finditer(text):
        name = match.group(1)
        window = text[max(0, match.start() - 800):match.start()]
        if name + ".sprite =" not in window:
            missing.append(name)
    return missing


def call(text, marker):
    start = text.index(marker)
    return text[start:text.index(";", start)]


def field_size(text, name):
    return vectors(call(text, name + " ="))[0]


def check(sources):
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    pet = sources["PetNest/PetNestUI.cs"]
    panel = field_size(pet, "PanelSize")
    content_pos, content_size = vectors(call(pet, '"Content",'))
    actions_pos, actions_size = vectors(call(pet, '"Actions",'))
    require(content_pos[1] - content_size[1] / 2 >= actions_pos[1] + actions_size[1] / 2 + 16,
            "PetNest 内容与操作区必须留出至少 16px 间隔")
    for name, pos, size in (("内容", content_pos, content_size), ("操作", actions_pos, actions_size)):
        require(abs(pos[1]) + size[1] / 2 <= panel[1] / 2 - 20,
                "PetNest " + name + "区超出面板安全边距")
    title_pos, title_size = vectors(call(pet, '"Title", parent,'))[-2:]
    require(abs(title_pos[0]) + title_size[0] / 2 <= panel[0] / 2 - 24,
            "PetNest 标题越过面板左边界")
    card = pet[pet.index("private void SpawnCard("):]
    card_width = field_size(pet, "CardSize")[0]
    for marker in ('"Title", card.transform', '"Subtitle", card.transform', '"Body", card.transform'):
        spec = vectors(call(card, marker))
        require(spec[0] == (0, 1) and spec[1] == (0, 1), "PetNest 卡片文字必须从左上向下排")
        pos, size = spec[-2:]
        require(pos[0] >= 20 and pos[0] + size[0] <= card_width - 216,
                "PetNest 卡片文字与右侧按钮列重叠")
    require("BossRushUI.MeasureTextHeight(body" in pet and "86f + bodyHeight + 18f" in pet,
            "PetNest 长正文必须扩展卡片高度，不能缩小或截断")
    require("_tabs[page] = tab" in pet and "pair.Key == _page" in pet, "PetNest 必须显示当前页签")

    hud = sources["ModeH/ModeHUI.cs"]
    _, status_height = field_size(hud, "StatusSize")
    rows = re.findall(r'CreateHudLine\(status.transform, "\w+", (-?[\d.]+)f,', hud)
    require(len(rows) == 3, "Mode H 三条状态行必须完整")
    offsets = sorted(float(v) for v in rows)
    require(all(abs(y) + 22 <= status_height / 2 - 12 for y in offsets), "Mode H 状态行超出背景")
    require(all(b - a >= 52 for a, b in zip(offsets, offsets[1:])), "Mode H 状态行间距不足")
    require('"TimerText", 0f, TimerSize.x - 32f' in hud, "计时文字必须按计时背景宽度排版")
    require('size, BossRushUIColors.Accent, createBackdrop: false)' in hud,
            "Mode H 页面不能在既有 Backdrop 上再叠一层遮罩")

    pages = sources["ModeH/ModeHUIPages.cs"]
    sections = []
    for name in ("Title", "Subtitle", "Body"):
        text = call(pages, 'CreateCardText(card.transform, "' + name + '"')
        match = re.search(r"cardWidth, (-?[\d.]+)f, ([\d.]+)f", text)
        require(match is not None, "Mode H 卡片各文本区必须声明独立高度")
        if match:
            top, height = map(float, match.groups())
            sections.append((top - height, top))
    require(all(-84 <= bottom < top <= 134 for bottom, top in sections), "Mode H 卡片文字越过标题/动作边界")
    ordered = sorted(sections)
    require(all(b[0] - a[1] >= 4 for a, b in zip(ordered, ordered[1:])), "Mode H 卡片文本区重叠")
    require(pages.count("GetActionBandReserve(panelSize, content)") >= 3,
            "卡片、战报、配装列表都必须避让换行后的动作区")
    require("BossRushUI.MeasureTextHeight(text" in pages, "战报长行应换行增高并滚动")

    for path in ("Campaign/CampaignBoardView.cs", "Integration/BackMountain/ShowcaseUI.cs"):
        anchors = vectors(call(sources[path], '"Header", parent,'))[:2]
        require(anchors == [(0, 1), (1, 1)], path + " 标题背景必须完整横跨面板，不能只锚在右半边")
    board = sources["Campaign/CampaignBoardView.cs"]
    require("BossRushUI.MeasureTextHeight(detail" in board and "scroll.content" in board,
            "征程目标必须按文字高度排入滚动区")

    helper = sources["ZombieMode/ZombieModeUIHelper.cs"]
    lib = sources["Common/UI/BossRushUI.cs"]
    require("scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;" in helper,
            "参考画布必须在两轴都容纳布局")
    require("graphic.color = Color.white;" in helper, "绝对按钮配色前必须清除 Graphic 重复乘色")
    require("label.rectTransform.anchorMax = Vector2.one;" in helper, "按钮文字必须随点击区拉伸")
    require("scroll.verticalScrollbar = scrollbar;" in lib and "hitArea.raycastTarget = true;" in lib,
            "滚动列表必须支持拖动滑块及空白处滚轮")
    for path in ("Achievement/AchievementView.cs", "Integration/Codex/CodexView.cs"):
        require("GetReferenceViewportSize()" in sources[path], path + " 不得按物理像素重复缩放")
    viewer = sources["Integration/UI/ImageViewerUI.cs"]
    require("Screen.width * IMAGE_MAX_SCALE" not in viewer and "Screen.height * IMAGE_MAX_SCALE" not in viewer,
            "图片查看器不得把物理像素直接写进缩放画布")
    require("imgRect.offsetMin = new Vector2(0f, 110f);" in viewer
            and "imgRect.offsetMax = new Vector2(0f, -110f);" in viewer,
            "图片必须避让标题和关闭提示，并在分辨率变化时保持锚定")
    require("Loading image..." not in viewer, "已失败的图片加载不得继续提示加载中")
    popup = sources["Achievement/SteamAchievementPopup.cs"]
    require("TextOverflowModes.Overflow" not in popup, "成就弹窗长名称/说明不得画到面板外")
    fx = sources["Common/Effects/RingParticleEffect.cs"]
    require("new GradientColorKey(tint" not in fx, "共享雾效生命周期颜色不得再次乘 tint")
    require("new GradientAlphaKey(0f, 0f)" in fx, "雾效应从透明进入，避免出生即亮斑")
    # alpha 只能施加一次：startColor 给强度，梯度只描述形状。两边都乘会得到 alpha²，
    # 常量含义失真，调参变成平方响应。
    require("new GradientAlphaKey(alpha" not in fx, "雾效 alpha 不得在梯度里再乘一次")
    require("main.startColor = new Color(tint.r, tint.g, tint.b, alpha);" in fx,
            "雾效强度必须由 startColor 一次性给足")

    # Image.Type.Filled 必须配 sprite，否则 Image 退回整块矩形、fillAmount 失效。
    for path, text in sources.items():
        for name in filled_without_sprite(text):
            errors.append(path + " 的 " + name + " 用了 Image.Type.Filled 却没赋 sprite，fillAmount 会失效")

    # 按钮三态不得依赖大于 1 的中性乘色：CanvasRenderer 的 tint 是 32 位色，>1 会被夹回白，
    # 悬停与常态完全一致。底色一律走 ColorBlock 绝对色。
    for value in TINT_CHANNEL.findall(helper):
        require(float(value) <= 1.0, "按钮 ColorBlock 不得使用大于 1 的乘色做悬停")
    require("ApplyButtonColors(button, backgroundColor," in helper,
            "CreateButton 必须与 ApplyButtonColors 走同一套绝对配色")
    require("Mathf.Max(0f, (size.x - textSize.x) * 0.5f)" in helper,
            "按钮内边距不得压到调用方给的 textSize 以下")
    require("internal static void SetButtonBaseColor(" in helper,
            "改按钮底色必须有共享入口，不能各自写 Image.color")
    require("scaler.matchWidthOrHeight" not in helper,
            "Expand 模式下 matchWidthOrHeight 是死参数，不要再赋值")
    for path, label in (("PetNest/PetNestUI.cs", "遗种巢页签"), ("ModeH/ModeHUI.cs", "Mode H 拍铃")):
        require("SetButtonBaseColor(" in sources[path],
                label + "改底色必须走 SetButtonBaseColor，直接写 Image.color 会和 ColorTint 相乘")

    # 亮底阈值与设计 token 的余量：任一 token 贴到阈值上，配色微调就会静默翻转标签。
    threshold = LIGHT_THRESHOLD.search(lib)
    require(threshold is not None, "共享库必须显式声明亮底判定阈值")
    if threshold:
        limit = float(threshold.group(1))
        tokens = dict((m[0], relative_luminance([float(v) for v in m[1:]]))
                      for m in COLOR.findall(lib))
        for name in BUTTON_TOKENS:
            require(name in tokens, "缺少按钮底色 token " + name)
            if name not in tokens:
                continue
            margin = abs(tokens[name] - limit) / limit
            require(margin >= TOKEN_MARGIN,
                    "按钮底色 " + name + " 的相对亮度贴着亮底阈值（余量 "
                    + format(margin, ".1%") + "），标签会随配色微调静默翻转")

    recovery = sources["ModeH/ModeHRecoveryPanel.cs"]
    require("createBackdrop: false" in recovery,
            "Mode H 恢复壳的根上已有 Backdrop，不能再叠第二层")
    require(pages.count("ConfigureScrollRect") >= 1 and "ConfigureScrollRect(scrollRect)" in pages,
            "押品格滚动区必须接入共享滚轮/滑块设置")
    require("BossRushUIColors.BackdropStrong" in viewer,
            "全屏看图要用强遮罩 token，不能沿用面板级 0.62")
    return errors


def main():
    sources = {p: (ROOT / p).read_text(encoding="utf-8-sig") for p in PATHS}
    errors = check(sources)
    probes = [
        ("PetNest/PetNestUI.cs", "new Vector2(0f, 14f), new Vector2(1120f, 412f)",
         "new Vector2(0f, -20f), new Vector2(1120f, 520f)"),
        ("ModeH/ModeHUI.cs", '"Enemies", -64f', '"Enemies", -104f'),
        ("ModeH/ModeHUIPages.cs", "cardWidth, 130f, 34f", "cardWidth, 122f, 96f"),
        ("Integration/BackMountain/ShowcaseUI.cs", '"Header", parent, new Vector2(0f, 1f)',
         '"Header", parent, new Vector2(0.5f, 1f)'),
        ("ZombieMode/ZombieModeUIHelper.cs", "if (graphic != null) graphic.color = Color.white;", ""),
        ("Common/Effects/RingParticleEffect.cs", "new GradientColorKey(Color.white, 0f)",
         "new GradientColorKey(tint, 0f)"),
        ("ModeH/ModeHRecoveryPanel.cs", "BossRushUIColors.Warning, createBackdrop: false)",
         "BossRushUIColors.Warning)"),
        ("Integration/Affinity/AffinityUIManager.cs",
         "progressBar.sprite = BossRushUI.GetSolidSprite();", ""),
        ("ZombieMode/ZombieModeUIHelper.cs", "Mathf.Max(0f, (size.x - textSize.x) * 0.5f)",
         "Mathf.Max(8f, (size.x - textSize.x) * 0.5f)"),
        ("Common/UI/BossRushUI.cs", "LightBackgroundLuminance = 0.30f",
         "LightBackgroundLuminance = 0.18f"),
        ("Common/Effects/RingParticleEffect.cs", "new GradientAlphaKey(1f, 0.18f)",
         "new GradientAlphaKey(alpha, 0.18f)"),
    ]
    for path, before, after in probes:
        if before not in sources[path]:
            errors.append("反向检查锚点失效：" + path)
            continue
        altered = dict(sources)
        altered[path] = altered[path].replace(before, after, 1)
        if not check(altered):
            errors.append("未拦截旧布局错误：" + path)
    if errors:
        print("UILayoutReadabilityGuard: FAIL\n  " + "\n  ".join(errors))
        return 1
    print("UILayoutReadabilityGuard: PASS（几何边界 + "
          + str(len(probes)) + " 个内存反向检查；非 Unity 实机）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
