/**
 * searchIndex.ts — 搜索索引的唯一加载点，带模块级缓存。
 *
 * 为什么要有这一层：
 *   默认弹层（VPLocalSearchBox）把 `MiniSearch.loadJSON` 写在 setup 的
 *   `computedAsync` 里，而弹层是 `v-if` 挂载的——于是**每打开一次就重新解析一遍
 *   索引**。中文那份 1.03 MB（gzip 250 KB）、两万多个词条，解析是同步的，
 *   主线程直接卡住；而且下载也要等到点开弹层才开始，中间是一片空白面板，
 *   实测从点击到出结果 3~4 秒。
 *
 *   搬到这里之后：同一份索引只解析一次，预热与弹层共用同一个 Promise，
 *   第二次打开是同步命中。
 *
 * MiniSearch 和索引 chunk 都走**动态 import**，不能写成顶部静态导入——
 * 这个模块被 Layout 间接引用，静态导入会把 minisearch 和整份索引拖进首屏包，
 * 那和预热的目的正好相反。
 *
 * ⚠ 构造参数必须和上游逐字一致（fields / storeFields / 三项 searchOptions 默认值 +
 *   用户在 search.mts 里配的 miniSearch）。少一项 `tokenize: cjkTokenize`，
 *   查询侧的切词就和建索引时对不上，中文直接搜不出东西。
 *   上游写法见 node_modules/vitepress/dist/client/theme-default/components/VPLocalSearchBox.vue。
 */
import type MiniSearchType from 'minisearch'

type AnyIndex = MiniSearchType<any>
type Loaders = Record<string, (() => Promise<{ default: string }>) | undefined>

/**
 * 缓存连 loaders 对象一起记。
 * dev 下 HMR 更新索引时 VitePress 会换掉整个 loaders 对象（见弹层里那段
 * `import.meta.hot.accept('/@localSearchIndex')`），只按 locale 做键会一直命中旧索引。
 */
let cache: { locale: string; loaders: Loaders; index: Promise<AnyIndex | null> } | null = null

/** search.mts 里配的那份 miniSearch 定制。 */
function userMiniSearch(theme: any): { options?: any; searchOptions?: any } {
  return theme?.search?.provider === 'local' ? theme.search.options?.miniSearch ?? {} : {}
}

export function loadSearchIndex(
  localeIndex: string,
  loaders: Loaders,
  theme: any
): Promise<AnyIndex | null> {
  if (cache && cache.locale === localeIndex && cache.loaders === loaders) return cache.index

  const user = userMiniSearch(theme)
  const task = Promise.all([import('minisearch'), loaders[localeIndex]?.()]).then(
    ([{ default: MiniSearch }, mod]: [any, any]) => {
      const json = mod?.default
      if (!json) return null
      return MiniSearch.loadJSON(json, {
        fields: ['title', 'titles', 'text'],
        storeFields: ['title', 'titles'],
        searchOptions: {
          fuzzy: 0.2,
          prefix: true,
          boost: { title: 4, text: 2, titles: 1 },
          ...user.searchOptions,
        },
        ...user.options,
      }) as AnyIndex
    }
  )

  cache = { locale: localeIndex, loaders, index: task }
  task.catch(() => {
    if (cache?.index === task) cache = null
  })
  return task
}

/**
 * 省流量连接上不预拉索引。
 *
 * 索引是全站最大的一块资源，而多数读者是来看某一页的、从不搜索。
 * 在 2G / 用户开了「数据节省」的情况下，替他决定下载 250 KB 不合适——
 * 那种情况下退回「点开再加载」，弹层里有明确的加载态兜着。
 */
function wantsLightPayload(): boolean {
  const conn = (navigator as any).connection
  if (!conn) return false
  return conn.saveData === true || /(^|-)2g$/.test(String(conn.effectiveType ?? ''))
}

/**
 * 空闲预热。
 *
 * 弹层组件那份**总是**预热：22 KB（gzip），点开就能立刻看见面板而不是等一秒。
 * 索引那份看连接质量，或者等读者把鼠标移到搜索框上（Layout 里挂了委托事件）
 * 时用 force 强制拉。
 *
 * 直接 import 本站的 fork，而不是绕 config.mts 里那条 alias 指的 vitepress 路径：
 * 两者解析到同一个模块 id，是同一个 chunk，但直接写不依赖 alias 生效。
 *
 * 全程 catch：预热失败只是回到「点开再加载」的老路，不该让首屏报错。
 */
export function warmupSearch(localeIndex: string, theme: any, force = false): void {
  if (typeof window === 'undefined') return

  import('../components/WikiSearchBox.vue').catch(() => {})

  if (!force && wantsLightPayload()) return

  import('@localSearchIndex')
    .then((m: any) => loadSearchIndex(localeIndex, m.default ?? {}, theme))
    .catch(() => {})
}
