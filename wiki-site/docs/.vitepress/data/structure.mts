/**
 * structure.mts — 站点结构的唯一事实源。
 *
 * 在此之前，站点结构散在三处：config.mts 里两份手写侧边栏（中/英各一份）、
 * 首页 index.md 里的 features、以及各索引页正文里的人肉清单。加一个条目要改三处，
 * 漏一处就出现「侧栏有、首页没有」这类不一致。
 *
 * 现在只维护本文件：
 *   - config.mts        由 CATEGORIES 生成 nav 与 sidebar；
 *   - WikiHome.vue      由 CATEGORIES 生成首页门户宫格；
 *   - WikiCardGrid.vue  由 CATEGORIES 生成类目主页底部的条目宫格；
 *   - WikiNavbox.vue    由 CATEGORIES 生成页尾的同类导航；
 *   - WikiBreadcrumb    由 CATEGORIES 反查当前页所属类目。
 *
 * 与 WikiContent/catalog.tsv 的关系：
 *   catalog.tsv 是**游戏内 Wiki 书**的目录，同时决定 sync-content.mjs 生成哪些页面。
 *   本文件是**在线站**的导航结构，两边条目应当一一对应——由
 *   tests/WikiSiteStructureGuard.py 双向校验（缺页 / 多页 / 图标失配都会红）。
 *   更新日志不在本文件里：它有 46+ 条且按版本号自动排序，仍由 config.mts 读 catalog 生成。
 *
 * icon 字段：
 *   写的是 wiki-site/scripts/image-manifest.json 里的 key，不是路径。
 *   缺图标不算错——WikiIcon.vue 会退化成首字母字牌，
 *   所以可以先把结构填全，图标后补。
 */

export type Locale = 'zh' | 'en'

export interface WikiEntry {
  /** 不带语言前缀的站内路径，例如 '/bosses/dragon-king' */
  path: string
  zh: string
  en: string
  /** image-manifest.json 里的图标 key；缺省时退化为首字母字牌 */
  icon?: string
  /** 卡片上的一句话说明，尽量短 */
  blurbZh?: string
  blurbEn?: string
  /** 卡片右上角的小徽标：武器类型、难度星级等 */
  tagZh?: string
  tagEn?: string
}

export interface WikiCategory {
  id: string
  /** 类目主页路径 */
  path: string
  zh: string
  en: string
  icon: string
  blurbZh: string
  blurbEn: string
  entries: WikiEntry[]
}

export const CATEGORIES: WikiCategory[] = [
  {
    id: 'getting-started',
    path: '/getting-started/overview',
    zh: '入门',
    en: 'Getting Started',
    icon: 'cat-start',
    blurbZh: '订阅、开局、第一张船票',
    blurbEn: 'Subscribe, launch, buy your first ticket',
    entries: [
      {
        path: '/getting-started/overview',
        zh: 'Mod 简介',
        en: 'Mod Overview',
        icon: 'cat-start',
        blurbZh: '把鸭科夫变成 Boss 竞技场的大型 Mod',
        blurbEn: 'The mod that turns Duckov into a boss arena',
      },
      {
        path: '/getting-started/installation',
        zh: '安装与启用',
        en: 'Installation',
        icon: 'start-install',
        blurbZh: '四步搞定，比泡面还简单',
        blurbEn: 'Four steps, faster than instant noodles',
      },
      {
        path: '/getting-started/first-steps',
        zh: '新手上路',
        en: 'First Steps',
        icon: 'start-firstrun',
        blurbZh: '买船票、选地图、第一次撤离',
        blurbEn: 'Buy a ticket, pick a map, extract alive',
      },
    ],
  },
  {
    id: 'game-modes',
    path: '/game-modes/',
    zh: '游戏模式',
    en: 'Game Modes',
    icon: 'cat-modes',
    blurbZh: '八种模式，从休闲竞技场到地狱级追猎',
    blurbEn: 'Eight modes, from casual arena to hell-tier hunts',
    entries: [
      {
        path: '/game-modes/',
        zh: '模式总览',
        en: 'Mode Overview',
        icon: 'cat-modes',
        blurbZh: '八种模式对照与入场优先级',
        blurbEn: 'All eight modes and how entry priority works',
      },
      {
        path: '/game-modes/standard',
        zh: '标准 BossRush',
        en: 'Standard BossRush',
        icon: 'mode-standard',
        blurbZh: '买张船票进去打，最经典的一档',
        blurbEn: 'Buy a ticket and fight — the classic run',
        tagZh: '★★☆☆☆',
        tagEn: '★★☆☆☆',
      },
      {
        path: '/game-modes/infinite-hell',
        zh: '无间炼狱',
        en: 'Infinite Hell',
        icon: 'mode-hell',
        blurbZh: '无限波次，Boss 越打越强',
        blurbEn: 'Endless waves, bosses scale forever',
        tagZh: '★★★★☆',
        tagEn: '★★★★☆',
      },
      {
        path: '/game-modes/mode-d',
        zh: '白手起家',
        en: 'From Scratch',
        icon: 'mode-scratch',
        blurbZh: '裸装入场，靠随机掉落一路逆袭',
        blurbEn: 'Enter naked, climb back up on random loot',
        tagZh: '★★★☆☆',
        tagEn: '★★★☆☆',
      },
      {
        path: '/game-modes/mode-e',
        zh: '划地为营',
        en: 'Faction War',
        icon: 'mode-faction',
        blurbZh: '选阵营、拉帮派，满地图混战',
        blurbEn: 'Pick a faction and start a map-wide brawl',
        tagZh: '★★★☆☆',
        tagEn: '★★★☆☆',
      },
      {
        path: '/game-modes/mode-f',
        zh: '血猎追击',
        en: 'Blood Hunt',
        icon: 'mode-bloodhunt',
        blurbZh: '进场就掉血，不杀 Boss 就死',
        blurbEn: 'You bleed on entry — kill or die',
        tagZh: '★★★★★',
        tagEn: '★★★★★',
      },
      {
        path: '/game-modes/mode-g',
        zh: '宿命回响',
        en: 'Fate Echo',
        icon: 'mode-fate',
        blurbZh: '九波三幕，专门反制你的打法',
        blurbEn: 'Nine waves that learn and counter your build',
        tagZh: '★★★★★',
        tagEn: '★★★★★',
      },
      {
        path: '/game-modes/zombie-mode',
        zh: '末日丧尸模式',
        en: 'Zombie Mode',
        icon: 'mode-zombie',
        blurbZh: '独立的 Roguelite 生存，无尽尸潮',
        blurbEn: 'Standalone roguelite survival vs endless hordes',
        tagZh: '★★★★☆',
        tagEn: '★★★★☆',
      },
      {
        path: '/game-modes/mode-h',
        zh: '百战留痕',
        en: 'Black Market Duck Cup',
        icon: 'mode-cup',
        blurbZh: '你不下场：签斗士、看盘口、下注',
        blurbEn: 'You never fight — you sign, bet, and call the odds',
        tagZh: '★★★☆☆',
        tagEn: '★★★☆☆',
      },
    ],
  },
  {
    id: 'bosses',
    path: '/bosses/',
    zh: 'Boss',
    en: 'Bosses',
    icon: 'cat-bosses',
    blurbZh: '三个全原创 Boss，各有技能与专属掉落',
    blurbEn: 'Three original bosses with unique kits and drops',
    entries: [
      {
        path: '/bosses/',
        zh: 'Boss 总览',
        en: 'Boss Overview',
        icon: 'cat-bosses',
        blurbZh: '在哪遇到、怎么筛掉、相关成就',
        blurbEn: 'Where they spawn, how to filter, related feats',
      },
      {
        path: '/bosses/dragon-descendant',
        zh: '龙裔遗族',
        en: 'Dragon Descendant',
        icon: 'dragondescendant',
        blurbZh: '入门级自定义 Boss，半血假死狂暴',
        blurbEn: 'Entry-tier boss — fakes death at half HP, then rages',
        tagZh: '500 血',
        tagEn: '500 HP',
      },
      {
        path: '/bosses/dragon-king',
        zh: '焚天龙皇',
        en: 'Skyburner Dragon Lord',
        icon: 'boss_dragonking',
        blurbZh: '最终 Boss，7 技能双阶段，毕业装来源',
        blurbEn: 'Final boss — 7 skills, two phases, endgame drops',
        tagZh: '800 血',
        tagEn: '800 HP',
      },
      {
        path: '/bosses/phantom-witch',
        zh: '幽灵女巫',
        en: 'Phantom Witch',
        icon: 'boss_phantomwitch',
        blurbZh: '隐身闪现 + 诅咒领域 + 召唤亡灵',
        blurbEn: 'Blinks, curses the ground, raises the dead',
        tagZh: '1000 血',
        tagEn: '1000 HP',
      },
    ],
  },
  {
    id: 'equipment',
    path: '/equipment/',
    zh: '装备',
    en: 'Equipment',
    icon: 'cat-equipment',
    blurbZh: '16 件原创武器、图腾与套装',
    blurbEn: '16 original weapons, totems, and armor sets',
    entries: [
      {
        path: '/equipment/',
        zh: '装备总览',
        en: 'Equipment Overview',
        icon: 'cat-equipment',
        blurbZh: '全家福、获取路线与选装建议',
        blurbEn: 'The full lineup, drop routes, and what to pick',
      },
      {
        path: '/equipment/phantom-scythe',
        zh: '噬魂挽歌',
        en: 'Soulreaper Requiem',
        icon: 'eq-phantom-scythe',
        blurbZh: '幽灵大镰，普攻叠诅咒减速',
        blurbEn: 'Spectral scythe that stacks slowing curses',
        tagZh: '近战',
        tagEn: 'Melee',
      },
      {
        path: '/equipment/dragon-set',
        zh: '龙裔套装',
        en: 'Dragon Set',
        icon: 'eq-dragon-set',
        blurbZh: '火伤转治疗 + 3 米冲刺的过渡装',
        blurbEn: 'Fire-to-heal plus a 3 m dash — the mid-game set',
        tagZh: '套装',
        tagEn: 'Set',
      },
      {
        path: '/equipment/dragon-king-set',
        zh: '龙王套装',
        en: 'Dragon King Set',
        icon: 'eq-dragon-king-set',
        blurbZh: '6 米二段冲刺 + 熔浆尾迹的毕业装',
        blurbEn: 'Double 6 m dash with a magma trail — endgame set',
        tagZh: '套装',
        tagEn: 'Set',
      },
      {
        path: '/equipment/flight-totem',
        zh: '腾云驾雾图腾',
        en: 'Cloud Rider Totem',
        icon: 'eq-flight-totem',
        blurbZh: '装上就能飞，消耗体力滑翔',
        blurbEn: 'Equip and fly — glide while stamina lasts',
        tagZh: '图腾',
        tagEn: 'Totem',
      },
      {
        path: '/equipment/reverse-scale',
        zh: '逆鳞',
        en: 'Reverse Scale',
        icon: 'eq-reverse-scale',
        blurbZh: '濒死回半血 + 棱彩弹反击，一次性',
        blurbEn: 'One-shot lifesaver: half HP back plus a prism volley',
        tagZh: '图腾',
        tagEn: 'Totem',
      },
      {
        path: '/equipment/halberd',
        zh: '焚皇断界戟',
        en: 'Skyburner Halberd',
        icon: 'eq-halberd',
        blurbZh: '三连击 + 地面炸裂的近战大杀器',
        blurbEn: 'Three-hit combo that cracks the ground open',
        tagZh: '近战',
        tagEn: 'Melee',
      },
      {
        path: '/equipment/dragon-breath',
        zh: '龙息',
        en: 'Dragon Breath',
        icon: 'eq-dragon-breath',
        blurbZh: '能叠灼烧的火焰枪',
        blurbEn: 'Flame gun that stacks burn',
        tagZh: '枪械',
        tagEn: 'Gun',
      },
      {
        path: '/equipment/dragon-cannon',
        zh: '焚天龙铳',
        en: 'Dragon Cannon',
        icon: 'eq-dragon-cannon',
        blurbZh: '15 种弹药改写成不同龙焰弹幕',
        blurbEn: '15 ammo types, 15 different dragonfire patterns',
        tagZh: '枪械',
        tagEn: 'Gun',
      },
      {
        path: '/equipment/frostmourne',
        zh: '霜之哀伤',
        en: 'Frostmourne',
        icon: 'eq-frostmourne',
        blurbZh: '冰属性大剑，右键召唤 5 只亡灵',
        blurbEn: 'Ice greatsword that raises five undead',
        tagZh: '近战',
        tagEn: 'Melee',
      },
      {
        path: '/equipment/viper-dagger',
        zh: '毒蛇匕首',
        en: 'Viper Dagger',
        icon: 'eq-viper-dagger',
        blurbZh: '连击叠 5 层毒素，满层爆发追伤',
        blurbEn: 'Stack five venom charges, then burst',
        tagZh: '近战',
        tagEn: 'Melee',
      },
      {
        path: '/equipment/summon-staff',
        zh: '召唤法杖',
        en: 'Summoning Staff',
        icon: 'eq-summon-staff',
        blurbZh: '召唤 3 只灵魂战士分摊仇恨',
        blurbEn: 'Summons three soul warriors to soak aggro',
        tagZh: '近战',
        tagEn: 'Melee',
      },
      {
        path: '/equipment/energy-shield',
        zh: '能量盾',
        en: 'Energy Shield',
        icon: 'eq-energy-shield',
        blurbZh: '正面受击，把部分伤害转回血量',
        blurbEn: 'Converts part of frontal damage back into health',
        tagZh: '图腾',
        tagEn: 'Totem',
      },
      {
        path: '/equipment/frost-spear',
        zh: '冰霜长矛',
        en: 'Frost Spear',
        icon: 'eq-frost-spear',
        blurbZh: '攻击距离长，命中必冻',
        blurbEn: 'Long reach, guaranteed freeze on hit',
        tagZh: '近战',
        tagEn: 'Melee',
      },
      {
        path: '/equipment/thunder-ring',
        zh: '雷电戒指',
        en: 'Thunder Ring',
        icon: 'eq-thunder-ring',
        blurbZh: '受击蓄力，满层放雷电爆发',
        blurbEn: 'Charges when hit, then discharges lightning',
        tagZh: '图腾',
        tagEn: 'Totem',
      },
      {
        path: '/equipment/frost-set',
        zh: '霜冠套装',
        en: 'Frost Set',
        icon: 'eq-frost-set',
        blurbZh: '冰伤转治疗 + 击杀冰葬 + 反击冻结',
        blurbEn: 'Ice heals, frost-nova kills, freezing riposte',
        tagZh: '套装',
        tagEn: 'Set',
      },
      {
        path: '/equipment/thunder-set',
        zh: '雷神套装',
        en: 'Thunder Set',
        icon: 'eq-thunder-set',
        blurbZh: '电伤转治疗 + 击杀引雷 + 反击雷击',
        blurbEn: 'Shock heals, chain-lightning kills, AoE riposte',
        tagZh: '套装',
        tagEn: 'Set',
      },
    ],
  },
  {
    id: 'items',
    path: '/items/',
    zh: '物品',
    en: 'Items',
    icon: 'cat-items',
    blurbZh: '40 多种自定义物品：船票、礼物、工事',
    blurbEn: '40+ custom items — tickets, gifts, fortifications',
    entries: [
      {
        path: '/items/',
        zh: '物品总览',
        en: 'Item Overview',
        icon: 'cat-items',
        blurbZh: '全部自定义物品的分类索引',
        blurbEn: 'A categorised index of every custom item',
      },
      {
        path: '/items/key-items',
        zh: '入场与功能物品',
        en: 'Entry & Utility Items',
        icon: 'item-key',
        blurbZh: '船票、营旗、信物——进场的钥匙',
        blurbEn: 'Tickets, banners, relics — the keys to every mode',
      },
      {
        path: '/items/npc-items',
        zh: 'NPC 相关物品',
        en: 'NPC Items',
        icon: 'item-npc',
        blurbZh: '钻石、戒指、图纸——送礼与召唤',
        blurbEn: 'Diamonds, rings, blueprints — gifts and summons',
      },
      {
        path: '/items/consumables',
        zh: '消耗品',
        en: 'Consumables',
        icon: 'item-consumable',
        blurbZh: '安神滴剂等活动限定好感度道具',
        blurbEn: 'Event-limited affinity boosters and one-shot aids',
      },
      {
        path: '/items/mode-items',
        zh: '模式专属物品',
        en: 'Mode-Exclusive Items',
        icon: 'item-mode',
        blurbZh: '划地为营的工事与路障，商人有售',
        blurbEn: 'Faction War fortifications, sold by the trader',
      },
    ],
  },
  {
    id: 'npcs',
    path: '/npcs/',
    zh: 'NPC',
    en: 'NPCs',
    icon: 'cat-npcs',
    blurbZh: '三个原创 NPC：打铁、治病、搬家',
    blurbEn: 'Three original NPCs — smith, medic, courier',
    entries: [
      {
        path: '/npcs/',
        zh: 'NPC 总览',
        en: 'NPC Overview',
        icon: 'cat-npcs',
        blurbZh: '三个 NPC 的分工与解锁顺序',
        blurbEn: 'What each NPC does and when they show up',
      },
      {
        path: '/npcs/goblin',
        zh: '叮当',
        en: 'Dingdang',
        icon: 'npc-goblin',
        blurbZh: '哥布林工匠——重铸、词缀锻造',
        blurbEn: 'Goblin smith — reforging and affix work',
        tagZh: '工匠',
        tagEn: 'Smith',
      },
      {
        path: '/npcs/nurse',
        zh: '羽织',
        en: 'Yuori',
        icon: 'npc-nurse',
        blurbZh: '护士——治疗、复活与野战诊所',
        blurbEn: 'Nurse — healing, revives, and a field clinic',
        tagZh: '护士',
        tagEn: 'Nurse',
      },
      {
        path: '/npcs/courier',
        zh: '阿稳',
        en: 'Awen',
        icon: 'npc-courier',
        blurbZh: '快递员——随叫随到的移动仓库',
        blurbEn: 'Courier — a walking stash, on call anywhere',
        tagZh: '快递',
        tagEn: 'Courier',
      },
      {
        path: '/systems/affinity-marriage',
        zh: '好感度与婚姻',
        en: 'Affinity & Marriage',
        icon: 'sys-marriage',
        blurbZh: '10 级好感、送礼、约会与结婚',
        blurbEn: 'Ten affinity tiers, gifts, dates, and weddings',
      },
    ],
  },
  {
    id: 'maps',
    path: '/maps/',
    zh: '地图',
    en: 'Maps',
    icon: 'cat-maps',
    blurbZh: '9 张竞技场地图与各自的地形脾气',
    blurbEn: 'Nine arena maps and how each one plays',
    entries: [
      {
        path: '/maps/',
        zh: '地图总览',
        en: 'Map Overview',
        icon: 'cat-maps',
        blurbZh: '9 张地图的可用模式与特点',
        blurbEn: 'Which modes each of the nine maps supports',
      },
    ],
  },
  {
    id: 'systems',
    path: '/systems/',
    zh: '系统',
    en: 'Systems',
    icon: 'cat-systems',
    blurbZh: '掉落、重铸、图鉴、征程、后山……',
    blurbEn: 'Loot, reforging, codex, campaign, backyard, and more',
    entries: [
      {
        // 类目主页来自 wiki-site/hubs/systems.{zh,en}.md（sync 复制），不来自 WikiContent
        path: '/systems/',
        zh: '系统总览',
        en: 'Systems Overview',
        icon: 'cat-systems',
        blurbZh: '按「你想干什么」分好类的系统索引',
        blurbEn: 'Every system, grouped by what you want to do',
      },
      {
        path: '/systems/loot-rewards',
        zh: '掉落与奖励',
        en: 'Loot & Rewards',
        icon: 'sys-loot',
        blurbZh: '各模式的战利品规则一览',
        blurbEn: 'How loot works in every mode',
      },
      {
        path: '/systems/death-wraith',
        zh: '死亡亡魂',
        en: 'Death Wraith',
        icon: 'sys-wraith',
        blurbZh: '把一次死亡变成一场回头补课',
        blurbEn: 'Turns one death into a rematch you must win',
      },
      {
        path: '/systems/starwish-fountain',
        zh: '星愿许愿台',
        en: 'StarWish Fountain',
        icon: 'sys-fountain',
        blurbZh: '基地可建造的许愿互动建筑',
        blurbEn: 'A buildable wishing fountain for your base',
      },
      {
        path: '/systems/reforge',
        zh: '重铸系统',
        en: 'Reforge System',
        icon: 'sys-reforge',
        blurbZh: '重铸装备数值，冷淬液锁词条',
        blurbEn: 'Reroll stats, lock affixes with quench fluid',
      },
      {
        path: '/systems/affix-forge',
        zh: '词缀锻造',
        en: 'Affix Forging',
        icon: 'forge-stone',
        blurbZh: '重铸改数值，词缀锻造改行为',
        blurbEn: 'Reforging changes numbers; forging changes behaviour',
      },
      {
        path: '/systems/boss-filter',
        zh: 'Boss 筛选器',
        en: 'Boss Filter',
        icon: 'sys-filter',
        blurbZh: 'Ctrl+F10 禁用 Boss、调出场权重',
        blurbEn: 'Ctrl+F10 to ban bosses or tune their weights',
      },
      {
        path: '/systems/mutators',
        zh: '变异词条系统',
        en: 'Mutator System',
        icon: 'sys-mutator',
        blurbZh: '28 条词条池，每局随机改写规则',
        blurbEn: '28 mutators that rewrite the rules each run',
      },
      {
        path: '/systems/petnest',
        zh: '遗种巢',
        en: 'PetNest',
        icon: 'sys-petnest',
        blurbZh: '打 Boss 捡崽、基地养崽、崽陪你打',
        blurbEn: 'Collect hatchlings, raise them, take them to war',
      },
      {
        path: '/systems/daily-report',
        zh: '鸭科夫日报',
        en: 'The Duckov Daily',
        icon: 'sys-daily',
        blurbZh: '每个游戏日一期，报道你昨天干了啥',
        blurbEn: 'A daily paper about everything you did yesterday',
      },
      {
        path: '/systems/codex',
        zh: '鸭皇图鉴',
        en: 'Duck King Codex',
        icon: 'codex-book',
        blurbZh: '会自己记账的 Boss 收集册',
        blurbEn: 'A boss collection book that tracks itself',
      },
      {
        path: '/systems/random-events',
        zh: '局内随机事件',
        en: 'Random Events',
        icon: 'sys-events',
        blurbZh: '鸭生无常：补给空投、血月、天降怪',
        blurbEn: 'Supply drops, blood moons, and worse surprises',
      },
      {
        path: '/systems/campaign',
        zh: '鸭王征程',
        en: 'Duck King Campaign',
        icon: 'sys-campaign',
        blurbZh: '六章剧情契约，串联现有全部模式',
        blurbEn: 'Six story contracts that string every mode together',
      },
      {
        path: '/systems/arena-backyard',
        zh: '竞技场后山',
        en: 'Arena Backyard',
        icon: 'sys-backyard',
        blurbZh: '种地、摆战利品、换战歌',
        blurbEn: 'Farm, display trophies, pick your battle anthem',
      },
      {
        path: '/systems/affinity-marriage',
        zh: '好感度与婚姻',
        en: 'Affinity & Marriage',
        icon: 'sys-marriage',
        blurbZh: '10 级好感、送礼、约会与结婚',
        blurbEn: 'Ten affinity tiers, gifts, dates, and weddings',
      },
      {
        path: '/systems/configuration',
        zh: '配置选项',
        en: 'Configuration',
        icon: 'sys-config',
        blurbZh: '所有可调参数与热更新说明',
        blurbEn: 'Every tunable knob and how hot-reload works',
      },
    ],
  },
  {
    id: 'achievements',
    path: '/achievements/',
    zh: '成就',
    en: 'Achievements',
    icon: 'cat-achievements',
    blurbZh: '45 个成就、9 个分类、现金奖励',
    blurbEn: '45 achievements across 9 categories, all paying cash',
    entries: [
      {
        path: '/achievements/',
        zh: '成就大全',
        en: 'Achievement List',
        icon: 'cat-achievements',
        blurbZh: '全部 45 个成就与奖金难度表',
        blurbEn: 'All 45 achievements with payouts and difficulty',
      },
    ],
  },
  {
    id: 'guides',
    path: '/guides/',
    zh: '攻略',
    en: 'Guides',
    icon: 'cat-guides',
    blurbZh: '从新手路线到地狱模式打法',
    blurbEn: 'From the beginner route to hell-tier tactics',
    entries: [
      {
        // 类目主页来自 wiki-site/hubs/guides.{zh,en}.md（sync 复制），不来自 WikiContent
        path: '/guides/',
        zh: '攻略索引',
        en: 'Guide Index',
        icon: 'cat-guides',
        blurbZh: '按你卡在哪一步挑攻略',
        blurbEn: 'Pick a guide by where you are stuck',
      },
      {
        path: '/guides/beginner-route',
        zh: '新手推荐路线',
        en: 'Beginner Route',
        icon: 'cat-guides',
        blurbZh: '第一张船票到第一件毕业装',
        blurbEn: 'From your first ticket to your first endgame piece',
      },
      {
        path: '/guides/boss-fights',
        zh: 'Boss 战攻略',
        en: 'Boss Fights',
        icon: 'cat-bosses',
        blurbZh: '三个原创 Boss 的逐阶段打法',
        blurbEn: 'Phase-by-phase tactics for all three bosses',
      },
      {
        path: '/guides/hell-and-mode-d',
        zh: '无间炼狱与白手起家',
        en: 'Infinite Hell & From Scratch',
        icon: 'mode-hell',
        blurbZh: '两种无限模式的续航思路',
        blurbEn: 'Staying alive in the two endless modes',
      },
      {
        path: '/guides/mode-e-strategy',
        zh: '划地为营攻略',
        en: 'Faction War Guide',
        icon: 'mode-faction',
        blurbZh: '阵营选择、工事布置与混战节奏',
        blurbEn: 'Picking a side, building works, pacing the brawl',
      },
      {
        path: '/guides/mode-f-strategy',
        zh: '血猎追击攻略',
        en: 'Blood Hunt Guide',
        icon: 'mode-bloodhunt',
        blurbZh: '四阶段失血曲线与续命节奏',
        blurbEn: 'The four bleed phases and how to outrun them',
      },
      {
        path: '/guides/mode-g-strategy',
        zh: '宿命回响攻略',
        en: 'Fate Echo Guide',
        icon: 'mode-fate',
        blurbZh: '被反制之后怎么换打法',
        blurbEn: 'How to switch builds once it starts countering you',
      },
    ],
  },
  {
    id: 'easter-eggs',
    path: '/easter-eggs',
    zh: '彩蛋',
    en: 'Easter Eggs',
    icon: 'cat-easter',
    blurbZh: '藏在角落里的玩笑与致敬',
    blurbEn: 'Jokes and homages hidden in the corners',
    entries: [
      {
        path: '/easter-eggs',
        zh: '彩蛋',
        en: 'Easter Eggs',
        icon: 'cat-easter',
        blurbZh: '等你自己撞上的那些细节',
        blurbEn: 'The details you are meant to stumble into',
      },
    ],
  },
]

/** 更新日志不进 CATEGORIES（条目太多且按版本号自动排序），但门户与面包屑要认得它。 */
export const CHANGELOG_CATEGORY = {
  id: 'changelog',
  path: '/changelog/',
  zh: '更新日志',
  en: 'Changelog',
  icon: 'cat-changelog',
  blurbZh: '每个版本改了什么，从 v1.6 到现在',
  blurbEn: 'What changed in every release since v1.6',
}

// ── 查询辅助 ─────────────────────────────────────────────

/** 给站内路径加语言前缀：'/bosses/' + en -> '/en/bosses/' */
export function localizePath(path: string, locale: Locale): string {
  return locale === 'en' ? '/en' + path : path
}

/** 从运行时路由反推 CATEGORIES 里的规范路径（去掉 base、语言前缀与 .html）。 */
export function canonicalPath(routePath: string, base = '/'): string {
  let p = routePath
  if (base !== '/' && p.startsWith(base)) p = '/' + p.slice(base.length)
  p = p.replace(/\.html$/, '')
  if (p.startsWith('/en/')) p = p.slice(3)
  else if (p === '/en') p = '/'
  return p
}

/** '/bosses' 与 '/bosses/' 视为同一页。 */
function samePath(a: string, b: string): boolean {
  const norm = (s: string) => (s.length > 1 ? s.replace(/\/$/, '') : s)
  return norm(a) === norm(b)
}

export interface Located {
  category: WikiCategory
  entry: WikiEntry
  /** 条目在所属类目 entries 中的下标 */
  index: number
}

/**
 * 反查规范路径属于哪个类目下的哪一条。
 *
 * 「好感度与婚姻」同时挂在 NPC 与系统两个类目下（它确实两边都属于），
 * 这里返回**第一个**命中——数组顺序即优先级，NPC 在前，所以面包屑显示 NPC。
 */
export function locate(canonical: string): Located | null {
  for (const category of CATEGORIES) {
    for (let i = 0; i < category.entries.length; i++) {
      if (samePath(category.entries[i].path, canonical)) {
        return { category, entry: category.entries[i], index: i }
      }
    }
  }
  return null
}

/** 该路径是否是某个类目的主页（决定要不要在页尾铺条目宫格）。 */
export function categoryOfHub(canonical: string): WikiCategory | null {
  for (const category of CATEGORIES) {
    if (samePath(category.path, canonical)) return category
  }
  return null
}
