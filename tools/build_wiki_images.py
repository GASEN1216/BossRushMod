# -*- coding: utf-8 -*-
"""build_wiki_images.py - 把 Assets/ 下的美术资源转成在线 Wiki 用的 WebP 与配图清单。

为什么需要本工具：
    `/Assets/*` 在 .gitignore 里（二进制不进 git），而在线 Wiki 由 GitHub Actions
    从仓库直接构建（.github/workflows/deploy.yml：checkout -> npm run build），
    CI 里根本看不到那些 PNG。所以配图必须以**已转好的产物**形式进仓库。

    本工具就是那道离线转换：源图在本机 Assets/ 下，产物落到
    wiki-site/docs/public/images/ 与 wiki-site/scripts/image-manifest.json，
    两者一起提交。美术更新后重跑本工具即可。

    游戏内 Wiki 书**不吃这些图**：WikiContent/ 保持纯文本（见 sync-content.mjs
    的 IMAGE_PLACEMENT 注释），本工具的产物只服务 wiki-site。

用法：
    python tools/build_wiki_images.py            # 全量重建
    python tools/build_wiki_images.py --check    # 只校验产物与清单是否齐全，不写文件

依赖：Pillow（本机已装；CI 不跑本工具，因此不进任何 requirements）。
"""
import argparse
import csv
import json
import os
import sys

try:
    from PIL import Image
except ImportError:
    print("build_wiki_images: FAIL - 需要 Pillow：python -m pip install Pillow")
    sys.exit(2)

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO, "Assets")
OUT_DIR = os.path.join(REPO, "wiki-site", "docs", "public", "images")
MANIFEST = os.path.join(REPO, "wiki-site", "scripts", "image-manifest.json")
LOC_ZH = os.path.join(REPO, "docs", "官方本地化表", "ChineseSimplified.csv")
LOC_EN = os.path.join(REPO, "docs", "官方本地化表", "English.csv")

WEBP_QUALITY = 82
PORTRAIT_MAX = 320          # 图鉴立绘：网格里最宽也就 ~160 CSS px，320 够 2x
POSTER_MAX = 560            # 章节海报：单栏展示，给大一点
ICON_MAX = 128              # 建筑/物品图标

# ── 本 Mod 自己的 Boss 名（代码常量逐字抄，改名时这里要跟）────────────
MOD_BOSS_NAMES = {
    "boss_dragonking":      ("焚天龙皇", "Skyburner Dragon Lord"),
    "boss_phantomwitch":    ("幽灵女巫", "Phantom Witch"),
    "dragondescendant":     ("龙裔遗族", "Dragon Descendant"),
    "zombie_boss_titan":     ("巨坦", "Titan"),
    "zombie_boss_hunter":    ("极速追猎", "Hunter"),
    "zombie_boss_splitter":  ("分裂尸群", "Splitter"),
    "zombie_boss_shielder":  ("护盾统御", "Shielder"),
    "zombie_boss_corruptor": ("腐蚀地面", "Corruptor"),
}

# ── 章节海报与立绘（战役）────────────────────────────────────────────
CAMPAIGN_FIGURES = [
    ("campaign_poster_ch1.png", "campaign/poster-ch1", "第 1 章 · 擂台旧影", "Chapter 1 — Echoes of the Ring"),
    ("campaign_poster_ch2.png", "campaign/poster-ch2", "第 2 章 · 白手起家的誓言", "Chapter 2 — Vow of the Empty-Handed"),
    ("campaign_poster_ch3.png", "campaign/poster-ch3", "第 3 章 · 立旗为界", "Chapter 3 — Planting the Banner"),
    ("campaign_poster_ch4.png", "campaign/poster-ch4", "第 4 章 · 猎杀名单", "Chapter 4 — The Kill List"),
    ("campaign_poster_ch5.png", "campaign/poster-ch5", "第 5 章 · 末日信标", "Chapter 5 — The Last Beacon"),
    ("campaign_poster_ch6.png", "campaign/poster-ch6", "第 6 章 · 冠军之影", "Chapter 6 — Shadow of the Champion"),
    ("campaign_portrait_broker.png", "campaign/portrait-broker", "中间人", "The Broker"),
    ("campaign_portrait_champion.png", "campaign/portrait-champion", "冠军之影", "Shadow of the Champion"),
]

# ── 建筑与物品图标 ───────────────────────────────────────────────────
ICON_FIGURES = [
    ("buildings/bossrush_daily_mailbox.png",     "icons/daily-mailbox",  "报箱", "The Mailbox"),
    ("buildings/bossrush_campaign_board.png",    "icons/campaign-board", "征程公告板", "Campaign Board"),
    ("buildings/bossrush_backmountain_showcase.png", "icons/showcase",   "战利品展示柜", "Trophy Showcase"),
    ("buildings/petnest_relic_nest.png",         "icons/petnest",        "遗种巢", "The Relic Nest"),
    ("Items/codex_book.png",                     "icons/codex-book",     "鸭皇图鉴", "Duck King Codex"),
    ("Items/affix_forge_stone.png",              "icons/forge-stone",    "词缀熔石", "Affix Forge Stone"),
    ("Items/relic_egg.png",                      "icons/relic-egg",      "遗种蛋", "Relic Egg"),
]


def unescape_loc(value):
    """官方 CSV 的值带引号与反斜杠转义：\"Goofy\\ Goose\" -> Goofy Goose。"""
    v = value.strip()
    if len(v) >= 2 and v[0] == '"' and v[-1] == '"':
        v = v[1:-1]
    out, i = [], 0
    while i < len(v):
        if v[i] == "\\" and i + 1 < len(v):
            out.append(v[i + 1])
            i += 2
        else:
            out.append(v[i])
            i += 1
    return "".join(out).strip()


def load_official_names():
    """key(lower) -> (zh, en)。官方本地化表是唯一事实源，避免自造译名。"""
    def read(path):
        table = {}
        if not os.path.isfile(path):
            return table
        with open(path, "r", encoding="utf-8-sig", newline="") as fh:
            for row in csv.reader(fh):
                if len(row) >= 2 and row[0]:
                    table[row[0].strip().lower()] = unescape_loc(row[1])
        return table

    zh, en = read(LOC_ZH), read(LOC_EN)
    keys = set(zh) | set(en)
    return {k: (zh.get(k, ""), en.get(k, "")) for k in keys}


def resolve_boss_name(key, official):
    """图鉴 key -> (zh, en)。Mod 自有的走常量，官方的查本地化表。"""
    if key in MOD_BOSS_NAMES:
        return MOD_BOSS_NAMES[key]
    zh, en = official.get(key, ("", ""))
    # 官方把部分 Boss 名就写成 ??? ——那是游戏里真实显示的样子，照搬不改写
    zh = zh or key
    en = en or zh
    return (zh, en)


def convert(src, rel_out, max_edge, check_only):
    """PNG -> WebP，长边压到 max_edge。返回 (相对路径, 字节数)。"""
    dst = os.path.join(OUT_DIR, rel_out + ".webp")
    if check_only:
        if not os.path.isfile(dst):
            return None, 0
        return "/images/" + rel_out + ".webp", os.path.getsize(dst)

    if not os.path.isfile(src):
        return None, 0

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with Image.open(src) as im:
        im = im.convert("RGBA")
        w, h = im.size
        scale = min(1.0, float(max_edge) / max(w, h))
        if scale < 1.0:
            im = im.resize((max(1, int(w * scale)), max(1, int(h * scale))), Image.LANCZOS)
        im.save(dst, "WEBP", quality=WEBP_QUALITY, method=6)
    return "/images/" + rel_out + ".webp", os.path.getsize(dst)


def build_favicon(check_only):
    """config.mts 的 head 里写死了 ${base}images/favicon.ico，但 public/ 此前根本不存在
    ——图标一直 404。用图鉴书的图标补上。"""
    dst = os.path.join(OUT_DIR, "favicon.ico")
    if check_only:
        return os.path.isfile(dst)
    src = os.path.join(ASSETS, "Items", "codex_book.png")
    if not os.path.isfile(src):
        return False
    os.makedirs(OUT_DIR, exist_ok=True)
    with Image.open(src) as im:
        im.convert("RGBA").save(dst, "ICO", sizes=[(16, 16), (32, 32), (48, 48)])
    return True


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true",
                        help="只校验产物与清单是否齐全，不写文件")
    args = parser.parse_args()

    official = load_official_names()
    manifest = {"codex": [], "campaign": [], "icons": []}
    total_bytes = 0
    missing = []

    # ── 图鉴立绘 ──────────────────────────────────────────────────
    codex_dir = os.path.join(ASSETS, "ui", "Codex")
    if os.path.isdir(codex_dir):
        names = sorted(n for n in os.listdir(codex_dir)
                       if n.startswith("codex_portrait_") and n.endswith(".png"))
    else:
        names = []
    for name in names:
        key = name[len("codex_portrait_"):-len(".png")]
        zh, en = resolve_boss_name(key, official)
        url, size = convert(os.path.join(codex_dir, name),
                            "codex/" + key.replace("_", "-"), PORTRAIT_MAX, args.check)
        if url is None:
            missing.append(name)
            continue
        total_bytes += size
        manifest["codex"].append({"key": key, "src": url, "zh": zh, "en": en})

    # ── 战役海报与立绘 ────────────────────────────────────────────
    for fname, rel, zh, en in CAMPAIGN_FIGURES:
        url, size = convert(os.path.join(ASSETS, "ui", "Campaign", fname),
                            rel, POSTER_MAX, args.check)
        if url is None:
            missing.append(fname)
            continue
        total_bytes += size
        manifest["campaign"].append({"key": rel.split("/")[-1], "src": url, "zh": zh, "en": en})

    # ── 建筑与物品图标 ────────────────────────────────────────────
    for relsrc, rel, zh, en in ICON_FIGURES:
        url, size = convert(os.path.join(ASSETS, relsrc), rel, ICON_MAX, args.check)
        if url is None:
            missing.append(relsrc)
            continue
        total_bytes += size
        manifest["icons"].append({"key": rel.split("/")[-1], "src": url, "zh": zh, "en": en})

    ok_icon = build_favicon(args.check)

    if args.check:
        if missing or not ok_icon:
            print("build_wiki_images: FAIL - 缺少产物 "
                  + str(len(missing)) + " 个" + ("，favicon 缺失" if not ok_icon else ""))
            for m in missing[:10]:
                print("  " + m)
            return 1
        print("build_wiki_images: OK - 产物齐全（%d 图鉴 / %d 战役 / %d 图标），共 %.1f MB"
              % (len(manifest["codex"]), len(manifest["campaign"]),
                 len(manifest["icons"]), total_bytes / 1048576.0))
        return 0

    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(manifest, fh, ensure_ascii=False, indent=2, sort_keys=False)
        fh.write("\n")

    print("build_wiki_images: 写出 %d 图鉴立绘 / %d 战役图 / %d 图标，合计 %.1f MB"
          % (len(manifest["codex"]), len(manifest["campaign"]),
             len(manifest["icons"]), total_bytes / 1048576.0))
    print("  产物: wiki-site/docs/public/images/")
    print("  清单: wiki-site/scripts/image-manifest.json")
    if missing:
        print("  跳过（源图缺失）: " + ", ".join(missing[:8]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
