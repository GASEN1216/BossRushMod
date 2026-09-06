/**
 * VitePress 的本地搜索插件用一个**虚拟模块** `@localSearchIndex` 暴露索引，
 * 它由插件的 resolveId / load 在构建期生成（见 vitepress 的 localSearchPlugin），
 * 磁盘上没有对应文件，包里也没有配套的 .d.ts。
 *
 * 站点构建不做类型检查，所以缺这份声明不会让 `npm run build` 失败；
 * 但编辑器会把 searchIndex.ts 里那句 import 标红。这里补一份最小声明。
 *
 * 形状：locale key -> 一个返回索引 JSON 字符串模块的加载函数。
 */
declare module '@localSearchIndex' {
  const index: Record<string, (() => Promise<{ default: string }>) | undefined>
  export default index
}

/** mark.js 的 vanilla 入口没有类型声明，弹层 fork 里要用它。 */
declare module 'mark.js/src/vanilla.js' {
  export default class Mark {
    constructor(element: Element | null)
    mark(keyword: string | string[], options?: Record<string, unknown>): void
    markRegExp(regexp: RegExp, options?: Record<string, unknown>): void
    unmark(options?: Record<string, unknown>): void
  }
}
