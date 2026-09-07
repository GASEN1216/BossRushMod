/**
 * useSkinTheme.ts — 皮肤切换。
 *
 * 机制照抄目标站：偏好存 localStorage 的 `skin-theme`，<html> 上挂
 * `theme-<Name>` 与 `view-dark|view-light`，深色的再顺带挂一个 `.dark`
 * （VitePress 的代码高亮按这个类切 --shiki-dark，留着比自己再写一套省事）。
 *
 * **首帧的类不是这里写的**：config.mts 的 head 里有一段自包含内联脚本
 * （id="skin-theme-init"）在 CSS 之前同步跑完，所以刷新不会先闪一下默认色。
 * 本模块只管「读者按下切换之后」。config.mts 因此也设了 appearance: false ——
 * 否则 VitePress 自己那段 check-dark-mode 会跟我们抢 .dark。
 *
 * 截图 / 分享时可以用 ?skin-theme=Snow 强制某套皮肤，内联脚本认这个查询串。
 */
import { onMounted, ref } from 'vue'
import { DEFAULT_THEME, SKIN_THEME_KEY, THEMES } from '../../data/themes.mts'

const current = ref(DEFAULT_THEME)

function viewOf(name: string): 'dark' | 'light' {
  return THEMES.find((t) => t.name === name)?.view ?? 'dark'
}

function apply(name: string) {
  if (typeof document === 'undefined') return
  const root = document.documentElement
  for (const theme of THEMES) {
    root.classList.remove('theme-' + theme.name)
  }
  root.classList.remove('view-dark', 'view-light')
  const view = viewOf(name)
  root.classList.add('theme-' + name, 'view-' + view)
  root.classList.toggle('dark', view === 'dark')

  // 地址栏配色跟着走。取值从令牌里读，免得和 tokens.css 各写一份再漂掉。
  const meta = document.querySelector('meta[name="theme-color"]')
  if (meta) {
    const color = getComputedStyle(root).getPropertyValue('--theme-meta-color').trim()
    if (color) meta.setAttribute('content', color)
  }
}

export function useSkinTheme() {
  onMounted(() => {
    // 内联脚本已经把类挂好了，这里只是把 ref 对齐到实际生效的那套
    const stored = document.documentElement.className.match(/(?:^|\s)theme-([\w-]+)/)
    current.value = stored?.[1] ?? DEFAULT_THEME
  })

  function set(name: string) {
    if (!THEMES.some((t) => t.name === name)) return
    current.value = name
    apply(name)
    try {
      localStorage.setItem(SKIN_THEME_KEY, name)
    } catch {
      // 隐私模式下写不进去：这一次切换照样生效，只是记不住
    }
  }

  return { current, themes: THEMES, set }
}
