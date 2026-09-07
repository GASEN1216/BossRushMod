# -*- coding: utf-8 -*-
"""WikiThemeTokensGuard - 在线 Wiki 皮肤令牌的契约。

背景：
    2026-09-07 换皮之后，站点的全部颜色与尺寸都收进了 theme/css/tokens.css 的
    --theme-* / --layout-* 令牌，其余 css 与组件只准 var() 引用、不准写字面值。
    这套约定断掉之后**不会有任何报错**，只会在线上悄悄变样：

    一、引用了没定义的令牌。
        `var(--theme-foo)` 拼错一个字母，CSS 里该属性变成「无效值」，
        元素回退到继承或初始值——多半是透明底、黑字，看起来像「这块没写样式」。

    二、浅色皮肤漏了某个令牌。
        Snow 只覆盖它需要改的那些，其余继承 :root。但**颜色类**的令牌几乎每个
        都得覆盖：漏一个就是「浅底上留着一处深色皮肤的值」，例如浅色页面里
        突然有一行 #eae3d1 的浅米字，几乎看不见。

    三、稀有度规则被搬进组件的 <style>。
        .wiki-rarity / [data-tier] 有五处消费者（信息框、对比表、宫格、
        页尾导航、正文实体链接）。写进任何一个组件，其余四处就开始依赖
        「那个块恰好没加 scoped」——哪天有人补上 scoped，另外四处静默变灰。
        AGENTS.md §4.6 / §4.7 写死了这条，这里把它机械化。

    四、useWiki.ts 被拖进 .vue / .css 依赖。
        scripts/check-navigation.mjs 用 esbuild 单独打包它做路由回归，
        没有配 vue / css 的 loader，一旦 useWiki 直接或间接 import 了组件，
        那条回归会以一句难懂的 esbuild 报错挂掉。

判据：
    1. theme/css/*.css 与 theme/**/*.vue 里出现的每个 --theme-* / --layout-* /
       --font-* / --wikigg-* / --icon- 令牌，都能在 tokens.css（或 icons.css）里
       找到定义；
    2. .vitepress/ 下不再出现 --brs-*（旧版式的令牌，应当已全部退场）；
    3. --vp-* 的**赋值**只允许出现在 css/vp-bridge.css，且右值必须引用本站令牌；
    4. html.theme-Snow 覆盖了 :root 里除 INHERITS_OK 之外的全部颜色类令牌；
    5. .wiki-rarity 与 data-tier= 只出现在 css/widgets.css，不在任何组件 <style> 里；
    6. <style scoped> 只允许出现在 WikiSearchBox.vue（上游 fork，要保持可 diff）；
    7. composables/useWiki.ts 不 import 任何 .vue / .css / 图片。

    本 guard 只管**契约在不在**，不管颜色好不好看：配色是人眼的事。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
THEME = os.path.join(REPO_ROOT, "wiki-site", "docs", ".vitepress", "theme")
CSS_DIR = os.path.join(THEME, "css")
COMPONENTS = os.path.join(THEME, "components")
TOKENS = os.path.join(CSS_DIR, "tokens.css")
ICONS = os.path.join(CSS_DIR, "icons.css")
BRIDGE = os.path.join(CSS_DIR, "vp-bridge.css")
USE_WIKI = os.path.join(THEME, "composables", "useWiki.ts")
VP_DIR = os.path.join(REPO_ROOT, "wiki-site", "docs", ".vitepress")

USE_RE = re.compile(r"var\(\s*(--(?:theme|layout|font|wikigg|icon)-[a-z0-9-]+)")
DEF_RE = re.compile(r"^\s*(--[a-z0-9-]+)\s*:", re.M)
VP_ASSIGN_RE = re.compile(r"^\s*(--vp-[a-z0-9-]+)\s*:\s*([^;]+);", re.M)
STYLE_BLOCK_RE = re.compile(r"<style[^>]*>(.*?)</style>", re.S)
SCOPED_RE = re.compile(r"<style[^>]*\bscoped\b")

# Snow 不必覆盖的令牌：要么与皮肤无关（尺寸 / 字族 / 顶栏），要么是从别的令牌派生的。
INHERITS_OK = {
    # Logo 是一张图，深浅两套皮肤共用：金色填充 + 深墨描边，蓝天雪天都压得住。
    # 哪天出了浅色专版，在 html.theme-Snow 里覆盖这一条即可，这里也要跟着删。
    "--theme-site-logo-image",
    "--theme-site-logo-width",
    "--theme-site-logo-height",
    "--theme-top-background-height",
    "--theme-top-background-offset",
    "--theme-heading-color",
    "--theme-text-color-control",
    "--theme-link-color-accent",
    "--theme-icon-color",
    "--theme-icon-color-link",
    "--theme-icon-color-hover",
    "--theme-border-color",
    "--theme-box-border-color-inner",
    "--theme-box-border-radius",
    "--theme-box-border-radius-inner",
    "--theme-widget-border-radius",
    "--theme-dropdown-border-width",
    "--theme-dropdown-border-style",
    "--theme-dropdown-border-radius",
    "--theme-dropdown-border-color",
    "--theme-dropdown-shadow",
    "--theme-modetabs-classic-border-color",
    "--theme-modetabs-classic-text-color",
    "--theme-modetabs-expert-border-color",
    "--theme-notice-blue-hue",
    "--theme-notice-red-hue",
    "--theme-notice-purple-hue",
    "--theme-notice-green-hue",
    "--theme-notice-yellow-hue",
    "--theme-notice-orange-hue",
    "--theme-notice-pink-hue",
    "--theme-notice-red-text-color",
    "--theme-notice-orange-text-color",
}


def fail(message):
    print("WikiThemeTokensGuard: FAIL - " + message)
    return 1


def read(path):
    with open(path, "r", encoding="utf-8") as fh:
        return fh.read()


# 构建产物与依赖不算源码：dist 里当然会有编译后的旧值
SKIP_DIRS = {"dist", "cache", "node_modules"}


def walk(root, suffix):
    for current, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in sorted(files):
            if name.endswith(suffix):
                yield os.path.join(current, name)


def block_between(text, selector):
    """取出 `selector {` ... `}` 之间的内容（tokens.css 是手写的，缩进规整）。"""
    start = text.find(selector + " {")
    if start < 0:
        return None
    end = text.find("\n}", start)
    return text[start:end] if end > 0 else None


def main():
    for path in (TOKENS, ICONS, BRIDGE, USE_WIKI):
        if not os.path.isfile(path):
            return fail("缺文件：" + os.path.relpath(path, REPO_ROOT))

    tokens_src = read(TOKENS)
    icons_src = read(ICONS)

    defined = set(DEF_RE.findall(tokens_src)) | set(DEF_RE.findall(icons_src))
    if not defined:
        return fail("tokens.css 里一个令牌都没解析到，正则与文件写法已经不匹配")

    # 1. 引用的令牌必须有定义
    sources = list(walk(CSS_DIR, ".css")) + list(walk(THEME, ".vue"))
    unknown = {}
    for path in sources:
        src = read(path)
        for name in USE_RE.findall(src):
            if name not in defined:
                unknown.setdefault(name, os.path.relpath(path, REPO_ROOT))
    if unknown:
        detail = "; ".join("%s（%s）" % (k, v) for k, v in sorted(unknown.items())[:6])
        return fail("引用了 tokens.css / icons.css 里没有定义的令牌：" + detail
                    + "。CSS 不会报错，只会让那条属性整个失效")

    # 2. 旧版式的令牌应当已全部退场
    stale = []
    for path in list(walk(VP_DIR, ".css")) + list(walk(VP_DIR, ".vue")) + list(walk(VP_DIR, ".ts")):
        if "--brs-" in read(path):
            stale.append(os.path.relpath(path, REPO_ROOT))
    if stale:
        return fail("这些文件里还留着旧版式的 --brs-* 令牌（已随 style.css 一起删除，"
                    "引用它们等于引用未定义值）：" + ", ".join(sorted(stale)[:5]))

    # 3. --vp-* 只准在桥接文件里赋值，且必须落到本站令牌上
    for path in sources:
        rel = os.path.relpath(path, REPO_ROOT)
        for name, value in VP_ASSIGN_RE.findall(read(path)):
            if os.path.abspath(path) != os.path.abspath(BRIDGE):
                return fail("%s 给 %s 赋值了。--vp-* 是 VitePress 默认主题的变量，"
                            "本站只在 css/vp-bridge.css 里为搜索弹层那个 fork 搭一层桥，"
                            "别处一律用 --theme-*" % (rel, name))
            if "var(--theme-" not in value and "var(--layout-" not in value and "rgba(" not in value:
                return fail("vp-bridge.css 里 %s 的值 %r 没有落到本站令牌上：桥接必须转发，"
                            "不能自己写死颜色，否则换皮时这几处不跟着变" % (name, value.strip()))

    # 4. Snow 必须覆盖 :root 的颜色类令牌
    root_block = block_between(tokens_src, ":root")
    snow_block = block_between(tokens_src, "html.theme-Snow")
    if root_block is None or snow_block is None:
        return fail("tokens.css 里找不到 :root 或 html.theme-Snow 块，本 guard 的解析要同步")
    root_theme = {n for n in DEF_RE.findall(root_block) if n.startswith("--theme-")}
    snow_theme = set(DEF_RE.findall(snow_block))
    missing = sorted(root_theme - snow_theme - INHERITS_OK)
    if missing:
        return fail("html.theme-Snow 没有覆盖这些令牌：" + ", ".join(missing[:8])
                    + "。它们会继承深色皮肤的值，在浅底上多半看不见。"
                    "确实该继承的请显式加进本 guard 的 INHERITS_OK")

    # 5/6. 稀有度规则与 scoped 的位置
    for path in walk(COMPONENTS, ".vue"):
        rel = os.path.relpath(path, REPO_ROOT)
        src = read(path)
        if SCOPED_RE.search(src) and os.path.basename(path) != "WikiSearchBox.vue":
            return fail("%s 用了 <style scoped>。本站的构件样式统一放 theme/css/，"
                        "只有 WikiSearchBox.vue（上游 fork）保留 scoped 以便与上游 diff" % rel)
        for block in STYLE_BLOCK_RE.findall(src):
            if ".wiki-rarity" in block or "data-tier=" in block:
                return fail("%s 的 <style> 里出现了稀有度规则。它有五处消费者，"
                            "必须留在 theme/css/widgets.css §0（见 AGENTS.md §4.6）" % rel)

    # 认「顶格的规则块」而不是子串：`.wiki-rarityX {` 这种改名也得红
    # （反向验证第一版就是这么漏的）
    widgets = os.path.join(CSS_DIR, "widgets.css")
    widgets_src = read(widgets) if os.path.isfile(widgets) else ""
    if not re.search(r"^\.wiki-rarity\s*\{", widgets_src, re.M):
        return fail("css/widgets.css 里找不到 .wiki-rarity 的规则块：稀有度色片会全站消失")
    # 每一档都得给三类消费者上色：色片（.wiki-rarity）、组件里的名字（.wiki-tier）、
    # 正文里的实体链接（.brs-eref）。只检查 [data-tier="N"] 出现过是不够的——
    # 三条选择器写在同一个组里，改坏其中一条，子串检查照样绿。
    for tier in ("5", "6", "7", "8"):
        for prefix in (".wiki-rarity", ".wiki-tier", ".brs-eref"):
            if ('%s[data-tier="%s"]' % (prefix, tier)) not in widgets_src:
                return fail('css/widgets.css 缺 %s[data-tier="%s"] 的上色规则：'
                            "这一档在对应的位置会退回默认色（三类消费者见 AGENTS.md §4.6）"
                            % (prefix, tier))

    # 7. useWiki 必须能被 esbuild 单独打包（check-navigation.mjs 那条回归）
    for line in read(USE_WIKI).splitlines():
        if line.strip().startswith("import") and re.search(r"\.(vue|css|png|webp|svg|woff2?)['\"]", line):
            return fail("useWiki.ts 里 import 了组件或资源：%s。scripts/check-navigation.mjs "
                        "用 esbuild 单独打包它，没有配这些 loader，回归会挂" % line.strip())

    print("WikiThemeTokensGuard: PASS - %d 个令牌，%d 个源文件的引用全部有定义，"
          "Snow 覆盖齐全，稀有度规则未被搬进组件" % (len(defined), len(sources)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
