# -*- coding: utf-8 -*-
"""把作者工程里的成就图标原图转成 256px PNG，放进 `Assets/achievement/`。

为什么需要：
  `Achievement/AchievementIconLoader.cs` 现在**优先读独立 PNG**，读不到才回退
  `achievement_icons` 这个旧 AssetBundle。旧 bundle 里 36 张图的 importer
  Max Size 全是 64（源图 1024），进游戏是一片糊。把源图按 256 落成独立 PNG
  就能立刻变清楚，**不需要重新导入或重打 bundle**；bundle 仍然保留，
  作为没有部署 PNG 时的回退。

用法：
    python tools/sync_achievement_icons.py --unity-project <作者工程绝对路径>
    python tools/sync_achievement_icons.py --unity-project <...> --force   # 覆盖已有 PNG

`--unity-project` 省略时读环境变量 `BOSSRUSH_UNITY_PROJECT`。
本脚本**不会覆盖** `tools/gen_codex_art.py` 生成的新成就图标（那些是带 alpha 的
生图产物，作者工程里没有对应源图），因为它只处理源目录里真实存在的文件名。
"""
from __future__ import annotations

import argparse
import os
import sys

from PIL import Image

#: 与 tools/gen_codex_art.py 的成就图标规格一致。
TARGET_SIZE = 256
SOURCE_SUBDIR = os.path.join("Assets", "AchievementIcons")
TARGET_SUBDIR = os.path.join("Assets", "achievement")

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def convert(src, dst):
    with Image.open(src) as source:
        image = source.convert("RGBA")
    if image.size[0] > TARGET_SIZE or image.size[1] > TARGET_SIZE:
        image.thumbnail((TARGET_SIZE, TARGET_SIZE), Image.LANCZOS)
    image.save(dst, "PNG", optimize=True)
    return image.size


def main(argv=None):
    parser = argparse.ArgumentParser(description="同步成就图标源图到 256px 独立 PNG")
    parser.add_argument("--unity-project", default=os.environ.get("BOSSRUSH_UNITY_PROJECT"),
                        help="Unity 作者工程根目录（含 Assets/AchievementIcons）")
    parser.add_argument("--force", action="store_true", help="覆盖已存在的目标 PNG")
    parser.add_argument("--list", action="store_true", help="只列出将要处理的文件")
    args = parser.parse_args(argv)

    if not args.unity_project:
        print("需要 --unity-project 或环境变量 BOSSRUSH_UNITY_PROJECT", file=sys.stderr)
        return 2
    source_dir = os.path.join(args.unity_project, SOURCE_SUBDIR)
    if not os.path.isdir(source_dir):
        print("找不到源目录: " + source_dir, file=sys.stderr)
        return 2
    target_dir = os.path.join(REPO_ROOT, TARGET_SUBDIR)
    os.makedirs(target_dir, exist_ok=True)

    written = 0
    skipped = 0
    for name in sorted(os.listdir(source_dir)):
        if not name.lower().endswith(".png"):
            continue
        src = os.path.join(source_dir, name)
        dst = os.path.join(target_dir, name)
        if os.path.exists(dst) and not args.force:
            skipped += 1
            continue
        if args.list:
            print("would write " + name)
            written += 1
            continue
        size = convert(src, dst)
        written += 1
        print("%-44s -> %dx%d  %d bytes" % (name, size[0], size[1], os.path.getsize(dst)))

    print("\n写出 %d 张，跳过 %d 张（已存在，加 --force 覆盖）" % (written, skipped))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
