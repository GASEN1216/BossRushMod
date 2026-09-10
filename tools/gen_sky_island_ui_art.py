# -*- coding: utf-8 -*-
"""天空岛交互面板插图：六位居民立绘 + 十二处区域场景横幅。

形态照 `tools/gen_codex_art.py`，但有两处刻意不同：

  1. **场景横幅不抠图**。图鉴立绘要浮在面板上，所以走 #ff00ff 色键 + remove_chroma_key；
     区域横幅是整幅铺在面板顶部的插图，抠掉背景反而什么都不剩。
  2. **画风锚定原版**。鸭科夫本体的色带实测是 H18–40 的暖琥珀（暖砂岩、赤陶瓦、黄绿草），
     记录在 `ArtSource/SkyIsland/VANILLA_GRADE.md`。不点名的话网关会给出冷蓝奇幻风，
     和岛上实际材质对不上，玩家一眼看出「这张图不是这个游戏的」。

可断点续跑：目标文件已存在就跳过，网关抽风或中途中断直接再跑一遍补齐。
按实测每张约 3~4 分钟（其中 20 秒是间隔，其余是网关出图延迟），18 张约排 70 分钟，
所以**必须放后台跑**，并且先 `--test` 出两张验风格再开全量。

用法（需要网络出口，密钥见 docs/AI生图API和密钥.md）：
    python tools/gen_sky_island_ui_art.py --test    # 先出 2 张验风格
    python tools/gen_sky_island_ui_art.py           # 全量补齐
"""
import argparse
import os
import subprocess
import sys
import time

from PIL import Image

HOME = os.path.expanduser("~")
IMAGEGEN = os.path.join(HOME, ".codex", "skills", ".system", "imagegen", "scripts", "image_gen.py")
CHROMA = os.path.join(HOME, ".codex", "skills", ".system", "imagegen", "scripts", "remove_chroma_key.py")

OUT_DIR = os.path.join("Assets", "ui", "SkyIsland")
RAW_DIR = os.path.join("output", "sky_island_ui_raw")

# 原版色带：暖砂岩 / 赤陶瓦 / 黄绿草，H18–40 暖琥珀。见 ArtSource/SkyIsland/VANILLA_GRADE.md。
PALETTE = ("Colour grade locked to the game's own palette: warm amber and sandstone, "
           "terracotta roof tiles, yellow-green grass, soft hazy sunlight. "
           "No cold blue fantasy grade, no purple magic glow. ")

STYLE = ("Painterly stylized game-art illustration with clean readable shapes, "
         "soft ambient occlusion, gentle rim light. " + PALETTE +
         "No text, no watermark, no logo, no border, no frame, no UI elements.")

DUCK = ("The subject is an ANTHROPOMORPHIC DUCK - a duck, not a human: rounded duck head, "
        "flat orange bill, feathered body, standing upright on two legs. ")

CHROMA_BG = (" Place the subject on a perfectly flat solid #ff00ff chroma-key background. "
             "Do not use #ff00ff anywhere on the subject itself.")


def portrait(desc):
    """居民立绘：半身、抠图、浮在面板左侧。"""
    return ("Friendly villager portrait for a dialogue panel in a cosy survival game. " + DUCK +
            "This duck is " + desc +
            " Waist-up three-quarter view, centered, calm approachable expression, at rest." +
            STYLE + CHROMA_BG)


def scene(desc):
    """区域横幅：整幅铺在面板顶部，不抠图。"""
    return ("Wide establishing shot of a floating sky-island location in a cosy survival game. " +
            desc +
            " Seen from a slightly elevated three-quarter angle, sea of clouds far below, "
            "no characters, no close-up faces." + STYLE)


PORTRAITS = {
    "sky_qinghe": "Qinghe the terrace farmer, in a straw sunhat and a dirt-smudged apron, "
                  "cradling a basket of fresh greens, a small pinwheel tucked behind one ear.",
    "sky_weibai": "Weibai the lane dispatcher, in a windproof shawl covered in small brass wind-chimes, "
                  "holding a clipboard of contract slips and a lantern hook.",
    "sky_fuzhou": "Fuzhou the old ferryman, in a weathered oilskin coat with coiled mooring rope over "
                  "one shoulder, a brass repair spanner at the belt.",
    "sky_miantai": "Miantai the moss herbalist, in soft moss-green layered robes, holding a shallow "
                   "clay bowl of damp green moss remedy, sleepy half-closed eyes.",
    "sky_zheling": "Zheling the keeper of the closed route, in a dark travel-worn flight coat with one "
                   "broken feather ornament at the collar, arms folded, guarded wary stance.",
    "sky_bellkeeper": "the Silent Bell Keeper, in a heavy hooded temple mantle with a rope-and-clapper "
                      "motif embroidered at the hem, hands clasped, serene and unreadable.",
}

SCENES = {
    "A": "Cloudrise Dock - a timber landing stage of the archipelago with mooring posts, "
         "coiled ropes, crates and a moored skiff at the cliff edge.",
    "B": "Windchime Market - a small hillside village square strung with hundreds of brass wind-chimes, "
         "awnings, a wooden notice board thick with pinned paper slips.",
    "C": "Green Terraces - stepped vegetable paddies descending toward the cloud sea, "
         "irrigation channels, a straw scarecrow and a turning pinwheel.",
    "D": "Hanging Root Wood - a grove where colossal tree roots arch overhead and dangle into open sky, "
         "a tall weathervane beacon mast jammed among the roots.",
    "E": "Windsong Boardwalk - a long slatted plank bridge on rope stays crossing open sky between "
         "two islands, tattered banners snapping in strong wind.",
    "F": "Mirrorwater Temple - a serene courtyard shrine around a perfectly still reflecting pool, "
         "stone lanterns and a mossy tiled roof.",
    "G": "Fallen Star Workshop - a domed workshop of brass rings and lenses, half-dismantled, "
         "a great star-lamp apparatus on a scaffold in the middle.",
    "H": "Homecoming Bell Court - a windswept stone courtyard with a huge bronze bell hung "
         "in a timber frame, worn steps and prayer ribbons.",
    "S1": "Frogsong Pool - a quiet reed-fringed pond on a small side islet, lily pads, "
          "a damp abandoned notebook on a flat stone.",
    "S2": "Upturned Post Hut - a tiny mail kiosk hanging upside-down beneath the island's underside, "
          "unsent letters wedged in its slots.",
    "S3": "Rainlisten Grotto - a shallow sea-cave mouth with three thin waterfalls curtaining "
          "the entrance, old charts pinned to the wet rock.",
    "S4": "Starfall Overlook - a bare stone observation platform at the archipelago's highest point, "
          "a large brass telescope on a swivel mount.",
}


def specs():
    """(目标路径, 输出宽, 输出高, 是否抠图, prompt)。"""
    rows = []
    for npc_id, desc in sorted(PORTRAITS.items()):
        rows.append((os.path.join(OUT_DIR, "skyisland_portrait_%s.png" % npc_id),
                     512, 512, True, portrait(desc)))
    # 横幅比例必须配面板里的槽位：正文宽 828、槽高上限 232 → 3.57:1。
    # 出 8:3 再拉成 3.57:1 会把画面竖向压掉 34%，云海一眼看出被压扁。
    for region, desc in sorted(SCENES.items()):
        rows.append((os.path.join(OUT_DIR, "skyisland_scene_%s.png" % region),
                     1024, 288, False, scene(desc)))
    return rows


# 先验风格的两张：一张立绘一张场景，覆盖两条不同的后处理路径。
TEST_KEYS = ("skyisland_portrait_sky_weibai", "skyisland_scene_A")


def normalize(src, dst, width, height):
    """裁成面板要的宽高比再缩放。横幅按中心裁，立绘保持正方形。"""
    im = Image.open(src)
    if im.mode != "RGBA":
        im = im.convert("RGBA")
    target = width / float(height)
    w, h = im.size
    if abs(w / float(h) - target) > 0.001:
        if w / float(h) > target:          # 太宽 → 左右裁
            new_w = int(round(h * target))
            left = (w - new_w) // 2
            im = im.crop((left, 0, left + new_w, h))
        else:                               # 太高 → 上下裁，偏上保留天际线
            new_h = int(round(w / target))
            top = int((h - new_h) * 0.38)
            im = im.crop((0, top, w, top + new_h))
    im = im.resize((width, height), Image.LANCZOS)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    im.save(dst)


def generate_one(index, total, dst, width, height, cut_out, prompt):
    stem = os.path.splitext(os.path.basename(dst))[0]
    raw = os.path.join(RAW_DIR, stem + "_raw.png")
    cut = os.path.join(RAW_DIR, stem + "_cut.png")
    print("[%d/%d] %s" % (index, total, dst), flush=True)
    if not os.path.exists(raw):
        last_err = ""
        for attempt in range(1, 4):
            r = subprocess.run([sys.executable, IMAGEGEN, "generate", "--model", "gpt-image-2",
                                "--size", "1024x1024", "--n", "1", "--no-augment",
                                "--out", raw, "--prompt", prompt],
                               capture_output=True, text=True, timeout=600)
            if r.returncode == 0 and os.path.exists(raw):
                break
            last_err = (r.stderr or "")[-200:]
            print("   [retry %d/3] %s" % (attempt, last_err.replace(chr(10), " ")[-110:]), flush=True)
            time.sleep(8 * attempt)
        if not os.path.exists(raw):
            print("   [FAIL] " + last_err, flush=True)
            return False
    src = raw
    if cut_out:
        im = Image.open(raw)
        # 网关有时直接回带 alpha 的 PNG，那就别再抠一次（2026-08-29 实测坑）。
        if not (im.mode == "RGBA" and im.split()[-1].getextrema()[0] < 255):
            subprocess.run([sys.executable, CHROMA, "--input", raw, "--out", cut,
                            "--auto-key", "border", "--soft-matte",
                            "--transparent-threshold", "12", "--opaque-threshold", "220",
                            "--despill"], capture_output=True, text=True, timeout=180)
            if os.path.exists(cut):
                src = cut
    normalize(src, dst, width, height)
    print("   [OK] -> %s (%dx%d)" % (dst, width, height), flush=True)
    return True


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--test", action="store_true", help="只出 TEST_KEYS 那两张，验风格用")
    args = parser.parse_args()

    os.makedirs(RAW_DIR, exist_ok=True)
    os.makedirs(OUT_DIR, exist_ok=True)
    rows = specs()
    if args.test:
        rows = [r for r in rows
                if os.path.splitext(os.path.basename(r[0]))[0] in TEST_KEYS]
    todo = [r for r in rows if not os.path.exists(r[0])]
    print("总计 %d 项，待生成 %d 项（间隔 %s 秒）"
          % (len(rows), len(todo), os.environ.get("ART_GEN_DELAY", "20")), flush=True)
    ok = fail = 0
    for i, (dst, w, h, cut_out, prompt) in enumerate(todo, 1):
        try:
            if generate_one(i, len(todo), dst, w, h, cut_out, prompt):
                ok += 1
            else:
                fail += 1
        except Exception as e:  # noqa: BLE001 - 单张失败不该中断整批
            fail += 1
            print("   [ERR] %s" % e, flush=True)
        if i < len(todo):
            time.sleep(int(os.environ.get("ART_GEN_DELAY", "20")))
    print("完成：成功 %d，失败 %d" % (ok, fail), flush=True)
    return 0 if fail == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
