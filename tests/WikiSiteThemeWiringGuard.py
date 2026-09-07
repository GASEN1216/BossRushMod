# -*- coding: utf-8 -*-
"""WikiSiteThemeWiringGuard - 在线 Wiki 主题层那几处「断了也不报错」的接线。

背景：
    2026-09-06 这轮前端修复引入了两处**跨文件的隐式约定**，它们断掉之后
    既不会让构建失败，也不会让任何现有 guard 变红，只会在线上悄悄变样：

    一、速查框的注入点。
        它从前挂在默认主题的 doc-before 插槽上，排在正文 h1 **之前**，
        于是 h1 那条 2px 底线整幅画过浮动的框身（这就是 owner 报的「布局错乱」）。
        现在改成 config.mts 的 infoboxSlotPlugin 在渲染期把 `<WikiInfobox />`
        插到第一个 h1 之后，组件在 theme/index.ts 里**全局注册**。
        三处缺一：
          - 插件没登记 -> 全站速查框整个消失（页面照常构建，只是少了框）；
          - 组件没全局注册 -> Vue 渲染出一个惰性的 <wikiinfobox> 未知元素，
            浏览器当它不存在，同样是「框没了」，控制台只有一条警告；
          - Layout.vue 里那句没删干净 -> 一页出现**两个**框。

    二、配图灯箱与图片口径。
        WikiLightbox 靠 Layout.vue 挂载 + extras.css 给可点的图上 zoom-in 光标，
        两处的选择器必须和组件里的 SELECTOR 对得上；漏了光标读者根本不知道图能点。
        更隐蔽的是**产物尺寸**：正文里 `.brs-icon` 按 205px 显示，而图标产物一度只有
        128px，浏览器默认会把它拉大——页面不报错，只是糊。owner 报的「有些太糊」
        就是这么来的。所以这里把 build_wiki_images.py 的尺寸常量和 style.css 里的
        展示宽度绑在一起：产物只能比展示尺寸大，不能小。

    三、搜索弹层的 fork。
        theme/components/WikiSearchBox.vue 是 vitepress 自带 VPLocalSearchBox.vue
        的副本，靠 config.mts 里一条 Vite alias 顶上去。alias 一旦写错或被删，
        站点会**静默退回**官方弹层：还能搜，只是首开又变回三四秒白屏、
        中文输入法上下键又被抢走——没有任何报错。
        更隐蔽的是升级 VitePress：fork 冻结在某个版本的实现上，上游改了
        （比如换了索引加载方式或结果结构）而这边没跟，同样只在运行时表现异常。
        所以这里把 fork 头部登记的 UPSTREAM 版本与 package-lock.json 里
        实际装的版本对齐——版本一动，先红在这里，逼人重新 diff 一遍。

判据：
    1. config.mts 注册了 infoboxSlotPlugin，且 theme/index.ts 全局注册了 WikiInfobox；
    2. Layout.vue 里不再渲染 <WikiInfobox（防止和渲染期注入重复）；
    3. config.mts 有指向 WikiSearchBox.vue 的 VPLocalSearchBox alias，且该文件存在；
    4. WikiSearchBox.vue 头部的 `UPSTREAM: vitepress@x.y.z` == package-lock.json
       里 node_modules/vitepress 的版本；
    5. search.mts 仍导出 cjkTokenize，且函数体自包含（不引用外部标识符）——
       它会被序列化进站点数据、在浏览器里 new Function 还原，引用外部变量会静默失效。
    6. Layout.vue 挂了 WikiLightbox，extras.css 给可点的图配了 zoom-in 光标；
    7. config.mts 给 markdown 图片补了 loading=lazy（图鉴那页 38 张图，漏了就是一开页
       全量拉取）；
    8. ICON_MAX / POSTER_MAX 不小于 style.css 里 .brs-icon / .brs-figure 的展示宽度。

    本 guard 只管**接线在不在**，不管样式好不好看：版式是人眼的事。
"""
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITE = os.path.join(REPO_ROOT, "wiki-site")
VP = os.path.join(SITE, "docs", ".vitepress")
CONFIG = os.path.join(VP, "config.mts")
SEARCH = os.path.join(VP, "search.mts")
THEME = os.path.join(VP, "theme")
THEME_INDEX = os.path.join(THEME, "index.ts")
LAYOUT = os.path.join(THEME, "Layout.vue")
SEARCH_BOX = os.path.join(THEME, "components", "WikiSearchBox.vue")
HEAD_SEARCH = os.path.join(THEME, "components", "WikiHeadSearch.vue")
LIGHTBOX = os.path.join(THEME, "components", "WikiLightbox.vue")
HEAD = os.path.join(THEME, "components", "WikiHead.vue")
NETBAR = os.path.join(THEME, "components", "WikiNetbar.vue")
DROPDOWN_DISMISS = os.path.join(THEME, "composables", "useDropdownDismiss.ts")
LAYOUT_CSS = os.path.join(THEME, "css", "layout.css")
# 2026-09-07 换皮：style.css / extras.css 拆成 theme/css/*.css。
# 可点图的光标在 widgets.css，配图块的展示宽度在 content.css。
ZOOM_CSS = os.path.join(THEME, "css", "widgets.css")
FIGURE_CSS = os.path.join(THEME, "css", "content.css")
BUILD_IMAGES = os.path.join(REPO_ROOT, "tools", "build_wiki_images.py")
LOCK = os.path.join(SITE, "package-lock.json")

# style.css 里这两个配图块的展示宽度，就是对应产物尺寸的下限
DISPLAY_WIDTH_RULES = (
    (".brs-icon", "ICON_MAX"),
    (".brs-figure", "POSTER_MAX"),
)

UPSTREAM_RE = re.compile(r"^\s*\*\s*UPSTREAM:\s*vitepress@([0-9][^\s]*)\s*$", re.M)
# fork 里除了注释，其余对外部标识符的引用都在 import 行上；cjkTokenize 的函数体
# 只允许出现它自己的局部名字。这里只做粗筛：函数体内不得出现这两个名字。
FORBIDDEN_IN_TOKENIZER = ("SEARCH", "import ")


def fail(message):
    print("WikiSiteThemeWiringGuard: FAIL - " + message)
    return 1


def read(path):
    with open(path, "r", encoding="utf-8") as fh:
        return fh.read()


def tokenizer_body(src):
    """取 cjkTokenize 的函数体（到下一个顶格 `}` 为止）。"""
    start = src.find("export function cjkTokenize")
    if start < 0:
        return None
    end = src.find("\n}", start)
    return src[start:end] if end > 0 else src[start:]


def main():
    for path in (CONFIG, SEARCH, THEME_INDEX, LAYOUT, SEARCH_BOX, HEAD_SEARCH, LIGHTBOX,
                 ZOOM_CSS, FIGURE_CSS, BUILD_IMAGES, LOCK):
        if not os.path.isfile(path):
            return fail("缺文件：" + os.path.relpath(path, REPO_ROOT))

    config_src = read(CONFIG)
    theme_src = read(THEME_INDEX)
    layout_src = read(LAYOUT)
    box_src = read(SEARCH_BOX)
    search_src = read(SEARCH)

    # 1. 速查框注入的三处接线
    if "md.use(infoboxSlotPlugin)" not in config_src:
        return fail("config.mts 没有 md.use(infoboxSlotPlugin)：速查框不会被插进正文，全站的框会消失")
    if "function infoboxSlotPlugin" not in config_src:
        return fail("config.mts 里找不到 infoboxSlotPlugin 的定义")
    if "app.component('WikiInfobox'" not in theme_src:
        return fail("theme/index.ts 没有全局注册 WikiInfobox：渲染期插进正文的标签会解析不到组件")

    # 2. 不能同时还挂在 Layout 的插槽上
    if "<WikiInfobox" in layout_src:
        return fail("Layout.vue 里还渲染着 <WikiInfobox />，会和渲染期注入的那份重复出现两个速查框")

    # 3. 搜索弹层的接线。
    #    从前靠 config.mts 里一条 Vite alias 顶替默认主题的 VPLocalSearchBox；
    #    换皮之后默认主题整个不在了，改由标签行的搜索框直接 import 这个 fork。
    #    断了的话搜索只剩联想下拉、没有「完整结果」那一层，而且不会报错。
    head_search_src = read(HEAD_SEARCH)
    if "./WikiSearchBox.vue" not in head_search_src:
        return fail("WikiHeadSearch.vue 没有 import('./WikiSearchBox.vue')："
                    "回车打不开完整结果弹层，搜索会静默退化成只有联想下拉")

    # 4. fork 的上游版本必须跟得上实际装的 vitepress
    matched = UPSTREAM_RE.search(box_src)
    if not matched:
        return fail("WikiSearchBox.vue 头部缺少 `UPSTREAM: vitepress@<版本>` 标记，"
                    "升级 VitePress 时无从判断这个 fork 是否还对得上上游")
    declared = matched.group(1)
    with open(LOCK, "r", encoding="utf-8") as fh:
        lock = json.load(fh)
    installed = (lock.get("packages", {}).get("node_modules/vitepress", {}) or {}).get("version")
    if not installed:
        return fail("package-lock.json 里读不到 node_modules/vitepress 的版本")
    if declared != installed:
        return fail("WikiSearchBox.vue 是 vitepress@%s 的 fork，但现在装的是 %s。"
                    "请把上游同名组件重新 diff 一遍，把改动搬过来，再更新头部的 UPSTREAM 标记"
                    % (declared, installed))

    # 5. cjkTokenize 的自包含约束
    body = tokenizer_body(search_src)
    if body is None:
        return fail("search.mts 不再导出 cjkTokenize：中文搜索会退回按整句切词，基本等于不可用")
    for token in FORBIDDEN_IN_TOKENIZER:
        if token in body:
            return fail("cjkTokenize 的函数体里出现了 %r。它会被序列化进站点数据、"
                        "在浏览器里用 new Function 还原，引用外部标识符会在运行时静默失效" % token)

    # 6. 灯箱接线
    if "<WikiLightbox" not in layout_src:
        return fail("Layout.vue 没有挂 WikiLightbox：正文配图点了不会放大")
    zoom_src = read(ZOOM_CSS)
    if "cursor: zoom-in" not in zoom_src:
        return fail("css/widgets.css 里没有 zoom-in 光标：图能点但没有任何提示，读者不会去点")
    box_selectors = [sel for sel in (".brs-gallery img", ".brs-figure img", ".brs-icon img")
                     if sel not in zoom_src]
    if box_selectors:
        return fail("css/widgets.css 的可点图选择器和 WikiLightbox 的 SELECTOR 对不上，缺：%s"
                    % ", ".join(box_selectors))

    # 7. 正文图片懒加载
    # 认实际那句 attrSet，不能只找 "lazy" —— 上面那段注释里就有这个词，
    # 代码删了照样能蒙混过去（本 guard 的反向验证第一版就是这么漏的）。
    if "attrSet('loading', 'lazy')" not in config_src:
        return fail("config.mts 没给 markdown 图片补 loading=lazy："
                    "图鉴那页 38 张图会在开页时一次性全部拉取")

    # 8. 产物尺寸 >= 展示尺寸（否则浏览器把图拉大，看起来就是糊）
    style_src = read(FIGURE_CSS)
    images_src = read(BUILD_IMAGES)
    for selector, const_name in DISPLAY_WIDTH_RULES:
        # 同一个选择器在 style.css 里出现多次（共用的网格规则 + 各自的限宽），
        # 扫全部同名规则块取最大的那个 max-width —— 那才是这张图能被撑到的宽度。
        pattern = "^" + re.escape(selector) + r" \{(.*?)^\}"
        widths = []
        for block in re.finditer(pattern, style_src, re.S | re.M):
            found = re.search(r"max-width:\s*(\d+)px", block.group(1))
            if found:
                widths.append(int(found.group(1)))
        if not widths:
            return fail("css/content.css 里没找到 %s 的 max-width，无法核对它会不会把图放大；"
                        "规则写法改了的话本 guard 的正则要同步" % selector)
        const = re.search(r"^%s\s*=\s*(\d+)" % const_name, images_src, re.M)
        if not const:
            return fail("build_wiki_images.py 里找不到 %s" % const_name)
        shown, produced = max(widths), int(const.group(1))
        if produced < shown:
            return fail("%s 在正文里按 %dpx 显示，而 %s 只出到 %dpx —— 浏览器会把它拉大，"
                        "页面上就是一张糊图。要么把产物尺寸提上去，要么把展示宽度降下来"
                        % (selector, shown, const_name, produced))

    # 9. 目录框的三处接线。
    #    page.headers **只有** markdown.headers 打开时才会被填（VitePress 默认不填），
    #    漏了这一项目录框永远判定「标题不足四个」而整个不渲染，页面照常构建。
    if "headers: { level: [2, 3] }" not in config_src:
        return fail("config.mts 的 markdown 没开 headers：page.headers 会一直是空的，"
                    "全站目录框静默消失（VitePress 默认不填这个字段）")
    if "md.use(tocSlotPlugin)" not in config_src or "function tocSlotPlugin" not in config_src:
        return fail("config.mts 没有登记 tocSlotPlugin：目录框不会被插进正文")
    if "app.component('WikiToc'" not in theme_src:
        return fail("theme/index.ts 没有全局注册 WikiToc：渲染期插进正文的标签解析不到组件")

    # 10. 标题下那两行位置提示，同一套注入机制、同一类静默失效
    if "md.use(contentSubSlotPlugin)" not in config_src or "function contentSubSlotPlugin" not in config_src:
        return fail("config.mts 没有登记 contentSubSlotPlugin：#siteSub / #contentSub 会整个消失")
    if "app.component('WikiContentSub'" not in theme_src:
        return fail("theme/index.ts 没有全局注册 WikiContentSub")
    # 插入点都是「h1 之后」，谁后登记谁排在前面。顺序错了框会跑到位置提示上面去。
    if config_src.index("md.use(infoboxSlotPlugin)") > config_src.index("md.use(contentSubSlotPlugin)"):
        return fail("md.use(contentSubSlotPlugin) 必须排在 md.use(infoboxSlotPlugin) **之后**："
                    "两者插入点同为 h1 之后，后登记的才会排在前面，"
                    "顺序反了速查框会跑到位置提示上方")

    # 11. 换皮的两条底线：不许再继承默认主题，不许再拉网络字体。
    #     这两件事都不会让构建失败，只会让页面悄悄变回旧样子 / 多两个跨域请求。
    if "vitepress/theme" in theme_src or "vitepress/theme" in layout_src:
        return fail("theme/index.ts 或 Layout.vue 又 import 了 vitepress/theme："
                    "默认主题的 DOM 与样式会被拖回来，和自绘的 Vector 骨架叠在一起")
    if not re.search(r"^\s*appearance:\s*false\s*,", config_src, re.M):
        return fail("config.mts 少了 appearance: false：VitePress 自带的 check-dark-mode 脚本"
                    "会和本站的 skin-theme 抢 <html> 上的 .dark，切换皮肤时闪回深色")
    if "fonts.googleapis.com" in config_src or "fonts.gstatic.com" in config_src:
        return fail("config.mts 又引了 Google Fonts：这套皮用系统字体栈"
                    "（正文 Helvetica / 标题 Verdana），不该有第三方字体请求")

    # 12. 两枚页面级浮层都得挂在 Layout 上（fixed 定位，放进正文流会被层叠上下文关住）
    if "<WikiRefPreview" not in layout_src:
        return fail("Layout.vue 没有挂 WikiRefPreview：实体链接的悬停预览整个不出现")

    # 13. 两个下拉是「CSS 开、JS 关」的混合体，最容易被拆散：
    #     开合全靠 :hover / :focus-within（零 JS、SSR 就位、和目标站一致），
    #     但纯 CSS 关不掉 Esc——焦点还在里面 :focus-within 就还是真。
    #     缺口由 useDropdownDismiss 补：Esc 时把焦点送回标题按钮 + 挂 is-dismissed。
    #     少了 CSS 那半边就是「按了没反应」，少了 JS 那半边就是「Esc 无效」，
    #     两种都不报错。而且压制规则的选择器必须带容器前缀：
    #     裸类名特指度比 `#mw-head …:focus-within` 低一档，压不住。
    for path in (DROPDOWN_DISMISS, HEAD, NETBAR, LAYOUT_CSS):
        if not os.path.isfile(path):
            return fail("缺 %s：两个下拉会退回「Esc 关不掉」"
                        % os.path.relpath(path, REPO_ROOT))
    layout_css = read(LAYOUT_CSS)
    for name, src in (("WikiHead.vue", read(HEAD)), ("WikiNetbar.vue", read(NETBAR))):
        # 查**接线**，不查「名字出现过」——文档注释里提一句就能让在场检查通过，
        # 改名 / 删 import / 拆掉 @keydown.esc 三种改坏方式因此全都不会红。
        # 做法：先解出 `= useDropdownDismiss()` 的解构别名，再拿别名去模板里对。
        if not re.search(r"import\s*\{[^}]*\buseDropdownDismiss\b[^}]*\}\s*from"
                         r"\s*'\.\./composables/useDropdownDismiss'", src):
            return fail("%s 没有 import useDropdownDismiss：这个下拉按 Esc 关不掉，"
                        "焦点还在里面 :focus-within 就还是真" % name)
        destructured = re.search(r"const\s*\{([^}]*)\}\s*=\s*useDropdownDismiss\(\)", src)
        if not destructured:
            return fail("%s 里找不到 `const { … } = useDropdownDismiss()`："
                        "本 guard 靠解构出来的别名去核模板接线，写法变了要同步" % name)
        alias = dict(re.findall(r"(\w+)\s*:\s*(\w+)", destructured.group(1)))
        for field in ("dismissed", "dismiss", "onFocusOut", "reopen"):
            if field not in alias:
                return fail("%s 的解构里少了 %s：Esc 关闭这条链缺一环就不起作用"
                            % (name, field))
        wiring = (
            (r'\@keydown \.esc="%s"' % alias["dismiss"],
             "@keydown.esc 没绑到 dismiss：按 Esc 不会有任何反应"),
            (r'\@focusout="%s"' % alias["onFocusOut"],
             "@focusout 没绑到 onFocusOut：按过 Esc 之后这个下拉就再也打不开了"),
            (r"'is-dismissed'\s*:\s*%s" % alias["dismissed"],
             "':class' 没把 is-dismissed 绑到 dismissed：Esc 的状态传不到 CSS"),
            (r'\@click="%s"' % alias["reopen"],
             "标题按钮没绑 reopen：按过 Esc 之后鼠标点标题打不开"),
        )
        for pattern, why in wiring:
            if not re.search(pattern.replace(" ", ""), src.replace(" ", "")):
                return fail("%s：%s" % (name, why))
    for prefix in ("#mw-head", ".wgg-netbar"):
        pattern = re.escape(prefix) + r"\s+\.vector-menu-dropdown\.is-dismissed\s+\.vector-menu-content\s*\{"
        if not re.search(pattern, layout_css):
            return fail("css/layout.css 缺 `%s .vector-menu-dropdown.is-dismissed "
                        ".vector-menu-content`。压制规则必须和展开规则同样带容器前缀，"
                        "裸类名的特指度低一档、压不住 :focus-within，"
                        "表现是类挂上了但菜单没关" % prefix)

    print("WikiSiteThemeWiringGuard: PASS - 速查框注入三处接线齐全、灯箱已挂载、"
          "配图产物不小于展示尺寸、目录框与位置提示注入齐全、未继承默认主题也未引网络字体、"
          "两个下拉的 Esc 关闭 JS 与 CSS 两半都在、"
          "搜索弹层 fork 对齐 vitepress@%s、cjkTokenize 自包含" % installed)
    return 0


if __name__ == "__main__":
    sys.exit(main())
