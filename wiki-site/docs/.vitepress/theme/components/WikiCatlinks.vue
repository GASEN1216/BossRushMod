<script setup lang="ts">
/**
 * WikiCatlinks — 正文最底下那条「分类：…」（MediaWiki 的 #catlinks）。
 *
 * 用 locateAll 而不是 locate：一个条目可以同时挂在多个类目下
 *（「好感度与婚姻」既是 NPC 也是系统），面包屑只能显示一条，分类栏应当列全——
 * 只写一个就等于把另一半藏了。
 */
import { computed } from 'vue'
import { useWiki } from '../composables/useWiki'
import { useUiText } from '../composables/useUiText'
import { CHANGELOG_CATEGORY, locateAll } from '../../data/structure.mts'

const ui = useUiText()
const { canonical, href, locale, categoryLabel, isChangelog } = useWiki()

const categories = computed(() => {
  const hits = locateAll(canonical.value)
  if (hits.length) {
    const seen = new Set<string>()
    return hits
      .filter((hit) => !seen.has(hit.category.id) && seen.add(hit.category.id))
      .map((hit) => ({ path: hit.category.path, label: categoryLabel(hit.category) }))
  }
  if (isChangelog.value) {
    return [
      {
        path: CHANGELOG_CATEGORY.path,
        label: locale.value === 'en' ? CHANGELOG_CATEGORY.en : CHANGELOG_CATEGORY.zh,
      },
    ]
  }
  return []
})
</script>

<template>
  <div v-if="categories.length" id="catlinks" class="catlinks">
    <div id="mw-normal-catlinks" class="mw-normal-catlinks">
      <span class="catlinks-label">{{ ui.categories }}：</span>
      <ul>
        <li v-for="cat in categories" :key="cat.path">
          <a :href="href(cat.path)">{{ cat.label }}</a>
        </li>
      </ul>
    </div>
  </div>
</template>
