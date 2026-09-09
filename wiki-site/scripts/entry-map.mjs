/**
 * entry-map.mjs — entryId ↔ 在线站路径的唯一一份映射，以及 catalog.tsv 的读取。
 *
 * 为什么从 sync-content.mjs 里拆出来：
 *   同一份映射有三个消费者——sync（生成页面）、config.mts（更新日志侧栏与实体别名）、
 *   seo.mts（把「编辑此页」反查回 WikiContent 源文件）。sync-content.mjs 一被 import 就会
 *   执行 main()，别处没法从它拿数据，只能把纯数据与纯函数放到这里。
 *
 * 加一个页面时这里是第 2 步（见 wiki-site/AGENTS.md §3）。
 */
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));

/** 仓库根目录（wiki-site 的上一级） */
export const MOD_ROOT = join(__dirname, '..', '..');
export const CATALOG_PATH = join(MOD_ROOT, 'WikiContent', 'catalog.tsv');

// ── entryId → wiki-site 路径映射（中英文共用）────────────
export const ENTRY_TO_PATH = {
  'start__overview':            'getting-started/overview.md',
  'start__how_to_enter':        'getting-started/installation.md',
  'start__first_run':           'getting-started/first-steps.md',
  'mode__overview':             'game-modes/index.md',
  'mode__mode_a':               'game-modes/standard.md',
  'mode__mode_c':               'game-modes/infinite-hell.md',
  'mode__mode_d':               'game-modes/mode-d.md',
  'mode__mode_e':               'game-modes/mode-e.md',
  'mode__mode_f':               'game-modes/mode-f.md',
  'mode__mode_g':               'game-modes/mode-g.md',
  'mode__zombie_mode':          'game-modes/zombie-mode.md',
  'mode__mode_h':               'game-modes/mode-h.md',
  'map__overview':              'maps/index.md',
  'map__sky_island':            'maps/sky-island.md',
  'boss__overview':             'bosses/index.md',
  'boss__dragon_descendant':    'bosses/dragon-descendant.md',
  'boss__dragon_king':          'bosses/dragon-king.md',
  'boss__phantom_witch':        'bosses/phantom-witch.md',
  'npc__overview':              'npcs/index.md',
  'npc__goblin':                'npcs/goblin.md',
  'npc__nurse':                 'npcs/nurse.md',
  'npc__courier':               'npcs/courier.md',
  'npc__affinity_and_marriage': 'systems/affinity-marriage.md',
  'equipment__overview':        'equipment/index.md',
  'equipment__phantom_scythe':  'equipment/phantom-scythe.md',
  'equipment__dragon_set':      'equipment/dragon-set.md',
  'equipment__dragon_king_set': 'equipment/dragon-king-set.md',
  'equipment__flight_totem':    'equipment/flight-totem.md',
  'equipment__reverse_scale':   'equipment/reverse-scale.md',
  'equipment__halberd':         'equipment/halberd.md',
  'equipment__dragon_breath':   'equipment/dragon-breath.md',
  'equipment__dragon_cannon':   'equipment/dragon-cannon.md',
  'equipment__frostmourne':     'equipment/frostmourne.md',
  'equipment__viper_dagger':    'equipment/viper-dagger.md',
  'equipment__summon_staff':    'equipment/summon-staff.md',
  'equipment__energy_shield':   'equipment/energy-shield.md',
  'equipment__frost_spear':     'equipment/frost-spear.md',
  'equipment__thunder_ring':    'equipment/thunder-ring.md',
  'equipment__frost_set':       'equipment/frost-set.md',
  'equipment__thunder_set':     'equipment/thunder-set.md',
  'item__overview':             'items/index.md',
  'item__key_items':            'items/key-items.md',
  'item__npc_items':            'items/npc-items.md',
  'item__consumables':          'items/consumables.md',
  'item__mode_f_items':         'items/mode-items.md',
  'system__rewards_and_loot':   'systems/loot-rewards.md',
  'system__death_wraith':       'systems/death-wraith.md',
  'system__wish_fountain':      'systems/starwish-fountain.md',
  'system__reforge_and_achievements': 'systems/reforge.md',
  'system__boss_filter_and_wiki':     'systems/boss-filter.md',
  'system__achievements_list':        'achievements/index.md',
  'system__mutators':                 'systems/mutators.md',
  'system__pet_nest':                 'systems/petnest.md',
  'system__daily_report':             'systems/daily-report.md',
  'system__codex':                    'systems/codex.md',
  'system__random_events':            'systems/random-events.md',
  'system__affix_forge':              'systems/affix-forge.md',
  'system__campaign':                 'systems/campaign.md',
  'system__back_mountain':            'systems/arena-backyard.md',
  'config__overview':                 'systems/configuration.md',
  'tips__new_player_route':     'guides/beginner-route.md',
  'tips__hell_and_mode_d':      'guides/hell-and-mode-d.md',
  'tips__mode_e_strategy':      'guides/mode-e-strategy.md',
  'tips__boss_fights':          'guides/boss-fights.md',
  'tips__mode_f_strategy':      'guides/mode-f-strategy.md',
  'tips__mode_g_strategy':      'guides/mode-g-strategy.md',
  'easter__kunkun':             'easter-eggs.md',
  'changelog__highlights':      'changelog/index.md',
  'changelog__legacy_archive':  'changelog/legacy-archive.md',
};

const VERSION_ENTRY = /^changelog__v(\d+)_(\d+)_(\d+)$/;
const VERSION_ROUTE = /^changelog\/v(\d+)\.(\d+)\.(\d+)\.md$/;

/** entryId → 'bosses/dragon-king.md'；更新日志版本页按规则生成；认不出返回 null */
export function getRoute(entryId) {
  if (entryId in ENTRY_TO_PATH) return ENTRY_TO_PATH[entryId];
  const m = entryId.match(VERSION_ENTRY);
  if (m) return `changelog/v${m[1]}.${m[2]}.${m[3]}.md`;
  return null;
}

const PATH_TO_ENTRY = new Map(Object.entries(ENTRY_TO_PATH).map(([id, route]) => [route, id]));

/** getRoute 的反函数：'bosses/dragon-king.md' → 'boss__dragon_king'；认不出返回 null */
export function entryIdOfRoute(route) {
  const hit = PATH_TO_ENTRY.get(route);
  if (hit) return hit;
  const m = route.match(VERSION_ROUTE);
  if (m) return `changelog__v${m[1]}_${m[2]}_${m[3]}`;
  return null;
}

/**
 * 读 WikiContent/catalog.tsv。列：categoryId / entryId / titleZh / titleEn / order。
 * 这里不排序、不过滤——调用方各取所需（sync 要全部，更新日志只要 changelog 类）。
 */
export function readCatalog() {
  const raw = readFileSync(CATALOG_PATH, 'utf-8');
  const rows = [];
  for (const line of raw.trim().split('\n').slice(1)) {
    const cols = line.split('\t');
    if (cols.length < 5) continue;
    rows.push({
      categoryId: cols[0],
      entryId: cols[1],
      titleZh: cols[2],
      titleEn: cols[3],
      order: parseInt(cols[4], 10),
    });
  }
  return rows;
}

/** 'changelog__v2_1_9' → [2, 1, 9]；非版本条目返回 null */
export function versionOf(entryId) {
  const m = entryId.match(VERSION_ENTRY);
  return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : null;
}
