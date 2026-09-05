# -*- coding: utf-8 -*-
"""WikiSiteStructureGuard - 在线 Wiki 的导航结构、生成页面与图标三者必须对得上。

背景：
    在线站的导航（顶栏 / 侧栏 / 首页门户 / 页尾同类导航 / 面包屑）现在全部由
    wiki-site/docs/.vitepress/data/structure.mts 一处生成，而页面本身由
    wiki-site/scripts/sync-content.mjs 从 WikiContent/ 同步过来——**两条独立的链路**。

    两边漂移不会有任何报错，只会在线上悄悄坏掉：
      - structure 里写了页面没有的路径 -> 侧栏 / 宫格里一个点进去 404 的链接；
      - 页面存在但 structure 没收录 -> 这一页从所有导航里消失，只能靠搜索撞到；
      - icon 写错 key                 -> 组件按设计**静默**退化成首字母字牌，
                                         看起来「只是还没配图」，实际是打错字。

    第三条最阴：WikiIcon.vue 缺图标不报错是有意为之（结构先行、图标后补），
    所以打错的 key 和还没出的图长得一模一样。这里用「必须已在清单里，
    或已在 gen_wiki_icons.py 的待生成清单里」把两者区分开。

判据：
    1. structure.mts 里每个 path 都能在 wiki-site/docs/ 与 docs/en/ 找到对应 .md；
    2. sync 生成的内容页（更新日志除外）都被 structure.mts 收录；
    3. structure.mts 与 infobox.mts 引用的每个 icon key，
       要么在 image-manifest.json 里，要么在 tools/gen_wiki_icons.py 的 ICONS 里；
    4. infobox.mts 的每个条目路径、以及它 links 里的每个路径，都存在于 structure.mts。

    本 guard **不**校验速查框里的数值对不对——它没法判断正文与速查框哪边才是对的，
    那是人工核对的事（见 infobox.mts 顶部注释）。
"""
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITE = os.path.join(REPO_ROOT, "wiki-site")
DOCS = os.path.join(SITE, "docs")
STRUCTURE = os.path.join(DOCS, ".vitepress", "data", "structure.mts")
INFOBOX = os.path.join(DOCS, ".vitepress", "data", "infobox.mts")
MANIFEST = os.path.join(SITE, "scripts", "image-manifest.json")
GEN_ICONS = os.path.join(REPO_ROOT, "tools", "gen_wiki_icons.py")

# sync-content.mjs 管理的目录（CONTENT_DIRS）中，更新日志条目太多且自动排序，
# 由 config.mts 直接读 catalog.tsv 生成，不进 structure.mts。
SKIP_DIRS = {"changelog"}

PATH_RE = re.compile(r"^\s*path:\s*'([^']+)'", re.M)
ICON_RE = re.compile(r"^\s*icon:\s*'([^']+)'", re.M)
INFOBOX_KEY_RE = re.compile(r"^  '(/[^']*)':\s*\{", re.M)
LINKS_RE = re.compile(r"links:\s*\[(.*?)\]", re.S)
QUOTED_RE = re.compile(r"'([^']+)'")
GEN_KEY_RE = re.compile(r'^\s*\("([a-z0-9-]+)",\s*"', re.M)


def fail(message):
    print("WikiSiteStructureGuard: FAIL - " + message)
    return 1


def read(path):
    with open(path, "r", encoding="utf-8") as fh:
        return fh.read()


def route_to_file(route):
    """'/bosses/' -> 'bosses/index.md'；'/bosses/x' -> 'bosses/x.md'；'/easter-eggs' -> 'easter-eggs.md'"""
    rel = route.lstrip("/")
    if rel == "" or rel.endswith("/"):
        return os.path.join(rel.replace("/", os.sep), "index.md")
    return rel.replace("/", os.sep) + ".md"


def collect_generated_pages():
    """列出 sync 生成的中文内容页，返回规范路径集合。"""
    routes = set()
    for dirpath, dirnames, filenames in os.walk(DOCS):
        rel_dir = os.path.relpath(dirpath, DOCS)
        top = rel_dir.split(os.sep)[0]
        if top in (".vitepress", "public", "en") or top in SKIP_DIRS:
            dirnames[:] = []
            continue
        for name in filenames:
            if not name.endswith(".md"):
                continue
            rel = os.path.relpath(os.path.join(dirpath, name), DOCS).replace(os.sep, "/")
            if rel == "index.md":
                continue  # 首页是门户组件，不是同步来的内容页
            if rel.endswith("/index.md"):
                routes.add("/" + rel[: -len("index.md")])
            else:
                routes.add("/" + rel[: -len(".md")])
    return routes


def norm(route):
    return route.rstrip("/") if len(route) > 1 else route


def main():
    for path in (STRUCTURE, INFOBOX, MANIFEST, GEN_ICONS):
        if not os.path.isfile(path):
            return fail("缺少文件 -> " + os.path.relpath(path, REPO_ROOT))

    structure_src = read(STRUCTURE)
    infobox_src = read(INFOBOX)

    declared = [r for r in PATH_RE.findall(structure_src) if r.startswith("/")]
    if not declared:
        return fail("structure.mts 里一个 path 都没解析到，正则与文件写法已经不匹配")
    declared_set = {norm(r) for r in declared}

    # 1. 声明的路径都得有页面（中英各一份）
    for route in sorted(set(declared)):
        rel = route_to_file(route)
        for base, label in ((DOCS, "zh"), (os.path.join(DOCS, "en"), "en")):
            if not os.path.isfile(os.path.join(base, rel)):
                return fail("structure.mts 声明了 %s，但 %s 页面不存在 -> %s"
                            % (route, label, rel.replace(os.sep, "/")))

    # 2. 生成的页面都得被收录
    orphans = sorted(r for r in collect_generated_pages() if norm(r) not in declared_set)
    if orphans:
        return fail("这些页面生成了却没进 structure.mts，导航里到不了：" + ", ".join(orphans))

    # 3. icon key 要么已有产物，要么已排进生成清单
    manifest = json.loads(read(MANIFEST))
    known_icons = set()
    for group in manifest.values():
        for item in group or []:
            known_icons.add(item.get("key"))
    known_icons |= set(GEN_KEY_RE.findall(read(GEN_ICONS)))

    used_icons = set(ICON_RE.findall(structure_src))
    unknown = sorted(k for k in used_icons if k not in known_icons)
    if unknown:
        return fail("structure.mts 引用了既不在 image-manifest.json、也不在 "
                    "gen_wiki_icons.py 待生成清单里的 icon key（多半是打错了）："
                    + ", ".join(unknown))

    # 4. infobox 的条目路径与 links 都要在 structure.mts 里
    for route in INFOBOX_KEY_RE.findall(infobox_src):
        if norm(route) not in declared_set:
            return fail("infobox.mts 给 %s 配了速查框，但 structure.mts 里没有这个条目" % route)

    for block in LINKS_RE.findall(infobox_src):
        for route in QUOTED_RE.findall(block):
            if norm(route) not in declared_set:
                return fail("infobox.mts 的 links 指向 %s，但 structure.mts 里没有这个条目" % route)

    print("WikiSiteStructureGuard: PASS - %d 个条目、%d 个图标 key、%d 个速查框，"
          "结构 / 页面 / 图标三方一致"
          % (len(declared_set), len(used_icons), len(INFOBOX_KEY_RE.findall(infobox_src))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
