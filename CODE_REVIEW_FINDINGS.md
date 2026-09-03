# CODE_REVIEW_FINDINGS.md — 已确认问题库

> 只记录 confirmed findings。未验证线索放本文件的 UNVERIFIED 区，或在 `FIX_TRACKER.md` 中标为 `accepted/deferred/refuted/documented`。

## 状态汇总

| 严重级 | Open | Fixed | Deferred | WontFix | 合计 |
| --- | ---: | ---: | ---: | ---: | ---: |
| P0 | 0 | 9 | 0 | 0 | 9 |
| P1 | 6 | 30 | 0 | 0 | 36 |
| P2 | 4 | 29 | 0 | 0 | 33 |
| P3 | 1 | 20 | 0 | 0 | 21 |

最后更新：2026-09-03 七日全面审核（96 个提交 / 约 10 万行新增）。新增 CR-2026-09-03-001..008：
1 个 P0（押品脱离仓库后无回滚，真实物品可永久丢失）、1 个 P1（濒退制「休息解除带伤」完全未接线）、
2 个 P2（锁盘全静默；ERROR 互换归属渗入图鉴与日报）、4 个 P3。八条全部已修：
Windows 编译通过、513 guard 全绿、新增/增补 guard 均经**反向验证**（逐条破坏不变式确认转红）。
实机 smoke 四项待人工，见 `FIX_TRACKER.md` 同日条目。

上一次更新：2026-09-02 第六轮（138 PASS / 0 FAIL / 0 SKIP / 0 WARN；014–017 转 Fixed。
H 初始整备 8/8、入场意图清除、丧尸结算返程、终章与最终订阅差值均实机通过。
人工清单仍有 113 条待验；既有 AI / 外部 Mod 异常不等同已排除）。

上一次更新：2026-09-02 第五轮（134 PASS / 3 FAIL / 1 WARN；H 首次认证及缓存通过，005/013 转 Fixed。
新增 014–017：丧尸撤离测试缺少正数样本、H 成功入场意图未消费、H 弹药误走装备槽、终章掉落订阅残留。
代码与离线验证完成；新增项在下一轮实机验证前保持 Open）。

上一次更新：2026-09-02 第四轮（完整 F3 报告 69 PASS / 2 FAIL；D 多波通过，010/012 转 Fixed。
H 逐候选不再拒绝或出现伤害异常，011 转 Fixed，但总体认证和缓存仍卡口令门，005 保持 Open。
新增 013：口令矩阵无生产写入者、签名绑定顺序与缓存恢复缺失；已修正并编译，游戏内复测前保持 Open）。

上一次更新：2026-09-01（新增 CR-2026-09-01-010，记录第二次 F3 报告确认的
2 个 P1 + 1 个 P2 + 1 个 P3；修复与静态验证已完成，下一份完整实机报告通过前保持 Open）。

上一次更新：2026-08-31（新增 CR-2026-08-31-009，记录首次 F3 完整验收日志确认的
4 个 P1 + 3 个 P2；代码、守卫和 Windows 编译修复已完成，但按发布门槛在下一份完整实机
报告通过前保持 Open，不提前标 Fixed）。

更早更新：2026-08-31（用户明确要求全部修复并保持内容开启。CR-2026-08-31-001..006
与原 deferred 的 CR-2026-08-31-007 七项均已 Fixed；新增 CR-2026-08-31-008 记录本轮
静态确认并修复的 5 个 P1 + 2 个 P2。模式H 赔率、日报未读提示两个旧 deferred 也已闭环。
修复内容与验证见 `FIX_TRACKER.md` 同日条目；涉及真实游戏对象、UI 和存档切槽的实机 smoke 仍待人工）。

更早更新：2026-08-29（四系统复审全面修复完成：CR-2026-08-29-008..021 全部 Fixed，
修复内容与验证见 `FIX_TRACKER.md` 的「四系统复审全面修复」条目。
Open 计数按问题条目计，分组条目内多项分别计数。上午修复轮的
CR-2026-08-29-001..007 未单独立条，见 `FIX_TRACKER.md` 四个修复包）。

## Confirmed Findings

### CR-2026-09-03-001：模式H 押品脱离仓库后阶段推进失败无回滚，真实物品永久丢失

**严重级**：P0
**兼容分类**：BREAKING（玩家真实资产）
**状态**：Fixed
**来源**：2026-09-03 七日全面审核（静态确认，含官方 API 与设计稿逐条对照）

#### 位置

- `ModeH/ModeHWarehouseStakeJournal.cs:481`（TryRemoveEscrow 末步）
- `ModeH/ModeHWarehouseStakeJournal.cs:690`（TryCancelWithoutRemoval 只看内存态）
- `ModeH/ModeHSaveFlushCoordinator.cs:173`（IsSaving 时写盘必然失败）

#### 问题

`TryRemoveEscrow` 先 `inventory.RemoveAt` 把押品摘出仓库，再 `TryAdvancePhase(EscrowRemovedDurable)`。
该推进内含落盘，`SavesSystem.IsSaving` 时返回 `flush_deferred_is_saving`，phase 回滚到
`EscrowSnapshotDurable`，但**物品不回滚**——只活在内存 `_escrowItems` 里。
同一函数的上一条失败路径（TryComputeInventoryDigest）是有 `RollbackDetached` 的，此处漏了。

#### 影响

三条出路全部封死：① 本会话取回走 `TryCancelWithoutRemoval`，被 `cancel_escrow_still_held`
挡死（a570162 新加的关停/挂起/生成失败三条返还路径全部经此函数，全部失败）；
② 重启/切槽后 `LoadPersisted` 清空 `_escrowItems`，该检查失效，journal 被静默归档成
`CancelledTerminal`（语义是"已证明从未移除"）；③ 玩家侧 `PrepareLockedMatch` 失败只写
`DevLog`，正式构建被 `[Conditional]` 剥离，按钮毫无反应。触发条件是「押品锁盘时恰逢官方自动存档」。

违反设计稿 `docs/设计提案/2026-08-17_斗蛐蛐新模式创意脑暴.md:1744`「匹配不到 pre-image → 人工介入」。

#### 修复

① `TryAdvancePhase` 失败分支补 `RollbackDetached`，与既有分支对称；
② 新增 `VerifyEscrowStillInInventory`：逐项 `CountOccurrences` 与 `preCount` 比对，
不足即 `EnterManualIntervention`。用逐项比对而非整仓 digest 全等，避免"玩家挪了别的东西"误报。

#### 验证需求

Windows 编译 + 513 guard 通过；`ModeHStakeJournalGuard` 新增两条断言并经反向验证。
**实机待做**：Dev 下令 `RequestStakeJournalWrite` 返回 false，确认物品回到仓库。

---
### CR-2026-09-03-002：模式H 濒退制「休息一场解除带伤」完全未实现

**严重级**：P1
**兼容分类**：COMPAT
**状态**：Fixed
**来源**：2026-09-03 七日全面审核（零调用点 grep 双重复核）

#### 位置

- `ModeH/ModeHCombatTelemetry.cs:169`（HasRested 零调用）
- `ModeH/ModeHInjuryAndScarSystem.cs:355`（injuryId 只写不清）
- `Localization/ModeHLocalization.cs:219-220`（Injury_Rested / Injury_Retired 零消费）

#### 问题

owner 2026-08-17（濒退制）与 2026-08-18（按实际登场判定休息）两次裁决冻结的规则，
代码里只有「进带伤 / 进退役」两条边，没有任何从 `Injured` 回到 `Available` 的路径。
判定用的积木 `HasRested` 写好了但全仓库零调用；两个文案 key 注入了但无人消费。

#### 影响

把带伤选手按在替补席完全没有收益（一个核心战术抉择是空的）；伤病 debuff
（腿伤/手伤/护具受损/旧伤/心气受挫）在其后**每一场**都生效；赔率惩罚
`starterInjured -5` / `relayInjured -3` 永久挂着。六场赛季比设计难度显著更高，
选手实际是「两条命、无恢复」。

#### 修复

新增 `ModeHInjuryAndScarSystem.ResolveRestRecovery`（清 `injuryId` + 复位 `Available`）；
`BeginMatchSettlement` 在两次 `ResolveDownInjury` 之后对 starter/relay 两席各调一次；
结算页消费两个文案 key。休息名单只存运行时 `_restedProfileIds`，**不进持久化 DTO**——
`ModeHCanonicalDigest` 按反射遍历全部公有字段，加字段会让已存赛季 VerifyDigest 失败进写屏障。

#### 验证需求

`ModeHStructureGuard` 新增断言（含"两席都要结算"）并经反向验证。
**实机待做**：主将倒地带伤 → 下一场留替补席不登场 → 确认解除且结算页显示「完整休息」。

---
### CR-2026-09-03-003：模式H 锁盘按钮所有失败原因均静默

**严重级**：P2
**兼容分类**：COMPAT
**状态**：Fixed
**来源**：2026-09-03 七日全面审核

#### 位置

- `ModeH/ModeHRuntimeModule_MatchFlow.cs:863-868`

#### 问题

`PrepareLockedMatch` 失败只写 `DevLog`，而它带 `[Conditional("BOSSRUSH_DEV")]`，
正式构建里整个被剥离。按钮 `Interactable` 也不检查这些前提。
可返回的失败原因含 `lock_command_missing`、`match_no_selectable_command`、
`lock_selection_missing`、`match_roster_no_live_contract` 与全部押品失败。

#### 影响

玩家点「锁盘」毫无反应，与按钮损坏无异，被堵在赔率页。`lock_command_missing` 现实可达：
`GetSelectableCommands` 按 stableKey 取，某选手若无通过认证的口令即为空。
这是继拍铃、押品选择之后的第三处同类静默。

#### 修复

新增 `ResolveLockRejectReason` 按原因分档（押品类委托既有押品文案），
新增 `LockReject_CommandUnavailable` / `LockReject_RosterMissing` / `LockReject_Generic`
三条中英文案；绝不把内部 reasonId 原文展示给玩家。

#### 验证需求

`ModeHStructureGuard` 断言失败分支必须 `ShowMessage` 且不得直接展示 reasonId，经反向验证。
**实机待做**：制造一次锁盘失败确认有提示。

---
### CR-2026-09-03-004：ERROR 互换期间的击杀归属渗入图鉴与日报

**严重级**：P2
**兼容分类**：COMPAT
**状态**：Fixed（owner 2026-09-03 裁决：Mode H 击杀不计入；日报整个采集器跳过）
**来源**：2026-09-03 七日全面审核

#### 位置

- `Integration/Codex/CodexKillCollector.cs`（OnGlobalDead / OnGlobalHurt）
- `Integration/DailyReport/DailyReportStatsCollector.cs`（IsActive）

#### 问题

两个全局采集器只按 `info.fromCharacter.IsMainCharacter` 过滤，而 ERROR 完整互换期间
官方会把归属改写成主角（`ModeHEventRouter.SetErrorSwapControlledParticipant` 的存在即为佐证）。
于是同一场 Mode H 比赛里 99% 的击杀（选手打的）不计、ERROR 那次却计。

对照：战役侧本就安全（`ResolveCampaignCurrentMode` 对 G/H 返回 null）；
成就侧不可达（`Assets/Data/ModeH/BossProfiles.json` 排除表已含 `DragonDescendant` / `boss_dragonking`）；
PetNest 侧不可达（随从被 `PetNestModeGate` 挡在 Mode H 外）。

#### 影响

擂台 Boss 可进鸭皇图鉴并污染「最快击杀」记录；日报战绩与悬赏可在观战模式里推进。

#### 修复

两处加 `ModBehaviour.IsModeHRunInProgressSafe()` 门控（该门面已被 RandomEventModeGate、
PetNestModeGate 用于同一目的）。日报按 owner 选择放在 `IsActive` 总闸上，一次覆盖
击杀/双向伤害/玩家阵亡三类，避免「击杀不算但伤害算」的自相矛盾。
保留 `CodexTuning.ModeIdModeH` 常量与 `FormatModeName` 分支（存档兼容面）。

#### 验证需求

`CodexKillTrackingGuard` / `DailyReportPersistenceGuard` 新增断言并经反向验证。
**实机待做**：Mode H 打完一场后确认图鉴与日报无新增。

---
### CR-2026-09-03-005：战役交付退款失败时可重复领取奖金

**严重级**：P3
**兼容分类**：COMPAT
**状态**：Fixed
**来源**：2026-09-03 七日全面审核

#### 位置

- `Campaign/CampaignProgressService.cs:307-372`

#### 问题

交付先发钱再写状态，写状态失败则退款。但退款也失败时章节仍是 `ReadyToDeliver`，
玩家可再次交付再拿一次奖金，此前只有一句文案「请勿重复交付」拦着。

#### 影响

经济漏洞面。需要「写盘失败 **且** 退款失败」两个低概率事件同时发生，实际概率极低。

#### 修复

新增会话级闩 `_cashPaidPendingChapterId`：发钱成功即置位，写盘成功或退款成功则清空；
重试交付时若闩命中则跳过发钱、只补写状态。换槽与静态复位一并清空。
**已接受的残留**：闩是会话级，跨重启失效——不为 P3 增加存档字段。

#### 验证需求

静态；实机不必专门构造（触发条件本身极罕见）。

---
### CR-2026-09-03-006：ResolveFallbackActions 已成死逻辑

**严重级**：P3
**兼容分类**：SAFE（仅注释）
**状态**：Fixed（按注释澄清，**刻意不删代码**）
**来源**：2026-09-03 七日全面审核

#### 位置

- `Utilities/ModeExtractionPointFactory.cs:69-73`

#### 问题

CR-2026-09-02-006 把它挪到 `ClearPersistentEvents` 之后（修复本身正确），
而后者是整体 `new UnityEvent()` 替换，此后 `GetPersistentEventCount()` 恒为 0，
函数内循环永不执行、恒返回 (true, true)。

#### 影响

无行为影响，但会误导后来者以为仍有「官方回调探测」能力。

#### 修复

**不删除函数**：`tests/ZombieModeExtractionFactoryGuard.py:50-73` 把
`ClearPersistentEvents < ResolveFallbackActions < ConfigureEvents` 的顺序冻结为不变式，
删掉等于抹掉该 finding 的回归防线（AGENTS.md 4.10 不得为改动放宽 guard）。
改为补注释说明现状与「何时会重新生效」。零行为改动。

---
### CR-2026-09-03-007：GameQuality 的 CS0649 注释与实际取色不符

**严重级**：P3
**兼容分类**：SAFE（仅注释）
**状态**：Fixed
**来源**：2026-09-03 七日全面审核

#### 位置

- `ModeH/ModeHRuntimeModule_MatchFlow.cs:362`

#### 问题

注释称「留 0 时渲染器走 accent 色」，实际 `ModeHUI.ResolveRarityColor(0)` 返回
`BossRushUIColors.RarityCommon`；accent 只在 `IsAnomaly` 时才换成 Warning。

#### 影响

表现无害（所有选秀卡统一中性描边），但「刻意不赋值」的理由写错了。

#### 修复

改注释为真实取色，保留结论（该 CS0649 是有意的）。

---
### CR-2026-09-03-008：摘要集合语义清单漏登记第二份入场名单

**严重级**：P3
**兼容分类**：COMPAT（未上线，owner 确认可直接优化）
**状态**：Fixed
**来源**：2026-09-03 制定修复计划时发现

#### 位置

- `ModeH/ModeHCanonicalDigest.cs:50`

#### 问题

入场名单在两个 DTO 上各有一份：`ModeHMatchRosterDto.enteredProfileIds`（:188）与
`ModeHMatchReportDto.entrantIds`（:358）。`SetSemanticFields` 长期只登记前者，
后者从未参与排序去重——而它来自 `HashSet` 枚举转 List。

> 首次记录时曾误判为「清单指向不存在的字段」并改成替换，
> 经 guard 反向验证发现 `enteredProfileIds` 也是真实字段，已更正为两份都登记。

#### 影响

潜在：`entrantIds` 顺序若变化会让同一逻辑状态算出不同摘要，误判
`season_digest_mismatch` 并进写屏障（押品禁用、恢复壳接管）。当前顺序实际稳定，未触发。

#### 修复

两份都登记；`ModeHStructureGuard` 新增断言——清单字段必须在 DTO 中真实存在，
且两份入场名单都必须登记，防止再次漂移。

---

### CR-2026-09-02-001：F3 直载基地子场景导致黑屏，模式失败返程污染后续用例

**严重级**：P1 Major

**兼容分类**：`COMPAT`

**状态**：Fixed（2026-09-02 完整 F3 报告验证）

**来源**：用户黑屏反馈、Player.log、`BossRushValidation_20260901_143149_397.log` 与实际调用链。

原 `DebugAndTools/F3GameplayValidationRunner.cs` 用 `LoadScene("Base_SceneV2")` 直载子场景，
日志只见该子场景、不见完整 `Base`；加载任务结束即记 PASS，终态还跳过 AfterInit。
同时 H 认证全拒返基地后，后续场内用例仍在基地启动，造成受污染的 PASS/FAIL。
新 `F3GameplayValidationScenes.cs` 使用官方 `LoadBaseScene` 并核对完整就绪状态；
`F3GameplayValidationStages.cs` 每个场内用例前恢复竞技场，已有加载结束前不发起第二次加载。
基地/竞技场未就绪则跳过依赖用例，如实记录状态；不放宽正式 H 认证退出约束。
`BossRushValidation_20260902_114245_015.log` 中返程、完整就绪、最终清场/回读均 PASS，
SUMMARY 完整输出，H 拒绝后的竞技场恢复也通过。本次报告未复现加载黑屏。

### CR-2026-09-02-002：F3 终章 DamageInfo 零值初始化造成伤害空引用

**严重级**：P1 Major

**兼容分类**：`COMPAT`

**状态**：Fixed（2026-09-02 实机 CAMPAIGN_FINAL_BOSS PASS）

**来源**：Player.log 7400 行与官方 `DamageInfo` / `Health.Hurt` 源码。

`RunCampaignFinalBoss` 使用无参 `new DamageInfo()`，对 struct 不会调用带可选参数的构造器，
`elementFactors` 为 null；官方 Hurt 读取其 Count，必然空引用。改为 `new DamageInfo(player)`
并填写受击目标与位置，继续走真实 Hurt/死亡/呈现链。新报告 death_presentations=1，清理 PASS。
后续第三轮报告的曲目加载、播放及租约用例也已通过，见 CR-2026-09-02-007。

### CR-2026-09-02-003：F3 验收源码引用三个不存在的 API，阻断正式编译

**严重级**：P0 Blocker

**兼容分类**：`COMPAT`

**状态**：Fixed（Windows 正式编译通过）

**来源**：Roslyn CS1061、官方 DamageInfo 与 Mode H 租约类定义。

`F3GameplayValidationRunner.cs` 的 `damage.damageCreator`、
`ModeHRuntimeModule_SceneFlow.cs` 的 `_arenaLease.Dispose()` / `_spectatorLease.Dispose()`
均不存在。伤害改用实际字段；`ForceResetStateForValidation` 改用既有完整
`ReleaseRuntimeObjects`（租约真实 API 是 Release(sceneGeneration)），保留 run 上下文先尝试
押品返还，释放对象后再清临时赛季/地图和 owner。Dev 构建通过，43 项相关守卫通过。
后续两轮 H 拒绝后的清理、场景恢复与最终泄漏差值通过；认证成功后的释放路径仍待覆盖。
完整记录见 FIX_TRACKER 的 2026-09-02 条目。

### CR-2026-09-02-004：距离休眠退订使用了角色的主场景索引

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed（第三轮 MODE_D_LIFECYCLE PASS）

F3 的 Mode D 首波和多波均记录三只狼 inactive 且存活。当前游戏 DLL 的 CreateCharacterAsync
调用 SetRelatedScene，后者将角色重挂到 MultiSceneCore 主场景父级，却按 relatedScene 子场景
登记距离休眠。helper 用 GO.scene 退订，移除失败也不报异常。现改为按已加载场景索引只移除
当前角色的登记，标准 Boss 路径也接入。F3 增补 activeSelf、父级与对象场景诊断。
第三轮报告 `BossRushValidation_20260902_121947_845.log` 中三只狼全部 active/self/parent 为 true，
实际对象场景为 Level_DemoChallenge_Main；首波生命周期通过。多波另受 CR-2026-09-02-012 阻断。

### CR-2026-09-02-005：Mode H 认证拒绝已归一化的克隆，且旧受控击杀未触发死亡

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed（第五轮首次认证与缓存实机 PASS）

12 个候选全部 audit_cannot_die。SpawnBridge 已在独立 clone 打开非 Raid 图死亡，但静态审计
先按原 preset 拒绝；同时 SetHealth(0) 只写生命值，不设置 IsDead 或触发事件。现保留其他静态资格，
改为检查实际 Health 的 CanDieIfNotRaidMap、对两个诊断 clone 执行完整 DamageInfo 的 Hurt，
实例事件监听在 finally 退订，只有 IsDead 与受伤/死亡事件齐全才通过。候选数量和缓存签名门不变。
第三轮候选均已进入真实击杀，但遇到 certification_kill_failed:NullReferenceException，
见 CR-2026-09-02-011；首次认证和缓存仍未获得实机 PASS，不能提前关闭此项。
第四轮没有逐 key 拒绝或伤害异常，流程进入整体门槛失败；口令矩阵漏测见 013。
生产认证整体 PASS 前继续保留此项的完整验证要求。

第五轮 `BossRushValidation_20260902_133917_599.log`：首次认证 45719ms、缓存 142ms，
均 drafting=True、archived=True；Player.log 记录 passed=12、common=7、overall=True。
本项认证链已闭环；全赛季、真实押品与本轮新增入场/整备问题分别验收，不扩大此 PASS 的范围。

### CR-2026-09-02-006：撤离工厂删除官方回调后仍跳过通知与返程兜底

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed（第三轮 Mode F 撤离及完整返基地 PASS）

Mode F 日志已经结算成功并退出模式，却未切回基地。ModeExtractionPointFactory 先从 prefab
持久事件判定无需兜底，再用空事件替换全部回调，两个执行方同时消失。改为替换后判断；
成功事件用一次性占有标记包住结算与兜底，避免 ZombieMode 在成功结算中重新派发同一事件导致双重返程。
第三轮 MODE_F_EXTRACTION 记录 resolved=True、stillActive=False、baseReady=True。
共享工厂的丧尸重入分支仅静态检查，丧尸实际撤离仍需单独实机覆盖。

### CR-2026-09-02-007：BGM 非空曲目表经 JsonUtility 读取后数组为空

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed（第三轮曲目加载、播放与租约 PASS）

已部署文件与源码哈希一致且含 2/2/2 条目，运行时却多次记录 boss=0/stinger=0/jukebox=0。
协调器未获得任何可播放曲目，租约用例随之失败。新增 BossBgmTrackTable 显式复用既有 token parser，
三组数组与可选默认值独立探针通过；不改音频资源或龙王旧 mp3 路径。
第三轮 Player.log 记录 boss=2/stinger=2/jukebox=2，女巫与龙裔播放/停止均有记录，
BGM_OWNER_LEASES 的共享、替换恢复与最终清空断言全部通过。

### CR-2026-09-02-008：F3 在 Mode F 退出重置后读取瞬时结算标志

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed（第三轮 MODE_F_EXTRACTION PASS）

成功事件同步调用 ExitModeF 并 Reset ExtractionResolved，测试随后读到 false。
现比较生产成功结算计数增量，并同时验证模式退出与基地完整就绪，避免把“结算过”误当作“撤离完成”。

### CR-2026-09-02-009：F3 在标准 Boss 完成登记之前击杀创建中对象

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed（第三轮 STANDARD_VICTORY_REWARD PASS）

Player.log 的 ForceKillAllEnemies 先于“记录 Boss 生成信息”和“生成成功”。
全场扫描能提前发现官方 async 创建中的角色，此时死亡未命中 currentBoss。
改为等生产登记的活跃 Boss，再定点 Hurt 并验证 IsDead；胜利仍由原波次逻辑与奖励箱断言确认。
第三轮记录 spawned=1、victory=True、rewardCrate=True。

### CR-2026-09-02-010：F3 多波测试把波次推进误当作下一波生成完成

**严重级**：P2 Minor

**兼容分类**：`COMPAT`

**状态**：Fixed（第四轮 MODE_D_MULTI_WAVE PASS）

ModeDStartNextWave 先增加编号，再异步生成新怪。旧测试自动开波后立即读取可玩数量，可能读到零；
击杀后再全场 Destroy 清理也可能碰到零间隔启动的下一波。改为快照并逐只 Hurt 当前波登记角色，
推进后有界等待新波活跃角色；两个波次仍都必须有真实活跃、存活、敌对的登记对象才 PASS。
第三轮首波活跃，但第二只狼的伤害链抛 FormatException，未走到新波检查，见 CR-2026-09-02-012。
第四轮 `BossRushValidation_20260902_124241_090.log` 记录 wave=1->2、playable=3/3、
advanced=True、manual_push=True；两波激活/存活/敌对均确认。

### CR-2026-09-02-011：Mode H 诊断伤害空来源与外部死亡订阅不兼容

**严重级**：P1 Major
**兼容分类**：COMPAT
**状态**：Fixed（第四轮逐 key 不再拒绝或抛伤害异常；总体认证另受 013 阻断）

第三轮首次认证和缓存用例中，12 个候选均在真实 Hurt 返回前出现 NullReferenceException。
认证构造 `new DamageInfo((CharacterMainControl)null)`；只读反编译本机已安装的
BattlefieldTypeKillNotice.dll，发现其 Health.OnDead 订阅直接读取 damageInfo.fromCharacter.IsMainCharacter，
没有空值保护。空来源与该订阅的组合必然异常，独立模拟回调也复现；旧日志只记录异常类型，
尚无原始完整栈能唯一归因这次异常，修复后游戏内结果仍需确认。

现在两只诊断 clone 互作受控伤害来源，拒绝 null、自身或主玩家来源，避免认证记入玩家击杀提示；
保持真实 Hurt、IsDead 与受伤/死亡事件断言。异常输出 stable key、队伍、已观察事件和完整栈，
finally 始终退订临时监听；不吞异常冒充通过，不修改外部 Mod。
第四轮日志没有认证伤害异常或逐 key 拒绝，仍由整体门槛拒绝。结合当前执行链可确认
逐候选已完成，但这不代表 H 完整开局和缓存通过；原异常无完整栈的归因限制仍保留。

### CR-2026-09-02-012：F3 同帧连续击杀触发外部经验提示的未初始化文本解析

**严重级**：P2 Minor
**兼容分类**：COMPAT
**状态**：Fixed（第四轮 MODE_D_MULTI_WAVE PASS，无该伤害格式异常）

第三轮 MODE_D_MULTI_WAVE 首波三只狼正常激活；前两只已进入死亡结算，第二次 Hurt 抛 FormatException。
已安装 BattlefieldTypeKillNotice 的 BuildUI 未将经验文本初始化为数字，首次 tween 在后续更新才写入；
同帧第二次击杀却会对旧模板文本调用 long.Parse。F3 原方法在一个循环内同步杀完快照，
符合该触发条件，模拟回调已复现。原日志无完整异常栈，保留新日志与实机确认要求。

当前波快照改由 IEnumerator 每次真实击杀后让出一帧，使 UI 更新有机会完成；
仍检查每只 IsDead，异常记完整栈并 FAIL，推进后仍等待新波真实生成。
改动只覆盖 F3 批量驱动节奏，不修改玩家正常战斗、官方死亡链或外部 Mod 的群体击杀实现。

### CR-2026-09-02-013：Mode H 口令矩阵无人写入，缓存也未恢复逐效果证据

**严重级**：P1 Major
**兼容分类**：COMPAT
**状态**：Fixed（第五轮首次认证与缓存实机 PASS）

第四轮逐 key 无拒绝，却仍 certification_threshold_not_met。全仓调用检查确认
RecordEffectStatus 只有声明、生产零调用；默认只有 steady 的自结算效果通过，
所有 key 永远只有 1 条可用通用口令，无法满足冻结的至少 3 条门槛。真实矩阵源码和内容数据
独立探针复现。认证后才首次 BindBuildSignature 还会清空矩阵，缓存 ApplyReportToRegistries
只物化 preset、不恢复 commandStatuses，修好测量后仍会丢证据。

新增已登记编译的 ModeHCommandCertificationProbe，复用生产 adapter 在两只实际激活的诊断
角色上采样可读、可还原字段：每条跨至少 3 帧、累计 0.3 秒，先读后重申，双方均保持且
还原成功才写逐效果 VerifiedBehavior；目标/路径/技能 marker 无对应遥测，保留 ReportOnly。
不改 8 候选、5 原型、3 口令门槛；取消时还原 adapter、退订并回收双角色。

测量前绑定三签名并清当前 key；报告涵盖通用和招牌口令。缓存恢复仅接受 Passed 记录中的
已知逐 effect 合法状态，聚合口令状态重新派生并重查门槛，不能由缓存整体 Passed 绕过。
日志增加逐 key 可用口令数、汇总计数和实际门槛失败原因。12 项生产源码模拟检查通过，
不等于 Unity AI 运行时认证；下一份完整 F3 需确认首次认证、缓存命中、退出清理和最终状态。

第五轮 `BossRushValidation_20260902_133917_599.log`：首次认证 45719ms、缓存 142ms，
均 drafting=True、archived=True；Player.log 记录 passed=12、common=7、overall=True。
本项认证链已闭环；全赛季、真实押品与本轮新增入场/整备问题分别验收，不扩大此 PASS 的范围。

### CR-2026-09-02-014：丧尸撤离验收要求正数净化点，却没有准备样本

**严重级**：P2 Minor
**兼容分类**：COMPAT
**状态**：Fixed（2026-09-02 第六轮实机通过）

第五轮 `MODE_ZOMBIE_EXTRACTION` 因 `no_points_to_verify_settlement` 失败。生产开局净化点为 0，
用例只等 6 秒，没有拾取或击杀，却要求净化点大于 0 才触发真实成功事件。
现在先用生产 `CollectZombieModePurificationPoint` 收取 3 点并回读增量，再走撤离 UI/事件、
两次派发、钱包差值、模式退出和完整返基地断言。保持生产初始值、奖励比例和人工自然倒计时场景。

第六轮 `BossRushValidation_20260902_140735_794.log`：MODE_ZOMBIE_EXTRACTION PASS，pickup=0->3，现金 26302460->26302463->26302463，active=False、base_ready=True。成功事件与重复派发已验证；自然倒计时/离圈中断仍待人工。

### CR-2026-09-02-015：H 成功创建赛季后保留入场意图，回到地图会重开 H

**严重级**：P1 Major
**兼容分类**：COMPAT
**状态**：Fixed（2026-09-02 第六轮实机通过）

第五轮 H 首次认证和缓存用例均通过，后续无 H 入场操作的场景恢复却再次出现“赛季已创建”。
Player.log 9637 行创建新赛季，9691 行终章主动让路，9718 行销毁迟到 Boss，最终终章生成超时。
`TryMatchModeHSceneIntent` 只匹配而不消费，成功 `CreateDraftingSeason` 与关停也未清理。
首份赛季写入/读回成功后用既有 `CancelPendingEntry` 消费意图及预扣票所有权；失败时仍走原退款。
Dev 强制清理增加遗留意图回收，F3 在正常归档后核对意图已消失。

第六轮 `BossRushValidation_20260902_140735_794.log`：两次 H 入场均 intent_cleared=True、archived=True。Player.log 只创建两个测试赛季，后续重访竞技场不再额外创建；终章顺利完成。正常入场消费已验证；入场失败退款仍按独立场景验收。

### CR-2026-09-02-016：H 将冻结弹药写入装备槽，真实比赛生成反复失败

**严重级**：P1 Major
**兼容分类**：COMPAT / WIRE+
**状态**：Fixed（2026-09-02 第六轮实机通过）

第五轮真实生成出现 `kit_apply_ammo_plug_failed:starter_sidearm` 与 `starter_marksman_rifle`。
官方 `ItemUtilities.TryPlug` 只枚举装备槽，不能存入弹药；直接给 StackCount 写总量还会被上限截断。
`ModeHLoadoutKitApplicator` 改为同步填新造枪弹匣和临时选手库存，按上限分堆，严格保留冻结总量；
逐实例登记所有权，不合并到旧堆，任何失败逆序回收。校验口径、实际存入结果与真实弹匣数量。
官方直接填库存不会刷新 `_bulletCountCache`：用缓存字段失效后通过公开 BulletCount getter 重算，
字段缺失明确拒绝；绑定名称在本机反编译源码确认，不改变其它模式或玩家武器。
新增 `MODE_H_STARTER_KITS` 在已认证 Drafting 租约内逐件生成/装配全部 starter，回读槽位、
弹匣可用数与实际库存总数；超时/取消迟到实例仍回收。此测试不代表整场 AI 战斗或六场赛季通过。

第六轮 `BossRushValidation_20260902_140735_794.log`：MODE_H_STARTER_KITS PASS，8/8。步枪 loaded=usable=30 / total=120，射手枪 10/40，手枪 13/60；全部槽位 TypeID 正确。Player.log 无 kit_apply_ammo_plug_failed 或 H 技术故障。装配已验证，完整 AI 比赛/六场赛季仍未由此证明。

### CR-2026-09-02-017：终章直接销毁 Boss 未清掉熔石掉落订阅

**严重级**：P2 Minor
**兼容分类**：COMPAT
**状态**：Fixed（2026-09-02 第六轮实机通过）

第五轮终章让路销毁迟到 Boss 后，`FINAL_LEAK_DELTA` 记录 affix_stone_hooks=0->1。
熔石服务只有死亡结算、宿主销毁和下一次登记时的死引用裁剪，没有场景收尾；
终章直接 Destroy 与迟到生成分支也未经过 `ClearBossRandomLootTracking`。
现在 Integration 场景回调先 ClearAllTracking；终章两条主动销毁路径先清自身掉落登记再 Destroy，
覆盖场景回调之后才到达的实例。自然死亡流程不提前清理，不改变掉落概率、不用测试清场抹平计数。

第六轮 `BossRushValidation_20260902_140735_794.log`：CAMPAIGN_FINAL_BOSS PASS，death_presentations=1、bgm_owners=0；FINAL_LEAK_DELTA PASS，affix_stone_hooks=0->0，全部被测登记回到基线。常规终章与返场已验证；主动中止/迟到生成分支仍属于故障注入边界。

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
**状态**：Fixed（第 2 项已于 2026-08-31 随完整战斗/押注闭环修复）
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

#### 2026-08-31 追加闭环

看盘页现由正式对局控制器生成确定性公开分、胜率与赔率；锁盘后进入分帧生成、战斗、结算、
伤病/战痕、赛间恢复、转会与名人堂流程，不再用 `combat_wiring_pending` 回滚看盘。

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
**状态**：Fixed（第 6 项于 2026-08-31 以可选字段 `PendingIssueBanner` 向后兼容落盘）
**来源**：2026-08-29 四系统复审（①经官方源语义核对）

#### 问题清单

1. (P2) **悬赏现金忽略 `EconomyManager.Add` 失败返回值**：`DailyReportRewards.cs:126` 丢弃返回值；官方 `Add` 在 `Instance==null` 时返回 false 不抛异常 → `SettleBounty` 仍置 `BountyRewardClaimed=true` 落盘，补发被闸死，现金永久丢失且报纸公示「已寄出」。窗口窄（场景闸使 gameplay 帧 Instance 通常就绪）但属确定的错误处理缺失，一行修复。与本系统「先发后标记、宁可重发不吞奖」纪律相悖——里程碑物品路径检查了投递结果，现金路径没有。
2. (P3) **`BuildCandidates` 把瞬时失败缓存成会话级空池**（`DailyReportRewards.cs:164-217`）：`Instance==null`/异常返回的空数组被缓存到进程结束，该品质整会话 `no_candidate`；奖励不丢（下会话补发）但当次拿不到。空结果不应缓存。
3. (P3) **同场景热切开后建造菜单没有报箱**：注入闸只在进基地装配管线跑一次（`DailyReportMailboxBuilder.cs:103`、`IntegrationDeferredBootstrap.cs:300-306`），原地开开关要出图再回。PetNest 同构（`PetNestBuilder.cs:94`）。
4. (P3) **F3 dump「悬赏题目」打的是昨日已结算题**（`F3DebugCheatMenuActions.cs:845` 用 `data.BountyKindId` 而非 `GetActiveBounty()?.Id`），与旁边「悬赏进度」（今日题）不一致，干扰 D6 排查。
5. (P3) **跨天补发里程碑会换奖品**：`DailyReportService.cs:582` 补发传当前 DayIndex，与 `DailyReportRewards.cs` 头注释「同一 (seed, day, slot) 同一件」矛盾；品质一致、无重复发放，纯确定性承诺破口。
6. (P3) **未读提示不持久**：`DailyReportService.cs:83` `_pendingIssueBanner` 仅内存；战斗中跨天→未回基地退游戏，下会话不再提示（数据无损）。

### CR-2026-08-31-001：后山 P1：出击餐在正常流程中永远不会生效

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核（静态证明 + 官方反编译源时序核对）

#### 位置

- `Integration/BackMountain/RaidMealService.cs:125` `ApplyForRun()` 要求 `CharacterMainControl.Main != null`
- `Integration/BackMountain/BackMountainRuntimeModule.cs`（修复前）唯一调用点在 `RefreshFacilitiesForScene`，由 `SceneManager.sceneLoaded` 驱动

#### 问题

官方主角由 `LevelManager.CreateMainCharacterAsync` **异步**创建（反编译源 `LevelManager.cs:520`），`sceneLoaded` 回调那一刻 `CharacterMainControl.Main` 必然为 null。`ApplyForRun` 早返后**没有任何重试路径**（模块 `OnUpdate` 不重试，全仓仅此一个调用点）。`RaidMealService.cs:13` 的头注释自己写明生效点应为 `OnLevelInitialized`，但实现没接到那里。

#### 影响

三种出击餐（龙息果 / 焚心椒 / 幽影蘑菇）全部失效：玩家在基地吃下 → 提示「下一局出击时生效」→ 进局零加成零提示，且登记不被消费，之后每局同样失败。「种地 → 做饭 → 带增益出击」这条养成闭环的最后一环是断的。

#### 修复

模块拆成两个时机：设施注入留 `OnSceneLoaded`，角色加成改挂 `LevelManager.OnAfterLevelInitialized`（官方 `BuildingEffect` 与本 mod `SetBonusManager` / `DragonSetBonus` 用的同一时机）；订阅幂等 + 成对退订；模块若在关卡初始化之后才 bootstrap，用 `LevelManager.AfterInit` 补一次。

#### 验证需求

编译 + guard 已绿；**需实机 smoke**：基地吃餐 → 进局确认飘字与属性变化。

### CR-2026-08-31-002：后山 P1：展示柜加成在战局内实际不存在

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核

#### 位置

- `Integration/BackMountain/ShowcaseService.cs:181` `ReapplyBonuses()`；修复前场景侧调用点与 001 同一时机

#### 问题

与 001 同源：主角尚不存在时 `ReapplyBonuses` 只摘不挂，而注释所称「等下次场景就绪再来」并无对应机制——下一次仍是同一个过早时机。加成实际只在「UI 里登记新战利品」与「交付章节触发解锁事件」两个瞬间挂上，此后任何一次切场景即消失且不再重挂。

#### 影响

建筑描述承诺的「登记得越多，你越经打」在战局里不成立：进竞技场打 Boss 时 MaxHealth 加成为零。

#### 修复

与 001 共用 `OnAfterLevelInitialized` 挂载点；解锁事件路径额外调一次 `RefreshCharacterBoundEffects()`，让交付当场生效。

#### 验证需求

编译 + guard 已绿；**需实机 smoke**：登记后进局确认最大生命提升。

### CR-2026-08-31-003：征程 P1：终章决战打输一次即永久卡死

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核

#### 位置

- `Campaign/CampaignFinalBoss.cs`：修复前 `CleanupCampaignFinalBoss` 只有两个调用点（死亡回调、让路 tick）

#### 问题

玩家召唤「冠军之影」后死亡或中途离场时，Boss 随场景销毁、`OnDeadEvent` 永不触发，`campaignFinalBossActive` 卡在 true。后果三连：召唤石不再生成、`CanStartCampaignFinalBoss` 恒 false、契约 HUD 因模式桥短路且 `campaignLastObservedMode` 为 null 而永不 `ResetSession`，横幅在基地常驻。隐藏解法（随便开一局其它模式触发让路清理）玩家不可能自行发现。

#### 影响

战役高潮不可重试——而 1.6× 数值的强化 Boss 恰恰是最可能打输的一场。

#### 修复

三条收尾路径补齐：① `CampaignRuntimeModule.OnSceneLoaded` 幂等收尾（覆盖死亡回基地这条主路径）；② 让路 tick 增加「生成已出结果但实例已销毁」检测；③ 收尾自增 `campaignFinalBossRunId` 作废在飞的异步生成，协程回来发现编号不符即销毁产物，避免留下无人记账的强化女巫。收尾同时清掉终章局内追踪（仅在武装章节为终章时），修掉 HUD 在基地常驻。

#### 验证需求

编译 + guard 已绿；**需实机 smoke**：召唤决战 → 故意战死 → 回基地再进竞技场，确认召唤石重新出现且 HUD 不残留。

### CR-2026-08-31-004：后山 P1：展示柜收藏跨存档槽泄漏，可写脏另一个档

**严重级**：P1 Major
**兼容分类**：`COMPAT`（修复本身不改 schema；未修时会产生错误数据）
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核

#### 位置

- `Integration/BackMountain/ShowcaseService.cs`：`_displayed` / `_loaded` 无槽位烙印、不订阅 `OnSetFile`；`NotifySlotChanged()`（:311）**全仓零调用**

#### 问题

同一次会话内从 A 档切到 B 档：B 档能看到并享受 A 档的收藏加成；在 B 档做一次登记，`Store()` 会把「A 档收藏 + 新条目」整体写进 B 档的 `BossRush_BackMountain_Showcase_v1`——永久污染。对照组 `CampaignPersistence` 做了完整防御（OnSetFile 订阅 + 槽位比对 + 下游广播），注释还点名「PetNest 曾踩过同类坑」。

#### 修复

两道防线：① 运行时模块订阅 `SavesSystem.OnSetFile` / `OnSaveDeleted`，调 `ShowcaseService.NotifySlotChanged()` 与 `GardenSeedInjector.NotifySlotChanged()`；② `ShowcaseService` 缓存加槽位烙印，`EnsureLoaded` 每次比对 `SavesSystem.CurrentSlot`，对不上就摘掉旧加成并从新槽重读。

#### 验证需求

编译 + guard 已绿；**需实机 smoke**：A 档登记 → 不退游戏切 B 档 → 确认 B 档展示柜为空且加成为 0。

### CR-2026-08-31-005：征程 P2：召唤石维护 tick 每帧分配字符串

**严重级**：P2 Minor
**兼容分类**：`SAFE`
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核

#### 位置

- `Campaign/CampaignFinalBoss.cs` `ShouldCampaignFinalBossAltarExist`（修复前把 `IsCurrentSceneValidBossRushArena()` 排在章节检查之前）
- `ModBehaviour.cs:82`：`GetCurrentMapConfig` 经 `SceneManager.GetActiveScene().name` 每次分配托管字符串

#### 问题

战役恒开，该 tick 对每个玩家的每一帧生效，60fps 下约 2–4 KB/s 稳定 gen0 垃圾。量级不致掉帧，但违反仓库自身的热路径零分配纪律（AGENTS.md 4.12）。

#### 修复

判定改序：零分配的终章契约查询先短路；场景判定另按 `CampaignRuntimeModule.SceneGeneration` 缓存（`IsCampaignArenaSceneCached`），彻底消除每帧分配。

### CR-2026-08-31-006：征程 P2：契约 HUD 每帧构建字符串，与头注释承诺不符

**严重级**：P2 Minor
**兼容分类**：`SAFE`
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核

#### 位置

- `Campaign/CampaignHud.cs`：头注释称「其余帧只有一次字符串构建前的短路比较」，实现却是先构建后比较（title 两次拼接 + `BuildBody()` 的 `ToString()`，合计 ≥3 次分配），只有 TMP 赋值被短路

#### 修复

改为构建**之前**做零分配脏检查：用复用的 `List<int>` / `List<bool>` 记录上次显示时各目标的 `Current` 与 `Failed`，逐项比对整数与 bool；内容真变了才拼字符串。隐藏时快照作废，保证再次显示必重建一次。

### CR-2026-08-31-007：征程/后山 P3 设计取舍汇总（7 项）

**严重级**：P3 Note
**兼容分类**：`COMPAT`
**状态**：Fixed（用户于 2026-08-31 明确要求全部修复）
**来源**：2026-08-31 征程/后山全面审核

#### 问题清单

1. **第一章对新玩家偏硬，且是整条内容线的总闸门**：ch1 要求「通关标准局 + 前 3 波无伤」同时达成，而 ch1 交付才解锁菜地。可考虑无伤挪去后面章节或降为 2 波。
2. **ch3 文案与实现口径不一致（宽松方向）**：「清掉 8 个敌对阵营的头目」实现上计数所有 `isBossCharacter` 击杀，无阵营过滤（`CampaignObjectiveCollector.cs:50`）。玩家不吃亏。
3. **展示柜「战利品」的真实定义是「手持 + Q≥5」**：登记走 `CurrentHoldItemAgent`（`ShowcaseUI.cs:249`），头盔/护甲等非手持高品质掉落无法登记；而出击餐（Q5、菜地可量产）反而可占 3 格并计入满柜加成。
4. **`CampaignNoteBridge.cs:18` 注释声称的兜底展示面不存在**：公告板面板实际没有线索页签，线索唯一展示面是官方笔记图鉴。需删注释或补页签。
5. **终章击杀后、交付前的毛边**：召唤石立即重新出现，可反复重打冠军之影（无重复奖励、无害，略破仪式感）。是否加 `ContractActive` 门禁由 owner 定。（同项的 HUD 常驻已随 003 修复。）
6. **`RegisterMeal` 失败时饭仍被框架吃掉**：`RaidMealUsageBehavior.cs:60` 登记失败直接 return，但 `CA_UseItem.OnFinish` 照常扣物品且无提示。窗口极小（需恰逢 `IsSaving`）。
7. **「目标已达成」不落盘，退游戏即整章重打**：ReadyToDeliver 是会话态（`CampaignProgressService.cs:36` 设计如此）。对 ch3（8 头目 + 撑满 10 分钟）这类长目标重打成本不低；是否为它落一位存档需 owner 取舍。

#### 修复决策

1. 第一章无伤门槛降为前 2 波，同时保留通关时的短局兜底判定。
2. 第三章中英文文案统一为「击败 8 名头目」，与实际 Boss 判定一致。
3. 展示柜增加穿戴物登记入口，并排除后山自产种子/餐品，避免量产物刷收藏。
4. 注释改为只承诺官方笔记图鉴；不再声称公告板存在未实现的线索页签。
5. 召唤石只在终章 `ContractActive` 时出现，击杀后等待交付期间不可重复召唤。
6. 出击餐在存盘中、陌生 ID 或登记失败时禁止使用，不让框架先扣物品。
7. `ReadyToDeliver` 作为可选字段向后兼容落盘，重启后恢复待交付态。

### CR-2026-08-31-008：全内容可用性复核补充（5 个 P1 + 2 个 P2）

**严重级**：P1（5 项）/ P2（2 项）
**兼容分类**：`COMPAT` + `SCHEMA+`
**状态**：Fixed
**来源**：2026-08-31 全内容静态审核、调用链与失败路径复核

#### 问题与修复

1. (P1) **模式H 地图列表与冻结目标漂移**：通用地图页可选不受支持地图，票已预扣但运行时拒绝接管。现只展示 `ModeHMapSupportRegistry.IsSupportedPair` 支持项，点击时按原配置索引重新冻结 sceneName/sceneID。
2. (P1) **模式H 清场误删友方/功能 NPC**：原生隔离会销毁除主玩家外的全部角色。现保留玩家队、主玩家、遗种巢随从和 `INPCController`，只清除明确敌对原生单位。
3. (P1) **征程交付忽略存档失败**：先改活缓存/发现金再写盘，失败时可重复交付。现克隆存档做事务写入，存档成功后才发布运行时 token，现金发放失败或落盘失败均回滚并保持待交付。
4. (P1) **词缀锻造静默写失败会吞现金/熔石或留下半成品**：现所有 KV 写入均读回核验；重铸、锁词缀按「核验旧值→扣款→扣材料→写入」执行，失败恢复槽位并核验退款/回滚结果，补偿失败会向玩家报错。
5. (P1) **后山登记/餐品存档写失败仍报告成功**：展示柜与出击餐写入均读回核验，登记/移除失败恢复内存与角色加成；餐品只有持久清除成功后才施加局内效果。
6. (P2) **鸭皇图鉴可被低价卖回且商店刷新会丢书**：图鉴设为不可出售、价格系数恢复正常；商店尚未可注入时保留缓存库存，存档事件订阅/退订成对。
7. (P2) **随机商人初始化时序会让库存为空或每次启动刷新**：反射绑定前置，配置完成前保持 inactive；首次激活只补一次库存，刷新时间戳稳定，弹药/医疗堆叠 99、高品质物品单件。

#### 验证需求

Windows 编译、相关结构守卫与全量 guard；实机需覆盖模式H 受支持地图、友方 NPC 共图、
模拟存档失败后的征程/锻造/后山重试，以及图鉴和随机商人的商店刷新。

### CR-2026-08-31-009：首次 F3 完整验收暴露的运行时回归（4 个 P1 + 3 个 P2）

**严重级**：P1（4 项）/ P2（3 项）
**兼容分类**：`COMPAT` + `OPERATIONAL`
**状态**：Open（修复与静态验证已完成；等待下一份完整 F3 报告确认后转 Fixed）
**来源**：Player.log + `BossRushValidation_20260831_125247_246.log`

#### 已确认问题

1. (P1) `CampaignContentCatalog` 使用 `JsonUtility` 解析两层对象数组时，实机只读出 version、
   `chapters` 静默为 null，导致已部署且哈希正确的正式表回退到硬编码。
2. (P1) Boss 乱入从图鉴目录抽到稳定 key 后直接进入 SpawnCore，但标准模式没有初始化
   `cachedCharacterPresets`；五次候选都报未找到预设。F3 只看 `TryForceTrigger=true`，仍把空转计 PASS。
3. (P1) F3 清场把除主玩家以外的所有存活角色都算成敌人；日志在开波前已有一个友方角色，
   清掉唯一 Boss 后仍报 `enemies=1` 并中止后续全套模式。
4. (P1) 动态商人 Animator 首个 `MagicBlendState.OnStateEnter` 早于 `MagicBlending.Start`，
   对空 Playable 调 `SetJobData` 抛异常；同时自定义 merchantID 被官方数据库报“未配置商人”。
5. (P2) Harmony 逐类隔离扫描对程序集内每个普通类型都创建 processor，普通业务方法名
   `Cleanup` 被 Harmony 当成 cleanup 回调，产生 3 条虚假补丁失败；真实补丁实际为 53/53。
6. (P2) 运行时 AddComponent 的日报报箱和征程公告板没有在 `base.Awake` 前初始化官方私有
   `otherInterablesInGroup`，每次进基地都产生可稳定复现的 NRE 警告；同类新增交互组件有相同风险。
7. (P2) F3 在 0.35 秒内轮流触发八种事件并立刻收尾，事件横幅在官方队列中延后播放，
   验收结束后仍持续弹出；这既污染清场结论，也遮蔽异步生成失败。

#### 已实现修复

- 征程表复用 Mode H 的严格 token parser，并保留整表签名/顺序/目标校验。
- 乱入桥先幂等准备官方 preset 缓存；八个事件新增实际副作用验收协议，F3 逐项等待至
  `Passed/Failed` 或 30 秒超时并写独立 case，不再把调度成功等同功能成功。
- 清场只统计 `Team.IsEnemy(Teams.player, team)` 的存活角色，明确排除遗种随从，并把残留实例、
  运行时 team 与 preset key 写进报告。
- 新增 MagicBlend 初始化顺序兼容补丁；商店以官方 ID 引导 Awake，同帧 Start 前切回稳定 Mod ID
  并覆盖事件库存。
- Harmony scanner 只处理类级或方法级含 `[HarmonyPatch]` 元数据的类型。
- 新增交互体均在 `base.Awake` 前建立私有分组空表；完整验收期间抑制普通消息/大横幅入队，finally 复位。

#### 验证需求

下一份 Dev F3 完整报告必须满足：Campaign `source=Json`；八个 `RANDOM_EVENT_*` 分项均 PASS；
Boss 乱入 `spawned>0`；商人 `merchant/shop=true, entries>0`；标准清场 `enemies=0`；完成后继续覆盖
Mode D/E/F/G/H、Zombie、终章/BGM、回基地回读与最终性能。Player.log 同时不得再出现本条所列
BossRush Harmony、Campaign、MagicBlend、merchantID 或自建 Interactable `base.Awake` 错误。

### CR-2026-09-01-010：第二次 F3 验收暴露的共享队列与模式清理回归

**严重级**：P1（2 项）/ P2（1 项）/ P3（1 项）
**兼容分类**：`COMPAT` + `OPERATIONAL`
**状态**：Open（修复与静态验证已完成；等待下一份完整 F3 报告确认后转 Fixed）
**来源**：Player.log + `BossRushValidation_20260831_152526_013.log`

#### 已确认问题

1. (P1) 标准模式的 Boss 乱入复用 Mode E/F 分帧后处理队列，但 scheduler 位于
   `TickWavesArenaRuntime` early-return 之后；标准模式中任务永远不推进，最终超时并在下一模式被清空。
2. (P1) Mode E/F 克隆预设在角色销毁前被提前 Destroy。Unity 伪 null 使后续
   `dropBoxOnDead`、Health 与 OnDestroy 链 NRE，Mode E 结束后留下 `no_preset` 角色并阻断整套验收。
3. (P2) Mode D 生成只强制 AI 仇恨，没有把中立官方预设的 runtime team 改成玩家敌对；
   角色虽进入本波登记表，却不满足战斗与 F3 敌对判定。旧 `EndModeD` 还只清列表，不销毁实体。
4. (P3) Mode E 动态分类商店在配置 merchantID 前以默认 `Albert` 执行 Awake，每次生成商人产生
   13 次无效官方数据库查询与警告；库存随后被覆盖，未造成当前用例失败，但污染日志并做无用工作。

#### 已实现修复

- 共享后处理 scheduler 在 WavesArena early-return 前无条件推进；空队列为 O(1) 快速返回。
- Mode D 在登记前设置并回读 wolf 敌对状态，失败即销毁；结束路径确定性注销恢复、禁掉落并销毁实体，
  非活动状态下重复调用也会清残留。
- Mode E/F 克隆预设改由对象级 lease 持有，角色 OnDestroy 后延迟释放；退出路径不再 Hurt，按顺序
  注销运行时并销毁角色。
- Mode E StockShop 使用 inactive → 官方 bootstrap ID → Awake → 稳定 Mode ID → 分类库存流程。
- F3 新增模式自有角色诊断与 2 秒逐帧清场等待；Mode D 报告登记对象的存活、阵营和敌对状态。

#### 验证需求

下一份完整 Dev F3 报告必须看到 `RANDOM_EVENT_BOSSINTRUSION`、`MODE_D_LIFECYCLE`、
`CLEANUP_AFTER_MODE_D`、`MODE_E_LIFECYCLE` 与 `CLEANUP_AFTER_MODE_E` 全部 PASS，并继续执行
Mode F/G/H、Zombie、终章/BGM、最终清场与存档回读。Player.log 不得再出现 Mode E 清理 NRE、
`scheduler_cleared` 乱入失败或 Mode E 分类商店 `Albert` 未配置噪声。

## UNVERIFIED / Seeded Leads

> 这里的内容不是 bug。升格前必须读代码或运行验证。

- **遗种巢 跨档写污染组合链**（016 的延伸，逐环节已静态核过、多步组合需实机）：关开关后已挂 handler 触发 `AddSouls` 会从当前槽重建缓存 → 主菜单切档（无人清缓存）→ 重开开关（EnsureBootstrapped 不重置缓存）→ 下次 Commit 把 A 档巢数据写进 B 档。与 017 的日报跨档渗漏同构。
- **遗种巢 远征奖励非崩溃失败被吞**：`PetNestExpeditionService.cs:420-447` 注释宣称可补发，但 `GrantRewards`（:560-631）内部吞异常正常返回、:432 无条件置 `rewardsGranted=true`——蛋实例化失败/EconomyManager 异常 = 一次性丢失。修复需按条目粒度记账（现金已发、物品失败的部分重发问题）。
- **遗种巢 OnExpedition 孤儿锁无自愈**：Nest key flush 成功、Expedition key flush 异常进 StoreFaulted 后（`PetNestSaveCoordinator.cs:141-144` 逐 key 独立失败），官方存盘可落「崽=OnExpedition」而远征记录缺失；无 reconcile 路径，该崽永久锁死。建议 Normalize 处加「OnExpedition 且远征表无匹配 → 复位 InNest」自愈。
- **遗种巢 P3 一组**：基地闲逛崽实为跟随玩家（含 >40m 传送，观感是仪仗队）；场景切换只停两个演出层、主面板 modal lease 极端时序可带进战斗图；天赋/战痕/蛋 KV 展示裸英文 key。
- **模式H 关停竞态孤儿**：StopCoroutine 打断 `CreateCharacterAsync` await 期间，`CreateIsolatedAsync`（SpawnBridge:58-176）async 延续仍会完成并登记抑制表+生成隔离角色，RollbackAll 只回收已入列 handle（窗口数帧）。
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
