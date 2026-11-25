#!/usr/bin/env python3
"""BossRushInkThemeGuard — 水墨主题接管共享 UI 库（2026-11-25 owner「项目已有的 UI……都改成出征令这种 UI」）的接线不变式。

全项目的界面都经过共享库的几个入口，所以水墨主题只接管入口、调用方一行不改；这里锁住那几处接管点：
1. 总开关 BossRushInkTheme.Active = true。
2. 底图：BossRushUISkin.GetInjected 先取水墨底图，取到就返回（图集只兜底水墨不管的档）。
3. 描边：BossRushUI.ApplyPanelStroke 先走 BossRushInkTheme.TryApplyFrame（墨笔边），水墨不管的档才画圆角描边环。
4. 质感：ApplyButtonDepth 在挂投影 / 斜面之前先按按钮角色挂墨笔边；ApplyDepth 在水墨主题下不挂投影与斜面（塑料感来源）。
5. 危险按钮：ApplyButtonColors 先把 Danger 实色底换成 DangerFill（墨底朱边浅朱字），再写 ColorBlock。
6. 弹窗：CreateModalSurface 在水墨主题下不画左侧强调竖条与顶边细条（压在墨笔边上）。
7. 打开动效：慢而沉、不回弹——缩放从 0.95 以上缓出到 1，时长 0.35–0.6 秒；背后的墨晕到时自毁，
   墨笔标题的金笔随后写出（WriteRevealStrokes），重播从记下的整笔宽度写起（PlayBrushWrite 的 FullWidth）。
8. 缓存：BossRushInkSkin.ResetStaticCaches 先清主题的 Sprite 壳；主题的每个 Sprite 壳字段都在 ResetStaticCaches 里销毁。
9. 标题金笔 AddTitleStroke 幂等、不参与布局、随打开动效写出。
10. 色板一致：BossRushUIColors 的同名语义 token 与 BossRushInkColors 同色（RGB 相同）。

反向检查：逐条在内存里破坏生产源码，守卫必须转红。观感（墨笔边粗细、动效节奏）靠实机清单。
"""
import io
import os
import re
import sys

from cs_source_util import clean_source

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FILES = {
    "theme": "Common/UI/BossRushInkTheme.cs",
    "ink": "Common/UI/BossRushInkSkin.cs",
    "ui": "Common/UI/BossRushUI.cs",
    "feel": "Common/UI/BossRushUIFeel.cs",
    "helper": "ZombieMode/ZombieModeUIHelper.cs",
    "anim": "Common/UI/BossRushUIAnimation.cs",
}
TOKEN_PAIRS = (
    ("Surface", "Surface"), ("SurfaceRaised", "SurfaceRaised"), ("TextPrimary", "Paper"),
    ("TextSecondary", "PaperDim"), ("Accent", "Gold"), ("AccentFill", "CinnabarFill"),
    ("Danger", "Cinnabar"), ("DangerText", "CinnabarText"),
)
COLOR = re.compile(r"internal static readonly Color (\w+) = new Color\(\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f")


def load():
    sources = {}
    for key, rel in FILES.items():
        path = os.path.join(ROOT, *rel.split("/"))
        with io.open(path, "r", encoding="utf-8-sig", errors="ignore") as fh:
            sources[key] = fh.read()
    return sources


def compact(text):
    return re.sub(r"\s+", "", text or "")


def body_of(source, signature):
    at = source.find(signature)
    if at < 0 or source.find(signature, at + 1) >= 0:
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


def class_colors(source, name):
    at = source.find("class " + name)
    if at < 0:
        return {}
    body = body_of(source[at:], "class " + name) or ""
    return dict((m.group(1), tuple(float(v) for v in m.groups()[1:])) for m in COLOR.finditer(body))


def const_float(source, name):
    match = re.search(r"const float " + name + r" = ([\d.]+)f;", source)
    return float(match.group(1)) if match else None


def check(raw):
    src = dict((k, clean_source(v)) for k, v in raw.items())
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    theme, ink, ui, feel, helper, anim = (src[k] for k in ("theme", "ink", "ui", "feel", "helper", "anim"))

    # 1. 总开关
    require("internal static readonly bool Active = true;" in theme, "[开关] BossRushInkTheme.Active 必须为 true（全项目统一水墨主题）")

    # 2. 底图
    injected = compact(body_of(ui, "internal static Sprite GetInjected(BossRushUISkinPart part)"))
    require(injected.startswith("Spriteink=BossRushInkTheme.GetSurface(part);if(ink!=null)returnink;"),
            "[底图] GetInjected 必须先取水墨底图，取到就返回")

    # 3. 描边
    stroke = compact(body_of(ui, "internal static Image ApplyPanelStroke(Image surface, int radius, BossRushUISkinPart part, Color color)"))
    require("stroke.sprite=BossRushInkTheme.TryApplyFrame(stroke,radius,part)?stroke.sprite:GetStrokeSprite(GetSkinCornerRadius(radius,part));" in stroke,
            "[描边] ApplyPanelStroke 必须先走墨笔边，水墨不管的档才画圆角描边环")

    # 4. 质感
    depth = compact(body_of(feel, "internal static void ApplyButtonDepth(Button button, Color baseColor)"))
    frame_at = depth.find("if(BossRushInkTheme.TryApplyButtonFrame(image,part,baseColor))return;")
    require(0 <= frame_at < depth.find("ApplyDepth(image,"),
            "[质感] ApplyButtonDepth 必须在挂投影 / 斜面之前按按钮角色挂墨笔边")
    apply_depth = compact(body_of(feel, "private static void ApplyDepth(Image surface, int radius, BossRushUISkinPart part, float surfaceAlpha)"))
    require(apply_depth.startswith("if(BossRushInkTheme.Active)return;"), "[质感] 水墨主题下 ApplyDepth 不挂投影与斜面")

    # 5. 危险按钮
    colors = compact(body_of(helper, "internal static void ApplyButtonColors("))
    danger_at = colors.find("if(BossRushInkTheme.IsDangerBase(normalColor)){normalColor=BossRushInkTheme.DangerFill;")
    require(0 <= danger_at < colors.find("colors.normalColor=normalColor;"),
            "[危险] ApplyButtonColors 必须先把 Danger 实色底换成墨底 DangerFill 再写 ColorBlock")
    require("internal static bool IsDangerBase(Color color)" in theme and "color.a > 0.95f" in theme,
            "[危险] 只换实色 Danger 底（幽灵关闭键那种半透明 Danger 不换）")

    # 6. 弹窗
    modal = compact(body_of(helper, "internal static GameObject CreateModalSurface("))
    require("BossRushUI.ApplyFramedPanelSkin(surfaceImage,14,BossRushUISkinPart.Panel);if(BossRushInkTheme.Active){returnsurface;}" in modal,
            "[弹窗] 水墨主题下 CreateModalSurface 不画强调竖条与顶边细条")

    # 7. 打开动效
    restart = compact(body_of(anim, "internal void Restart()"))
    require("if(scales){transform.localScale=Vector3.one*(BossRushInkTheme.Active?InkStartScale:StartScale);"
            "BossRushInkTheme.SpawnOpenBloom(transformasRectTransform);"
            "BossRushInkTheme.WriteRevealStrokes(transformasRectTransform);}" in restart,
            "[动效] 打开时按水墨起始缩放、洇开墨晕并写出墨笔标题")
    update = compact(body_of(anim[anim.find("class BossRushUIOpenAnimation"):], "private void Update()"))
    require("(ink?InkDurationSeconds:DurationSeconds)" in update and "(ink?InkFadeSeconds:FadeSeconds)" in update
            and "BossRushUI.EaseOut(t)" in update, "[动效] 水墨时长与缓出曲线必须接进每帧更新")
    duration, fade, start = (const_float(anim, n) for n in ("InkDurationSeconds", "InkFadeSeconds", "InkStartScale"))
    require(duration is not None and 0.35 <= duration <= 0.6 and fade is not None and fade <= duration,
            "[动效] 水墨打开要慢而沉：0.35–0.6 秒，淡入不长于缩放")
    require(start is not None and 0.95 <= start < 1.0, "[动效] 水墨打开从 0.95 以上缓出到 1（不回弹、不大幅缩放）")
    bloom = compact(body_of(theme, "internal static void SpawnOpenBloom(RectTransform panel)"))
    require("if(size.x<240f||size.y<160f)return;" in bloom and "Object.Destroy(bloomObject)" in bloom,
            "[动效] 墨晕只给大面板，到时自毁、不留在屏幕上")
    write = compact(body_of(theme, "internal static void WriteRevealStrokes(RectTransform panel)"))
    require("GetComponentsInChildren<BossRushInkRevealStroke>(true)" in write and "BossRushInkSkin.PlayBrushWrite(" in write,
            "[动效] WriteRevealStrokes 写出面板里所有墨笔标题金笔")
    brush = compact(body_of(ink, "internal static void PlayBrushWrite(RectTransform strokeMask, float delay, float seconds, BossRushInkMotion motion = null)"))
    require("if(marker.FullWidth<0f)marker.FullWidth=full;elsefull=marker.FullWidth;" in brush,
            "[动效] 金笔重播必须从第一次记下的整笔宽度写起（半途重开不会越写越短）")

    # 8. 缓存
    reset_ink = compact(body_of(ink, "internal static void ResetStaticCaches()"))
    require(reset_ink.startswith("BossRushInkTheme.ResetStaticCaches();"), "[缓存] BossRushInkSkin.ResetStaticCaches 必须先清主题的 Sprite 壳")
    reset_theme = compact(body_of(theme, "internal static void ResetStaticCaches()"))
    shells = re.findall(r"private static Sprite (\w+);", theme)
    require(shells and all("DestroyVariant(ref" + name + ");" in reset_theme for name in shells),
            "[缓存] 主题的每个 Sprite 壳字段都要在 ResetStaticCaches 里销毁")

    # 9. 标题金笔
    title = compact(body_of(theme, "internal static float AddTitleStroke(TextMeshProUGUI title)"))
    require("if(title.transform.Find(TitleStrokeName)!=null)return0f;" in title and "ignoreLayout=true;" in title
            and "AddComponent<BossRushInkRevealStroke>()" in title, "[标题] AddTitleStroke 幂等、不参与布局、随打开动效写出")

    # 10. 色板一致
    ui_colors = class_colors(ui, "BossRushUIColors")
    ink_colors = class_colors(ink, "BossRushInkColors")
    for ui_name, ink_name in TOKEN_PAIRS:
        a, b = ui_colors.get(ui_name), ink_colors.get(ink_name)
        require(a is not None and b is not None and all(abs(x - y) < 1e-6 for x, y in zip(a, b)),
                "[色板] BossRushUIColors." + ui_name + " 与 BossRushInkColors." + ink_name + " 必须同色")
    return errors


PROBES = (
    ("theme", "internal static readonly bool Active = true;", "internal static readonly bool Active = false;"),
    ("ui", "            if (ink != null) return ink;\n", ""),
    ("ui", "BossRushInkTheme.TryApplyFrame(stroke, radius, part) ? stroke.sprite : ", ""),
    ("feel", "            if (BossRushInkTheme.TryApplyButtonFrame(image, part, baseColor)) return;\n", ""),
    ("feel", "            if (BossRushInkTheme.Active) return;\n            Profile profile;", "            Profile profile;"),
    ("helper", "if (BossRushInkTheme.IsDangerBase(normalColor))", "if (BossRushInkTheme.IsDangerBase(highlightedColor))"),
    ("helper", "            if (BossRushInkTheme.Active)\n            {\n                return surface;",
     "            if (false)\n            {\n                return surface;"),
    ("anim", "                BossRushInkTheme.WriteRevealStrokes(transform as RectTransform);\n", ""),
    ("anim", "InkDurationSeconds = 0.42f;", "InkDurationSeconds = 0.12f;"),
    ("anim", "InkStartScale = 0.97f;", "InkStartScale = 0.80f;"),
    ("theme", "() => { if (bloomObject != null) Object.Destroy(bloomObject); }", "() => { if (bloomObject != null) bloomObject.SetActive(true); }"),
    ("theme", "            DestroyVariant(ref buttonFrame);\n", ""),
    ("ink", "            BossRushInkTheme.ResetStaticCaches();\n", ""),
    ("ink", "                else full = marker.FullWidth;\n", ""),
    ("theme", "            stroke.gameObject.AddComponent<BossRushInkRevealStroke>();\n", ""),
    ("ui", "internal static readonly Color Accent = new Color(0.788f,", "internal static readonly Color Accent = new Color(0.200f,"),
)


def main():
    raw = load()
    errors = check(raw)
    for key, before, after in PROBES:
        text = raw[key].replace("\r\n", "\n")
        if before not in text:
            errors.append("反向检查锚点失效：" + FILES[key] + " ← " + before.strip()[:60])
            continue
        altered = dict(raw)
        altered[key] = text.replace(before, after, 1)
        if not check(altered):
            errors.append("反向检查未转红：" + FILES[key] + " ← " + before.strip()[:60])
    if errors:
        print("BossRushInkThemeGuard: FAIL")
        for line in errors:
            print("  - " + line)
        return 1
    print("BossRushInkThemeGuard: PASS（10 类接管点 + " + str(len(PROBES)) + " 个内存反向检查；观感靠实机）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
