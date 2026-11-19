/**
 * slug.mts — 与 VitePress 默认标题锚点**逐字相同**的 slugify。
 *
 * 标题的 id 由 VitePress 内置的 markdown-it-anchor 按 @mdit-vue/shared 的 slugify 生成；
 * 实体链接（config.mts 的 entityLinkPlugin）与抗性表（WikiEnemyTable.vue）要拼出
 * 指向别的页面某个小节的 `#锚点`，必须用同一个函数，否则中文全角标点（「：」经 NFKD
 * 变成「:」再被替换成「-」）这类细节一错，链接就只落到页顶。
 *
 * 照抄自 node_modules/vitepress/dist/node 里打包的那一份（vitepress@1.6.4）；
 * 升级 VitePress 时与上游比一下。没有 import 上游：那一份不在公开导出里。
 */
const rControl = /[\u0000-\u001f]/g
const rSpecial = /[\s~`!@#$%^&*()\-_+=[\]{}|\\;:"'“”‘’<>,.?/]+/g
const rCombining = /[̀-ͯ]/g

export function slugify(str: string): string {
  return str
    .normalize('NFKD')
    .replace(rCombining, '')
    .replace(rControl, '')
    .replace(rSpecial, '-')
    .replace(/-{2,}/g, '-')
    .replace(/^-+|-+$/g, '')
    .replace(/^(\d)/, '_$1')
    .toLowerCase()
}

/** 列表项锚点的前缀：和标题锚点分开命名，同一页上不会撞 id。 */
export const ENTITY_ANCHOR_PREFIX = 'e-'

/** 抗性表外层的 id：没有专门落点的敌人（潮闸镇的突击队员等）链接到这里。 */
export function enemyTableAnchor(group: string): string {
  return 'enemies-' + group
}
