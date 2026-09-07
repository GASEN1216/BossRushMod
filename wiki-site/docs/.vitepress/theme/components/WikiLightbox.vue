<script setup lang="ts">
/**
 * WikiLightbox — 点开正文里的配图看大图。
 *
 * 覆盖三类**带说明文字的配图块**（sync-content.mjs 的 renderImageBlock 生成）：
 * 图鉴立绘墙 `.brs-gallery`、章节海报 `.brs-figure`、建筑物品图标 `.brs-icon`，
 * 外加速查框顶上那枚大图（Boss 页那枚就是 320px 的图鉴立绘，缩略成 46px 太可惜）。
 *
 * **不覆盖**正文里的实体链接小图标（`.brs-eref__icon`）与导航图标：
 * 那些是 20 像素上下的功能性贴图，本身没有更多细节，而且点它们应该跳转不是弹窗。
 *
 * 尺寸规则：**永不超过原始像素**。
 *   这是本组件唯一一条硬规则。放大到超过原图只会得到一张更大的糊图，
 *   而「有些太糊」正是要修的问题——症结在产物尺寸，不在展示尺寸
 *   （建筑图标此前产物 128px 却按 205px 显示，一直在被拉伸；
 *   已在 tools/build_wiki_images.py 把口径提上去了）。
 *   所以这里按 min(原始尺寸, 视口余量) 展示，宁可小一点也要清楚。
 *
 * 交互：点图打开、Esc / 点背景 / 点关闭键收起、同一个块内左右键翻页。
 * 图片本身补了 tabindex 与 role，键盘用户可以 Tab 过去回车打开。
 *
 * 三条和 WikiRefPreview 同源的约束：
 *   - **不要用 `<Transition>`**：Vue 的过渡靠双 rAF 推进，标签页切到后台 rAF 停摆，
 *     离场动画走不完，遮罩会永远糊在屏幕上。淡入用元素自己的 CSS animation，
 *     `v-if` 一变 false 立刻消失。
 *   - 路由切换立刻关掉，否则翻页之后还挂着上一页的图。
 *   - `prefers-reduced-motion` 由 style.css 的全局规则统一关掉动画。
 */
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { onContentUpdated, useRouter } from 'vitepress'
import { useScrollLock } from '@vueuse/core'
import { useUiText } from '../composables/useUiText'

const ui = useUiText()

type Shot = { src: string; alt: string; caption: string; w: number; h: number }

/** 哪些图可以点开。选择器写在一处，enhance 与点击判定共用。 */
const SELECTOR =
  '.mw-parser-output .brs-gallery img, .mw-parser-output .brs-figure img, ' +
  '.mw-parser-output .brs-icon img, .infobox .section.images img'

const shots = ref<Shot[]>([])
const index = ref(0)
const open = ref(false)
const locked = useScrollLock(typeof document !== 'undefined' ? document.body : null)

let opener: HTMLElement | null = null

const current = computed<Shot | null>(() => shots.value[index.value] ?? null)

/** 「3 / 37 · 512 × 512」。拼成一个字符串，免得模板里的换行变成多余空格。 */
const metaText = computed(() => {
  const s = current.value
  if (!s) return ''
  const parts: string[] = []
  if (shots.value.length > 1) parts.push(`${index.value + 1} / ${shots.value.length}`)
  if (s.w && s.h) parts.push(`${s.w} × ${s.h}`)
  return parts.join(' · ')
})

/**
 * 展示尺寸：原图与视口余量取小。
 * 视口留白按短边算，横图竖图都不会顶到边。
 */
const frameStyle = computed(() => {
  const s = current.value
  if (!s) return {}
  // 尺寸还没拿到（理论上 show() 已经等过了，这里是解码失败的兜底）：按视口上限走
  if (!s.w || !s.h) return { width: 'min(92vw, 640px)' }
  return {
    width: `min(${s.w}px, 92vw, ${((s.w / s.h) * 78).toFixed(2)}vh)`,
  }
})

function captionOf(img: HTMLImageElement): string {
  // 配图块的结构是 <p><img><em>说明</em></p>，说明就在同一个 <p> 里
  const em = img.parentElement?.querySelector('em')
  if (em?.textContent) return em.textContent.trim()
  return img.getAttribute('alt') || ''
}

function shotOf(img: HTMLImageElement): Shot {
  return {
    src: img.currentSrc || img.src,
    alt: img.getAttribute('alt') || '',
    caption: captionOf(img),
    // 0 = 还不知道原始尺寸。正文配图是懒加载的（config.mts 给 markdown 图片补了
    // loading=lazy），没进过视口的那些 naturalWidth 就是 0——**不能**退回用
    // 渲染尺寸兜底，那会把灯箱按缩略图的 143px 打开。改由 show() 先解码再切。
    w: img.naturalWidth,
    h: img.naturalHeight,
  }
}

/** 同一个配图块里的图算一组，左右键在组内翻；速查框那枚自成一组。 */
function groupOf(img: HTMLImageElement): HTMLImageElement[] {
  const block = img.closest('.brs-gallery, .brs-figure, .brs-icon')
  if (!block) return [img]
  return [...block.querySelectorAll('img')] as HTMLImageElement[]
}

/**
 * 切到第 i 张：先把原始尺寸拿准再换。
 *
 * 已经加载过的图（比如刚点的那张）decode() 立刻就 resolve，看不出等待；
 * 还没懒加载的图会等一次小请求，换来的是「打开即是正确尺寸」，
 * 不会先按错误尺寸铺一帧再跳。token 防抖：连按方向键时只有最后一次算数。
 */
let showToken = 0
async function show(i: number) {
  const shot = shots.value[i]
  if (!shot) return
  const token = ++showToken
  if (!shot.w || !shot.h) {
    const probe = new Image()
    probe.src = shot.src
    try {
      await probe.decode()
    } catch {
      /* 解码失败就按视口上限展示，总比不显示强 */
    }
    if (token !== showToken) return
    shot.w = probe.naturalWidth || 0
    shot.h = probe.naturalHeight || 0
  }
  index.value = i
}

/**
 * 兜底：弹层里那张图自己加载完时把原始尺寸回写。
 *
 * show() 里的 decode() 探针**通常**已经拿到了尺寸，但那条路依赖 decode 的实现
 * （点击发生在缩略图还在下载的那一刻时，实测有拿不到的情况）。这里不猜，
 * 以真正渲染出来的那张图为准——它 load 完必然有 naturalWidth。
 */
function onImgLoad(event: Event) {
  const el = event.target as HTMLImageElement
  const shot = current.value
  if (!shot || !el.naturalWidth) return
  if (shot.w !== el.naturalWidth || shot.h !== el.naturalHeight) {
    shot.w = el.naturalWidth
    shot.h = el.naturalHeight
  }
}

function openAt(img: HTMLImageElement) {
  const group = groupOf(img)
  shots.value = group.map(shotOf)
  opener = img
  open.value = true
  show(Math.max(0, group.indexOf(img)))
}

function close() {
  open.value = false
  shots.value = []
  // 焦点还给刚才那张图，键盘用户不会被丢回页首
  opener?.focus?.()
  opener = null
}

function step(delta: number) {
  if (shots.value.length < 2) return
  show((index.value + delta + shots.value.length) % shots.value.length)
}

/** 给可点的图补上可聚焦与语义标记；客户端路由换页后要重跑。 */
function enhance() {
  document.querySelectorAll<HTMLImageElement>(SELECTOR).forEach((img) => {
    if (img.dataset.brsZoom) return
    img.dataset.brsZoom = '1'
    img.setAttribute('tabindex', '0')
    img.setAttribute('role', 'button')
  })
}

function onClick(event: MouseEvent) {
  const el = event.target as HTMLElement | null
  if (!el || el.tagName !== 'IMG' || !(el as HTMLImageElement).dataset.brsZoom) return
  event.preventDefault()
  openAt(el as HTMLImageElement)
}

function onKeydown(event: KeyboardEvent) {
  if (open.value) {
    if (event.key === 'Escape') return close()
    if (event.key === 'ArrowLeft') return step(-1)
    if (event.key === 'ArrowRight') return step(1)
    return
  }
  if (event.key !== 'Enter' && event.key !== ' ') return
  const el = document.activeElement as HTMLElement | null
  if (!el || el.tagName !== 'IMG' || !(el as HTMLImageElement).dataset.brsZoom) return
  event.preventDefault()
  openAt(el as HTMLImageElement)
}

watch(open, (v) => {
  locked.value = v
})

const router = useRouter()
let stopRouter: (() => void) | null = null

onMounted(() => {
  enhance()
  document.addEventListener('click', onClick)
  document.addEventListener('keydown', onKeydown)
  // 站内跳转后这张图已经不在页上了，灯箱必须跟着关。
  // 补丁挂在 onMounted 而不是 setup 里：setup 在 SSR 阶段也会跑，
  // 那时候改的是服务端那个 router 对象，等于每渲染一页就套一层。
  const prev = router.onAfterRouteChange
  router.onAfterRouteChange = (...args) => {
    if (open.value) close()
    prev?.(...args)
  }
  stopRouter = () => {
    router.onAfterRouteChange = prev
  }
})

onContentUpdated(enhance)

onBeforeUnmount(() => {
  document.removeEventListener('click', onClick)
  document.removeEventListener('keydown', onKeydown)
  locked.value = false
  stopRouter?.()
})
</script>

<template>
  <div
    v-if="open && current"
    class="mw-mmv-wrapper"
    role="dialog"
    aria-modal="true"
    :aria-label="current.caption || current.alt"
    @click="close"
  >
    <button class="mw-mmv-close" type="button" :aria-label="ui.close" @click.stop="close">
      <span class="tw-icon tw-icon--x" aria-hidden="true" />
    </button>

    <button
      v-if="shots.length > 1"
      class="mw-mmv-prev-image"
      type="button"
      :aria-label="ui.prevImage"
      @click.stop="step(-1)"
    >
      <span class="tw-icon tw-icon--chevron-left" aria-hidden="true" />
    </button>

    <figure class="mw-mmv-main" @click.stop>
      <div class="mw-mmv-image" :style="frameStyle">
        <img
          :src="current.src"
          :alt="current.alt"
          :width="current.w || undefined"
          :height="current.h || undefined"
          @load="onImgLoad"
        />
      </div>
      <figcaption class="mw-mmv-post-image">
        <span class="mw-mmv-title-para">{{ current.caption || current.alt }}</span>
        <span class="mw-mmv-image-metadata">{{ metaText }}</span>
      </figcaption>
    </figure>

    <button
      v-if="shots.length > 1"
      class="mw-mmv-next-image"
      type="button"
      :aria-label="ui.nextImage"
      @click.stop="step(1)"
    >
      <span class="tw-icon tw-icon--chevron-right" aria-hidden="true" />
    </button>
  </div>
</template>
