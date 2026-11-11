# -*- coding: utf-8 -*-
"""末班编组站两件物品图标：512x512 透明 PNG → Assets/Items/<ICON_NAME>.png。

流水线与画风整段复用 tools/gen_sky_island_item_icons.py（色键 #ff00ff 出图 → 抠图 → 裁到内容、居中贴进透明正方形；
目标 PNG 或 output 里的原图存在就跳过，可断点续跑；间隔 ART_GEN_DELAY 秒、单张三次退避重试），口径同潮闸镇图标脚本。
编组站是停运的夜间铁路货场，主体带一点油污、铁锈和旧纸色，画风块不改。

用法（需要网络出口；密钥只从环境变量 OPENAI_BASE_URL / OPENAI_API_KEY 读，来源见 docs/AI生图API和密钥.md，
不要写进脚本、命令行或日志）：
    python tools/gen_lastshift_item_icons.py --only lastshift_stamp   # 先出一张验风格
    python tools/gen_lastshift_item_icons.py                          # 补齐其余（已存在的跳过）
图标文件名必须与 Integration/LastShift/LastShiftItems.cs 里各物品的 IconName 一致；
要重画某张，连 output/lastshift_item_icons_raw/<名字>_raw.png 与 _cut.png 一起删。
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_codex_art as base  # noqa: E402
import gen_sky_island_item_icons as sky  # noqa: E402

ICONS = [
    ("lastshift_stamp",
     "a heavy steel hand stamp for pressing railway lead seals: a short turned dark-wood handle on top of a squat "
     "cylindrical steel body, the round stamping face tilted toward the viewer and worn bright silver with a simple "
     "raised train-wheel emblem, a loop of cracked brown leather strap through the handle, a little dark oil and rust "
     "in the grooves."),
    ("lastshift_ticket_stub",
     "a small old cardboard railway ticket stub in faded cream and pale green, printed with a simple steam locomotive "
     "silhouette and blank unreadable lines, two neat punched holes each with a tiny notch on one edge and an empty "
     "circle marked where a third hole should be, slightly curled corners and a soft brown stain."),
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
    base.RAW = "output/lastshift_item_icons_raw"
    return base.main([])


if __name__ == "__main__":
    sys.exit(main())
