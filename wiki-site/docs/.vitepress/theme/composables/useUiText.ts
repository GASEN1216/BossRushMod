/**
 * useUiText.ts — 界面词表（中 / 英）。
 *
 * 为什么不放 themeConfig：VitePress 会把 themeConfig 整份序列化进**每一页**的
 * 站点数据，几十条界面文案乘以 229 页都是白搭的字节。这些字符串只有主题层用得到，
 * 打进 JS bundle 里一份就够。
 *
 * 措辞照抄目标站中文版的口径（「查看源代码」而不是「源码」、「此页面最后编辑于」
 * 而不是「最后更新」），这样版式与文案是同一套语言。
 */
import { computed } from 'vue'
import { useData } from 'vitepress'

const TEXT = {
  zh: {
    // 标签行
    page: '页面',
    talk: '讨论',
    read: '阅读',
    viewSource: '查看源代码',
    viewHistory: '查看历史',
    more: '更多',
    edit: '编辑此页',
    printPage: '打印',
    // 门户栏
    navigation: '导航',
    mainPage: '首页',
    recentChanges: '最近更改',
    randomPage: '随机页面',
    help: '帮助',
    otherLanguages: '其他语言',
    allVersions: '全部版本',
    tools: '工具',
    menu: '菜单',
    // 正文
    contents: '目录',
    toggleContents: '折叠 / 展开目录',
    toggleNavbox: '折叠 / 展开本组导航',
    navboxLabel: (name: string) => `${name}：同类条目导航`,
    latestVersion: '最新版本',
    siteTools: '站点工具',
    fromWiki: '来自 BossRush Wiki',
    categories: '分类',
    jumpToContent: '跳转到正文',
    seeAlso: '相关条目',
    statistics: '详细信息',
    allEntries: '全部条目',
    andMore: (n: number) => `还有 ${n} 条`,
    morePropsOnPage: (n: number) => `条目页还有 ${n} 项`,
    // 搜索
    searchPlaceholder: '搜索 BossRush Wiki 内容',
    search: '搜索',
    searchAll: (q: string) => `搜索包含「${q}」的页面`,
    noResults: '没有找到匹配的页面',
    loadingIndex: '正在加载搜索索引…',
    // 外观
    appearance: '外观',
    themeOverworld: '地表（深色）',
    themeSnow: '雪原（浅色）',
    // 页脚
    lastEdited: (d: string) => `此页面最后编辑于 ${d}。`,
    attribution:
      '版式参照 Official Terraria Wiki（terraria.wiki.gg）净室重写，不含任何 Re-Logic 素材。',
    about: '关于本站',
    sourceRepo: 'GitHub 仓库',
    feed: '更新订阅',
    changelog: '更新日志',
    // 灯箱 / 404
    close: '关闭',
    prevImage: '上一张',
    nextImage: '下一张',
    imageOf: (i: number, n: number) => `第 ${i} / ${n} 张`,
    backHome: '回首页',
  },
  en: {
    page: 'Page',
    talk: 'Discussion',
    read: 'Read',
    viewSource: 'View source',
    viewHistory: 'View history',
    more: 'More',
    edit: 'Edit this page',
    printPage: 'Print',
    navigation: 'Navigation',
    mainPage: 'Main page',
    recentChanges: 'Recent changes',
    randomPage: 'Random page',
    help: 'Help',
    otherLanguages: 'Other languages',
    allVersions: 'All versions',
    tools: 'Tools',
    menu: 'Menu',
    contents: 'Contents',
    toggleContents: 'Toggle contents',
    toggleNavbox: 'Toggle this navigation group',
    navboxLabel: (name: string) => `${name}: related entries`,
    latestVersion: 'Latest version',
    siteTools: 'Site tools',
    fromWiki: 'From BossRush Wiki',
    categories: 'Categories',
    jumpToContent: 'Jump to content',
    seeAlso: 'See also',
    statistics: 'Statistics',
    allEntries: 'All entries',
    andMore: (n: number) => `${n} more`,
    morePropsOnPage: (n: number) => `${n} more on the entry page`,
    searchPlaceholder: 'Search BossRush Wiki',
    search: 'Search',
    searchAll: (q: string) => `Search for pages containing "${q}"`,
    noResults: 'No matching pages',
    loadingIndex: 'Loading the search index…',
    appearance: 'Appearance',
    themeOverworld: 'Overworld (dark)',
    themeSnow: 'Snow (light)',
    lastEdited: (d: string) => `This page was last edited on ${d}.`,
    attribution:
      'Layout inspired by the Official Terraria Wiki (terraria.wiki.gg); clean-room CSS, no Re-Logic assets.',
    about: 'About',
    sourceRepo: 'GitHub repository',
    feed: 'Feed',
    changelog: 'Changelog',
    close: 'Close',
    prevImage: 'Previous',
    nextImage: 'Next',
    imageOf: (i: number, n: number) => `${i} of ${n}`,
    backHome: 'Back to the main page',
  },
} as const

export type UiText = (typeof TEXT)['zh']

export function useUiText() {
  const { lang } = useData()
  return computed<UiText>(() => (lang.value.startsWith('en') ? (TEXT.en as UiText) : TEXT.zh))
}
