# 模块解耦迁移状态

依据：`docs/design/2026-09-22_BossRushMod模块解耦与上下文治理计划.md`（2026-09-24 修订版）。本文件在每个检查点更新；续接时先看本文件和 `git log -5`。

## 当前进度

| 检查点 | 状态 | 证据或下一步 |
| --- | --- | --- |
| P0 基线 | 完成，`3323e33e` | 665 项全量守卫 PASS，59 项全量回归 PASS；隔离正式与 Dev 构建 PASS |
| P1 上下文治理 | 完成，`adef32ef` | 根规则 180 行 / 17,329 B；台账 1,050 / 975 行；47 模块导航覆盖 1,020 源 |
| P2 复用试点 | 完成，`403a09a4` | 词缀追踪器与建筑恢复核心；203 项相关守卫、60 项全量回归与两种隔离构建通过 |
| P3 状态提取 | 完成，终态 `cae48e91` | 宿主 58 文件 / 20,114 行；模式/系统状态归模块，生命周期、装配和跨模式入场协调保留 |
| P4 耦合点 | 完成 | §6 第 1、3–9 条已处理；第 2、10 条按计划保持，同步回归通过 |
| P5 目录归位 | 完成，`6409a0f4` | 天空岛 99 源与规则归位；697 守卫、14 项相关回归、两种隔离构建通过 |
| P6 收口 | 离线完成（本节所在提交） | 后测、实机清单、697 守卫、95 回归、两种构建及正式真实部署已完成；L3 待 owner |

当前结论：P0–P6 离线完成，实机待验收。宿主 58 文件 / 20,114 行，正式 DLL 已部署真实游戏目录并核对 SHA-256。下文历史检查点保留当时的未完成说明，当前状态以本表及末节 P6 为准。P6 提交号用 `git log -1 -- architecture/MIGRATION_STATUS.md` 查询；最后的用户交付同时列出该号。

## P0 基线（2026-09-24）

- 启动前 `git status --short --branch` 为干净的 `main...origin/main`。构建依赖：本机 .NET SDK 8.0.302、游戏 `D:\software\steam\steamapps\common\Escape from Duckov`、Harmony 创意工坊 DLL。隔离游戏根在忽略的 `Build/migration/isolated-game/`，其中 `Duckov_Data/Managed` 为真实游戏 DLL 的文件副本；未接触真实游戏 Mod 目录。
- 编译清单：1,020 个 `.cs`；宿主 partial：202 文件 / 103,052 行，预算 202 / 103,200。根 `AGENTS.md` 317 行 / 38,493 字节，`CLAUDE.md` 361 字节，自动导入链合计 38,854 字节。`FIX_TRACKER.md` 9,342 行 / 1,033,436 字节；`CODE_REVIEW_FINDINGS.md` 5,225 行 / 617,431 字节。上下文任务前测详见 `CONTEXT_BASELINE.md`。
- 全量守卫：665 PASS、0 FAIL、0 known-red（68.9 秒）。编译清单双向守卫 1,020 源文件 PASS；partial 预算守卫 PASS。
- 首轮执行回归：52 PASS / 7 FAIL，失败项为 `AuditCombatSeptember`、`AuditModeLifecycle`、`ModeHRecoverySecondReview`、`ModeHReviewFixes`、`NpcAuditFixes`、`SkyIslandLighting`、`SkyIslandOfficialContract`。其中 3 项缺官方 DLL 环境变量，2 项要求本机不存在的 .NET 10，2 项因聚合运行器传入输出路径与夹具的 `Directory.Build.props` 冲突。修正环境、目标框架和运行器后为 58 PASS / 1 FAIL；剩余 `ModeHRecoverySecondReview` 的奖励选择断言仍点旧版底栏按钮，生产页已将两个选择改为卡片。夹具改为点真实卡片后单项 PASS。首轮结果归档于忽略的 `Build/migration/p0-regressions-original.json`。
- 修复验证链后全量回归为 59 PASS / 0 FAIL，结果归档于忽略的 `Build/migration/p0-regressions-final.json`。本轮改动的 `--changed-only` 守卫 15 PASS / 0 FAIL。
- 隔离正式构建：`Build succeeded!`，正式 Dev 标识检查 `absent` PASS，DLL SHA-256 `395C6229BCC7DF060448E1BBDE6A7304E12F9235CDFF49C758515DE58A941432`。首次隔离根未预建 Mod 目录，自动复制提示失败；编译本身成功。之后预建目录。
- 隔离 Dev 构建：`Build succeeded!`，标识检查 `present` PASS，DLL SHA-256 `E0FABBDBBDB5987F00C5CC42F669DCAA2EEFA4D36B86E3AC9246AB6C7887B369`；隔离目录中的 DLL 哈希一致，发布资源清单 72 个 bundle 哈希通过。日志与哈希记录在忽略的 `Build/migration/`。

## P3 簇盘点与迁移

### 簇 1：BossFilter、UIAndSigns、Injection（`71b68f60`，COMPAT）

- 字段：Boss 启用表、无间炼狱因子、过滤缓存及脏标记、窗口 Canvas / 面板 / 控件与模态租约由 `BossFilterRuntimeModule` 持有；消息文本、计时器、横幅去重、路牌 / 垃圾桶引用由 `UIAndSignsRuntimeModule` 持有。宿主仅保留旧公开入口和调用点的转发，以及波次逻辑共用的竞技场中心静态值。
- 协程：路牌扫描、创建重试的 `IEnumerator` 实体迁到 UI 模块；原宿主启动位置及等待时序不变。事件：本簇原本没有新增全局订阅，Boss 筛选面板销毁经模块 `OnDestroy`；计时器与窗口状态跟随模块实例。静态缓存：通知持续时长调节标记保持原静态语义；`Injection/Injection.cs` 为 22 行无成员占位，检索仅见编译清单与索引引用，已从受跟踪文件、清单、预算和导航删除。
- 公开入口：Boss 筛选和路牌旧签名保留在薄桥，转给唯一模块实例；Boss 配置仍由宿主保存，筛选失效继续通知遗种巢与图鉴。`GetDirectionFromPlayer` 的实体迁入 UI 模块，随机事件原调用留兼容转发。
- 新 `BossFilterRuntime` 执行回归抽取生产筛选方法，覆盖配置、默认因子、缓存失效、两个目录刷新及两个 owner 隔离，1 PASS / 0 FAIL；人为令过滤判据恒真后按预期转红，原文件按 SHA-256 还原。`ModeGCombat` 1 PASS / 0 FAIL。六个相关结构守卫的真实调用锚点作破坏转红与逐字还原。
- 用 Git 暂存区单独导出簇 1 快照（不含并行叶子改动）后，全量守卫 669 PASS / 0 FAIL / 0 known-red；预算 202 文件 / 103,052 行降为 198 文件 / 100,642 行，上限同步下调为 198 / 100,700。正式隔离构建 `Build succeeded!`、Dev 标识缺席、SHA-256 `6FA33504E00D1C1EEC30CE3D3507BDE0D628EF9EECB7AA38FF0B8D9089E22237`；Dev 隔离构建 `Build succeeded!`、Dev 标识在位、SHA-256 `D11C60E85A079C7DC5A6B289D44621DDEB7905430AA0966A109D463AE48DB4DB`。两者部署到隔离游戏根后的 SHA-256 都与构建副本一致，72 个 bundle 清单验证通过。未启动游戏，L3 待 owner。

### 簇 6：Integration 部分叶子与提前完成的 P4 耦合点（`74251963`，COMPAT）

- 字段归属：DeathWraith、Bonus 套装、DragonKing / DragonDescendant / PhantomWitch、Wedding、WishFountain、FlightTotem / Frostmourne / ReverseScale、Goblin、Affinity 的状态由各自 RuntimeModule 或独立 owner 持有。原宿主公开签名保留薄桥；`BossRushRuntimeModuleRegistration` 装配顺序与 `AlwaysOnRuntimeHooks` 调度位置未改。`IntegrationDeferredBootstrap` 的阶段顺序未改。`BossRushIntegration_*` 等其余宿主 partial 尚未提取，簇 6 不得记为完成。
- 生命周期：各叶子的事件订阅与退订、协程启动与停止、静态缓存重置按原入口迁移；死亡亡魂与套装的夹具覆盖 owner 隔离和清理。运行时轨迹仅经 L1/L2 静态与隔离验证，未启动游戏。
- P4 §6 第 5 条：共享字体、CanvasScaler、模态租约、timeScale / Cursor 快照归 `Common/UI/BossRushUIFoundation`；丧尸帮助类只转发。两个消费者并存的租约夹具及反向验证通过。第 6 条：`ValidationHasActiveMode` 从 F3 partial 搬到正式可达的 `Utilities/ModeRuntimeHooks`；正式入口与 F3 均沿用同一判据。第 8 条：临时 NPC 商店创建者传入现金 / 净化点策略，通用交易仍按官方回调、交付、扣款 / 回滚顺序执行；生产方法抽取夹具验证两种支付和失败回滚。第 1、3、7 条待做。
- 索引：`architecture/modules.json` 的 12 个 Integration owner / 入口同步改写，`MODULES.md` 由工具再生；`task_context.py --check` 报 47 模块 / 1,036 编译源。宿主 partial 由 198 文件 / 100,642 行降至 179 文件 / 86,974 行，预算下调为 179 / 87,000。实例分类表与守卫同步更新。
- 验证：全量守卫 675 PASS / 0 FAIL / 0 known-red；修改或新增的守卫以真实破坏转红并逐字还原。全量执行回归 64 PASS / 0 FAIL（Harmony 指向创意工坊 `3588386576/0Harmony.dll`）；隔离正式构建 `Build succeeded!`，Dev 标识缺席，SHA-256 `7A0BAB42FD41CF9F5946E7E7AA662DA7056C5216D87796BAF8E369248EB1E91B`；隔离 Dev 构建 `Build succeeded!`，Dev 标识在位，SHA-256 `A903E60E8AE9AC9B94FE50A31CA5548F122194DA57FC9CD24F0069E5C9565EBB`。两种 DLL 的隔离部署哈希一致，72 个 bundle 清单核对通过。当前 `Build/BossRush.dll` 是 Dev，真实游戏目录尚未部署。

### 簇 2、簇 5、簇 6 后续子范围（进行中，COMPAT）

- 簇 2：`InfiniteHellCashMagnet.cs` 的飞行集合、计时器、缓冲与处理方法归 `WavesArenaRuntimeModule`；原 `UpdateCashMagnet` / `ClearCashMagnetState` 保留一行宿主转发，Mode D/E/F 与场景清理仍从原入口调用。波次倒计时、敌人数、每波 Boss 数与当前波 Boss 列表、无间炼狱现金池/里程碑/高品质候选缓冲、完整性自检与大兴兴清理计时器也由同一个已注册实例持有，原调用点暂经属性访问；新增归属守卫反向转红并逐字还原。宿主预算从 179 / 87,000 下调到 179 / 86,800；簇 2 其他波次与奖励 partial 尚未迁移。
- 簇 5：`ZombieModeRuntimeModule` 已拥有局状态、入场事务、奖励候选缓存、待入场标记、暂停时钟和 run ID；宿主兼容桥在挂载时交接原引用，销毁时交还。入场门控、资源提交/退款、场景保留与等待、run 初始化及状态推进的 23 个方法体迁入模块；宿主保留公开兼容入口。未挂载备用分支补齐 Mode G 隔离门。8 个专项守卫做破坏转红、SHA-256 还原；`ZombieModeEntryDebt` 与链接模块生产爆炸方法的 `AffixCombat` 通过。Tick、暂停时钟及其余奖励/战斗/清理业务还在宿主，不记簇完成。
- 簇 6：`IntegrationRuntimeModule` 已拥有商店扫描/缓存、船票、日志与砖石库存的静态缓存和存档订阅，以及购买计数/事件、状态监控与重铸恢复协程句柄、龙息持握事件 owner；旧宿主入口转发，原订阅槽位顺序不变。`IntegrationRuntimeModuleGuard`、`MenuSceneRuntimeHookGuard` 与 `ContentRegistryGuard`、`NPCShopPayment` 回归通过，修改的 owner 守卫反向转红且字节还原。地图克隆/撤离点、共享出生点、传送与跨模式场景编排、延迟 bootstrap 等余项待后续步骤，不记簇完成。

### P4 后续耦合点（进行中，COMPAT）

- §6 第 1 条：`Common/Lifecycle/BossRushRuntimeModuleRegistration.cs` 移到根 `ModBehaviourRuntimeModules.cs`；清单、预算、守卫及 repowiki 文件链接同步。定向守卫通过；15 个改路径守卫在移除注册内容的反向探针中均转红，字节还原。第 2 条：新增 `ModeRuntimeDispatch` 夹具，抽取生产 `TickModeRuntimeGroup`，验证共享刷怪后处理、Arena 早返与 E/F/G/Zombie/清理顺序及时间源；1 PASS；人为重复 F 调度后回归转红，源码哈希还原。
- 第 3 条：Mode F 模块拥有悬赏击杀 victim 闩；`HasCampaignBountyMark` 纯查询，死亡采集器在原点显式消费。`CampaignPlayability` 直接链接生产 Mode F 模块，验证先写后删、错 victim 不消费、正确 victim 一次消费，1 PASS；新结构守卫反向转红、逐字还原。
- 第 7 条：`EquipmentFactory` 改为按 key 原位替换的配置器登记，枪械前置配置与常规配置分开；各 Config 自行登记，`EquipmentConfiguratorBootstrap` 按旧顺序装配，Item 初始化先于装备加载。`DynamicItemInitialization`、`ManualEquipmentRecovery`、`EquipmentConfiguratorRegistry` 各 1 PASS；新守卫与夹具反向探针均转红并还原。
- 本批完成后的整树证据（L1/L2）：全量守卫 679 PASS / 0 FAIL / 0 known-red；全量执行回归 66 PASS / 0 FAIL / 0 SKIP，初轮两项旧夹具替身失配已修并复跑。编译清单 1,040 源，模块索引 47 模块；宿主 partial 179 文件 / 85,427 行，预算 179 / 86,800。修正新文件空白行后复建：隔离正式构建 `Build succeeded!`、Dev 标识缺席，DLL SHA-256 `741AC9DA699A5BCB8C317793A7153E85C7FEBB5DDD66AB90CE8418F11AB55EDC`；隔离 Dev 构建 `Build succeeded!`、Dev 标识在位，DLL SHA-256 `58B20F3A55773DF6D06F79DC60201D053F85F8693982567F4409529D2B8F87E8`。两种 DLL 分别与隔离发布副本 SHA-256 一致，72 bundle 清单通过。`Build/BossRush.dll` 当前为 Dev；未启动游戏、未部署真实游戏目录，L3 待 owner。

### 簇 2、5、6 后续可恢复步骤（当前批次，COMPAT）

- 簇 2 状态：`WavesArenaRuntimeModule` 新增敌人预设池、扫描次数、基础生命范围、会话初始化标记、刷怪器禁用闩及原反射缓存；宿主旧属性和入口保留转发。`WavesArenaSpawnerControl.cs` 改为模块 partial，禁用刷怪器本帧置位、分帧灯光保留和卡波修复方法体归模块；`WavesArenaRuntimeModule_Tick.cs` 执行倒计时、完整性检查、大兴兴清理 Tick，`WavesArenaRuntimeHooks.cs` 只作原调用点薄桥。原 `ModeRuntimeDispatch` 顺序与时间源未改。宿主当前波之外的刷怪、奖励业务仍在原 partial，簇 2 仍未完成。
- 簇 5：`ZombieModeRuntimeModule` 接管 `TickZombieMode`、暂停判据和 `unscaledTime` 时钟；宿主调度入口和旧暂停 API 作薄桥。13 条生产方法抽取断言覆盖控制器顺序、原 `deltaTime`、暂停冻结与恢复、不活动局复位；6 项结构守卫反向转红且逐字还原。其余奖励、临时 NPC、RunOnly 清理业务仍待迁。
- 簇 6：地图克隆配置、生成协程、撤离点创建与场景初始化等待移至 `IntegrationRuntimeModule` 的 `BossRushIntegrationRuntimeModule_MapObjects.cs`；宿主保留地图生成、等待与完成后的挑战设置三个薄桥。共享出生点、传送器和跨模式场景协调仍在宿主。`IntegrationRuntimeModuleGuard` 在断开生产协程调用后转红并逐字还原。按原入口核对，Ground Zero 中地图生成与禁用刷怪器相对顺序未变。
- 当前 L1/L2：全量守卫 679 PASS / 0 FAIL / 0 known-red；全量执行回归 66 PASS / 0 FAIL / 0 SKIP。首轮 `AuditModeLifecycle` 因预设池入模块而缺少夹具的 `EnemyPresetInfo` 替身，修补后定向与全量复跑通过。Arena 结构、异常、状态归属及清理守卫均做真实破坏转红和 SHA-256 逐字还原。编译清单 1,042 源，模块索引 47 模块，宿主 partial 178 文件 / 84,357 行，预算降至 178 / 84,400。隔离正式构建 `Build succeeded!`、Dev 标识缺席，SHA-256 `7CEFBBC162A8593BE9B15CEEE8D2F51FD556FC4540FE56F31121CFD1B1D536E2`；隔离 Dev 构建 `Build succeeded!`、标识在位，SHA-256 `8E34CD937000A5ABF72E40C3A83C44FF9220A2AA7A90C6E5B017A83DFFCF1F37`。两种 DLL 均与隔离发布副本哈希一致，72 bundle 清单通过。当前 `Build/BossRush.dll` 为 Dev；未启动游戏，未部署真实游戏目录，L3 待 owner。

### 当前可恢复步骤：奖励、RunOnly 与延迟初始化（COMPAT）

- Arena：`WavesArenaRuntimeModule` 接管前期波次强 Boss 排除、宝箱模板 / 物品价值 / 候选集静态缓存以及无间炼狱高品质奖励抽取；`ModBehaviour` 保留旧签名薄桥。多 Boss 刷怪点选择迁到 `Utilities/SpawnPositionHelper`，保持原三维距离与补足顺序；原有丧尸单点选择仍按 XZ 距离。`SpawnPositionPolicy` 与 `AuditModeLifecycle` 的定向执行回归、相关守卫及反向验证已通过。其他 Arena 刷怪、奖励与结算业务仍待迁。
- Zombie：奖励准备时长选项、默认值与阶段门归 `ZombieModeRuntimeModule`；宿主和地图选择 UI 经薄桥调用。`AuditModeLifecycle` 覆盖 20 个选项、边界钳位、跨波次选值；相关守卫反向转红后按字节还原。RunOnly 记录注册、剪枝、局失效和逆序清理也归模块，旧宿主入口转发；清理动作、停协程、销毁对象的顺序保持。
- Integration：`BossRushIntegrationRuntimeModule_DeferredBootstrap.cs` 接管延迟初始化的阶段状态与协程，宿主保留入口和回调绑定；基础物品、`EssentialContentReady`、配偶 / 建筑恢复、装备加载及场景占位符的顺序未改。`DeferredBootstrapGuard` 反向验证通过；五个旧路径守卫已按新生产文件定位并完成反向验证；整树守卫与回归均已通过。
- 当前编译清单 1,044 源，宿主 partial 实测 177 文件 / 83,562 行，预算下调至 177 / 83,600。改动相关守卫 184 PASS / 0 FAIL；全量守卫 679 PASS / 0 FAIL / 0 known-red，修改的 RunOnly 守卫均反向转红并按字节恢复；全量执行回归 67 PASS / 0 FAIL / 0 SKIP；RunOnly 清理夹具直接抽取生产模块、记录与 `RunScopedRegistry` 验证反向回收及事件顺序。隔离正式构建 `Build succeeded!`、Dev 标识缺席，SHA-256 `2252254A5F47D05553A16B01076A9DBB049C5236DB191C16E3D5C9A7B153D221`；隔离 Dev 构建 `Build succeeded!`、标识在位，SHA-256 `FC4AD39ADEAC84DC0B93A96DCF133164542EF0A7DF3F1DB537EF983594BDB5B9`。两种 DLL 均与隔离发布副本哈希一致，72 bundle 清单通过。本节离线验证完成，真实游戏目录未动，L3 待 owner。

### 当前可恢复步骤：预设、波次结算、入场转存与物品引导（进行中，COMPAT）

- 簇 2：`WavesArenaRuntimeModule_EnemyPresets.cs` 接管预设全量扫描、非 Boss 剪枝、基地幂等预热、静态反射缓存及无间炼狱加权抽取；`WavesArenaRuntimeModule_BossSpawning.cs` 接管单/多 Boss 生成与原重试等待；`WavesArenaRuntimeModule_Countdown.cs` 接管间隔、里程碑加时与横幅；`WavesArenaRuntimeModule_WaveDeaths.cs` 接管本波成员判定、击杀去重与推波；首波初始化、持续清场、无间炼狱完成回调和掉落追踪/奖励临时状态也归同一模块。宿主保留原订阅委托与兼容入口薄桥；持续清场仍经窄查询读取原共享角色缓存与 Mode E 身份。`WavesArenaPresetWeight` 直接抽取生产选择器，覆盖空池、因子回退、波次血量权重、用户因子和随机数调用次数；去掉波次权重会转红。死亡成员闸、关键异常日志、首波顺序及状态归属守卫按实际模块方法反向转红并逐字还原。簇 2 的剩余掉落、奖励和结算方法仍在宿主，不记完成。
- 簇 5：入场物品转存/回滚已迁入 `ZombieModeRuntimeModule_InventoryTransfer.cs`；敌人 marker 注册与缓存正在迁入 `ZombieModeRuntimeModule_EnemyRuntime.cs`。保持原物与 inbox 唯一副本语义、RunId 与事件相对顺序；相关旧路径守卫和执行回归由独立叶子收尾。
- 簇 6：Wiki Book 与生日蛋糕初始化状态归 `IntegrationRuntimeModule`，保留一次性 latch、原 TypeID / 资源 / 本地化 / 存档键；原 2 秒生日赠礼等待和 DebugGive 入口保持。`BirthdayCakeGift` 从生产协程抽取，已通过聚合执行回归，两个专项守卫做反向验证。出行叶子将准备轮询、落点计算、子场景传送器扫描与出生点配置解析归同一模块；`SetupBossRushInGroundZero` 编排 Mode D/E/F/G/H/Zombie，按 §2.2 保留宿主，`currentMapSpawnPoints` 仍由宿主兼容入口赋值。Travel 专项守卫和 `ModeHSceneEntry` 执行回归已通过反向验证。
- 本批离线检查点：编译清单 1,057 源，模块索引 47 个模块；宿主 partial 177 文件 / 80,384 行，预算下调到 177 / 80,400。全量守卫 679 PASS / 0 FAIL / 0 known-red；全量执行回归 69 PASS / 0 FAIL（SKIP 0）。隔离正式构建 `Build succeeded!`、Dev 标识缺席、SHA-256 `7D5C5F881C4AA23F926403DC4A896657AC97CF8ADF6A450B5C48C01EA1F8E44E`；隔离 Dev 构建 `Build succeeded!`、标识在位、SHA-256 `D50D5CA925A3F3E164CB422A9193BC6C434A8EC5A5B6179C539BB9553C1F6CB5`。两种 DLL 与隔离发布副本哈希分别一致，72 个 bundle 清单通过。真实游戏目录未动，L3 待 owner。
- 下一动作：完成簇 2 余下掉落与奖励主体；簇 5 的 HUD 叶子在本批提交后开始；簇 6 剩余 Integration partial 再逐片盘点。P3 簇 3、4、7，P4 §6 第 9 条，P5 与 P6 均未完成。

### 当前可恢复步骤：随机掉落、污染、Boss 控制器与快递员（COMPAT）

- 簇 2：已注册的 `WavesArenaRuntimeModule` 接管 Boss 掉落候选/价值缓存、品质保底、警告节流、难度奖励箱清理协程及 scratch、Boss 掉落事件追踪与退订、龙裔/龙王专属奖励、随机 Boss 奖励箱的反射缓存和候选缓冲。`LootAndRewards` 旧调用签名继续转发；随机事件奖励箱共用移入模块的反射字段，保底与龙系成就仍在成功入箱后投递。`OnBossBeforeSpawnLoot`、玩家死亡跨模式收尾、标准胜利与奖励箱演出等尚留宿主，不记簇完成。
- 簇 5：HUD 控制、Boss 控制器及污染技能/词条主体迁入 `ZombieModeRuntimeModule` 的分片，旧入口只作兼容转发；污染模块按热路径与调参职责拆成三个低于 1,200 行的分片。Boss 控制器清理 Frenzy 修饰器及分裂刷怪仍从原调度位置进入。其余丧尸奖励、临时 NPC、波次控制及清理业务尚待迁移，不记簇完成。
- 簇 6：物品商店注入和内容初始化方法迁入 `IntegrationRuntimeModule`，宿主旧入口保留；快递员 NPC 的实例、控制器与静态资源缓存归唯一 `CourierNpcRuntimeModule`，旧公开入口与只读状态属性转发。卸载仍先走 `DestroyCommonNPCs` 释放待领扫箱结果，再走模块逆序销毁。图鉴书商店库存等其他 Integration 宿主状态待续。
- 守卫按生产方法新位置收敛，修改的守卫经定点破坏转红、按字节还原。`BossRewardDelivery` 从龙系新生产文件抽取方法；`AirdropSecondReview` 替身匹配迁入模块的同一反射类型；`AffixCombat` 抽取丧尸污染模块的真实爆炸方法。首次全量回归分别因 `AffixCombat`、`AirdropSecondReview` 旧替身失配各有 1 FAIL；修正验证链后全量 69 PASS / 0 FAIL / 0 SKIP。全量守卫 680 PASS / 0 FAIL / 0 known-red。
- 本批编译清单 1,069 源，索引 47 模块；宿主 partial 177 文件 / 74,535 行，预算从 177 / 80,400 下调到 177 / 74,600，预算破坏探针转红并按字节还原。隔离正式构建 `Build succeeded!`、Dev 标识缺席、DLL SHA-256 `85C1C36229CD2E7AF09802EDB332AB4BD041EC6642EB52E2A0C6457812EEC926`；隔离 Dev 构建 `Build succeeded!`、Dev 标识在位、SHA-256 `69142705B24716FA93C3AB568930EC38217854B07C67EE9D7B7A40B19AF625E2`。两种 DLL 与隔离发布副本哈希分别一致，72 个 bundle 清单通过。真实游戏目录未动，L3 待 owner。
- 下一动作：继续簇 2 胜利奖励与掉落事件漏斗；簇 6 图鉴书库存叶子；随后主窗口推进簇 3 Mode D、簇 4 Mode E/F、簇 7 宿主收口。P4 §6 第 9 条、P5、P6 仍未完成。

### 当前可恢复步骤：标准通关、Boss 掉落事件、撤离与奖励目录（`9667fdb9`，COMPAT）

- 簇 2：`WavesArenaRuntimeModule_VictoryRewards.cs` 接管标准通关两秒等待、虚影箱起落、Mode G 严格物化器入口与难度奖励箱生成；`WavesArenaRuntimeModule_BossLootEvent.cs` 接管 Boss 死前掉落分流与 Mode E/F 原生箱路径；`WavesArenaRuntimeModule_SpecialLoot.cs` 接管箱内专属奖励、Mode F 掠夺调整、额外掉落回退与无间炼狱世界投放。宿主原签名只转发；静态常量与 scratch 随生产方法迁走。旧随机数调用、Loader 等待、`finally` 清理以及无间炼狱先投放后 Finalize 的顺序保留。胜利奖励与掉落专题文档已更新。`OnPlayerDeathInBossRush` 的跨模式死亡收尾仍留宿主，簇 2 尚未完成。
- 簇 5：丧尸撤离/信标/安全区业务已提取到 `ZombieModeRuntimeModule_Extraction.cs`，奖励目录与选择流程已提取到 `ZombieModeRuntimeModule_RewardCatalogAndSelection.cs`；旧 UI 入口保留桥。撤离叶子 20 项相关守卫与 20 项定点反向变异通过，奖励目录叶子 13 项核心守卫反向变异通过；`AuditModeLifecycle` 抽取生产方法验证结算时序。簇 5 尚有其余宿主业务。
- 簇 6：图鉴书库存、注入与切档处理已提取到 `BossRushIntegrationRuntimeModule_CodexBook.cs`，宿主桥与守卫由叶子收尾。其余 Integration 宿主业务仍待提取。
- 当前文件数为编译清单 1,075 源、47 模块；宿主 partial 177 文件 / 71,168 行，预算上限从 74,600 下调至 71,200 并做破坏转红/逐字还原。相关守卫的迁移锚点按新生产方法反向验证。全量守卫 680 PASS / 0 FAIL / 0 known-red；全量执行回归 69 PASS / 0 FAIL / 0 SKIP，其中 `AuditModeLifecycle` 已抽取迁移后的撤离成功结算与分发方法。隔离 Dev 构建 `Build succeeded!`、标识在位，SHA-256 `EEE1F5B474D15DE118AD6E09C47BECBD0BECBD377EA575ECDEE78A653A3C858B`；隔离正式构建 `Build succeeded!`、标识缺席，SHA-256 `2993084102D25D6241C3F9AD4350BD67471121A7540DA7C048BCDA9023354C38`。两种 DLL 均与隔离发布副本哈希一致，72 bundle 清单通过。真实游戏目录未动，L3 待 owner。
- 下一动作：继续簇 2 跨模式死亡漏斗和簇 3 Mode D。P3 簇 3、4、7，P4 §6 第 9 条，P5 与 P6 均未完成。

## P1 上下文治理（2026-09-24）

- 根 `AGENTS.md` 从 317 行 / 38,493 B 降至 180 行 / 17,329 B，§4.1、§4.3 原节逐字保留。`CLAUDE.md` 自动导入链从 38,854 B 降至 17,811 B。五项代表任务的源码与规则字节前后对照在 `CONTEXT_BASELINE.md`；实际 token 用量未测。
- 原 §4.14、§4.16、§4.17 的全文逐字迁到 `Common/UI/AGENTS.md`、`Integration/AGENTS.md`、`DebugAndTools/AGENTS.md`。旧 §3 路径表原文在 `archive/AGENTS_SUBSYSTEM_MAP_2026-09-24.md`，导航由 47 模块的 `architecture/modules.json` 与生成的 `MODULES.md` 提供。
- `FIX_TRACKER.md` 从 9,342 行降至 1,050 行；`CODE_REVIEW_FINDINGS.md` 从 5,225 行降至 975 行。8 月与 9 月整节原文存入各自的 `archive/` 月份文件，主文件保留索引、未闭环 finding。近 14 天的大篇幅已闭环详情也归档，只在正文保留索引，以同时满足行数上限；原节正文未改字。`SkyIslandOfficialApiReuseGuard` 改从归档读取其完整决策节，内置 28 个反向探针仍通过。
- 全量守卫 668 PASS / 0 FAIL。新 `RootAgentsBudgetGuard`、`LedgerSizeGuard`、`ModuleIndexGuard` 分别作破坏转红、按字节还原；模块索引验证了无效 glob 和生成区漂移两种破坏。首次仅改台账标题是等价变异，未转红，随后改为超过 1,500 行并转红。归档文段标题破坏也使 `SkyIslandOfficialApiReuseGuard` 转红。还原后各守卫 PASS。
- P1 无生产 C# 变更，执行回归沿用 P0 全量 59 PASS / 0 FAIL。隔离正式构建 `Build succeeded!`、Dev 标识缺席、DLL SHA-256 `2FCE9388A09E27232C7FC69C36ED03ADB36EE34490FE4CD7B0CEB2451A989317`；隔离 Dev 构建 `Build succeeded!`、Dev 标识在位、SHA-256 `BED667125B7F5E596EF93799D2C97307B4BD753C8608E309938307C935824704`。两者各自与隔离目录部署哈希一致，72 bundle 清单通过。

## P2 复用试点（2026-09-24，COMPAT）

- 词缀运行时属性改用 `Common/Stats/RuntimeStatModifierTracker` 的显式 `ModifierType` 入口；词缀原失败日志由静态回调保留，移除仍用 `RemoveAll`。删除重复的 `AffixStatModifierApplier`。记录类统一为 `BossRushStatModifierRecord` 并移到共享追踪器；生产引用与夹具一次改名，不保留旧类型别名。`AffixCombat` 直接链接生产追踪器，覆盖 `Add`、`PercentageAdd`、`PercentageMultiply`、失败、移除和 owner 隔离。
- 报箱与遗种巢各持一个 `BuildingRestoreCore`；核心承担两帧等待、请求合并、等待后场景句柄读取、Unity 假 null、实例 ID 缓存、取消与 `finally`。两个构建器仍提供身份、修复判据、装配与管理器存在判据，原清理时序保持。新 `BuildingRestoreCore` 执行回归覆盖两个 owner、等待期间切图、重复请求、取消与旧请求迟到收尾、销毁对象、单个装配异常后的重试；`ContentBuildingOwnership` 继续通过。相关覆盖表与两篇 repowiki 专题已更新。
- `--changed-only` 守卫 203 PASS / 0 FAIL；五个修改的守卫均作破坏转红并按 SHA-256 验证原字节还原。首次全量回归 57 PASS / 3 FAIL，原因是三个夹具随生产追踪器链接的同名记录替身重复定义；删除替身后全量 60 PASS / 0 FAIL。基线红项 0，SKIP 0。结果留在忽略的 `Build/runtime-regressions/`，P2 副本留在 `Build/migration/`。
- 隔离正式构建 `Build succeeded!`，Dev 标识缺席，DLL SHA-256 `4786D683A6781F73BDCCC16F89FC9AFD9F2F33E2947B0FFE8E902435AEE67388`，与隔离部署副本一致。隔离 Dev 构建 `Build succeeded!`，Dev 标识在位，SHA-256 `B1FD48544858D15C20649E2D202C8859770B8403307FF9B92C97D27D065C8AAF`，与隔离部署副本一致；两次均通过 72 bundle 清单。未启动游戏，L3 待 owner。

## 2026-09-25 新窗口续接：Mode D 主体与共享配装（COMPAT）

- `ModeDRuntimeModule` 的同一注册实例接管启停、敌池扫描、预设选择、波次生成、死亡结案、完整性检查、自动下一波及路牌状态。`ModeDItemPool` 接管 D/E/F 共用配装、分帧物化计划、末次武器、品质分桶和全局掉落；装配时只绑定原候选目录及配置、征程、候选过滤查询。`ModeEntryInventory` 承接三种模式共用的裸装 / 背包检查。保留旧 API、跨模式入场协调及原 D/Arena 共用完整性时钟，后者随簇 4/7 收口，不改变调度位置。
- 续接树包含原线程留下的 `ZombieModeRuntimeModule_WaveController` 和 `BossRushIntegrationRuntimeModule_SceneLifecycle` 提取。Mode D 与上述叶子的实际业务方法都已登记编译清单。编译清单 1,088 源、导航 47 模块；宿主 partial 从上一检查点 177 文件 / 71,168 行降到 175 文件 / 65,411 行，预算同步降到实测值。
- L2：全量执行回归 69 PASS / 0 FAIL / 0 SKIP。`AuditModeLifecycle` 增加直接链接的 Mode D 波次结案覆盖，验证迟到结果拒绝、等齐生成、活敌阻塞、死敌清理与完成幂等；稀疏副本去掉等齐生成的判断后在对应断言失败，生产文件 SHA-256 按字节还原。12 项本窗口涉及的守卫完成定点破坏转红和字节还原。`LatestPlayerLogRegressionGuard` 首次破坏未触及其 token / 顺序判据而保持绿，改用破坏登记参数后转红；未修改断言求红。反向验证记录在忽略的 `Build/migration/continue-moded-negative.json` 与 `continue-moded-runtime-negative.json`。
- 全量守卫首轮 678 PASS / 2 FAIL，分别为 partial 清单未下调和配装计划仍按宿主私有类型定位；同步真实新结构后全量复跑 680 PASS / 0 FAIL / 0 known-red。编译清单、模块索引、GameplayCoverage、repowiki 引用检查通过。正式和 Dev 隔离 Windows 构建均 `Build succeeded!`，72 bundle 清单通过；正式 Dev 标识缺席，SHA-256 `2692C1DBE7C81EC431E2CCEFFF29EB2A2B2D59C9BA4CDD56609CE56B903E8F6F`；Dev 标识在位，SHA-256 `F584F3FBAC5DECA1ABDA3074C54B2247FA9F9D8DD5314399CD82BB132F625489`。两种 DLL 各自与隔离部署副本哈希一致，当前 `Build/BossRush.dll` 为正式配置。
- 未取得本轮 L3。Mode D 验收：从路牌进入白手起家，开始首波并完成一波，观察敌人数、击杀后下一波入口及自动倒计时；生成中退出再重入，观察是否有迟到敌人 / 奖励；死亡退出后回基地再进入 E/F，检查开局枪械、弹药及医疗品。生成未结案却推进、敌人不敌对、重复结算、跨局实体残留或共享配装缺失均不合格。

## 2026-09-25 继续执行：Mode E 主体与商人支撑拆分（COMPAT）

- 原 15 个宿主 class 块中的 479 个成员迁入同一 `ModeERuntimeModule` partial；`ModeEHostBridge` 保留既有公开接口与跨模块转发。活敌、阵营索引、死亡和掉落订阅、缩放修饰器、生成分配、会话代次、商人身份、贝壳余额与事务、启动和恢复协程都归模块；完整性计时器从根宿主迁入模块。宿主仍在原阶段调用 Tick，未增加一次 Update，也未移动清理顺序。
- `ModeEMerchantSupportClasses` 的原 3 个模块片段分为契约/价格、会话/余额和交易；交互、静态 UI、宠物按类型独立。静态 UI 的初始化与布局继续作为同一类型的 partial，缓存与事件 owner 没有复制。每个拆出文件均在 1,200 行以内，删除原 4,085 行大文件例外。共用生成计时器原样提到 `Utilities/ModeEFSpawnProfiler`。编译清单 1,097 源，导航 47 模块；宿主 partial 从 175 文件 / 65,411 行降到 163 文件 / 53,131 行，预算同步下调。
- L2：全量守卫 680 PASS / 0 FAIL / 0 known-red；全量执行回归 69 PASS / 0 FAIL / 0 SKIP。商人回归直接抽取模块生产方法，既有迟到 null/fault/success 与后继请求交错断言继续通过。23 个修改守卫/预算完成 25 次定点破坏转红及按 SHA-256 原字节恢复，记录在 `Build/migration/modee-negative.json`；奖励探针第一次指定错文件，仅锚点检查失败未作变异，改为实际缩放文件后转红。全量初次失败是架构守卫仍定位旧的常量限定名和宿主字段名，按实际 owner 更新后通过，没有放宽判据。
- Windows 隔离 Dev 和正式构建均 `Build succeeded!`，两次 72 bundle 哈希清单通过。恢复原 Dev 耗时标签后重新构建：Dev SHA-256 `12DD213F462CF2AE82D6657A3D796E735A6BB093E4CD48060A29263B70572AD0`，14 个 Dev 标识在位；正式 SHA-256 `AC9ED7D87331E413355AB3BFB082993274B4ED5436AAE61E516705E62E5C02FF`，14 个 Dev 标识缺席。两种 DLL 各自与隔离部署副本一致，当前 Build 为正式配置。日志位于 `Build/migration/modee-delivery-*.log`。
- 本批是簇 4 的中间检查点。E/F 的生成准备、阵营分配、商人和后处理仍有经宿主的兼容调用，Mode F 状态仍待提取，不能记为最终解耦。未取得 L3：owner 从路牌用营旗进入划地为营，观察各阵营敌人、商人购买/出售与贝壳余额；生成中退出再入，检查迟到商人/敌人；结束后进入血猎追击，检查分类商店与工事商品。敌人不敌对、重复生成/扣款/奖励、旧请求关闭新商人、界面或阵营残留均不合格。

## 2026-09-25 继续执行：Mode F 主体提取（COMPAT）

- 原 16 个宿主片段中的 434 个成员迁入 `ModeFRuntimeModule` partial，根宿主中的 active 与 `ModeFState` 同时迁出；旧 API 由 `ModeFHostBridge` 转发。阶段时钟、悬赏、补位队列、死亡/掉落订阅、活敌索引、生命成长与命火修饰器、工事/预览、撤离与 HUD 状态均归模块。同一实例在原注册槽位注册，Tick 仍从原阶段驱动。
- D 配装与 Arena 奖励目录、掉落追踪改为已绑定模块调用。票据、掉落配置、丧尸工事的 active/RunId 查询与 RunOnly 登记显式装配，查询留在原短路位置；没有新增每帧扫描。E/F 仍共享 Mode E 的生成/阵营/商人接口，最终共享服务提取待下一步。悬赏闩原字段和四个方法逐字移到 `ModeFRuntimeModule_BountyLatch.cs`，原消费与清除顺序不变。
- 宿主 partial 降至 148 文件 / 43,629 行，预算同步下调；编译清单 1,099 源、导航 47 模块。L2 全量守卫 680 PASS / 0 FAIL / 0 known-red；全量执行回归 69 PASS / 0 FAIL / 0 SKIP。补位回归在模块替身中执行当前生产方法；战役回归直接链接新的生产悬赏闩片段。首轮相关守卫 12 个红项均为旧签名/路径/限定名定位，已逐项同步；自动签名迁移误命中更长的命火方法名后已纠正，仅修改断言定位。
- 12 个修改守卫/预算完成 13 次定点破坏转红与 SHA-256 字节恢复，记录在 `Build/migration/modef-negative.json`。额外核对 Mode F 迁移前后的字符串字面量集合一致；原 HUD 双门、补位义务、最大生命清理与悬赏消费判据保留。
- Windows 隔离 Dev/正式构建均 `Build succeeded!`，72 bundle 哈希清单均通过。Dev SHA-256 `058B953C4349F47558FF87A5027D347B82A125DCBC2A5F764FCD68094B9944CB`；正式 SHA-256 `98C1B6CE25354D5929221FB0045B74404041724802049AD41B5AB9214AFD37AB`。两者各自与隔离部署副本一致，Dev 标识检查分别 present/absent 通过，当前 Build 为正式配置。
- L3 待 owner：裸装带船票/预付票与血猎收发器从路牌进入血猎追击，依次观察准备阶段补位、悬赏击杀、命火过载、工事放置/维修和撤离；退出后检查生命上限、HUD、商人和工事残留；再在丧尸模式放置同类工事并退出。补位停止或重复、悬赏被提前消费、最大生命泄漏、旧补位改写新局、HUD/工事残留均不合格。

## 2026-09-25 继续执行：E/F 共享刷怪准备（COMPAT）

- `Utilities/ModeEFSpawnPreparation` 持有场景 spawner 扫描、10 米网格间距过滤、按玩家距离排序和阵营轮询、扁平化点缓存与安全传送。根注册处只创建一个实例并绑定 E/F；阵营在原读取位置经委托查询，D 后备算法通过原默认参数调用。原扫描、排序、分配、NavMesh 修正和通知顺序保持；E 模块兼容入口转发，F 直接调用共享服务。
- 分配和场景缓存按原两个布尔参数在原清理位置重置，没有新增协程或每帧扫描。宿主 partial 为 148 文件 / 43,626 行，预算下调至实测值；编译清单 1,100 源，导航 47 模块。三篇 Mode E 专题已同步。
- L2：全量守卫 680 PASS / 0 FAIL / 0 known-red；全量执行回归 70 PASS / 0 FAIL / 0 SKIP。新增夹具直接链接完整生产服务，覆盖 Points 世界坐标、零点/已销毁 root、场景缓存、配置点优先、严格 10 米边界、阵营顺序、条件清理、双实例隔离、安全落点与通知次序、后备请求。6 次反向验证（3 个守卫/属性测试含预算、一个执行回归）均在预期判据转红，按 SHA-256 原字节恢复；移除扁平化缓存清理会在清理断言失败。证据在 `Build/migration/shared-spawn-negative.json`。
- Windows 正式/Dev 隔离构建均 `Build succeeded!`，72 bundle 清单通过，14 个 Dev 标识分别 absent/present。正式 SHA-256 `7970AE63276D7E3D326F8EBA3A13DBC42E4F5D9F3740876B6C83A6FA2CFF6CD7`；Dev SHA-256 `9C222A930168945950CC8D3A01DD5641C5CB41659A800F53FCFF9042693D6F49`；分别与隔离部署副本一致。当前 Build 为 Dev，真实游戏目录未部署。
- 本步骤完成共享准备算法，簇 4 的共享商人、生成注册与后处理仍待完成。L3 待 owner：分别进入划地为营和血猎追击，观察玩家与阵营出生区域；结束后切图再进入另一模式。配置落点失效、阵营出生拥挤、切图沿用旧点或传送提示与位置不符均不合格。

## 2026-09-25 继续执行：共享商人预设与分类目录（COMPAT）

- `Utilities/ModeEFMerchantCatalog` 接管 E/F、丧尸商店、随机事件共用的商人预设查找、分类目录、多标签搜索缓存、医疗品排除与分帧预热。根装配预设目录和标签查询，E 模块只保留调用转发，丧尸/随机事件的旧宿主查询入口直接转共享服务。原静态缓存语义保留，并在 Mode E 销毁的原位置清理；商人 NPC、商店实体、贝壳策略和交付仍待后续归位。
- 8 个算法方法体对照前一提交，在依赖限定名替换后逐个一致。未改变分类/HashSet 顺序、品质边界、TypeID、key、等待帧和清理次序。删除无调用方的宿主冷天装备私有桥，宿主预算保持 148 文件 / 43,626 行；编译清单 1,101 源，导航 47 模块。
- L2 全量守卫 680 PASS / 0 FAIL / 0 known-red；全量执行回归 71 PASS / 0 FAIL / 0 SKIP。新回归直接链接生产服务，验证预设回退与已销毁缓存重选、共用缓存、返回副本、排除条件、原分类顺序和逐类预热帧。3 个修改守卫及执行回归共 5 次反向探针按预期转红，SHA-256 原字节恢复；移除分类缓存清理会在重新搜索断言失败。记录在 `Build/migration/merchant-catalog-negative.json`。
- Windows 正式/Dev 隔离构建均 `Build succeeded!`，72 bundle 清单通过；正式 SHA-256 `26CC5CFC3782FBD4A59091DC09FAAA04652368DAD98612E68080D5CF23CAD931`，Dev SHA-256 `2DCBFA4FEC37831D40EF31EDDAFBF667B7237AD06B837F42D8C3C23DBBD11751`。两者分别与隔离部署副本一致，Dev 标识 absent/present 通过；当前 Build 为 Dev，真实游戏目录未部署。
- L3 待 owner：打开 E/F 分类商店、丧尸局内商店及随机事件商店，核对武器/医疗品/面部装备分类和商品；关店重开、切图后重开。分类缺失、医疗品排除失效或旧场景预设报错均不合格。本步只完成共享目录，簇 4 仍未闭环。

## 2026-09-25 继续执行：共享商人实体与内容注册（COMPAT）

- `Utilities/ModeEFMerchantRuntime` 接管共用商人 NPC、主交互和商店列表、异步生成、StockShop 身份装配、8 次尝试分帧预热及逆序清理。E 在绑定时提供贝壳事务、会话判据、交互和专属商品策略；F 持有同一实例并直接生成/清理。E 贝壳逻辑访问同一商店列表，退役与销毁顺序不变。7 个生产算法方法对照前一提交，在依赖名与策略调用展开后逐个一致；保留原异步迟到拒绝、场景校验和商店身份回读。
- FlightTotem 工厂的 3 个状态字段与加载/配置/本地化实体归已有 `FlightTotemRuntimeModule`，删除两个无外部调用方的宿主反调。初始化闩、独立 owner、半秒等待两次与清理位置保持。`BossRushIntegrationRuntimeModule_ContentRegistration` 接管扩展本地化和装备后加载配置，原宿主入口转发，配置器及本地化调用顺序保持；专题和模块索引已更新。
- 宿主 partial 为 147 文件 / 43,264 行，预算降至实测值并移除 FlightTotemFactory 旧条目；编译清单 1,103 源，导航 47 模块。L2 全量守卫 680 PASS / 0 FAIL / 0 known-red；全量执行回归 72 PASS / 0 FAIL / 0 SKIP。商人回归抽取共享生产 Spawn/Cleanup/预热方法，验证迟到结果、共享列表清理、贝壳先退役、重复清理、对象销毁与预热快照；FlightTotem 新夹具直接链接生产模块和能力 helper；装备加载夹具覆盖重复初始化与各失败边界。
- 反向验证：共享商人及预算 6 次、FlightTotem 6 次、内容注册 9 个守卫探针加 1 个执行探针，均在预期判据转红并按 SHA-256 原字节恢复。证据分别为 `Build/migration/merchant-runtime-negative.json`、`flight-totem-negative.json`、`integration-content-registration/negative-results.json`。商人反向副本首轮缺 Mode H 夹具数据，在基线阶段失败；补齐 7 份生产 JSON 后重跑通过，没有修改判据。
- Windows 正式/Dev 隔离构建均 `Build succeeded!`，72 bundle 哈希清单通过。正式 SHA-256 `6942700BD265EFBFFD66CC97D5E7BE53A8325DD37C878539D751EE05D72C6DC5`，Dev SHA-256 `524BAAD88D7744FA8E3FCA8A054A24B2B091676DC5677C8DFCCCA2E94DC467AF`；各自与隔离部署副本一致，14 个 Dev 标识 absent/present 通过。当前 Build 为 Dev，真实游戏目录尚未部署。
- L3 待 owner：E/F 商人生成期间退出再进入，确认旧商人不会覆盖新局；打开分类商店后退出，观察商人/交互残留与贝壳余额。飞行图腾手持、离手、切图并重新装备，检查能力与资源恢复；切换中英文并打开装备/天空岛物品，检查 raw key。迟到实体残留、重复扣款、能力卸下后继续运行、缺失本地化均不合格。簇 4 共享战斗生成与后处理、簇 5/6 余项及 P5/P6 仍未完成。

## 2026-09-25 继续执行：生成后处理、丧尸安全区与装备初始化（COMPAT）

- `Utilities/ModeEFSpawnPostprocessScheduler` 接管后处理队列、逐步配装、倍率、提交和失败清理；宿主只持一个实例并在原 Tick/Clear 位置转发，注册阶段绑定原配装与掉落操作。10 个方法体在依赖限定名替换后与前一提交一致。60 帧期限、最后 5 帧加速、8/16 步上限、实时时钟预算和冻结提交门保持。
- 丧尸安全区控制、两类清敌、射击监听和敌人 marker/AI/空间查询归同一 `ZombieModeRuntimeModule`，Extraction/Wave/Boss/HUD 在原位置直调模块。23 个搬移方法体归一化后完全一致；恢复注销在注册前显式绑定。逆鳞工厂及大镰、焚皇断界戟初始化归各自现有模块，旧 API 转发，静态资源闩、半秒等待、15 秒上限和清理次序保持。
- 宿主 partial 降至 143 文件 / 41,426 行，预算同步下调；编译清单 1,104 源、47 模块索引通过。L2 全量守卫 680 PASS / 0 FAIL / 0 known-red；全量执行回归 75 PASS / 0 FAIL / 0 SKIP。新增三个夹具分别直接执行生产后处理、安全区与装备初始化逻辑，覆盖跨 owner、失效局、对象销毁、缓存与预算边界、事件退订、重入和清理顺序。
- root 的 7 次反向探针、Zombie 的 15 个守卫 + 4 个执行探针、装备的 8 个守卫 + 4 个执行探针均在预期断言转红，并按 SHA-256 原字节恢复。证据在 `Build/migration/postprocess-negative.json`、`zombie-safezone-negative.json`、`zombie-safezone-parity.json`、`equipment-bootstrap-owners/negative-results.json`。架构守卫首次因旧注册写法报红，按新实例的绑定/注册顺序更新；正式编译首次揭示可选参数方法组不匹配，改为显式 lambda 保持原默认参数后通过。
- Windows 正式/Dev 隔离构建均 `Build succeeded!`，两次 72 bundle 哈希清单通过。正式 SHA-256 `9120DEC485E857F8BA84FBE9BF9CB3FCCDCFCF50AC98673AF9D934323977AA34`；Dev SHA-256 `042EB6F3772C759C169EF51BFBAF80FA81249B3C824DC1EF39C852170BA97D0E`。两者分别与隔离部署副本一致，14 个 Dev 标识 absent/present 通过。当前 Build 为 Dev；真实游戏目录尚未部署。
- L3 待 owner：分别在 E/F 生成过程中退出再进入，观察配装、激活与迟到敌人；丧尸准备阶段部署双安全区，玩家进出并射击，观察敌人驱逐与追击恢复；装备逆鳞、大镰和焚皇断界戟，离手、切图后重装备。生成卡死或重复提交、Boss 被当普通丧尸销毁、旧监听继续响应、能力重绑缺失或重复均不合格。本批仍是 P3/P4 中间检查点。

## 2026-09-25 继续执行：扫箱、虚拟 spawner、丧尸奖励与集成叶子（COMPAT）

- `LootAndRewards/AwenLootSweepRuntime` 接管扫箱查询、200ms 缓存、计数、失败提示节流与 runner；根注册处绑定原会话、Courier、投递与通知操作。`Utilities/ModeEFVirtualSpawnerRegistry` 接管虚拟 root、已登记敌人集和原静态反射缓存；E/F 绑定同一实例，F 直接登记/注销，E 的原位置清理。22 个扫箱方法与 6 个 spawner 方法体归一化等价；20 次有效 Boss 死亡发令牌、退款参数、反射目标与重复登记顺序保持。
- 丧尸奖励、掉落与性能辅助、候选/tag 缓存、临时 NPC 和发放辅助的 203 个方法归同一 `ZombieModeRuntimeModule`。原消费位置直调模块，临时 NPC 通过根装配的 prefab resolver 与 Courier 交互动作接入；未增加默认非空委托。4 个消费方模块文件在去除宿主限定后等价，11 个已迁文件中的独立组件尾部逐字一致。
- Nurse 状态归唯一注册的 `NurseNpcRuntimeModule`，后山种子归已有 BackMountain 模块，船票与动态初始化状态归 Integration 模块；Mutator 流程和 Mode G 托管角色的共用算法分别归 `MutatorModeFlow`、`ModeGManagedCharacterService`。29 个方法体及共享商店源文件归一化等价。静态闩、标签/注册/卸载、随机调用数、异步失效清理顺序保持；专题与模块索引同步。
- 宿主 partial 降至 129 文件 / 33,649 行，预算同步下调；编译清单 1,110 源、47 模块索引通过。全量执行回归 79 PASS / 0 FAIL / 0 SKIP；新增四个夹具分别覆盖扫箱、虚拟 spawner、丧尸奖励与集成叶子，既有运行时归属、爆炸、弹道和种子奖励夹具同步连接生产逻辑。全量守卫 682 PASS / 0 FAIL / 0 known-red；首轮 34 个红项均为迁移后旧定位与旧预算，按实际 owner/调用点适配，BackMountain 扫描范围改为官方编译清单定义的生产集合。
- 反向验证：扫箱 7 次、虚拟 spawner 4 次、预算 2 次、Integration 14 个守卫 + 5 个行为探针、Zombie 31 个守卫的 34 个探针 + 6 个行为探针均在预期判据转红并按 SHA-256 原字节恢复。证据在 `Build/migration/loot-sweep-negative.json`、`virtual-spawner-negative.json`、`rewards-loot-budget-negative.json`、`integration-leaves/negative-results.json`、`zombie-reward-guards-negative.json`、`zombie-reward-guards-remaining-negative.json`、`zombie-reward-runtime-negative.json`。
- Windows 最终正式/Dev 隔离构建均 `Build succeeded!`，两次 72 bundle 清单通过；正式 SHA-256 `BFE1B3CF57C1017BF79D3B4583CA49B5314234BEA83607CBF18D2569A8BB0101`，Dev SHA-256 `DD8277292017D8AE0ACE8CAC19BFE6736F4B5795D3B062053875219F89243765`。两者分别与隔离部署副本一致，14 个 Dev 标识 absent/present 通过。当前 Build 为正式配置；真实游戏目录尚未部署。前三次正式编译中前两次揭示遗留私有桥缺失，按原语义补回 Mode D 候选过滤、Nurse 调试只读属性与 Mode G 激活/玩家增益兼容入口，第三次及最终两种构建均通过。
- L3 待 owner：在 E/F 击杀满扫箱令牌条件后使用阿稳扫箱，检查有效尸体、投递与失败退款；退出再入观察地图敌人登记。丧尸局购买奖励、使用医疗并切图，观察临时 NPC 回收、扣点/退款与散射效果。基地接近羽织并交互，击杀相关 Boss 检查种子掉落；中英切换后检查船票与 Mutator 显示。重复登记/计数、旧局继续发放、重复或漏退、NPC/事件残留、异步角色未清理均不合格。本批仍是 P3/P4 中间检查点。

## 2026-09-25 继续执行：共享生成、恢复、丧尸生成、随机事件与征程（COMPAT）

- `EnemySpawnRuntime` 接管完整生成算法；`EnemySpawnHostBridge` 保留原入口与静态 dispatcher 转发，注册时绑定原预设、特殊 Boss、配装和归属服务。预设缓存就绪方法归原缓存 owner Mode D。6 个方法体归一化等价；5 次随机回退、原等待、提交时机、取消后清理与共享后处理实例保持。
- `EnemyRecoveryMonitor` 成为独立服务，持有原恢复状态、缓存和计时器，接管即时与延迟生成位置验证；丧尸及 Arena 各自枚举敌人并引用同一 monitor。原 27 个方法体等价，模式优先级、时间源、下坠/静止判据、冷却、保血、物理清零和阵营仇恨策略保持；根装配窄查询与策略，Tick/Clear 原位置只转发。
- 丧尸剩余生成、初始装备、地图隔离、净化点与现金投资的 65 个方法归既有模块；5 个完整消费者和 3 处组件边界等价，移除 5 个业务宿主 partial。RandomEvents 的 33 个效果方法归原注册模块。Campaign 的 25 个方法、11 个状态归原注册模块，保留终局 API；异步工厂入口捕获原宿主，保留原 `this` 在宿主销毁后对迟到对象清理的寿命，新增真实 OnDestroy 与迟到生成回收用例。
- 宿主 partial 为 121 文件 / 27,142 行，预算同步下调；编译清单 1,116 源、47 模块索引通过。全量守卫 685 PASS / 0 FAIL / 0 known-red；全量执行回归 84 PASS / 0 FAIL。新夹具覆盖完整生成核心、恢复服务、随机事件效果、丧尸生成和初始装备；Campaign 既有夹具扩展到 196 个判据。Audit 和后处理夹具按真实新 owner 接线。
- 反向验证：生成核心 8 次、恢复 17 次、预算 2 次、RandomEvents 15 次、Campaign 18 次、Zombie 的 19 个守卫/26 个探针与两个执行夹具/14 个探针均在预期判据转红，并按 SHA-256 原字节恢复。证据见 `Build/migration/spawn-core-negative.json`、`recovery-owner-negative.json`、`spawn-recovery-budget-negative.json`、`random-event-owners/`、`campaign-owners/` 和 `zombie-remainder-*.json`。恢复反向初轮仅因预期错误文本与实际错误文本不一致中止，实际变异已转红；修正报告判据后完成，无生产改动。
- Windows 正式/Dev 隔离构建均 `Build succeeded!`，两次 72 bundle 哈希清单通过；正式 SHA-256 `571774A6C361120D7EFB353B35D60338ABDA4A6E6D053CCF3E4852DB970AFCB7`，Dev SHA-256 `B50AA395AEA33B97DA4E8E80704F90A1948D64D92359DF688C05C9638D57EBF6`。两者分别与隔离部署副本一致，14 个 Dev 标识 absent/present 通过；当前 Build 为 Dev。真实游戏目录尚未部署。
- L3 待 owner：标准模式、D/E/F 和丧尸各进入一局，检查刷怪、敌对性、卡位恢复后血量与追击；生成期间退出再入，检查迟到角色和旧局奖励。丧尸进入/退出检查初始装备、原版 NPC/地图隔离和现金结算；触发随机效果与征程终局后切图，检查迟到 Boss 回收。重复生成/提交、回血重置、Boss 被普通丧尸远距策略拉走、旧局继续改写新局或卸载后角色残留均不合格。本批仍是 P3/P4 中间检查点。

## 2026-09-26 继续执行：Arena 余项、成就、音效与 Mode G 入口（COMPAT）

- 根宿主 10 个生成、地图角色缓存与掉落模板方法归 Arena，8 个字段归该模块；原 static / instance 寿命保持。生成 await 前捕获原协程宿主，迟到结果继续走原清理。根回血 if 块归 MutatorBossRegenRuntime，仍在原 Update 槽执行，Arena 与 D/E/F 分支、时间源与缓存顺序保持。10 方法与回血块归一化等价。
- Achievement 的触发、勋章商店和 Tick 归同一原注册模块；15 个旧入口只转发。原 Awake 初始化、Integration 勋章订阅/退订和清理顺序保持，模块 OnDestroy 只兜底。Audio 四个入口转发到独立服务，五个缓存保持 static；官方非 void 与兼容 void 的反射返回策略保持。
- Mode G EntryRuntime 接管 33 方法、5 个实例状态与 4 个静态状态，六组窄委托保留原即时查询；24 个旧桥保留。注册 shell 与每局 new 核心仍分别存在，core/HUD/RunContext 指向同一局；第二局不复用已 Dispose 核心。33 方法归一等价，未迁 preview、静态 PrepareHostDestroy 和纯事务区域字节一致。
- 宿主 partial 为 117 文件 / 23,387 行，预算同步下调；编译清单 1,123 源、47 模块索引通过。最终全量守卫 689 PASS / 0 FAIL / 0 known-red；全量回归 88 PASS / 0 FAIL，结果见 `Build/migration/host-remainder-first-full-regressions.json`。新增 ArenaHostRemainder 39、AudioRuntime 129、ModeGEntryOwners 217 个执行判据；AchievementRuntime 覆盖真实模块与根接线。全量回归后只调整注册期窄查询绑定的位置以保持 Zombie 连续装配序列、恢复 Mode G 未迁区域换行；相关守卫与入口回归随后通过。
- 反向验证：Arena/根余项 19 次及预算 2 次；Achievement 12 个守卫 + 12 个执行探针；Audio 9 个守卫 + 14 个执行探针；Mode G 24 个外部探针与 HUD 47 个内置探针均在预期判据转红，独立副本按字节/SHA-256 恢复后全绿。证据分别为 `host-remainder-negative.json`、`host-remainder-budget-negative.json`、`achievement-negative.json`、`audio-negative.json` 与 `modeg-entry-owners/mutation-results.json`，均在 `Build/migration/`。Arena 反向末项最初因读取 CRLF 锚点规范化失败，未变异生产；改为原字节读取后完成。
- Windows 最终正式/Dev 隔离构建均 `Build succeeded!`，两次 72 bundle 清单与 14 个 Dev 标识 absent/present 通过。正式 SHA-256 `7135F99FBCACC2DC2FC29F899B1D75FEF1241D6CE37E254053331EBF4E9BB9B7`；Dev SHA-256 `A75FD76AC1D60FB2AB6EAA1A06D5639E5271FBA4240D053D9A1665D2D8A43821`，分别与隔离部署副本一致。初次正式编译发现 F3 仍调用 StartModeGRuntime，保留原签名桥后通过。当前 Build 为 Dev；真实游戏目录尚未部署。
- L3 待 owner：标准与无间模式观察敌对性、清场、奖励箱与回血；打开成就页并触发成就和商店勋章补货；播放战斗/胜利/彩蛋声音后切图；Mode G 连续两局并在生成中离场，使用既有 F3 `MODE_G_NINE_WAVES` 检查九波推进与终态。迟到角色残留、重复订阅/奖励、成就漏记、声音重复、Mode G 第二局停滞或退款不一致均不合格。本批仍是 P3/P4 中间检查点。

## 2026-09-26 继续执行：E/F 共享生成与宿主薄桥归并（COMPAT）

- `ModeEFEnemySpawnRuntime` 持有原阵营预设缓存、失效标记、生成计数和龙裔/龙王占位，E/F 绑定同一实例。16 方法归一化等价；原候选次序、随机次数、800/500 ms 等待、提交与失败计数、旧局迟到结果拒绝保持。E 专属贝壳奖励分类与生成提交仍在 E 模块；共享登记与 E 专属副作用的边界继续处理。
- Integration 的 34 个宿主 partial 归并为 4 个：两份核心/兼容载体与两份待收口场景流程。30 个迁入宿主块无初始化字段，四个建筑仍在原 getter 懒创建；同文件独立组件正文保持。日报 view 和开关判断归原 DailyReportRuntimeModule，旧入口传入当时配置值。新载体 400/1,192 行，未抬大文件预算。
- Zombie 的 15 个宿主文件归并为 Entry/Combat/Reward 三份薄桥，原四个独立组件留在原文件。`ZombieModeHostLifecycle` 持有构造期备用状态和模块 attach/detach 转交，未创建第二个 RuntimeModule、未提前 OnAwake；原启动协程归注册模块并捕获原 coroutine owner，保留卸载后迟到 StopCoroutine 的宿主寿命。归并初版超过新文件 1,200 行预算，已按职责拆为三份 840/832/681 行。
- 编译清单 1,094 源、47 模块；宿主 partial 实测 75 文件 / 21,255 行，预算同步下调。全量守卫 692 PASS / 0 FAIL / 0 known-red。全量回归初跑 90 PASS / 1 FAIL，仅 AffixCombat 仍读已删除的技能桥；改到真实 CombatHostBridge 后单项通过，最终逐夹具证据 91 PASS / 0 FAIL（`Build/migration/ef-bridges-final-runtime.json`）。本批新执行判据：共享生成 37、日报 UI 16、丧尸宿主 owner 117；AuditModeLifecycle 的 F 重生替身同步到真实共享 owner，原旧局回调断言通过。
- 反向验证：E/F 16 个方法等价、16 次结构/执行探针；根共享守卫和两项预算共 16 次；Integration 18 个结构探针与 15 个执行探针均命中预期断言并按字节/SHA-256 恢复。证据为 `ef-spawn-negative.json`、`ef-bridges-shared-negative.json`、`integration-host-negative.json`，均在 `Build/migration/`。Zombie 的 41 个修改守卫与新 ownership 守卫已完成 52 次真实源码结构变异，另有 12 次执行变异；全部在预期断言转红并按字节/SHA-256 恢复后回绿。证据见 `zombie-host-consolidation/negative-semantic-{first,last,ownership}.json` 与 `negative-fixture.json`；先前路径缺失探针仅记路径读取验证，不计结构反向。388 个宿主成员逐项对照、四个混装文件独立组件按字节对照均无意外差异，见同目录 `equivalence-manifest.json`。
- Windows 最终正式/Dev 隔离构建均 `Build succeeded!`，两次 72 bundle 清单与 14 个 Dev 标识 absent/present 通过。正式 SHA-256 `64F75BE2AA64978ECEEF22B7BF86138ABF8C6B81E72C044BDB152C943078E0AE`；Dev SHA-256 `3B8D42DADD4E4E4C4156E25AD172ED87F7761914F4F9C95D3FB04778B3F72A9F`，分别与隔离部署副本一致（`ef-bridges-final-builds.json`）。当前 Build 为 Dev；真实游戏目录尚未部署。
- L3 待 owner：E/F 各开局并观察初始 Boss、狼阵营预设、龙裔唯一性与 F 死亡补位；生成期间退出再入，检查旧局回调是否污染新局。打开日报并关闭/重开，切图后再打开；丧尸在选图、加载、准备、撤离和卸载路径观察状态交接。重复敌人、丢失补位、重复 view、已销毁 Canvas 复用、启动债务丢失、协程或 UI 残留均不合格。本批仍是 P3/P4 中间检查点。

## 2026-09-26 继续执行：E/F 共享登记与 Steam 查询 owner（COMPAT）

- 新 `ModeEFEnemyRegistry` 接管活敌列表、去重集合、阵营查询/列表、回血缓存及死亡/掉落订阅字典，E/F 绑定同一实例。11 个旧方法体等价；E 的同阵营禁掉落策略留原模块，注册字典及订阅两句归共享 owner。F 仍通过明确动作清理 E 残留缩放和入局状态，原直接仇恨字典 Remove 改为同一步动作。重置顺序及公开旧 List 的对象身份保留。
- `SteamPlatformInfo` 接管原 static 反射缓存与一次性日志状态；旧入口在 Utilities 宿主薄桥转发。两方法体仅 DevLog 限定名改变。
- 新执行夹具 `ModeEFEnemyRegistry` 27 条断言、`SteamPlatformInfo` 13 条断言；全量守卫 694 PASS / 0 FAIL / 0 known-red，全量回归 93 PASS / 0 FAIL；1,095 源和 47 模块索引通过。宿主预算下调到 74 文件 / 21,139 行。证据为 `Build/migration/ef-registry-full-guards.log` 与 `ef-registry-full-runtime.json`。
- 13 个方法归一化等价；20 个结构、预算及真实执行探针均命中预期断言转红，按字节/SHA-256 恢复后回绿，见 `Build/migration/ef-registry-negative.json`。首次缓存探针已在更早的缓存断言失败，修正报告预期文本后完成，未修改测试求红。
- Windows 最终正式/Dev 隔离构建均通过，两次 72 bundle 清单、14 个 Dev 标识 absent/present 与隔离部署 SHA 一致。正式 SHA-256 `1093719AB8182471C9D3890EDFF813E623C0FE0E9C220CC8B8E3C5E222FE4926`；Dev SHA-256 `BD050E60A2C0B441B94D3838B2077F0C42E9C6B5CFDF7442A10253A3DC45105C`，见 `Build/migration/ef-registry-final-builds.json`。当前 Build 为 Dev；真实游戏目录尚未部署。
- L3 待 owner：E/F 各开局、击杀、退出再入，观察敌人登记、阵营掉落、回血与死亡补位；E 同阵营禁掉落切换应即时生效。检查 Steam 昵称在首选/兼容平台环境下显示；重复敌人、重复掉落/回调、跨局残留与缓存旧昵称均不合格。本批仍是 P3/P4 中间检查点。
- 预演脚本 `Build/migration/extract_ef_registry.py` 已执行，不可重跑；当前原 Goblin bin/obj 仍不动。P5 只读映射在 `Build/migration/sky-island-p5-readonly/`，Integration P6 实机清单在 `Build/migration/integration-owner-smoke.md`。

## 2026-09-26 继续执行：根宿主收口与余下薄桥（COMPAT）

- `BossRushMapRuntime` 持有原 static 地图注册表和每 owner 当前刷新点；原地图顺序、SceneID 查询、集合身份与坐标优先级保持。公共 NPC 点位策略归 `CommonNpcRuntimeModule`，四个实时查询由根装配。Arena 持有标准激活状态、玩家缓存与原 static planned/active 标记，接管配置、返程与返回交互点创建；地图选择器对私有 planned 字段的反射跟随新 owner，字段名和策略保持。
- 加油站创建、身份查询、计数重置及离场/玩家死亡两条清理归 Integration；死亡路径原日志边界保持。共享倍率与等待缓存归独立类型，场景帧缓存归 `SceneRuntimeGate`；丧尸特例留在丧尸模块，共享 sanitizer 接受策略且保持两次查询的时点。通知反射清理与船点过滤归 UIAndSigns，婚姻调试字段原样归已有 Dev 文件。
- Mode D 6 → 1、Loot 7 → 1、Arena 7 → 3 个宿主 partial；独立 interactable、loot enum/target 留原位，readonly sweep 初始化器保持。保留的长宿主方法已逐项复核：生命周期/官方事件、跨模式入场协调、模块装配、参数/兼容转发或 Dev 演练；`ModeHEntry.cs` 中支付事务为独立类型，并非宿主状态。列表见 `Build/migration/host-terminal-long-methods.json`。Integration Start/Destroy/Scene 与 GroundZero、Teleport 的模式门仍在原槽位。
- 宿主 58 文件 / 20,114 行；编译 1,091 源，47 模块索引通过。全量守卫 697 PASS / 0 FAIL / 0 known-red；全量执行回归 95 PASS / 0 FAIL。`HostMapArenaOwners` 85 条断言、`HostUtilityOwners` 74 条，`EquipmentResourceScene` 增补同帧采样与下一帧刷新。末次返回交互点提取为相同正文和玩家字段 owner 置换，补接线反向及正式/Dev 构建。证据为 `host-terminal-final-guards.log`、`host-terminal-final-runtime.json`，均在 `Build/migration/`。
- 等价及反向：根 29 方法/清理块在限定 owner 置换后等价，数据和日志字面量保持（`host-terminal-parity.json`）；根 15 个探针和末次 4 个增量/预算探针均按预期转红、原字节/SHA 还原回绿。Mode D/Loot 13 个原块及两个独立类型文件原字节相等，20 个守卫/22 次变异；Arena 88 成员、5 个原类体等价，7 守卫/7 变异。地图/Arena 6 个结构 + 22 个执行变异；工具 8 个结构 + 16 个执行变异均命中预期断言并恢复。证据在 `mode-d-loot-host/`、`waves-arena-host-consolidation/`、`host-map-arena-owners/`、`host-utility-owners/`。归并后 root 继续提取的加油站与返回点单列，未冒充整文件终态字节不变。
- Windows 最终正式/Dev 隔离构建通过，两次 72 bundle 清单与 14 个 Dev 标识 absent/present 通过。正式 SHA-256 `33956709FAEFEA2AAAF6160DF6F0BE60085D910C1F735B8BC02074923A153166`；Dev SHA-256 `71904F429A280F901C459C48AE30020F36258691A8510B9ECCB28F2D7AD52670`；各自与隔离部署副本一致（`host-terminal-final-builds.json`）。首次编译揭示两处旧 ammoShop 调用点，已迁身份查询和带原日志的死亡清理，随后编译通过。当前 Build 为 Dev，真实游戏目录尚未部署。
- L3 待 owner：依次进入标准/无间/D/E/F/丧尸，检查出生点、返回交互点、公共 NPC、加油站与敌人倍率；死亡或离场后再次进入，检查旧商店/角色是否残留；菜单与加载场景观察界面/能力暂停。重复商店或事件、计数未重置、阵营特例丢失、返回点错误、加载帧仍执行业务均不合格。没有游戏进程或性能采样证据。

## P5 天空岛目录归位（2026-09-26，SAFE / OPERATIONAL）

- 原 `DebugAndTools/SkyIsland/` 的 99 个 C# 和专项规则归位到 `SkyIsland/`；97 个源码逐字不变，2 个仅改规则路径注释。三份全文件 Dev 源保持原字节；编译顺序、条件、命名空间、资源与存档键保持。编译清单仍为 1,091 源，47 模块；100 项前后 SHA 见 `Build/migration/p5-move-evidence.json`，`.bat` CRLF 核对通过。
- 路径消费者、生成导航、当前规则/工具/夹具与 7 篇专题定位引用同步。旧路径仅在历史归档、台账、前测及计划迁移说明中保留。`gameplay_coverage.py` 现码没有旧计划所述的特殊深度算法；迁移自然成为一级域。递归 F3 扫描显式加入新目录，Instance 目录分组随迁，总量不变。
- 全量守卫 697 PASS / 0 FAIL / 0 known-red；天空岛执行回归 13 PASS / 0 FAIL，F3AutotestJudges 1 PASS / 0 FAIL。结果分别归档为 `p5-guards.log`、`p5-sky-runtime.json`、`p5-f3-runtime.json`。
- 反向验证：42 个天空岛前缀脚本（36 Guard + 6 PropertyTest）在独立副本基线、42 项真实生产变异命中预期断言、逐字节/SHA 还原及整组复绿通过；另有 12 个共享守卫/动态消费者基线与 29 项结构变异，全部命中预期断言、逐字节/SHA 恢复转绿；包含新目录递归扫描、任务重复 Tick 和清单消费者的反向探针。没有以文件缺失替代结构变异。
- Windows 正式/Dev 隔离构建均成功，两种配置分别通过 72 bundle SHA 核验、14 个 Dev 标识 absent/present 检查及隔离部署 SHA 一致性。正式 SHA-256 `1DBEC086A8ABDB8BCAC48E88E01ACB7DBCB181BCEC8D35549A93EAAB41EEC2B9`；Dev SHA-256 `A728B21D72040BF02737FCE9CA3F3BBA327A98C4A074971B5E0D4CD4D736E48B`。证据为 `p5-release.log`、`p5-dev.log`、`p5-builds.json`。当前 Build 为 Dev。
- L1/L2 完成，未启动游戏；L3 留待 owner 实机验收。

## P6 最终交付（2026-09-26，SAFE / COMPAT / OPERATIONAL）

P0–P6 的代码、文档、守卫与离线验证工作已完成；游戏内验收仍为 MANUAL_PENDING。没有启动游戏、读取玩家存档、推送或发布。

### 检查点与验证总览

| 检查点 | 提交 | 守卫 / 回归证据 |
| --- | --- | --- |
| P0 | `3323e33e` | 修复验证链后 665 全量守卫 / 59 全量回归 PASS；原红项与修复过程见本文件 P0 |
| P1 | `adef32ef` | 668 全量守卫 PASS；无生产代码改动，沿用 P0 的 59 项回归；规则/台账/索引反向通过 |
| P2 | `403a09a4` | 203 相关守卫 / 60 全量回归 PASS；两项共享实现及反向通过 |
| P3、P4 | `71b68f60` 至 `cae48e91` | 各簇的提交、owner、等价/反向与两种构建记录见前文；终态 697 全量守卫 / 95 全量回归 PASS |
| P5 | `6409a0f4` | 697 全量守卫 / 13 天空岛 + 1 F3 回归 PASS；42 + 29 项独立副本结构变异全部命中原断言并恢复 |
| P6 | 本节所在提交 | 修复最终入口审查发现的票 ID 问题后，697 全量守卫 / 95 全量回归 PASS；正式、Dev 与最终正式真实部署完成 |

最终守卫：697 PASS / 0 NEW-FAIL / 0 KNOWN-RED；最终执行回归：95 PASS / 0 FAIL。聚合器没有单列 SKIP 计数，未将 L3 未执行项目计为 PASS。最终日志为 `Build/migration/p6-final-guards.log`、`p6-final-runtime.log`、`p6-final-runtime.json`。未使用 source-only 降级验证。P5 反向证据为 `sky-island-p5-prefix-new-1790392928108843400/delivery.json` 与 `p5-shared-negative-prep/execution/evidence.json`、`final-verification.json`。

### 最终修复与审查

- `CR-2026-09-26-001`：船票字段早已迁为宿主转发属性，地图费用仍用 GetField，已注册 500001 时错误退回 868。改为直接读 `IntegrationRuntimeModule.BossRushTicketTypeId`，保持正值判定、868 回退、现金为零及一张票语义。新增生产方法回归先转红再修复转绿，并覆盖备用正 ID 与重新注册刷新；随后重跑最终全量及两种构建。修前 `p6-*.log` 的首轮绿色结果不替代 `p6-final-*`。
- 扩展反射审计核对宿主迁出的字段/属性/方法名及精确字符串；除已修船票外未发现第二个 confirmed 残留。arena planned 的反射已指向模块真实私有 static 字段。审计记录 `p6-host-reflection-audit.md` 明确区分修前 DLL 元数据和修后源码。
- `modules.json`、MODULES、宿主/生命周期/hooks 专题已同步；补 22 组现有专题导航。四篇 local-only 架构文档按当前 owner/路径更新并核对引用，见 `p6-docs-audit.md`。没有将这些本地文件强行纳管。
- 建筑人工用例修正退役事实：旧公告板/旧展示柜恢复并指引，报箱/遗种巢验证当前建造功能。实机清单经过独立入口审查，修正基地船点、G 完整一局加第二局、H 选图加载链、百科物品入口及 manifest 小写 file 路径。

### 前后度量

| 指标 | P0 前测 | P6 终态 |
| --- | ---: | ---: |
| 宿主 partial 文件 / 行数 | 202 / 103,052 | 58 / 20,114 |
| 宿主预算上限 | 202 / 103,200 | 58 / 20,114 |
| 编译源码 / 导航模块 | 1,020 / 47 | 1,091 / 47 |
| 自动导入 CLAUDE + AGENTS | 38,854 B | 17,783 B |
| 根 AGENTS 行数 / 字节 | 317 / 38,493 B | 180 / 17,315 B |

宿主减少 144 个 partial 文件、82,938 行；自动导入减少 21,071 B。固定成员样本的源码读量没有普遍下降：日报 79,714 → 132,702 B、天空岛 130,056 → 130,042 B、商店 136,815 → 202,917 B、武器 79,874 → 173,779 B、Mode F 96,641 → 188,723 B。完整兼容载体和新的 owner/绑定文件都计入，不能用薄桥行数抵扣迁出实现。五组限定目录导航演练均无需全仓检索，前测同样为否；原成员迁移审计使用过全仓检索。规则字节与逐文件映射见 `architecture/CONTEXT_BASELINE.md`，未测实际 token、未换算 token 百分比。

### Windows 构建与真实部署

- 最终隔离正式 SHA-256：`07A6BB1E9EB21BEEFDDE052CAA40BE2F09A5BAAF2A7A9596CF85452C21ECBA97`。
- 最终隔离 Dev SHA-256：`140B1181F5ED1A010C4CE35A317C094725DE6B387F83552D6FFFD0E6703A9DF7`。
- 两种配置均为 Windows 真编译，日志出现 Build succeeded；各自通过 14 个 Dev 标识 absent/present、72 bundle 清单 SHA 核对，隔离部署 DLL 与构建输出一致。记录：`p6-final-builds.json`、`p6-final-release.log`、`p6-final-dev.log`。
- 最后再次以正式配置构建并部署：`D:\software\steam\steamapps\common\Escape from Duckov\Duckov_Data\Mods\BossRush\BossRush.dll`。
- 交付正式 DLL SHA-256：`47F26AD82B1E67BFE1ECB0C728A8E97B40EBA80745790694B54AA7AD184A88B3`；`Build/BossRush.dll` 与真实部署目标逐字节哈希一致，两份均检查 14 个 Dev 标识全部 absent。72 bundles 核对通过。记录：`p6-final-deploy.log`、`p6-final-deployment.json`。
- 部署前游戏进程未运行；原目标 DLL 已备份于 `D:\code\ykf\BossRushMod\Build\migration\p6-deployment-before-20260926-114204`。本轮未启动游戏。隔离目录的最后一次产物是 Dev，真实游戏目录与根 Build 的交付产物均为正式。

### 交付范围与待实机项

L1 静态接线、L2 守卫/隔离执行与 Windows 构建已完成；L3 尚未执行。完整 owner 清单为 `architecture/MIGRATION_ACCEPTANCE.md`，覆盖真实模式入场/奖励/退出、G 第二局、跨模式和异常清理、天空岛/征程、NPC支付、装备卸下/死亡/切图、建筑恢复、共享 UI 和 Dev F3。55 个显式人工 ID（展开范围与装备组后 91 个）及 4 个自动截图主名已核对；所有实机结果保持 MANUAL_PENDING，截图由 owner 目检，AI 只读文字报告。未进行性能采样，不宣称无性能问题。

P0–P6 无剩余离线步骤；剩余为上述 L3 验收以及后续是否继续降低代表任务源码阅读量的取舍。没有把已实现的宿主预算收敛宣称为所有任务的上下文成本降低。
