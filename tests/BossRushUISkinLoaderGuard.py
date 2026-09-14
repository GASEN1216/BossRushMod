# -*- coding: utf-8 -*-
"""Guard: UI 图集换皮加载器的 fail-open 契约、两个时序陷阱，以及六张图的分档接线。

规格见 docs/制作教程/BossRushUI_图集规格.md。守这几件事：

  1. **fail-open**。bundle 缺失/加载失败一律不注入，程序化圆角皮肤继续工作。
     这与 Mode G 展示资源的 fail-closed 完全相反：那里缺资源就该拒绝进入，
     而 UI 底图缺失只是观感降级，不该挡住玩法。加载器里不许出现 fail-closed 味道的早退。

  2. **注入必须早于任何面板创建**。ApplyPanelSkin 只在创建 Image 时赋一次 sprite，
     已建好的面板不会追溯换图。因此注入点挂在 InitializeAlwaysOnRuntime（Awake 阶段），
     不能挪到某个界面第一次打开时。

  3. **Cleanup 必须先把每一个注入点复位，再 Unload(true)**。顺序反了、或漏掉任何一档，
     BossRushUISkin 里留着的就是已销毁的 Sprite 引用，之后每个面板都会贴一张空图（白板）。
     漏一档最阴：只有走那一档的控件白板，其余正常，现场很难定位。

  4. **分档必须完整**（CR-2026-09-13-001）。bundle 里六张图，改之前只有「面板 / 按钮」两档：
     - `panel_raised` / `divider` / `scroll_handle` 三张躺在包里从来取不出来；
     - 3px 的强调竖条落进 32×32 / border 10 的按钮图，Unity 的 GetAdjustedBorders 把
       10+10 等比压进 3px 并把中心区归零，画出来是按钮圆角的一道糊痕——**比程序化皮肤还差**。
     所以这里钉住：枚举六档齐全、Auto 的落位口径、以及天空岛那几个细元素确实传了显式档。
     `button_hover` 刻意不注入（按钮三态走 ColorBlock 绝对色，再换底图会和乘色打架），
     这一条写在加载器注释里，不在此断言。

另外不提供 raw PNG fallback 是刻意的：运行时 LoadImage 出来的散图没有九宫格 border，
拉伸会糊，却让人误以为素材已生效。

反向检查在内存里恢复旧的错误写法，确保每条断言真的抓得住。
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]

LOADER = "Common/UI/BossRushUISkinLoader.cs"
SKIN = "Common/UI/BossRushUI.cs"
ALWAYS_ON = "Utilities/AlwaysOnRuntimeHooks.cs"
MOD_BEHAVIOUR = "ModBehaviour.cs"
HUD = "DebugAndTools/SkyIsland/SkyIslandHud.cs"
PANEL = "DebugAndTools/SkyIsland/SkyIslandStoryPresentation.cs"

PATHS = [LOADER, SKIN, ALWAYS_ON, MOD_BEHAVIOUR, HUD, PANEL]

# bundle 里必须被取出来喂给注入点的五张（第六张 button_hover 见文件头）。
SKIN_ASSETS = ("panel_surface", "panel_raised", "button_normal", "divider", "scroll_handle")
# 每一档的注入点都必须在 Cleanup 里复位。
INJECTORS = ("InjectPanelSprite", "InjectRaisedSprite", "InjectButtonSprite",
             "InjectRuleSprite", "InjectScrollHandleSprite")
# 分档枚举必须齐全：少一档就等于那张图又取不出来了。
SKIN_PARTS = ("Auto", "Hairline", "Rule", "Button", "Card", "Panel", "ScrollHandle")


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def extract_method(text, signature):
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for i in range(brace, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    return ""


def extract_all(text, signature):
    """同名重载全部取出来：只看第一个的话，后面的重载可以随便写。"""
    bodies = []
    at = 0
    while True:
        start = text.find(signature, at)
        if start < 0:
            return bodies
        body = extract_method(text[start:], signature)
        if not body:
            return bodies
        bodies.append(body)
        at = start + max(1, len(body))


def check(sources):
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    loader = strip_comments(sources[LOADER])
    skin = strip_comments(sources[SKIN])
    always_on = strip_comments(sources[ALWAYS_ON])
    mod_behaviour = strip_comments(sources[MOD_BEHAVIOUR])
    hud = strip_comments(sources[HUD])
    panel = strip_comments(sources[PANEL])

    # ---- 1) 注入点仍然存在且 ApplyPanelSkin 每次现查（调用方零改动的前提）----
    for token in ("InjectPanelSprite", "InjectButtonSprite", "GetPanelSprite", "GetButtonSprite"):
        require(token in skin, SKIN + " 缺少皮肤注入点 " + token)

    overloads = extract_all(skin, "internal static void ApplyPanelSkin(")
    require(len(overloads) >= 1, SKIN + " 找不到 ApplyPanelSkin 方法体")
    # 每一个重载要么自己现查 BossRushUISkin，要么转发给另一个重载。缓存 Sprite 会让注入
    # 只对之后创建的面板生效，「调用方零改动」的承诺就断了；而只检查第一个重载的话，
    # 把实现挪到第二个重载里就能绕过去。
    for index, body in enumerate(overloads):
        require("BossRushUISkin.Get" in body or "ApplyPanelSkin(image, radius" in body,
                SKIN + " 的第 %d 个 ApplyPanelSkin 重载既不现查 BossRushUISkin 也不转发。"
                       "一旦改成缓存 Sprite，注入就只对之后创建的面板生效。" % (index + 1))
    require(any("BossRushUISkin.Get" in body for body in overloads),
            SKIN + " 所有 ApplyPanelSkin 重载都只在转发，没有一个真的现查 BossRushUISkin")

    # ---- 2) fail-open ----
    ensure = extract_method(loader, "internal static void EnsureInjected()")
    require(bool(ensure), LOADER + " 找不到 EnsureInjected 方法体")
    if ensure:
        require("throw" not in ensure,
                LOADER + " 的 EnsureInjected 会抛异常。"
                         "UI 底图缺失只是观感降级，必须 fail-open 让程序化皮肤继续工作。")
        # 五张图必须都取出来并喂进对应注入点，否则包里躺着的图等于不存在。
        for asset in SKIN_ASSETS:
            require('"' + asset + '"' in loader,
                    LOADER + " 没有加载图集资源 " + asset + "：bundle 里有这张图却取不出来")
        for injector in INJECTORS:
            require("BossRushUISkin." + injector + "(" in ensure,
                    LOADER + " 的 EnsureInjected 没有把图喂给注入点 " + injector)

    # 不许引入 raw PNG fallback：散图没有九宫格 border，拉伸会糊却看似生效
    require("LoadImage" not in loader,
            LOADER + " 出现了 raw PNG fallback（LoadImage）。"
                     "运行时加载的散图没有九宫格 border，拉伸会变形，"
                     "却让人误以为素材已生效；九宫格必须在 Unity Sprite Editor 里预设后打 bundle。")

    # ---- 3) 注入时机：必须挂在 always-on 初始化，早于任何面板创建 ----
    init = extract_method(always_on, "internal void InitializeAlwaysOnRuntime()")
    require(bool(init), ALWAYS_ON + " 找不到 InitializeAlwaysOnRuntime 方法体")
    if init:
        require("BossRushUISkinLoader.EnsureInjected" in init,
                ALWAYS_ON + " 的 InitializeAlwaysOnRuntime 没有注入 UI 皮肤。"
                            "ApplyPanelSkin 只在面板创建时赋一次 sprite，注入晚于建面板就换不了皮。")

    # ---- 4) Cleanup 顺序：先把**每一档**复位，再 Unload ----
    cleanup = extract_method(loader, "internal static void Cleanup()")
    require(bool(cleanup), LOADER + " 找不到 Cleanup 方法体")
    if cleanup:
        unload_at = cleanup.find("Unload(")
        for injector in INJECTORS:
            at = cleanup.find(injector + "(null)")
            require(at >= 0,
                    LOADER + " 的 Cleanup 没有把注入点 " + injector + " 复位为 null："
                             "Unload(true) 之后那一档留的是已销毁的 Sprite，走这一档的控件会白板")
            if at >= 0 and unload_at >= 0:
                require(at < unload_at,
                        LOADER + " 的 Cleanup 先 Unload 再复位 " + injector + "，顺序反了。")

    require("BossRushUISkinLoader.Cleanup" in mod_behaviour,
            MOD_BEHAVIOUR + " 宿主销毁时没有调用 BossRushUISkinLoader.Cleanup，"
                            "bundle 会泄漏到下一次 runtime。")

    # ---- 5) 分档表：枚举齐全 + Auto 落位口径 ----
    for part in SKIN_PARTS:
        require(re.search(r"\b" + part + r"\b", skin) is not None,
                SKIN + " 的 BossRushUISkinPart 缺少分档 " + part)
    resolve = extract_method(skin, "private static BossRushUISkinPart ResolvePart(")
    require(bool(resolve), SKIN + " 找不到 Auto 分档的落位函数 ResolvePart")
    if resolve:
        # ≤3 必须落细条：3px 上任何一张图的 border 都会被压到中心区归零。
        require("BossRushUISkinPart.Hairline" in resolve,
                SKIN + " 的 Auto 分档没有把细条（radius ≤ 3）单列出来："
                       "3px 的强调竖条会穿上 32×32 / border 10 的按钮图")
        require("radius <= 3" in resolve,
                SKIN + " 的 Auto 分档没有按 radius ≤ 3 判细条")
        require("BossRushUISkinPart.Panel" in resolve and "BossRushUISkinPart.Button" in resolve,
                SKIN + " 的 Auto 分档缺少面板档或按钮档")

    # ---- 6) 天空岛的细元素必须传显式档，不能靠 Auto 撞运气 ----
    # 按**具体调用点**钉，不是找一下枚举名就算数：同一个文件里别处出现过 Card，
    # 不代表卡片底真的走了 Card 档。
    for source, call, label in (
        (hud, "ApplyPanelSkin(barImage, 2, BossRushUISkinPart.Hairline)",
         HUD + " 的强调竖条没有走细条档：3px 上按钮图的 border 会被压到中心区归零"),
        (hud, "ApplyPanelSkin(ruleImage, 2, BossRushUISkinPart.Rule)",
         HUD + " 大标题下的细横线没有走分隔线档（旧写法是 1px 裸 Image，非整数缩放下会被采样吃掉）"),
        (hud, "ApplyPanelSkin(background, 10, BossRushUISkinPart.Card)",
         HUD + " 右上常驻卡片底没有走卡片档：它是一块信息板，不是一个按钮"),
        (panel, "ApplyPanelSkin(dividerImage, 2, BossRushUISkinPart.Rule)",
         PANEL + " 标题下的分隔线没有走分隔线档"),
        (panel, "ApplyPanelSkin(image, 10, BossRushUISkinPart.Card)",
         PANEL + " 选项行底没有走卡片档"),
        (panel, "ApplyPanelSkin(surface, 18, BossRushUISkinPart.Panel)",
         PANEL + " 面板底没有走面板档"),
        (skin, "ApplyPanelSkin(trackImage, 4, BossRushUISkinPart.ScrollHandle)",
         SKIN + " 滚动条轨道没有走滑块档：12px 宽上按钮图的 border 会被压到中心区只剩 2px"),
        (skin, "ApplyPanelSkin(handleImage, 4, BossRushUISkinPart.ScrollHandle)",
         SKIN + " 滚动条滑块没有走滑块档"),
    ):
        require(call in source, label)

    # 描边必须真的铺到那几块底上，不是只在共享库里存在一个入口。
    for source, call, label in (
        (hud, "ApplyPanelStroke(background, 10, BossRushUISkinPart.Card",
         HUD + " 右上卡片没有描边：卡底 Surface 在暗地形上对背景只有 1.50:1，没有边就没有轮廓"),
        (panel, "ApplyPanelStroke(surface, 18, BossRushUISkinPart.Panel",
         PANEL + " 面板没有描边"),
        (panel, "ApplyPanelStroke(image, 10, BossRushUISkinPart.Card",
         PANEL + " 选项行没有描边：SurfaceRaised 对面板底只有 1.03:1，不画边玩家看到的只是几行浮着的字"),
    ):
        require(call in source, label)

    # ---- 7) 描边必须是独立 Image，不能指望图集里烤进去的那圈 ----
    # 实算：panel_surface 的描边像素与填充像素灰度差 49/255，乘上 BossRushUIColors.Surface
    # 之后屏幕上只剩 2.9/255（SurfaceRaised 4.9/255），是 8bit 量化底噪级别。
    require("internal static Image ApplyPanelStroke(" in skin,
            SKIN + " 缺少描边入口 ApplyPanelStroke")
    stroke = extract_method(skin, "internal static Sprite GetStrokeSprite(int radius)")
    require(bool(stroke), SKIN + " 缺少程序化描边环 GetStrokeSprite")
    apply_stroke = extract_method(skin, "internal static Image ApplyPanelStroke(")
    if apply_stroke:
        require("GetSkinCornerRadius(" in apply_stroke,
                SKIN + " 描边没有按图集实际的 border 取圆角："
                       "注入图集后圆角由图的 border 决定（面板图 16），"
                       "按调用方传的 radius 画会和底图差出两像素、露出一道错位的弧")
        require("raycastTarget = false" in apply_stroke,
                SKIN + " 描边会吃点击，盖住底图上的按钮")
    reset = extract_method(skin, "public static void ResetStaticCaches()")
    require(bool(reset) and "strokeSpriteCache.Clear();" in reset,
            SKIN + " 描边环缓存没有在 ResetStaticCaches 里销毁：贴图带 HideFlags.DontSave，切场景不回收")
    return errors


def main():
    sources = {}
    for rel in PATHS:
        path = ROOT / rel
        if not path.is_file():
            print("BossRushUISkinLoaderGuard: FAIL - 找不到 " + rel)
            return 1
        sources[rel] = path.read_text(encoding="utf-8", errors="ignore")

    errors = check(sources)

    # 反向检查：在内存里恢复旧的错误写法，确认断言真的抓得住。
    probes = [
        (LOADER, '"panel_raised"', '"panel_surface"'),                       # 卡片图又取不出来
        (LOADER, "BossRushUISkin.InjectRuleSprite(null);", ""),              # Cleanup 漏一档 → 白板
        (SKIN, "if (radius <= 3) return BossRushUISkinPart.Hairline;", ""),  # 细条又落回按钮图
        (SKIN, "stroke.sprite = GetStrokeSprite(GetSkinCornerRadius(radius, part));",
               "stroke.sprite = GetStrokeSprite(radius);"),                  # 描边与底图圆角错位
        (SKIN, "strokeSpriteCache.Clear();", ""),                            # 描边贴图泄漏
        (HUD, "ApplyPanelSkin(ruleImage, 2, BossRushUISkinPart.Rule)",
              "ApplyPanelSkin(ruleImage, 2, BossRushUISkinPart.Button)"),     # 细横线又穿按钮图
        (PANEL, "ApplyPanelSkin(image, 10, BossRushUISkinPart.Card)",
                "ApplyPanelSkin(image, 10, BossRushUISkinPart.Button)"),      # 选项行又穿按钮图
        (PANEL, "BossRushUI.ApplyPanelStroke(image, 10, BossRushUISkinPart.Card, BossRushUIColors.Stroke);",
                ""),                                                          # 选项行又变回没有边
        (HUD, "BossRushUI.ApplyPanelStroke(background, 10, BossRushUISkinPart.Card, BossRushUIColors.Stroke);",
              ""),                                                            # 卡片又变回没有边
    ]
    for path, before, after in probes:
        if before not in sources[path]:
            errors.append("反向检查锚点失效：" + path + " -> " + before)
            continue
        altered = dict(sources)
        altered[path] = altered[path].replace(before, after, 1)
        if not check(altered):
            errors.append("未拦截旧写法：" + path + " -> " + before)

    if errors:
        print("BossRushUISkinLoaderGuard: FAIL\n  " + "\n  ".join(errors))
        return 1
    print("BossRushUISkinLoaderGuard: PASS（fail-open + 注入时机 + 清理顺序 + 六图分档 + "
          + str(len(probes)) + " 个反向检查）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
