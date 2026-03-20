import { readFileSync } from 'fs'
import { dirname, resolve } from 'path'
import { fileURLToPath } from 'url'
import { defineConfig } from 'vitepress'

const __dirname = dirname(fileURLToPath(import.meta.url))
const CATALOG_PATH = resolve(__dirname, '..', '..', '..', 'WikiContent', 'catalog.tsv')

function getChangelogLink(entryId: string, prefix: string) {
  if (entryId === 'changelog__highlights') return `${prefix}/changelog/`
  if (entryId === 'changelog__legacy_archive') return `${prefix}/changelog/legacy-archive`

  const versionMatch = entryId.match(/^changelog__v(\d+)_(\d+)_(\d+)$/)
  if (!versionMatch) return null

  return `${prefix}/changelog/v${versionMatch[1]}.${versionMatch[2]}.${versionMatch[3]}`
}

function getChangelogItems(locale: 'zh' | 'en') {
  const prefix = locale === 'en' ? '/en' : ''
  const raw = readFileSync(CATALOG_PATH, 'utf-8')
  const lines = raw.trim().split('\n').slice(1)

  return lines
    .map((line) => line.split('\t'))
    .filter((cols) => cols.length >= 5 && cols[0] === 'changelog')
    .map((cols) => ({
      entryId: cols[1],
      text: locale === 'en' ? cols[3] : cols[2],
      order: Number(cols[4]),
    }))
    .sort((a, b) => a.order - b.order)
    .map((entry) => {
      const link = getChangelogLink(entry.entryId, prefix)
      if (!link) return null

      return {
        text: entry.text,
        link,
      }
    })
    .filter((entry): entry is { text: string; link: string } => entry !== null)
}

// ── 侧边栏定义 ───────────────────────────────────────────
// 以 docs/wiki-site/ 的实际文件结构为准

function sidebarZh() {
  return [
    {
      text: '入门',
      items: [
        { text: 'Mod 简介', link: '/getting-started/overview' },
        { text: '安装与启用', link: '/getting-started/installation' },
        { text: '新手上路', link: '/getting-started/first-steps' },
      ],
    },
    {
      text: '游戏模式',
      items: [
        { text: '模式总览', link: '/game-modes/' },
        { text: '标准 BossRush', link: '/game-modes/standard' },
        { text: '无间炼狱', link: '/game-modes/infinite-hell' },
        { text: '白手起家', link: '/game-modes/mode-d' },
        { text: '划地为营', link: '/game-modes/mode-e' },
        { text: '血猎追击', link: '/game-modes/mode-f' },
      ],
    },
    {
      text: '地图',
      items: [
        { text: '地图总览', link: '/maps/' },
      ],
    },
    {
      text: 'Boss',
      items: [
        { text: 'Boss 总览', link: '/bosses/' },
        { text: '龙裔遗族', link: '/bosses/dragon-descendant' },
        { text: '焚天龙皇', link: '/bosses/dragon-king' },
      ],
    },
    {
      text: 'NPC',
      items: [
        { text: 'NPC 总览', link: '/npcs/' },
        { text: '叮当（哥布林工匠）', link: '/npcs/goblin' },
        { text: '羽织（护士）', link: '/npcs/nurse' },
        { text: '阿稳（快递员）', link: '/npcs/courier' },
      ],
    },
    {
      text: '装备',
      items: [
        { text: '装备总览', link: '/equipment/' },
        { text: '龙裔套装', link: '/equipment/dragon-set' },
        { text: '龙王套装', link: '/equipment/dragon-king-set' },
        { text: '腾云驾雾图腾', link: '/equipment/flight-totem' },
        { text: '逆鳞', link: '/equipment/reverse-scale' },
        { text: '焚皇断界戟', link: '/equipment/halberd' },
        { text: '龙息', link: '/equipment/dragon-breath' },
        { text: '焚天龙铳', link: '/equipment/dragon-cannon' },
      ],
    },
    {
      text: '物品',
      items: [
        { text: '物品总览', link: '/items/' },
        { text: '入场与功能物品', link: '/items/key-items' },
        { text: 'NPC 相关物品', link: '/items/npc-items' },
        { text: '消耗品', link: '/items/consumables' },
        { text: '模式专属物品', link: '/items/mode-items' },
      ],
    },
    {
      text: '系统',
      items: [
        { text: '掉落与奖励', link: '/systems/loot-rewards' },
        { text: '重铸系统', link: '/systems/reforge' },
        { text: 'Boss 筛选器', link: '/systems/boss-filter' },
        { text: '好感度与婚姻', link: '/systems/affinity-marriage' },
        { text: '配置选项', link: '/systems/configuration' },
      ],
    },
    {
      text: '成就',
      items: [
        { text: '成就大全', link: '/achievements/' },
      ],
    },
    {
      text: '攻略',
      items: [
        { text: '新手推荐路线', link: '/guides/beginner-route' },
        { text: 'Boss 战攻略', link: '/guides/boss-fights' },
        { text: '无间炼狱与白手起家', link: '/guides/hell-and-mode-d' },
        { text: '划地为营攻略', link: '/guides/mode-e-strategy' },
        { text: '血猎追击攻略', link: '/guides/mode-f-strategy' },
      ],
    },
    {
      text: '彩蛋',
      items: [
        { text: '彩蛋', link: '/easter-eggs' },
      ],
    },
    {
      text: '更新日志',
      collapsed: true,
      items: getChangelogItems('zh'),
    },
  ]
}

function sidebarEn() {
  return [
    {
      text: 'Getting Started',
      items: [
        { text: 'Mod Overview', link: '/en/getting-started/overview' },
        { text: 'Installation', link: '/en/getting-started/installation' },
        { text: 'First Steps', link: '/en/getting-started/first-steps' },
      ],
    },
    {
      text: 'Game Modes',
      items: [
        { text: 'Mode Overview', link: '/en/game-modes/' },
        { text: 'Standard BossRush', link: '/en/game-modes/standard' },
        { text: 'Infinite Hell', link: '/en/game-modes/infinite-hell' },
        { text: 'From Scratch', link: '/en/game-modes/mode-d' },
        { text: 'Faction War', link: '/en/game-modes/mode-e' },
        { text: 'Blood Hunt', link: '/en/game-modes/mode-f' },
      ],
    },
    {
      text: 'Maps',
      items: [
        { text: 'Map Overview', link: '/en/maps/' },
      ],
    },
    {
      text: 'Bosses',
      items: [
        { text: 'Boss Overview', link: '/en/bosses/' },
        { text: 'Dragon Descendant', link: '/en/bosses/dragon-descendant' },
        { text: 'Skyburner Dragon Lord', link: '/en/bosses/dragon-king' },
      ],
    },
    {
      text: 'NPCs',
      items: [
        { text: 'NPC Overview', link: '/en/npcs/' },
        { text: 'Dingdang (Goblin Smith)', link: '/en/npcs/goblin' },
        { text: 'Yuori (Nurse)', link: '/en/npcs/nurse' },
        { text: 'Awen (Courier)', link: '/en/npcs/courier' },
      ],
    },
    {
      text: 'Equipment',
      items: [
        { text: 'Equipment Overview', link: '/en/equipment/' },
        { text: 'Dragon Set', link: '/en/equipment/dragon-set' },
        { text: 'Dragon King Set', link: '/en/equipment/dragon-king-set' },
        { text: 'Cloud Rider Totem', link: '/en/equipment/flight-totem' },
        { text: 'Reverse Scale', link: '/en/equipment/reverse-scale' },
        { text: 'Skyburner Halberd', link: '/en/equipment/halberd' },
        { text: 'Dragon Breath', link: '/en/equipment/dragon-breath' },
        { text: 'Dragon Cannon', link: '/en/equipment/dragon-cannon' },
      ],
    },
    {
      text: 'Items',
      items: [
        { text: 'Item Overview', link: '/en/items/' },
        { text: 'Key Items', link: '/en/items/key-items' },
        { text: 'NPC Items', link: '/en/items/npc-items' },
        { text: 'Consumables', link: '/en/items/consumables' },
        { text: 'Mode-Exclusive Items', link: '/en/items/mode-items' },
      ],
    },
    {
      text: 'Systems',
      items: [
        { text: 'Loot & Rewards', link: '/en/systems/loot-rewards' },
        { text: 'Reforge System', link: '/en/systems/reforge' },
        { text: 'Boss Filter', link: '/en/systems/boss-filter' },
        { text: 'Affinity & Marriage', link: '/en/systems/affinity-marriage' },
        { text: 'Configuration', link: '/en/systems/configuration' },
      ],
    },
    {
      text: 'Achievements',
      items: [
        { text: 'Achievement List', link: '/en/achievements/' },
      ],
    },
    {
      text: 'Guides',
      items: [
        { text: 'Beginner Route', link: '/en/guides/beginner-route' },
        { text: 'Boss Fights', link: '/en/guides/boss-fights' },
        { text: 'Infinite Hell & From Scratch', link: '/en/guides/hell-and-mode-d' },
        { text: 'Faction War Guide', link: '/en/guides/mode-e-strategy' },
        { text: 'Blood Hunt Guide', link: '/en/guides/mode-f-strategy' },
      ],
    },
    {
      text: 'Easter Eggs',
      items: [
        { text: 'Easter Eggs', link: '/en/easter-eggs' },
      ],
    },
    {
      text: 'Changelog',
      collapsed: true,
      items: getChangelogItems('en'),
    },
  ]
}

// ── 导出配置 ──────────────────────────────────────────────
const base = process.env.DEPLOY_TARGET === 'cloudflare' ? '/' : '/BossRushMod/'

export default defineConfig({
  title: 'BossRush Wiki',
  description: 'Escape from Duckov — BossRush Mod 百科',
  base,
  cleanUrls: true,

  head: [
    ['link', { rel: 'icon', href: `${base}images/favicon.ico` }],
  ],

  locales: {
    root: {
      label: '中文',
      lang: 'zh-CN',
      themeConfig: {
        nav: [
          { text: '入门', link: '/getting-started/overview' },
          { text: '游戏模式', link: '/game-modes/' },
          { text: 'Boss', link: '/bosses/' },
          { text: '装备', link: '/equipment/' },
          { text: '攻略', link: '/guides/beginner-route' },
          { text: '更新日志', link: '/changelog/' },
        ],
        sidebar: sidebarZh(),
        outline: { label: '本页目录' },
        docFooter: { prev: '上一篇', next: '下一篇' },
        lastUpdated: { text: '最后更新' },
        returnToTopLabel: '返回顶部',
        sidebarMenuLabel: '菜单',
        darkModeSwitchLabel: '深色模式',
      },
    },
    en: {
      label: 'English',
      lang: 'en',
      themeConfig: {
        nav: [
          { text: 'Getting Started', link: '/en/getting-started/overview' },
          { text: 'Game Modes', link: '/en/game-modes/' },
          { text: 'Bosses', link: '/en/bosses/' },
          { text: 'Equipment', link: '/en/equipment/' },
          { text: 'Guides', link: '/en/guides/beginner-route' },
          { text: 'Changelog', link: '/en/changelog/' },
        ],
        sidebar: sidebarEn(),
      },
    },
  },

  themeConfig: {
    search: {
      provider: 'local',
      options: {
        locales: {
          root: {
            translations: {
              button: { buttonText: '搜索', buttonAriaLabel: '搜索' },
              modal: {
                noResultsText: '没有找到结果',
                resetButtonTitle: '清除搜索',
                footer: { selectText: '选择', navigateText: '导航', closeText: '关闭' },
              },
            },
          },
        },
      },
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/GASEN1216/BossRushMod' },
    ],

    footer: {
      message: 'BossRush Mod for Escape from Duckov',
      copyright: '© 2024-2026 BossRush Mod Team',
    },
  },
})
