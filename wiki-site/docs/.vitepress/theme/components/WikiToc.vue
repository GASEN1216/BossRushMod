<script setup lang="ts">
/**
 * WikiToc — 正文里的目录框（MediaWiki 的 #toc）。
 *
 * 换皮之前目录是右侧那条常驻的悬浮列。目标站没有这个东西：目录是**正文的一部分**，
 * 一个收缩到内容宽的方框，摆在导语之后、第一个 h2 之前，可以整块折叠。
 * 好处是正文可用宽度多出两百多像素，右浮的速查框才有地方站。
 *
 * 插入位置由 config.mts 的 tocSlotPlugin 决定（第一个 h2 之前），
 * 和速查框、#contentSub 同一套渲染期注入的办法，生成的 .md 一个字节不变。
 *
 * 只在四个及以上标题时出现——这是 MediaWiki 的老规矩，三条以内的目录
 * 比正文本身还长，纯属噪音。
 */
import { computed, onMounted, ref } from 'vue'
import { useData } from 'vitepress'
import { useUiText } from '../composables/useUiText'

const ui = useUiText()
const { page } = useData()

interface Header {
  level: number
  title: string
  link: string
  children?: Header[]
}

const headers = computed<Header[]>(() => (page.value.headers ?? []) as Header[])

function count(list: Header[]): number {
  return list.reduce((n, h) => n + 1 + count(h.children ?? []), 0)
}

const enough = computed(() => count(headers.value) >= 4)

const collapsed = ref(false)
onMounted(() => {
  try {
    collapsed.value = localStorage.getItem('brs-toc-collapsed') === '1'
  } catch {
    // 隐私模式：目录保持展开
  }
})

function toggle() {
  collapsed.value = !collapsed.value
  try {
    localStorage.setItem('brs-toc-collapsed', collapsed.value ? '1' : '0')
  } catch {
    // 同上，记不住不影响这一次
  }
}
</script>

<template>
  <div v-if="enough" id="toc" class="toc" :class="{ 'is-collapsed': collapsed }" role="navigation">
    <div class="toctitle">
      <h2 id="mw-toc-heading">{{ ui.contents }}</h2>
      <button
        class="toctogglelabel"
        type="button"
        :aria-expanded="!collapsed"
        :aria-label="ui.toggleContents"
        @click="toggle"
      />
    </div>
    <ul>
      <li
        v-for="(h1, i) in headers"
        :key="h1.link"
        class="toclevel-1"
        :class="'tocsection-' + (i + 1)"
      >
        <a :href="h1.link">
          <span class="tocnumber">{{ i + 1 }}</span>
          <span class="toctext">{{ h1.title }}</span>
        </a>
        <ul v-if="h1.children && h1.children.length">
          <li v-for="(h2, j) in h1.children" :key="h2.link" class="toclevel-2">
            <a :href="h2.link">
              <span class="tocnumber">{{ i + 1 }}.{{ j + 1 }}</span>
              <span class="toctext">{{ h2.title }}</span>
            </a>
          </li>
        </ul>
      </li>
    </ul>
  </div>
</template>
