# 上下文开销基线

测量日期：2026-09-24。字节为磁盘文件实际字节数，文件数仅计从玩家入口追到生产逻辑时需打开的源码；规则文档另计。前后使用同一组具体任务和同一组生产入口。`rg --files` 限定已知子系统目录来定位文件；前测均无需全仓 grep。字节和文件数只是代理指标，不换算 token 百分比。

## 固定自动导入

| 阶段 | `CLAUDE.md` | 导入的根 `AGENTS.md` | 合计 |
| --- | ---: | ---: | ---: |
| 前 | 361 B | 38,493 B | 38,854 B |
| P1 后 | 482 B | 17,329 B | 17,811 B |

`MEMORY.md` 不计。自动导入链以 `CLAUDE.md` 中的 `@AGENTS.md` 为准。

## 五项任务

| 任务 | 生产入口到实现：文件数 / 字节（前 → P1 后） | 准备阶段需全仓 grep（前 → 后） | 必读规则字节（前 → P1 后） |
| --- | ---: | --- | ---: |
| 日报版面改动 | 5 / 79,714 B → 5 / 79,714 B | 否 → 否 | 63,305 B → 51,925 B |
| 天空岛居民服务加一项 | 4 / 130,056 B → 4 / 130,056 B | 否 → 否 | 127,079 B → 113,067 B |
| NPC 商店交易改价 | 4 / 136,815 B → 4 / 136,815 B | 否 → 否 | 119,576 B → 101,044 B |
| 给一把新武器配数值 | 4 / 79,874 B → 4 / 79,874 B | 否 → 否 | 119,576 B → 101,044 B |
| Mode F 奖励调整 | 4 / 96,641 B → 4 / 96,641 B | 否 → 否 | 111,798 B → 90,634 B |

源码样本：日报为 `DailyReportInteractable`、`DailyReportUIBridge`、`DailyReportUI`、`DailyReportUI_Dashboard`、`DailyReportLayoutTable`；天空岛为 `SkyIslandResidentInteractable`、`SkyIslandResidents`、`SkyIslandServices`、`SkyIslandSession`；商店为 `NPCShopInteractable`、`NPCShopSystem`、`INPCShopConfig`、`GoblinAffinityConfig`；武器为 `FenHuangHalberdConfig`、`FenHuangHalberdWeaponConfig`、`FenHuangHalberdBootstrap`、`BossRushIntegration`；Mode F 为 `ModeFBounty`、`ModeFBounty_EquipmentAndLoot`、`ModeFEntry`、`ModeEFLootboxTracker`。后测如目录迁移，仅替换文件路径，成员集合保持相同。

规则口径：所有任务读根 `AGENTS.md`；日报加 `Integration/AGENTS.md` 与 `docs/architecture/UI制作共识.md`；天空岛加 `DebugAndTools/SkyIsland/AGENTS.md` 与 `docs/contracts.md`；商店和武器加 `Integration/AGENTS.md` 与 `docs/contracts.md`；Mode F 加 `docs/contracts.md`。P1 后日报与天空岛的 UI 任务再读新的 `Common/UI/AGENTS.md`，已计入后测。源码样本未变，故 P1 的生产字节不变；后续状态提取与目录归位会在 P6 重测。自动导入链下降 21,043 B；实际 token 用量未测。

## P6 终态后测（2026-09-26）

按 `architecture/CONTEXT_BASELINE.md` 的 5 组固定成员追到当前真实 owner，完整计入新入口/共享载体。结果没有显示源码阅读字节普遍下降；合并载体与跨模块装配会增加整文件阅读量。

| 固定任务 | 前 / P1 文件数 | 前 / P1 源码字节 | P6 文件数 | P6 源码字节 | 源码差额 | P1 规则字节 | P6 固定规则字节 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 日报版面改动 | 5 | 79,714 | 6 | 132,702 | +52,988 | 51,925 | 52,018 |
| 天空岛居民服务加一项 | 4 | 130,056 | 4 | 130,042 | -14 | 113,067 | 113,019 |
| NPC 商店交易改价 | 4 | 136,815 | 6 | 202,917 | +66,102 | 101,044 | 101,137 |
| 给一把新武器配数值 | 4 | 79,874 | 9 | 173,779 | +93,905 | 101,044 | 101,137 |
| Mode F 奖励调整 | 4 | 96,641 | 12 | 188,723 | +92,082 | 90,634 | 90,620 |

自动导入链 `CLAUDE.md -> @AGENTS.md`：前 38,854 B，P1 为 17,811 B，当前为 17,783 B（CLAUDE 468 B + 根 AGENTS 17,315 B）；相对前 -21,071 B，相对 P1 -28 B。`MEMORY.md` 与文字提及的专项路径不计为自动导入。

根 AGENTS 同时出现在固定规则集合中；自动导入与规则是不同视角，不能直接相加再重复计根规则。字节只是代理指标，未测 token，也不折算 token 百分比。

## 口径与证据

- 固定 5 个任务与基线全部原文件成员；以 3323e33e blob 跟踪原声明和正文，不只统计迁移后的薄桥。
- 每组当前所有入口、原成员真实 owner、必要新绑定载体均按完整磁盘文件计字节，同组同路径去重；合并文件中不相关 region 也计入。
- 追踪原样本内部移出的逻辑；基线原本就是样本外的方法/类型依赖不递归扩张。共享落点及这一边界逐组列明。
- 规则集合固定为 P1 口径；天空岛仅换现路径。规则与源码分开，自动导入另列，不能将根 AGENTS 重复相加。
- 仅代理指标，无 token 测量，不折算 token 百分比，不以结果下降为前提。

历史表记录的是混合换行的磁盘字节，未提供逐文件原始磁盘快照；git blob 仅用于原成员/正文追踪。历史比较值保持基线表，不用 git LF 字节或统一 CRLF 重写历史值。当前字节逐文件直接 read_bytes，并记录 CRLF/LF 数供复算。

P6 导航演练分别运行五个模块的 task_context，再以当前入口与 owner 所在目录执行限定范围的 rg --files，五组全部能定位完整样本，无需全仓检索（前测也均为否）。原成员追踪审计使用过全仓 rg，这是建立后测映射的审计成本，不计入日常定位演练；未宣称检索次数下降。证据为 Build/migration/p6-navigation.json 及对应模块日志。

21 个原样本已保存为 `Build/migration/p6-context-scan/before/` 的旧 blob；归一化差异见 `comparison.json` 与 `diffs/`，原声明定位见 `original-members.json`、`member-candidates.json`。JSON 产物记录每个当前文件的字节、SHA-256、行数和换行计数，可按磁盘重新核对。声明索引只用于定位；当前集合结合正文差异和调用/状态落点核对。

## 日报版面改动

完整文件后测集合：

- `Integration/DailyReport/DailyReportInteractable.cs` — 1,678 B。原玩家交互入口；正文与 3323e33e 归一化后相同，仍调用宿主 OpenDailyReportUI。
- `Integration/IntegrationHostCompatibility.cs` — 52,948 B。原 DailyReportUIBridge 的 OpenDailyReportUI / EnsureDailyReportView 兼容入口并入此载体；计整个文件，包含无关 region。
- `Integration/DailyReport/DailyReportRuntimeModule_UI.cs` — 1,910 B。原 dailyReportView 字段、打开判断与 View 创建算法的真实 owner。
- `Integration/DailyReport/DailyReportUI.cs` — 36,736 B。原 View 与版面成员完整保留；归一化正文相同。
- `Integration/DailyReport/DailyReportUI_Dashboard.cs` — 25,965 B。原 Dashboard 成员完整保留；归一化正文相同。
- `Integration/DailyReport/DailyReportLayoutTable.cs` — 13,465 B。原布局表完整保留；归一化正文相同。

原成员映射：

- `DailyReportUIBridge`：dailyReportView、OpenDailyReportUI、EnsureDailyReportView → `Integration/IntegrationHostCompatibility.cs`、`Integration/DailyReport/DailyReportRuntimeModule_UI.cs`。入口不等于实现；必须同时读完整兼容载体和 UI owner。
- `其余 4 文件`：全部原成员 → `原路径`。与旧 blob 逐文件归一化对照相同，无成员外移。

固定规则集合：`AGENTS.md`、`Integration/AGENTS.md`、`docs/architecture/UI制作共识.md`、`Common/UI/AGENTS.md`；共 52,018 B。

DailyReportRuntimeModule.cs 的既有生命周期、DailyReportService 与共享 UI 库是原样本外的既有依赖；没有原样本成员迁入这些文件，故不递归扩张。

## 天空岛居民服务加一项

完整文件后测集合：

- `SkyIsland/SkyIslandResidentInteractable.cs` — 6,124 B。原居民入口只迁目录；归一化正文相同。
- `SkyIsland/SkyIslandResidents.cs` — 20,024 B。原居民定义只迁目录；归一化正文相同。
- `SkyIsland/SkyIslandServices.cs` — 26,620 B。原服务全部成员保留；与基线仅 records 类型由 ZombieModeAttributeModifierRecord 改为 BossRushStatModifierRecord，2 处类型名各少 7 字节。
- `SkyIsland/SkyIslandSession.cs` — 77,274 B。原会话成员只迁目录；归一化正文相同。

原成员映射：

- `DebugAndTools/SkyIsland 下 4 文件`：全部原成员 → `SkyIsland 下同名文件`。3 文件正文归一化相同，Services 仅共享记录类型更名；未发现原成员迁出，不能因目录变动另换样本。

固定规则集合：`AGENTS.md`、`SkyIsland/AGENTS.md`、`docs/contracts.md`、`Common/UI/AGENTS.md`；共 113,019 B。

Session 的其它 partial 在基线已经是样本外文件。共享 StatModifier 记录定义也属于原本外部类型依赖；本次不扩张类型定义闭包。

## NPC 商店交易改价

完整文件后测集合：

- `Integration/Affinity/Interactables/NPCShopInteractable.cs` — 6,182 B。原交互入口保留；现在把 PaymentStrategy 一并传给商店。
- `Integration/Affinity/Systems/NPCShopSystem.cs` — 39,381 B。原库存、定价、交易、退款主体保留；包含新增 NPCShopPaymentStrategy 类。
- `Integration/Affinity/Core/INPCShopConfig.cs` — 3,185 B。原配置接口成员完整保留；归一化正文相同。
- `Integration/Affinity/NPCs/GoblinAffinityConfig.cs` — 89,246 B。原价格/库存配置完整保留；归一化正文相同。
- `Integration/Reforge/GoblinReforgeInteractable.cs` — 18,958 B。原商店内的模式支付选择现经 GoblinInteractable.ShopPaymentStrategy 和子交互创建时的 PaymentStrategy 赋值接入；计完整载体。
- `ZombieMode/ZombieModeRewardEffectsAndNpc.cs` — 45,965 B。临时哥布林创建者现在构造净化点策略，原商店 CanAfford / TrySpend 的玩法回调移至此处闭包；包含真实付款入口。

原成员映射：

- `NPCShopSystem`：IsZombieModeTemporaryPurificationShop、CanPurchaseTemporaryPurificationShopSelection、OnItemPurchased 的支付选择与调用 → `Integration/Affinity/Systems/NPCShopSystem.cs`、`Integration/Reforge/GoblinReforgeInteractable.cs`、`Integration/Reforge/GoblinReforgeInteractable.cs`、`ZombieMode/ZombieModeRewardEffectsAndNpc.cs`。新 IsPurificationShop / 策略代理保留在商店；必须追到创建者策略闭包与传递路径，不能只量原 4 文件。
- `INPCShopConfig、GoblinAffinityConfig`：全部原成员 → `原路径`。归一化正文相同；GoblinAffinityConfig_LanguageCache 没有接收原样本成员。

固定规则集合：`AGENTS.md`、`Integration/AGENTS.md`、`docs/contracts.md`；共 101,137 B。

GoblinAffinityConfig_LanguageCache.cs、NPCShopSystem 的官方 StockShop API 与付款总账是基线外依赖。纳入新支付策略创建/传递，但不递归计入丧尸整个 runtime。规则比较仍用固定 Integration + contracts 集合，未临时追加 ZombieMode/AGENTS。

## 给一把新武器配数值

完整文件后测集合：

- `Integration/DragonKing/Weapons/FenHuangHalberdConfig.cs` — 7,859 B。原数值配置全保留；归一化正文相同。
- `Integration/DragonKing/Weapons/FenHuangHalberdWeaponConfig.cs` — 24,230 B。原武器配置/效果绑定全保留；归一化正文相同。
- `Integration/DragonKing/Weapons/FenHuangHalberdBootstrap.cs` — 6,711 B。原 4 个启动/场景/协程/清理成员转为 DragonKingRuntimeModule，但算法仍完整在本文件。
- `Integration/IntegrationHostCompatibility.cs` — 52,948 B。原 Bootstrap 兼容入口及 sharedWait05s 访问桥，并入此文件；计整个载体。
- `Integration/DragonKing/DragonKingRuntimeModuleHostBridge.cs` — 8,085 B。Bootstrap 当前使用的 owner 字段及 OnAwake / OnDestroy 绑定真实落点。
- `Integration/BossRushIntegration.cs` — 17,941 B。原 Integration 成员的宿主兼容入口所在合并文件；计全部 region，不能只计 BossRushIntegration region。
- `Integration/BossRushIntegrationRuntimeModule.cs` — 31,688 B。原船票/日志/砖石库存常量、缓存、日志限流、场景读取、商店查找/注入与存档回调真实 owner。
- `Integration/BossRushIntegrationRuntimeModule_Initialization.cs` — 9,969 B。原初始化、本地化、RegisterCustomWeaponRuntimeConfigs、OnFenHuangHalberdLoaded 等回调和 TryInjectAllBossRushItemsIntoShop 的算法落点。
- `Integration/BossRushIntegrationRuntimeModule_RuntimeHooks.cs` — 14,348 B。原 item105PurchaseCount 与 runtimeStateMonitorCoroutine 分别迁为 _item105PurchaseCount / _runtimeStateMonitorCoroutine；同时包含监控启动、停止及迭代实现，不能只量兼容属性。

原成员映射：

- `BossRushIntegration`：52 项原顶层声明（原始索引用于定位，不把 regex 当语义证明） → `Integration/BossRushIntegration.cs`、`Integration/BossRushIntegrationRuntimeModule.cs`、`Integration/BossRushIntegrationRuntimeModule_Initialization.cs`、`Integration/BossRushIntegrationRuntimeModule_RuntimeHooks.cs`。所有同名候选经正文核实；两个改名状态额外追到 RuntimeHooks。原 Initialize / RegisterCustomWeaponRuntimeConfigs 主体仍在 Initialization；其原本已存在的外部 Factory/Registry 调用不另扩张。
- `FenHuangHalberdBootstrap`：InitializeFenHuangHalberdSystem、SetupFenHuangHalberdForScene、DelayedSetupHalberdAbility、CleanupFenHuangHalberdSystem → `Integration/DragonKing/Weapons/FenHuangHalberdBootstrap.cs`、`Integration/IntegrationHostCompatibility.cs`、`Integration/DragonKing/DragonKingRuntimeModuleHostBridge.cs`。保留算法文件，并完整计入口载体与 owner 绑定文件。

固定规则集合：`AGENTS.md`、`Integration/AGENTS.md`、`docs/contracts.md`；共 101,137 B。

BossRushDynamicItemRegistry、ItemFactory、CustomItemRuntimeStateHelper、NewWeaponRuntime、各物品 RegisterItemContentConfigurators 的实现，在基线已是样本外调用或状态定义；未把所有 Integration partial 一并加入。当前登记函数真实语句已核对，没有将其薄代理误当最终 owner。

## Mode F 奖励调整

完整文件后测集合：

- `ModeF/ModeFBounty.cs` — 34,733 B。原悬赏、成长、换装/掠夺记账主体保留，宿主改为 ModeFRuntimeModule；击杀闩和 config 判定须继续追踪。
- `ModeF/ModeFBounty_EquipmentAndLoot.cs` — 25,327 B。原 15 个方法完整保留；奖励池调用现在经 arena，原先也已是外部方法，不误计为本文件算法缩减。
- `ModeF/ModeFEntry.cs` — 16,860 B。原会话、检测、扣费/退款、启动主流程保留；StartModeF 内联的竞技场重置移到 ModeD 共享入口。
- `LootAndRewards/ModeEFLootboxTracker.cs` — 399 B。原 enum 和 AwenLootSweepTarget 类型留在此文件；不能以其 399 B 代替原扫箱实现。
- `ModeF/ModeFHostBridge.cs` — 9,493 B。原 Mode F 宿主公开/内部入口所在完整兼容文件。
- `ModeF/ModeFRuntimeModule_BountyLatch.cs` — 1,198 B。原两字段及 Latch / Consume / Reset 算法的真实 owner；现在另提供 Has 查询。
- `ModeF/ModeFRuntimeModule.cs` — 3,588 B。原样本现访问的 modeFState / modeFActive owner；保存共享 Mode D/E、arena、spawnPreparation 引用和委托绑定。
- `ModBehaviourRuntimeModules.cs` — 16,450 B。原 ShouldUseModeFAbstractPlunderLootTracking 的 config != null && config.enableRandomBossLoot 判定现在在 BindSharedServices 的 useRandomBossLoot 闭包中，计完整装配文件。
- `ModeD/ModeD.cs` — 14,713 B。StartModeF 原 5 条竞技场字段重置和 ClearCashMagnetState 调用的真实共享算法现在是 ResetArenaForModeD；计合并后完整文件。
- `WavesArena/WavesArenaRuntimeModule.cs` — 9,255 B。上述 5 个竞技场属性的真实可写状态 owner；其宿主属性桥在已计入的 LootAndRewards.cs。
- `LootAndRewards/AwenLootSweepRuntime.cs` — 21,852 B。原扫箱常量、计数、目标缓存、runner 引用、目标构建、发令牌和退款的实际 owner；包含绑定委托。
- `LootAndRewards/LootAndRewards.cs` — 34,855 B。保留原 3 个模式/会话上下文判断和其余扫箱宿主入口，另包含竞技场字段桥；必须计整个合并载体。

原成员映射：

- `ModeFBounty`：击杀闩两字段及 Latch / Consume / Reset → `ModeF/ModeFBounty.cs`、`ModeF/ModeFRuntimeModule_BountyLatch.cs`。原同名方法现只转发，字段已改名，必须计真实状态和算法文件。
- `ModeFBounty`：ShouldUseModeFAbstractPlunderLootTracking → `ModeF/ModeFBounty.cs`、`ModeF/ModeFRuntimeModule.cs`、`ModBehaviourRuntimeModules.cs`。新委托的实际 config 判断必须计入装配文件，不能只算 return useRandomBossLoot()。
- `ModeFEntry`：StartModeF 内联竞技场重置；新 owner / 共享绑定 → `ModeF/ModeFEntry.cs`、`ModeD/ModeD.cs`、`LootAndRewards/LootAndRewards.cs`、`WavesArena/WavesArenaRuntimeModule.cs`、`ModeF/ModeFRuntimeModule.cs`。5 条赋值与清理调用抽到共享 ResetArenaForModeD；继续追到状态 owner，不能停在宿主属性桥。
- `ModeEFLootboxTracker`：全部原声明与方法 → `LootAndRewards/ModeEFLootboxTracker.cs`、`LootAndRewards/AwenLootSweepRuntime.cs`、`LootAndRewards/LootAndRewards.cs`。数据类型留原文件；运行时缓存与算法在 Awen owner；3 个真实模式/会话判断仍在宿主载体，三者均完整计入。

固定规则集合：`AGENTS.md`、`docs/contracts.md`；共 90,620 B。

shared_external_routes 明列旧样本已是跨文件调用的当前落点，不把整个 Mode E/D/Utilities 依赖闭包重新加入历史 4 文件样本。原样本内部实际移出的赋值、config 判断、闩和扫箱算法均已计入当前集合。Mode E 共享服务引用通过已计入的 ModeFRuntimeModule 与 ModBehaviourRuntimeModules 核实。

已核实但不扩张入固定样本的原外部共享调用：

- `FindFirstPlayerInventoryItemByTypeId / IsPlayerNakedWithAllowedItems` → `Utilities/ModeEntryInventory.cs`。
- `DetectFactionFlag / TryConsumeModeEntryItem` → `ModeE/ModeEStartup.cs`。
- `InitializeModeDItemPools / EnsureModeDGlobalItemPool / GivePlayerStarterKit` → `ModeD/ModeDItemPool.cs`、`ModeD/ModeDGlobalLoot.cs`、`ModeD/ModeDEquipment_StarterKit.cs`。
- `EnsureModeEFSpawnPoolsReady / ModeESpawnAllBosses` → `Utilities/ModeEFEnemySpawnRuntime.cs`。
- `PreCacheMapSpawnerPositions / AllocateSpawnPoints / TeleportPlayerToSafePosition` → `Utilities/ModeEFSpawnPreparation.cs`。
- `ModeEGiveColdWeatherGear` → `ModeE/ModeEIntegrityAndHelpers.cs`。
- `SpawnModeEMerchant` → `Utilities/ModeEFMerchantRuntime.cs`。
- `GetRandomInfiniteHellHighQualityRewardTypeID` → `WavesArena/WavesArenaRuntimeModule_RewardPool.cs`。
- `PrepareModeESharedRuntimeForModeF` → `ModeF/ModeFRespawn.cs`、`ModeE/ModeEStartup.cs`。

## 验证范围

本度量完成文件存在性、逐文件原文差异定位、真实 owner 追踪、完整字节求和和 SHA 清单。它不验证运行时行为或性能，不替代编译、守卫、隔离回归与实机结果。
