<script setup lang="ts">
/**
 * Layout — 整站骨架，照 MediaWiki Vector-legacy 的 DOM 重建。
 *
 * 这一版**不再继承 VitePress 默认主题**。默认主题的 DOM（固定顶栏 VPNav、
 * 272px 的 VPSidebar、右侧 VPDocAside 目录、上下篇 VPDocFooter）在 Vector 那套
 * 皮里一个都没有对应物：网络栏、Logo 带、门户框、标签页、分类栏、页脚全得新造。
 * 继续套着它就只能「先 display:none 掉一半，再往插槽里塞另一半」，最后是两套 DOM
 * 叠着加上千行 !important。自己写这二百行反而是改动更小的那条路。
 *
 * 换掉的只有表现层。数据与逻辑一行没动：structure.mts / infobox.mts /
 * useWiki / searchIndex / search.mts / seo.mts / feed.mts / sync-content.mjs
 * 全部照旧，VitePress 仍然管构建、路由、i18n、<Content>、page.headers、
 * lastUpdated 与本地搜索索引。
 *
 * id 与 class 一律沿用 MediaWiki 的原名（#mw-panel / #mw-head / #content /
 * .catlinks / .mw-parser-output …）：它们是通用名字，照抄能让 CSS 逐条对着
 * 目标站的实现改，将来排错不用做心智转换。
 *
 * 网格挂在 .mw-layout 而不是 <body> 上——VitePress 的根是 #app，
 * 这是与目标站唯一的结构性差别。
 */
import { computed, onMounted } from 'vue'
import { Content, useData } from 'vitepress'
import WikiNetbar from './components/WikiNetbar.vue'
import WikiPanel from './components/WikiPanel.vue'
import WikiHead from './components/WikiHead.vue'
import WikiCatlinks from './components/WikiCatlinks.vue'
import WikiFooter from './components/WikiFooter.vue'
import WikiCompare from './components/WikiCompare.vue'
import WikiCardGrid from './components/WikiCardGrid.vue'
import WikiNavbox from './components/WikiNavbox.vue'
import WikiNotFound from './components/WikiNotFound.vue'
import WikiRefPreview from './components/WikiRefPreview.vue'
import WikiLightbox from './components/WikiLightbox.vue'
import { useWiki } from './composables/useWiki'
import { useUiText } from './composables/useUiText'

const ui = useUiText()
const { page, frontmatter, localeIndex, theme, site } = useData()
const { hubCategory } = useWiki()

/** 首页是门户版式：不出分类栏，也不出页尾同类导航。 */
const isHome = computed(() => frontmatter.value.layout === 'page')

/**
 * 搜索预热（原样保留，只把「意图」的选择器换成新搜索框的 #searchInput）。
 *
 * 索引是全站最大的一块资源（中文 1.03 MB），默认要等读者点开才开始下载与解析。
 * 分两档：空闲时总是拉弹层组件，索引只在连接不省流量时拉；鼠标移到搜索框上
 * 或它拿到焦点，说明真要搜了，强制拉全套。细节见 composables/searchIndex.ts。
 */
function scheduleWarmup() {
  let level = 0

  const warm = (force: boolean) => {
    const want = force ? 2 : 1
    if (level >= want) return
    level = want
    if (level === 2) {
      document.removeEventListener('pointerover', onIntent, true)
      document.removeEventListener('focusin', onIntent, true)
    }
    import('./composables/searchIndex')
      .then((m) => m.warmupSearch(localeIndex.value, theme.value, force))
      .catch(() => {})
  }

  function onIntent(event: Event) {
    const el = event.target as HTMLElement | null
    if (el?.closest?.('#searchInput, #searchButton')) warm(true)
  }

  document.addEventListener('pointerover', onIntent, true)
  document.addEventListener('focusin', onIntent, true)

  const idle = (window as any).requestIdleCallback
  if (typeof idle === 'function') idle(() => warm(false), { timeout: 4000 })
  else window.setTimeout(() => warm(false), 1500)
}

onMounted(scheduleWarmup)
</script>

<template>
  <div class="mw-layout" :class="{ 'is-home': isHome, 'page-notfound': page.isNotFound }">
    <a class="mw-jump-link" href="#content">{{ ui.jumpToContent }}</a>

    <WikiNetbar />

    <div id="p-logo" role="banner">
      <a class="mw-wiki-logo" :href="site.base" :title="site.title">
        <span class="mw-wiki-logo__wordmark">
          BossRush
          <em>WIKI</em>
        </span>
      </a>
    </div>

    <div id="mw-navigation">
      <WikiHead />
      <WikiPanel />
    </div>

    <div class="content-wrapper">
      <div id="mw-page-base" class="noprint" />

      <div id="content" class="mw-body" role="main">
        <WikiNotFound v-if="page.isNotFound" />
        <template v-else>
          <div id="bodyContent" class="vector-body">
            <div id="mw-content-text" class="mw-body-content">
              <!-- 正文内部的顺序由 config.mts 在渲染期决定：
                   h1 → #contentSub → 速查框 → 导语 → 目录框 → h2 … -->
              <Content class="mw-parser-output" />

              <WikiCompare v-if="hubCategory" :category="hubCategory" />
              <WikiCardGrid v-if="hubCategory" :category="hubCategory" />
              <WikiNavbox v-if="!isHome" />
            </div>
          </div>
          <WikiCatlinks v-if="!isHome" />
        </template>
      </div>
    </div>

    <WikiFooter />

    <!-- 两枚页面级浮层：position:fixed，放在正文流里会被内容面板的层叠上下文关住 -->
    <WikiRefPreview />
    <WikiLightbox />
  </div>
</template>
