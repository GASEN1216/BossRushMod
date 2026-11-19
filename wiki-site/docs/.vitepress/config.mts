import { readFileSync } from 'fs'
import { dirname, resolve } from 'path'
import { fileURLToPath } from 'url'
import { defineConfig, type HeadConfig } from 'vitepress'
import { CATEGORIES, localizePath, type Locale } from './data/structure.mts'
import { INFOBOX } from './data/infobox.mts'
import { ENEMY_TABLES } from './data/enemy-tables.mts'
import { ENTITY_ANCHOR_PREFIX, enemyTableAnchor, slugify } from './data/slug.mts'
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
// 正式站 https://bossrushmod.pages.dev/ 在 Cloudflare Pages 上以根路径部署（构建环境设 DEPLOY_TARGET=cloudflare）；
// 默认的 /BossRushMod/ 留给 .github/workflows/deploy.yml 仍在发布的 GitHub Pages 副本与本地构建。
const base = process.env.DEPLOY_TARGET === 'cloudflare' ? '/' : '/BossRushMod/'

// 站点绝对地址：默认是正式站；换域名用 SITE_URL 环境变量（见 seo.mts 的 siteUrl）。
// sitemap / canonical / hreflang / RSS 这些必须是绝对地址的东西都用它，两种 base 的构建都指向正式站。
const SITE_URL = siteUrl(base)

const FEED_HEAD: HeadConfig[] = SITE_URL
  ? [['link', { rel: 'alternate', type: 'application/rss+xml', title: 'BossRush · 晴岚群岛 Wiki 更新日志', href: `${SITE_URL}feed.xml` }]]
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

// ── 正文里的实体名 → 小图标 + 链接 ───────────────────────
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
 * 认得的名字（索引）有四个来源，前面的优先：
 *   1. structure.mts 的条目名（模式、Boss、装备、系统……整页）；
 *   2. catalog.tsv 的标题，当别名（「百战留痕（黑市鸭王杯）」）；
 *   3. scripts/wiki-entities.json：物品、装备部件、人物，落在某页的某一节；
 *   4. data/enemy-stats.json：头目、岛主、地图敌人（与抗性表同一份数据）。
 * 3、4 两类会落到目标页里的具体位置：同名小节标题，或者以这个名字开头的加粗列表项
 * （渲染那一页时给该列表项补 id，见 LIST_IDS）。都没有时退到抗性表或指定的小节。
 *
 * 三种落点，宁可漏，不可错：
 *   ✓ 列表项开头 / 表格整格的 **加粗**（原规则，每处都换）；
 *   ✓ 正文任意位置的 **加粗**，文字与索引里的名字**完全相等**；
 *   ✓ 纯文字里**两头都是标点或句首句尾**的名字（「掉落：赤龙首、焰鳞甲、龙息」）。
 *     中文另放行几个虚词（左：打、用、把、给……；右：的、和、与、等），
 *     英文要求两头不是字母数字。后两种在同一小节里只换第一次，免得一段话里满屏链接。
 *   ✗ 标题、表头、已有链接、行内代码里一律不动；不指向自己所在的页（落点在本页某处的除外）。
 */
type EntityHit = {
  /** 不带 base；落到小节时带 #锚点 */
  href: string
  icon: string | null
  /** 目标页的规范路径（无尾斜杠），悬停预览与「不自指」判断用 */
  canonical: string
  anchor: string | null
  tier?: number
}

const DOCS_DIR = resolve(__dirname, '..')
const SCRIPTS_DIR = resolve(__dirname, '..', '..', 'scripts')

function readJson<T>(path: string, fallback: T): T {
  try {
    return JSON.parse(readFileSync(path, 'utf-8')) as T
  } catch {
    return fallback // 清单缺失时退化成「只加链接不加图标」，不阻断构建
  }
}

const ICON_SRC = new Map<string, string>()
for (const group of Object.values(
  readJson<Record<string, { key: string; src: string }[]>>(resolve(SCRIPTS_DIR, 'image-manifest.json'), {})
)) {
  for (const item of group ?? []) ICON_SRC.set(item.key, item.src)
}

interface RegistryEntity {
  kind: string
  zh?: string
  en?: string
  page: string
  icon?: string
  tier?: number
  anchorZh?: string
  anchorEn?: string
}
const REGISTRY = readJson<{ entities: RegistryEntity[] }>(resolve(SCRIPTS_DIR, 'wiki-entities.json'), {
  entities: [],
}).entities

interface EnemyRecord {
  id: string
  group: string
  zh: string
  en: string
  page: string
  icon?: string
  anchor?: { zh: string; en: string }
  members?: { zh: string; en: string }[]
}
const ENEMIES = readJson<{ enemies: EnemyRecord[] }>(resolve(__dirname, 'data', 'enemy-stats.json'), {
  enemies: [],
}).enemies

const norm = (p: string) => (p.length > 1 ? p.replace(/\/$/, '') : p)

/** 生成页（sync 产物）里能当落点的东西：各级标题，以及开头加粗的列表项。 */
interface PageMarks {
  headings: { text: string; slug: string }[]
  listHeads: string[]
}
const PAGE_MARKS = new Map<string, PageMarks | null>()

function marksFor(canonical: string, locale: Locale): PageMarks | null {
  const key = locale + ':' + canonical
  if (PAGE_MARKS.has(key)) return PAGE_MARKS.get(key)!
  const rel = canonical === '/' ? 'index' : canonical.replace(/^\//, '')
  const base = locale === 'en' ? resolve(DOCS_DIR, 'en') : DOCS_DIR
  let src: string | null = null
  for (const candidate of [resolve(base, rel + '.md'), resolve(base, rel, 'index.md')]) {
    try {
      src = readFileSync(candidate, 'utf-8')
      break
    } catch {
      // 下一个候选
    }
  }
  if (src === null) {
    PAGE_MARKS.set(key, null)
    return null
  }
  const headings: PageMarks['headings'] = []
  const listHeads: string[] = []
  const used = new Map<string, number>()
  let fence = false
  for (const line of src.split(/\r?\n/)) {
    if (/^\s*(```|~~~)/.test(line)) fence = !fence
    if (fence) continue
    const h = /^#{1,6}\s+(.+?)\s*$/.exec(line)
    if (h) {
      const text = h[1].replace(/\*\*/g, '').replace(/`/g, '')
      // 与 markdown-it-anchor 的去重一致：第二个同名标题是 slug-1
      let slug = slugify(text)
      const n = used.get(slug) ?? 0
      used.set(slug, n + 1)
      if (n > 0) slug = slug + '-' + n
      headings.push({ text, slug })
      continue
    }
    const li = /^\s*- \*\*(.+?)\*\*/.exec(line)
    if (li) listHeads.push(li[1].trim())
  }
  const marks = { headings, listHeads }
  PAGE_MARKS.set(key, marks)
  return marks
}

/** 「赤龙首（头盔）」「折翎 / 无声钟守」「浮舟（登云码头）· 渡口整备」都算「浮舟」等名字的落点。 */
function namesTheSpot(text: string, name: string): boolean {
  if (text === name) return true
  if (text.split(/\s*\/\s*/).includes(name)) return true
  return ['（', '(', ' · ', '·', ' / ', '：', ':', ' '].some((d) => text.startsWith(name + d))
}

/** locale:canonical -> 列表项加粗原文 -> 要补的 id。渲染那一页时由 entityLinkPlugin 写到 <li> 上。 */
const LIST_IDS = new Map<string, Map<string, string>>()

function resolveAnchor(
  name: string,
  canonical: string,
  locale: Locale,
  fallbackHeading?: string,
  fallbackId?: string | null
): string | null {
  const marks = marksFor(canonical, locale)
  if (!marks) return fallbackId ?? null
  // 跳过页面标题（h1）：落到页顶不需要锚点
  const heading = marks.headings.slice(1).find((h) => namesTheSpot(h.text, name))
  if (heading) return heading.slug
  const li = marks.listHeads.find((text) => namesTheSpot(text, name))
  if (li) {
    const key = locale + ':' + canonical
    let ids = LIST_IDS.get(key)
    if (!ids) LIST_IDS.set(key, (ids = new Map()))
    if (!ids.has(li)) ids.set(li, ENTITY_ANCHOR_PREFIX + slugify(name))
    return ids.get(li)!
  }
  if (fallbackHeading) {
    const fh = marks.headings.find((h) => h.text === fallbackHeading)
    if (fh) return fh.slug
  }
  return fallbackId ?? null
}

function buildEntityIndex(locale: Locale): Map<string, EntityHit> {
  const index = new Map<string, EntityHit>()
  const add = (name: string | undefined, hit: EntityHit) => {
    const n = (name ?? '').trim()
    if (n.length >= 2 && !index.has(n)) index.set(n, hit)
  }

  // 1. structure.mts：整页
  for (const category of CATEGORIES) {
    for (const entry of category.entries) {
      const canonical = norm(entry.path)
      // href / src 都**不带 base**：交给 VitePress 自己的 link_open 与 image
      // 渲染规则去补前缀。自己拼 base 会让 Vite 把 <img src> 当模块去解析，
      // 报 "Rollup failed to resolve import /BossRushMod/images/..." —— 这正是
      // sync-content.mjs 里 renderImageBlock() 那段注释警告过的同一个坑。
      add(locale === 'en' ? entry.en : entry.zh, {
        href: localizePath(entry.path, locale),
        icon: entry.icon ? ICON_SRC.get(entry.icon) ?? null : null,
        canonical,
        anchor: null,
        tier: INFOBOX[canonical]?.rows.find((r) => r.tier)?.tier,
      })
    }
  }

  // 2. catalog.tsv 的标题当别名：目录写「百战留痕（黑市鸭王杯）」而 structure.mts 写「百战留痕」。
  //    别名只补不覆盖，structure.mts 的名字优先。
  const byCanonical = new Map<string, EntityHit>()
  for (const hit of index.values()) if (!byCanonical.has(hit.canonical)) byCanonical.set(hit.canonical, hit)
  for (const row of readCatalog()) {
    const route = getRoute(row.entryId)
    if (!route) continue
    const canonical = '/' + route.replace(/\.md$/, '').replace(/\/index$/, '')
    const hit = byCanonical.get(canonical)
    if (hit) add(locale === 'en' ? row.titleEn : row.titleZh, hit)
  }

  // 3. 物品、装备部件、人物
  for (const e of REGISTRY) {
    const name = locale === 'en' ? e.en : e.zh
    if (!name) continue
    const canonical = norm(e.page)
    const anchor = resolveAnchor(name, canonical, locale, locale === 'en' ? e.anchorEn : e.anchorZh)
    add(name, {
      href: localizePath(e.page, locale) + (anchor ? '#' + anchor : ''),
      icon: e.icon ? ICON_SRC.get(e.icon) ?? null : null,
      canonical,
      anchor,
      tier: e.tier,
    })
  }

  // 4. 敌人：名字与抗性表同源；没有专门落点的退到那一页的抗性表
  for (const e of ENEMIES) {
    if (e.group === 'sky-island-pool' || e.group === 'zombie') continue
    const canonical = norm(e.page)
    const table = ENEMY_TABLES.find((p) => p.group === e.group && norm(p.page) === canonical)
    const names = e.members ?? [{ zh: e.zh, en: e.en }]
    for (const m of names) {
      const name = locale === 'en' ? m.en : m.zh
      const anchor = resolveAnchor(
        name,
        canonical,
        locale,
        e.anchor ? (locale === 'en' ? e.anchor.en : e.anchor.zh) : undefined,
        table ? enemyTableAnchor(e.group) : null
      )
      add(name, {
        href: localizePath(e.page, locale) + (anchor ? '#' + anchor : ''),
        icon: e.icon ? ICON_SRC.get(e.icon) ?? null : null,
        canonical,
        anchor,
      })
    }
  }
  return index
}

const ENTITY_INDEX = { zh: buildEntityIndex('zh'), en: buildEntityIndex('en') }

/** 纯文字匹配用的正则：长名字在前，「噬风之核」不会被「噬风」截胡。 */
function buildMatcher(index: Map<string, EntityHit>, locale: Locale): RegExp | null {
  const names = [...index.keys()].filter((n) => (locale === 'en' ? n.length >= 3 && /[A-Z]/.test(n) : true))
  if (!names.length) return null
  names.sort((a, b) => b.length - a.length)
  return new RegExp(names.map((n) => n.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|'), 'g')
}
const ENTITY_MATCHER = { zh: buildMatcher(ENTITY_INDEX.zh, 'zh'), en: buildMatcher(ENTITY_INDEX.en, 'en') }

// 名字两头允许出现的字符。中文放行几个最常见的虚词与动词，其余汉字一律当成「名字还没完」。
const EDGE_PUNCT = '\\s、，。：；！？（）()「」『』《》【】“”‘’"\'/,;:!?—–·×+=*|<>\\[\\]'
const ZH_LEFT = new RegExp('[' + EDGE_PUNCT + '和与或及打用把给带拿找去在从向跟对被是到让]')
const ZH_RIGHT = new RegExp('[' + EDGE_PUNCT + '的和与或及等]')
const WORDISH = /[A-Za-z0-9]/

function edgeOk(text: string, start: number, end: number, name: string): boolean {
  const before = start > 0 ? text[start - 1] : ''
  const after = end < text.length ? text[end] : ''
  const latinHead = WORDISH.test(name[0])
  const latinTail = WORDISH.test(name[name.length - 1])
  const leftOk = !before || (latinHead ? !WORDISH.test(before) && !/[一-龥]/.test(before) : ZH_LEFT.test(before))
  const rightOk = !after || (latinTail ? !WORDISH.test(after) : ZH_RIGHT.test(after))
  return leftOk && rightOk
}

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
    // md.parseInline() 也会跑 core 规则（VitePress 推断页面标题时就会调它），不给标题加链接
    if (state.inlineMode) return
    const { locale, canonical } = pageIdentity(state.env?.relativePath ?? '')
    const index = ENTITY_INDEX[locale]
    const matcher = ENTITY_MATCHER[locale]
    const listIds = LIST_IDS.get(locale + ':' + canonical)
    const idsDone = new Set<string>()
    const tokens = state.tokens
    let seen = new Set<string>()

    const Token = state.Token
    const iconToken = (src: string) => {
      const image = new Token('image', 'img', 0)
      image.attrs = [
        ['class', 'brs-eref__icon'],
        ['src', src],
        ['alt', ''],
      ]
      image.children = []
      image.content = ''
      return image
    }
    // 用 markdown-it 原生 token 而不是裸 HTML：这样 VitePress 的
    // link_open / image 渲染规则会照常补 base、走客户端路由、纳入死链扫描。
    const wrap = (inner: any[], hit: EntityHit) => {
      const linkOpen = new Token('link_open', 'a', 1)
      linkOpen.attrs = [
        // `i` 是 MediaWiki / 泰拉瑞亚 Wiki 里「带贴图的物品链接」那个类；
        // `brs-eref` 留着不动，悬停预览与稀有度上色都靠它认人。
        ['class', 'i brs-eref'],
        ['href', hit.href],
        // 悬停预览（WikiRefPreview.vue）靠这个属性反查速查框数据。
        // 写规范路径而不是 href：href 带了 base 与语言前缀，组件那边还要再剥一次。
        ['data-brs-ref', hit.canonical],
      ]
      // 稀有度：物品名按档上色（widgets.css §0 的 [data-tier] 规则）
      if (hit.tier) linkOpen.attrs.push(['data-tier', String(hit.tier)])
      linkOpen.meta = { brsEntity: true }
      const pieces: any[] = [linkOpen]
      if (hit.icon) pieces.push(iconToken(hit.icon))
      pieces.push(...inner, new Token('link_close', 'a', -1))
      return pieces
    }
    /** 指向本页本身（且没有页内落点）的不链；同一小节第二次出现的也不链。 */
    const takeOnce = (hit: EntityHit) => {
      if (hit.canonical === canonical && !hit.anchor) return false
      if (seen.has(hit.href)) return false
      seen.add(hit.href)
      return true
    }

    const splitText = (tok: any): any[] => {
      if (!matcher || !tok.content) return [tok]
      const text: string = tok.content
      const out: any[] = []
      let last = 0
      matcher.lastIndex = 0
      let m: RegExpExecArray | null
      while ((m = matcher.exec(text))) {
        const name = m[0]
        const start = m.index
        const end = start + name.length
        const hit = index.get(name)
        if (!hit || !edgeOk(text, start, end, name) || !takeOnce(hit)) continue
        if (start > last) {
          const before = new Token('text', '', 0)
          before.content = text.slice(last, start)
          out.push(before)
        }
        const label = new Token('text', '', 0)
        label.content = name
        out.push(...wrap([label], hit))
        last = end
      }
      if (!out.length) return [tok]
      if (last < text.length) {
        const rest = new Token('text', '', 0)
        rest.content = text.slice(last)
        out.push(rest)
      }
      return out
    }

    for (let i = 0; i < tokens.length; i++) {
      const tok = tokens[i]
      if (tok.type === 'heading_open') {
        seen = new Set() // 每个小节重新计「第一次」
        continue
      }
      if (tok.type !== 'inline' || !tok.children) continue
      const prev = tokens[i - 1]?.type
      if (prev === 'heading_open' || prev === 'th_open') continue

      const kids = tok.children
      const inListItem = prev === 'paragraph_open' && tokens[i - 2]?.type === 'list_item_open'
      const inCell = prev === 'td_open'

      // 第一种落点：列表项开头 / 表格整格的加粗——每处都换
      // 加粗前面常常挂着一个**空 text token**（markdown-it 切 inline 时留下的），先跳过。
      let s = 0
      while (s < kids.length && kids[s].type === 'text' && kids[s].content === '') s++
      if (
        (inListItem || inCell) &&
        kids[s]?.type === 'strong_open' &&
        kids[s + 1]?.type === 'text' &&
        kids[s + 2]?.type === 'strong_close'
      ) {
        const text = kids[s + 1].content.trim()
        kids[s].meta = { ...(kids[s].meta ?? {}), brsHead: true }
        // 本页是这个名字的落点：给 <li> 补 id，让别处的链接能跳到这一条
        const id = inListItem ? listIds?.get(text) : undefined
        if (id && !idsDone.has(id)) {
          tokens[i - 2].attrSet('id', id)
          idsDone.add(id)
        }
        const hit = index.get(text)
        if (hit) {
          if (hit.canonical === canonical && (!hit.anchor || hit.anchor === id)) {
            // 落点本身：只补图标，不链自己
            if (hit.icon && hit.anchor) kids.splice(s, 0, iconToken(hit.icon))
          } else {
            kids.splice(s, 3, ...wrap(kids.slice(s, s + 3), hit))
            seen.add(hit.href)
          }
        }
      }

      // 第二、三种落点：正文里的加粗与纯文字，同一小节只换第一次
      const out: any[] = []
      let inLink = 0
      for (let k = 0; k < kids.length; k++) {
        const c = kids[k]
        if (c.type === 'link_open') inLink++
        if (c.type === 'link_close') inLink--
        if (inLink > 0 || c.type === 'link_close') {
          out.push(c)
          continue
        }
        if (c.type === 'strong_open') {
          // 加粗：整段等于名字才换；加粗里的文字不做纯文字匹配（多半是强调，不是专名）
          let close = k + 1
          while (close < kids.length && kids[close].type !== 'strong_close') close++
          const inner = kids.slice(k, close + 1)
          const single = close === k + 2 && kids[k + 1].type === 'text'
          const hit = single && !c.meta?.brsHead ? index.get(kids[k + 1].content.trim()) : undefined
          if (hit && takeOnce(hit)) out.push(...wrap(inner, hit))
          else out.push(...inner)
          k = close
          continue
        }
        if (c.type === 'text') {
          out.push(...splitText(c))
          continue
        }
        out.push(c)
      }
      tok.children = out
    }
  })
}

/**
 * 抗性表：按 data/enemy-tables.mts 把 `<WikiEnemyTable />` 插进指定小节。
 *
 * 和速查框同一套办法（渲染期往 token 流里塞 html_block），sync 产物一个字节不变。
 * 按标题**文字**找小节：几张地图页的中英小节并不一一对应，序号靠不住；
 * 标题改名时这里只 warn，tests/WikiEnemyStatsGuard.py 会先红。
 */
export function enemyStatsSlotPlugin(md: any) {
  md.core.ruler.push('brs_enemy_tables', (state: any) => {
    if (state.inlineMode) return
    const { locale, canonical } = pageIdentity(state.env?.relativePath ?? '')
    const placements = ENEMY_TABLES.map((p, order) => ({ ...p, order })).filter((p) => norm(p.page) === canonical)
    if (!placements.length) return
    const tokens = state.tokens
    const inserts: { at: number; order: number; html: string }[] = []
    for (const p of placements) {
      const title = locale === 'en' ? p.en : p.zh
      const h = tokens.findIndex(
        (t: any, i: number) =>
          t.type === 'heading_open' && tokens[i + 1]?.type === 'inline' && tokens[i + 1].content.trim() === title
      )
      if (h < 0) {
        console.warn(`[enemy-tables] ${locale}${canonical} 找不到小节「${title}」，抗性表 ${p.group} 没插上`)
        continue
      }
      const level = Number(tokens[h].tag.slice(1))
      let at = tokens.length
      if (p.at === 'start') at = h + 3
      else {
        for (let j = h + 3; j < tokens.length; j++) {
          if (tokens[j].type === 'heading_open' && Number(tokens[j].tag.slice(1)) <= level) {
            at = j
            break
          }
        }
      }
      const ids = p.ids ? ` ids="${p.ids.join(',')}"` : ''
      inserts.push({ at, order: p.order, html: `<WikiEnemyTable group="${p.group}"${ids} />\n` })
    }
    // 倒序插：后面的位置先插，前面的下标不受影响；同一位置按登记顺序排
    inserts.sort((a, b) => b.at - a.at || b.order - a.order)
    for (const ins of inserts) {
      const slot = new state.Token('html_block', '', 0)
      slot.content = ins.html
      slot.block = true
      tokens.splice(ins.at, 0, slot)
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
  // 站名跟着模组名「BossRush · 晴岚群岛」走；英文站名在 locales.en 里单独给
  title: 'BossRush · 晴岚群岛 Wiki',
  description: 'Escape from Duckov — BossRush · 晴岚群岛 模组百科',
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
      md.use(enemyStatsSlotPlugin)

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
      title: 'BossRush · Qinglan Archipelago Wiki',
      description: 'Escape from Duckov — BossRush · Qinglan Archipelago mod wiki',
      themeConfig: {
        editLink: { pattern: EDIT_LINK_PATTERN, text: 'Edit this page on GitHub' },
        footer: {
          message: 'BossRush · Qinglan Archipelago for Escape from Duckov',
          copyright: '© 2024-2026 BossRush Mod Team',
        },
      },
    },
  },

  themeConfig: {
    /*
     * RSS 的地址（没有就是 null）。
     *
     * feed.mts 只在拿得到站点绝对地址时才写 dist/feed.xml。主题层几处「订阅」入口因此不能写死链接，
     * 得按这个值决定渲染不渲染；写死的下场是生不出 feed 时三处死链
     * （2026-09-07 实测：当时 `DEPLOY_TARGET=cloudflare` 拿不到绝对地址，check_wiki_links --base /
     * 报 feed.xml missing output）。2026-09-26 起 siteUrl 默认返回正式站，两种 base 都会生成 feed。
     */
    feedUrl: SITE_URL ? base + 'feed.xml' : null,

    // 本地搜索：中文二元分词 + 文案，见 search.mts。
    // provider 必须留着 'local'——@localSearchIndex 这个虚拟模块由它决定生不生成，
    // 与用哪套主题无关。
    search: SEARCH,

    footer: {
      // 英文站的页脚在 locales.en.themeConfig.footer 里覆盖（themeConfig 按语言浅合并）
      message: '《逃离鸭科夫》模组 BossRush · 晴岚群岛',
      copyright: '© 2024-2026 BossRush Mod Team',
    },
  },
})
