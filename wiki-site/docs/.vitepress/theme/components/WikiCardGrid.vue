<script setup lang="ts">
/**
 * WikiCardGrid — 类目主页底部的条目宫格。
 *
 * 泰拉瑞亚 Wiki 的分类页不是一段散文，是一张带图标的清单：一眼看全这类有什么、
 * 每条是干嘛的、点哪进去。本站的 /bosses/ /equipment/ 这些主页此前只有正文散文，
 * 读者要么回侧栏找，要么在正文里找加粗词——两条路都比看图标差。
 *
 * 数据来自 structure.mts，所以加一个条目只改一处，宫格、侧栏、首页门户同时更新。
 * 主页自己那一条（entries[0] 通常就是主页）会被剔掉，不自指。
 */
import { computed } from 'vue'
import WikiIcon from './WikiIcon.vue'
import { useWiki } from '../composables/useWiki'
import type { WikiCategory } from '../../data/structure.mts'

const props = defineProps<{ category: WikiCategory }>()

const { href, t, canonical, entryLabel, entryBlurb, entryTag, categoryLabel } = useWiki()

const items = computed(() => {
  const here = canonical.value.replace(/\/$/, '')
  return props.category.entries.filter((e) => e.path.replace(/\/$/, '') !== here)
})
</script>

<template>
  <section v-if="items.length" class="wiki-grid-section">
    <!--
      标题带上类目名，而不是笼统的「本类目全部条目」。
      系统 / 攻略 / 入门这三类没有独立主页，是拿第一条当主页的，
      所以宫格会出现在「掉落与奖励」这种看着像普通内容页的页面上——
      不写清是哪一类的话，读者会以为这堆卡片属于当前这一页。
    -->
    <h2 class="wiki-grid-section__title">
      {{ categoryLabel(category) }} · {{ t('全部条目', 'All pages') }}
      <span class="wiki-grid-section__count">{{ items.length }}</span>
    </h2>

    <ul class="wiki-grid">
      <li v-for="entry in items" :key="entry.path">
        <a class="wiki-card" :href="href(entry.path)">
          <WikiIcon :icon="entry.icon" :label="entryLabel(entry)" :size="40" />
          <div class="wiki-card__body">
            <p class="wiki-card__name">
              {{ entryLabel(entry) }}
              <span v-if="entryTag(entry)" class="wiki-card__tag">{{ entryTag(entry) }}</span>
            </p>
            <p v-if="entryBlurb(entry)" class="wiki-card__blurb">{{ entryBlurb(entry) }}</p>
          </div>
        </a>
      </li>
    </ul>
  </section>
</template>

<style>
.wiki-grid-section {
  margin-top: 52px;
  padding-top: 0;
  clear: both;
}

.wiki-grid-section__title {
  display: flex;
  align-items: baseline;
  gap: 10px;
  margin: 0 0 20px;
  padding-bottom: 9px;
  border-bottom: 1px solid var(--brs-rule);
  font-family: var(--brs-sans);
  font-size: 14px;
  font-weight: 700;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--brs-brass);
}

.wiki-grid-section__count {
  font-family: var(--brs-mono);
  font-size: 11px;
  letter-spacing: 0.08em;
  color: var(--brs-ink-faint);
}

.wiki-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(248px, 1fr));
  gap: 12px;
  margin: 0;
  padding: 0;
  list-style: none;
  max-width: none;
}

.wiki-grid li {
  margin: 0;
}

.wiki-grid li::marker {
  content: none;
}

.wiki-card {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  height: 100%;
  padding: 14px 15px;
  border: 1px solid var(--brs-rule);
  background: var(--brs-surface);
  text-decoration: none !important;
  transition: border-color 0.15s, background 0.15s;
}

.wiki-card:hover {
  border-color: var(--brs-brass);
  background: var(--brs-ground);
}

.wiki-card__body {
  min-width: 0;
}

.wiki-card__name {
  display: flex;
  align-items: baseline;
  gap: 8px;
  flex-wrap: wrap;
  margin: 0;
  font-family: var(--brs-serif);
  font-size: 16px;
  font-weight: 700;
  line-height: 1.3;
  color: var(--brs-ink);
  max-width: none;
}

.wiki-card__tag {
  font-family: var(--brs-mono);
  font-size: 9.5px;
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--brs-ink-faint);
  border: 1px solid var(--brs-rule);
  padding: 1px 5px;
  white-space: nowrap;
}

.wiki-card__blurb {
  margin: 5px 0 0;
  font-size: 12.5px;
  line-height: 1.6;
  color: var(--brs-ink-soft);
  max-width: none;
}
</style>
