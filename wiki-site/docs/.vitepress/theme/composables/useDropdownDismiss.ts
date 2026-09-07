import { ref } from 'vue'

/**
 * 让 `:focus-within` 开合的下拉能被 Esc 关掉。
 *
 * 版式照抄的是 Vector-legacy：下拉纯靠 CSS 的 `:hover` / `:focus-within` 开合，
 * 没有一行 JS。好处是零状态、SSR 就位、鼠标用户体验和目标站一模一样；
 * 代价是**键盘用户按 Esc 关不掉**——焦点还在里面，`:focus-within` 就还是真。
 *
 * 直接 blur 掉能关，但那样焦点回到 body，读者得从头 Tab 一遍，比不关还糟。
 * 所以做法是：Esc 时把焦点送回触发按钮（符合 WAI-ARIA 对菜单的期待），
 * 同时挂一个 `is-dismissed`——CSS 用它盖过 `:focus-within`。
 * 焦点真正离开这个下拉时再把标记清掉，否则下次 Tab 进来会打不开。
 *
 * 用法（两个下拉共用，别各写一份）：
 *
 *   const dd = useDropdownDismiss()
 *   <nav class="vector-menu-dropdown" :class="{ 'is-dismissed': dd.dismissed.value }"
 *        @keydown.esc="dd.dismiss" @focusout="dd.onFocusOut">
 *     <button ref="dd.trigger" class="vector-menu-heading">…</button>
 */
export function useDropdownDismiss() {
  const dismissed = ref(false)
  const trigger = ref<HTMLElement | null>(null)

  function dismiss(event: KeyboardEvent) {
    // 菜单没展开时 Esc 不该被吃掉——搜索框、灯箱都还等着这个键
    if (!(event.currentTarget as HTMLElement)?.contains(document.activeElement)) return
    event.stopPropagation()
    dismissed.value = true
    trigger.value?.focus()
  }

  /**
   * focusout 的 relatedTarget 是**接下来**拿到焦点的元素。它还在下拉里面
   * （比如从触发按钮 Tab 到第一项）就不算离开，清早了会把 Esc 的效果抹掉。
   */
  function onFocusOut(event: FocusEvent) {
    const root = event.currentTarget as HTMLElement
    const next = event.relatedTarget as Node | null
    if (next && root.contains(next)) return
    dismissed.value = false
  }

  /** 鼠标点触发按钮时清掉标记：按过 Esc 之后还能用鼠标正常打开。 */
  function reopen() {
    dismissed.value = false
  }

  return { dismissed, trigger, dismiss, onFocusOut, reopen }
}
