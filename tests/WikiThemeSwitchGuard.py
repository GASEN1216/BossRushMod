# -*- coding: utf-8 -*-
"""WikiThemeSwitchGuard - 在线 Wiki 换肤的三方对齐，以及版式归属声明。

背景：
    皮肤（Overworld 深色 / Snow 浅色）由三处共同决定，谁也不知道另外两处：

      1. config.mts head 里的 `skin-theme-init` 内联脚本 —— 首帧把
         `theme-<Name>` / `view-dark|light` 挂到 <html> 上；
      2. data/themes.mts 的 THEMES —— 「外观」下拉列出哪几套；
      3. css/tokens.css 的 `html.theme-<Name>` —— 那套皮肤的令牌值。

    三处任一处漏了一套皮肤，都不会报错：
      - 脚本漏了 -> 刷新之后回到默认皮肤，读者以为「设置没保存」；
      - themes.mts 漏了 -> 下拉里选不到，但 URL 参数还能进，出现「菜单里没有的皮肤」；
      - tokens.css 漏了 -> 类挂上了、颜色没换，页面变成「深色骨架 + 浅色文字」。

    内联脚本还有一条硬约束：它是以**字符串**写进 HTML 的，模块作用域够不着，
    所以不能引用任何 import。写着写着改成引用外部常量，构建照样过，
    浏览器里直接抛错、类一个都挂不上。

    最后一条与代码无关但同样重要：本站版式是照 Official Terraria Wiki
    （terraria.wiki.gg）逐值量出来净室重写的，CSS 与素材全部自制。
    页脚那行归属声明是这件事的唯一对外说明，删掉就成了默不作声地照搬。

判据：
    1. config.mts 有 appearance: false（否则 VitePress 自带的 check-dark-mode
       会和本站的 skin-theme 抢 <html> 上的 .dark）；
    2. config.mts 有 id 为 skin-theme-init 的内联脚本，内容自包含
       （含 'skin-theme'、view-、dark；不含 import / require( / from '），且短；
    3. 脚本里的主题表、themes.mts 的 THEMES、tokens.css 的 html.theme-* 三者一致
       （默认皮肤写在 :root，tokens.css 里没有它的块）；
    4. themes.mts 的 DEFAULT_THEME 在主题表里，且 SKIN_THEME_KEY 与脚本用的键一致；
    5. 页脚组件里仍有指向 terraria.wiki.gg 的归属声明。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VP = os.path.join(REPO_ROOT, "wiki-site", "docs", ".vitepress")
CONFIG = os.path.join(VP, "config.mts")
THEMES = os.path.join(VP, "data", "themes.mts")
TOKENS = os.path.join(VP, "theme", "css", "tokens.css")
FOOTER = os.path.join(VP, "theme", "components", "WikiFooter.vue")
UI_TEXT = os.path.join(VP, "theme", "composables", "useUiText.ts")

SCRIPT_RE = re.compile(r"\['script',\s*\{\s*id:\s*'skin-theme-init'\s*\},\s*([A-Z_]+)\]")
# 脚本正文是拼接起来的字符串常量，取它的定义整段
CONST_RE = "const SKIN_THEME_INIT ="
THEME_TABLE_RE = re.compile(r"var T=\{([^}]*)\}")
TABLE_ENTRY_RE = re.compile(r"(\w+):'(dark|light)'")
THEMES_ENTRY_RE = re.compile(r"\{\s*name:\s*'([^']+)',\s*view:\s*'(dark|light)'")
TOKENS_BLOCK_RE = re.compile(r"^html\.theme-([\w-]+)\s*\{", re.M)

FORBIDDEN_IN_SCRIPT = ("import ", "require(", "from '")


def fail(message):
    print("WikiThemeSwitchGuard: FAIL - " + message)
    return 1


def read(path):
    with open(path, "r", encoding="utf-8") as fh:
        return fh.read()


def script_body(config_src):
    """取 SKIN_THEME_INIT 常量的定义（到第一个空行为止；它是一串拼接的字符串字面量）。"""
    start = config_src.find(CONST_RE)
    if start < 0:
        return None
    end = config_src.find("\n\n", start)
    return config_src[start:end] if end > 0 else config_src[start:]


def main():
    for path in (CONFIG, THEMES, TOKENS, FOOTER, UI_TEXT):
        if not os.path.isfile(path):
            return fail("缺文件：" + os.path.relpath(path, REPO_ROOT))

    config_src = read(CONFIG)
    themes_src = read(THEMES)
    tokens_src = read(TOKENS)

    # 1. VitePress 自己的深浅切换必须让位
    if not re.search(r"^\s*appearance:\s*false\s*,", config_src, re.M):
        return fail("config.mts 少了 appearance: false。VitePress 会注入自己的 "
                    "check-dark-mode 脚本，和本站的 skin-theme 抢 <html> 上的 .dark，"
                    "表现是切到浅色后一刷新又变回深色")

    # 2. 内联脚本存在、自包含
    matched = SCRIPT_RE.search(config_src)
    if not matched:
        return fail("config.mts 的 head 里找不到 id 为 skin-theme-init 的内联脚本："
                    "首帧不挂皮肤类，每次刷新都会先闪一下默认色")
    body = script_body(config_src)
    if body is None:
        return fail("config.mts 里找不到 SKIN_THEME_INIT 的定义")
    for token in FORBIDDEN_IN_SCRIPT:
        if token in body:
            return fail("skin-theme-init 脚本里出现了 %r。它是以字符串形式写进 HTML 的，"
                        "拿不到模块作用域，引用外部标识符会在浏览器里直接抛错" % token)
    if len(body) > 1500:
        return fail("skin-theme-init 脚本长到 %d 字符了。它是阻塞首屏的内联脚本，"
                    "只该做「读偏好 + 挂三个 class」这一件事" % len(body))
    for needed in ("'skin-theme'", "view-", "'dark'"):
        if needed not in body:
            return fail("skin-theme-init 脚本里没有 %s：它至少要读 localStorage 的 "
                        "skin-theme、挂 view-* 与 .dark 这三件事" % needed)

    # 3. 三处主题清单一致
    table = THEME_TABLE_RE.search(body)
    if not table:
        return fail("skin-theme-init 脚本里找不到 `var T={...}` 主题表，本 guard 的解析要同步")
    script_themes = dict(TABLE_ENTRY_RE.findall(table.group(1)))
    declared = dict(THEMES_ENTRY_RE.findall(themes_src))
    if not declared:
        return fail("data/themes.mts 里一个 THEMES 条目都没解析到")
    if script_themes != declared:
        return fail("内联脚本的主题表 %s 与 data/themes.mts 的 THEMES %s 对不上。"
                    "两边都得列出全部皮肤及其明暗，否则会出现「菜单里选得到但刷新丢失」"
                    % (sorted(script_themes.items()), sorted(declared.items())))

    default_match = re.search(r"DEFAULT_THEME\s*=\s*'([^']+)'", themes_src)
    if not default_match:
        return fail("data/themes.mts 里找不到 DEFAULT_THEME")
    default = default_match.group(1)
    if default not in declared:
        return fail("DEFAULT_THEME=%s 不在 THEMES 里" % default)
    if ("t='%s'" % default) not in body:
        return fail("内联脚本的兜底皮肤与 DEFAULT_THEME（%s）不一致：读者第一次进站"
                    "看到的皮肤会和「外观」菜单标出的默认项不同" % default)

    key_match = re.search(r"SKIN_THEME_KEY\s*=\s*'([^']+)'", themes_src)
    if not key_match or ("'%s'" % key_match.group(1)) not in body:
        return fail("data/themes.mts 的 SKIN_THEME_KEY 与内联脚本读的 localStorage 键对不上："
                    "切换写进去的偏好下次刷新读不出来")

    # tokens.css：默认皮肤在 :root，其余各一个 html.theme-* 块
    styled = set(TOKENS_BLOCK_RE.findall(tokens_src))
    expected = set(declared) - {default}
    if styled != expected:
        return fail("tokens.css 里的 html.theme-* 块是 %s，按 THEMES 应当是 %s"
                    "（默认皮肤 %s 的值写在 :root）。缺一套就是「类挂上了、颜色没换」"
                    % (sorted(styled), sorted(expected), default))

    # 4. 每套皮肤在词表里都有名字，否则「外观」菜单会出现空白项
    ui_src = read(UI_TEXT)
    for label in re.findall(r"label:\s*'([^']+)'", themes_src):
        if ("%s:" % label) not in ui_src:
            return fail("data/themes.mts 用了词表键 %s，但 useUiText.ts 里没有它："
                        "「外观」菜单会显示成空白项" % label)

    # 5. 版式归属声明
    footer_src = read(FOOTER)
    if "terraria.wiki.gg" not in ui_src or "attribution" not in footer_src:
        return fail("页脚的版式归属声明不见了。本站版式是照 Official Terraria Wiki"
                    "（terraria.wiki.gg）逐值测量后净室重写的，这行是唯一的对外说明，"
                    "文案在 useUiText.ts 的 attribution、渲染在 WikiFooter.vue")

    print("WikiThemeSwitchGuard: PASS - %d 套皮肤在内联脚本 / themes.mts / tokens.css "
          "三处一致，默认 %s，归属声明在位" % (len(declared), default))
    return 0


if __name__ == "__main__":
    sys.exit(main())
