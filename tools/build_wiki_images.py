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
# 在线 Wiki 导航图标的边车清单，由 tools/gen_wiki_icons.py 写出并提交。
# 它的源图在 Assets/wiki_icons/（local-only），别人的机器上没有，
# 所以这一组只能照清单登记，不能靠扫 Assets/ 重建——否则重跑本工具会把 ui 组抹掉，
# 留下一堆 WikiImageAssetGuard 判定为孤儿的 WebP 产物。
UI_SIDECAR = os.path.join(REPO, "wiki-site", "scripts", "wiki-icons.json")
LOC_ZH = os.path.join(REPO, "docs", "reference", "official-localization", "ChineseSimplified.csv")
LOC_EN = os.path.join(REPO, "docs", "reference", "official-localization", "English.csv")

WEBP_QUALITY = 82
# 尺寸口径（2026-09-06 定）：**任何图都不得在页面上被放大显示**，
# 而且点开灯箱（WikiLightbox）要能比缩略图大一截，否则「点开看大图」没有意义。
#   立绘  源图 512，提满。缩略图只有 142px，320 本来够清楚，但灯箱里 320 太小
#         （1440 视口下就是中间一小块），点开等于没放大。这一组 37 张全在图鉴
#         那一页上，此前正文图片没有 loading=lazy、一开页就全量拉取，提尺寸会
#         明显加重那一页；懒加载已在 config.mts 补上，首屏只拉可见的十来张。
#   海报  正文里最宽 410px，560 只有 1.37x，点开几乎没变大；源图就是 1024，提满。
#   图标  正文里 205px 而产物只有 128 —— 一直在被**放大**显示，这就是「有些太糊」
#         的出处。源图 256/512，提到 256 至少不再放大。
PORTRAIT_MAX = 512          # 图鉴立绘（源图上限）
POSTER_MAX = 1024           # 章节海报（源图上限）
ICON_MAX = 256              # 建筑/物品图标
# 正文实体链接里的物品小图标（2026-11-19 加）：content.css 的 .brs-eref__icon 最高 28px、
# 悬停预览 26px，64 是两倍密度屏上的两倍余量。同一件东西若还当条目页的图标用
# （速查框里按 128px 显示），走 ENTITY_REGISTRY 的 pageIcons，按 ICON_MAX 出图。
ITEM_ICON_MAX = 64
# 实体登记表：哪些游戏图标要进在线站、key 叫什么。站点（config.mts 的实体链接）与本工具读同一份。
ENTITY_REGISTRY = os.path.join(REPO, "wiki-site", "scripts", "wiki-entities.json")

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
    ("Items/codex_book.png",                     "icons/codex-book",     "鸭皇图鉴", "Duckov Codex"),
    ("Items/affix_forge_stone.png",              "icons/forge-stone",    "词缀熔石", "Affix Forge Stone"),
    ("Items/relic_egg.png",                      "icons/relic-egg",      "遗种蛋", "Relic Egg"),
    ("Items/roll_call_ledger_icon.png",           "icons/eq-roll-call-ledger", "教导主任的点名册", "Headmaster's Roll-Call Ledger"),
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


def load_ui_sidecar():
    """读 wiki-icons.json。缺失或损坏时返回空表并出声——这一组是可选的，
    不该因为没跑过 gen_wiki_icons.py 就让整个转换失败。"""
    if not os.path.isfile(UI_SIDECAR):
        return []
    try:
        with open(UI_SIDECAR, "r", encoding="utf-8") as fh:
            data = json.load(fh)
    except (ValueError, OSError) as err:
        print("build_wiki_images: 警告 - wiki-icons.json 读取失败，本次不登记 ui 组：%s" % err)
        return []
    return [e for e in data if isinstance(e, dict) and e.get("key") and e.get("src")]


def load_entity_icons():
    """wiki-entities.json 里带 src 的条目 -> [(Assets 相对路径, key, zh, en, 最长边)]。

    同一个 key 只转一次（营旗「随机」那条和「营旗」共用一张图）；pageIcons 按 ICON_MAX，
    其余按 ITEM_ICON_MAX。缺文件或坏 JSON 时返回空表：这一组是可选的。"""
    if not os.path.isfile(ENTITY_REGISTRY):
        return []
    try:
        with open(ENTITY_REGISTRY, "r", encoding="utf-8") as fh:
            data = json.load(fh)
    except (ValueError, OSError) as err:
        print("build_wiki_images: 警告 - wiki-entities.json 读取失败，本次不登记 items 组：%s" % err)
        return []
    out, seen = [], set()
    for entry in data.get("pageIcons", []):
        if entry.get("src") and entry.get("icon") and entry["icon"] not in seen:
            seen.add(entry["icon"])
            out.append((entry["src"], entry["icon"], "", "", ICON_MAX))
    for entry in data.get("entities", []):
        if entry.get("src") and entry.get("icon") and entry["icon"] not in seen:
            seen.add(entry["icon"])
            out.append((entry["src"], entry["icon"], entry.get("zh", ""), entry.get("en", ""), ITEM_ICON_MAX))
    return out


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
    parser.add_argument("--only", choices=["codex", "campaign", "icons", "items"],
                        help="只重新转换这一组；其余组按已有产物登记，不重新编码"
                             "（避免换了 Pillow 版本后整批 WebP 字节变化）")
    args = parser.parse_args()

    official = load_official_names()
    manifest = {"codex": [], "campaign": [], "icons": [], "ui": [], "items": []}
    total_bytes = 0
    missing = []
    # 某一组「只登记不重转」：沿用 convert() 的 check_only 分支，只认已有产物
    keep = lambda group: args.check or (args.only is not None and args.only != group)

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
                            "codex/" + key.replace("_", "-"), PORTRAIT_MAX, keep("codex"))
        if url is None:
            missing.append(name)
            continue
        total_bytes += size
        manifest["codex"].append({"key": key, "src": url, "zh": zh, "en": en})

    # ── 战役海报与立绘 ────────────────────────────────────────────
    for fname, rel, zh, en in CAMPAIGN_FIGURES:
        url, size = convert(os.path.join(ASSETS, "ui", "Campaign", fname),
                            rel, POSTER_MAX, keep("campaign"))
        if url is None:
            missing.append(fname)
            continue
        total_bytes += size
        manifest["campaign"].append({"key": rel.split("/")[-1], "src": url, "zh": zh, "en": en})

    # ── 建筑与物品图标 ────────────────────────────────────────────
    for relsrc, rel, zh, en in ICON_FIGURES:
        url, size = convert(os.path.join(ASSETS, relsrc), rel, ICON_MAX, keep("icons"))
        if url is None:
            missing.append(relsrc)
            continue
        total_bytes += size
        manifest["icons"].append({"key": rel.split("/")[-1], "src": url, "zh": zh, "en": en})

    # ── 在线 Wiki 导航图标（边车清单直通）────────────────────────
    for entry in load_ui_sidecar():
        # src 形如 /images/ui/xxx.webp，而 OUT_DIR 本身就是 .../public/images
        rel = entry["src"][len("/images/"):]
        path = os.path.join(OUT_DIR, rel.replace("/", os.sep))
        if not os.path.isfile(path):
            missing.append(entry["src"].lstrip("/"))
            continue
        total_bytes += os.path.getsize(path)
        manifest["ui"].append(entry)

    # ── 正文实体链接的物品图标（wiki-entities.json）──────────────
    for relsrc, key, zh, en, edge in load_entity_icons():
        url, size = convert(os.path.join(ASSETS, relsrc.replace("/", os.sep)),
                            "items/" + key, edge, keep("items"))
        if url is None:
            missing.append(relsrc)
            continue
        total_bytes += size
        manifest["items"].append({"key": key, "src": url, "zh": zh, "en": en})

    ok_icon = build_favicon(args.check or args.only is not None)

    if args.check:
        if missing or not ok_icon:
            print("build_wiki_images: FAIL - 缺少产物 "
                  + str(len(missing)) + " 个" + ("，favicon 缺失" if not ok_icon else ""))
            for m in missing[:10]:
                print("  " + m)
            return 1
        print("build_wiki_images: OK - 产物齐全（%d 图鉴 / %d 战役 / %d 图标 / %d 站点图标 / %d 物品），共 %.1f MB"
              % (len(manifest["codex"]), len(manifest["campaign"]),
                 len(manifest["icons"]), len(manifest["ui"]), len(manifest["items"]),
                 total_bytes / 1048576.0))
        return 0

    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(manifest, fh, ensure_ascii=False, indent=2, sort_keys=False)
        fh.write("\n")

    print("build_wiki_images: 写出 %d 图鉴立绘 / %d 战役图 / %d 图标 / %d 站点图标，合计 %.1f MB"
          % (len(manifest["codex"]), len(manifest["campaign"]),
             len(manifest["icons"]), len(manifest["ui"]), total_bytes / 1048576.0))
    print("  产物: wiki-site/docs/public/images/")
    print("  清单: wiki-site/scripts/image-manifest.json")
    if missing:
        print("  跳过（源图缺失）: " + ", ".join(missing[:8]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
