<script setup lang="ts">
/**
 * WikiHead — 内容面板顶上那排标签（MediaWiki 的 #mw-head）。
 *
 * 左边是命名空间标签（页面 / 讨论），右边是视图标签（阅读 / 查看源代码 /
 * 查看历史）、一个「更多」下拉，和搜索框。选中的那枚与内容面板同底色、
 * 没有底边，两块因此连成一体——这是 Vector 那套皮最认得出来的一处细节。
 *
 * 本站没有 wiki 后台，「查看源代码 / 查看历史 / 编辑」一律指向 GitHub 上的
 * **正文源文件**（不是 wiki-site/docs/ 里的生成物，改那儿会被 sync 抹掉）。
 * 路径来自 seo.mts 写进 frontmatter 的 editSource；首页与 404 没有源文件，
 * 这三枚就不渲染——不给读者一个 404 的入口。
 *
 * 下拉靠 CSS 的 :hover / :focus-within 开合，标题是 <button> 所以触屏也能点开。
 */
import { computed } from 'vue'
import { useData, useRouter } from 'vitepress'
import WikiHeadSearch from './WikiHeadSearch.vue'
import { useUiText } from '../composables/useUiText'
import { usePageLinks } from '../composables/usePageLinks'
import { useWiki } from '../composables/useWiki'
import { CATEGORIES, CHANGELOG_CATEGORY } from '../../data/structure.mts'

const ui = useUiText()
const router = useRouter()
const { theme } = useData()
const { viewSource, viewHistory, editPage, talk, repo } = usePageLinks()
const { href } = useWiki()

const hasSource = computed(() => viewSource.value !== null)
// 根部署且没配 SITE_URL 时 feed.xml 根本不生成，这时不渲染这一项
const feedHref = computed<string | null>(() => theme.value.feedUrl ?? null)

function randomPage() {
  const all = CATEGORIES.flatMap((c) => c.entries.map((e) => e.path))
  if (all.length) router.go(href(all[Math.floor(Math.random() * all.length)]))
}

function print() {
  if (typeof window !== 'undefined') window.print()
}
</script>

<template>
  <div id="mw-head">
    <div id="left-navigation">
      <nav id="p-namespaces" class="mw-portlet vector-menu vector-menu-tabs" :aria-label="ui.page">
        <ul>
          <li id="ca-nstab-main" class="mw-list-item selected">
            <a :href="href('/')" @click.prevent>
              <span class="tw-icon tw-icon--file" aria-hidden="true" />
              <span class="tab-label">{{ ui.page }}</span>
            </a>
          </li>
          <li id="ca-talk" class="mw-list-item">
            <a :href="talk" target="_blank" rel="noopener">
              <span class="tw-icon tw-icon--messages" aria-hidden="true" />
              <span class="tab-label">{{ ui.talk }}</span>
            </a>
          </li>
        </ul>
      </nav>
    </div>

    <div id="right-navigation">
      <nav id="p-views" class="mw-portlet vector-menu vector-menu-tabs" :aria-label="ui.read">
        <ul>
          <li id="ca-view" class="mw-list-item selected">
            <a href="#" @click.prevent>
              <span class="tw-icon tw-icon--eye" aria-hidden="true" />
              <span class="tab-label">{{ ui.read }}</span>
            </a>
          </li>
          <li v-if="hasSource" id="ca-viewsource" class="mw-list-item">
            <a :href="viewSource!" target="_blank" rel="noopener">
              <span class="tw-icon tw-icon--code" aria-hidden="true" />
              <span class="tab-label">{{ ui.viewSource }}</span>
            </a>
          </li>
          <li v-if="hasSource" id="ca-history" class="mw-list-item">
            <a :href="viewHistory!" target="_blank" rel="noopener">
              <span class="tw-icon tw-icon--history" aria-hidden="true" />
              <span class="tab-label">{{ ui.viewHistory }}</span>
            </a>
          </li>
        </ul>
      </nav>

      <nav id="p-cactions" class="mw-portlet vector-menu vector-menu-dropdown" :aria-label="ui.more">
        <button class="vector-menu-heading" type="button" aria-haspopup="true">
          <span class="tw-icon tw-icon--dots-vertical" aria-hidden="true" />
          <span class="tab-label">{{ ui.more }}</span>
        </button>
        <div class="vector-menu-content">
          <ul>
            <li v-if="editPage">
              <a :href="editPage" target="_blank" rel="noopener">
                <span class="tw-icon tw-icon--pencil" aria-hidden="true" />{{ ui.edit }}
              </a>
            </li>
            <li>
              <a href="#" @click.prevent="randomPage()">
                <span class="tw-icon tw-icon--dice" aria-hidden="true" />{{ ui.randomPage }}
              </a>
            </li>
            <li>
              <a :href="href(CHANGELOG_CATEGORY.path)">
                <span class="tw-icon tw-icon--history" aria-hidden="true" />{{ ui.changelog }}
              </a>
            </li>
            <li v-if="feedHref">
              <a :href="feedHref">
                <span class="tw-icon tw-icon--rss" aria-hidden="true" />{{ ui.feed }}
              </a>
            </li>
            <li>
              <a :href="repo" target="_blank" rel="noopener">
                <span class="tw-icon tw-icon--github" aria-hidden="true" />{{ ui.sourceRepo }}
              </a>
            </li>
            <li>
              <a href="#" @click.prevent="print()">
                <span class="tw-icon tw-icon--printer" aria-hidden="true" />{{ ui.printPage }}
              </a>
            </li>
          </ul>
        </div>
      </nav>

      <div id="p-search" role="search">
        <WikiHeadSearch />
      </div>
    </div>
  </div>
</template>
