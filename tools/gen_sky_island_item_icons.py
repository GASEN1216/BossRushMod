# -*- coding: utf-8 -*-
"""天空岛（晴岚群岛）新物品图标：512x512 透明 PNG → Assets/Items/<ICON_NAME>.png。

流水线整段复用 tools/gen_codex_art.py（色键 #ff00ff 出图 → 网关已回 alpha 就跳过抠图 → remove_chroma_key
→ 裁到内容、居中贴进透明正方形；可断点续跑、间隔 ART_GEN_DELAY 秒、单张三次退避重试），这里只换清单与画风：
按晴岚群岛的暖色基准收紧（琥珀、奶油、天蓝、柔青绿，柔和暖逆光，不要紫色 / 洋红辉光），
而不是图鉴图标那套「dramatic rim lighting, rich saturated colors」。

用法（需要网络出口；密钥只从环境变量 OPENAI_BASE_URL / OPENAI_API_KEY 读，来源见 docs/AI生图API和密钥.md，
不要写进脚本、命令行或日志）：
    python tools/gen_sky_island_item_icons.py --test     # 先出前 2 张验风格
    python tools/gen_sky_island_item_icons.py            # 补齐其余（已存在的跳过）
    python tools/gen_sky_island_item_icons.py --only sky_island_windeater_core
图标文件名必须与 Integration/SkyIsland/SkyIslandItems.cs 里各物品的 IconName 一致；Assets/ 不进 git，原图留在 output/。
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_codex_art as base  # noqa: E402

STYLE = (" Stylized hand-painted game inventory art with soft painterly shading, a bold readable silhouette "
         "that stays legible at 64 pixels, a warm sunlit palette of amber, cream, sky blue and soft teal, "
         "gentle warm rim light, no purple glow and no magenta glow. Create the subject on a perfectly flat solid "
         "#ff00ff chroma-key background. Do not use #ff00ff, pink or magenta anywhere in the subject. "
         "No text, no letters, no watermark, no logo, no border, no frame, no scene, no character, no hands.")


def icon(desc):
    return "Single game UI item icon, one centered object on an empty background: " + desc + STYLE


ICONS = [
    ("sky_island_homecoming_badge",
     "a small round polished brass medal shaped like a bell, a cream enamel cloud and a tiny sailboat engraved "
     "on its face, hanging from a short sky-blue ribbon, warm gold edges."),
    ("sky_island_windeater_core",
     "a fist-sized glassy orb with a miniature pale-teal whirlwind trapped inside, cradled in a cracked "
     "amber-gold stone shell, faint white wind streaks curling around it."),
    ("sky_island_wind_vane_compass",
     "an antique brass pocket compass with its lid open, a tiny wind-vane arrow instead of a needle, a cloud "
     "engraved inside the lid, weathered teal patina on the rim, a short leather strap."),
    ("sky_island_homecoming_bento",
     "a rustic round bamboo lunch box with its woven lid set aside, filled with steamed rice and fresh green "
     "vegetables, a tiny paper pinwheel stuck in the rice, a knotted cream cloth wrap underneath."),
    ("sky_island_starmoss_salve",
     "a small round clay jar with a cork lid tied with twine, filled with soft green moss salve dotted with tiny "
     "pale-gold star-shaped sparkles, a sprig of moss leaning against the jar."),
]

SPECS = [("Assets/Items/%s.png" % name, 512, icon(desc)) for name, desc in ICONS]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--test", action="store_true", help="只出清单前 2 张，先验风格")
    parser.add_argument("--only", nargs="*", default=None, help="只出这些图标名（不带 .png）")
    args = parser.parse_args()
    if not os.environ.get("OPENAI_API_KEY") or not os.environ.get("OPENAI_BASE_URL"):
        print("缺少 OPENAI_BASE_URL / OPENAI_API_KEY 环境变量", flush=True)
        return 2
    specs = SPECS
    if args.only:
        wanted = set(args.only)
        specs = [s for s in SPECS if os.path.splitext(os.path.basename(s[0]))[0] in wanted]
    elif args.test:
        specs = SPECS[:2]
    base.SPECS = specs
    base.RAW = "output/sky_island_item_icons_raw"
    return base.main()


if __name__ == "__main__":
    sys.exit(main())
