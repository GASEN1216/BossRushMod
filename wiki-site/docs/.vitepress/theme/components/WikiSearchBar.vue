<script setup lang="ts">
/**
 * WikiSearchBar — 首页刊头下的整行搜索框 + 「随机条目」。
 *
 * 首页的职责是分流，而搜索是最短的那条路：泰拉瑞亚 Wiki、Fandom 的首页都把
 * 搜索框放在第一屏正中，本站此前只有顶栏右上角那枚小按钮。
 * 这里不另起一套搜索 UI——点击只是把事件转给 VitePress 自带的搜索按钮
 * （.DocSearch-Button），索引、快捷键、结果面板都还是官方那一份。
 *
 * 「随机条目」是维基的老传统：从 structure.mts 的全部条目里随机跳一页，
 * 给逛着玩的读者一个入口。
 */
import { onMounted, ref } from 'vue'
import { useRouter } from 'vitepress'
import { useWiki } from '../composables/useWiki'

const { t, href, categories } = useWiki()
const router = useRouter()

/**
 * 快捷键徽标跟着顶栏那枚走。
 *
 * VitePress 在 <head> 里注入过一段 `check-mac-os` 脚本，按 navigator.platform
 * 给 <html> 打 `.mac` 类，顶栏搜索按钮的 ⌘ / Ctrl 就是靠它用 CSS content 切的。
 * 这里读同一个结论，不再自己写一遍平台嗅探——从前那份正则漏了 iPod，
 * 而且和顶栏那枚各判各的，同一个页面上可能显示成两种样子。
 */
const shortcut = ref('Ctrl K')
onMounted(() => {
  if (document.documentElement.classList.contains('mac')) shortcut.value = '⌘ K'
})

function openSearch() {
  const button = document.querySelector<HTMLButtonElement>('.DocSearch-Button')
  if (button) button.click()
}

function randomPage() {
  const pool = categories.flatMap((category) => category.entries)
  const pick = pool[Math.floor(Math.random() * pool.length)]
  if (pick) router.go(href(pick.path))
}
</script>

<template>
  <div class="wiki-searchbar">
    <button type="button" class="wiki-searchbar__input" @click="openSearch">
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
        <circle cx="11" cy="11" r="7" />
        <path d="m20 20-3.5-3.5" stroke-linecap="round" />
      </svg>
      <span class="wiki-searchbar__placeholder">
        {{ t('搜索 Boss、装备、模式、成就……', 'Search bosses, gear, modes, achievements…') }}
      </span>
      <kbd>{{ shortcut }}</kbd>
    </button>
    <button type="button" class="wiki-searchbar__random" @click="randomPage">
      {{ t('随机条目', 'Random page') }}
    </button>
  </div>
</template>
