<script setup lang="ts">
/**
 * WikiNotFound — 404。
 *
 * 自定义主题不继承默认主题，那份 NotFound.vue 也就不再接管路由落空的情况，
 * 得自己渲染一页。文案仍从 themeConfig.notFound 取（config.mts 里那段中文没动）。
 */
import { computed } from 'vue'
import { useData } from 'vitepress'
import { useUiText } from '../composables/useUiText'
import { useWiki } from '../composables/useWiki'

const ui = useUiText()
const { theme } = useData()
const { href } = useWiki()

const nf = computed(() => theme.value.notFound ?? {})
</script>

<template>
  <div id="bodyContent" class="vector-body">
    <div id="mw-content-text" class="mw-body-content">
      <div class="mw-parser-output">
        <h1 class="firstHeading">
          {{ nf.code || '404' }} · {{ nf.title || 'This page does not exist' }}
        </h1>
        <div class="message-box msgbox-color-red">
          <div class="icon"><span class="tw-icon tw-icon--alert" aria-hidden="true" /></div>
          <div class="msgbox-text">
            {{ nf.quote || '' }}
          </div>
        </div>
        <p>
          <a :href="href('/')">{{ nf.linkText || ui.backHome }}</a>
        </p>
      </div>
    </div>
  </div>
</template>
