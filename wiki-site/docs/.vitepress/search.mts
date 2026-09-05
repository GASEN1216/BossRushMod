/**
 * search.mts — 本地搜索：中文分词 + 文案。
 *
 * VitePress 的本地搜索基于 MiniSearch，默认按空格与标点切词。中文句子没有空格，
 * 「焚天龙皇悬浮在空中」整句会被当成**一个**词进索引，搜「龙皇」什么都搜不到——
 * 只有恰好从句首开始的前缀才命中。这在一个以中文为主的 Wiki 上等于搜索不可用。
 *
 * 这里把连续汉字切成**二元组**（bigram）：「焚天龙皇」→ 焚天 / 天龙 / 龙皇。
 * 建索引与解析查询用同一个切法，「龙皇」直接命中；单个汉字的查询靠 prefix
 * 匹配二元组的首字。拉丁字母、数字仍按原样整词处理，"Frostmourne" 照常可搜。
 *
 * ⚠ 自包含约束：themeConfig 里的函数会被 VitePress 序列化进站点数据
 *   （serializeFunctions → fn.toString()），在浏览器里用 new Function 还原。
 *   所以 cjkTokenize **不能引用本文件的任何其他变量或 import**，正则也要写在函数体内。
 *   建索引（Node，构建期）与查询（浏览器）两侧都用它，切法一致才能命中。
 */
import type { DefaultTheme } from 'vitepress'

export function cjkTokenize(text: string): string[] {
  const out: string[] = []
  const words = String(text).split(/[\n\r\s\p{Z}\p{P}]+/u)
  for (const word of words) {
    if (!word) continue
    const parts = word.match(/[㐀-䶿一-鿿豈-﫿]+|[^㐀-䶿一-鿿豈-﫿]+/gu)
    if (!parts) continue
    for (const part of parts) {
      if (/[㐀-䶿一-鿿豈-﫿]/u.test(part)) {
        if (part.length === 1) out.push(part)
        else for (let i = 0; i + 1 < part.length; i++) out.push(part.slice(i, i + 2))
      } else {
        out.push(part)
      }
    }
  }
  return out
}

export const SEARCH: DefaultTheme.Config['search'] = {
  provider: 'local',
  options: {
    // 结果里带正文摘录，而不是只有标题——中文标题往往只有三四个字，光看标题挑不出来
    detailedView: true,
    miniSearch: {
      options: { tokenize: cjkTokenize },
      searchOptions: {
        // 二元组只有两个字，fuzzy 0.2 折算为 0 次编辑，等于对中文关闭模糊匹配；
        // 拉丁词仍保留（"Frostmorne" 少个 u 也能搜到）。
        fuzzy: 0.2,
        prefix: true,
        boost: { title: 4, text: 2, titles: 1 },
      },
    },
    locales: {
      root: {
        translations: {
          button: { buttonText: '搜索', buttonAriaLabel: '搜索' },
          modal: {
            displayDetails: '显示详细列表',
            resetButtonTitle: '清除搜索',
            backButtonTitle: '关闭搜索',
            noResultsText: '没有找到结果',
            footer: { selectText: '选择', selectKeyAriaLabel: '回车', navigateText: '导航', navigateUpKeyAriaLabel: '上', navigateDownKeyAriaLabel: '下', closeText: '关闭', closeKeyAriaLabel: 'esc' },
          },
        },
      },
    },
  },
}
