import { readFileSync } from 'fs'
import { resolve } from 'path'
import { MOD_ROOT } from '../../../scripts/entry-map.mjs'

/**
 * stats.data.mts — 首页「数据速览」里不能从 structure.mts 数出来的两个数。
 *
 * 模式 / Boss / 装备的数量首页直接数 structure.mts 的条目；成就和地图没有独立条目，
 * 从前是写死的「45」「9」，成就表每加一条首页就悄悄错一位。
 * 这里在构建期从 WikiContent 正文里数：成就按成就表的固定句式，地图按「地图列表」小节的条目。
 * 句式一变数就归零——首页那边对 0 做了兜底，不会显示「0 个成就」。
 */
const ACHIEVEMENTS = resolve(MOD_ROOT, 'WikiContent', 'zh', 'system__achievements_list.md')
const MAPS = resolve(MOD_ROOT, 'WikiContent', 'zh', 'map__overview.md')

export interface WikiStats {
  achievements: number
  maps: number
}

declare const data: WikiStats
export { data }

export default {
  watch: [ACHIEVEMENTS, MAPS],
  load(): WikiStats {
    const achievementsSrc = readFileSync(ACHIEVEMENTS, 'utf-8')
    const achievements = (achievementsSrc.match(/^- .+奖励 `[^`]+`[，,]\s*难度 `[^`]+`\s*$/gm) ?? []).length

    const mapsSrc = readFileSync(MAPS, 'utf-8')
    const section = mapsSrc.split(/^### 地图列表\s*$/m)[1]?.split(/^### /m)[0] ?? ''
    const maps = (section.match(/^- \*\*/gm) ?? []).length

    return { achievements, maps }
  },
}
