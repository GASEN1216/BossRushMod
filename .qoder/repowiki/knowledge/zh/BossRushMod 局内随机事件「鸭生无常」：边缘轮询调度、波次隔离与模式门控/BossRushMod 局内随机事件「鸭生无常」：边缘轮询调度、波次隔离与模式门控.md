---
kind: gameplay_system
name: BossRushMod 局内随机事件「鸭生无常」：边缘轮询调度、波次隔离与模式门控
category: gameplay_system
scope:
    - RandomEvents/**
source_files:
    - RandomEvents/RandomEventsTuning.cs
    - RandomEvents/RandomEventModels.cs
    - RandomEvents/RandomEventModeGate.cs
    - RandomEvents/RandomEventDirector.cs
    - RandomEvents/RandomEventCatalog.cs
    - RandomEvents/RandomEventCatalog_Fun.cs
    - RandomEvents/RandomEventEffectsBridge.cs
    - RandomEvents/RandomEventEffectsBridge_Loot.cs
    - RandomEvents/RandomEventEffectsBridge_Spawn.cs
    - RandomEvents/RandomEventHud.cs
    - RandomEvents/RandomEventsRuntimeModule.cs
    - Config/ConfigRandomEvents.cs
    - tests/RandomEventsWaveIsolationGuard.py
    - tests/RandomEventsModeGateGuard.py
    - tests/RandomEventsRuntimeModuleGuard.py
---

## 1. 系统概述

局内限时随机事件：战局进行中按冷却 + 权重抽取，横幅播报 → 生效 → 到时清理。
它是变异词条的**动态互补**——词条开局抽定、整局恒定；事件是限时的、有播报、有调度。

8 个事件：空投补给、血月凶兆、Boss 乱入、神秘商人路过、声东击西、鸭王的烟花、金鸭雨、鸭群巡游。

**零存档、零新物品、零新增 Harmony、零新增音频**。系统总开关字段与旧键兼容保留但恒为 true；
玩家仍可调频率档 1~3（默认 2）。

## 2. 关键文件与职责

| 文件 | 职责 |
| --- | --- |
| `RandomEventsTuning.cs` | 全部数值单点：权重、时长、冷却、单局上限、各事件参数 |
| `RandomEventModels.cs` | `RandomEventId` / `RandomEventPhase` / `RandomEventEndReason` 枚举、`RandomEventContext`、抽象基类 `RandomEventBase` |
| `RandomEventModeGate.cs` | 模式门控唯一入口，fail-closed |
| `RandomEventDirector.cs` | 调度状态机：边缘轮询、冷却、权重抽取、全量清理编排 |
| `RandomEventCatalog.cs` / `_Fun.cs` | 8 个事件的 `RandomEventBase` 子类实现 |
| `RandomEventEffectsBridge*.cs` | `partial ModBehaviour` 桥：收口所有需要触碰宿主私有基建的代码（生成、掉落箱、Buff 目标收集） |
| `RandomEventHud.cs` | 活动事件徽章 + 剩余秒数 |
| `RandomEventsRuntimeModule.cs` | 宿主回调唯一落点，dormant 契约 |

## 3. 架构与设计约定

### 3.1 事件建模：抽象基类而非委托字段

`RandomEventBase` 定义 `Id` / `DisplayName` / `DurationSeconds` / `Weight` /
`CanTrigger` / `OnTrigger` / `OnTick` / `OnCleanup`。`OnCleanup` 是 abstract——
编译器强制每个事件都必须实现清理，这是「事件必须能还原」的第一道保险。

### 3.2 局开始/结束用 run signature 边缘轮询

标准模式的结束路径至少有 4 处（胜利、死亡、切图、模式专属 End），逐处插调用既侵入又易漏。
因此调度器每帧只做 O(1) 的 bool 读，组合成 run signature：
`none → some` 是开局（重置计数与冷却），`some → none` 是局末（触发全量清理）。

**只认「局进行中」（`IsActive` / `IsModeDActive`），不认 `IsBossRushArenaActive` 空闲态**——
空闲竞技场有清怪维护循环，会把事件生成物直接清掉。

`OnSceneLoaded` 推进 generation，作废所有异步续作。

### 3.3 波次隔离（本系统最高危面）

标准 / 无间炼狱按 Boss **实例匹配**推进波次（`currentWaveBosses` / `currentBoss`，
外加 `TryFixStuckWaveIfNoBossAlive` 自愈路径）；Mode D 用逐实例 `OnDeadEvent` 监听 +
`modeDCurrentWaveEnemies`。

「不速之客」乱入 Boss 只要**不写进这些容器、不注册波次死亡回调**，对波次状态机就完全不可见。
写进去会出两类事故：乱入被计入本波导致卡波；乱入死亡被当成波次死亡导致跳波。两者都只在
实机长局里偶发，人工冒烟抓不到——因此由 `RandomEventsWaveIsolationGuard.py` 静态锁死符号面。

> **2026-09-03 更正（CR-2026-09-03-009）**：只锁 `RandomEvents/` 的符号面**不够**。
> 乱入 Boss 走 `SpawnEnemyCoreInternalAsync` 的 Legacy 路径（options 传 null）时，
> 会被共享刷怪核心登记进 `bossSpawnTimes`，其死亡经
> `LootAndRewardsRandomBossLoot.OnBossBeforeSpawnLoot_LootAndRewards` 调到 `HandleBossDeath`
> ——那是一条**绕过本目录**的间接推波路径，静态符号守卫看不到，实机确实跳波。
>
> 现在的真正防线在被调方：`HandleBossDeath` 在成就与去重之后有一道
> `IsCurrentWaveBossMember` 分界线，之下才是波次账。同一道闸顺带修好了 Mode D
> 的跨模式串台（Mode D 也置 `IsActive`，其 Boss 曾能把标准竞技场的 Boss 刷进 Mode D）。
> 由 `tests/WavesArenaBossMembershipGuard.py` 守卫。本目录的符号隔离仍然保留为第一道防线。

乱入生成额外的硬要求：
- `onCommit` 必须包含敌对性安全网 `SetTeam(Teams.wolf)`（AGENTS.md 4.5）；
- 注册 recovery 锚点防卡死；
- 生成物命名前缀 `RndEvt_Intruder_`，实机排查残留靠它。

### 3.4 空投箱的翻箱保护（2026-09-03 新增）

空投箱由 `ctx.Scope` 托管，事件到时即 `Destroy`。首版不看玩家是否正开着箱子，
于是权重最高的事件经常连同没拿走的东西一起在玩家眼前消失。

修法的关键是**延「到期」而不是延「清理」**：`RandomEventDirector.EndActiveEvent` 在
`evt.OnCleanup` 之后**无条件**再跑一次 `ctx.Scope.Clear`，所以在 `OnCleanup` 里放行无效。
`RandomEventAirdropSupply` 覆写 `OnTick`，在 `InteractableBase.Interacting` 为真时把
`ctx.DurationSeconds` 推到 `ElapsedSeconds + AirdropHoldOpenGraceSeconds`（3s）。

两条硬约束：
- **必须有硬帽**（`AirdropHoldOpenMaxSeconds` = 120s）。并发恒 1，事件停在 `EventActive`
  期间本局不再抽下一个事件，无上限等于玩家挂着界面就能把整局随机事件卡死。
- **只有 `Expired` 一条路径可延后**。`RunEnded` / `SceneChanged` / `SwitchDisabled` /
  `HostDestroyed` / `DebugForced` / `TriggerFailed` 都直接调 `EndActiveEvent`，
  绕过到期比较，照旧强制销毁，不存在跨图跨局泄漏。

销毁前先关战利品界面（`crate.StopInteract()` 走官方联动，`LootView.TargetInventory` 比对兜底）。
代码在 `RandomEvents/RandomEventAirdropHold.cs`（partial），拆出来是因为
`RandomEventCatalog.cs` 贴着 `LargeFileBudgetGuard` 的 1200 行硬预算；文件名刻意不以
`RandomEventCatalog` 开头，否则 `RandomEventsWaveIsolationGuard` 的「子类数 vs OnCleanup 数」
配平计数会被多数一次。由 `tests/RandomEventAirdropHoldGuard.py` 守卫。

### 3.5 血月的两个坑

- `TimeOfDayController.NightViewAngleFactor` 等静态因子**会被官方 URP Volume 组件每帧回写**，
  Mod 直接写会被覆盖，因此**不采用**；改走全屏红 vignette（纯色 Image + alpha 呼吸）+
  存活敌人临时 Stat Modifier。
- 强制天气 `WeatherManager.SetForceWeather` 可用且**不进存档**（`SaveData` 只存 seed），
  但 `Instance` 可能为 null（竞技场场景待实测），因此设计成「遮罩必达、天气分支带守卫自适应」，
  开始前捕获原值、三处兜底还原（事件到时 / 局末 / `OnDestroy`）。

敌人增益按 2 秒节流补挂新刷的敌人，目标列表经 Bridge 只读收集，量级十几个，**不每帧扫**。

### 3.5 与变异词条完全独立

两者生命周期语义相反，硬塞进 `MutatorContext` 会破坏 AGENTS.md 4.11 的共享变异系统不变式。
只共用底层设施：`RuntimeStatModifierTracker`、`SpawnEnemyCoreInternalAsync`、横幅播报、
`RuntimeScope`、共享 UI 库。

乱入 Boss 走 Legacy 生成路径，因此**自动吃当局的 Mutator `ApplyToEnemy`**，与在场敌人一致，
体验自洽。

## 4. 模式门控矩阵

| 入口 | 启用 | 理由 |
| --- | --- | --- |
| 标准 BossRush / 无间炼狱 / Mode D | 是 | 波次结构简单，隔离已核实；Mode D 空投品质单独压上限 |
| Mode E 划地为营 | 否 | 多阵营 + 扫箱令，乱入 Boss 阵营归属语义不清 |
| Mode F 血猎追击 | 否 | 悬赏雷达只认注册过的 Boss；阶段播报已密集会打架 |
| Mode G 宿命回响 | 否 | 固定九波编排 + 严格奖励事务，运行期连 Legacy 波次 tick 都被整体冻结 |
| Mode H 斗蛐蛐 | 否 | 观战 + arena isolation lease 独占清场 |
| 末日丧尸 | 否 | 独立生命周期与奖励体系（AGENTS.md 4.11 边界） |
| 普通撤离图 | 否 | 官方 spawner / 任务 / 天气均活跃 |

判定形态照 `PetNest/PetNestModeGate.cs`：**先排除后允许**，no-throw fail-closed，
只经公开门面（`IsModeGRunInProgressSafe` 等），**禁引** `ModeGRuntimeGates` /
`ModeHRuntimeGates` / `ZombieModePhaseGuards` 等内部符号。

## 5. 性能

Dormant / Armed 状态每帧只有几次 bool 读 + 一次 float 累加；血月补挂 2 秒节流且列表有界；
生成走既有分帧后处理；`OnUpdate` 路径零日志。并发事件恒为 1。

## 6. 契约面（发布后冻结）

- `RandomEventId` 枚举值（HUD 图标按整数值取 `Assets/ui/random_events/evt_<id>.png`）。
- 配置键 `BossRush_RandomEventsEnabled` / `BossRush_RandomEventsFrequency`。
- 生成物命名前缀 `RndEvt_`。

## 7. 调试

F3 调试菜单提供逐事件强制触发与立刻结束当前事件，是实机 smoke 的必备入口。

## 8. 实机状态与未完成项

首次 F3 已在标准竞技场轮流调度八个事件，但旧 runner 每个仅等 0.35 秒并只看入口返回值：
Boss 乱入五次预设解析均失败，最终仍误报 8/8 PASS；因此这次不能算功能通过。
当前 runner 改为逐事件等待实际副作用并输出独立 case，完整复测待 owner 运行。

仍只能实机确认的表现项：竞技场是否存在 `WeatherManager.Instance`；零伤害 `CreateExplosion`
是否附带击退（若有则烟花改纯粒子）。关键回归项：乱入 Boss 在场时打死波次 Boss，
波次应正常推进且乱入仍在。

## 9. 2026-08-31 限时商店修复

商店对象先 inactive 创建，用官方存在的 `Merchant_Normal` 引导 `StockShop.Awake`，避免数据库打印
“未配置商人”；激活后的同一帧、`Start` 之前恢复稳定 Mod merchantID，再覆盖为事件库存。
后续保存/读档订阅因此使用有界的稳定 Mod key。弹药/医疗各 99 件、高品质彩头 1 件；每次新事件
只补满一次。动态商人的 Animator 若先于 `MagicBlending.Start` 进入状态，由共享兼容补丁有界推迟
该次回调，初始化完成且仍在原状态时才重放。

## 10. F3 实际副作用协议

`RandomEventBase.GetValidationOutcome` 默认适用于同步事件；异步事件覆写后记录 requested、spawned、
failed 与 completed。Boss/商人必须等实体和商店库存真实可用，空投等落地，声东击西/烟花等序列
完成，金鸭雨/巡游等全部分帧生成回调。任一项 30 秒未收敛或生成数不符即 FAIL，随后先强制清理；
只有清理安全才继续下一项。乱入桥调用 SpawnCore 前还会幂等初始化官方 preset cache，修复标准模式
未经过 Mode D/Zombie 预热时“目录有 key、缓存无 preset”的空转。

## 2026-09-04 审核修复

**乱入 Boss 不再夺走本波身份。** 事件池取自 `GetFilteredEnemyPresets()`，其中**含**三个自定义
Boss（龙裔 / 龙王 / 女巫）。抽中时 SpawnCore 会路由到它们的专用生成器，而那些生成器此前
无条件写 `currentBoss` 与 `currentWaveBosses`——本文件第 9 节承诺的「绝不写入任何波次容器」
在这条路径上是失效的，因为写入发生在生成器体内，桥本身挡不住。
后果：单 Boss 档真 Boss 击杀不再推波；乱入者被销毁后卡波自检读到「无存活 Boss」反而主动推波。
现由 `EnemySpawnCoreOptions.SuppressWaveBossRegistration` 透传到三个生成器的
`isNonWaveSpawn` 门控（形态照龙裔既有的 `isChildProtectionSummon`）。

**商人与巡游鸭补解距离休眠。** 两者都走官方 `CreateCharacterAsync`，此前没调
`SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep`；玩家跑远再回来会被官方
`SetActiveByPlayerDistance` 每帧 `SetActive(false)` 关掉，表现为凭空消失。

**商人交互名接上本地化。** `BossRush_RandomEvent_MerchantShop` 此前全仓零注入，
玩家看到带星号的原始 key。新增 `Localization/RandomEventsLocalization.cs` 并挂进
`InjectLocalization_Extra_Integration()`。本模块其余文案仍走内联 `L10n.T(中,英)`，
只有走官方按 key 查表的 `_overrideInteractNameKey` 需要注入。

**空投「翻箱保护」判据换成官方 loot 事件。** 旧判据 `InteractableBase.Interacting` 在官方
打开战利品界面后一帧就变 false（界面仍开着），宽限窗口几乎永不生效。现改用公有静态事件
`InteractableLootbox.OnStartLoot` / `OnStopLoot` 做闩，`Interacting` 保留为次要信号。

## 2026-09-04 深度复审修复

`COMPAT`。血月对已加成目标订阅一次具名 Health.OnDead，记录死亡事实；HashSet 移除保证同一目标只计一次。两秒 Tick 兑现欠款，OnCleanup 在退订、清表之前最后结算，也覆盖静态死亡通知之前触发的局末收尾。销毁/退场不能当作击杀。订阅有 owner 标记，Scope 和正常清理共用幂等退订。此规则替代先前仅靠轮询、结束直接清表的描述。

章节来源：`RandomEvents/RandomEventCatalog.cs` 的 BloodMoonEvent。
