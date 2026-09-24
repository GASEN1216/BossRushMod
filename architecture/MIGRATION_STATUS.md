# 模块解耦迁移状态

依据：`docs/design/2026-09-22_BossRushMod模块解耦与上下文治理计划.md`（2026-09-24 修订版）。本文件在每个检查点更新；续接时先看本文件和 `git log -5`。

## 当前进度

| 检查点 | 状态 | 证据或下一步 |
| --- | --- | --- |
| P0 基线 | 完成，`3323e33e` | 665 项全量守卫 PASS，59 项全量回归 PASS；隔离正式与 Dev 构建 PASS |
| P1 上下文治理 | 完成，`adef32ef` | 根规则 180 行 / 17,329 B；台账 1,050 / 975 行；47 模块导航覆盖 1,020 源 |
| P2 复用试点 | 完成，`403a09a4` | 词缀追踪器与建筑恢复核心；203 项相关守卫、60 项全量回归与两种隔离构建通过 |
| P3 状态提取 | 进行中 | 簇 1 已完成；簇 2、5、6 完成多个可恢复子范围，簇 3、4、7 仍待做 |
| P4 耦合点 | 提前并行 | §6 第 1–8 条已处理；第 9 条随簇 3、4 处理，第 10 条按计划保持 |
| P5 目录归位 | 未开始 | 天空岛迁移与注入占位清理 |
| P6 收口 | 未开始 | 全量验证、交付报告、最终正式部署 |

最近已提交：`e81ff798`（P3 簇 2 Tick / 生成器 / 预设、簇 5 Tick / 暂停、簇 6 地图物件）；当前批次迁移 Arena 奖励缓存与前期波次排除、丧尸奖励准备时长 / RunOnly 清理及集成延迟初始化。本节证据随当前检查点提交归档；下一动作是继续簇 2 余下业务。真实游戏目录尚未部署。

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

## 未完成项

P3–P6 尚未完成；不能据此宣称离线迁移完成或实机通过。
