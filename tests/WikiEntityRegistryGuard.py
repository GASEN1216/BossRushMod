# -*- coding: utf-8 -*-
"""WikiEntityRegistryGuard - 正文实体链接的登记表（wiki-site/scripts/wiki-entities.json）必须落得了地。

背景：
    在线 Wiki 正文里提到物品、装备部件、人物时，config.mts 的 entityLinkPlugin 会把名字换成
    「游戏图标 + 链接」，链接落到目标页里写这个东西的那一节（同名标题，或以它开头的加粗列表项，
    渲染那一页时给该列表项补 id）。三类漂移都**不报错**，只会让链接悄悄变差：
      - 目标页里找不到落点        -> 链接只落到页顶，读者还得自己翻；
      - 图标 key 不在配图清单里   -> 名字前没有图标（与 structure.mts 的图标同一个坑）；
      - 中英名在两种语言里撞名    -> 后登记的那条永远不生效。

判据：
    1. 每条 page 都是 structure.mts 里存在的路径；
    2. 每条 icon 都在 image-manifest.json 里；带 src 的，清单 items 组里有同 key 的产物；
    3. 每条中 / 英名在目标页对应语言的源文件里有落点：同名小节标题、以它开头的加粗列表项、
       「A / B」并列中的一项，或 anchorZh / anchorEn 指定的小节；
    4. 同一语言内名字不重复，且不与 structure.mts 的条目名重复（条目名优先，登记了也白登）。

    落点规则与 config.mts 的 namesTheSpot() 是同一套：「赤龙首（头盔）」「折翎 / 无声钟守」
    「浮舟（登云码头）· 渡口整备」分别算赤龙首、折翎、浮舟的落点。
"""
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITE = os.path.join(REPO_ROOT, "wiki-site")
REGISTRY = os.path.join(SITE, "scripts", "wiki-entities.json")
MANIFEST = os.path.join(SITE, "scripts", "image-manifest.json")
STRUCTURE = os.path.join(SITE, "docs", ".vitepress", "data", "structure.mts")
ENTRY_MAP = os.path.join(SITE, "scripts", "entry-map.mjs")
HUB_DIR = os.path.join(SITE, "hubs")

DELIMS = ["（", "(", " · ", "·", " / ", "：", ":", " "]


def fail(message):
    print("WikiEntityRegistryGuard: FAIL - " + message)
    return 1


def read(path):
    with open(path, "r", encoding="utf-8-sig") as fh:
        return fh.read()


def norm(p):
    return p.rstrip("/") or "/"


def page_sources():
    text = read(ENTRY_MAP)
    out = {}
    for entry_id, route in re.findall(r"'([a-z_0-9]+)':\s*'([^']+\.md)'", text):
        canonical = "/" + re.sub(r"(/index)?\.md$", "", route)
        sub = entry_id.split("__", 1)[0]
        for lang in ("zh", "en"):
            for cand in (os.path.join(REPO_ROOT, "WikiContent", lang, sub, entry_id + ".md"),
                         os.path.join(REPO_ROOT, "WikiContent", lang, entry_id + ".md")):
                if os.path.isfile(cand):
                    out.setdefault(canonical, {})[lang] = cand
                    break
    for name in os.listdir(HUB_DIR):
        m = re.match(r"(\w+)\.(zh|en)\.md$", name)
        if m:
            out.setdefault("/" + m.group(1), {})[m.group(2)] = os.path.join(HUB_DIR, name)
    return out


def spots(path):
    """(小节标题列表 —— 不含页面标题, 加粗列表项开头列表)"""
    heads, items = [], []
    first = True
    for line in read(path).splitlines():
        h = re.match(r"^#{2,6}\s+(.+?)\s*$", line)
        if h:
            if first:
                first = False  # WikiContent 的 ## 是页面标题
                continue
            heads.append(re.sub(r"\*\*|`", "", h.group(1)).strip())
            continue
        li = re.match(r"^\s*- \*\*(.+?)\*\*", line)
        if li:
            items.append(li.group(1).strip())
    return heads, items


def names_the_spot(text, name):
    if text == name:
        return True
    if name in re.split(r"\s*/\s*", text):
        return True
    return any(text.startswith(name + d) for d in DELIMS)


def main():
    for path in (REGISTRY, MANIFEST, STRUCTURE, ENTRY_MAP):
        if not os.path.isfile(path):
            return fail("缺文件 " + os.path.relpath(path, REPO_ROOT))
    data = json.loads(read(REGISTRY))
    entities = data.get("entities", [])
    if len(entities) < 20:
        return fail("登记表只有 %d 条，被清空了？" % len(entities))

    structure_src = read(STRUCTURE)
    structure_paths = {norm(p) for p in re.findall(r"^\s*path:\s*'([^']+)'", structure_src, re.M)}
    structure_names = {"zh": set(re.findall(r"^\s*zh:\s*'([^']+)'", structure_src, re.M)),
                       "en": set(re.findall(r"^\s*en:\s*[\"']([^\"']+)[\"']", structure_src, re.M))}
    manifest = json.loads(read(MANIFEST))
    known_icons = {item["key"] for group in manifest.values() for item in group}
    item_icons = {item["key"] for item in manifest.get("items", [])}
    sources = page_sources()

    problems = []
    seen = {"zh": {}, "en": {}}
    for idx, e in enumerate(data.get("pageIcons", [])):
        if e.get("icon") not in item_icons:
            problems.append("pageIcons[%d] %s 没有转出产物（重跑 build_wiki_images.py --only items）" % (idx, e.get("icon")))
    for idx, e in enumerate(entities):
        label = e.get("zh") or e.get("en") or ("#%d" % idx)
        page = norm(e.get("page", ""))
        if page not in structure_paths:
            problems.append("%s：page %s 不在 structure.mts 里" % (label, e.get("page")))
            continue
        icon = e.get("icon")
        if icon and icon not in known_icons:
            problems.append("%s：图标 %s 不在 image-manifest.json 里" % (label, icon))
        if e.get("src") and icon not in item_icons:
            problems.append("%s：src 已登记但 items 组里没有产物 %s" % (label, icon))
        src = sources.get(page, {})
        for lang in ("zh", "en"):
            name = e.get(lang)
            if not name:
                continue
            if name in seen[lang]:
                problems.append("%s 名「%s」登记了两次（第二条永远不生效）" % (lang, name))
            seen[lang][name] = idx
            if name in structure_names[lang]:
                problems.append("%s 名「%s」与 structure.mts 的条目重名，登记表这条不会生效" % (lang, name))
            if lang not in src:
                problems.append("%s：%s 找不到 %s 版源文件" % (label, page, lang))
                continue
            heads, items = spots(src[lang])
            anchor = e.get("anchorZh" if lang == "zh" else "anchorEn")
            ok = (any(names_the_spot(h, name) for h in heads)
                  or any(names_the_spot(i, name) for i in items)
                  or (anchor is not None and anchor in heads))
            if not ok:
                problems.append("%s 名「%s」在 %s 里没有落点（同名小节或以它开头的加粗列表项）"
                                % (lang, name, os.path.relpath(src[lang], REPO_ROOT)))
    if problems:
        return fail("登记表有 %d 处问题：\n  %s" % (len(problems), "\n  ".join(problems[:30])))
    print("WikiEntityRegistryGuard: OK - %d 条实体，中 %d / 英 %d 个名字都在目标页上有落点"
          % (len(entities), len(seen["zh"]), len(seen["en"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
