# -*- coding: utf-8 -*-
"""WikiThemeAssetGuard - 在线 Wiki 版式贴图的在场、归属与预算。

背景：
    换皮之后，站点的「材质」不再是 CSS 画出来的，而是 theme/assets/ 下的几张图：
    天空底图、部件木纹 / 冰霜、草皮条、站点 Logo。它们由本机的
    tools/build_wiki_theme_assets.py 产出（源图在 Assets/wiki_theme/，local-only），
    清单落在 wiki-site/scripts/wiki-theme-assets.json。

    这条链上有四种断法，全都不会让构建报错：

      1. **贴图丢了**：CSS 里 url(../assets/sky-overworld.webp) 还在，文件没了。
         Vite 会在构建期喊 "Could not resolve"，但如果只是**重命名**，
         旧名字仍在 tokens.css 的另一套皮肤块里，构建照过、那套皮肤全站没底图。
      2. **孤儿产物**：删了 CSS 引用但文件还躺在 assets/ 里，
         仓库越背越重，谁也不知道它还有没有用。
      3. **归属写错**：把 sky-snow.webp 记成 Overworld 皮肤的，
         预算就按错的皮肤算——默认皮肤实际下载了 400 KB 却显示「没超」。
         浏览器只请求**当前皮肤真的用到**的 url()，所以归属必须以
         tokens.css 里它写在哪个块为准，而不是以清单里手写的为准。
      4. **预算失守**：换一张没压过的天空图，首屏多背 2 MB。
         页面底图是 background-attachment: fixed 的全屏图，它变大读者立刻有感。

    另外守一条与贴图无关但同样容易悄悄回潮的事：换皮的净收益之一是
    **零网络字体**（原来 14 个 Inter woff2 + 3 条 Google Fonts 外链）。
    任何人往 config.mts 或主题里加一条 fonts.googleapis.com 就前功尽弃。

判据：
    1. theme/css/*.css 与 theme/**/*.vue 里每个非 data: 的 url(...) 都指向
       theme/assets/ 下真实存在的文件；
    2. theme/assets/ 里每个文件都被 CSS 引用到，且都在清单里（无孤儿、无漏登记）；
    3. 清单记的字节数与磁盘一致（改了图不刷新清单 = 预算失效）；
    4. 清单记的 skins 与 tokens.css 里的实际归属一致
       （:root 块 = 默认皮肤，html.theme-<Name> 块 = 那套皮肤，块外 = 所有皮肤）；
    5. 每套皮肤实际会下载的版式贴图总字节 <= 600000，单张天空 <= 200000；
    6. config.mts 与 theme/** 不含 fonts.googleapis.com / fonts.gstatic.com；
    7. gen_codex_art.py / gen_wiki_icons.py / build_wiki_theme_assets.py 三者的
       STYLE 与 DUCK 逐字相同（同一页面上的角色美术不能有两套画风）。

只用标准库、只读文件大小，不需要 Pillow —— CI 上没有 Pillow 也要跑得动。
"""
import ast
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VP = os.path.join(REPO_ROOT, "wiki-site", "docs", ".vitepress")
THEME = os.path.join(VP, "theme")
CSS_DIR = os.path.join(THEME, "css")
ASSET_DIR = os.path.join(THEME, "assets")
TOKENS = os.path.join(CSS_DIR, "tokens.css")
CONFIG = os.path.join(VP, "config.mts")
THEMES = os.path.join(VP, "data", "themes.mts")
SIDECAR = os.path.join(REPO_ROOT, "wiki-site", "scripts", "wiki-theme-assets.json")

# 站上的角色美术分三个脚本产出，但读者是在同一个页面上同时看到它们的：
#   gen_codex_art.py          -> 图鉴立绘（docs/public/images/codex/）
#   gen_wiki_icons.py         -> 类目与条目图标（docs/public/images/ui/）
#   build_wiki_theme_assets.py-> 站点 Logo 的徽记（theme/assets/logo.webp）
# 三者的风格串必须逐字相同，否则画风会打架 —— 2026-09-07 就是徽记改成赛璐璐平涂，
# 摆在厚涂的图标旁边一眼看出是两个世界，owner 直接打回。
ART_SCRIPTS = (
    os.path.join(REPO_ROOT, "tools", "gen_codex_art.py"),
    os.path.join(REPO_ROOT, "tools", "gen_wiki_icons.py"),
    os.path.join(REPO_ROOT, "tools", "build_wiki_theme_assets.py"),
)
SHARED_ART_CONSTS = ("STYLE", "DUCK")

# 与 tools/build_wiki_theme_assets.py 的同名常量一一对应，改一处必须改两处。
BUDGET_SKIN_TOTAL = 600000
BUDGET_SINGLE_SKY = 200000

URL_RE = re.compile(r"url\(\s*(['\"]?)([^'\")]+)\1\s*\)")
COMMENT_RE = re.compile(r"/\*.*?\*/", re.S)
BLOCK_RE = re.compile(r"^(:root|html\.theme-[\w-]+)\s*\{", re.M)
DEFAULT_RE = re.compile(r"DEFAULT_THEME\s*=\s*'([^']+)'")
THEMES_NAME_RE = re.compile(r"\{\s*name:\s*'([^']+)'")
WEBFONT_HOSTS = ("fonts.googleapis.com", "fonts.gstatic.com")


def fail(message):
    print("WikiThemeAssetGuard: FAIL - " + message)
    return 1


def read(path):
    """读文件并去掉块注释。

    注释里的 url() 不算引用——否则「先注释掉再说」的改法能骗过在场检查：
    贴图已经没人用了，guard 还以为它是活的。"""
    with open(path, "r", encoding="utf-8") as fh:
        return COMMENT_RE.sub(" ", fh.read())


def read_raw(path):
    with open(path, "r", encoding="utf-8") as fh:
        return fh.read()


def walk(root, suffixes):
    for base, _dirs, names in os.walk(root):
        for name in sorted(names):
            if name.endswith(suffixes):
                yield os.path.join(base, name)


def asset_urls(text):
    """取出所有非 data: 的 url() 目标文件名（只关心 basename，路径由 §1 单独核）。"""
    found = []
    for _quote, target in URL_RE.findall(text):
        target = target.strip()
        if target.startswith("data:") or target.startswith("#"):
            continue
        found.append(target)
    return found


def blocks_of(css):
    """把 tokens.css 切成 [(选择器, 块内文本)]，用来判断一张图属于哪套皮肤。"""
    out = []
    for matched in BLOCK_RE.finditer(css):
        start = css.index("{", matched.start()) + 1
        depth = 1
        i = start
        while i < len(css) and depth:
            if css[i] == "{":
                depth += 1
            elif css[i] == "}":
                depth -= 1
            i += 1
        out.append((matched.group(1), css[start:i - 1]))
    return out


def art_constant(src, name):
    """取 `NAME = ( "..." "..." )` 这种拼接字符串常量的值。

    用 ast 求值而不是正则比字符串：脚本里这些串是折行拼接的，
    换行位置和缩进随时会变，但**值**不该变，要比的正是值。
    """
    matched = re.search(r"^" + name + r"\s*=\s*(\(.*?\))\s*$", src, re.S | re.M)
    if not matched:
        return None
    try:
        return ast.literal_eval(matched.group(1))
    except (SyntaxError, ValueError):
        return None


def declarations(body):
    """把一个块的正文切成 {自定义属性: 值}。值可能跨行（天空图那条就是两行）。"""
    props = {}
    for chunk in body.split(";"):
        name, sep, value = chunk.partition(":")
        name = name.strip()
        if sep and name.startswith("--"):
            props[name] = value.strip()
    return props


def main():
    for path in (TOKENS, CONFIG, THEMES):
        if not os.path.isfile(path):
            return fail("缺文件：" + os.path.relpath(path, REPO_ROOT))
    if not os.path.isdir(ASSET_DIR):
        return fail("没有 theme/assets/ 目录。版式贴图由本机的 "
                    "tools/build_wiki_theme_assets.py 产出，产物要提交")
    if not os.path.isfile(SIDECAR):
        return fail("缺清单 wiki-site/scripts/wiki-theme-assets.json。"
                    "没有它就无法校验产物是否齐全、是否超预算（源图在别人机器上不存在）")

    themes_src = read(THEMES)
    default_match = DEFAULT_RE.search(themes_src)
    if not default_match:
        return fail("data/themes.mts 里找不到 DEFAULT_THEME")
    default_theme = default_match.group(1)
    all_themes = set(THEMES_NAME_RE.findall(themes_src))
    if default_theme not in all_themes:
        return fail("DEFAULT_THEME=%s 不在 THEMES 里" % default_theme)

    # ── 1. 每个 url() 都能落到实处 ────────────────────────────────
    sources = list(walk(CSS_DIR, (".css",))) + list(walk(THEME, (".vue",)))
    referenced = set()
    for path in sources:
        rel = os.path.relpath(path, REPO_ROOT)
        for target in asset_urls(read(path)):
            name = os.path.basename(target)
            if "assets/" not in target.replace("\\", "/"):
                return fail("%s 里的 url(%s) 不在 theme/assets/ 下。版式贴图必须放那儿，"
                            "才会被 Vite 打哈希并自动补 base 前缀；写死在 public/ 的路径"
                            "换 base 部署时会 404" % (rel, target))
            if not os.path.isfile(os.path.join(ASSET_DIR, name)):
                return fail("%s 引用了 theme/assets/%s，文件不存在。"
                            "重命名贴图时另一套皮肤的引用最容易漏改——"
                            "那套皮肤会整站没有底图，而构建不会报错" % (rel, name))
            referenced.add(name)

    # ── 2. 无孤儿、无漏登记 ──────────────────────────────────────
    on_disk = set(n for n in os.listdir(ASSET_DIR) if os.path.isfile(os.path.join(ASSET_DIR, n)))
    orphans = on_disk - referenced
    if orphans:
        return fail("theme/assets/ 里有没人引用的文件：%s。删掉，或在 CSS 里用起来——"
                    "仓库不该背着谁也不知道还有没有用的图" % ", ".join(sorted(orphans)))

    sidecar = json.loads(read(SIDECAR))
    entries = {e["file"]: e for e in sidecar.get("files", [])}
    if set(entries) != on_disk:
        return fail("清单与 theme/assets/ 对不上：清单多 %s，磁盘多 %s。"
                    "重跑 python tools/build_wiki_theme_assets.py 刷新清单"
                    % (sorted(set(entries) - on_disk), sorted(on_disk - set(entries))))

    # ── 3. 字节数与磁盘一致 ──────────────────────────────────────
    for name, entry in sorted(entries.items()):
        actual = os.path.getsize(os.path.join(ASSET_DIR, name))
        if actual != entry.get("bytes"):
            return fail("theme/assets/%s 实际 %d 字节，清单写的是 %s。"
                        "换了图没刷新清单，预算校验就形同虚设——"
                        "重跑 python tools/build_wiki_theme_assets.py"
                        % (name, actual, entry.get("bytes")))

    # ── 4. 归属以 tokens.css 的实际写法为准 ──────────────────────
    # 级联要照算：:root 里的值是所有皮肤的默认，某套皮肤在自己的块里重新赋值才算覆盖。
    # 所以「sky-overworld.webp 属于谁」= 逐套皮肤解出 --theme-site-background-image
    # 的最终值，看它引不引用这张图 —— 而不是看它写在哪个块里。
    tokens_css = read(TOKENS)
    root_props, skin_props = {}, {}
    for selector, body in blocks_of(tokens_css):
        props = declarations(body)
        if selector == ":root":
            root_props.update(props)
        else:
            skin_props.setdefault(selector.split("html.theme-", 1)[1], {}).update(props)

    actual_skins = dict((name, set()) for name in on_disk)
    for skin in all_themes:
        resolved = dict(root_props)
        resolved.update(skin_props.get(skin, {}))
        for value in resolved.values():
            for target in asset_urls(value):
                actual_skins.setdefault(os.path.basename(target), set()).add(skin)
    # tokens.css 之外的引用（layout.css 等）对所有皮肤都生效
    for path in sources:
        if os.path.abspath(path) == os.path.abspath(TOKENS):
            continue
        for target in asset_urls(read(path)):
            actual_skins.setdefault(os.path.basename(target), set()).update(all_themes)

    for name, entry in sorted(entries.items()):
        listed = set(entry.get("skins", []))
        real = actual_skins.get(name, set())
        if not real:
            return fail("theme/assets/%s 在清单里，但 tokens.css 与其它样式表都没引用它" % name)
        if listed != real:
            return fail("theme/assets/%s 的归属对不上：清单写 %s，tokens.css 里实际写在 %s 的块里。"
                        "浏览器只请求当前皮肤用到的 url()，归属错了预算就算在错的皮肤头上"
                        % (name, sorted(listed) or ["(空)"], sorted(real)))

    # ── 5. 预算 ──────────────────────────────────────────────────
    per_skin = {}
    for name, entry in entries.items():
        for skin in entry["skins"]:
            per_skin[skin] = per_skin.get(skin, 0) + entry["bytes"]
    for skin in sorted(per_skin):
        if per_skin[skin] > BUDGET_SKIN_TOTAL:
            return fail("%s 皮肤要下载 %d 字节版式贴图，预算 %d。"
                        "页面底图是 fixed 的全屏图，超了读者首屏立刻有感"
                        % (skin, per_skin[skin], BUDGET_SKIN_TOTAL))
    for name, entry in sorted(entries.items()):
        if name.startswith("sky-") and entry["bytes"] > BUDGET_SINGLE_SKY:
            return fail("theme/assets/%s 有 %d 字节，单张天空底图预算 %d"
                        % (name, entry["bytes"], BUDGET_SINGLE_SKY))

    # ── 6. 零网络字体 ────────────────────────────────────────────
    font_sources = [CONFIG] + list(walk(THEME, (".css", ".vue", ".ts")))
    for path in font_sources:
        text = read_raw(path)
        for host in WEBFONT_HOSTS:
            if host in text:
                return fail("%s 里出现了 %s。换皮的净收益之一是零网络字体"
                            "（原来 14 个 woff2 + 3 条外链），加回去就前功尽弃；"
                            "要自托管字体请放 theme/assets/ 并走 @font-face"
                            % (os.path.relpath(path, REPO_ROOT), host))

    # ── 7. 三个生图脚本的角色风格串逐字相同 ──────────────────
    #     徽记和类目图标、图鉴立绘会同时出现在一个页面上；风格串一分叉，
    #     画风就分叉。这条不看产物看源头——产物长什么样 guard 判断不了，
    #     但「用的是不是同一套串」是判得了的。
    baseline = {}
    for path in ART_SCRIPTS:
        if not os.path.isfile(path):
            return fail("缺 %s：站点美术的风格串对不起来了"
                        % os.path.relpath(path, REPO_ROOT))
        src = read_raw(path)
        for const in SHARED_ART_CONSTS:
            value = art_constant(src, const)
            if value is None:
                return fail("%s 里找不到 %s 常量（或它不是一个字符串字面量）。"
                            "三个生图脚本共用同一套角色风格串，本 guard 靠它比对；"
                            "写法变了要同步改 art_constant()"
                            % (os.path.relpath(path, REPO_ROOT), const))
            if const not in baseline:
                baseline[const] = (value, path)
                continue
            if value != baseline[const][0]:
                return fail("%s 的 %s 与 %s 的不一致。三处产出的角色美术会同时出现在"
                            "同一个页面上，风格串一分叉画风就打架（2026-09-07 实测："
                            "徽记改成平涂后摆在厚涂图标旁边一眼看出是两个世界）。"
                            % (os.path.relpath(path, REPO_ROOT), const,
                               os.path.relpath(baseline[const][1], REPO_ROOT)))

    print("WikiThemeAssetGuard: PASS - %d 张版式贴图在位（%s），"
          "各皮肤预算 %s，零网络字体，三个生图脚本的风格串一致"
          % (len(entries), ", ".join(sorted(entries)),
             " / ".join("%s %.0fKB" % (s, per_skin[s] / 1024.0) for s in sorted(per_skin))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
