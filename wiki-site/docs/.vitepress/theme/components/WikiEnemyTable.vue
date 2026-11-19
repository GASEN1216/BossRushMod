<script setup lang="ts">
/**
 * WikiEnemyTable — 敌人的生命、七种元素承伤倍率与免疫异常。
 *
 * 玩家反馈「敌怪的抗性没标出来」之后加的。数据只读 data/enemy-stats.json，
 * 那份文件由 tools/build_wiki_enemy_stats.py 从代码常量与官方 preset 快照算出来，
 * 这里一个数字都不写；改了 Boss 血量重跑脚本，表自己就对了。
 *
 * 放在哪一页的哪一节由 data/enemy-tables.mts 决定，config.mts 的 enemyStatsSlotPlugin
 * 在渲染期把 `<WikiEnemyTable group="…" />` 插进去（sync 产物一个字节不变）。
 *
 * 倍率的读法与游戏里「承伤倍率」一致：伤害 = 基础伤害 × 倍率。
 * 低于 1 的上绿色（抗），高于 1 的上红色（怕），0 与「打掉又补回」的写成「免疫」。
 * 色值走 --theme-text-color-green / -red，两套皮肤各有一组，对比度已按 4.5 核过。
 */
import { computed } from 'vue'
import { withBase } from 'vitepress'
import data from '../../data/enemy-stats.json'
import WikiIcon from './WikiIcon.vue'
import { useWiki } from '../composables/useWiki'
import { localizePath } from '../../data/structure.mts'
import { enemyTableAnchor, slugify } from '../../data/slug.mts'

type Factor = number | [number, number]
interface Text {
  zh: string
  en: string
}
interface Enemy {
  id: string
  group: string
  zh: string
  en: string
  page: string
  icon?: string
  role?: Text
  health: number | { min: number; max: number } | Text
  healthNote?: Text
  elements: Record<string, Factor>
  elementNotes?: Record<string, Text>
  status: string[]
  statusAll?: boolean
  note?: Text
  anchor?: Text
  members?: Text[]
}

const props = defineProps<{ group: string; ids?: string }>()
const { locale, canonical } = useWiki()
const en = computed(() => locale.value === 'en')
const tx = (t?: Text) => (t ? (en.value ? t.en : t.zh) : '')

const ENEMIES = new Map<string, Enemy>((data.enemies as unknown as Enemy[]).map((e) => [e.id, e]))
const STATUS = new Map(data.statuses.map((s) => [s.key, s]))

const group = computed(() => data.groups.find((g) => g.id === props.group))
const rows = computed<Enemy[]>(() => {
  const want = props.ids ? props.ids.split(',') : null
  return (group.value?.enemies ?? [])
    .filter((id) => !want || want.includes(id))
    .map((id) => ENEMIES.get(id)!)
    .filter(Boolean)
})
const pool = computed(() => props.group === 'sky-island-pool')

const norm = (p: string) => (p.length > 1 ? p.replace(/\/$/, '') : p)

/** 不在本页的敌人才给链接（征程页上的冠军之影指回 Boss 页没有意义，Boss 总览上的反过来就有）。 */
function linkOf(e: Enemy): string | null {
  if (norm(e.page) === norm(canonical.value)) return null
  const anchor = e.anchor ? '#' + slugify(tx(e.anchor)) : ''
  return withBase(localizePath(e.page, locale.value)) + anchor
}

function names(e: Enemy): string[] {
  return e.members ? e.members.map((m) => tx(m)) : [en.value ? e.en : e.zh]
}

function fmt(n: number): string {
  return Number.isInteger(n) ? String(n) : String(Math.round(n * 100) / 100)
}

function health(e: Enemy): string {
  const h = e.health as any
  if (typeof h === 'number') return fmt(h)
  if (h && typeof h.min === 'number') return `${fmt(h.min)}–${fmt(h.max)}`
  return tx(h)
}

interface Cell {
  text: string
  cls: string
  title?: string
}
function cell(e: Enemy, key: string): Cell {
  const note = e.elementNotes?.[key]
  const immune = en.value ? 'immune' : '免疫'
  if (note) return { text: immune, cls: 'is-immune', title: tx(note) }
  const v = e.elements[key]
  if (Array.isArray(v)) {
    const [lo, hi] = v
    return { text: `×${fmt(lo)}–${fmt(hi)}`, cls: lo < 1 ? 'is-resist' : 'is-weak' }
  }
  if (v === 0) return { text: immune, cls: 'is-immune', title: '×0' }
  return { text: `×${fmt(v)}`, cls: v < 1 ? 'is-resist' : v > 1 ? 'is-weak' : 'is-neutral' }
}

function status(e: Enemy): string {
  if (e.statusAll) return en.value ? 'all' : '全部'
  if (!e.status.length) return '—'
  return e.status.map((k) => (en.value ? STATUS.get(k)?.en : STATUS.get(k)?.zh) ?? k).join(en.value ? ', ' : '、')
}

function notes(e: Enemy): string[] {
  const out: string[] = []
  if (e.note) out.push(tx(e.note))
  for (const [k, t] of Object.entries(e.elementNotes ?? {})) {
    const el = data.elements.find((x) => x.key === k)
    out.push((en.value ? el?.en + ': ' : el?.zh + '：') + tx(t))
  }
  return out
}

const ui = computed(() =>
  en.value
    ? {
        enemy: 'Enemy',
        health: 'Health',
        status: 'Status immunities',
        notes: 'Notes',
        legend:
          'Numbers are damage-taken multipliers, the same stat the game calls “DMG Taken Multiplier”: ×1 is normal, below 1 it shrugs off part of that damage, above 1 it takes extra, and immune means none gets through. Health is the base value; the Boss Stat Multiplier and Boss Health Percent settings only scale bosses on top of it.',
        source: (d: string) => `From the mod’s code plus the official wiki’s creature data (snapshot ${d}).`,
        expand: (n: number) => `Show the ${n} vanilla bosses that can fill these slots`,
      }
    : {
        enemy: '敌人',
        health: '生命',
        status: '免疫异常',
        notes: '说明',
        legend:
          '数字是承伤倍率，和游戏里「承伤倍率」是同一个东西：×1 正常，低于 1 打着吃力，高于 1 打着省劲，免疫就是这类伤害打不进去。生命是基础值，设置里的「Boss 全局属性倍率」和「Boss 生命比例」只对 Boss 再乘一次。',
        source: (d: string) => `数据来自本 Mod 代码与官方 Wiki 生物图鉴（${d} 快照）。`,
        expand: (n: number) => `展开：这些位置可能抽到的 ${n} 位官方 Boss`,
      }
)
</script>

<template>
  <div v-if="group && rows.length" :id="enemyTableAnchor(group.id)" class="brs-enemy">
    <component :is="pool ? 'details' : 'div'" class="brs-enemy__box">
      <component :is="pool ? 'summary' : 'p'" class="brs-enemy__title">
        {{ pool ? ui.expand(rows.length) : en ? group.en : group.zh }}
      </component>
      <div class="brs-table-scroll">
        <table class="terraria lined brs-enemy__table">
          <thead>
            <tr>
              <th scope="col">{{ ui.enemy }}</th>
              <th scope="col">{{ ui.health }}</th>
              <th v-for="el in data.elements" :key="el.key" scope="col" class="brs-enemy__el">
                {{ en ? el.en : el.zh }}
              </th>
              <th scope="col">{{ ui.status }}</th>
              <th v-if="!pool" scope="col">{{ ui.notes }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="e in rows" :key="e.id">
              <th scope="row" class="brs-enemy__name">
                <component
                  :is="linkOf(e) ? 'a' : 'span'"
                  :href="linkOf(e) || undefined"
                  class="brs-enemy__who"
                >
                  <WikiIcon v-if="e.icon" :icon="e.icon" :label="names(e)[0]" :size="28" />
                  <span class="brs-enemy__names">
                    <span v-for="n in names(e)" :key="n" class="brs-enemy__n">{{ n }}</span>
                  </span>
                </component>
                <span v-if="e.role" class="brs-enemy__role">{{ tx(e.role) }}</span>
              </th>
              <td class="brs-enemy__hp">
                {{ health(e) }}
                <span v-if="e.healthNote" class="brs-enemy__sub">{{ tx(e.healthNote) }}</span>
              </td>
              <td
                v-for="el in data.elements"
                :key="el.key"
                :class="['brs-enemy__f', cell(e, el.key).cls]"
                :title="cell(e, el.key).title"
              >
                {{ cell(e, el.key).text }}
              </td>
              <td class="brs-enemy__status">{{ status(e) }}</td>
              <td v-if="!pool" class="brs-enemy__notes">
                <span v-for="n in notes(e)" :key="n" class="brs-enemy__note">{{ n }}</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="brs-enemy__legend">
        <template v-if="group.note">{{ tx(group.note) }}</template>
        {{ ui.legend }}
        {{ ui.source(data.snapshot.fetchedAt) }}
      </p>
    </component>
  </div>
</template>
