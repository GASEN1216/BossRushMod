<script setup lang="ts">
/**
 * Layout — 在默认主题外面套一层，把 Wiki 化的构件塞进它的插槽。
 *
 * 一个字都不改 WikiContent/，也不改 sync-content.mjs 的产物：正文照旧是那些
 * markdown，面包屑 / 速查框 / 条目宫格 / 页尾导航全部由路由反查 structure.mts 得来。
 * 这样在线站可以长出泰拉瑞亚 Wiki 那套结构，而**游戏内 Wiki 书一个字都不受影响**
 * （它的解析器不认表格也不认图片，见 Integration/WikiContentManager.cs）。
 *
 * 插槽选择：
 *   doc-before        —— 只剩面包屑。
 *                        **速查框不在这里了**：这个插槽落在 .content-container 里、
 *                        `main > .vp-doc` 之前，速查框摆在这儿就排在 h1 前面，
 *                        宽屏上 h1 的底线会整幅穿过浮动的框身，窄屏上则是
 *                        「先一张大卡片、再标题」。现在改由 config.mts 的
 *                        infoboxSlotPlugin 在渲染期插到正文第一个 h1 之后，
 *                        组件在 theme/index.ts 里全局注册。
 *   doc-footer-before —— 速查对比表 + 条目宫格 + 页尾导航。放在 doc-after 会掉到
 *                        「上一篇 / 下一篇」下面，读起来像附录；这里才在正文末尾。
 *                        VPDocFooter 只在有 pager 或 lastUpdated 时渲染，
 *                        所以 config.mts 打开了 lastUpdated —— 顺手也把
 *                        「最后更新」那个早就配好却从未生效的标签点亮了。
 *   layout-bottom     —— 悬停预览与配图灯箱（position:fixed 的页面级浮层）。
 *
 * 类目主页上的顺序：先对比表（数据），再宫格（导航），最后同类导航。
 * 更新日志的页面用版本时间线代替同类导航——四十多个版本平铺成一行没法读。
 */
import { onMounted, watch } from 'vue'
import DefaultTheme from 'vitepress/theme'
import { useData } from 'vitepress'
import WikiBreadcrumb from './components/WikiBreadcrumb.vue'
import WikiCompare from './components/WikiCompare.vue'
import WikiCardGrid from './components/WikiCardGrid.vue'
import WikiNavbox from './components/WikiNavbox.vue'
import WikiChangelogTimeline from './components/WikiChangelogTimeline.vue'
import WikiRefPreview from './components/WikiRefPreview.vue'
import WikiLightbox from './components/WikiLightbox.vue'
import { useWiki } from './composables/useWiki'

const { hubCategory, isChangelog } = useWiki()
const { isDark, localeIndex, theme } = useData()

/**
 * 手机地址栏配色跟着**实际**主题走。
 *
 * config.mts 里那两枚 theme-color 各带一个 prefers-color-scheme media，
 * 只认系统偏好；读者手动按下顶栏那个开关之后，系统仍是浅色的话地址栏还是纸色，
 * 与页面对不上。这里把两枚的 content 一起改成当前主题的颜色——
 * 无论哪一枚的 media 命中，拿到的都是对的值。媒体查询保留着，
 * 关了 JS 的浏览器仍按系统偏好走，不会退化成没有 theme-color。
 *
 * 取值与 style.css §1 的 --brs-ground 一致。
 */
function syncThemeColor(dark: boolean) {
  const color = dark ? '#141110' : '#eae6de'
  document
    .querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')
    .forEach((meta) => meta.setAttribute('content', color))
}

/**
 * 搜索预热。
 *
 * 索引是全站最大的一块资源（中文 1.03 MB），默认要等读者点开弹层才开始下载
 * 与解析，中间是一片空白面板。这里在首屏空闲时先把它和弹层组件拉下来，
 * 按下 Ctrl+K 时通常已经是热的。细节见 composables/searchIndex.ts。
 *
 * 分两档，因为多数读者是来看某一页的、从不搜索，不该替他们决定下载 250 KB：
 *   空闲档  —— 总是拉弹层组件；索引只在连接不省流量时拉。
 *   意图档  —— 鼠标移到搜索框上或它拿到焦点，说明真要搜了，强制拉全套。
 * 意图事件用 pointerover / focusin（都冒泡），一次性委托在 document 上；
 * pointerenter 不冒泡，委托不到。
 */
function scheduleWarmup() {
  // 0 = 没预热过，1 = 空闲预热（省流量连接上只拉弹层组件），2 = 已强制拉全套
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
    if (el?.closest?.('.DocSearch-Button, .wiki-searchbar__input')) warm(true)
  }

  document.addEventListener('pointerover', onIntent, true)
  document.addEventListener('focusin', onIntent, true)

  const idle = (window as any).requestIdleCallback
  if (typeof idle === 'function') idle(() => warm(false), { timeout: 4000 })
  else window.setTimeout(() => warm(false), 1500)
}

onMounted(() => {
  syncThemeColor(isDark.value)
  watch(isDark, syncThemeColor)
  scheduleWarmup()
})
</script>

<template>
  <DefaultTheme.Layout>
    <!-- 悬停预览与配图灯箱都是页面级浮层，挂在 layout-bottom 而不是正文插槽里：
         它们 position:fixed，放在正文流里会被 .vp-doc 的层叠上下文关住。 -->
    <template #layout-bottom>
      <WikiRefPreview />
      <WikiLightbox />
    </template>

    <template #doc-before>
      <WikiBreadcrumb />
    </template>

    <template #doc-footer-before>
      <WikiCompare v-if="hubCategory" :category="hubCategory" />
      <WikiCardGrid v-if="hubCategory" :category="hubCategory" />
      <WikiChangelogTimeline v-if="isChangelog" />
      <WikiNavbox v-else />
    </template>
  </DefaultTheme.Layout>
</template>
