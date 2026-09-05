<script setup lang="ts">
/**
 * WikiIcon — 条目图标，缺图时退化成首字母字牌。
 *
 * 结构先行、图标后补：structure.mts 里可以先写下 icon key 再去出图，
 * 这期间页面必须照常好看，不能出现裂图或空洞。所以 src 取不到时不渲染 <img>，
 * 改用一枚同尺寸的字牌（中文取首字，英文取首字母），底色按 key 稳定散列，
 * 同一个条目在任何页面上都是同一个颜色。
 */
import { computed } from 'vue'
import { iconUrl } from '../composables/useWiki'

const props = withDefaults(
  defineProps<{
    icon?: string
    label: string
    /** 边长，px */
    size?: number
  }>(),
  { size: 40 }
)

const src = computed(() => iconUrl(props.icon))

/** 字牌上的一两个字：中文取首字，拉丁取首字母。 */
const initial = computed(() => {
  const s = (props.label || '').trim()
  if (!s) return '?'
  return /[一-龥]/.test(s[0]) ? s[0] : s[0].toUpperCase()
})

/** 按 key 稳定散列出色相，保证同一条目到处同色（而不是随机或按渲染顺序）。 */
const hue = computed(() => {
  const seed = props.icon || props.label || ''
  let h = 0
  for (let i = 0; i < seed.length; i++) h = (h * 31 + seed.charCodeAt(i)) % 360
  return h
})
</script>

<template>
  <img
    v-if="src"
    class="wiki-icon"
    :src="src"
    :alt="label"
    :width="size"
    :height="size"
    loading="lazy"
    decoding="async"
  />
  <span
    v-else
    class="wiki-icon wiki-icon--fallback"
    :style="{
      width: size + 'px',
      height: size + 'px',
      fontSize: Math.round(size * 0.42) + 'px',
      '--wiki-icon-hue': hue,
    }"
    :aria-hidden="true"
    >{{ initial }}</span
  >
</template>

<style>
.wiki-icon {
  display: block;
  flex: none;
  object-fit: contain;
  border: 1px solid var(--brs-rule-soft);
  background: var(--brs-sunk);
  padding: 3px;
  /* 有图那支靠 width/height 属性定尺寸、还带 padding + border，
     字牌那支只有 width/height——不统一到 border-box，同一行里
     有图的会比字牌大 8px，正好是「有的对齐有的不对齐」那种别扭。 */
  box-sizing: border-box;
}

.wiki-icon--fallback {
  display: grid;
  place-items: center;
  padding: 0;
  font-family: var(--brs-serif);
  font-weight: 700;
  line-height: 1;
  color: hsl(var(--wiki-icon-hue) 34% 32%);
  background: hsl(var(--wiki-icon-hue) 28% 88%);
  border-color: hsl(var(--wiki-icon-hue) 22% 74%);
}

.dark .wiki-icon--fallback {
  color: hsl(var(--wiki-icon-hue) 42% 74%);
  background: hsl(var(--wiki-icon-hue) 20% 18%);
  border-color: hsl(var(--wiki-icon-hue) 18% 30%);
}
</style>
