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
| `ZombieModeBossMultiplierGuard.py` | 不允许 `Health.defaultMaxHealth` 反射写。 |
| `ZombieModeRunScopedRegistryGuard.py` | `RunOnlyObjects` 是协程/事件唯一可信源（无 `RegisteredCoroutines` / `EventListenerHandles` 双登记）。 |
| `ZombieModeStateModelGuard.py` | 核心状态机字段、相位枚举、清理流程结构稳定。 |
| `ZombieModeRunOnlyCleanupGuard.py` | Run-only cleanup 通道完整。 |
| `ZombieModeTransactionBoundaryGuard.py` | 入场资源提交/回退事务边界（SPEC 14/15）。 |
| `ZombieModeInsuranceExitGuard.py` | 失败保险结算条件。 |
| `ZombieModeSafeZoneGuard.py` | 安全区破隐 5 种触发。 |
| `ZombieModeTemporaryNpcProtectionGuard.py` | 临时 NPC 保护（避免被原版 cleanup 误清理）。 |
| `ZombieModeCashAndOriginalExtractionGuard.py` | 现金与原版撤离点的场景隔离 invariant。 |
| `ZombieModeNpcHelperGuard.py` | NPC 服务 helper 注入流程。 |
| `ZombieModeRewardCatalogGuard.py` | 奖励 catalog 与 L10n key 一致性（**不再核对具体数值**，避免阻挡平衡迭代）。 |
| `ZombieModeExtractionFactoryGuard.py` | 撤离 NPC/Area 通过 `ModeExtractionPointFactory` 创建。 |
| `ZombieModePerformanceRegistryGuard.py` | 性能层级队列引用 run-only 注册表。 |
| `ZombieModeHotPathMeleeCacheGuard.py` | 受伤热路径不得实例化临时物品判断近战类型。 |
| `ZombieModeBossLifecycleGuard.py` | BossInstance 必须通过 Lifecycle 子对象访问运行期追踪字段；死字段不得回归。 |
| `ZombieModeRewardCandidateCacheGuard.py` | 奖励/掉落随机物品候选必须缓存 `ItemAssetsCollection.Search` 结果。 |
| `ZombieModeSpawnPositionHelperGuard.py` | 刷怪位置 helper 迁移后，旧引用和编译列表必须一致。 |
| `ZombieModeTemporaryNpcBoundaryGuard.py` | 临时 NPC 是 run-only service terminal 的边界。 |

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
