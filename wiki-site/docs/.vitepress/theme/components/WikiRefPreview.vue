<script setup lang="ts">
/**
 * WikiRefPreview — 悬停/聚焦正文里的实体链接时浮出的速查卡。
 *
 * 泰拉瑞亚 Wiki 上把鼠标停在任何物品链接上，都会浮出那件物品的信息框缩略版：
 * 贴图 + 类型 + 几行关键数据。读者因此**不必离开当前页**就能比较两件装备——
 * 这是它「查阅体验」里最省事的一环，也是我们此前唯一还缺的标志性行为。
 *
 * 数据来源和速查框完全同一份（data/infobox.mts + structure.mts），
 * 所以卡片永远不会和条目页对不上；没配速查框的条目**不弹卡**，
 * 只当普通链接——宁可不弹，也不弹一张空卡。
 *
 * 实现上刻意用事件委托而不是给每个链接挂组件：
 *   全站 200+ 个实体链接，逐个包一层 Vue 组件会把每页的 vnode 数量顶上去，
 *   而它们 99% 的时间只是静态文本。委托到 document 上，代价是一对监听器。
 *
 * 三条边界：
 *   - 只在真正有悬停能力的设备上启用（`hover: hover` + `pointer: fine`）。
 *     触屏上 hover 是「点一下」的副作用，弹卡会挡住用户真正想点的链接。
 *   - 键盘聚焦同样弹，Esc 关掉——不能只有鼠标用户看得到。
 *   - 滚动、路由切换、指针移出立刻收起，不留悬空卡片。
 *
 * 无障碍上标 `aria-hidden`，不标 `role="tooltip"`：
 *   tooltip 这个角色要求有个元素用 aria-describedby 指向它才成立，而卡片是委托生成的、
 *   与触发链接没有绑定关系——只写 role 不写关联，等于给读屏器一个悬空的提示框角色，
 *   是**假的无障碍标记**。这张卡的内容在链接指向的页面上一字不差都有，
 *   所以老实把它标成装饰层：读屏器跳过，视力正常的键盘用户仍然能靠 focus 看到。
 */
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vitepress'
import WikiIcon from './WikiIcon.vue'
import { useWiki } from '../composables/useWiki'
import { INFOBOX } from '../../data/infobox.mts'
import { locate } from '../../data/structure.mts'

const { locale, entryLabel, canonical } = useWiki()
const router = useRouter()

const OPEN_DELAY = 200
const CARD_W = 264
/** 卡片最多列几行——速查框全量搬过来会比正文还高，失去「瞥一眼」的意义 */
const MAX_ROWS = 5

const path = ref<string | null>(null)
const x = ref(0)
const y = ref(0)
const flipUp = ref(false)

let timer: ReturnType<typeof setTimeout> | null = null
let anchor: HTMLElement | null = null

const entry = computed(() => (path.value ? locate(path.value)?.entry ?? null : null))
const box = computed(() => (path.value ? INFOBOX[path.value] ?? null : null))

const rows = computed(() =>
  (box.value?.rows ?? []).slice(0, MAX_ROWS).map((r) => ({
    label: locale.value === 'en' ? r.en : r.zh,
    value: locale.value === 'en' ? r.ve || r.vz : r.vz,
    tier: r.tier,
  }))
)

const more = computed(() => Math.max(0, (box.value?.rows.length ?? 0) - MAX_ROWS))
const visible = computed(() => !!(entry.value && box.value))

function close() {
  if (timer) {
    clearTimeout(timer)
    timer = null
  }
  anchor = null
  path.value = null
}

function place(el: HTMLElement) {
  const r = el.getBoundingClientRect()
  // 右侧放不下就贴右边界；下方放不下就翻到链接上方
  const left = Math.min(Math.max(8, r.left), window.innerWidth - CARD_W - 8)
  const below = window.innerHeight - r.bottom
  flipUp.value = below < 240 && r.top > below
  x.value = left
  y.value = flipUp.value ? window.innerHeight - r.top + 6 : r.bottom + 6
}

function open(el: HTMLElement) {
  const ref = el.getAttribute('data-brs-ref')
  // 没配速查框的条目直接不弹：locate 找不到或 INFOBOX 没有，visible 就是 false
  if (!ref || !INFOBOX[ref]) return
  // 不给当前页自己弹卡。markdown 插件那边已经过滤过自指，但速查框的「相关条目」
  // 是组件直接挂的 data-brs-ref，绕开了那道检查——不变量放在这里才对所有来源都成立。
  if (ref === canonical.value) return
  anchor = el
  place(el)
  path.value = ref
}

function onOver(e: Event) {
  const el = (e.target as HTMLElement | null)?.closest?.('[data-brs-ref]') as HTMLElement | null
  if (!el || el === anchor) return
  if (timer) clearTimeout(timer)
  timer = setTimeout(() => open(el), OPEN_DELAY)
}

function onOut(e: Event) {
  const el = (e.target as HTMLElement | null)?.closest?.('[data-brs-ref]')
  if (el && el === anchor) close()
  else if (el && timer) {
    clearTimeout(timer)
    timer = null
  }
}

function onFocusIn(e: Event) {
  const el = (e.target as HTMLElement | null)?.closest?.('[data-brs-ref]') as HTMLElement | null
  if (el) open(el)
  else if (anchor) close()
}

function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape') close()
}

let stopRouter: (() => void) | null = null

onMounted(() => {
  if (!window.matchMedia('(hover: hover) and (pointer: fine)').matches) return
  document.addEventListener('mouseover', onOver, true)
  document.addEventListener('mouseout', onOut, true)
  document.addEventListener('focusin', onFocusIn, true)
  document.addEventListener('keydown', onKey)
  window.addEventListener('scroll', close, true)
  window.addEventListener('resize', close)
  // 站内跳转后锚点元素已经不存在了，卡片必须跟着消失
  const prev = router.onAfterRouteChange
  router.onAfterRouteChange = (...args) => {
    close()
    prev?.(...args)
  }
  stopRouter = () => {
    router.onAfterRouteChange = prev
  }
})

onBeforeUnmount(() => {
  document.removeEventListener('mouseover', onOver, true)
  document.removeEventListener('mouseout', onOut, true)
  document.removeEventListener('focusin', onFocusIn, true)
  document.removeEventListener('keydown', onKey)
  window.removeEventListener('scroll', close, true)
  window.removeEventListener('resize', close)
  stopRouter?.()
  if (timer) clearTimeout(timer)
})
</script>

<template>
  <!--
    刻意**不用** <Transition>：它靠双 rAF 推进 enter/leave 状态机，而标签页一旦不可见
    （切后台、最小化）rAF 就停摆，离场走不完，卡片会卡在屏幕上不消失。
    淡入改成元素自己的 CSS animation——由合成器跑，与 Vue 的删除时机无关，
    `v-if` 一变 false 节点立刻就没了。
  -->
  <aside
    v-if="visible"
    class="brs-peek"
    aria-hidden="true"
    :style="{
      left: x + 'px',
      [flipUp ? 'bottom' : 'top']: y + 'px',
      width: CARD_W + 'px',
    }"
  >
      <div class="brs-peek__head">
        <WikiIcon :icon="entry!.icon" :label="entryLabel(entry!)" :size="34" />
        <div>
          <p class="brs-peek__eyebrow">
            {{ locale === 'en' ? box!.eyebrowEn : box!.eyebrowZh }}
          </p>
          <p class="brs-peek__name">{{ entryLabel(entry!) }}</p>
        </div>
      </div>

      <dl class="brs-peek__rows">
        <div v-for="row in rows" :key="row.label">
          <dt>{{ row.label }}</dt>
          <dd>
            <span v-if="row.tier" class="wiki-rarity" :data-tier="row.tier">{{ row.value }}</span>
            <template v-else>{{ row.value }}</template>
          </dd>
        </div>
      </dl>

    <p v-if="more" class="brs-peek__more">
      {{ locale === 'en' ? `+${more} more on the page` : `条目页还有 ${more} 项` }}
    </p>
  </aside>
</template>

<style>
.brs-peek {
  position: fixed;
  z-index: 60;
  padding: 11px 13px 12px;
  border: 1px solid var(--brs-rule);
  border-top: 2px solid var(--brs-ink);
  background: var(--brs-surface);
  box-shadow: 0 6px 22px rgb(0 0 0 / 14%);
  pointer-events: none; /* 卡片本身不吃鼠标，否则会挡住它下面的链接 */
}

.brs-peek__head {
  display: flex;
  align-items: center;
  gap: 10px;
  padding-bottom: 9px;
  border-bottom: 1px solid var(--brs-rule);
}

.brs-peek__eyebrow {
  margin: 0;
  font-family: var(--brs-mono);
  font-size: 9.5px;
  font-weight: 600;
  letter-spacing: 0.13em;
  text-transform: uppercase;
  color: var(--brs-brass);
}

.brs-peek__name {
  margin: 2px 0 0;
  font-family: var(--brs-serif);
  font-size: 15px;
  font-weight: 700;
  line-height: 1.25;
  color: var(--brs-ink);
}

.brs-peek__rows {
  margin: 0;
  font-size: 12px;
  line-height: 1.5;
}

.brs-peek__rows > div {
  display: grid;
  grid-template-columns: minmax(52px, auto) 1fr;
  gap: 0 12px;
  align-items: baseline;
  padding: 5px 0;
  border-bottom: 1px solid var(--brs-rule-soft);
}

.brs-peek__rows > div:last-child {
  border-bottom: none;
}

.brs-peek__rows dt,
.brs-peek__rows dd {
  margin: 0;
}

.brs-peek__rows dt {
  font-family: var(--brs-mono);
  font-size: 9.5px;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--brs-ink-faint);
}

.brs-peek__rows dd {
  color: var(--brs-ink);
  text-align: right;
}

.brs-peek__more {
  margin: 8px 0 0;
  font-family: var(--brs-mono);
  font-size: 9.5px;
  letter-spacing: 0.06em;
  color: var(--brs-ink-faint);
}

@keyframes brs-peek-in {
  from {
    opacity: 0;
  }
}

.brs-peek {
  animation: brs-peek-in 0.12s ease;
}

@media (prefers-reduced-motion: reduce) {
  .brs-peek {
    animation: none;
  }
}

@media print {
  .brs-peek {
    display: none;
  }
}
</style>
