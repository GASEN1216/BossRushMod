import { CATALOG_PATH, readCatalog, versionOf } from '../../../scripts/entry-map.mjs'

/**
 * changelog.data.mts — 全部版本的列表（按版本号降序）。
 * 首页「最近更新」取前 6 条；更新日志的时间线（WikiChangelogTimeline）与
 * 面包屑 / 同类导航（useWiki 的虚拟类目）取全部。
 *
 * VitePress 数据加载器：load() 在构建期跑在 Node 里，产物被静态内联进页面，
 * 所以首页组件不必在浏览器里读文件，也不用把 catalog.tsv 打进 bundle。
 *
 * 排序按语义版本号数值比较，不能按字符串——否则 v2.1.9 会排在 v2.1.37 后面。
 * catalog.tsv 的读取与版本号解析都在 scripts/entry-map.mjs，与 sync / config / feed 共用一份。
 */

export interface ChangelogRelease {
  version: string
  /** 不带语言前缀的站内路径 */
  path: string
  zh: string
  en: string
}

declare const data: ChangelogRelease[]
export { data }

export default {
  watch: [CATALOG_PATH],
  load(): ChangelogRelease[] {
    return readCatalog()
      .map((row) => ({ row, sort: versionOf(row.entryId) as number[] | null }))
      .filter((x): x is { row: typeof x.row; sort: number[] } => x.sort !== null)
      .sort((a, b) => b.sort[0] - a.sort[0] || b.sort[1] - a.sort[1] || b.sort[2] - a.sort[2])
      .map(({ row, sort }) => {
        const version = sort.join('.')
        return { version, path: `/changelog/v${version}`, zh: row.titleZh, en: row.titleEn }
      })
  },
}
