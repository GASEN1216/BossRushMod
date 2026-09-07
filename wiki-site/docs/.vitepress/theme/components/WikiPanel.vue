<script setup lang="ts">
/**
 * WikiPanel — 左侧的门户框栏（MediaWiki 的 #mw-panel）。
 *
 * 取代了从前 config.mts 里生成的 VitePress 侧边栏。结构与目标站一致：一摞
 * 可折叠的「门户框」，每框一个标题加一列链接，第一框顶上压着一条草皮。
 *
 * 框的构成：
 *   导航     —— 首页 / 最近更改 / 随机页面 / 帮助
 *   每个类目 —— 一框，当前所在的那个默认展开，其余折叠
 *   更新日志 —— 总览 + 最近十个版本 + 「全部版本」
 *   更多     —— 只在 <=1366 的横条模式里出现，收着 NAV_MORE 那六个类目
 *   其他语言 —— 中 / 英
 *
 * 折叠状态存 localStorage，但**首屏不读**：SSR 时按「当前类目展开」渲染，
 * 挂载后再套上读者自己的偏好。否则服务端与客户端的类名对不上，
 * hydration 会整块重画。
 *
 * <=1366 时这一栏由 responsive.css 改造成横条 + 悬停下拉，<=900 再收成汉堡；
 * 那两档不需要另一套 DOM，只是同一份结构换个摆法。
 */
import { computed, onMounted, ref } from 'vue'
import { useData, useRouter, withBase } from 'vitepress'
import { useWiki } from '../composables/useWiki'
import { useUiText } from '../composables/useUiText'
import {
  CATEGORIES,
  CHANGELOG_CATEGORY,
  NAV_MORE,
  NAV_PRIMARY,
  localizePath,
} from '../../data/structure.mts'
import { data as releases } from '../../data/changelog.data.mts'

const ui = useUiText()
const router = useRouter()
const { lang } = useData()
const { canonical, located, href, locale, categoryLabel, entryLabel, tierOf } = useWiki()

const norm = (p: string) => (p.length > 1 ? p.replace(/\/$/, '') : p)

/** 每个类目一框；NAV_PRIMARY 之外的在横条模式里让位给「更多」下拉 */
const portals = computed(() =>
  CATEGORIES.map((category) => ({
    category,
    secondary: !NAV_PRIMARY.includes(category.id),
  }))
)

const currentCategoryId = computed(() => located.value?.category.id ?? null)

/** 手动折叠的覆盖值：undefined = 跟着「是不是当前类目」走 */
const overrides = ref<Record<string, boolean>>({})
const menuOpen = ref(false)

function collapsed(id: string): boolean {
  const manual = overrides.value[id]
  if (manual !== undefined) return manual
  return currentCategoryId.value !== id
}

function toggle(id: string) {
  const next = !collapsed(id)
  overrides.value = { ...overrides.value, [id]: next }
  try {
    localStorage.setItem('brs-portal-' + id, next ? '1' : '0')
  } catch {
    // 隐私模式：这次折叠照样生效，只是记不住
  }
}

onMounted(() => {
  const stored: Record<string, boolean> = {}
  for (const { category } of portals.value) {
    try {
      const raw = localStorage.getItem('brs-portal-' + category.id)
      if (raw === '0' || raw === '1') stored[category.id] = raw === '1'
    } catch {
      return
    }
  }
  overrides.value = stored
})

const isHere = (path: string) => norm(path) === norm(canonical.value)

const recentReleases = computed(() => releases.slice(0, 10))
const changelogHere = computed(() => canonical.value.startsWith('/changelog'))

const moreCategories = computed(() =>
  NAV_MORE.map((id) => CATEGORIES.find((c) => c.id === id)).filter((c): c is (typeof CATEGORIES)[number] => !!c)
)

/** 随机条目：把所有类目的条目摊平抽一条，等价于 MediaWiki 的 Special:Random */
function randomPage() {
  const all = CATEGORIES.flatMap((c) => c.entries.map((e) => e.path))
  if (!all.length) return
  router.go(href(all[Math.floor(Math.random() * all.length)]))
}

const otherLangHref = computed(() =>
  withBase(localizePath(canonical.value, lang.value.startsWith('en') ? 'zh' : 'en'))
)
</script>

<template>
  <div id="mw-panel" :class="{ 'is-open': menuOpen }">
    <!-- <=900 才显形：汉堡与语言 -->
    <button
      class="menu-toggle"
      type="button"
      :aria-label="ui.menu"
      :aria-expanded="menuOpen"
      @click="menuOpen = !menuOpen"
    >
      <span class="tw-icon tw-icon--category" aria-hidden="true" />
    </button>
    <a class="menu-lang-toggle" :href="otherLangHref" :aria-label="ui.otherLanguages">
      <span class="tw-icon tw-icon--language" aria-hidden="true" />
    </a>

    <!-- 导航 -->
    <nav id="p-navigation" class="mw-portlet portal vector-menu vector-menu-portal">
      <button class="vector-menu-heading" type="button" @click="toggle('__nav')">
        <span class="vector-menu-heading-label">{{ ui.navigation }}</span>
      </button>
      <div class="vector-menu-content">
        <ul class="vector-menu-content-list">
          <li class="mw-list-item"><a :href="href('/')">{{ ui.mainPage }}</a></li>
          <li class="mw-list-item">
            <a :href="href(CHANGELOG_CATEGORY.path)">{{ ui.recentChanges }}</a>
          </li>
          <li class="mw-list-item">
            <a href="#" @click.prevent="randomPage()">{{ ui.randomPage }}</a>
          </li>
          <li class="mw-list-item">
            <a :href="href('/getting-started/overview')">{{ ui.help }}</a>
          </li>
        </ul>
      </div>
    </nav>

    <!-- 每个类目一框 -->
    <nav
      v-for="p in portals"
      :id="'p-cat-' + p.category.id"
      :key="p.category.id"
      class="mw-portlet portal vector-menu vector-menu-portal"
      :class="{ collapsed: collapsed(p.category.id), 'portal--secondary': p.secondary }"
    >
      <button class="vector-menu-heading" type="button" @click="toggle(p.category.id)">
        <span class="vector-menu-heading-label">{{ categoryLabel(p.category) }}</span>
      </button>
      <div class="vector-menu-content">
        <ul class="vector-menu-content-list">
          <li
            v-for="entry in p.category.entries"
            :key="entry.path"
            class="mw-list-item"
            :class="{ selected: isHere(entry.path) }"
          >
            <a :href="href(entry.path)" class="wiki-tier" :data-tier="tierOf(entry.path)">
              {{ entryLabel(entry) }}
            </a>
          </li>
        </ul>
      </div>
    </nav>

    <!-- 更新日志 -->
    <nav
      id="p-changelog"
      class="mw-portlet portal vector-menu vector-menu-portal"
      :class="{ collapsed: !changelogHere }"
    >
      <button class="vector-menu-heading" type="button" @click="toggle('changelog')">
        <span class="vector-menu-heading-label">
          {{ locale === 'en' ? CHANGELOG_CATEGORY.en : CHANGELOG_CATEGORY.zh }}
        </span>
      </button>
      <div class="vector-menu-content">
        <ul class="vector-menu-content-list">
          <li class="mw-list-item" :class="{ selected: isHere(CHANGELOG_CATEGORY.path) }">
            <a :href="href(CHANGELOG_CATEGORY.path)">
              {{ locale === 'en' ? 'Overview' : '总览' }}
            </a>
          </li>
          <li
            v-for="rel in recentReleases"
            :key="rel.version"
            class="mw-list-item"
            :class="{ selected: isHere(rel.path) }"
          >
            <a :href="href(rel.path)">v{{ rel.version }}</a>
          </li>
          <li class="mw-list-item portal-more">
            <a :href="href(CHANGELOG_CATEGORY.path)">{{ ui.allVersions }} →</a>
          </li>
        </ul>
      </div>
    </nav>

    <!-- 「更多」：只在横条模式里顶替被收起的那几个类目框 -->
    <nav
      id="p-more"
      class="mw-portlet portal vector-menu vector-menu-portal portal--barmenu"
    >
      <button class="vector-menu-heading" type="button">
        <span class="vector-menu-heading-label">{{ ui.more }}</span>
      </button>
      <div class="vector-menu-content">
        <ul class="vector-menu-content-list">
          <li v-for="cat in moreCategories" :key="cat.id" class="mw-list-item">
            <a :href="href(cat.path)">{{ categoryLabel(cat) }}</a>
          </li>
        </ul>
      </div>
    </nav>

    <!-- 其他语言 -->
    <nav id="p-lang" class="mw-portlet portal vector-menu vector-menu-portal">
      <button class="vector-menu-heading" type="button" @click="toggle('__lang')">
        <span class="vector-menu-heading-label">{{ ui.otherLanguages }}</span>
      </button>
      <div class="vector-menu-content">
        <ul class="vector-menu-content-list">
          <li class="mw-list-item">
            <a :href="otherLangHref">{{ lang.startsWith('en') ? '中文' : 'English' }}</a>
          </li>
        </ul>
      </div>
    </nav>
  </div>
</template>
