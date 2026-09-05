<script setup lang="ts">
/**
 * WikiCompare — 类目主页的「速查对比」表。
 *
 * 泰拉瑞亚 Wiki 的分类页核心不是散文，是一张带图标、可排序的属性总表：
 * 读者要比两把武器，扫一行就够。本站的速查框（WikiInfobox）已经把每个条目的
 * 硬数据结构化在 infobox.mts 里，这里只是把同一类目下所有条目的速查框
 * **横向拼成一张表**——数据仍然只有那一份，本组件零维护。
 *
 * 分组：按速查框眉标的第一段分（「近战武器 · 火焰」→「近战武器」）。
 *   装备类会自然拆成近战 / 套装 / 图腾 / 枪械四张表，各自的列才可比；
 *   模式、Boss、NPC 眉标首段相同，各自就是一张表。
 * 列：组内至少两个条目都有的标签，按首次出现的顺序排。只出现在一个条目上的
 *   属性留在它自己的条目页——否则图腾那组会拖出十几列、每列只有一格有值。
 *   组里只有一个条目时不出表：一行没什么可比的。
 * 别名：「获取」与「来源」、「基础伤害」与「伤害」在数据里是两种写法、一个意思，
 *   合并成一列。这是表现层的合并，不动 infobox.mts。
 * 排序：点表头。值开头有数字按数字排，星级按 ★ 的个数，都没有按字符串；
 *   缺值的行永远沉底。
 * 样式：表格本身在 theme/extras.css §1；带 tier 的格子复用稀有度色片 .wiki-rarity，
 *   定义在 extras.css §0（与 WikiInfobox 共用，刻意不放在任一组件的 <style> 里）。
 */
import { computed, ref } from 'vue'
import WikiIcon from './WikiIcon.vue'
import { useWiki } from '../composables/useWiki'
import { INFOBOX } from '../../data/infobox.mts'
import type { WikiCategory, WikiEntry } from '../../data/structure.mts'

const props = defineProps<{ category: WikiCategory }>()

const { locale, t, href, entryLabel } = useWiki()

interface Cell {
  text: string
  tier?: number
}
interface Row {
  entry: WikiEntry
  cells: Record<string, Cell>
}
interface Group {
  key: string
  columns: string[]
  rows: Row[]
}

const ALIAS: Record<string, string> = {
  获取: '来源',
  Availability: 'Source',
  基础伤害: '伤害',
  'Base damage': 'Damage',
}

/** 速查框里给人查的、但拿来「比」没有意义的行：内部 TypeID 留在条目页，不进对比表。 */
const HIDDEN = new Set(['物品 ID', 'Internal ID'])

const NAME_COL = '__name'

const groups = computed<Group[]>(() => {
  const en = locale.value === 'en'
  const byKey = new Map<string, Group>()

  for (const entry of props.category.entries) {
    const box = INFOBOX[entry.path.replace(/\/$/, '')]
    if (!box) continue

    const key = (en ? box.eyebrowEn : box.eyebrowZh).split(' · ')[0].trim()
    let group = byKey.get(key)
    if (!group) {
      group = { key, columns: [], rows: [] }
      byKey.set(key, group)
    }

    const cells: Record<string, Cell> = {}
    for (const row of box.rows) {
      const raw = en ? row.en : row.zh
      if (HIDDEN.has(raw)) continue
      const label = ALIAS[raw] ?? raw
      if (!(label in cells)) cells[label] = { text: en ? row.ve || row.vz : row.vz, tier: row.tier }
      if (!group.columns.includes(label)) group.columns.push(label)
    }
    group.rows.push({ entry, cells })
  }

  const out: Group[] = []
  for (const group of byKey.values()) {
    if (group.rows.length < 2) continue
    group.columns = group.columns.filter(
      (col) => group.rows.filter((row) => col in row.cells).length >= 2
    )
    if (group.columns.length) out.push(group)
  }
  return out
})

// ── 排序 ────────────────────────────────────────────────
const sortState = ref<Record<string, { col: string; dir: 1 | -1 }>>({})

function toggle(groupKey: string, col: string) {
  const current = sortState.value[groupKey]
  const dir: 1 | -1 = current && current.col === col && current.dir === 1 ? -1 : 1
  sortState.value = { ...sortState.value, [groupKey]: { col, dir } }
}

function ariaSort(groupKey: string, col: string): 'ascending' | 'descending' | 'none' {
  const s = sortState.value[groupKey]
  if (!s || s.col !== col) return 'none'
  return s.dir === 1 ? 'ascending' : 'descending'
}

/** 「6（史诗）」→ 6，「1.56 / 2.22 米」→ 1.56，「★★★☆☆」→ 3；没有数字 → null */
function numeric(text: string): number | null {
  if (text.includes('★')) return (text.match(/★/g) ?? []).length
  const m = text.match(/-?\d+(?:\.\d+)?/)
  return m ? parseFloat(m[0]) : null
}

function compareCells(a: Cell, b: Cell): number {
  const na = numeric(a.text)
  const nb = numeric(b.text)
  if (na !== null && nb !== null) return na - nb
  if (na !== null) return -1
  if (nb !== null) return 1
  return a.text.localeCompare(b.text, locale.value === 'en' ? 'en' : 'zh')
}

function sortedRows(group: Group): Row[] {
  const s = sortState.value[group.key]
  if (!s) return group.rows
  const lang = locale.value === 'en' ? 'en' : 'zh'
  const rows = group.rows.slice()
  if (s.col === NAME_COL) {
    return rows.sort((x, y) => entryLabel(x.entry).localeCompare(entryLabel(y.entry), lang) * s.dir)
  }
  return rows.sort((x, y) => {
    const a = x.cells[s.col]
    const b = y.cells[s.col]
    if (!a && !b) return 0
    if (!a) return 1
    if (!b) return -1
    return compareCells(a, b) * s.dir
  })
}
</script>

<template>
  <section v-if="groups.length" class="wiki-compare" :aria-label="t('速查对比', 'Comparison table')">
    <h2 class="wiki-compare__title">
      {{ t('速查对比', 'Compare at a glance') }}
      <span class="wiki-compare__hint">{{ t('点击表头排序', 'Click a header to sort') }}</span>
    </h2>

    <div v-for="group in groups" :key="group.key" class="wiki-compare__group">
      <h3 v-if="groups.length > 1" class="wiki-compare__subtitle">
        {{ group.key }}
        <span class="wiki-compare__count">{{ group.rows.length }}</span>
      </h3>

      <div class="wiki-compare__scroll">
        <table class="wiki-compare__table">
          <thead>
            <tr>
              <th scope="col" :aria-sort="ariaSort(group.key, NAME_COL)">
                <button type="button" @click="toggle(group.key, NAME_COL)">
                  {{ t('名称', 'Name') }}
                  <span class="wiki-compare__arrow" aria-hidden="true"></span>
                </button>
              </th>
              <th v-for="col in group.columns" :key="col" scope="col" :aria-sort="ariaSort(group.key, col)">
                <button type="button" @click="toggle(group.key, col)">
                  {{ col }}
                  <span class="wiki-compare__arrow" aria-hidden="true"></span>
                </button>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in sortedRows(group)" :key="row.entry.path">
              <th scope="row">
                <a class="wiki-compare__name" :href="href(row.entry.path)">
                  <WikiIcon :icon="row.entry.icon" :label="entryLabel(row.entry)" :size="26" />
                  <span>{{ entryLabel(row.entry) }}</span>
                </a>
              </th>
              <td v-for="col in group.columns" :key="col" :class="{ 'is-empty': !row.cells[col] }">
                <template v-if="row.cells[col]">
                  <span v-if="row.cells[col].tier" class="wiki-rarity" :data-tier="row.cells[col].tier">
                    {{ row.cells[col].text }}
                  </span>
                  <template v-else>{{ row.cells[col].text }}</template>
                </template>
                <template v-else>—</template>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </section>
</template>
