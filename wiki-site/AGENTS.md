# wiki-site/AGENTS.md — 在线 Wiki 站点专项规则

> 先读根目录 `AGENTS.md`。本文件只讲 `wiki-site/`（VitePress 在线站）。
> 游戏内那本 Wiki 书归 `Integration/WikiContentManager.cs`，规则见 `Integration/AGENTS.md`。

## 1. 两条链路，一份正文

```
WikiContent/zh|en/*.md ─┬─→ 游戏内 Wiki 书（WikiContentManager 自己的解析器）
                        └─→ sync-content.mjs ─→ wiki-site/docs/**/*.md ─→ VitePress
```

正文只有 `WikiContent/` 一份。**改内容改那里，不要改 `wiki-site/docs/` 下的 `.md`**——
`sync-content.mjs` 每次跑都会先 `cleanOutput()` 删掉再重生成，改了就丢。

`wiki-site/docs/` 下**只有三处是手写的**，不受 sync 管辖：

| 路径 | 作用 |
| --- | --- |
| `docs/index.md` | 中文首页（`layout: page` + `<WikiHome />`） |
| `docs/.vitepress/` | 配置、结构数据、主题组件、样式，以及 `search.mts` / `seo.mts` / `feed.mts` 三个构建期模块 |
| `docs/public/` | 图片产物与 favicon |

英文首页 `docs/en/index.md` 由 `sync-content.mjs` 的 `generateEnIndex()` 生成，
和中文首页同构；要改首页结构，两边一起改。

`docs/` 之外还有一处手写正文：`wiki-site/hubs/`。系统、攻略两个类目在 `WikiContent/`
里没有「总览」条目（游戏内那本书不需要），在线站的类目主页就写在这里，中英各一份
（`systems.zh.md` / `systems.en.md` …），sync 复制成 `docs/systems/index.md` 与
`docs/en/systems/index.md`。**改主页改 `hubs/`，别改 `docs/` 里的副本。**
这些页只服务在线站，所以可以用表格、可以写 frontmatter `description`。

## 2. WikiContent 必须保持纯文本

游戏内解析器（`Integration/WikiContentManager.cs` 的预编译正则）只认
标题、粗体、列表、行内代码、链接、`[tip]` / `[warn]`。它**不认图片，也不认表格**：

- `![alt](src)` 会被链接正则吃掉 `[alt](src)`，把 `!` 单独留在原地，渲染成
  「一个野生的 ! + 一条指向不存在路径的蓝色可点链接」。
- `| a | b |` 表格会原样显示成竖线文本。

所以配图、速查框、宫格这些**只服务在线站**的东西，一律在 sync 或主题层加，
不要写进 `WikiContent/`。

`[tip]` / `[warn]` 必须写成单行（`tests/WikiCalloutSingleLineGuard.py` 守卫），
原因见那个 guard 的 docstring。

## 3. 站点结构的唯一事实源

`docs/.vitepress/data/structure.mts` 一处定义全部类目与条目，下面五处**全部**由它生成：

- `config.mts` 的 `nav` 与 `sidebar`（中英各一份，不再手写）
- `WikiHome.vue` 的首页门户宫格
- `WikiCardGrid.vue` 的类目主页条目宫格
- `WikiNavbox.vue` 的页尾同类导航
- `WikiBreadcrumb.vue` 的面包屑
- `WikiCompare.vue` 的类目主页对比表（按类目的 `entries` 逐条去 `infobox.mts` 取数据）

**加一个页面要做三件事**，缺一不可：

1. `WikiContent/zh|en/` 加正文，`WikiContent/catalog.tsv` 加一行；
2. `scripts/entry-map.mjs` 的 `ENTRY_TO_PATH` 加 entryId → 路径映射
   （sync、`config.mts`、`seo.mts` 共用这一份；它不能留在 sync 里，因为 sync 一被 import 就跑 `main()`）；
3. `structure.mts` 对应类目的 `entries` 加一条。

漏第 3 步时页面能生成、能搜到，但所有导航里都没有它——
`tests/WikiSiteStructureGuard.py` 会因为「页面生成了却没进 structure.mts」而红。

**加一个类目主页**（像系统 / 攻略那样没有 WikiContent 总览条目的类目）：
`hubs/<dir>.zh.md` + `hubs/<dir>.en.md` 两份都写；`sync-content.mjs` 的 `HUBS` 加 `<dir>`；
`structure.mts` 里该类目的 `path` 改成 `/<dir>/`，`entries[0]` 加一条同路径的「总览」条目。

更新日志是唯一的例外：46+ 条且按版本号排序，仍由 `config.mts` 直接读 `catalog.tsv`。
它在 `useWiki.ts` 里被拼成一个**虚拟类目**（`CHANGELOG_AS_CATEGORY`，条目来自
`changelog.data.mts`），所以面包屑认得它；页尾用版本时间线（`WikiChangelogTimeline.vue`）
代替同类导航——四十多个版本平铺成一行没法读。

## 4. 速查框（infobox）

`docs/.vitepress/data/infobox.mts` 按条目路径配置，正文一个字都不用改。

**注入位置：正文第一个 `h1` 之后**，由 `config.mts` 的 `infoboxSlotPlugin`
在渲染期往 token 流里插一个 `html_block`（`<WikiInfobox path="..." />`），
组件在 `theme/index.ts` **全局注册**。和 §4.5 的实体链接同一套办法：
只动渲染层，生成的 `.md` 一个字节不变。

它**曾经**挂在默认主题的 `doc-before` 插槽上，那是 2026-09-06 修掉的版式 bug 的根源：
那个插槽落在 `.content-container` 里、`main > .vp-doc` **之前**，DOM 顺序是「框 → 标题」，
于是宽屏上 `h1` 那条 2px 底线整幅画过浮动的框身，窄屏上读者先看到一张大卡片再看到标题。
**别往回搬。**`tests/WikiSiteThemeWiringGuard.py` 会同时检查
「插件登记了」「组件全局注册了」「Layout 里没有重复渲染」三件事。

两条随之而来的约束：

- **框现在长在 `.vp-doc` 里**，默认主题那套正文排版会级联进来。框里因此不用 `<p>`
  （`.vp-doc p` 的 16px 外边距和行高权重压得过组件自己的类），链接也在组件样式里
  用 `.vp-doc` 前缀显式压回来。往框里加新元素前先想一下 `.vp-doc` 有没有管它。
- **带整幅边线 / 底色的块要自成 BFC**。浮动只让行盒避让，块盒的边框和底色照旧铺满
  容器宽度——`h2` 的黄铜栏带、分隔线、提示块会从框底下穿过去。`style.css` §6 给
  `h2/h3/h4/hr/blockquote/.custom-block` 在 `≥1400px` 下加了 `display: flow-root`。
  表格和代码块自带 `overflow` 本来就是 BFC，配图块走 `clear: both`。
  `h1` **不在**这份名单里：框插在它之后，够不着它。

- 数值以 `WikiContent/zh` 正文为准，**改数值时两边一起改**。guard 只校验路径与图标，
  不校验数值——它没法判断哪边才是对的。
- `links` 里写路径不写标题，标题从 `structure.mts` 取，避免改名时两处漂移。
- 版式：≥1400px 右浮动竖框，更窄时通栏横排并隐藏与 `h1` 重名的那行。
  门槛之所以定在 1400 是因为侧栏 272 + 右侧目录 224 吃掉近 500px，
  1280 视口下正文只剩 ~610px，再浮一个框文字就没法读了。

## 4.5 正文列表里的实体名 → 图标 + 链接

`config.mts` 的 `entityLinkPlugin` 是一条 markdown-it core 规则：把
**列表项开头（或表格单元格整格）、独占一个 `**加粗**`、且文字与 `structure.mts`
某条目名（或 `catalog.tsv` 里的标题，如「百战留痕（黑市鸭王杯）」）完全相等**
的地方，换成「小图标 + 加粗名 + 链接」。总览页因此长成泰拉瑞亚那种带贴图的清单；
`sync-content.mjs` 的 `TABLEIZE` 把列表转成表格时首列保留加粗，也是为了让这条规则接手。

三条不能动的约束：

1. **必须放在渲染层，不能放进 `sync-content.mjs`。**
   sync 的产物 `docs/game-modes/zombie-mode.md` 被 `ZombieModeMutantWikiGuard.py`
   逐字节比对，在 sync 里塞装饰会立刻让它红，而且以后每改一次规则都要同步改
   那边的 Python 镜像。放渲染层，生成的 `.md` 一个字节不变。

2. **必须用 markdown-it 原生 token，不能拼裸 `<img>` / `<a>` HTML。**
   裸 `<img src="/BossRushMod/images/...">` 会被 Vite 当模块 import 去解析，
   构建直接失败（`Rollup failed to resolve import`）。用 `image` / `link_open`
   token，VitePress 自己的渲染规则会补 base、走客户端路由。
   这和 `sync-content.mjs` 里 `renderImageBlock()` 坚持用 markdown 图片语法是同一个坑。

3. **匹配要宁漏勿错。** 只认「完全相等」的条目名，且不自指。
   漏了只是少个图标；错了会把正文里的普通强调变成乱链接。
   加宽匹配范围前先想清楚 `**火焰伤害转治疗**` 这类加粗会不会被误伤。

改完跑一遍抽查，确认没有多链：

```bash
npm --prefix wiki-site run build
grep -rho '<a class="brs-eref"[^>]*><img[^>]*><strong>[^<]*' wiki-site/docs/.vitepress/dist --include='*.html' \
  | grep -o '<strong>.*' | sort -u
```

## 4.6 速查框里的稀有度与物品 ID

- `InfoboxRow.tier` 写了才渲染成稀有度色片（5 石板蓝 / 6 紫晶 / 7 黄铜 / 8 余烬）。
  **显式字段，不去嗅探「品质」这个标签**——标签一改口径就静默失效。
  没照抄泰拉那套十二级配色，用的是本站强调色排的递进。
- 同一个 `tier` 还会让**物品名**在正文链接、宫格、页尾导航、速查框相关条目里按档上色
  （`extras.css` §0 的 `[data-tier]` 规则；正文链接由 `entityLinkPlugin` 写属性，组件挂 `.wiki-tier`）。
  16 件装备的 tier 现已齐全，其中龙裔 / 龙王套装、龙息、龙铳的值**不在代码或正文里**，
  是用 UnityPy 从 `Assets/Equipment/dragon_equipment`、`dragonking_equipment` 预制体的
  typetree 读出来的（`Quality` 字段）。改这几件的品质要重读预制体，别猜。
- `物品 ID` 行抄自 `docs/Bossrush使用物品ID表.md`。
  **那份表不在 git 里**（`.gitignore` 挡了 `/docs/*`），所以 CI 无法交叉校验，
  guard 也没法管——改 TypeID 时要人工同步这里，别指望有东西提醒你。

## 4.7 类目主页的速查对比表（WikiCompare）

`Layout.vue` 在类目主页（`structure.mts` 里 `path` 与类目 `path` 相同的页）的正文之后、
条目宫格之前挂 `WikiCompare.vue`：把该类目下所有**有速查框**的条目横向拼成表，数据只读
`infobox.mts`，**没有任何单独维护的清单**。规则：

- 按速查框眉标的第一段分组（「近战武器 · 火焰」→「近战武器」），装备会拆成近战 / 套装 / 图腾 / 枪械；
- 列 = 组内**至少两个条目**都有的标签，按首次出现顺序；只在一个条目上出现的属性留在条目页；
- 组里只有一个条目时不出表；「获取 / 来源」「基础伤害 / 伤害」在组件里按别名合并，不动数据；
  「物品 ID」行只进速查框、不进对比表（组件里的 `HIDDEN`），内部 TypeID 拿来比没有意义；
- 带 `tier` 的格子和速查框一样渲染成稀有度色片；`.wiki-rarity` 定义在 `theme/extras.css` §0，
  两个组件共用，**不要挪回任一组件的 `<style>`**（一加 scoped 另一处就静默变灰）；
- 点表头排序：值开头的数字、`★` 个数、否则字符串；缺值行沉底。

想让某个属性可比，只要让至少两个条目在速查框里用**同一个标签**写它。

## 4.8 本地搜索的中文分词

`search.mts` 给 MiniSearch 换了 tokenizer：连续汉字切成二元组（「焚天龙皇」→ 焚天 / 天龙 / 龙皇），
建索引与解析查询同一切法，所以「龙皇」能命中正文。默认切法按空格和标点，中文整句是一个词，
只有从句首开始的前缀才搜得到——此前站内搜索对中文基本不可用。

**这个函数会被 VitePress 序列化进站点数据、在浏览器里 `new Function` 还原**，
所以它必须自包含：不能引用文件里的其他变量或 import，正则写在函数体内。
`config.mts` 里的 `EDIT_LINK_PATTERN` 受同一条约束。

### 4.8.1 弹层是 fork，不是默认那份

`theme/components/WikiSearchBox.vue` 是 **vitepress 自带 `VPLocalSearchBox.vue` 的副本**，
靠 `config.mts` 里一条 Vite alias（官方文档的 Overriding Internal Components 办法）顶上去。
顶栏按钮、`Ctrl+K` / `/` 快捷键、开关状态全都还是官方那份，只有弹层本体换人。

fork 相对上游的改动逐条写在**那个文件的头注释**里，不在这儿重复。要点：

- 索引加载搬到 `theme/composables/searchIndex.ts`，模块级缓存 + 首屏空闲预热
  （`Layout.vue` 的 `scheduleWarmup`）。上游把 `loadJSON` 写在 setup 的 `computedAsync` 里，
  而弹层是 `v-if` 挂载的，**每打开一次就重新解析一遍** 1 MB 索引。
- 预热分两档：弹层组件总是拉，索引只在连接不省流量时拉；鼠标移到搜索框上则强制拉全套。
  多数读者从不搜索，不该替他们决定下载 250 KB。
- 先 AND 后 OR（不足 5 条才用 OR 补齐）。二元组下 AND 约等于短语匹配。
- `ArrowUp` / `ArrowDown` 判 `isComposing`——上游只有 Enter 判了，中文输入法候选框
  开着时上下键会被结果列表抢走。

**升级 VitePress 时必须把上游同名组件重新 diff 一遍。**
`WikiSiteThemeWiringGuard` 用 fork 头部的 `UPSTREAM: vitepress@x.y.z` 与
`package-lock.json` 的实际版本对齐，版本一动就先红在那里。
`package.json` 因此把 `vitepress` 钉死在具体版本，并显式登记了 fork 用到的
`minisearch` / `mark.js` / `@vueuse/*`（原先靠 npm 提升的幽灵依赖）。

**别再试 `options._render`**（2026-09-06 实测过）：索引器读的那一层拿不到它，
放探针进去重新构建，标记词根本不进索引；而且就算调得到也没用——markdown-it
渲染块级元素时本来就带换行，去标签后单元格已经隔开，不存在「跨单元格拼出假词」这回事。
理由完整记在 `search.mts` 头注释里。

## 4.9 逐页 SEO、编辑链接与 RSS

- `seo.mts`：`transformPageData` 从正文第一段抽 `description`（frontmatter 写了就用 frontmatter 的），
  并把「编辑此页」应指向的源文件写进 `frontmatter.editSource`——WikiContent 源文件或 `hubs/` 手写页，
  都没有的页（首页）隐藏编辑链接；`transformHead` 出 Open Graph、Twitter 卡片、canonical 与
  zh-CN ⇄ en 的 hreflang。
- `feed.mts`：`buildEnd` 生成 `dist/feed.xml`，条目是 catalog 里的每个版本页，日期取版本页
  「发布日期」小节，没写才退回 git 提交时间。
- 绝对地址前缀由 `seo.mts` 的 `siteUrl()` 决定：GitHub Pages 默认；换域名设 `SITE_URL` 环境变量；
  `DEPLOY_TARGET=cloudflare` 又没设 `SITE_URL` 时，sitemap / canonical / hreflang / RSS 一并省略。

## 4.10 实体链接的悬停预览（WikiRefPreview）

`entityLinkPlugin` 给每个实体链接加了 `data-brs-ref="<规范路径>"`；
`WikiRefPreview.vue` 挂在 `Layout.vue` 的 `layout-bottom` 插槽上，
用**事件委托**监听整个文档的 hover / focus，浮出该条目速查框的缩略版
（前 5 行 + 「条目页还有 N 项」）。数据仍然只读 `infobox.mts`，
没配速查框的条目**不弹卡**，只当普通链接。

四条约束：

- **不要用 `<Transition>` 包这张卡。** Vue 的过渡靠双 `requestAnimationFrame`
  推进 enter/leave 状态机，而标签页一旦不可见（切后台、最小化、无头环境）
  rAF 就停摆，离场永远走不完，卡片会卡在屏幕上不消失。淡入用元素自己的
  CSS `animation`，`v-if` 一变 false 节点立刻消失。
- **只在 `(hover: hover) and (pointer: fine)` 的设备上启用。**
  触屏上 hover 是「点一下」的副作用，弹卡会挡住用户真正想点的链接。
- 卡片 `pointer-events: none`，否则它会挡住自己下面的链接。
- 键盘 focus 同样弹、Esc 关闭；滚动 / 缩放 / 路由切换立刻收起。

抽查（浏览器控制台）：

```js
const a = document.querySelector('[data-brs-ref]')
a.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }))
// 250ms 后应有 .brs-peek；再 dispatch mouseout 应立刻消失
```

## 4.11 配图灯箱（WikiLightbox）

正文里三类配图块（`.brs-gallery` / `.brs-figure` / `.brs-icon`）与速查框顶上那枚大图
可以点开看大图，组件在 `theme/components/WikiLightbox.vue`，挂在 `Layout.vue` 的
`layout-bottom` 插槽上。

- **永不超过原始像素**。这是组件唯一一条硬规则：放大到超过原图只会得到一张更大的糊图，
  症结在产物尺寸不在展示尺寸（见 §5）。
- 切图前**先 `decode()` 再换**。正文图片是懒加载的，没进过视口的那些 `naturalWidth`
  是 0；直接读会按缩略图尺寸把灯箱打开（实测第二张会变成 143px）。
  弹层里那张图的 `@load` 再回写一次作为兜底。
- 可点的图由 `extras.css` 给 `cursor: zoom-in`，选择器和组件里的 `SELECTOR`
  **必须一一对应**，guard 会核对。
- **不覆盖**正文里的实体链接小图标与导航图标：那些是 20px 上下的功能贴图，
  没有更多细节，而且点它们应该跳转不是弹窗。
- 不用 `<Transition>`，理由同 §4.10 的悬停预览。

## 5. 图标

`structure.mts` 与 `infobox.mts` 的 `icon` 字段写的是
`scripts/image-manifest.json` 里的 **key**，不是路径。

- **缺图标不是错误**：`WikiIcon.vue` 会退化成按 key 稳定散列上色的首字母字牌，
  所以可以结构先行、图标后补。
- 正因为缺图标静默降级，**打错的 key 和还没出的图长得一模一样**。
  `WikiSiteStructureGuard.py` 因此要求每个 key 要么已在清单里，
  要么已在 `tools/gen_wiki_icons.py` 的 `ICONS` 待生成清单里。

**产物尺寸不得小于展示尺寸。** 浏览器把小图拉大是静默的，页面不报错、只是糊——
「有些图太糊」这条反馈就是这么来的：`.brs-icon` 在正文里按 205px 显示，
而图标产物一度只有 128px，一直在被拉伸。`tools/build_wiki_images.py` 顶部三个常量
（`PORTRAIT_MAX` / `POSTER_MAX` / `ICON_MAX`）就是这条纪律的落点，
`WikiSiteThemeWiringGuard` 把它们和 `style.css` 里 `.brs-icon` / `.brs-figure`
的 `max-width` 绑在一起核对。改展示宽度或改产物尺寸时，两边一起想。

顺带：正文 markdown 图片由 `config.mts` 统一补 `loading="lazy" decoding="async"`。
图鉴那一页有 38 张图，漏了这条就是一开页把整组立绘全拉下来。

三个图片来源：

| 来源 | 生成工具 | 源图位置 |
| --- | --- | --- |
| 图鉴立绘、战役海报、建筑物品图标 | `tools/build_wiki_images.py` | `Assets/`（local-only） |
| 站点导航小图标（`ui` 组） | `tools/gen_wiki_icons.py` | `Assets/wiki_icons/`（local-only） |

两者的 **WebP 产物与清单都要提交**：`Assets/` 被 `.gitignore` 挡着，
GitHub Actions 构建时看不到源图。`gen_wiki_icons.py` 额外写一份
`scripts/wiki-icons.json` 边车清单，`build_wiki_images.py` 读它登记 `ui` 组——
没有这份边车，别人重跑 `build_wiki_images.py` 会把 `ui` 组整个抹掉，
`WikiImageAssetGuard` 立刻红。

## 6. 常用命令

```bash
npm --prefix wiki-site run dev      # 本地预览（会先跑 sync）
npm --prefix wiki-site run build    # 构建，CI 跑的就是这条；产物含 sitemap.xml 与 feed.xml
npm --prefix wiki-site run test:navigation   # 真实 useWiki/withBase 的中英文与两种 base 路由回归
python tools/check_wiki_links.py            # 构建后核对链接与资源落点；根部署追加 --base /
SITE_URL=https://example.com/ npm --prefix wiki-site run build   # 换域名部署时给绝对地址用
python tools/build_wiki_images.py --check    # 只校验图片产物齐不齐
python tools/gen_wiki_icons.py --webp-only   # 不生图，只重出 WebP 与边车清单
python tools/run_guards.py --filter Wiki     # 跑全部 Wiki 相关 guard
npm --prefix wiki-site run preview           # 看构建产物（版式改动要按 1500 / 1366 / 375 三档各看一遍）
```

## 7. 相关 guard

| Guard | 管什么 |
| --- | --- |
| `WikiSiteStructureGuard.py` | 结构 / 页面 / 图标 key 三方一致 |
| `WikiSiteThemeWiringGuard.py` | 速查框注入的三处接线、灯箱挂载与 zoom-in 光标、正文图片懒加载、配图产物不小于展示尺寸、搜索弹层 fork 的 alias 与上游版本、`cjkTokenize` 自包含 |
| `WikiImageAssetGuard.py` | 图片清单、产物、引用三者对得上 |
| `WikiCalloutSingleLineGuard.py` | `WikiContent` 的 callout 必须单行 |
| `ZombieModeMutantWikiGuard.py` | 丧尸模式页与生成产物逐字节一致（**改 `transformContent` 必须同步改它的 Python 镜像**） |
| `BossWikiGuideContentGuard.py` | Boss 攻略页内容约束 |
