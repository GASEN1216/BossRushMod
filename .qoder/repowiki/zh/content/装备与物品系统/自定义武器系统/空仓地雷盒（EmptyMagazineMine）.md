# 空仓地雷盒（EmptyMagazineMine）

2026-10-08 新增内容（COMPAT）。空仓地雷盒是占用图腾槽的装备，`BossRushItemIds.EmptyMagazineMine` 为 `500105`，品质 5、重量 0.5、基础价值 16000。图腾服务于枪械打空后的撤退换弹：同一把枪在一轮装填中实际开火至少 6 次，最后一次开火将弹匣清空时，在玩家脚下留下一枚延时地雷。

## 触发与伤害

每次实际开火计一次，空扣扳机和同次开火的多颗弹丸不能累加。提前换弹、切枪和卸图腾会清空计数；不足 6 次开火就打空的枪不能触发。地雷触发冷却为 6 秒，最多同时存在 1 枚未爆地雷，触发不额外消耗物品。卸下再装备保留已开始的冷却，冷却中的空仓当场作废，不保留补发请求。

地雷延时 1.5 秒爆炸，范围半径 4 米，基础物理效果伤害为 160，不伤害玩家本人或友方，遮挡可以阻止伤害。卸下图腾、玩家死亡和切图开始会取消未爆地雷；通过官方 `SceneLoader.onStartedLoadingScene` 在旧场景黑幕渐变之前取消，并在使用判据中拒绝 `SceneLoader.IsSceneLoading`，不等待目标场景初始化。最终扣血仍受目标防御影响；效果伤害不能冒充直接枪械命中。

## 获取方式

官方炸弹狂人（Mad Bomber，`nameKey=Cname_Grenade`，`EnemyPreset_Boss_Grenade`）有 20% 概率额外掉落，原版地图正常出击同样可获取；不进入叮当商店或通用随机奖池。名称以官方中英文本地化为准，没有指定未经核实的出生地图。

掉落复用 `NewWeaponBossDropHandler` 的死亡前缀与延后结算协议，沿用官方尸体箱、落地及失败回退处理；获取来源和图腾运行时相互独立，不改变官方 Boss 原有战斗技能。

## 配置与注册

配置、计数规则、运行时与表现层放在 `Integration/NewWeapons/EmptyMagazineMine/`。图腾复用已有新装备注册链：`NewWeaponPlaceholderRegistry` 提供克隆兜底，`NewWeaponItemConfigurators` 登记配置器，`NewWeaponItemAttributes` 负责基础属性，`NewWeaponRuntime` 接入生命周期。常量登记在 `Config/ConfigItemIds.cs`，自定义 TypeID 同时进入动态注册表和随机掉落黑名单。

数值采用现有品质 5 图腾的基础价值 16000，避免只因新增机制抬高出售收益；至少 6 次开火和 6 秒冷却共同限制小弹匣高频触发。图腾效果只在主玩家实际穿戴时工作；单纯放在背包或仓库里不会放置地雷。`IsUsableOwner` 排除基地；只有存在未爆地雷时创建逐帧 driver，暂停时引信不推进。地雷表现由独立 `EmptyMagazineMineFx` 持有，固定外圈显示范围，收缩内圈和三次短鸣提示引信进度。

## 验证边界

玩家正文位于 `WikiContent/zh/equipment/equipment__empty_magazine_mine.md` 和对应英文页。目录登记为 `equipment__empty_magazine_mine`，在线路径为 `/equipment/empty-magazine-mine`，图标复用装备类目图标。

代码接线、守卫与隔离回归分别提供 L1 / L2 证据。名称与图标显示、Boss 实际掉落、地雷位置和红光可读性、敌我过滤、遮挡与战斗手感仍需真实游戏进程确认，本文不把构建成功视为 L3 证据。

## 生产入口

- [数值配置](file://Integration/NewWeapons/EmptyMagazineMine/EmptyMagazineMineConfig.cs)
- [物品配置器](file://Integration/NewWeapons/EmptyMagazineMine/EmptyMagazineMineWeaponConfig.cs)
- [触发规则](file://Integration/NewWeapons/EmptyMagazineMine/EmptyMagazineMineRules.cs)
- [运行时](file://Integration/NewWeapons/EmptyMagazineMine/EmptyMagazineMineRuntime.cs)
- [地雷表现](file://Integration/NewWeapons/EmptyMagazineMine/EmptyMagazineMineFx.cs)
- [Boss 专属掉落](file://Integration/NewWeapons/Common/NewWeaponBossDropHandler.cs)
- [TypeID 常量](file://Config/ConfigItemIds.cs)
