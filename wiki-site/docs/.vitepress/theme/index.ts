import type { Theme } from 'vitepress'
import DefaultTheme from 'vitepress/theme'
import Layout from './Layout.vue'
import WikiHome from './components/WikiHome.vue'
import WikiCardGrid from './components/WikiCardGrid.vue'
import WikiIcon from './components/WikiIcon.vue'
import './style.css'
// 功能构件的样式（对比表 / 时间线 / 首页搜索框 / 打印）单独一份，style.css 只管版式语言
import './extras.css'

export default {
  extends: DefaultTheme,
  Layout,
  enhanceApp({ app }) {
    // 首页 index.md 用 layout: page + <WikiHome />，所以要全局注册。
    // 另外两个是给正文里偶尔要手工插一块宫格 / 图标时准备的。
    app.component('WikiHome', WikiHome)
    app.component('WikiCardGrid', WikiCardGrid)
    app.component('WikiIcon', WikiIcon)
  },
} satisfies Theme
