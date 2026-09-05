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
 *   doc-before        —— 面包屑 + 速查框。在 .vp-doc 之前、同一个块级流里，
 *                        速查框右浮动时正文会自动绕开。
 *   doc-footer-before —— 速查对比表 + 条目宫格 + 页尾导航。放在 doc-after 会掉到
 *                        「上一篇 / 下一篇」下面，读起来像附录；这里才在正文末尾。
 *                        VPDocFooter 只在有 pager 或 lastUpdated 时渲染，
 *                        所以 config.mts 打开了 lastUpdated —— 顺手也把
 *                        「最后更新」那个早就配好却从未生效的标签点亮了。
 *
 * 类目主页上的顺序：先对比表（数据），再宫格（导航），最后同类导航。
 * 更新日志的页面用版本时间线代替同类导航——四十多个版本平铺成一行没法读。
 */
import DefaultTheme from 'vitepress/theme'
import WikiBreadcrumb from './components/WikiBreadcrumb.vue'
import WikiInfobox from './components/WikiInfobox.vue'
import WikiCompare from './components/WikiCompare.vue'
import WikiCardGrid from './components/WikiCardGrid.vue'
import WikiNavbox from './components/WikiNavbox.vue'
import WikiChangelogTimeline from './components/WikiChangelogTimeline.vue'
import WikiRefPreview from './components/WikiRefPreview.vue'
import { useWiki } from './composables/useWiki'

const { hubCategory, isChangelog } = useWiki()
</script>

<template>
  <DefaultTheme.Layout>
    <!-- 悬停预览是页面级浮层，挂在 layout-bottom 而不是正文插槽里：
         它 position:fixed，放在正文流里会被 .vp-doc 的层叠上下文关住。 -->
    <template #layout-bottom>
      <WikiRefPreview />
    </template>

    <template #doc-before>
      <WikiBreadcrumb />
      <WikiInfobox />
    </template>

    <template #doc-footer-before>
      <WikiCompare v-if="hubCategory" :category="hubCategory" />
      <WikiCardGrid v-if="hubCategory" :category="hubCategory" />
      <WikiChangelogTimeline v-if="isChangelog" />
      <WikiNavbox v-else />
    </template>
  </DefaultTheme.Layout>
</template>
