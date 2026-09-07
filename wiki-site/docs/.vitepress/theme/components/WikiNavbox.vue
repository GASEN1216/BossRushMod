<script setup lang="ts">
/**
 * WikiNavbox — 页尾的导航盒（MediaWiki / 泰拉瑞亚 Wiki 的 .navbox）。
 *
 * 一个横跨正文宽度的盒子，标题是所属类目，底下按「组名：条目 • 条目 • 条目」
 * 逐行列出同类页面。相比从前那种平铺一行的同类导航，分组之后一眼能看出
 * 「我在近战武器这一档，隔壁还有套装和枪械」。
 *
 * 分组规则来自 composables/useNavbox.ts，与类目主页那张对比表**同一条**
 * （速查框眉标的第一段），改一处两边一起变。
 *
 * 更新日志页不按类目分组——四十多个版本平铺没法读，改按大版本（2.3.x）分行。
 */
import { computed, ref } from 'vue'
import { useWiki } from '../composables/useWiki'
import { useUiText } from '../composables/useUiText'
import { groupByEyebrow } from '../composables/useNavbox'
import { CHANGELOG_CATEGORY } from '../../data/structure.mts'
import { data as releases } from '../../data/changelog.data.mts'

const ui = useUiText()
const { canonical, located, isChangelog, locale, href, entryLabel, categoryLabel, tierOf } =
  useWiki()

const collapsed = ref(false)
const norm = (p: string) => (p.length > 1 ? p.replace(/\/$/, '') : p)
const isHere = (path: string) => norm(path) === norm(canonical.value)

interface Row {
  key: string
  items: { path: string; label: string; tier?: number }[]
}

const title = computed(() => {
  if (isChangelog.value) {
    return locale.value === 'en' ? CHANGELOG_CATEGORY.en : CHANGELOG_CATEGORY.zh
  }
  return located.value ? categoryLabel(located.value.category) : ''
})

const titleHref = computed(() =>
  isChangelog.value
    ? href(CHANGELOG_CATEGORY.path)
    : located.value
      ? href(located.value.category.path)
      : href('/')
)

const rows = computed<Row[]>(() => {
  // 更新日志：按 major.minor 分行，同一行内按版本号排
  if (isChangelog.value) {
    const order: string[] = []
    const bySeries = new Map<string, Row['items']>()
    for (const rel of releases) {
      const parts = rel.version.split('.')
      const series = `${parts[0]}.${parts[1]}.x`
      if (!bySeries.has(series)) {
        bySeries.set(series, [])
        order.push(series)
      }
      bySeries.get(series)!.push({ path: rel.path, label: 'v' + rel.version })
    }
    return order.map((key) => ({ key, items: bySeries.get(key)! }))
  }

  const category = located.value?.category
  if (!category) return []
  return groupByEyebrow(category, locale.value, category.path, ui.value.allEntries).map(
    (group) => ({
      key: group.key,
      items: group.entries.map((entry) => ({
        path: entry.path,
        label: entryLabel(entry),
        tier: tierOf(entry.path),
      })),
    })
  )
})
</script>

<template>
  <nav v-if="rows.length" class="navbox" :class="{ 'is-collapsed': collapsed }" :aria-label="ui.navboxLabel(title)">
    <div class="header">
      <span class="navbox-title"><a :href="titleHref">{{ title }}</a></span>
      <button
        class="navbox-toggle"
        type="button"
        :aria-expanded="!collapsed"
        :aria-label="ui.toggleNavbox"
        @click="collapsed = !collapsed"
      />
    </div>
    <div class="navbox-body">
      <div v-for="row in rows" :key="row.key" class="navbox-row">
        <div class="title">{{ row.key }}</div>
        <div class="dotlist">
          <ul>
            <li v-for="item in row.items" :key="item.path" :class="{ 'is-here': isHere(item.path) }">
              <a :href="href(item.path)" class="wiki-tier" :data-tier="item.tier">
                {{ item.label }}
              </a>
            </li>
          </ul>
        </div>
      </div>
    </div>
  </nav>
</template>
