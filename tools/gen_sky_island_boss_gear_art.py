# -*- coding: utf-8 -*-
"""天空岛头目 / 岛主专属装备的美术：Tripo 输入概念图 + 与概念图同一件东西的背包图标。

两步，都可断点续跑（成品已存在就跳过；要重画必须连 output/ 里的原图一起删，口径同 gen_codex_art）：
1. concepts：单体居中、浅灰纯色底、均匀柔光无投影——这是 Tripo image-to-3D 的输入要求（强方向光会被烘进 albedo 拆不掉）。
   输出 output/sky_island_boss_gear_concepts/<件名>.png；由 owner 在 Tripo 网页手动上传生成 GLB（API 钱包未开通，上传无法自动化），
   GLB 存到 ArtSource/SkyIsland/BossGear/<件名>.glb，再跑 tools/sky_island_boss_gear_import.py。
2. icons：拿概念图当参考图走 images/edits（input_fidelity=high，写法同 tools/sky_island_minimap_art.py），出 #ff00ff 色键底
   的图标 → 网关已回 alpha 就跳过抠图 → remove_chroma_key → 裁到内容、居中贴进 512² 透明方图 → Assets/Items/<IconName>.png。
   图标与模型出自同一张概念图：玩家在背包里看到的，和 Boss 身上穿的是同一件。

不并进 tools/gen_sky_island_item_icons.py：那份清单被 SkyIslandFieldcraftGuard 钉成与岛上十八件物品一一对应。
图标文件名与 DebugAndTools/SkyIsland/SkyIslandBossRules.cs 的 GearSpecs.IconName 一一对应；Assets/ 与 output/ 都不进 git。

用法（需要网络出口；密钥只从环境变量 OPENAI_BASE_URL / OPENAI_API_KEY 读，来源见 docs/AI生图API和密钥.md，
不要写进脚本、命令行或日志）：
    python -X utf8 tools/gen_sky_island_boss_gear_art.py concepts [--only starworks_foreman_helmet ...]
    python -X utf8 tools/gen_sky_island_boss_gear_art.py icons [--only ...]
"""
import argparse
import base64
import os
import subprocess
import sys
import time
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_codex_art as base  # noqa: E402
from imagegen_model import IMAGE_MODEL, LEGACY_MODEL  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONCEPTS = os.path.join(ROOT, "output", "sky_island_boss_gear_concepts")
ICON_RAW = os.path.join(ROOT, "output", "sky_island_boss_gear_icons_raw")
ITEMS = os.path.join(ROOT, "Assets", "Items")

CONCEPT_STYLE = (
    " Single stylized 3D game asset render, exactly one object, centered in frame with generous empty margin "
    "on every side, front three-quarter view, shown on its own as if worn by an invisible body. "
    "Plain seamless flat light grey background, soft even diffuse studio lighting from all around, "
    "no cast shadow, no contact shadow, no strong specular highlights, no glow, no bloom. "
    "Chunky simplified shapes, bold readable silhouette, hand-painted matte texture look, "
    "warm amber, brass, cream and soft teal palette, no purple and no magenta. "
    "No character, no head, no mannequin, no display stand, no text, no letters, no logo, no watermark.")

DUCK_FIT = "Sized and shaped to be worn by a small round cartoon duck: "

ICON_PROMPT = (
    "Turn the object in this image into a single game inventory icon. Keep it the same object: the same shape, "
    "materials, colors and details. Three-quarter view, centered, filling most of the frame, stylized hand-painted "
    "game inventory art with soft painterly shading and a bold readable silhouette that stays legible at 64 pixels, "
    "gentle warm rim light. Put it on a perfectly flat solid #ff00ff chroma-key background. Do not use #ff00ff, pink "
    "or magenta anywhere in the object. No text, no letters, no watermark, no border, no frame, no scene, no character, no hands.")

# 件名 -> (图标名, 概念图描述)。件名与 tools/sky_island_boss_gear_import.py 的 PIECES 一致。
PIECES = [
    ("starworks_foreman_helmet", "sky_island_starbrass_visor_helm",
     DUCK_FIT + "a sturdy riveted brass workshop helmet with a rounded dome and an open face with a wide notch at "
     "the front for a flat duck bill, a hinged smoked-amber welding visor flipped up above the brow, a small brass "
     "astrolabe ring crest with a tiny five-pointed star on top, weathered teal verdigris along the rivet seams, "
     "a thick leather chin strap."),
    ("starworks_foreman_armor", "sky_island_starfurnace_harness",
     DUCK_FIT + "a sleeveless workshop chest armor made of layered overlapping brass plates riveted onto a thick "
     "cream canvas apron, a round brass pressure gauge set in the middle of the chest, star-shaped brass buckles on "
     "the shoulder straps, light soot and heat scorch marks along the lower edge, short plated tassets at the hips."),
    ("starworks_foreman_backpack", "sky_island_starfurnace_pack",
     DUCK_FIT + "a compact backpack-mounted miniature furnace: a squat brass boiler canister with a small round "
     "amber glass porthole (not glowing), two short stubby chimney pipes, thick canvas shoulder straps and a "
     "buckled leather tool roll strapped underneath, tiny star etchings on the brass, teal verdigris on the pipe joints."),
    ("lookout_stargazer_helmet", "sky_island_stargazer_lens_helm",
     DUCK_FIT + "a light explorer skull cap of stitched brown leather with brass trim and an open face with a wide "
     "notch at the front for a flat duck bill, a brass monocular eyepiece rig on a hinged arm hanging over the right "
     "eye with three stacked telescoping lenses of pale sky-blue glass, a small rolled star chart tucked under a side strap."),
]


def delay():
    return int(os.environ.get("ART_GEN_DELAY", "20"))


def run_concepts(pieces):
    os.makedirs(CONCEPTS, exist_ok=True)
    failures = 0
    for name, _icon, desc in pieces:
        dst = os.path.join(CONCEPTS, name + ".png")
        if os.path.exists(dst):
            print("[skip] " + dst, flush=True)
            continue
        print("[concept] " + name, flush=True)
        last = ""
        for attempt in range(1, 4):
            result = subprocess.run([sys.executable, base.IMAGEGEN, "generate", "--model", IMAGE_MODEL,
                                     "--size", "1024x1024", "--n", "1", "--no-augment",
                                     "--out", dst, "--prompt", desc + CONCEPT_STYLE],
                                    capture_output=True, text=True, timeout=420)
            if result.returncode == 0 and os.path.exists(dst):
                break
            last = (result.stderr or "")[-200:].replace("\n", " ")
            print("   [retry %d/3] %s" % (attempt, last[-120:]), flush=True)
            time.sleep(10 * attempt)
        if not os.path.exists(dst):
            failures += 1
            print("   [FAIL] " + last[-160:], flush=True)
        time.sleep(delay())
    return failures


def run_icons(pieces):
    from openai import OpenAI
    from PIL import Image
    os.makedirs(ICON_RAW, exist_ok=True)
    client = OpenAI(timeout=1200, max_retries=0)
    failures = 0
    for name, icon, _desc in pieces:
        concept = os.path.join(CONCEPTS, name + ".png")
        dst = os.path.join(ITEMS, icon + ".png")
        if os.path.exists(dst):
            print("[skip] " + dst, flush=True)
            continue
        if not os.path.exists(concept):
            failures += 1
            print("[FAIL] 缺概念图，先跑 concepts：" + concept, flush=True)
            continue
        raw = os.path.join(ICON_RAW, icon + "_raw.png")
        cut = os.path.join(ICON_RAW, icon + "_cut.png")
        print("[icon] " + icon, flush=True)
        for attempt in range(1, 4):
            if os.path.exists(raw):
                break
            started = time.time()
            try:
                with open(concept, "rb") as image:
                    result = client.images.edit(model=LEGACY_MODEL, image=image, prompt=ICON_PROMPT, size="1024x1024",
                                                quality="high", input_fidelity="high", n=1)
                item = result.data[0]
                if getattr(item, "b64_json", None):
                    with open(raw, "wb") as handle:
                        handle.write(base64.b64decode(item.b64_json))
                else:
                    urllib.request.urlretrieve(item.url, raw)
                print("   generated in %.0fs" % (time.time() - started), flush=True)
            except Exception as error:  # noqa: BLE001 - 网关限流时是连接错误，退避重试
                print("   [retry %d/3] %s: %s" % (attempt, type(error).__name__, str(error)[:200]), flush=True)
                time.sleep(delay() * attempt)
        if not os.path.exists(raw):
            failures += 1
            print("   [FAIL] 图标生成失败", flush=True)
            continue
        picture = Image.open(raw)
        if picture.mode == "RGBA" and picture.split()[-1].getextrema()[0] < 255:
            source = raw  # 网关直接回了透明图，跳过抠图
        else:
            subprocess.run([sys.executable, base.CHROMA, "--input", raw, "--out", cut, "--auto-key", "border",
                            "--soft-matte", "--transparent-threshold", "12", "--opaque-threshold", "220", "--despill"],
                           capture_output=True, text=True, timeout=180)
            source = cut if os.path.exists(cut) else raw
        base.normalize(source, dst, 512)
        print("   [OK] " + dst, flush=True)
        time.sleep(delay())
    return failures


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("step", choices=("concepts", "icons"))
    parser.add_argument("--only", nargs="*", default=None)
    args = parser.parse_args()
    if not os.environ.get("OPENAI_API_KEY") or not os.environ.get("OPENAI_BASE_URL"):
        print("缺少 OPENAI_BASE_URL / OPENAI_API_KEY 环境变量", flush=True)
        return 2
    only = set(args.only) if args.only else None
    pieces = [p for p in PIECES if not only or p[0] in only]
    failures = run_concepts(pieces) if args.step == "concepts" else run_icons(pieces)
    print("完成：失败 %d" % failures, flush=True)
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
