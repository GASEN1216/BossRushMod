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
| `ZombieModeCashPromptButtonLayoutGuard.py` | 现金投入弹窗底部确认/跳过/返回按钮必须固定在独立按钮栏。 |
| `ZombieModeNpcHelperGuard.py` | NPC 服务 helper 注入流程。 |
| `ZombieModeRewardCatalogGuard.py` | 奖励 catalog 与 L10n key 一致性（**不再核对具体数值**，避免阻挡平衡迭代）。 |
| `ZombieModeExtractionFactoryGuard.py` | 撤离 NPC/Area 通过 `ModeExtractionPointFactory` 创建。 |
| `ZombieModePerformanceRegistryGuard.py` | 性能层级队列引用 run-only 注册表。 |
| `ZombieModeHotPathMeleeCacheGuard.py` | 受伤热路径不得实例化临时物品判断近战类型。 |
| `ZombieModeBossLifecycleGuard.py` | BossInstance 必须通过 Lifecycle 子对象访问运行期追踪字段；死字段不得回归。 |
| `ZombieModeRewardCandidateCacheGuard.py` | 奖励/掉落随机物品候选必须缓存 `ItemAssetsCollection.Search` 结果。 |
| `ZombieModeSpawnPositionHelperGuard.py` | 刷怪位置 helper 迁移后，旧引用和编译列表必须一致。 |
| `ZombieModeTemporaryNpcBoundaryGuard.py` | 临时 NPC 是 run-only service terminal 的边界。 |
| `ZombieModeGoalExperienceGuard.py` | `docs/2026-05-03_末日丧尸模式_goal执行文档.md` 的玩家体验 P0/P1/P2 代码 invariant。 |
| `ZombieModeUIHelperGraphicCompositionGuard.py` | 运行时 UI helper 不得在同一对象叠加 `Image` 与 `TextMeshProUGUI`。 |
| `ZombieModeProductionReadinessGuard.py` | 共享刷怪、掉落、Boss 状态 modifier、属性清理等生产化 invariant。 |
| `ZombieModeReview20260503Guard.py` / `ZombieModeReviewFixGuard.py` | 2026-05-03 审查修复项，包含“物品不阻止入场、入图后转仓库/收件箱”契约，防止已确认代码债回归。 |
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
- `PerformanceTierAdjusterGuard.py`：通用性能层级判定的 invariant；ZombieMode 必须复用 helper 而非自走 if/else。
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
