/**
 * usePageLinks.ts — 标签行那几枚 GitHub 链接。
 *
 * MediaWiki 的「查看源代码 / 查看历史 / 讨论」在本站没有对应的 wiki 后台，
 * 但它们指向的东西是有的：正文源文件在 GitHub 上，历史就是那个文件的提交记录，
 * 讨论就是 issues。四个 URL 的拼法收在这里一处，免得三个组件各写一遍前缀。
 *
 * 源文件路径不是自己算的，是 seo.mts 在构建期写进每页 frontmatter.editSource
 * （它才知道 WikiContent/ 与 hubs/ 那套映射）。没有源文件的页（首页、404）
 * frontmatter 里是 editLink === false，这时四枚链接一律返回 null，
 * 组件据此不渲染——不要给读者一个 404 的「查看源代码」。
 */
import { computed } from 'vue'
import { useData } from 'vitepress'

const REPO = 'https://github.com/GASEN1216/BossRushMod'

export function usePageLinks() {
  const { frontmatter, page } = useData()

  const source = computed<string | null>(() => {
    const value = frontmatter.value.editSource
    return typeof value === 'string' && value ? value : null
  })

  return {
    repo: REPO,
    /** 正文源文件在仓库里的相对路径，没有就是 null */
    source,
    viewSource: computed(() => (source.value ? `${REPO}/blob/main/${source.value}` : null)),
    viewHistory: computed(() => (source.value ? `${REPO}/commits/main/${source.value}` : null)),
    editPage: computed(() => (source.value ? `${REPO}/edit/main/${source.value}` : null)),
    /** 「讨论」：拿本页标题去 issues 里搜，没有 wiki 讨论页时这是最近的等价物 */
    talk: computed(
      () => `${REPO}/issues?q=${encodeURIComponent('is:issue ' + (page.value.title || ''))}`
    ),
    issues: `${REPO}/issues`,
  }
}
