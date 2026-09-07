# -*- coding: utf-8 -*-
"""build_wiki_theme_assets.py - 生成在线 Wiki 版式用的贴图（天空 / 木纹 / 草皮 / Logo）。

和另外两个生图脚本的分工：
    tools/gen_codex_art.py    -> 游戏内美术（图鉴立绘、词缀图标），源图进 Unity 打包。
    tools/gen_wiki_icons.py   -> 站点**内容**图标（类目、条目、速查框里的小图）。
    本脚本                     -> 站点**版式**贴图（页面底图、部件木纹、草皮条、站点 Logo）。

    三者产物落点不同：内容图标进 docs/public/images/ui/（受 WikiImageAssetGuard 管），
    版式贴图进 theme/assets/（受 WikiThemeAssetGuard 管）。放错地方会被判成孤儿产物。

为什么版式贴图要放 theme/assets/ 而不是 public/：
    public/ 下的东西是「按原样复制、URL 写死」；theme/assets/ 里的会被 Vite 打哈希、
    自动补 base 前缀，CSS 里写 url(../assets/x.webp) 就够了。换 base 部署（根 / 子路径）
    时不用改一个字，也不会被 build_wiki_images.py 重建清单时误删。

产物三件套：
    Assets/wiki_theme/<key>.png                     AI 出的源图，local-only（Assets/ 被 .gitignore 挡着）
    wiki-site/docs/.vitepress/theme/assets/*        站点实际加载的产物（提交）
    wiki-site/scripts/wiki-theme-assets.json        产物清单 + 字节数（提交）

    清单存在的理由同 wiki-icons.json：源图在别人机器上不存在，没有清单就无法校验
    「产物是不是全的、有没有超预算」。tests/WikiThemeAssetGuard.py 只读清单和文件大小，
    不需要 Pillow，CI 上跑得动。

三类贴图三种做法，不是都靠生图：
    天空、木纹、冰霜             -> 生图（painterly 风格与图鉴立绘同源）
    Logo 徽记                    -> 从入库的 preview.png（创意工坊主视觉）里 GrabCut 抠出来。
                                    模组自己的龙裔遗族读者已经认得，比另画一个徽记强
    木纹 / 冰霜的**平铺化**      -> 镜像四拼，接缝天然为零
    木纹 / 冰霜的**透明化**      -> 只保留亮度起伏做成半透明颗粒层，
                                    这样同一张贴图铺在棕色面板上是木头、铺在浅蓝上是冰
                                    （底色由 --theme-*-background 决定，贴图只加材质）
    草皮条 13px                  -> 程序化绘制。1024 的图缩到 13px 只会糊成一条色带，
                                    还不如直接画：又小（≈1KB）、又脆、又能保证左右接缝对齐
    Logo 文字                    -> 程序化排版。生图模型写不对字，这是实测结论

用法（生图需要网络出口，密钥见 docs/AI生图API和密钥.md）：
    set OPENAI_BASE_URL=https://colorflowai.com/v1 && set OPENAI_API_KEY=sk-...
    python tools/build_wiki_theme_assets.py                # 断点续跑：已有源图的跳过生图
    python tools/build_wiki_theme_assets.py --no-gen       # 不生图，只用现有源图重出产物
    python tools/build_wiki_theme_assets.py --only wood    # 只处理某几个 key（试跑验风格）
    python tools/build_wiki_theme_assets.py --check        # 只校验产物与清单（不需要 Pillow）

网关限流：间隔低于 20 秒会大面积撞 APIConnectionError，每张实际耗时 3~4 分钟。
间隔用 ART_GEN_DELAY 控制，默认 20 秒。
"""
import argparse
import json
import os
import subprocess
import sys
import time

HOME = os.path.expanduser("~")
IMAGEGEN = os.path.join(HOME, ".codex", "skills", ".system", "imagegen", "scripts", "image_gen.py")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_DIR = os.path.join(REPO, "Assets", "wiki_theme")
RAW_DIR = os.path.join(REPO, "output", "wiki_theme_raw")
OUT_DIR = os.path.join(REPO, "wiki-site", "docs", ".vitepress", "theme", "assets")
SIDECAR = os.path.join(REPO, "wiki-site", "scripts", "wiki-theme-assets.json")

# 预算：与 tests/WikiThemeAssetGuard.py 的常量一一对应，改这里必须同步改那里。
BUDGET_SKIN_TOTAL = 600000   # 单套皮肤实际会下载的版式贴图总字节
BUDGET_SINGLE_SKY = 200000   # 单张天空底图

TILE = 256                   # 木纹 / 冰霜的平铺边长
SKY_W, SKY_H = 1600, 1000    # 天空底图产物尺寸（背景是 cover，不需要 2x）
LOGO_W, LOGO_H = 842, 280    # Logo 产物尺寸（= 版位 421x140 的 2x）
GRASS_W, GRASS_H = 192, 26   # 草皮条画 2x（CSS 里按 96x13 铺），HiDPI 上边缘才不糊

# ── 生图提示词 ──────────────────────────────────────────────────────
# 与 gen_wiki_icons.py / gen_codex_art.py 的 STYLE 同源（painterly stylized game art），
# 只去掉「单个物件、留白背景」那部分——版式贴图要的是满幅材质与景。

SCENE_STYLE = (" Painterly stylized game-art matte painting, soft brush strokes, "
               "gentle atmospheric perspective, low contrast, muted palette. "
               "No text, no watermark, no logo, no border, no frame, no characters, "
               "no user interface, nothing sharp or high-contrast in the middle of the frame.")

TILE_STYLE = (" Painterly stylized game-art texture, flat even lighting with no highlights, "
              "no shadows, no vignette, no perspective, no depth of field, shot straight on, "
              "the material fills the entire frame edge to edge with no border and no object. "
              "No text, no watermark, no logo.")

# key, 生图尺寸, prompt（None = 不生图，程序化产出）
SOURCES = [
    ("sky-overworld", "1536x1024",
     "A wide view of a daytime sky seen from high in the air: layered soft cumulus clouds, "
     "a smooth gradient from periwinkle blue at the top to pale warm haze near the bottom, "
     "two or three small distant floating rock islands with green grass on top and thin "
     "waterfalls spilling off them, placed low and far away near the left and right edges, "
     "everything hazy and far off." + SCENE_STYLE),

    ("sky-snow", "1536x1024",
     "A wide view of a cold overcast winter sky seen from high in the air: soft pale violet and "
     "icy blue clouds, gently falling snow, a smooth gradient from lavender at the top to near "
     "white near the bottom, two or three small distant floating rock islands capped with snow "
     "and hanging icicles, placed low and far away near the left and right edges, "
     "everything hazy and far off." + SCENE_STYLE),

    ("wood", "1024x1024",
     "Old dark wooden planks seen from directly above, fine straight vertical grain, a few small "
     "knots, narrow dark seams between the planks, weathered and slightly rough." + TILE_STYLE),

    ("frost", "1024x1024",
     "A sheet of frosted ice seen from directly above, fine feathery frost ferns and hairline "
     "crystal cracks spreading across pale glassy ice, faint powdered snow." + TILE_STYLE),

]


def fail(message):
    print("build_wiki_theme_assets: FAIL - " + message)
    return 1


# ── 生图 ────────────────────────────────────────────────────────────

def generate_one(key, size, prompt):
    """出一张源图到 Assets/wiki_theme/<key>.png。已存在则跳过。返回是否成功。"""
    from PIL import Image

    dst = os.path.join(SRC_DIR, key + ".png")
    if os.path.exists(dst):
        return True
    os.makedirs(SRC_DIR, exist_ok=True)
    os.makedirs(RAW_DIR, exist_ok=True)

    raw = os.path.join(RAW_DIR, key + "_raw.png")
    if not os.path.exists(raw):
        # 网关会间歇性抛 APIConnectionError，单次失败不代表这张出不来，带指数退避重试。
        last_err = ""
        for attempt in range(1, 4):
            r = subprocess.run(
                [sys.executable, IMAGEGEN, "generate", "--model", "gpt-image-2",
                 "--size", size, "--n", "1", "--no-augment", "--out", raw, "--prompt", prompt],
                capture_output=True, text=True, timeout=600)
            if r.returncode == 0 and os.path.exists(raw):
                break
            last_err = (r.stderr or r.stdout or "")[-200:]
            print("   [retry %d/3] %s" % (attempt, last_err.replace(chr(10), " ")[-110:]), flush=True)
            time.sleep(10 * attempt)
        if not os.path.exists(raw):
            print("   [FAIL] " + last_err, flush=True)
            return False

    Image.open(raw).convert("RGB").save(dst, "PNG")
    return True


# ── 后期：天空 ──────────────────────────────────────────────────────

def build_sky(key):
    """源图裁成 16:10 再压 WebP。天空是 cover 铺满，细节全被内容面板盖住，质量给低些。"""
    from PIL import Image

    src = os.path.join(SRC_DIR, key + ".png")
    with Image.open(src) as im:
        im = im.convert("RGB")
        w, h = im.size
        want = float(SKY_W) / SKY_H
        have = float(w) / h
        if have > want:                      # 太宽，切左右
            new_w = int(round(h * want))
            box = ((w - new_w) // 2, 0, (w - new_w) // 2 + new_w, h)
        else:                                # 太高，切下面（天空的上半段更好看）
            new_h = int(round(w / want))
            box = (0, 0, w, new_h)
        im = im.crop(box).resize((SKY_W, SKY_H), Image.LANCZOS)
        out = os.path.join(OUT_DIR, key + ".webp")
        for quality in (84, 76, 68, 58, 48, 38):
            im.save(out, "WEBP", quality=quality, method=6)
            if os.path.getsize(out) <= BUDGET_SINGLE_SKY:
                break
    return key + ".webp"


# ── 后期：平铺材质 ──────────────────────────────────────────────────

def build_tile(key, strength, blur=0.0):
    """把源图变成「无缝平铺的半透明颗粒层」。

    两步：
      1. 镜像四拼 —— 左上原图、右上左右翻、左下上下翻、右下翻两次。
         接缝两侧像素天然相等，所以绝不会有拼缝。代价是有对称感，
         但这层最终只有 8~14% 不透明度，看不出来。
      2. 只留亮度起伏 —— 比平均亮的地方给白、暗的地方给黑，
         偏离越多越不透明。底色因此仍由 --theme-panel-background 决定：
         同一张贴图铺在棕色上是木头、铺在浅蓝上是冰，不会把皮肤颜色糊掉。
    """
    from PIL import Image, ImageFilter, ImageOps, ImageStat

    src = os.path.join(SRC_DIR, key + ".png")
    half = TILE // 2
    with Image.open(src) as im:
        grey = im.convert("L").resize((half, half), Image.LANCZOS)
    if blur:
        # 图样越「像个东西」，镜像四拼的对称感越刺眼（冰花是典型）。
        # 先柔一下把纹样揉成材质，对称就不容易被一眼认出来。
        grey = grey.filter(ImageFilter.GaussianBlur(blur))

    quad = Image.new("L", (TILE, TILE))
    quad.paste(grey, (0, 0))
    quad.paste(ImageOps.mirror(grey), (half, 0))
    quad.paste(ImageOps.flip(grey), (0, half))
    quad.paste(ImageOps.mirror(ImageOps.flip(grey)), (half, half))

    mean = ImageStat.Stat(quad).mean[0]
    colour = quad.point(lambda v: 255 if v >= mean else 0)
    alpha = quad.point(lambda v: min(255, int(abs(v - mean) * strength)))
    rgba = Image.merge("RGBA", (colour, colour, colour, alpha))

    out = os.path.join(OUT_DIR, key + ".webp")
    rgba.save(out, "WEBP", lossless=True, method=6, exact=False)
    return key + ".webp"


# ── 后期：草皮条 ────────────────────────────────────────────────────

def build_strip(key, palette, seed, jag):
    """程序化画 192x26 的横向平铺条：门户框顶上压着的那道草皮（Snow 皮肤是雪檐）。

    形状是「参差的上沿 + 纵向渐变的本体」——不是在平顶上贴方块，
    高出的那几像素之上是**透明**的，所以草叶真的像草叶，而不是一排色块。

    上沿高度用随机游走生成，再把首尾几列插值回同一个值，
    repeat-x 时左右接缝因此对得上，看不出重复起点。
    """
    import random
    from PIL import Image

    top, mid, bottom, tip = palette
    rng = random.Random(seed)

    # 随机游走出上沿；步长限 ±1，避免出现孤立的高塔
    edge = [rng.randint(0, jag)]
    for _ in range(GRASS_W - 1):
        step = rng.choice((-1, 0, 0, 1))
        edge.append(max(0, min(jag, edge[-1] + step)))
    blend = 12                                   # 首尾各 12 列插值成同一高度，保证接缝连续
    for i in range(blend):
        t = i / float(blend)
        edge[GRASS_W - blend + i] = int(round(edge[GRASS_W - blend + i] * (1 - t) + edge[0] * t))

    im = Image.new("RGBA", (GRASS_W, GRASS_H), (0, 0, 0, 0))
    px = im.load()
    for x in range(GRASS_W):
        start = edge[x]
        span = max(1, GRASS_H - start - 1)
        for y in range(start, GRASS_H):
            t = (y - start) / float(span)
            if y == start:
                colour = tip                     # 上沿高光：一条 1px 的亮边
            elif y == GRASS_H - 1:
                colour = bottom                  # 最底一行压暗，当落在框上的影
            elif t < 0.5:
                colour = _mix(top, mid, t / 0.5)
            else:
                colour = _mix(mid, bottom, (t - 0.5) / 0.5)
            px[x, y] = colour + (255,)

    out = os.path.join(OUT_DIR, key + ".png")
    im.save(out, "PNG", optimize=True)
    return key + ".png"


def _mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


# ── 后期：Logo 徽记 ─────────────────────────────────────────────

PREVIEW = os.path.join(REPO, "preview.png")
# 角色在 512x512 预览图里的位置。下边界落在胯部而不是脚底：
# 抠图到脚底会连地面一起带出来（脚和焦土同色），而 Logo 里角色是**贴着画布下沿**放的，
# 平切口正好被边框吃掉，看上去就是「角色从下边缘探出来」，是常见的 Logo 手法。
EMBLEM_BOX = (176, 118, 367, 356)
EMBLEM_CACHE = os.path.join(SRC_DIR, "emblem-cut.png")


def build_emblem():
    """从创意工坊预览图 preview.png 里抠出龙裔遗族，作 Logo 的徽记。

    为什么不生图：模组自己的主视觉就是这只龙裔遗族，读者在创意工坊见过它，
    Logo 用同一个形象比另画一个徽记认得快。preview.png 是入库文件（big_preview.png
    被 .gitignore 挡着），所以换台机器也能一模一样地重建。

    背景是同色系的火海战场，硬阈值抠不动，用 GrabCut 给个矩形初值迭代 8 轮，
    再只留最大连通块、闭运算补洞、开运算削毛刺，最后轻微高斯柔化边缘。
    抠好的图缓存到 Assets/wiki_theme/emblem-cut.png，重出 Logo 时不必再算一遍。
    """
    from PIL import Image

    if os.path.isfile(EMBLEM_CACHE):
        return Image.open(EMBLEM_CACHE).convert("RGBA")

    try:
        import cv2
        import numpy as np
    except ImportError:
        raise SystemExit("build_wiki_theme_assets: 抠角色需要 opencv-python 与 numpy："
                         "python -m pip install opencv-python numpy")

    src = cv2.imread(PREVIEW)
    if src is None:
        raise SystemExit("build_wiki_theme_assets: 读不到 " + PREVIEW)
    crop = src[EMBLEM_BOX[1]:EMBLEM_BOX[3], EMBLEM_BOX[0]:EMBLEM_BOX[2]]
    # 先放大一倍再抠：512 的原图上 GrabCut 的边界会有台阶，放大后柔和得多
    crop = cv2.resize(crop, (crop.shape[1] * 2, crop.shape[0] * 2), interpolation=cv2.INTER_LANCZOS4)
    h, w = crop.shape[:2]

    mask = np.zeros((h, w), np.uint8)
    bgd = np.zeros((1, 65), np.float64)
    fgd = np.zeros((1, 65), np.float64)
    rect = (int(w * 0.05), int(h * 0.03), int(w * 0.90), int(h * 0.95))
    cv2.grabCut(crop, mask, rect, bgd, fgd, 8, cv2.GC_INIT_WITH_RECT)

    fg = np.where((mask == cv2.GC_FGD) | (mask == cv2.GC_PR_FGD), 255, 0).astype(np.uint8)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(fg, 8)
    if count > 1:
        biggest = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
        fg = np.where(labels == biggest, 255, 0).astype(np.uint8)
    fg = cv2.morphologyEx(fg, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8))
    fg = cv2.morphologyEx(fg, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
    fg = cv2.GaussianBlur(fg, (5, 5), 0)

    rgba = np.dstack([cv2.cvtColor(crop, cv2.COLOR_BGR2RGB), fg])
    im = Image.fromarray(rgba, "RGBA")
    os.makedirs(SRC_DIR, exist_ok=True)
    im.save(EMBLEM_CACHE, "PNG")
    return im


# ── 后期：Logo ──────────────────────────────────────────────────────

WORDMARK = "BOSSRUSH"
SUBMARK = "ESCAPE FROM DUCKOV \u00b7 WIKI"
# 字标只是「把字排成图」，产物是位图不是字体文件，不涉及字体再分发。
FONT_MAIN = [os.path.join("C:\\", "Windows", "Fonts", "ariblk.ttf"),
             os.path.join("C:\\", "Windows", "Fonts", "arialbd.ttf"),
             "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"]
FONT_SUB = [os.path.join("C:\\", "Windows", "Fonts", "verdanab.ttf"),
            os.path.join("C:\\", "Windows", "Fonts", "arialbd.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"]

GOLD_TOP = (255, 244, 214)
GOLD_BOTTOM = (226, 165, 62)
INK = (36, 21, 14)
SUB_COLOR = (240, 231, 208)


def _font(candidates, size):
    from PIL import ImageFont
    for path in candidates:
        if os.path.isfile(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def build_logo():
    """徽记 + 字标拼成 842x280 的站点 Logo（= 版位 421x140 的 2x）。

    徽记是从创意工坊预览图里抠出来的龙裔遗族（见 build_emblem），字标是 Pillow 排的
    ——生图模型写不对字母，这是实测结论。字标做成「深墨描边 + 金色渐变填充 + 落影」，
    深浅两套皮肤的天空底都压得住，不用出两版。
    """
    from PIL import Image, ImageDraw, ImageFilter

    canvas = Image.new("RGBA", (LOGO_W, LOGO_H), (0, 0, 0, 0))

    # 徽记：等比缩到几乎满高，**贴着画布下沿**放（见 EMBLEM_BOX 的注释）
    em = _trim_alpha(build_emblem())
    scale = 272.0 / em.size[1]
    em = em.resize((max(1, int(round(em.size[0] * scale))), 272), Image.LANCZOS)
    canvas.alpha_composite(em, (6, LOGO_H - em.size[1]))
    text_left = 6 + em.size[0] + 14

    avail = LOGO_W - text_left - 20
    main_font, stroke, main_box = _fit(FONT_MAIN, WORDMARK, avail)
    main_w = main_box[2] - main_box[0]
    main_h = main_box[3] - main_box[1]

    sub_size = max(14, int(main_h * 0.19))
    sub_font, tracking, sub_w, sub_h = _fit_sub(SUBMARK, sub_size, avail)

    gap = int(main_h * 0.14)
    top = (LOGO_H - (main_h + gap + sub_h)) // 2
    cx = text_left + avail // 2
    # textbbox 给的是「墨迹」的框，不是绘制原点。要让墨迹左上角落在 (x, y)，
    # 得把原点往回挪一个 box[0]/box[1]——少了这一步，粗描边的字会被右边界切掉。
    mx = cx - main_w // 2 - main_box[0]
    my = top - main_box[1]

    # 描边层：整块字（含描边）先画成实心墨色，既是轮廓也是落影的形状
    ink_layer = Image.new("RGBA", (LOGO_W, LOGO_H), (0, 0, 0, 0))
    ImageDraw.Draw(ink_layer).text(
        (mx, my), WORDMARK, font=main_font, fill=INK + (255,),
        stroke_width=stroke, stroke_fill=INK + (255,))
    canvas.alpha_composite(_offset(ink_layer.filter(ImageFilter.GaussianBlur(5)), 0, 6))
    canvas.alpha_composite(ink_layer)

    # 填充层：金色纵向渐变，用字形本身当蒙版盖在描边里面
    mask = Image.new("L", (LOGO_W, LOGO_H), 0)
    ImageDraw.Draw(mask).text((mx, my), WORDMARK, font=main_font, fill=255)
    canvas.paste(_gradient(LOGO_W, LOGO_H, top, top + main_h), (0, 0), mask)

    # 副标：字间距靠逐字画
    sub_stroke = max(2, sub_size // 7)
    sy = top + main_h + gap
    sx = cx - sub_w // 2
    probe = ImageDraw.Draw(Image.new("L", (8, 8)))
    sub_box = probe.textbbox((0, 0), SUBMARK, font=sub_font, stroke_width=sub_stroke)
    sub_ink = Image.new("RGBA", (LOGO_W, LOGO_H), (0, 0, 0, 0))
    sub_draw = ImageDraw.Draw(sub_ink)
    for ch in SUBMARK:
        sub_draw.text((sx - sub_box[0], sy - sub_box[1]), ch, font=sub_font,
                      fill=SUB_COLOR + (255,), stroke_width=sub_stroke, stroke_fill=INK + (235,))
        sx += int(round(sub_font.getlength(ch))) + tracking
    canvas.alpha_composite(_offset(sub_ink.filter(ImageFilter.GaussianBlur(3)), 0, 3))
    canvas.alpha_composite(sub_ink)

    out = os.path.join(OUT_DIR, "logo.webp")
    canvas.save(out, "WEBP", quality=88, method=6)
    return "logo.webp"


def _fit(candidates, text, avail):
    """从大往小找第一个塞得进 avail 的字号，返回 (font, 描边宽, 墨迹框)。"""
    from PIL import Image, ImageDraw
    probe = ImageDraw.Draw(Image.new("L", (8, 8)))
    size = 104
    while size > 24:
        font = _font(candidates, size)
        stroke = max(4, int(size * 0.085))
        box = probe.textbbox((0, 0), text, font=font, stroke_width=stroke)
        if box[2] - box[0] <= avail:
            return font, stroke, box
        size -= 2
    font = _font(candidates, 24)
    return font, 4, probe.textbbox((0, 0), text, font=font, stroke_width=4)


def _fit_sub(text, size, avail):
    """副标带字间距，宽度得逐字量；塞不下就一号一号往下降。"""
    from PIL import Image, ImageDraw
    probe = ImageDraw.Draw(Image.new("L", (8, 8)))
    while size > 11:
        font = _font(FONT_SUB, size)
        tracking = max(1, int(size * 0.34))
        width = int(sum(round(font.getlength(ch)) for ch in text) + tracking * (len(text) - 1))
        if width <= avail:
            box = probe.textbbox((0, 0), text, font=font, stroke_width=max(2, size // 7))
            return font, tracking, width, box[3] - box[1]
        size -= 1
    font = _font(FONT_SUB, 11)
    box = probe.textbbox((0, 0), text, font=font, stroke_width=2)
    return font, 1, box[2] - box[0], box[3] - box[1]


def _gradient(w, h, y0, y1):
    from PIL import Image
    grad = Image.new("RGB", (1, max(1, y1 - y0)))
    span = max(1, y1 - y0 - 1)
    for y in range(grad.size[1]):
        grad.putpixel((0, y), _mix(GOLD_TOP, GOLD_BOTTOM, y / float(span)))
    grad = grad.resize((w, max(1, y1 - y0)), Image.BILINEAR)
    full = Image.new("RGB", (w, h), GOLD_BOTTOM)
    full.paste(grad, (0, y0))
    if y0 > 0:
        full.paste(Image.new("RGB", (w, y0), GOLD_TOP), (0, 0))
    return full


def _offset(im, dx, dy):
    from PIL import Image
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    out.alpha_composite(im, (dx, dy))
    return out


def _trim_alpha(im):
    box = im.split()[-1].getbbox()
    return im.crop(box) if box else im


# ── 清单与校验 ──────────────────────────────────────────────────────
# skins 决定这张图算进哪套皮肤的预算：只有当前皮肤引用到的 url() 才会被浏览器请求。

PRODUCTS = [
    ("sky-overworld.webp", ["Overworld"]),
    ("sky-snow.webp", ["Snow"]),
    ("wood.webp", ["Overworld"]),
    ("frost.webp", ["Snow"]),
    ("grass.png", ["Overworld"]),
    ("grass-snow.png", ["Snow"]),
    ("logo.webp", ["Overworld", "Snow"]),
]


def write_sidecar():
    files = []
    missing = []
    for name, skins in PRODUCTS:
        path = os.path.join(OUT_DIR, name)
        if not os.path.isfile(path):
            missing.append(name)
            continue
        files.append({"file": name, "bytes": os.path.getsize(path), "skins": skins})
    with open(SIDECAR, "w", encoding="utf-8", newline="\n") as fh:
        json.dump({"files": files}, fh, ensure_ascii=False, indent=2)
        fh.write("\n")
    return files, missing


def report(files):
    per_skin = {}
    for entry in files:
        for skin in entry["skins"]:
            per_skin[skin] = per_skin.get(skin, 0) + entry["bytes"]
    for name in sorted(per_skin):
        print("  %-10s %7.1f KB / %.0f KB" % (name, per_skin[name] / 1024.0,
                                              BUDGET_SKIN_TOTAL / 1024.0))
    return per_skin


def check():
    """只读文件大小与清单，不 import Pillow —— CI 上没有 Pillow 也要能跑。"""
    if not os.path.isfile(SIDECAR):
        return fail("缺清单 %s；先在本机跑一次 python tools/build_wiki_theme_assets.py"
                    % os.path.relpath(SIDECAR, REPO))
    with open(SIDECAR, "r", encoding="utf-8") as fh:
        files = json.load(fh).get("files", [])
    listed = set(e["file"] for e in files)
    expected = set(name for name, _ in PRODUCTS)
    if listed != expected:
        return fail("清单与 PRODUCTS 对不上：清单多 %s，少 %s"
                    % (sorted(listed - expected), sorted(expected - listed)))
    for entry in files:
        path = os.path.join(OUT_DIR, entry["file"])
        if not os.path.isfile(path):
            return fail("清单里有 %s，theme/assets/ 里没有" % entry["file"])
        actual = os.path.getsize(path)
        if actual != entry["bytes"]:
            return fail("%s 实际 %d 字节，清单写的是 %d —— 重跑本脚本刷新清单"
                        % (entry["file"], actual, entry["bytes"]))
    per_skin = report(files)
    for skin, total in per_skin.items():
        if total > BUDGET_SKIN_TOTAL:
            return fail("%s 皮肤的版式贴图共 %d 字节，超预算 %d" % (skin, total, BUDGET_SKIN_TOTAL))
    for entry in files:
        if entry["file"].startswith("sky-") and entry["bytes"] > BUDGET_SINGLE_SKY:
            return fail("%s 有 %d 字节，单张天空底图预算是 %d"
                        % (entry["file"], entry["bytes"], BUDGET_SINGLE_SKY))
    print("build_wiki_theme_assets: PASS - %d 个版式贴图在位，各皮肤均在预算内" % len(files))
    return 0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="只校验产物与清单（不需要 Pillow）")
    parser.add_argument("--no-gen", action="store_true", help="不调生图接口，只用现有源图重出产物")
    parser.add_argument("--only", default="", help="逗号分隔的 key，只处理这几个")
    parser.add_argument("--gen-only", action="store_true", help="只生图，不出产物")
    args = parser.parse_args()

    if args.check:
        return check()

    os.makedirs(OUT_DIR, exist_ok=True)
    wanted = set(k.strip() for k in args.only.split(",") if k.strip())

    if not args.no_gen:
        specs = [s for s in SOURCES if not wanted or s[0] in wanted]
        todo = [s for s in specs if not os.path.exists(os.path.join(SRC_DIR, s[0] + ".png"))]
        print("源图共 %d 项，待生成 %d 项" % (len(specs), len(todo)), flush=True)
        delay = int(os.environ.get("ART_GEN_DELAY", "20"))
        for i, (key, size, prompt) in enumerate(todo, 1):
            print("[%d/%d] %s (%s)" % (i, len(todo), key, size), flush=True)
            try:
                ok = generate_one(key, size, prompt)
                print("   [%s] %s" % ("OK" if ok else "FAIL", key), flush=True)
            except Exception as e:  # noqa: BLE001 - 单张失败不该中断整批
                print("   [ERR] %s" % e, flush=True)
            if i < len(todo):
                time.sleep(delay)

    if args.gen_only:
        return 0

    made = []
    def want(key):
        return not wanted or key in wanted

    for key in ("sky-overworld", "sky-snow"):
        if want(key) and os.path.isfile(os.path.join(SRC_DIR, key + ".png")):
            made.append(build_sky(key))
    if want("wood") and os.path.isfile(os.path.join(SRC_DIR, "wood.png")):
        made.append(build_tile("wood", 1.9))
    if want("frost") and os.path.isfile(os.path.join(SRC_DIR, "frost.png")):
        made.append(build_tile("frost", 0.85, blur=1.1))
    if want("grass"):
        made.append(build_strip("grass", ((124, 194, 66), (92, 163, 48),
                                          (44, 96, 30), (176, 226, 104)), 20260907, 6))
    if want("grass-snow"):
        made.append(build_strip("grass-snow", ((249, 252, 255), (219, 234, 247),
                                               (146, 176, 203), (255, 255, 255)), 20260907, 4))
    if want("logo"):
        made.append(build_logo())

    print("产出 %d 个：%s" % (len(made), ", ".join(made)), flush=True)
    files, missing = write_sidecar()
    report(files)
    if missing:
        print("尚缺 %d 个产物：%s" % (len(missing), ", ".join(missing)), flush=True)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
