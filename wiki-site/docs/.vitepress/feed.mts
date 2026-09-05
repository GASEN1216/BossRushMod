/**
 * feed.mts — 更新日志的 RSS 订阅源（构建期 buildEnd 生成 dist/feed.xml）。
 *
 * Mod 玩家关心的是「又更新了什么」，而不是整站。一个 RSS 让 Steam 群、Discord 机器人、
 * 阅读器都能订阅版本页，不用每次来翻侧栏。
 *
 * 条目 = catalog.tsv 里的每个版本页；发布时间取该版本源文件最后一次提交的 git 时间
 * （CI 的 checkout 必须 fetch-depth: 0，否则所有条目同一天，见 .github/workflows/deploy.yml）；
 * 摘要取版本页正文第一段。没有绝对域名（siteUrl 返回 null）时不生成——RSS 的 link 必须是绝对地址。
 */
import { execFileSync } from 'child_process'
import { existsSync, readFileSync, writeFileSync } from 'fs'
import { resolve } from 'path'
import type { SiteConfig } from 'vitepress'
import { MOD_ROOT, readCatalog, versionOf } from '../../scripts/entry-map.mjs'
import { extractDescription, siteUrl } from './seo.mts'

function escapeXml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/** 版本页「发布日期」小节写的 YYYY-MM-DD（按北京时间零点）；没写返回空串，由 git 时间兜底。 */
function releaseDate(markdown: string): string {
  const m = markdown.match(/^#{2,4}\s*发布日期\s*\n+\s*-?\s*(\d{4}-\d{2}-\d{2})/m)
  return m ? new Date(`${m[1]}T00:00:00+08:00`).toUTCString() : ''
}

function gitDate(rel: string): string {
  try {
    const iso = execFileSync('git', ['log', '-1', '--format=%cI', '--', rel], {
      cwd: MOD_ROOT,
      encoding: 'utf-8',
      stdio: ['ignore', 'pipe', 'ignore'],
    }).trim()
    return iso ? new Date(iso).toUTCString() : ''
  } catch {
    return ''
  }
}

export function writeChangelogFeed(siteConfig: SiteConfig): void {
  const url = siteUrl(siteConfig.site.base)
  if (!url) return

  const releases = readCatalog()
    .map((row) => ({ row, version: versionOf(row.entryId) }))
    .filter((x): x is { row: ReturnType<typeof readCatalog>[number]; version: number[] } => x.version !== null)
    .sort((a, b) => b.version[0] - a.version[0] || b.version[1] - a.version[1] || b.version[2] - a.version[2])

  const items = releases.map(({ row, version }) => {
    const v = version.join('.')
    const rel = `WikiContent/zh/${row.entryId}.md`
    const file = resolve(MOD_ROOT, rel)
    const source = existsSync(file) ? readFileSync(file, 'utf-8') : ''
    const summary = source ? extractDescription(source, 300) : ''
    const date = releaseDate(source) || gitDate(rel)
    const link = `${url}changelog/v${v}`
    return [
      '    <item>',
      `      <title>BossRush Mod v${v}</title>`,
      `      <link>${link}</link>`,
      `      <guid isPermaLink="true">${link}</guid>`,
      date ? `      <pubDate>${date}</pubDate>` : '',
      `      <description>${escapeXml(summary)}</description>`,
      '    </item>',
    ]
      .filter(Boolean)
      .join('\n')
  })

  const xml = [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom">',
    '  <channel>',
    '    <title>BossRush Wiki · 更新日志</title>',
    `    <link>${url}changelog/</link>`,
    '    <description>Escape from Duckov — BossRush Mod 每个版本改了什么</description>',
    '    <language>zh-CN</language>',
    `    <atom:link href="${url}feed.xml" rel="self" type="application/rss+xml" />`,
    ...items,
    '  </channel>',
    '</rss>',
    '',
  ].join('\n')

  writeFileSync(resolve(siteConfig.outDir, 'feed.xml'), xml, 'utf-8')
  console.log(`[feed] feed.xml: ${releases.length} 个版本`)
}
