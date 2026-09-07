<script setup lang="ts">
/**
 * WikiNetbar — 页面最顶上那条固定的网络栏。
 *
 * 目标站顶上是 wiki.gg 的站群栏（35px、深蓝、Nunito）：左边站群 Logo 与一枚
 * 胶囊链接，右边是「外观」下拉与账号入口。本站没有站群也没有账号，于是
 * 同一条位置放：站名 + 创意工坊入口 / 外观 + 语言 + 仓库。
 *
 * 「外观」是本站唯一的换肤入口，机制见 composables/useSkinTheme.ts。
 * 下拉靠 :hover 与 :focus-within 开合（layout.css），标题是 <button>，
 * 所以触屏点一下拿到焦点也能展开；Esc 关闭那一条纯 CSS 做不到，
 * 由 useDropdownDismiss 补。
 */
import { computed } from 'vue'
import { useData, withBase } from 'vitepress'
import { useUiText } from '../composables/useUiText'
import { useSkinTheme } from '../composables/useSkinTheme'
import { usePageLinks } from '../composables/usePageLinks'
import { useDropdownDismiss } from '../composables/useDropdownDismiss'
import { canonicalPath, localizePath } from '../../data/structure.mts'
import { useRoute } from 'vitepress'

const ui = useUiText()
const { current, themes, set } = useSkinTheme()
const { repo } = usePageLinks()
const {
  dismissed: skinDismissed,
  trigger: skinTrigger,
  dismiss: skinDismiss,
  onFocusOut: skinFocusOut,
  reopen: skinReopen,
} = useDropdownDismiss()
const { lang, site } = useData()
const route = useRoute()

const isEn = computed(() => lang.value.startsWith('en'))

/** 另一语言的同一页。全站条目中英一一对应，所以直接换前缀即可。 */
const otherLangHref = computed(() =>
  withBase(localizePath(canonicalPath(route.path, site.value.base), isEn.value ? 'zh' : 'en'))
)
</script>

<template>
  <div id="wgg-netbar" class="wgg-netbar" role="navigation" :aria-label="ui.siteTools">
    <div class="wgg-netbar__left">
      <a class="wgg-netbar__logo" :href="withBase('/')">
        <span class="wgg-netbar__mark">BossRush</span>
        <span>Wiki</span>
      </a>
      <a
        class="wgg-netbar__pill"
        href="https://steamcommunity.com/app/2411430/workshop/"
        target="_blank"
        rel="noopener"
      >
        <span class="tw-icon tw-icon--external" aria-hidden="true" />
        <span>{{ isEn ? 'Steam Workshop' : '创意工坊' }}</span>
      </a>
    </div>

    <nav class="wgg-netbar__right" :aria-label="ui.appearance">
      <div
        id="p-appearance"
        class="vector-menu vector-menu-dropdown"
        :class="{ 'is-dismissed': skinDismissed }"
        @keydown.esc="skinDismiss"
        @focusout="skinFocusOut"
      >
        <button
          ref="skinTrigger"
          class="vector-menu-heading"
          type="button"
          :aria-label="ui.appearance"
          aria-haspopup="true"
          @click="skinReopen"
        >
          <span class="tw-icon tw-icon--brush" aria-hidden="true" />
          <span>{{ ui.appearance }}</span>
          <span class="tw-icon tw-icon--chevron-down" aria-hidden="true" />
        </button>
        <div class="vector-menu-content">
          <ul>
            <li v-for="theme in themes" :key="theme.name">
              <button
                type="button"
                :class="{ 'is-current': current === theme.name }"
                :aria-pressed="current === theme.name"
                @click="set(theme.name)"
              >
                <span
                  class="tw-icon tw-icon--check"
                  :style="{ visibility: current === theme.name ? 'visible' : 'hidden' }"
                  aria-hidden="true"
                />
                <span>{{ ui[theme.label] }}</span>
              </button>
            </li>
          </ul>
        </div>
      </div>

      <a class="wgg-netbar__pill" :href="otherLangHref">
        <span class="tw-icon tw-icon--language" aria-hidden="true" />
        <span>{{ isEn ? '中文' : 'English' }}</span>
      </a>

      <a class="wgg-netbar__pill" :href="repo" target="_blank" rel="noopener" aria-label="GitHub">
        <span class="tw-icon tw-icon--github" aria-hidden="true" />
      </a>
    </nav>
  </div>
</template>
