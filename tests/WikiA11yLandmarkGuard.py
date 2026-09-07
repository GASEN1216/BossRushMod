# -*- coding: utf-8 -*-
"""WikiA11yLandmarkGuard - 在线 Wiki 的地标（landmark）命名与唯一性。

背景：
    2026-09-08 用 Lighthouse + axe 扫了一遍站点，无障碍分只有 89~94，
    其中一半的失分来自地标：

      - **两个 banner**：`<header id="wgg-netbar">` 是隐式 banner，
        `#p-logo` 又挂了 `role="banner"`。读屏器的地标列表里出现两个「横幅」。
      - **一堆没名字的 <nav>**：左栏十几个门户框、目录框、页尾导航盒全是
        `<nav>` 或 `role="navigation"`，但都没有 aria-label。在读屏器的
        地标列表里它们全叫「导航」，读者没法挑，等于这份列表废了。
      - **Logo 带落在所有地标之外**：把 `#p-logo` 的 role 摘掉之后，
        它成了「不属于任何地标的内容」（axe 的 region 规则）。

    这三件事都不会让构建失败、不会有控制台报错、肉眼也看不出来——
    只有读屏器用户会撞上。全部修完之后五个页面四项都是 100、axe 零违规。

    这个约定最容易被下一个人破坏的方式，是**新增一个门户框**：
    照着相邻的 <nav> 复制一段，很自然就漏了 aria-label。

判据（只看源码，不跑浏览器，CI 上也能跑）：
    1. 主题里 `role="banner"` 恰好出现一次（站点的横幅只能有一个）；
    2. 每个 `<nav` 开标签都带 aria-label / :aria-label / aria-labelledby；
    3. 每个 `role="navigation"` 的元素同上；
    4. 词表里 aria 用到的那几个键都在（否则标签会是 undefined）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
THEME = os.path.join(REPO_ROOT, "wiki-site", "docs", ".vitepress", "theme")
UI_TEXT = os.path.join(THEME, "composables", "useUiText.ts")

# <nav ...> 的开标签，含跨行写法
NAV_OPEN_RE = re.compile(r"<nav\b[^>]*?>", re.S)
ROLE_NAV_RE = re.compile(r"<(\w+)\b([^>]*?role=\"navigation\"[^>]*?)>", re.S)
BANNER_RE = re.compile(r"role=\"banner\"")
LABEL_RE = re.compile(r"(?::aria-label|aria-label|aria-labelledby)\s*=")
# 词表里 aria 用到的键
NEEDED_UI_KEYS = ("siteTools", "navigation", "otherLanguages", "contents",
                  "navboxLabel", "toggleNavbox", "toggleContents", "search")


def fail(message):
    print("WikiA11yLandmarkGuard: FAIL - " + message)
    return 1


def read(path):
    with open(path, "r", encoding="utf-8") as fh:
        return fh.read()


def vue_files():
    out = []
    for base, _dirs, names in os.walk(THEME):
        for name in sorted(names):
            if name.endswith(".vue"):
                out.append(os.path.join(base, name))
    return out


def main():
    if not os.path.isdir(THEME):
        return fail("找不到 wiki-site/docs/.vitepress/theme/")
    if not os.path.isfile(UI_TEXT):
        return fail("找不到 composables/useUiText.ts")

    files = vue_files()
    if not files:
        return fail("theme/ 下一个 .vue 都没有，本 guard 的扫描范围要同步")

    # ── 1. banner 只能有一个 ──────────────────────────────────────
    banners = []
    for path in files:
        for _ in BANNER_RE.finditer(read(path)):
            banners.append(os.path.relpath(path, REPO_ROOT))
    if len(banners) != 1:
        return fail("role=\"banner\" 出现 %d 次（%s），站点的横幅只能有一个。"
                    "多一个会让读屏器的地标列表里出现两个「横幅」；"
                    "一个都没有则 Logo 带会变成不属于任何地标的内容（axe 的 region 规则）"
                    % (len(banners), ", ".join(banners) or "无"))

    # ── 2./3. 每个导航地标都要有名字 ─────────────────────────────
    unnamed = []
    for path in files:
        src = read(path)
        rel = os.path.relpath(path, REPO_ROOT)
        for tag in NAV_OPEN_RE.findall(src):
            if not LABEL_RE.search(tag):
                unnamed.append("%s: %s" % (rel, " ".join(tag.split())[:70]))
        for matched in ROLE_NAV_RE.finditer(src):
            attrs = matched.group(2)
            if not LABEL_RE.search(attrs):
                unnamed.append("%s: <%s role=\"navigation\"> 无名"
                               % (rel, matched.group(1)))
    if unnamed:
        return fail("有 %d 个导航地标没有名字：%s。"
                    "读屏器的地标列表里它们全叫「导航」，读者没法挑，"
                    "这份列表就废了。每个 <nav> / role=\"navigation\" 都要带 "
                    "aria-label（新增门户框时最容易漏这一条）"
                    % (len(unnamed), "；".join(unnamed[:4])))

    # ── 4. 词表键在场 ────────────────────────────────────────────
    ui_src = read(UI_TEXT)
    for key in NEEDED_UI_KEYS:
        # 词表是中英两份，两份都要有：只加一半的话，另一种语言的 aria-label
        # 会是 undefined，而页面照常渲染、什么都不报。
        hits = len(re.findall(r"^\s+" + key + r"\s*:", ui_src, re.M))
        if hits < 2:
            return fail("useUiText.ts 里 %s 只出现 %d 次（中英两份词表各要一次）："
                        "缺的那一份语言里，用它当 aria-label 的地标会拿到 undefined，"
                        "读屏器读出来是空的" % (key, hits))

    nav_count = sum(len(NAV_OPEN_RE.findall(read(p))) for p in files)
    print("WikiA11yLandmarkGuard: PASS - %d 处 <nav> 全部有名字，"
          "role=\"banner\" 唯一（%s），词表键齐全"
          % (nav_count, banners[0]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
