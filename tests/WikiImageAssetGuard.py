# -*- coding: utf-8 -*-
"""WikiImageAssetGuard - 在线 Wiki 配图的清单、产物与引用三者必须对得上。

背景：
    在线 Wiki 的配图走「离线转换 + 产物进仓库」这条路（见 tools/build_wiki_images.py）：
    源图在 /Assets/* 下被 .gitignore 挡着，GitHub Actions 构建时根本看不到，
    所以 wiki-site/docs/public/images/ 下的 WebP 与 wiki-site/scripts/image-manifest.json
    都是**提交进仓库的产物**。

    三者任何一处漂移都不会报错，只会在线上悄悄变成裂图或空白：
      - 清单里有、产物没有  -> 线上 404 裂图
      - 产物有、清单没有    -> 白占仓库体积，且没有任何页面会用到
      - sync 脚本 IMAGE_PLACEMENT 里引用的 key 在清单里不存在 -> 那一处配图静默消失
        （injectImages 只 console.warn，退化成不配图，同步照样 exit 0）

    最后一条是最阴的：改名/删图之后 sync 依旧绿，页面就是少了张图。

判据：
    1. 清单里每条 src 都能在 wiki-site/docs/public/ 下找到真实文件；
    2. public/images 下每个 .webp 都被清单收录（无孤儿产物）；
    3. sync-content.mjs 的 IMAGE_PLACEMENT 引用的每个 key / group 都在清单里存在；
    4. 清单非空（清单被清空时应当红，而不是"零条也算通过"）。

    本 guard **不**校验图片内容或尺寸——那是 build_wiki_images.py 的职责。
"""
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MANIFEST = os.path.join(REPO_ROOT, "wiki-site", "scripts", "image-manifest.json")
PUBLIC_DIR = os.path.join(REPO_ROOT, "wiki-site", "docs", "public")
IMAGES_DIR = os.path.join(PUBLIC_DIR, "images")
SYNC_SCRIPT = os.path.join(REPO_ROOT, "wiki-site", "scripts", "sync-content.mjs")

PLACEMENT_RE = re.compile(r"const\s+IMAGE_PLACEMENT\s*=\s*\{", re.S)
KEYS_RE = re.compile(r"keys:\s*\[([^\]]*)\]")
GROUP_RE = re.compile(r"group:\s*'([^']+)'")


def fail(message):
    print("WikiImageAssetGuard: FAIL - " + message)
    return 1


def extract_placement_block(text):
    """截出 IMAGE_PLACEMENT = { ... } 的字面量体，按大括号配平。"""
    m = PLACEMENT_RE.search(text)
    if not m:
        return None
    start = text.index("{", m.start())
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    return None


def main():
    if not os.path.isfile(MANIFEST):
        return fail("配图清单缺失：wiki-site/scripts/image-manifest.json"
                    "（跑 python tools/build_wiki_images.py 生成）")

    with open(MANIFEST, "r", encoding="utf-8") as handle:
        try:
            manifest = json.load(handle)
        except ValueError as exc:
            return fail("image-manifest.json 不是合法 JSON: " + str(exc))

    if not isinstance(manifest, dict):
        return fail("image-manifest.json 顶层应当是对象（分组名 -> 条目数组）")

    # ── 1. 清单 -> 产物 ────────────────────────────────────────────
    known_keys = set()
    referenced = set()
    entry_count = 0
    for group, items in manifest.items():
        if not isinstance(items, list):
            return fail("分组 " + group + " 的值不是数组")
        for item in items:
            entry_count += 1
            src = item.get("src", "")
            key = item.get("key", "")
            if not src or not key:
                return fail("分组 " + group + " 里有条目缺 src 或 key")
            known_keys.add(key)
            rel = src.lstrip("/")
            abs_path = os.path.join(PUBLIC_DIR, rel.replace("/", os.sep))
            if not os.path.isfile(abs_path):
                return fail("清单条目 " + key + " 指向的产物不存在: " + src
                            + "（跑 python tools/build_wiki_images.py 重建）")
            referenced.add(os.path.normcase(os.path.abspath(abs_path)))

    if entry_count == 0:
        return fail("配图清单是空的——若确实不再配图，请同时移除本 guard 与 IMAGE_PLACEMENT")

    # ── 2. 产物 -> 清单（孤儿检测）──────────────────────────────────
    orphans = []
    if os.path.isdir(IMAGES_DIR):
        for current, _dirs, files in os.walk(IMAGES_DIR):
            for name in files:
                if not name.endswith(".webp"):
                    continue
                full = os.path.normcase(os.path.abspath(os.path.join(current, name)))
                if full not in referenced:
                    orphans.append(os.path.relpath(full, PUBLIC_DIR).replace(os.sep, "/"))
    if orphans:
        return fail(str(len(orphans)) + " 个产物没有被清单收录（白占体积）: "
                    + ", ".join(sorted(orphans)[:6]))

    # ── 3. sync 脚本引用的 key / group 必须存在 ──────────────────────
    if not os.path.isfile(SYNC_SCRIPT):
        return fail("sync-content.mjs 不存在，配图注入链已断")
    with open(SYNC_SCRIPT, "r", encoding="utf-8") as handle:
        sync_text = handle.read()

    block = extract_placement_block(sync_text)
    if block is None:
        return fail("sync-content.mjs 里找不到 IMAGE_PLACEMENT 字面量，"
                    "配图注入可能已被移除或改名，本 guard 需同步")

    missing_keys = []
    for raw in KEYS_RE.findall(block):
        for token in re.findall(r"'([^']+)'", raw):
            if token not in known_keys:
                missing_keys.append(token)
    missing_groups = [g for g in GROUP_RE.findall(block) if g not in manifest]

    if missing_keys or missing_groups:
        detail = []
        if missing_keys:
            detail.append("key " + ", ".join(sorted(set(missing_keys))))
        if missing_groups:
            detail.append("group " + ", ".join(sorted(set(missing_groups))))
        return fail("IMAGE_PLACEMENT 引用了清单里没有的 " + "；".join(detail)
                    + "。sync 只会 warn 并静默跳过这处配图")

    print("WikiImageAssetGuard: PASS - " + str(entry_count) + " 条配图，"
          + str(len(manifest)) + " 个分组，产物与 IMAGE_PLACEMENT 引用全部对齐")
    return 0


if __name__ == "__main__":
    sys.exit(main())
