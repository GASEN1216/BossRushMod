---
kind: gameplay_system
name: BossRushMod 鸭科夫日报：自算游戏日、签到梯度与报箱建筑
category: gameplay_system
scope:
    - Integration/DailyReport/**
source_files:
    - Integration/DailyReport/DailyReportTuning.cs
    - Integration/DailyReport/DailyReportModels.cs
    - Integration/DailyReport/DailyReportCodec.cs
    - Integration/DailyReport/DailyReportPersistence.cs
    - Integration/DailyReport/DailyReportSaveCoordinator.cs
    - Integration/DailyReport/DailyReportService.cs
    - Integration/DailyReport/DailyReportBounty.cs
    - Integration/DailyReport/DailyReportContent.cs
    - Integration/DailyReport/DailyReportRewards.cs
    - Integration/DailyReport/DailyReportStatsCollector.cs
    - Integration/DailyReport/DailyReportRuntimeModule.cs
    - Integration/DailyReport/DailyReportInteractable.cs
    - Integration/DailyReport/DailyReportUI.cs
    - Integration/DailyReport/DailyReportUIBridge.cs
    - Integration/DailyReport/DailyReportMailboxBuilder.cs
    - Integration/DailyReport/DailyReportMailboxRuntime.cs
    - Config/ConfigDailyReport.cs
    - Localization/DailyReportLocalization.cs
    - tests/DailyReportPersistenceGuard.py
---

## 1. 系统概述

玩家在基地花 500 金建一个**报箱**建筑，此后每过一个游戏日（≈24 现实分钟）收到一期《鸭科夫日报》：昨日战绩被写成报纸新闻，附每日悬赏结果、签到墙、明日天气预报与趣味杂谈。

设计文档：`docs/未来拓展/设计/P2-日报系统.md`。

系统由入口开关 `dailyReportEnabled`（默认 true）总控，关闭时整个子系统 dormant：不订阅存档、不计时、不出刊、不提示。

## 2. 关键文件与职责

| 路径 | 作用 |
|---|---|
| `DailyReportTuning.cs` | 数值常量单点（一天秒数、签到梯度、建筑参数、存档 key、本地化前缀） |
| `DailyReportModels.cs` | 运行时 DTO（`DailyReportData` / `DailyReportStats`），刻意保持**扁平** |
| `DailyReportCodec.cs` | 扁平 JSON 编解码，复用 `Utilities/SimpleJsonHelper.cs`，不新造解析器 |
| `DailyReportPersistence.cs` | 单 key 存档管线：整存 `Save<string>`、写屏障、回读核对、fail-closed |
| `DailyReportSaveCoordinator.cs` | **唯一** `SaveFile` 调用点，每批至多一次；`IsSaving` 时 deferred 重试 |
| `DailyReportService.cs` | 核心状态机：自算计时、跨天结算、断签、签到、悬赏、统计写入 |
| `DailyReportBounty.cs` | 悬赏目录与判定（当日悬赏是纯函数，不占存档） |
| `DailyReportContent.cs` | 版面组装：头条选题、战绩栏、天气预报、运势与杂谈 |
| `DailyReportRewards.cs` | 发奖：按品质全表随机 + 黑名单过滤 → 官方快递缓冲 |
| `DailyReportStatsCollector.cs` | 战绩采集（Health / EconomyManager / RaidUtilities） |
| `DailyReportRuntimeModule.cs` | 宿主回调唯一落点，单实例由 ModBehaviour 持有 |
| `DailyReportMailboxBuilder.cs` / `_MailboxRuntime.cs` | 报箱建筑注入（资源、prefab、数据、事件、场景恢复） |
| `DailyReportUI.cs` / `_UIBridge.cs` | 报纸面板（官方 `Duckov.UI.View` + FadeGroup） |
| `DailyReportInteractable.cs` | 报箱交互组件（`InteractableBase`） |

## 3. 架构与设计约定

### 3.1 「一天」是自算的，不跟随官方 GameClock.Day

**这是本系统最容易被改错的地方，改动前必读。**

`DailyReportRuntimeModule.OnUpdate` 里累计 `deltaTime × clockTimeScale`，与官方 `GameClock.Update` 的 `StepTime(Time.deltaTime * clockTimeScale)` 逐帧同源（`鸭科夫源码/TeamSoda.Duckov.Core/GameClock.cs:191`）。

**绝不订阅 `GameClock.OnGameClockStep`，也不读 `GameClock.Day`**，理由有三条：

1. 官方睡觉（`SleepView`）与 Continue 跳早 7 点（`LevelManager.OnNewBoot`）走的是 `StepTimeTil`，不经 `Update` → 自动被排除，不需要任何"跳变阈值"启发式。
2. 读档时 `GameClock.Load()` 会额外 fire 一次 `OnGameClockStep`；没订阅所以零影响。
3. 暂停时 `Time.timeScale = 0` → `deltaTime = 0`，与官方钟同步停表。

一天 = **86300 游戏秒**（镜像官方 `GameClock.SecondsPerDay`，**不是 86400**）。

宿主 `Update` 由 `SceneRuntimeGate` 门控，只排除主菜单、失败加载屏、撤离屏与加载中；**基地不在排除列表**，所以基地挂机照常计时（产品决策：挂机也算）。

由 `tests/DailyReportPersistenceGuard.py` 守卫这几条。

### 3.2 跨天结算时序

`while (carry >= 86300)` 循环（单帧增量至多约 120 游戏秒，实际不可能一帧跨多天，循环仅兜底）：

1. 悬赏结算（**必须在 `Today` 被重置之前**，进度源就是今日统计）
2. 断签判定：刚结束那天没签 → `Streak` / `PeriodSignedCount` / `PeriodClaimedMask` 清零，**`PeriodIndex` 保留**
3. `Today` 快照转存为 `Yesterday`，`Today` 清零
4. `DayIndex++`
5. 入队持久化 → 协调器落盘
6. 出刊提示：在基地立即横幅，否则挂起到回基地再发

### 3.3 存档

- key `BossRush_DailyReport_v1`（槽级），`Save<string>` 整存扁平 JSON，顶层带 `schemaVersion`。
- 不用 typed `Save<T>`：ES3 会把 assembly-qualified 类型名写进存档，mod 程序集改名就会让老档读不回来。
- 未知/更高 `schemaVersion`、payload 不可读 → 写屏障，只读不写，**绝不覆盖该 key**。
- 当天余数借官方 `OnCollectSaveData` 顺带写（零额外 IO），跨天才主动入队。
- DTO 保持扁平是刻意的：里程碑领取用**位掩码** `PeriodClaimedMask` 而不是 token 列表，因此只需 `SimpleJsonHelper`，不必引入第三套 JSON 解析器（ModeH 有 `ModeHJsonValue`、遗种巢有 `PetNestJson`，都与各自模块语义绑定）。

### 3.4 签到梯度

每期 30 天一页。第 1 期第 7/15/24/30 格发品质 5/6/7/8 各一件；第 2 期起第 7/14/21/28 格固定发品质 8。UI 显示累计天号（第 2 期第 1 格显示 31）。

本期签满 30 后**不立刻翻期**，而是在下一次签到时先翻期再签，这样"第 30 格已签"能在 UI 上停留一整天。

发奖顺序是**先发后标记**：`TryGrantMilestone` 成功才 `MarkMilestoneClaimed` 置掩码，宁可极端情况下重发也不吞奖励。发放失败时保留未领状态，下次打开面板由 `TryRedeliverPendingMilestones` 补发。

### 3.5 悬赏

**当日悬赏不占存档**：它是 `(bountySeed, dayIndex)` 的纯函数（`ModeHSeedStream` 派生），任何时候重算都得到同一条，因此重启/读档不会换题。存档里那组 `Bounty*` 字段记的是**已结算的昨日悬赏**，即当期日报要公布的那条。

进度不单独计数，直接把 `DailyReportStats` 当进度源——少一套计数器就少一处双重计数和漏退订的风险。

### 3.6 报箱建筑

完整照抄 `PetNest/PetNestBuilder.cs` 两件套形态。要点：

- 建筑 ID `bossrush_daily_mailbox` 会进官方 `BuildingData` 存档，**发布后永不可改名**。
- prefab 创建时先 `SetActive(false)`，等反射把 `graphicsContainer` / `functionContainer` 填好再激活（官方 `Building.Awake` 会解引用它们）。
- `BuildingInfo` 的 `requireBuildings` / `alternativeFor` / `requireQuests` **必须给空数组不能留 null**，官方 `RequirementsSatisfied` 会直接遍历。
- 注入后必须把 `readonlyInfos` 置 null，让官方重建只读视图。
- 老存档三保险：`TryInitializeDailyReportMailboxEarly`（抢在 `BuildingArea.Start` 前）→ deferred bootstrap 兜底 → 有存量时 repaint。
- 无美术时走 primitive 占位模型（立柱 + 箱体 + 斜盖 + 小红旗）；`CreatePrimitive` 自带的 Collider 必须删掉。

### 3.7 UI

继承官方 `Duckov.UI.View` + `FadeGroup` 运行时装配，挂在 `GameplayUIManager` 下——官方 View 栈自带 ESC 与焦点互斥，不必手动判 `View.ActiveView`。

纸张的浅色配色是**局部的**，不是第二套设计 token：`BossRushUI.ApplyPanelSkin` 只给形状不给色，颜色由调用方传入。层级常量与遮罩色仍走共享库（`BossRushUILayers.Panel` / `BossRushUIColors.Backdrop`），由 `tests/BossRushUISharedLibraryGuard.py` 守卫。

正文**不进本地化表**：日报是每天变的动态长文，塞进全局字符串表既污染表也无法按天变，一律走 `L10n.T(cn, en)` 内联。只有官方会主动查表的 key（`Building_*`、交互名）才进 `Localization/DailyReportLocalization.cs`。

## 4. 性能

| 每帧路径 | 成本 |
|---|---|
| `RuntimeModule.OnUpdate` | 两个早退判断 + 一次乘加 + 一次比较，零分配零反射 |
| `Health.OnDead` / `OnHurt` handler | 开关早退 → 主角判定 → 整数/浮点自增；**禁字符串拼接、LINQ、装箱** |
| 落盘 | 仅跨天入队与官方 collect 搭车；每批至多一次 `SaveFile`；绝无每帧 IO |
| 奖品候选表 | 按品质缓存，`ItemAssetsCollection.Search` 是全表扫描，只跑一次 |

## 5. 契约面（发布后冻结）

- 存档 key `BossRush_DailyReport_v1`
- 配置 key `BossRush_DailyReportEnabled`
- 建筑 ID `bossrush_daily_mailbox`
- 本地化前缀 `BossRush_DailyReport_` 与 `Building_bossrush_daily_mailbox`

零新增 TypeID（奖品全部使用官方物品随机抽取）。

## 6. 调试

F3 调试菜单提供三个按钮：**快进一天**（一个游戏日是 24 现实分钟，冒烟不可能真等）、**打开报纸**、**输出状态**。
