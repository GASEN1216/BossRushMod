import { readFileSync } from 'fs'
import { dirname, resolve } from 'path'
import { fileURLToPath } from 'url'
import { defineConfig, type DefaultTheme, type HeadConfig } from 'vitepress'
import { CATEGORIES, CHANGELOG_CATEGORY, localizePath, type Locale } from './data/structure.mts'
import { INFOBOX } from './data/infobox.mts'
import { getRoute, readCatalog } from '../../scripts/entry-map.mjs'
import { SEARCH } from './search.mts'
import { decoratePage, headFor, siteUrl } from './seo.mts'
import { writeChangelogFeed } from './feed.mts'

const __dirname = dirname(fileURLToPath(import.meta.url))

// ── 更新日志：条目多且按版本号排序，仍从 catalog.tsv 生成 ─────────
// 其余导航一律来自 data/structure.mts（唯一事实源），不在本文件里手写。

function getChangelogLink(entryId: string, prefix: string) {
  if (entryId === 'changelog__highlights') return `${prefix}/changelog/`
  if (entryId === 'changelog__legacy_archive') return `${prefix}/changelog/legacy-archive`

  const versionMatch = entryId.match(/^changelog__v(\d+)_(\d+)_(\d+)$/)
  if (!versionMatch) return null

  return `${prefix}/changelog/v${versionMatch[1]}.${versionMatch[2]}.${versionMatch[3]}`
}

function getChangelogItems(locale: Locale) {
  const prefix = locale === 'en' ? '/en' : ''

  return readCatalog()
    .filter((row) => row.categoryId === 'changelog')
    .map((row) => ({
      entryId: row.entryId,
      text: locale === 'en' ? row.titleEn : row.titleZh,
      order: row.order,
    }))
    .sort((a, b) => a.order - b.order)
    .map((entry) => {
      const link = getChangelogLink(entry.entryId, prefix)
      if (!link) return null
      return { text: entry.text, link }
    })
    .filter((entry): entry is { text: string; link: string } => entry !== null)
}

// ── 侧边栏 / 顶栏：由 structure.mts 生成 ─────────────────────────
//
// 全部分组默认折叠。VitePress 会把**包含当前页**的那一组自动展开
// （useSidebarControl 里 hasActiveLink 会把 collapsed 打回 false），
// 所以读者看到的永远是「十来个类目 + 展开的这一类」，
// 而不是从前那种一屏装不下的 80 行长列表。

function sidebarFor(locale: Locale): DefaultTheme.SidebarItem[] {
  const groups: DefaultTheme.SidebarItem[] = CATEGORIES.map((category) => ({
    text: locale === 'en' ? category.en : category.zh,
    collapsed: true,
    items: category.entries.map((entry) => ({
      text: locale === 'en' ? entry.en : entry.zh,
      link: localizePath(entry.path, locale),
    })),
  }))

  groups.push({
    text: locale === 'en' ? CHANGELOG_CATEGORY.en : CHANGELOG_CATEGORY.zh,
    collapsed: true,
    items: getChangelogItems(locale),
  })

  return groups
}

/**
 * 顶栏：五个高频入口 + 一个「更多」把剩下的类目收进去。
 *
 * 从前顶栏只有 6 个类目、侧栏有 11 个，物品 / NPC / 系统 / 成就 / 地图 / 彩蛋
 * 在顶栏里根本不存在——窄屏侧栏收起来之后就没有入口了。现在顶栏覆盖全部类目。
 */
const NAV_PRIMARY = ['getting-started', 'game-modes', 'bosses', 'equipment', 'guides']
const NAV_MORE = ['items', 'npcs', 'maps', 'systems', 'achievements', 'easter-eggs']

function navFor(locale: Locale): DefaultTheme.NavItem[] {
  const byId = (id: string) => CATEGORIES.find((c) => c.id === id)!
  const label = (id: string) => {
    const c = byId(id)
    return locale === 'en' ? c.en : c.zh
  }

  const items: DefaultTheme.NavItem[] = NAV_PRIMARY.map((id) => ({
    text: label(id),
    link: localizePath(byId(id).path, locale),
    activeMatch: `^${localizePath('/' + byId(id).path.split('/')[1], locale)}/`,
  }))

  items.push({
    text: locale === 'en' ? 'More' : '更多',
    items: NAV_MORE.map((id) => ({
      text: label(id),
      link: localizePath(byId(id).path, locale),
    })),
  })

  items.push({
    text: locale === 'en' ? CHANGELOG_CATEGORY.en : CHANGELOG_CATEGORY.zh,
    link: localizePath(CHANGELOG_CATEGORY.path, locale),
  })

  return items
}

// ── 导出配置 ──────────────────────────────────────────────
const base = process.env.DEPLOY_TARGET === 'cloudflare' ? '/' : '/BossRushMod/'

// 站点绝对地址（含 base）：GitHub Pages 默认；换域名用 SITE_URL 环境变量（见 seo.mts）。
// 取不到时 sitemap / canonical / hreflang / RSS 这些必须是绝对地址的东西一并省略。
const SITE_URL = siteUrl(base)

const FEED_HEAD: HeadConfig[] = SITE_URL
  ? [['link', { rel: 'alternate', type: 'application/rss+xml', title: 'BossRush Wiki · 更新日志', href: `${SITE_URL}feed.xml` }]]
  : []

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
        ['class', 'brs-eref'],
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

export default defineConfig({
  title: 'BossRush Wiki',
  description: 'Escape from Duckov — BossRush Mod 百科',
  base,
  cleanUrls: true,

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
    config(md) {
      md.renderer.rules.table_open = () => '<div class="brs-table-scroll"><table>'
      md.renderer.rules.table_close = () => '</table></div>'
      md.use(entityLinkPlugin)
    },
  },

  head: [
    ['link', { rel: 'icon', href: `${base}images/favicon.ico` }],
    // 移动端浏览器地址栏染成纸面色，与顶栏连成一片；深色偏好下用深色地（值与 style.css §1 的 --brs-ground 一致）
    ['meta', { name: 'theme-color', content: '#eae6de', media: '(prefers-color-scheme: light)' }],
    ['meta', { name: 'theme-color', content: '#141110', media: '(prefers-color-scheme: dark)' }],
    ...FEED_HEAD,
    // 档案版式字体：衬线标题 + 正文黑体 + 等宽微标签，与玩法档案报告同源。
    // 用 media=print + onload 切换，避免 fonts.googleapis.com 不可达时阻塞首屏；
    // 取不到时按 style.css 里的本地字体栈降级（苹方 / 微软雅黑 / 宋体）。
    ['link', { rel: 'preconnect', href: 'https://fonts.googleapis.com' }],
    ['link', { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: '' }],
    [
      'link',
      {
        rel: 'stylesheet',
        href: 'https://fonts.googleapis.com/css2?family=Noto+Serif+SC:wght@500;700;900&family=Noto+Sans+SC:wght@300;400;500;700&family=IBM+Plex+Mono:wght@400;500;600&display=swap',
        media: 'print',
        onload: "this.media='all'",
      },
    ],
  ],

  locales: {
    root: {
      label: '中文',
      lang: 'zh-CN',
      themeConfig: {
        nav: navFor('zh'),
        sidebar: sidebarFor('zh'),
        outline: { level: [2, 3], label: '本页目录' },
        docFooter: { prev: '上一篇', next: '下一篇' },
        lastUpdated: { text: '最后更新' },
        editLink: { pattern: EDIT_LINK_PATTERN, text: '在 GitHub 上编辑此页' },
        returnToTopLabel: '返回顶部',
        sidebarMenuLabel: '菜单',
        darkModeSwitchLabel: '深色模式',
        langMenuLabel: '切换语言',
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
        nav: navFor('en'),
        sidebar: sidebarFor('en'),
        outline: { level: [2, 3], label: 'On this page' },
        lastUpdated: { text: 'Last updated' },
        editLink: { pattern: EDIT_LINK_PATTERN, text: 'Edit this page on GitHub' },
      },
    },
  },

  themeConfig: {
    // 本地搜索：中文二元分词 + 文案，见 search.mts
    search: SEARCH,

    socialLinks: [{ icon: 'github', link: 'https://github.com/GASEN1216/BossRushMod' }],

    footer: {
      message: 'BossRush Mod for Escape from Duckov',
      copyright: '© 2024-2026 BossRush Mod Team',
    },
  },
})
