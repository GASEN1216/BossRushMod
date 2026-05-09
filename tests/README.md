# tests/ — 项目守护脚本说明

本目录的 `*.py` 脚本是**静态文本守护**（grep 风格），用于防止特定代码 invariant 被误改。

每个守护对应一条具体的"易回归点"，**不是**代码功能测试。功能验证仍以"编译 + 现场冒烟"为主。

---

## 丧尸模式（ZombieMode）守护

### 防回归类（保留）

每个守护针对一条具体 invariant，破坏 invariant 时立即报错：

| 守护 | 守护内容 |
| --- | --- |
| `ZombieModeCompileListGuard.py` | `compile_official.bat` 必须列出全部 `ZombieMode/*.cs`。 |
| `ZombieModeLocalizationGuard.py` | 丧尸模式 L10n key 必须被 `LocalizationInjector.cs` 注入。 |
| `ZombieModeItemIdentityGuard.py` | `ZombieTideInvitation` / `ZombieTideBeacon` TypeID 不被复用。 |
| `ZombieModeSpawnEnemyCoreReuseGuard.py` | `TrySpawnZombieModeNormalZombieAsync` / `TrySpawnZombieModeBossAsync` 必须走 `SpawnEnemyCore(...)`。 |
| `ZombieModeBossRushSpawnPointsOnlyGuard.py` | 丧尸刷怪点只能来自 BossRush 地图配置画像，不得混入原版 `CharacterSpawnerRoot`。 |
| `ZombieModeNormalZombieCapAndAggroGuard.py` | 普通丧尸压力必须受 50 只上限、最近 BossRush 刷怪点、玩家仇恨锁定约束。 |
| `ZombieModePacingTuningGuard.py` | 周期压力刷怪间隔必须保持为 30 秒，避免回归到每秒补怪。 |
| `ZombieModeNormalSpawnPhaseGuard.py` | 普通丧尸异步生成必须在等待/落地注册阶段继续检查调用方允许的战斗阶段。 |
| `ZombieModeBossMultiplierGuard.py` | 不允许 `Health.defaultMaxHealth` 反射写。 |
| `ZombieModeRunScopedRegistryGuard.py` | `RunOnlyObjects` 是协程/事件唯一可信源（无 `RegisteredCoroutines` / `EventListenerHandles` 双登记）。 |
| `ZombieModeStateModelGuard.py` | 核心状态机字段、相位枚举、清理流程结构稳定。 |
| `ZombieModeRunOnlyCleanupGuard.py` | Run-only cleanup 通道完整。 |
| `ZombieModeTransactionBoundaryGuard.py` | 入场资源提交/回退事务边界（SPEC 14/15）。 |
| `ZombieModeMapSelectionEntryFailureGuard.py` | 地图加载前的入场失败不得强制回基地，且随身/任务/绑定物品不得在打开或确认地图 UI 前阻止入场。 |
| `ZombieModeTargetSceneActivationGuard.py` | 目标子场景必须成为 ActiveScene 后才初始化丧尸模式，避免扫错场景。 |
| `ZombieModeGroundZeroBossRushIsolationGuard.py` | 丧尸模式入图/传送期间不得触发普通 GroundZero BossRush 初始化。 |
| `ZombieModeSpawnCoreModeDIsolationGuard.py` | 丧尸模式复用 `SpawnEnemyCore(...)` 时不得执行 ModeD 伤害倍率归一化。 |
| `ZombieModeChoiceUiPauseAndLayoutGuard.py` | 丧尸模式选择 UI 必须暂停时间/释放鼠标并保持 HUD 指定偏移。 |
| `ZombieModeStarterEquipmentAndNpcUiGuard.py` | 开局保护三件套、补给终端头盔可见、服务 UI 重建不得重复加 Canvas。 |
| `ZombieModeInsuranceExitGuard.py` | 失败保险结算条件。 |
| `ZombieModeSafeZoneGuard.py` | 安全区破隐 5 种触发。 |
| `ZombieModeAreaDamagePlayerGuard.py` | 丧尸区域伤害必须保留真实伤害源并从 `mainDamageReceiver` 进入原版伤害链，避免空 source 触发 `Health.Hurt` 补丁异常。 |
| `ZombieModeTemporaryNpcProtectionGuard.py` | 临时 NPC 保护（避免被原版 cleanup 误清理）。 |
| `ZombieModeCashAndOriginalExtractionGuard.py` | 现金与原版撤离点的场景隔离 invariant。 |
| `ZombieModeBeaconUnavailableReasonGuard.py` | 尸潮信标在撤离读条等互斥状态下必须显示准确不可用原因。 |
| `ZombieModeCashPromptButtonLayoutGuard.py` | 现金投入弹窗底部确认/跳过/返回按钮必须固定在独立按钮栏。 |
| `ZombieModeNpcHelperGuard.py` | NPC 服务 helper 注入流程。 |
| `ZombieModeRewardCatalogGuard.py` | 奖励 catalog 与 L10n key 一致性（**不再核对具体数值**，避免阻挡平衡迭代）。 |
| `ZombieModeExtractionFactoryGuard.py` | 撤离 NPC/Area 通过 `ModeExtractionPointFactory` 创建。 |
| `ZombieModePerformanceRegistryGuard.py` | 性能层级队列引用 run-only 注册表。 |
| `ZombieModeHotPathMeleeCacheGuard.py` | 受伤热路径不得实例化临时物品判断近战类型。 |
| `ZombieModeBossLifecycleGuard.py` | BossInstance 必须通过 Lifecycle 子对象访问运行期追踪字段；死字段不得回归。 |
| `ZombieModeRewardCandidateCacheGuard.py` | 奖励/掉落随机物品候选必须缓存 `ItemAssetsCollection.Search` 结果。 |
| `ZombieModeRewardOptionCapGuard.py` | 已达到 stack/bool 上限的新奖励不得继续进入 reward catalog，避免空奖励。 |
| `ZombieModeRewardContractStateGuard.py` | Phase-2 contract 不得保留“只写不读”的运行时状态；即时效果与清理链必须保持闭环。 |
| `ZombieModeRewardPerformanceTierGuard.py` | 奖励运行时不得按性能档禁用，已选择的弹道/战场效果必须稳定生效。 |
| `ZombieModeRewardTrajectoryOptionGuard.py` | 跳弹/分叉/返程/螺旋/尾迹奖励必须接入生效路径；支援弹复用原版默认子弹兜底且不得递归触发。 |
| `ZombieModeRewardLowTierOptionGuard.py` | 奖励池不得按性能档屏蔽高成本弹道/战场奖励，避免不同机器出现不同玩法。 |
| `ZombieModeRewardGravityRuntimeResumeGuard.py` | 战场重力效果不得按性能档暂停/恢复，选择后在有效 run 内稳定运行。 |
| `ZombieModeNoPerformanceGameplayScalingGuard.py` | 生产代码不得保留按性能档改变玩法的旧标识、helper 或编译引用。 |
| `ZombieModeRewardPurificationCostGuard.py` | 消耗净化点的奖励必须在目录和点击路径同时做支付守卫，不得静默扣到 0。 |
| `ZombieModeRewardPlainTextGuard.py` | 新奖励文案必须说明玩家可见结果，避免概率/散射/战场效果描述误导。 |
| `ZombieModeRewardProjectilePoolGuard.py` | 池化 `Projectile` 上的玩家弹道 runtime 必须在禁用/不适用时清理并重置状态。 |
| `ZombieModeRewardSpreadSafetyGuard.py` | 散射奖励必须基于原武器基础值叠加，并在切枪/无枪/性能保护时恢复旧武器。 |
| `ZombieModePurificationPointPrefabCacheGuard.py` | 净化点 SoulCube prefab 查找必须在初始化阶段预热，并缓存未命中以避免掉点热路径反复全局扫描。 |
| `ZombieModeProjectileElementDamageGuard.py` | 元素弹奖励必须写入 ProjectileContext 的元素伤害字段，不能只挂 buff。 |
| `ZombieModeMerchantBulletSlotAmmoGuard.py` | 补给终端子弹价格与主/副武器口径匹配发弹契约。 |
| `ZombieModeLifestealChanceTradeoffGuard.py` | 命中回血必须是概率/品质/50% 上限/移速代价模型。 |
| `ZombieModeSpawnPositionHelperGuard.py` | 刷怪位置 helper 迁移后，旧引用和编译列表必须一致。 |
| `ZombieModeTemporaryNpcBoundaryGuard.py` | 临时 NPC 是 run-only service terminal 的边界。 |
| `ZombieModeRealTemporaryNpcRewardGuard.py` | 真人临时 NPC 奖励类型、低权重和终端共存契约。 |
| `ZombieModeRealTemporaryNpcPaymentGuard.py` | 真人临时 NPC 的净化点支付隔离契约。 |
| `ZombieModeRealTemporaryNpcCleanupGuard.py` | 真人临时 NPC 的 run cleanup 与追踪契约。 |
| `ZombieModeRealTemporaryNpcUiCurrencyGuard.py` | 真人临时 NPC 的阿稳/商店 UI 必须显示净化点口径而不是现金。 |
| `ZombieModeGoalExperienceGuard.py` | `docs/2026-05-03_末日丧尸模式_goal执行文档.md` 的玩家体验 P0/P1/P2 代码 invariant。 |
| `ZombieModeUIHelperGraphicCompositionGuard.py` | 运行时 UI helper 不得在同一对象叠加 `Image` 与 `TextMeshProUGUI`。 |
| `ZombieModeProductionReadinessGuard.py` | 共享刷怪、掉落、Boss 状态 modifier、属性清理等生产化 invariant。 |
| `ZombieModeReview20260503Guard.py` / `ZombieModeReviewFixGuard.py` | 2026-05-03 审查修复项，包含“物品不阻止入场、入图后转仓库/收件箱”契约，防止已确认代码债回归。 |
| `ZombieModeTimeAxisGuard.py` | ZombieMode 定时/运行时逻辑统一走 unscaled 时间轴，禁止无注释的 `Time.deltaTime` / `Time.time`。 |
| `ZombieModeWindowsVerificationScriptGuard.py` | Windows 端 `test_zombiemode_goal_windows.bat` 编译/部署/手工冒烟入口必须存在并保持串联。 |

### 已删除（2026-05-01 修复）

按 `docs/项目可能的待修复问题/2026-05-01_丧尸模式代码审查.md` §四.4 建议清理的 11 个：

- `ZombieModePhase{1-5}*Guard.py`：5 个阶段重复守护，由 `ZombieModeStateModelGuard.py` 覆盖。
- `ZombieModeReviewOptimizationGuard.py`：阶段性优化守护，整体已被本计划覆盖。
- `ZombieModeHudAndNpcServiceGuard.py`：与 `ZombieModeNpcHelperGuard.py` 重复。
- `ZombieModeSpec17NumericGuard.py` / `ZombieModeSpec17RewardTableGuard.py`：规格守门类，按章节核对数值会阻挡平衡迭代。
- `ZombieModeSpawnPathGuard.py`：被新 `ZombieModeSpawnEnemyCoreReuseGuard.py` 替代。
- `ZombieModeSpawnPresetHelperGuard.py`：守护对象 `TryCreateZombieModePresetCharacterAsync` 已删（合并到 `SpawnEnemyCore`）。

---

## 其他模式 / 共享层守护

- `OfficialCompileListFileExistenceGuard.py`：`compile_official.bat` 列出的 `.cs` 源文件必须存在。
- `PerformanceTierAdjusterGuard.py`：旧性能档 helper 必须从生产代码和编译列表移除，避免玩法随性能档变化。
- `RunScopedRegistryGuard.py`：通用局生命周期注册表迭代 helper；ZombieMode cleanup 必须复用 helper。
- `ModeD*Guard.py`：Mode D 波次/装备/掉落 invariant。
- `ModeE*Guard.py`、`ModeF*Guard.py`：对应模式守护。
- `MapSelectionInjectionReuseGuard.py`：BossRush 与 Zombie 都用 `MapSelectionEntryInjectionHelper`。
- `EnemyRecoveryHealthPreservationGuard.py`：敌人卡住回收时不要重置生命值。

---

## 怎么跑

```cmd
for %f in (tests\*.py) do python %f
```

或针对单个：
```cmd
python tests\ZombieModeBossMultiplierGuard.py
```

退出码 0 = 通过；非 0 = 失败（输出会指明哪条 invariant 被破坏）。
