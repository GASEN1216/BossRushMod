<script setup lang="ts">
/**
 * WikiInfobox — 实体页的速查框，泰拉瑞亚 Wiki 那种右上角信息框。
 *
 * 为什么要有：Boss / 装备页把「生命值 800」「伤害 35.5」这类硬数据写成正文
 * 项目符号，读者想比两把武器就得来回翻正文。速查框把这些提到页顶一眼可比，
 * 正文回去讲机制和体验。
 *
 * 版式——两种形态，按可用宽度切换：
 *   ≥1400px  右浮动 288px 的竖框。正文行盒自动绕开它；表格 / 代码块 / 配图
 *            自带 overflow 形成 BFC，会在浮动旁边收窄而不是被压住。
 *   <1400px  通栏卡片，条目横排成 2~3 列。
 *
 *   为什么门槛这么高：侧栏 272 + 右侧目录 224 吃掉近 500px，1280 视口下正文
 *   容器只剩 ~610px，再浮一个 288 的框，文字就只剩 280px 一行五六个词，
 *   比不浮动难读得多。宁可在中等宽度老老实实通栏。
 *
 *   通栏时隐藏框内的条目名：它紧挨着下面的 h1，两行同名字读起来像出了错。
 *   眉标（「自定义 Boss · 最终」）留着，正好当 h1 的引题。
 */
import { computed } from 'vue'
import WikiIcon from './WikiIcon.vue'
import { useWiki } from '../composables/useWiki'
import { INFOBOX } from '../../data/infobox.mts'

const { canonical, located, locale, t, href, entryLabel, entryByPath } = useWiki()

const box = computed(() => INFOBOX[canonical.value] ?? null)

const rows = computed(() =>
  (box.value?.rows ?? []).map((r) => ({
    label: locale.value === 'en' ? r.en : r.zh,
    value: locale.value === 'en' ? r.ve || r.vz : r.vz,
    tier: r.tier,
  }))
)

/** links 里存的是路径，标题回 structure.mts 取，避免改名时两处漂移。 */
const links = computed(() =>
  (box.value?.links ?? [])
    .map((path) => {
      const entry = entryByPath(path)
      return entry ? { path, label: entryLabel(entry), icon: entry.icon } : null
    })
    .filter((x): x is { path: string; label: string; icon?: string } => x !== null)
)
</script>

<template>
  <aside v-if="box && located" class="wiki-infobox">
    <div class="wiki-infobox__head">
      <WikiIcon :icon="located.entry.icon" :label="entryLabel(located.entry)" :size="46" />
      <div class="wiki-infobox__ident">
        <p class="wiki-infobox__eyebrow">
          {{ locale === 'en' ? box.eyebrowEn : box.eyebrowZh }}
        </p>
        <p class="wiki-infobox__name">{{ entryLabel(located.entry) }}</p>
      </div>
    </div>

    <dl class="wiki-infobox__rows">
      <div v-for="row in rows" :key="row.label" class="wiki-infobox__row">
        <dt>{{ row.label }}</dt>
        <dd>
          <span v-if="row.tier" class="wiki-rarity" :data-tier="row.tier">{{ row.value }}</span>
          <template v-else>{{ row.value }}</template>
        </dd>
      </div>
    </dl>

    <div v-if="links.length" class="wiki-infobox__links">
      <p class="wiki-infobox__linkhead">{{ t('相关条目', 'See also') }}</p>
      <div class="wiki-infobox__linklist">
        <!-- data-brs-ref 让这几条也吃到悬停预览（WikiRefPreview）：它们和正文里的
             实体链接是同一类东西，只有这里没有预览会显得规则不一致。
             这一格只有图标 + 名字，预览带来的信息增量反而比正文里更大。 -->
        <a
          v-for="link in links"
          :key="link.path"
          :href="href(link.path)"
          :data-brs-ref="link.path"
        >
          <WikiIcon :icon="link.icon" :label="link.label" :size="20" />
          <span>{{ link.label }}</span>
        </a>
      </div>
    </div>
  </aside>
</template>

<style>
.wiki-infobox {
  border: 1px solid var(--brs-rule);
  border-top: 2px solid var(--brs-ink);
  background: var(--brs-ground);
  padding: 14px 18px 16px;
  margin: 0 0 26px;
}

.wiki-infobox__head {
  display: flex;
  align-items: center;
  gap: 12px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--brs-rule);
}

.wiki-infobox__ident {
  min-width: 0;
}

.wiki-infobox__eyebrow {
  margin: 0;
  font-family: var(--brs-mono);
  font-size: var(--brs-label-size);
  font-weight: 600;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--brs-brass);
}

.wiki-infobox__name {
  margin: 3px 0 0;
  font-family: var(--brs-serif);
  font-size: 19px;
  font-weight: 700;
  line-height: 1.25;
  color: var(--brs-ink);
}

.wiki-infobox__rows {
  margin: 0;
  font-size: 13px;
  line-height: 1.55;
}

.wiki-infobox__row {
  display: grid;
  grid-template-columns: minmax(58px, auto) 1fr;
  gap: 0 14px;
  align-items: baseline;
  padding: 7px 0;
  border-bottom: 1px solid var(--brs-rule-soft);
}

.wiki-infobox__row dt,
.wiki-infobox__row dd {
  margin: 0;
}

.wiki-infobox__row dt {
  font-family: var(--brs-mono);
  font-size: 10.5px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--brs-ink-faint);
}

.wiki-infobox__row dd {
  color: var(--brs-ink);
  text-align: right;
}

/*
 * 稀有度色片 .wiki-rarity 的样式在 theme/extras.css §0：它同时被本组件与
 * WikiCompare.vue（类目主页的对比表）使用，放在组件的 <style> 里会让另一处
 * 悄悄依赖「这个块没加 scoped」这件事。只有 tier 字段写了的行才会变成色片，见 data/infobox.mts。
 */

.wiki-infobox__links {
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px solid var(--brs-rule);
}

.wiki-infobox__linkhead {
  margin: 0 0 8px;
  font-family: var(--brs-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--brs-ink-faint);
}

.wiki-infobox__linklist a {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 0;
  font-size: 13px;
  color: var(--brs-brass);
  text-decoration: none;
}

.wiki-infobox__linklist a:hover span {
  text-decoration: underline;
  text-underline-offset: 3px;
}

/* ── 通栏形态（<1400px）：条目横排，隐藏与 h1 重名的那行 ── */
@media (max-width: 1399px) {
  .wiki-infobox__name {
    display: none;
  }

  .wiki-infobox__head {
    padding-bottom: 10px;
  }

  .wiki-infobox__rows {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(230px, 1fr));
    gap: 0 26px;
    padding-top: 4px;
  }

  .wiki-infobox__linklist {
    display: flex;
    flex-wrap: wrap;
    gap: 4px 22px;
  }
}

/* ── 浮动形态（≥1400px）：竖框贴右 ── */
@media (min-width: 1400px) {
  .wiki-infobox {
    float: right;
    width: 288px;
    margin: 4px 0 22px 28px;
  }

  .wiki-infobox__row:last-child {
    border-bottom: none;
  }
}
</style>
