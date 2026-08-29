# CODE_REVIEW_FINDINGS.md — 已确认问题库

> 只记录 confirmed findings。未验证线索放本文件的 UNVERIFIED 区，或在 `FIX_TRACKER.md` 中标为 `accepted/deferred/refuted/documented`。

## 状态汇总

| 严重级 | Open | Fixed | Deferred | WontFix | 合计 |
| --- | ---: | ---: | ---: | ---: | ---: |
| P0 | 0 | 7 | 0 | 0 | 7 |
| P1 | 0 | 11 | 0 | 0 | 11 |
| P2 | 0 | 15 | 1 | 0 | 16 |
| P3 | 0 | 7 | 1 | 0 | 8 |

最后更新：2026-08-29（四系统复审全面修复完成：CR-2026-08-29-008..021 全部 Fixed，
修复内容与验证见 `FIX_TRACKER.md` 的「四系统复审全面修复」条目。
Open 计数按问题条目计，分组条目内多项分别计数；两项 Deferred 分别是
模式H「赔率页没有赔率」（并入下一批战斗/押注接线）与日报「未读提示不持久」
（需新增可选存档字段，留 owner 拍板）。上午修复轮的 CR-2026-08-29-001..007
未单独立条，见 `FIX_TRACKER.md` 四个修复包）。

## Confirmed Findings

### CR-2026-07-05-001：焚天龙铳切弹时容量 baseline 会被取整反推污染

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：代码审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 守卫文件：`tests/DragonKingBossGunReforgeBaselineGuard.py`

#### 问题

弹种属性切换时优先从当前已套 profile 的 Stat 反推 baseline，而不是优先使用已缓存 baseline。`Capacity` 写入前会取整，重铸过容量后连续切弹可能把真实弹匣基准反推歪。

#### 修复

`CaptureAmmoStatBaseline()` 改为优先返回 `statBaselineByItemInstance` 中的真实基准，只在缓存缺失时才从上一 profile 反推。守卫新增顺序断言，避免回退。

#### 验证需求

Windows 编译和相关 guard 通过；仍需进游戏确认重铸容量后的连续切弹面板与实际弹匣符合预期。

### CR-2026-07-05-002：焚天龙铳场景清理会丢失手持枪弹种 baseline

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：代码审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 守卫文件：`tests/DragonKingBossGunReforgeBaselineGuard.py`

#### 问题

场景加载时 `ClearDragonKingStaticCache()` 会调用龙铳 `ClearSceneCaches()`，旧实现同时清掉 per-item profile/baseline。若玩家手持枪实例仍保留已套 profile 的 Stat，后续场景重应用可能把已覆盖值当成新 baseline。

#### 修复

把场景级弹幕/命中缓存与枪实例弹种状态分离：`ClearSceneCaches()` 不再清 per-item profile/baseline；`CleanupRuntime()` / reset 路径统一清理弹种状态。守卫新增约束。

#### 验证需求

Windows 编译和相关 guard 通过；仍需进游戏切图后确认当前装填弹种属性不会二次倍率。

### CR-2026-07-05-003：焚天龙铳射击热路径每发重复写 Stat

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：代码审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 守卫文件：`tests/DragonKingBossGunReforgeBaselineGuard.py`

#### 问题

`ShootOneBullet` 兜底每发都会调用弹种属性应用，重复写入 `Damage`、`ShootSpeed`、`Capacity`、`ReloadTime`、`BulletDistance` 并在 dev 模式产生噪声日志。高射速弹种下会放大热路径开销。

#### 修复

`TryApplyAmmoProfile()` 在同一枪实例已经应用同一 profile 且 baseline 存在时直接跳过；仅 `Legacy`、`RuntimeRestore`、`SceneReapply` 强制重写。守卫禁止把 `ShootOneBullet` 加入强制重写路径。

#### 验证需求

Windows 编译和相关 guard 通过；仍需 dev 模式实机确认高射速射击不再刷弹种覆盖日志。

### CR-2026-07-01-001：售货机 UI 崩溃 — 延迟注入商品未缓存 itemInstance

**严重级**：P0 Blocker  
**兼容分类**：`WIRE-` / `OPERATIONAL`  
**状态**：Fixed  
**来源**：从旧 `docs/代码审查/CODE_REVIEW_FINDINGS.md` 与 `docs/协作/FIX_TRACKER.md` 迁移；本次文档收敛未重新进游戏验证。

#### 位置

- 修复文件：`Patches/Economy/StockShopGetItemInstanceDirectPatch.cs`
- 编译清单：`compile_official.bat`

#### 问题

商店条目延迟注入晚于原生 `StockShop.Start()` 的缓存时机，导致 BossRush 注入商品的 `itemInstance` 未缓存。`StockShopItemEntry.Setup()` 取到 null 后访问 `item.StackCount` 触发 `NullReferenceException`，售货机 UI 无法打开。

#### 修复

通过 Harmony Prefix 拦截 `StockShop.GetItemInstanceDirect(typeID)`：缓存命中时放行；缓存未命中且属于延迟注入条目时即时实例化并写回缓存，使调用点不再拿到 null。

#### 验证需求

Windows 实机：进入基地，打开售货机，确认商品显示、可购买，`Player.log` 无对应 NPE。

### CR-2026-07-02-001：许愿台弹幕当前打开轮次不会接入新拉取结果

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：本轮 git 审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/WishFountain/WishFountainUI.cs`
- 修复文件：`Integration/WishFountain/WishFountainDanmakuView.cs`

#### 问题

当面板先用本地缓存/内存缓存启动弹幕时，联网成功后的新数据只会写回缓存，不会接管当前这一次打开中的弹幕来源。结果是本轮看到的仍是旧弹幕，必须关闭再打开一次才会出现最新内容。

#### 修复

保留现有对象池、泳道和滚动逻辑，仅新增“更新内容源但不重置当前滚动”的能力：已在屏弹幕继续滑出，后续新入场弹幕改用最新拉取结果，避免整层闪断或重排。

#### 验证需求

Windows 编译通过；仍需进游戏确认打开许愿台时，已有缓存场景下联网返回后能无感接入新弹幕，且不会出现整层闪烁或输入卡顿。

### CR-2026-07-02-002：许愿台弹幕失败结果被 45 秒 TTL 缓存，重开面板也不会立即重试

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：本轮 git 审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/WishFountain/WishFountainFetchPipeline.cs`

#### 问题

弹幕读取把“最近一次失败”与“最近一次成功”共用同一套 45 秒 TTL 缓存。只要飞书鉴权或列表请求瞬时失败一次，玩家在接下来 45 秒内反复关闭再打开许愿台，也只会立即复用失败结果，不会重新发起拉取。

#### 修复

保留成功结果的短 TTL，避免每次开面板都重新鉴权拉表；失败结果不再走 TTL 短路，玩家重新打开面板时会直接重试联网拉取。

#### 验证需求

Windows 编译通过；仍需进游戏在弱网或临时断网后反复开关许愿台，确认恢复联网后无需再等 45 秒就能重新拉到弹幕。

### CR-2026-07-02-003：许愿台关闭后未解绑静态弹幕回调，旧 View 会被挂到请求结束

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：本轮 git 审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/WishFountain/WishFountainFetchPipeline.cs`
- 修复文件：`Integration/WishFountain/WishFountainUI.cs`
- 守卫文件：`tests/WishDanmakuFetchLifecycleGuard.py`

#### 问题

每次打开许愿台都会往静态 waiter 列表追加新的 success/failure lambda，但关闭面板时只递增本地版本号，没有把这些 lambda 从 `WishFountainService` 移除。慢网、切场景或频繁开关面板时，旧 View 会一直被闭包引用到请求结束，额外保留无效回调。

#### 修复

为弹幕拉取增加显式的 waiter 解绑入口；`WishFountainView.CancelDanmakuFetch()` 在关闭、重开和销毁时统一撤销本轮注册的回调，避免旧面板实例被静态等待队列继续持有。

#### 验证需求

Windows 编译通过；仍需进游戏确认弱网下频繁开关许愿台不会报错、不会留下卡住的旧 UI，也不会影响下一次打开的弹幕刷新。

### CR-2026-08-17-001：Mode G 官方快照绕过逐 key eligibility，且九波计划错误要求整局全局去重

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed（2026-08-17 owner 发布裁决已替代逐 key allowlist）
**来源**：代码审查 + 静态代码验证。

#### 问题与影响

`CreateModeGBossSnapshot()` 曾把共享过滤池中的所有普通 preset 直接加入 Mode G；未知 Mod preset、未纳管 attachment/async/死亡/掉落 owner 也可能进入。`ModeGWavePlan` 又把所有 official primary 与 6 个 reserve 做整局全局去重，合法的 6-key 池也会无故拒绝 Starting。

#### 修复与验证

新增默认拒绝的官方资格记录（stable key、revision、副作用摘要、适应能力），快照和 lookup 全部消费 registry，同 key 多引用拒绝；计划改为逐槽 reserve、同波互斥、draw bag 跨波复用。2026-08-17 owner 进一步确认现有 `GetFilteredEnemyPresets()` 池内 Boss 均可用于 Mode G，因此生产策略改为信任该池，不再要求逐 key 硬编码 allowlist；托管 Boss 排除、唯一 stable key、重复引用拒绝仍保留。2026-08-18 owner 进一步裁决：启动最低为 1 个唯一 key，6 个只是完整 primary/reserve 编排目标；1-5 个时按 runSeed 从已有 stable key 确定性复制，事务按槽位/实例而非 key 去重。同期恢复玩家自带装备入场，并移除旧路牌的独立 Mode G 选项，自动分流后的 presenter 仍保持独立确认页。`ModeGManagedBossEligibilityGuard.py`、`ModeGPlayerLoadoutGuard.py` 与全部 Mode G guards 通过。

### CR-2026-08-17-002：未知存档版本和临时挂起宿敌可能被后续对局覆盖

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：代码审查 + 静态代码验证。

#### 问题与影响

未知/不可读 profile 或 nemesis payload 会回退空 DTO，但旧 `Store()` 没有 per-key 写屏障；未来版本数据可能被 v1 覆盖。有效持久宿敌因 preset、BossFilter、revision 或 adapter 暂时不可用时，本局会选临时宿敌，玩家死亡归因又可能把原 key/Rank 改成临时击杀者。

#### 修复与验证

两个 key 独立建立写屏障，Store/flush 均拒绝覆盖，另一 key 仍可保存；RunState 冻结 `ModeGNemesisSelectionSource`，`SuspendedPersistentV1` 时失败归因只展示受保护。宿主销毁前增加不重入官方保存的最终尽力 flush。三个持久化 guards 通过。

### CR-2026-08-17-003：Mode G 奖励 API 回调异常会误判交付失败，Rewarding 死亡可能被完成回调抢占

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：官方源码调用顺序核对 + 代码审查。

#### 问题与影响

官方背包/仓库 API 可能已提交物品后才由事件回调抛异常，旧逻辑会误判失败、跳过 fallback 或重复处理；取消 materializer 的同步完成回调还可能先锁定 `RewardAbandoned`，覆盖真正的 `RewardInterruptedByDeath`。

#### 修复与验证

交付后核对 Inventory、实例消费和 Incoming buffer，三路均失败才销毁；取消前先失效 reward nonce，完成回调首先检查 nonce，非 Victory 的 `End()` 统一取消 Rewarding。`ModeGRewardGuard.py`、`ModeGDeathRoutingGuard.py`、`ModeGCleanupGuard.py` 通过。

### CR-2026-08-17-004：Mode G 启动退款所有权不唯一，后续初始化异常可能双退

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：代码审查 + 失败路径推演。

#### 问题与影响

Runtime 接管启动退款后，若 `StartRun()` 已推进但 HUD 等后续步骤抛异常，Runtime `End()` 与外层失败分支可能各退款一次；首波同步启动后也可能被外层误判为启动前失败。

#### 修复与验证

`StartModeGRuntime` 显式返回退款所有权，`ArmStartupRefund()` 后只有 Runtime 可退款，外层仅在尚未转移所有权时处理；退款逐项核对交付结果。`ModeGPlayerLoadoutGuard.py` 与正式编译通过。

### CR-2026-08-17-005：Mode G 候选地图没有显式验证状态，未实测地图会被当作 Verified

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed（2026-08-17 owner 已批准地图选择 UI 全部有效地图）
**来源**：设计契约与实际注册表静态对照。

#### 问题与影响

`ModeGMapSupportRegistry` 只有候选 scene pair 和 revision，没有 `NotVerified/Verified` 状态；`TryGetPrimaryVerifiedPair()`、`IsSupported()` 和 `IsVerifiedSceneName()` 会直接接受首发候选。当前发布开关和官方 Boss 空表还能阻断入口，但后续填表开闸时会绕过地图死亡语义、安全三元组、导航与清理 smoke。

#### 修复与验证

新增显式 `ModeGMapSupportStatus`，所有支持查询统一要求状态、当前 revision、死亡风险和安全摘要完整。2026-08-17 owner 确认地图选择 UI 中全部有效配置均为安全地图，注册表因此改为直接复用 `GetAllMapConfigs()` 并生成 Verified 快照；preview 按当前 active scene 冻结玩家实际选择的 exact pair。空 scene、空刷新点、重复 pair 或不属于 UI 配置的场景仍 fail-closed。`ModeGMapSupportGuard.py`、`ModeGReleaseAvailabilityGuard.py` 和全部 28 个 Mode G guards 通过。

### CR-2026-08-29-008：模式H 锁盘按钮转换非法，玩家被困在时停赔率页

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（静态调用链验证，双重复核）

#### 位置

- `ModeH/ModeHRuntimeModule_MatchFlow.cs:441`（`TryTransition(当前状态→LoadoutLocked)`）
- `ModeH/ModeHStateMachine.cs:117-123`（冻结表：LoadoutEditing 只允许 `{OddsPreview, Recovering, ErrorRecoveryPending, Suspended}`）
- `tests/ModeHStateMachineGuard.py:38`（冻结同表）

#### 问题

看盘→赔率页走 MatchBrief→LoadoutEditing，全仓**没有任何调用**转入 OddsPreview（grep 全部 TryTransition 调用点核实）；锁盘按钮请求的 `LoadoutEditing→LoadoutLocked` 不在冻结表内，必被拒绝且仅 DevLog。`MatchFlow:427` 注释「主干走 LoadoutEditing/OddsPreview -> LoadoutLocked」与其引用的冻结表自相矛盾——批 2（a579c3e）新代码按错记的表写成。

#### 影响

赔率页经 `ClaimModalInput` 时停 + 禁输入，页面唯一按钮无效、无关闭按钮，玩家只能靠游戏自身 ESC 菜单逃生。批 2 宣称交付的「锁盘→分帧生成→校验→回滚」整段实机**一步都走不进去**（FIX_TRACKER 批 2 条目的交付边界描述不实）。

#### 建议修复

按设计意图三选一并同步 `ModeHStateMachineGuard`：打开赔率页时补 LoadoutEditing→OddsPreview 转换（贴合注释的主干）；或冻结表补 LoadoutEditing→LoadoutLocked 边；锁盘/回退按钮加失败可见反馈。建议同时给 ReachabilityGuard 增补「每个 TryTransition 调用点的 (from,to) 必须在冻结表内」静态断言（可一并抓住 009/010）。

#### 验证需求

编译 + ModeH guard 全量 + 实机：看盘→赔率→锁盘可推进到生成段。

### CR-2026-08-29-009：模式H 生成回滚「退回看盘」转换非法，成功路径卡死在 MatchSpawning

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（静态调用链验证，双重复核）

#### 位置

- `ModeH/ModeHRuntimeModule_MatchFlow.cs:550`（`TryTransition(→MatchBrief, "combat_wiring_pending")`）
- `ModeH/ModeHStateMachine.cs:148-154`（MatchSpawning 只允许 `{MatchFighting, Recovering, ErrorRecoveryPending, Suspended}`）

#### 问题

分帧生成校验通过并回滚后，代码试图 MatchSpawning→MatchBrief 退回看盘，该边不在冻结表内。转换被拒后玩家停留在 MatchSpawning：模态页已关、只剩空 HUD 与无效拍铃，toast 却提示已退回看盘。

#### 影响

当前被 008 遮蔽（生成段不可达）；修复 008 后立刻暴露为新的困死点。

#### 建议修复

与 008 同批：冻结表补边或改走合法中转，同步 guard；见 008 的 ReachabilityGuard 增补建议。

#### 验证需求

同 008，实机走完「生成→校验→回滚→看盘」全段。

### CR-2026-08-29-010：模式H Recovering 是死态：全部技术故障出口通向无按钮的壳

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（静态调用链验证，双重复核）

#### 位置

- 转入点：`ModeH/ModeHRuntimeModule_MatchFlow.cs:56/164/306/346/356/452/580`、`ModeHRuntimeModule.cs:242`、`_UiFlow.cs:279`
- 出口：`ModeHStateMachine.cs:209-221` 允许 Recovering→MatchBrief 等，但全仓**零调用**以 Recovering 为起点发起转换
- 恢复壳动作：`_UiFlow.cs:235-271`（只有 Suspended 才给「同场重开」，且该动作只做 Suspended→Recovering——转回死路）

#### 问题

选秀失败、计划失败、生成失败、晚到 spawner 等全部技术故障入口都进 Recovering，但没有任何代码把状态转出去；`MaxAutomaticTechnicalRetriesPerMatch=2` 的重试预算永远走不到第二次。

#### 影响

任何一次技术故障后，玩家面对无可点动作的恢复壳（spectator 租约还禁着输入），只能 ESC 离场——随后触发 011 的闩锁链。

#### 建议修复

给 Recovering 接出口：自动重试消耗预算→回 MatchBrief/重建计划，超限→Suspended；恢复壳补玩家动作。属下一批恢复流程的地基，与 012 一并设计。

#### 验证需求

实机模拟计划/生成失败（可用调试开关），确认重试与挂起路径。

### CR-2026-08-29-011：模式H shutdown 闩锁永不复位：一次会话只能玩一局，二次入场吞船票并搁浅

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（静态调用链验证，双重复核）

#### 位置

- `ModeH/ModeHRuntimeModule_SceneFlow.cs:431`（`_commandsClosed = true`，全仓无复位写入）
- `ModeH/ModeHRuntimeModule.cs:289-290`（`_shutdownCompleted = true`；唯一复位在 OnAwake:79，宿主每进程一次）
- 被拦截入口：`_SceneFlow.cs:83`、`_MatchFlow.cs:26`、`_UiFlow.cs:150` 等

#### 问题

ShutdownRuntime 单次执行正确，但两个闩锁落下后没有 per-run 复位。可用性门不感知闩锁：再点船坞入口→船票预扣→传送进图→模块完全不响应（OnSceneLoaded 被闩锁拦截），Legacy 接管又因 ModeH intent 让位。

#### 影响

玩家站在无模式接管的原版地图上，票已消耗、无退款、无提示。由于离开 008/010 困局的唯一方式就是触发 shutdown，这条链当前几乎必踩。

#### 建议修复

入场（BeginSeasonSetup / OnSceneLoadedInternal 起点）做 per-run 闩锁复位；或可用性门感知闩锁并拒绝入场（拒绝文案接 013）。

#### 验证需求

实机同会话连续两次入场。

### CR-2026-08-29-012：模式H 恢复壳在重启后不可达，「船坞恢复分支」未实现

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（零调用 grep 双重复核；启动时序两分支待实机定夺）

#### 位置

- `OpenRecoveryShell` 全仓唯一调用点 `_UiFlow.cs:187`（活动局页面路由）；`ModeHAvailability.EvaluateRecovery` 零调用；`ModBehaviour.ModeHRuntime` 门面零消费
- `UIAndSigns/UIAndSigns.cs:854` 注释宣称「恢复入口复用同一选项（内部按可用性分流）」，但 `ModeHInteractable.OpenEntryFlow`（:162-198）只有 TryEnter 一条路，`ModeHAvailability:92-96` 在 recovery-only 时直接拒绝

#### 问题

中断赛季重启游戏后没有任何路径打开恢复壳。叠加存档恢复只在 OnAwake 跑一次（`ModeHRuntimeModule.cs:83→179-212`）、`HandleSetFile` 不重跑，两种启动顺序各有一个坏结局：(a) SetFile 晚于 Awake（常规）→ 不触发 recovery-only，玩家可开新赛季，`CreateDraftingSeason` 静默覆盖旧赛季存档；(b) 恢复逻辑生效 → recovery-only 永久为真且壳不可达，模式被自己的存档记录锁死。哪种发生需实机确认，但两种都是缺陷。

#### 建议修复

船坞入口按 EvaluateRecovery 分流到恢复壳；存档恢复挂到 SetFile 回调重跑。属下一批恢复流程主体。

#### 验证需求

实机：中断赛季→重启→船坞入口应给恢复选项且旧赛季不被覆盖。

### CR-2026-08-29-013：模式H 开局中止链全程无玩家可见文案，14 个 Unavailable_* 键零消费

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（零消费 grep 双重复核）

#### 位置

- `ModeH/ModeHRuntimeModule_SceneFlow.cs:403-422`（AbortSetup 只 DevLog）→ `ModeHEntry.cs:161-169`（AbortAndRefund 只退款+传送）
- `ModeHAvailability.cs:139-146`（`GetReasonLocalizationKey` 与全部 `Unavailable_*` 键、`Unavailable_TicketRefunded` 零消费）；入口被拒时 `LastReasonId` 无人读

#### 问题

认证失败、取消认证、租约失败、Season 写盘失败、入口被拒时，玩家被无解释地退款传回基地。文案已做、没接——对照路牌 `OnTimeOut` 路径有文案（`ModeGInteractable` 同型参照）。

#### 建议修复

AbortSetup / 入口拒绝出口统一走 ShowMessage + GetReasonLocalizationKey。

#### 验证需求

实机制造认证超时/租约失败，确认横幅出现。

### CR-2026-08-29-014：模式G 无伤成就读跨局残留的 HasTakenDamage，同进程受过伤后 flawless 永久锁死

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（唯一置 false 点与调用方 grep 双重复核）

#### 位置

- `Achievement/AchievementTriggers.cs:421-433`（`BeginModeGAchievementSession` 只清去重集）
- `Achievement/AchievementTracker.cs:84`（唯一 `HasTakenDamage=false`，入口 `BeginAchievementSession` 仅 `ModeD/ModeDWaves.cs:76`、`WavesArena/WavesArenaBossSpawning.cs:76` 调用；模式G 启动路径不经过）
- 消费点：`ModeG/ModeGRuntimeModule.cs:805-810`（`wasFlawlessAtDeath` 快照）

#### 问题

同一进程先打过任何会受伤的模式（含前一局模式G），`HasTakenDamage` 残留 true → 之后模式G 真·无伤击杀龙王/龙裔时 `kill_dragon_king_flawless`/`kill_dragon_descendant_flawless` 不解锁。进程首局无伤的 smoke 恰好测不出。

#### 建议修复

`BeginModeGAchievementSession` 内重置 `HasTakenDamage`（或调 `ResetSessionStats`，注意与 Legacy 会话语义隔离）；同步 `ModeGAchievementIsolationGuard` 等相关 guard。

#### 验证需求

实机：先打一局受伤的任意模式，再打无伤模式G，确认 flawless 解锁。

### CR-2026-08-29-015：遗种巢 会话重启后血脉目录空窗：进一次竞技场之前官方血脉在基地全面不可用

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（调用点 grep 双重复核）

#### 位置

- `PetNest/PetNestLineageCatalog.cs:129-165`（AddOfficialLineages 读 `GetFilteredEnemyPresets`，`enemyPresets==null` 时返回空表：`BossFilter/BossFilter.cs:203-206`）
- `InitializeEnemyPresets` 全部调用点均在进竞技场路径与调试面板（`Integration/BossRushIntegration_StartAndScene.cs:350` 的 `bossRushArenaPlanned` 分支、`_TravelAndSetup.cs:332/466`、`ModeE/ModeEBattle.cs:160`、`ModeG/ModeGRuntimeBridge.cs:17`、`BossFilter.cs:373`），基地启动无一触发

#### 问题

重启会话后直接在基地孵官方血脉蛋 → `lineage_unknown`（文案误导玩家以为蛋坏了）；巢页卡片显示裸 `Cname_*` key、博物馆分母可能显示「5 / 3」、遗魂账本官方血脉整行缺失、凝蛋按钮消失。进一次竞技场（或开一次 Boss 池窗）后当场自愈。5e667b2 修的是「填充后重建」，未覆盖「填充之前」这段每会话必现的窗口。

#### 建议修复

`EnsureBootstrapped` 或基地早期装配主动触发一次 `InitializeEnemyPresets`（数据源 ObjectCache 在基地即可用）。

#### 验证需求

实机：重启→基地直接孵化官方蛋成功、图鉴/账本显示正常。

### CR-2026-08-29-016：遗种巢 关开关不清掉落追踪，dormant 契约被已挂接的 per-boss handler 穿透

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（唯一调用链 grep 双重复核；跨档污染组合链见 UNVERIFIED）

#### 位置

- `PetNestRuntimeModule` 两个关闭分支（OnSceneLoaded:107-113、OnUpdate:163-170）与 `ShutdownIfEnabledTurnedOff`（:270-291）均不调 `PetNestDropService.ClearAllTracking()`
- `ClearAllTracking` 全仓唯一调用链：`PetNestDropService.cs:340`（ResetStaticCaches）← 宿主销毁
- handler 本体（`PetNestDropService.cs:137-170`）不查开关

#### 问题

竞技场中途关闭遗种巢开关后，场上已追踪 Boss 死亡仍会记遗魂/可能掉蛋/弹「可凝蛋」提示，违反「关闭即不产蛋不记魂」契约（每只已追踪 Boss 一发）。

#### 建议修复

三个关闭/停机路径并联 `PetNestDropService.ClearAllTracking()`（一行级）。

#### 验证需求

实机：战斗中关开关→击杀已追踪 Boss，确认无遗魂进账无掉蛋。

### CR-2026-08-29-017：日报 开关关闭期间换档：跨存档槽状态渗漏并覆写新档

**严重级**：P1 Major
**兼容分类**：`COMPAT`（修复本身；现象含存档覆写风险）
**状态**：Fixed
**来源**：2026-08-29 四系统复审（退订/缓存/重置路径三环 grep 双重复核）

#### 位置

- `Integration/DailyReport/DailyReportPersistence.cs:84-101`（`ShutdownSubscription` 退订全部三个 SavesSystem 事件，含 OnSetFile）
- `:121-133`（`HandleSetFile` 是唯一槽位重置路径）；`:152-158`（`LoadOrInit` 命中缓存直接返回，不校验槽位）
- `DailyReportService.cs:765-774`（`_initialized`/`_carrySeconds` 只靠 NotifySlotChanged 重置）

#### 问题

槽 A 游戏中关闭日报开关（退订 OnSetFile，缓存保留）→ 主菜单换槽 B（无人监听、缓存不重置，全仓无兜底）→ 重开开关 → 缓存仍是 A 数据 → 任一次 Persist/官方存盘把 A 的日报 JSON 写进 B 的存档：B 的天数/签到墙/连签/悬赏 claimed 被 A 整体顶掉，可造成进度丢失或重复领悬赏现金。删档变体同理。

#### 建议修复

`LoadOrInit` 记录 `SavesSystem.CurrentSlot`（或文件路径）不匹配即自失效；或关停时清 `_cache`/`_initialized`；或让 OnSetFile 订阅独立于开关存续。**PetNest 是同构形态，需一并排查**（其关停同样退订 OnSetFile：`PetNestRuntimeModule:275-283`）。

#### 验证需求

实机：槽 A 签到→关开关→载槽 B→开开关→开报纸看签到墙、存盘重进 B 确认不被覆写。

### CR-2026-08-29-018：模式H P2/P3 打磨项汇总（复审确认，6+3 项）

**严重级**：P2（6 项）/ P3（3 项）
**兼容分类**：`COMPAT`（⑥ 为 `SAFE` 文档同步）
**状态**：Fixed（第 2 项「赔率页没有赔率」为 Deferred：属战斗/押注接线的一部分，并入下一批）
**来源**：2026-08-29 四系统复审（各项均静态确认，零调用类经双重 grep）

#### 问题清单

1. **看盘页显示原文 "{0}"**：`_MatchFlow.cs:265-269` 把值为「第 {0} 场」的 `Label_Match` 当纯前缀拼接 → 页面显示「第 {0} 场 1 / 6」；`RecoveryPanel:166` 用 Replace 是对的，两处不一致。
2. **赔率页没有赔率**：`ModeHOddsController`（BuildQuote/公开分）全仓零调用；`BuildOddsPageContent`（`_MatchFlow.cs:398-419`）不设 Body/Lines；§23.1 三档下注控件未做。批 2 的「接通赔率」实际只是「赔率页可达」。
3. **SavesSystem 订阅不退订（违反 4.6）**：`ModeHSaveFlushCoordinator.ShutdownSubscription`（:52-56）零调用；对照 PetNest/日报/模式G 均在销毁路径退订。
4. **技术重试不换计划**：`EnsureMatchPlan`（`_MatchFlow.cs:324`）只按 matchIndex 判缓存，technicalRetrySequence 织进种子（EncounterPlanner:102）却复用刚失败的同一计划，与 §17.4 相悖（当前被 010 遮蔽）。
5. **`_pendingContractMainId` 在 AbortSetup 后残留**（`_MatchFlow.cs:251`，仅成功签约清 :236）：同会话再开局，残留主将 ID 让新赛季首次点击直接触发签约判定。
6. **a579c3e 未同步 repowiki（违反 4.13）**：知识卡仍写「尚未接线：战斗主体（生成/…）」，而生成段已接线（`Mode H 百战留痕模式运行时/架构设计.md:27`）。
7. (P3) 赛季创建链两次相邻物理落盘（draft_candidates+首写；match_plan+first_match_brief）。
8. (P3) `modeHPlayerSpawnPos`/`modeHExitPos` 被 MapSupportRegistry:152-153 必填校验但运行时零使用。
9. (P3) 换档后模块内存 `_runState` 残留（BeginSeasonSetup 会覆盖，基本无害）。

### CR-2026-08-29-019：模式G P2 打磨项汇总（复审确认，4 项）

**严重级**：P2
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审

#### 问题清单

1. **自动入场流确认页打不开时对玩家完全静默**：`WavesArena/BossRushEntryFlow.cs:191-217`、`Integration/BossRushIntegration_TravelAndSetup.cs:396-424` 只退款+DevLog（else 分支日志还误写「确认页已取消」）；路牌 `OnTimeOut` 路径有文案，auto 路径缺同款。
2. **宿敌存档「战斗中不写盘」承诺未实现**：`ModeGNemesisPersistence.cs:13` 类头声称写屏障避战斗，实际波 3/6 宿敌死亡帧 `CheckNemesisDefeat`（`ModeGRuntimeModule.cs:926-951`）→ RequestFlush → 下一帧全量 `SavesSystem.SaveFile`，只避 IsSaving 不避战斗；与日报同轮「落盘避开战斗帧」标准相悖（卡顿幅度需实机测量，每局至多 2 次）。
3. **`ModeGInteractable.OnDestroy` 用 `new` 隐藏基类 virtual**（:495）：官方 `InteractableBase.OnDestroy` 是 `protected virtual`（Interacting 时 StopInteract）；当前唯一实例无碰撞体不触发，属潜伏缺陷。改 `protected override` + `base.OnDestroy()`。
4. **AFK 过期提示「重新打开确认页」场内无可达入口**（`ModeGEntry.cs:401-410`）：场内无模式G交互物，auto 确认页只在进图协程开一次；玩家唯一路径是出图重进，提示与可达操作不符。

### CR-2026-08-29-020：遗种巢 P2：PetNestDropService._hooks 慢泄漏

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审

#### 位置

- `PetNest/PetNestDropService.cs:34`（`_hooks` 表）；摘除仅在 boss 死亡路径（`LootAndRewards/LootAndRewardsRandomBossLoot.cs:443→ClearTracking`）与逐只清理
- `LootAndRewards/LootAndRewards.cs:473-481` 的 stale 清扫只清自家四表，不清 PetNest 并联表

#### 问题

未死亡也未被逐只清理的 Boss（弃局、直接撤离、切图销毁）在 `_hooks` 的条目永不移除，长会话跨多局无上限累积（死角色 key + 捕获 owner/character 的委托）。纯内存慢泄漏，无每帧成本。

#### 建议修复

stale 清扫处并联移除，或场景回调 `ClearAllTracking()`（与 016 同批顺手修）。

### CR-2026-08-29-021：日报 P2/P3 打磨项汇总（复审确认，1+5 项）

**严重级**：P2（1 项）/ P3（5 项）
**兼容分类**：`COMPAT`
**状态**：Fixed（第 6 项「未读提示不持久」为 Deferred：需新增可选存档字段，
不为数据无损的 P3 动 schema，留 owner 拍板）
**来源**：2026-08-29 四系统复审（①经官方源语义核对）

#### 问题清单

1. (P2) **悬赏现金忽略 `EconomyManager.Add` 失败返回值**：`DailyReportRewards.cs:126` 丢弃返回值；官方 `Add` 在 `Instance==null` 时返回 false 不抛异常 → `SettleBounty` 仍置 `BountyRewardClaimed=true` 落盘，补发被闸死，现金永久丢失且报纸公示「已寄出」。窗口窄（场景闸使 gameplay 帧 Instance 通常就绪）但属确定的错误处理缺失，一行修复。与本系统「先发后标记、宁可重发不吞奖」纪律相悖——里程碑物品路径检查了投递结果，现金路径没有。
2. (P3) **`BuildCandidates` 把瞬时失败缓存成会话级空池**（`DailyReportRewards.cs:164-217`）：`Instance==null`/异常返回的空数组被缓存到进程结束，该品质整会话 `no_candidate`；奖励不丢（下会话补发）但当次拿不到。空结果不应缓存。
3. (P3) **同场景热切开后建造菜单没有报箱**：注入闸只在进基地装配管线跑一次（`DailyReportMailboxBuilder.cs:103`、`IntegrationDeferredBootstrap.cs:300-306`），原地开开关要出图再回。PetNest 同构（`PetNestBuilder.cs:94`）。
4. (P3) **F3 dump「悬赏题目」打的是昨日已结算题**（`F3DebugCheatMenuActions.cs:845` 用 `data.BountyKindId` 而非 `GetActiveBounty()?.Id`），与旁边「悬赏进度」（今日题）不一致，干扰 D6 排查。
5. (P3) **跨天补发里程碑会换奖品**：`DailyReportService.cs:582` 补发传当前 DayIndex，与 `DailyReportRewards.cs` 头注释「同一 (seed, day, slot) 同一件」矛盾；品质一致、无重复发放，纯确定性承诺破口。
6. (P3) **未读提示不持久**：`DailyReportService.cs:83` `_pendingIssueBanner` 仅内存；战斗中跨天→未回基地退游戏，下会话不再提示（数据无损）。

## UNVERIFIED / Seeded Leads

> 这里的内容不是 bug。升格前必须读代码或运行验证。

- **遗种巢 跨档写污染组合链**（016 的延伸，逐环节已静态核过、多步组合需实机）：关开关后已挂 handler 触发 `AddSouls` 会从当前槽重建缓存 → 主菜单切档（无人清缓存）→ 重开开关（EnsureBootstrapped 不重置缓存）→ 下次 Commit 把 A 档巢数据写进 B 档。与 017 的日报跨档渗漏同构。
- **遗种巢 远征奖励非崩溃失败被吞**：`PetNestExpeditionService.cs:420-447` 注释宣称可补发，但 `GrantRewards`（:560-631）内部吞异常正常返回、:432 无条件置 `rewardsGranted=true`——蛋实例化失败/EconomyManager 异常 = 一次性丢失。修复需按条目粒度记账（现金已发、物品失败的部分重发问题）。
- **遗种巢 OnExpedition 孤儿锁无自愈**：Nest key flush 成功、Expedition key flush 异常进 StoreFaulted 后（`PetNestSaveCoordinator.cs:141-144` 逐 key 独立失败），官方存盘可落「崽=OnExpedition」而远征记录缺失；无 reconcile 路径，该崽永久锁死。建议 Normalize 处加「OnExpedition 且远征表无匹配 → 复位 InNest」自愈。
- **遗种巢 P3 一组**：基地闲逛崽实为跟随玩家（含 >40m 传送，观感是仪仗队）；场景切换只停两个演出层、主面板 modal lease 极端时序可带进战斗图；天赋/战痕/蛋 KV 展示裸英文 key。
- **模式H 地图选择不锁定冻结目标**：`ModeHEntry.TryEnter:130` 冻结唯一支持图后打开通用地图 UI；选其它图 → intent 仍为 ModeH → Legacy 三处让位、模块按场景名拒绝 → 无人接管、预扣票不退、无提示（静态成立，需实机复现）。
- **模式H 关停竞态孤儿**：StopCoroutine 打断 `CreateCharacterAsync` await 期间，`CreateIsolatedAsync`（SpawnBridge:58-176）async 延续仍会完成并登记抑制表+生成隔离角色，RollbackAll 只回收已入列 handle（窗口数帧）。
- **模式H `ClearNativeEnemies` 无差别清场**（ArenaIsolationLease:187-213）：销毁除主玩家外全部 CharacterMainControl——PetNest 出战崽/跟随配偶/快递员若出现在该图会被直接 Destroy（不走 OnDead）；`player==null` 异常分支下连玩家也会被清。
- **模式G 弃局确认页开在 Spawning 相位且时停拖住工厂 >15s 是否误报 TechnicalIntegrityLoss**：取决于官方 `CreateCharacterAsync` 是否受 timeScale 影响，静态无法判定。
- **模式G SceneChanged 终局的 `ShowMessage` 在 OnSceneLoaded 时机是否可见**：需实机确认。
- **日报 中途弃 raid 可能计成「成功撤离」**：`DailyReportStatsCollector.cs:190-204` 按 `!info.dead` 计撤离；官方对「战局中退出→回基地」是否判死亡未定，若不判则撤离数可刷（并入 D6 冒烟项）。

## 新条目模板

```markdown
### CR-YYYY-MM-DD-NNN：问题标题

**严重级**：P0/P1/P2/P3  
**兼容分类**：SAFE / COMPAT / SCHEMA+ / SCHEMA- / WIRE+ / WIRE- / BREAKING / OPERATIONAL  
**状态**：Open / Fixed / Deferred / WontFix  
**来源**：代码审查 / 用户复现 / Player.log / guard / 人工 smoke

#### 位置

- `文件路径:行号`

#### 问题

是什么错，为什么是错。

#### 影响

玩家可见后果、静默失效、性能、存档或维护风险。

#### 建议修复

最小修复步骤。

#### 验证需求

编译、guard、人工 smoke 或无法验证原因。
```
