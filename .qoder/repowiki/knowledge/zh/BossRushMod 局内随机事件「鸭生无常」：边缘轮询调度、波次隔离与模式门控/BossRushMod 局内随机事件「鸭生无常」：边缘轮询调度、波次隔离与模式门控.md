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

**零存档、零新物品、零新增 Harmony、零新增音频**。总开关
`BossRush_RandomEventsEnabled`（默认 true）+ 频率档 1~3（默认 2）。

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

乱入生成额外的硬要求：
- `onCommit` 必须包含敌对性安全网 `SetTeam(Teams.wolf)`（AGENTS.md 4.5）；
- 注册 recovery 锚点防卡死；
- 生成物命名前缀 `RndEvt_Intruder_`，实机排查残留靠它。

### 3.4 血月的两个坑

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

## 8. 已知未完成项

实机 smoke 未做。两项只能实机确认：竞技场是否存在 `WeatherManager.Instance`；
零伤害 `CreateExplosion` 是否附带击退（若有则烟花改纯粒子）。
关键回归项：乱入 Boss 在场时打死波次 Boss，波次应正常推进且乱入仍在。
