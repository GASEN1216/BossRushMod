# AGENTS.md — BossRushMod 协作规则

本文件是仓库级的唯一规则来源；`CLAUDE.md`、`GEMINI.md`、`.github/copilot-instructions.md`、`.cursor/rules/agents.mdc` 只做转发。
在某个子目录工作时，再读离它最近的 `AGENTS.md`（§3 表格最后一列）。

事实优先级：**当前代码、构建脚本与 guard > 本文件与子系统 `AGENTS.md` > `docs/contracts.md` > `.qoder/repowiki/` > 历史设计稿与旧报告**。
发现本文与代码冲突时以代码为准，顺手修正本文，并在 `docs/ai-docs-migration.md` 记一笔。

## 1. 项目概览

《逃离鸭科夫》（Escape from Duckov）的大型 Unity Mod。以 BossRush 竞技场为起点，现在包含标准 BossRush 与无间炼狱、Mode D–H、末日丧尸模式、独立出击地图天空岛、剧情战役鸭王征程，以及自定义 Boss、装备、NPC、基地建筑、游戏内百科与在线 Wiki。

- C# 7.3，命名空间统一 `BossRush`，运行在游戏内嵌的 Mono 与 Unity 主线程上。
- 没有 `.csproj`：`compile_official.bat` 显式列出全部源码，直接调用 Roslyn `csc.dll`，引用本机游戏的 `Duckov_Data\Managed` 与创意工坊的 Harmony。只有装了游戏的 Windows 能真正编译。
- 对官方游戏没有稳定 API，靠 Harmony 补丁、反射和官方类型强绑定。官方反编译源在 `鸭科夫源码/`，只读参考、不参与编译。
- 质量保障四层：Windows 编译、`tests/` 的结构守卫与属性测试（CI 自动跑）、`tests/fixtures/` 的隔离执行回归、游戏内实机。前三层都过，不代表运行时正确。
- 维护语言中文：文档、回复、提交信息默认中文。

## 2. 常用命令

在仓库根目录执行。AI 会话里编译要用 PowerShell 的 `&` 调**完整路径**：`cmd /c compile_official.bat` 和 `compile_dev.bat`（内部是相对路径 `call`）在沙箱下都会报「不是内部或外部命令」，那不是代码错误。

```powershell
& "$PWD\compile_official.bat"                                  # 正式构建，成功标志 Build succeeded!，并自动部署到游戏 Mod 目录
$env:BOSSRUSH_DEV_BUILD = "1"; & "$PWD\compile_official.bat"   # Dev 构建：DevLog、F2–F12 调试热键、F3 验收套件（输出先有 [DEV] BOSSRUSH_DEV enabled）
python tools/run_guards.py --changed-only                      # 与改动相关的守卫；去掉参数跑全量（run_guards.bat 等价）
python tools/run_runtime_regressions.py --filter <名字>         # 隔离执行回归；--list 看全部
python tools/verify_syntax.py --with-bcl                       # 没装游戏时的语法探针，不等于编译通过
npm --prefix wiki-site run build                               # 改了 wiki-site/ 或 WikiContent/ 时
```

- `compile_official.bat` 自动探测 `GAME_PATH` 与 `WORKSHOP_PATH`，探测不到时显式设置；部署写 `%GAME_PATH%\Duckov_Data\Mods\BossRush\`。游戏开着时 DLL 与 bundle 被锁、copy 静默失败，部署后按实际游戏路径核对 SHA-256。`BOSSRUSH_NO_PAUSE=1` 跳过结尾的 pause。
- 只想确认某个提交能编译、不碰游戏目录：在临时 worktree 上编，`GAME_PATH` 指向放了 `Duckov_Data\Managed` **拷贝**的临时目录（不要用 junction，PowerShell 5.1 递归删除会顺着删掉游戏 DLL）。
- 交付部署用正式构建，不把 Dev 构建留在游戏目录。
- 执行回归里依赖官方 DLL 的夹具需要能找到游戏程序集，环境变量见 `tests/AGENTS.md`。
- 改 `.bat` 不要用 `sed -i`：`.bat` 必须是 CRLF（`.gitattributes`），换成 LF 后 cmd 会报与代码无关的怪错；`compile_official.bat` 的注释用 ASCII 标点（`chcp 65001` 下全角标点后的尾段会被当命令执行）。

## 3. 子系统地图

| 路径 | 职责 | 专项规则 |
| --- | --- | --- |
| `ModBehaviour.cs`、`ModConfigApi.cs` | 主入口、全局状态、ModConfig API | 本文 §4.15 |
| `WavesArena/` | 标准 BossRush 与无间炼狱波次 | |
| `ModeD/`、`ModeE/`、`ModeF/` | 白手起家、划地为营、血猎追击 | |
| `ModeG/` | 宿命回响：九波三幕、宿敌、契约 | |
| `ModeH/` | 百战留痕（黑市鸭王杯）：经理人模式 | |
| `ZombieMode/` | 末日丧尸模式，独立生命周期与奖励 | `ZombieMode/AGENTS.md` |
| `Campaign/` | 鸭王征程：六章剧情契约，经全局采集器与少量 notify 漏斗挂到各模式，不重构模式代码 | |
| `PetNest/`、`RandomEvents/` | 遗种巢（养崽）、局内随机事件 | |
| `Integration/` | 物品、装备、NPC、商店、好感、婚姻、重铸、词缀锻造、图鉴、日报、竞技场后山、新武器与套装、天空岛物品 | `Integration/AGENTS.md` |
| `DebugAndTools/` | 调试工具、F3 调试菜单与玩法验收；`SkyIsland/` 是天空岛运行时（正式内容）；`ArenaPrototype/` 是石堡等场景原型 | `DebugAndTools/SkyIsland/AGENTS.md` |
| `Common/` | 共享特效、装备能力、数据解析、存档引擎、共享 UI 库 `Common/UI/BossRushUI.cs` | 本文 §4.14 |
| `Utilities/` | 跨模块运行时 hooks、刷怪核心、场景门控、恢复监控 | `Utilities/AGENTS.md` |
| `Patches/` | 跨模块 Harmony 补丁 | `Patches/AGENTS.md` |
| `Config/`、`Localization/`、`LootAndRewards/` | 配置与数据注册、本地化注入、掉落与奖励 | |
| `Achievement/`、`Audio/`、`BossFilter/`、`Interactables/`、`MapSelection/`、`UIAndSigns/` | 各自独立的小子系统 | |
| `Injection/` | 旧注入逻辑的残留占位，实际逻辑已并入 `ModBehaviour` / `Integration` | |
| `Assets/Data/`、`Assets/SpawnPoints/` | 进 git 的 JSON 数据表；其余 `Assets/`（bundle、图片、音效）local-only | |
| `ArtSource/SkyIsland/`、`tools/` | 天空岛可重复生成的数据；生成器、构建与校验脚本 | |
| `tests/` | 结构守卫、属性测试、执行回归夹具 | `tests/AGENTS.md` |
| `WikiContent/`、`wiki-site/` | 游戏内百科正文（中英）、VitePress 在线 Wiki | `wiki-site/AGENTS.md` |
| `docs/` | 设计、教程、审查、契约资料，默认 local-only | `docs/AGENTS.md` |
| `.qoder/repowiki/` | 详细知识库（底子是 2026-08 的生成快照） | 本文 §4.13 |
| `鸭科夫源码/` | 官方反编译源码，只读参考；grep 时排除或写明用途 | |

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

自定义物品 / 装备 TypeID 用 5000xx 区间，严格递增，不回填删掉的号。TypeID 会进存档键、掉落表、Wiki 与调试流程，复用会破坏存档。

- 当前登记范围：`500001-500085`。
- 保留空洞：`500009`、`500047`，不回填。
- 下一可用：`500086`。
- 新增时同时更新本节、`docs/contracts.md` §1 与 `docs/Bossrush使用物品ID表.md`（`TypeIdLedgerGuard` 交叉核对前两处），接线清单见 `Integration/AGENTS.md`。Boss、NPC、建筑的字符串 ID 不占这个序列。

### 4.4 `DisplayNameRaw` 必须配本地化注入

设置 `DisplayNameRaw = "BossRush_<Name>"` 的物品 / 装备，必须在对应 Config 的 `InjectLocalization()` 注入中英文，并挂进 `InjectLocalization_Extra_Integration()`，否则游戏内显示 `*BossRush_<Name>*`（`LocalizationInjectionGuard`）。玩家可见文本一律中英双语；语言在取用时解析，因为玩家能在游戏里切语言。

### 4.5 刷怪敌对性安全网不得移除

官方 preset 的队伍可能是中立。Boss 生成后若不与玩家敌对，必须走 `SetTeam(Teams.wolf)` 安全网，否则会出现不攻击、打不死、卡波次；玩家方随从要豁免，Mode E/F 有独立阵营体系。Mod 刷出的敌人还要解除官方距离休眠（`docs/contracts.md` §7.1）。

### 4.6 事件订阅幂等且必须退订

静态 / 全局事件订阅要有私有布尔或同等 owner 状态防重复，用可退订的命名方法，并在 `OnDestroy`、`ShutdownRuntime()`、`Cleanup*()` 等销毁路径退订。死亡触发的变异词条走 `MutatorContext.EnemyKilledCallbacks`，不直接订阅死亡事件。子系统清理只有一个 owner：各自 `RuntimeModule.OnDestroy()`（`EventSubscriptionLifecycleGuard`、`StaticCacheLifecycleGuard`，约定见 `docs/架构说明/事件订阅生命周期约定.md`）。

### 4.7 防御式 `try/catch` 是宿主防崩策略

大量 `catch` 是有意空吞，防止 Mod 异常拖崩游戏。不要成批清理，也不要把「空 catch 存在」本身当 bug。关键初始化、存档、绑定、刷怪路径可以补 `DevLog` 或 `Debug.LogWarning`；每帧热路径不加日志。`DevLog` 带 `[Conditional("BOSSRUSH_DEV")]`，正式构建里整条调用不存在。

- `catch` 块里不能 `yield return`（CS1631）：在 catch 里只置标志，把 `yield return` 挪到外面（`IteratorYieldInCatchGuard`）。
- 手工 `MoveNext()` 驱动协程时必须透传 `Current`：`yield return` 出来的子 `IEnumerator` 要递归驱动，丢掉就等于子协程一次都没跑。

### 4.8 Config 三层归位

1. 运行时可调参数：`Config/Config.cs` + `ModConfigApi`。新增 ModConfig 键要登记白名单，否则热更新静默失效（`ModConfigOptionChangeGuard`）。玩法系统总开关不暴露给玩家、默认恒开，只暴露调参旋钮。
2. 玩法强耦合常量：模块自己的 `XxxConfig.cs` / `XxxTuning.cs`。
3. 大型数据表：`Assets/Data/*.json` + Registry + guard + 硬编码 fallback。嵌套 JSON 用 `Common/Data/BossRushJsonValue`，不再建第二套解析器；`JsonUtility` DTO 字段的 CS0649 是误报，定点 `#pragma warning disable 0649` 并写明原因。

详见 `docs/架构说明/Config归位约定.md`。

### 4.9 Hooks 分层

单模块 hook 留在模块目录；跨模块 / 全局基础设施 hook 放 `Utilities/`。不因为「未来可能复用」提前提升到全局层（`docs/架构说明/Hooks分层约定.md`）。

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

- Canvas `sortingOrder` 用 `BossRushUILayers` 常量；颜色用 `BossRushUIColors` token，遮罩用 `Backdrop`。
- 面板、按钮、卡片底图走 `BossRushUI.ApplyPanelSkin`：卡片、分隔线、滚动滑块显式传 `BossRushUISkinPart`，`radius <= 3` 的细条程序化绘制（规格见 `docs/制作教程/BossRushUI_图集规格.md`）。深色面板要有边就调 `BossRushUI.ApplyPanelStroke`（描边色 `BossRushUIColors.Stroke`）——图集里烤进去的内描边会被深色 token 乘到看不见。
- 按钮字色用 `BossRushUI.GetButtonTextColor(背景色)`，不写死；对比度按实际合成后的底色算，正文至少 4.5:1。
- 缓动只用 `BossRushUI.EaseOut`（位移）与 `BossRushUI.SmoothStep`（原地淡变），子元素错峰入场用 `BossRushUIEntranceAnimation`，不引入 DOTween 一类第三方依赖。走 unscaled 时间的表现层自带 `BossRushUI.IsGamePaused()` 门；常驻 HUD 跟随 `BossRushUI.IsOfficialHudHidden()`。
- 字体用 `BossRushUI.ApplyGameFont` / `ZombieModeUIHelper.GetGameFont()`，新文本用 TMP（内置 Arial 渲染不了中文）；`CanvasScaler` 调 `ZombieModeUIHelper.ConfigureCanvasScaler`。
- 能直接复用官方 prefab（`GameplayDataSettings.UIPrefabs.*`、克隆 `MapSelectionEntry` 等）就不用共享库重造。
- 选项先判断再挂，不挂灰掉的占位项；「能不能挂」与「点了会不会被拒」共用同一份判据；同一页超过 3–4 项就分二级。
- 叙事走官方对话（`DialogueManager.ShowDialogueSequenceBilingual` / `ShowMultipleChoiceBilingual`，长文案一句一屏），图鉴条目走官方 `NoteIndex`（我们的存档是权威，官方图鉴只做镜像）。自绘面板只在官方给不了的能力上保留，理由写进文件头。官方任务系统 `Duckov.Quests` 刻意不接（§10）。

守卫：`BossRushUISharedLibraryGuard`、`BossRushUISkinLoaderGuard`、`SkyIslandUiContrastGuard`、`SkyIslandOfficialApiReuseGuard`、`SkyIslandChoiceGateGuard`。

### 4.15 新子系统的状态归属与宿主 partial 预算

- 新子系统的状态、异步任务和专属算法放在自己的 RuntimeModule、服务或对象里，不新增承载这些职责的 `partial class ModBehaviour`；表现层（特效、光、粒子）写独立类型。
- 宿主只保留必要的生命周期分发和旧公开入口，兼容转发尽量一行；跨模块建筑反射与注入走 `BuildingInjectionHelper`。
- `tests/ModBehaviourPartialBudgetGuard.py` 检查 partial 文件清单、文件数与所在文件总行数，预算在 `tests/modbehaviour_partial_budget.json`：收敛后下调，不为普通功能抬高预算，不靠压缩排版或删必要注释凑数。
- 其他有行数预算的大文件（如 `Config/Config.cs`、`Common/UI/BossRushUI.cs`）要腾空间时，原样提取到同一 partial 的新文件，行为逐字不变，并同步查找它的守卫。

### 4.16 新增内容：可以加，但要接得上

新物品、新系统、新 TypeID、`SCHEMA+` 的存档与配置扩展、重打 AssetBundle 都是正常开发手段，不需要事先申请；需要 owner 签字的只有 §10 的破坏性事项。要求在于做完整：

- 每件内容写清「从哪来 / 拿来做什么（卖钱不算）/ 串到哪条系统线」。功能重叠的拉开定位，不为新增而新增。
- 「有代码」不等于「拿得到」：零获取途径、入口没接线、配置器没登记，编译和守卫都查不出来。交付前从玩家入口读一遍到生产逻辑，能写成守卫或属性测试的写上。
- 存档扩展走 `SCHEMA+`：新字段可选、旧档读出有合理默认值、掩码与版本同步。持久化复用 `Common/Lifecycle/BossRushSaveCoordinatorEngine` 与 `BossRushSlotJsonStore`，不再复制状态机。
- 重打包会把作者工程当下的全部资产一起发出去：打包前确认作者工程里没有别人未完成的改动，打包与部署后按 `DebugAndTools/SkyIsland/AGENTS.md` §6 核对。
- 交付记录里写的「本轮不加 TypeID / 不改存档 / 不重打包」只描述那一轮的范围，不是长期规则。

### 4.17 F3 验收用例与常驻 HUD

F3 玩法验收只在 Dev 构建里存在（`BOSSRUSH_DEV_BUILD=1`），目标是把实机前能自动看的都收进报告，人工清单只留读报告与手感项。新增用例时：

- **判据与取数分开**：判据写成 `#region 纯判据` 里的静态函数，只吃基本类型与小结构、返回 ok / reason / metrics，由执行回归逐字抽取运行（天空岛是 `tests/fixtures/SkyIslandValidationJudges`）；取数方法只读观测面（天空岛是 `SkyIslandSessionValidation.cs` 的属性）。物理、渲染、Harmony 这类离线造不出来的只做 L1 守卫，不硬凑执行回归。
- **只读套件就是只读**：不写剧情与存档、不注册或写官方图鉴、不刷怪、不改强制夜里、不打补丁（`SkyIslandValidationSuiteGuard` 的禁用清单）。会改状态的检查进 Dev 演练套件：整文件 `#if BOSSRUSH_DEV`、独立按钮、复用专用测试档的开跑门、报告头 `read_only=false`、`finally` 还原、不写存档不收录；只读套件不得引用演练代码（`SkyIslandReadOnlySuiteDrillIsolationGuard`、`SkyIslandDrillNoPersistenceGuard`）。
- **SKIP 不能吞缺陷**：先查「缺了会让这条用例永远 SKIP」的前提（资源在不在、补丁装没装、皮肤注入没有），再按场景条件（白天、场上没有目标）记 SKIP。
- **用例 id 写字面量交给外壳**：`RunSyncCase` / `RunSkyIslandSync` / `RunSkyIslandCase` 一类外壳的第一个参数写字面量；新增外壳登记进 `tools/gameplay_coverage.py` 的 `CASE_RUNNERS`，用例登记进 `Assets/Data/GameplayCoverage.json`（`GameplayCoverageCaseRunnerGuard`）。要在基地看的（官方图鉴镜像、出击残留）挂主套件 `RunSuite`，不塞进岛内套件。
- **常驻 HUD**（进局就一直在、不是玩家主动打开的）每帧入口同时经过 `BossRushUI.IsOfficialHudHidden()` 与 `BossRushUI.IsGamePaused()`，并登记进 `tests/PersistentHudVisibilityGuard.py`。引用 HUD 层级常量的文件都要在那里归类；模态面板与玩家主动打开的面板写明理由排除。

## 5. 不可破坏的契约

详细契约见 `docs/contracts.md`。进入代码前先识别这些兼容面：

- TypeID、存档 key、`SavesSystem` key、配置 key。
- `StreamingAssets/BossRushModConfig.txt` 的 JSON 配置格式。
- `Assets/SpawnPoints/*.json` 地图刷新点格式与硬编码 fallback。
- `WikiContent/catalog.tsv` 与 Wiki 正文索引；在线站导航 `wiki-site/docs/.vitepress/data/structure.mts` 与它一一对应（`WikiSiteStructureGuard`）。
- 本地化 key，尤其 `BossRush_*` raw key。
- AssetBundle 文件名、Prefab base name、`EquipmentFactory` / `ItemFactory` 命名规则。
- Harmony 目标、`AccessTools` 字段、字符串反射绑定。数量随代码变化，以源码与逐类安装日志为准；官方更新后按 `docs/架构说明/Harmony补丁契约稳定性.md` 复查。
- 官方游戏行为：静默失败类陷阱（距离休眠、搜刮箱随机关闭、品质静默降级、空壳物品、暂停压 timeScale 等）见 `docs/contracts.md` §7.1。
- 地图 `sceneName` / `sceneID`、场景传送坐标、NPC / 建筑字符串 ID。
- 渲染路径：游戏跑在 URP Deferred 下，自研着色器必须带 `UniversalGBuffer` pass，缺了编译、守卫、判包全绿而玩家进游戏看不见（`docs/架构说明/自研着色器与官方渲染管线约定.md`，闸门 `tools/verify_sky_island_bundle_shaders.py`）。
- Python 守卫断言的结构约束。

## 6. 兼容性分类

变更说明、审查 finding、修复记录都标注以下分类之一或多项：

| 分类 | 含义 | 例子 |
| --- | --- | --- |
| `SAFE` | 纯文档、注释、无行为代码整理，或静态证明无运行时变化 | AI 入口转发、文档索引 |
| `COMPAT` | 向后兼容的功能 / 数据扩展 | 新增内容、新增可选配置且默认保持旧行为 |
| `SCHEMA+` | 文件 / 配置 / 存档 schema 的向后兼容扩展 | JSON 增加可选字段、存档新增旗标位 |
| `SCHEMA-` | schema 删除、重命名或语义改变 | 配置 key 改名、字段必填化 |
| `WIRE+` | 与外部服务 / 游戏 API 的兼容扩展 | 飞书请求增加可选字段 |
| `WIRE-` | 外部 API、协议、反射目标的破坏性改变 | Harmony 目标换方法、请求字段改名 |
| `BREAKING` | 破坏存档、玩家现有配置、旧资源或旧工作流 | TypeID 复用、删除旧 key |
| `OPERATIONAL` | 部署、构建、路径、密钥、人工流程变化 | 改构建脚本路径、改发布流程 |

`SCHEMA-`、`WIRE-`、`BREAKING` 与高风险 `OPERATIONAL` 先拿 owner 明确确认（§10）。

## 7. 工作方式

- 先读实际代码、调用点、构建脚本和相关守卫再下结论。触及 TypeID、本地化、存档、配置、事件、Harmony / 反射、刷怪、模式状态机时，读对应专项文档与 `docs/contracts.md`。官方行为以 `鸭科夫源码/` 核实，不凭记忆。
- 旧审查线索、日志片段、用户猜测是「未验证线索」，不是 confirmed bug；只能静态推断的结论写明「未运行验证」。
- 工程原则（owner 定）：最小化修改、最大化复用、无性能问题、沿用仓库已验证的设计模式。「最小化修改」指不做与任务无关的重构、格式化、命名清洗或目录搬迁，**不是**限制完成任务所需的新增。
- 先找能复用的：官方系统与 prefab，仓库现有管线（`ItemFactory` / `EquipmentFactory`、共享 UI、存档引擎、JSON 解析器、建筑交互体基类、刷怪核心）。不另起第二套状态、缓存、注册表、计时器或解析器。
- 决策分三档：
  - **自己定**：实现方式、复用哪条管线、守卫与测试怎么写、不破坏兼容的新增（新内容、新 TypeID、`COMPAT`、`SCHEMA+`）。
  - **定了之后写明理由**：玩法与数值取舍。owner 授权拍板时按「好玩优先，其次离线可证、改动小、可回退」直接定，把决定、理由、回退办法写进交付报告与 `FIX_TRACKER.md`；没有授权又拿不准的，给出推荐方案并标 `Needs owner confirmation`。
  - **先问**：§10 的全部事项。
- 多个会话可能同时在本工作区写文件：动手前看 `git status`，只改、只暂存自己的文件，提交前再查一次。

## 8. 验证与如实交付

代码改动的默认顺序：

1. `python tools/run_guards.py --changed-only`（大改动跑全量）。
2. Windows 编译（§2）。只做了语法探针时，写「语法通过，未正式编译」。
3. 可隔离的逻辑跑或补执行回归：`python tools/run_runtime_regressions.py --filter <名字>`。
4. 改了 `wiki-site/` 或 `WikiContent/`：`npm --prefix wiki-site run build`，再按 `wiki-site/AGENTS.md` 的检查项。
5. 专题文档按 §4.13 更新。
6. 运行时行为（UI、本地化、刷怪、事件泄漏、过图性能、Harmony / 反射是否命中）只能靠游戏内确认。

证据分级。结论要标明级别，不能往上抬：

- **L1 静态接线**：从玩家入口一路读到生产逻辑。
- **L2 隔离回归**：守卫、执行回归、离线属性测试全绿。
- **L3 实机**：真实游戏进程里跑出来的结果。

「编译绿 + 守卫绿 + 部署成功」不能写成「已生效」「已验证可玩」。拿不到 L3 时，交付一份粒度到操作步骤的人工验证清单（按哪个键、看哪一行、什么算不合格）。没有实际采样时不宣称「无性能问题」。无法验证的，在回复、`FIX_TRACKER.md` 或交付文档里写明原因。

纯文档改动至少确认引用路径存在、没有和其他规则冲突；改了脚本、守卫、资源路径或文档里的命令，按影响范围验证。

## 9. 审查、Findings 与修复台账

- 审查方法：`CODE_REVIEW.md`；confirmed finding 库：`CODE_REVIEW_FINDINGS.md`；修复流水：`FIX_TRACKER.md`。旧路径 `docs/代码审查/CODE_REVIEW*.md`、`docs/协作/FIX_TRACKER.md` 只做转发。
- findings 只记已确认的问题，未证实的放 UNVERIFIED / Seeded Leads；accepted、refuted、deferred 都写理由，避免重复排查。
- 修 bug、回归、兼容问题后更新 `FIX_TRACKER.md`，回填 finding 的状态、验证方式与证据级别。
- 根目录的 `AUDIT_*.md`、`FIXES_*.md` 是历史审计快照。2026-09-13 在 WSL 里生成、未入库的 `CODE_QUALITY_AUDIT_*`、`CODE_REVIEW_REPORT_*`、`SKY_ISLAND_AUDIT_*` 含编造的锚点与过期状态，引用前逐条复核。

## 10. 必须先得到 owner 明确同意的事

- 删除、迁移、批量重写玩家数据或存档；存档与配置 schema 的破坏性变更（`SCHEMA-`、`BREAKING`）。
- TypeID 复用、删除、回填；改已发布内容的 TypeID、存档 key、本地化 key。
- 写入官方存档键（例如接入 `Duckov.Quests`：卸载 Mod 后官方会对缺失的 id 报错）。
- 公开 API、跨模块契约、外部协议的破坏性变更（`WIRE-`）；密钥、飞书与生图网关等外部服务配置。
- `git push`、建 PR、创意工坊发布、改部署流水线或全局改造构建脚本。
- 启动游戏做测试、读写玩家存档目录（实机由 owner 自己做）。
- 经济数值的大幅改变（owner 已授权「好玩优先」拍板的范围除外）。
- 游戏模式状态机大规模重构、Harmony / 反射绑定策略整体替换。
- 大规模目录迁移、批量格式化、批量清理 catch。
- 删除 git 之外的本地文件（`docs/`、`Assets/`、`skills/` 等不在 git 里，删了找不回来）。

## 11. 不是规则来源的目录与文件

- `鸭科夫源码/`：官方反编译源码，只读参考。
- `Build/`：构建产物与旧审查快照（里面有旧版 `AGENTS.md` 副本，grep 时排除）；DLL、bundle、部署产物不进 git。
- `docs/superpowers/`、`.kiro/specs/`、`.claude/plans/`：历史计划与工具过程材料。
- `skills/`、`codex-skills/`：2026-03 至 04 的技能快照，没有工具会自动加载，内容大多过时（逐个状态见 `skills/README.md`）。
- `.cunzhi-memory/`、`.claude/settings*.json`：工具私有记忆与本机权限配置。
- `.qoder/better-harness*/`：工具过程材料；`.qoder/repowiki/` 是知识库（§4.13）。
- `docs/项目全景文档.md`：已归档，停在 2026-08-27 的基线。
- `docs/飞书应用密钥.md`、`docs/AI生图API和密钥.md`：含密钥，不复制进回答、提交、PR 或其他文档。

## 12. 提交

- 只在用户明确要求时 `git commit`；`push` 与 PR 见 §10。
- 提交信息用简短中文摘要（例：`修复售货机 UI 崩溃`），不用英文 conventional-commit。
- 暂存路径限定到本次改的文件，不用 `git add -A` / `git add .`。同一文件里要拆开的改动，用 `git hash-object -w` + `git update-index --cacheinfo` 暂存，不改写工作区。
- 提交前确认：新增 `.cs` 已进编译清单、TypeID 台账已更新；不带 `Build/`、DLL、bundle、密钥和别的会话正在写的文件。

## 13. 文档纳管

- `docs/`、`Assets/`（`Assets/Data/`、`Assets/SpawnPoints/` 除外）、`ArtSource/`（天空岛的数据与说明除外）默认 local-only。被守卫直接读取的文件在 `.gitignore` 里逐个放行；新增这类依赖时同步放行，否则 fresh clone 一跑守卫就红。
- 仓库级规则只写在进 git 的文件里（本文、子系统 `AGENTS.md`、`CODE_REVIEW.md`、`docs/contracts.md`）。`docs/架构说明/` 等本地文档里的关键结论，要在这些文件或守卫里有落点。
- 规则文档里不写会过期的数字（守卫数、文件数、包体大小）；需要时写「以某命令输出为准」。每轮交付的范围与数字写在 `FIX_TRACKER.md` 或带日期的交付文档里，不写进本文。

## 14. 变更记录（已迁出）

2026-09-14 之前，本节按日期记录每轮交付的经验，逐渐长到四百多行。长期有效的规则已提炼进 §4、§7、§8 与子系统 `AGENTS.md`（天空岛的在 `DebugAndTools/SkyIsland/AGENTS.md`），官方 API 的静默失败类陷阱进了 `docs/contracts.md` §7.1；每轮的数字与明细原本就在 `FIX_TRACKER.md`。

- 代码注释或旧报告里写「见 AGENTS §14」的，原文在提交 `00c8624` 的版本里：`git show 00c8624:AGENTS.md`。
- 不再往本文追加变更记录：交付明细写 `FIX_TRACKER.md`，新发现的长期规则写进对应章节或子系统 `AGENTS.md`，AI 协作文档本身的调整记在 `docs/ai-docs-migration.md`。

最后整理：2026-09-14。
