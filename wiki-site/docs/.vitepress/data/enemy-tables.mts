/**
 * enemy-tables.mts — 抗性表插在哪一页的哪一节。
 *
 * 表的数据只有 enemy-stats.json 一份（tools/build_wiki_enemy_stats.py 从代码与官方快照生成）；
 * 这里只管摆放：config.mts 的 enemyStatsSlotPlugin 在渲染期找到 `heading` 那一节，
 * 把 `<WikiEnemyTable group="…" />` 插在这一节的末尾（`at: 'start'` 则紧跟标题）。
 * 和速查框、实体链接同一套办法——sync 生成的 .md 一个字节不变
 * （丧尸模式页被 ZombieModeMutantWikiGuard 逐字节比对）。
 *
 * 用小节标题文字定位，不用序号：几张地图页的中英小节并不一一对应。
 * 标题改名时表会悄悄消失，所以 tests/WikiEnemyStatsGuard.py 核对每个标题在中英两版里都在。
 */
export interface EnemyTablePlacement {
  /** structure.mts 里的规范路径（不带语言前缀） */
  page: string
  /** 小节标题原文（WikiContent 里 ### 后面那段） */
  zh: string
  en: string
  /** enemy-stats.json 里 groups[].id */
  group: string
  /** 只列这几位；缺省列整组 */
  ids?: string[]
  at?: 'start' | 'end'
}

export const ENEMY_TABLES: EnemyTablePlacement[] = [
  { page: '/bosses/', zh: '抗性与弱点', en: 'Resistances and Weak Spots', group: 'custom-bosses' },
  { page: '/maps/sky-island', zh: '岛上的敌人', en: "Who you'll fight", group: 'sky-island-bosses' },
  { page: '/maps/sky-island', zh: '岛上的敌人', en: "Who you'll fight", group: 'sky-island-pool' },
  { page: '/maps/tidegate', zh: '搜刮与敌人', en: 'Loot and enemies', group: 'tidegate' },
  { page: '/maps/last-shift-yard', zh: '小队长', en: 'Squad leaders', group: 'last-shift' },
  { page: '/game-modes/zombie-mode', zh: '敌人体系', en: 'Enemy Types', group: 'zombie', at: 'start' },
  { page: '/systems/field-adventures', zh: '贪吃宝箱怪', en: 'Gluttonous Mimic', group: 'mimic' },
  { page: '/systems/campaign', zh: '终章决战怎么打', en: 'How the final showdown works', group: 'custom-bosses', ids: ['sandstorm-champion'] },
]
