/**
 * useWiki.ts — 结构数据与运行时路由之间的胶水层。
 *
 * 三件事：
 *   1. 当前语言（中/英）——所有组件用 t() 取本地化字段，不各自判断 lang；
 *   2. 当前页在 structure.mts 里的位置（类目 / 条目 / 相邻条目）；
 *   3. 图标 key -> 实际 URL——查 image-manifest.json，查不到返回 null，
 *      由 WikiIcon.vue 退化成首字母字牌。**缺图标永远不该让页面报错**，
 *      因为图标是后补的，结构先行。
 */
import { computed } from 'vue'
import { useData, useRoute, withBase } from 'vitepress'
import manifest from '../../../../scripts/image-manifest.json'
import {
  CATEGORIES,
  CHANGELOG_CATEGORY,
  canonicalPath,
  categoryOfHub,
  locate,
  localizePath,
  type Locale,
  type Located,
  type WikiCategory,
  type WikiEntry,
} from '../../data/structure.mts'
import { data as releases } from '../../data/changelog.data.mts'

interface ManifestItem {
  key: string
  src: string
  zh: string
  en: string
}

/** key -> 条目。三个分组（codex / campaign / icons / ui）平铺成一张表，key 全局唯一。 */
const ICONS: Record<string, ManifestItem> = {}
for (const group of Object.values(manifest as Record<string, ManifestItem[]>)) {
  for (const item of group ?? []) ICONS[item.key] = item
}

/** 图标 key -> 带 base 前缀的 URL；没有这个 key 时返回 null。 */
export function iconUrl(key?: string): string | null {
  if (!key) return null
  const item = ICONS[key]
  return item ? withBase(item.src) : null
}

/**
 * 更新日志当作一个「虚拟类目」。它不进 CATEGORIES（条目太多、按版本号自动排序），
 * 但面包屑与页尾导航得认得它——否则四十多个版本页顶上没有位置提示、底下没有同类跳转。
 * 条目 = 总览 + 全部版本（来自 changelog.data.mts），只在这里拼一次。
 */
const CHANGELOG_AS_CATEGORY: WikiCategory = {
  ...CHANGELOG_CATEGORY,
  entries: [
    { path: '/changelog/', zh: '更新日志总览', en: 'Changelog Overview', icon: CHANGELOG_CATEGORY.icon },
    ...releases.map((release) => ({ path: release.path, zh: release.zh, en: release.en })),
  ],
}

function locateChangelog(canonical: string): Located | null {
  const norm = (s: string) => (s.length > 1 ? s.replace(/\/$/, '') : s)
  const index = CHANGELOG_AS_CATEGORY.entries.findIndex((entry) => norm(entry.path) === norm(canonical))
  if (index < 0) return null
  return { category: CHANGELOG_AS_CATEGORY, entry: CHANGELOG_AS_CATEGORY.entries[index], index }
}

export function useWiki() {
  const { lang, site } = useData()
  const route = useRoute()

  const locale = computed<Locale>(() => (lang.value.startsWith('en') ? 'en' : 'zh'))

  /** 取本地化字段：t(entry, 'zh'|'en' 后缀无关的一对值) */
  const t = (zh?: string, en?: string) => (locale.value === 'en' ? en || zh || '' : zh || '')

  const canonical = computed(() => canonicalPath(route.path, site.value.base))

  const located = computed(() => locate(canonical.value) ?? locateChangelog(canonical.value))

  /** 当前页是更新日志（总览或某个版本页）：页尾用版本时间线代替同类导航 */
  const isChangelog = computed(() => located.value?.category.id === 'changelog')

  /** 当前页是不是某个类目的主页 */
  const hubCategory = computed(() => categoryOfHub(canonical.value))

  /** 给站内路径补语言前缀与 base，直接可用于 <a href> */
  const href = (path: string) => withBase(localizePath(path, locale.value).replace(/^\//, ''))

  const entryLabel = (entry: WikiEntry) => (locale.value === 'en' ? entry.en : entry.zh)
  const entryBlurb = (entry: WikiEntry) =>
    locale.value === 'en' ? entry.blurbEn || entry.blurbZh || '' : entry.blurbZh || ''
  const entryTag = (entry: WikiEntry) =>
    locale.value === 'en' ? entry.tagEn || entry.tagZh || '' : entry.tagZh || ''
  const categoryLabel = (category: WikiCategory) =>
    locale.value === 'en' ? category.en : category.zh
  const categoryBlurb = (category: WikiCategory) =>
    locale.value === 'en' ? category.blurbEn : category.blurbZh

  /** 按规范路径找条目，用于 infobox 的 links —— 标题只有 structure.mts 一处。 */
  const entryByPath = (path: string): WikiEntry | null => {
    const hit = locate(canonicalPath(path))
    return hit ? hit.entry : null
  }

  return {
    locale,
    t,
    canonical,
    located,
    isChangelog,
    hubCategory,
    href,
    iconUrl,
    entryLabel,
    entryBlurb,
    entryTag,
    entryByPath,
    categoryLabel,
    categoryBlurb,
    categories: CATEGORIES,
  }
}
