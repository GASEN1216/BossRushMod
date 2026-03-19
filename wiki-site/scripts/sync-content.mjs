/**
 * sync-content.mjs
 *
 * 中文内容：直接从 docs/wiki-site/ 复制到 wiki-site/docs/（权威源）
 * 英文内容：从 WikiContent/en/ 读取，转换后写入 wiki-site/docs/en/
 *
 * 转换逻辑（仅英文）：
 *   1. 标题层级提升：## → #, ### → ##, #### → ###
 *   2. Callout 转换：[tip] → ::: tip, [warn] → ::: warning
 *   3. 注入 frontmatter（title）
 */

import { readFileSync, writeFileSync, mkdirSync, existsSync, rmSync, cpSync, readdirSync, statSync } from 'fs';
import { join, dirname, relative, extname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const MOD_ROOT = join(__dirname, '..', '..');              // BossRushMod 根目录
const ZH_SOURCE = join(MOD_ROOT, 'docs', 'wiki-site');    // 中文权威源
const EN_WIKI = join(MOD_ROOT, 'WikiContent', 'en');       // 英文 WikiContent 源
const CATALOG = join(MOD_ROOT, 'WikiContent', 'catalog.tsv');
const DOCS_DIR = join(__dirname, '..', 'docs');            // VitePress srcDir

// ── docs/wiki-site 的实际目录结构 ─────────────────────────
// 这些目录会被 sync 管理（清理 + 复制）
const CONTENT_DIRS = [
  'getting-started', 'game-modes', 'bosses', 'equipment',
  'items', 'npcs', 'maps', 'systems', 'achievements',
  'guides', 'changelog',
];
const CONTENT_FILES = ['easter-eggs.md'];

// ── entryId → docs/wiki-site 实际路径 的映射 ─────────────
// 用于英文内容生成：从 WikiContent/en 的 entryId 找到对应的输出路径
const ENTRY_TO_PATH = {
  'start__overview':            'getting-started/overview.md',
  'start__how_to_enter':        'getting-started/installation.md',
  'start__first_run':           'getting-started/first-steps.md',
  'mode__overview':             'game-modes/index.md',
  'mode__mode_a':               'game-modes/standard.md',  // A+B 合并为 standard
  'mode__mode_b':               null,                       // 跳过，已合并到 standard
  'mode__mode_c':               'game-modes/infinite-hell.md',
  'mode__mode_d':               'game-modes/mode-d.md',
  'mode__mode_e':               'game-modes/mode-e.md',
  'mode__mode_f':               'game-modes/mode-f.md',
  'map__overview':              'maps/index.md',
  'map__all_maps':              null,                       // 合并到 maps/index
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

function getEnRoute(entryId) {
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

// ── 查找英文 WikiContent 源文件 ───────────────────────────
function findEnSourceFile(entryId, categoryId) {
  const fileName = `${entryId}.md`;
  const subDir = join(EN_WIKI, categoryId, fileName);
  if (existsSync(subDir)) return subDir;
  const rootLevel = join(EN_WIKI, fileName);
  if (existsSync(rootLevel)) return rootLevel;
  return null;
}

// ── 英文内容转换 ─────────────────────────────────────────
function transformEnContent(raw) {
  let content = raw;

  // 标题层级提升（WikiContent 用 ## 做页面标题，VitePress 需要 #）
  content = content.replace(/^####\s/gm, '### ');
  content = content.replace(/^###\s/gm, '## ');
  content = content.replace(/^##\s/gm, '# ');

  // Callout 转换
  content = content.replace(/^\[tip\]\s*(.+)$/gm, '::: tip\n$1\n:::');
  content = content.replace(/^\[warn\]\s*(.+)$/gm, '::: warning\n$1\n:::');

  // 清理本地绝对路径链接（如 [text](/E:/... ) 或 [text](/C:/...)）
  content = content.replace(/\[([^\]]*)\]\(\/[A-Za-z]:[^\)]*\)/g, '$1');

  return content;
}

// ── 清理输出目录中的生成内容 ─────────────────────────────
function cleanOutput() {
  // 清理中文内容目录
  for (const dir of CONTENT_DIRS) {
    const zhDir = join(DOCS_DIR, dir);
    if (existsSync(zhDir)) rmSync(zhDir, { recursive: true, force: true });
  }
  for (const f of CONTENT_FILES) {
    const zhF = join(DOCS_DIR, f);
    if (existsSync(zhF)) rmSync(zhF, { force: true });
  }
  // 清理英文内容目录
  const enDir = join(DOCS_DIR, 'en');
  if (existsSync(enDir)) rmSync(enDir, { recursive: true, force: true });
}

// ── 递归复制目录（排除 README.md 和 index.md 首页） ──────
function copyDirRecursive(src, dest, excludeFiles = []) {
  if (!existsSync(src)) return 0;
  mkdirSync(dest, { recursive: true });
  let count = 0;
  for (const entry of readdirSync(src)) {
    const srcPath = join(src, entry);
    const destPath = join(dest, entry);
    const stat = statSync(srcPath);
    if (stat.isDirectory()) {
      count += copyDirRecursive(srcPath, destPath, excludeFiles);
    } else if (extname(entry) === '.md' && !excludeFiles.includes(entry)) {
      writeFileSync(destPath, readFileSync(srcPath, 'utf-8'), 'utf-8');
      count++;
    }
  }
  return count;
}

// ── 主流程 ────────────────────────────────────────────────
function main() {
  console.log('[sync] 开始同步...');
  cleanOutput();

  // ─── 1. 中文：从 docs/wiki-site/ 复制 ───
  let zhCount = 0;

  // 复制内容目录
  for (const dir of CONTENT_DIRS) {
    const src = join(ZH_SOURCE, dir);
    const dest = join(DOCS_DIR, dir);
    zhCount += copyDirRecursive(src, dest);
  }

  // 复制单独文件
  for (const f of CONTENT_FILES) {
    const src = join(ZH_SOURCE, f);
    if (existsSync(src)) {
      writeFileSync(join(DOCS_DIR, f), readFileSync(src, 'utf-8'), 'utf-8');
      zhCount++;
    }
  }

  // 补充中文内容：docs/wiki-site/ 缺失的文件从 WikiContent/zh/ 转换补充
  const ZH_WIKI = join(MOD_ROOT, 'WikiContent', 'zh');
  let zhSupplementCount = 0;
  const catalogEntries = parseCatalog();
  for (const entry of catalogEntries) {
    const route = getEnRoute(entry.entryId); // 路由映射中英文共用
    if (!route) continue;
    const outPath = join(DOCS_DIR, route);
    // 如果 docs/wiki-site/ 已经提供了该文件，跳过
    if (existsSync(outPath)) continue;
    // 从 WikiContent/zh/ 查找源文件
    const fileName = `${entry.entryId}.md`;
    let srcPath = join(ZH_WIKI, entry.categoryId, fileName);
    if (!existsSync(srcPath)) srcPath = join(ZH_WIKI, fileName);
    if (!existsSync(srcPath)) {
      console.warn(`[sync] 中文源缺失: ${entry.entryId}`);
      continue;
    }
    const raw = readFileSync(srcPath, 'utf-8');
    const transformed = transformEnContent(raw); // 同样做标题提升和 callout 转换
    mkdirSync(dirname(outPath), { recursive: true });
    writeFileSync(outPath, transformed, 'utf-8');
    zhCount++;
    zhSupplementCount++;
  }
  if (zhSupplementCount > 0) {
    console.log(`[sync] 中文补充: ${zhSupplementCount} 篇（从 WikiContent/zh/ 转换）`);
  }

  console.log(`[sync] 中文合计: ${zhCount} 篇`);

  // ─── 2. 英文：从 WikiContent/en/ 转换生成 ───
  const catalog = parseCatalog();
  let enCount = 0, skipped = 0;

  for (const entry of catalog) {
    const route = getEnRoute(entry.entryId);
    if (!route) { skipped++; continue; }

    const srcPath = findEnSourceFile(entry.entryId, entry.categoryId);
    if (!srcPath) {
      console.warn(`[sync] 英文源缺失: ${entry.entryId}`);
      skipped++;
      continue;
    }

    const raw = readFileSync(srcPath, 'utf-8');
    const transformed = transformEnContent(raw);
    const outPath = join(DOCS_DIR, 'en', route);

    mkdirSync(dirname(outPath), { recursive: true });
    writeFileSync(outPath, transformed, 'utf-8');
    enCount++;
  }

  console.log(`[sync] 英文: ${enCount} 篇（从 WikiContent/en/ 转换）, 跳过 ${skipped}`);

  // ─── 3. 英文首页 ───
  // 如果 docs/en/index.md 不存在，生成一个基础版本
  const enIndexPath = join(DOCS_DIR, 'en', 'index.md');
  if (!existsSync(enIndexPath)) {
    generateEnIndex(enIndexPath);
    console.log('[sync] 生成英文首页');
  }

  console.log('[sync] 同步完成！');
}

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

main();
