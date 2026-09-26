# AGENTS.md — BossRushMod 协作规则

本文件是仓库级的唯一规则来源；`CLAUDE.md`、`GEMINI.md`、`.github/copilot-instructions.md`、`.cursor/rules/agents.mdc` 只做转发。
在某个子目录工作时，再读离它最近的 `AGENTS.md`（§3 表格最后一列）。

事实优先级：**当前代码、构建脚本与 guard > 本文件与子系统 `AGENTS.md` > `docs/contracts.md` > `.qoder/repowiki/` > 历史设计稿与旧报告**。
发现本文与代码冲突时以代码为准，顺手修正本文，并在 `docs/ai-docs-migration.md` 记一笔。

## 1. 项目概览

《逃离鸭科夫》（Escape from Duckov）的 Unity Mod，含 BossRush、Mode D–H、丧尸模式、天空岛、鸭王征程与内容集成。C# 7.3，命名空间 `BossRush`，运行于游戏内嵌 Mono 和 Unity 主线程。
没有 `.csproj`；`compile_official.bat` 显式列源码，以本机游戏程序集与创意工坊 Harmony 真编译。官方反编译源 `鸭科夫源码/` 只读，不参与编译；Harmony、反射和官方类型需对照现码。
质量证据分为 Windows 编译、`tests/` 守卫与属性测试、隔离回归、游戏内实机；前三者不能证明实机正确。维护语言、文档、回复与提交信息默认中文。

## 2. 常用命令

在仓库根目录用 PowerShell 的 `& "$PWD\compile_official.bat"` 正式构建；Dev 构建先设 `$env:BOSSRUSH_DEV_BUILD="1"`，再调同一完整路径。`cmd /c` 在沙箱下可能误报找不到命令。守卫：`python tools/run_guards.py --changed-only`（全量去参数）；回归：`python tools/run_runtime_regressions.py --filter <名字>`（全量去参数）；语法探针：`python tools/verify_syntax.py --with-bcl`；改 Wiki 时 `npm --prefix wiki-site run build`。
构建自动探测 `GAME_PATH` / `WORKSHOP_PATH`，正式部署写游戏 Mod 目录；游戏锁文件时复制可能静默失败，按实际目标 SHA-256 核对。`BOSSRUSH_NO_PAUSE=1` 跳过等待。隔离构建的 `GAME_PATH` 指向游戏 Managed 的**拷贝**，不用 junction；交付部署用正式构建，不把 Dev DLL 留在游戏目录。依赖游戏 DLL 的夹具环境变量见 `tests/AGENTS.md`。
`.bat` 保持 CRLF，修改勿用 `sed -i`；`compile_official.bat` 的注释用 ASCII 标点，避免 `chcp 65001` 下尾段被当命令执行。

## 3. 子系统地图

模块职责、入口和规则文件见 `MODULES.md`；按任务运行 `python tools/task_context.py --module <id>`。
专项规则：`Integration/AGENTS.md`、`Patches/AGENTS.md`、`Utilities/AGENTS.md`、`ZombieMode/AGENTS.md`、`DebugAndTools/AGENTS.md`、`SkyIsland/AGENTS.md`、`Common/UI/AGENTS.md`、`tests/AGENTS.md`、`docs/AGENTS.md`、`wiki-site/AGENTS.md`。
官方反编译源 `鸭科夫源码/` 只读、不编译；`docs/` 与多数 `Assets/` 默认 local-only。

## 4. 硬规则

括号里是守卫这条规则的脚本；没有守卫的靠审查。

### 4.1 新增 `.cs` 必须登记编译清单

`compile_official.bat` 显式列出全部源文件，没有通配符。新增 `.cs` 不登记，就不会编进 `Build/BossRush.dll`，而且不报错。清单与磁盘双向一致由 `tests/OfficialCompileListFileExistenceGuard.py` 守卫；当前文件数以它的输出为准，不写进文档。

```bash
python tools/run_guards.py --filter OfficialCompileList
```

### 4.2 只有 Windows 上的真编译才算编译通过

编译依赖本机游戏程序集与 Harmony。WSL、Linux、CI 只能读代码、跑守卫与语法探针，不能据此声称「已编译通过」。语法探针有结构性盲区：缺游戏程序集时 Roslyn 不分析迭代器方法体，CS16xx 一类错误根本不会产出。csc 在中文 Windows 下输出 GBK，用 grep 解析它的输出可能静默失配，别拿空结果当「无错误」。

### 4.3 TypeID 严格递增、不复用

自定义物品 / 装备 TypeID 用 500xxx 区间，严格递增，不回填删掉的号。TypeID 会进存档键、掉落表、Wiki 与调试流程，复用会破坏存档。

- 当前登记范围：`500001-500103`。
- 保留空洞：`500009`、`500047`，不回填。
- 下一可用：`500104`。
- 新增时同时更新本节、`docs/contracts.md` §1 与 `docs/reference/Bossrush使用物品ID表.md`（`TypeIdLedgerGuard` 交叉核对前两处），接线清单见 `Integration/AGENTS.md`。Boss、NPC、建筑的字符串 ID 不占这个序列。

### 4.4 `DisplayNameRaw` 必须配本地化注入

设置 `DisplayNameRaw = "BossRush_<Name>"` 的物品 / 装备，必须在对应 Config 的 `InjectLocalization()` 注入中英文，并挂进 `InjectLocalization_Extra_Integration()`，否则游戏内显示 `*BossRush_<Name>*`（`LocalizationInjectionGuard`）。玩家可见文本一律中英双语；语言在取用时解析，因为玩家能在游戏里切语言。

### 4.5 刷怪敌对性安全网不得移除

官方 preset 的队伍可能是中立。Boss 生成后若不与玩家敌对，必须走 `SetTeam(Teams.wolf)` 安全网，否则会出现不攻击、打不死、卡波次；玩家方随从要豁免，Mode E/F 有独立阵营体系。Mod 刷出的敌人还要解除官方距离休眠（`docs/contracts.md` §7.1）。

### 4.6 事件订阅幂等且必须退订

静态 / 全局事件订阅要有私有布尔或同等 owner 状态防重复，用可退订的命名方法，并在 `OnDestroy`、`ShutdownRuntime()`、`Cleanup*()` 等销毁路径退订。死亡触发的变异词条走 `MutatorContext.EnemyKilledCallbacks`，不直接订阅死亡事件。子系统清理只有一个 owner：各自 `RuntimeModule.OnDestroy()`（`EventSubscriptionLifecycleGuard`、`StaticCacheLifecycleGuard`，约定见 `docs/architecture/事件订阅生命周期约定.md`）。

### 4.7 防御式 `try/catch` 是宿主防崩策略

大量 `catch` 是有意空吞，防止 Mod 异常拖崩游戏。不要成批清理，也不要把「空 catch 存在」本身当 bug。关键初始化、存档、绑定、刷怪路径可以补 `DevLog` 或 `Debug.LogWarning`；每帧热路径不加日志。`DevLog` 带 `[Conditional("BOSSRUSH_DEV")]`，正式构建里整条调用不存在。

- `catch` 块里不能 `yield return`（CS1631）：在 catch 里只置标志，把 `yield return` 挪到外面（`IteratorYieldInCatchGuard`）。
- 手工 `MoveNext()` 驱动协程时必须透传 `Current`：`yield return` 出来的子 `IEnumerator` 要递归驱动，丢掉就等于子协程一次都没跑。

### 4.8 Config 三层归位

1. 运行时可调参数：`Config/Config.cs` + `ModConfigApi`。新增 ModConfig 键要登记白名单，否则热更新静默失效（`ModConfigOptionChangeGuard`）。玩法系统总开关默认恒开，只暴露调参旋钮；鸭生无常例外：默认开启并允许玩家手动关闭。
2. 玩法强耦合常量：模块自己的 `XxxConfig.cs` / `XxxTuning.cs`。
3. 大型数据表：`Assets/Data/*.json` + Registry + guard + 硬编码 fallback。嵌套 JSON 用 `Common/Data/BossRushJsonValue`，不再建第二套解析器；`JsonUtility` DTO 字段的 CS0649 是误报，定点 `#pragma warning disable 0649` 并写明原因。

详见 `docs/architecture/Config归位约定.md`。

### 4.9 Hooks 分层

单模块 hook 留在模块目录；跨模块 / 全局基础设施 hook 放 `Utilities/`。不因为「未来可能复用」提前提升到全局层（`docs/architecture/Hooks分层约定.md`）。

### 4.10 守卫与被守卫的结构一起改

`tests/` 下的守卫断言结构不变式，不是功能测试。改动被守卫断言的结构时同步守卫；不要靠放宽断言或加白名单掩盖行为变化。新增或修改守卫要做反向验证（人为破坏 → 实跑转红 → 按字节还原）。写守卫、属性测试与夹具的纪律见 `tests/AGENTS.md`。

### 4.11 变异词条系统不变式

- `enableMutators` 默认 true。
- 共享变异系统不含 loot 类行为，不要重新引入 `LootChange` 或对 loot 质量、数量、类型的消费。
- 丧尸模式不接入共享变异 roll，它有独立的局内奖励系统。

### 4.12 重运行时工作按实际使用状态门控

- 装备专属的预热、扫描、轮询、对象池扩容或资源准备，只在主玩家实际手持、穿戴或启用时启动；NPC、仓库里的物品、背包里没用的物品和未激活的系统不触发。
- 复用现有装备 / 手持变化事件做幂等 owner 门控；离手、卸下、停用、死亡、切图和 runtime cleanup 时立即取消未完成的任务。
- 必须预热时按帧分摊并设完成目标（通常约 1 秒内），限制单帧预算；不靠削减特效、伤害或玩法来换性能。
- 系统关闭时每帧成本是 O(1) 早返。

### 4.13 知识库 `.qoder/repowiki/` 随专题更新

代码、脚本与守卫才是事实源。repowiki 是帮人定位的详细资料，底子是 2026-08 的生成快照，状态见 `.qoder/repowiki/README.md`。

- 改动改变了**已有专门文档的专题**（行为、流程、配置、内容）时，在同一次变更里更新那篇文档；新增子系统写一篇主题文档。
- 快照期旧文档里发现的错误，直接改正或标注过时，不要求一次补齐。
- 删除或改名代码文件时同步 `file://` 引用（`RepowikiReferenceGuard` 只查链接目标是否存在，不查正文是否过时）。

### 4.14 UI 走共享库与官方界面

- UI 规则全文在 `Common/UI/AGENTS.md`；共享库 `Common/UI/BossRushUI.cs`，优先复用官方界面。
- 按钮质感走共享 `BossRushUIFeel`，按钮颜色用 `ZombieModeUIHelper` 入口；面板描边走 `ApplyPanelStroke` / `ApplyFramedPanelSkin`。
- 主操作填充用 `BossRushUIColors.AccentFill`，`Accent` 只作强调；可见文字遵守 `PlayerFacingGlyphGuard`。
- 新 UI 按 `docs/architecture/UI制作共识.md` 定页型与按钮归属；确认弹窗统一 `BossRushConfirmDialog`。

### 4.15 新子系统的状态归属与宿主 partial 预算

- 新子系统的状态、异步任务和专属算法放在自己的 RuntimeModule、服务或对象里，不新增承载这些职责的 `partial class ModBehaviour`；表现层（特效、光、粒子）写独立类型。
- 宿主只保留必要的生命周期分发和旧公开入口，兼容转发尽量一行；跨模块建筑反射与注入走 `BuildingInjectionHelper`。
- `tests/ModBehaviourPartialBudgetGuard.py` 检查 partial 文件清单、文件数与所在文件总行数，预算在 `tests/modbehaviour_partial_budget.json`：收敛后下调，不为普通功能抬高预算，不靠压缩排版或删必要注释凑数。
- 其他有行数预算的大文件（如 `Config/Config.cs`、`Common/UI/BossRushUI.cs`）要腾空间时，原样提取到同一 partial 的新文件，行为逐字不变，并同步查找它的守卫。

### 4.16 新增内容：可以加，但要接得上

- 新内容与 `SCHEMA+` 可以自主实现；每件内容要有获取途径、用途和系统接线。细则见 `Integration/AGENTS.md`「原根规则 §4.16」。
- 存档扩展保留旧档默认值、掩码与版本，复用 `BossRushSaveCoordinatorEngine` 和 `BossRushSlotJsonStore`。
- 资源重打前核对作者工程，贴图导入和头盔校准按 `Integration/AGENTS.md` 执行；作者工程路径由 `tools/unity_project_path.py` 解析。
- 历史交付范围不是长期禁令；破坏性事项仍按 §10。

### 4.17 F3 验收用例与常驻 HUD

- F3、只读套件、Dev 演练和全自动实机回归的完整规则在 `DebugAndTools/AGENTS.md`；F3 只在 Dev 构建存在。
- 截图由 owner 看，AI 只读文字报告；请 owner 实测时给步骤 id、文件名、画面位置与不合格条件。
- 常驻 HUD 每帧经官方 HUD 显隐与暂停双门，并登记 `PersistentHudVisibilityGuard`。

## 5. 不可破坏的契约

详细内容见 `docs/contracts.md`；改代码前识别：TypeID、存档 / `SavesSystem` / 配置 key 与 JSON 格式；`Assets/SpawnPoints/*.json` 和 fallback；`WikiContent/catalog.tsv` 与在线导航；本地化 key；AssetBundle / Prefab / Factory 命名；Harmony 目标、反射字段和官方静默失败行为；sceneName / sceneID、传送坐标与 NPC / 建筑字符串 ID；URP Deferred 的自研 shader `UniversalGBuffer` pass；Python 守卫的结构约束。任何相关改动先读对应专项规则、契约与实际调用点。

## 6. 兼容性分类

变更说明和审查记录标分类：`SAFE` 是文档、注释或静态证明无运行时变化；`COMPAT` 是向后兼容的功能 / 数据扩展；`SCHEMA+` 是向后兼容的文件、配置、存档 schema 扩展；`SCHEMA-` 是 schema 删除、改名或语义改变。
`WIRE+` 是外部服务 / 游戏 API 兼容扩展；`WIRE-` 是外部 API、协议、反射目标破坏性改变；`BREAKING` 破坏存档、旧配置、资源或工作流；`OPERATIONAL` 涉及部署、构建、路径、密钥或人工流程。`SCHEMA-`、`WIRE-`、`BREAKING` 和高风险 `OPERATIONAL` 按 §10 先问 owner。

## 7. 工作方式

先读代码、调用点、构建脚本、守卫与专项规则，触及兼容面再读 `docs/contracts.md`；官方行为以 `鸭科夫源码/` 核实。历史审查、日志片段与猜测只是线索，未运行验证时明说。
最小化相关改动、复用官方系统与仓库现有管线，不做无关重构；实施方式及 `COMPAT` / `SCHEMA+` 自己定，玩法数值按「好玩、可证、改动小、可回退」判断并记理由；§10 事项先问。
多个会话可能共写工作区：动手前和提交前看 `git status`，只修改、暂存自己的文件。

## 8. 验证与如实交付

代码改动依次跑相关守卫（大改动全量）、Windows 真编译、可隔离的执行回归；改 Wiki 再跑站点构建，改变专题时更新 `.qoder/repowiki/`，运行时行为留实机验证。语法探针只证明语法，不等于正式编译。
证据分级：L1 为玩家入口到生产逻辑的静态接线；L2 为守卫、回归和离线属性测试；L3 为真实游戏进程。编译、守卫与部署成功不得写成「已生效 / 已验证可玩」。拿不到 L3 时给按键、观察位置和不合格条件的人工清单；未采样不宣称无性能问题，无法验证写明原因。
纯文档至少核对引用路径与规则冲突；脚本、守卫、资源路径或文档命令按影响范围验证。

## 9. 审查、Findings 与修复台账

审查方法在 `CODE_REVIEW.md`，confirmed finding 在 `CODE_REVIEW_FINDINGS.md`，修复流水在 `FIX_TRACKER.md`；未证实线索只记 UNVERIFIED / Seeded Leads。修复后回填状态、理由、验证和证据级别。根 `AUDIT_*.md` / `FIXES_*.md` 是历史快照，引用前复核。
每月 1 日把上月已闭环节按月整体归档到 `archive/`；正文只留最近 14 天索引与未闭环节，归档原文逐字保留。

## 10. 必须先得到 owner 明确同意的事

删除 / 迁移 / 批量重写玩家数据或存档；破坏性 schema / 兼容变更；TypeID 复用、删除、回填或改已发布 key；写官方存档键或加新 `Duckov.Quests` 任务；破坏公开 API / 外部协议；密钥与服务配置；`git push`、PR、创意工坊发布、部署流水线或全局构建改造；启动游戏、读写玩家存档；大幅改经济；模式状态机或 Harmony / 反射策略整体替换；大规模目录迁移、批量格式化 / 清理 catch；删除 git 外本地文件。
已授权例外：官方 `NoteIndex` 镜像、天空岛任务 590001 / 590011–590013、鸭王征程任务 590101–590106，均以 Mod 状态为权威并过滤官方保存快照；授权不扩到其它新任务。已明确授权的当次工作不用重复问。

## 11. 不是规则来源的目录与文件

`鸭科夫源码/` 只读；`Build/` 为不进 git 的构建产物；`.kiro/specs/`、`.claude/plans/`、`.qoder/better-harness*/` 为过程材料，`.qoder/repowiki/` 是知识库。`skills/`、`codex-skills/` 为过时快照；`.cunzhi-memory/`、`.claude/settings*.json` 为工具私有配置。`docs/飞书应用密钥.md`、`docs/AI生图API和密钥.md` 含密钥，不复制进回答、提交或文档。

## 12. 提交

仅用户明确要求时提交；信息用简短中文，不用 conventional commit。只暂存本次路径，不用 `git add -A` / `git add .`；同文件拆改可用 `git hash-object -w` 加 `git update-index --cacheinfo`。提交前核对 `.cs` 清单、TypeID 台账及不带 `Build/`、DLL、bundle、密钥或别人的文件；push / PR 见 §10。

## 13. 文档纳管

`docs/`、多数 `Assets/` 与 `ArtSource/` 默认 local-only；守卫直接读的文件须在 `.gitignore` 精确放行。仓库级规则仅放受跟踪的规则文件或 `docs/contracts.md`；本地 `docs/architecture/` 的关键结论应落到规则或守卫。规则文档不写易过期数字，交付数字写台账或带日期报告。

## 14. 变更记录（已迁出）

2026-09-14 前历史原文见 `git show 00c8624:AGENTS.md`；长期规则已进 §4、§7、§8 和子系统规则，数字与明细在 `FIX_TRACKER.md`。不再往本节追加，AI 文档调整记 `docs/ai-docs-migration.md`。
