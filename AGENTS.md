# AGENTS.md — 仓库级唯一事实来源

> 本文件是所有 AI 协作者（Claude / Gemini / Copilot / Cursor 等）进入本仓库后必读的第一份文件，承载**不可违背的硬约束**。
> `CLAUDE.md`、`GEMINI.md`、`.github/copilot-instructions.md`、`.cursor/rules/agents.mdc` 均为轻量入口，统一指向本文件——**实质约定只在此维护，避免多处分叉**。
>
> 全文事实均来自 2026-07-01 对仓库现状的实测（编译脚本、grep、文件核对）。凡带具体数字/路径处，均为核对结果，非估算。

---

## 1. 项目概况

- **游戏**：鸭科夫（Escape from Duckov）—— Unity 游戏
- **Mod**：BossRush —— 以 Boss 挑战竞技场为核心的大型内容包（多模式、自定义 Boss/装备/NPC、成就、Wiki 等）
- **语言**：C# 7.3，**无 `.csproj`**，由 batch 脚本直接调 Roslyn `csc.dll` 编译
- **命名空间**：`BossRush`（全 Mod 唯一根命名空间）
- **维护者语言**：中文——设计文档、需求、回复、提交信息一律中文

---

## 2. 硬约束（Golden Rules，禁止静默改坏）

### 2.1 构建即门禁：新增 `.cs` 必须手动登记

`compile_official.bat` **显式列出每个 `.cs` 文件（483 条，无任何通配符）**。新增 `.cs` 文件后**必须**手动把它加入该 bat 对应段落，否则编译器根本看不到它——**静默不编译进 DLL，不报错，功能残缺**。历史上多次因此导致功能 shipping 时缺失。

**自检**：`grep -n "你的新文件.cs" compile_official.bat` 必须命中。

### 2.2 仅 Windows 可编译，WSL/Linux 不能

编译需 `Duckov_Data\Managed` 下的游戏 DLL + Windows .NET SDK 的 Roslyn 编译器。**WSL/Linux 环境无编译器**。

- ✅ 正确：从 Windows 侧运行，或 WSL 内 `cmd.exe /c "... && compile_official.bat"`
- ❌ **禁止仅凭 Linux 侧的阅读/推断就断言"已验证/已编译通过"**。Linux 侧只能做静态阅读与 `python3 tests/*.py` 守卫。

### 2.3 TypeID 严格递增、不复用

自定义物品/装备 TypeID 在 **5000xx 区间**严格递增分配，**不得复用或回填已删 ID**（会破坏存档键与掉落引用）。Shipping 前必须登记 `docs/Bossrush使用物品ID表.md`。

- **当前已分配**：`500001–500056`（其中 **500009、500047 空缺未用**）
- **下一可用**：`500057`（以 `docs/Bossrush使用物品ID表.md` 实际末尾为准）
- **例外**：Boss/NPC/建筑用字符串 ID（如 `wedding_chapel`），不占此序列

### 2.4 本地化陷阱：`DisplayNameRaw` 必须配套注入

设了 `DisplayNameRaw = "BossRush_<Name>"` 的物品/装备，**必须**在其 Config 的 `InjectLocalization()` 注入该 key，并挂进 `InjectLocalization_Extra_Integration()`（定义于 `Integration/BossRushIntegration.cs`）。否则游戏内渲染成 `*BossRush_<Name>*`（带星号的未解析原始键）。此坑历史反复出现（能量盾、雷戒、霜冠、雷霆战甲等首发均踩过）。

**自检**：`grep -rn 'DisplayNameRaw = "BossRush_' Integration/`，逐一确认对应 key 有 `InjectLocalization` 注入。

### 2.5 刷怪敌对性安全网：`SetTeam(Teams.wolf)`

官方 preset 的队伍字段可能是中立（如 `Teams.middle`），导致 Boss 不主动攻击、无法击杀、卡波次。安全网在 **`ModBehaviour.cs:1106-1112`**：检测到非敌对 Boss 时强制 `character.SetTeam(Teams.wolf)`。这是 load-bearing，**不得移除**。Mode E/F 走独立阵营体系（`ModeE/ModeE.cs` 的阵营映射），不受此路径影响。

### 2.6 事件订阅生命周期：幂等 + 退订

静态/全局事件订阅（`Health`/`Character` 死亡事件、套装事件、玩家生命周期）**必须**：
1. 用 `_subscribed` 布尔守护，防重复订阅
2. 在对应 `OnDestroy_*` / cleanup 路径显式退订

死亡触发的变异词条走 `MutatorContext.EnemyKilledCallbacks` 注册，**不直接订阅**死亡事件。历史教训：雷戒爆炸事件递归订阅曾导致 `StackOverflowException` 秒杀玩家；跨局未退订导致重复回调。

### 2.7 防御式 try/catch 是有意设计

代码库中 **807 个 catch 块（其中 756 个为纯空 catch）**是**有意的宿主崩溃保险**——Mod 异常必须被吞掉以防崩溃宿主游戏。**不得成批清除**。在 mod/游戏边界新增错误处理时，遵循现有 swallow-and-continue 模式。关键初始化/绑定路径可加 `Debug.LogWarning` 但不要在每帧热路径加日志。

### 2.8 Config 三层归位

遵循 `docs/架构说明/Config归位约定.md`：
1. **运行时可调参数**（玩家/调试）→ `Config/Config.cs` + `ModConfigApi` 注册
2. **玩法耦合常量**（Boss HP、技能伤害、武器属性）→ `Integration/{Module}/XxxConfig.cs`
3. **大型数据表**（坐标、ID 映射）→ `Assets/Data/*.json` + Registry 类（含硬编码 fallback）+ 一致性 Guard

禁止硬编码可调参数，或把配置散布到上述三层之外。

### 2.9 Hooks 分层

遵循 `docs/架构说明/Hooks分层约定.md`：单模块 hook 留在模块目录内（作为 `partial class ModBehaviour` 的 `XxxRuntimeHooks.cs`）；跨模块/全局基础设施 hook（Harmony、缓存、玩家生命周期）→ `Utilities/`。YAGNI：不预先把单模块 hook 提升到 `Utilities/`。

### 2.10 Python 守卫同步

改动被守卫断言的结构（如 `MutatorSemanticsGuard`、`MapSpawnRegistryConsistencyGuard` 等）时**必须同步守卫脚本**。`tests/` 下现有 **358 个 `.py` 守卫脚本**（直接位于 `tests/*.py`，**无 `tests/guards/` 子目录**）。

**验证**：`python3 tests/<相关guard>.py`，应保持全绿。

### 2.11 变异词条系统不变式

- `enableMutators` 默认 **true**
- 系统**禁止包含 loot 类别行为**——所有 `LootChange` 变异、枚举值、loot 质量/数量/类型消费已被故意移除，守卫脚本断言这些禁用符号不再出现
- ZombieMode **不接入**共享变异 roll（有独立局内奖励系统）

详见 `docs/mutator-system.md`（若存在）与守卫脚本。重新引入 loot 变异或挪动 roll 时机会重现 per-wave 重抽与跨模式泄漏。

---

## 3. 产品决策（不能擅自修改）

以下是有意的设计取向，**不得在未与维护者确认的情况下"顺手优化"掉**：

- **中文提交信息**（见 §5）——不是疏忽，是约定
- **807 个防御式 catch**——不是技术债，是崩溃保险（§2.7）
- **变异词条移除 loot 类别**——是有意的设计逆转（§2.11）
- **模式激活用离散 bool 标志**（`modeFActive`、`bossRushArenaActive`、`modeEActive`、`IsZombieModeActive` 等，见 `ModBehaviour.cs:340/493`）——已知有非法多模式并存的隐患，收敛为单一 enum 属于**目标**而非现状，改动前先读 `docs/架构说明/游戏模式状态机设计.md`
- **恢复系统经验常数**（`Utilities/EnemyRecoveryMonitor.cs`）——是调参得到的经验值，非第一性原理值，调整前读 `docs/架构说明/刷怪与恢复系统设计.md`
- **`docs/` 不入 git**（§7）——是有意的本地文档约定

---

## 4. 入口与模块结构

**入口**：根目录 `ModBehaviour.cs`（主入口与全局状态）+ `ModConfigApi.cs` + `TeleportDebugMonitor.cs`，加上 **30+ 个 `Integration/` 下的 `partial class ModBehaviour`** 文件（如 `BossRushIntegration.cs`、`BossRushIntegration_StartAndScene.cs`、`BossRushIntegration_TravelAndSetup.cs`、`IntegrationDeferredBootstrap.cs`）。逻辑分散在这些 partial 中，改动时先 grep 确认目标方法所在文件。

**核心模块**：

| 目录 | 职责 |
|------|------|
| `Integration/` | 动态物品、装备、NPC、商店、Wiki、套装、新武器、死亡亡魂等集成总线 |
| `WavesArena/` | 标准 BossRush / 无间炼狱核心波次逻辑 |
| `ModeD/` `ModeE/` `ModeF/` | 白手起家 / 划地为营 / 血猎追击 各自模式逻辑 |
| `ZombieMode/`（经 `Integration/ZombieModeIntegration.cs` 接入） | 末日丧尸模式 |
| `Utilities/` | 刷怪、缓存、敌人恢复监控等跨模块工具 |
| `Patches/` | Harmony 补丁（按 BaseHub / Combat / Death / Economy 分组，共 12 个文件） |
| `Common/` | 共享特效、装备能力框架、地图配置、通用辅助 |
| `Config/` | 三层配置与运行时数据 |
| `Localization/` | 本地化注入与文本管理 |
| `LootAndRewards/` | 掉落、奖励箱、扫箱令 |
| `Achievement/` `Audio/` `BossFilter/` `Interactables/` `MapSelection/` `UIAndSigns/` `WikiContent/` | 各自子系统 |
| `tests/` | Python 架构守卫脚本（358 个 `.py`） |
| `docs/` | 设计文档与项目说明（**local-only，不入 git**） |
| `wiki-site/` | VitePress 在线 Wiki 站点 |
| `鸭科夫源码/` | 官方 `Assembly-CSharp.dll` 反编译源码（对照用，非 mod 代码；grep 时注意排除） |

---

## 5. 提交流程（Commit & PR）

**提交信息一律为简短中文摘要**，禁止英文/长格式/conventional-commit：

- ✅ `修复售货机 UI 崩溃`、`护士治疗更加全面`、`新增雷霆战甲套装效果`
- ❌ `feat(weapons): add thunder armor set bonus`
- ❌ `fix(vending): resolve itemInstances null reference`

**时机**：仅在用户明确要求时才 `git commit` / `git push` / 建 PR。不确定就先问。

**提交前检查**：新增 `.cs` 已进 `compile_official.bat`（§2.1）；TypeID 已登记（§2.3）；不要把 `Build/`、`.dll`、个人工作区文件加入索引；`docs/` 下文件不入 git（§7）。

---

## 6. 验证方法（本仓库现状）

**本仓库无 C# 单元测试框架**。验证 = 三层：

1. **Windows 编译**：`compile_official.bat` 成功产出 `Build/BossRush.dll`（静态类型/语法/引用检查，C# 7.3）
   - WSL 内：`cmd.exe /c "cd /d D:\...\BossRushMod && compile_official.bat"`
2. **Python 守卫**：`python3 tests/*.py`（358 个，静态断言结构不变式、常量一致性、生命周期契约、scratch 复用等）
3. **游戏内 smoke 测试**（需人工，Windows）：部署 DLL + `Assets/` JSON → 启动 `Duckov.exe` → 手动进竞技场验证波次、武器、套装、UI、过图性能

### 只能阅读验证 / 需人工复测的范围

以下**编译器无法检测**，必须游戏内运行时验证：

- **31 个 Harmony 补丁** + **493 处字符串反射（GetField/GetMethod）** + **9 处 AccessTools** 的运行时绑定——官方更新后签名漂移会静默失效，方法见 `docs/架构说明/Harmony补丁契约稳定性.md`
- 波次生成 / 敌人恢复 / 传送入场 / 武器/套装运行时行为
- 售货机 UI（延迟注入 / `StockShop.GetItemInstanceDirect` 缓存修复）
- 菜单/过图/抬回去帧率（transition-lag 优化效果）
- 本地化实际渲染（是否出现 `*BossRush_*` 占位符）
- 事件泄漏/递归（需多局测试触发）

---

## 7. 本地文档约定（Local Memory）

`docs/` 树**不入 git**，仅本地使用：不要 `git add -f docs/...`；若某 docs 文件已被跟踪，用 `git rm --cached` 移出索引但保留工作区文件。

**根级 tracked 文件**（入 git）：`AGENTS.md`（本文）、`CLAUDE.md`、`GEMINI.md`、`README.md` / `README_EN.md`、`.github/copilot-instructions.md`、`.cursor/rules/agents.mdc`。

**过程文档**（FIX_TRACKER / CODE_REVIEW / CODE_REVIEW_FINDINGS / 专项设计文档）均放 `docs/` 下（local-only）：

- `docs/协作/FIX_TRACKER.md` —— 修复流水账
- `docs/代码审查/CODE_REVIEW.md` —— 审查方法论
- `docs/代码审查/CODE_REVIEW_FINDINGS.md` —— 结构化审查发现
- `docs/架构说明/` —— 专项设计文档（Config 归位、Hooks 分层、游戏模式状态机、刷怪与恢复、Harmony 契约稳定性等）

---

## 8. 高风险改动边界

- **31 个 Harmony 补丁 + 493 反射绑定 + 9 AccessTools**：官方游戏更新的主要断裂面。每次官方更新后按 `docs/架构说明/Harmony补丁契约稳定性.md` 走自检。
- **刷怪与恢复系统**：无编译期/单测覆盖的运行期高危区，读 `docs/架构说明/刷怪与恢复系统设计.md`。
- **模式状态机**：离散 bool 标志允许非法并存，读 `docs/架构说明/游戏模式状态机设计.md`。
- **存档序列化 / TypeID 注册**：错误会损坏玩家存档，属 P0。

---

**最后更新**：2026-07-01
