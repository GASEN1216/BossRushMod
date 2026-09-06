/**
 * infobox.mts — 实体页右上角「速查框」的数据。
 *
 * 为什么单独放一份数据，而不是从正文里解析：
 *   正文的权威源是 WikiContent/，它同时喂**游戏内 Wiki 书**，必须保持纯文本
 *   （见 sync-content.mjs 顶部注释）。速查框是**只服务在线站**的表现层结构，
 *   硬塞进 WikiContent 会污染游戏内那本书；而反过来去正则解析「- 生命值：800」
 *   这类行，等于把版式绑死在某种行文习惯上，作者改一个顿号就静默失效。
 *
 *   所以这里手写一份小表：条目少（不到 30 条）、字段稳定、改动频率低。
 *   数值以 WikiContent/zh 正文为准，改数值时两边一起改——
 *   tests/WikiSiteStructureGuard.py 只校验路径与图标，**不校验数值**，
 *   因为它没法判断哪边才是对的。
 *
 * links 里写路径不写标题：标题从 structure.mts 取，避免两处改名漂移。
 */

export interface InfoboxRow {
  zh: string
  en: string
  /** 值。中英一致时（纯数字、星级）写一个字符串即可 */
  vz: string
  ve?: string
  /**
   * 稀有度档位。写了就把这一行的值渲染成带色的稀有度片
   * （泰拉瑞亚 Wiki 按稀有度给物品名上色，这里落在品质行上）。
   * 显式字段而不是去嗅探「品质」这个标签——标签一改口径就静默失效。
   */
  tier?: number
}

export interface Infobox {
  /** 框顶那行小字：这是个什么东西 */
  eyebrowZh: string
  eyebrowEn: string
  rows: InfoboxRow[]
  /** 相关条目，写 structure.mts 里的规范路径 */
  links?: string[]
}

export const INFOBOX: Record<string, Infobox> = {
  // ── Boss ────────────────────────────────────────────────
  '/bosses/dragon-descendant': {
    eyebrowZh: '自定义 Boss',
    eyebrowEn: 'Custom Boss',
    rows: [
      { zh: '生命值', en: 'Health', vz: '500' },
      { zh: '碰撞伤害', en: 'Contact damage', vz: '20（1.5 米）', ve: '20 (1.5 m)' },
      { zh: '阶段', en: 'Phases', vz: '2（致命伤时假死复活）', ve: '2 (fakes death on lethal hit)' },
      { zh: '免疫', en: 'Immunity', vz: '火焰（受火伤反而回血）', ve: 'Fire (heals from it)' },
      { zh: '持有装备', en: 'Wields', vz: '赤龙首 / 焰鳞甲 / 龙息', ve: 'Dragon set + Dragon Breath' },
    ],
    links: ['/equipment/dragon-set', '/equipment/dragon-breath', '/guides/boss-fights'],
  },
  '/bosses/dragon-king': {
    eyebrowZh: '自定义 Boss · 最终',
    eyebrowEn: 'Custom Boss · Final',
    rows: [
      { zh: '生命值', en: 'Health', vz: '800' },
      { zh: '伤害倍率', en: 'Damage scale', vz: '0.3x' },
      { zh: '碰撞伤害', en: 'Contact damage', vz: '15（1.5 米）', ve: '15 (1.5 m)' },
      { zh: '悬浮高度', en: 'Hover height', vz: '3–5 米（玩家上方）', ve: '3–5 m above player' },
      { zh: '技能数', en: 'Skills', vz: '7' },
      { zh: '免疫', en: 'Immunity', vz: '火焰（受火伤反而回血）', ve: 'Fire (heals from it)' },
    ],
    links: [
      '/equipment/dragon-king-set',
      '/equipment/flight-totem',
      '/equipment/reverse-scale',
      '/equipment/halberd',
      '/equipment/dragon-cannon',
    ],
  },
  '/bosses/phantom-witch': {
    eyebrowZh: '自定义 Boss',
    eyebrowEn: 'Custom Boss',
    rows: [
      { zh: '生命值', en: 'Health', vz: '1000' },
      { zh: '伤害倍率', en: 'Damage scale', vz: '1.1x' },
      { zh: '体型', en: 'Size', vz: '普通幽灵的 2 倍', ve: '2× a normal ghost' },
      { zh: '阶段', en: 'Phases', vz: '3（100% / 60% / 25%）', ve: '3 (100% / 60% / 25%)' },
      { zh: '持有武器', en: 'Wields', vz: '噬魂挽歌', ve: 'Soulreaper Requiem' },
    ],
    links: ['/equipment/phantom-scythe', '/guides/boss-fights'],
  },

  // ── 近战武器 ────────────────────────────────────────────
  '/equipment/phantom-scythe': {
    eyebrowZh: '近战武器 · 幽灵',
    eyebrowEn: 'Melee · Spectral',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '6（史诗）', ve: '6 (Epic)', tier: 6 },
      { zh: '伤害', en: 'Damage', vz: '35.5' },
      { zh: '攻速 / 范围', en: 'Speed / Range', vz: '1.56 / 2.22 米', ve: '1.56 / 2.22 m' },
      { zh: '暴击', en: 'Crit', vz: '5.5% · 1.33x' },
      { zh: '持械移速', en: 'Move speed', vz: '120%（全自定义武器最高）', ve: '120% (highest of any custom weapon)' },
      { zh: '宝石槽', en: 'Gem slots', vz: '2' },
      { zh: '来源', en: 'Source', vz: '幽灵女巫（50% 额外掉落）', ve: 'Phantom Witch (50% bonus drop)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500044' },
    ],
    links: ['/bosses/phantom-witch'],
  },
  '/equipment/halberd': {
    eyebrowZh: '近战武器 · 火焰',
    eyebrowEn: 'Melee · Fire',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '7（传说）', ve: '7 (Legendary)', tier: 7 },
      { zh: '基础伤害', en: 'Base damage', vz: '55' },
      { zh: '攻速 / 范围', en: 'Speed / Range', vz: '2.2 / 2.5 米', ve: '2.2 / 2.5 m' },
      { zh: '暴击', en: 'Crit', vz: '10% · 2.0x' },
      { zh: '持械移速', en: 'Move speed', vz: '110%' },
      { zh: '宝石槽', en: 'Gem slots', vz: '2' },
      { zh: '来源', en: 'Source', vz: '焚天龙皇（15% 掉率）', ve: 'Skyburner Dragon Lord (15%)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500034' },
    ],
    links: ['/bosses/dragon-king'],
  },
  '/equipment/frostmourne': {
    eyebrowZh: '近战武器 · 冰',
    eyebrowEn: 'Melee · Ice',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '6', tier: 6 },
      { zh: '伤害', en: 'Damage', vz: '38.5' },
      { zh: '攻速 / 范围', en: 'Speed / Range', vz: '1.54 / 2.0 米', ve: '1.54 / 2.0 m' },
      { zh: '暴击', en: 'Crit', vz: '7% · 1.4x' },
      { zh: '持械移速', en: 'Move speed', vz: '107%' },
      { zh: '寒冷防护', en: 'Cold protection', vz: '+2' },
      { zh: '来源', en: 'Source', vz: '原版「???」Boss（50% 额外掉落）', ve: 'Vanilla “???” boss (50% bonus drop)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500041' },
    ],
  },
  '/equipment/viper-dagger': {
    eyebrowZh: '近战武器 · 毒',
    eyebrowEn: 'Melee · Venom',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '5', tier: 5 },
      { zh: '伤害', en: 'Damage', vz: '22' },
      { zh: '攻速 / 范围', en: 'Speed / Range', vz: '2.1 / 1.4 米', ve: '2.1 / 1.4 m' },
      { zh: '暴击', en: 'Crit', vz: '8% · 1.5x' },
      { zh: '持械移速', en: 'Move speed', vz: '112%' },
      { zh: '获取', en: 'Availability', vz: '暂无常规来源（开发预览）', ve: 'No regular source yet (preview)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500048' },
    ],
  },
  '/equipment/frost-spear': {
    eyebrowZh: '近战武器 · 冰',
    eyebrowEn: 'Melee · Ice',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '5', tier: 5 },
      { zh: '伤害', en: 'Damage', vz: '32' },
      { zh: '攻速 / 范围', en: 'Speed / Range', vz: '1.3 / 2.4 米', ve: '1.3 / 2.4 m' },
      { zh: '暴击', en: 'Crit', vz: '3% · 1.2x' },
      { zh: '持械移速', en: 'Move speed', vz: '104%' },
      { zh: '寒冷防护', en: 'Cold protection', vz: '+1' },
      { zh: '获取', en: 'Availability', vz: '暂无常规来源（开发预览）', ve: 'No regular source yet (preview)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500051' },
    ],
  },
  '/equipment/summon-staff': {
    eyebrowZh: '近战武器 · 召唤',
    eyebrowEn: 'Melee · Summon',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '5', tier: 5 },
      { zh: '伤害', en: 'Damage', vz: '18' },
      { zh: '攻速 / 范围', en: 'Speed / Range', vz: '1.2 / 1.8 米', ve: '1.2 / 1.8 m' },
      { zh: '暴击', en: 'Crit', vz: '4% · 1.3x' },
      { zh: '持械移速', en: 'Move speed', vz: '105%' },
      { zh: '获取', en: 'Availability', vz: '暂无常规来源（开发预览）', ve: 'No regular source yet (preview)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500049' },
    ],
  },

  // ── 枪械 ────────────────────────────────────────────────
  '/equipment/dragon-breath': {
    eyebrowZh: '枪械 · 火焰',
    eyebrowEn: 'Gun · Fire',
    rows: [
      // 品质来自 Assets/Equipment/dragon_equipment 预制体
      { zh: '品质', en: 'Rarity', vz: '7', tier: 7 },
      { zh: '伤害', en: 'Damage', vz: '23' },
      { zh: '射速 / 弹匣', en: 'Fire rate / Mag', vz: '13 / 20' },
      { zh: '装填', en: 'Reload', vz: '3 秒', ve: '3 s' },
      { zh: '射程', en: 'Range', vz: '28' },
      { zh: '暴击', en: 'Crit', vz: '25% · 1.5x' },
      { zh: '持枪移速', en: 'Move speed', vz: '82%（瞄准 45%）', ve: '82% (45% aiming)' },
      { zh: '来源', en: 'Source', vz: '龙裔遗族（10% 掉率）', ve: 'Dragon Descendant (10%)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500005' },
    ],
    links: ['/bosses/dragon-descendant'],
  },
  '/equipment/dragon-cannon': {
    eyebrowZh: '枪械 · 火焰',
    eyebrowEn: 'Gun · Fire',
    rows: [
      // 品质来自 Assets/Equipment/dragonking_equipment 预制体
      { zh: '品质', en: 'Rarity', vz: '8', tier: 8 },
      { zh: '基础伤害', en: 'Base damage', vz: '26' },
      { zh: '射速 / 弹匣', en: 'Fire rate / Mag', vz: '9.2 / 15' },
      { zh: '装填', en: 'Reload', vz: '3.35 秒', ve: '3.35 s' },
      { zh: '射程', en: 'Range', vz: '24' },
      { zh: '暴击', en: 'Crit', vz: '28% · 1.6x' },
      { zh: '弹药 Profile', en: 'Ammo profiles', vz: '15 种，改写全部面板', ve: '15, each rewrites the panel' },
      { zh: '来源', en: 'Source', vz: '焚天龙皇（1% 掉率）', ve: 'Skyburner Dragon Lord (1%)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500035' },
    ],
    links: ['/bosses/dragon-king'],
  },

  // ── 图腾 ────────────────────────────────────────────────
  '/equipment/flight-totem': {
    eyebrowZh: '图腾',
    eyebrowEn: 'Totem',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '6（传说）', ve: '6 (Legendary)', tier: 6 },
      { zh: '效果', en: 'Effect', vz: '按住冲刺键起飞，松开滑翔', ve: 'Hold dash to fly, release to glide' },
      { zh: '代价', en: 'Cost', vz: '持续消耗体力', ve: 'Drains stamina while airborne' },
      { zh: '死亡掉落', en: 'Drops on death', vz: '不会', ve: 'No' },
      { zh: '来源', en: 'Source', vz: '焚天龙皇（15% 掉率）', ve: 'Skyburner Dragon Lord (15%)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500010' },
    ],
    links: ['/bosses/dragon-king', '/equipment/reverse-scale'],
  },
  '/equipment/reverse-scale': {
    eyebrowZh: '图腾 · 一次性',
    eyebrowEn: 'Totem · One-shot',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '6（传说）', ve: '6 (Legendary)', tier: 6 },
      { zh: '触发', en: 'Trigger', vz: '血量降到 1 或以下', ve: 'HP drops to 1 or below' },
      { zh: '回复', en: 'Heal', vz: '最大生命值 50%', ve: '50% of max HP' },
      { zh: '反击', en: 'Counter', vz: '8 向棱彩弹，每颗 15 伤害', ve: '8 prism bolts, 15 damage each' },
      { zh: '触发后', en: 'After trigger', vz: '0.5 秒无敌，然后碎裂消失', ve: '0.5 s invuln, then shatters' },
      { zh: '来源', en: 'Source', vz: '焚天龙皇（39% 掉率）', ve: 'Skyburner Dragon Lord (39%)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500013' },
    ],
    links: ['/bosses/dragon-king', '/equipment/flight-totem'],
  },
  '/equipment/energy-shield': {
    eyebrowZh: '图腾',
    eyebrowEn: 'Totem',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '5', tier: 5 },
      { zh: '体甲', en: 'Body armor', vz: '+3' },
      { zh: '效果', en: 'Effect', vz: '正面受击，部分伤害转为回血', ve: 'Frontal hits partly convert into healing' },
      { zh: '获取', en: 'Availability', vz: '暂无常规来源（开发预览）', ve: 'No regular source yet (preview)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500050' },
    ],
  },
  '/equipment/thunder-ring': {
    eyebrowZh: '图腾',
    eyebrowEn: 'Totem',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '5', tier: 5 },
      { zh: '蓄力', en: 'Charge', vz: '每次受击 +1 层，最多 5 层', ve: '+1 stack per hit, max 5' },
      { zh: '蓄力冷却', en: 'Stack cooldown', vz: '0.3 秒', ve: '0.3 s' },
      { zh: '衰减', en: 'Decay', vz: '8 秒未受击清空', ve: 'All stacks drop after 8 s without a hit' },
      { zh: '获取', en: 'Availability', vz: '暂无常规来源（开发预览）', ve: 'No regular source yet (preview)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500052' },
    ],
  },

  // ── 套装 ────────────────────────────────────────────────
  '/equipment/dragon-set': {
    eyebrowZh: '套装 · 2 件',
    eyebrowEn: 'Armor set · 2 pieces',
    rows: [
      // 品质来自 Assets/Equipment/dragon_equipment 预制体（Config 不设 Quality，随预制体走）
      { zh: '品质', en: 'Rarity', vz: '6', tier: 6 },
      { zh: '部件', en: 'Pieces', vz: '赤龙首（头盔）/ 焰鳞甲（护甲）', ve: 'Dragon Helm / Flamescale Armor' },
      { zh: '护甲 / 耐久', en: 'Armor / Durability', vz: '7 / 200（每件）', ve: '7 / 200 each' },
      { zh: '套装效果', en: 'Set bonus', vz: '火焰伤害转治疗', ve: 'Fire damage converts to healing' },
      { zh: '冲刺', en: 'Dash', vz: '3 米 / 冷却 1.5 秒', ve: '3 m / 1.5 s cooldown' },
      { zh: '定位', en: 'Role', vz: '过渡装', ve: 'Mid-game set' },
      { zh: '来源', en: 'Source', vz: '龙裔遗族', ve: 'Dragon Descendant' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500003 / 500004' },
    ],
    links: ['/bosses/dragon-descendant', '/equipment/dragon-king-set'],
  },
  '/equipment/dragon-king-set': {
    eyebrowZh: '套装 · 2 件',
    eyebrowEn: 'Armor set · 2 pieces',
    rows: [
      // 品质来自 Assets/Equipment/dragonking_equipment 预制体（Config 不设 Quality，随预制体走）
      { zh: '品质', en: 'Rarity', vz: '8', tier: 8 },
      { zh: '部件', en: 'Pieces', vz: '龙王之冕（头盔）/ 龙王鳞铠（护甲）', ve: 'Dragon Crown / Dragon Scale Mail' },
      { zh: '护甲 / 耐久', en: 'Armor / Durability', vz: '7 / 200（每件）', ve: '7 / 200 each' },
      { zh: '套装效果', en: 'Set bonus', vz: '火焰伤害转治疗', ve: 'Fire damage converts to healing' },
      { zh: '冲刺', en: 'Dash', vz: '6 米 + 3 米二段 / 冷却 0.5 秒', ve: '6 m + 3 m second dash / 0.5 s cooldown' },
      { zh: '定位', en: 'Role', vz: '毕业装', ve: 'Endgame set' },
      { zh: '来源', en: 'Source', vz: '焚天龙皇（每件 15% 掉率）', ve: 'Skyburner Dragon Lord (15% per piece)' },
      { zh: '物品 ID', en: 'Internal ID', vz: '500011 / 500012' },
    ],
    links: ['/bosses/dragon-king', '/equipment/dragon-set'],
  },
  '/equipment/frost-set': {
    eyebrowZh: '套装 · 2 件',
    eyebrowEn: 'Armor set · 2 pieces',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '6', tier: 6 },
      { zh: '部件', en: 'Pieces', vz: '霜冠（头盔）/ 寒冰铠甲（护甲）', ve: 'Frost Crown / Ice Armor' },
      { zh: '耐久', en: 'Durability', vz: '999（近似永久）', ve: '999 (effectively permanent)' },
      { zh: '套装效果', en: 'Set bonus', vz: '寒冰之护：冰伤转治疗 + 击杀冰葬霜爆 + 反击冻结', ve: 'Ice heals you, kills unleash a frost nova, freezing riposte' },
      { zh: '获取', en: 'Availability', vz: '「???」Boss 掉落（每件 20%，原版地图也算）/ 叮当商店好感 6 级', ve: "\"???\" boss drop (20% per piece, vanilla raids too) / Dingdang's shop at affinity 6" },
      { zh: '物品 ID', en: 'Internal ID', vz: '500053 / 500054' },
    ],
    links: ['/equipment/thunder-set'],
  },
  '/equipment/thunder-set': {
    eyebrowZh: '套装 · 2 件',
    eyebrowEn: 'Armor set · 2 pieces',
    rows: [
      { zh: '品质', en: 'Rarity', vz: '6', tier: 6 },
      { zh: '部件', en: 'Pieces', vz: '雷神之角（头盔）/ 雷霆战甲（护甲）', ve: 'Thunder Horns / Storm Plate' },
      { zh: '耐久', en: 'Durability', vz: '999（近似永久）', ve: '999 (effectively permanent)' },
      { zh: '套装效果', en: 'Set bonus', vz: '雷霆之怒：电伤转治疗 + 击杀引雷连锁 + 反击雷击 AOE', ve: 'Shock heals you, kills chain lightning, lightning AoE riposte' },
      { zh: '获取', en: 'Availability', vz: '风暴区 Boss 掉落（每件 20%，原版地图也算）/ 叮当商店好感 6 级', ve: "Storm Zone boss drop (20% per piece, vanilla raids too) / Dingdang's shop at affinity 6" },
      { zh: '物品 ID', en: 'Internal ID', vz: '500055 / 500056' },
    ],
    links: ['/equipment/frost-set'],
  },

  // ── 游戏模式 ────────────────────────────────────────────
  // 「带什么进 / 打多少波 / 掉不掉箱子 / 抽不抽词条」是选模式时唯一要比的四件事，
  // 从前分散在各页的「怎么进」「通关奖励」「核心规则」三节里，比一次要开三页。
  '/game-modes/standard': {
    eyebrowZh: '游戏模式 · 竞技场',
    eyebrowEn: 'Game mode · Arena',
    rows: [
      { zh: '入场', en: 'Entry', vz: 'BossRush 船票', ve: 'BossRush ticket' },
      { zh: '装备要求', en: 'Loadout', vz: '不限', ve: 'Bring anything' },
      { zh: '波次', en: 'Waves', vz: '有限波次', ve: 'Finite' },
      { zh: '掉落', en: 'Loot', vz: '杀 Boss 掉箱子', ve: 'Boss kills drop crates' },
      { zh: '变异词条', en: 'Mutators', vz: '抽', ve: 'Yes' },
      { zh: '地图', en: 'Maps', vz: '9 张任选', ve: 'Any of the 9' },
      { zh: '难度', en: 'Difficulty', vz: '★★☆☆☆' },
    ],
    links: ['/items/key-items', '/guides/beginner-route', '/game-modes/infinite-hell'],
  },
  '/game-modes/infinite-hell': {
    eyebrowZh: '游戏模式 · 无限',
    eyebrowEn: 'Game mode · Endless',
    rows: [
      { zh: '入场', en: 'Entry', vz: '船票 + 路牌选「无间炼狱」', ve: 'Ticket + pick Infinite Hell' },
      { zh: '装备要求', en: 'Loadout', vz: '不限', ve: 'Bring anything' },
      { zh: '波次', en: 'Waves', vz: '无限，逐波增强', ve: 'Endless, scaling' },
      { zh: '掉落', en: 'Loot', vz: '不掉箱子，改发现金', ve: 'Cash instead of crates' },
      { zh: '变异词条', en: 'Mutators', vz: '抽', ve: 'Yes' },
      { zh: '难度', en: 'Difficulty', vz: '★★★★☆' },
    ],
    links: ['/guides/hell-and-mode-d', '/systems/mutators'],
  },
  '/game-modes/mode-d': {
    eyebrowZh: '游戏模式 · 成长',
    eyebrowEn: 'Game mode · Progression',
    rows: [
      { zh: '入场', en: 'Entry', vz: 'BossRush 船票', ve: 'BossRush ticket' },
      { zh: '装备要求', en: 'Loadout', vz: '裸装（背包与宠物包都要清空）', ve: 'Empty kit, empty bags' },
      { zh: '波次', en: 'Waves', vz: '无限', ve: 'Endless' },
      { zh: '掉落', en: 'Loot', vz: '系统随机发装备 + 掉箱子', ve: 'Random issued gear plus crates' },
      { zh: '变异词条', en: 'Mutators', vz: '抽', ve: 'Yes' },
      { zh: '难度', en: 'Difficulty', vz: '★★★☆☆' },
    ],
    links: ['/guides/hell-and-mode-d'],
  },
  '/game-modes/mode-e': {
    eyebrowZh: '游戏模式 · 沙盒',
    eyebrowEn: 'Game mode · Sandbox',
    rows: [
      { zh: '入场', en: 'Entry', vz: '营旗（进场消耗）', ve: 'Faction banner (consumed)' },
      { zh: '装备要求', en: 'Loadout', vz: '裸装', ve: 'Empty kit' },
      { zh: '波次', en: 'Waves', vz: '无（沙盒混战）', ve: 'None — open brawl' },
      { zh: '掉落', en: 'Loot', vz: '只有打敌对阵营才掉', ve: 'Only from hostile factions' },
      { zh: '变异词条', en: 'Mutators', vz: '抽', ve: 'Yes' },
      { zh: '难度', en: 'Difficulty', vz: '★★★☆☆' },
    ],
    links: ['/guides/mode-e-strategy', '/items/mode-items'],
  },
  '/game-modes/mode-f': {
    eyebrowZh: '游戏模式 · 计时',
    eyebrowEn: 'Game mode · Timed',
    rows: [
      { zh: '入场', en: 'Entry', vz: '船票 + 血猎收发器（都消耗）', ve: 'Ticket + transponder (both consumed)' },
      { zh: '装备要求', en: 'Loadout', vz: '裸装', ve: 'Empty kit' },
      { zh: '节奏', en: 'Structure', vz: '四阶段，失血越来越快', ve: 'Four phases, bleed accelerates' },
      { zh: '掉落', en: 'Loot', vz: '杀 Boss 掉箱子', ve: 'Boss kills drop crates' },
      { zh: '变异词条', en: 'Mutators', vz: '抽', ve: 'Yes' },
      { zh: '难度', en: 'Difficulty', vz: '★★★★★' },
    ],
    links: ['/guides/mode-f-strategy'],
  },
  '/game-modes/mode-g': {
    eyebrowZh: '游戏模式 · 编排',
    eyebrowEn: 'Game mode · Scripted',
    rows: [
      { zh: '入场', en: 'Entry', vz: '船票 + 宿命回响信物', ve: 'Ticket + Fate Echo relic' },
      { zh: '装备要求', en: 'Loadout', vz: '保留当前装备', ve: 'Keep your own gear' },
      { zh: '波次', en: 'Waves', vz: '九波三幕，固定编排', ve: 'Nine waves, three acts, fixed' },
      { zh: '掉落', en: 'Loot', vz: '仅第 9 波胜利后发放', ve: 'Only after clearing wave 9' },
      { zh: '变异词条', en: 'Mutators', vz: '不抽', ve: 'No' },
      { zh: '难度', en: 'Difficulty', vz: '★★★★★' },
    ],
    links: ['/guides/mode-g-strategy', '/items/key-items'],
  },
  '/game-modes/zombie-mode': {
    eyebrowZh: '游戏模式 · 独立生存',
    eyebrowEn: 'Game mode · Standalone',
    rows: [
      { zh: '入场', en: 'Entry', vz: '尸潮邀请函', ve: 'Horde invitation' },
      { zh: '装备要求', en: 'Loadout', vz: '裸装（物品自动转仓库）', ve: 'Auto-stashed on entry' },
      { zh: '结构', en: 'Structure', vz: 'Roguelite 生存，无尽尸潮', ve: 'Roguelite survival, endless horde' },
      { zh: '经济', en: 'Economy', vz: '净化点数（自成一套）', ve: 'Purification points, self-contained' },
      { zh: '变异词条', en: 'Mutators', vz: '不抽（另有局内奖励）', ve: 'No — own reward track' },
      { zh: '难度', en: 'Difficulty', vz: '★★★★☆' },
    ],
  },
  '/game-modes/mode-h': {
    eyebrowZh: '游戏模式 · 经理人',
    eyebrowEn: 'Game mode · Manager',
    rows: [
      { zh: '入场', en: 'Entry', vz: '船票（码头船上选「黑市鸭王杯」）', ve: 'Ticket, via the dock boat menu' },
      { zh: '装备要求', en: 'Loadout', vz: '不限，你不下场', ve: "Anything — you don't fight" },
      { zh: '结构', en: 'Structure', vz: '一季六场，签两名斗士', ve: 'Six bouts a season, two signed fighters' },
      { zh: '你能做的', en: 'Your input', vz: '看盘、下注、每场喊一次口令', ve: 'Read odds, bet, one call per bout' },
      { zh: '变异词条', en: 'Mutators', vz: '不抽', ve: 'No' },
      { zh: '地图', en: 'Maps', vz: '仅 DEMO 终极挑战', ve: 'DEMO Ultimate Challenge only' },
      { zh: '难度', en: 'Difficulty', vz: '★★★☆☆' },
    ],
  },

  // ── NPC ─────────────────────────────────────────────────
  '/npcs/goblin': {
    eyebrowZh: 'NPC · 基地',
    eyebrowEn: 'NPC · Base',
    rows: [
      { zh: '身份', en: 'Role', vz: '哥布林工匠', ve: 'Goblin smith' },
      { zh: '提供', en: 'Services', vz: '装备重铸 / 词缀锻造', ve: 'Reforging and affix forging' },
      { zh: '好感度', en: 'Affinity', vz: '10 级，可结婚', ve: '10 tiers, marriable' },
    ],
    links: ['/systems/reforge', '/systems/affix-forge', '/systems/affinity-marriage'],
  },
  '/npcs/nurse': {
    eyebrowZh: 'NPC · 基地',
    eyebrowEn: 'NPC · Base',
    rows: [
      { zh: '身份', en: 'Role', vz: '前 J-Lab 医疗研究员', ve: 'Ex-J-Lab medical researcher' },
      { zh: '提供', en: 'Services', vz: '治疗 / 复活 / 野战诊所', ve: 'Healing, revives, field clinic' },
      { zh: '好感度', en: 'Affinity', vz: '10 级，可结婚', ve: '10 tiers, marriable' },
    ],
    links: ['/systems/affinity-marriage', '/items/npc-items'],
  },
  '/npcs/courier': {
    eyebrowZh: 'NPC · 随行',
    eyebrowEn: 'NPC · On call',
    rows: [
      { zh: '身份', en: 'Role', vz: '快递员', ve: 'Courier' },
      { zh: '提供', en: 'Services', vz: '基地与竞技场内的仓库存取', ve: 'Stash access in base and arena' },
      { zh: '召唤', en: 'Summon', vz: '阿稳的快递令', ve: "Awen's courier token" },
    ],
    links: ['/items/npc-items'],
  },
}
