/**
 * sync-content.mjs
 *
 * 唯一权威源：WikiContent/zh/ 和 WikiContent/en/
 * 只需维护 WikiContent 目录，运行此脚本即可同步到 wiki-site/docs/
 *
 * 转换逻辑（中英文统一）：
 *   1. 标题层级提升：## → #, ### → ##, #### → ###
 *   2. Callout 转换：[tip] → ::: tip, [warn] → ::: warning
 *   3. 清理本地绝对路径链接
 */

import { readFileSync, writeFileSync, mkdirSync, existsSync, rmSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const MOD_ROOT = join(__dirname, '..', '..');
const ZH_WIKI = join(MOD_ROOT, 'WikiContent', 'zh');
const EN_WIKI = join(MOD_ROOT, 'WikiContent', 'en');
const CATALOG = join(MOD_ROOT, 'WikiContent', 'catalog.tsv');
const DOCS_DIR = join(__dirname, '..', 'docs');

// ── 这些目录由 sync 管理（清理 + 生成）──────────────────
const CONTENT_DIRS = [
  'getting-started', 'game-modes', 'bosses', 'equipment',
  'items', 'npcs', 'maps', 'systems', 'achievements',
  'guides', 'changelog',
];
const CONTENT_FILES = ['easter-eggs.md'];

// ── entryId → wiki-site 路径映射（中英文共用）────────────
const ENTRY_TO_PATH = {
  'start__overview':            'getting-started/overview.md',
  'start__how_to_enter':        'getting-started/installation.md',
  'start__first_run':           'getting-started/first-steps.md',
  'mode__overview':             'game-modes/index.md',
  'mode__mode_a':               'game-modes/standard.md',
  'mode__mode_b':               null,
  'mode__mode_c':               'game-modes/infinite-hell.md',
  'mode__mode_d':               'game-modes/mode-d.md',
  'mode__mode_e':               'game-modes/mode-e.md',
  'mode__mode_f':               'game-modes/mode-f.md',
  'map__overview':              'maps/index.md',
  'map__all_maps':              null,
  'boss__overview':             'bosses/index.md',
  'boss__dragon_descendant':    'bosses/dragon-descendant.md',
  'boss__dragon_king':          'bosses/dragon-king.md',
  'npc__overview':              'npcs/index.md',
  'npc__goblin':                'npcs/goblin.md',
  'npc__nurse':                 'npcs/nurse.md',
  'npc__courier':               'npcs/courier.md',
  'npc__affinity_and_marriage': 'systems/affinity-marriage.md',
  'equipment__overview':        'equipment/index.md',
  'equipment__dragon_set':      'equipment/dragon-set.md',
  'equipment__dragon_king_set': 'equipment/dragon-king-set.md',
  'equipment__flight_totem':    'equipment/flight-totem.md',
  'equipment__reverse_scale':   'equipment/reverse-scale.md',
  'equipment__halberd':         'equipment/halberd.md',
  'equipment__dragon_breath':   'equipment/dragon-breath.md',
  'equipment__dragon_cannon':   'equipment/dragon-cannon.md',
  'item__overview':             'items/index.md',
  'item__key_items':            'items/key-items.md',
  'item__npc_items':            'items/npc-items.md',
  'item__consumables':          'items/consumables.md',
  'item__mode_f_items':         'items/mode-items.md',
  'system__rewards_and_loot':   'systems/loot-rewards.md',
  'system__reforge_and_achievements': 'systems/reforge.md',
  'system__boss_filter_and_wiki':     'systems/boss-filter.md',
  'system__achievements_list':        'achievements/index.md',
  'config__overview':                 'systems/configuration.md',
  'tips__new_player_route':     'guides/beginner-route.md',
  'tips__hell_and_mode_d':      'guides/hell-and-mode-d.md',
  'tips__mode_e_strategy':      'guides/mode-e-strategy.md',
  'tips__boss_fights':          'guides/boss-fights.md',
  'tips__mode_f_strategy':      'guides/mode-f-strategy.md',
  'easter__kunkun':             'easter-eggs.md',
  'changelog__highlights':      'changelog/index.md',
  'changelog__legacy_archive':  'changelog/legacy-archive.md',
};

function getRoute(entryId) {
  if (entryId in ENTRY_TO_PATH) return ENTRY_TO_PATH[entryId];
  const vMatch = entryId.match(/^changelog__v(\d+)_(\d+)_(\d+)$/);
  if (vMatch) return `changelog/v${vMatch[1]}.${vMatch[2]}.${vMatch[3]}.md`;
  return null;
}

// ── 解析 catalog.tsv ──────────────────────────────────────
function parseCatalog() {
  const raw = readFileSync(CATALOG, 'utf-8');
  const lines = raw.trim().split('\n');
  const entries = [];
  for (let i = 1; i < lines.length; i++) {
    const cols = lines[i].split('\t');
    if (cols.length < 5) continue;
    entries.push({
      categoryId: cols[0],
      entryId:    cols[1],
      titleZh:    cols[2],
      titleEn:    cols[3],
      order:      parseInt(cols[4], 10),
    });
  }
  return entries;
}

// ── 查找 WikiContent 源文件（先找子目录，再找根目录）─────
function findSourceFile(wikiDir, entryId, categoryId) {
  const fileName = `${entryId}.md`;
  const subDir = join(wikiDir, categoryId, fileName);
  if (existsSync(subDir)) return subDir;
  const rootLevel = join(wikiDir, fileName);
  if (existsSync(rootLevel)) return rootLevel;
  return null;
}

// ── WikiContent → VitePress 格式转换 ─────────────────────
function transformContent(raw) {
  let content = raw;

  // 标题层级提升（WikiContent 用 ## 做页面标题，VitePress 需要 #）
  content = content.replace(/^####\s/gm, '### ');
  content = content.replace(/^###\s/gm, '## ');
  content = content.replace(/^##\s/gm, '# ');

  // Callout 转换
  content = content.replace(/^\[tip\]\s*(.+)$/gm, '::: tip\n$1\n:::');
  content = content.replace(/^\[warn\]\s*(.+)$/gm, '::: warning\n$1\n:::');

  // 清理本地绝对路径链接
  content = content.replace(/\[([^\]]*)\]\(\/[A-Za-z]:[^\)]*\)/g, '$1');

  return content;
}

// ── 清理输出目录 ─────────────────────────────────────────
function cleanOutput() {
  for (const dir of CONTENT_DIRS) {
    const zhDir = join(DOCS_DIR, dir);
    if (existsSync(zhDir)) rmSync(zhDir, { recursive: true, force: true });
  }
  for (const f of CONTENT_FILES) {
    const zhF = join(DOCS_DIR, f);
    if (existsSync(zhF)) rmSync(zhF, { force: true });
  }
  const enDir = join(DOCS_DIR, 'en');
  if (existsSync(enDir)) rmSync(enDir, { recursive: true, force: true });
}

// ── 同步单语言 ──────────────────────────────────────────
function syncLanguage(wikiDir, outBase, langLabel) {
  const catalog = parseCatalog();
  let count = 0, skipped = 0;

  for (const entry of catalog) {
    const route = getRoute(entry.entryId);
    if (!route) { skipped++; continue; }

    const srcPath = findSourceFile(wikiDir, entry.entryId, entry.categoryId);
    if (!srcPath) {
      console.warn(`[sync] ${langLabel}源缺失: ${entry.entryId}`);
      skipped++;
      continue;
    }

    const raw = readFileSync(srcPath, 'utf-8');
    const transformed = transformContent(raw);
    const outPath = join(outBase, route);

    mkdirSync(dirname(outPath), { recursive: true });
    writeFileSync(outPath, transformed, 'utf-8');
    count++;
  }

  return { count, skipped };
}

// ── 生成英文首页 ─────────────────────────────────────────
function generateEnIndex(outPath) {
  const content = `---
layout: home
hero:
  name: BossRush Mod
  text: Escape from Duckov Wiki
  tagline: The ultimate guide to BossRush Mod — game modes, bosses, equipment, and more.
  actions:
    - theme: brand
      text: Getting Started
      link: /en/getting-started/overview
    - theme: alt
      text: Game Modes
      link: /en/game-modes/

features:
  - icon: ⚔️
    title: 5 Game Modes
    details: Standard BossRush, Infinite Hell, From Scratch, Faction War, Blood Hunt
  - icon: 🐉
    title: Custom Bosses
    details: Dragon Descendant and Skyburner Dragon Lord with unique skill sets
  - icon: 🛡️
    title: Equipment System
    details: Dragon Set, Dragon King Set, totems, and legendary weapons
  - icon: 👥
    title: NPC System
    details: Goblin Smith, Nurse, Courier — affinity, gifting, and marriage
  - icon: 🏆
    title: 35 Achievements
    details: Completion, no-hit, speedrun, collection, and more
  - icon: 🔧
    title: Reforge System
    details: Reroll equipment stats and lock affixes with Cold Quench Fluid
---
`;
  mkdirSync(dirname(outPath), { recursive: true });
  writeFileSync(outPath, content, 'utf-8');
}

// ── 主流程 ────────────────────────────────────────────────
function main() {
  console.log('[sync] 开始同步（权威源：WikiContent/）...');
  console.log('');
  cleanOutput();

  // 1. 中文：WikiContent/zh/ → wiki-site/docs/
  const zh = syncLanguage(ZH_WIKI, DOCS_DIR, '中文');
  console.log(`[sync] 中文: ${zh.count} 篇, 跳过 ${zh.skipped}`);

  // 2. 英文：WikiContent/en/ → wiki-site/docs/en/
  const enBase = join(DOCS_DIR, 'en');
  const en = syncLanguage(EN_WIKI, enBase, '英文');
  console.log(`[sync] 英文: ${en.count} 篇, 跳过 ${en.skipped}`);

  // 3. 英文首页
  const enIndexPath = join(enBase, 'index.md');
  if (!existsSync(enIndexPath)) {
    generateEnIndex(enIndexPath);
    console.log('[sync] 生成英文首页');
  }

  console.log('');
  console.log(`[sync] 同步完成！共 ${zh.count + en.count} 篇`);
}

main();
