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
    - Campaign/CampaignQuestTable.cs
    - Campaign/CampaignBaseObjectives.cs
    - Campaign/CampaignOfficialQuestClient.cs
    - Campaign/CampaignBoardInteractable.cs
    - Campaign/CampaignBoardBuilder.cs
    - Campaign/CampaignHud.cs
    - Campaign/CampaignFinalBoss.cs
    - Campaign/CampaignFinalBossInteractable.cs
    - Config/ConfigCampaign.cs
    - Localization/CampaignLocalization.cs
    - tests/CampaignSkeletonGuard.py
    - Utilities/OfficialQuests/OfficialQuestProjection.cs
---

## 1. 系统概述

### 2026-09-22 改由官方 Jeff 发放、换新故事《册子上的名字》（COMPAT / SCHEMA+ / WIRE+）

**本节之后的旧内容凡提到「公告板」「自绘六章面板」「中间人」「名人堂 32 席」的，都已过时。**

- 发放链路：六章投影成官方 Quest `590101`–`590106`（`590100 + order`，给予者官方 Jeff=1，2026-09-22 owner 授权），
  经共享投影核心 `Utilities/OfficialQuests/`（由天空岛桥抽出，唯一实例、四个 Harmony 补丁只装一次）登记。
  `Campaign/CampaignOfficialQuestClient.cs` 是客户端，`Campaign/CampaignQuestTable.cs` 是纯规则表（ID 映射、可接取 / 可交付 / 目标完成判据、目标行文案）。
  权威仍是 `CampaignProgressService`：可接取↔`Available`、官方接受→`TryAcceptContract`、Task 行←本局进度 / 基地侧事实、官方「完成任务」→`TryDeliver`
  （发钱 / token / 线索仍归交付事务，官方奖励行只展示 `def.RewardCash`）。官方 UI 没有放弃入口，`TryAbandonContract` 只留 Dev 演练。
- 公告板退役：`CampaignBoardView.cs` 删除；`CampaignBoardBuilder` 只在老档已建过（三态 `ProbeExistingCampaignBoards`，`Unknown` 不当 `None`）时注入 info + prefab，
  新档不进建造菜单；已建的互动只飘字「找杰夫」。
- 新目标类型 `garden_built` / `trophy_displayed`（基地侧：不进 `CampaignObjectiveTracker`、不落盘，事实由 `Integration/BackMountain` 经 `CampaignBaseObjectives` 注册提供者给出；
  `ReadyToDeliver` 仍只由局内目标同局达成触发，交付另核对基地侧目标全真）。ch2 加「在基地建好菜地」、ch3 加「摆上一件战利品」、ch5 波次门 4→5；
  `chapterId` / `clueId` / token / 奖金不变（冻结说明见 `docs/contracts.md` §3.2）。
- 文案：任务标题 / 说明（`BossRush_Campaign_chN_Name/_Description`）、线索、交付对话、终章独白全部换成杰夫口吻；说话人直接用官方 `Character_Jeff`；召唤石改名报名石。
  交付后顺序：官方完成面板 → 杰夫对话 → 解锁飘字（`CampaignContentCatalog.GetDeliveredNotice`）。
- 守卫 / 回归：`CampaignSkeletonGuard`（ID 冻结、`Campaign/` 不含官方任务符号、`PayReward = null`、交付先落事实再排对话）、`CampaignFlowGuard`、
  `CampaignPlayability`（任务表判据穷举、基地侧目标不武装）、`OfficialQuestProjectionGuard`。


鸭王征程是 mod 的**第一个剧情系统**：一条六章的悬赏契约线，把既有的五个玩法入口
串成一次调查。玩家在基地建「征程公告板」接约，进指定模式完成特殊目标，回来交付，
听中间人讲一段，拿到一件「证物」写进官方笔记图鉴；前三章各解锁一处竞技场后山设施。

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
| `CampaignPersistence.cs` | 槽位级存档门面：DTO、key / schema、JsonUtility 编解码绑定、token 发布与下游复位；JSON 整存、写屏障、槽位烙印、`Store()` 只入队在共享的 `Common/Lifecycle/BossRushSlotJsonStore.cs` |
| `CampaignSaveCoordinator.cs` | 征程**唯一**物理落盘入口：门面持有 `Common/Lifecycle/BossRushSaveCoordinatorEngine.cs` 实例（基地场景闸 + deferred 重试预算）并携带现金快照义务；`SaveFile` 本身只在引擎里 |
| `CampaignContentCatalog.cs` | 章节表：JSON 优先、校验不过**整表**回退硬编码；六章全量兜底 |
| `CampaignObjectiveTracker.cs` | 单局目标追踪，**完全不落盘**；武装/计数/计时/失败判定 |
| `CampaignObjectiveCollector.cs` | `Health.OnDead/OnHurt` 命名 handler，热路径零分配；近战与悬赏印记判定 |
| `CampaignProgressService.cs` | 状态机核心：状态推导、接约/放弃/交付、奖励与 token 授予 |
| `CampaignModeBridge.cs` | `partial ModBehaviour`：直读五个模式的私有状态 + 4 个 notify 漏斗 + 每帧 tick |
| `CampaignNoteBridge.cs` | 线索接入官方 NoteIndex；**两边都写**（列表 + 字典），fail-open |
| `CampaignDialoguePlayer.cs` | 交付剧情 + 终章冠军独白：复用 `DialogueManager` 与官方对话 UI 的原生立绘位。**两个说话人各有独立 actor 宿主 GameObject**——`DialogueActorFactory` 的缓存按 GameObject 索引，`Create` 命中缓存时会忽略传入的 actorId/nameKey/portrait，共用宿主会让冠军顶着中间人的名字和立绘说话 |
| `CampaignBoardBuilder.cs` | 公告板建筑注入（照日报报箱：反射 BuildingInfo、dormant 契约、老档幽灵防护） |
| `CampaignBoardView.cs` | 独立公告板面板：共享皮肤、模态输入租约、Esc 和语言刷新；旧宿主入口在 ModeBridge 薄转发 |
| `CampaignHud.cs` | 局内目标追踪条，未武装时早返；每帧先做零分配脏检查（只比整数与 bool），内容真变了才拼字符串写 TMP |
| `CampaignFinalBoss.cs` | 终章决战编排：召唤石维护、门禁、变体改造、让路策略、**开战前冠军独白**（`StartCampaignFinalBossPrologueThenSpawnAsync` 先 await 独白再生成 Boss；F3 的 `DebugStartCampaignFinalBossForValidation` 刻意直连 `StartCampaignFinalBossAsync` 绕过独白，否则对话要等玩家点击才 resolve，验收会一路等到超时记 `spawn_timeout`）|

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

#### 武装时机（2026-09-03 修正，CR-2026-09-03-011）

追踪按**开局**武装，不是按进场景：

- **标准竞技场**必须 `bossRushArenaActive && IsActive`。
  `bossRushArenaActive == true && IsActive == false` 是一等长存状态——整个大厅期都是它
  （`WavesArenaEnemyMaintenance` 的持续清怪循环和路牌可交互判定都依赖这一点）。
  只看前者会让第 1 章的无伤目标在玩家走去路牌的路上挨一下伤就被判死，
  且胜利后（`IsActive` 已复位、`bossRushArenaActive` 仍为真）追踪还赖着不走。
- **丧尸**用 `ZombieModePhaseGuards.IsRunActive`（`IsZombieModeActive` 用的就是它），
  不用 `LifecyclePhase != None`——后者从 `SelectingMap` 就为真，
  会让第 5 章在玩家还在基地点地图选择界面时就武装。
  ⚠️ `IsRunActive` 在 `ZombieModePhaseGuards` 上，**不是** `ZombieModeTuning`，
  两者同在 `ZombieModeTuning.cs` 一个文件里，按文件定位极易记错类名。
- **换模式必须先解除**：`EnsureArmedFor` 在「当前章节模式 ≠ 传入模式」时先 `ResetSession()`
  再走后续判定。否则接了第 1 章去打 Mode E，`_armedMode` 会停在 `"standard"`，
  Mode E 里挨一下伤就把第 1 章的无伤目标判死（Mode E 在 `GetCampaignCurrentWave` 返回 0）。

胜利结算不受影响：`SetBossRushRuntimeActive(false)` 与 `NotifyCampaignStandardCleared()`
之间没有 await，且四条 Notify 漏斗各自会先调 `EnsureArmedFor`，此时仍是本局武装状态。

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
补战役倍率、工厂 `extraModelScale` 在碰撞体缓存前缩放、`MaterialPropertyBlock` 绯红染色。
官方 preset 在生成流程里已被克隆过一份，改 `nameKey` 只影响这一只。

**门禁与隔离**：复用女巫工厂的 `isNonWaveSpawn: true`，不登记标准波次；只在终章契约进行中、主玩家存活且场上没有其它模式时显示/允许召唤。玩家中途开了模式则征程取消独白、回收 Boss 并让路，不修改路牌入口。

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

## 2026-09-02 终章生成中止与掉落清理

兼容分类：COMPAT。第五轮终章 spawn_timeout 的日志证明 H 残留入场意图在重访地图后重开赛季，触发决战主动让路；并非证据表明女巫资源无法生成。H 成功创建赛季后消费意图，保持终章对真实活动模式的既有互斥规则。

`CampaignFinalBoss` 在生成编号失配的迟到分支与主动 destroyBoss 清理分支，先 `ClearBossRandomLootTracking` 再 Destroy；自然死亡不提前解除掉落回调。配合 Integration 场景订阅回收，避免最后一只 Boss 被销毁后静态熔石追踪留到下次刷怪。第六轮 `BossRushValidation_20260902_140735_794.log` 已实机确认终章 death_presentations=1、bgm_owners=0，最终熔石及其它被测订阅全部归零。H 重访不再抢占终章；主动中止/迟到生成故障注入仍独立保留。

章节来源：`Campaign/CampaignFinalBoss.cs`、`ModeH/ModeHRuntimeModule_SceneFlow.cs`、`Integration/IntegrationRuntimeHooks.cs`。

## 2026-09-04 审核修复

**落盘重试链不再被自身消费。** `CampaignSaveCoordinator.FlushBatch` 先 `FlushPending()`
（成功即消费 pending，`HasPendingWrite` 随之变 false），再 `SavesSystem.SaveFile(false)`。
旧写法开头只用 `HasPendingWrite` 判断「有没有事要做」，于是 `SaveFile` 失败后置起的重试标记
在下一帧命中该早返直接返回成功——**Tick 重试与宿主销毁兜底一起失效**，进度停在
SavesSystem 内存里从不落盘。现新增独立的 `_saveFilePending`（欠一次 SaveFile），
早返同时看它，只有 SaveFile 真正成功才清除。`Integration/Codex/CodexSaveCoordinator.cs`
是同一形态，已同批修复。2026-09-06 起这套状态机只存在于共享引擎
`Common/Lifecycle/BossRushSaveCoordinatorEngine.cs`，四个内容子系统各持一个实例，
同类修复不再需要逐份重做；公告板 / 终章召唤石交互体的骨架也收进了
`Interactables/BossRushBuildingInteractableBase.cs`。


## 2026-09-05 余额与完成标记同批保存

`COMPAT`。官方 `EconomyManager.Add` 不采集存档，`SaveFile(false)` 不调用 `OnCollectSaveData`。现金发放前通过 `CampaignSaveCoordinator.TryPrepareCashReward` 确认当前槽、经济实例与存档就绪，并登记余额采集义务。协调器和官方采集回调都先通过 `GenerateSaveData` 保存官方 `EconomyData`，再刷新战役完成标记。

余额采集义务与 typed pending、物理写欠账分开保存；任何采集失败、物理失败、基地门禁或同帧节流都不会清除该义务。后续重试重新采集实时余额，避免复用奖励发放时的旧快照覆盖之后的收入/支出。物理 SaveFile 成功后才清除，切槽与卸载清空旧槽会话义务。原有存档键、schema、金额、状态机和退款策略保持兼容。

回归：`tests/ContentCashSnapshotGuard.py`、`tests/fixtures/ContentTransactions/run.py`。夹具直接编译完整战役进度和持久化源码，模拟缓存与物理文件边界、采集失败、物理失败、节流、官方采集和切槽；Unity/ES3 与公告板 UI 仍需实机验证。

## 2026-09-06 建筑注入器归属收口（D-1）

`SAFE / COMPAT`。报箱、征程公告板、后山展示柜、遗种巢的建筑实现分别归 `DailyReportMailboxBuilder`、`CampaignBoardBuilder`、`ShowcaseBuildingBuilder`、`PetNestBuilder` 四个模块类型，各自持有创建它的 `ModBehaviour _owner`。原有 init、early、restore、notes、slot-change、cleanup 入口保留在 `Integration/ContentBuildingBridges.cs` 薄转发；同一宿主内复用模块实例，既有场景装配顺序、事件退订、恢复协程和清理义务不变。

官方建筑反射绑定共用 `Common/Buildings/BuildingInjectionHelper.cs`，包括查询失败结果的一次解析缓存。模型包围盒、shader 与碰撞体工具共用 `Common/Buildings/BuildingModelHelper.cs`；报箱经 owner 的只读模型属性借许愿台现有缓存，加载/卸载仍归许愿台。基地重绘保留唯一 ModBehaviour 协程，由模块显式请求。没有更改建筑 ID、prefab 名、造价、建造条件或官方存档格式。

验证：`tests/ContentBuildingOwnershipGuard.py` 与 `tests/fixtures/ContentBuildingOwnership/run.py`。实际共享反射工具和宿主桥的执行回归覆盖反射契约、容器赋值、调用顺序与 owner 隔离；不替代 Unity 旧档建筑恢复和建造交互 smoke。实现总述见 `.qoder/repowiki/zh/content/架构设计/内容建筑模块归属.md`。

## 2026-09-07 界面可读性与视觉整理（COMPAT）

`Campaign/CampaignBoardView.cs` 改为 1040×840 公告板：标题和关闭入口固定，六章正文放入带滑块的独立 ScrollRect。每条目标另起一行，18 号正文按实际高度扩展卡片，标题、目标与右侧动作互相让位；不再把整章目标压进固定 40px 高度。公告板和 `Integration/BackMountain/ShowcaseUI.cs` 的标题背景都锚到面板左右两端，修复旧版只占右半边导致的偏移。章节状态、接约/交付/放弃与登记奖励不变；实机操作和双语文本待验。


## 2026-09-17 无伤目标与可完成路径

CampaignObjectiveCollector 只把正 finalDamage 计为受伤，避免官方零伤害事件误败第一章。CampaignPlayability 链接实际六章数据、追踪器和采集器，覆盖无伤边界、所有章节完成输入、暂停/撤离、非玩家击杀、写入重试与换局。恢复旧受击判定会报错；各模式实机 notify 仍需 L3，不能将目标输入回归当作通关实证。

## 2026-09-18 可玩性与生产接线复核（COMPAT / SAFE）

六章定位保持：标准无伤技巧 → 白手起家近战取舍 → 阵营敌对战斗 → 悬赏后撤离 → 尸潮撤离 → 冠军决战。第二章在活动近战契约下保证走原近战配装入口；共用整备同时服务 E/F，调用必须带 `modeDActive`。第三章保留八名敌方头目，移除十分钟纯等待；敌我关系走官方 `Team.IsEnemy`，友军和中立不计数。未调整奖金、TypeID、存档字段或身份键；达标/已交付旧档继续有效，活动第三章按新目标开始下一局。

完成态冻结，不再次武装或继续增长计数；完成入队失败保留本会话终点事实，离场后仍每秒重试入队，物理保存仍只有共享协调器。换槽清该事实并取消终章、对话及面板；进程崩溃前从未成功写入的事件不具备跨重启保证。

公告板给出入场准备、设施回报和丧尸最早第五波 Boss 后撤离的说明，复用现有模态租约；HUD 脏检查包含语言并按实际文本量高。线索以 Mod 当前槽为权威，修复官方列表已有但字典缺失，并撤回非权威镜像；存档读故障时不以空集撤回。对话 token 沿用共享管理器的 owner 清理，取消旧等待不影响后继会话。

终章生成编号隔离旧成功/旧异常，返回无 Health/已死亡产物也回收。落点复用关卡点和 `SpawnPositionHelper`，召唤石采样至多每秒一次。程序化公告板/召唤石复用现有 URP shader，纹理、sprite、材质与染色工具归 `CampaignAssetCache` 的现有账本；没有另建缓存或调度器。

执行证据：`CampaignPlayability`、`ContentTransactions`；结构接线：`CampaignFlowGuard`。新增内容必须同时维护 JSON、硬编码 fallback 与签名校验，不能承诺只改 JSON 即热扩展。详细范围、失败记录与实机清单见 `docs/代码审查/2026-09-18-鸭王征程生产审核与体验优化.md`。离线结果不证明实战难度、物理/渲染或帧时间。
