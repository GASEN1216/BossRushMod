<script setup lang="ts">
/**
 * WikiBreadcrumb — 正文顶上的类目面包屑。
 *
 * 侧边栏在窄屏是收起的，滚到半页时顶栏也只剩标题，读者很容易不知道
 * 「我现在在哪一类里」。泰拉瑞亚 Wiki 用页顶的分类条解决这个问题，这里同理：
 * 首页 › 类目 › 本页。类目那一格可点，直接回类目主页。
 *
 * 认不出所属类目时整条不渲染（更新日志、404 等），不占位、不留空行。
 */
import { useWiki } from '../composables/useWiki'

const { located, href, t, categoryLabel, entryLabel } = useWiki()
</script>

<template>
  <nav v-if="located" class="wiki-crumbs" :aria-label="t('位置', 'Breadcrumb')">
    <a class="wiki-crumbs__home" :href="href('/')">{{ t('首页', 'Home') }}</a>
    <span class="wiki-crumbs__sep" aria-hidden="true">›</span>
    <a class="wiki-crumbs__cat" :href="href(located.category.path)">
      {{ categoryLabel(located.category) }}
    </a>
    <span class="wiki-crumbs__sep" aria-hidden="true">›</span>
    <span class="wiki-crumbs__here">{{ entryLabel(located.entry) }}</span>
  </nav>
</template>

<style>
.wiki-crumbs {
  display: flex;
  align-items: center;
  gap: 7px;
  flex-wrap: wrap;
  margin-bottom: 18px;
  font-family: var(--brs-mono);
  font-size: var(--brs-label-size);
  letter-spacing: 0.11em;
  text-transform: uppercase;
  color: var(--brs-ink-faint);
}

.wiki-crumbs a {
  color: var(--brs-ink-faint);
  text-decoration: none;
  border-bottom: 1px solid transparent;
}

.wiki-crumbs a:hover {
  color: var(--brs-brass);
  border-bottom-color: var(--brs-brass);
}

.wiki-crumbs__cat {
  color: var(--brs-brass) !important;
}

.wiki-crumbs__here {
  color: var(--brs-ink-soft);
}

.wiki-crumbs__sep {
  color: var(--brs-rule);
}
</style>
