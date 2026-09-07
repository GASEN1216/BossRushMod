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
| `docs/.vitepress/` | 配置、结构数据、主题组件、样式（`theme/css/`，见 §8），以及 `search.mts` / `seo.mts` / `feed.mts` 三个构建期模块 |
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

`docs/.vitepress/data/structure.mts` 一处定义全部类目与条目，下面六处**全部**由它生成：

- `WikiPanel.vue` 的左侧门户框栏与 `WikiHead.vue` 的标签行
  （**不再经过 `config.mts` 的 `nav` / `sidebar`**：换皮之后左栏是 MediaWiki 那种
  门户框、顶上是标签页，都不是 VitePress 默认主题的构件，组件直接读本文件。
  横条模式下哪几个类目留在外面由本文件导出的 `NAV_PRIMARY` / `NAV_MORE` 决定。）
- `WikiHome.vue` 的首页门户盒
- `WikiCardGrid.vue` 的类目主页条目清单
- `WikiNavbox.vue` 的页尾同类导航
- `WikiContentSub.vue` 的位置提示（`#siteSub` / `#contentSub`）与 `WikiCatlinks.vue` 的分类栏
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

- **框长在 `.mw-parser-output` 里**（换皮之后正文容器改用 MediaWiki 的同名 class，
  `.vp-doc` 已经整个不存在了）。往框里加新元素前先想一下 `content.css` 里
  `.mw-parser-output` 那几条有没有管它。
- **带整幅边线 / 底色的块要自成 BFC**。浮动只让行盒避让，块盒的边框和底色照旧铺满
  容器宽度——`h2` 那条底线、分隔线、提示块会从框底下穿过去（2026-09-06 报的
  「线穿框」就是这个）。`css/content.css` 给 `h1`~`h6` / `hr` / `blockquote` /
  `.message-box` / `.toc` 都加了 `display: flow-root`（等价于 MediaWiki 给标题的
  `overflow: hidden`，但不会把锚点裁掉）。表格与代码块自带 `overflow` 本来就是 BFC，
  配图块走 `clear: both`。

- 数值以 `WikiContent/zh` 正文为准，**改数值时两边一起改**。guard 只校验路径与图标，
  不校验数值——它没法判断哪边才是对的。
- `links` 里写路径不写标题，标题从 `structure.mts` 取，避免改名时两处漂移。
- 版式：≥641px 右浮动 300px 竖框，更窄时通栏居中。
  门槛从前是 1400，因为默认主题的侧栏 272 + 右侧目录 224 吃掉近 500px；
  换皮之后侧栏 188、目录改成正文里的方框，正文宽度回来了，门槛也就跟着
  目标站回到「窄屏才通栏」。

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
grep -rho '<a class="i brs-eref"[^>]*><img[^>]*><strong>[^<]*' wiki-site/docs/.vitepress/dist --include='*.html' \
  | grep -o '<strong>.*' | sort -u
```

## 4.6 速查框里的稀有度与物品 ID

- `InfoboxRow.tier` 写了才渲染成稀有度色片（5 石板蓝 / 6 紫晶 / 7 黄铜 / 8 余烬）。
  **显式字段，不去嗅探「品质」这个标签**——标签一改口径就静默失效。
  没照抄泰拉那套十二级配色，用的是本站强调色排的递进。
- 同一个 `tier` 还会让**物品名**在正文链接、宫格、页尾导航、速查框相关条目里按档上色
  （`css/widgets.css` §0 的 `[data-tier]` 规则；正文链接由 `entityLinkPlugin` 写属性，
  组件挂 `.wiki-tier`）。三类消费者（`.wiki-rarity` / `.wiki-tier` / `.brs-eref`）
  每一档都要有规则，`tests/WikiThemeTokensGuard.py` 逐条核对。
  色值取自 `css/tokens.css` 的 `--theme-rarity-5..8`，深浅两套各一组。
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
- 带 `tier` 的格子和速查框一样渲染成稀有度色片；`.wiki-rarity` 定义在 `theme/css/widgets.css` §0，
  五处共用，**不要挪回任一组件的 `<style>`**（一加 scoped 其余几处就静默变灰）；
  分组规则（眉标首段）与页尾导航盒共用 `theme/composables/useNavbox.ts` 的
  `eyebrowGroupKey`，改一处两边一起变；
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

`theme/components/WikiSearchBox.vue` 是 **vitepress 自带 `VPLocalSearchBox.vue` 的副本**。

换皮之前它靠 `config.mts` 里一条 Vite alias 顶替默认主题那份；现在默认主题整个不用了，
改由标签行里的搜索框 `WikiHeadSearch.vue` 直接 `import('./WikiSearchBox.vue')`。
两者的分工照抄目标站：**联想下拉**（`WikiHeadSearch`，打字即出、认页面级结果）负责日常，
**弹层**只在回车且没选中候选时打开，当「完整结果页」用，带摘录与高亮。
`Ctrl+K` 开弹层、`/` 聚焦输入框这两个快捷键搬进了 `WikiHeadSearch`。

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

fork 的 scoped 样式里还留着七个 `--vp-*`，**不要逐条改写**（改了就没法再和上游 diff），
它们由 `theme/css/vp-bridge.css` 映射到本站令牌——那也是全站唯一允许给 `--vp-*` 赋值的地方。

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
- 可点的图由 `css/widgets.css` §6 给 `cursor: zoom-in`，选择器和组件里的 `SELECTOR`
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
`WikiSiteThemeWiringGuard` 把它们和 `css/content.css` 里 `.brs-icon` / `.brs-figure`
的 `max-width` 绑在一起核对（那两条规则必须顶格单独成块，guard 的正则认这个写法）。
改展示宽度或改产物尺寸时，两边一起想。

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
python tools/build_wiki_theme_assets.py --check   # 只校验版式贴图与清单（不需要 Pillow）
python tools/run_guards.py --filter Wiki     # 跑全部 Wiki 相关 guard
npm --prefix wiki-site run preview           # 看构建产物（版式改动要按 1500 / 1366 / 900 / 375 四档各看一遍）
```

版式改动的目视清单（预览时逐档过）：网络顶栏 35px 固定、Logo 带 140px（背景图，
字标是无障碍隐藏的文本）、第一枚门户框顶上的草皮条、门户框与标签行的木纹/冰霜贴图、
页面底图的天空、当前类目默认展开、选中标签与内容面板连成一体、`h1` / `h2` 的双线不穿过
右浮的速查框、目录框在导语之后第一个 `h2` 之前、页尾 navbox → 分类栏 → 页脚三段齐全。
浅色皮肤在 URL 后加 `?skin-theme=Snow` 就能直接看，不用点菜单。

## 7. 相关 guard

| Guard | 管什么 |
| --- | --- |
| `WikiSiteStructureGuard.py` | 结构 / 页面 / 图标 key 三方一致 |
| `WikiSiteThemeWiringGuard.py` | 速查框 / 位置提示 / 目录框三套渲染期注入的接线、灯箱与悬停预览挂载、zoom-in 光标、`markdown.headers`、正文图片懒加载、配图产物不小于展示尺寸、搜索弹层 fork 的直接 import 与上游版本、`cjkTokenize` 自包含、没有继承默认主题也没有引网络字体 |
| `WikiThemeTokensGuard.py` | 皮肤令牌的契约：引用的令牌都有定义、浅色皮肤覆盖齐全、`--brs-*` 已退场、`--vp-*` 只在桥接文件里赋值、稀有度规则没被搬进组件、`useWiki.ts` 不 import 组件 |
| `WikiThemeSwitchGuard.py` | 换肤的三方对齐（内联脚本 / `themes.mts` / `tokens.css`）与页脚的版式归属声明 |
| `WikiThemeAssetGuard.py` | 版式贴图的在场 / 归属 / 字节数 / 每套皮肤的下载预算，外加「没有网络字体」 |
| `WikiImageAssetGuard.py` | 图片清单、产物、引用三者对得上 |
| `WikiCalloutSingleLineGuard.py` | `WikiContent` 的 callout 必须单行 |
| `ZombieModeMutantWikiGuard.py` | 丧尸模式页与生成产物逐字节一致（**改 `transformContent` 必须同步改它的 Python 镜像**） |
| `BossWikiGuideContentGuard.py` | Boss 攻略页内容约束 |

## 8. 版式与皮肤

站点版式参照 **Official Terraria Wiki**（`terraria.wiki.gg`，MediaWiki 的 Vector-legacy
皮肤 + 它自己那套主题系统）的公开呈现，逐值测量之后**净室重写**：`theme/css/` 里没有
一行来自它的样式表，Logo、纹理、徽标全部自制，未使用任何 Re-Logic 素材。
它的 Wiki 内容以 CC BY-NC-SA 4.0 授权、游戏美术版权归 Re-Logic，本站均未收录。
界面图标按 Tabler Icons（MIT）的线条口径自绘，内联成 data URI 当 CSS mask 用。

页脚那行归属声明（文案在 `theme/composables/useUiText.ts` 的 `attribution`）是这件事
的唯一对外说明，`tests/WikiThemeSwitchGuard.py` 守着它。

DOM 的 id 与 class 一律沿用 MediaWiki 的原名（`#mw-panel` / `#mw-head` / `#content` /
`.mw-parser-output` / `.infobox` / `.navbox` / `.toc` / `.catlinks` / `table.terraria` /
`.i` …）。它们是通用名字，照抄的好处是 CSS 能逐条对着目标站的实现改，排错时不用做
心智转换。**例外**是几个被别处绑住的自有 class，改名会连带弄坏别的东西：
`.brs-gallery` / `.brs-figure` / `.brs-icon`（`sync-content.mjs` 写进 `.md`，产物被
`ZombieModeMutantWikiGuard` 逐字节比对）、`.brs-table-scroll`、`.brs-eref` 与
`data-brs-ref` / `data-tier`（悬停预览与稀有度上色的钩子）。

样式分九层，`theme/index.ts` 里的 import 顺序就是层叠顺序：

| 文件 | 管什么 |
| --- | --- |
| `css/tokens.css` | 全部 `--theme-*` / `--layout-*` / 字体令牌；`:root` 是默认皮肤 Overworld（深色），`html.theme-Snow` 是浅色 |
| `css/base.css` | 浏览器默认值。**没有**抄目标站那句 `* { outline: 0 }`——那会让键盘用户彻底看不见焦点，这里改成只在 `:focus-visible` 描边 |
| `css/icons.css` | Tabler 口径的图标，`--icon-*` + `.tw-icon--*`，用 CSS mask 上色，所以自动跟着皮肤走 |
| `css/vp-bridge.css` | **唯一**允许给 `--vp-*` 赋值的地方，只服务搜索弹层那个 fork |
| `css/layout.css` | 网格骨架与外壳：网络顶栏 / Logo 带 / 门户栏 / 标签行 / 内容面板 / 分类栏 / 页脚 |
| `css/content.css` | `.mw-parser-output` 里的一切：标题、目录框、提示框、表格、配图块、代码 |
| `css/widgets.css` | 稀有度、信息框、导航盒、条目卡、对比表表头、悬停预览、灯箱、搜索联想 |
| `css/mainpage.css` | 首页 `#mainpage-wrapper` |
| `css/responsive.css` | 全部媒体查询集中在这里：≥2472 / ≤1800 / ≤1366 / ≤900 / ≤720 / ≤640 / ≤600 |
| `css/print.css` | 打印 |

**颜色一律走令牌，不写字面值。** 加一套皮肤三步，构件一个字不用改：

1. `css/tokens.css` 抄一段 `html.theme-<Name>` 覆盖块，把每个 `--theme-*` 填满；
2. `data/themes.mts` 的 `THEMES` 加一条 `{ name, view, label }`，`label` 是
   `useUiText.ts` 里的词条键；
3. `config.mts` 里 `SKIN_THEME_INIT` 那段内联脚本的主题表加一项。

三处漏一处都不会报错，只会「刷新丢皮肤」「菜单里选不到」或「类挂上了颜色没换」，
所以 `tests/WikiThemeSwitchGuard.py` 把它们绑在一起核对。

那段内联脚本必须**自包含**：它以字符串形式写进 HTML，拿不到模块作用域，引用任何
外部标识符都会在浏览器里直接抛错（和 `search.mts` 的 `cjkTokenize`、`config.mts` 的
`EDIT_LINK_PATTERN` 是同一条约束）。`config.mts` 也因此设了 `appearance: false`——
否则 VitePress 自带的 `check-dark-mode` 会和它抢 `<html>` 上的 `.dark`。

### 8.1 版式贴图

材质由 `theme/assets/` 下的七张图提供，由 `tools/build_wiki_theme_assets.py` 在本机产出，
清单（含字节数与所属皮肤）落在 `wiki-site/scripts/wiki-theme-assets.json`：

| 产物 | 皮肤 | 怎么来的 | 接在哪个令牌 |
| --- | --- | --- | --- |
| `sky-overworld.webp` / `sky-snow.webp` | 各一套 | 生图（1536×1024 → 裁 16:10 → WebP） | `--theme-site-background-image` |
| `wood.webp` / `frost.webp` | 各一套 | 生图 → 镜像四拼平铺 → 只留亮度起伏的半透明颗粒层 | `--theme-widget-texture` |
| `grass.png` / `grass-snow.png` | 各一套 | 程序化绘制（192×26，CSS 按 96×13 铺） | `--theme-top-background` |
| `logo.webp` | 两套共用 | 生图出徽记（风格串与图鉴立绘同源）+ Pillow 排字标 | `--theme-site-logo-image` |

三件事值得知道：

- **贴图是半透明的颗粒层，不是不透明的图。** 底色仍由 `--theme-panel-background`
  之类决定，所以同一张 `wood.webp` 铺在棕色面板上是木头、铺在浅蓝上是冰，
  换皮肤不会串色。做法是只保留源图的亮度起伏（比平均亮给白、暗给黑，偏离越多越不透明）。
- **平铺靠镜像四拼**，接缝两侧像素天然相等，永远不会有缝。代价是有对称感，
  所以图样越「像个东西」（冰花是典型）越要先柔化、再压低不透明度。
- **草皮条与字标不生图。** 13px 的草皮条：1024 的图缩下去只会糊成一条色带，
  程序化画又小又脆还能保证左右接缝对齐。字标：生图模型写不对字母。
- **徽记的 `STYLE` 与 `DUCK` 必须和 `tools/gen_codex_art.py` 逐字相同。**
  徽记、类目图标、图鉴立绘是同一批读者在同一个页面上看到的；风格串一分叉画风就打架
  （2026-09-07 试过给徽记单开一套赛璐璐平涂，摆在厚涂图标旁边一眼看出是两个世界，
  被 owner 打回）。`WikiThemeAssetGuard` 的检查 7 守着这两个常量三处一致。
  景（`SCENE_STYLE`）与材质（`TILE_STYLE`）是另一回事，它们不画角色，各自一套。
- **拟人鸭要在正面描述里反复点名 `ANTHROPOMORPHIC DUCK`**，
  写在 negative prompt 里挡不住模型把它画成人。
- **出图后先验背景是不是 `#ff00ff`。** 提示词里出现往暗里推的措辞
  （`gunmetal grey`、`out of the dark`）会盖过 `STYLE` 末尾那句色键要求，
  网关直接给黑底；这时候照抠不误，抠出来是一张**整体半透明**的图，
  没有任何报错，摆进 Logo 才看出不对。`generate_one()` 现在验四角，
  不是洋红就判这张废、留给断点续跑补。
- **字标要画两遍描边，外面那遍更宽。** 字与字之间是两条描边背靠背（14~19px），
  首尾字母的外缘只有一条（7px），一半的厚度差在「B」那道长直左竖上看着像被削平了
  （owner 报过）。外轮廓那一遍只加厚外缘，字缝里本来就是实心墨色，不受影响。

产物放 `theme/assets/`（**不要**放 `docs/public/images/`——那是 `WikiImageAssetGuard`
的地盘，没进 `image-manifest.json` 的 WebP 会被判成孤儿）。放这儿还有个好处：
Vite 会打哈希并自动补 `base` 前缀，根部署和子路径部署都不用改一个字；
小于 4 KB 的（两张草皮条）会被直接内联成 data URI，省两个请求。

浏览器只请求**当前皮肤真的用到**的 `url()`，所以预算是按皮肤算的：
单套 ≤ 600 KB、单张天空 ≤ 200 KB，现状 Overworld / Snow 各约 100 KB。
`tests/WikiThemeAssetGuard.py` 守着在场、归属、字节数与预算四件事，
归属以 `tokens.css` 里级联解出来的实际值为准，不认清单里手写的。

五张源图（天空 ×2、木纹、冰霜、徽记）都在 `Assets/wiki_theme/`，是 local-only 的
（`Assets/` 被 `.gitignore` 挡着），重出要走生图网关。
别人机器上没有源图也能校验，因为 guard 只读产物大小和清单。

### 8.4 下拉的 Esc：CSS 开、JS 关

「更多」（`#p-cactions`）与「外观」（`#p-appearance`）两个下拉照抄 Vector-legacy，
开合全靠 CSS 的 `:hover` / `:focus-within`——零状态、SSR 就位、鼠标体验和目标站一致。
但**纯 CSS 关不掉 Esc**：焦点还在下拉里，`:focus-within` 就还是真。

缺口由 `composables/useDropdownDismiss.ts` 补，两个下拉共用一份：

- Esc → 把焦点送回标题按钮（WAI-ARIA 对菜单的期待），同时挂 `is-dismissed`；
- `focusout` 且新焦点不在下拉内 → 清掉 `is-dismissed`，否则下次 Tab 进来打不开；
- 标题按钮的 `click` → 也清掉，让按过 Esc 之后鼠标还能正常打开。

**压制规则的选择器必须带容器前缀**（`#mw-head …` / `.wgg-netbar …`）：
裸类名的特指度比展开规则里那条 `#mw-head …:focus-within` 低一档，压不住，
表现是「类挂上了、菜单没关」。`WikiSiteThemeWiringGuard` 的检查 13 守这一整条链，
而且是按**解构出来的别名**去核模板接线的——只查「名字出现过」的话，
文档注释里提一句就能骗过去（反向验证实测）。

## 9. 不再有的东西

换皮之后这几样按目标站的做法**取消**了，不是漏做：

- VitePress 的顶栏 / 侧边栏 / 右侧悬浮目录（改成门户框栏 + 标签行 + 正文里的 `.toc` 方框）；
- 正文底部的「上一篇 / 下一篇」（同类导航由页尾的 navbox 承担）；
- 面包屑（改成 `#siteSub` + `#contentSub` 两行小字，占的高度只有一半）；
- 首页那个大搜索框（搜索入口统一在标签行右侧）；
- Google Fonts 三族（正文 Helvetica、标题 Verdana，中文交给系统字体，与目标站中文版
  的实际渲染同一口径）。这一项加上不再打包默认主题，产物少了 14 个 woff2、
  3 条外链，样式表从 147 KB 降到 85 KB（含内联的两张草皮条），
  换来的版式贴图每套皮肤约 100 KB。
