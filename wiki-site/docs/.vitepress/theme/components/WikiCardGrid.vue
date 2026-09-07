<script setup lang="ts">
/**
 * WikiCardGrid — 类目主页底部的条目清单。
 *
 * 换皮之后不再是自研的卡片宫格，而是目标站首页那种 .infocard 盒 + .mclist
 * 多栏图标清单：一行一条，左边贴图右边名字，底下一行小字说明。同样的信息
 * 占的高度只有卡片的三分之一，一屏能看完整个类目。
 *
 * 数据仍来自 structure.mts，本组件零维护。
 */
import { computed } from 'vue'
import WikiIcon from './WikiIcon.vue'
import { useWiki } from '../composables/useWiki'
import { useUiText } from '../composables/useUiText'
import type { WikiCategory } from '../../data/structure.mts'

const props = defineProps<{ category: WikiCategory }>()

const ui = useUiText()
const { href, entryLabel, entryBlurb, entryTag, categoryLabel, tierOf } = useWiki()

const norm = (p: string) => p.replace(/\/$/, '')

/** 类目主页自己不进清单：读者就站在那一页上。 */
const entries = computed(() =>
  props.category.entries.filter((e) => norm(e.path) !== norm(props.category.path))
)
</script>

<template>
  <section v-if="entries.length" class="infocard compact terraria">
    <div class="main-heading">
      <span class="hgroup">
        <span class="main">{{ categoryLabel(category) }}</span>
        <span class="namenote">{{ ui.allEntries }} · {{ entries.length }}</span>
      </span>
      <span class="icon">
        <WikiIcon :icon="category.icon" :label="categoryLabel(category)" :size="28" />
      </span>
    </div>
    <div class="mclist mclist--wide">
      <ul>
        <li v-for="entry in entries" :key="entry.path">
          <a class="i" :href="href(entry.path)" :data-brs-ref="norm(entry.path)">
            <WikiIcon :icon="entry.icon" :label="entryLabel(entry)" :size="40" />
            <span class="mclist__text">
              <span class="mclist__name wiki-tier" :data-tier="tierOf(entry.path)">
                {{ entryLabel(entry) }}
              </span>
              <span v-if="entryBlurb(entry) || entryTag(entry)" class="mclist__blurb">
                {{ entryBlurb(entry) || entryTag(entry) }}
              </span>
            </span>
          </a>
        </li>
      </ul>
    </div>
  </section>
</template>
