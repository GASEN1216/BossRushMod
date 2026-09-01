---
kind: gameplay_system
name: BossRushMod 鸭王征程：六章契约战役、零模式重构的目标采集与跨系统解锁
category: gameplay_system
scope:
    - Campaign/**
source_files:
    - Campaign/CampaignTuning.cs
    - Campaign/CampaignModels.cs
    - Campaign/CampaignFacilityUnlocks.cs
    - Campaign/CampaignPersistence.cs
    - Campaign/CampaignSaveCoordinator.cs
    - Campaign/CampaignContentCatalog.cs
    - Campaign/CampaignObjectiveTracker.cs
    - Campaign/CampaignObjectiveCollector.cs
    - Campaign/CampaignProgressService.cs
    - Campaign/CampaignModeBridge.cs
    - Campaign/CampaignAssetCache.cs
    - Campaign/CampaignNoteBridge.cs
    - Campaign/CampaignDialoguePlayer.cs
    - Campaign/CampaignBoardView.cs
    - Campaign/CampaignBoardInteractable.cs
    - Campaign/CampaignBoardBuilder.cs
    - Campaign/CampaignHud.cs
    - Campaign/CampaignFinalBoss.cs
    - Campaign/CampaignFinalBossInteractable.cs
    - Config/ConfigCampaign.cs
    - Localization/CampaignLocalization.cs
    - tests/CampaignSkeletonGuard.py
---

## 1. 系统概述

鸭王征程是 mod 的**第一个剧情系统**：一条六章的悬赏契约线，把既有的五个玩法入口
串成一次调查。玩家在基地建「征程公告板」接约，进指定模式完成特殊目标，回来交付，
听中间人讲一段，拿到一件「证物」写进官方笔记图鉴，并解锁一处竞技场后山设施。

定位与鸭皇图鉴同属「乘法型」内容：不新开模式，而是给已有模式**加一层动机**。

**剧情锚在 ModeH 已实现的机制上，不另造设定**：黑市鸭王杯名人堂只有 32 席，
第 33 个进来最底下那个就被挤掉（见 ModeH 设计提案 §17.8 与 `ModeHHallOfFamePersistence`）。
整条线索链就长在这条规则上——冠军不是被谁抹掉的，是排队排出去的；他之后做的每件事
都是在找一个不会被挤掉的名字，最后他找到了：Boss 图鉴不挤人。代价是不再当选手。

术语用「鸭**王**」与 ModeH 的「黑市鸭王杯」统一（「鸭皇图鉴」是 Boss 图鉴，另一个域）。

总开关 `campaignEnabled`（`Config/ConfigCampaign.cs`）属于**默认内容，恒为开启**，
不注册进 ModConfig UI，由 `ForceContentSystemSwitchesOn` 抹平老档残留的 false。
字段与 dormant 契约保留：关闭时不订阅存档、不注入公告板、不采集击杀、不发 token。

## 2. 关键文件与职责

| 文件 | 职责 |
| --- | --- |
| `CampaignTuning.cs` | 常量单点：存档 key、token 前缀、建筑 ID、笔记 key 前缀（四个冻结契约）、章节数、终章倍率/缩放/染色 |
| `CampaignModels.cs` | `CampaignChapterState` / `CampaignObjectiveKind` 枚举与章节、目标、进度模型 |
| `CampaignFacilityUnlocks.cs` | **与后山之间唯一的耦合面**：token 授予、权威查询、实时事件、换槽复位 |
| `CampaignPersistence.cs` | 槽位级存档门面：JSON 整存、写屏障、槽位烙印、`Store()` 只入队 |
| `CampaignSaveCoordinator.cs` | 征程**唯一** `SavesSystem.SaveFile` 调用点：基地场景闸 + deferred 重试预算 |
| `CampaignContentCatalog.cs` | 章节表：JSON 优先、校验不过**整表**回退硬编码；六章全量兜底 |
| `CampaignObjectiveTracker.cs` | 单局目标追踪，**完全不落盘**；武装/计数/计时/失败判定 |
| `CampaignObjectiveCollector.cs` | `Health.OnDead/OnHurt` 命名 handler，热路径零分配；近战与悬赏印记判定 |
| `CampaignProgressService.cs` | 状态机核心：状态推导、接约/放弃/交付、奖励与 token 授予 |
| `CampaignModeBridge.cs` | `partial ModBehaviour`：直读五个模式的私有状态 + 4 个 notify 漏斗 + 每帧 tick |
| `CampaignNoteBridge.cs` | 线索接入官方 NoteIndex；**两边都写**（列表 + 字典），fail-open |
| `CampaignDialoguePlayer.cs` | 交付剧情：复用 `DialogueManager` 与官方对话 UI 的原生立绘位 |
| `CampaignBoardBuilder.cs` | 公告板建筑注入（照日报报箱：反射 BuildingInfo、dormant 契约、老档幽灵防护） |
| `CampaignBoardView.cs` | 公告板面板（走 `Common/UI/BossRushUI.cs` 共享库） |
| `CampaignHud.cs` | 局内目标追踪条，未武装时早返；每帧先做零分配脏检查（只比整数与 bool），内容真变了才拼字符串写 TMP |
| `CampaignFinalBoss.cs` | 终章决战编排：召唤石维护、门禁、变体改造、让路策略 |

## 3. 架构与设计约定

### 3.1 对五个既有模式零重构

这是本系统最重要的约束。目标检测分三层，没有任何一层需要改模式的状态机：

1. **全局 Health 采集器**（零侵入）：`CampaignObjectiveCollector` 由
   `Utilities/PlayerLifecycleRuntimeHooks.cs` 转发官方静态事件，与日报、图鉴同一条管线。
2. **partial 状态桥轮询**（零侵入）：`CampaignModeBridge` 是 `partial ModBehaviour`，
   因此能直读 `modeDActive`、`modeEActive`、`modeFState`、`zombieModeRunState`、
   `currentEnemyIndex` 这些私有字段。整数比较的每帧成本可忽略。
3. **胜利/撤离漏斗**：四处各插一行 `NotifyCampaign*`，位置见 §5。

**标准竞技场没有 `currentWave` 字段**：它记的是 `currentEnemyIndex`（当前第几个敌人）
与 `bossesPerWave`，波次要现算，口径与 `WavesArena.cs` 的 `completedWave` 一致。

### 3.2 跨系统解锁契约

选静态 API 而非「后山去读征程的存档 key」：键名改一次两边静默失联，方法签名改一次编译期报错。

- `IsTokenGranted` / `GetGrantedTokens`：权威查询，**未装载存档时 fail-closed**。
- `OnFacilityTokenGranted`：**只在本会话真正新授予时触发**，读档回放不发事件。
  因此消费方必须在自身 init 与每次场景加载时全量查询，且**不得缓存查询结果**——
  否则玩家上次通关解锁的设施在重进游戏后会消失。
- 换槽：`LoadGrantedTokens` 整体替换而非追加；`ResetForSlotReload` 负责复位。

### 3.3 状态推导而非状态存储

存档里只存每章的 state 整数。「哪一章可接」是推导出来的：前一章 Completed 则本章
Available。这样调整章节表不需要迁移存档，也不会出现「存了 Available 但前置没过」的
自相矛盾状态。同时只允许一个进行中契约——两个并行会让局内 HUD 与目标采集互相打架。

### 3.4 终章：零新增 3D 资产

复用 `SpawnPhantomWitch` 的公开生成 API，生成后叠三层：`ApplyBossStatMultiplier`
补战役倍率、`localScale` 放大、`MaterialPropertyBlock` 绯红染色（照丧尸模式污染染色）。
官方 preset 在生成流程里已被克隆过一份，改 `nameKey` 只影响这一只。

**门禁**：legacy 生成会写标准竞技场的静态流程状态，因此开战前逐一检查六个模式标志，
任一激活即拒绝。玩家中途开了模式则战役主动中止让路，而不是去改路牌的 `IsInteractable`。

**用召唤石而非自动开战**：进场即刷 Boss 会抢掉玩家想跑的普通局，而且标准模式要等
玩家点路牌才置 `bossRushArenaActive`，自动触发恰好卡在那个窗口里。

**收尾有三条路径，缺一不可**（CR-2026-08-31-003 修复）：正常击杀走 `OnDeadEvent`；
玩家中途开了别的模式走让路 tick；**玩家打输或离场走场景回调 + 「Boss 已不在场」检测**。
第三条是重点——打输时 Boss 随场景销毁、死亡回调永远不会来，少了它
`campaignFinalBossActive` 会永久卡在 true，召唤石不再生成、终章再也打不了，
而终章恰恰是最可能打输的一场。另外生成是异步的，收尾会自增 `campaignFinalBossRunId`
作废在飞的生成协程，协程回来发现编号对不上就销毁产物，避免留下无人记账的强化女巫。
收尾还会清掉终章的局内追踪（终章不经模式桥武装，桥上那条「离开模式即 ResetSession」
永远轮不到它，不清则 HUD 在基地常驻）。

**召唤石维护的判定顺序是性能约束**：这是每帧路径，
`IsCurrentSceneValidBossRushArena()` 内部走 `GetActiveScene().name`，每次调用分配字符串。
因此零分配的终章契约查询必须排在前面短路，场景判定再按 scene generation 缓存
（`IsCampaignArenaSceneCached`）。见 AGENTS.md 4.12。（CR-2026-08-31-005 修复。）

### 3.5 线索接入官方 NoteIndex 的坑

官方 `NoteIndex.SetNoteDynamic(note)` 只调 `MSetEntryDynamic`，而后者**只写查询字典
`MDic`，不写 `notes` 列表**；而图鉴界面列条目走的是遍历 `notes`。只调 SetNoteDynamic
的结果是：按 key 查得到，界面里一条也看不见。正确做法是两边都写。

## 4. 剧情与文案约定

写法固定为「物证 + 一个精确到荒诞的细节 + 一句旁人证词」，证词轮流交给三位既有 NPC
（阿稳 / 叮当 / 羽织），让线索链同时把 mod 的老角色串进来。中间人每章两句：
对上一件物证的评述 + 指向下一章的钩子；终章三句收束。

调性硬约束：短句、不用抒情词、情绪靠留白、每段收在一个不解释的转折上、玩家永远沉默、
不写现实梗、不承诺未实现的机制（名人堂只读，不可招募）。

## 5. 既有文件触点

| 文件 | 改动 |
| --- | --- |
| `Utilities/PlayerLifecycleRuntimeHooks.cs` | ±2 行订阅/退订采集器 |
| `LootAndRewards/LootAndRewardsVictoryRewards.cs` | +1 行 `NotifyCampaignStandardCleared()` |
| `ModeD/ModeDWaves.cs` | +1 行 `NotifyCampaignModeDWaveComplete(modeDWaveIndex)` |
| `ModeF/ModeFExtraction.cs` | +1 行 `NotifyCampaignModeFExtracted()`，**必须在 ExitModeF 之前** |
| `ZombieMode/ZombieModeExtractionController.cs` | +1 行 `NotifyCampaignZombieExtracted()`，**必须早于场景切换** |
| `Integration/IntegrationDeferredBootstrap.cs` | +2 个 deferred 步骤（建筑注入、线索注册） |
| `Integration/BossRushIntegration_StartAndScene.cs` | 本地化注入 + 早期建筑注入 |
| `Common/Lifecycle/BossRushRuntimeModuleRegistration.cs` | 注册单实例，**必须排在后山之前** |

## 6. 冻结契约

- 存档 key `BossRush_Campaign_Progress_v1`
- token 前缀 `BossRush_Campaign_Unlock_Ch`（完整形态 `...Ch1` … `...Ch6`）
- 建筑 ID `bossrush_campaign_board`
- 笔记 key 前缀 `BossRushCampaign_`（同时进官方本地化键 `Note_{key}_Title/_Content`）

四条均由 `tests/CampaignSkeletonGuard.py` 钉住字面值，登记见 `docs/contracts.md` §3、§3.1。

## 7. 美术资产

`Assets/ui/campaign_presentation`（AssetBundle）：立绘 `campaign_portrait_broker` /
`campaign_portrait_champion`，章节海报 `campaign_poster_ch1` … `ch6`。
开发期允许 `Assets/ui/Campaign/*.png` raw 直读；建筑图标
`Assets/buildings/bossrush_campaign_board.png`。全部 **fail-open**——缺资源时
官方对话 UI 会自动隐藏立绘位，玩法一点不少。
Unity 侧构建器：`Assets/Editor/CampaignPresentationBundleBuilder.cs`。

## 8. 2026-08-31 交付与目标收口

`ReadyToDeliver` 现为存档权威状态：目标全部达成时立即写入，重启后仍可回公告板交付。
交付采用补偿式事务：先发奖金，再用独立存档副本把 Completed、设施 token 与线索一次入队；
入队失败原路撤回奖金，且不会提前发布后山解锁事件，避免反复交付刷钱或写失败却提前解锁。

第一章无伤阈值与数据表统一为前 2 波，避免 Boss 池缩小时不可完成；第三章文案与实际采集口径
统一为击败 8 名头目。终章祭坛只在 `ContractActive` 时出现，待交付阶段不会重复开战。

## 9. 2026-08-31 章节 JSON 与终章验收

正式章节来源新增 `Assets/Data/Campaign/Chapters.json`，内容与六章硬编码 fallback 完全一致。
`CampaignContentCatalog` 对 version、六章顺序、模式、阈值、奖励、唯一 token/线索及终章位置做
整表校验，任何错误整表 fallback，并公开 `Source` 与内容签名。构建脚本部署该 JSON；Dev F3
只把 `Json + 六章 + 签名匹配` 判为通过，fallback 只承担灾备。

首次实机 F3 发现 Unity `JsonUtility` 对该二级对象数组只填 version、把 chapters 静默留成 null，
即使源文件与部署文件哈希完全一致也会 fallback。当前实现改为复用 Mode H 已在生产表使用的严格
token parser（支持 BOM、严格数字/字符串/数组类型），再映射到只读章节模型；守卫禁止退回
`JsonUtility.FromJson`。运行时复测必须报告 `source=Json`。

终章冠军之影死亡表现由 Campaign 独占：幽灵女巫公共清理照常执行，但普通 Boss 的胜利文案与
stinger 在终章受抑制，确保最终文案、`RunVictory` 与 stinger 各一次。
