# BossRushMod 模块解耦、复用体系与上下文治理计划

日期：2026-09-22 初稿；2026-09-24 全面修订（修订记录见 §13）。状态：待新窗口执行。分类：本文 `SAFE`；执行内容按检查点标 `COMPAT` / `OPERATIONAL`。研究依据见 [GitHub 架构对照与深审记录](2026-09-22_BossRushMod_GitHub架构对照与深审记录.md)。

## 0. 新窗口从这里开始

本计划在一个连续窗口里做完三件事，按 §8 的 P0–P6 推进，每个检查点结束时仓库可编译、守卫与回归绿、已提交：

1. **上下文治理**：把 AI 会话每次自动加载的内容和每个任务必须读的内容压下来，并留下可测量的前后数字（§3）。
2. **状态归属**：`ModBehaviour` 只留生命周期、调度与装配；各模式与系统的字段、协程、事件订阅、计时器归自己的 RuntimeModule（§5）。
3. **复用与解耦**：已证实的重复实现收敛（§4），已核实的耦合点逐个解开（§6），做一处有限的目录归位（§7）。

**明确不做**（理由见 §2.3）：全仓目录搬到 `Host/Shared/Frameworks/Modules` 树；Frameworks 的 Core / Contracts / Adapter 三分层；Roslyn 语义分析器与 IL 依赖检查；逐文件的 `migration-map.json` / `legacy-boundaries.json` 台账；拆分 DLL；改 TypeID、存档键、本地化键、资源名、Harmony 目标；新增玩法内容或改经济数值。

**完成的定义**：P0–P6 全部退出判据达成；正式与 Dev 两种构建在隔离游戏根通过；`python tools/run_guards.py` 与 `python tools/run_runtime_regressions.py` 全绿（基线已有红项单列）；§3.1 的前后度量已填；§9 的交付报告与 §11 的 owner 实机清单已写。L3 由 owner 实机，离线完成时写「离线完成，实机待验收」。

**执行纪律**：owner 已承诺执行期间停止自己的提交，执行窗口按检查点 `git commit`（不 push，提交信息简短中文，暂存路径逐个列出）。上下文压缩不是停止理由；只有验证链本身坏了、遇到根 `AGENTS.md` §10 事项、或 owner 打断才停。做不完的项在 `architecture/MIGRATION_STATUS.md` 如实标「未开始 / 进行中」，不得记为完成，也不得靠放宽守卫、加 known-red、目录级豁免来变绿。子代理最多 3 个、一律 Opus，只分配 §5 里互不相邻的叶子簇；`ModBehaviour.cs`、编译清单、`Common/`、`Utilities/`、索引与状态文件由主窗口独占。

## 1. 现状（2026-09-24 重采）

数字以命令输出为准，实施 P0 时重采一次写进 `architecture/MIGRATION_STATUS.md`。

| 项 | 当前值 | 来源 |
| --- | --- | --- |
| 正式编译清单源文件 / 行数 | 1,020 个 / 约 41.7 万行 | `python tests/OfficialCompileListFileExistenceGuard.py`；`tools/compile_list.py` 解析 |
| 宿主 partial 文件 / 行数 | 202 个 / 103,052 行 | `python tests/ModBehaviourPartialBudgetGuard.py`；预算 `tests/modbehaviour_partial_budget.json` |
| 守卫 / 执行回归夹具 | 约 670 个 / 59 个 | `ls tests/*.py`；`python tools/run_runtime_regressions.py --list` |
| 每会话自动加载 | `CLAUDE.md` 导入根 `AGENTS.md`：318 行、38 KB，估约 1 万 token | `wc -c AGENTS.md` |
| 台账 | `FIX_TRACKER.md` 9,309 行 / 1,027 KB；`CODE_REVIEW_FINDINGS.md` 5,225 行 / 617 KB | `wc` |
| 硬编码顶层目录路径的守卫文件 | 468 个（`Integration/` 217、`ZombieMode/` 153、`DebugAndTools/` 69） | `grep -l` |
| 夹具工程引用生产文件 | 28 个 `.csproj`、212 条相对路径 | `grep Include=` |
| repowiki `file://` 链接 | 11,598 条 | `grep -rho` |

宿主 partial 按目录的占比，决定 §5 的提取顺序：

| 目录 | 文件 | 行 | 宿主 partial 文件 / 行 | 占比 |
| --- | ---: | ---: | ---: | ---: |
| `ModeE/` | 18 | 13,960 | 13 / 12,616 | 90% |
| `ModeF/` | 22 | 10,814 | 16 / 9,719 | 89% |
| `ModeD/` | 9 | 5,471 | 8 / 5,409 | 98% |
| `WavesArena/` | 9 | 3,844 | 8 / 3,774 | 98% |
| `LootAndRewards/` | 12 | 6,660 | 8 / 5,091 | 76% |
| `ZombieMode/` | 46 | 25,654 | 32 / 18,934 | 73% |
| `BossFilter/`、`UIAndSigns/`、`Injection/` | 6 | 2,489 | 6 / 2,489 | 100% |
| `Integration/` | 403 | 165,634 | 62 / 24,079 | 14% |
| `DebugAndTools/` | 159 | 56,400 | 16 / 8,132 | 14% |
| `Utilities/` | 36 | 8,197 | 9 / 3,133 | 38% |
| `Config/` | 16 | 3,193 | 11 / 2,130 | 66% |
| `RandomEvents/`、`ModeG/`、`Achievement/`、`Campaign/`、`Audio/`、`ModeH/`、`Common/` | 202 | 89,903 | 15 / 6,616 | 7% |
| 根 `ModBehaviour.cs` | 1 | 1,785 | 1 / 1,785 | |

已经是模块化样板、不需要提取的：`PetNest/`（无宿主 partial）、`ModeH/`（1 个 327 行入口 partial）、`ModeG/`、`Campaign/`、`RandomEvents/`、`Integration/DailyReport/`、`Integration/Codex/`、`Integration/BackMountain/`、`Utilities/OfficialQuests/`。

## 2. 目标架构

### 2.1 结构

保留现有顶层目录与命名空间，只补语义，不搬树：

| 角色 | 位置 | 职责 | 不允许 |
| --- | --- | --- | --- |
| Host | 根 `ModBehaviour.cs`、`ModConfigApi.cs`、`Utilities/*RuntimeHooks.cs`、`Common/Lifecycle/` 的 host / 注册 / 基类 | 官方生命周期、`Update` 阶段调度（§2.4 冻结）、`RegisterRuntimeModules()` 显式装配、跨模式入场协调、ModConfig 绑定、逐项登记的旧公开入口一行转发 | 持有任何模式 / 系统的业务字段、协程状态、计时器、支付与持久化状态 |
| 模块 | 各模式与系统目录（`ModeD/`…`ZombieMode/`、`PetNest/`、`Campaign/`、`Integration/<系统>/`、`DebugAndTools/SkyIsland/`） | 自己的 `XxxRuntimeModule`（或 `XxxRuntime` 服务）持有状态、订阅、协程句柄、静态缓存重置；对外只暴露窄的查询 / 动作方法 | 读写别的模块的私有状态；通过宿主字段间接读 |
| 共享底座 | `Common/`（UI、存档引擎、JSON、装备能力、属性追踪、建筑基类）、`Utilities/`（刷怪核心、`RuntimeScope`、`RunScopedRegistry`、场景门控、恢复监控） | 无玩法归属的能力，静态无状态或按 owner 创建实例 | 引用具体模式类型（丧尸、Mode E 等）；持有跨模块可变状态 |
| 官方接点 | `Patches/`（共享补丁）、各模块自己的补丁、`Utilities/OfficialQuests/` | Harmony、反射、官方 prefab 适配 | 换目标、换优先级、改同步拦截顺序 |
| 开发工具 | `DebugAndTools/`（除 `SkyIsland/`） | F3、调试热键、场景原型 | 承载正式生产判据（§6 第 6 条） |

模块可以持有宿主引用，但只用于 `StartCoroutine` / `StopCoroutine` / `gameObject` / `transform` 这类 Unity 服务；`IBossRushRuntimeModule.OnAwake(ModBehaviour)` 签名保留。模块之间的通信按语义选：查询（同步只读）、动作（显式结果）、事实通知（沿用 `BossRushEventBus`，保持同步派发）、拦截判据（Harmony 同步返回值）。不建服务定位器，不建装下所有服务的 Context，不把已有同步通知改成排队。

### 2.2 宿主终态（P6 的判定口径）

- `ModBehaviour` 各 partial 文件只允许：官方生命周期回调、`Update` / `LateUpdate` 阶段调度、模块装配与引用字段、跨模式入场协调（`WavesArena/BossRushEntryFlow.cs` 的选择部分）、ModConfig / Config 参数存储（`Config/*.cs` 保留）、一行转发的旧公开入口、为二进制兼容保留的嵌套类型声明（如 `ModBehaviour.OriginalWeaponData`，实例归龙裔模块）。
- `tests/modbehaviour_partial_budget.json` 的 `allowed_files` 与预算随每簇提取下调，只降不升；终态目标：预算文件数 ≤ 60、总行数 ≤ 25,000，做不到的簇写明原因。
- 不允许的通过方式：只改 `internal`、只改文件夹、把字段包进仍由宿主支配的大 Context、加服务定位器、把私有字段改 public。

### 2.3 明确不做与理由

| 原稿方案 | 不做的理由 |
| --- | --- |
| 全仓搬到 `Host/Shared/Frameworks/Modules/…` | 搬目录不减少任何读取字节；波及 1,020 条清单、468 个守卫文件、212 条夹具引用、1.16 万条 repowiki 链接；深路径让每条引用更长。导航靠 §3.4 的索引解决 |
| Frameworks 的 Core / Contracts / Adapter，`GameIntegration` 实现 Frameworks 定义的接口 | 每个接口永远只有一个实现者（这款游戏），剩下的是转发层；夹具靠链接生产文件加官方类型替身已经可测 |
| Roslyn 成员级语义分析器、IL 层、三份架构 JSON | 单 DLL 下 `internal` 全可见，边界靠归属与审查；现有约 670 个守卫加 partial 预算已在做同方向约束，成本低得多。出现现有守卫抓不到的越界时再评估 |
| `tools/compile_sources.py` | 2026-09-23 已由 `tools/compile_list.py` 落地（提交 29480ec0，配 `SyntaxProbeCompileListParityGuard`），所有清单消费者从它取 |
| 多 DLL | partial 不能跨程序集；NPC 与 Harmony 扫描只看宿主程序集；bundle 带类型身份 |

### 2.4 必须冻结的运行时轨迹

提取只换状态的家，不换顺序、时间源、随机数调用次数、yield 帧数：

1. `ModBehaviour.Update`：`TickAlwaysOnRuntime` → `CanRunGameplayThisFrame` 门 → `runtimeModuleHost.OnUpdate` → `MutatorUI.Tick` → `TickEquipmentAbilityRuntime` → `TickGameplaySupportRuntime` → `TickModeRuntimeGroup`（先共享 spawn postprocess，再 Arena；Arena 提前返回跳过 E/F/G/Zombie 与后续）→ Boss 回血 → debug。
2. 注册 / 销毁：`RegisterRuntimeModules()` 的顺序（OfficialQuest 先于 SkyIsland / Campaign，Campaign 先于 BackMountain）；host 逆注册顺序销毁；Integration 全局清理与模块销毁的先后（PetNest 建筑在 `BossRushIntegration_StartAndScene` 停，DailyReport 建筑在其模块 `OnDestroy` 停）；Mode G 的 `PrepareHostDestroy` 先于成就等全局清理；`BossRushSaveFileThrottle.ResetStaticCaches` 晚于所有模块落盘。
3. Mode G：host 注册的实例与 `ModeGEntry` 每局 new 的 run 核心是两个来源，run 的 `Dispose` 后不可重用；本次不合并，只保证入口、HUD、RunContext 指向同一局核心。
4. Campaign：活跃模式 / 波次是纯查询；`ModeFBounty.ConsumeModeFPlayerBountyKillLatch` 是同步消费、读一次清一次，保留 victim 身份与顺序（§6 第 3 条拆开查询与消费）。
5. UI：字体、缩放、模态输入租约只有一个 owner；租约首个获取时快照、最后一个释放时恢复；丧尸结束只释放自己的租约。
6. 异步：Zombie 在 Loading / Menu / 目标子场景的启动保留条件按原码；装备离手、模式失败退款 / 欠账、`RuntimeScope.Clear` 的「清理动作 → 停协程 → 销毁对象」分阶段顺序不变。

## 3. 上下文与 token 治理（P1）

### 3.1 度量

先测再改，改完再测，结果写 `architecture/CONTEXT_BASELINE.md`（受跟踪）：

- **固定开销**：`CLAUDE.md` 及其导入链自动加载的字节数；`MEMORY.md` 不算。
- **代表任务**（5 个）：日报版面改动、天空岛居民服务加一项、NPC 商店交易改价、给一把新武器配数值、Mode F 奖励调整。每个任务记录：从玩家入口读到生产逻辑需要打开的文件数与字节数；准备阶段是否需要全仓 `grep`；必须读的规则文档字节数。前后各测一次，同一人同一方法。
- 字节与文件数是代理指标，不换算成 token 百分比；有同工具同模型的实际用量时另记。

### 3.2 根 `AGENTS.md` 瘦身

目标 ≤ 180 行、≤ 20 KB。章节编号一律不动（守卫注释与 `TypeIdLedgerGuard`、`SkyIslandFieldcraftGuard`、`OfficialCompileListFileExistenceGuard` 按 §4.1 / §4.3 原文解析；改前 `grep -n "AGENTS.md" tests/*.py` 复核）。

| 章节 | 处理 |
| --- | --- |
| §3 子系统地图 | 换成「看 `MODULES.md`」加子目录 `AGENTS.md` 清单，≤ 8 行 |
| §4.1、§4.3 | 原文不动 |
| §4.14 UI | 全文迁到新建 `Common/UI/AGENTS.md`；根只留 4 行：共享库、质感层、AccentFill 口径、交互骨架文档路径 |
| §4.16 新增内容 | 细则并入 `Integration/AGENTS.md`；根留 4 个要点 |
| §4.17 F3 与常驻 HUD | 全文迁到新建 `DebugAndTools/AGENTS.md`；根留 3 行 |
| §5 契约 | 留 6 条要点加 `docs/contracts.md` 指针 |
| §7、§9、§11、§14 | 压缩到要点，不删规则 |

同步：`CLAUDE.md` 的子目录清单加 `Common/UI/AGENTS.md`（做任何 UI 前读）与 `DebugAndTools/AGENTS.md`（F3 / 调试）；新增 `tests/RootAgentsBudgetGuard.py`：字节上限、必需标题存在、§4.1 / §4.3 标题原样。迁出的每一条在新位置逐字可找到，`docs/ai-docs-migration.md` 记「原 §x → 新位置」对照。

### 3.3 台账拆档

- `FIX_TRACKER.md` 正文只留最近 14 天与未闭环节；更早的节按月整体剪到 `archive/FIX_TRACKER_2026-08.md`、`archive/FIX_TRACKER_2026-09.md`（受跟踪，逐字不改，开头一行写来源与区间）。正文顶部加「更早记录见 `archive/`」。
- `CODE_REVIEW_FINDINGS.md` 同法：UNVERIFIED / Seeded Leads / Open 项与最近 14 天留正文，其余按月归档。`tests/SkyIslandOfficialApiReuseGuard.py` 读该文件（`FINDINGS` 常量），先读它要找什么；被归档的 CR 编号在正文加一行「CR-xxx → archive 文件」索引或让守卫同时读归档，二选一并反向验证。
- 新增 `tests/LedgerSizeGuard.py`：`FIX_TRACKER.md` ≤ 1,500 行、`CODE_REVIEW_FINDINGS.md` ≤ 1,200 行。根 `AGENTS.md` §9 加一句：每月 1 日把上月节归档。

### 3.4 导航索引

- `architecture/modules.json`：唯一人工维护的归属表。每个模块：`id`、`title`、`kind`（host / mode / map / system / content / shared / devtools）、`paths`（glob，含散落的 `Config/ConfigXxx.cs`、`Localization/XxxLocalization.cs`、`Integration/Items/` 里的配置器）、`entry`、`state_owner`、`public_api`、`data`（`Assets/Data/*.json`）、`tests`（守卫前缀、夹具名）、`docs`（repowiki 与 `docs/architecture/` 链接）、`rules`（适用的 AGENTS 文件）、`depends_on`（声明用于导航，不做机器强制）。
- `MODULES.md`（根目录）：由脚本生成的表，一模块一行：id、一句职责、入口、规则文件。手写说明放生成区之外。
- `tools/task_context.py --module <id> [--task <一句话>]`：输出阅读顺序（根规则要点 → 模块规则 → 入口 → 公开接口 → 数据 → 相关守卫与夹具 → 专题文档），未知 id 非零退出；`--check` 校验：编译清单每个文件至少属于一个模块、每个 glob 至少命中一个文件、id 唯一、`MODULES.md` 生成区无漂移。
- `tests/ModuleIndexGuard.py` 调 `--check`；反向验证：删一个 glob、改坏 `MODULES.md` 各转红一次再还原。
- 不新增每模块 README；只有有独有约束的模块才加 ≤ 60 行的子 `AGENTS.md`。`.qoder/repowiki/` 不重生成，只把各模块专题链接登进 `modules.json`。
- `CLAUDE.md` 加一句：开工先 `python tools/task_context.py --module <id>`，id 见 `MODULES.md`。

### 3.5 退出判据

根 `AGENTS.md` 与两份台账达标且守卫在位；`task_context.py --check` 通过；5 个代表任务的前后数字已填；全量守卫绿。

## 4. 复用试点（P2）

### 4.1 1A：词缀属性挂载并入共享追踪器

- 事实：`Integration/AffixForge/AffixRuntimeService_Effects.cs` 的 `AffixStatModifierApplier.TryAdd` 自建挂载，文件头说共享追踪器写死 `PercentageAdd`；`Common/Stats/RuntimeStatModifierTracker.cs` 已有显式 `ModifierType` 重载，说明已过时。移除路径本来就走 `RemoveAll`。
- 做法：词缀改调共享 `TryAdd(…, ModifierType)`，保留词缀自己的错误日志文案与记录容器；删除自建挂载器或降为一行转发。
- 夹具：`tests/fixtures/AffixCombat/Stubs.cs` 现在用同名替身 `RuntimeStatModifierTracker`（只有 `RemoveAll`）；改为链接真实 `Common/Stats/RuntimeStatModifierTracker.cs` 并补底层 Stat 替身，覆盖三种 `ModifierType`、失败分支、移除与 owner 隔离。
- 记录类型改名见 §6 第 4 条，与本项同一提交。

### 4.2 1B：建筑恢复核心

- 事实：`Integration/DailyReport/DailyReportMailboxRuntime.cs` 与 `PetNest/PetNestBuilder_DataEventsAndRuntime.cs` 的恢复协程都是：等两帧（两次 `yield return null`）→ 按当前活动场景句柄刷新缓存 → 遍历官方建筑 → 判身份与功能点 → 装配并登记 → `finally` 释放协程句柄。
- 做法：在 `Common/Buildings/` 加恢复核心（与 `BuildingInjectionHelper` 同目录；交互基类在 `Interactables/BossRushBuildingInteractableBase.cs`，不动），核心负责等待、请求合并、场景缓存刷新、遍历、实例缓存与 `finally`；日报与遗种巢各提供「目标建筑判定、功能点检查、装配」策略；每个 owner 一个核心实例、一个取消 owner。
- 保留：两帧等待与「等待后读当时活动场景」语义；`Common/Infrastructure/ObjectCache.cs` 的 Unity 假 null、`GetInstanceID()` 去重、`ReferenceEquals` 排除常驻 prefab；两条清理入口的先后（§2.4 第 2 条）。不新增「请求场景与当前场景不同就放弃」的行为。
- 回归：新增执行回归覆盖双 owner 并存、重复请求、等待期间换场景、恢复中取消、取消后再请求且旧请求迟到收尾、对象销毁、单个装配失败；`ContentBuildingOwnership` 继续验证桥接。

### 4.3 退出判据

两处旧实现各只剩一份共享实现或一行转发；相关夹具链接真实生产文件；`AffixCombat`、`ContentBuildingOwnership` 与新增回归绿；两种构建通过。

## 5. 宿主状态提取（P3）

### 5.1 每簇做法模板

1. **盘点**：列出该簇 partial 文件里的字段、协程句柄、事件订阅、计时器、静态缓存与公开入口，每项标去向：模块状态 / 宿主调度 / 兼容转发 / 删除。写进 `architecture/MIGRATION_STATUS.md` 的簇小节。
2. **改声明**：`partial class ModBehaviour` 改为 `partial class XxxRuntimeModule`（模块类允许 partial 跨同目录文件），文件留在原目录、原文件名。模块类继承 `BossRushRuntimeModuleBase`，用现有 `RegisterRuntimeModules()` 的实例。
3. **修引用**：簇内 `this.` 的宿主成员改为模块成员；簇外读该簇状态的地方改为经模块的查询方法（`ModBehaviour` 上留一行转发属性供旧调用方，如 `IsModeDActive => modeDRuntime.IsActive`）；`StartCoroutine` 经保留的宿主引用。
4. **生命周期**：事件订阅按根 `AGENTS.md` §4.6 迁到模块的 `OnDestroy` / `OnSceneLoaded`；`*StaticCacheReset.cs` 的重置随迁；`*RuntimeHooks.cs` 的 Tick 主体迁进模块 `OnUpdate`，宿主 hook 只留调用顺序。
5. **守卫与预算**：从 `tests/modbehaviour_partial_budget.json` 的 `allowed_files` 删掉本簇文件并下调预算；跑 `python tools/run_guards.py --changed-only`，动态读清单的守卫（`ModBehaviourPartialBudget`、`OfficialCompileList`、`GameplayCoverage`、`ModuleIndex`）每簇显式全跑。
6. **回归与构建**：相关夹具（§9 表）加本簇新增回归；正式 + Dev 隔离构建；`check_dll_identifiers.py` 正反检查。
7. **提交**：一簇一个提交；`MIGRATION_STATUS.md` 记录完成项与下一簇。

### 5.2 提取顺序

| 序 | 簇 | 规模 | 要点 |
| ---: | --- | --- | --- |
| 1 | `BossFilter/`、`UIAndSigns/`、`Injection/` | 6 文件 2.5K 行 | 热身，验证模板；`Injection/Injection.cs` 若确无引用则删除并更新清单、预算、根 §3 |
| 2 | `WavesArena/` + `LootAndRewards/` | 16 文件 8.9K 行 | Arena 模块持标准 / 无间炼狱状态；`BossRushEntryFlow` 的全模式选择部分留宿主；奖励欠账、里程碑投递 owner 明确 |
| 3 | `ModeD/` | 8 文件 5.4K 行 | 先把被 F 借用的物品池与配装提成 `ModeDItemPool` 类服务，再迁 D 自有状态；`ModeDRuntimeModule.CaptureValidity` 改读模块状态 |
| 4 | `ModeE/` + `ModeF/` | 29 文件 22.3K 行 | 真实共享的生成准备、阵营、商人、后处理进 `Utilities/`（已有 `EnemySpawnCore`、`RuntimeScope`、`RunScopedRegistry`）；E/F 私有各归各；`ModeEMerchantSupportClasses.cs`（4,080 行）拆成按类型的文件；活敌、死亡 / 掉落订阅、恢复锚点、修饰器、代次逐项列 owner |
| 5 | `ZombieMode/` | 32 文件 18.9K 行 | 已有 `ZombieModeRuntimeModule` 与子目录 `AGENTS.md`；奖励、净化点、临时 NPC、run-only 对象、启动债务语义不变；共享 UI 能力先按 §6 第 5 条移出 |
| 6 | `Integration/` 的 62 个宿主 partial | 24K 行 | 按子系统：DeathWraith（6）、Bonus 套装（8）、三位 Boss 与 `_ModeGAdapter`、Wedding、WishFountain、FlightTotem / Frostmourne / ReverseScale bootstrap、`BossRushIntegration_*`（4）、`IntegrationDeferredBootstrap`。`IntegrationDeferredBootstrap` 的阶段顺序（物品初始化 → EssentialContentReady → 配偶 / 基地恢复 → 装备加载 → 占位符）不变 |
| 7 | 根 `ModBehaviour.cs` | 1,785 行 | 前六簇完成后清理残留字段，`Update` 顺序不动 |
| 保留 | `Config/*.cs`、`Utilities/*RuntimeHooks.cs`、`DebugAndTools/` 的 F3 partial、`Common/Lifecycle/` | | 参数存储、调度层、开发工具按 §2.1 属于 Host / 开发工具；只迁 §6 第 6 条那一个判据 |

簇 1–4 必须由主窗口顺序做（共享底座在变）；簇 5、6 的子系统互不相邻，可派子代理并行，宿主转发属性与 `Utilities/` 改动仍由主窗口合。

### 5.3 退出判据

簇 1–7 全部完成，预算达到 §2.2 目标或写明差距；`ModBehaviour` 各 partial 只剩 §2.2 允许的内容；每簇有提交与回归证据。

## 6. 耦合点（P4）

锚点写符号名，不写行号。做之前 `grep` 复核仍然成立。

| 序 | 事实 | 动作 | 验证 |
| ---: | --- | --- | --- |
| 1 | `Common/Lifecycle/BossRushRuntimeModuleRegistration.cs` 是宿主 partial 却在共享目录 | 移到根目录 `ModBehaviourRuntimeModules.cs`；清单、预算、守卫路径同步 | 编译；`ModBehaviourPartialBudget` |
| 2 | `Utilities/ModeRuntimeHooks.cs` 的 `TickModeRuntimeGroup` 集中驱动多模式，Arena 提前返回 | 不改；P3 迁 Tick 主体时保持调用顺序与早返 | 新增执行回归抽取生产调度顺序对照（不复制期望表） |
| 3 | `Campaign/CampaignModeBridge.cs` 的 `HasCampaignBountyMark` 调 `ModeFBounty.ConsumeModeFPlayerBountyKillLatch`（读即清） | 拆成 `ModeF` 模块上的纯查询与显式 `Consume…` 两个方法；Campaign 只在原消费点调 Consume | 回归：只消费一次、victim 身份、先写后删顺序 |
| 4 | `Common/Stats/RuntimeStatModifierTracker.cs` 用丧尸命名的 `ZombieModeAttributeModifierRecord`（20 个文件引用） | 改名为 `BossRushStatModifierRecord`，一次改完含夹具，不留双名 | 编译；`AffixCombat` 等夹具 |
| 5 | `Common/UI/BossRushUI.cs` 调 `ZombieModeUIHelper.GetGameFont()`；缩放、模态租约与 `timeScale` 快照 / 恢复（`_modalPreviousTimeScale`）也在丧尸帮助类，被 `Common/UI/BossRushConfirmDialog.cs` 等共享件反向依赖 | 字体、缩放、租约、暂停维持迁到 `Common/UI/`（`BossRushUI` / `BossRushUIKit`），`ZombieModeUIHelper` 只转发；租约计数只有一份 | 回归：两个消费者并存与释放；`BossRushUISharedLibraryGuard`、`BossRushUIFeelGuard` |
| 6 | `DebugAndTools/F3GameplayValidationRunner.cs` 的 `ValidationHasActiveMode` 是天空岛正式入口的模式冲突判据（`SkyIslandSession` 两处调用） | 判据迁到生产门控（`Utilities/` 场景门控或 Host），F3 与天空岛共用 | 正式构建里可达；`SkyIslandValidationSuiteGuard` |
| 7 | `Integration/EquipmentFactory.cs` 枚举具体装备配置器 | 核对后改为与 `ItemFactory.RegisterConfigurator` 同款的注册入口，各装备 bootstrap 自行登记；发布顺序、克隆兜底、恢复重入不变 | `DynamicItemInitialization`、`ManualEquipmentRecovery` 夹具 |
| 8 | `Integration/Affinity/Systems/NPCShopSystem.cs` 通用商店里有丧尸净化点分支 | 临时商店由创建者传入支付策略（现金 / 净化点），保持官方购买回调之后的交付 / 回滚时机 | `ContentTransactions` 夹具；回归覆盖回滚 |
| 9 | `ModeF/ModeFEntry.cs` 调 D 的物品池与 E 的生成 / 商人 | 随 §5 簇 3、4 处理，不单独做 | 同上 |
| 10 | `Integration/NPCs/Common/NPCModuleRegistry.cs`、`Utilities/AlwaysOnRuntimeHooks.cs` 只扫宿主程序集 | 不动（单 DLL） | |

退出判据：第 1、3–8 条完成并有回归；第 2、9、10 条按表处理。

## 7. 有限目录归位（P5）

只做两处，最后做，各一个提交：

1. `DebugAndTools/SkyIsland/` → `SkyIsland/`。理由：正式地图内容住在调试目录，是本仓库唯一一处名实不符。波及：编译清单约 96 条、47 个守卫、17 条夹具引用、3 个工具、7 篇 repowiki、根 / `Integration/` 的 `AGENTS.md` 与 `CLAUDE.md`、`Assets/Data/GameplayCoverage.json` 的 `DebugAndTools/SkyIsland` 域键、4 处源码注释、`tools/gameplay_coverage.py` 的分域深度逻辑。做法：`git mv` 后用 Python 按字节替换上述文件里的路径（`.bat` 保持 CRLF，不用 `sed -i`，不用 Edit 工具改混排换行的文件），`DebugAndTools/SkyIsland/AGENTS.md` 随目录走并在 `CLAUDE.md` 改指向。完成判据：`grep -rn "DebugAndTools/SkyIsland"` 在 `archive/` 与台账历史之外为零；全量守卫、天空岛全部夹具、正式与 Dev 构建绿。
2. `Injection/` 在 §5 簇 1 里确认无引用后删除（受 git 管理的 23 行占位）。

其余目录不搬。`Integration/` 的分域由 `modules.json` 表达。

## 8. 执行顺序与检查点

| 检查点 | 内容 | 进入 | 退出 |
| --- | --- | --- | --- |
| P0 基线 | 读根与相关子目录 `AGENTS.md`、`docs/contracts.md`；`git status` 干净；环境（§9.1）；隔离构建正式 + Dev；全量守卫与全部回归跑一遍记基线；§3.1 前测；建 `architecture/MIGRATION_STATUS.md` | | 基线数字与红项清单写入状态文件；提交 |
| P1 上下文治理 | §3.2–3.4 | P0 | §3.5；提交 |
| P2 复用试点 | §4 | P1 | §4.3；提交 |
| P3 状态提取 | §5 簇 1→7 | P2 | §5.3；每簇提交 |
| P4 耦合点 | §6 | P3（第 4、5 条可在 P2 / P3 顺带做） | §6 退出判据；提交 |
| P5 目录归位 | §7 | P4 | §7 判据；提交 |
| P6 收口 | 预算按实际下调；`modules.json` 与 `MODULES.md` 对齐终态；repowiki 专题（§4.13）与 `docs/architecture/` 相关篇更新；§3.1 后测；`FIX_TRACKER.md` 一节汇总；§9 交付报告；§11 清单；最终正式构建部署到真实游戏目录并核对 SHA-256（游戏须关闭，DLL 被锁则报告） | P5 | §0「完成的定义」 |

检查点之间不停下来问；同一检查点内可以细分可恢复步骤。验证链坏了先修验证链，再继续。

## 9. 验证与证据

### 9.1 环境

- 仓库在 `D:\code\ykf\BossRushMod`；游戏由 `compile_official.bat` 自动探测（本机 `D:\software` 下）。执行回归需要的三个 D 盘环境变量见 `tests/AGENTS.md`。
- 隔离构建：把游戏 `Duckov_Data\Managed` **复制**到临时游戏根（不用 junction / symlink），`GAME_PATH` 指向它，`WORKSHOP_PATH` 指向创意工坊 Harmony；`BOSSRUSH_NO_PAUSE=1`。编译用 PowerShell `& "$PWD\compile_official.bat"`，`cmd /c` 在沙箱下解析不到。正式与 Dev 共享 `Build/BossRush.dll` 与 rsp，必须串行；正式构建前 `Remove-Item Env:BOSSRUSH_DEV_BUILD`（设成 0 也会开 Dev）。每种配置构建后立即 `python tools/check_dll_identifiers.py --dll Build/BossRush.dll --expect present|absent` 并归档 SHA-256 到 `Build/migration/<run-id>/`（不进 git）。
- csc 在中文 Windows 输出 GBK，别拿 `grep` 的空结果当无错误；成功标志是 `Build succeeded!` 加 DLL 指纹变化。

### 9.2 每个检查点必跑

1. `python tools/run_guards.py --changed-only`；P1、P3 每簇、P5、P6 跑全量。`--changed-only` 按守卫正文里的路径粗筛，动态读清单或索引的守卫要显式 `--filter` 跑。
2. 新增或修改的守卫做反向验证：人为破坏 → 实跑转红 → 按字节还原；不靠放宽断言或白名单。
3. 相关执行回归；P0、P3 结束、P6 跑全部。回归入口会覆盖 `Build/runtime-regressions/results.json`，每次归档。
4. 正式 + Dev 隔离构建。
5. 改了 `wiki-site/` 或 `WikiContent/` 才跑 `npm --prefix wiki-site run build`（本计划默认不改）。

回归定位种子（以 `--list` 为准）：属性 / UI：`AffixCombat`、`AffixSelectionUI`、`RuntimeOwnership`；建筑 / 工厂：`ContentBuildingOwnership`、`ResourceProduction`、`DynamicItemInitialization`、`ContentTransactions`、`ManualEquipmentRecovery`；征程 / 任务：`CampaignPlayability`、`BackMountainLifecycle`、`SkyIslandOfficialContract`；模式：`AuditModeLifecycle`、`AuditCombatSeptember`、`ModeGCombat`、`ModeH*`、`ZombieModeEntryDebt`、`BossRewardDelivery`、`RewardPoolReliability`；NPC：`NpcAuditFixes`、`PermanentDuckNpcDialogue`、`IntegrationThirdReviewFixes`、`SetBonusCoroutines`；随机事件 / 天空岛 / F3：`RandomEventsFailure`、`RandomEventTempo`、`SkyIsland*`、`F3ValidationExecution`、`F3AutotestJudges`。

### 9.3 证据分级与交付报告

结论标 L1（静态接线）/ L2（守卫、回归、离线属性测试）/ L3（实机），不往上抬。交付报告写进 `architecture/MIGRATION_STATUS.md` 末节并在 `FIX_TRACKER.md` 加一节：每个检查点的提交号、守卫 / 回归 PASS / FAIL / SKIP / 基线红项、两种构建的 SHA-256、§3.1 前后数字、预算前后、未完成项与原因、剩余 L3。「无新增回归」与「全部通过」分开写；没有性能采样不写「无性能问题」。

## 10. 续接与回退

- `architecture/MIGRATION_STATUS.md`（受跟踪）每个可恢复步骤更新：当前检查点、已完成簇、下一具体动作、最近提交号、基线红项、未完成项。新窗口先 `git status` 与 `git log -5`，再从第一个未完成步骤继续。
- 回退用 git：每个检查点一个提交，`git revert` 单个提交即可；绝不 `reset --hard`、`git clean` 或递归删除。不删 git 之外的本地文件。
- 与 `docs/` 相关的改动：`docs/` 默认 local-only，本计划只改被 `.gitignore` 放行的文件（本文、研究记录、`docs/architecture/UI制作共识.md`）与 `docs/ai-docs-migration.md`；需要新的受跟踪文档时放 `architecture/` 或 `archive/`。

## 11. owner 实机清单（P6 生成）

从实际入口文字与 F3 用例生成，给入口路径 / 按键、期望、日志或报告位置、不合格条件；不编造不存在的步骤 ID；截图由 owner 目检，AI 只读文字报告。必须覆盖：

| 范围 | owner 操作 | 不合格条件 |
| --- | --- | --- |
| 基地两次进出 | 用测试档进基地，开迁移涉及的 NPC、报箱、图鉴、百科，出入一次 | 入口缺失、重复选项、raw 本地化键、残留界面 |
| 标准 / 无间 / D / E / F / G / H / 丧尸 | 各开一局到奖励与退出；G 开第二局 | 入口失效、重复敌人或奖励、退款错、G 第二局不推进、退出残留 |
| 跨模式与异常退出 | 结束一种再进另一种；死亡、撤离、生成中切图 | 旧协程在新局刷怪或发奖；阵营、装备、时间流速、音乐、HUD 残留 |
| 天空岛 / 征程 | 出发返回，查 Jeff 与岛上任务 | 任务重复或丢失、官方保存过滤报错 |
| NPC / 装备 / 建筑 | 现金与净化点服务；装备卸下切图；已建建筑再进基地后交互 | 扣费交付不一致、离手仍运行、建筑功能点消失 |
| 共享 UI | 连续开关两个用模态租约的界面；中英切换 | 暂停或鼠标状态错、字体错位 |
| Dev F3 | 主套件与岛内套件各跑一轮，附本轮相关 case / 步骤 ID 与看图清单 | 报告红项 |

## 12. 可复制到新窗口的启动指令

```text
请执行 docs/design/2026-09-22_BossRushMod模块解耦与上下文治理计划.md（2026-09-24 修订版）的全部内容，按第 8 节 P0–P6 连续推进到第 0 节「完成的定义」。

授权范围：第 3 节上下文治理（根 AGENTS.md 瘦身但章节编号不动、台账按月归档到 archive/、新建 MODULES.md / architecture/modules.json / tools/task_context.py 与对应守卫、改 CLAUDE.md 的子目录清单）；第 4 节两个复用试点；第 5 节全部簇的宿主状态提取（保持第 2.4 节冻结的运行时轨迹）；第 6 节耦合点；第 7 节仅有的两处目录归位；对应的编译清单、守卫、夹具、覆盖表、repowiki 链接与专题文档更新；隔离游戏根的正式与 Dev 构建；最终正式构建部署到真实游戏目录并核对 SHA-256。
我在你执行期间不提交。你按检查点 git commit（简短中文信息、逐个列出暂存路径），不 push、不建 PR。
不启动游戏、不读写玩家存档、不重打 AssetBundle、不改 TypeID / 存档键 / 本地化键 / 资源名 / Harmony 目标、不新增玩法内容、不改经济数值、不删除 git 之外的本地文件。
不做计划第 2.3 节列出的「明确不做」项。
子代理最多 3 个、一律 Opus，只分配第 5 节互不相邻的叶子簇；ModBehaviour.cs、编译清单、Common/、Utilities/、索引与状态文件由你自己改。
不要在检查点之间停下来问我是否继续；上下文压缩后先看 architecture/MIGRATION_STATUS.md 与 git log 再续接。守卫红了修根因，不放宽断言、不加 known-red、不做目录级豁免；做不完的项如实标未完成。
完成后给我：第 9.3 节的交付报告、第 3.1 节的前后数字、正式 DLL 的 SHA-256 与部署核对结果、第 11 节的实机清单。
```

## 13. 修订记录

**2026-09-24 全面修订**（本会话审核后由 owner 要求）：

- 范围从「全仓搬迁到新目录树 + Frameworks 分层 + Roslyn / IL 分析器 + 三份 JSON 台账」改为「上下文治理 + 宿主状态提取 + 复用与耦合点 + 两处目录归位」，理由见 §2.3。审核依据：搬目录不减少读取字节；波及 468 个守卫文件、212 条夹具引用、1.16 万条 repowiki 链接；仓库两天 18 提交、+5.6 万行，与并行开发冲突（owner 随后承诺执行期间停止提交，故本版允许按检查点提交）。
- 原 MIG-08「统一清单解析器」已于 2026-09-23 由 `tools/compile_list.py` 落地（提交 29480ec0），删去拟建的 `tools/compile_sources.py`。
- 原 §9.2「本次仓库实际位于 `D:\sofrware`」是笔误：仓库在 `D:\code\ykf\BossRushMod`，游戏在 `D:\software`，改为自动探测加显式变量。
- 原 §11 要求「独立叶子任务并行委派」与 owner 的子代理上限（3 个、Opus）相冲，改为 §0 的执行纪律。
- 数字按 2026-09-24 HEAD 重采（§1）；原稿的 960 / 974 文件、203 partial 等旧快照删除。
- 新增台账归档（§3.3）：审核时量得 `FIX_TRACKER.md` 1,027 KB、`CODE_REVIEW_FINDINGS.md` 617 KB，是单次会话最大的可避免读取量。
- 原 §2、§3、§5–§10 中仍有效的判据（宿主终态、运行时轨迹、1A / 1B 判据、验证口径、owner 清单）保留并压缩；研究记录里的 A1–A15 仍是本版的设计依据。
- 本文由约 95 KB 压到约三分之一；新窗口按 §12 一段话启动，先读本文再读根 `AGENTS.md`。
