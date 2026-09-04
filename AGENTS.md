# AGENTS.md — BossRushMod AI 协作唯一事实来源

> 所有 AI 协作者进入本仓库后必须先读本文件，再按任务范围读取子系统 `AGENTS.md` 和专项文档。`CLAUDE.md`、`GEMINI.md`、`.github/copilot-instructions.md`、`.cursor/rules/agents.mdc` 只做入口转发，不维护独立规则。
>
> 本文件收敛自旧 AI 文档、README、`docs/` 架构资料、审查记录、Kiro/spec 历史计划和当前代码/脚本静态核对。若本文与实际代码、构建脚本或 guard 冲突，优先相信当前代码与脚本，并把冲突记录到 `docs/ai-docs-migration.md`。

## 1. 项目一句话

BossRushMod 是《鸭科夫 / Escape from Duckov》的大型 Unity Mod，以 BossRush 竞技场为核心，扩展了多模式玩法、自定义 Boss/装备/NPC、成就、Wiki、重铸、婚姻、丧尸模式和大量运行时稳定性修复。

- 语言：C# 7.3。
- 构建：无 `.csproj`，`compile_official.bat` 显式列出源码并直接调用 Roslyn `csc.dll`。
- 命名空间：全 Mod 统一使用 `BossRush`。
- 维护语言：中文。设计文档、需求讨论、回复和提交信息默认中文。

## 2. 进入仓库后的阅读顺序

1. `AGENTS.md`（本文件）。
2. 任务所在目录最近的 `AGENTS.md`，例如 `Integration/AGENTS.md`、`Patches/AGENTS.md`、`Utilities/AGENTS.md`、`ZombieMode/AGENTS.md`、`tests/AGENTS.md`、`docs/AGENTS.md`。
3. 代码审查或修复任务读 `CODE_REVIEW.md`、`CODE_REVIEW_FINDINGS.md`、`FIX_TRACKER.md`。
4. 涉及外部契约读 `docs/contracts.md`。
5. 涉及架构边界读 `docs/架构说明/` 下对应专项文档。
6. 涉及详细架构/子系统/玩法说明，先查 `.qoder/repowiki/` 详细 Wiki 内容库（见 4.13）。
7. 最后读实际代码、构建脚本、guard 脚本。旧设计稿和历史计划只能作为线索，不能替代代码确认。

## 3. 子系统地图

| 路径 | 职责 |
| --- | --- |
| `ModBehaviour.cs`、`ModConfigApi.cs` | 主入口、全局状态、配置 API |
| `Integration/` | 物品、装备、NPC、商店、Wiki、好感度、婚姻、重铸、新武器、死亡亡魂等集成总线 |
| `WavesArena/` | 标准 BossRush 与无间炼狱波次逻辑 |
| `ModeD/`、`ModeE/`、`ModeF/` | 白手起家、划地为营、血猎追击 |
| `Campaign/` | 鸭王征程：剧情契约战役，六章串联现有模式；对模式代码零重构，只经全局采集器与 4 处 notify 漏斗挂钩 |
| `Integration/BackMountain/` | 竞技场后山：菜地（复用官方种植系统）、战利品登记簿、点唱机战歌；由战役章节 token 解锁 |
| `ZombieMode/` | 末日丧尸模式，独立生命周期和奖励系统 |
| `Common/` | 共享特效、装备能力、地图配置、统计 modifier、通用模型、共享 UI 库（`Common/UI/BossRushUI.cs`） |
| `Utilities/` | 跨模块运行时 hooks、刷怪核心、场景门控、缓存、敌人恢复 |
| `Patches/` | Harmony 补丁分组 |
| `Config/` | 运行时配置、NPC 刷新点、黑名单等 |
| `Localization/` | 本地化注入与文本辅助 |
| `LootAndRewards/` | 掉落、奖励箱、扫箱令、胜利奖励 |
| `Achievement/`、`Audio/`、`BossFilter/`、`Interactables/`、`MapSelection/`、`UIAndSigns/`、`WikiContent/` | 各自独立子系统 |
| `Assets/` | JSON 数据、图片、AssetBundle 等运行时资源 |
| `tests/` | Python 静态守卫脚本，不是 C# 单元测试 |
| `docs/` | 本地设计、迁移、契约和历史资料，默认 local-only |
| `wiki-site/` | VitePress 在线 Wiki 站点 |
| `.qoder/repowiki/` | 详细 Wiki 内容库：`knowledge/zh/` 模块级知识卡 + `zh/content/` 主题级详解，随代码同步维护（见 4.13） |
| `鸭科夫源码/` | 官方 `Assembly-CSharp.dll` 反编译源码，对照用，非 Mod 源码 |

## 4. Golden Rules

### 4.1 新增 `.cs` 必须登记编译清单

`compile_official.bat` 显式列出全部 `.cs` 文件，无通配符。新增 `.cs` 文件后必须手动加入对应段落，否则源码存在但不会编译进 `Build/BossRush.dll`，且不会报错。

清单与磁盘的一致性由 `tests/OfficialCompileListFileExistenceGuard.py` 双向守卫（清单里的文件必须存在 + 仓库里的生产 `.cs` 必须都在清单里），**不要在本文写死文件数**——历史上这个数字漂移过（曾写 483，实际已 532）。要看当前数量跑：

```bash
python tools/run_guards.py --filter OfficialCompileList
```

自检：

```bash
grep -n "你的新文件.cs" compile_official.bat
```

### 4.2 只能在 Windows 环境真正编译

编译依赖 Windows .NET SDK、`Duckov_Data\Managed` 游戏 DLL 和本机 Workshop/Harmony 路径。WSL/Linux 只能做阅读、grep 和 Python guard，不能据此声称“已编译通过”或“运行时已验证”。

Windows/WSL 调用示例：

```cmd
cmd.exe /c "cd /d D:\...\BossRushMod && compile_official.bat"
```

### 4.3 TypeID 严格递增、不复用

自定义物品/装备 TypeID 使用 5000xx 区间，严格递增，不回填已删 ID。TypeID 会进入存档键、掉落表、Wiki、调试流程，复用属于存档兼容风险。

- 当前登记范围：`500001-500067`。
- 已知空缺：`500009`、`500047` 仍视为保留空洞，不回填。
- 下一可用：`500068`，以 `docs/Bossrush使用物品ID表.md` 实际末尾为准。
- Boss/NPC/建筑字符串 ID 不占此序列。

### 4.4 `DisplayNameRaw` 必须配本地化注入

凡设置 `DisplayNameRaw = "BossRush_<Name>"` 的物品/装备，必须在对应 Config 的 `InjectLocalization()` 注入该 key，并挂进 `InjectLocalization_Extra_Integration()`。否则游戏内会显示 `*BossRush_<Name>*`。

自检：

```bash
grep -rn 'DisplayNameRaw = "BossRush_' Integration/
```

### 4.5 刷怪敌对性安全网不得移除

官方 preset 队伍可能是中立，Boss 生成后若不是玩家敌对必须走 `SetTeam(Teams.wolf)` 安全网，避免不攻击、不可击杀、卡波次。标准路径的 load-bearing 位置见 `ModBehaviour.cs` 附近的敌对性修正逻辑；Mode E/F 有独立阵营体系。

### 4.6 事件订阅必须幂等并退订

静态/全局事件订阅必须有私有布尔或同等 owner 状态防重复订阅，并在 `OnDestroy_*`、`ShutdownRuntime()`、`Cleanup*()` 或对象销毁路径退订。死亡触发的变异词条走 `MutatorContext.EnemyKilledCallbacks`，不要直接订阅死亡事件。详见 `docs/架构说明/事件订阅生命周期约定.md`。

### 4.7 防御式 `try/catch` 是宿主防崩策略

本项目有大量防御式 `catch`，其中许多是有意空吞，用于避免 Mod 异常拖崩宿主游戏。本次迁移的文本扫描显示代码中 `catch` 关键字数量很高、单行空 catch 约 800 处；不要成批清理，也不要把“空 catch 存在”本身当作 bug。关键初始化、存档、绑定、刷怪路径可以补 `DevLog`/`Debug.LogWarning`，每帧热路径不要加噪声日志。

### 4.8 Config 三层归位

遵循 `docs/架构说明/Config归位约定.md`：

1. 运行时可调参数：`Config/Config.cs` + `ModConfigApi`。
2. 玩法强耦合常量：`Integration/{Module}/XxxConfig.cs` 或对应模块配置类。
3. 大型数据表：`Assets/Data/*.json` 或 `Assets/{SubDir}/*.json` + Registry + guard + 硬编码 fallback。

### 4.9 Hooks 分层

遵循 `docs/架构说明/Hooks分层约定.md`：单模块 hook 留模块目录；跨模块/全局基础设施 hook 放 `Utilities/`。不要因“未来可能复用”提前提升到全局层。

### 4.10 Python guard 同步

`tests/` 下有数百个 `.py` 脚本（准确数量以 `python tools/run_guards.py` 的输出为准，不要在文档里写死），其中大多数是 `*Guard.py`。它们是结构不变式守卫，不是功能测试。改动被 guard 断言的结构时，必须同步 guard；不要通过放宽 guard 来掩盖行为变化。

### 4.11 变异词条系统不变式

- `enableMutators` 默认 true。
- 共享变异系统不包含 loot 类别行为；不要重新引入 `LootChange` 或 loot 质量/数量/类型消费。
- ZombieMode 不接入共享变异 roll，它有独立局内奖励系统。

### 4.12 重运行时工作按实际使用状态门控

- 装备专属的预热、扫描、轮询、对象池扩容或资源准备，默认只在主玩家实际手持、穿戴或启用对应装备时启动；NPC、仓库物品、背包内未使用物品和未激活系统不得触发同类重工作。
- 优先复用现有装备/手持变化事件做幂等 owner 门控，离手、卸下、停用、死亡、切图和 runtime cleanup 时应立即取消尚未完成的任务并清理 owner 状态。
- 必须预热时按帧分摊并设置明确完成目标；通常应在约 1 秒内完成，同时限制单帧时间/步骤预算。首次立即使用的兜底不能通过削减特效、伤害或玩法行为来换性能。

### 4.13 代码变更必须同步 `.qoder/repowiki/`

`.qoder/repowiki/` 是仓库的详细 Wiki 内容库，维护入口见 [`.qoder/repowiki/README.md`](.qoder/repowiki/README.md)：

- `knowledge/zh/`：按模块组织的知识卡（模块边界、职责、架构约定），`_index.yaml` 是模块索引。
- `zh/content/`：主题级详解文档（项目概览、架构设计、游戏模式、自定义 Boss、装备与物品、NPC 关系、本地化、调试工具等），每篇带引用文件与章节来源。
- `zh/meta/`：repowiki 元数据。

凡修改被这些文档描述的行为、架构、配置、Boss/NPC/装备/物品内容、流程或约束时，必须同步更新对应知识卡与内容文档（新增、改写或标注），保持 repowiki 与代码基线一致；新增子系统或大功能时补充对应条目。这属于变更的一部分，repowiki 内容过时视为未完成变更。


### 4.14 UI 走共享库，不再各写各的

新建或改动界面时：

- Canvas 的 `sortingOrder` 一律引用 `BossRushUILayers` 常量，不写魔法数字。历史上这些值分成 10~1001 与 28000~32000 两个孤岛，跨模式叠加时谁压谁靠运气。
- 颜色用 `BossRushUIColors` 的设计 token，尤其遮罩必须用 `Backdrop`，不要再引入第二套 `(0,0,0,0.7)`。
- 面板/按钮/卡片底图走 `BossRushUI.ApplyPanelSkin`，当前是运行时程序化圆角九宫格，将来换美术图集时通过 `BossRushUISkin` 注入，调用方零改动（规格见 `docs/制作教程/BossRushUI_图集规格.md`）。
- 字体一律 `BossRushUI.ApplyGameFont` / `ZombieModeUIHelper.GetGameFont()`，**不要用 `Resources.GetBuiltinResource<Font>("Arial.ttf")`**——内置 Arial 渲染不了中文。新建文本用 TMP，不要用 legacy `UI.Text`。
- `CanvasScaler` 必须调 `ZombieModeUIHelper.ConfigureCanvasScaler`；只 `AddComponent` 不配置会退化成 `ConstantPixelSize`，高分屏上面板会缩成一小块。
- 能直接复用官方 prefab（`GameplayDataSettings.UIPrefabs.*`、克隆 `MapSelectionEntry` 等）的地方优先复用官方，不要用共享库重造。

由 `tests/BossRushUISharedLibraryGuard.py` 守卫。

## 5. 不可破坏契约

详细契约见 `docs/contracts.md`。本节列出进入代码前必须先识别的兼容面：

- TypeID、存档 key、`SavesSystem` key、配置 key。
- `StreamingAssets/BossRushModConfig.txt` JSON 配置格式。
- `Assets/SpawnPoints/*.json` 地图刷新点格式与硬编码 fallback。
- `WikiContent/catalog.tsv` 与 Wiki markdown 内容索引。
- 本地化 key，尤其 `BossRush_*` raw key。
- AssetBundle 文件名、Prefab base name、EquipmentFactory/ItemFactory 命名规则。
- Harmony 目标、`AccessTools` 字段、字符串反射绑定。当前代码约 31 个 `[HarmonyPatch(typeof(...))]`，并有大量 `GetField/GetMethod/GetProperty` 动态绑定，官方更新后需按 `docs/架构说明/Harmony补丁契约稳定性.md` 复查。
- 地图 `sceneName` / `sceneID`、场景传送坐标、NPC/建筑字符串 ID。
- Python guard 断言的结构约束。

## 6. 兼容性分类

任何变更说明、审查 finding、修复记录都要标注以下分类之一或多项：

| 分类 | 含义 | 例子 |
| --- | --- | --- |
| `SAFE` | 纯文档、注释、无行为代码整理，或静态证明无运行时变化 | AI 入口转发、文档索引 |
| `COMPAT` | 向后兼容的功能/数据扩展 | 新增可选配置且默认保持旧行为 |
| `SCHEMA+` | 文件/配置/存档 schema 向后兼容扩展 | JSON 增加可选字段 |
| `SCHEMA-` | schema 删除/重命名/语义改变 | 配置 key 改名、字段必填化 |
| `WIRE+` | 与外部服务/游戏 API 的兼容扩展 | Feishu 请求增加可选字段 |
| `WIRE-` | 外部 API/协议/反射目标破坏性改变 | Harmony 目标换方法、请求字段改名 |
| `BREAKING` | 会破坏存档、玩家现有配置、旧资源或旧工作流 | TypeID 复用、删除旧 key |
| `OPERATIONAL` | 部署、构建、路径、密钥、人工流程变化 | 改构建脚本路径、改发布流程 |

`SCHEMA-`、`WIRE-`、`BREAKING`、高风险 `OPERATIONAL` 必须先拿 owner 明确确认。

## 7. AI 修改代码前的硬要求

- 必须读实际代码、调用点、构建脚本和相关 guard 后再判断问题。
- seeded lead、旧审查线索、日志片段、用户猜测都是“未验证线索”，不是 confirmed bug。
- 不修没有验证过的问题；若只能静态推断，必须写明“未运行验证/需人工复测”。
- 不做与任务无关的重构、格式化、命名清洗或大规模移动。
- 不把产品/数值/安全/数据迁移决策擅自定案；不确定就写 `Needs owner confirmation`。
- 新增 `.cs` 后先查 `compile_official.bat`。
- 触及 TypeID、本地化、存档、配置、事件、Harmony/反射、刷怪、模式状态机时，必须读对应专项文档。

## 8. 修改后的验证要求

代码改动默认验证顺序：

1. Windows 编译：`compile_official.bat`。
2. Python guard：相关 guard，必要时 `python3 tests/*.py` 或 Windows 等价循环。
3. repowiki 同步：改动涉及的行为、架构、配置或内容是否已同步到 `.qoder/repowiki/`（见 4.13）。
4. Lint/格式检查：本仓库当前没有统一 C# lint；若任务触及的子系统另有 lint、站点构建或格式脚本，按该子系统要求运行。
5. 需要部署时：`test_bossrush_official.bat` 或相关 smoke bat。
6. 游戏内人工 smoke：运行时行为、UI、本地化、刷怪、事件泄漏、过图性能、Harmony/反射只能靠实机确认。

文档-only 改动至少做静态检查：确认链接路径存在、旧入口能转发、新旧规则冲突已记录。文档-only 通常不需要编译，但若改动了脚本、guard、资源路径或 README 中命令，必须按影响范围验证。

无法验证时必须在回复、`FIX_TRACKER.md` 或 migration 中明确说明原因。

## 9. Review、Findings 与 Fix Tracker

- 代码审查方法：`CODE_REVIEW.md`。
- confirmed finding 库：`CODE_REVIEW_FINDINGS.md`。
- 修复流水账：`FIX_TRACKER.md`。
- 旧路径 `docs/代码审查/CODE_REVIEW.md`、`docs/代码审查/CODE_REVIEW_FINDINGS.md`、`docs/协作/FIX_TRACKER.md` 仅做兼容转发。

规则：

- findings 只记录已确认问题。未证实线索放 UNVERIFIED/Seeded Leads，不得当 bug。
- 修复 bug、回归、兼容问题后更新 `FIX_TRACKER.md`。
- confirmed finding fixed 后回填状态、验证方式、commit（若有）。
- accepted/refuted/deferred/documented 都要写理由，避免重复排查。

## 10. Off-limits without sign-off

以下事项没有 owner 明确确认不得执行：

- 生产/玩家数据删除、迁移、批量重写。
- 存档 schema、配置 schema、协议、文件格式破坏性变更。
- 公开 API 或跨模块契约 breaking change。
- 认证/权限模型、密钥、证书、飞书/外部服务配置。
- 部署流水线、Workshop 发布、构建脚本全局改造。
- 计费/支付/经济数值的大幅改变。
- TypeID 复用、删除、回填。
- 游戏模式状态机大规模重构。
- Harmony/反射绑定策略整体替换。
- 大规模目录迁移、批量格式化、批量清理 catch。

## 11. 旧目录和废弃系统说明

- `鸭科夫源码/` 是官方反编译源码，对照用，不是 Mod 源码，grep 时注意排除或明确用途。
- `Build/`、DLL、部署产物不应加入索引。
- `docs/superpowers/`、`.kiro/specs/`、`.claude/plans/` 是历史计划/工具过程材料，不是当前规则源。
- `.cunzhi-memory/`、`.claude/settings*.json` 是工具私有记忆或本机权限配置，不是 canonical；其中可能含个人偏好或本机路径。
- `.qoder/better-harness/`、`.qoder/better-harness-runs/` 是工具过程材料，不是规则源；`.qoder/repowiki/` 是 active 的详细 Wiki 内容库，按 4.13 随代码同步维护，两者地位不同。
- `docs/飞书应用密钥.md` 可能包含敏感信息，只能作为本地部署资料，不要复制到回答、commit、PR 或迁移正文。
- `Injection/` 是旧注入逻辑占位，实际逻辑多已并入 `ModBehaviour` / `Integration`，不要按目录名推断当前架构。
- `skills/`、`codex-skills/` 是辅助工作流。若 skill 与本文件冲突，以本文件为准，并在 `docs/ai-docs-migration.md` 记录。

## 12. Commit & PR

- 仅在用户明确要求时 `git commit`、`git push` 或建 PR。
- 提交信息使用简短中文摘要，例如 `修复售货机 UI 崩溃`。
- 禁止英文 conventional-commit 作为本仓库提交格式。
- 提交前确认：新增 `.cs` 已进 `compile_official.bat`、TypeID 已登记、不要加入 `Build/`、DLL、密钥、个人工作区文件。

## 13. 本地文档约定

`docs/` 默认 local-only，不要主动 `git add -f docs/...`。本次 AI 文档收敛新增的 `docs/contracts.md`、`docs/ai-docs-migration.md` 也是本地协作资料，是否纳入版本控制由 owner 决定。

根级 `CODE_REVIEW.md`、`CODE_REVIEW_FINDINGS.md`、`FIX_TRACKER.md` 是当前 AI 协作流程入口；旧 `docs/` 路径保留转发，避免老工具失联。

## 14. 最后更新

2026-09-01（F3 完整玩法验收：三个产品 bug + 一次性测完改造）：由实机验收报告反查出的三个真 bug，
**共性是「静默失败」——编译绿、guard 绿、日志里最多一行 warning，但功能实际不工作**。

- **官方距离休眠会关掉 Mod 刷出的怪**：`CreateCharacterAsync` 传 `relatedScene != -1` 且 preset 的
  `setActiveByPlayerDistance` 默认 true 时，角色进官方 `SetActiveByPlayerDistance`，
  该组件每帧 `SetActive(距玩家 < 100m)`。玩家跑远后怪被静默关掉但 `IsDead` 仍 false，
  波次永远不结算。新增 `Utilities/SpawnedEnemyActivationHelper.cs`，
  在共享生成核心两个激活点 + 三个自管激活的托管 Boss 处统一解除；
  Mode G 冻结分支与原版 spawner 角色**不适用**。Mode E 早就单独修过这件事，可对照。
- **角色没有 `MoveSpeed` 这个 stat**：官方移动只读 `WalkSpeed` / `RunSpeed` / `Moveability`
  （`CharacterMainControl` 的三个 hash 字段），`"MoveSpeed"` 只是 Animator 参数名。
  给它挂 Modifier 会被 `RuntimeStatModifierTracker` 当缺失 stat 静默丢弃。写移动相关 Modifier 前先查这三个 key。
- **手工 `MoveNext()` 驱动协程必须透传 `Current`**：`yield return` 出来的子 `IEnumerator`
  要靠调用方递归驱动，丢掉 `Current` 等于子协程一次都不跑。Mode H 生产认证踩此坑
  导致模式完全无法开局。正确写法见 `ModeHRuntimeModule_MatchFlow.DriveMatchSpawning`。
- **F3 验收改为按用例隔离**：单个红项不再 `fatalAbort` 整套（旧版第 4/5 阶段一个都跑不到）。
  七阶段、超时 2700s、`TIMEOUT`/`CANCELLED`/`ABORTED_DIRTY` 三态分离、`SUMMARY` 附 `failed_ids`。
  用例按主题拆进 `DebugAndTools/F3GameplayValidation{Stages,Modes,BackMountain,Economy,Depth,Leaks}.cs`。
  **无 code-drivable 入口的项（Mode E 撤离、Mode F 赏金与撤离、标准胜利奖励）如实记 `SKIP`
  并标注「需人工」，不伪造 PASS，也不为凑绿给产品代码开测试后门。**
- **`catch` 子句体内不能 `yield return`（CS1631）**：`yield break` 可以（不产生值），
  `yield return` 不行。正确写法是 catch 里只置标志/记账，把 `yield return` 移到 catch 之外
  （参照 `RunIsolatedCase` 的 `needsReclaim`）。由 `IteratorYieldInCatchGuard` 守卫。
- **`verify_syntax.bat` 有结构性盲区，不能替代真编译**：本仓库缺 `Duckov_Data\Managed`，
  几乎每个文件都有未解析类型（CS0246）。Roslyn 在绑定阶段解析不出类型就不进入
  **迭代器方法体分析**，于是 CS16xx 这一类错误**根本不会被产出**——探针会报 PASS，
  实机编译直接失败（本轮已实测双向验证）。`--with-bcl` 现已默认开启，能多抓一层 BCL 用法错误，
  但抓不到这类。结论：探针 PASS 只代表词法/语法层没问题，**必须按 4.2 在 Windows 上真编译**。
  另注：csc 在中文 Windows 下输出 GBK，用 grep 解析它的输出会静默失配，别拿空结果当「无错误」。
- **JsonUtility 反序列化 DTO 的 CS0649 是误报**：字段由反射赋值，改成属性或加初始值会让
  JsonUtility 绑不上（它只认公有字段）。定点 `#pragma warning disable/restore 0649` 包住 DTO，
  并写明原因（见 `Audio/BossBgmCoordinator.cs`）。
- 新增守卫：`ModeHCertificationCoroutineDriveGuard`、`IteratorYieldInCatchGuard`；
  `ModeFBloodfireOverloadGuard` 与 `GameplayValidationRunnerGuard` 已同步。
  明细见 `FIX_TRACKER.md` 同日条目。

2026-09-01（可达性修复 + Mode H 真实押品接线）：本轮审核发现的缺陷**全部是「实现完整但入口没接线」**，
508 个 guard 与编译都查不出——guard 断言结构不变式，不验证「玩家操作能否走到功能」；
未被调用的 `internal` 方法编译完全合法。

- **词缀锻造**曾 100% 不可达：`GoblinAffixForgeInteractable` 从未被 `AddSubInteractable`
  （`GoblinReforgeInteractable.EnsureGroupedInteractionOptions` 挂了 6 个子交互独缺它），
  而唯一开 UI 的 `ReforgeUIManager.OpenAffixForgeUI` 只被这个组件调用。已挂上 `AffixForgeOption`。
- **鸭皇图鉴**曾 100% 不可达：`TryInjectCodexBookIntoShop` / `InjectCodexBookIntoShops` 零调用点。
  已分别接进 `TryInjectAllBossRushItemsIntoShop` 与 `IntegrationDeferredBootstrap`。
- **词缀熔石**曾无任何产出：补哥布林商店（好感 2 级、库存 5）+ Boss 掉落 8%
  （`AffixForgeStoneDropService`，形态照 `PetNestDropService`）。熔石带 `Special` tag。
  **2026-09-04 更正**：先前这里写「Special 一律不进星愿许愿台奖池」过于绝对。实际是
  底池按 tag **排除** Special（`WishFountainRewardPoolBuild.cs:869`），但 `gift` 与 `healing`
  两个类别把 `Special` 列进了 **requireTags**（`:616`、`:617`），带 Special 的物品能从这两类进池。
  日报签到池更宽：`requireTags = null`，**只**过 `LootBlacklistRegistry`（`DailyReportRewards.cs:222`）。
  结论：自定义物品要挡住随机奖池，靠的是**登记掉落黑名单**，不能指望 Special tag。
- **500060 / 500061 补登记进 `BossRushDynamicItemRegistry`**：shell 早已写好但没登记，
  重启后玩家手里的熔石与图鉴书会退化成官方 `FallbackItem`（契约第 6 节）。
- **Mode H 真实押品已接线**（owner 要求）：新增 `ModeHStakeJournalPersistence`（独立 key
  `BossRush_ModeH_StakeJournal_v1`，此前只被风险扫描读、从来没人写）与 `ModeHRealStakeService`。
  **落盘是阶段推进的一部分**（`TryAdvancePhase` 内联写盘、失败整体回滚），
  押品那场走 `LoadoutLocked → StakePrepared → MatchSpawning` 支路。
  单场上限 3 件；风险提示改为仅在真能押时显示，禁用原因分因展示。
  `SCHEMA+`：该 key 必须同时可反序列化为 header 与完整 DTO，
  **今后增删 journal 字段不得改动 header 的 7 个字段名**。
- **`compile_official.bat` 注释全角标点改 ASCII**：`chcp 65001` 下 cmd 会把全角标点后的
  注释尾段当命令执行，每次构建刷 5 行 `is not recognized`（既有问题）。中文正文保留。
- 明细与未验证项见 `FIX_TRACKER.md` 同日条目。当前编译绿、508 guard 全绿；
  **Mode H 押品的 escrow 重建 / 满仓返还 / ManualIntervention 三条路径仍待实机验证**。

2026-08-30（鸭王征程 + 竞技场后山 完整实装）：新增两个联动子系统。

- **鸭王征程**（`Campaign/`）：六章剧情契约战役，1-5 章分别派往 标准/ModeD/ModeE/ModeF/丧尸 完成特殊目标，终章在竞技场打幽灵女巫的强化变体「冠军之影」（数值倍率 + 体型放大 + MaterialPropertyBlock 染色，零新增 3D 资产）。对五个既有模式**零重构**：只经全局 Health 采集器、`partial ModBehaviour` 状态桥轮询、以及 4 处一行 notify 漏斗挂钩。基地公告板建筑接取/交付契约，线索走官方 NoteIndex 图鉴，交付剧情复用现有 `DialogueManager`（官方对话 UI 原生带立绘位）。
- **竞技场后山**（`Integration/BackMountain/`）：三设施由战役章节 token 解锁。菜地复用官方 `CropDatabase`（作物外观即产出物品的 ItemGraphic，故不需要植物模型）；战利品展示柜是**登记簿而非储物柜**（登记不收走物品，避免玩家在「留着传说武器」和「换几点属性」之间被迫二选一）；点唱机追加 mod 战歌。出击餐因官方 Buff 不跨场景，走「食用登记 → 下一局挂 Modifier」。
- **横切**：`Audio/BossBgmCoordinator.cs` Boss 战 BGM 与 stinger（曲目表驱动，零素材时行为与从前完全一致）；`Common/UI/BossRushUISkinLoader.cs` UI 图集换皮（fail-open，缺 bundle 回退程序化皮肤）。
- 开关：`campaignEnabled`、`backMountainEnabled` 已按「内容系统恒开」口径转为**默认内容**（默认 true、不进 ModConfig UI、由 `ForceContentSystemSwitchesOn` 抹平老档 false）；`BossRush_BackMountainUnlockAll` 是**旋钮**不是内容开关，照常注册进 UI，默认 false。字段与 dormant 契约全部保留。
- **术语**：战役名统一为「鸭**王**征程」，与 ModeH 的「黑市鸭**王**杯」同名——剧情讲的就是那场赛事名人堂里的事，异名会让玩家以为是两个系统。（「鸭皇图鉴」是 Boss 图鉴，另一个域，不冲突。）
- **剧情锚在 ModeH 的真实机制上**：名人堂 32 席、第 33 个进来最底下那个被挤掉。冠军不是被谁抹掉的，是排队排出去的；他之后每件事都在找一个不会被挤掉的名字，最后找到了——Boss 图鉴不挤人，代价是不再当选手。不新造世界观设定，不承诺 ModeH 未实现的机制（名人堂只读、不可招募）。
- 美术资产已产出并部署：`Assets/ui/campaign_presentation`（2 立绘 + 6 章节海报，bundle 1.28 MB）、`Assets/ui/Campaign/*.png`（开发期 raw fallback）、两个建筑图标、六个物品图标。Unity 构建器 `Assets/Editor/CampaignPresentationBundleBuilder.cs`。
- TypeID 台账更新至 500067（500062-500064 菜地种子、500065-500067 出击餐，下一可用 500068）。
- `IsHandledModConfigOptionKey` 因 `Config/Config.cs` 触及 1200 行预算，已原样提取至 `Config/ConfigModConfigKeys.cs`（同一 partial 类，行为逐字不变），`ModConfigOptionChangeGuard` 与 `ModeHConfigApiGuard` 已同步两处查找。
- 新增守卫：`CampaignSkeletonGuard`、`BackMountainStructureGuard`、`BossBgmCoordinatorGuard`、`BossRushUISkinLoaderGuard`；`ModConfigOptionChangeGuard` 的 `CONTENT_SYSTEM_SWITCHES` 已收录两个新开关。
- `.qoder/repowiki/` 已补两张知识卡并登记进 `_index.yaml`（`campaign`、`back_mountain`）。
- **UI 图集换皮已完成**：6 张九宫格底图程序化生成（灰度+alpha、描边与微渐变烘进图里），
  `Assets/ui/bossrush_ui_skin`（8.8 KB）。九宫格 border **由构建器用代码设定并回读校验**
  （`Assets/Editor/BossRushUISkinBundleBuilder.cs`）——border 漏设不会报错，运行时表现是
  面板拉伸变形，所以不能靠手工在 Sprite Editor 里点。
- **BGM 已完成**：龙裔与女巫的循环曲、两条 stinger（`Assets/Sounds/BGM/*.wav`，32kHz 单声道，
  RMS 约 -20 dBFS）。**龙王刻意不进曲目表**，维持作者已有的 `dragonking.mp3` 路径。
  曲目是程序化合成的氛围乐（本机网关只有文本与图像模型，无音频能力），换正式曲目只需
  替换同名文件；保持 -20 dBFS 左右，否则会盖过枪声。
- **实机 smoke（已做，2026-08-30）**：dev 构建 + Steam 启动到主菜单，Player.log 证据——
  征程与后山两个运行时模块均已启动；**UI 皮肤注入成功**（`panel=True button=True`，
  证明图集→bundle→loader→注入整条链通）；ModConfig 只出现调试旋钮、两个内容开关正确隐藏；
  Harmony 仍是 53 个补丁类生效。唯一报错（3 个 `Cleanup` 方法 patch 失败）在 8/29 的旧日志里
  一字不差地存在，属先前既有问题。
- **仍待人工验证**：基地场景内的路径（官方 Garden 三连、公告板/展示柜建筑注入、线索进笔记图鉴、
  出击餐生效与清理）需要加载存档进基地，无法由脚本驱动。验证前先用
  `set BOSSRUSH_DEV_BUILD=1 && compile_official.bat` 出 dev 包，否则 `DevLog` 被
  `[Conditional("BOSSRUSH_DEV")]` 整个剥离，日志里什么都看不到。
- **已全面审核（2026-08-30）**：查出并修掉 5 个真问题——空 catch 预算回归、
  幽影蘑菇「描述说减伤、实现给生命上限」、出击餐遇陌生 ID 被静默吃掉、
  第一章无伤目标在 Boss 池被筛小时会卡死、终章门禁漏了 ModeG/ModeH。
  明细见 `FIX_TRACKER.md` 同日条目。当前编译绿、503 guard 绿（1 既有红项）。
- 完整方案见 `.claude/plans/mod-unity-starry-neumann.md`。

2026-08-30：新增三个子系统——鸭皇图鉴（`Integration/Codex/`）、局内随机事件「鸭生无常」（`RandomEvents/`）、词缀锻造（`Integration/AffixForge/`）；成就分类枚举追加 `Codex`；重铸 `IsRuntimeTrackingVariableKey` 增加 `AFX_` 前缀互斥；TypeID 台账更新至 500061（500060 词缀熔石、500061 鸭皇图鉴，下一可用 500062）。

2026-08-28：新增遗种巢（PetNest）养崽子系统，目录 `PetNest/`；TypeID 台账更新至 500059（遗种蛋，下一可用 500060）。

2026-08-27：新增共享 UI 库 `Common/UI/BossRushUI.cs` 并登记 UI 约定（4.14）；TypeID 台账更新至 500058（下一可用 500059）。

2026-08-12：登记 `.qoder/repowiki/` 为仓库详细 Wiki 内容库并纳入同步维护（4.13）。

2026-07-01：完成兼容式 AI 协作文档收敛，迁移记录见 `docs/ai-docs-migration.md`。
