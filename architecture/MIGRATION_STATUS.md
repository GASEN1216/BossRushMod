# 模块解耦迁移状态

依据：`docs/design/2026-09-22_BossRushMod模块解耦与上下文治理计划.md`（2026-09-24 修订版）。本文件在每个检查点更新；续接时先看本文件和 `git log -5`。

## 当前进度

| 检查点 | 状态 | 证据或下一步 |
| --- | --- | --- |
| P0 基线 | 完成，`3323e33e` | 665 项全量守卫 PASS，59 项全量回归 PASS；隔离正式与 Dev 构建 PASS |
| P1 上下文治理 | 完成，`adef32ef` | 根规则 180 行 / 17,329 B；台账 1,050 / 975 行；47 模块导航覆盖 1,020 源 |
| P2 复用试点 | 完成，`403a09a4` | 词缀追踪器与建筑恢复核心；203 项相关守卫、60 项全量回归与两种隔离构建通过 |
| P3 状态提取 | 进行中 | 簇 1 已提交；簇 6 的部分叶子已合入并完成整树离线验证，待提交；簇 2–5、簇 6 余项、簇 7 待做 |
| P4 耦合点 | 提前并行 | §6 第 5、6、8 条已完成代码及离线验证，待提交；第 1、3、7 条待做 |
| P5 目录归位 | 未开始 | 天空岛迁移与注入占位清理 |
| P6 收口 | 未开始 | 全量验证、交付报告、最终正式部署 |

最近提交：`71b68f60`（P3 簇 1）。下一具体动作：提交已验证的簇 6 部分叶子与提前完成的 P4 耦合点，再顺序进入簇 2（WavesArena、LootAndRewards）。

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

### 簇 6：Integration 部分叶子与提前完成的 P4 耦合点（待提交，COMPAT）

- 字段归属：DeathWraith、Bonus 套装、DragonKing / DragonDescendant / PhantomWitch、Wedding、WishFountain、FlightTotem / Frostmourne / ReverseScale、Goblin、Affinity 的状态由各自 RuntimeModule 或独立 owner 持有。原宿主公开签名保留薄桥；`BossRushRuntimeModuleRegistration` 装配顺序与 `AlwaysOnRuntimeHooks` 调度位置未改。`IntegrationDeferredBootstrap` 的阶段顺序未改。`BossRushIntegration_*` 等其余宿主 partial 尚未提取，簇 6 不得记为完成。
- 生命周期：各叶子的事件订阅与退订、协程启动与停止、静态缓存重置按原入口迁移；死亡亡魂与套装的夹具覆盖 owner 隔离和清理。运行时轨迹仅经 L1/L2 静态与隔离验证，未启动游戏。
- P4 §6 第 5 条：共享字体、CanvasScaler、模态租约、timeScale / Cursor 快照归 `Common/UI/BossRushUIFoundation`；丧尸帮助类只转发。两个消费者并存的租约夹具及反向验证通过。第 6 条：`ValidationHasActiveMode` 从 F3 partial 搬到正式可达的 `Utilities/ModeRuntimeHooks`；正式入口与 F3 均沿用同一判据。第 8 条：临时 NPC 商店创建者传入现金 / 净化点策略，通用交易仍按官方回调、交付、扣款 / 回滚顺序执行；生产方法抽取夹具验证两种支付和失败回滚。第 1、3、7 条待做。
- 索引：`architecture/modules.json` 的 12 个 Integration owner / 入口同步改写，`MODULES.md` 由工具再生；`task_context.py --check` 报 47 模块 / 1,036 编译源。宿主 partial 由 198 文件 / 100,642 行降至 179 文件 / 86,974 行，预算下调为 179 / 87,000。实例分类表与守卫同步更新。
- 验证：全量守卫 675 PASS / 0 FAIL / 0 known-red；修改或新增的守卫以真实破坏转红并逐字还原。全量执行回归 64 PASS / 0 FAIL（Harmony 指向创意工坊 `3588386576/0Harmony.dll`）；隔离正式构建 `Build succeeded!`，Dev 标识缺席，SHA-256 `7A0BAB42FD41CF9F5946E7E7AA662DA7056C5216D87796BAF8E369248EB1E91B`；隔离 Dev 构建 `Build succeeded!`，Dev 标识在位，SHA-256 `A903E60E8AE9AC9B94FE50A31CA5548F122194DA57FC9CD24F0069E5C9565EBB`。两种 DLL 的隔离部署哈希一致，72 个 bundle 清单核对通过。当前 `Build/BossRush.dll` 是 Dev，真实游戏目录尚未部署。

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
