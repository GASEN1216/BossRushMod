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
 *   5. 列表转表格：按 TABLEIZE 把固定句式的列表段落排成表格
 *   6. 类目主页：把 wiki-site/hubs/ 下手写的系统 / 攻略主页复制进 docs/
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

import { readFileSync, writeFileSync, mkdirSync, existsSync, rmSync, copyFileSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';
import { MOD_ROOT, getRoute, readCatalog } from './entry-map.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const ZH_WIKI = join(MOD_ROOT, 'WikiContent', 'zh');
const EN_WIKI = join(MOD_ROOT, 'WikiContent', 'en');
const DOCS_DIR = join(__dirname, '..', 'docs');
const HUB_DIR = join(__dirname, '..', 'hubs');

// ── 这些目录由 sync 管理（清理 + 生成）──────────────────
const CONTENT_DIRS = [
  'getting-started', 'game-modes', 'bosses', 'equipment',
  'items', 'npcs', 'maps', 'systems', 'achievements',
  'guides', 'changelog',
];
const CONTENT_FILES = ['easter-eggs.md'];

// ── 配图 ────────────────────────────────────────────────
// 清单由 tools/build_wiki_images.py 生成；缺文件时降级为「不配图」，不阻断同步。
// 放置表与多图文案的单一事实源为 WikiContent/illustrations.json（供站点与游戏内百科共用）。
const MANIFEST_PATH = join(__dirname, 'image-manifest.json');
const ILLUSTRATIONS_PATH = join(MOD_ROOT, 'WikiContent', 'illustrations.json');
let IMAGES = { codex: [], campaign: [], icons: [], diagrams: [], scenes: [] };
try {
  IMAGES = JSON.parse(readFileSync(MANIFEST_PATH, 'utf-8'));
} catch {
  console.warn('[sync] image-manifest.json 缺失或损坏，本次不注入配图');
}

let IMAGE_PLACEMENT = {};
let ILLUSTRATION_ASSETS = {};
try {
  const illData = JSON.parse(readFileSync(ILLUSTRATIONS_PATH, 'utf-8'));
  IMAGE_PLACEMENT = illData.entries ?? {};
  ILLUSTRATION_ASSETS = illData.assets ?? {};
} catch {
  console.warn('[sync] WikiContent/illustrations.json 缺失或损坏，本次不注入配图');
}

const imageByKey = new Map();
for (const group of Object.values(IMAGES)) {
  for (const item of group ?? []) {
    const assetMeta = ILLUSTRATION_ASSETS[item.key];
    const enriched = assetMeta
      ? {
          ...item,
          srcEn: item.srcEn || assetMeta.webSrcEn || undefined,
          zh: item.zh || assetMeta.zh,
          en: item.en || assetMeta.en,
        }
      : item;
    imageByKey.set(item.key, enriched);
  }
}

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
    const meta = ILLUSTRATION_ASSETS[item.key];
    const caption = (lang === 'en' ? (item.en || meta?.en) : (item.zh || meta?.zh)) || item.key;
    const alt = caption.replace(/[[\]]/g, '');
    const src = (lang === 'en' && (item.srcEn || meta?.webSrcEn)) || item.src;
    return `![${alt}](${src})\n*${caption}*`;
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

    const items = (spec.keys && spec.keys.length)
      ? spec.keys.map((k) => imageByKey.get(k)).filter(Boolean)
      : (spec.group ? (IMAGES[spec.group] ?? []) : []);
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

// ── 列表转表格 ──────────────────────────────────────────
/**
 * 把「每行都是同一种 key-value 句式」的列表段落转成表格。
 *
 * 只对**在线站**做，而且只对登记过的 entryId 做：
 *   - WikiContent/ 必须保持纯文本，游戏内解析器不认表格（会原样显示成竖线），
 *     所以源文件那边只能是列表；
 *   - 只登记确实值得表格化的页面。成就大全是最典型的一个——45 条
 *     「名称：条件。奖励 X，难度 Y」写成项目符号，读者想按奖金或难度比较就得逐行读；
 *     排成四列之后一眼可扫。
 *
 * 全有或全无：一个连续列表段里只要有一行不匹配，整段原样保留。
 * 这样作者哪天换了句式，最坏结果是「表格变回列表」，而不是掉数据或出半截表。
 */
const TABLEIZE = {
  system__achievements_list: {
    zh: {
      head: ['成就', '解锁条件', '奖金', '难度'],
      re: /^- (.+?)[：:](.+?)奖励 `([^`]+)`[，,]\s*难度 `([^`]+)`\s*$/,
    },
    en: {
      head: ['Achievement', 'Requirement', 'Reward', 'Difficulty'],
      re: /^- (.+?): (.+?)Reward `([^`]+)`,\s*difficulty `([^`]+)`\s*$/i,
    },
  },
  // 模式总览的「一张表看明白」：八行「**模式** — 入场 / 波次 / 玩法 / 难度 ★」。
  // 首列连加粗一起保留，config.mts 的 entityLinkPlugin 会把它换成「图标 + 链接」。
  mode__overview: {
    zh: {
      head: ['模式', '入场', '波次', '玩法', '难度'],
      re: /^- (\*\*.+?\*\*) — (.+?) \/ (.+?) \/ (.+?) \/ 难度 (\S+)\s*$/,
    },
    en: {
      head: ['Mode', 'Entry', 'Waves', 'What it is', 'Difficulty'],
      re: /^- (\*\*.+?\*\*) — (.+?) \/ (.+?) \/ (.+?) \/ (★[★☆]*)\s*$/,
    },
  },
};

/** 单元格里的竖线会把表格结构撑断，转义掉。 */
function cell(text) {
  return text.trim().replace(/\|/g, '\\|');
}

function tableizeLists(content, entryId, lang) {
  const spec = TABLEIZE[entryId] && TABLEIZE[entryId][lang];
  if (!spec) return content;

  const lines = content.split('\n');
  const out = [];
  let converted = 0;

  for (let i = 0; i < lines.length; ) {
    if (!lines[i].startsWith('- ')) {
      out.push(lines[i++]);
      continue;
    }

    // 收下这一整段连续的列表行
    let end = i;
    while (end < lines.length && lines[end].startsWith('- ')) end++;
    const run = lines.slice(i, end);

    // 单条也转：「终极成就」那一节只有一条，留成孤零零一个项目符号夹在八张表中间
    // 反而更不协调。误判风险由 TABLEIZE 的按页登记兜住，不靠条数。
    const rows = run.map((line) => spec.re.exec(line));
    if (rows.every(Boolean)) {
      out.push('| ' + spec.head.join(' | ') + ' |');
      out.push('| ' + spec.head.map(() => '---').join(' | ') + ' |');
      for (const m of rows) {
        // 每个捕获组一列；句尾的句号在表格里是多余的，去掉
        const cells = m.slice(1).map((text) => cell(text).replace(/[。.]$/, ''));
        out.push('| ' + cells.join(' | ') + ' |');
      }
      converted++;
    } else {
      out.push(...run);
    }
    i = end;
  }

  if (converted === 0) {
    console.warn(`[sync] ${entryId} (${lang}): 登记了列表转表格但一段都没匹配上，句式可能变了`);
  }
  return out.join('\n');
}

// ── entryId → 路径映射（ENTRY_TO_PATH / getRoute）与 catalog.tsv 读取（readCatalog）──
// 都在 ./entry-map.mjs：config.mts 与 seo.mts 也要用同一份，本文件一被 import 就会跑 main()，
// 所以纯数据与纯函数不能留在这里。加页面时改那边的 ENTRY_TO_PATH。

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
  const catalog = readCatalog();
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
    // transformContent 被 tests/ZombieModeMutantWikiGuard.py 逐字节镜像，
    // 所以列表转表格与配图注入都作为**它之后的独立步骤**，不动它本身。
    const tabled = tableizeLists(transformContent(raw), entry.entryId, langKey);
    const transformed = injectImages(tabled, entry.entryId, langKey);
    const outPath = join(outBase, route);

    mkdirSync(dirname(outPath), { recursive: true });
    writeFileSync(outPath, transformed, 'utf-8');
    count++;
  }

  return { count, skipped };
}

// ── 生成英文首页 ─────────────────────────────────────────
function generateEnIndex(outPath) {
  // 英文首页与中文首页同构：都是 layout: page + <WikiHome />，
  // 门户内容由主题组件读 structure.mts 生成，这里不再重复一份 features 清单。
  const content = `---
layout: page
sidebar: false
aside: false
---

<WikiHome />
`;
  mkdirSync(dirname(outPath), { recursive: true });
  writeFileSync(outPath, content, 'utf-8');
}

// ── 类目主页（hubs/）──────────────────────────────────────
/**
 * 系统、攻略两个类目在 WikiContent 里没有「总览」条目——游戏内那本书按类目翻页，
 * 不需要；在线站却需要一个真正的类目主页，否则顶栏「系统」只能落在「掉落与奖励」上，
 * 面包屑里的类目链接也只能指向自己。
 *
 * 这些页只服务在线站，所以手写在 wiki-site/hubs/ 而不是 WikiContent/，由本脚本复制：
 *   hubs/<dir>.zh.md → docs/<dir>/index.md
 *   hubs/<dir>.en.md → docs/en/<dir>/index.md
 * 缺任一语言直接报错——不允许半份。正文里以 **条目名** 开头的列表项会被
 * config.mts 的 entityLinkPlugin 换成图标 + 链接，所以写清单时用 structure.mts 里的名字。
 * 对应的类目路径与「总览」条目登记在 structure.mts（WikiSiteStructureGuard 会双向校验）。
 */
const HUBS = ['systems', 'guides'];

function copyHubs() {
  let count = 0;
  for (const dir of HUBS) {
    for (const lang of ['zh', 'en']) {
      const src = join(HUB_DIR, `${dir}.${lang}.md`);
      if (!existsSync(src)) {
        throw new Error(`[sync] 类目主页缺失：${src}（hubs/ 下中英两份都要有）`);
      }
      const out = lang === 'zh'
        ? join(DOCS_DIR, dir, 'index.md')
        : join(DOCS_DIR, 'en', dir, 'index.md');
      mkdirSync(dirname(out), { recursive: true });
      copyFileSync(src, out);
      count++;
    }
  }
  return count;
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

  const catalog = readCatalog();
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

  // 4. 类目主页（系统 / 攻略），中英各一份
  console.log(`[sync] 类目主页: ${copyHubs()} 篇`);

  console.log('');
  console.log(`[sync] 同步完成！共 ${zh.count + en.count} 篇`);
}

main();
