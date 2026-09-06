<script setup lang="ts">
/**
 * WikiNavbox — 页尾的同类导航条，对应泰拉瑞亚 Wiki 底部那种 navbox 模板。
 *
 * 读完一件装备通常要接着看下一件，但 VitePress 默认页脚只给「上一篇 / 下一篇」
 * 两个链接，读者要跳到同类第五条就得回侧栏。navbox 把整类平铺在页尾，
 * 一行一类、当前条目高亮，读完直接横跳。
 *
 * 只在能定位到类目的页面出现；类目里只有一条（地图、成就、彩蛋）时不渲染——
 * 一个只指向自己的导航条没有意义。
 */
import { computed } from 'vue'
import { useWiki } from '../composables/useWiki'

const { located, href, t, canonical, entryLabel, categoryLabel, tierOf } = useWiki()

const siblings = computed(() => located.value?.category.entries ?? [])
const show = computed(() => siblings.value.length > 1)

const isHere = (path: string) =>
  path.replace(/\/$/, '') === canonical.value.replace(/\/$/, '')
</script>

<template>
  <nav v-if="located && show" class="wiki-navbox" :aria-label="t('同类条目', 'Related pages')">
    <a class="wiki-navbox__cat" :href="href(located.category.path)">
      {{ categoryLabel(located.category) }}
    </a>
    <ul class="wiki-navbox__list">
      <li v-for="entry in siblings" :key="entry.path">
        <!-- .wiki-tier 给装备名上稀有度色；当前页的 .is-here（(0,2,1)）比它（(0,2,0)）权重高，
             所以当前条目仍显示为墨色下划线，不会被稀有度色盖掉 -->
        <a
          class="wiki-tier"
          :data-tier="tierOf(entry.path)"
          :href="href(entry.path)"
          :class="{ 'is-here': isHere(entry.path) }"
          :aria-current="isHere(entry.path) ? 'page' : undefined"
        >
          {{ entryLabel(entry) }}
        </a>
      </li>
    </ul>
  </nav>
</template>

<style>
.wiki-navbox {
  clear: both;
  display: grid;
  grid-template-columns: minmax(90px, auto) 1fr;
  gap: 0 18px;
  align-items: start;
  margin-top: 48px;
  padding-top: 14px;
  border-top: 1px solid var(--brs-rule);
}

.wiki-navbox__cat {
  font-family: var(--brs-mono);
  font-size: var(--brs-label-size);
  font-weight: 600;
  letter-spacing: var(--brs-label-track);
  text-transform: uppercase;
  color: var(--brs-brass);
  text-decoration: none;
  padding-top: 3px;
}

.wiki-navbox__cat:hover {
  text-decoration: underline;
  text-underline-offset: 3px;
}

.wiki-navbox__list {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 0;
  margin: 0;
  padding: 0;
  list-style: none;
  max-width: none;
}

.wiki-navbox__list li {
  margin: 0;
}

.wiki-navbox__list li::marker {
  content: none;
}

/* 用竖线分隔而不是逗号或点：与档案版式的直角硬边一致 */
.wiki-navbox__list li + li::before {
  content: "";
  display: inline-block;
  width: 1px;
  height: 10px;
  margin: 0 10px;
  background: var(--brs-rule);
  vertical-align: -1px;
}

.wiki-navbox__list a {
  font-size: 13px;
  color: var(--brs-ink-soft);
  text-decoration: none;
}

.wiki-navbox__list a:hover {
  color: var(--brs-brass);
  text-decoration: underline;
  text-underline-offset: 3px;
}

.wiki-navbox__list a.is-here {
  color: var(--brs-ink);
  font-weight: 500;
  border-bottom: 2px solid var(--brs-brass);
}

@media (max-width: 640px) {
  .wiki-navbox {
    grid-template-columns: 1fr;
    gap: 8px;
  }
}
</style>
