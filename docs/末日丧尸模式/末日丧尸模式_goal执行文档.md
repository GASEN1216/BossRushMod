# 末日丧尸模式体验对齐 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把末日丧尸模式从“代码里有完整循环”修到“玩家实际进游戏时入口、波次、奖励、撤离、UI 文案和低端机表现都与设计承诺一致”。

**Architecture:** 保持当前 `ZombieMode/*.cs` partial 分层，不重做大架构。优先修真实体验差异：状态机、经济换算、奖励实际效果、波次清理、撤离窗口、物品文案、UI 显示和低端机兜底。

**Tech Stack:** C# partial `ModBehaviour`、Duckov 原生 `MapSelectionView` / `SceneLoader` / `EconomyManager` / `ItemAssetsCollection` / `CountDownArea`、项目内 `SpawnEnemyCore` / `RunScopedRegistry` / `RuntimeStatModifierTracker` / Python 静态守卫脚本。

---

## 执行边界

- 本文档是给后续 Codex `/goal` 使用的执行总控，不是玩家公告。
- 当前工作树已有大量未提交改动，执行时不要回滚不属于本目标的改动。
- `tests/*.py` 是静态文本守卫，不是功能测试；最终完成口径必须包含 Windows 下 `compile_official.bat` 和至少一轮进游戏冒烟。
- 如玩家给出新的实测差异，以实测差异为最高优先级；本文档列的是基于当前代码阅读已经能锁定的高风险落差。
- 不要把旧审查文档里已被守卫覆盖的问题重新当成未修问题。先看当前代码，再看 `tests/README.md` 和对应守卫。

## 当前执行状态（2026-05-03）

- 代码侧 P0/P1 体验落差已按本文档落到 `tests/ZombieModeGoalExperienceGuard.py`，当前 ZombieMode 静态守卫全部通过。
- 已覆盖：波次结束/开波前 combat enemy 清理、减伤 modifier 负向写入、信标文案改为快速开波、继续战斗真正关闭撤离机会、现金语义改为局内资源、空投改为即时补给、开局核心装备失败阻止开始、装备奖励 fallback 净化点、终端命名、UI TMP autosizing，以及运行时 UI 的 `CreateRect` / `CreateText` / `CreateButton` helper 集中复用。
- 仍未完成生产闭环证明：Windows 下 `compile_official.bat` 编译、`test_bossrush_official.bat` 部署，以及进游戏至少打到第 5 波 Boss 后验证撤离成功和继续战斗两条路径。
- 后续 Codex 继续 `/goal` 时，除非代码已漂移或玩家给出新的实测差异，否则不要重做已被 `ZombieModeGoalExperienceGuard.py` 覆盖的问题，优先补编译/现场冒烟证据。

## 当前环境编译阻塞记录（2026-05-03）

- `cmd.exe /c ver`、`powershell.exe -NoProfile ...`、`/mnt/c/Program Files/dotnet/dotnet.exe --info` 均在 WSL interop 阶段失败，错误为 `WSL ... UtilBindVsockAnyPort ... socket failed 1`。
- Linux 侧未找到可用的 `dotnet`、`csc`、`mcs`、`mono`、`msbuild`、`xbuild`。
- D 盘 Unity 安装里能找到 `DotNetSdkRoslyn/csc.dll` 和 `MonoBleedingEdge/lib/mono/4.5/mcs.exe`，但缺可在 WSL Linux 执行的 .NET/Mono runtime；Unity 自带 `MonoBleedingEdge/bin/mono-sgen` 是 Mach-O，可执行时报 `Exec format error`。
- `Build/BossRush.dll` 已存在但时间早于当前改动，不能作为当前工作树已编译证明。
- 在当前 WSL 会话里不要继续重复尝试 Windows 编译；需要切到真实 Windows shell 或修复 WSL interop 后再跑 `compile_official.bat`。

## 已阅读文件地图

入口与交易：
- `Integration/Items/ZombieTideInvitationConfig.cs`
- `Integration/Items/ZombieTideInvitationUsage.cs`
- `Integration/Items/ZombieTideBeaconConfig.cs`
- `Integration/Items/ZombieTideBeaconUsage.cs`
- `ZombieMode/ZombieModeEntry.cs`
- `ZombieMode/ZombieModeMapSelection.cs`
- `ZombieMode/ZombieModeMapSelectionHelper.cs`
- `ZombieMode/ZombieModeCashInvestmentView.cs`

运行主循环：
- `ZombieMode/ZombieModeModels.cs`
- `ZombieMode/ZombieModeInventoryTransfer.cs`
- `ZombieMode/ZombieModeMapIsolation.cs`
- `ZombieMode/ZombieModeSpawner.cs`
- `ZombieMode/ZombieModeWaveController.cs`
- `ZombieMode/ZombieModeEnemyRuntime.cs`
- `ZombieMode/ZombieModePollution.cs`
- `ZombieMode/ZombieModeBossController.cs`
- `ZombieMode/ZombieModeDropsAndPerformance.cs`

奖励、撤离、UI、清理：
- `ZombieMode/ZombieModeRewards.cs`
- `ZombieMode/ZombieModeNpcCatalog.cs`
- `ZombieMode/ZombiePurificationPointController.cs`
- `ZombieMode/ZombieModeSafeZoneController.cs`
- `ZombieMode/ZombieModeExtractionController.cs`
- `ZombieMode/ZombieModeHudController.cs`
- `ZombieMode/ZombieModeUIHelper.cs`
- `ZombieMode/ZombieModeCleanup.cs`
- `ZombieMode/ZombieModeDebug.cs`

外部挂载与验证：
- `Localization/LocalizationInjector.cs`
- `Integration/ZombieModeIntegration.cs`
- `ModBehaviour.cs`
- `Config/Config.cs`
- `Config/LootBlacklistRegistry.cs`
- `compile_official.bat`
- `tests/ZombieMode*.py`
- `tests/README.md`

## 当前代码真实行为

入口：
- `尸潮邀请函` TypeID 是 `500045`，`尸潮信标` TypeID 是 `500046`。
- 使用邀请函后走 `ZombieModeMapSelectionHelper.ShowZombieModeMapSelection()`，把末日丧尸地图条目注入原版 `MapSelectionView`。
- 选图后先打开现金投入弹窗，`ZombieModeTuning.CashToPurificationRatio = 100`，即 `100` 现金换 `1` 初始净化点数，向下取整。
- 确认进图后才扣邀请函和现金；失败在正式开始前通过 `RefundZombieModeInvitationIfNeeded()` / `RefundZombieModeCashIfNeeded()` 回滚。

开局：
- 入图后 `PrepareZombieModeInventoryTransferShell()` 会把玩家身上顶层物品全部转入仓库；任务/绑定物品不阻止入场，仓库格满时走原版仓库收件箱。
- `ApplyZombieModeMapIsolationShell()` 禁用原版 spawner、原版撤离点，并清理原版敌人，保留对话/商人/任务类 NPC。
- `CollectZombieModeSpawnPoints()` 读取地图配置刷怪点、原版 spawner 点；没有刷怪点时在玩家周围生成 16 个虚拟点。
- `GrantZombieModeBeacon()` 给玩家发一个局内 `尸潮信标`，局结束清理所有同 TypeID 的信标。
- 必须选择开局流派后才进入第一轮准备期。近战流派发随机近战、医疗、食物、饮料；枪械流派发随机枪、匹配口径弹药 `2000` 发、补给。

波次：
- 准备期 `30s`；信标读条 `3s` 后立即开下一波；撤离读条 `15s`。
- 普通波击杀目标是 `effectiveSpawnPointCount + (wave - 1) * 5`。
- 每 `5` 波是 Boss 节点。Boss 波会刷 Boss，也会同时刷普通丧尸；Boss 全死即进入结算。
- Boss 数量是 `max(1, 1 + floor(effectiveSpawnPointCount / 10))`，种类按 Titan / Hunter / Splitter / Shielder / Corruptor 轮转。
- 战斗中每 `1s` 追加压力刷怪，低端保护会降低追加数量。

污染与敌人：
- 每个 Boss 节点完成后 `PollutionFromNatural++`；契约奖励会增加 `PollutionFromContracts`。
- 污染越高，特殊/精英概率越高；污染也线性放大敌人血量和伤害。
- 普通丧尸基于 `Cname_Zombie`，通过 `SpawnEnemyCore(...)` 创建，不走 BossRush 掉落跟踪。

奖励与撤离：
- 普通节点给 `3` 个奖励选项，Boss 节点给 `4` 个奖励选项。
- 奖励类别包含属性、装备、经济、NPC 服务、工事、契约、保险、地图事件。
- Boss 节点奖励选完后进入撤离机会窗口。`立即撤离` 会创建/使用撤离点并倒计时；`继续战斗` 当前只关闭 UI。
- 撤离成功时 `SettleZombieModeExtractionCashShell()` 把剩余净化点数按 `1 点 = 1 现金` 加回现金。

清理与性能：
- `RunOnlyObjects` 是局内对象/协程/临时 UI/敌人/撤离点的清理主通道。
- `LivingZombieCount` 达到 `150/250/350` 附近进入观察/软保护/极限保护。
- 极限保护会回收远离玩家、非 Boss、非关键交战中的普通敌人。
- 掉落清理按 `3` 波或 `300s` 过期。

## 必须修到位的体验差异

### P0. 波次完成后旧敌人可能跨波残留

**证据路径：**
- `ZombieMode/ZombieModeWaveController.cs`
- `StartZombieModeWave()` 只调用 `CleanupZombieModePreparationObjects(runId)`，没有清理上一波仍存活的 combat 敌人。
- 普通波按击杀目标完成，压力刷出来的额外敌人不一定被计入完成条件。
- Boss 波 Boss 全死即结算，但该波同时刷出的普通丧尸不参与 Boss 完成条件。

**玩家症状：**
- 奖励 UI、准备期、安全区或下一波开始时仍有上一波敌人追杀。
- Boss 节点后玩家按理应有撤离/商人/护士窗口，但实际仍被旧怪干扰。

**目标行为：**
- 进入 `RewardSelection` 前，当前波仍存活的 ZombieMode 普通/特殊/精英敌人必须被安全处理。
- Boss 本体死亡后的分裂/死亡云可按技能结算，但不得让常规旧怪跨入奖励或下一波。
- 清理不应误杀玩家、临时 NPC、原版保留 NPC、撤离点、安全区、奖励 UI。

**建议实现：**
- 在 `ZombieModeCleanup.cs` 增加 `CleanupZombieModeCombatEnemiesForWaveEnd(int runId, string reason)`。
- 通过 `RunOnlyObjects` 或 `CollectZombieModeRuntimeEnemyMarkers(runId, scratch, includeBoss: false)` 收集当前 run 的非 Boss 敌人。
- 对未 `DeathSettled` 且未 `RecycledForPerformance` 的 marker：标记 `RecycledForPerformance = true`、`UnregisterZombieModeEnemyInstanceId(marker.Owner)`、递减 `LivingZombieCount`、销毁 GameObject、`PruneZombieModeRunOnlyEnemyRecords(runId)`。
- 在 `CompleteZombieModeWave()` 设置 `Settling` 后、展示奖励前调用一次；在 `StartZombieModeWave()` 开头加防御性调用，保证下一波开始前没有旧 combat 敌人。

**静态守卫：**
- 新增或扩展 `tests/ZombieModeReview20260503Guard.py`，检查 `CompleteZombieModeWave` 和 `StartZombieModeWave` 均出现 combat enemy cleanup 调用。

### P0. “伤害减免”属性奖励符号疑似反向

**证据路径：**
- `ZombieMode/ZombieModeRewards.cs`
- `AttributeDamageReduction` 使用 `ElementFactor_Physics`，当前调用 `ApplyZombieModeAttributeReward(..., 0.05f, 0.40f)`。
- `Integration/Config/DragonSetConfig.cs` 注释明确：`ElementFactor_Physics` 是物理伤害倍率，`<1` 减伤，`>1` 增伤；现有龙套物理减伤使用负数 `PercentageAdd`。

**玩家症状：**
- 选择“伤害减免”后可能实际增加承受的物理伤害。

**目标行为：**
- 玩家看到“伤害减免 +5%”时，实际受到的物理伤害应降低 `5%`。
- UI 文案、累计上限、实际 stat modifier 符号一致。

**建议实现：**
- 把 `AttributeDamageReduction` 的运行时 percent 改成负值，例如每层 `-0.05f`。
- 单独处理 cap：显示层仍可显示正向“减伤 5%/上限 40%”，内部累计用正向计数或在添加 modifier 时取负。
- 避免把所有属性奖励统一限制为 `percent > 0f`，否则负向减伤 modifier 会被 `AddZombieModeAttributeModifier()` 早返回拒绝。

**静态守卫：**
- 新增检查：`AttributeDamageReduction` 不得以 `+0.05f` 写入 `ElementFactor_Physics`。
- 新增检查：`AddZombieModeAttributeModifier` 不得用 `percent <= 0f` 阻止合法负向 `PercentageAdd`。

### P0. 尸潮信标物品文案与实际功能冲突

**证据路径：**
- `Integration/Items/ZombieTideBeaconConfig.cs`
- `ZombieTideBeaconConfig.USE_DESC_CN = "使用：准备撤离。"`
- `Integration/Items/ZombieTideBeaconUsage.cs` 调用 `TryUseZombieModeBeacon()`。
- `ZombieMode/ZombieModeExtractionController.cs` 的信标实际是在准备/撤离窗口读条后调用 `StartZombieModeWave(runId)`。

**玩家症状：**
- 玩家以为信标用于撤离，实际会提前开波。

**目标行为：**
- 物品描述、使用提示、HUD 提示、通知文本都统一表达“准备期快速开波”，不要再写“准备撤离”。

**建议实现：**
- `ZombieTideBeaconConfig.USE_DESC_CN` 改为 `使用：准备期快速开始下一波。`
- `ZombieTideBeaconConfig.USE_DESC_EN` 改为 `Use: start the next wave during preparation.`
- 保持 `BossRush_ZombieMode_Hud_BeaconReady`、`BossRush_ZombieMode_Notify_BeaconChannelStarted` 的语义一致。

**静态守卫：**
- `tests/ZombieModeLocalizationGuard.py` 或新守卫检查 `准备撤离` 不再出现在信标使用描述中。

### P0. Boss 节点“继续战斗”按钮没有真正提交选择

**证据路径：**
- `ZombieMode/ZombieModeExtractionController.cs`
- `ContinueZombieModeAfterExtractionOpportunity(int runId)` 当前只调用 `ClearZombieModeExtractionOpportunityUi()`。
- `CombatPhase` 仍为 `ExtractionOpportunity`，`ActiveExtractionArea` 仍保留，HUD 仍可能显示撤离开放。

**玩家症状：**
- 点“继续战斗”后看起来 UI 没了，但系统仍处于撤离窗口；玩家不知道自己是否已经放弃撤离。

**目标行为：**
- 点击“继续战斗”后必须产生明确状态变化和反馈。
- 推荐目标：关闭撤离 UI，销毁/释放 `ActiveExtractionArea`，把 `CombatPhase` 从 `ExtractionOpportunity` 改为 `Preparation`，保留剩余准备倒计时，显示“下一波尸潮即将到来”。
- 信标在新的 `Preparation` 阶段仍可用于提前开波。

**建议实现：**
- 新增 `CloseZombieModeExtractionOpportunityAndContinue(int runId)` 私有 helper，复用 `TryReleaseZombieModeExtractionCountdownUi()`、清理 `ActiveExtractionArea`、清理 UI、切换 phase。
- `ContinueZombieModeAfterExtractionOpportunity()` 调用该 helper，而不是只关 UI。
- `GetZombieModeExtractionHudText()` 保持只在真实撤离窗口显示。

**静态守卫：**
- 检查 `ContinueZombieModeAfterExtractionOpportunity` 中必须有 phase 改写或 helper 调用，不允许只调用 `ClearZombieModeExtractionOpportunityUi()`。

### P1. 现金投入和撤离返现金的经济语义严重混乱

**证据路径：**
- `ZombieMode/ZombieModeCashInvestmentView.cs`
- 入场：`100` 现金换 `1` 净化点数。
- `Localization/LocalizationInjector.cs` 文案写“撤离时按 1:1 转回现金”。
- `ZombieMode/ZombieModeExtractionController.cs` 确实把剩余净化点数按 `1 点 = 1 现金` 返还。

**玩家症状：**
- 投入 `1000` 现金只得到 `10` 净化点，撤离剩余 `10` 点只带出 `10` 现金，玩家会认为现金被吞。

**目标行为二选一，执行前选定一种并全链路统一：**
- 方案 A：净化点是战斗资源。入场现金投入不承诺返本，文案明确“现金转换为局内资源，撤离只把剩余净化点按点数结算为现金奖励”。
- 方案 B：现金可保值。记录 `ConfirmedCashInvested`，撤离成功时先按原始投资比例返还未消耗的投资价值，再把战斗获得净化点转现金。

**推荐目标：**
- 采用方案 A，改文案，避免重做经济账本。
- 现金弹窗正文改成“兑换比例：100 现金 = 1 局内净化点数。失败/死亡全部损失；撤离时剩余净化点按点数结算为现金奖励。”
- 撤离结算通知改成“剩余净化点数结算为 {0} 现金”，不要写“退回”。

**静态守卫：**
- 检查现金提示不再出现“按 1:1 转回现金”。
- 检查 `CashToPurificationRatio` 和 `SettleZombieModeExtractionCashShell()` 的关系在文档或注释中明确。

### P1. 高价值空投是不可交互方块，奖励却立即发到玩家身上

**证据路径：**
- `ZombieMode/ZombieModeRewards.cs`
- `CreateZombieModeHighValueAirdrop()` 创建 `PrimitiveType.Cube`，随后立刻 `TryGiveRandomItemByTags(...)` 给玩家。

**玩家症状：**
- 地上出现“空投”视觉物，但无法打开；奖励已经进背包，视觉与交互不一致。

**目标行为二选一，执行前选定一种并全链路统一：**
- 方案 A：改名为“即时补给”，不创建空投物体，只直接发奖励并通知。
- 方案 B：真正创建可交互空投箱，玩家走近打开后领取奖励。

**推荐目标：**
- 采用方案 A，最小修改且低风险。删除 cube 视觉，奖励名和通知改成“高价值补给已发放”。
- 若坚持保留“空投”，必须复用 `InteractableLootbox` 或项目已有 lootbox 创建路径，不要保留无交互 cube。

**静态守卫：**
- 检查 `ZombieMode_HighValueAirdrop` 不再由 `GameObject.CreatePrimitive(PrimitiveType.Cube)` 创建，或检查它接入真实交互组件。

### P1. 奖励和开局物品候选为空时可能静默失败

**证据路径：**
- `ZombieMode/ZombieModeEntry.cs`
- `GrantZombieModeStarterLoadout()` 最后 `return grantedAny || loadout == Melee || loadout == Gunner`，即使核心武器没发到也可进入模式。
- `ZombieMode/ZombieModeRewards.cs`
- 多个 `GrantZombieModeRandom*` / `TryGiveRandomItemByTags(...)` 失败时不会保证 UI 选项被替换或给玩家补偿。

**玩家症状：**
- 开局选了枪械/近战但没拿到核心武器。
- 奖励选项点击后没有实际物品，只有提示或什么都没有。

**目标行为：**
- 任何展示给玩家的奖励选项必须能实际兑现。
- 核心开局物品失败时，应给明确失败提示并阻止开始，或给稳定 fallback。

**建议实现：**
- 在构建奖励 catalog 时过滤当前候选为空的装备类奖励。
- 为随机装备奖励增加 `fallback to PurificationPoints`，失败时明确通知。
- 开局流派至少要求核心武器发放成功；补给发放失败可以降级但要记录 DevLog。

**静态守卫：**
- 检查 `GrantZombieModeStarterLoadout` 不再无条件因 loadout 枚举返回 true。
- 检查装备类奖励失败路径会通知或 fallback。

### P1. UI 仍是固定尺寸原型界面，低分辨率下有文本挤压风险

**证据路径：**
- `ZombieMode/ZombieModeCashInvestmentView.cs`
- `ZombieMode/ZombieModeEntry.cs`
- `ZombieMode/ZombieModeRewards.cs`
- `ZombieMode/ZombieModeExtractionController.cs`
- 多处固定 `new Vector2(...)` 面板和按钮，没有根据文字长度、语言、屏幕比例动态布局。

**玩家症状：**
- 中文/英文长句可能挤在按钮里或和其他文字重叠。
- 商人/护士服务列表、奖励按钮、现金说明在小屏上可能难读。

**目标行为：**
- 所有丧尸模式运行 UI 至少在 1920x1080、1366x768、1280x720 可读，不重叠。
- 按钮文本允许换行，奖励按钮高度固定但文字不溢出。

**建议实现：**
- 扩展 `ZombieModeUIHelper`，集中 `CreateRect`、`CreateText`、`CreateButton`。
- 给奖励项/NPC 服务项统一使用 `ContentSizeFitter` 或动态 font-size 下限。
- 避免每个 View 私有复制 `CreateRect` / `CreateText`。

**验证：**
- 进游戏依次打开现金投入、开局流派、奖励选择、撤离机会、临时商人、临时护士。
- 每个界面截 1920x1080 和 1366x768 图，确认无重叠。

### P1. 临时商人/护士是 capsule service terminal，不是玩家预期 NPC

**证据路径：**
- `ZombieMode/ZombieModeRewards.cs`
- `CreateZombieModeTemporaryServiceTerminal()` 创建 `PrimitiveType.Capsule`。
- `ApplyZombieModeTemporaryNpcProtection()` 的 Health invincible 分支对 primitive terminal 基本无效，主要靠威胁目标清理保护。

**玩家症状：**
- 选择“临时商人/护士”后出现一个胶囊体，不像 NPC。
- 玩家可能误以为资源缺失或功能没刷出来。

**目标行为二选一，执行前选定一种并全链路统一：**
- 方案 A：明确这就是“补给终端/医疗终端”，改显示名、交互文案、模型颜色，不再叫临时 NPC。
- 方案 B：复用已有 NPC/商人/护士 prefab 或项目 NPCNameTag/Interactable 体系，真正生成 NPC。

**推荐目标：**
- 采用方案 A 作为第一版生产可用修复。改文案为“补给终端”“医疗终端”，避免承诺真实 NPC。

**静态守卫：**
- 检查玩家可见文本不再把 primitive terminal 称作“临时商人/临时护士”，或检查已接入真实 NPC prefab。

### P2. Boss/特殊敌人的技能可读性仍偏原型

**证据路径：**
- `ZombieMode/ZombieModePollution.cs`
- `ZombieMode/ZombieModeBossController.cs`
- 当前主要通过 `PopText`、telegraph flat zone、瞬移/直接位移/范围伤害表达技能。

**目标行为：**
- 玩家至少能区分冲刺、爆炸、毒区、召唤、骚扰投射、Boss 护盾、腐蚀区域。
- 不要求一次性做 AssetBundle 级特效，但要保证有清晰预警、颜色、名字、伤害时机。

**建议实现：**
- 给每种特殊/Boss 技能统一命名和颜色。
- 复用 `CreateZombieModeFlatZoneVisual()` 做低成本预警，不再新增高 GC primitive。
- Boss 技能 PopText 不只显示 Boss 名，也显示技能名。

## 任务包

### Task 1: 波次清理闭环

**Files:**
- Modify: `ZombieMode/ZombieModeCleanup.cs`
- Modify: `ZombieMode/ZombieModeWaveController.cs`
- Test: `tests/ZombieModeReview20260503Guard.py` or new `tests/ZombieModeWaveCleanupGuard.py`

- [x] 增加 `CleanupZombieModeCombatEnemiesForWaveEnd(int runId, string reason)`。
- [x] 在普通波和 Boss 波进入奖励前调用。
- [x] 在下一波开始前做防御性调用。
- [x] 确认 `LivingZombieCount`、`zombieModeEnemyInstanceIds`、`RunOnlyObjects` 同步清理。
- [x] 静态守卫覆盖两个调用点。

### Task 2: 属性奖励符号与经济文案

**Files:**
- Modify: `ZombieMode/ZombieModeRewards.cs`
- Modify: `Localization/LocalizationInjector.cs`
- Test: `tests/ZombieModeRewardCatalogGuard.py` plus new focused guard

- [x] 把 `AttributeDamageReduction` 的内部 modifier 改为负向物理承伤倍率。
- [x] 调整 `AddZombieModeAttributeModifier()`，允许合法负数 modifier。
- [x] 奖励显示继续写正向“伤害减免”，不要暴露负号给玩家。
- [x] 现金投入文案按推荐方案 A 改清楚。
- [x] 添加守卫防止 `ElementFactor_Physics` 减伤再写成正数。

### Task 3: 信标、撤离继续、空投语义对齐

**Files:**
- Modify: `Integration/Items/ZombieTideBeaconConfig.cs`
- Modify: `ZombieMode/ZombieModeExtractionController.cs`
- Modify: `ZombieMode/ZombieModeRewards.cs`
- Modify: `Localization/LocalizationInjector.cs`
- Test: `tests/ZombieModeLocalizationGuard.py` plus focused guard

- [x] 信标使用描述改为快速开波。
- [x] `继续战斗` 改为真正关闭撤离机会并切到 `Preparation`。
- [x] 清理隐藏撤离点和 HUD 撤离提示。
- [x] 高价值空投按推荐方案 A 改为即时补给，或接入真实交互箱。
- [ ] 运行一次 Boss 节点后手测 `立即撤离`、`继续战斗`、信标提前开波三条路径。

### Task 4: 奖励候选和开局物品兑现

**Files:**
- Modify: `ZombieMode/ZombieModeEntry.cs`
- Modify: `ZombieMode/ZombieModeRewards.cs`
- Test: `tests/ZombieModeRewardCandidateCacheGuard.py` plus focused guard

- [x] 开局流派核心武器候选为空时阻止开始并给提示，或发固定 fallback。
- [x] 奖励 catalog 过滤当前不可兑现的装备类奖励。
- [x] 物品奖励发放失败时 fallback 净化点并通知。
- [x] 保留候选缓存，避免每次刷新奖励都全量 `ItemAssetsCollection.Search`。

### Task 5: UI 和终端表现收口

**Files:**
- Modify: `ZombieMode/ZombieModeUIHelper.cs`
- Modify: `ZombieMode/ZombieModeCashInvestmentView.cs`
- Modify: `ZombieMode/ZombieModeEntry.cs`
- Modify: `ZombieMode/ZombieModeRewards.cs`
- Modify: `ZombieMode/ZombieModeExtractionController.cs`
- Modify: `Localization/LocalizationInjector.cs`

- [x] `ZombieModeUIHelper` 提供统一 rect/text/button helper。
- [x] 现金、开局、奖励、撤离、终端 UI 删除重复私有 helper。
- [x] 奖励按钮、服务按钮、长文本支持换行和动态缩小字号。
- [x] 临时商人/护士按推荐方案 A 改名为补给终端/医疗终端，或实现真实 NPC。
- [ ] 手测 1920x1080、1366x768、1280x720 三个分辨率无重叠。

## 验证命令

在 WSL/当前 shell 先跑静态守卫，使用 `python3`：

```bash
python3 tests/ZombieModeCompileListGuard.py
python3 tests/ZombieModeLocalizationGuard.py
python3 tests/ZombieModeItemIdentityGuard.py
python3 tests/ZombieModeSpawnEnemyCoreReuseGuard.py
python3 tests/ZombieModeRunOnlyCleanupGuard.py
python3 tests/ZombieModeTransactionBoundaryGuard.py
python3 tests/ZombieModeStateModelGuard.py
python3 tests/ZombieModeSafeZoneGuard.py
python3 tests/ZombieModeTemporaryNpcBoundaryGuard.py
python3 tests/ZombieModeRewardCatalogGuard.py
python3 tests/ZombieModeRewardCandidateCacheGuard.py
python3 tests/ZombieModePerformanceRegistryGuard.py
python3 tests/ZombieModeBossLifecycleGuard.py
python3 tests/ZombieModeReview20260503Guard.py
python3 tests/ZombieModeReviewFixGuard.py
python3 tests/PerformanceTierAdjusterGuard.py
python3 tests/RunScopedRegistryGuard.py
python3 tests/MapSelectionInjectionReuseGuard.py
python3 tests/OfficialCompileListFileExistenceGuard.py
```

在 Windows 仓库根目录跑编译：

```cmd
compile_official.bat
```

部署和进游戏测试：

```cmd
test_bossrush_official.bat
```

也可以在 Windows 仓库根目录使用本目标专用串联脚本：

```cmd
test_zombiemode_goal_windows.bat
```

该脚本会顺序调用 `compile_official.bat` 和 `test_bossrush_official.bat`，然后打印本文档的关键进游戏冒烟路径。脚本通过不等于生产完成；仍必须完成下方手工冒烟。

## 手工冒烟路径

### 入口和开局

- [ ] 基地购买或获得 `尸潮邀请函`。
- [ ] 使用邀请函打开末日丧尸地图选择。
- [ ] 选择地图后现金弹窗显示清楚的 `100 现金 = 1 局内净化点数`。
- [ ] 取消现金弹窗会关闭/返回，不扣邀请函和现金。
- [ ] 投入现金不足时不进入地图，提示现金不足。
- [ ] 正常进图后身上原装备转入仓库，只保留局内开局物品和信标。
- [ ] 近战开局至少拿到近战核心武器。
- [ ] 枪械开局至少拿到枪和匹配弹药。

### 波次和清理

- [ ] 第 1 波开始前有安全区和 `30s` 准备倒计时。
- [ ] 使用信标后 `3s` 开波，文案不再写撤离。
- [ ] 普通波完成后奖励 UI 出现时没有旧敌人继续追杀。
- [ ] 下一波开始时没有上一波残留敌人。
- [ ] 第 5 波 Boss 死亡后进入奖励/撤离机会时，没有该 Boss 波普通小怪残留。

### 奖励和属性

- [ ] 每个奖励选项点击后都有实际效果或明确 fallback 通知。
- [ ] 伤害减免奖励选择后，物理承伤方向确认为降低。
- [ ] 装备类奖励候选为空时不会显示无法兑现的选项。
- [ ] 临时补给/医疗终端交互可用，命名与外观一致。

### 撤离

- [ ] Boss 节点奖励后出现撤离机会。
- [ ] `立即撤离` 开始 `15s` 倒计时，离开区域会取消读条。
- [ ] 撤离成功回基地，剩余净化点数按当前设计转换为现金奖励。
- [ ] `继续战斗` 会关闭撤离机会、清理撤离点、切回准备期，并给明确反馈。
- [ ] `继续战斗` 后 HUD 不再显示撤离开放。

### 低端机

- [ ] 活怪数高时性能保护启用，不产生同帧大量创建尖峰。
- [ ] 保护回收不会回收 Boss、近距离正在交战的精英/特殊敌人。
- [ ] 连续 10 波后掉落、净化星、临时 UI、终端、撤离点都能被清理。

## 完成标准

- 所有任务包完成，且没有新建与现有职责重复的大型系统。
- `compile_official.bat` 成功生成 `Build/BossRush.dll`。
- 上方静态守卫全部通过。
- 至少一轮进游戏从邀请函入场打到第 5 波 Boss 节点，并验证撤离成功与继续战斗两条路径。
- 最终报告必须明确列出未验证项；没有进游戏证据时不得宣称“生产水平已完成”。
