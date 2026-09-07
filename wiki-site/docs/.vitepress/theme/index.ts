import type { Theme } from 'vitepress'
import Layout from './Layout.vue'
import WikiHome from './components/WikiHome.vue'
import WikiCardGrid from './components/WikiCardGrid.vue'
import WikiInfobox from './components/WikiInfobox.vue'
import WikiContentSub from './components/WikiContentSub.vue'
import WikiToc from './components/WikiToc.vue'
import WikiIcon from './components/WikiIcon.vue'

/* 样式分层，顺序即层叠顺序：令牌 → 重置 → 图标 → 骨架 → 正文 → 构件 → 首页
   → 响应式 → 打印。--vp-* 的桥接单独一份，只服务搜索弹层那个 fork。 */
import './css/tokens.css'
import './css/base.css'
import './css/icons.css'
import './css/vp-bridge.css'
import './css/layout.css'
import './css/content.css'
import './css/widgets.css'
import './css/mainpage.css'
import './css/responsive.css'
import './css/print.css'

export default {
  Layout,
  enhanceApp({ app }) {
    // 首页 index.md 用 layout: page + <WikiHome />
    app.component('WikiHome', WikiHome)
    app.component('WikiCardGrid', WikiCardGrid)
    // 这三个由 config.mts 的 markdown-it 插件在渲染期写进正文（h1 之后 / 首个 h2 之前），
    // 出现在页面组件的模板里而不是 Layout 的作用域内，所以必须全局注册。
    app.component('WikiInfobox', WikiInfobox)
    app.component('WikiContentSub', WikiContentSub)
    app.component('WikiToc', WikiToc)
    app.component('WikiIcon', WikiIcon)
  },
} satisfies Theme
