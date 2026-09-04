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
 *   4. 配图注入：按 IMAGE_PLACEMENT 把 image-manifest.json 里的图插进指定小节
 *
 * 关于配图为什么只在这一侧（重要）：
 *   WikiContent/ 同时喂**游戏内 Wiki 书**和本站点。游戏内解析器
 *   （Integration/WikiContentManager.cs:144 的 RxMdLink）没有图片规则，
 *   `![alt](src)` 会被它的链接正则吃掉 `[alt](src)` 而把 `!` 留在原地，
 *   渲染成「一个野生的 ! + 一条指向不存在路径的蓝色下划线可点链接」。
 *   所以 **WikiContent/ 必须保持纯文本**，配图只在同步到 wiki-site 时注入。
 *   代价是文字与配图分两处维护，换来的是游戏内书一个字都不受影响。
 *
 *   图片产物与 image-manifest.json 由 tools/build_wiki_images.py 离线生成并提交
 *   （源图在 Assets/ 下，被 .gitignore 挡着，CI 里看不到）。
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

// ── 配图 ────────────────────────────────────────────────
// 清单由 tools/build_wiki_images.py 生成；缺文件时降级为「不配图」，不阻断同步。
const MANIFEST_PATH = join(__dirname, 'image-manifest.json');
let IMAGES = { codex: [], campaign: [], icons: [] };
try {
  IMAGES = JSON.parse(readFileSync(MANIFEST_PATH, 'utf-8'));
} catch {
  console.warn('[sync] image-manifest.json 缺失或损坏，本次不注入配图');
}

const imageByKey = new Map();
for (const group of Object.values(IMAGES)) {
  for (const item of group ?? []) imageByKey.set(item.key, item);
}

/**
 * entryId → 配图位置。
 *
 * `at` 是**标题序号**（从 0 数，含页面标题），不是标题文字：
 * 中英两版的标题层级序列逐条一致（同步时会校验，不一致直接报错），
 * 所以同一个序号在两种语言里指向同一小节，比匹配标题文字稳得多。
 *
 * `mode`：
 *   'after-heading' —— 插在该标题行的正下方（配图先于正文，适合章节海报）
 *   'section-end'   —— 插在该小节末尾、下一个标题之前（适合总结性的画廊）
 */
const IMAGE_PLACEMENT = {
  system__codex: [
    // [2] = 「怎么打开」：讲在商店买书，旁边就给出书长什么样
    { at: 2, mode: 'section-end', kind: 'icon', keys: ['codex-book'] },
    // [5] = 「哪些 Boss 会进图鉴」：讲完收录范围紧接着摆立绘，正好对上
    { at: 5, mode: 'section-end', kind: 'gallery', group: 'codex' },
  ],
  system__campaign: [
    { at: 1, mode: 'section-end', kind: 'figure', keys: ['portrait-broker'] },
    { at: 3, mode: 'section-end', kind: 'icon', keys: ['campaign-board'] },
    { at: 8, mode: 'after-heading', kind: 'figure', keys: ['poster-ch1'] },
    { at: 9, mode: 'after-heading', kind: 'figure', keys: ['poster-ch2'] },
    { at: 10, mode: 'after-heading', kind: 'figure', keys: ['poster-ch3'] },
    { at: 11, mode: 'after-heading', kind: 'figure', keys: ['poster-ch4'] },
    { at: 12, mode: 'after-heading', kind: 'figure', keys: ['poster-ch5'] },
    { at: 13, mode: 'after-heading', kind: 'figure', keys: ['poster-ch6'] },
    { at: 14, mode: 'after-heading', kind: 'figure', keys: ['portrait-champion'] },
  ],
  system__daily_report: [
    { at: 2, mode: 'section-end', kind: 'icon', keys: ['daily-mailbox'] },
  ],
  system__back_mountain: [
    { at: 4, mode: 'section-end', kind: 'icon', keys: ['showcase'] },
  ],
  system__pet_nest: [
    { at: 4, mode: 'section-end', kind: 'icon', keys: ['petnest', 'relic-egg'] },
  ],
  system__affix_forge: [
    { at: 3, mode: 'section-end', kind: 'icon', keys: ['forge-stone'] },
  ],
};

const KIND_CLASS = { gallery: 'brs-gallery', figure: 'brs-figure', icon: 'brs-icon' };

/**
 * 渲染一个配图块。
 *
 * 刻意用 **markdown 图片语法**而不是裸 <img>：VitePress 只给 markdown 图片自动补
 * base 前缀（部署到 GitHub Pages 是 /BossRushMod/，Cloudflare 是 /），裸 <img src>
 * 不补，换个部署目标就整片 404。
 *
 * 外面那层 <div> 与内容之间留空行 —— CommonMark 的 HTML 块遇空行即结束，
 * 于是中间的内容照常按 markdown 解析。每张图与它的说明文字构成一个段落，
 * 也就是网格里的一个格子（样式见 theme/style.css §14）。
 */
function renderImageBlock(kind, items, lang) {
  if (!items.length) return '';
  const cells = items.map((item) => {
    const caption = (lang === 'en' ? item.en : item.zh) || item.key;
    const alt = caption.replace(/[[\]]/g, '');
    return `![${alt}](${item.src})\n*${caption}*`;
  });
  return `<div class="${KIND_CLASS[kind]}">\n\n${cells.join('\n\n')}\n\n</div>`;
}

/** 把配图插进已转换的正文。entryId 没登记配图时原样返回。 */
function injectImages(content, entryId, lang) {
  const plan = IMAGE_PLACEMENT[entryId];
  if (!plan || imageByKey.size === 0) return content;

  const lines = content.split('\n');
  // 标题序号 → 行号
  const headingLines = [];
  for (let i = 0; i < lines.length; i++) {
    if (/^#{1,6}[ \t]/.test(lines[i])) headingLines.push(i);
  }

  // 收集插入点，最后倒序插入，避免前面的插入把后面的行号顶偏
  const inserts = [];
  for (const spec of plan) {
    const headIdx = headingLines[spec.at];
    if (headIdx === undefined) {
      console.warn(`[sync] ${entryId}: 标题序号 ${spec.at} 不存在，跳过该配图`);
      continue;
    }

    const items = spec.kind === 'gallery'
      ? (IMAGES[spec.group] ?? [])
      : spec.keys.map((k) => imageByKey.get(k)).filter(Boolean);
    if (!items.length) {
      console.warn(`[sync] ${entryId}: 配图 ${spec.keys ?? spec.group} 在清单里找不到，跳过`);
      continue;
    }

    let insertAt;
    if (spec.mode === 'after-heading') {
      insertAt = headIdx + 1;
    } else {
      // section-end：下一个标题之前；顺带回退掉尾部空行，避免堆出多余空段
      let end = headingLines.find((n) => n > headIdx) ?? lines.length;
      while (end > headIdx + 1 && lines[end - 1].trim() === '') end--;
      insertAt = end;
    }
    inserts.push({ insertAt, block: renderImageBlock(spec.kind, items, lang) });
  }

  inserts.sort((a, b) => b.insertAt - a.insertAt);
  for (const { insertAt, block } of inserts) {
    lines.splice(insertAt, 0, '', block, '');
  }
  return lines.join('\n');
}

// ── entryId → wiki-site 路径映射（中英文共用）────────────
const ENTRY_TO_PATH = {
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
  // 单次回调、层级无关。不要拆成三次 replace：那会级联（#### → ### → ## → #），
  // 把所有层级压成 #；且 /^####[ \t]/ 匹配不了 ##### （第 4 位是 # 不是空白）。
  // 注意：tests/ZombieModeMutantWikiGuard.py 用 Python 镜像了本函数并做逐字节比对，
  // 改这里必须同步改那边。
  content = content.replace(
    /^(#{2,6})([ \t])/gm,
    (_m, hashes, space) => '#'.repeat(hashes.length - 1) + space
  );

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
function syncLanguage(wikiDir, outBase, langLabel, langKey) {
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
    const transformed = injectImages(transformContent(raw), entry.entryId, langKey);
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
  text: Official Wiki
  tagline: The ultimate guide to BossRush Mod — game modes, bosses, equipment, and more.
  actions:
    - theme: brand
      text: Getting Started
      link: /en/getting-started/overview
    - theme: alt
      text: Game Modes
      link: /en/game-modes/

features:
  - title: 7 Game Modes
    details: Standard BossRush, Infinite Hell, From Scratch, Faction War, Blood Hunt, Fate Echo, Zombie Mode
  - title: Custom Bosses
    details: Dragon Descendant, Skyburner Dragon Lord, and Phantom Witch with unique skill sets
  - title: Equipment System
    details: Dragon sets, totems, legendary weapons, plus new frost/thunder gear
  - title: NPC System
    details: Goblin Smith, Nurse, Courier — affinity, gifting, and marriage
  - title: Run Mutators
    details: 1–10 random mutators per run change enemy, player, and environment rules
  - title: Reforge System
    details: Reroll equipment stats and lock affixes with Cold Quench Fluid
---
`;
  mkdirSync(dirname(outPath), { recursive: true });
  writeFileSync(outPath, content, 'utf-8');
}

/**
 * 校验配图页的中英标题层级序列一致。
 *
 * IMAGE_PLACEMENT 用标题序号定位，这个前提一旦被打破（某一语言多了/少了一节），
 * 配图会**静默插到错误的小节**——图还在、页面也不报错，只是位置不对，
 * 属于最难被发现的一类错误。所以这里直接抛，让 sync 当场失败。
 */
function verifyPlacementParity() {
  const levels = (dir, entryId, categoryId) => {
    const p = findSourceFile(dir, entryId, categoryId);
    if (!p) return null;
    return readFileSync(p, 'utf-8')
      .split('\n')
      .map((l) => /^(#{2,6})[ \t]/.exec(l))
      .filter(Boolean)
      .map((m) => m[1].length)
      .join(',');
  };

  const catalog = parseCatalog();
  for (const entry of catalog) {
    if (!IMAGE_PLACEMENT[entry.entryId]) continue;
    const z = levels(ZH_WIKI, entry.entryId, entry.categoryId);
    const e = levels(EN_WIKI, entry.entryId, entry.categoryId);
    if (z === null || e === null) continue;
    if (z !== e) {
      throw new Error(
        `[sync] ${entry.entryId} 的中英标题结构不一致，配图会插错位置。\n` +
        `  zh: ${z}\n  en: ${e}\n` +
        `  修法：让两版小节一一对应，或把该条目从 IMAGE_PLACEMENT 摘掉。`);
    }
  }
}

// ── 主流程 ────────────────────────────────────────────────
function main() {
  console.log('[sync] 开始同步（权威源：WikiContent/）...');
  console.log('');
  verifyPlacementParity();
  cleanOutput();

  // 1. 中文：WikiContent/zh/ → wiki-site/docs/
  const zh = syncLanguage(ZH_WIKI, DOCS_DIR, '中文', 'zh');
  console.log(`[sync] 中文: ${zh.count} 篇, 跳过 ${zh.skipped}`);

  // 2. 英文：WikiContent/en/ → wiki-site/docs/en/
  const enBase = join(DOCS_DIR, 'en');
  const en = syncLanguage(EN_WIKI, enBase, '英文', 'en');
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
