<script setup lang="ts">
/**
 * WikiHome — 首页门户。
 *
 * 换掉的是 VitePress 默认的 hero + features：那套版式是给「一个软件项目」用的，
 * 六张纯文字卡片说完就没了，读者想找「霜之哀伤在哪」得先点进侧栏再翻。
 * Wiki 首页的职责不是介绍自己，是**分流**——把人尽快送到他要找的那一页。
 *
 * 所以这里是四段：
 *   1. 刊头 + 数据速览（这个 Mod 有多大，一眼看到）
 *   2. 从这里开始（新读者的三步路径）
 *   3. 门户宫格（十个类目，每格直接列出前几条，可以从首页两跳到任意页面）
 *   4. 最近更新（版本列表，构建期从 catalog.tsv 生成）
 *
 * 数据全部来自 structure.mts 与 changelog.data.mts，首页没有自己的一份清单，
 * 所以不会再出现「侧栏加了条目、首页忘了同步」。
 */
import { computed } from 'vue'
import WikiIcon from './WikiIcon.vue'
import WikiSearchBar from './WikiSearchBar.vue'
import { useWiki } from '../composables/useWiki'
import { CHANGELOG_CATEGORY } from '../../data/structure.mts'
import { data as releases } from '../../data/changelog.data.mts'
import { data as wikiStats } from '../../data/stats.data.mts'

const { t, href, categories, entryLabel, categoryLabel, categoryBlurb, locale } = useWiki()

/**
 * 速览数字都是数出来的，不写死——加条目时数字自己跟着变。
 * 模式 / Boss / 装备数 structure.mts 的条目；成就与地图没有独立条目，
 * 由 stats.data.mts 在构建期从 WikiContent 正文里数（句式变了会归零，这里兜底回老数字）。
 */
const stats = computed(() => {
  const count = (id: string) => categories.find((c) => c.id === id)?.entries.length ?? 0
  return [
    { n: String(count('game-modes') - 1), zh: '游戏模式', en: 'Game modes' },
    { n: String(count('bosses') - 1), zh: '原创 Boss', en: 'Original bosses' },
    { n: String(count('equipment') - 1), zh: '原创装备', en: 'Custom gear' },
    { n: String(wikiStats.achievements || 45), zh: '成就', en: 'Achievements' },
    { n: String(wikiStats.maps || 9), zh: '竞技场地图', en: 'Arena maps' },
  ]
})

/** 每格最多列 5 条，超出的用「+N」收口，避免长类目把宫格撑成一根柱子。 */
const PEEK = 5
const portals = computed(() =>
  categories.map((cat) => {
    const hub = cat.path.replace(/\/$/, '')
    const rest = cat.entries.filter((e) => e.path.replace(/\/$/, '') !== hub)
    return {
      cat,
      shown: rest.slice(0, PEEK),
      more: Math.max(0, rest.length - PEEK),
    }
  })
)

const firstSteps = computed(() => [
  {
    n: '01',
    path: '/getting-started/installation',
    zh: '装上 Mod',
    en: 'Install the mod',
    dz: '创意工坊订阅，四步搞定',
    de: 'Subscribe on Workshop — four steps',
  },
  {
    n: '02',
    path: '/getting-started/first-steps',
    zh: '买第一张船票',
    en: 'Buy your first ticket',
    dz: '找基地商人，选张地图进场',
    de: 'Find the base trader, pick a map',
  },
  {
    n: '03',
    path: '/guides/beginner-route',
    zh: '按新手路线打',
    en: 'Follow the beginner route',
    dz: '从龙裔遗族刷到毕业装',
    de: 'From Dragon Descendant to endgame gear',
  },
])
</script>

<template>
  <div class="wiki-home">
    <!-- 1. 刊头 -->
    <header class="wiki-home__masthead">
      <p class="wiki-home__eyebrow">鸭科夫 / Escape from Duckov · BossRush Mod</p>
      <h1 class="wiki-home__title">
        BossRush<span class="wiki-home__title-sub">{{ t('官方百科', 'Official Wiki') }}</span>
      </h1>
      <p class="wiki-home__tagline">
        {{
          t(
            '游戏模式、Boss、装备、NPC、成就——你需要的一切都在这里。',
            'Game modes, bosses, equipment, NPCs, achievements — everything in one place.'
          )
        }}
      </p>

      <WikiSearchBar />

      <ul class="wiki-home__stats">
        <li v-for="s in stats" :key="s.en">
          <strong>{{ s.n }}</strong>
          <span>{{ locale === 'en' ? s.en : s.zh }}</span>
        </li>
      </ul>
    </header>

    <!-- 2. 从这里开始 -->
    <section class="wiki-home__section">
      <h2 class="wiki-home__rule">{{ t('从这里开始', 'Start here') }}</h2>
      <ol class="wiki-home__steps">
        <li v-for="step in firstSteps" :key="step.n">
          <a :href="href(step.path)">
            <span class="wiki-home__stepnum">{{ step.n }}</span>
            <span class="wiki-home__steptitle">{{ locale === 'en' ? step.en : step.zh }}</span>
            <span class="wiki-home__stepdesc">{{ locale === 'en' ? step.de : step.dz }}</span>
          </a>
        </li>
      </ol>
    </section>

    <!-- 3. 门户宫格 -->
    <section class="wiki-home__section">
      <h2 class="wiki-home__rule">{{ t('全站索引', 'Browse the wiki') }}</h2>
      <div class="wiki-home__portals">
        <article v-for="p in portals" :key="p.cat.id" class="wiki-portal">
          <a class="wiki-portal__head" :href="href(p.cat.path)">
            <WikiIcon :icon="p.cat.icon" :label="categoryLabel(p.cat)" :size="36" />
            <span>
              <span class="wiki-portal__name">{{ categoryLabel(p.cat) }}</span>
              <span class="wiki-portal__blurb">{{ categoryBlurb(p.cat) }}</span>
            </span>
          </a>
          <ul class="wiki-portal__list">
            <li v-for="entry in p.shown" :key="entry.path">
              <a :href="href(entry.path)">{{ entryLabel(entry) }}</a>
            </li>
            <li v-if="p.more" class="wiki-portal__more">
              <a :href="href(p.cat.path)">
                {{ t('还有 ', '+') }}{{ p.more }}{{ t(' 条 →', ' more →') }}
              </a>
            </li>
          </ul>
        </article>
      </div>
    </section>

    <!-- 4. 最近更新 -->
    <section class="wiki-home__section">
      <h2 class="wiki-home__rule">{{ t('最近更新', 'Latest releases') }}</h2>
      <ul class="wiki-home__releases">
        <li v-for="rel in releases.slice(0, 6)" :key="rel.version">
          <a :href="href(rel.path)">
            <span class="wiki-home__ver">v{{ rel.version }}</span>
            <span class="wiki-home__reltitle">{{ locale === 'en' ? rel.en : rel.zh }}</span>
          </a>
        </li>
      </ul>
      <p class="wiki-home__allrel">
        <a :href="href(CHANGELOG_CATEGORY.path)">
          {{ t('查看全部更新日志 →', 'All release notes →') }}
        </a>
      </p>
    </section>
  </div>
</template>

<style>
.wiki-home {
  max-width: 1180px;
  margin: 0 auto;
  padding: 40px 24px 80px;
}

/* ── 刊头 ── */
.wiki-home__eyebrow {
  margin: 0;
  font-family: var(--brs-mono);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.22em;
  text-transform: uppercase;
  color: var(--brs-brass);
}

.wiki-home__title {
  display: flex;
  align-items: baseline;
  flex-wrap: wrap;
  gap: 0 18px;
  margin: 16px 0 0;
  font-family: var(--brs-serif);
  font-size: clamp(38px, 6vw, 62px);
  font-weight: 900;
  letter-spacing: -0.02em;
  line-height: 1.05;
  color: var(--brs-ink);
}

.wiki-home__title-sub {
  font-size: clamp(20px, 2.6vw, 30px);
  font-weight: 500;
  color: var(--brs-ink-soft);
}

.wiki-home__tagline {
  max-width: 60ch;
  margin: 18px 0 0;
  padding-top: 16px;
  border-top: 1px solid var(--brs-rule);
  font-family: var(--brs-serif);
  font-size: 17px;
  line-height: 1.7;
  color: var(--brs-ink-soft);
}

.wiki-home__stats {
  display: flex;
  flex-wrap: wrap;
  gap: 0;
  margin: 26px 0 0;
  padding: 0;
  list-style: none;
  border: 1px solid var(--brs-rule);
  background: var(--brs-surface);
}

.wiki-home__stats li {
  flex: 1 1 128px;
  padding: 14px 18px;
  border-right: 1px solid var(--brs-rule);
}

.wiki-home__stats li:last-child {
  border-right: none;
}

.wiki-home__stats strong {
  display: block;
  font-family: var(--brs-serif);
  font-size: 30px;
  font-weight: 900;
  line-height: 1;
  color: var(--brs-brass);
}

.wiki-home__stats span {
  display: block;
  margin-top: 6px;
  font-family: var(--brs-mono);
  font-size: 10.5px;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: var(--brs-ink-faint);
}

/* ── 分区栏带 ── */
.wiki-home__section {
  margin-top: 56px;
}

.wiki-home__rule {
  margin: 0 0 22px;
  padding-bottom: 9px;
  border-bottom: 1px solid var(--brs-rule);
  font-family: var(--brs-sans);
  font-size: 14px;
  font-weight: 700;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--brs-brass);
}

/* ── 三步上手 ── */
.wiki-home__steps {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 12px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.wiki-home__steps a {
  display: block;
  height: 100%;
  padding: 18px 20px 20px;
  border: 1px solid var(--brs-rule);
  background: var(--brs-surface);
  text-decoration: none;
  transition: border-color 0.15s;
}

.wiki-home__steps a:hover {
  border-color: var(--brs-brass);
}

.wiki-home__stepnum {
  display: block;
  font-family: var(--brs-mono);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.16em;
  color: var(--brs-brass);
}

.wiki-home__steptitle {
  display: block;
  margin-top: 10px;
  font-family: var(--brs-serif);
  font-size: 19px;
  font-weight: 700;
  color: var(--brs-ink);
}

.wiki-home__stepdesc {
  display: block;
  margin-top: 6px;
  font-size: 13px;
  line-height: 1.6;
  color: var(--brs-ink-soft);
}

/* ── 门户宫格 ── */
.wiki-home__portals {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(268px, 1fr));
  gap: 12px;
}

.wiki-portal {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--brs-rule);
  background: var(--brs-surface);
  transition: border-color 0.15s;
}

.wiki-portal:hover {
  border-color: var(--brs-brass);
}

.wiki-portal__head {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 16px 18px 14px;
  border-bottom: 1px solid var(--brs-rule-soft);
  text-decoration: none;
}

.wiki-portal__name {
  display: block;
  font-family: var(--brs-serif);
  font-size: 18px;
  font-weight: 700;
  line-height: 1.25;
  color: var(--brs-ink);
}

.wiki-portal__blurb {
  display: block;
  margin-top: 5px;
  font-size: 12.5px;
  line-height: 1.55;
  color: var(--brs-ink-soft);
}

.wiki-portal__list {
  flex: 1;
  margin: 0;
  padding: 12px 18px 16px;
  list-style: none;
}

.wiki-portal__list li {
  margin: 0;
  padding: 3px 0;
}

.wiki-portal__list a {
  font-size: 13.5px;
  color: var(--brs-ink-soft);
  text-decoration: none;
}

.wiki-portal__list a:hover {
  color: var(--brs-brass);
  text-decoration: underline;
  text-underline-offset: 3px;
}

.wiki-portal__more a {
  font-family: var(--brs-mono);
  font-size: 11px;
  letter-spacing: 0.08em;
  color: var(--brs-brass);
}

/* ── 最近更新 ── */
.wiki-home__releases {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: 0 24px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.wiki-home__releases a {
  display: flex;
  align-items: baseline;
  gap: 14px;
  padding: 10px 0;
  border-bottom: 1px solid var(--brs-rule-soft);
  text-decoration: none;
}

.wiki-home__ver {
  font-family: var(--brs-mono);
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.06em;
  color: var(--brs-brass);
  white-space: nowrap;
}

.wiki-home__reltitle {
  font-size: 13.5px;
  color: var(--brs-ink-soft);
}

.wiki-home__releases a:hover .wiki-home__reltitle {
  color: var(--brs-brass);
}

.wiki-home__allrel {
  margin: 18px 0 0;
}

.wiki-home__allrel a {
  font-family: var(--brs-mono);
  font-size: 11.5px;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--brs-brass);
  text-decoration: none;
}

@media (max-width: 640px) {
  .wiki-home {
    padding: 28px 20px 60px;
  }

  .wiki-home__stats li {
    flex-basis: 50%;
    border-bottom: 1px solid var(--brs-rule);
  }
}
</style>
