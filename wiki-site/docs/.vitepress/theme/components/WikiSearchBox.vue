<script lang="ts" setup>
/**
 * WikiSearchBox — 本地搜索弹层。
 *
 * ⚠ 这是 **vitepress@1.6.4** 自带 `VPLocalSearchBox.vue` 的 fork
 *   （node_modules/vitepress/dist/client/theme-default/components/VPLocalSearchBox.vue）。
 *   config.mts 里一条 Vite alias 把默认主题那句 `import('./VPLocalSearchBox.vue')`
 *   指到这里，顶栏按钮、Ctrl+K / `/` 快捷键、开关状态全都还是官方那份，只有弹层换人。
 *
 *   升级 VitePress 时必须把上游同名文件重新 diff 一遍，再把下面这些改动搬过来。
 *   `tests/WikiSiteThemeWiringGuard.py` 会核对这里登记的版本号与 package-lock.json
 *   里的实际版本，版本一变守卫先红，防止「升级了却忘了对照 fork」。
 *
 * UPSTREAM: vitepress@1.6.4
 *
 * 相对上游的改动（除此之外逐字不动，class 名一律保持原样，
 * 好让 theme/style.css §13 那几条不带 scoped 的规则继续生效）：
 *
 *   1. 相对导入改成包导入（`vitepress` 公开入口 + `vitepress/dist/**` 深层路径），
 *      并把 `types/local-search` 的类型标注去掉（那个路径不在包的 exports 里）。
 *   2. 索引加载交给 composables/searchIndex.ts：模块级缓存 + 首屏空闲预热。
 *      上游把 loadJSON 写在 setup 的 computedAsync 里，而弹层是 v-if 挂载的，
 *      于是**每打开一次就重新解析一遍** 1 MB 索引。
 *   3. 索引没就绪时结果区显示「正在加载搜索索引…」。上游是一片空白，
 *      读者不知道是没结果还是没加载完（实测首开要 3~4 秒）。
 *   4. ArrowUp / ArrowDown 加 IME 组合态判断。上游只有 Enter 判了 isComposing，
 *      于是拼音候选框开着时按上下键会去翻搜索结果，选不了字。
 *   5. 先按 AND 检索，命中太少时再用 OR 结果补齐（AND 的排在前面）。
 *      中文按二元组切词，AND 约等于短语匹配：搜「无间炼狱奖励」不会再把
 *      只沾了「奖励」两个字的段落顶到前面。
 *   6. 输入框占位符换成一句有内容的提示，不再复用按钮上那两个字。
 *   7. 弹层开合加 0.14s 的 CSS 动画。不用 <Transition>：它靠双 rAF 推进状态机，
 *      标签页切到后台时 rAF 停摆，离场动画走不完（同 WikiRefPreview.vue 的坑）。
 *      prefers-reduced-motion 由 style.css 的全局规则统一关掉。
 *   8. 结果条边框固定 1px（上游 2px，与 style.css 里那条 1px 打架，选中态会抖），
 *      摘要缓存 16 -> 32 篇。
 *   9. 快捷键条左侧加一个结果计数。
 */
import localSearchIndex from '@localSearchIndex'
import {
  computedAsync,
  debouncedWatch,
  onKeyStroke,
  useEventListener,
  useLocalStorage,
  useScrollLock,
  useSessionStorage
} from '@vueuse/core'
import { useFocusTrap } from '@vueuse/integrations/useFocusTrap'
import Mark from 'mark.js/src/vanilla.js'
import type { SearchResult } from 'minisearch'
import { dataSymbol, inBrowser, useData, useRouter } from 'vitepress'
import { pathToFile } from 'vitepress/dist/client/app/utils.js'
import { escapeRegExp } from 'vitepress/dist/client/shared.js'
import { LRUCache } from 'vitepress/dist/client/theme-default/support/lru.js'
import { createSearchTranslate } from 'vitepress/dist/client/theme-default/support/translation.js'
import {
  computed,
  createApp,
  markRaw,
  nextTick,
  onBeforeUnmount,
  onMounted,
  ref,
  shallowRef,
  watch,
  watchEffect,
  type Ref
} from 'vue'
import { loadSearchIndex } from '../composables/searchIndex'

const emit = defineEmits<{
  (e: 'close'): void
}>()

const el = shallowRef<HTMLElement>()
const resultsEl = shallowRef<HTMLElement>()

/* Search */

const searchIndexData = shallowRef(localSearchIndex)

// hmr
if (import.meta.hot) {
  import.meta.hot.accept('/@localSearchIndex', (m) => {
    if (m) {
      searchIndexData.value = m.default
    }
  })
}

interface Result {
  title: string
  titles: string[]
  text?: string
}

const vitePressData = useData()
const { activate } = useFocusTrap(el, {
  immediate: true,
  allowOutsideClick: true,
  clickOutsideDeactivates: true,
  escapeDeactivates: true
})
const { localeIndex, theme, lang } = vitePressData

/**
 * 索引：共享缓存 + 预热（见 composables/searchIndex.ts）。
 * 传 `searchIndexData.value` 而不是让那边自己 import，是为了让 dev 下的 HMR
 * 生效——索引更新时 VitePress 换掉的正是这个 loaders 对象，缓存拿它当键的一部分。
 */
const searchIndex = computedAsync(async () => {
  const index = await loadSearchIndex(localeIndex.value, searchIndexData.value, theme.value)
  return index ? markRaw(index) : null
}, null)

const disableQueryPersistence = computed(() => {
  return (
    theme.value.search?.provider === 'local' &&
    theme.value.search.options?.disableQueryPersistence === true
  )
})

const filterText = disableQueryPersistence.value
  ? ref('')
  : useSessionStorage('vitepress:local-search-filter', '')

const showDetailedList = useLocalStorage(
  'vitepress:local-search-detailed-list',
  theme.value.search?.provider === 'local' &&
    theme.value.search.options?.detailedView === true
)

const disableDetailedView = computed(() => {
  return (
    theme.value.search?.provider === 'local' &&
    (theme.value.search.options?.disableDetailedView === true ||
      theme.value.search.options?.detailedView === false)
  )
})

/**
 * 输入框占位符。
 *
 * 上游把按钮上那两个字（「搜索」）直接当占位符用，输入框里因此只有一个词，
 * 什么都没提示。这里给一句有内容的——文案与首页 WikiSearchBar.vue 保持一致，
 * 改一处记得改另一处（两边都只有这一句，没必要为它单起一个模块）。
 */
const placeholder = computed(() =>
  lang.value.startsWith('en')
    ? 'Search bosses, gear, modes, achievements…'
    : '搜索 Boss、装备、模式、成就……'
)

const buttonText = computed(() => {
  const options = theme.value.search?.options ?? theme.value.algolia

  return (
    options?.locales?.[localeIndex.value]?.translations?.button?.buttonText ||
    options?.translations?.button?.buttonText ||
    'Search'
  )
})

watchEffect(() => {
  if (disableDetailedView.value) {
    showDetailedList.value = false
  }
})

const results: Ref<(SearchResult & Result)[]> = shallowRef([])

const enableNoResults = ref(false)

watch(filterText, () => {
  enableNoResults.value = false
})

const mark = computedAsync(async () => {
  if (!resultsEl.value) return
  return markRaw(new Mark(resultsEl.value))
}, null)

// 摘要缓存。上游 16 篇：结果一页最多 16 条，翻一次查询就能把它冲干净，
// 回头改个字再搜同一批页面又要重新 import + mount 一遍。放宽到 32。
const cache = new LRUCache<string, Map<string, string>>(32) // 32 files

debouncedWatch(
  () => [searchIndex.value, filterText.value, showDetailedList.value] as const,
  async ([index, filterTextValue, showDetailedListValue], old, onCleanup) => {
    if (old?.[0] !== index) {
      // in case of hmr
      cache.clear()
    }

    let canceled = false
    onCleanup(() => {
      canceled = true
    })

    if (!index) return

    // Search
    //
    // 先 AND 后 OR。中文按二元组切词（search.mts 的 cjkTokenize），
    // 「无间炼狱奖励」会切成 无间/间炼/炼狱/狱奖/奖励 五个词，AND 要求同一段里
    // 全部出现——约等于短语匹配，正好把「只沾了『奖励』两个字」的段落挡在外面。
    // 但 AND 也可能过严（几个词分散在不同小节），命中少于 PAD_BELOW 条时
    // 用 OR 的结果补齐，AND 的仍排在前面，去重按 id。
    const PAD_BELOW = 5
    const strict = index.search(filterTextValue, { combineWith: 'AND' })
    let hits = strict
    if (strict.length < PAD_BELOW) {
      const seen = new Set(strict.map((r) => r.id))
      hits = strict.concat(index.search(filterTextValue).filter((r) => !seen.has(r.id)))
    }
    results.value = hits.slice(0, 16) as (SearchResult & Result)[]
    enableNoResults.value = true

    // Highlighting
    const mods = showDetailedListValue
      ? await Promise.all(results.value.map((r) => fetchExcerpt(r.id)))
      : []
    if (canceled) return
    for (const { id, mod } of mods) {
      const mapId = id.slice(0, id.indexOf('#'))
      let map = cache.get(mapId)
      if (map) continue
      map = new Map()
      cache.set(mapId, map)
      const comp = mod.default ?? mod
      if (comp?.render || comp?.setup) {
        const app = createApp(comp)
        // Silence warnings about missing components
        app.config.warnHandler = () => {}
        app.provide(dataSymbol, vitePressData)
        Object.defineProperties(app.config.globalProperties, {
          $frontmatter: {
            get() {
              return vitePressData.frontmatter.value
            }
          },
          $params: {
            get() {
              return vitePressData.page.value.params
            }
          }
        })
        const div = document.createElement('div')
        app.mount(div)
        const headings = div.querySelectorAll('h1, h2, h3, h4, h5, h6')
        headings.forEach((el) => {
          const href = el.querySelector('a')?.getAttribute('href')
          const anchor = href?.startsWith('#') && href.slice(1)
          if (!anchor) return
          let html = ''
          while ((el = el.nextElementSibling!) && !/^h[1-6]$/i.test(el.tagName))
            html += el.outerHTML
          map!.set(anchor, html)
        })
        app.unmount()
      }
      if (canceled) return
    }

    const terms = new Set<string>()

    results.value = results.value.map((r) => {
      const [id, anchor] = r.id.split('#')
      const map = cache.get(id)
      const text = map?.get(anchor) ?? ''
      for (const term in r.match) {
        terms.add(term)
      }
      return { ...r, text }
    })

    await nextTick()
    if (canceled) return

    await new Promise((r) => {
      mark.value?.unmark({
        done: () => {
          mark.value?.markRegExp(formMarkRegex(terms), { done: r })
        }
      })
    })

    const excerpts = el.value?.querySelectorAll('.result .excerpt') ?? []
    for (const excerpt of excerpts) {
      excerpt
        .querySelector('mark[data-markjs="true"]')
        ?.scrollIntoView({ block: 'center' })
    }
    // FIXME: without this whole page scrolls to the bottom
    resultsEl.value?.firstElementChild?.scrollIntoView({ block: 'start' })
  },
  { debounce: 200, immediate: true }
)

async function fetchExcerpt(id: string) {
  const file = pathToFile(id.slice(0, id.indexOf('#')))
  try {
    if (!file) throw new Error(`Cannot find file for id: ${id}`)
    return { id, mod: await import(/*@vite-ignore*/ file) }
  } catch (e) {
    console.error(e)
    return { id, mod: {} }
  }
}

/* Search input focus */

const searchInput = ref<HTMLInputElement>()
const disableReset = computed(() => {
  return filterText.value?.length <= 0
})
function focusSearchInput(select = true) {
  searchInput.value?.focus()
  select && searchInput.value?.select()
}

onMounted(() => {
  focusSearchInput()
})

function onSearchBarClick(event: PointerEvent) {
  if (event.pointerType === 'mouse') {
    focusSearchInput()
  }
}

/* Search keyboard selection */

const selectedIndex = ref(-1)
const disableMouseOver = ref(true)

watch(results, (r) => {
  selectedIndex.value = r.length ? 0 : -1
  scrollToSelectedResult()
})

function scrollToSelectedResult() {
  nextTick(() => {
    const selectedEl = document.querySelector('.result.selected')
    selectedEl?.scrollIntoView({ block: 'nearest' })
  })
}

onKeyStroke('ArrowUp', (event) => {
  // 输入法候选框开着的时候上下键是用来翻候选字的，不能被结果列表抢走。
  // 上游只在 Enter 上判了 isComposing，这两个漏了——中文用户按下拼音之后
  // 想选第二个候选字，翻的却是搜索结果。keyCode 229 是老浏览器的等价信号。
  if (event.isComposing || event.keyCode === 229) return
  event.preventDefault()
  selectedIndex.value--
  if (selectedIndex.value < 0) {
    selectedIndex.value = results.value.length - 1
  }
  disableMouseOver.value = true
  scrollToSelectedResult()
})

onKeyStroke('ArrowDown', (event) => {
  // 输入法候选框开着的时候上下键是用来翻候选字的，不能被结果列表抢走。
  // 上游只在 Enter 上判了 isComposing，这两个漏了——中文用户按下拼音之后
  // 想选第二个候选字，翻的却是搜索结果。keyCode 229 是老浏览器的等价信号。
  if (event.isComposing || event.keyCode === 229) return
  event.preventDefault()
  selectedIndex.value++
  if (selectedIndex.value >= results.value.length) {
    selectedIndex.value = 0
  }
  disableMouseOver.value = true
  scrollToSelectedResult()
})

const router = useRouter()

onKeyStroke('Enter', (e) => {
  if (e.isComposing) return

  if (e.target instanceof HTMLButtonElement && e.target.type !== 'submit')
    return

  const selectedPackage = results.value[selectedIndex.value]
  if (e.target instanceof HTMLInputElement && !selectedPackage) {
    e.preventDefault()
    return
  }

  if (selectedPackage) {
    router.go(selectedPackage.id)
    emit('close')
  }
})

onKeyStroke('Escape', () => {
  emit('close')
})

// Translations
// 上游这里标了 `{ modal: ModalTranslations }`，那个类型在 vitepress 的
// types/ 下、不在包的 exports 里，深层导入不到。标注去掉，靠推断即可。
const defaultTranslations = {
  modal: {
    displayDetails: 'Display detailed list',
    resetButtonTitle: 'Reset search',
    backButtonTitle: 'Close search',
    noResultsText: 'No results for',
    footer: {
      selectText: 'to select',
      selectKeyAriaLabel: 'enter',
      navigateText: 'to navigate',
      navigateUpKeyAriaLabel: 'up arrow',
      navigateDownKeyAriaLabel: 'down arrow',
      closeText: 'to close',
      closeKeyAriaLabel: 'escape'
    }
  }
}

const translate = createSearchTranslate(defaultTranslations)

// Back

onMounted(() => {
  // Prevents going to previous site
  window.history.pushState(null, '', null)
})

useEventListener('popstate', (event) => {
  event.preventDefault()
  emit('close')
})

/** Lock body */
const isLocked = useScrollLock(inBrowser ? document.body : null)

onMounted(() => {
  nextTick(() => {
    isLocked.value = true
    nextTick().then(() => activate())
  })
})

onBeforeUnmount(() => {
  isLocked.value = false
})

function resetSearch() {
  filterText.value = ''
  nextTick().then(() => focusSearchInput(false))
}

function formMarkRegex(terms: Set<string>) {
  return new RegExp(
    [...terms]
      .sort((a, b) => b.length - a.length)
      .map((term) => `(${escapeRegExp(term)})`)
      .join('|'),
    'gi'
  )
}

function onMouseMove(e: MouseEvent) {
  if (!disableMouseOver.value) return
  const el = (e.target as HTMLElement)?.closest<HTMLAnchorElement>('.result')
  const index = Number.parseInt(el?.dataset.index!)
  if (index >= 0 && index !== selectedIndex.value) {
    selectedIndex.value = index
  }
  disableMouseOver.value = false
}
</script>

<template>
  <Teleport to="body">
    <div
      ref="el"
      role="button"
      :aria-owns="results?.length ? 'localsearch-list' : undefined"
      aria-expanded="true"
      aria-haspopup="listbox"
      aria-labelledby="localsearch-label"
      class="VPLocalSearchBox"
    >
      <div class="backdrop" @click="$emit('close')" />

      <div class="shell">
        <form
          class="search-bar"
          @pointerup="onSearchBarClick($event)"
          @submit.prevent=""
        >
          <label
            :title="buttonText"
            id="localsearch-label"
            for="localsearch-input"
          >
            <span aria-hidden="true" class="vpi-search search-icon local-search-icon" />
          </label>
          <div class="search-actions before">
            <button
              class="back-button"
              :title="translate('modal.backButtonTitle')"
              @click="$emit('close')"
            >
              <span class="vpi-arrow-left local-search-icon" />
            </button>
          </div>
          <input
            ref="searchInput"
            v-model="filterText"
            :aria-activedescendant="selectedIndex > -1 ? ('localsearch-item-' + selectedIndex) : undefined"
            aria-autocomplete="both"
            :aria-controls="results?.length ? 'localsearch-list' : undefined"
            aria-labelledby="localsearch-label"
            autocapitalize="off"
            autocomplete="off"
            autocorrect="off"
            class="search-input"
            id="localsearch-input"
            enterkeyhint="go"
            maxlength="64"
            :placeholder="placeholder"
            spellcheck="false"
            type="search"
          />
          <div class="search-actions">
            <button
              v-if="!disableDetailedView"
              class="toggle-layout-button"
              type="button"
              :class="{ 'detailed-list': showDetailedList }"
              :title="translate('modal.displayDetails')"
              @click="
                selectedIndex > -1 && (showDetailedList = !showDetailedList)
              "
            >
              <span class="vpi-layout-list local-search-icon" />
            </button>

            <button
              class="clear-button"
              type="reset"
              :disabled="disableReset"
              :title="translate('modal.resetButtonTitle')"
              @click="resetSearch"
            >
              <span class="vpi-delete local-search-icon" />
            </button>
          </div>
        </form>

        <ul
          ref="resultsEl"
          :id="results?.length ? 'localsearch-list' : undefined"
          :role="results?.length ? 'listbox' : undefined"
          :aria-labelledby="results?.length ? 'localsearch-label' : undefined"
          class="results"
          @mousemove="onMouseMove"
        >
          <li
            v-for="(p, index) in results"
            :key="p.id"
            :id="'localsearch-item-' + index"
            :aria-selected="selectedIndex === index ? 'true' : 'false'"
            role="option"
          >
            <a
              :href="p.id"
              class="result"
              :class="{
                selected: selectedIndex === index
              }"
              :aria-label="[...p.titles, p.title].join(' > ')"
              @mouseenter="!disableMouseOver && (selectedIndex = index)"
              @focusin="selectedIndex = index"
              @click="$emit('close')"
              :data-index="index"
            >
              <div>
                <div class="titles">
                  <span class="title-icon">#</span>
                  <span
                    v-for="(t, index) in p.titles"
                    :key="index"
                    class="title"
                  >
                    <span class="text" v-html="t" />
                    <span class="vpi-chevron-right local-search-icon" />
                  </span>
                  <span class="title main">
                    <span class="text" v-html="p.title" />
                  </span>
                </div>

                <div v-if="showDetailedList" class="excerpt-wrapper">
                  <div v-if="p.text" class="excerpt" inert>
                    <div class="vp-doc" v-html="p.text" />
                  </div>
                  <div class="excerpt-gradient-bottom" />
                  <div class="excerpt-gradient-top" />
                </div>
              </div>
            </a>
          </li>
          <!-- 索引还在路上：明确说一声，别让读者对着空面板猜。
               enableNoResults 只有真搜过才为真，所以「没有找到结果」不会在这时候冒出来。 -->
          <li v-if="!searchIndex" class="no-results brs-loading">
            {{ lang.startsWith('en') ? 'Loading search index…' : '正在加载搜索索引……' }}
          </li>
          <li
            v-else-if="filterText && !results.length && enableNoResults"
            class="no-results"
          >
            {{ translate('modal.noResultsText') }} "<strong>{{ filterText }}</strong
            >"
          </li>
        </ul>

        <div class="search-keyboard-shortcuts">
          <span v-if="results.length" class="brs-count">
            {{ lang.startsWith('en') ? `${results.length} results` : `${results.length} 条结果` }}
          </span>
          <span>
            <kbd :aria-label="translate('modal.footer.navigateUpKeyAriaLabel')">
              <span class="vpi-arrow-up navigate-icon" />
            </kbd>
            <kbd :aria-label="translate('modal.footer.navigateDownKeyAriaLabel')">
              <span class="vpi-arrow-down navigate-icon" />
            </kbd>
            {{ translate('modal.footer.navigateText') }}
          </span>
          <span>
            <kbd :aria-label="translate('modal.footer.selectKeyAriaLabel')">
              <span class="vpi-corner-down-left navigate-icon" />
            </kbd>
            {{ translate('modal.footer.selectText') }}
          </span>
          <span>
            <kbd :aria-label="translate('modal.footer.closeKeyAriaLabel')">esc</kbd>
            {{ translate('modal.footer.closeText') }}
          </span>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.VPLocalSearchBox {
  position: fixed;
  z-index: 100;
  inset: 0;
  display: flex;
}

.backdrop {
  position: absolute;
  inset: 0;
  background: var(--vp-backdrop-bg-color);
  transition: opacity 0.5s;
}

/*
 * 开场动画。上游是「唰」地直接出现，配上首开还要等索引，很像卡了一下。
 *
 * 用元素自己的 CSS animation，不用 <Transition>：Vue 的过渡靠双 requestAnimationFrame
 * 推进状态机，标签页切到后台 rAF 就停摆，离场永远走不完，弹层会卡在屏幕上
 * （WikiRefPreview.vue 踩过同一个坑，那里也是这么处理的）。
 * v-if 一变 false 节点立刻消失，所以只做入场。
 * prefers-reduced-motion 由 style.css 末尾的全局规则统一关掉。
 */
@keyframes brs-search-backdrop-in {
  from {
    opacity: 0;
  }
}

@keyframes brs-search-shell-in {
  from {
    opacity: 0;
    transform: translateY(-6px);
  }
}

.backdrop {
  animation: brs-search-backdrop-in 0.14s ease-out;
}

.shell {
  animation: brs-search-shell-in 0.14s ease-out;
}

.shell {
  position: relative;
  padding: 12px;
  margin: 64px auto;
  display: flex;
  flex-direction: column;
  gap: 16px;
  background: var(--vp-local-search-bg);
  width: min(100vw - 60px, 900px);
  height: min-content;
  max-height: min(100vh - 128px, 900px);
  border-radius: 6px;
}

@media (max-width: 767px) {
  .shell {
    margin: 0;
    width: 100vw;
    height: 100vh;
    max-height: none;
    border-radius: 0;
  }
}

.search-bar {
  border: 1px solid var(--vp-c-divider);
  border-radius: 4px;
  display: flex;
  align-items: center;
  padding: 0 12px;
  cursor: text;
}

@media (max-width: 767px) {
  .search-bar {
    padding: 0 8px;
  }
}

.search-bar:focus-within {
  border-color: var(--vp-c-brand-1);
}

.local-search-icon {
  display: block;
  font-size: 18px;
}

.navigate-icon {
  display: block;
  font-size: 14px;
}

.search-icon {
  margin: 8px;
}

@media (max-width: 767px) {
  .search-icon {
    display: none;
  }
}

.search-input {
  padding: 6px 12px;
  font-size: inherit;
  width: 100%;
}

@media (max-width: 767px) {
  .search-input {
    padding: 6px 4px;
  }
}

.search-actions {
  display: flex;
  gap: 4px;
}

@media (any-pointer: coarse) {
  .search-actions {
    gap: 8px;
  }
}

@media (min-width: 769px) {
  .search-actions.before {
    display: none;
  }
}

.search-actions button {
  padding: 8px;
}

.search-actions button:not([disabled]):hover,
.toggle-layout-button.detailed-list {
  color: var(--vp-c-brand-1);
}

.search-actions button.clear-button:disabled {
  opacity: 0.37;
}

.search-keyboard-shortcuts {
  font-size: 0.8rem;
  opacity: 75%;
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  line-height: 14px;
}

.search-keyboard-shortcuts span {
  display: flex;
  align-items: center;
  gap: 4px;
}

@media (max-width: 767px) {
  .search-keyboard-shortcuts {
    display: none;
  }
}

.search-keyboard-shortcuts kbd {
  background: rgba(128, 128, 128, 0.1);
  border-radius: 4px;
  padding: 3px 6px;
  min-width: 24px;
  display: inline-block;
  text-align: center;
  vertical-align: middle;
  border: 1px solid rgba(128, 128, 128, 0.15);
  box-shadow: 0 2px 2px 0 rgba(0, 0, 0, 0.1);
}

.results {
  display: flex;
  flex-direction: column;
  gap: 6px;
  overflow-x: hidden;
  overflow-y: auto;
  overscroll-behavior: contain;
}

.result {
  display: flex;
  align-items: center;
  gap: 8px;
  border-radius: 4px;
  transition: none;
  line-height: 1rem;
  /* 1px：theme/style.css 里那套硬边描线用的都是 1px，上游 2px 会让选中态
     每条结果多撑出 2px，翻结果时整列跟着抖。 */
  border: 1px solid var(--brs-rule-soft);
  outline: none;
}

.result > div {
  margin: 12px;
  width: 100%;
  overflow: hidden;
}

@media (max-width: 767px) {
  .result > div {
    margin: 8px;
  }
}

.titles {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  position: relative;
  z-index: 1001;
  padding: 2px 0;
}

.title {
  display: flex;
  align-items: center;
  gap: 4px;
}

.title.main {
  font-weight: 500;
}

.title-icon {
  opacity: 0.5;
  font-weight: 500;
  color: var(--vp-c-brand-1);
}

.title svg {
  opacity: 0.5;
}

.result.selected {
  /* 摘要上下那两条渐变遮罩用的就是这个变量，底色改了要一起改，否则接缝处会露白 */
  --vp-local-search-result-bg: var(--brs-sunk);
  background: var(--brs-sunk);
  border-color: var(--brs-brass);
}

.excerpt-wrapper {
  position: relative;
}

.excerpt {
  opacity: 50%;
  pointer-events: none;
  max-height: 140px;
  overflow: hidden;
  position: relative;
  margin-top: 4px;
}

.result.selected .excerpt {
  opacity: 1;
}

.excerpt :deep(*) {
  font-size: 0.8rem !important;
  line-height: 130% !important;
}

.titles :deep(mark),
.excerpt :deep(mark) {
  background-color: var(--vp-local-search-highlight-bg);
  color: var(--vp-local-search-highlight-text);
  border-radius: 2px;
  padding: 0 2px;
}

.excerpt :deep(.vp-code-group) .tabs {
  display: none;
}

.excerpt :deep(.vp-code-group) div[class*='language-'] {
  border-radius: 8px !important;
}

.excerpt-gradient-bottom {
  position: absolute;
  bottom: -1px;
  left: 0;
  width: 100%;
  height: 8px;
  background: linear-gradient(transparent, var(--vp-local-search-result-bg));
  z-index: 1000;
}

.excerpt-gradient-top {
  position: absolute;
  top: -1px;
  left: 0;
  width: 100%;
  height: 8px;
  background: linear-gradient(var(--vp-local-search-result-bg), transparent);
  z-index: 1000;
}

.result.selected .titles,
.result.selected .title-icon {
  color: var(--vp-c-brand-1) !important;
}

.no-results {
  font-size: 0.9rem;
  text-align: center;
  padding: 12px;
}

.no-results.brs-loading {
  font-family: var(--brs-mono);
  font-size: 0.8rem;
  letter-spacing: 0.06em;
  color: var(--brs-ink-faint);
}

.brs-count {
  font-family: var(--brs-mono);
  letter-spacing: 0.06em;
  color: var(--brs-brass);
}

svg {
  flex: none;
}
</style>
