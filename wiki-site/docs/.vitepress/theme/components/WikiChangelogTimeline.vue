<script setup lang="ts">
/**
 * WikiChangelogTimeline — 更新日志的版本时间线。
 *
 * 四十多个版本页在侧栏里是一根长柱子，页尾同类导航（WikiNavbox）铺开也是一行
 * 四十多个芯片，都没法一眼看出「2.1.x 这一系列有多少个补丁、我该从哪一个看起」。
 * 这里按 major.minor 分行：一行一个小版本系列，行内是各补丁版本，最新的一个带标。
 * 更新日志的所有页面（总览 + 每个版本页）都挂它，当前页高亮，代替 navbox。
 *
 * 数据来自 changelog.data.mts（构建期读 catalog.tsv），这里不维护任何清单。
 */
import { computed } from 'vue'
import { useWiki } from '../composables/useWiki'
import { data as releases } from '../../data/changelog.data.mts'

const { t, href, canonical } = useWiki()

const series = computed(() => {
  const rows = new Map<string, typeof releases>()
  for (const release of releases) {
    const key = release.version.split('.').slice(0, 2).join('.') + '.x'
    if (!rows.has(key)) rows.set(key, [])
    rows.get(key)!.push(release)
  }
  return [...rows.entries()].map(([key, items]) => ({ key, items }))
})

const latest = computed(() => releases[0]?.version)

const isHere = (path: string) => path.replace(/\/$/, '') === canonical.value.replace(/\/$/, '')
</script>

<template>
  <nav v-if="releases.length" class="wiki-timeline" :aria-label="t('版本时间线', 'Release timeline')">
    <h2 class="wiki-timeline__title">
      {{ t('版本时间线', 'Release timeline') }}
      <span class="wiki-timeline__count">{{ releases.length }}</span>
    </h2>
    <ol class="wiki-timeline__list">
      <li v-for="s in series" :key="s.key" class="wiki-timeline__row">
        <span class="wiki-timeline__series">
          {{ s.key }}
          <small>{{ s.items.length }}</small>
        </span>
        <ul class="wiki-timeline__chips">
          <li v-for="release in s.items" :key="release.version">
            <a
              :href="href(release.path)"
              :class="{ 'is-here': isHere(release.path), 'is-latest': release.version === latest }"
              :aria-current="isHere(release.path) ? 'page' : undefined"
            >
              v{{ release.version }}
              <span v-if="release.version === latest" class="wiki-timeline__latest">
                {{ t('最新', 'latest') }}
              </span>
            </a>
          </li>
        </ul>
      </li>
    </ol>
  </nav>
</template>
