<script setup lang="ts">
/**
 * WikiFooter — 页脚（MediaWiki 的 #footer）。
 *
 * 三段，顺序照抄目标站：
 *   #footer-info   最后编辑时间 + 版权 + 版式出处
 *   #footer-places 一排普通链接
 *   #footer-icons  右浮的 88×31 徽标（这里是自制的文字徽标，不用别家的图）
 *
 * 「最后编辑于」的日期在 onMounted 之后才格式化。page.lastUpdated 是个时间戳，
 * 服务端与浏览器所在时区不一定相同，直接在 SSR 里格式化会与 hydration 后的结果
 * 对不上，Vue 会整块重画并在控制台报 mismatch。
 *
 * 归属声明（Terraria Wiki / wiki.gg）是**必须留着**的一行：本站的版式是照它
 * 逐值量出来重写的，CSS 与素材全部自制，但出处该说清楚。
 * tests/WikiThemeSwitchGuard.py 会检查这行还在。
 */
import { computed, onMounted, ref } from 'vue'
import { useData } from 'vitepress'
import { useUiText } from '../composables/useUiText'
import { usePageLinks } from '../composables/usePageLinks'
import { useWiki } from '../composables/useWiki'
import { CHANGELOG_CATEGORY } from '../../data/structure.mts'

const ui = useUiText()
const { page, theme, lang } = useData()
const { repo, editPage, issues } = usePageLinks()
const { href } = useWiki()

const formatted = ref('')
onMounted(() => {
  const stamp = page.value.lastUpdated
  if (!stamp) return
  formatted.value = new Intl.DateTimeFormat(lang.value.startsWith('en') ? 'en-US' : 'zh-CN', {
    dateStyle: 'long',
    timeStyle: 'short',
  }).format(new Date(stamp))
})

const feedHref = computed<string | null>(() => theme.value.feedUrl ?? null)
const footerText = computed(() => theme.value.footer ?? {})
</script>

<template>
  <div id="footer" class="mw-footer" role="contentinfo">
    <ul id="footer-icons" class="noprint">
      <li>
        <a href="https://vitepress.dev" target="_blank" rel="noopener">Powered by VitePress</a>
      </li>
      <li>
        <a :href="repo" target="_blank" rel="noopener">BossRush Mod</a>
      </li>
      <li>
        <a href="https://store.steampowered.com/app/2411430/" target="_blank" rel="noopener">
          Escape from Duckov
        </a>
      </li>
    </ul>

    <ul id="footer-info">
      <li v-if="formatted" id="footer-info-lastmod">{{ ui.lastEdited(formatted) }}</li>
      <li id="footer-info-copyright">
        {{ footerText.message }} · {{ footerText.copyright }}
      </li>
      <li id="footer-info-attrib">{{ ui.attribution }}</li>
    </ul>

    <ul id="footer-places">
      <li><a :href="href('/getting-started/overview')">{{ ui.about }}</a></li>
      <li><a :href="href(CHANGELOG_CATEGORY.path)">{{ ui.changelog }}</a></li>
      <li v-if="feedHref"><a :href="feedHref">{{ ui.feed }}</a></li>
      <li v-if="editPage">
        <a :href="editPage" target="_blank" rel="noopener">{{ ui.edit }}</a>
      </li>
      <li><a :href="issues" target="_blank" rel="noopener">{{ ui.talk }}</a></li>
      <li><a :href="repo" target="_blank" rel="noopener">{{ ui.sourceRepo }}</a></li>
    </ul>
  </div>
</template>
