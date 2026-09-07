/**
 * useNavbox.ts — 按速查框眉标给类目里的条目分组。
 *
 * 页尾导航盒（WikiNavbox）与类目主页的对比表（WikiCompare）用的是**同一条**
 * 分组规则：眉标第一段就是组名（「近战武器 · 火焰」→「近战武器」）。
 * 两处各写一遍迟早会漂，所以提到这里，两边都从这里取。
 *
 * 没有速查框的条目归到「其它」组；一个组都没有（Boss、模式、系统这类眉标
 * 首段全相同的类目）时，调用方退回单行「全部条目」。
 */
import { INFOBOX } from '../../data/infobox.mts'
import type { WikiCategory, WikiEntry } from '../../data/structure.mts'

/** 眉标 -> 组名。对比表与导航盒共用，改这里两处一起变。 */
export function eyebrowGroupKey(eyebrow: string): string {
  return eyebrow.split(' · ')[0].trim()
}

export interface NavboxGroup {
  key: string
  entries: WikiEntry[]
}

/**
 * 把类目里的条目按眉标首段分组。
 * @param hubPath 类目主页路径：它自己不进导航盒（读者就在那一页上）
 */
export function groupByEyebrow(
  category: WikiCategory,
  locale: 'zh' | 'en',
  hubPath?: string,
  fallbackLabel = '全部条目'
): NavboxGroup[] {
  const norm = (p: string) => p.replace(/\/$/, '')
  const hub = hubPath ? norm(hubPath) : null

  const order: string[] = []
  const byKey = new Map<string, WikiEntry[]>()

  for (const entry of category.entries) {
    if (hub && norm(entry.path) === hub) continue
    const box = INFOBOX[norm(entry.path)]
    const key = box
      ? eyebrowGroupKey(locale === 'en' ? box.eyebrowEn : box.eyebrowZh)
      : fallbackLabel
    if (!byKey.has(key)) {
      byKey.set(key, [])
      order.push(key)
    }
    byKey.get(key)!.push(entry)
  }

  const groups = order.map((key) => ({ key, entries: byKey.get(key)! }))
  // 只分出一个组时不值得写组名，交给调用方按「全部条目」渲染
  if (groups.length === 1) groups[0].key = fallbackLabel
  return groups
}
