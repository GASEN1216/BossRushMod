import { readFileSync } from 'fs'
import { dirname, resolve } from 'path'
import { fileURLToPath } from 'url'
import { defineConfig, type HeadConfig } from 'vitepress'
import { CATEGORIES, localizePath, type Locale } from './data/structure.mts'
import { INFOBOX } from './data/infobox.mts'
import { getRoute, readCatalog } from '../../scripts/entry-map.mjs'
import { SEARCH } from './search.mts'
import { decoratePage, headFor, siteUrl } from './seo.mts'
import { writeChangelogFeed } from './feed.mts'

const __dirname = dirname(fileURLToPath(import.meta.url))

/*
 * 导航不再由本文件生成。
 *
 * 换皮之后左栏是 MediaWiki 那种「门户框」而不是 VitePress 的侧边栏，
 * 标签行也不是 VPNavBar，两者都由 theme/components/WikiPanel.vue 与
 * WikiHead.vue 直接读 data/structure.mts 渲染。于是从前这里的
 * navFor / sidebarFor / getChangelogItems 一并删掉，
 * NAV_PRIMARY / NAV_MORE 挪回了唯一事实源 structure.mts。
 */

// ── 导出配置 ──────────────────────────────────────────────
const base = process.env.DEPLOY_TARGET === 'cloudflare' ? '/' : '/BossRushMod/'

// 站点绝对地址（含 base）：GitHub Pages 默认；换域名用 SITE_URL 环境变量（见 seo.mts）。
// 取不到时 sitemap / canonical / hreflang / RSS 这些必须是绝对地址的东西一并省略。
const SITE_URL = siteUrl(base)

const FEED_HEAD: HeadConfig[] = SITE_URL
  ? [['link', { rel: 'alternate', type: 'application/rss+xml', title: 'BossRush Wiki · 更新日志', href: `${SITE_URL}feed.xml` }]]
  : []

/**
 * 首帧就把皮肤类挂到 <html> 上，避免刷新时先闪一下默认色。
 *
 * 必须**自包含**：它是以字符串形式写进 HTML 的内联脚本，引用不到任何模块。
 * 主题表要与 data/themes.mts 和 theme/css/tokens.css 里的 html.theme-* 对齐
 * （tests/WikiThemeSwitchGuard.py 三方核对）。
 * 认 ?skin-theme=Snow 查询串，方便截图与分享指定皮肤。
 */
const SKIN_THEME_INIT =
  "(function(){try{var T={Overworld:'dark',Snow:'light'},d=document.documentElement,q=null;" +
  "try{q=new URL(location.href).searchParams.get('skin-theme')}catch(e){}" +
  "var t=q||localStorage.getItem('skin-theme');if(!T[t])t='Overworld';" +
  "d.classList.add('theme-'+t,'view-'+T[t]);if(T[t]==='dark')d.classList.add('dark');}catch(e){}})()"

/**
 * 「编辑此页」：指向 WikiContent/ 的源文件，不是 wiki-site/docs/ 的生成物（改生成物会被 sync 抹掉）。
 * 这个函数在**浏览器里**执行（VitePress 把 themeConfig 里的函数序列化进站点数据），
 * 拿不到 Node 侧的映射表，所以源文件路径由 seo.mts 在构建期写进每页的 frontmatter.editSource；
 * 函数体必须自包含——不要在这里引用任何外部常量。
 */
const EDIT_LINK_PATTERN = (page: { frontmatter: Record<string, any> }) =>
  'https://github.com/GASEN1216/BossRushMod/edit/main/' + page.frontmatter.editSource

// ── 列表项里的实体名 → 小图标 + 链接 ─────────────────────
/**
 * 泰拉瑞亚 Wiki 最认得出来的一件事，是**正文里每次提到一件装备都带着它的贴图**：
 * 掉落表、合成表、清单，名字前面永远有个 32px 小图。我们的总览页此前是
 * 「- **龙裔套装**（头盔+护甲）— 龙裔遗族掉落」这样一行纯文字，
 * 读者想看它长什么样、想跳过去，都得回侧栏找。
 *
 * 为什么放在 markdown-it 而不是 sync-content.mjs：
 *   sync 的产物 `wiki-site/docs/game-modes/zombie-mode.md` 被
 *   tests/ZombieModeMutantWikiGuard.py **逐字节**比对。在 sync 里塞图标，
 *   那个 guard 立刻红，而且以后每改一次装饰规则都要同步改它的 Python 镜像。
 *   放在渲染层，生成的 .md 一个字节都不变，装饰只存在于 HTML 里。
 *
 * 匹配范围刻意收得很窄——只认「列表项开头（或表格单元格整格）、独占一个 **加粗**、
 * 且文字与 structure.mts 里某个条目名（或 catalog.tsv 里的标题）**完全相等**」这一种形态：
 *   ✓ - **逆鳞** — 焚天龙皇掉落…        （逆鳞是条目名）
 *   ✗ - **火焰伤害转治疗**的核心被动…   （不是条目名，原样保留）
 *   ✗ …掉落：赤龙首、焰鳞甲、龙息        （不在行首加粗位，不碰）
 * 宁可漏，不可错：漏了只是少一个图标，错了会把正文里的普通强调变成乱链接。
 */
type EntityHit = { href: string; icon: string | null; canonical: string }

function buildEntityIndex(locale: Locale): Map<string, EntityHit> {
  const iconSrc = new Map<string, string>()
  try {
    const raw = readFileSync(resolve(__dirname, '..', '..', 'scripts', 'image-manifest.json'), 'utf-8')
    for (const group of Object.values(JSON.parse(raw) as Record<string, { key: string; src: string }[]>)) {
      for (const item of group ?? []) iconSrc.set(item.key, item.src)
    }
  } catch {
    // 清单缺失时退化成「只加链接不加图标」，不阻断构建
  }

  const index = new Map<string, EntityHit>()
  for (const category of CATEGORIES) {
    for (const entry of category.entries) {
      const name = locale === 'en' ? entry.en : entry.zh
      if (!name || index.has(name)) continue
      const src = entry.icon ? iconSrc.get(entry.icon) ?? null : null
      // href / src 都**不带 base**：交给 VitePress 自己的 link_open 与 image
      // 渲染规则去补前缀。自己拼 base 会让 Vite 把 <img src> 当模块去解析，
      // 报 "Rollup failed to resolve import /BossRushMod/images/..." —— 这正是
      // sync-content.mjs 里 renderImageBlock() 那段注释警告过的同一个坑。
      index.set(name, {
        href: localizePath(entry.path, locale),
        icon: src,
        canonical: entry.path.replace(/\/$/, ''),
      })
    }
  }

  // catalog.tsv 里的标题也算别名：目录写「百战留痕（黑市鸭王杯）」而 structure.mts 写「百战留痕」，
  // 模式总览的清单用的是前者。别名只补不覆盖，structure.mts 的名字优先。
  const byCanonical = new Map<string, EntityHit>()
  for (const hit of index.values()) byCanonical.set(hit.canonical, hit)
  for (const row of readCatalog()) {
    const route = getRoute(row.entryId)
    if (!route) continue
    const canonical = '/' + route.replace(/\.md$/, '').replace(/\/index$/, '')
    const alias = (locale === 'en' ? row.titleEn : row.titleZh).trim()
    const hit = byCanonical.get(canonical)
    if (alias && hit && !index.has(alias)) index.set(alias, hit)
  }
  return index
}

const ENTITY_INDEX = { zh: buildEntityIndex('zh'), en: buildEntityIndex('en') }

/** 'en/equipment/dragon-set.md' → { locale:'en', canonical:'/equipment/dragon-set' } */
function pageIdentity(relativePath: string) {
  let p = '/' + (relativePath || '').replace(/\.md$/, '')
  const locale: Locale = p.startsWith('/en/') || p === '/en' ? 'en' : 'zh'
  if (locale === 'en') p = p.slice(3) || '/'
  p = p.replace(/\/index$/, '')
  return { locale, canonical: p.replace(/\/$/, '') || '/' }
}

export function entityLinkPlugin(md: any) {
  md.core.ruler.push('brs_entity_links', (state: any) => {
    const { locale, canonical } = pageIdentity(state.env?.relativePath ?? '')
    const index = ENTITY_INDEX[locale]
    const tokens = state.tokens

    for (let i = 0; i < tokens.length; i++) {
      if (tokens[i].type !== 'inline') continue
      // 两种落点，都用 token 结构定位而不是正则扫原文，才不会误伤代码块里的同名文字：
      //   「列表项里的第一段」＝ list_item_open, paragraph_open, inline；
      //   「表格单元格」      ＝ td_open, inline —— 给 sync 列表转表格后的首列用
      //                        （模式总览那张表的首列就是 **模式名**）。
      const inListItem =
        tokens[i - 1]?.type === 'paragraph_open' && tokens[i - 2]?.type === 'list_item_open'
      const inCell = tokens[i - 1]?.type === 'td_open'
      if (!inListItem && !inCell) continue

      const kids = tokens[i].children
      if (!kids) continue

      // 加粗前面常常挂着一个**空 text token**（markdown-it 切 inline 时留下的），
      // 直接按 kids[0] 判断会全线不匹配——这里先跳过开头的空文本。
      let s = 0
      while (s < kids.length && kids[s].type === 'text' && kids[s].content === '') s++
      if (kids[s]?.type !== 'strong_open') continue
      if (kids[s + 1]?.type !== 'text' || kids[s + 2]?.type !== 'strong_close') continue

      const hit = index.get(kids[s + 1].content.trim())
      if (!hit || hit.canonical === canonical) continue // 不自指

      // 用 markdown-it 原生 token 而不是裸 HTML：这样 VitePress 的
      // link_open / image 渲染规则会照常补 base、走客户端路由、纳入死链扫描。
      const linkOpen = new state.Token('link_open', 'a', 1)
      linkOpen.attrs = [
        // `i` 是 MediaWiki / 泰拉瑞亚 Wiki 里「带贴图的物品链接」那个类；
        // `brs-eref` 留着不动，悬停预览与稀有度上色都靠它认人。
        ['class', 'i brs-eref'],
        ['href', hit.href],
        // 悬停预览（WikiRefPreview.vue）靠这个属性反查速查框数据。
        // 写规范路径而不是 href：href 带了 base 与语言前缀，组件那边还要再剥一次。
        ['data-brs-ref', hit.canonical],
      ]
      // 稀有度：物品名按档上色（extras.css §0 的 [data-tier] 规则）。
      // 只有 infobox.mts 给了 tier 的条目才写这个属性，Boss / 模式 / 系统没有稀有度。
      const tier = INFOBOX[hit.canonical]?.rows.find((r) => r.tier)?.tier
      if (tier) linkOpen.attrs.push(['data-tier', String(tier)])
      const linkClose = new state.Token('link_close', 'a', -1)

      const pieces: any[] = [linkOpen]
      if (hit.icon) {
        const image = new state.Token('image', 'img', 0)
        image.attrs = [
          ['class', 'brs-eref__icon'],
          ['src', hit.icon],
          ['alt', ''],
        ]
        image.children = []
        image.content = ''
        pieces.push(image)
      }
      // 原来的 strong_open / text / strong_close 直接复用，加粗语义不丢
      pieces.push(kids[s], kids[s + 1], kids[s + 2], linkClose)
      kids.splice(s, 3, ...pieces)
    }
  })
}

// ── 速查框注入到 h1 之后 ────────────────────────────────────
/**
 * 速查框（WikiInfobox）从前挂在默认主题的 `doc-before` 插槽上。那个插槽落在
 * `.content-container` 里、`main > .vp-doc` **之前**，于是 DOM 顺序是
 * 「速查框 → h1」：宽屏上框右浮，h1 却在更深一层、仍是整宽块，它那条 2px 底线
 * 就整幅画过框身（实测 1500 视口下正好穿过第一行数据）；窄屏上更直白——
 * 读者先看到一张大卡片，才看到标题。
 *
 * 正确的位置是**标题之后、正文之前**，和泰拉瑞亚 / 维基百科一样。
 * 但正文来自 sync 产物，一个字节都不能改（`ZombieModeMutantWikiGuard` 逐字节比对），
 * 所以和 entityLinkPlugin 同一套办法：在**渲染层**往 token 流里插一个 `html_block`，
 * 内容是 `<WikiInfobox path="..." />`。它随后由 VitePress 的 markdown→Vue 编译原样带进模板，
 * 解析成全局注册的组件（`docs/index.md` 里的 `<WikiHome />` 走的就是这条路）。
 *
 * 两个必须这么写的点：
 *   - 组件名后面带 `path` 属性（规范路径），**不让组件自己去查路由**。
 *     它现在渲染在页面组件里而不是 Layout 里，SSR 阶段少一层对路由的依赖，
 *     就少一类 hydration 不一致的风险；顺带 DOM 上能直接看出这框属于哪个条目。
 *   - 只给 `INFOBOX` 里有数据的页面插。没有数据的页面插了也只会渲染出空，
 *     组件里那句 `v-if="box && located"` 是第二道闸。
 *
 * 搜索索引器用的是同一个 markdown 实例（见 vitepress 的 localSearchPlugin），
 * 所以索引里的 HTML 也会带上这个标签——它随后被 `clearHtmlTags` 整个剥掉，
 * 索引内容不受影响。
 */
export function infoboxSlotPlugin(md: any) {
  md.core.ruler.push('brs_infobox_slot', (state: any) => {
    // md.parseInline() 也会跑 core 规则（VitePress 推断页面标题时就会调它），
    // 那趟的 env 是空对象、tokens 是一条 inline。不挡住就会往标题里插一个组件标签。
    if (state.inlineMode) return

    const { canonical } = pageIdentity(state.env?.relativePath ?? '')
    if (!INFOBOX[canonical]) return

    const tokens = state.tokens

    // 插在第一个 h1 的 heading_close 之后；没有 h1 的页面退回开头
    // （等于旧的 doc-before 位置，宁可版式退化也不能把框弄丢）。
    let at = 0
    for (let i = 0; i < tokens.length; i++) {
      if (tokens[i].type !== 'heading_open' || tokens[i].tag !== 'h1') continue
      for (let j = i + 1; j < tokens.length; j++) {
        if (tokens[j].type === 'heading_close' && tokens[j].tag === 'h1') {
          at = j + 1
          break
        }
      }
      break
    }

    const slot = new state.Token('html_block', '', 0)
    slot.content = `<WikiInfobox path="${canonical}" />\n`
    slot.block = true
    tokens.splice(at, 0, slot)
  })
}

/**
 * #siteSub / #contentSub：标题之下那两行小字，插在第一个 h1 之后。
 *
 * 与速查框同一套办法（渲染期往 token 流里塞一个 html_block），因此 sync 的
 * 产物一个字节不变。**必须比 infoboxSlotPlugin 后登记**：两者插入点都是
 * 「h1 之后」，后登记的先执行完再被前一个推开，最终顺序才是
 * h1 → #contentSub → 速查框，与目标站一致。
 */
export function contentSubSlotPlugin(md: any) {
  md.core.ruler.push('brs_contentsub_slot', (state: any) => {
    if (state.inlineMode) return
    const { canonical } = pageIdentity(state.env?.relativePath ?? '')
    if (canonical === '/') return // 首页是门户版式，没有这两行

    const tokens = state.tokens
    let at = -1
    for (let i = 0; i < tokens.length; i++) {
      if (tokens[i].type !== 'heading_open' || tokens[i].tag !== 'h1') continue
      for (let j = i + 1; j < tokens.length; j++) {
        if (tokens[j].type === 'heading_close' && tokens[j].tag === 'h1') {
          at = j + 1
          break
        }
      }
      break
    }
    if (at < 0) return // 没有 h1 就不插：位置提示挂在标题下面才讲得通

    const slot = new state.Token('html_block', '', 0)
    slot.content = `<WikiContentSub path="${canonical}" />\n`
    slot.block = true
    tokens.splice(at, 0, slot)
  })
}

/**
 * 目录框：插在**第一个 h2 之前**（MediaWiki 的位置——导语之后、正文之前）。
 *
 * 组件自己判断标题够不够四个，不够就整个不渲染，所以这里无脑插即可。
 * 没有 h2 的页面（首页、少数短页）不插。
 */
export function tocSlotPlugin(md: any) {
  md.core.ruler.push('brs_toc_slot', (state: any) => {
    if (state.inlineMode) return
    const { canonical } = pageIdentity(state.env?.relativePath ?? '')
    if (canonical === '/') return

    const tokens = state.tokens
    const at = tokens.findIndex((t: any) => t.type === 'heading_open' && t.tag === 'h2')
    if (at < 0) return

    const slot = new state.Token('html_block', '', 0)
    slot.content = '<WikiToc />\n'
    slot.block = true
    tokens.splice(at, 0, slot)
  })
}

/** ::: tip / ::: warning 的容器名 -> 提示框配色与图标（照抄 MediaWiki 的 .message-box）。 */
const MSGBOX: Record<string, { color: string; icon: string }> = {
  tip: { color: 'blue', icon: 'info' },
  info: { color: 'green', icon: 'info' },
  warning: { color: 'yellow', icon: 'alert' },
  danger: { color: 'red', icon: 'alert' },
}

export default defineConfig({
  title: 'BossRush Wiki',
  description: 'Escape from Duckov — BossRush Mod 百科',
  base,
  cleanUrls: true,

  // 深浅切换归本站的 skin-theme 管（见 head 里的内联脚本与 theme/composables/useSkinTheme.ts）。
  // 不关掉的话 VitePress 自己那段 check-dark-mode 会和我们抢 <html> 上的 .dark。
  appearance: false,

  // 锚点跳转要让开固定的网络顶栏（35px），默认值 134 是给它自己那条顶栏的
  scrollOffset: 35,

  // 页面「最后更新」时间取自 git。CI 的 checkout 必须 fetch-depth: 0，
  // 否则浅克隆里所有文件都是同一个提交时间（见 .github/workflows/deploy.yml）。
  lastUpdated: true,

  // sitemap.xml：VitePress 会把中英两份页面配成 hreflang 对；hostname 必须带 base
  sitemap: SITE_URL ? { hostname: SITE_URL } : undefined,

  // 逐页元数据：description 取正文第一段、编辑链接反查源文件（transformPageData），
  // OG / canonical / hreflang（transformHead），更新日志 RSS（buildEnd）。实现见 seo.mts / feed.mts。
  transformPageData(pageData, ctx) {
    decoratePage(pageData, ctx)
  },
  transformHead(ctx) {
    return headFor(ctx)
  },
  buildEnd(siteConfig) {
    writeChangelogFeed(siteConfig)
  },

  markdown: {
    // 表格外套一层横向滚动框：窄屏下宽表格自己滚，不撑破版心。
    // 这层 BFC 顺带让表格在速查框（右浮动）旁边自动收窄而不是被压住。
    // 对应档案报告里的 .tw 包裹层，样式见 theme/style.css §7。
    // 目录框要用 page.headers，而 VitePress 只有开了这一项才会填它（默认不填）。
    headers: { level: [2, 3] },

    config(md) {
      // 表格套 terraria 皮（内框 + 高亮表头），外面那层横向滚动框保持不变
      md.renderer.rules.table_open = () =>
        '<div class="brs-table-scroll"><table class="terraria lined">'
      md.renderer.rules.table_close = () => '</table></div>'

      // h1 补 firstHeading：正文里那条标题线与 MediaWiki 同名同款
      const renderToken = md.renderer.renderToken.bind(md.renderer)
      md.renderer.rules.heading_open = (tokens: any, idx: number, options: any, _env: any, self: any) => {
        if (tokens[idx].tag === 'h1') tokens[idx].attrJoin('class', 'firstHeading')
        return renderToken(tokens, idx, options, self)
      }

      /*
       * 提示框改写成 .message-box。
       *
       * VitePress 的容器插件在本回调**之前**注册（dist 里 containerPlugin 先跑、
       * options.config(md) 最后跑），所以这里覆盖它的渲染规则是稳的。
       * 只改渲染层：生成的 .md 仍是 `::: tip`，ZombieModeMutantWikiGuard 的
       * 逐字节比对不受影响。
       */
      for (const [name, box] of Object.entries(MSGBOX)) {
        md.renderer.rules[`container_${name}_open`] = () =>
          `<div class="message-box msgbox-color-${box.color}">` +
          `<div class="icon"><span class="tw-icon tw-icon--${box.icon}" aria-hidden="true"></span></div>` +
          `<div class="msgbox-text">\n`
        md.renderer.rules[`container_${name}_close`] = () => '</div></div>\n'
      }

      md.use(entityLinkPlugin)
      md.use(infoboxSlotPlugin)
      // 顺序要紧：contentSub 后登记，才会排在速查框**之前**（两者插入点同为 h1 之后）
      md.use(contentSubSlotPlugin)
      md.use(tocSlotPlugin)

      /*
       * 正文配图一律懒加载。
       *
       * markdown 里的 `![]()` 渲染出来是一个光秃秃的 <img>，没有 loading 属性——
       * 图鉴那一页有 38 张图，等于一开页就把整组立绘（1.3 MB）全拉下来，
       * 而首屏最多看得到十来张。WikiIcon.vue 那些组件图标一直是 lazy 的，
       * 只有走 markdown 这条路的漏了。
       *
       * 包住既有规则而不是替换：图片 src 的 base 前缀是 VitePress 在别处做的，
       * 这里只往 token 上补两个属性再交回去，不碰它的渲染逻辑。
       */
      const renderImage = md.renderer.rules.image
      md.renderer.rules.image = (tokens: any, idx: number, options: any, env: any, self: any) => {
        const token = tokens[idx]
        if (!token.attrGet('loading')) token.attrSet('loading', 'lazy')
        if (!token.attrGet('decoding')) token.attrSet('decoding', 'async')
        return renderImage
          ? renderImage(tokens, idx, options, env, self)
          : self.renderToken(tokens, idx, options)
      }
    },
  },

  // 搜索弹层从前靠一条 Vite alias 顶替 VitePress 默认主题里的 VPLocalSearchBox；
  // 现在默认主题整个不在了，`theme/components/WikiHeadSearch.vue` 直接
  // import('./WikiSearchBox.vue')，alias 也就没必要了（fork 本身照旧，
  // 仍要按它头部的 UPSTREAM 标记跟着 VitePress 版本走）。

  head: [
    ['link', { rel: 'icon', href: `${base}images/favicon.ico` }],
    // 地址栏配色跟着皮肤走；这里给的是默认皮肤（Overworld）的值，
    // 读者切换时由 useSkinTheme 就地改写成 --theme-meta-color。
    ['meta', { name: 'theme-color', content: '#000538' }],
    ...FEED_HEAD,
    // 首帧就把皮肤类挂上，避免刷新时闪一下。必须在样式表之前同步执行。
    ['script', { id: 'skin-theme-init' }, SKIN_THEME_INIT],
    // 字体一个都不下载：正文 Helvetica、标题 Verdana，中文交给系统字体，
    // 与目标站中文版的实际渲染同一口径（见 theme/css/tokens.css）。
  ],

  locales: {
    root: {
      label: '中文',
      lang: 'zh-CN',
      // nav / sidebar / outline / docFooter 这些都是默认主题的配置项，本站已经不用它了：
      // 导航来自 structure.mts、目录是正文里的 .toc、上下篇按目标站的做法取消。
      // 界面文案统一在 theme/composables/useUiText.ts（放这里会被序列化进每一页）。
      themeConfig: {
        editLink: { pattern: EDIT_LINK_PATTERN, text: '在 GitHub 上编辑此页' },
        notFound: {
          code: '404',
          title: '这一页不存在',
          quote: '可能是链接写错了，也可能这一页还没写出来。回首页从索引找，或者用搜索。',
          linkLabel: '返回首页',
          linkText: '回首页',
        },
      },
    },
    en: {
      label: 'English',
      lang: 'en',
      description: 'Escape from Duckov — BossRush Mod Wiki',
      themeConfig: {
        editLink: { pattern: EDIT_LINK_PATTERN, text: 'Edit this page on GitHub' },
      },
    },
  },

  themeConfig: {
    /*
     * RSS 的地址（没有就是 null）。
     *
     * feed.mts 只在拿得到站点绝对地址时才写 dist/feed.xml —— `DEPLOY_TARGET=cloudflare`
     * 又没设 SITE_URL 时它整个不生成。主题层几处「订阅」入口因此不能写死链接，
     * 得按这个值决定渲染不渲染；写死的下场是根部署下三处死链
     * （2026-09-07 实测：check_wiki_links --base / 报 feed.xml missing output）。
     */
    feedUrl: SITE_URL ? base + 'feed.xml' : null,

    // 本地搜索：中文二元分词 + 文案，见 search.mts。
    // provider 必须留着 'local'——@localSearchIndex 这个虚拟模块由它决定生不生成，
    // 与用哪套主题无关。
    search: SEARCH,

    footer: {
      message: 'BossRush Mod for Escape from Duckov',
      copyright: '© 2024-2026 BossRush Mod Team',
    },
  },
})
