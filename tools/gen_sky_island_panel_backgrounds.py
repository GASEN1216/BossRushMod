# -*- coding: utf-8 -*-
"""从既有区域横幅派生「面板整屏底图」：`skyisland_scene_X.png` → `skyisland_bg_X.png`。

## 为什么要单独出这一张，而不是直接把横幅铺满面板

面板是竖的（880 × 约 940，0.94:1），横幅是 1024×288 的 3.56:1。把横幅 cover 到面板上
需要放大 **3.26 倍**，只看得见原图中间 26% 的宽度，而且云海的平滑渐变在这个倍率下会出现
明显的带状阶梯。所以底图必须是**为模糊而生**的小图：模糊之后分辨率就不重要了，
220×236 在面板上放大 4 倍照样看不出来，反而正是想要的那种「环境色」。

这也是为什么**不调生图 API**：底图要的是这张场景本身的色彩与明暗结构，
重新生一张只会得到一张色调对不上的图。派生比新画更对。

## 参数为什么是这几个

- **中心裁 62% 宽**：横幅的主体几乎都在中段（码头、集市、钟庭都是中心构图），
  两端多半是云。裁太窄会只剩天空，裁太宽会把左右的空云也算进平均色。
- **模糊半径 9（在 220 px 上）**：换算到面板上的显示尺寸约等于 36 px 的模糊，
  足以化掉所有可识别的形状，只留色彩与明暗。半径再小会看出「这是被拉糊的某张图」。
- **降饱和到 0.82**：底图要托住正文，不能自己抢眼。色相保留，所以每个区域仍然是
  自己的颜色（钟庭偏暖、听雨洞偏青）。
- **不在这里压暗**：压暗由运行时的 `BgTint`（`BossRushUIColors.Surface`，alpha 由
  `SkyIslandStoryPresentation.BackgroundTintAlpha` 给）负责——压暗量与正文对比度绑定，
  归代码管，烤进图里就没法跟着 token 走了（同 `BossRushUI_图集规格.md` 的口径）。

用法（不需要网络、不需要密钥、完全确定性）：
    python tools/gen_sky_island_panel_backgrounds.py            # 补齐缺的
    python tools/gen_sky_island_panel_backgrounds.py --force    # 全部重出
"""
import argparse
import os
import sys

from PIL import Image, ImageEnhance, ImageFilter

ART_DIR = os.path.join("Assets", "ui", "SkyIsland")
SCENE_PREFIX = "skyisland_scene_"
BG_PREFIX = "skyisland_bg_"

# 输出尺寸：面板约 880×940（0.94:1），这里取 0.93:1，运行时几乎是等比拉伸。
OUT_W, OUT_H = 220, 236
CROP_FRACTION = 0.62
BLUR_RADIUS = 9.0
SATURATION = 0.82


def derive(src_path, dst_path):
    im = Image.open(src_path).convert("RGB")
    w, h = im.size
    crop_w = int(round(w * CROP_FRACTION))
    left = (w - crop_w) // 2
    im = im.crop((left, 0, left + crop_w, h))
    # 先缩到目标尺寸再模糊：在小图上模糊比在大图上便宜得多，视觉结果一致。
    im = im.resize((OUT_W, OUT_H), Image.LANCZOS)
    im = im.filter(ImageFilter.GaussianBlur(BLUR_RADIUS))
    im = ImageEnhance.Color(im).enhance(SATURATION)
    im.convert("RGBA").save(dst_path)
    return os.path.getsize(dst_path)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--force", action="store_true", help="已存在也重出")
    args = parser.parse_args()

    if not os.path.isdir(ART_DIR):
        print("找不到 " + ART_DIR)
        return 1

    scenes = sorted(n for n in os.listdir(ART_DIR)
                    if n.startswith(SCENE_PREFIX) and n.endswith(".png"))
    if not scenes:
        print("没有找到任何 " + SCENE_PREFIX + "*.png")
        return 1

    made = skipped = 0
    total = 0
    for name in scenes:
        region = name[len(SCENE_PREFIX):-len(".png")]
        dst = os.path.join(ART_DIR, BG_PREFIX + region + ".png")
        if os.path.exists(dst) and not args.force:
            skipped += 1
            total += os.path.getsize(dst)
            continue
        size = derive(os.path.join(ART_DIR, name), dst)
        total += size
        made += 1
        print("  %-28s -> %-26s %5.1f KB" % (name, os.path.basename(dst), size / 1024.0))
    print("完成：新出 %d，跳过 %d，%d 张合计 %.1f KB"
          % (made, skipped, len(scenes), total / 1024.0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
