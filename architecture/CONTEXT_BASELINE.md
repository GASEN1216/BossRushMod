# 上下文开销基线

测量日期：2026-09-24。字节为磁盘文件实际字节数，文件数仅计从玩家入口追到生产逻辑时需打开的源码；规则文档另计。前后使用同一组具体任务和同一组生产入口。`rg --files` 限定已知子系统目录来定位文件；前测均无需全仓 grep。字节和文件数只是代理指标，不换算 token 百分比。

## 固定自动导入

| 阶段 | `CLAUDE.md` | 导入的根 `AGENTS.md` | 合计 |
| --- | ---: | ---: | ---: |
| 前 | 361 B | 38,493 B | 38,854 B |
| 后 | 待测 | 待测 | 待测 |

`MEMORY.md` 不计。自动导入链以 `CLAUDE.md` 中的 `@AGENTS.md` 为准。

## 五项任务

| 任务 | 生产入口到实现：文件数 / 字节（前） | 准备阶段需全仓 grep（前） | 必读规则字节（前） | 后测 |
| --- | ---: | --- | ---: | --- |
| 日报版面改动 | 5 / 79,714 B | 否 | 63,305 B | 待测 |
| 天空岛居民服务加一项 | 4 / 130,056 B | 否 | 127,079 B | 待测 |
| NPC 商店交易改价 | 4 / 136,815 B | 否 | 119,576 B | 待测 |
| 给一把新武器配数值 | 4 / 79,874 B | 否 | 119,576 B | 待测 |
| Mode F 奖励调整 | 4 / 96,641 B | 否 | 111,798 B | 待测 |

源码样本：日报为 `DailyReportInteractable`、`DailyReportUIBridge`、`DailyReportUI`、`DailyReportUI_Dashboard`、`DailyReportLayoutTable`；天空岛为 `SkyIslandResidentInteractable`、`SkyIslandResidents`、`SkyIslandServices`、`SkyIslandSession`；商店为 `NPCShopInteractable`、`NPCShopSystem`、`INPCShopConfig`、`GoblinAffinityConfig`；武器为 `FenHuangHalberdConfig`、`FenHuangHalberdWeaponConfig`、`FenHuangHalberdBootstrap`、`BossRushIntegration`；Mode F 为 `ModeFBounty`、`ModeFBounty_EquipmentAndLoot`、`ModeFEntry`、`ModeEFLootboxTracker`。后测如目录迁移，仅替换文件路径，成员集合保持相同。

规则口径：所有任务读根 `AGENTS.md`；日报加 `Integration/AGENTS.md` 与 `docs/architecture/UI制作共识.md`；天空岛加 `DebugAndTools/SkyIsland/AGENTS.md` 与 `docs/contracts.md`；商店和武器加 `Integration/AGENTS.md` 与 `docs/contracts.md`；Mode F 加 `docs/contracts.md`。如果新索引在阅读顺序中加入必要规则，后测照实计入。
