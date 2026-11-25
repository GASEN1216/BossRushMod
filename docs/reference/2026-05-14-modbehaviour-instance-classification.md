# 2026-05-14 ModBehaviour.Instance Classification

## Baseline

- 2026-10-08：沙暴冠军工厂新增 1 处当前宿主身份核对，归 Keep: Unity owner；防止旧 owner 的异步生成被新会话接收。技能音效用注入的宿主引用。

- 2026-09-27 合并后复算：新增后山变身、丧尸 Boss 及初始出生读取按实际位置计数；保留 P3 owner 迁移和 SkyIsland 正式目录。初始出生仍在主角销毁前冻结同一入场判据。

- Command: `rg -n "ModBehaviour\\.Instance" --glob "*.cs"`
- Raw matches: 412
- 2026-11-20：异步物品恢复先记录宿主、跨帧后再核对身份，两处归 Keep: Unity owner。GetModPath 在宿主类内直接使用 Instance，通过官方 info.path 读取启用目录。
- 2026-09-24 P3：DeathWraith 异步生成与 Wedding 配偶恢复迁入各自 RuntimeModule 后，两处原先在宿主内使用的 `Instance` 校验改为显式 `ModBehaviour.Instance == owner`。行数增加 2，旧请求隔离条件保持不变。
- 2026-09-24 P3/P4：Integration 的当前净值从 270 降为 267。临时哥布林商店由创建者传入现金 / 净化点策略，通用商店不再用 `ModBehaviour.Instance` 猜测丧尸模式；各运行时模块保留异步请求的宿主身份校验。
- Current event-bus pilot: achievement popup notification only.
- Guard evidence: `BossRushEventBusLifecycleGuard.py` PASS; `LongTermGoalNonGoalGuard.py` still blocks broad `EventBus`, `IGameWorldProbe`, and `IBossRushEventSubscriber` abstractions.

## Classification Policy

| Category | Meaning | Current action |
|---|---|---|
| Keep: Unity owner | Needs the live mod instance for coroutine start/stop, `gameObject`, scene state, or Unity object ownership. | Retain until a local owner is introduced. |
| Keep: gameplay state | Reads or mutates active mode state, run id, spawn points, boss flow, temporary NPC economy, or core combat lifecycle. | Retain; do not route through event bus in this pass. |
| Candidate: notification | `ShowMessage`, `ShowBigBanner`, `PlaySoundEffect`, small UI refresh signals. | Migrate only when low-risk and guarded. Achievement notification already moved to `BossRushEventBus`. |
| Candidate: service query | NPC/shop/reforge/courier checks that ask whether a transform belongs to ZombieMode temporary service state. | Keep for now because the service owner is still `ModBehaviour`; document for later local interface extraction. |
| Debug/manual | F3, teleport monitor, manual debug actions. | Retain. |

## Current File Grouping

| Area | Matches | Classification | Evidence / reason |
|---|---:|---|---|
| `Integration/` | 267 | mixed: Unity owner, gameplay state, temporary NPC service query, notification | 2026-11-25 −3：冒险家日志（`Integration/WikiUIManager.cs`）改版为水墨书卷界面，关闭淡出由 `BossRushInkSkin.PlayInkDismiss` 与面板自身 `BossRushInkMotion` 驱动，不再借 `ModBehaviour.Instance` 启停淡出协程（2026-09-24 +3 的那 3 行撤除）。 2026-11-19 −1：`DragonKingAssetManager` 的异步 `LoadAssetBundle`（无调用方）随 AssetBundle 加载统一删除，它捕获宿主的那一行一起去掉（见表下 2026-11-19 第二段）。 2026-09-24 +5：菜地收获横幅 +2（见表下 2026-09-24 段）；百科面板开关加淡入淡出（UI 共识 A-34），`WikiUIManager` 关闭时的淡出协程挂在宿主上（启动 2 行、停止 1 行），属 Keep: Unity owner（协程宿主）；面板自己在关闭时会被 SetActive(false)，协程不能挂在自己身上。 2026-09-23 −6：UI 与特效审美修复把许愿台、重铸、好感、婚礼里几处经宿主转发的音效 / 横幅改走 `Integration/UI/IntegrationUIFeedback.cs` 与共享 UI 音效入口（−8），阿稳扫箱与寄存的确认 / 横幅新增两处宿主取法（+2，Keep: notification）。2026-09-22 −8：自建战利品展示柜面板退役（ShowcaseUI 删除，陈列改接官方陈列柜；扫描器与标签注入器不用 Instance）。Most usages are NPC/reward/reforge/courier/DragonKing/PhantomWitch wiring. They touch active run state, temporary NPC currency, coroutine owners, or audio/banner notifications. 2026-08-28 +5：日报报箱交互（3）、战绩采集门控（1）、日报面板横幅（1），三处都属 Keep 类别（交互回调宿主、开关查询、通知）。2026-09-04 +4：常驻捏脸 NPC 交互体（`PermanentDuckNpcInteractable`）读配偶跟随/离婚/回家三个选项的可见性，外加一次 null 判空。它是 MonoBehaviour、手上没有 owner 引用，`ModBehaviour.Instance` 是这类交互体的既定取法（与 Interactables/ 同款），且调用前已判空，属 Keep 类别。2026-09-05 +1（CR-2026-09-05-018）：`PermanentDuckNpcModule.IsSpawnRequestValid` 将捕获的 owner 与当前实例比对，阻止旧宿主的后继/迟到请求在新 runtime 登记 NPC，属 Keep: Unity owner；不是新增全局服务依赖。 2026-09-06 +4：冰霜/雷霆套装开放获取与回血取证。`SetBonusBossDropHandler.ShouldDeferToBossRushLootbox`（1）经宿主查 defer 判定，形态与 `FrostmourneBlueBossDropHandler` 完全一致；`SetBonusDamageObservation`（2）是 `Health.Hurt` 的 IL 观察补丁，prefix/取值两处都要拿当前宿主校验套装是否激活、并比对观察上下文归属；另 1 条来自同期交互体/图鉴基类归一化（`ShowcaseInteractable`、`CodexBossCatalog`、`DailyReportInteractable` 增删相抵后的净值）。三处均属 Keep 类别（Harmony 补丁入口、交互体宿主取法），不是新增全局服务依赖。 2026-09-07 +2：P0 五把新武器开放获取与表现层。`NewWeaponBossDropHandler.ShouldDeferToBossRushLootbox`（1）经宿主查 defer 判定，形态与 `SetBonusBossDropHandler` 完全一致；`NewWeaponFx.PlaySound`（1）经宿主播放触发音效，与 `FrostSetBonus_Nova` 走同一个 `PlaySoundEffect` 入口。两处均属 Keep 类别（额外掉落 defer 协议入口、音效通知），不是新增全局服务依赖。 |
| `ZombieMode/` | 40 | gameplay state and runtime owner | Runtime components ask for `ZombieModeCurrentRunId`, pause state, reward UI, temporary NPC service opening, and projectile/reward effects. These stay direct to avoid changing mode behavior. |
| `Interactables/` | 22 | gameplay command and UI notification | 2026-11-24 −1：竞技场路牌的三个阵容子选项收进「出征令」面板，原来读无间每波 Boss 数的两行合成一次宿主取法。BossRush sign, difficulty selection, lootbox return/clear actions call active mode commands. The Mode G entry path reuses one captured host instead of repeatedly resolving the singleton; the remaining calls are player-facing commands and should not be event-bus migrated without smoke. |
| `ModeE/` | 26 | gameplay state / cached instance | Harmony patches and Mode E merchant/UI use the current active mode state and cached instance; guarded by Mode E/F no-gameplay-throttle and parity tests. |
| `ModeF/` | 6 | gameplay state / UI | Mode F bounty radar/merchant/transponder paths use active Mode F session state. |
| `Campaign/` | 12 | notification + gameplay state | 2026-09-22 −4：公告板自绘面板退役（CampaignBoardView 删除，征程改由官方 Jeff 发放；官方任务客户端持模块引用，不用 Instance）。2026-09-18 +1：切槽主动取消终章生成并回收 Boss，防旧槽异步结果进入新槽。2026-08-30 新增。鸭王征程用它做三件事：玩家可见通知（`ShowMessage`：接约/交付/线索到手）、开关与波次查询（采集器与桥读活动模式状态）、以及公告板面板的宿主。2026-09-03 +1：终章冠军独白拿不到对话 actor 时的飘字兜底（`PlayFinalBossPrologueAsync`），与既有交付剧情的兜底同款。全部属 Keep 类别——契约状态机本来就长在 `ModBehaviour` 的 partial 上（`CampaignModeBridge`），走事件总线反而要把私有模式状态再导出一遍。 |
| `Audio/` | 9 | candidate notification + Unity owner | Audio manager uses `ModBehaviour.Instance` as the component host and sound playback bridge. It is a later candidate for a narrow audio service, not a broad event bus. 2026-08-30 +1：`BossBgmCoordinator` 经它播 stinger（复用既有 `PlaySoundEffect`，不另起音频通道）。 |
| `Patches/` | 10 | patch entrypoint / Unity owner | Harmony patches need the current mod singleton to route base-game callbacks into the mod. `MagicBlendInitializationOrderPatch` additionally uses it as a coroutine owner while waiting for the official `MagicBlending.Start()` initialization, then replays the same state entry. Async item restoration captures the owner and checks it again after yielding. |
| `SkyIsland/` | 3 | Keep: Unity owner / gameplay state | 2026-09-26：从 DebugAndTools 原样迁出，三个居民交互宿主查询保持。 |
| `MapSelection/` | 3 | gameplay command | Map selection must call active mod entry/exit state. |
| `WavesArena/` | 2 | Keep: gameplay command / gameplay state | 2026-11-24 +2：出征令面板打开时取宿主读启用 Boss 池、立誓后经宿主开局（`ArenaExpeditionOrderPanel.Open`）；局内战帖朱印每帧查宿主 `IsActive` 判本局是否在跑（`ArenaExpeditionHud.IsRunLive`）。两处都是玩家入口与模式状态，与 `Interactables/` 同型。 |
| `ModeG/` | 4 | gameplay state / Unity owner | Mode G uses the live mod instance for entry, presentation and managed runtime ownership; these calls stay direct to preserve the run transaction boundary. |
| `ModeH/` | 1 |
| `RandomEvents/` | 5 | gameplay command / Unity owner | Mode H 场内交互只在一个解析器里取活动 mod 实例，其余路径复用捕获的 host，保持入口事务边界。 |
| `PetNest/` | 1 | candidate notification | 2026-09-20 新增：孵化揭晓演出在抽到异色时播放许愿台的大奖音乐（`PetNestHatchRevealView.PlayJackpotMusic` 经宿主 `PlaySoundEffect`），与 `NewWeaponFx.PlaySound`、`FrostSetBonus_Nova` 同一个入口。属 Keep: 通知/音效，不是新增全局服务依赖；遗种巢其余路径一律走运行时模块持有的 owner 引用。 |
| `ModeD`, `DebugAndTools` | 1 | debug/manual or mode command | Mode D 保留模式指令入口。F3 runner 固定绑定启动宿主，并在该宿主销毁时收尾。 |

## Already Migrated

- `Achievement/BossRushAchievementManager.cs` publishes `BossRushAchievementUnlockedEvent`.
- `Achievement/AchievementRuntimeHooks.cs` subscribes/unsubscribes and owns `SteamAchievementPopup.Show`.
- `Common/Events/BossRushEventBus.cs` is reset in `AlwaysOnRuntimeHooks` and guarded by `BossRushEventBusLifecycleGuard.py`.

## Findings

- The current raw matches are classified; broad replacement remains out of scope because it would touch combat, reward, UI, service, and patch entrypoints at once.
- The low-risk notification pilot is complete for achievements. Other notification candidates are documented but intentionally not migrated in this pass because the user required no player-visible behavior changes.
- Any future migration should be one narrow event at a time, with a dedicated lifecycle guard and runtime smoke for the affected workflow.

## Guard Coverage

- `ModBehaviourInstanceClassificationGuard.py` locks the current raw count, file grouping, classification policy, migrated achievement notification, and explicit non-migration reasons.
- `BossRushEventBusLifecycleGuard.py` locks the controlled achievement notification pilot and subscriber cleanup.
- `LongTermGoalNonGoalGuard.py` continues to block broad generic abstractions such as `EventBus`, `IGameWorldProbe`, and `IBossRushEventSubscriber`.

## Current Completion Status

Classification is complete for the current raw count, and Batch Final-5 is source-side complete under the report's "migrate low-risk notification or document the retention reason" criteria. Broad decoupling remains a future long-term goal, not a completion gate for this pass, because the remaining direct singleton calls are gameplay state, Unity-owner, service-query, patch-entrypoint, debug/manual, or smoke-required notification paths.

2026-09-17（COMPAT）：词缀延迟殉爆取一次协程宿主、执行时核对宿主身份（2），共享变异死亡转发取一次协程宿主（1）。三处均为 Keep: Unity owner；装备 context / 变异 context 失效时取消旧结算，不增加全局状态或消息总线。

2026-09-17（集成登记）：并行婚姻衔接修复在 `SkyIslandResidentInteractable` 增加三处宿主解析，用于查询当前剧情会话、打开配偶剧情对话及补挂岛上官方给予者，归 Keep: Unity owner / gameplay state。该生产变更由原会话负责，本轮仅同步计数与归类。

2026-09-20（COMPAT）：词缀选物刷新增加 4 条宿主引用：ScheduleAffixSelectionRefresh 的判空/启动与 StopAffixSelectionRefresh 的判空/取消。属于 Keep: Unity owner，用于在官方 UI 回调完成后的下一帧刷新；关闭、切模式及 Cleanup 取消，不新增轮询或全局服务。

2026-09-20 +1: DragonKingAssetManager captures the current ModBehaviour.Instance once as Keep: Unity owner, cancelling asynchronous bundle loading when that host is destroyed.

2026-09-22 审计修复：事务捕获宿主、异步请求核对当前宿主、飞行/武器按实际持有者启动均归 Keep: Unity owner；寄存统一 owner 替代分散的静态查询，减少重复 Instance 取用。上表按本轮最终生产代码重新计数，不扩大解耦范围。

2026-09-24（COMPAT / WIRE+）：菜地收获横幅新增两处宿主引用，均属 Keep: Unity owner。Harmony 入口捕获当前宿主，交付完成时核对宿主仍为同一实例，阻止卸载或重载后的旧异步通知；复用现有 ShowBigBanner，不增加服务层或每帧扫描。

2026-09-26（COMPAT）：丧尸防御在官方扣血前经 `ZombieModeDamageRuntime` 捕获当前宿主一次（+1，Keep: gameplay state / Unity owner），核对活动模式与 run marker 后消费护盾和减伤。静态伤害入口没有实例 owner，不增加第二份全局状态。

2026-09-27（COMPAT）：新增龙裔技能提示音入口（`DragonDescendantAbilities_ProjectilesAndGrenades.PlayDragonCue`，火箭预警与二阶段冲刺起手共用）和龙王二段冲刺预警音（`DragonKingAbilityController_ProjectileAndMovement`）两处宿主音效调用，归 Candidate: notification，本轮保留既有 `PlaySoundEffect` 入口；幽灵女巫起手音复用 `NewWeaponFx.PlaySound`，不新增宿主取法。计数从 Integration 267 / 总 410 更新为 269 / 412，未增加服务层、全局扫描或第二份音频 owner。

2026-10-24（COMPAT）：冠军之影入池适配器 `SandstormChampionPoolAdapter.SpawnAsync` 的有效期委托比对 `ModBehaviour.Instance == owner`，阻止旧宿主的迟到异步结果在新 runtime 登记角色，属 Keep: Unity owner（宿主身份校验），与 `PermanentDuckNpcModule.IsSpawnRequestValid` 同型，不是新增全局服务依赖。计数从 Integration 270 / 总 413 更新为 271 / 414。

2026-11-19（COMPAT）：地图缩略图改走 `ModSprites` 按路径取图后，`BossRushMapSelectionHelper.GetModPath()`（经 `ModBehaviour.Instance` 反射读 ModInfo.path）不再有调用方，随之删除。计数从 MapSelection 4 / 总 414 更新为 3 / 413。

2026-11-19（COMPAT）：AssetBundle 加载与「找已加载包」统一到 `ResourceBundleLoader` 时，删除没有调用方的 `DragonKingAssetManager.LoadAssetBundle`（异步预载，2026-09-20 +1 那一行 `ModBehaviour owner = ModBehaviour.Instance;` 在其中）；手持武器预载 `PrepareForHeldWeapon` 与 Boss 同步加载不变。计数从 Integration 271 / 总 413 更新为 270 / 412。

2026-11-24（COMPAT / WIRE+）：尸潮寻路节流 `ZombieModePathRequestThrottlePatch`（官方 `AI_PathControl.MoveToPos` 的 Harmony 前缀）第一句取 `ModBehaviour.Instance` 判丧尸模式是否激活、再查本局在册丧尸的 marker，属 Keep: gameplay state（Harmony 入口路由到模式状态），与同目录其他丧尸补丁同型，不是新增全局服务依赖。计数从 ZombieMode 39 / 总 413 更新为 40 / 414。

2026-11-24（COMPAT）：玩家许愿批 A「出征令」把竞技场路牌的阵容子选项收进新面板。`Interactables/` 净 −1（23 → 22），新增 `WavesArena/` 2 行（面板读池与开局、局内朱印查本局是否在跑），均属 Keep 类别，不是新增全局服务依赖。计数从总 414 更新为 415。

2026-11-25（COMPAT）：冒险家日志（`Integration/WikiUIManager.cs`）改版为纯代码水墨书卷界面，关闭淡出由 `BossRushInkSkin.PlayInkDismiss` 与面板自身 `BossRushInkMotion` 驱动，去掉原 3 处 `ModBehaviour.Instance` 协程启停引用。`Integration/` 计数从 270 降为 267，总计数从 415 降为 412。
