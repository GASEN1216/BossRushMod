# -*- coding: utf-8 -*-
"""潮闸镇五件物品图标：512x512 透明 PNG → Assets/Items/<ICON_NAME>.png。

流水线与画风整段复用 tools/gen_sky_island_item_icons.py（色键 #ff00ff 出图 → 抠图 → 裁到内容、居中贴进透明正方形；
目标 PNG 或 output 里的原图存在就跳过，可断点续跑；间隔 ART_GEN_DELAY 秒、单张三次退避重试），
这里只换清单：潮闸镇是泡水的海边小镇，主体多带一点水渍、锈和泥沙，画风块不改，跟天空岛图标放在一起不跳。

用法（需要网络出口；密钥只从环境变量 OPENAI_BASE_URL / OPENAI_API_KEY 读，来源见 docs/AI生图API和密钥.md，
不要写进脚本、命令行或日志）：
    python tools/gen_tidegate_item_icons.py --only tidegate_handwheel   # 先出一张验风格
    python tools/gen_tidegate_item_icons.py                             # 补齐其余（已存在的跳过）
图标文件名必须与 Integration/Tidegate/TidegateItems.cs 里各物品的 IconName 一致；
要重画某张，连 output/tidegate_item_icons_raw/<名字>_raw.png 与 _cut.png 一起删。
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_codex_art as base  # noqa: E402
import gen_sky_island_item_icons as sky  # noqa: E402

ICONS = [
    ("tidegate_handwheel",
     "a heavy round cast-iron valve handwheel with four curved spokes and a square hole in the hub, chipped dark "
     "teal paint over warm rusty orange iron, a little dried sand and mud caked on the rim."),
    ("tidegate_tide_watch",
     "an old brass pocket watch with its lid open and a short brass chain, a cream enamel dial with an extra outer "
     "ring of small hand-cut wave-shaped tide marks, faint blue-green water stains on the brass, warm gold highlights."),
    ("tidegate_dry_bag",
     "a roll-top waterproof dry bag made of faded mustard-yellow coated canvas, the top rolled down three times and "
     "fastened with a dark buckle, a short shoulder strap, a few water droplets beading on its surface."),
    ("tidegate_flippers",
     "a pair of bright orange rubber swim flippers shaped like webbed duck feet, laid side by side, adjustable heel "
     "straps with small brass buckles, a few water droplets on the rubber."),
    ("tidegate_letter",
     "a folded cream paper letter with soft brown water stains and blurred unreadable ink smudges, tied with thin "
     "twine and closed with a small rust-red wax seal, resting on a corner of a damp brown paper parcel."),
]

SPECS = [("Assets/Items/%s.png" % name, 512, sky.icon(desc)) for name, desc in ICONS]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", nargs="*", default=None, help="只出这些图标名（不带 .png）")
    args = parser.parse_args()
    if not os.environ.get("OPENAI_API_KEY") or not os.environ.get("OPENAI_BASE_URL"):
        print("缺少 OPENAI_BASE_URL / OPENAI_API_KEY 环境变量", flush=True)
        return 2
    specs = SPECS
    if args.only:
        wanted = set(args.only)
        specs = [s for s in SPECS if os.path.splitext(os.path.basename(s[0]))[0] in wanted]
    base.SPECS = specs
    base.RAW = "output/tidegate_item_icons_raw"
    return base.main([])


if __name__ == "__main__":
    sys.exit(main())
