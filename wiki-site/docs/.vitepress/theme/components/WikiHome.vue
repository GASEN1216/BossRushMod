<script setup lang="ts">
/**
 * WikiHome — 首页门户（MediaWiki 主页那套 #mainpage-wrapper）。
 *
 * 版式照抄目标站的主页：最上面一块「欢迎 + 站点数据 + 最新版本」的抬头，
 * 底下是一排会自己换行的盒子，每盒一个类目的图标清单。首页的职责是**分流**，
 * 所以每个类目直接把前几条摊在外面——从首页两跳能到任意页面。
 *
 * 数据全部来自 structure.mts / changelog.data.mts / stats.data.mts，
 * 首页没有自己的一份清单，不会出现「侧栏加了条目、首页忘了同步」。
 */
import { computed } from 'vue'
import WikiIcon from './WikiIcon.vue'
import { useWiki } from '../composables/useWiki'
import { useUiText } from '../composables/useUiText'
import { usePageLinks } from '../composables/usePageLinks'
import { useData } from 'vitepress'
import { CHANGELOG_CATEGORY, NAV_PRIMARY } from '../../data/structure.mts'
import { data as releases } from '../../data/changelog.data.mts'
import { data as wikiStats } from '../../data/stats.data.mts'

const ui = useUiText()
const { theme } = useData()
const { repo, issues } = usePageLinks()
const { t, href, categories, entryLabel, entryBlurb, categoryLabel, categoryBlurb, locale, tierOf } =
  useWiki()

const feedHref = computed<string | null>(() => theme.value.feedUrl ?? null)

/**
 * 速览数字都是数出来的，不写死——加条目时数字自己跟着变。
 * 成就与地图没有独立条目，由 stats.data.mts 在构建期从 WikiContent 正文里数
 * （句式变了会归零，这里兜底回老数字）。
 */
const stats = computed(() => {
  const count = (id: string) => categories.find((c) => c.id === id)?.entries.length ?? 0
  return [
    { n: String(count('game-modes') - 1), zh: '个游戏模式', en: 'game modes' },
    { n: String(count('bosses') - 1), zh: '个原创 Boss', en: 'original bosses' },
    { n: String(count('equipment') - 1), zh: '件原创装备', en: 'custom gear' },
    { n: String(wikiStats.achievements || 45), zh: '个成就', en: 'achievements' },
    { n: String(wikiStats.maps || 9), zh: '张竞技场地图', en: 'arena maps' },
  ]
})

const latest = computed(() => releases[0] ?? null)

/** 每盒最多列 8 条，超出的用「还有 N 条」收口，免得一个长类目把整排撑歪。 */
const PEEK = 8
const portals = computed(() =>
  categories.map((cat) => {
    const hub = cat.path.replace(/\/$/, '')
    const rest = cat.entries.filter((e) => e.path.replace(/\/$/, '') !== hub)
    return {
      cat,
      shown: rest.slice(0, PEEK),
      more: Math.max(0, rest.length - PEEK),
      wide: NAV_PRIMARY.includes(cat.id),
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
  <div id="mainpage-wrapper">
    <!-- ── 抬头 ────────────────────────────────────────── -->
    <div id="box-wikiheader" class="terraria">
      <div class="inner">
        <div class="main-title">
          <span class="welcome">
            {{ t('欢迎来到 BossRush Wiki', 'Welcome to the BossRush Wiki') }}
          </span>
          <div class="tagline">
            {{
              t(
                '鸭科夫 / Escape from Duckov 的 BossRush Mod 百科，由玩家编写与维护。',
                'The community-written reference for the BossRush mod for Escape from Duckov.'
              )
            }}
          </div>
          <div class="dotlist statistics">
            <ul>
              <li v-for="s in stats" :key="s.en">
                <strong>{{ s.n }}</strong> {{ locale === 'en' ? s.en : s.zh }}
              </li>
            </ul>
          </div>
        </div>

        <div class="related-info">
          <div class="ii">
            <span class="iconlist-label">{{ t('相关链接', 'Links') }}</span>
            <a :href="repo" target="_blank" rel="noopener">
              <span class="tw-icon tw-icon--github" aria-hidden="true" />GitHub
            </a>
            <a :href="href(CHANGELOG_CATEGORY.path)">
              <span class="tw-icon tw-icon--history" aria-hidden="true" />{{ ui.changelog }}
            </a>
            <a v-if="feedHref" :href="feedHref">
              <span class="tw-icon tw-icon--rss" aria-hidden="true" />{{ ui.feed }}
            </a>
            <a :href="href('/getting-started/overview')">
              <span class="tw-icon tw-icon--help" aria-hidden="true" />{{ ui.help }}
            </a>
          </div>
        </div>

        <ul v-if="latest" id="latest-version">
          <li>
            <span class="pic"><span class="tw-icon tw-icon--dice" aria-hidden="true" /></span>
            <span class="platform-and-version-text">
              <span class="platform">{{ t('PC · Steam 创意工坊', 'PC · Steam Workshop') }}</span>
              <span class="versionnumber">
                <a :href="href(latest.path)">v{{ latest.version }}</a>
              </span>
              <span class="equivalentversion">
                {{ locale === 'en' ? latest.en : latest.zh }}
              </span>
            </span>
          </li>
        </ul>
      </div>
    </div>

    <!-- ── 盒子阵列 ────────────────────────────────────── -->
    <div id="main-section">
      <!-- 从这里开始 -->
      <section class="infocard compact terraria box-wide">
        <div class="main-heading">
          <span class="hgroup">
            <span class="main">{{ t('从这里开始', 'Start here') }}</span>
          </span>
        </div>
        <div class="outro">
          <div class="introtext">
            <p>
              {{
                t(
                  'BossRush 把鸭科夫变成一座 Boss 竞技场：买张船票进场，打完撤离，带着战利品回来变强。',
                  'BossRush turns Duckov into a boss arena: buy a ticket, fight, extract, come back stronger.'
                )
              }}
            </p>
          </div>
          <ol class="startsteps">
            <li v-for="step in firstSteps" :key="step.n">
              <a :href="href(step.path)">
                <span class="stepnum">{{ step.n }}</span>
                <span>
                  <span class="steptitle">{{ locale === 'en' ? step.en : step.zh }}</span>
                  <span class="stepdesc">{{ locale === 'en' ? step.de : step.dz }}</span>
                </span>
              </a>
            </li>
          </ol>
        </div>
      </section>

      <!-- 新闻 -->
      <section class="infocard compact terraria box-news newspart">
        <div class="main-heading">
          <span class="hgroup">
            <span class="main">
              <a :href="href(CHANGELOG_CATEGORY.path)">{{ ui.changelog }}</a>
            </span>
          </span>
          <span class="icon">
            <WikiIcon :icon="CHANGELOG_CATEGORY.icon" :label="ui.changelog" :size="28" />
          </span>
        </div>
        <div class="news">
          <dl v-if="latest">
            <dd>
              <a :href="href(latest.path)">
                v{{ latest.version }} · {{ locale === 'en' ? latest.en : latest.zh }}
              </a>
            </dd>
          </dl>
          <ul>
            <li v-for="rel in releases.slice(1, 6)" :key="rel.version">
              <time>v{{ rel.version }}</time>
              <a :href="href(rel.path)">{{ locale === 'en' ? rel.en : rel.zh }}</a>
            </li>
          </ul>
        </div>
        <div class="links">
          <a :href="href(CHANGELOG_CATEGORY.path)">
            <span class="tw-icon tw-icon--history" aria-hidden="true" />{{ ui.allVersions }}
          </a>
          <a v-if="feedHref" :href="feedHref">
            <span class="tw-icon tw-icon--rss" aria-hidden="true" />{{ ui.feed }}
          </a>
        </div>
      </section>

      <!-- 每个类目一盒 -->
      <section
        v-for="p in portals"
        :key="p.cat.id"
        class="infocard compact terraria"
        :class="{ 'box-wide': p.wide }"
      >
        <div class="main-heading">
          <span class="hgroup">
            <span class="main"><a :href="href(p.cat.path)">{{ categoryLabel(p.cat) }}</a></span>
            <span class="namenote">{{ categoryBlurb(p.cat) }}</span>
          </span>
          <span class="icon">
            <WikiIcon :icon="p.cat.icon" :label="categoryLabel(p.cat)" :size="28" />
          </span>
        </div>
        <div class="mclist">
          <ul>
            <li v-for="entry in p.shown" :key="entry.path">
              <a class="i" :href="href(entry.path)" :data-brs-ref="entry.path.replace(/\/$/, '')">
                <WikiIcon :icon="entry.icon" :label="entryLabel(entry)" :size="28" />
                <span class="mclist__text">
                  <span class="mclist__name wiki-tier" :data-tier="tierOf(entry.path)">
                    {{ entryLabel(entry) }}
                  </span>
                  <span v-if="entryBlurb(entry)" class="mclist__blurb">
                    {{ entryBlurb(entry) }}
                  </span>
                </span>
              </a>
            </li>
            <li v-if="p.more">
              <a class="i" :href="href(p.cat.path)">
                <span class="mclist__text">
                  <span class="mclist__name">{{ ui.andMore(p.more) }} →</span>
                </span>
              </a>
            </li>
          </ul>
        </div>
      </section>

      <!-- Wiki 社区 -->
      <section class="infocard compact terraria">
        <div class="main-heading">
          <span class="hgroup">
            <span class="main">{{ t('Wiki 社区', 'Wiki community') }}</span>
          </span>
        </div>
        <div class="outro">
          <dl>
            <dt><a :href="repo" target="_blank" rel="noopener">{{ ui.sourceRepo }}</a></dt>
            <dd>{{ t('正文、代码与本站的源都在这里。', 'Content, code and this site live here.') }}</dd>
            <dt><a :href="issues" target="_blank" rel="noopener">{{ ui.talk }}</a></dt>
            <dd>{{ t('发现错漏、想加条目，开个 issue。', 'Spotted a mistake? Open an issue.') }}</dd>
            <template v-if="feedHref">
              <dt><a :href="feedHref">{{ ui.feed }}</a></dt>
              <dd>{{ t('订阅更新日志，新版本第一时间知道。', 'Subscribe to the changelog feed.') }}</dd>
            </template>
          </dl>
        </div>
      </section>
    </div>

    <div class="footer">
      <div>{{ ui.attribution }}</div>
    </div>
  </div>
</template>
