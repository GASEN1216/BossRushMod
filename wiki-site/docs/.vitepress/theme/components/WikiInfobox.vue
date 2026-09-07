<script setup lang="ts">
/**
 * WikiInfobox — 条目页右上角的信息框（MediaWiki / 泰拉瑞亚 Wiki 的 .infobox）。
 *
 * 数据仍然只有 data/infobox.mts 一份，正文一个字都不用改；换皮改的是版式：
 * 从前是本站自研的竖排速查框，现在是目标站那套「标题条 + 大图 + 详细信息表 +
 * 底部 ID 条」，类名也照抄（.infobox / .section / table.stat / .tags / .section.ids）。
 *
 * 摆在哪——**正文第一个 h1 之后**，由 config.mts 的 infoboxSlotPlugin 在渲染期
 * 插进 token 流，组件在 theme/index.ts 全局注册。别搬回 Layout 的插槽里：
 * 那样 DOM 顺序会变成「框 → 标题」，宽屏上 h1 那条底线会整幅画过框身。
 * tests/WikiSiteThemeWiringGuard.py 同时守着这三处接线。
 *
 * 浮动门槛：>=641px 一律右浮。从前是 1400，因为默认主题的 272px 侧栏加 224px
 * 右目录吃掉近 500px；现在侧栏 188、目录改成正文里的方框，正文宽度回来了，
 * 门槛也就跟着目标站回到「窄屏才通栏」。
 *
 * 「物品 ID」这类内部编号从数据行里抽出来单独放底部灰条——和对比表里
 * 把它列进 HIDDEN 是同一个判断：给人查有用，摆在属性表里比没有意义。
 */
import { computed } from 'vue'
import WikiIcon from './WikiIcon.vue'
import { useWiki } from '../composables/useWiki'
import { useUiText } from '../composables/useUiText'
import { INFOBOX } from '../../data/infobox.mts'
import { canonicalPath, locate } from '../../data/structure.mts'

const props = defineProps<{ path?: string }>()

const ui = useUiText()
const {
  canonical,
  located: routeLocated,
  locale,
  href,
  entryLabel,
  entryTag,
  entryByPath,
  tierOf,
} = useWiki()

/** 规范路径：优先用注入时写死的 path，退回当前路由。 */
const key = computed(() => (props.path ? canonicalPath(props.path) : canonical.value))
const located = computed(() => (props.path ? locate(key.value) : routeLocated.value))
const box = computed(() => INFOBOX[key.value] ?? null)

/** 内部编号：单独进底部灰条，不占属性表的行 */
const ID_LABELS = new Set(['物品 ID', 'Internal ID'])

const rows = computed(() =>
  (box.value?.rows ?? [])
    .filter((r) => !ID_LABELS.has(locale.value === 'en' ? r.en : r.zh))
    .map((r) => ({
      label: locale.value === 'en' ? r.en : r.zh,
      value: locale.value === 'en' ? r.ve || r.vz : r.vz,
      tier: r.tier,
    }))
)

const ids = computed(() =>
  (box.value?.rows ?? [])
    .filter((r) => ID_LABELS.has(locale.value === 'en' ? r.en : r.zh))
    .map((r) => ({
      label: locale.value === 'en' ? r.en : r.zh,
      value: locale.value === 'en' ? r.ve || r.vz : r.vz,
    }))
)

/** links 里存的是路径，标题回 structure.mts 取，避免改名时两处漂移。 */
const links = computed(() =>
  (box.value?.links ?? [])
    .map((path) => {
      const entry = entryByPath(path)
      return entry ? { path, label: entryLabel(entry), icon: entry.icon } : null
    })
    .filter((x): x is { path: string; label: string; icon?: string } => x !== null)
)

const eyebrow = computed(() =>
  box.value ? (locale.value === 'en' ? box.value.eyebrowEn : box.value.eyebrowZh) : ''
)
const tag = computed(() => (located.value ? entryTag(located.value.entry) : ''))
</script>

<template>
  <div v-if="box && located" class="infobox">
    <div class="title">
      {{ entryLabel(located.entry) }}
      <span v-if="eyebrow" class="namesub">{{ eyebrow }}</span>
    </div>

    <div class="section images">
      <WikiIcon :icon="located.entry.icon" :label="entryLabel(located.entry)" :size="128" />
    </div>

    <div v-if="tag" class="section">
      <div class="tags">
        <span class="tag">{{ tag }}</span>
      </div>
    </div>

    <div v-if="rows.length" class="section statistics">
      <div class="title">{{ ui.statistics }}</div>
      <table class="stat">
        <tbody>
          <tr v-for="row in rows" :key="row.label">
            <th>{{ row.label }}</th>
            <td>
              <span v-if="row.tier" class="wiki-rarity" :data-tier="row.tier">{{ row.value }}</span>
              <template v-else>{{ row.value }}</template>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="links.length" class="section links">
      <div class="title">{{ ui.seeAlso }}</div>
      <ul>
        <li v-for="link in links" :key="link.path">
          <!-- data-brs-ref 让这几条也吃到悬停预览：它们和正文里的实体链接是同一类东西 -->
          <a class="i" :href="href(link.path)" :data-brs-ref="link.path">
            <WikiIcon :icon="link.icon" :label="link.label" :size="20" />
            <span class="wiki-tier" :data-tier="tierOf(link.path)">{{ link.label }}</span>
          </a>
        </li>
      </ul>
    </div>

    <div v-if="ids.length" class="section ids">
      <template v-for="id in ids" :key="id.label">{{ id.label }}：{{ id.value }}</template>
    </div>
  </div>
</template>
