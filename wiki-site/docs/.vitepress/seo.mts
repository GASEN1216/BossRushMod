/**
 * seo.mts — 逐页元数据：description、Open Graph、canonical / hreflang、编辑链接来源。
 *
 * 之前全站 230 多页共用 config.mts 里那一句站点 description，分享到 Discord / 微信
 * 时每一页的卡片都长一样；中英双语页之间也没有 hreflang，搜索引擎会把两份当重复内容。
 *
 * 这里的三件事都在构建期完成，页面 .md 一个字不改：
 *   decoratePage()  — transformPageData：从正文抽第一段做 description；
 *                     把「编辑此页」应指向的源文件路径写进 frontmatter.editSource。
 *   headFor()       — transformHead：OG / Twitter 卡片、canonical、zh-CN ⇄ en 的 hreflang。
 *   siteUrl()       — 绝对地址前缀。GitHub Pages 是默认；换域名用 SITE_URL 环境变量。
 *
 * 为什么编辑链接要绕一圈 frontmatter：themeConfig.editLink.pattern 是个在**浏览器里**
 * 执行的函数（VitePress 把它序列化进站点数据），拿不到 Node 侧的映射表；
 * 所以映射在这里算好写进每页的 frontmatter，pattern 只负责拼 URL。
 * 编辑目标是 WikiContent/ 里的源文件而不是 wiki-site/docs/ 的生成物——
 * 改生成物会被下一次 sync 抹掉（见 wiki-site/AGENTS.md §1）。
 */
import { existsSync, readFileSync } from 'fs'
import { dirname, resolve } from 'path'
import { fileURLToPath } from 'url'
import type { HeadConfig, PageData, TransformContext, TransformPageContext } from 'vitepress'
import { locate } from './data/structure.mts'
import { entryIdOfRoute, MOD_ROOT } from '../../scripts/entry-map.mjs'

const __dirname = dirname(fileURLToPath(import.meta.url))

/** 站点绝对地址（含 base，以 / 结尾）。取不到域名时返回 null，绝对地址类标签随之省略。 */
export function siteUrl(base: string): string | null {
  const env = process.env.SITE_URL?.trim()
  if (env) return env.replace(/\/*$/, '/')
  if (process.env.DEPLOY_TARGET === 'cloudflare') return null
  return 'https://gasen1216.github.io' + base
}

export interface PageRoute {
  locale: 'zh' | 'en'
  /** 去掉语言前缀的源文件相对路径：'bosses/dragon-king.md' */
  file: string
  /** 本页站内 URL（不含 base，cleanUrls 形态，无前导 /）：'en/bosses/dragon-king'、'bosses/'、'' */
  url: string
  /** 另一语言版本的 URL，同上形态 */
  alternate: string
  /** structure.mts 的规范路径：'/bosses/dragon-king'、'/bosses/'、'/' */
  canonical: string
}

export function pageRoute(relativePath: string): PageRoute {
  const isEn = relativePath.startsWith('en/')
  const file = isEn ? relativePath.slice(3) : relativePath
  const clean = file.replace(/\.md$/, '').replace(/(^|\/)index$/, '$1')
  return {
    locale: isEn ? 'en' : 'zh',
    file,
    url: isEn ? 'en/' + clean : clean,
    alternate: isEn ? clean : 'en/' + clean,
    canonical: '/' + clean,
  }
}

/**
 * 正文第一段 → description。跳过标题、配图块、提示框围栏、表格；
 * 第一段若是列表项就取第一条。去掉 markdown 记号后截到 max 字。
 */
export function extractDescription(markdown: string, max = 150): string {
  const body = markdown.replace(/^---[\s\S]*?\n---\n?/, '')
  let seenTitle = false
  let inHtmlBlock = false
  let picked = ''
  for (const raw of body.split('\n')) {
    const line = raw.trim()
    if (!line) continue
    // 第一个标题就是页面标题：docs/ 里是 #，WikiContent/ 源文件里是 ##（sync 才提升层级），两边都要认
    if (/^#{1,6}\s/.test(line)) {
      seenTitle = true
      continue
    }
    if (!seenTitle) continue
    // sync 注入的配图块是 <div class="brs-…"> … </div>，中间的图与「*说明文字*」都不是正文，整块跳过
    if (/^<div\b/.test(line)) {
      inHtmlBlock = true
      continue
    }
    if (inHtmlBlock) {
      if (/^<\/div>/.test(line)) inHtmlBlock = false
      continue
    }
    if (/^(<|:::|!\[|\||```)/.test(line)) continue
    const candidate = line.replace(/^[-*]\s+/, '').replace(/^\d+\.\s+/, '')
    // 版本页开头是「发布日期 - 2026-09-03」，一个日期当不了摘要，跳过往下找
    if (/^\d{4}-\d{2}-\d{2}$/.test(candidate)) continue
    picked = candidate
    break
  }
  if (!picked) return ''
  // 只去 markdown 的强调 / 代码 / 删除线记号；单个 ~ 与 _ 是正文的一部分（「3~5 米」「500_001」），不能碰
  const text = picked
    .replace(/!\[[^\]]*\]\([^)]*\)/g, '')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/<[^>]+>/g, '')
    .replace(/\*{1,3}|~~|`/g, '')
    .replace(/\s+/g, ' ')
    .trim()
  return text.length > max ? text.slice(0, max - 1).trimEnd() + '…' : text
}

/** 「编辑此页」应指向仓库里的哪个文件（相对仓库根，posix 分隔符）；没有对应源文件返回 null。 */
export function editSourceFor(route: PageRoute): string | null {
  const entryId = entryIdOfRoute(route.file)
  if (entryId) {
    // WikiContent 里 boss/equipment/item/mode/npc/tips 六类放子目录，其余在语言根目录
    const prefix = entryId.split('__')[0]
    const candidates = [
      `WikiContent/${route.locale}/${prefix}/${entryId}.md`,
      `WikiContent/${route.locale}/${entryId}.md`,
    ]
    for (const rel of candidates) if (existsSync(resolve(MOD_ROOT, rel))) return rel
    return null
  }
  const hub = route.file.match(/^([a-z-]+)\/index\.md$/)
  if (hub) {
    const rel = `wiki-site/hubs/${hub[1]}.${route.locale}.md`
    if (existsSync(resolve(MOD_ROOT, rel))) return rel
  }
  return null
}

/** transformPageData：就地补 description 与 editSource。 */
export function decoratePage(pageData: PageData, ctx: TransformPageContext): void {
  const route = pageRoute(pageData.relativePath)
  const fm = pageData.frontmatter

  if (!pageData.description) {
    const file = resolve(ctx.siteConfig.srcDir, pageData.relativePath)
    if (existsSync(file)) {
      const description = extractDescription(readFileSync(file, 'utf-8'))
      if (description) pageData.description = description
    }
  }

  const source = editSourceFor(route)
  if (source) fm.editSource = source
  else fm.editLink = false
}

// ── OG 图：条目图标（有就用，没有就不出这一行）──────────
let iconSrcByKey: Map<string, string> | null = null

function iconFor(canonical: string): string | null {
  if (!iconSrcByKey) {
    iconSrcByKey = new Map()
    try {
      const raw = readFileSync(resolve(__dirname, '..', '..', 'scripts', 'image-manifest.json'), 'utf-8')
      const manifest = JSON.parse(raw) as Record<string, { key: string; src: string }[]>
      for (const group of Object.values(manifest)) for (const item of group ?? []) iconSrcByKey.set(item.key, item.src)
    } catch {
      // 清单缺失只影响 og:image，不阻断构建
    }
  }
  const hit = locate(canonical)
  const key = hit?.entry.icon
  return key ? iconSrcByKey.get(key) ?? null : null
}

/** transformHead：Open Graph、Twitter 卡片、canonical、hreflang。 */
export function headFor(ctx: TransformContext): HeadConfig[] {
  const route = pageRoute(ctx.pageData.relativePath)
  const url = siteUrl(ctx.siteConfig.site.base)
  const locale = route.locale === 'en' ? 'en_US' : 'zh_CN'

  const head: HeadConfig[] = [
    ['meta', { property: 'og:type', content: route.canonical === '/' ? 'website' : 'article' }],
    ['meta', { property: 'og:site_name', content: ctx.siteData.title }],
    ['meta', { property: 'og:title', content: ctx.title }],
    ['meta', { property: 'og:description', content: ctx.description }],
    ['meta', { property: 'og:locale', content: locale }],
    ['meta', { property: 'og:locale:alternate', content: locale === 'en_US' ? 'zh_CN' : 'en_US' }],
    ['meta', { name: 'twitter:card', content: 'summary' }],
  ]
  // 404 页没有真实地址：不给它 canonical / hreflang，否则等于告诉搜索引擎去收录一个不存在的页
  if (!url || route.file === '404.md') return head

  const abs = (path: string) => url + path.replace(/^\//, '')
  const self = abs(route.url)
  const other = abs(route.alternate)
  const zh = route.locale === 'en' ? other : self
  head.push(
    ['link', { rel: 'canonical', href: self }],
    ['link', { rel: 'alternate', hreflang: route.locale === 'en' ? 'en' : 'zh-CN', href: self }],
    ['link', { rel: 'alternate', hreflang: route.locale === 'en' ? 'zh-CN' : 'en', href: other }],
    ['link', { rel: 'alternate', hreflang: 'x-default', href: zh }],
    ['meta', { property: 'og:url', content: self }]
  )
  const icon = iconFor(route.canonical)
  if (icon) head.push(['meta', { property: 'og:image', content: abs(icon) }])
  return head
}
