/**
 * themes.mts — 可选皮肤清单。
 *
 * 与三处必须对齐（`tests/WikiThemeSwitchGuard.py` 会核对）：
 *   1. config.mts 里 skin-theme-init 内联脚本的主题表；
 *   2. theme/css/tokens.css 里的 html.theme-<Name> 覆盖块（默认主题写在 :root，没有块）；
 *   3. 本文件。
 *
 * 加一套皮肤就是这三步，构件一个字都不用改——这正是把颜色全部收进
 * --theme-* 令牌的意义。
 *
 * `view` 决定 <html> 上跟着挂 view-dark 还是 view-light；深色的那些还会同时
 * 挂 VitePress 的 .dark（代码高亮的 --shiki-dark 认这个类）。
 */
export interface SkinTheme {
  name: string
  view: 'dark' | 'light'
  /** 词表的键（见 theme/composables/useUiText.ts） */
  label: 'themeOverworld' | 'themeSnow'
}

export const THEMES: SkinTheme[] = [
  { name: 'Overworld', view: 'dark', label: 'themeOverworld' },
  { name: 'Snow', view: 'light', label: 'themeSnow' },
]

export const DEFAULT_THEME = 'Overworld'

/** localStorage 的键，和内联脚本里写死的那个必须一致 */
export const SKIN_THEME_KEY = 'skin-theme'
