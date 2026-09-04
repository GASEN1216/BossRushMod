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
        { text: '宿命回响', link: '/game-modes/mode-g' },
        { text: '末日丧尸模式', link: '/game-modes/zombie-mode' },
        { text: '百战留痕（黑市鸭王杯）', link: '/game-modes/mode-h' },
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
        { text: '幽灵女巫', link: '/bosses/phantom-witch' },
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
        { text: '噬魂挽歌', link: '/equipment/phantom-scythe' },
        { text: '龙裔套装', link: '/equipment/dragon-set' },
        { text: '龙王套装', link: '/equipment/dragon-king-set' },
        { text: '腾云驾雾图腾', link: '/equipment/flight-totem' },
        { text: '逆鳞', link: '/equipment/reverse-scale' },
        { text: '焚皇断界戟', link: '/equipment/halberd' },
        { text: '龙息', link: '/equipment/dragon-breath' },
        { text: '焚天龙铳', link: '/equipment/dragon-cannon' },
        { text: '霜之哀伤', link: '/equipment/frostmourne' },
        { text: '毒蛇匕首', link: '/equipment/viper-dagger' },
        { text: '召唤法杖', link: '/equipment/summon-staff' },
        { text: '能量盾', link: '/equipment/energy-shield' },
        { text: '冰霜长矛', link: '/equipment/frost-spear' },
        { text: '雷电戒指', link: '/equipment/thunder-ring' },
        { text: '霜冠套装', link: '/equipment/frost-set' },
        { text: '雷神套装', link: '/equipment/thunder-set' },
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
        { text: '死亡亡魂', link: '/systems/death-wraith' },
        { text: '布满了灰尘的星愿许愿台', link: '/systems/starwish-fountain' },
        { text: '重铸系统', link: '/systems/reforge' },
        { text: 'Boss 筛选器', link: '/systems/boss-filter' },
        { text: '变异词条系统', link: '/systems/mutators' },
        { text: '遗种巢', link: '/systems/petnest' },
        { text: '鸭科夫日报', link: '/systems/daily-report' },
        { text: '鸭皇图鉴', link: '/systems/codex' },
        { text: '局内随机事件', link: '/systems/random-events' },
        { text: '词缀锻造', link: '/systems/affix-forge' },
        { text: '鸭王征程', link: '/systems/campaign' },
        { text: '竞技场后山', link: '/systems/arena-backyard' },
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
        { text: '宿命回响攻略', link: '/guides/mode-g-strategy' },
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
        { text: 'Fate Echo', link: '/en/game-modes/mode-g' },
        { text: 'Zombie Mode', link: '/en/game-modes/zombie-mode' },
        { text: 'Black Market Duck Cup', link: '/en/game-modes/mode-h' },
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
        { text: 'Phantom Witch', link: '/en/bosses/phantom-witch' },
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
        { text: "Soulreaper's Requiem", link: '/en/equipment/phantom-scythe' },
        { text: 'Dragon Set', link: '/en/equipment/dragon-set' },
        { text: 'Dragon King Set', link: '/en/equipment/dragon-king-set' },
        { text: 'Cloud Rider Totem', link: '/en/equipment/flight-totem' },
        { text: 'Reverse Scale', link: '/en/equipment/reverse-scale' },
        { text: 'Skyburner Halberd', link: '/en/equipment/halberd' },
        { text: 'Dragon Breath', link: '/en/equipment/dragon-breath' },
        { text: 'Dragon Cannon', link: '/en/equipment/dragon-cannon' },
        { text: 'Frostmourne', link: '/en/equipment/frostmourne' },
        { text: 'Viper Dagger', link: '/en/equipment/viper-dagger' },
        { text: 'Summoning Staff', link: '/en/equipment/summon-staff' },
        { text: 'Energy Shield', link: '/en/equipment/energy-shield' },
        { text: 'Frost Spear', link: '/en/equipment/frost-spear' },
        { text: 'Thunder Ring', link: '/en/equipment/thunder-ring' },
        { text: 'Frost Set', link: '/en/equipment/frost-set' },
        { text: 'Thunder Set', link: '/en/equipment/thunder-set' },
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
        { text: 'Death Wraith', link: '/en/systems/death-wraith' },
        { text: 'Dust-Covered StarWish Fountain', link: '/en/systems/starwish-fountain' },
        { text: 'Reforge System', link: '/en/systems/reforge' },
        { text: 'Boss Filter', link: '/en/systems/boss-filter' },
        { text: 'Mutator System', link: '/en/systems/mutators' },
        { text: 'PetNest', link: '/en/systems/petnest' },
        { text: 'The Duckov Daily', link: '/en/systems/daily-report' },
        { text: 'Duck King Codex', link: '/en/systems/codex' },
        { text: 'Random Events', link: '/en/systems/random-events' },
        { text: 'Affix Forging', link: '/en/systems/affix-forge' },
        { text: 'Duck King Campaign', link: '/en/systems/campaign' },
        { text: 'Arena Backyard', link: '/en/systems/arena-backyard' },
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
        { text: 'Fate Echo Guide', link: '/en/guides/mode-g-strategy' },
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

  markdown: {
    // 表格外套一层横向滚动框：窄屏下宽表格自己滚，不撑破版心。
    // 对应档案报告里的 .tw 包裹层，样式见 theme/style.css §7。
    config(md) {
      md.renderer.rules.table_open = () => '<div class="brs-table-scroll"><table>'
      md.renderer.rules.table_close = () => '</table></div>'
    },
  },

  head: [
    ['link', { rel: 'icon', href: `${base}images/favicon.ico` }],
    // 档案版式字体：衬线标题 + 正文黑体 + 等宽微标签，与玩法档案报告同源。
    // 用 media=print + onload 切换，避免 fonts.googleapis.com 不可达时阻塞首屏；
    // 取不到时按 style.css 里的本地字体栈降级（苹方 / 微软雅黑 / 宋体）。
    ['link', { rel: 'preconnect', href: 'https://fonts.googleapis.com' }],
    ['link', { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: '' }],
    [
      'link',
      {
        rel: 'stylesheet',
        href: 'https://fonts.googleapis.com/css2?family=Noto+Serif+SC:wght@500;700;900&family=Noto+Sans+SC:wght@300;400;500;700&family=IBM+Plex+Mono:wght@400;500;600&display=swap',
        media: 'print',
        onload: "this.media='all'",
      },
    ],
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
