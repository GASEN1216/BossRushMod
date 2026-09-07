<script setup lang="ts">
/**
 * WikiHeadSearch — 标签行右侧那个搜索框，加它底下的联想下拉。
 *
 * 换皮之前搜索只有一个入口：顶栏按钮点开的全屏弹层。目标站不是这样——
 * 它把输入框直接摆在标签行里，边打字边掉出八条带缩略图的候选，
 * 回车才去「完整结果」。这里照这个分工：
 *
 *   联想下拉（本组件）—— 打字即出，认页面级结果，选中直接跳。
 *   完整结果（WikiSearchBox）—— 回车没选中候选时打开，带上已输入的词；
 *                               它仍是那份 vitepress fork，带摘录、高亮与详细视图。
 *
 * 索引与分词一个字都没改：仍是 composables/searchIndex.ts 的模块级缓存 +
 * search.mts 里的中文二元组切词，Layout 的首屏空闲预热也照旧（它盯的就是
 * 本组件的 #searchInput）。
 *
 * 两个键盘上的坑，和 fork 里那两条同源：
 *   - 上下键要判 isComposing，否则中文输入法候选框开着时按上下会去翻搜索结果；
 *   - 回车也要判，拼音没上屏就回车不该跳页。
 */
import { computed, nextTick, onMounted, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import { defineAsyncComponent } from 'vue'
import { useData, useRouter } from 'vitepress'
import WikiIcon from './WikiIcon.vue'
import { useUiText } from '../composables/useUiText'
import { loadSearchIndex } from '../composables/searchIndex'
import { canonicalPath, locate } from '../../data/structure.mts'

const WikiSearchBox = defineAsyncComponent(() => import('./WikiSearchBox.vue'))

const ui = useUiText()
const router = useRouter()
const { localeIndex, theme, site, lang } = useData()

const input = shallowRef<HTMLInputElement>()
const root = shallowRef<HTMLElement>()
const query = ref('')
const open = ref(false)
const active = ref(-1)
const loading = ref(false)
const showOverlay = ref(false)
const overlayQuery = ref('')

interface Suggestion {
  id: string
  title: string
  description: string
  icon?: string
  tier?: number
}
const results = ref<Suggestion[]>([])

const index = shallowRef<any>(null)
let loaderPromise: Promise<void> | null = null

/** 索引按需拉：读者不搜索就不该替他下载这 250 KB（预热逻辑在 Layout 里）。 */
function ensureIndex() {
  if (index.value || loaderPromise) return loaderPromise ?? Promise.resolve()
  loading.value = true
  loaderPromise = import('@localSearchIndex')
    .then((m: any) => loadSearchIndex(localeIndex.value, m.default ?? {}, theme.value))
    .then((loaded) => {
      index.value = loaded
    })
    .catch(() => {
      index.value = null
    })
    .finally(() => {
      loading.value = false
    })
  return loaderPromise
}

const MAX = 8

function toSuggestion(hit: any): Suggestion {
  const pagePath = String(hit.id).split('#')[0]
  const canonical = canonicalPath(pagePath.replace(/\.html$/, ''), site.value.base)
  const located = locate(canonical)
  const isEn = lang.value.startsWith('en')
  const crumbs: string[] = Array.isArray(hit.titles) ? hit.titles.filter(Boolean) : []
  return {
    id: hit.id,
    title: hit.title || crumbs[0] || canonical,
    description: located
      ? (isEn ? located.category.en : located.category.zh)
      : crumbs.join(' › '),
    icon: located?.entry.icon,
    tier: located ? undefined : undefined,
  }
}

function runSearch() {
  const q = query.value.trim()
  if (!q || !index.value) {
    results.value = []
    return
  }
  // 先 AND（二元组下约等于短语匹配），不足再用 OR 补齐——与弹层里同一套口径
  const strict = index.value.search(q, { combineWith: 'AND' })
  let hits = strict
  if (strict.length < MAX) {
    const seen = new Set(strict.map((r: any) => r.id))
    hits = strict.concat(index.value.search(q).filter((r: any) => !seen.has(r.id)))
  }

  const seenPages = new Set<string>()
  const out: Suggestion[] = []
  for (const hit of hits) {
    const page = String(hit.id).split('#')[0]
    if (seenPages.has(page)) continue
    seenPages.add(page)
    out.push(toSuggestion(hit))
    if (out.length >= MAX) break
  }
  results.value = out
  active.value = -1
}

let timer: ReturnType<typeof setTimeout> | null = null
watch(query, () => {
  open.value = true
  if (timer) clearTimeout(timer)
  timer = setTimeout(() => {
    ensureIndex()?.then(runSearch)
  }, 120)
})

const hasPanel = computed(() => open.value && query.value.trim().length > 0)

function go(item: Suggestion) {
  open.value = false
  router.go(item.id)
}

function submit() {
  if (active.value >= 0 && results.value[active.value]) {
    go(results.value[active.value])
    return
  }
  const q = query.value.trim()
  if (!q) return
  overlayQuery.value = q
  showOverlay.value = true
  open.value = false
}

function move(delta: number, event: KeyboardEvent) {
  if (event.isComposing) return
  if (!hasPanel.value) return
  event.preventDefault()
  const total = results.value.length
  if (!total) return
  active.value = (active.value + delta + total + 1) % (total + 1) - 1
  if (active.value < -1) active.value = total - 1
}

function onEnter(event: KeyboardEvent) {
  if (event.isComposing) return
  event.preventDefault()
  submit()
}

function onGlobalKey(event: KeyboardEvent) {
  if ((event.key === 'k' || event.key === 'K') && (event.metaKey || event.ctrlKey)) {
    event.preventDefault()
    overlayQuery.value = query.value.trim()
    showOverlay.value = true
    return
  }
  if (event.key === '/' && !isEditable(event.target)) {
    event.preventDefault()
    input.value?.focus()
  }
}

function isEditable(target: EventTarget | null) {
  const el = target as HTMLElement | null
  if (!el) return false
  const tag = el.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable
}

function onDocPointer(event: Event) {
  if (!root.value?.contains(event.target as Node)) open.value = false
}

onMounted(() => {
  window.addEventListener('keydown', onGlobalKey)
  document.addEventListener('pointerdown', onDocPointer)
})
onBeforeUnmount(() => {
  window.removeEventListener('keydown', onGlobalKey)
  document.removeEventListener('pointerdown', onDocPointer)
  if (timer) clearTimeout(timer)
})

function closeOverlay() {
  showOverlay.value = false
  nextTick(() => input.value?.blur())
}
</script>

<template>
  <div ref="root" class="wiki-headsearch">
    <h3>{{ ui.search }}</h3>
    <form id="searchform" role="search" @submit.prevent="submit">
      <div id="simpleSearch">
        <input
          id="searchInput"
          ref="input"
          v-model="query"
          type="search"
          name="search"
          accesskey="f"
          autocomplete="off"
          spellcheck="false"
          :placeholder="ui.searchPlaceholder"
          :aria-label="ui.searchPlaceholder"
          @focus="ensureIndex(); open = true"
          @keydown.down="move(1, $event)"
          @keydown.up="move(-1, $event)"
          @keydown.enter="onEnter"
          @keydown.esc="open = false"
        />
        <button id="searchButton" type="submit" :title="ui.search">{{ ui.search }}</button>

        <div v-if="hasPanel" class="suggestions">
          <div v-if="results.length" class="suggestions-results">
            <a
              v-for="(item, i) in results"
              :key="item.id"
              class="suggestions-result"
              :class="{ 'is-current': i === active }"
              :href="item.id"
              @click.prevent="go(item)"
              @mouseenter="active = i"
            >
              <span class="suggestions-thumbnail">
                <WikiIcon :icon="item.icon" :label="item.title" :size="24" />
              </span>
              <span class="suggestions-text">
                <span class="suggestions-label">{{ item.title }}</span>
                <span v-if="item.description" class="suggestions-description">
                  {{ item.description }}
                </span>
              </span>
            </a>
          </div>
          <div v-else-if="loading" class="suggestions-empty">{{ ui.loadingIndex }}</div>
          <div v-else class="suggestions-empty">{{ ui.noResults }}</div>

          <a
            class="suggestions-special"
            :class="{ 'is-current': active === -1 && results.length > 0 }"
            href="#"
            @click.prevent="submit"
          >
            {{ ui.searchAll(query.trim()) }}
          </a>
        </div>
      </div>
    </form>

    <ClientOnly>
      <WikiSearchBox v-if="showOverlay" :query="overlayQuery" @close="closeOverlay" />
    </ClientOnly>
  </div>
</template>
