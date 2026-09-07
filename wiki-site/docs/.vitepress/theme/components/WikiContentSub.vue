<script setup lang="ts">
/**
 * WikiContentSub — 标题底下那两行小字（MediaWiki 的 #siteSub 与 #contentSub）。
 *
 * 取代了原来的面包屑组件。目标站在 h1 之下放的是「来自 XX Wiki」加一行
 * 「‹ 上级页面」的位置提示，而不是一条完整的面包屑链——同样的信息量，
 * 占的高度只有一半，正文能早半屏开始。
 *
 * 由 config.mts 的 contentSubSlotPlugin 在渲染期插到第一个 h1 之后
 * （和速查框同一套办法），所以生成的 .md 一个字节不变。
 */
import { computed } from 'vue'
import { useWiki } from '../composables/useWiki'
import { useUiText } from '../composables/useUiText'

const props = defineProps<{ path?: string }>()
const ui = useUiText()
const { located, href, categoryLabel, canonical } = useWiki()

/** 注入时写死的 path 优先；没有就退回当前路由（手工写进 markdown 时仍可用）。 */
const here = computed(() => props.path || canonical.value)
const category = computed(() => located.value?.category ?? null)
const isHub = computed(
  () => category.value != null && category.value.path.replace(/\/$/, '') === here.value.replace(/\/$/, '')
)
</script>

<template>
  <div class="wiki-contentsub">
    <div id="siteSub">{{ ui.fromWiki }}</div>
    <div id="contentSub">
      <span class="subpages">
        <a :href="href('/')">{{ ui.mainPage }}</a>
        <template v-if="category && !isHub">
          <a :href="href(category.path)">{{ categoryLabel(category) }}</a>
        </template>
      </span>
    </div>
    <div class="mw-content-sub-rule" />
  </div>
</template>
