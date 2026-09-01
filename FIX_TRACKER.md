# FIX_TRACKER.md — 修复状态与兼容性流水账

> 修 bug、回归、兼容问题或 owner decision 后更新本文件。旧路径 `docs/协作/FIX_TRACKER.md` 只做兼容转发。

## 状态定义

| 状态 | 含义 |
| --- | --- |
| `fixed` | 已修复，并记录验证方式 |
| `accepted` | 已确认是问题，方案已定，尚未落地 |
| `refuted` | 曾怀疑是问题，核实后不是 |
| `deferred` | 确认是问题，但本轮不修，原因明确 |
| `documented` | 不改代码，仅文档化限制、取舍或人工验收要求 |
| `needs-owner-decision` | 需要 owner 对兼容/产品/安全取舍拍板 |

## 条目模板

```markdown
---
### YYYY-MM-DD 标题

**状态**: fixed / accepted / refuted / deferred / documented / needs-owner-decision  
**Finding**: CR-YYYY-MM-DD-NNN / 无  
**兼容分类**: SAFE / COMPAT / SCHEMA+ / SCHEMA- / WIRE+ / WIRE- / BREAKING / OPERATIONAL  
**版本/Commit**: commit hash / 未提交 / 不适用  
**Owner decision**: 需要/不需要；结论
**现象**: 玩家可见症状、日志或触发条件
**根因**: 代码路径、引入机制、为什么会发生
**修复内容**:
- 新增文件: 路径（是否加入 `compile_official.bat`）
- 修改文件: 路径
**兼容性影响**: 对存档、配置、TypeID、Harmony/反射、资源、部署的影响
**验证方法**:
1. 编译:
2. Guard:
3. 人工 smoke:
**未验证/需人工**: 如有
**失败尝试**: 如有
```

---
### 2026-09-01 编译错误 CS1631 + 语法探针的检测盲区 + 两组 CS0649

**状态**: fixed
**Finding**: 无（owner 实机 `compile_official.bat` 报出）
**兼容分类**: COMPAT（CS1631 修复与 Mode H 卡面补全）+ SAFE（CS0649 抑制、探针默认值）
**版本/Commit**: 未提交
**Owner decision**: 不需要

#### 1. CS1631：catch 子句体内不能 yield（编译失败，必修）

**现象**: `F3GameplayValidationStages.cs(100,17)` 与 `(122,21)` 两条
`error CS1631: 无法在 catch 子句体中生成值`。

**根因**: 我在 `RunIsolatedCase` 的两个 catch 体里写了
`yield return ForceReclaimArena();`。C# 禁止在 catch 子句体内 `yield return`
（`yield break` 允许，因为它不产生值）。

**修复内容**: catch 里只置 `needsReclaim = true` 并记账，把 `yield return ForceReclaimArena()`
移到 catch 块之外执行。语义不变（照样记 FAIL、照样强清、照样中止本用例）。

**为什么没提前发现——这是本条最该记的部分**:
`verify_syntax.bat` 当时报了 PASS，而它的过滤器 `CS1\d{3}` 本来就该匹配 CS1631。
实测查明真因：**本仓库缺 `Duckov_Data\Managed` 游戏程序集，几乎每个文件都有未解析类型
（CS0246）。Roslyn 一旦在绑定阶段解析不出类型/基类，就不会进入迭代器方法体分析，
于是 CS16xx 这类错误根本不会被产出。** 已用最小样例双向验证：
给类加一个不存在的基类，CS1631 就消失；去掉基类，CS1631 立刻出现。
把 CS1631 注回真实源码后 `verify_syntax.bat`（含 `--with-bcl`）仍报 PASS，
证明这是结构性盲区，不是配置问题。

我在修复前跑的那次「两种写法都合法」的探针结论是**错的**：当时用
`grep -oP "error CS\d+"` 解析 csc 输出，而 csc 在中文 Windows 下输出 GBK，
grep 静默匹配失败 → 空结果被我读成「无错误」。

**配套改动**:
- `tools/verify_syntax.py`：`--with-bcl` 改为**默认开启**（新增 `--no-bcl` 反向开关），
  并在文件头与 `RE_SYNTAX_ERROR` 处写明「CS16xx 需要 BCL 引用才走得到」。
  这能多抓一层 BCL 用法错误，但**抓不到本仓库的 CS16xx**（原因同上）。
- 新增 `tests/IteratorYieldInCatchGuard.py`：静态文本守卫，扫全仓 `.cs`，
  禁止 catch 子句体内出现 `yield return`。刻意放过两个合法形态：
  `catch { ...; yield break; }`（仓库里多处在用）与 `try { yield return ...; } finally { }`。
  已做正反验证：干净仓库 PASS，注入 CS1631 后精确报出行号。
  **这条规则只能靠静态守卫兜住，因为语法探针结构上做不到。**

#### 2. `ModeHCardData` 三个字段从未赋值（CS0649，且是真实内容缺口）

**现象**: `ModeHUIPages.cs` 报 `Body` / `GameQuality` / `IsAnomaly` 从未赋值。

**根因**: 唯一的卡片生产者 `BuildProfileCard` 只填了 Title/Subtitle/ActionLabel/OnClick。
但渲染器 `ModeHUIPages` 会读 `Body` 画正文、读 `IsAnomaly`/`GameQuality` 定描边色——
**玩家看到的选秀卡是空白卡身**：只有名字和原型，看不到怪癖/异常、招牌口令和试棚传闻，
等于让人盲选。这不是「多余字段」，是接线漏了一半。

**修复内容**: `BuildProfileCard` 补 `IsAnomaly`（`anomalyId` 非空即真）与 `Body`
（新增 `BuildProfileCardBody`：按「异常优先于普通怪癖」——两者按 DTO 注释互斥——
拼「怪癖/异常 + 招牌口令 + 试棚传闻」三行，缺字段整行不出，不留半截标签）。
本地化 key 已全部存在（`Anomaly_blood/crowd/strong/error`、`Quirk_*`、`Command_*`），
`rumorKey` 在内容 JSON 里已是完整 key（`BossRush_ModeH_Rumor_*`）故不再补前缀。
`GameQuality` **刻意保持不赋值**：选手不是装备、没有品质等级，留 0 时渲染器走 accent 色，
正是想要的表现。

#### 3. `BossBgmCoordinator` 四个 DTO 的 CS0649（误报，定点抑制）

**根因**: `BossBgmTrackEntry` / `BossBgmStingerEntry` / `BossBgmJukeboxEntry` /
`BossBgmTrackTable` 是 `JsonUtility.FromJson` 的反序列化目标，字段由反射赋值，
代码里只读不写。按提示改成属性或加初始值反而会让 JsonUtility 绑不上（它只认公有字段）。

**修复内容**: 在四个 DTO 外围加 `#pragma warning disable/restore 0649`，
范围只覆盖这四个类（已核对 pragma 落在第 38/95 行，`BossBgmKeys` 等其余类型不受影响），
并写明为什么不能按提示改。

**兼容性影响**: 无 schema / 存档 key / TypeID / 配置 key 改动。Mode H 卡面补全只影响
选秀页显示文本，不改任何状态机与结算。

**验证方法**:
1. 语法: `verify_syntax.bat` PASS；另用带 BCL 引用的 csc 全量跑，
   逐文件核对我改过的 12 个文件只剩 CS0246（缺游戏程序集），**无 CS1631**。
2. Guard: 全量 PASS，含新增 `IteratorYieldInCatchGuard`（已做注入式反向验证）。
3. **仍需 owner 在装有《鸭科夫》的 Windows 上跑 `compile_official.bat` 确认真编译通过**——
   本轮恰好证明了本机探针存在盲区，不能替代真编译。

---
### 2026-09-01 F3「完整玩法验收」三个 FAIL 的产品代码根因 + 一次性测完改造

**状态**: fixed（三个产品 bug）；一处 `needs-owner-decision`（命火是否补 Moveability）
**Finding**: 无（由 2026-09-01 实机验收报告
`BossRushTestReports/BossRushValidation_20260901_012335_057.log` 反查得出）
**兼容分类**: COMPAT（全部三项；无 schema / TypeID / 存档 key 改动）
**版本/Commit**: 未提交
**Owner decision**: 需要一处，见文末

**现象**: 最近一次实机跑出 `SUMMARY | CANCELLED | pass=31 fail=3`，且在 Mode H 处整套中断，
第 4/5 阶段（征程终章、最终清场回读）一个都没跑到。三个红项：
`MODE_D_LIFECYCLE`、`MODE_F_BLOODFIRE`、`MODE_H_FIRST_CERTIFICATION`。
经核查三个都是**产品代码真 bug**，不是验收器误报。

#### 1. Mode D 敌人被官方距离休眠系统每帧关掉（真实玩法同样中招）

**根因**: `EnemySpawnCore` 用 `CreateCharacterAsync(pos, dir, relatedScene, ...)` 生成敌人，
`relatedScene` 是当前场景 buildIndex。官方 `CharacterMainControl.SetRelatedScene`
在 preset 的 `setActiveByPlayerDistance`（默认 **true**）下把角色注册进
`Duckov.Utilities.SetActiveByPlayerDistance`，该组件 `FixedUpdate` 每帧无条件执行
`SetActive(距玩家 < 100m)`。

报告证据是三只狼 `health_alive=True, team=wolf, hostile=True` 但 `active=False`——
存活、敌对、就是被关掉了。实测距离：玩家在竞技场中心时 23 个刷怪点全在 100m 内（13.6~29.4m），
而验收流程直接 `SceneLoader.LoadScene` 进图、没走 `BossRushEntryFlow` 的玩家传送，
玩家停在原版落点，到刷怪点 221.7~254.3m，**0/23 在 100m 内**。

**这在真实玩法里也是 bug，只是被掩盖了**：玩家一旦跑远，已刷出的怪会被静默关掉，
`Health.IsDead` 仍为 false、仍在存活列表里 → 波次永远不结算 → 玩家卡波次。
Mode E 早就单独处理过（`ModeERespawnItems.TryForceActivateModeEEnemy` 里显式 `Unregister`），
Mode D / 标准波次 / Mode F / 三个自定义 Boss 都没有这层保护——这也解释了同一份报告里
`MODE_E_LIFECYCLE` PASS（9 只）而 Mode D 是 0。

**修复内容**:
- 新增 `Utilities/SpawnedEnemyActivationHelper.cs`（已进 `compile_official.bat`）：
  `ReleaseFromPlayerDistanceSleep` = `Unregister` + `SetSleeping(false)` + 必要时 `SetActive(true)`，
  逐项 try/catch。把 Mode E 已验证的逻辑提到共享层（AGENTS.md 4.9：跨模块基础设施放 `Utilities/`）。
- 接线 5 处激活点：`EnemySpawnCore` 同步路径与分帧路径各一处（Mode D/E/F/标准/丧尸共用），
  以及三个自管激活的托管 Boss（`DragonDescendantBoss` / `DragonKingBoss` / `PhantomWitchBoss`）。
- **不动** Mode G 的 `HoldForExternalCommit` 冻结分支。原版 spawner 生成的角色也不动——
  那里的休眠优化该保留，否则远处整图 AI 都会常驻。

#### 2. Mode F 命火过载挂了一个不存在的 stat

**根因**: 角色身上**没有名为 `MoveSpeed` 的 stat**。官方移动只读三个 key
（`CharacterMainControl` 的 `walkSpeedHash` / `runSpeedHash` / `moveabilityHash`）：
`CharacterWalkSpeed = GetFloatStatValue(walkSpeedHash) * CharacterMoveability * 武器倍率`。
全官方源码里 `"MoveSpeed"` 只作为 `Animator.StringToHash` 参数名出现。

于是 `AddModeFBloodfireOverloadModifier(player, "MoveSpeed", ...)` 每次都走
`RuntimeStatModifierTracker.TryAdd` 的 `stat == null → return false`，只留一条 DevLog。
**玩家实际拿到的移速加成（Walk/Run +15%）一直是生效的**，坏的是那条无效 modifier
和依赖它的验收断言（`speedModifierCount == 3` 恒不成立，且 metrics 为空——
因为 `move == null` 在写 metrics 之前就 return 了）。

**修复内容**:
- `ModeF/ModeFBloodfire.cs`：删掉 `"MoveSpeed"` 那一行，只保留 Walk/Run 两条。
  **玩家可感知数值零变化**（删的是从未生效的东西）。
- `DebugValidateModeFBloodfire`：期望值 3 → 2，去掉 move stat 的取值与断言，
  缺 stat 时也写明确的 `missing_stat=` metrics 而不是空串。
- `tests/ModeFBloodfireOverloadGuard.py` 硬编码断言了那行 buggy 代码，已同步为
  Walk/Run 两条，并加一条回归护栏禁止 `"MoveSpeed"` 重新出现
  （AGENTS.md 4.10：改被 guard 断言的结构必须同步 guard，不许放宽掩盖）。

#### 3. Mode H 认证的嵌套协程从未被推进（P0：正式入场同一路径）

**根因**: `ModeHRuntimeModule_SceneFlow.DriveCertification` 手工 `MoveNext()` 驱动
`ModeHProductionCertification.Run`，但循环体写死 `yield return null`，把 `inner.Current` 丢了。
而 `Run` 内部是 `yield return CertifyKey(...)`——yield 出**子 IEnumerator**，
指望调用方（Unity 协程调度器）递归驱动。

结果子协程被创建但一次都没 MoveNext：逐 key 的生成、阵营核对、受控击杀、
`RecordPassed/RecordRejected` 全没执行 → `keyResult.FailureReasonId` 恒 null
→ 日志里 12 条**空原因**的「认证拒绝」→ `_records` 恒空 → `passedStableKeys.Count = 0`
→ 撞 `MinProductionCandidateCount = 8` 门槛失败 → **Mode H 完全无法开局**。
`StartCertification` 是唯一认证入口，所以这不是验收专属路径。

同仓库正确写法就在隔壁：`ModeHRuntimeModule_MatchFlow.DriveMatchSpawning`
的 `while (inner.MoveNext()) yield return inner.Current;`。

**修复内容**:
- `ModeH/ModeHRuntimeModule_SceneFlow.cs`：`yield return null;` → `yield return inner.Current;`。
  `Current` 为 null 时语义等价于等一帧，所以每帧 owner/generation 校验与诊断页刷新节奏不变。
- 新增 `tests/ModeHCertificationCoroutineDriveGuard.py`（已做正反验证：注入回归确实变红）。

#### 4. 验收器改造：按用例隔离，一次跑完

- `SuiteTimeoutSeconds` 1800 → 2700（覆盖面扩充后 30 分钟不够）。
- 新增 `RunIsolatedCase`：协程异常只记本用例 FAIL，强清后继续下一个。
  壳内**必须** `yield return inner.Current` 透传——与上面 Mode H 同款坑。
- 清场失败降级：先 `ForceReclaimArena` 强清复检，只有**连续两次**不可恢复才 `_fatalAbort`。
- 超时与取消分离：`TIMEOUT`（剩余记 SKIP，仍走完最终清场回读）vs `CANCELLED`（人喊停）
  vs `ABORTED_DIRTY`。`SUMMARY` 增加 `failed_ids=` / `skipped_ids=`。
- 阶段 5 → 7；新增 `ValidationForceClearArenaEnemies`（复用 F10 的 `ForceKillAllEnemies`）。

**新增文件**（全部已进 `compile_official.bat`）:
`DebugAndTools/F3GameplayValidation{Stages,Modes,BackMountain,Economy,Depth,Leaks}.cs`。
主 runner 从 1182 行降到 1096（`LargeFileBudgetGuard` 新文件上限 1200，原本已顶格）。

**新增用例**: 后山种子↔产出双向完备性/展示柜登记回落/出击餐覆盖语义/设施解锁门一致性、
词缀拒绝路径零消耗/锁槽保全、图鉴编解码往返/清缓存回读、Mode D 多波连打、跨套件泄漏差值。
**Mode E 撤离、Mode F 赏金与撤离、标准胜利奖励**在产品代码里全是 private 且无 Debug 入口，
如实记 `SKIP` + 「需人工」，不伪造 PASS，也不为凑绿加测试后门。

**兼容性影响**: 无 schema / 存档 key / TypeID / 配置 key 改动。Harmony 目标与反射绑定未动。
行为变化两处，都是修复：① 已刷出的敌人不再因玩家跑远被关掉（竞技场内玩家始终在 100m 内，
逐字无变化）；② Mode H 认证会**第一次真正执行**。

**验证方法**:
1. 语法: `verify_syntax.bat` PASS（750 源文件，CS1xxx 零错误；新增文件的错误码分布
   与既有文件一致，只有缺游戏程序集的 CS0246/CS0518）。
2. Guard: `python tools/run_guards.py` → PASS=509 NEW-FAIL=0 KNOWN-RED=0。
   `ModeHCertificationCoroutineDriveGuard` 已做注入式反向验证。
3. repowiki: 已同步 `zh/content/工具与调试/调试工具.md`。

**未验证/需人工**:
- **没有真编译**：本机是 WSL，无 `Duckov_Data\Managed`。必须在装有《鸭科夫》的 Windows 上
  `set BOSSRUSH_DEV_BUILD=1 && compile_official.bat`（AGENTS.md 4.2）。
- **Mode H 认证的真实结果无法静态预判**：修好之后逐 key 认证第一次真跑，
  门槛是 8 个 key 通过 + 5 种原型覆盖，可能出现真实 reject。per-case 隔离保证它不再拖垮整套。
- 距离休眠修复对「玩家跑远后波次仍能结算」的效果需实机复测（这才是它真正要修的玩法症状）。

**Owner decision 待拍板**: 命火过载原本想给的是「移速 +15%」，实际生效的是 Walk/Run 各 +15%。
官方还有一个 `Moveability`（Walk/Run 的公共乘数）。要不要补挂 `Moveability +15%`？
本轮按**保守方案**处理（只删无效行、不新增），玩家数值零变化。补挂等于给命火实际加强，
属于数值决策，按 AGENTS.md 第 7 条不擅自定案。

---
### 2026-09-01 新增玩法可达性修复：词缀锻造与鸭皇图鉴入口从未接线 + Mode H 真实押品接线

**状态**: fixed
**Finding**: 无（本轮全面审核新发现）
**兼容分类**: COMPAT + SCHEMA+ + OPERATIONAL
**版本/Commit**: 未提交
**Owner decision**: 需要；owner 明确要求「概率与数值自行按项目既有口径决定，Mode H 直接接线，其余问题全部修复」

**现象**:
1. 词缀锻造（10 个源文件、13 条词缀）玩家**完全无法进入**：哥布林身上没有「词缀锻造」选项。
2. 鸭皇图鉴（13 个源文件）玩家**完全无法进入**：基地商人不卖图鉴书，图鉴面板无从打开。
3. 词缀熔石在游戏里**不存在任何产出途径**，而游戏内 Wiki 已承诺三条获取途径。
4. Mode H 看盘页无条件显示「本模式允许你押上真实仓库物品，失败会永久没收」，
   紧挨着却是「当前存档槽无法证明资产安全，押品已禁用」——功能不存在却挂着恐吓文案，
   且第二句把「未实装」表述成「你的存档有问题」。
5. 重启后玩家背包/仓库里的熔石与图鉴书会退化成官方 `FallbackItem`。

**根因**:
- 词缀锻造：`GoblinAffixForgeInteractable` 文件头写明"组件创建由
  `EnsureGroupedInteractionOptions` 统一负责"，但那个方法挂了 6 个子交互、**独缺它**；
  而 `ReforgeUIManager.OpenAffixForgeUI` 的唯一调用点就在这个从未被挂载的组件里。
- 鸭皇图鉴：`TryInjectCodexBookIntoShop` / `InjectCodexBookIntoShops` **零调用点**。
  8/31 的 `5842911`「修复鸭皇图鉴商店与库存持久化」修对了定价（旧实现
  `priceFactor = 1/rawValue` 把 4000 金压成 1 金）与库存持久化，但没发现注入本身从未执行。
- 熔石：只有 `ConsumeItem` / `GetItemCountInInventory` 三处消耗端，无掉落表、无商店条目。
- Mode H：`ModeHWarehouseStakeJournal.LoadPersisted` 零调用点 → `_slotConsistent` 恒 false
  → 整套抵押事务 API（14 个方法）与 `ModeHRewardTransaction` 全部无人调用。
  存档 key `BossRush_ModeH_StakeJournal_v1` 只被风险扫描**读**过，从来没有人写。
- 动态注册：两个 TypeID 的 `EnsureRuntimeFallbackRegistrationShell` 早已写好，
  但一直没登记进 `BossRushDynamicItemRegistry` 的 plans 表（AGENTS 契约第 6 节）。
- **为什么 508 个 guard 一个都没抓到**：guard 断言的是结构不变式（文件存在、清单登记、
  订阅成对、层级常量），不验证「调用链能否从玩家操作走到功能」；编译也不报错，
  因为未被调用的 `internal` 方法完全合法。

**修复内容**:
- 新增文件（均已加入 `compile_official.bat`）:
  - `Integration/AffixForge/AffixForgeStoneDropService.cs` — 熔石 Boss 掉落轨，
    形态逐字照 `PetNestDropService`（同一挂接点、同一三段式、同一 dormant 二道防线）
  - `ModeH/ModeHStakeJournalPersistence.cs` — journal 独立 key 读写，照 `ModeHProfilePersistence`
  - `ModeH/ModeHRealStakeService.cs` — 押品选择与结算编排
- 修改文件:
  - `Integration/Reforge/GoblinReforgeInteractable.cs` — 挂上 `AffixForgeOption` 子交互
  - `Integration/BossRushIntegration.cs` + `Integration/IntegrationDeferredBootstrap.cs` — 图鉴上架两条路径
  - `Integration/Affinity/NPCs/GoblinAffinityConfig.cs` — 熔石进哥布林商店（好感 2 级、库存 5）
  - `Integration/AffixForge/AffixDefinitions.cs` — 掉落率 8%、单次 1 颗
  - `Integration/BossRushDynamicItemRegistry.cs` — 补登记 500060 / 500061
  - `LootAndRewards/LootAndRewards.cs` + `Integration/AffixForge/AffixForgeHostCleanup.cs` — 掉落轨挂接与退订
  - `ModeH/ModeHWarehouseStakeJournal.cs` — 阶段推进内联落盘 + 失败整体回滚 + 终态重算一致性
  - `ModeH/ModeHProfilePersistence.cs` — `OnSetFile` / `OnSaveDeleted` 装载/清空 journal
  - `ModeH/ModeHSaveFlushCoordinator.cs` — 纳管 journal 的订阅与同批落盘
  - `ModeH/ModeHInventoryPersistenceBridge.cs` — 下沉三个只读查询原语（守住 Isolation 白名单）
  - `ModeH/ModeHRuntimeModule_{CombatFlow,MatchFlow,SceneFlow}.cs` — 锁盘/结算/中止三处接线 + 选择器 UI
  - `ModeH/ModeHConfig.cs` — `MaxRealStakeItemsPerMatch = 3`
  - `Localization/ModeHLocalization.cs` — 分因禁用文案 + 预览文案
  - `PetNest/PetNestSaveCoordinator.cs` — 补上另外三个协调器都有的 `IsBaseLevelSafe` 场景闸
  - `Patches/Compatibility/MagicBlendInitializationOrderPatch.cs` — 热路径 instanceID 白名单短路 + `ResetStaticCaches`
  - `Utilities/AlwaysOnRuntimeHooks.cs` — 接上该补丁的缓存清理
  - `compile_official.bat` — 注释里的全角标点改 ASCII（见下）
  - Wiki / repowiki 同步（4.13）

**数值决策**（owner 授权自行决定，全部对齐项目既有口径）:
- 熔石 Boss 掉落 8%：遗种蛋是 0.04（能开出随从的终局奖励），熔石只是消耗材料且一次重随机
  只花 1 颗，故放宽一倍。标准竞技场一局十几只 Boss 期望掉 1 颗。
- 熔石商店：好感度 2 级（与 `SHOP_UNLOCK_LEVEL`、钻石同级）、库存 5（与钻石/冷淬液同档）。
- Mode H 单场押品上限 3 件：配合 x5 赔率上限最多赢回 15 件同品质，又不至于一场清空半个仓库。

**兼容性影响**:
- 存档 `SCHEMA+`：`BossRush_ModeH_StakeJournal_v1` 从「只读不写」变为真正写入。
  该 key 必须同时可反序列化为 header（风险扫描用）与完整 DTO，header 是完整 DTO 的字段子集，
  **今后增删 journal 字段不得改动 header 的 7 个字段名**。老档无此 key 时按"从未押过"处理。
- TypeID：无新增、无复用（仍是 500060 / 500061），仅补登记进动态注册表。
- 玩家可见变化：哥布林多一个「词缀锻造」选项；基地商人多卖图鉴书（4000 金）；
  Boss 会掉熔石；Mode H 看盘页出现可用的押品选择器。均为加法。
- `OPERATIONAL`：`compile_official.bat` 注释里的全角标点会让 `chcp 65001` 下的 cmd
  把注释尾段当命令执行（实测每次构建刷 5 行 `is not recognized`）。已改 ASCII 标点，
  中文正文保留。这是既有问题，非本轮引入。

**验证方法**:
1. 编译: `cmd.exe /c "cd /d D:\code\ykf\BossRushMod && compile_official.bat"` →
   **Build succeeded**，0 error，`Build\BossRush.dll` 4.09 MB 已部署；
   构建输出中此前每次都出现的 5 行 `is not recognized` 噪声已消失。
2. Guard: `python3 tools/run_guards.py` → **508/508 PASS**（0 NEW-FAIL、0 KNOWN-RED）。
   过程中 `ModeHIsolationGuard` 曾拦下一次真实违规：`ModeHRealStakeService` 直接引用了
   `Inventory`，而白名单只许 journal 与 bridge 碰玩家资产符号。**未放宽 guard**，
   改为把只读查询下沉进 `ModeHInventoryPersistenceBridge`。
3. 人工 smoke: 未做（见下）。

**未验证/需人工**:
- 全部运行时行为。WSL 只能编译与跑 Python guard（AGENTS 4.2）。
- Mode H 押品三条路径必须实机走一遍：escrow 重建、**满仓返还**（`return_no_empty_slot`
  分支）、`ManualIntervention` 出口。这三条静态审计与 guard 都覆盖不到，
  且涉及玩家真实物品，**上线前务必先在备份存档上验证**。
- 哥布林商店刷新是否真的出现熔石（需好感度 2 级）、图鉴书是否出现在基地商人货架。
- 建议先出 dev 包再验：`set BOSSRUSH_DEV_BUILD=1 && compile_official.bat`，
  否则 `DevLog` 被 `[Conditional("BOSSRUSH_DEV")]` 整个剥离，日志里什么都看不到。

**失败尝试**:
- 起初把押品事务 ID 派生在 `ModeHSeedStream.Domains.Reward` 上，与
  `ModeHRewardTransaction` 抢同一条流的序列位置，会破坏「重启后重放同一场得到同一组 ID」
  的幂等依赖；改用真实资产路径本来就预留的 `Domains.PlannedLoss`。
- `slotId` 起初误传 `ModeHRuntimeGates.SlotGeneration`（那是 generation 不是槽号），
  改为 `SavesSystem.CurrentSlot`，读不到时传 -1 而不是静默改写成 0 冒充 0 号槽。
- 排查 bat 噪声时先怀疑 CRLF、字节偏移、`::` vs `REM`、全角括号，逐一实测否证；
  最终二分定位到全角标点在 `chcp 65001` 下触发 cmd 的 token 切分。

---
### 2026-08-29 四系统复审全面修复：Mode H 四个 P0 状态机断裂 + G/日报/遗种巢各自的 P1

**状态**: fixed（本轮全部 confirmed finding）；模式H 战斗驱动与真实押注仍为计划内未接线
**Finding**: CR-2026-08-29-008 ~ CR-2026-08-29-021（见 `CODE_REVIEW_FINDINGS.md`）
**兼容分类**: COMPAT（全部；无 SCHEMA 改动——四个系统均未新增/改名存档 key、配置 key，
DTO 与编解码零改动）
**版本/Commit**: 本条目所在 commit
**Owner decision**: 三处待拍板，见文末「需 owner 确认」

**背景**: 上午四包修复提交后做了第二轮四代理全面复审。三个系统（模式G/日报/遗种巢）的
上午修复全部复核成立，但各自查出新的 P1；模式H 批 2（a579c3e）宣称交付的
「赔率→锁盘→分帧生成→回滚回看盘」经核实是**纸面交付**：调用方都在，调用必然失败。

#### Mode H（4 个 P0 + 2 个 P1 + 6 项 P2，主会话直接修复）

**根因共性**：全部是「运行时调用 vs 冻结转换表 / 关停闩锁」的断裂。编译与当时 33 条
guard 全绿。上一次事故是「构件在、没人调」，这一次是「有人调、调不通」。

- **CR-008 锁盘按钮永远无效（P0）**：批 2 按错记的表写成 `LoadoutEditing -> LoadoutLocked`，
  而冻结表里锁盘只有 `OddsPreview -> LoadoutLocked`，且全仓无任何调用转入 `OddsPreview`。
  赔率页是 `ClaimModalInput` 时停模态页且无关闭按钮，玩家只能靠游戏 ESC 菜单逃生。
  修复：主干改为 `MatchBrief -> LoadoutEditing -> OddsPreview -> LoadoutLocked -> MatchSpawning`，
  开页即推进 `OddsPreview`（同一页既是配装也是看赔率），锁盘前再幂等补一跳兜底。
  **不改冻结表**（`ModeHRuntimeModule_MatchFlow.cs` 的 `EnterLoadoutEditing` /
  新增 `EnsureOddsPreview` / `LockLoadoutAndStartMatch`）。
- **CR-009 回滚退看盘转换非法（P0）**：`MatchSpawning` 无直达 `MatchBrief` 的边。
  改为经 `Recovering` 过渡，由恢复驱动落回同场；这一支**刻意**用不消耗预算的
  `RequestRecovering`——功能没写完不是技术故障。
- **CR-010 `Recovering` 是死态（P0）**：所有技术故障出口都转进它，却无任何调用点以它为源
  转出，`MaxAutomaticTechnicalRetriesPerMatch=2` 永远走不到第二次。新增
  `DriveRecovery`（`OnUpdateInternal` 内按 stateSequence 判重驱动）+
  `ResolveRecoveryResumeLifecycle`（战前战中一律回落同场 MatchBrief，早期/幕间回落自己）+
  `RequestTechnicalRetry`（消耗预算 → 未超走 Recovering，超则挂起），
  各故障点统一改用后者。**两条设计约束（都踩过）**：① 恢复驱动必须异步（下一帧），
  因为 `EnsureMatchPlan` 是在页面组装里报故障的，同步回落会无限递归；
  ② 页面组装路径上的故障必须消耗预算，否则「回落→重建页面→再失败」变成每帧死循环。
- **CR-011 关停闩锁永不复位（P0）**：`_commandsClosed` / `_shutdownCompleted` 只在
  `OnAwake`（每进程一次）复位，一次会话只能玩一局；再次入场船票已预扣、模块却静默早返，
  玩家站在无人接管的原图上。新增 `BeginNewRunSession()`，在**意图匹配成功之后**复位
  （放在匹配之后才不会因「路过一张受支持地图」就解除关停），顺带清残留 `_pendingContractMainId`。
- **CR-012 恢复壳不可达（P1）**：船坞入口在 recovery-only 时直接拒绝；且存档恢复只在
  `OnAwake` 跑一次，从主菜单载入中断赛季时 recovery-only 闸立不起来，玩家可以开新赛季
  **静默覆盖**它。修复：`ModeHInteractable` 新增 `TryOpenRecoveryShellForEntry`
  （有待恢复记录时打开恢复壳，但有其它模式在跑时不接管）；
  `ModeHProfilePersistence` 的 SetFile/SaveDeleted 回调补
  `ModeHRuntimeModule.NotifySlotRestored()` 重建内存 run owner。
- **CR-013 中止链全程无文案（P1）**：`AbortSetup` 现给一句归类文案 + 退票告知
  （新增 7 个 `Abort_*` 中英 key，`ResolveAbortMessageKey` 归类内部 reasonId）；
  入口被拒有 `ShowUnavailableNotice`；`GetReasonLocalizationKey` 改为**白名单式 fail-safe**——
  未注入的内部原因（如 `entry_exception:NullReference`）回落通用文案，
  不再可能给玩家看到 `*星号原文*`。
- **CR-018 六项 P2**：看盘页 `Label_Match` 改 `Replace("{0}", …)`（此前显示「第 {0} 场 1 / 6」）；
  `ResetModeHStaticCaches` 补 `ModeHSaveFlushCoordinator.ShutdownSubscription()` 退订（4.6）；
  `EnsureMatchPlan` 缓存判据补 `technicalRetrySequence`（此前技术重试会复用刚失败的同一计划）；
  `_pendingContractMainId` 残留随 CR-011 一并清；repowiki 补 a579c3e 起漏同步的两批内容。
- **自查追加修复（复核自己的改动时发现，不在原 finding 内）**：
  ① `Suspended -> Recovering` 会被 `ApplyTransition` 把故障源**覆盖成 Suspended**，
  真实故障源丢失，导致恢复目标解析不出来又弹回挂起——新增
  `DeriveResumeFromSeasonProgress()` 从赛季进度反推（已开赛→同场看盘 / 签完约→名单 / 否则选秀）；
  ② 玩家手动「同场重开」会被自动重试预算当场判定超预算弹回挂起，按钮等于没有——
  `ModeHRunState.ResetTechnicalRetry()` 由 `ResumeFromSuspended` 调用，
  手动重开拿回全新预算与新的计划候选。
- **`RouteUiForLifecycle` 离开恢复通道时收起恢复壳**（`HideRecoveryShell`），
  否则回落后的看盘页会被壳盖住。

**Guard 增补（`tests/ModeHReachabilityGuard.py`）**：
① 两端都写死的 `TryTransition` 调用点，其 (from, to) 必须在冻结表内；
② 恢复通道三态一旦被进入，就必须有以它为源的转换调用点。
只查恢复通道是因为普通玩法状态可由 `RequestRecovering` 这类**动态源**门面退出、
静态归属不了会误报，而恢复通道是所有故障路径的终点、出口只能写死源状态。
**反例验证**：把 CR-008 与 CR-010 各自改回原样 → guard 分别报「请求了非法转换
LoadoutEditing -> LoadoutLocked」与「Recovering 是死态」；还原后 PASS。
另：新增的宿主解析改走 `ModeHInteractable.ResolveHost`，保持
`ModBehaviourInstanceClassificationGuard` 的 ModeH 基线恒为 1（该文档的既定约定）。

#### Mode G（1 个 P1 + 4 项 P2，代理修复）

- **CR-014 无伤成就跨局残留（P1）**：`BeginModeGAchievementSession` 只清去重集，
  不重置 `HasTakenDamage`（唯一置 false 点 `ResetSessionStats` 的入口只被 ModeD 与
  标准竞技场调用，模式G 启动路径不经过）。同进程受过一次伤，之后所有模式G 局的
  `kill_dragon_king_flawless` / `kill_dragon_descendant_flawless` 永不解锁；
  「进程首局无伤」的 smoke 恰好测不出。修复：**只重置 `HasTakenDamage`**，
  不整体 `ResetSessionStats()`——逐字段核对后确认它还会重置 `ArenaEnterTime`（Legacy/ModeD 速通基准）、
  `HasUsedHealItem`（无间炼狱）、`HasPickedUpItem`（ModeD），模式G 一个都不消费，
  整体 reset 属纯跨模式副作用。
- **P2 自动入场确认页打不开时静默**：补路牌同款玩家提示，并修正 else 分支
  「确认页已取消」的错误日志（实际从未打开）。
- **P2 宿敌存档「战斗中不写盘」承诺未实现**：选择**兑现承诺**而非改注释。
  typed `Store` 保持立即（官方任一次存盘可顺带带走），只把物理 `SaveFile` 在战斗帧
  记欠账顺延；欠账三处结清：下一次非战斗批次 / `End()` 归零相位后补写 /
  `TryFlushOnHostDestroy` 强制落盘。未照搬日报的「只在基地落盘」——模式G 整局非基地，
  那等于整局不落盘。
- **P2 `OnDestroy` 用 `new` 隐藏基类 virtual**：改 `protected override` + `base.OnDestroy()`。
- **P2 AFK 提示与可达操作不符**：改文案指向「撤离返回基地后重新入场」，
  不新增场景交互物。

#### 鸭科夫日报（1 个 P1 + 1 个 P2 + 3 项 P3，代理修复）

- **CR-017 关开关期间换档跨档覆写（P1）**：关开关会退订 `OnSetFile`，此时换槽无人重置缓存，
  重开开关后槽 A 的日报 JSON 会被写进槽 B（签到墙/连签/悬赏 claimed 被顶掉，
  可致进度丢失或重复领悬赏现金）。修复：`LoadOrInit` 给缓存打**槽位烙印**，
  命中缓存时比对 `SavesSystem.CurrentSlot`，不一致即自失效重读，并并联复位
  `SaveCoordinator` 与 `Service` 的运行时状态（数据换了、计时也要换）。
  另补 `DiscardCacheOnResubscribe()`：核对官方源码后确认
  `SavesSystem.DeleteCurrentSave` 删的是当前槽且**不改 `CurrentSlot`**，
  槽位烙印挡不住「同槽删档重开」，而 dormant 期间没有任何回调可用，
  重新挂监听是唯一确定的重新对齐点。
- **P2 悬赏现金忽略 `EconomyManager.Add` 失败返回值**：官方 `Add` 在 `Instance == null` 时
  返回 false 且不抛异常，旧码仍置 `BountyRewardClaimed=true` 落盘，
  补发被闸死、现金永久丢失且报纸公示「已寄出」。修复：接住返回值，失败不置 claimed。
- **P3 瞬时失败被缓存成会话级空池**：空结果不再缓存（官方 `Search` 自带降品质兜底，
  合法空池几乎不可能，缓存里的空数组必然是故障残影）。
- **P3 F3 dump 悬赏题目打的是昨日已结算题**：改用今日在售题，与旁边的进度同源。
- **P3 跨天补发换奖品**：**零 schema 改动**解决——断签会把 `PeriodSignedCount` 清零，
  故 `1..PeriodSignedCount` 必然连续，第 slot 格的天号可由
  `LastSignedDayIndex - (PeriodSignedCount - slot)` 从既有字段精确推导。



#### 遗种巢（2 个 P1 + 1 个 P2 + 2 项防御性自愈 + 1 项语义对齐，代理修复）

- **CR-015 会话重启后血脉目录空窗（P1）**：`InitializeEnemyPresets` 的调用点全在进竞技场路径
  与调试面板，基地启动无一触发；重启后直接在基地孵官方血脉蛋必报 `lineage_unknown`
  （玩家会以为蛋坏了），巢页/账本/博物馆大面积裸 key 或数字错乱。
  修复：`WavesArena` 新增 `EnsureEnemyPresetsReadyForPetNest()`（池已填充零成本返回），
  由 `PetNestRuntimeModule` 在**回基地分支最前**调用，并补一次 `InitializeBossPoolFilter()`
  ——不补的话基地侧目录会用未过滤全池，把玩家禁用的 Boss 也算进血脉。
  **不放在 `EnsureBootstrapped`**：它首次生效在主菜单期，
  `Resources.FindObjectsOfTypeAll<CharacterRandomPreset>` 那时可能只扫到部分预设，
  而 `_enemyPresetsInitialized` 一旦以 `Count>0` 置位就永不重扫，会把残缺池冻死。
  4.12 门控落在调用侧（未开开关一次也走不到）。
- **CR-016 关开关不清掉落追踪（P1）**：`ClearAllTracking()` 全仓唯一调用链是宿主销毁，
  关开关后已追踪 Boss 死亡仍记遗魂/掉蛋，违反 dormant 契约。
  修复：`ShutdownIfEnabledTurnedOff()` 在 `if (!_bootstrapped) return;` **之前**调用
  （放早返之后等于没修）；handler 本体加开关检查作第二道防线（只在 Boss 死亡帧判一次）。
- **CR-020 `_hooks` 慢泄漏（P2）**：弃局/直接撤离/切图销毁的 Boss 条目永不移除。
  修复：场景回调清一次（比 `LootAndRewards` 的 stale 清扫覆盖更全——后者漏掉龙王手动订阅路径）；
  已核实全部追踪登记都发生在 `CreateCharacterAsync` 完成回调里，晚于场景回调，不会误清本局。
- **防御性自愈：OnExpedition 孤儿锁**：逐 key 独立 flush 失败可落盘出「崽=OnExpedition」
  而远征记录缺失，该崽永久锁死且全仓无 reconcile。新增 `ReconcileOrphanedExpeditionLocks()`，
  三条误修防线：`CanStoreAll` 为假时一步不走（读不回来 ≠ 记录不存在）、
  匹配放宽到「id 命中」或「同崽未结算记录」、只有 `state==OnExpedition` 才复位状态。
- **防御性自愈：跨存档槽缓存渗漏**（与日报同构）：`PetNestKeyStore<T>` 给缓存打槽位戳，
  `LoadOrInit` / `Store` / `FlushPending` 三处校验。比日报多校验 `FlushPending` 一处，
  因为官方 `OnCollectSaveData` 可能在换档之后、任何 `LoadOrInit` 之前先触发 flush。
  **同槽删档**（`DeleteCurrentSave` 不改 `CurrentSlot`，槽位戳挡不住）在遗种巢侧
  由既有的 `ResetCachesForSlotReload()`（关开关时即清缓存）覆盖，
  日报侧则由新增的 `DiscardCacheOnResubscribe()` 覆盖——两系统机制不同但覆盖等价，
  **无需再互相镜像**（本条为主会话核对结论）。
- **远征奖励失败语义与注释不符**：注释宣称可补发，实际异常被内部吞掉后无条件置
  `rewardsGranted = true`。选**按条目记账**（首选方案，零重复发放风险）：
  记录新增 `cashGranted` / `grantedLootUnits` / `rewardGrantAttempts` 三个可选字段
  （`SCHEMA+`，`CurrentSchemaVersion` 保持 1——老档缺键回落默认值语义正确，
  bump 反而会让老档整体进写屏障）；现金接住 `EconomyManager.Add` 返回值后才记账、
  战利品按件数游标续投。**必须配尝试上限**（`MaxRewardGrantAttempts = 6`）：
  `MarkRevealed` 在 `rewardsGranted=false` 时拒绝翻牌，无上限会让一件永远发不出去的战利品
  把翻牌永久卡住——那等于用一个新的永久锁换掉一次奖励丢失。

#### 跨系统：模式H 清场会连玩家一起销毁（主会话核对 UNVERIFIED 线索时确认并修复）

`ModeHArenaIsolationLease.ClearNativeEnemies` 的守卫写成
`if (player != null && ReferenceEquals(character, player)) continue;`，
`player == null` 时守卫被整条跳过——**玩家自己也会被 Destroy**，而注释却写着「不会误伤」。
改为 fail-closed：认不出玩家就不清场，返回 `isolation_player_unresolved` 让开局走
AbortSetup 退款离场。隔离失败可以重来，销毁玩家角色不可逆。
（同线索里的「出战崽被无差别清场」经核实**不成立**：`PetNestModeGate` 已用
`mode_h_banned` 禁止模式H 局内放崽。）

**兼容性影响**:
- 存档：仅遗种巢远征记录新增三个可选字段（`SCHEMA+`），老档缺键回落默认值且不触发写屏障；
  其余三个系统零 schema 改动。四个系统的存档 key、配置 key 均未新增或改名。
- 本地化：模式H 新增 7 个 `Abort_*` 中英 key（已按 4.4 注入并有消费点）；
  模式G 沿用 `L10n.T(中文, English)` 内联双语，无新 key。
- Harmony/反射：无改动。
- 运行时总回退：各系统开关关闭即可回到 dormant，行为与修复前一致。

**验证方法**:
1. 编译: Windows `compile_official.bat` 通过并部署（合并四个系统改动后重编一次，`Build succeeded`）。
2. Guard: `python tools/run_guards.py` 全量 494 项 → PASS=493 / NEW-FAIL=0 / KNOWN-RED=1。
   同步并**反例验证**的 guard：`ModeHReachabilityGuard`（新增 2 条断言）、
   `ModeGAchievementIsolationGuard`、`ModeGPersistenceFlushCoordinatorGuard`、
   `DailyReportPersistenceGuard`、`PetNestRuntimeModuleGuard`、`PetNestDropLifecycleGuard`、
   `PetNestPersistenceGuard`、`PetNestExpeditionSettlementGuard`。
   四个系统合计 20+ 项反例验证（改坏→确认红→还原→确认绿），未放宽任何既有断言语义
   （仅两处纯字符数上界因函数体合法变长而放宽）。
3. 人工 smoke: **全部待实机**，见下。

**未验证/需人工**（按系统）:
- 模式H：船坞入口 → 认证 → 选秀 → 看盘 → **赔率页点锁盘应能推进到生成段**（这是本轮核心）；
  生成校验后应回到同一场看盘且恢复壳不残留；同一会话**连续开两局**不再吞船票；
  制造一次计划/生成失败，确认自动回落同场、超预算后挂起、恢复壳「同场重开」按钮真的能重开；
  中断赛季后重启游戏，船坞入口应给恢复壳且旧赛季不被覆盖；各失败出口都应看到中文提示。
- 模式G：先打一局受伤的任意模式，再打无伤模式G 击杀龙王/龙裔，确认 flawless 解锁；
  波 3/6 宿敌死亡帧不应再有落盘卡顿，且整局结束/切图/Alt+F4 后宿敌记录完整。
- 日报：槽 A 签到 → 关开关 → 载入槽 B → 开开关，确认看到 B 的进度且存盘后未被覆写；
  同槽删档重开应从第 1 天干净开始；同槽连续游玩不应刷槽位漂移 WARNING。
- 遗种巢：重启后直接在基地孵官方蛋应成功、UI 无裸 key；**基地首帧预热的卡顿幅度需实测**
  （首次回基地会同步跑一次全内存预设扫描，若不可接受可改为延迟一帧，结构不变）；
  战斗中关开关后击杀已追踪 Boss 应无进账。

**需 owner 确认（3 项）**:
1. **模式G 落盘推迟的丢数据窗口**：typed 数据仍立即入内存存档（官方任一次存盘可带走），
   但战斗中途进程被强杀时，波 3 宿敌记账最长可能损失到本局结束（旧行为是死亡后约 1 帧落盘）。
   这是「消除战斗帧卡顿」换来的确定性代价，判断可接受（宿敌记录只在下一局入口消费），但属产品取舍。
2. **遗种巢 `MaxRewardGrantAttempts = 6`** 是草案值，与 `PetNestTuning` 全表同属待审定数值。
3. **遗种巢基地侧预热时机**（场景加载帧内同步 vs 延迟一帧），取决于上面的卡顿实测结果。

**未修（有意，已登记）**:
- 模式H 战斗驱动与真实仓库押注仍为计划内未接线（见下一条目），本轮只保证未接线边界干净。
- 模式H 的 P2「赔率页没有赔率」（`ModeHOddsController` 零调用）：属战斗/押注接线的一部分，
  与下一批一起做更合理。
- 日报 P3「未读提示不持久」：需新增可选持久字段才能正确实现，
  不为一个数据无损的 P3 动 schema，留 owner 拍板。
- 遗种巢 P3 一组（闲逛崽实为跟随玩家、主面板 modal lease 极端时序、天赋/战痕裸英文 key）
  与模式G/H 的其余 UNVERIFIED 线索：见 `CODE_REVIEW_FINDINGS.md` 的 UNVERIFIED 区。

---
### 2026-08-29 Mode H 编排层接线：入口、场景开局、状态投影与可达性守卫（四系统审查修复 包4 第一批）

**状态**: fixed（编排骨架与入口）；真实押注接线仍为 accepted，见「未完成部分」
**Finding**: CR-2026-08-29-007（模式整体不可达）
**兼容分类**: SAFE（补齐从未生效的接线）+ SCHEMA+（一张地图新增五个可选点位字段）
+ COMPAT（旧模式拒绝文案由一句拆成两句，新增两个本地化 key）
**版本/Commit**: 本条目所在 commit
**Owner decision**: 需要；已拍板「模式H 全量完成含真实押注」。本批交付编排骨架，
真实押注按计划的资产安全顺序留到虚拟注实机通过之后。

**现象**: 玩家订阅 Mod 并打开「百战留痕」开关后，游戏内**找不到任何入口**；
即使人为构造入场意图进图，也只会停在空场景——不认证、不选秀、不生成、无 UI。
约 25/55 个 ModeH 文件是不可达代码。而 29 条 ModeH guard 全绿、编译通过、
`docs/contracts.md` §6.1 写着「已一次性完整实现」。

**根因**（三处独立缺件，任何一处都足以让模式不可达）:
1. `ModeHInteractable.TryOpenEntry` 零调用方，也没有任何代码把它 AddComponent 到场景，
   玩家没有触达路径。
2. `ModeHRuntimeModule.cs:353-362` 四个 `partial void`（OnSceneLoadedInternal /
   OnUpdateInternal / OnTransitionApplied / ShutdownRuntimeInternal）**只有声明没有实现体**。
   C# 对无实现的 partial 方法会连同**全部调用点**一起静默删除：编译不报错、guard 不报错、
   运行时什么都不发生。整个编排层就消失在这里。
3. 九张 `Assets/SpawnPoints/*.json` 均无 `modeH*` 五点位，`SupportedMapCount` 恒为 0，
   入口即便接通也会被 `modeh_map_unsupported` 拒绝。
根因之上还有一层：29 条 guard 全部只断言「构件内部长什么样」，没有一条断言「构件有人调用」。

**修复内容**:
- 新增文件（均已登记 `compile_official.bat`）:
  - `ModeH/ModeHRuntimeModule_SceneFlow.cs` —— 运行时字段唯一声明处 +
    `OnSceneLoadedInternal`（意图匹配 → 双租约按 §19.2 顺序取得 → 认证协程 →
    固定 runSeed 原子创建 Drafting Season 并读回）+ `ShutdownRuntimeInternal`（§18.3 十步顺序，
    释放严格逆序）+ 统一失败出口 `AbortSetup`（退款、逆序释放；
    arena 租约清过原生敌人时**强制离场**，绝不原地回落 Legacy）。
  - `ModeH/ModeHRuntimeModule_UiFlow.cs` —— `OnTransitionApplied`（内存投影 + 页面路由 + 标脏）
    与恢复壳。**落盘策略裁决**：转换本身只标脏，真正落盘只发生在少数显式点
    （Drafting 创建 / RosterLocked / MatchBrief / MatchSettling / Intermission / TransferWindow /
    HallOfFame / SeasonEnded / Suspended），满足 §20.3「每批至多一次 SaveFile」与
    §17.8「MatchSettling 是唯一原子全量写入点」。
  - `ModeH/ModeHRuntimeModule_MatchFlow.cs` —— `OnUpdateInternal`（租约完整性按秒节流巡检，
    不每帧全场扫）+ 选秀命令与看盘/赔率/结算页内容组装。
  - `ModeH/ModeHRuntimeModule_SeasonFlow.cs` —— 转会窗口（接受/拒绝/过期走同一出口）、
    名人堂、赛季终局；**matchIndex 唯一推进点** `OpenNextMatchBrief`。
  - `tests/ModeHReachabilityGuard.py` —— 见下。
- 修改文件:
  - `UIAndSigns/UIAndSigns.cs`：基地船坞交互组注入 Mode H 入口选项
    （注入门 = 开关开启或有待恢复记录，避免出现点了必被拒的死选项）。
  - `Assets/SpawnPoints/Level_DemoChallenge_1.json`：补五点位。
    坐标由既有 Legacy 擂台环质心与远端 modeE 点几何推算，
    staging 距 arenaCenter 约 218m、距看台约 238m，均远超 `MinStagingIsolationDistance=30`。
    **其余八张暂不补**：registry 对缺字段天然 fail-closed，先实机核准这一张再铺开。
  - `ModeH/ModeHRecoveryPanel.cs`：`Show` 增 `stopRewardAnimation` 回调形参，
    删除 `FindObjectsOfType<WishFountainRewardAnimationView>()` 全场扫描——
    它会把**原版许愿池**正在播放的奖励动画一并销毁；现在只终止本模式自己登记的那一个实例。
  - `ModeH/ModeHRuntimeGates.cs`：区分「扫描因 I/O 失败」与「确有未结算事务」
    （新增 `_riskScanFaulted`、`IsModeHRiskScanFaulted`、`TryRetryRiskScan` 按槽代数节流、
    `GetLegacyBlockedMessageKey` / `ResolveLegacyBlockedMessageKey`）。
    落实 `docs/contracts.md:157` 承诺却一直没有实现的「I/O 异常提供重试」。
  - 七个旧模式入口（ModeD / ModeE / ModeF / ModeG / WavesArena / ZombieMode×2）：
    统一改用 `ResolveLegacyBlockedMessageKey()`——先自愈重试一次，再按真实成因取文案。
    此前 5 处硬编码同一句「仍有未结算的真实资产事务」，ZombieMode 一处显示无关的
    「其他模式进行中」，另一处**完全静默**（点了没反应也不知道为什么）。
  - `ZombieMode/ZombieModeMapSelection.cs`：新增 `IsZombieModeStartBlocked` 承接判定。
    放在这里是因为 `ZombieModeEntry.cs` 已顶到 `large_file_existing_allowlist.txt` 的行数上限。
  - `Localization/ModeHLocalization.cs`：补 14 个此前会显示 raw key 的条目——
    `State_*`×7 与 `StakePhase_*`×2（恢复面板按枚举名拼 key，缺一个就露 raw key）、
    招牌口令 `_Desc`×5（Commands.json 引用了它们）；另加本批新增的 4 个 key。
  - `docs/contracts.md`：修正两处与 §6.1 自相矛盾的「Mode H 尚未实现」旧文，
    并写明 `modeHRealWarehouseStakeEnabled` 是**禁止引入**的符号而非「拟议配置」。
- **新增 guard `tests/ModeHReachabilityGuard.py`**（本批最重要的交付）:
  它只回答一个问题——「这段代码到底会不会被执行」。断言：四个 partial 各有恰好一个
  带方法体的实现、入口可达（挂载或程序化二选一）、`RequestSeasonWrite` 有调用方、
  `TryMatchModeHSceneIntent` 有消费方、恢复壳有实例化、声明支持的地图五点位齐全。
  已做反例验证：把 partial 实现体改回纯声明、删掉入口挂载、抹掉落盘调用方，三种情况都会红。
- `tests/ModeHEntryIntegrationGuard.py`：允许旧模式入口把风险门判定委托给同一 partial class
  的具名 helper，但必须登记 helper 名与所在文件，且该文件仍被逐字检查到 `IsLegacyModeEntryAllowed`。

**兼容性影响**:
- 存档：无 schema 变更。Season key 只在玩家真正开局时才产生。
- 地图 JSON：新增五个**可选**字段，旧 JSON 缺字段时该图不支持 Mode H（既有 fail-closed 行为）。
- 旧模式：拒绝逻辑不变，只是文案分成两句并多了一次自愈重试。
- 运行时总回退：`modeHEnabled=false` 即可让入口不再注入。

**验证方法**:
1. 编译: Windows `compile_official.bat` 通过并部署。
2. Guard: `python tools/run_guards.py` 全量 494 项（新增 1 条）→ PASS=493 / NEW-FAIL=0 /
   KNOWN-RED=1。新 guard 与改动过的 guard 均做了反例验证。
3. 人工 smoke: 待实机——见下。

**未完成部分（accepted，按计划的资产安全顺序推进）**:
- 本批交付到「进场 → 认证 → 建赛季 → 选秀 → 看盘（含敌军计划）→ 赔率 → 锁盘 → 分帧生成校验」。
  生成事务会真的把本场敌军按计划生成出来并校验引用，随后**干净回滚并退回看盘**，
  同时给玩家一条明确提示。这里刻意**不**消耗同场重试预算、**不**挂起赛季：
  那两条路是给真实技术故障准备的，用在「功能尚未接线」上会把玩家的赛季推进死路。
- 战斗驱动本身（`ModeHCombatControl.Tick` 每帧驱动、口令窗口、拍铃、接力、
  ERROR 互换、四类快照采集、`MatchSettling` 单 token 结算）尚未接线：
  构件已就绪但仍无调用方。这是下一批的主要内容。
- **真实仓库押注一律不接线**：`ModeHWarehouseStakeJournal` 全部事务方法保持零调用，
  赔率页的押品选择器固定显示禁用原因。前置条件是 escrow 快照重建器（官方
  `ItemTreeData.InstantiateAsync` 路线）、满仓返还策略与 ManualIntervention 人工出口三者齐备，
  且虚拟注全链实机通过。在此之前接线会有资产丢失风险。
- `ModeHInteractable.TryOpenEntry` 目前无调用方（入口走 AddComponent + OnTimeOut）。
  保留而非删除：它与 `_autoPresenter`、`DismissActive` 构成一组协同语义——
  只销毁自建的短命 presenter，不销毁挂在船坞组上的组件。删它会牵连这两处（documented）。

**未验证/需人工**:
- 基地船坞交互应出现「黑市鸭王杯」选项；关掉开关且无恢复记录时不应出现。
- 选中后走地图选择 → 传送到 Level_DemoChallenge_1：日志应出现「跳过 Legacy 接管」，
  随后双租约取得、认证诊断页出现、认证通过后日志出现「赛季已创建 runId=…」。
- 认证诊断页点取消：应退还预扣船票并安全离场，且**不得**原地回落 Legacy BossRush。
- 五点位实机核准：staging 点不可被索敌、看台视野能看到擂台、exit 点安全可站立。
- 恢复壳打开时，原版许愿池正在播放的奖励动画**不应**被销毁。
- 旧模式入口在风险门命中时应显示区分后的两句文案之一，ZombieMode 地图选择不再静默。

---
### 2026-08-29 遗种巢 血脉目录时序 P0、自定义崽 P1 与养成内容实装（四系统审查修复 包3）

**状态**: fixed
**Finding**: CR-2026-08-29-004（血脉目录时序 P0）、CR-2026-08-29-005（自定义崽 preset P1）、
CR-2026-08-29-006（龙王掉落从未挂接）
**兼容分类**: SAFE（P0/P1 修复与口径修正）+ COMPAT（等级/放生/扩容为纯新增玩法，存档字段早已就绪，
老档 level=1/exp=0 由 Normalize 兜底，无 schema 变更）
**版本/Commit**: 本条目所在 commit
**Owner decision**: 需要；已拍板四项数值——扩容里程碑=图鉴 10/20/30 → 容量 16/20/24；
等级 Lv10 封顶每级 100 exp、来源 归巢+10 / 崽击杀+2（单局顶 30）/ 远征存活+25、每 3 级 PetCapcity+1；
放生返还 60 同血脉遗魂；careerCount 补进局计数。

**现象**:
1. （P0）玩家开启遗种巢后进竞技场杀**任何官方 Boss**：不掉蛋、不记遗魂、图鉴不涨；
   已有的官方血脉蛋孵化报 `lineage_unknown`；官方血脉的崽设为出战后每局静默不入场。
   即「全 Boss 皆可出崽」这一核心卖点对全部官方 Boss 失效。
2. （P1）三个自定义 Boss（焚天龙皇 / 幽灵女巫 / 龙裔遗族）的蛋能孵出来，但崽永远进不了场
   （`lineage_preset_missing`），基地闲逛也跳过；面板显示的是 `boss_dragonking` 这类裸 key。
3. （新发现）龙王走的是自己的手动掉落订阅，从不经 `RegisterBossRandomLootTracking`，
   因此遗种巢掉落追踪对它从未生效——焚天龙皇血脉连蛋都拿不到（P0 修完也依然拿不到）。
4. （P2/P3）巢满 12 是硬墙（无放生、里程碑扩容未实装）；等级/经验是死系统（恒 Lv1 却到处展示）；
   孵化揭晓页天赋数值少乘 100（"+0.08%"）；快速离开基地时闲逛崽可能落进战斗场景；
   名字长度数据层 16 / UI 层 12 两套口径；`MarkRevealed` 先改内存后提交且失败不回滚；
   运行时关开关不清在场随从；面板内结算的远征要等再次回基地才翻牌；两个死字段；
   五个无消费者的本地化 key；入场重试窗口一次 in-flight 失败即永久关窗；
   蛋只扫顶层容器（塞在背包件/箱子里的蛋在孵化页看不见）。

**根因**:
1. `PetNestLineageCatalog.EnsureBuilt` 在 bootstrap（`ModBehaviour.Start`）时执行，而
   `enemyPresets` 要等玩家第一次进竞技场才由 `WavesArena.InitializeEnemyPresets` 填充。
   构建时 `GetFilteredEnemyPresets()` 返回空表 → `AddOfficialLineages` 一条不加；
   `_built = true` 之后 `EnsureBuilt` 永久 no-op，全程没有任何重建时机。
2. 三个自定义 Boss 的 runtime preset 是各自生成那一刻才 `Instantiate` 的角色属性，不进任何全局
   注册表，而 `ObjectCache.GetCharacterPresets()` 是一次性快照（有意不刷新），因此永远查不到。
3. 龙王的掉落订阅是文件内自建的 `dragonKingLootEventHandlers` 手动路径。

**修复内容**:
- 新增文件（均已加入 `compile_official.bat`）:
  - `PetNest/PetNestProgressionService.cs`（等级/经验/生涯场次；击杀订阅幂等成对、热路径零分配早返链）
  - `PetNest/PetNestReleaseConfirmModal.cs`（放生二次确认弹窗，形态照 `PetNestRenameModal`）
- 修改文件:
  - `PetNest/PetNestRuntimeModule.cs`：新增 `NotifyEnemyPresetsRefreshed()` 重建入口；
    基地分支最前面插归巢结算（必须早于 `RestoreDownedPetsOnReturnToBase` 与 `CleanupOnce`）；
    OnUpdate 关闭分支补清随从与闲逛崽；删除死字段 `_lastEnabledState`。
  - `WavesArena/WavesArena.cs`：`InitializeEnemyPresets` 填充完成后通知重建目录。
  - `BossFilter/BossFilter.cs`：`InvalidateFilteredPresetsCache`（唯一咽喉点，覆盖 5 条过滤变化路径）
    通知重建目录，使掉落资格随玩家在场内改 Boss 池即时收敛。
  - `PetNest/PetNestCompanionSpawner.cs`：新增 `ResolveCompanionSourcePreset` 与
    `ResolveCustomLineageBasePreset`（自定义血脉走各自官方底模 `Cname_Boss_Red` / `Cname_Ghost`）；
    `CreateIsolatedAsync` 在中性化前写 `clone.nameKey = lineageKey` 身份戳。
    **有意不改 `ResolveSourcePreset`**：目录的官方循环靠它对自定义 key 返 null 来 fail-closed 跳过，
    改了会让官方循环以 `IsCustomBoss=false` 抢注自定义血脉，元素/缩放/显示名全部失效。
  - `PetNest/PetNestCompanionRuntime.cs`、`PetNestBaseIdleSpawner.cs`、`PetNestDebugProbe.cs`：
    三个消费点改调 `ResolveCompanionSourcePreset`。
  - `PetNest/PetNestLineageCatalog.cs`：自定义血脉显示名改用各 Boss Config 的双语常量。
  - `Integration/DragonKing/DragonKingBoss.cs`：手动掉落订阅处并联 `TryTrack`，
    离场与死亡两个清理点并联 `ClearTracking`（不重构手动路径）。
  - `PetNest/PetNestService.cs`：新增 `GetEffectiveNestCapacity()`（容量单点，纯派生不写档，
    存档 capacity 作老档下限）、`TryReleasePet`（单事务：移出+清席+返魂，失败逐项回滚）、
    私有回滚原语 `SetSouls`；`Capacity`/`TryAddPet` 收敛到同一口径。
  - `PetNest/PetNestTuning.cs`：新增扩容里程碑表、放生返还数与 7 个养成常量。
  - `PetNest/PetNestUIPages.cs`：巢页加放生动作（IsDanger，远征锁定禁用）与扩容进度提示；
    卡片副标题加经验进度/成年徽记；`FormatModifierValue` 提升 `internal` 供揭晓页复用；
    空巢补系统说明；凝蛋区加区头。
  - `PetNest/PetNestUI.cs`：接入放生弹窗；关闭面板时补翻牌（基地门控，只挂用户点击路径）。
  - `PetNest/PetNestHatchRevealView.cs`：天赋数值改用 `FormatModifierValue`（修 100 倍单位错）。
  - `PetNest/PetNestBaseIdleSpawner.cs`：非基地分支同步推进 `_sceneGeneration`，
    使 in-flight 分帧协程的代数比对必然失效。
  - `PetNest/PetNestHatchService.cs`：`SanitizeName` 改用 `PetNestTuning.MaxPetNameLength`；
    找蛋改为递归下钻 `Slots` + `Inventory`（深度限 16，逐层 try/catch，仅开孵化页时扫一次）。
  - `PetNest/PetNestExpeditionService.cs`：`MarkRevealed` 补整体回滚（形态同 `TryDepart`）；
    存活结算加 `AddExp(PetExpExpeditionSurvive)`（只改内存，落档并进既有 `CommitBoth`，不破坏原子性）。
  - `PetNest/PetNestDropService.cs`：遗魂跨过「可凝一枚蛋」阈值时提示一次
    （**不逐次击杀提示**：无间炼狱一局几十杀会刷屏）。
  - `PetNest/PetNestDownedHandler.cs`：删除死字段 `_pendingKillerName`。
  - `PetNest/PetNestModels.cs`：`Normalize` 补 level 上限、exp 与 careerCount 下限防御。
  - `Localization/PetNestLocalization.cs`：新增放生 4 个 + 扩容提示 + 成年徽记共 6 个 key；
    删除无实体道具对应的 `Soul`；`SystemDesc`/`SoulDesc`/`CondenseProgress`/`SoulGained` 接入消费点。
- Guard 同步（全部做了反例验证）:
  - `tests/PetNestRuntimeModuleGuard.py`：新增目录重建三断言 + WavesArena/BossFilter 两处通知断言。
  - `tests/PetNestCompanionLifecycleGuard.py`：新增 companion 解析入口、底模分支、nameKey 身份戳
    位置、以及两个消费点不得回退到通用解析。
  - `tests/PetNestDropLifecycleGuard.py`：新增 `check_dragonking_parallel`（龙王并联对）。
  - `tests/PetNestExpeditionSettlementGuard.py`：`MarkRevealed` 正则窗口 900 → 1600（回滚代码使其超窗，
    不改必红），并新增两条回滚断言。

**兼容性影响**:
- 存档：无 schema 变更。`level`/`exp`/`careerCount` 字段与 codec 早已就绪，老档默认值由 Normalize 兜底。
- 巢容量：不写档，纯由图鉴解锁数派生；曾手动扩过容的老档以存档值为下限，不会缩容。
- TypeID / Harmony / 资源 / 部署：无变化。
- 行为变化（均为修复方向）：官方 Boss 与龙王开始正常掉蛋记魂；自定义崽可出战；
  崽开始积累等级与生涯场次；巢满可放生腾位。

**验证方法**:
1. 编译: Windows `compile_official.bat` 通过并部署。
2. Guard: `python tools/run_guards.py` 全量 493 项 → PASS=492 / NEW-FAIL=0 / KNOWN-RED=1。
   四个改动过的 guard 均做了反例验证（回退源码确认会红）。
3. 人工 smoke: 待实机——见下。

**未验证/需人工**:
- **掉蛋（P0 主验证）**：开开关 → 进竞技场 → 日志应出现两次「血脉目录构建完成，条目数=<全量>」
  （预设初始化末尾 + Boss 池过滤初始化）；杀官方 Boss 应记遗魂并有 4% 掉蛋；
  **单独验证焚天龙皇**（P1 的龙王并联）。场内 Boss 池窗口关掉某 Boss 后，该 Boss 应立即不再记魂。
- **自定义崽出战（P1 主验证）**：孵三个自定义血脉各一只，面板显示应为「焚天龙皇/幽灵女巫/龙裔遗族」
  而非裸 key；设为出战应能正常入场（名条为血脉名、缩放正确、玩家阵营、伤害归一）。
- 孵化揭晓页天赋应显示 "+8%" 而非 "+0.08%"。
- 扩容：图鉴解锁 10 血脉后巢页容量应变 16，第 13 只不再 `nest_full`。
- 升级：归巢 +10（重伤退场的崽也应 +10 且 careerCount+1）、崽击杀 +2 至单局 30 封顶、
  远征存活 +25；Lv3 后捡漏背包应 +1 格；Lv10 显示「成年」；图鉴最高等级跟随。
- 放生：远征中应被拒；确认后崽消失、同血脉 +60 遗魂、不进纪念碑；写屏障下失败应整体回滚（崽还在）。
- 翻牌：远征页开着等到期 → 关面板应立即翻牌；开关关闭时关面板不得弹演出。
- 闲逛崽：进基地后 1-2 秒内立刻过图，压测确认战斗场景不出现闲逛崽。
- 嵌套蛋：把蛋放进背包里的箱子，孵化页应能看到。

**留置项**:
- 成年体快照 `PetNestAdultSnapshot` 本轮**不写入**（deferred）：Lv10 的三个触发点（归巢/击杀/远征）
  大多不在局内，`maxHealth`/`damageFactor` 拿不到真值，写残缺快照会把坏 schema 语义冻进 v1 存档；
  当前只做 Lv10 封顶 + 图鉴 `RecordLevel` + UI「成年」徽记（纯派生，零存档字段）。
- 遗魂提示采用「跨过可凝蛋阈值」而非逐次击杀（documented），理由见上。

---
### 2026-08-29 鸭科夫日报 开关复活、战斗帧落盘、死建筑闸与发放语义修复（四系统审查修复 包2）

**状态**: fixed
**Finding**: CR-2026-08-29-002（战斗帧同步落盘）、CR-2026-08-29-003（快递回退误销毁）；其余为同批审查 P2/P3
**兼容分类**: SAFE（行为修复；无 schema/TypeID/Harmony 变化。删除三个无人查表的本地化 key 属注入表减项，老档无感）
**版本/Commit**: 本条目所在 commit
**Owner decision**: 需要；已拍板——P3 全修、悬赏抽取层不动（只加 F3 诊断供实机定位）。

**现象**:
1. （P2）玩家在游戏中关掉再打开日报开关且不切场景，模块永远 dormant：计时器冻结、当天永不推进，
   期间战绩只留内存不随官方存盘落地。
2. （P2）跨天结算会在触发帧同步整档写盘（官方 `SaveFile` 含备份文件拷贝），而跨天由计时器驱动、
   每约 24 现实分钟一次，完全可能砸在交火帧上造成卡顿；与 `DailyReportPersistence` 头注释
   「战斗中不写盘」自相矛盾。
3. （P2）关闭开关后建造菜单里仍有报箱，玩家花 500 金买下后 `IsInteractable` 恒 false，
   连交互提示都不出现，无任何反馈。
4. （P2）`PlayerStorage.Push` 抛异常时 `CourierService` 会回退 `SendToPlayer` 把物品直接交给玩家，
   但不计入 `sentCount`；日报据此判定发放失败，对一件**已经在玩家背包里**的物品调 `DestroyTree`，
   并标记未领待补发（下次开面板重抽，可能换一件或变两件）。
5. （P3）首次开报纸与之后开报纸的头条/运势/杂谈会整版换一次；签到墙里程碑「已领」配色不查领取掩码；
   写屏障下悬赏现金仍会发放且已领标记落不了盘，可跨会话反复领；悬赏奖金被计入被报道那天的「进账」；
   关开关时统计采集器不退订；一个死常量、三个无人查表的本地化 key；采集了却从不展示的输出/承伤统计。

**根因**:
1. `OnUpdate` 首行 `if (!_bootstrapped) return;` 位于 `IsEnabled` 判断之前且从不调 `EnsureBootstrapped`，
   唯一的运行时唤醒路径是 `OnSceneLoaded`。
2. `FlushBatch` 只挡 `SavesSystem.IsSaving`，没有任何场景判定。
3. `InitDailyReportMailbox` 只有 `dailyReportBuildingInjected` 幂等判，缺 PetNest 同款的开关闸。
4. `QuickDeliverItems` 的 catch 分支回退成功后不计数，返回值语义与调用方期待不一致。
5. `DailyReportContent` 直读 `data.BountySeed`（新档为 0），真种子由后续任一悬赏查询才派生冻结；
   签到墙只用 `signed` 决定配色；`SettleBounty` 发钱前无写屏障检查；
   `EconomyManager.Add` 同步触发 `OnMoneyChanged`，而 `SettleBounty` 先于昨日快照转存执行。

**修复内容**:
- 新增文件: 无
- 修改文件:
  - `Integration/DailyReport/DailyReportRuntimeModule.cs`：`OnUpdate` 未 bootstrap 时先试
    `EnsureBootstrapped()` 再早返（关闭状态仍是 O(1)）；`ShutdownIfEnabledTurnedOff` 补
    `DailyReportStatsCollector.ShutdownSubscription()` 与 `OnDestroy` 对齐。
  - `PetNest/PetNestRuntimeModule.cs`：`OnUpdate` 同形同修（两系统保持同构）。
  - `Integration/DailyReport/DailyReportSaveCoordinator.cs`：`FlushBatch` 增 `bypassSceneGate` 形参，
    非基地一律推迟并保留 pending；`Tick` 在非基地直接返回，**不消耗** deferred 重试预算
    （战斗可远超 600 帧，否则 pending 会被丢成 `budget_exhausted`）；
    `TryFlushOnHostDestroy` 传 `true` 绕过闸（销毁/关停是最后机会）；新增 `IsBaseLevelSafe()`。
  - `Integration/DailyReport/DailyReportPersistence.cs`：头注释补「只在基地物理落盘」。
  - `Integration/DailyReport/DailyReportMailboxBuilder.cs`：注入前加开关闸（老档已建过仍照常注册
    prefab，形态与 `PetNestBuilder` 一致），两个入口汇入同一私有重载故一处即可。
  - `Integration/NPCs/Courier/CourierService.cs`：新增带 `out int fallbackDeliveredCount` 的重载，
    原 3 参方法委托之（**返回值语义不变**，`AwenDepositTokenUsage` 等既有调用方零改动）。
  - `Integration/DailyReport/DailyReportRewards.cs`：两路皆 0 才销毁；仅回退成功视为已送达返回 true
    （"先发后标记"顺序不变）；`TryGrantBountyCash` 用 try/finally 包住自家 `EconomyManager.Add`。
  - `Integration/DailyReport/DailyReportStatsCollector.cs`：新增 `_suppressMoneyDelta` 静态开关与
    `SetMoneyDeltaSuppressed`，`HandleMoneyChanged` 首行早返（零分配，热路径合规）。
  - `Integration/DailyReport/DailyReportService.cs`：`EnsureBountySeed` 提升 `internal`；
    `SettleBounty` 与 `TryRedeliverPendingBountyReward` 发钱前加写屏障闸。
  - `Integration/DailyReport/DailyReportContent.cs`：改调 `EnsureBountySeed`；战绩栏新增输出/承伤行。
  - `Integration/DailyReport/DailyReportUI.cs`：里程碑格配色改用 `IsMilestoneClaimed`。
  - `Integration/DailyReport/DailyReportTuning.cs`：删除零消费者的 `RewardRollMaxAttempts`。
  - `Localization/DailyReportLocalization.cs`：删除无人查表的 `SystemName` / `SignIn` / `AlreadySigned`
    （刊头是刻意字距版式、按钮走内联 L10n.T），保留唯一被消费的 `Interact`。
  - `DebugAndTools/F3DebugCheatMenuActions.cs`：日报 dump 增加悬赏题目、当前场景与
    `LevelConfig.IsRaidMap`，用于实机定位撤离类悬赏可达性。
- Guard 同步: `tests/PetNestRuntimeModuleGuard.py` 的 OnUpdate 早返正则改为新形（已做反例验证：
  回退成旧写法会红）。`tests/DailyReportPersistenceGuard.py` 逐项核对后无需改动
  （SaveFile 调用点计数未变、"先发后标记"顺序未变、订阅成对计数未变）。

**兼容性影响**:
- 存档：无 schema 变化。落盘时机推迟到基地，pending 仍在持久层，官方任何一次存盘也会顺带带走。
- 配置：无新增 key。
- 本地化：删除三个从未被查询的 key，玩家侧无可见变化。
- 行为变化：关闭开关的新档不再能建造报箱（老档已建的照常可用）；悬赏奖金不再计入被报道那天的进账。

**验证方法**:
1. 编译: Windows `compile_official.bat` 通过并部署。
2. Guard: `python tools/run_guards.py` 全量 493 项 → PASS=492 / NEW-FAIL=0 / KNOWN-RED=1。
3. 人工 smoke: 待实机——见下。

**未验证/需人工**:
- 战斗图用 F3「快进一天」触发跨天：应出现 `flush_deferred_not_base` 且无卡顿；回基地后自动补落盘并出刊。
- 战斗中跨天后直接杀进程重进：核对签到/悬赏数据一致性，不得出现重复发钱。
- 场景内关开关再打开（不切图）：当帧应出现「运行时模块已启动」。
- 关开关新档：建造 UI 不应出现报箱；老档已建过 + 关开关：不得报缺 prefab。
- 完成一次悬赏跨天后，新一期「进账」不含奖金额。
- **D6 线索**：进竞技场打一场后 F3 dump，确认 `Raids`/`Extractions` 是否增长、`isRaidMap` 取值。
  若竞技场非 raid map，「成功撤离 N 次」「出击且零阵亡」两类悬赏在纯竞技场玩法下不可达，
  后续修法应在**采集层**合成 raid 事件，绝不改抽取层（`SelectForDay` 是 `(seed, dayIndex)` 纯函数，
  动它会破坏「重启不重抽」承诺）。

**留置项**:
- D4 的 `PlayerStorage.Push` 异常分支依赖仓库异常态，难以实机构造，本轮只做静态验证（deferred）。
- 承伤统计不含无来源者的环境伤害（摔落/燃烧等），采集侧要求 `fromCharacter` 非空；
  展示文案不声明该口径（documented）。

---
### 2026-08-29 Mode G 成就断链、静默终局、放弃入口与双实例清理（四系统审查修复 包1）

**状态**: fixed
**Finding**: CR-2026-08-29-001（P1 成就断链）；其余为同批审查的 P2/P3
**兼容分类**: SAFE（行为修复与死代码清理）+ COMPAT（新增 1 个可选配置字段 `modeGAbandonHotkey`）
**版本/Commit**: 本条目所在 commit
**Owner decision**: 需要；已拍板——补放弃入口接通既有 ManualExit 链、P3 全修、双实例走「删死分支」方案 B。

**现象**:
1. （P1）正常打完一局 Mode G 后，本局全部 Boss 击杀不计入任何成就（`kill_50/100/500/1000_bosses`、
   `kill_dragon_king(_flawless)`、`dragon_slayer_master` 全部不涨），只有「本局仍活跃时直接退出游戏」才会补报。
2. （P2）波中生成耗尽、奖励候选池不足、奖励事务启动失败等终局对玩家完全静默：无横幅、无 recap、无解释，
   其中「胜利但奖励计划为空」一支还会连信物一起不返还。
3. （P2）玩家中途想放弃只能走图，零代价且不记败，设计稿的 `ManualExit` 终局与契约连胜惩罚整链是死代码。
4. （P3）recap「新纪录」每局必报；确认页挂机超 300 秒后点「立即迎战」静默失败且无重试路径；
   `ModeGSpawnTransaction` 类头注释与代码相反；若干孤儿公共成员。

**根因**:
1. `ModeGCombatTelemetry._pendingReports` 的唯一消费点在 `ModeG.PrepareHostDestroy`（`ModeGEntry.cs`），
   而正常终局 `End()` 先调 `EndModeGAchievementSession()` 关闭 session，
   `AchievementTriggers.ReportModeGBossKillAchievement` 首行 `if (!modeGAchievementSessionActive) return;` 直接丢弃。
   另外 token 由 `Fnv1a64(bossType|waveEpoch)` 派生，同波同类型多 Boss 会合并成一条被窄去重吞掉。
2. `End()` 自身不发任何横幅，播报全部散落在 `ModeGDeathRouting` 的 Victory/PlayerDeath 两条路由上。
3. `End(ModeGExitReason.ManualExit)` 全仓库无调用方；run 进行中场内 `ModeGInteractable` 被
   `IsModeGEntryBlocked` 挡掉，HUD 是只读无按钮，玩家没有任何可达入口。
4. `IsNewBestWave` 在 `RecordRun`（已把本局并入 profile）之后用 `>=` 比较；
   `TryStartModeG` 的 `modeGEntryPreview ?? GetOrCreateModeGEntryPreview()` 短路让过期 preview 绕过「过期即重建」。

**修复内容**:
- 新增文件: `Config/ConfigModeG.cs`（已加入 `compile_official.bat`；从 `Config/Config.cs` 拆出只为
  行数预算，`LargeFileBudgetGuard` 硬上限 1200 行，形态照 `ConfigDailyReport.cs`）
- 修改文件:
  - `ModeG/ModeGRuntimeModule_PublicApiAndShutdown.cs`：`End()` 在遥测退订前消费 pending 成就 report
    （早于 `EndModeGAchievementSession`）；新增 `ShowTerminalBannerFallback(reason)` 按 reason 白名单兜底播报
    （Victory/PlayerDeath 交给路由横幅，不双报）；删除永不可达的 `OnSceneLoaded` override。
  - `ModeG/ModeGRuntimeModule.cs`：成就 token 掺入 `health.GetInstanceID()`，同波同类型多 Boss 不再合并。
  - `ModeG/ModeGRunState.cs` / `ModeG/ModeGEntry.cs`：删除只写不读的 `pendingAchievementReportsConsumed`。
  - `ModeG/ModeGRewardTransaction.cs`：`TryReturnRelicOnce` 提升 `internal`（幂等仍由 Interlocked CAS 保证）。
  - `ModeG/ModeGDeathRouting.cs`：奖励事务未启动分支补幂等信物返还；两条终局路由在 `RecordRun` 之前
    快照旧最佳波次并透传给 recap。
  - `ModeG/ModeGRecapPanel.cs`：`Show` 增 `previousBestWave` 形参，`IsNewBestWave` 改 `>` 且不再读已更新的 profile。
  - `ModeG/ModeGInteractable.cs`：同文件新增 `ModeGAbandonPresenter`（短命 presenter + `ClaimModalInput`
    时停确认页 + §3.1 强制披露「船票与信物不返还，契约连胜清零」），确认分支唯一调用 `End(ManualExit)`。
  - `ModeG/ModeGEntry.cs`：`UpdateModeG` 加放弃快捷键轮询（`state.IsActive` + `IsModalInputPaused` 双闸，
    每帧一次 bool 链，零分配）；开局提示当前绑定键；`TryStartModeG` 去掉 `??` 短路并对过期/重建两种情形补提示。
  - `Utilities/ModeRuntimeHooks.cs`：带局切图先显式 `End(SceneChanged)` 再 `ShutdownModeG`（原先被 Dispose
    兜底成 `ModDestroyed`，reason 失真且兜底播报不可达）。
  - `ModeG/ModeGSpawnTransaction.cs`：类头注释改为与代码/guard 一致（两路均 `HoldForExternalCommit=true`）；
    删除无消费者的 `CommittedSlotCount`。
  - `ModeG/ModeGCombatTelemetry.cs`：删除无消费者的 `TotalShotCount` / `NamedAmmoTypeIds` /
    `GetBossDamageContribution`（backing 字段与在用的 `TotalAmmoSamples` 保留）。
  - `Config/Config.cs`：三处接线改为委托新 partial 文件的三个方法。
- Guard 同步:
  - `tests/ModeGPlayerLoadoutGuard.py`：`TryReturnRelicOnce` 可见性正则 `private` → `internal`。
  - `tests/ModeGMapSupportGuard.py`：`RuntimeFrozenPair` 不变式改述——禁止 `ModeGRuntimeModule` 再实现
    `OnSceneLoaded`（host 注册的是空壳实例，该回调不可达），并断言 `ModeRuntimeHooks` 先
    `End(SceneChanged)` 再 `ShutdownModeG`。
  - `tests/ModeGEntryPreviewGuard.py`：新增「禁止 `??` 短路跳过过期 preview 重建」与
    「重建后必须作废旧契约选择」两条断言。

**兼容性影响**:
- 存档：无。删除的 `pendingAchievementReportsConsumed` 是内存 volatile 字段，从不进任何 DTO。
- 配置：新增可选 key `BossRush_ModeGAbandonHotkey`（默认 K）；缺失时取默认值，老配置无感。
- TypeID / Harmony / 反射 / 资源 / 部署：无变化。
- 成就：修复后击杀开始正常累计；此前丢失的历史击杀不追溯。

**验证方法**:
1. 编译: Windows `compile_official.bat` 通过并部署 `Build/BossRush.dll`。
2. Guard: `python tools/run_guards.py` 全量 493 项 → PASS=492 / NEW-FAIL=0 / KNOWN-RED=1
   （既有 `DragonKingBossGunRocketSplitGuard`，与本轮无关）。三个改动过的 guard 已做反例验证：
   把源码回退成旧写法后确认会红。
3. 人工 smoke: 待实机——见下。

**未验证/需人工**:
- 一局内击杀 2 只同类型 Boss，终局后**不退出游戏**打开成就面板确认计数 +2；无伤击杀龙王确认 flawless 解锁。
- 放弃入口全流程：战斗中按 K 弹时停确认页 → 确认 → 横幅 + 日志 `reason=ManualExit` + 契约连胜清零 +
  `IsModeGEntryBlocked` 解除可再次入场；Rewarding 阶段按键应无效；确认页打开时再按键不叠开。
- 带局切图确认日志 `run 结束 reason=SceneChanged` 并出现「离开战场」提示。
- 确认页挂机 >300 秒后点「立即迎战」：应提示候选已刷新、退回预扣船票、重开确认页可正常开局。
- 回归红线：正常胜利一局（无双横幅、信物恰一枚、奖励发满）；正常失败一局（平纪录不再误报「新纪录」）。
- 默认键 K 与官方键位是否冲突需实机确认（可在 ModConfig 下拉改绑）。

**留置项**:
- `ModeGCleanupController.EmergencyShutdown` 与 `ModeGNemesisPersistence.MarkTombstone` 无调用点，
  但被 `ModeGStructureGuard` / `ModeGNemesisPersistenceGuard` 存在性冻结，本轮不动（deferred）。
  其中 `EmergencyShutdown` 内的 `ModeGPresentationAssetCache.Unload()` 是全仓唯一 bundle 卸载点，
  即展示 bundle 当前从不卸载（约 87KB 常驻），属既有轻微泄漏，接线需实机验证 bundle 生命周期后再做。

---
### 2026-08-28 Mode H（百战留痕：黑市鸭王杯）一次性完整实现

**状态**: fixed
**Finding**: 无（owner 指定的新模式完整交付）
**兼容分类**: COMPAT（新增一个配置字段）+ SCHEMA+（新增三个 v1 存档 key）+ OPERATIONAL（新增一个 local-only AssetBundle 制品）
**版本/Commit**: 见本轮 8 个里程碑 commit（da3e373 / 6488c43 / 250bc36 / 035c5c3 / 99bf95f / 29ab1e6 / bfe4754 / 448590c / 3c27fd5）
**Owner decision**: 需要；已拍板两项——(1) 资源门走“接线就位、PNG 与 bundle 由 owner 在 Unity 环境补”；(2) 提交策略走里程碑分批 commit。

**现象**: 不是 bug 修复，而是按 `docs/设计提案/2026-08-17_斗蛐蛐新模式创意脑暴.md` §17–§29 的冻结契约，
把 Mode H 从设计一次性实现为可实机验收的完整模式。交付前仓库中没有任何 Mode H 代码、数据或守卫。

**实现内容**:
- 新增 `ModeH/` 56 个 `.cs` + `Localization/ModeHLocalization.cs`，全部登记进 `compile_official.bat`。
- 新增 `Assets/Data/ModeH/` 七份签名 JSON（BossProfiles / Commands / CommandCompatibility /
  LoadoutKits / ThreatPlans / Scars / OddsWeights），加载后生成一个 `contentCatalogSignature`。
- 修改既有文件：`Config/Config.cs`（只新增 `modeHEnabled` 一个字段 + 一个 getter + 六处 ModConfig 接线）、
  入口链 6 处、旧模式最终入口 7 处双门、`Patches/Combat/CharacterOnDeadPatch.cs`、
  地图配置与刷怪点注册表五个可选字段、`Common/UI/BossRushUI.cs`（四个层级常量按升序插入）、
  `Common/Data/JsonDataRegistry.cs`（subDir 重载）、两个 bat 的部署块。
- 新增 29 条 Python guard，全部纳入交付阻断（不进 known_red 基线）。

**关键设计决策（都在代码注释与 `docs/contracts.md` §6.1 留痕）**:
- **没有真实资产开关**：`modeHRealWarehouseStakeEnabled` / `IsModeHRealWarehouseStakeConfiguredEnabled` /
  `GatePassed` 三个符号被守卫显式禁止；唯一的自动禁用来源是只读派生结果 `IsSlotConsistent`。
- **Harmony 新增严格限于两个 postfix**（`CA_ControlOtherCharacter.CanMove` / `CanRun`）；
  `CanUseHand` / `CanControlAim` 保持原版 `false`，这本身就是看台身体的引擎层隔离。
- **口令、伤病与战痕共用同一套字段修改设施**，不允许第二条路径；控制点严格限于 §17.6.2 白名单。
- **玩家真实资产只有三条白名单路径**，其余 Mode H 文件不得出现 `Inventory` / `PlayerStorage` /
  玩家 `ItemTreeData` 任一符号。
- **快照 fail-closed 一律回落“技术中止 + 同场重开”**，绝不判负、绝不声称继续了原战斗。

**数据点回填（原计划需要 owner 跑一趟游戏，已改为零成本解决）**:
`LoadoutKits.json` 的官方 typeId 原本打算由编译期 harness dump 后回填。改为直接采用官方 wiki
（escapefromduckov.net）的物品数据库，并用仓库与反编译中的五个已知锚点交叉验证其 `typeID`
就是游戏的 `Item.TypeID`：`862`=带火AK-47（对应仓库 `FIRE_AK47_TYPE_ID`）、`1254`=皇冠（`CROWN_TYPE_ID`）、
`1158`=水族箱（反编译 `Aquarium.aquariumItemTypeID`）、`1165`=蓝色方块（`SoulCollector.soulCubeID`）、
`868`=挑战船票（`GetBossRushTicketTypeId()` 兜底）。16 件 kit 与弹药全部固定为真实 typeId，
且全部核对过 `ControlMindType=0`、非 hidden、无 `LockInDemo`、品质落在 Q1–Q8。
`ModeHLoadoutKitRegistry.ResolveOne` 的 `typeId>0` 分支本就直读 `GetMetaData`，
因此这是**纯数据改动、零代码改动、运行时零检索开销**；守卫追加断言防止退回运行时 Search。

**验证方式**:
- Windows `compile_official.bat` 通过，零错误，`Build/BossRush.dll` 已更新并部署到游戏目录。
- 全量 guard：470 PASS / 1 待资源门红 / 1 既有红（DragonKing，与本轮无关）。
  唯一的新红是 `ModeHPresentationAssetGuard` 的“bundle 存在”断言——展示 AssetBundle
  是 local-only 制品，按 owner 拍板由其在 Unity 环境生成后复制；该守卫的其余断言全绿，
  bundle 落位后自动整体转绿。
- 运行时结论（生成无副作用、AI 双向伤害、资源显示、存档物理落盘、无泄漏、ERROR 互换、
  押品 exactly-once）**尚未验证**，只能由设计提案 §26.5 的 18 项实机 smoke 矩阵确认。
  交付状态为“完整实现已完成并待实机验收”。

**文档同步**:
- 改写 `docs/contracts.md` §6.1（旧稿的“只允许先做 H0 技术样机”“真实资产开关”“四门口径”全部作废）。
- `.qoder/repowiki/`：模式索引两处平行副本补 Mode H（主索引同时把“七大”改为“八大”，
  平行副本顺带补上此前缺失的 Mode G），新增 `Mode H：百战留痕（黑市鸭王杯）.md` 详解页，
  新增 `knowledge/zh/.../Mode H 百战留痕模式运行时/` 五份知识卡并登记 `_index.yaml`。
- `docs/未来拓展/` 的 `P2-ModeH-孤胆英雄` 三个文件加“名称让渡 / 已作废”标注，
  防止未来检索时被误当作 `ModeH/` 的设计依据。
- `AGENTS.md` §4.10 去掉写死的 guard 脚本数量。

**遗留**:
- 展示 AssetBundle 待 owner 按 `ArtSource/ModeH/prompts.md` 生成两张 PNG 并用
  `ModeHPresentationBundleBuilder.BuildOnlyAndExit` 打包后复制到 `Assets/ui/modeh_presentation`。
- 至少一张地图的 Mode H 五点位是基于既有 modeE 点位几何推算的，标注为待实机核准。

---
### 2026-08-27 c36e011..HEAD 全区间回归核对与 Wiki 同步缺口补齐

**状态**: fixed
**Finding**: 无（owner 要求的全区间回归核对）
**兼容分类**: SAFE（文档/站点配置补齐，零代码改动）
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: owner 要求确认 c36e011..HEAD 全部改动不影响原有玩法（只是优化），且 Wiki 内容已同步。

**核对结论（六路独立审查 + 编译 + guard）**:
1. Windows 正式编译通过（零错误，产出 `Build\BossRush.dll`）；全量 guard 443 脚本 442 PASS / 0 NEW-FAIL / 1 KNOWN-RED（既有红项）。
2. 等价重构类改动（反射缓存改名 22 处调用点、Boss preset 收口、物品模板化、本地化注入提取、分派表化、UI 常量化同值段、死代码删除）全部独立复核为逐字等价，无残留引用。
3. 区间内确认存在 owner 已确认的有意玩法变化（85e91e2 丧尸模式功能与数值、Mode F 命火/回血/成长重定、UI 观感升级与 9 处层级重排、MutatorUI IMGUI→uGUI、ModeG 宿敌击败计数按局一次），均已在既有 FIX_TRACKER 条目登记，非回归。
4. 两条 UI 风险线索经官方源码复核排除：官方射击输入不走 uGUI 射线（全源码无 `IsPointerOverGameObject`），MutatorUI 面板射线拦截不影响开火；官方光标为硬件光标（`CursorManager` → `Cursor.SetCursor`），任何 Canvas 层级都不可能遮挡。
5. 兼容面确认未变：存档 key、配置 key、TypeID（500058 合规新增）、本地化 key、AssetBundle、Harmony 目标；无新事件泄漏或 cleanup 缺口。

**修复内容（Wiki 同步缺口补齐）**:
- 修改文件: `wiki-site/docs/.vitepress/config.mts` —— 中英侧边栏补上宿命回响模式页与攻略页共 4 条导航（页面此前已生成但导航不可达）。
- 修改文件: `WikiContent/zh|en/item/item__mode_f_items.md` —— 模式专属物品页补「末日丧尸模式专属：便携安全区装置」小节（消除与 item__overview 分类行的内部指向不一致），并重跑 `sync-content.mjs` 再生成站点 docs（中英各 102 篇）。
- 修改文件: `.qoder/repowiki/zh/content/用户界面与本地化/UI 系统集成.md`、`.qoder/repowiki/zh/content/架构设计/组件交互模式.md` —— 补齐本轮 UI 收口（BossRushUI/BossRushUILayers/MutatorUI 迁移）与兼容性加固（Harmony 逐类 apply、HarmonyBindingSelfCheck、CriticalLog、急切反射缓存改名）的 repowiki 正文覆盖（此前仅修了死链）。

**验证方法**:
1. Guard: `python tools/run_guards.py` —— 442 PASS / 0 NEW-FAIL / 1 KNOWN-RED（补齐后复跑）。
2. 站点: `node scripts/sync-content.mjs` 通过（204 篇）；`npm run build` 验证侧边栏链接。
3. 编译: 本轮为文档/配置改动，无需重编（区间代码已于同日正式编译通过）。

**未验证/需人工**: 实机 smoke 仍未做，重点清单沿用各 Phase 条目（Mode F 命火节奏、丧尸安全区双槽、UI 多分辨率观感、补丁自检汇总行）。

---
### 2026-08-27 扩展性投资 Phase C：新增物品清单、Boss preset 收口与两条落地约定

**状态**: fixed
**Finding**: 无（全面代码审查产出的扩展性改进）
**兼容分类**: SAFE（文档 + 静态等价重构）、COMPAT（新增共享 helper）
**版本/Commit**: 未提交
**Owner decision**: 已确认（推进顺序 A→D→B→C）

**现象**:
1. 新增一个物品要动 9–13 个文件，代码侧**零文档**——`docs/制作教程/` 下 14 篇教程全是
   Unity 资源侧（建模/打包/图标）。漏掉任何一条接线都不会编译报错，只会运行时缺功能。
2. 每个自定义 Boss 各写一份 `FindXxxPresetInfo()` / `IsXxxPreset()`，三份 `IsXxxPreset`
   方法体逐字相同，只差各自 Config 的三个常量。加一个 Boss 就再抄一遍。
3. `EndModeE()`（~110 行）/ `ExitModeF()`（~105 行）是纯人工清理清单，新增 per-run 状态
   忘补一行即静默泄漏；模式激活标志 228 处裸读散在 8 个文件里各写 `else if` 链。

**修复内容**:
- **C1** 新增 `docs/制作教程/新增物品代码接线清单.md` —— 按最近两个真实物品
  （`ZombieTideBeacon` 500046、`PortableSafeZoneDevice` 500058）的实测改动面整理：
  两个新文件 + 8 个必改接线点（含"漏了会怎样"与对应守卫）+ 4 个必须同步的 guard
  + 4 处必须同步的文档 + 验证命令 + 三个参考实现。特别写明简单消耗品应走
  Phase B 新建的 `ModeFItemConfigHelper.ConfigureSimpleConsumable`，不要再手抄。
- **C2** 新增 `Utilities/ModBossPresetLookup.cs`（已登记 `compile_official.bat`）——
  `FindByNameKey` 与 `Matches` 两个静态方法收口 5 处重复实现（DragonKing / PhantomWitch
  的 Find + DragonKing / PhantomWitch / DragonDescendant 的 Is）。
  各 Boss 保留原方法名与可见性、改为一行转发，**调用点零改动**。
  行为逐字一致：遍历顺序、null 处理、三个匹配条件的短路顺序均不变。
  按 `docs/架构说明/Hooks分层约定.md` 放 `Utilities/`（跨模块基础设施）。
- **C5** `docs/架构说明/Harmony补丁契约稳定性.md` 新增 §6.5「新增补丁 / 新增反射绑定的标准做法」，
  把 Phase A 落地的三个范式登记为标准：补丁自检（新补丁类自动被覆盖，只有
  `TargetMethod(s)` 动态选目标的需自行校验）、`CriticalLog` 的使用规则与热路径禁令、
  以及 ModeG 的人工签署 verification revision 形态。
- **C3/C4** `docs/架构说明/游戏模式状态机设计.md` 新增 §6.3.1 与 §6.3.2 两条落地约定：
  新增 per-run 状态必须优先走 run-only 注册表、必须同一次改动补清理、清理要能被 guard 看见、
  要配 guard；以及"判断当前模式先看现成聚合谓词，新代码不要再写 `else if` 链"。

**降级说明（C3/C4 未按原方案改代码，理由）**:
- 原方案要"扩展 `IsAnyBossRushLikeModeActive()` 一族聚合谓词"。核对后发现现有三个谓词
  已覆盖实际用例，再加就是**没有消费者的预留抽象**，直接违反 `AGENTS.md` §4.9
  「不要因'未来可能复用'提前提升到全局层」。
- 原方案要"把 ModeF per-run 对象接入 `RunScopedRegistry`"。但本次没有新增 per-run 对象，
  所以只能二选一：迁移存量（要改两个上百行清理方法的执行顺序，而 ModeE→ModeF→ZombieMode
  的清理顺序有耦合，属 §10 需 owner 确认的状态机重构）或加无消费者的基础设施（同上违规）。
- **处理：把两者都落成"下一个人该怎么写"的硬约定**，写进模式状态机设计文档。
  这直接堵住审查发现的结构性风险（漏补一行即泄漏），且零行为变更。

**兼容性影响**: 存档 key、配置 key、TypeID、本地化 key、Harmony 目标全部未变。
C2 是提取方法 + 转发，其余为新增文档。

**验证方法**:
1. 编译: **已通过**（2026-08-27，`compile_official.bat`，GAME_PATH=`D:\software\steam\steamapps\common\Escape from Duckov`，WORKSHOP_PATH=`...\workshop\content\3167020`）——Roslyn 4.10 / C# 7.3，零错误零警告，产出 `Build\BossRush.dll`（3,132,928 字节）
2. 语法探针: `python tools/verify_syntax.py --with-bcl` —— 533 源文件（新增 1 个），CS1xxx 零错误
3. Guard: `python tools/run_guards.py` —— 443 脚本 **442 PASS / 0 NEW-FAIL / 1 KNOWN-RED**
4. 人工 smoke: 未执行，见下

**未验证/需人工**:
- ~~Windows 正式编译未运行~~ → 已于 2026-08-27 补跑通过（见上）。
- 实机 smoke 未做。C2 需确认：焚天龙皇、幽灵女巫、龙裔遗族三个自定义 Boss 仍能被正确识别与生成
  （波次里出现、被 ModeG 托管分流认到、清理正常）。

---
### 2026-08-27 重复代码收口 Phase B：模板化、注册表化与两处编码损坏修复

**状态**: fixed
**Finding**: 无（全面代码审查产出的可维护性收口）
**兼容分类**: SAFE（全部为静态可证明的等价重构或纯注释）
**版本/Commit**: 未提交
**Owner decision**: 已确认（推进顺序 A→D→B→C，硬要求"确保不影响原有功能"）

**现象**: 审查发现多处复制粘贴与文档级缺陷。逐项核对后，**有两项的前提被证伪**，见"降级说明"。

**修复内容（逐项独立落地，每项通过验证后才做下一项）**:

- **B9 编码损坏（3 处，非 2 处）**: 全仓扫描 GBK→UTF-8 双重编码乱码，实际找到 3 个文件：
  `LootAndRewards/LootAndRewardsRuntimeHooks.cs`（整文件注释）、
  `Integration/DragonKing/DragonKingAssetManager.cs:457`（会输出到日志的 DevLog 字符串）、
  `UIAndSigns/UIAndSigns.cs:52/55/58`（三行字段注释，审查线索未提及）。
  其中两处含私有区字符（U+E62C 等），终端复制会丢字，改用字节级定位替换。
  按字段名与上下文重写为准确中文，不是机械解码（解码结果本身也有丢字）。
- **B9 场景常量副本**: `ModBehaviour.cs` 里 `BaseRootSceneName`/`BaseSceneSubName`/`BaseSewerSceneName`
  三个常量与 `Utilities/SceneRuntimeGate.cs` 重复，且**全仓零引用**（`BaseSceneName` 有 8 处引用故保留）。
  删除三个死常量并留注释说明完整清单在 SceneRuntimeGate。
- **B7 Reforge 武器特效 if 链 → 分派表**: `Integration/Reforge/ReforgeDataPersistence.cs` 此前 TypeID 判断
  和特效调用分散两处，加一把带特效武器要改两个地方。改为 `ShowcaseWeaponEffectEntry[]` 只读分派表
  + `FindShowcaseWeaponEffect(typeId)`。行为等价（同 TypeID、同方法、同样只处理第一个 ItemGraphicInfo）。
  注：最初写成 `private static readonly Dictionary<>`，被 `StaticCacheLifecycleGuard` 正确拦下
  （它按 Dictionary 形状识别静态缓存）。这是不可变分派表而非缓存，改用只读数组表达——
  既诚实也不用动 guard、不用进白名单。
- **B4 ModeF 工事包 4 个同构 Config → 共享模板**: 四个 `ConfigureItem` 逐字相同 45 行，
  实际只差 MaxStackCount / Value / Quality 三个数字和日志前缀。新增
  `ModeFItemConfigHelper.ConfigureSimpleConsumable(...)`，四个 Config 各自缩到约 14 行。
  改写脚本对每个文件断言了旧值（stack/value/quality）才允许替换，防止改错文件；
  顺带移除因此失效的 `using System;`。TypeID、key、文案、赋值顺序、日志文案全部不变。
- **B3 本地化注入三段复制 → 提取**: `Localization/LocalizationInjector.cs` 的船票/生日蛋糕/WikiBook
  三个注入方法结构逐行相同。提取 `InjectItemLocalization(typeId, nameKeyCn, nameKeyEn, canonicalKey, ...)`。
  **等价性已证明**：用脚本从 git HEAD 取出旧版本，提取三个方法注入的 key 表达式清单，
  确认新实现产生的 key 集合逐字一致（`canonicalKey + "_Desc"` 恰好等于原来的字面量
  `"BossRush_Ticket_Desc"` / `"BossRush_BirthdayCake_Desc"` / `"BossRush_WikiBook_Desc"`）；
  WikiBook 额外的「冒险家日志」两条保留在调用方，key 互不相同故顺序无关。
- **B8 NPC 模块共享基类**: `NPCModuleRegistry.cs` 的 Goblin/Nurse `ShouldSpawnInScene` 逐字相同
  （只差取哪个 NPC_ID 判婚姻状态，而 NpcId 本就是接口成员）。提取 `ArenaSupportNpcModuleBase`。
  快递员不并入——它没有婚姻门控、竞技场判定也不同。
  已确认 `AutoDiscoverModules` 显式跳过 `type.IsAbstract`，新基类不会被误实例化。
- **B2 CanvasScaler 收口（5 处）**: 5 个界面各自手写 CanvasScaler 参数。逐个核对后确认全部
  与 `ZombieModeUIHelper.ConfigureCanvasScaler` 等值：三项参数完全相同；其中 3 处额外显式写了
  `screenMatchMode = MatchWidthOrHeight`，而这正是新建 CanvasScaler 的默认值，且这 5 处的
  CanvasScaler 均为新建（`AddComponent` 或 `new GameObject(typeof(CanvasScaler))`），故行为不变。
  全仓手写 `uiScaleMode` 赋值已清零（仅剩 helper 自身实现）。
- **B2 新增 guard 断言**: `tests/BossRushUISharedLibraryGuard.py` 增加第 8 条——
  全仓除 helper 自身外不得出现 `.uiScaleMode =` 赋值，并把 3 个新收口文件加入 MIGRATED 白名单。
  该断言做了负例测试（临时改回手写 → 正确 FAIL）。

**降级说明（原计划两项，核对后证伪前提，未按原方案执行）**:

1. **B1「ModeE/ModeF 血条名牌 ~500 行 1:1 逐字复制」——前提不成立。**
   程序化归一化比对后，真正逐字相同的只有 3 个小方法
   （`TryGetCached*HealthBar`、`Mark*NamesDirty`、`Sync*NameLanguageState`）。
   其余已实质分叉：`RegisterHealthBar` 在 ModeE 多清一个 `BaseText` 字典；
   节流位置不同（ModeE 在 `Find` 里、ModeF 在 `ScanAndCache(force)` 里）；
   ModeF 另有 `FindModeFPlayerHealthBar` 与 `FindModeFHealthBar` 并存；
   失败处理一边是限流日志一边是空 catch；ModeF 还额外挂悬赏后缀缓存与雷达节流。
   抽公共基类必然要统一这些差异 = 改行为，与"不影响原有功能"冲突；
   而只抽那 3 个小方法净收益接近零（省下的行数和新增 helper 相当）。
   **处理：保持两套独立，在两个文件顶部写明差异清单与"不要合并"的结论**，
   防止后续协作者按"看起来是复制粘贴"再次尝试合并并踩坑。
2. **B6「ObjectCache 统一失效判断」——不做语义变更，降级为文档化。**
   核对发现 `GetBoxColliders()` **全仓零调用点**（其只判 null 的写法不构成实际问题）；
   `GetNotificationTexts` 走 `Resources.FindObjectsOfTypeAll`（资产级，不随场景销毁）。
   把 `== null` 改成 `IsUnityObjectArrayAlive` 会改变重扫时机（对 BoxCollider 还可能是性能回归），
   静态证明不了安全。**处理：补注释说明"场景级 vs 资产级"两类失效判断为何不同、
   `_cachedCharacterPresets` 的不对称为何是有意的**，零行为变更。

**兼容性影响**: 存档 key、配置 key、TypeID、本地化 key、AssetBundle 名、Harmony 目标、
UI 数值与组件集合全部逐字未变。所有改动为提取方法/常量收口/删死代码/改注释。

**一个可观察但无害的差别（B3 副作用，已记录在代码注释里）**：
`BossRush_Ticket_Desc` / `BossRush_BirthdayCake_Desc` / `BossRush_WikiBook_Desc` 三个 key
此前是源码字面量，现在由 `canonicalKey + "_Desc"` 运行时拼出，**源码里 grep 不到**。
已用脚本核对：全仓（排除 Build/官方源码）对这三个字面量的引用为 0，
且运行时注册的 key 集合与改动前完全一致。收口后全仓 `BossRush_*` 源码字面量
从 756 个降为 753 个，差额恰好是这三个，无其它丢失、无新增。

**验证方法**:
1. 编译: **已通过**（2026-08-27，`compile_official.bat`，GAME_PATH=`D:\software\steam\steamapps\common\Escape from Duckov`，WORKSHOP_PATH=`...\workshop\content\3167020`）——Roslyn 4.10 / C# 7.3，零错误零警告，产出 `Build\BossRush.dll`（3,132,928 字节）
2. 语法探针: 每项改动后跑 `python tools/verify_syntax.py --with-bcl` —— 532 源文件，CS1xxx 零错误
3. Guard: 每项改动后跑 `python tools/run_guards.py --changed-only`，全部项完成后跑全量 ——
   443 脚本 **442 PASS / 0 NEW-FAIL / 1 KNOWN-RED**（既有红项）
4. 等价性证明: B3 用 git HEAD 版本做了 key 集合逐字比对；B4 改写时逐文件断言旧数值；
   B2 逐站点核对了 CanvasScaler 参数与组件新建方式；B9 删常量前 grep 确认零引用
5. 人工 smoke: 未执行，见下

**未验证/需人工**:
- ~~Windows 正式编译未运行~~ → 已于 2026-08-27 补跑通过（见上）。
- 实机 smoke 未做。重点看：Mode F 工事包四件（折叠掩体/加固路障/阻滞铁丝网/应急维修喷雾）
  名称、描述、堆叠上限、售价、品质与改动前一致；船票/生日蛋糕/冒险家日志的名称描述正常
  （不出现 `*BossRush_xxx*` 星号原文）；重铸展示柜上龙息与霜之哀伤的展示特效仍在；
  哥布林与护士的刷新场景规则不变；成就弹窗、婚礼过场、许愿池奖励动画、Mode F 赏金雷达
  在 1080p 与 4K 下缩放正常。

---
### 2026-08-27 流程自动化 Phase D：聚合 guard runner、语法探针、授权清理与三个缺失 guard

**状态**: fixed
**Finding**: 无（全面代码审查产出的流程改进）
**兼容分类**: OPERATIONAL（构建/验证流程与 git 纳管）、SAFE（死代码清理与新增 guard）
**版本/Commit**: 未提交
**Owner decision**: 已确认（授权删除 TeleportDebugMonitor.cs、SpeedrunGauntletTicket 常量、cleanup_old_files.bat；授权 git 纳管关键文件）

**现象**:
1. guard 体系是本仓最大质量资产（原 440 个脚本），但只能本地手动跑，且推荐的两种跑法都有硬伤：
   `for %f in (tests\*.py) do python %f` 不聚合结果、不 fail-fast；`validate_refactor_step.bat` 的循环
   是 fail-fast——既有红项 `DragonKingBossGunRocketSplitGuard`（字母序 D）会**永久遮蔽其后 300+ 个 guard**。
   guard 中文输出在 cp936 控制台还会 mojibake。CI 里只有 Wiki 部署，零 guard。
2. 本机没装游戏，`compile_official.bat` 跑不了，"验证"长期只到 guard 层，且没有可重复的语法层检查手段。
3. 三条被反复强调的 golden rule 零自动化：TypeID 台账（4.3）、`DisplayNameRaw` 配本地化注入（4.4）、
   repowiki 同步（4.13）。
4. 死代码与危险遗留：`TeleportDebugMonitor.cs`（301 行 MonoBehaviour，全仓无任何 `AddComponent`，
   从未被挂载）；`Config/Config.cs` 的 `SpeedrunGauntletTicket = 500047`（零使用点，且与三份契约文档
   「500047 为保留空洞」直接矛盾）；`cleanup_old_files.bat`（含 `del /q *.py`、`del /q *.ps1`）。
5. `test_bossrush_official.bat`（部署流水线）与 4 份被 guard 直接读取的 docs 未被 git 跟踪 →
   fresh clone 跑 guard 必红，且部署流程丢了就无法复现。
6. `AGENTS.md` §4.1 把编译清单文件数写死成 483，实际 532。

**根因**: 验证工具链停留在"能跑就行"，没有把既有红项、编码、聚合、CI 这些工程化问题解决掉；
golden rule 靠人工遵守；`docs/` 默认 local-only 的策略与「guard 硬依赖某些 docs」冲突。

**修复内容**:
- 新增文件: `tools/run_guards.py` + `run_guards.bat` —— 聚合式 runner：全量跑不中断、并发执行
  （443 脚本约 30 秒，此前顺序循环数分钟）、PASS/FAIL 汇总与失败清单、强制 UTF-8、
  `--changed-only`（按 git 改动挑相关 guard）、`--filter`、`--verbose`；
  已知红项走 `tests/known_red_guards.txt` 基线，登记项失败不判红但单独列出，
  **登记后又转绿的条目报 STALE-BASELINE 并判失败**，防止基线越积越多掩盖回归。
- 新增文件: `tools/verify_syntax.py` + `verify_syntax.bat` —— 用本机 .NET SDK Roslyn 对编译清单
  做语法层（CS1xxx）检查，输出显式声明「通过 ≠ 编译通过」；`--with-bcl` 可多挂 BCL 引用再抓一层。
- 新增文件: `tests/TypeIdLedgerGuard.py` + `tests/typeid_literal_allowlist.txt` —— 从 `docs/contracts.md` §1
  解析台账（范围 + 保留空洞）作为事实源，交叉核对 `AGENTS.md` §4.3 数字一致，再扫描全部源码
  （**剥离注释**，保留字符串）里的 `5000xx` 字面量，命中保留空洞或越界即 FAIL。
  已知非 TypeID 的同形数字走豁免清单（当前 1 条：成就阈值 500000）。实测 56 个在用 TypeID，
  与「500001-500058 去掉 2 个空洞」完全吻合。
- 新增文件: `tests/LocalizationInjectionGuard.py` + `tests/localization_injection_allowlist.txt` ——
  golden rule 4.4 首次自动化。注意仓库里**没有**文档所写的字面量写法，统一是
  `item.DisplayNameRaw = LOC_KEY_DISPLAY;`，且注入点常在别的文件（Mode F 工事包集中在
  `BossRushIntegration.InjectModeFItemLoc`），因此 guard 先把常量解析回字符串，再在全仓
  接受三种注入证据（同文件常量 / 限定名 `XxxConfig.LOC_KEY_DISPLAY` / 字面量）。
  实测 19 个 `BossRush_*` 显示名 key 全部找到注入；2 处 `locKey` 是通用 helper 的方法参数，
  按 WARN 列出（人工确认无误）。
- 新增文件: `tests/RepowikiReferenceGuard.py` + `tests/repowiki_reference_allowlist.txt` ——
  校验 `.qoder/repowiki` 全部 `file://` 引用的目标存在。落地时一次性修掉 **13 个死链、103 处引用**：
  历史路径前缀写错（`BossRushMod/...`、`WavesArena/BossRushMod/...`）、缺目录前缀
  （`ModeFPhases.cs` 等）、错字（`BossRashRuntimeModuleHost`）、改名文件
  （`DragonFlameMarkMarkTracker`、`GoblinReforgeInteractable` 位置）、wiki-site 路径层级，
  以及本轮 A6 改名遗留的 `Common/Infrastructure/ReflectionCache.cs`。
- 新增文件: `.github/workflows/guards.yml` —— CI 跑 guard runner（ubuntu + Python 3.12）。
  明确只跑 guard 不跑编译（runner 上没有也不该有游戏程序集）。
- 删除文件: `TeleportDebugMonitor.cs`（同步 `compile_official.bat`、`AGENTS.md` 子系统地图、
  `tests/ModBehaviourInstanceClassificationGuard.py` 的计数与 `docs/testing/2026-05-14-modbehaviour-instance-classification.md`
  的基线 357→355、`docs/项目全景文档.md`、以及 2 篇 repowiki 内容文档）。
  删除前二次确认：它是 MonoBehaviour 但全仓无任何 `AddComponent`，从未被挂载，属死代码。
- 删除文件: `cleanup_old_files.bat`（2025 年遗留，含 `del /q *.py` / `del /q *.ps1` 危险命令）。
- 修改文件: `Config/Config.cs` —— 移除 `SpeedrunGauntletTicket = 500047`，原地留注释说明 500047 是保留空洞。
- 修改文件: `.gitignore` —— 移除已失效/误导的 `compile_official.bat`、`cleanup_old_files.bat` 条目；
  放行 `test_bossrush_official.bat`；用精确的 `/docs/*` + 反选规则放行 4 份 guard 硬依赖的文档，
  同时确认 `docs/飞书应用密钥.md`、`docs/项目全景文档.md` 等仍被忽略（已用 `git check-ignore` 逐条验证）。
- git 纳管（已 `git add`，未提交）: `test_bossrush_official.bat`、
  `docs/testing/2026-05-14-final-runtime-smoke.md`、`docs/testing/2026-05-14-modbehaviour-instance-classification.md`、
  `docs/制作教程/便携安全区装置_Unity资源制作约定.md`、`docs/末日丧尸模式/末日丧尸模式_goal执行文档.md`。
  注：审查线索称有 6 个 guard 依赖未跟踪 docs，实测只有 **4 个**真正读取文件，另两个仅在注释里提到路径。
- 修改文件: `tests/OfficialCompileListFileExistenceGuard.py` —— 新增断言：`AGENTS.md` §4.1 不得再把
  文件数写死（并在 PASS 行输出实测数量）。
- 修改文件: `AGENTS.md` §4.1、`tests/AGENTS.md`、`CODE_REVIEW.md` —— 运行入口改指向新 runner 与语法探针，
  并写明为什么不能再用旧的 `for %f in (tests\*.py)` 写法。

**兼容性影响**:
- 运行时零影响：删除的 `TeleportDebugMonitor` 从未被挂载；删除的 `SpeedrunGauntletTicket` 零使用点；
  其余全是脚本、guard、CI 与文档。
- 存档 key、配置 key、TypeID、本地化 key、Harmony 目标：全部未变（500047 仍是保留空洞，未回填）。
- `.gitignore` 改动只影响哪些文件可被跟踪，不影响已跟踪文件。

**验证方法**:
1. 编译: **已通过**（2026-08-27，`compile_official.bat`，GAME_PATH=`D:\software\steam\steamapps\common\Escape from Duckov`，WORKSHOP_PATH=`...\workshop\content\3167020`）——Roslyn 4.10 / C# 7.3，零错误零警告，产出 `Build\BossRush.dll`（3,132,928 字节）
2. 语法探针: `python tools/verify_syntax.py --with-bcl` —— 532 源文件，CS1xxx 零错误
3. Guard: `python tools/run_guards.py` —— 443 脚本 **442 PASS / 0 NEW-FAIL / 1 KNOWN-RED**（既有红项）
4. 新 guard 自测: TypeIdLedgerGuard 的注释剥离、台账解析、AGENTS 交叉核对做了正负例测试
   （故意传错范围/空洞均正确报错）；OfficialCompileListFileExistenceGuard 的新断言做了负例测试
   （临时把 999 写回文档 → 正确 FAIL，随后还原）；`.gitignore` 放行规则用 `git check-ignore` 正反验证。
5. 人工 smoke: 不需要（无运行时改动）

**未验证/需人工**:
- ~~Windows 正式编译未运行~~ → 已于 2026-08-27 补跑通过（见上）。
- `.github/workflows/guards.yml` 未在真实 CI 上跑过（仓库未推送），首次 PR 时需确认 runner 上
  Python 3.12 能跑通全部 guard（本地 3.12 已通过）。
- git 纳管的 5 个文件目前只是 `git add` 到暂存区，尚未提交（按 AGENTS.md §12，提交需 owner 明确要求）。

---
### 2026-08-27 兼容性加固 Phase A：关键失败日志、Harmony 逐类 apply 与绑定自检

**状态**: fixed
**Finding**: 无（全面代码审查产出的兼容性加固，非既有 finding）
**兼容分类**: COMPAT（纯加法 + 失败路径改善）、SAFE（`ReflectionCache` 改名为静态等价重命名）、WIRE+（Harmony apply 粒度，owner 已确认）
**版本/Commit**: 未提交
**Owner decision**: 已确认（推进顺序 A→D→B→C；Harmony 改逐类 apply；四项授权清理待 Phase D）

**现象**: 三项系统性兼容风险，官方游戏更新时会「静默塌方且玩家日志无痕」：
1. `DevLog` 带 `[Conditional("BOSSRUSH_DEV")]`，而 `compile_official.bat` 正式构建不注入该 define，
   编译器会整体删除全部 4748 处调用点——其中含 378 条 `[ERROR]` 与 874 条 `[WARNING]`。
   Harmony 失败、反射绑定失败、AssetBundle 缺失、数据表损坏在玩家机器上一条都不打印。
2. `harmony.PatchAll()` 用单个 try/catch 包住：官方改动 50 个补丁目标里任意 1 个，其余补丁全部不生效，
   且失败日志被第 1 条编译掉。`Patches/Combat/ProjectileHalfObstaclePatch.cs:11` 的注释证明该风险已发生过一次。
3. 约 744 处字符串反射绑定无集中失败上报；唯一自检 `EnsureCriticalPatchesApplied` 只覆盖 8 个方法。
   另：`BossRush.ReflectionCache`（Common/Infrastructure，启动期急切绑定）与
   `BossRush.Common.Utils.ReflectionCache`（懒字典缓存）同名，调用点语义只能靠 using 区分。

**根因**: 可观测性设计假设「调试期能看到日志」，未考虑正式构建把诊断整体编译掉；
补丁 apply 粒度沿用 Harmony 默认的全程序集一次性 `PatchAll`。

**修复内容**:
- 新增文件: `Common/Infrastructure/HarmonyBindingSelfCheck.cs`（已加入 `compile_official.bat`）——
  启动期只读校验全部 `[HarmonyPatch]` 类的目标方法是否实际挂载本 Mod 补丁，输出 `verified/total` 与失败清单；
  `TargetMethod(s)` 动态选目标的补丁类跳过不计入分母；全程 try/catch，不 apply、不补装。
- 新增文件: `Common/Infrastructure/BossRushEagerReflectionCache.cs`（由 `Common/Infrastructure/ReflectionCache.cs` 改名而来，
  编译清单同步替换）——消除与 `Common.Utils.ReflectionCache` 的同名歧义；28 处调用点同步更新，成员名与行为逐字不变。
- 修改文件: `DebugAndTools/DebugAndTools.cs` —— 新增非 `Conditional` 的 `CriticalLog(message)` /
  `CriticalLog(dedupeKey, message)`，按 key 去重、上限 64 条、含 `[ERROR]` 走 `LogError` 否则 `LogWarning`；
  配套 `ResetCriticalLogStaticCaches()`。
- 修改文件: `Utilities/AlwaysOnRuntimeHooks.cs` —— `harmony.PatchAll()` 改为
  `ApplyHarmonyPatchesPerClass(harmony)`（同程序集、同类型顺序、同 processor，逐类 try/catch 隔离 + CriticalLog 汇总）；
  接入 `HarmonyBindingSelfCheck.RunStartupSelfCheck`；OnDestroy 路径补两处去重状态重置。
- 修改文件（DevLog → CriticalLog，仅失败分支）: `Utilities/SafeRuntime.cs`、`Common/Events/BossRushEventBus.cs`、
  `Common/Lifecycle/BossRushRuntimeModuleHost.cs`、`Common/Infrastructure/BossRushEagerReflectionCache.cs`、
  `Integration/BossRushDynamicItemRegistry.cs`、`Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs`、
  `Common/Data/JsonDataRegistry.cs`、`Common/MapConfig/MapSpawnPointRegistry.cs`、`Config/LootBlacklistRegistry.cs`。
- 修改文件: `tests/ArchitectureStructureGuard.py` —— 钉住新结构（禁止回退 `harmony.PatchAll(`、
  要求 `ApplyHarmonyPatchesPerClass` helper 及其四个关键 token、要求自检接入与两处 OnDestroy 重置），
  并把 `HarmonyBindingSelfCheck.cs` 加入 `REQUIRED_COMPILE_SOURCES`。
- 修改文件: `tests/ModeEShellHarmonyUiContractGuard.py` —— 反射缓存 token 同步改名。
- 修改文件: `docs/架构说明/Harmony补丁契约稳定性.md` —— §0 注册点、§3 F1/F2/F5 可观测性、§6.1/§6.2 状态改为已实施。

**兼容性影响**:
- 存档 key、配置 key、TypeID、本地化 key、AssetBundle 名、Harmony 补丁目标：**全部逐字未变**。
- Harmony apply：全部补丁成功时与 `PatchAll` 逐字等价（`PatchAll` 内部即
  `AccessTools.GetTypesFromAssembly(assembly).Do(t => CreateClassProcessor(t).Patch())`）；
  仅在「本来就已失败」的场景下表现不同——由 50 个全塌变为只塌失配的那一个。
- 日志：正式构建新增少量契约级失败输出（去重 + 上限 64），成功路径零新增输出。
- `ReflectionCache` 改名为纯重命名，`Common.Utils.ReflectionCache` 未受影响。

**验证方法**:
1. 编译: **已通过**（2026-08-27，`compile_official.bat`，GAME_PATH=`D:\software\steam\steamapps\common\Escape from Duckov`，WORKSHOP_PATH=`...\workshop\content\3167020`）——Roslyn 4.10 / C# 7.3，零错误零警告，产出 `Build\BossRush.dll`（3,132,928 字节）
2. 语法/语义探针: 用本机 .NET SDK 8.0.302 Roslyn 对全部 533 个源文件做两轮探针——
   仅 BCL 引用时 CS1xxx 语法错误 **0**；追加真实 `0Harmony.dll`(2.9) 引用后，
   `HarmonyBindingSelfCheck.cs` 与 `AlwaysOnRuntimeHooks.cs` 诊断 **0**，
   其余文件残留诊断全部为缺 Unity/Duckov 程序集的 CS0246，无一条可归因本次改动。
   另用反射逐个核对了所用 Harmony API 签名（`CreateClassProcessor`、`PatchClassProcessor.Patch`、
   `AccessTools.GetTypesFromAssembly/Method/PropertyGetter/PropertySetter/Constructor`、
   `HarmonyMethod.Merge` 及其四个字段、`HarmonyAttribute.info`、`Patches.*`、`Patch.owner`、`Harmony.Id`）全部吻合。
3. Guard: 全量 439 个脚本 **438 PASS / 1 FAIL**，唯一失败为既有红项 `DragonKingBossGunRocketSplitGuard`
   （c36e011 之前即为红，与本次改动无关）。改动前后基线一致，零回归。
4. 人工 smoke: 未执行，见下。

**未验证/需人工**:
- ~~Windows 正式编译未运行~~ → 已于 2026-08-27 补跑通过（见上）。
- 实机 smoke 未做，需确认：进游戏后 `Player.log` 出现补丁自检汇总行且 `verified == total`；
  正常流程下不出现 `[BossRush][CRITICAL]` 噪声；各模式补丁功能（阵营锁定、血条染色、售货机、
  动态物品图标、亡魂、掉落箱、近战特效）表现与改动前一致。
- 逐类 apply 的等价性依据 HarmonyLib 2.x `PatchAll` 实现（同程序集、同顺序、同 processor），
  已用 2.9 程序集核对 API 但未在游戏内实跑，属静态推断。


**状态**: fixed
**Finding**: 无（防御性收口，当前组合不可达）
**兼容分类**: SAFE（对现有调用点静态证明无行为变化）
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 无玩家可见症状。`SpawnEnemyCoreInternalAsync` 的 `deferActivationUntilNextFrame` 分支提前 `return await ScheduleModeEFSpawnPostprocessAsync(...)`，参数列表不含 `options`，因此完全绕过 `HoldForExternalCommit` 冻结分支（SetInvincible + SetActive(false)）、`ApplySharedMutators` 门控和跳过 Legacy 提交回调的门控。当前不可达：Mode G 三处调用点均传 defer=false 且带 options；Mode E/F 走 defer=true 但 options=null。若将来出现 defer=true + HoldForExternalCommit=true 组合，冻结语义会被静默丢失（Boss 直接激活、应用共享变异并走 Legacy 提交）。

**根因**: `ScheduleModeEFSpawnPostprocessAsync` 与 `ModeEFSpawnPostprocessJob` 在 Mode G options 契约（任务 #7）落地时未同步扩展，defer 队列收尾 `FinalizeModeEFSpawnPostprocessJob` 固定执行 SetActive(true) + ApplyToEnemy + onCommit。

**修复内容**:
- 修改文件: `Utilities/EnemySpawnCore.cs` —— `ModeEFSpawnPostprocessJob` 增加 `options` 字段；`ScheduleModeEFSpawnPostprocessAsync` 增加显式无默认值的 `EnemySpawnCoreOptions options` 参数（防止未来新增 defer 调用点静默漏传）并入队透传；`FinalizeModeEFSpawnPostprocessJob` 镜像同步路径三个门控：HoldForExternalCommit 冻结（SetInvincible + SetActive(false)）、ApplySharedMutators 门控变异、HoldForExternalCommit 跳过 Legacy 提交回调。`options == null` 时逐字保持原行为。
- 修改文件: `tests/ModeEFSpawnPostprocessSchedulerGuard.py` —— 新增 6 条断言锁住 options 字段、显式签名、入队透传与三个门控。
- 修改文件: `.qoder/repowiki/zh/content/游戏模式系统/标准 BossRush 模式/Boss 生成系统.md` —— 后处理队列小节补充 options 门控一致性说明。

**兼容性影响**: 无。私有方法签名变化仅影响文件内唯一调用点；存档、配置、TypeID、Harmony/反射、资源均不涉及。

**验证方法**:
1. 编译: 未执行（本机无游戏安装，缺 `Duckov_Data\Managed` DLL，`compile_official.bat` 无法运行）
2. Guard: `ModeEFSpawnPostprocessSchedulerGuard`、`EnemySpawnCoreObservableGuard`、`ModeGSpawnTransactionGuard`、`ManagedSpecialBossDeferredActivationGuard`、`ModeFRespawnObservableSpawnGuard`、`StandardBossRushSpecialBossMutatorGuard`、`ManagedBossSpawnOwnershipGuard` 全部 PASS
3. 人工 smoke: 不需要（现有调用组合行为不变）；若将来启用 defer+hold 组合需实机验证批量激活

**未验证/需人工**: Windows 编译未运行（无本机游戏），需在有游戏 DLL 的机器上跑一次 `compile_official.bat` 确认。改动仅用 C# 7.3 已有语法（字段、参数、if 门控），静态审查无语法风险。

---
### 2026-08-27 全项目 UI 收口与观感升级（第一步：程序化皮肤）

**状态**: fixed
**Finding**: owner 功能要求（全项目 UI 优化美化）
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 已确认；UI 皮肤两步走——先纯代码程序化圆角九宫格并预留图集注入接口，将来出美术图集时直接换上

**现象**: 全 Mod 44 个 UI 文件约 2.7 万行、20 个独立 Canvas，存在系统性问题：零圆角、零 `Image.Type.Sliced` 底图（"简陋感"根源）；两套遮罩色并存；6 套标题栏、4 套按钮工厂、4 套滚动列表各写各的；sortingOrder 分裂成 10~1001 与 28000~32000 两个孤岛，成就界面 `sortingOrder=10` 会被任何模态压住；3 个文件仍用 legacy `UI.Text` + 内置 Arial（渲染不了中文，界面上是方块）；`BossFilterUi` 的 `CanvasScaler` 加了却从未配置，4K 屏上面板缩成一小块。

**修复内容**:
- 新增 `Common/UI/BossRushUI.cs`：设计 token（`BossRushUIColors`）、Canvas 层级表（`BossRushUILayers`）、运行时程序化圆角九宫格（按半径缓存共享，1px 抗锯齿过渡带，`Sprite.Create` 带 border）、皮肤注入点（`BossRushUISkin`）、卡片/遮罩/Canvas 根工厂、面板打开动画。字体与模态输入租约仍转发 `ZombieModeUIHelper`，不重复实现也不搬动被 guard 钉死的原语。
- 打开动画不用官方 `Duckov.UI.Animations.ScaleFade`：它的 duration/scale 全是私有 `[SerializeField]`，运行时 `AddComponent` 得到的 HiddenScale 恰好等于正常缩放（动画不可见），且需要 `FadeElement` 驱动。改用自包含的 `BossRushUIOpenAnimation`（unscaledDeltaTime，模态会把 timeScale 置 0）。
- 圆角皮肤接到 `ZombieModeUIHelper.CreateButton` / `CreateModalSurface`，ZombieMode 与 ModeG 全线界面一次性受益；另外单独接了丧尸模式奖励卡、成就界面与条目、Boss 池面板、快递员确认框。
- 丧尸模式 HUD 三块裸文本补半透底板（`raycastTarget=false`，不挡游戏内点击）。
- 修 `BossFilterUi` 的 `CanvasScaler` 未配置（高分屏实际 bug）；成就界面 `sortingOrder` 10→2000、补 `matchWidthOrHeight`、配色从 Material Design 绿换成本 Mod 深色调 token；确认框 `sortingOrder` 10→3200；ModeF 悬赏雷达距离底板 alpha 0.14→0.55（亮场景几乎看不见）。
- legacy Arial 清零：成就解锁弹窗与 NPC 传送面板转 TMP + 游戏字体；F3 作弊菜单把 `Font` 贯穿了十来个辅助方法并混用 legacy `InputField`，全量 TMP 化超出本轮范围，改用 `BossRushUI.GetLegacyChineseFont()`（优先取 TMP 字体的 `sourceFontFile`，取不到回退 Arial，不构成回退）。成就/好感度界面补上从未设置的 `.font`。
- 新增 `tests/BossRushUISharedLibraryGuard.py`：锁住层级表严格递增、图集注入点、九宫格 border、`ResetStaticCaches` 销毁程序化贴图并挂在 OnDestroy 路径、字体走四级回退、编译清单登记、已迁移界面不得回退成裸数值或第二套遮罩色、源码不得再出现内置 Arial。
- 新增 `docs/制作教程/BossRushUI_图集规格.md`：素材清单（尺寸/border/命名）、注入方式、fail-open 约定与层级表。

**第二批（B2/B3 收尾）**:
- 变异词条 overlay 从 IMGUI 迁到 uGUI：`MutatorUI` 重写为 Canvas + EventTrigger 悬停，由 `Update` 的 `Tick()` 驱动（IMGUI 一帧会跑多次，不适合做对象管理），`OnGUI` 调用点移除，Canvas 挂进 OnDestroy 释放路径。这是全 Mod 唯一常驻可见的 IMGUI，此前不随 CanvasScaler 缩放、观感与其余界面割裂。`MutatorUiOverlaySuppressionGuard` 按 uGUI 形态重写，仍锁死同一条契约：抑制判定必须早于显示、抑制期间清悬停、InputManager 抛异常按抑制处理、不得退回 IMGUI。
- Canvas 层级表补齐并全量接管：新增 ModeG 三档（900/940/950）与独立模式档（ZombieMode 28000~30500、婚礼过场 32000），数值沿用既有实现，只消除魔法数、不改叠放次序；ZombieMode HUD/奖励/撤离/配装/现金/服务面板、成就弹窗、图片查看器、许愿池动画与宿主、婚礼过场、F3/传送面板全部改引用常量。
- 奖励卡改按类别着色：新增 `GetZombieModeRewardAccentColor`，12 个奖励类别各有强调色（契约与地图事件用警示色，它们带负面代价），取代所有卡片共用一个青色。奖励系统没有稀有度字段，按类别是唯一有真实数据支撑的分级。
- Steam 成就弹窗配色并入设计 token（原米色边框与深色调冲突）；ModeF 悬赏雷达距离底板从 2x2 纯白硬边换成共享圆角九宫格，顺带删掉不再有人用的 `GetModeFBountyRadarPanelSprite` 与其静态字段。
- 许愿池面板/内容卡/按钮接入圆角皮肤，按钮态改用共享 `ApplyButtonColors`（净减 4 行，文件回到冻结上限之下）。
- `NPCTeleportUI`、`F3DebugCheatMenuUi`、`ImageViewerUI` 的 CanvasScaler 统一走 `ConfigureCanvasScaler`；其中传送面板此前没设 `matchWidthOrHeight`，默认只按宽度缩放，超宽屏会溢出。

**有意未做**:
- 临时 NPC 服务面板的「4 列小格改宽行列表」：改成 2 列后 `ZombieModeStarterEquipmentAndNpcUiGuard` 与 `ZombieModeTemporaryNpcResponsiveUiGuard` 同时失败，两者都记录了同一条历史约束——列数变少会让内容变高，把靠后的头盔项推到关闭按钮上面。这是一次真实重叠 bug 的修复，本机无法实机复验是否已被 ScrollRect 兜住，按“不放宽 guard 掩盖行为变化”回退，只在代码里补注释说明列数为何锁死。
- F3 作弊菜单全量 TMP 化：`Font` 贯穿十余个辅助方法且混用 legacy `InputField`（其 `textComponent` 只接受 legacy `Text`），半迁移会直接编译失败，只做字体替换。

**兼容性影响**: 纯表现层。不改存档、配置、TypeID、本地化 key、Harmony/反射目标或装备工厂。程序化贴图带 `HideFlags.DontSave`，已在 `ModBehaviour` 的 OnDestroy 路径显式销毁。Wiki 阅读器（美术驱动 prefab）与地图选择界面（克隆官方 prefab）有意不接管。

**验证方法**:
1. Python guard：全量 `tests/*.py` 通过（含新增的 `BossRushUISharedLibraryGuard`），既有 UI 结构 guard（`ZombieModeUIHelperGraphicCompositionGuard`、`ZombieModeTemporaryNpcResponsiveUiGuard`、`ZombieModeHudTextAssignmentGuard`、`ZombieModeChoiceUiPauseAndLayoutGuard`、`ModeFTmpFontReuseGuard`、`StaticCacheLifecycleGuard`）全部保持绿。
2. 语法层校验：Roslyn CS1xxx 检查通过。

**未验证/需人工**: 同上一条——本机未安装游戏，**没有真正编译**，也**完全没有实机看过界面**。UI 改动的观感、布局是否被圆角底图挤压、HUD 底板在各分辨率下的位置、TMP 转换后的对齐与字号，都只能实机确认。建议优先看：奖励三选一面板、丧尸模式 HUD、成就界面、Boss 池设置（1080p 与 4K 各一次），以及成就解锁弹窗和 F3 菜单的中文是否不再是方块。
---
### 2026-08-27 自 c36e011 起改动的全面审查修复

**状态**: fixed
**Finding**: CR-2026-08-27-001（本轮审查，覆盖 c36e011..HEAD 与工作区未提交改动）
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 已确认；命火成长改比例制 +4%/杀；便携安全区清场有意不计入击杀；便携安全区改为战斗期替换主槽、准备期并存副槽

**现象**: 三项功能缺陷。1) Mode F 命火过载在真实数值下不可达：成长奖励是绝对 +1 HP，而封顶与充能基数是入场上限的 50%（约 425 HP），需约 850 杀才首次过载，实机日志一局仅约 24 杀，整套机制是死代码，本次改动净效果退化为纯回血削弱。2) 丧尸模式安全区的仇恨抑制会永久泄漏：弹出路径无条件按 `!SafeZoneStealthBroken`（恒 true）去仇恨，而释放路径被全局开关早退，玩家在区外时蹭到边界的丧尸/Boss 再也不会追击，且本次改动把 tick 从准备期扩到全阶段，战斗期即触发，构成卡波次风险。3) 玩家在安全区内用手雷或奖励弹道命中丧尸会误取消整个安全区，与注释承诺的“投掷不触发”相反。

**根因**: 1) 成长奖励与成长上限量纲不一致（绝对 HP vs 入场上限比例）。2) 物理禁入与仇恨抑制两条规则被耦合在同一次弹出调用里，抑制记账又只走全局开关。3) 上一次改动删除 `IsZombieModeDamageFromStealthBreakingWeapon` 后只剩 `isFromBuffOrEffect` 过滤，武器 tag 门控丢失。

**修复内容**:
- Mode F 命火：新增 `MODEF_MAX_HP_GROWTH_RATIO_NORMAL = 0.04f`，成长奖励改为入场上限的 4%（悬赏 ×印记数），与 +50% 封顶同量纲；约 13 杀顶满上限、每杀充 8 点命火，一局约 25 杀迎来首次过载。回血 30%/45%/60% 不变。
- Mode F 生命清理：`CleanupModeFPlayerMaxHealthGrowth()` 增加 `hadGrowth` 前置条件，只在本局确实授予过成长时才钳制当前生命，不再误伤其他来源的超额生命；`StartModeFRun()` 改调同一清理方法，修复上一局 `ExitModeF` 中途抛出时成长 Modifier 永久残留；`AddModifier` 失败时回滚 `TempMaxHealthGrowth` 与 Modifier 引用。
- 丧尸模式安全区改双槽：新增 `PortableSafeZone*` 副槽字段与 `AnyZombieModeSafeZoneActive`。战斗期部署替换主槽、波次结束随准备期清理消失并重新生成带商人的正常安全区（原「保留便携区导致整个准备期没有商人终端」一并消失）；准备期部署写入独立副槽，不带商人、不回收主槽绑定服务，与正常区并存到下一波开始。几何查询、弹出、清场、视觉刷新与 HUD 全部按双槽处理，弹出方向按敌人实际所在槽计算。
- 仇恨抑制泄漏：抑制判定提前到弹出之前并作为参数传入 `KeepZombieModeEnemiesOutsideSafeZone(bool)`，物理禁入不再擅自去仇恨；`SetZombieModeEnemyThreatSuppressed` 置位时同步记账 `SafeZoneThreatSuppressed`，保证任何来源的逐敌抑制都能被释放路径恢复。
- 安全区取消：恢复武器 tag 门控 `IsZombieModeSafeZoneCancellingWeapon`，只有枪械/近战直伤取消安全区，且两槽一并取消。
- 丧尸模式其他：掉落候选的 `GetComponent<Item>` 拾取检查由每帧改为 1 秒一次（波次强制清理不受节流）；Boss 解卡增加近身豁免 `BossStuckEngagedDistance = 6f`，避免举盾/telegraph/贴脸近战 Boss 被 12 秒阈值反复瞬移；背包废品回收改为移除成功后再记账，异常时不再白送净化点。
- 清理：移除恒为 false 的死状态 `SafeZoneStealthBroken` 及其全部死分支与本地化 key、死常量 `BossPreparationCountdownSeconds`、无调用点且返回可变静态数组的 `GetZombieModePreparationDurationOptions`；`protectedTags` 提为静态只读；prune 路径的 `record.Cleanup` 补具名 catch；Harasser 弹道缓存 `Shader.Find` 并修掉首帧从世界原点拉线；`compile_official.bat` 缩进回归 4 空格。
- 文件拆分（LargeFileBudgetGuard）：命火逻辑拆出 `ModeF/ModeFBloodfire.cs`，击杀奖励气泡文本拆出 `ModeF/ModeFUI_KillRewardBubble.cs`，两个新文件已登记 `compile_official.bat`。
- 文档：AGENTS.md TypeID 台账更新为 500001-500058 / 下一可用 500059；ID 表修正 Mode G 入场条件（可携带自己的装备）；`BossRush_PortableSafeZoneDevice_Desc` 改为引用配置常量，消除双重注入文案分叉；中英 Wiki、在线 Wiki 与 repowiki 同步命火数值与便携安全区新生命周期。

**兼容性影响**: 不改变存档 schema、配置 schema、TypeID、AssetBundle、Harmony/反射目标或装备工厂。删除的本地化 key `BossRush_ZombieMode_Hud_SafeZone_StealthBroken` 对应的状态已不可达，删除后无显示变化。新增副槽状态只存在于本局运行时。Mode F 局内成长与回血数值有调整。

**验证方法**:
1. Python guard：全量 `tests/*.py` 通过，仅剩两项与本轮无关的既有项——`DragonKingBossGunRocketSplitGuard`（在 c36e011 之前即为红，DragonKing 目录本轮未改动）与 `SmokeLogScan`（实机日志扫描，非结构 guard）。
2. 同步更新的 guard：`ModeFBloodfireOverloadGuard`（新增比例量纲断言）、`ModeFKillRewardBubbleNoListGuard`、`ZombieModeSafeZoneGuard`（双槽与取消契约）、`ZombieModeSafeZoneMarkerReuseGuard`、`ZombieModeMarkerOwnerFallbackCacheGuard`、`ZombieModeRunOnlyMarkerFallbackCacheGuard`、`ZombieModePacingTuningGuard`、`ZombieModeStateModelGuard`、`ZombieModeRewardCatalogGuard`、`ZombieModeRewardServiceAtomicityGuard`。
3. 在线 Wiki：`node scripts/sync-content.mjs` 通过，中英各 102 篇。

**未验证/需人工**: **本机未安装《鸭科夫》**（Steam 库中无 Escape from Duckov，缺 `Duckov_Data\Managed`），`compile_official.bat` 无法运行，因此本轮没有真正编译。仅用 Roslyn 做了 CS1xxx 语法层校验，语义错误（签名不匹配、类型错误）无法覆盖，**必须在有游戏的机器上补一次正式编译**。实机 smoke 待验证：Mode F 约 25 杀触发首次过载与三条退出清理路径；丧尸模式战斗期部署便携区后波次结束能拿回带商人的正常安全区、准备期部署双区并存且商人保留、玩家在区外时被弹出的丧尸仍会追击、手雷命中丧尸不取消安全区、近战 Boss 12 秒内不被误传送。
---
### 2026-08-26 Mode F 退出钳制与命火过载后期曲线

**状态**: fixed
**Finding**: CR-2026-08-26-001
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 已确认；最大生命成长封顶 +50%，溢出转命火，满值进入带烧伤和双倍失血的短时过载；过载开始使用玩家头顶气泡提醒

**现象**: 玩家在 Mode F 内通过击杀提高最大生命并回满后，退出模式仍可能保留高于正常上限的当前生命；同时局内最大生命与按当前上限计算的 50%/75% 回血均无封顶，后期固定伤害和持续失血占比不断下降，玩家逐渐成为普通敌人难以威胁的血牛。

**根因**: `ExitModeF()` 只从 `MaxHealth` Stat 移除 `modeFMaxHealthModifier`，没有同步限制 `Health.CurrentHealth`。局内 `TempMaxHealthGrowth` 也没有上限，击杀回血按不断增长的 `health.MaxHealth` 计算，而模式失血固定按入场生命计算，形成越打越安全的单向正反馈。

**修复内容**:
- 修改 `ModeF/ModeFPhases.cs`：新增 `CleanupModeFPlayerMaxHealthGrowth()`，通过 Modifier 原始 Stat 目标撤销本局成长，再读取恢复后的真实上限并调用 `Health.SetHealth()` 压低超额当前生命；正常撤离、死亡和场景切换继续统一经过 `ExitModeF()`。
- 玩家本局最大生命成长封顶于入场最大生命的 +50%；成长奖励本身也按入场上限比例结算——普通击杀 +4%、悬赏击杀 +4%×印记数（至少 1 倍），约 13 次普通击杀顶满上限。回血按入场生命结算：普通 30%，悬赏 45%，每层额外印记 +5%、最高 60%，不再随成长后的当前上限继续放大。
- 超出生命成长上限的奖励按同一上限容量换算为 0～100 命火（每次普通击杀约 +8，顶满上限后再约 13 杀充满，一局约 25 杀迎来首次过载）。满命火进入 15 秒过载：枪械/近战 +40%、移速 +15%、Mode F 失血 ×2，并复用官方 `GameplayDataSettings.Buffs.Burn` 向玩家施加烧伤；悬赏击杀续时 3 秒、剩余时间最高 24 秒，自然结束保留 25 命火。
- 过载开始通过现有玩家头顶气泡显示 4 秒高优先级提醒；普通命火增长、悬赏续时和每 15 秒阶段广播同步显示命火状态。
- `ExitModeF()` 在生命钳制前调用 `EndModeFBloodfireOverload(false)`，撤销枪械、近战和移速 Modifier 并清空命火状态。
- 新增 `tests/ModeFMaxHealthCleanupGuard.py`：锁定“撤销 Modifier → 读取恢复后上限 → 钳制当前生命”的顺序，并要求 `ExitModeF()` 使用集中清理方法。
- 新增 `tests/ModeFBloodfireOverloadGuard.py`：锁定经 owner 确认的成长、回血、充能、过载、Burn、气泡和退出清理契约。
- 同步中英文游戏内 Wiki、在线 Wiki、Mode F repowiki 知识卡与专项玩法文档。

**兼容性影响**: 不改变存档、配置 schema、TypeID、本地化 key、资源、Harmony/反射目标或装备工厂；调整 Mode F 局内回血与生命成长数值，并新增只存在于本局的命火状态和临时 Stat Modifier。烧伤复用官方 Buff，不新增 Buff ID 或 AssetBundle。

**验证方法**:
1. Windows 正式编译：`compile_official.bat` 通过，已生成 `Build/BossRush.dll` 并部署到游戏 Mod 目录。
2. 定向 Guard：`ModeFBloodfireOverloadGuard.py`、`ModeFMaxHealthCleanupGuard.py`、`ModeFKillRewardBubbleNoListGuard.py` 通过。
3. 全量验证：15/15 个 `ModeF*Guard.py` 通过；`ArchitectureStructureGuard.py`、`OfficialCompileListFileExistenceGuard.py`、`EmptyCatchGuard.py`、`BossWikiGuideContentGuard.py` 通过。
4. 在线 Wiki：`npm run build` 通过，同步中英文各 102 篇并由 VitePress 成功构建 204 篇页面。
5. 静态检查：目标文件 `git diff --check` 通过，仅有仓库既有 LF/CRLF 提示。

**未验证/需人工**: 需实机验证 150% 总生命上限、普通/多印记悬赏回血、命火充能速度、过载 Burn/火力/移速/双倍失血、悬赏续时上限、头顶气泡时长，以及成功撤离、玩家死亡和切图兜底三条退出清理路径。

---
### 2026-08-25 丧尸模式波次体验、Boss 解卡、骚扰弹道、休息时间与安全区装置

**状态**: fixed
**Finding**: 玩家反馈 / owner 功能要求
**兼容分类**: COMPAT / OPERATIONAL
**版本/Commit**: 85e91e2
**Owner decision**: 已确认；普通散落物在下一波正式开始时清理，奖励界面默认折叠显示休息时长，修改范围 15～300 秒且跨波次沿用

**现象**: 波次掉落长期堆积会造成卡顿；Boss 偶尔卡在异常位置导致波次不能结束；第 8～10 波骚扰者攻击缺少可读弹道且会在旧目标点补爆；玩家缺少高概率的背包废品兑换途径、可主动部署的安全区，以及每波可选的休息时长。

**根因**: 掉落回收只按老化时间和波次年龄处理；Boss 解卡同时依赖“不可达”和“未受击”，持续射击会不断推迟恢复；骚扰者超时/抵达旧目标点仍可结算爆炸且视觉反馈不足；奖励目录和 UI 尚未提供对应入口。

**修复内容**:
- 下一波 `StartZombieModeWave()` 启动时强制清理普通散落物，保留 Boss 奖励箱，并排除已经进入背包或装备槽的物品。
- Boss 连续 12 秒没有产生有效位移时，在玩家附近的可达 NavMesh 点恢复位置，同时重置速度、刷新仇恨；受击不再阻止解卡。
- 骚扰者改为带轨迹、线条和发光反馈的实体投射物；只有实际命中才造成伤害和减速区，超时或玩家躲开旧目标点时直接消失。
- 新增高权重“背包废品回收”奖励，仅回收低品质普通叶子物品，保护武器、装备、弹药、医疗、食物、钥匙、任务/特殊物品及带内容容器。
- 新增便携安全区装置 TypeID `500058`、运行时注册和一次性使用行为；Unity 构建生成 `portable_safe_zone_device` AssetBundle，构建/测试脚本自动部署。
- 安全区部署完成时立即清除范围内普通、特殊和精英丧尸；Boss 不直接击杀，而是移到排斥边界外，且清除不计击杀、不掉落、不发放净化点。
- 安全区创建后的持续 Tick 与普通丧尸/Boss 生成回调共同执行禁入检查；安全区未取消时，生成在安全区或 1.5 米排斥缓冲内的单位会立即移出，恢复锚点记录移出后的实际位置。
- 便携安全区现在跨奖励界面和下一次准备期保留，仅在下一波正式开始时清理；玩家在当前安全区内直接伤害丧尸会立即销毁安全区视觉、地图 POI 与绑定终端，战斗和准备阶段统一适用。
- 丧尸模式弹药数量统一翻倍：枪械开局 2000 发；枪械+弹药奖励 120 发；弹药补给 240 发；契约枪械弹药 120 发；战场弹药雨 120/180 发；商人按当前枪械口径每次购买 200 发。
- 开局医疗品复核确认：近战和枪械流派的保底回血品、额外医疗品都复用 `IsZombieModeRewardCandidateAllowed`，要求 `Healing`/医疗标签并排除医疗黑名单和 `AdvancedDebuffMode`，没有发现绕过过滤的发放路径；新增守卫锁定这条契约。
- 奖励 UI 默认只显示当前休息时长与“修改/Edit”；展开后可选 15～300 秒、15 秒步进，默认 45 秒，修改值在本局后续波次沿用。
- 同步中英文 WikiContent、在线 Wiki、`.qoder/repowiki/`、TypeID/资源契约文档、本地化和静态守卫。

**兼容性影响**: 不改变旧存档、配置 schema、Harmony/反射目标或已有 TypeID；新增 TypeID `500058` 和可选 AssetBundle。缺少 Bundle 时保留运行时兜底物品注册，但显示资源会降级。

**验证方法**:
1. Windows 正式编译：`compile_official.bat` 通过并部署 `Build/BossRush.dll`。
2. Unity AssetBundle：`portable_safe_zone_device` 为 57,201 字节，`UnityFS` 文件头；工作区与游戏部署目录 SHA-256 均为 `4766CC770FE518183E4FEC476975C0362F7AB13AF5DD4DED40B12E7BC89B251A`。
3. 定向 Guard：波次清理/Boss 刷新、骚扰者投射物、节奏、奖励、安全区、物品资源和 Windows 路径守卫全部通过。
4. 全量 Guard：435/437 通过；剩余 `DragonKingBossGunRocketSplitGuard.py` 是未触及的龙皇炮既有问题，`SmokeLogScan.py` 因最新游戏日志早于本次 DLL 部署而失败。
5. 密钥扫描：工作区未发现 `sk-...` 形式的密钥落盘。

**未验证/需人工**: 需进游戏复测掉落清理时机、Boss 多地形解卡、第 8～10 波骚扰弹道与闪避、废品过滤与点数、奖励 UI 多分辨率/中英文布局、便携安全区跨奖励/准备保留、区内攻击立即取消、道具消耗/地图标记和下一波清理时机，以及连续多波休息时间继承。

---
### 2026-08-11 Mode E 抽奖价格改回中位数

**状态**: fixed
**Finding**: owner 决策反馈
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 已确认；抽奖价格改用该分类全部可抽商品价格的中位数，不再用算术平均

**现象**: owner 上一轮要求抽奖价 = 全店平均价；本轮反馈平均价被高价装备拉高，更希望用中位数贴近"中档商品"的价格。

**根因**: `BuildModeELotteryPoolState()` 上一轮实现为算术平均（`(totalPrice + pricedItemCount - 1L) / pricedItemCount`），对偏态分布（少量高价装备 + 大量低价消耗品）会偏离典型商品价。

**修复内容**:
- `ModeE/ModeELotteryAndHiring.cs`: `BuildModeELotteryPoolState()` 改为收集所有可抽商品价格到 `List<int> prices`，`prices.Sort()` 后取 `prices[prices.Count / 2]`（偶数项取上中位数）作为抽奖价。
- `tests/ModeELotteryGuard.py`: 反转不变式——要求 `prices.Sort()`、`medianPrice`，禁止 `averagePrice` 与算术平均除法。

**兼容性影响**: 不改变存档、配置、TypeID、商店商品池或 Harmony 目标；只调整 Mode E 局内抽奖价算法。

**验证方法**:
1. Guard: `python tests/ModeELotteryGuard.py` 通过。
2. 编译: 需 Windows `compile_official.bat`。

**未验证/需人工**: 需实机确认各分类抽奖按钮价格是否贴近该分类中档商品价格。

---
### 2026-08-11 Mode E 子弹商店整组贝壳价上调

**状态**: fixed
**Finding**: owner 实机截图反馈 / 官方 `StockShop.ConvertPrice`、`Item.GetTotalRawValue` 源码复核
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 已确认；子弹商店显示的价格要像“一组”的价格，不能接近一发的价格

**现象**: 子弹商店整组（如 320 发）只显示 1~21 贝壳，owner 认为仍是一发子弹的价钱。

**根因**: 整组计价链路本身无 bug——`NormalizeModeEShellStackForShop()` 在计价前把样品补到 `MaxStackCount`，官方 `GetTotalRawValue()` 对可堆叠物品乘 `StackCount`，截图中 11/15/21 贝壳的中高级弹不可能是单发价（单发现金值不可能超过 25000）。真正原因是子弹商店 `priceFactor = 1.0`（其余分类 ×10）且单发现金值仅几到几百，导致整组折算后只有 1~2 贝壳，体感等同单发价。

**修复内容**:
- `ModeE/ModeEMerchantSupportClasses.cs`: 新增 `MODE_E_SHELL_BULLET_STACK_PRICE_FACTOR = 5L`，`TryCalculateModeEShellPrice()` 对 `ModeE_Bullet` 商店且已补满整组的样品，在 `ConvertPrice` 现金结果上乘该系数再折算贝壳；新增 `IsModeEBulletStackShop()` 统一子弹商店判定。
- `tests/ModeEShellPriceNormalizationGuard.py`: 锁定溢价系数常量、辅助方法与乘算调用。
- 子弹抽奖价格（全店平均）随单组价格同步上调约 5 倍；卖出走原版现金路径，不产生套利。

**兼容性影响**: 不改变存档、配置、TypeID、商店商品池、Mode F 现金价格或 Harmony 目标；只调整 Mode E 局内子弹商店的贝壳价格水平。

**验证方法**:
1. Guard: `python tests/ModeEShellPriceNormalizationGuard.py`、`python tests/ModeELotteryGuard.py` 通过。
2. 编译: 需 Windows `compile_official.bat`。

**未验证/需人工**: 需实机确认子弹整组价格（基础弹约 5 贝壳/组，高级穿甲约 55~105 贝壳/组）与子弹抽奖均价是否合适。

---
### 2026-08-10 Mode E 商品与抽奖贝壳价格再平衡

**状态**: fixed
**Finding**: 玩家实机反馈 / 官方 `StockShop.ConvertPrice` 与 `Item.GetTotalRawValue` 契约复核
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 已确认；降低固定商品的贝壳价格，抽奖使用当前分类全部可抽商品的平均价，整组商品按整组计价和交付

**现象**: 上一轮将现金折算单位从 `10000` 调到 `2000` 后，高价装备的贝壳成本偏高；抽奖价格仍取分类价格中位数，未满足全店平均价语义；整组商品需要继续确保显示、扣费与到手数量使用同一组数。

**根因**: 固定商品仍沿用偏激进的 `/2000` 向上取整；`BuildModeELotteryPoolState()` 对价格排序后取中位项；整组语义依赖计价和交付前共同调用 `NormalizeModeEShellStackForShop()`，需要由守卫持续锁定。

**修复内容**:
- Mode E 固定商品改为按原版商店现金基准价 `/5000` 向上取整，最低 1 贝壳；介于最初 `/10000` 和上一轮 `/2000` 之间。
- 抽奖池只纳入已有有效贝壳价的可见商品，对分类内每个可抽商品价格求算术平均并向上取整，不再取中位数。
- 保留 Bullet 商店在计价、UI 样品和实际交付前补到 `MaxStackCount` 的统一路径；原版总价值会乘 `StackCount`，因此价格与到手数量均为整组口径。
- 更新 `ModeEShellPriceNormalizationGuard.py` 与 `ModeELotteryGuard.py`，锁定折算单位、整组计价/交付和平均价算法。

**兼容性影响**: 不改变存档、配置、TypeID、商店商品池、Boss 贝壳奖励、Mode F 现金商店或 Harmony 目标；只调整 Mode E 局内贝壳价格和抽奖成本。

**验证方法**:
1. Windows 编译: `cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过，并部署 `Build/BossRush.dll`。
2. Guard: 全部 28 个 `ModeE*.py` 守卫通过。
3. 静态检查: 本次相关文件 `git diff --check` 通过，仅有仓库既有 LF/CRLF 转换提示。

**未验证/需人工**: 需实机检查不同分类的价格分布、子弹整组显示/扣费/到手数量，以及抽奖按钮价格与该分类手工平均值一致。

---
### 2026-08-10 Mode E 贝壳折算单位再下调至 2500

**状态**: fixed
**Finding**: owner 实机反馈
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 已确认；贝壳很容易刷到几百个，/5000 下商店整体太便宜，需要再上调价格。

**现象**: owner 反馈贝壳在局内容易积累到几百个，/5000 折算下绝大多数商品只要 1~30 贝壳，购买缺乏重量感。

**根因**: 上一轮将 `MODE_E_SHELL_CASH_UNIT` 定为 `5000L` 后，价格回归偏便宜的一端；贝壳奖励基准（500 血量 = 10 贝壳，每翻倍 +3）不变，导致中后期几百贝壳可大量扫货。

**修复内容**:
- `ModeE/ModeEMerchantSupportClasses.cs`: `MODE_E_SHELL_CASH_UNIT` 从 `5000L` 降到 `2500L`，固定商品贝壳价格整体提高约 2 倍。
- `tests/ModeEShellPriceNormalizationGuard.py`: 同步锁定值到 `2500L`。
- 抽奖价格（全店可抽商品算术平均）和子弹整组计价/交付路径不变，价格随折算单位自然上调。

**兼容性影响**: 不改变存档、配置、TypeID、商店商品池、Boss 贝壳奖励或 Harmony 目标；只调整 Mode E 局内贝壳价格水平。

**验证方法**:
1. Guard: `python tests/ModeEShellPriceNormalizationGuard.py` 通过。
2. 编译: 需 Windows `compile_official.bat`。

**未验证/需人工**: 需实机确认各分类价格体感、抽奖价格与子弹整组价格是否合适；若高价装备仍偏贵或低价消耗品偏贵，可进一步微调。

---
### 2026-08-10 Mode E 抽奖按钮改为刷新标题下方组合

**状态**: fixed
**Finding**: 新实机截图 / 官方 `StockShopView` 源码复核 / owner 明确布局要求
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；刷新提示在上，抽奖按钮居中放在其下方，并放大按钮与文字

**现象**: 抽奖按钮虽然已经避开刷新标题，但仍位于标题右侧；按钮和文字偏小，不符合新的目标布局。

**根因**: 官方 `StockShopView` 只通过私有 `refreshCountDown` 更新倒计时数字，"下次刷新"前缀属于预制体静态文本；原自定义按钮逻辑仍按左右并排布局处理。

**修复内容**:
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：以完整可见刷新标题字形中心为锚点，将抽奖按钮固定放在标题下方，取消左右排列和窄屏横向回退。
- 抽奖按钮尺寸调整为 `176x42`，按钮文字字号调整为 `18`，自动适配范围 `14-18`。
- 更新 `tests/ModeELotteryGuard.py`，锁定上下排列、尺寸和放大字体契约。

**兼容性影响**: 不改变存档、配置、TypeID、资源、反射字段或交易行为；仅改变 Mode E 商店抽奖按钮的位置和视觉尺寸。

**验证方法**:
1. Windows 正式编译：`compile_official.bat` 通过并部署 `Build/BossRush.dll`。
2. Guard: Mode E/Mutator 相关守卫全部通过。
3. 静态检查：`git diff --check` 无 whitespace 错误。

**未验证/需人工**: 需重启游戏确认当前分辨率和窄分辨率下标题在上、按钮在下且不遮挡商店第一行；英文文本和重复打开商店也需确认。

---
### 2026-08-10 Mode E 抽奖按钮继续遮挡刷新标题字形

**状态**: fixed
**Finding**: 新实机截图 / `Player.log` 商店打开记录 / TMP 布局静态复核
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；按钮必须避让完整可见的刷新标题

**现象**: 按刷新标题父级 RectTransform 左边界定位后，抽奖按钮仍压住"下次刷新"的第一个字。

**根因**: TMP 文本的实际字形边界可向其 RectTransform 外溢出；父级矩形边缘并不等于玩家看到的标题起点。重新定位时若把抽奖按钮自身纳入文本扫描，还会造成重复刷新后的自引用漂移。

**修复内容**:
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：扫描商店根节点内与倒计时处于同一水平带的 TMP 文本，调用 `ForceMeshUpdate` 后合并实际 `textBounds`，以完整可见标题范围定位按钮。
- 排除 `ModeELotteryButton` 自身及其子节点，避免布局刷新时按钮参与边界计算。
- 窄屏回退和纵向对齐同样使用合并后的可见标题范围；无有效字形时保留原 RectTransform 兜底。
- 更新 `tests/ModeELotteryGuard.py`，锁定字形边界、按钮排除和布局定位契约。

**兼容性影响**: 不改变存档、配置、TypeID、资源、反射字段或交易行为；仅调整 Mode E 商店按钮布局。

**验证方法**:
1. Windows 正式编译：`compile_official.bat` 通过并部署 `Build/BossRush.dll`。
2. Guard: Mode E/Mutator 相关守卫全部通过。
3. 静态检查：`git diff --check` 无 whitespace 错误。

**未验证/需人工**: 需重启游戏，在中文与英文、当前截图分辨率及窄分辨率下确认按钮与"下次刷新"标题之间保留间距，且不发生重复打开商店后的漂移。

---
### 2026-08-10 变异词条 UI 避让模态界面

**状态**: fixed
**Finding**: 玩家反馈 / 官方 `InputManager.InputActived` 与仓库 UI 输入租约静态复核
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；模态界面打开期间隐藏，关闭后恢复

**现象**: 打开背包或其他会接管游戏输入的 UI 时，左侧变异词条列表仍通过 `OnGUI` 绘制，遮挡界面内容。

**根因**: `MutatorUI.DrawGUI()` 只按模式和缓存状态绘制，没有跟随官方 View、暂停状态或自定义 UI 的输入门控。

**修复内容**:
- 修改 `Integration/Mutators/MutatorUI.cs`：绘制前检查 `InputManager.InputActived` 与 `Time.timeScale`；输入被 UI 接管或游戏暂停时跳过当前帧，并清理悬停详情矩形，关闭 UI 后缓存自动恢复。
- 新增 `tests/MutatorUiOverlaySuppressionGuard.py`：锁定门控位于 IMGUI 实际绘制前，并要求抑制期间清理 hover 状态。

**兼容性影响**: 不改变变异词条、模式状态、存档或配置；只改变模态 UI/暂停期间的 HUD 可见性。

**验证方法**:
1. Windows 正式编译：`compile_official.bat` 通过；最终构建时游戏已启动，目标 DLL 被占用，未自动部署。
2. Guard: `MutatorUiOverlaySuppressionGuard.py`、变异语义/Mode E 相关守卫通过。

**未验证/需人工**: 退出游戏后需部署最终 `Build/BossRush.dll`；随后分别打开背包、商店、地图、暂停、Wiki/成就、图片查看器，确认词条 UI 隐藏，关闭后确认列表与悬停详情恢复。

---
### 2026-08-10 Mode E 抽奖按钮避让完整刷新标题

**状态**: fixed
**Finding**: 玩家实机截图反馈 / `StockShopView.refreshCountDown` 原版字段与当前布局静态复核
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；保持抽奖按钮位于刷新标题左侧

**现象**: 抽奖按钮虽然已移到商店根节点，但仍插在"下次刷新"前缀与倒计时数字之间，遮挡标题并表现为横向错位。

**根因**: `PositionModeELotteryButtonOutsideShop()` 只用 `refreshCountDown` 数字文本自身的左边界定位按钮，没有把同一父容器中的本地化刷新前缀算入占用范围。

**修复内容**:
- 按 `refreshCountDown` 父级刷新标题容器的完整边界计算按钮右侧位置，同时继续以倒计时文本中心做纵向对齐。
- 按完整刷新标题边界执行窄屏下移回退，按钮仍挂在无遮罩的 `StockShopView` 根节点，并保持 132x32 单行尺寸。
- 更新 `ModeELotteryGuard.py`，锁定完整刷新标题避让规则。

**兼容性影响**: 不改变存档、配置、TypeID、资源、反射字段或交易行为；只调整 Mode E 商店抽奖按钮布局。

**验证方法**:
1. Windows 正式编译：`compile_official.bat` 通过；最终构建时游戏已启动，目标 DLL 被占用，未自动部署。
2. Guard: `ModeELotteryGuard.py`、`ModeEShellHarmonyUiContractGuard.py` 及 Mode E/Mutator 相关守卫通过。
3. 静态检查: 相关文件 `git diff --check` 无 whitespace error，仅有仓库既有 LF/CRLF 转换提示。

**未验证/需人工**: 退出游戏后需部署最终 `Build/BossRush.dll`；随后在当前截图分辨率及窄分辨率下确认按钮完整位于"下次刷新"左侧，且窄屏回退不遮挡商店面板。

---
### 2026-08-07 动态物品偶发整批变成问号

**状态**: fixed
**Finding**: 多名玩家反馈 / `鸭科夫源码` 与当前 Managed DLL 反编译复核
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 未提交
**Owner decision**: 完全移除两个重刷道具的人口上限，让玩家自由刷 Boss；仅保留单个重刷任务互斥和 Mode E 会话安全
**现象**: 修复加载菜单卡顿、把内容注册移入延迟 bootstrap 后，部分玩家完整启动或进入基地时会看到 BossRush 物品变成白底问号；出现频率受官方存档恢复与 Mod 延迟注册的先后顺序影响。
**根因**: 官方 `SavedInventory.Start()` 经 `InventoryData.LoadIntoInventory()` / `ItemTreeData.InstantiateAsync()` 立即按 TypeID 实例化存档物品，找不到动态 prefab 时会创建 `FallbackItem_<TypeID>`。现有 2026-07-02 按需注册只依赖程序集级 `Harmony.PatchAll()` 和逐物品入口，没有验证关键补丁是否真的挂载，也没有在整批库存实例化前建立注册屏障；此外 `500032/500033` 被错误映射到只包含 `500027/500028` 的 `respawn_items` bundle。
**修复内容**:
- 修改 `Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs`：新增 `InventoryData.LoadIntoInventory` 批量注册屏障，先扫描所有根物品和嵌套物品 TypeID，再允许官方异步实例化；新增 `InstantiateFallbackItem` 最后兜底，能注册 BossRush prefab 时直接返回真实实例。
- 修改 `Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs`、`Utilities/AlwaysOnRuntimeHooks.cs`：启动后逐项验证 8 个动态物品关键 Harmony prefix；程序集全量 Patch 失败或漏装时单独补装关键补丁，并输出 `verified/total` 日志。
- 修改 `Integration/BossRushDynamicItemRegistry.cs`：提供绕过 prefix 的已注册 prefab 读取；将 `500027/500028` 保留在 `respawn_items`，把 `500032` 改到 `bosscall_whistle`、`500033` 改到 `bloodhunt_beacon`。
- 修改 `Integration/BossRushDynamicItemRegistry.cs`、`Utilities/AlwaysOnRuntimeHooks.cs`：若关键补丁补装后仍未达到 `8/8`，立即同步注册注册表中的全部已发布 TypeID；仅异常分支承担全量加载开销，避免补丁失效时继续生成问号占位物品。
- 修改 `tests/BossRushDynamicItemRegistryGuard.py`：锁定库存批量屏障、fallback 兜底、启动自检调用和三组正确 bundle 映射。
**兼容性影响**: 不改 TypeID、存档 key、配置 schema、AssetBundle 文件名或正常延迟加载策略；关键补丁完整时只在官方准备恢复 BossRush 存档物品时同步加载该存档实际需要的 bundle，只有补丁验证失败的异常环境会在启动时全量同步注册。
**验证方法**:
1. 当前 Managed DLL 反编译确认 `InventoryData.LoadIntoInventory` 会逐项调用 `ItemTreeData.InstantiateAsync`，缺失 prefab 最终进入 `InstantiateFallbackItem`。
2. Windows 编译: `compile_official.bat` 通过并部署 `Build/BossRush.dll`。
3. Guard: `BossRushDynamicItemRegistryGuard.py`、`DeferredIntegrationBootstrapGuard.py`、`ContentRegistryGuard.py`、`DragonBossRewardContentPreloadGuard.py`、`PhantomWitchScytheRewardBundleGuard.py`、Harmony 相关守卫通过。
4. 静态检查: 本次相关文件 `git diff --check` 通过。
5. 全量 400 个 Python guard 中 392 个通过；其余 8 个是本次改动前已存在、来自工作区其他未提交改动或需要实机刷新日志的无关失败：`DragonKingBossGunRocketSplitGuard.py`、`EmptyCatchGuard.py`、`LargeFileBudgetGuard.py`、`ModBehaviourInstanceClassificationGuard.py`、`SmokeLogScan.py`、`ZombieModeNormalZombieCapAndAggroGuard.py`、`ZombieModePacingTuningGuard.py`、`ZombieModeSpawnSelectionSqrGuard.py`。
6. 游戏内 smoke: `Player.log` 显示 `关键补丁验证完成: 8/8, 补装=0`，未出现 `FallbackItem_5000xx` 或动态注册失败；`500032/500033` 分别从 `bosscall_whistle` / `bloodhunt_beacon` 成功加载，ItemFactory 36 件、EquipmentFactory 19 件均完成注册。
**未验证/需人工**: 仍建议在不同玩家环境连续冷启动复测；缺失或损坏的 Workshop 资源文件不属于代码可恢复范围。日志中的 `FallbackItem_-1` 来自官方 `Duckov.MiniGames.GamingConsole.Load()`，不属于 BossRush TypeID 注册链。
**失败尝试**: 首轮编译中 `Patches` 在补丁命名空间内被解析成命名空间，已将 Harmony patch-info 类型改为 `HarmonyLib.Patches` 后重新编译通过。

---
### 2026-08-07 Mode E 分类商店首开卡顿与贝壳价格过低

**状态**: fixed
**Finding**: 玩家实机反馈 / `Player.log` 与官方 `StockShopView.Setup` 反编译复核
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 本提交
**Owner decision**: 已确认；提高 Mode E 贝壳商品价格并修复商店首开卡顿
**现象**: Mode E 商人分类商店首次打开前会明显卡顿；首轮分帧创建上线后，玩家实测从饰品切到护甲仍会出现数秒 0 帧停顿。当前 `/10000` 价格量化还使玩家击杀少量 Boss 后即可快速购买大量商品。
**根因**: 官方 `StockShopView.Setup()` 会在主线程一次遍历分类全部条目并同步创建、绑定 UI；当前最大分类有 456 件商品。首轮修复只分摊了创建，最新 `Player.log` 显示饰品已成功分帧创建 `24 + 432` 个条目，随后打开护甲时才卡顿；官方 `PrefabPool.ReleaseAll()` 对 active list 逐个调用 `Remove`，再逐个禁用和重挂 456 个 UI，切店释放仍形成二次方与布局尖峰。线性回收上线后的实测进一步显示 Setup 已降至 `1-3ms`、`ShowUI` 约 `37-72ms`，但完整打开仍偶发 `153-961ms`；剩余时间位于 Attach 阶段，其全量刷新使用 `GetComponentsInChildren(..., true)`，把对象池中已隐藏的旧条目也重复刷新。Mode E 贝壳奖励常见为晋升 Boss 每只约 7–20，首个正奖励额外 10，而大量低中价商品经 `/10000` 向上取整后只需少量贝壳。
**修复内容**:
- 修改 `ModeE/ModeEHarmonyPatch.cs`、`ModeE/ModeEMerchantSupportClasses.cs`：Mode E 首开只同步建立 24 个商品条目，其余每帧追加 12 个；切店时先关闭商品内容根节点、清空 active 索引并线性归还旧条目，使原版 `ReleaseAll()` 不再重复做二次方删除，首批绑定后一次恢复布局；切换分类、关闭 UI、模式失效和 runtime reset 会让旧任务失效，反射契约漂移时回退完整原版 Setup；同分类完整加载后继续复用现有 UI。
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：新增 `[Profile] shop setup sync` 与 `shop open sync` 日志，分别记录分类条目数、回收数、回收/Setup 耗时，以及样本就绪检查、`ShowUI` 和完整交互同步耗时，供实机确认剩余尖峰。
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：Attach、余额和交易门控刷新不再遍历任何商品行；价格完成事件只刷新当前活动层级中匹配的商品行，排除对象池内隐藏条目；打开日志新增 `attachMs`。复用的一键卖出按钮尺寸改为每次从原整理按钮派生，避免重复打开后宽度累加。
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：现金价格量化单位由 `10000` 调整为 `2000`，即非取整边界商品约提高到原来的 5 倍贝壳价格，保留向上取整、子弹满堆和最小 1 贝壳规则。
- 修改 `tests/ModeEMerchantOpenReuseGuard.py`、`tests/ModeEShellPriceNormalizationGuard.py`：锁定 Mode E 作用域、首批/逐帧预算、线性回收、内容根节点恢复、完整 entries 恢复、生命周期失效和新价格单位。
**兼容性影响**: 不改变存档、配置、TypeID、商品池、Boss 奖励、出售现金、模式 F 或普通官方商店；沿用现有精确 `StockShopView.Setup(StockShop)` Harmony 目标，并新增对其私有 `_entryPool`、`PrefabPool.activeObjects` 和 `StockShopItemEntry.Setup` 的可选反射使用，字段类型或方法契约不匹配时不接管对应优化并回退原版行为。
**验证方法**:
1. Windows 编译: `compile_official.bat` 通过并部署 `Build/BossRush.dll`。
2. Guard: `ModeEMerchantOpenReuseGuard.py`、`ModeEShellPriceNormalizationGuard.py`、`ModeEShellHarmonyUiContractGuard.py` 通过。
3. 最终实机日志: 所有分类首次打开总耗时为 `37-65ms`、`attachMs=1-4ms`；饰品 456 条完整加载后切护甲时回收 `456` 条仅 `3ms`，`ShowUI=64ms`、`attachMs=1ms`、总耗时 `65ms`，随后分帧加载 `24+31` 条且 `failed=0`。日志未出现 Mode E 商店异常、分帧失败或对象池回收失败。
4. 静态检查: 本次 Mode E 作用域 `git diff --check` 通过。
**未验证/需人工**: 性能与分类切换已完成实机验证；仍需按前 10 次击杀的实际购买选择观察 5 倍价格下的经济节奏，必要时再做小幅数值微调。
**失败尝试**: 第一版只分帧创建，没有处理下一次切店时原版同步释放全部活动条目，最新实测仍卡；首次误把量化单位提高到 `50000`，复核公式后确认这会降低价格，未保留该改动；Harmony `__state` 初版公开方法触发 C# 可见性编译错误，已将该补丁方法收为私有静态并重新验证。

---
### 2026-08-06 丧尸模式变异僵尸中英文图鉴与源码一致性守卫

**状态**: fixed
**Finding**: `2026-08-03_玩家反馈保守优化方案复审.md` 第 12.1 节
**兼容分类**: SAFE
**版本/Commit**: `224c35d`
**Owner decision**: 不需要；纯文档与开发期静态导出，不修改战斗运行时
**现象**: 丧尸模式 Wiki 只有 Special/Elite 能力简表，未覆盖 `OfficialExploder`，也没有登记确定性的目标色、安全视觉体型、污染倍率、完整技能参数或精英颜色优先级。
**根因**: Wiki 页面早于变异视觉身份实现，`SpecialKind`、`EliteAffixes`、`ZombieModeTuning` 与运行时技能数据没有开发期一致性检查，页面随源码演进后出现信息缺口。
**修复内容**:
- 修改 `WikiContent/zh/mode/mode__zombie_mode.md` 与 `WikiContent/en/mode/mode__zombie_mode.md`：覆盖 6 个 Special 和 12 个 Elite 词缀，登记污染前战斗倍率、目标色、安全视觉缩放、颜色优先级、爆炸/冲刺/毒区/减速/召唤/护盾/适应等已知数据及运行时未知边界。
- 生成 `wiki-site/docs/game-modes/zombie-mode.md` 与 `wiki-site/docs/en/game-modes/zombie-mode.md`。
- 新增 `tests/ZombieModeMutantWikiGuard.py`：从 C# 枚举读取图鉴身份，核对调优/技能/视觉常量、双语覆盖、生成页同步和 ZombieMode 运行时代码无 Wiki 扫描引用；Python guard 不进入 `compile_official.bat`。
**兼容性影响**: 不改变 TypeID、存档、配置、掉落、战斗倍率、技能、资源或 schema；开发期同步继续以 `WikiContent/` 为唯一权威源，未新增 C# Wiki 导出器、战斗实体扫描或常驻工作。
**验证方法**:
1. C# 编译：未运行；本条只修改 Markdown、生成 Markdown 与 Python guard，没有 `.cs` 或编译清单变更。
2. Guard：`ZombieModeMutantWikiGuard.py` 通过（6 个 Special、12 个词缀、双语生成页同步）；`ZombieModeMutantVisualProfileGuard.py` 与 `BossWikiGuideContentGuard.py` 通过。
3. Wiki：`npm.cmd run sync` 成功生成中文 99 篇、英文 99 篇；`npm.cmd run build` 通过，VitePress 1.6.4 完成客户端/服务端 bundle 与页面渲染。
4. 静态检查：本条作用域 `git diff --check` 无 whitespace 错误，仅有仓库既有 LF/CRLF 转换提示。
**未验证/需人工**: 游戏内置 Wiki 的长表横向阅读、字体换行和颜色代码显示仍需实机打开页面确认；纯文档改动不需要战斗行为 smoke。

---
### 2026-08-06 玩家反馈：D/E/F 开局资源、丧尸破隐/朝向与分类商店重复卡顿

**状态**: fixed
**Finding**: 最新玩家反馈 / 完整调用链与官方反编译复核
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: `012c900` / `8f5d9b5`
**Owner decision**: 不需要；沿用已确认的 ZombieMode 医疗/近战排除契约与现有 Mode E/F 商店边界
**现象**: Mode D/E/F 共用的开局医疗/近战发放仍可能绕过丧尸模式排除；丧尸模式前方延迟爆炸使用角色根节点朝向；安全区内对丧尸的致死一击或自定义近战伤害可能不破隐；Mode E/F 分类商店重开时每次重复全量 UI Setup，Mode E 附加按钮和余额文本还反复克隆/销毁。
**根因**: `GivePlayerStarterKit()` 只调用未带 Zombie 排除的池选择，医疗硬编码兜底也未过滤；`ZombieModeBattlefieldAreaCoroutine()` 直接读取 `player.transform.forward`；官方致死事件顺序为 `OnDead -> SetActive(false) -> OnHurt`，死亡处理先注销 marker 后，旧 `OnHurt` 破隐路径会提前返回，且自定义近战实际使用的 `Weapon` 标签未被识别；官方 `StockShopView.Setup()` 每次 `SetupAndShow()` 都遍历商品并对玩家/宠物/仓库重复调用会清空重建条目的 `InventoryDisplay.Setup()`，Mode E 附加控件生命周期也没有复用。
**修复内容**:
- 修改 `ModeD/ModeDEquipment_StarterKit.cs`：D/E/F 共用医疗与近战 starter pool 使用 `IsZombieModeRewardCandidateAllowed()`，401/402/403 硬编码兜底也经过同一过滤；无安全候选时跳过该格。
- 修改 `ZombieMode/ZombieModeRewardProjectileSpread.cs`：延迟爆炸优先取 `player.characterModel.transform.forward`，水平化并归一化，根节点/世界前方仅作兜底。
- 修改 `ZombieMode/ZombieModeWaveController.cs`：非致死命中只在确认 Zombie marker 后破隐；致死命中按官方同帧事件顺序在 `DeathSettled` 和 marker 注销前破隐；补充自定义近战 `Weapon` 标签，避免误伤非丧尸目标也破隐。
- 修改 `ModeE/ModeEHarmonyPatch.cs`、`ModeE/ModeE.cs`、`ModeE/ModeEMerchantSupportClasses.cs`：同一 BossRush Mode E/F 分类商店重开时跳过重复官方 `Setup`；切换分类仍完整刷新商品列表，但相同玩家/宠物/仓库 Inventory 不再清空重建；一键卖出按钮与贝壳余额文本关闭时隐藏、运行时销毁时释放，避免重复 Instantiate/Destroy。
- 新增/修改守卫：`tests/ModeDStarterKitZombieFilterGuard.py`、`tests/ModeEMerchantOpenReuseGuard.py`、`tests/ModeEShellHarmonyUiContractGuard.py`、`tests/ZombieModeRewardAreaOptionGuard.py`、`tests/ZombieModeSafeZoneGuard.py`。
**兼容性影响**: 不改变 TypeID、存档、配置、奖励数值、商店商品池或普通官方商店；新增 `StockShopView.Setup(StockShop)` 与 `InventoryDisplay.Setup(Inventory,Func<Item,bool>,Func<Item,bool>,bool,Func<Item,bool>)` Harmony 契约只在 BossRush Mode E/F 分类商店同步 Setup 调用栈内复用相同 Inventory，契约缺失时既有 Mode E fail-closed 逻辑保持关闭。新增文件均为 Python guard，不进入 `compile_official.bat`。
**验证方法**:
1. 日志：`Player.log`（2026-08-06 19:41，282265 bytes）未出现 `[ZombieMode]`、商店打开耗时或 BossRush 相关异常；可见异常是官方 `Duckov.MiniGames.GamingConsole.Load` 在 `Item ID:-1` 后的 NRE，以及第三方 TriangleDuckAttachmentExpansion 退出清理 NRE。
2. Windows 编译：`cmd.exe /c compile_official.bat` 通过并部署 `Build/BossRush.dll`。
3. Guard：定向 starter、safe-zone、延迟爆炸朝向、Mode E Harmony 契约和商店复用 guard 通过；`git diff --check` 通过。
**未验证/需人工**: ZombieMode 实际 characterModel 朝向、同帧死亡/对象销毁窗口、Harmony 实际命中、Mode D/E/F 开局抽样、安全区枪械/近战破隐、同店重开/切换分类 UI 手感和商店卡顿改善仍需实机验证；当前日志无法量化商店卡顿。
**失败尝试**: 无。

---
### 2026-08-06 Mode E 贝壳物品内部名误用导致商人全部关闭

**状态**: fixed
**Finding**: 玩家实机反馈 / 最新 `Player.log` 静态回溯
**兼容分类**: COMPAT
**版本/Commit**: `012c900`
**Owner decision**: 不需要；修复既定贝壳 economy capability 的运行时物品身份解析
**现象**: 进入 Mode E 后分类商人完全不生成；日志先出现 `[ModeE/Shell] capability disabled: session preflight failed`，随后出现 `merchant spawn preflight failed`。
**根因**: `ItemAssetsCollection.TryGetIDByName()` 按 `ItemMetaData.Name` 查找。当前游戏资源中贝壳内部名为 `SeaShell`，`Item_SeaShell` 是显示文本本地化键；错误查询返回 `-1`，使商人创建按 fail-closed 契约直接退出。
**修复内容**:
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：使用常量 `MODE_E_SHELL_ITEM_NAME = "SeaShell"` 查询，并在返回 `-1` 或抛异常时记录可区分的身份诊断；反射契约与 Harmony 安装证明现在逐项报告失败名称。
- 修改 `tests/ModeEShellCurrencyBoundaryGuard.py`、`tests/ModeEShellHarmonyUiContractGuard.py`：锁定 metadata name 与 localization key 的边界，并要求完整契约/补丁诊断。
**兼容性影响**: 不改变 TypeID、价格、余额、存档、配置、Harmony 目标或商人商品池；仅恢复当前资源下本应可用的 Mode E 贝壳 capability 和商人生成，并改善失败诊断。
**验证方法**:
1. 资源/官方契约：当前 `resources.assets` 同一贝壳记录包含内部名 `SeaShell` 与本地化键 `Item_SeaShell`；官方反编译确认 `TryGetIDByName` 比较 `Entry.metaData.Name`。
2. Windows 编译：`compile_official.bat` 通过并部署到游戏 Mods 目录。
3. Guard：全部 Mode E guards 通过；`BossWikiGuideContentGuard.py` 通过。
**未验证/需人工**: 需重新启动游戏进入 Mode E，确认日志出现 `M1 capability ready ... SeaShell=<TypeID>`、四个 Harmony 安装证明成功、神秘商人生成成功，并实际打开至少一个分类商店；购买/出售、Incoming Buffer、切图和 UI 手感仍需实机验证。
**失败尝试**: 无。

---
### 2026-08-06 Mode E 退役商店伪 null 现金旁路与复审 guard 失真

**状态**: fixed
**Finding**: 2026-08-03 玩家反馈保守优化方案复审 / 本轮完整调用链复核
**兼容分类**: COMPAT / SAFE
**版本/Commit**: `012c900` / `224c35d`
**Owner decision**: 不需要；修复既定"受管 Mode E 商店不得回落原版现金购买"契约，并收紧静态守卫
**现象**: Mode E 商店退役并调用 `Destroy` 后，Unity 会在实际销毁前把组件比较为 `null`。此时同帧迟到的购买/出售/UI 回调可能被三态路由误判为普通商店，放行原版现金逻辑。另有发货 guard 仍断言旧版五参数事务入口，造成正确实现被误报；Wiki 12 个目标页面没有成对同步与最低完整度 guard。
**根因**: `GetModeEShellShopPatchDisposition()` 使用 Unity 重载的 `shop == null` 作为 `PassOriginal` 条件，没有区分真实 CLR null 与仍在 tombstone 中的 Unity 伪 null；`ModeEShellDeliveryContractGuard.py` 未随事务 owner 签名收敛；Wiki 仅依赖人工同步和站点构建。
**修复内容**:
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：用 `object.ReferenceEquals` 识别真实 CLR null，使受管但已退役的 Unity 对象继续返回 `Block`，不再回落现金路径。
- 修改 `tests/ModeEShellModeScopeGuard.py`：精确提取 disposition 方法，强制真实 null / 伪 null 分类顺序，并禁止重新引入 `shop == null` 放行。
- 修改 `tests/ModeEShellDeliveryContractGuard.py`：改为当前事务 owner 签名，并验证 `isSellAll`、Busy 所有权初始状态等契约。
- 新增 `tests/BossWikiGuideContentGuard.py`：检查 6 个 canonical 页面与 6 个生成页面、catalog 登记、玩家所需的概述/基础数据/技能或阶段/掉落/战斗建议/出现限制章节，以及忽略站点标题和提示框标记后的正文同步；不再强制开发者式源码路径、待实机字样、固定标题或表格数量。
**兼容性影响**: 不改变 TypeID、存档、配置、掉落、价格或交易 schema；仅阻止已属 Mode E 的退役商店在 Unity 销毁窗口误入原版现金路径。新文件为 Python guard，不进入 `compile_official.bat`。
**验证方法**:
1. Windows 编译：`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过并部署。
2. Guard：复审文档 31 个 guard 加 Wiki guard 共 32/32 通过；全部 `ZombieMode*.py` 与 `ModeE*.py` 共 171/171 通过；编译清单与内容注册 guard 通过。
3. Wiki：`npm.cmd --prefix wiki-site run build` 通过，中文 99 篇、英文 99 篇同步，VitePress 1.6.4 构建完成。
4. 静态检查：`git diff --check` 无 whitespace 错误，仅有既有 LF/CRLF 提示。
**未验证/需人工**: Unity 伪 null 同帧迟到回调、Harmony 实际命中、商店对象销毁、UI 手感、Incoming Buffer 故障注入和性能仍需实机验证。
**失败尝试**: 首次从 `wiki-site` 子目录直接运行仓库根相对路径 guard，因工作目录错误失败；改回仓库根后通过，内容同步本身未失败。首次最终 `git diff --check` 还发现本条目的 Markdown 硬换行空格，已移除并复检。

---
### 2026-08-05 丧尸模式候选、准备节奏、直接掉落与变异辨识优化

**状态**: fixed
**Finding**: 玩家反馈 / `2026-08-03_玩家反馈保守优化方案复审.md`
**兼容分类**: COMPAT
**版本/Commit**: `8f5d9b5`
**Owner decision**: 已确认；采用九个 opaque TypeID 的上下文过滤、45/75 秒节奏和双失败脚底标记规则
**现象**: 丧尸模式可能抽到不适用的医疗品或近战物品，普通准备时间偏紧；敌人直接掉落在分类失败时可能退化为任意物品，重物或背包满时缺少统一落地反馈；Special/Elite 混群中的常驻辨识不足。
**根因**: 候选缓存只有标签/品质通用过滤；准备阶段只有单一常量；敌人直接掉落保留无类别 fallback 且未在 `AddAndMerge` 前投影负重；变异身份虽已登记，但没有安全视觉节点、完整 CustomFace 探针和双失败常驻 fallback 的闭环。
**修复内容**:
- 新增文件: `tests/ZombieModeRewardContextFilterGuard.py`、`tests/ZombieModeMutantVisualProfileGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 修改文件: `ZombieMode/ZombieModeEntry.cs`、`ZombieMode/ZombieModeTuning.cs`、`ZombieMode/ZombieModeWaveController.cs`
- 修改文件: `ZombieMode/ZombieModeDropsAndPerformance.cs`、`ZombieMode/ZombieModeEnemyRuntime.cs`、`ZombieMode/ZombieModePollution.cs`、`ZombieMode/ZombieModeRuntimeHooks.cs`
- 修改文件: `tests/ZombieModeStateModelGuard.py`、`tests/ZombieModePacingTuningGuard.py`、`tests/ZombieModeEnemyDropInventoryGuard.py`、`tests/ZombieModeEnemyDropPickupBubbleGuard.py`
- 变异视觉节点现在同时检查 renderer 子树和 renderer 到敌人根节点的祖先链；武器、socket、碰撞、攻击、导航等祖先一律 fail-closed。Elite 常驻颜色按词条语义映射为黄/红/绿/紫/青/橙，并保留 CustomFace 与双失败脚底标记兜底。
**兼容性影响**: 不新增 TypeID、存档 key、配置 key 或地图 schema；过滤仅作用于 ZombieMode 对应医疗/近战上下文，同标签逐级降品质，不扩散到其他模式。敌人直接掉落不再做无类别 fallback，箱子和其他发货路径保持原样。全部 Boss 跳过变异视觉二次缩放。
**验证方法**:
1. Windows 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1&&compile_official.bat"` 已在最终共享代码树通过并部署
2. Guard: 15 个 ZombieMode 定向守卫全部通过；其中 `ZombieModeRunScopedRegistryGuard.py` 成功路径静默退出码 0
3. 静态检查: `git diff --check` 无 whitespace 错误，仅有换行符提示
**未验证/需人工**: 文档第 10.1 节仍需游戏内完成 30 局医疗/近战候选抽样、200 次敌人直接掉落、CustomFace 覆盖与故障注入、安全缩放节点及碰撞/socket 核对，以及 50 敌人 Profiler 压测。

---
### 2026-08-05 Mode E 局内贝壳购买与 Boss 奖励闭环

**状态**: fixed
**Finding**: 玩家反馈 / `2026-08-03_玩家反馈保守优化方案复审.md`
**兼容分类**: BREAKING / WIRE+ / OPERATIONAL
**版本/Commit**: `012c900`
**Owner decision**: 已确认；购买替换为本局账本式贝壳，价格按现金基准 `/10000` 单调量化，首版限制 `amount == 1`，出售继续使用现金
**现象**: Mode E 分类商店沿用局外账户现金，裸装入场仍可用局外财富购买；敌对 Boss 没有局内货币奖励，缺少从战斗到购买的会话内经济闭环。
**根因**: 原商店完全复用官方现金购买事务、UI 和商品 sample 缓存，没有独立 session 余额、价格身份、交易 owner、发货提交边界或 Boss 奖励 state。
**修复内容**:
- 新增文件: `tests/ModeEShellCurrencyBoundaryGuard.py`、`tests/ModeEShellModeScopeGuard.py`、`tests/ModeEShellTransactionGateGuard.py`
- 新增文件: `tests/ModeEShellDeliveryContractGuard.py`、`tests/ModeEShellObserverOrderGuard.py`、`tests/ModeEShellPriceNormalizationGuard.py`
- 新增文件: `tests/ModeEShellRewardGuard.py`、`tests/ModeEShellRuntimeLifecycleGuard.py`、`tests/ModeEShellHarmonyUiContractGuard.py`（均为 Python guard，不进入编译清单）
- 修改文件: `ModeE/ModeE.cs`、`ModeE/ModeEStartup.cs`、`ModeE/ModeEMerchant.cs`、`ModeE/ModeEMerchantSupportClasses.cs`
- 修改文件: `ModeE/ModeEHarmonyPatch.cs`、`ModeE/ModeEBattle.cs`、`ModeE/ModeEBattle_ScalingAndRuntime.cs`、`ModeE/ModeELifecycle.cs`、`ModeE/ModeERuntimeModule.cs`
- 修改文件: `Patches/Economy/StockShopGetItemInstanceDirectPatch.cs`、`compile_official.bat`
- 交易 owner 初始不认领官方 `buying`/`selling`；Buy、Sell、SellAll 只有在通过捕获商店 `Busy` 检查后才标记对应字段所有权，`finally` 不会清理由其他流程持有的 Busy。异步购买返回后还会确认捕获条目仍以同一对象引用存在于当前 `shop.entries`。
**兼容性影响**: Mode E 已登记分类商店的购买支付由现金破坏性替换为本局内存贝壳账本；不写存档、不生成实体贝壳，不改变商品池、库存、Bullet 满堆、出售现金、Mode F 或普通商店。新增四个精确 Harmony/反射契约与非 Harmony 安装证明；`compile_official.bat` 增加现有游戏 DLL `Plugins.dll` 引用以使用官方通知格式扩展。
**验证方法**:
1. Windows 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1&&compile_official.bat"` 通过并部署
2. Guard: 9 个 `ModeEShell*.py` 全部通过
3. Guard: 复审文档第 9 节列出的 10 个 Mode E 回归守卫全部通过
4. Guard: `StaticCacheLifecycleGuard.py` 为 `PASS=64, WARN=11, FAIL=0`，警告均为既有白名单
5. 静态检查: `git diff --check` 无 whitespace 错误，仅有换行符提示
**未验证/需人工**: 文档第 10.2 节 30 项事务矩阵、M0 Harmony 命中/对象状态探针、Incoming Buffer 故障注入、UI 操作、Mode F 隔离和连续购买 Profiler 数据仍需游戏内执行；当前静态 guard 不能替代这些运行时门禁。

---
### 2026-08-05 三个原创 Boss 中英文完整攻略与数据卡

**状态**: fixed
**Finding**: 玩家反馈 / `2026-08-03_玩家反馈保守优化方案复审.md`
**兼容分类**: SAFE
**版本/Commit**: `224c35d`
**Owner decision**: 不需要；仅按当前活动源码补齐攻略和来源标注
**现象**: 龙裔遗族、焚天龙皇和幽灵女巫的中英文页面缺少统一生存/移动与武器/技能数据卡，阶段打法、常见误区、模式规则和来源说明不足。
**根因**: 旧页面以简短机制摘要为主，没有系统区分活动源码常量、掉落装备面板、AssetBundle 数据和待实机快照字段。
**修复内容**:
- 修改文件: `WikiContent/zh/boss/boss__dragon_descendant.md`、`boss__dragon_king.md`、`boss__phantom_witch.md`
- 修改文件: `WikiContent/en/boss/boss__dragon_descendant.md`、`boss__dragon_king.md`、`boss__phantom_witch.md`
- 生成文件: `wiki-site/docs/bosses/` 与 `wiki-site/docs/en/bosses/` 下对应六个页面
- 三位 Boss 的掉落概率与龙裔/龙皇成就奖金已补充到具体源码文件；幽灵女巫实际手持武器行逐项登记伤害、元素、攻击间隔、近战射程的待实机状态，以及散布、弹速、弹匣、装填的近战 N/A。
**兼容性影响**: 纯文档内容；不改变 `WikiContent/catalog.tsv`、运行时数值、TypeID、资源或任何 schema。龙裔二阶段直线弹与扇形弹明确记录 `SpawnBulletDirect` 强制 100% 火焰元素，未知面板继续标为待实机快照。
**验证方法**:
1. 同步: `npm.cmd run sync` 成功，中文 99 篇、英文 99 篇，共 198 篇
2. 构建: `npm.cmd run build` 通过，VitePress 1.6.4 完成客户端/服务端 bundle 与页面渲染
3. 定向检查: canonical 与生成页面均包含已确认的二阶段火焰元素、当前英文成就名称、掉落/成就源码路径及幽灵女巫逐项武器字段
4. 浏览器渲染: `/BossRushMod/` 下中英文三个 Boss 共六个路由均已打开；标题、正文、三张表、warning 块正常，根容器无横向溢出
5. 静态检查: Wiki 作用域 `git diff --check` 无 whitespace 错误，仅有换行符提示
**未验证/需人工**: 文档第 10.3 节的游戏内置 Wiki 实机阅读仍待后续验证，所有标注"待实机快照"的字段也仍需按指定采样条件补录。

---
### 2026-07-09 丧尸模式自定义自爆怪无距离门控且未自毁

**状态**: fixed
**Finding**: 玩家反馈 / 静态代码确认
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于既有 `ExploderTriggerDistance` 调参常量未接入、且"自爆怪"语义未闭环的行为修复
**现象**: 玩家反馈丧尸模式 BOSS 关有东西持续追着玩家爆炸。静态排查确认 BOSS 波仍会按既有压力系统维持环境尸潮；第 6 波以后特殊怪池包含自定义 `Exploder` 和官方 `OfficialExploder`。其中自定义 `Exploder` 的技能冷却到点后直接原地起手爆炸，没有检查与玩家距离，爆炸后不会死亡；起手期间若丧尸移动，旧实现还会保留起手时的旧爆心。
**根因**: `ZombieModeTuning.ExploderTriggerDistance = 2.5f` 已存在，但 `TryExecuteZombieModeSpecialSkill()` 的 `ZombieModeSpecialKind.Exploder` 分支未使用该距离门控；同时自定义爆炸只调用 telegraph/ExplosionManager 伤害路径，没有对自身走 `Health.Hurt()` 死亡链路，导致自定义自爆怪只要存活并追踪玩家，就会按 9 秒冷却反复放红圈爆炸。通用 telegraph 默认固定起手坐标，不能表达"自爆中心始终是丧尸当前位置"。
**修复内容**:
- 新增文件: `tests/ZombieModeExploderTriggerDistanceGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 修改文件: `ZombieMode/ZombieModeEnemyRuntime.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeComponents.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeSkills.cs`
- 修改文件: `tests/ZombieModeExploderOfficialSelfDestructGuard.py`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；不改变 BOSS 波环境尸潮规则，也不改变官方 `OfficialExploder` 的原版自爆保留逻辑。自定义 `Exploder` 改为遵守既有 2.5 米触发距离，技能起手红圈跟随丧尸当前位置，爆炸后走正常死亡链路自毁；被玩家提前打死时也会在死亡位置触发一次自爆。通过 marker 标记跳过技能自爆后的死亡二次爆炸，并让 pending 跟随红圈在来源已死亡时取消，避免双炸。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过并部署到游戏 Mods 目录
2. Guard: `python tests\ZombieModeExploderTriggerDistanceGuard.py` 通过
3. Guard: `python tests\ZombieModeExploderOfficialSelfDestructGuard.py` 通过
4. Guard: `python tests\ZombieBoomAttachmentSanitizerGuard.py` 通过
5. Guard: `python tests\ZombieModeWaveEventMarkerCacheGuard.py` 通过
6. Guard: `python tests\ZombieModeNormalZombieCapAndAggroGuard.py` 通过
**未验证/需人工**: 需要进游戏在第 10 波及以后 BOSS 关实测，确认自定义 `Exploder` 只有贴近后才起爆，红圈/爆心跟随丧尸当前位置，起爆后正常死亡、不再反复追炸；同时确认起手期间被击杀只在死亡位置爆一次。`Hunter` Boss 每 5 秒闪近并在玩家脚下放爆炸圈仍是当前设计技能。
**失败尝试**: 无

---
### 2026-07-09 丧尸模式瘟疫/骚扰特殊怪行为与公开描述不一致

**状态**: fixed
**Finding**: 全面静态排查 / Wiki 与调参常量对照
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于公开描述、调参常量与运行时行为对齐的行为修复
**现象**: 继续排查特殊丧尸、精英词缀和 BOSS 技能后确认，瘟疫者描述为持续毒云但旧实现是一次性爆点；精英 `ToxicAura`/`Plague` 描述为毒雾/毒云但旧实现也是一次性爆点；骚扰者描述为发射投射物命中后生成减速区，但旧实现是在玩家脚下直接延迟减速；共享区域 tick 在带 `slowPercent` 时直接给玩家上减速，导致可见区域外也可能吃到减速；腐蚀者毒圈有起手常量但首跳没有真正延迟。
**根因**: `TryExecuteZombieModeSpecialSkill()` 和 `TryExecuteZombieModeEliteSkill()` 复用了瞬发 telegraph damage helper 表达毒云/毒雾；骚扰者复用了玩家减速 telegraph 而没有投射物 runtime；`ZombieModeAreaTickRuntime` 只把 slow 当作全局玩家状态写入，没有走范围判定 helper；腐蚀区只把 startup 加进对象寿命，未传入首 tick 延迟。
**修复内容**:
- 新增文件: `tests/ZombieModePlagueCloudBehaviorGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 新增文件: `tests/ZombieModeHarasserProjectileGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 新增文件: `tests/ZombieModeAreaTickSlowScopeGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 修改文件: `ZombieMode/ZombieModeBossController.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeComponents.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeSkills.cs`
- 修改文件: `tests/ZombieModeAreaDamagePlayerGuard.py`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；不改变特殊怪/精英/BOSS 刷新池或数值常量。瘟疫者和精英毒雾改为 telegraph 后生成持续区域伤害云；骚扰者改为生成可飞行投射物，命中或到期后才结算 25 伤害并创建 3.5m 减速区；区域减速改为只在玩家位于可见区域内时生效；腐蚀区首跳遵守 `CorruptorZoneStartupSeconds`。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过并部署到游戏 Mods 目录
2. Guard: `python tests\ZombieModePlagueCloudBehaviorGuard.py` 通过
3. Guard: `python tests\ZombieModeHarasserProjectileGuard.py` 通过
4. Guard: `python tests\ZombieModeAreaTickSlowScopeGuard.py` 通过
5. Guard: `python tests\ZombieModeAreaDamagePlayerGuard.py` 通过
6. Guard: `python tests\ZombieModePauseMenuGuard.py`、`python tests\ZombieModeTimeAxisGuard.py` 退出码 0
7. Guard: `python tests\ZombieModeRunOnlyCleanupGuard.py`、`python tests\ZombieModeCompileListGuard.py` 通过
8. Guard: `python tests\ZombieBoomAttachmentSanitizerGuard.py`、`python tests\ZombieModeWaveEventMarkerCacheGuard.py`、`python tests\ZombieModeNormalZombieCapAndAggroGuard.py` 通过
9. 静态检查: `git diff --check` 无 whitespace 错误，仅有既有 CRLF 提示
**未验证/需人工**: 需要进游戏在普通波和 BOSS 波实测，确认瘟疫/毒雾持续云、骚扰者投射物、腐蚀区起手与减速范围符合可见表现。
**设计确认项**: `SprinterDashStartupSeconds = 0.5f` 已在后续修复接入；`Hunter` Boss 仍按当前 Wiki 明文描述执行 15m 传送式 Dash，若后续要改成可躲避冲刺，需要 owner 作为设计改动确认。
**失败尝试**: 无

---
### 2026-07-09 丧尸模式疾行者瞬移与死亡爆炸表现不一致

**状态**: fixed
**Finding**: 全面静态排查 / Wiki 与调参常量对照
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于调参常量、公开描述与运行时行为对齐的行为修复
**现象**: 继续排查特殊丧尸、精英词缀和 BOSS 死亡效果后确认，疾行者描述/常量是 0.5 秒起手后的 12m 冲刺，但旧实现直接 `transform.position` 改坐标，表现为瞬移；精英 `Burst`、Splitter/Titan 死亡爆炸走 player-only 区域伤害路径，玩家会吃到伤害但缺少原生爆炸 VFX、屏幕反馈和墙体阻挡语义。
**根因**: `ZombieModeTuning.SprinterDashStartupSeconds` 只存在于调参表，`TryExecuteZombieModeSpecialSkill()` 的 `Sprinter` 分支没有接入；死亡爆炸沿用了早期 `DealZombieModeAreaDamageToPlayer()` fallback，而不是 `ExplosionManager.CreateExplosion` 封装。
**修复内容**:
- 新增文件: `tests/ZombieModeSprinterDashTelegraphGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 新增文件: `tests/ZombieModeDeathExplosionVisualGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeComponents.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeSkills.cs`
- 修改文件: `ZombieMode/ZombieModeBossController.cs`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；不改变疾行者冷却、冲刺距离或死亡爆炸数值。疾行者改为先显示跟随本体的短起手提示，再用 `SetForceMoveVelocity` 执行 12m 冲刺，并在暂停、死亡、run 清理和销毁时归零强制速度；精英 Burst、Splitter/Titan 死亡爆炸改走原生爆炸路径，保留 fallback 避免 ExplosionManager 不可用时技能失效。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过并部署到游戏 Mods 目录
2. Guard: `python tests\ZombieModeSprinterDashTelegraphGuard.py` 通过
3. Guard: `python tests\ZombieModeDeathExplosionVisualGuard.py` 通过
4. Guard: `python tests\ZombieModeExploderTriggerDistanceGuard.py`、`python tests\ZombieModeExploderOfficialSelfDestructGuard.py` 通过
5. Guard: `python tests\ZombieModePlagueCloudBehaviorGuard.py`、`python tests\ZombieModeHarasserProjectileGuard.py`、`python tests\ZombieModeAreaTickSlowScopeGuard.py` 通过
6. Guard: `python tests\ZombieModeAreaDamagePlayerGuard.py`、`python tests\ZombieModePauseMenuGuard.py`、`python tests\ZombieModeTimeAxisGuard.py`、`python tests\ZombieModeRunOnlyCleanupGuard.py`、`python tests\ZombieModeCompileListGuard.py` 退出码 0
7. Guard: `python tests\ZombieBoomAttachmentSanitizerGuard.py`、`python tests\ZombieModeWaveEventMarkerCacheGuard.py`、`python tests\ZombieModeNormalZombieCapAndAggroGuard.py` 通过
8. 静态检查: `git diff --check` 无 whitespace 错误，仅有既有 CRLF 提示
**未验证/需人工**: 需要进游戏实测疾行者起手提示/冲刺体感，以及 Burst、Splitter、Titan 死亡爆炸的 VFX、屏幕反馈、伤害和墙体阻挡。
**设计确认项**: `Hunter` Boss 仍按 Wiki 明文描述执行 15m 传送式 Dash；若需要把它也改为可预警冲刺，应按设计改动单独确认。
**失败尝试**: 无

---
### 2026-07-08 焚天龙铳烟花弹多余开火/终点特效

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳烟花弹表现修复
**现象**: 烟花弹 FireWork 与前面多个弹种类似，开火或弹幕结束处仍会出现不适配的多余资源特效。
**根因**: Firework profile 已关闭 `PlayObstacleHitFx` / `PlaySplitTriggerFx`，但仍保留 `Fx_DragonGun_Firework_*` 的 Trail/Hit/Explosion prefab，导致部分路径仍可实例化烟花弹专属资源特效。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；仅清空烟花弹自定义资源特效引用，螺旋飞行、空爆分裂、伤害和标记逻辑保持不变。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过并部署到游戏 Mods 目录
2. Guard: `Get-ChildItem tests -Filter 'DragonKingBossGun*.py' | ForEach-Object { python $_.FullName }` 通过
**未验证/需人工**: 需要进游戏用 FireWork 烟花弹实测，确认开火、空爆分裂和弹幕结束处不再出现多余光效。

---
### 2026-07-08 焚天龙铳纳米弹多余开火/终点特效

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳纳米弹表现修复
**现象**: 纳米弹 NM 与前面多个弹种类似，开火或弹幕结束处仍会出现不适配的多余资源特效。
**根因**: Nano profile 已关闭 `PlayObstacleHitFx` / `PlaySplitTriggerFx`，但仍保留 `Fx_DragonGun_Nano_*` 的 Trail/Hit/Explosion prefab，导致部分路径仍可实例化纳米弹专属资源特效。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；仅清空纳米弹自定义资源特效引用，追踪、分裂、伤害和标记逻辑保持不变。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过并部署到游戏 Mods 目录
2. Guard: `Get-ChildItem tests -Filter 'DragonKingBossGun*.py' | ForEach-Object { python $_.FullName }` 通过
**未验证/需人工**: 需要进游戏用 NM 纳米弹实测，确认开火、分裂触发和弹幕结束处不再出现多余光效。

---
### 2026-07-08 焚天龙铳雪球弹滚雪球重做

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳雪球弹表现修复
**现象**: 用户不希望 Snow 继续作为高弧线冰弹，而是要超慢速直线滚雪球：主雪球滚 5 秒并越滚越大，命中/死亡后分裂 4 个小雪球；小雪球滚 2 秒继续变大，出生后短暂无敌防止刚分裂就被敌怪吞掉。后续又要求主雪球最大改为旧最大雪球 10 倍、小雪球最大 5 倍、降低基础伤害，并且只允许主雪球留下冰区。
**根因**: 旧 Snow profile 使用 High arc / gravity / 落地冰区的通用配置，主弹和二段弹都复用同一套冰区生成策略；分裂弹没有专门的出生保护，也没有"当前体积影响伤害"的运行时状态。
**修复内容**:
- 新增文件: `tests/DragonKingBossGunSnowballGuard.py`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs`
- 修改文件: `WikiContent/zh/equipment/equipment__dragon_cannon.md`
- 修改文件: `WikiContent/en/equipment/equipment__dragon_cannon.md`
- 修改文件: `wiki-site/docs/equipment/dragon-cannon.md`
- 修改文件: `wiki-site/docs/en/equipment/dragon-cannon.md`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；仅调整 Snow profile 与 DragonKingBossGunProjectileAgent/发射上下文运行时表现。复用既有弹体、对象池、分裂、半径伤害和冰区系统；未新增每帧分配或独立弹幕系统。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 编译通过；若游戏进程运行中，自动部署会因 DLL 被锁而失败，需关游戏后重新部署
2. Guard: `python tests\\DragonKingBossGunSnowballGuard.py` 通过
3. Guard: `python tests\\DragonKingBossGunProfileCoverageGuard.py` 通过
4. Guard: `python tests\\DragonKingBossGunEnergyPwsGuard.py` 通过
5. Guard: `python tests\\F3DragonKingBossGunDebugKitGuard.py` 通过
**未验证/需人工**: 需要进游戏用雪球弹实测，确认主雪球直线慢滚 5 秒、最大视觉接近旧最大 10 倍且伤害随体积提高；主雪球命中/寿命结束分裂 4 个小雪球，小雪球 0.1 秒内不会被敌怪吞掉、2 秒内长到出生 5 倍；只有主雪球留下 1 秒冰区，小雪球不再铺冰。

---
### 2026-07-06 逆鳞致死触发兼容与复活后短暂无敌

**状态**: fixed
**Finding**: 玩家反馈 / 官源静态确认
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 本次提交
**Owner decision**: 不需要；属于官方 `Health.Hurt()` 时序变化后的逆鳞兼容修复
**现象**: 玩家反馈新版本更新后逆鳞不触发；致死伤害下无法完成"濒死回血、反击、碎裂"的保命流程。
**根因**: 当前官源 `Health.Hurt()` 的执行顺序为扣血后先进入死亡分支、触发 `OnDeadEvent` / `Health.OnDead` 并 `SetActive(false)`，最后才触发 `OnHurtEvent` / `Health.OnHurt`。逆鳞旧逻辑只在 `Health.OnHurt` 里看 `CurrentHealth <= 1`，致死伤害会先把主角送入死亡流程，导致保命窗口来不及触发。
**修复内容**:
- 新增文件: `tests/ReverseScaleLethalProtectionGuard.py`
- 修改文件: `Patches/Combat/BossLethalHealthProtectionPatch.cs`
- 修改文件: `Integration/ReverseScale/ReverseScaleAbilityManager.cs`
- 修改文件: `Integration/ReverseScale/ReverseScaleConfig.cs`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名或掉落表；扩展既有 `Health.Hurt` / `Health.CurrentHealth` Harmony 兼容补丁，在玩家装备逆鳞且受到致死伤害时先钳到触发阈值，并确保逆鳞 `OnHurt` 回调已注册。逆鳞触发回血后新增 0.5 秒免伤窗口。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过；最终部署目标 `Mods\\BossRush\\BossRush.dll` 被正在运行的 `Duckov.exe` 占用，自动部署和手动覆盖均失败，`Build\\BossRush.dll` 已生成
2. Guard: `python tests\\ReverseScaleLethalProtectionGuard.py` 通过
3. Guard: `python tests\\EventSubscriptionLifecycleGuard.py` 通过
4. Guard: `python tests\\MenuSceneRuntimeHookGuard.py` 通过
**未验证/需人工**: 关闭游戏释放 DLL 锁后重新部署，再进游戏装备逆鳞承受致死伤害，确认回血、棱彩弹、气泡、图腾销毁和触发后 0.5 秒免伤都符合预期。

---
### 2026-07-06 焚天龙铳火箭弹空爆只单次爆炸

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳火箭弹表现修复
**现象**: 火箭弹到空爆距离后只在空中触发一枚原版爆炸，未表现为多枚分裂火箭落地/命中后的连续爆炸；后续实测中分裂弹又会在敌人头上散开后高速飞走，且主弹仍按固定射程比例空爆，不是射到鼠标位置再爆开。再次调整时用户要求分裂弹不再按固定定时爆炸，而是依靠自身碰撞爆炸。最新实测中日志已缓存 `BulletRocket`，但分裂弹命中后没有爆炸。
**根因**: 火箭 profile 的 `SplitCount` 仍为 1 且使用向下分裂；同时主弹使用原版火箭预制体，空爆置 `dead` 后仍会被原版 `Projectile.Update()` 按 `context.explosionRange` 当作最终爆炸处理。后续迭代中分裂弹固定引信路径全生命周期跳过碰撞，且主弹空爆距离仍用 `distance * AirburstDistanceFactor`，没有读取玩家当前鼠标瞄准点；原版火箭预制体缓存还会接受 Rocket 口径武器上的 `BulletNormal_Burn`，导致分裂弹视觉/行为取错基底。官源 `Projectile.Init()` 的 `hitLayers` 只包含敌人、墙、地面和 `blockBulletLayers`，不包含 Projectile 自身，因此分裂弹之间不会因为彼此弹体互相触发爆炸。最新问题是分裂弹虽已使用原版 `BulletRocket` 预设，但自定义 `ProjectileContext` 没有设置 `explosionRange` / `explosionDamage`，原版 `Projectile.Update()` 因 `context.explosionRange == 0` 不会调用 `ExplosionManager.CreateExplosion`。
**修复内容**:
- 新增文件: `tests/DragonKingBossGunRocketSplitGuard.py`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs`
**性能处理**: 按用户要求将主弹/分裂弹预设互换：主弹用焚天龙铳轻量弹体负责飞到玩家鼠标瞄准点后空爆，分裂弹使用原版火箭视觉，并且原版预制体缓存改为按火箭弹 TypeID `326` 的确定关系绑定：只接受 `TargetBulletID`、`PreferdBulletsToLoad.TypeID` 或枪内当前装填弹 TypeID 等于 `326` 的原版枪械，不再用 `Rocket` / `RPG` / `Missile` 名字模糊匹配，也不再扫 `Projectile` 名字池兜底。火箭分裂弹数量固定 6 枚，继续走对象池与复用的 `raycastBuffer`；取消出生 `0.1s` 免碰撞窗口和 `SplitFuseTime` 定时爆炸，让分裂弹从第一帧开始使用同一套 `SphereCastNonAlloc` 检测敌人、墙和地面，自身不互相碰撞，命中后由原版 `Projectile.Update()` 根据 `context.explosionRange` 走 `ExplosionManager.CreateExplosion` 爆炸，未命中则按射程自然结束。重力分裂弹禁用追踪，二段初速从 `0.78x` 降到 `0.12x`，`SplitGravity` 提到 `24`，带重力 Radial 分裂固定使用世界水平圆环轴并加入轻微向下偏置，使其按水平 360 度径向初速 + 重力在首发弹四周下坠散开；非原版预设的二段爆炸才保留自定义 `OverlapSphereNonAlloc` 半径伤害，避免原版火箭双重伤害/双重特效。火焰爆炸 FX 改为尊重 profile 的 `ExplosionFxDuration`，火箭小爆炸按 `0.35s` 级别清理，避免固定残留 2 秒。
**兼容性影响**: 不涉及存档、配置、TypeID 或资源文件变更；仅调整焚天龙铳火箭弹运行时弹幕表现。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过
2. Guard: `python tests\DragonKingBossGunRocketSplitGuard.py` 通过
3. Guard: `python tests\DragonKingBossGunProfileCoverageGuard.py` 通过
4. Guard: `python tests\DragonKingBossGunAmmoSwitchGuard.py` 通过
5. Guard: `python tests\DragonKingBossGunReforgeBaselineGuard.py` 通过
6. Guard: `python tests\F3DragonKingBossGunDebugKitGuard.py` 通过
7. 格式: `git diff --check -- Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs tests/DragonKingBossGunRocketSplitGuard.py FIX_TRACKER.md` 通过
**未验证/需人工**: 需要进游戏用 TypeID `326` 火箭弹实测，确认日志出现 `按火箭弹 TypeID=326 缓存原版火箭弹预制体: BulletRocket`，主弹在鼠标瞄准点附近空爆、空爆后散出 6 枚子火箭、分裂弹不会因彼此重叠互相引爆、分裂弹可正常因命中敌人/墙/地面触发原版火箭爆炸且未命中时按射程自然结束。
---
### 2026-07-06 焚天龙铳弹种 baseline 与射击热路径修复

**状态**: fixed
**Finding**: CR-2026-07-05-001 / CR-2026-07-05-002 / CR-2026-07-05-003
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于已确认的焚天龙铳运行时稳定性修复
**现象**: 重铸过容量的焚天龙铳连续切换弹种时，弹匣 baseline 可能因取整反推被污染；场景切换清理会丢失手持枪的弹种 baseline；射击兜底每发重复写 Stat 并在 dev 模式刷覆盖日志；弹药 UI 选择仅靠 Caliber 兼容的 TypeID 时，本次换弹可能沿用上一弹种容量/换弹时间。
**根因**: 弹种 baseline 捕获优先从已套 profile 的当前 Stat 反推；`ClearSceneCaches()` 同时清掉枪实例弹种状态；`ShootOneBullet` 路径没有按枪实例跳过已应用的同 profile；`SetTargetBulletType(int)` / 空枪 TargetBulletID 兜底只查 `TryResolveTypeId`，没有回到真实弹药实例走 Caliber 解析。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 修改文件: `tests/DragonKingBossGunAmmoSwitchGuard.py`
- 修改文件: `tests/DragonKingBossGunReforgeBaselineGuard.py`
- 修改文件: `CODE_REVIEW_FINDINGS.md`
**兼容性影响**: 不改 TypeID、存档 key、配置 schema、资源命名或掉落表；仅调整焚天龙铳运行时 Stat 覆盖缓存生命周期与重复写入判定。场景级弹幕/命中缓存仍在切图时清理，枪实例弹种 baseline 改为卸载/reset 时清理；targetTypeId 解析兜底只读取背包/枪内弹药实例用于 Caliber profile 同步。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过
2. Guard: `python tests\\DragonKingBossGunProfileCoverageGuard.py` 通过
3. Guard: `python tests\\DragonKingBossGunAmmoSwitchGuard.py` 通过
4. Guard: `python tests\\DragonKingBossGunReforgeBaselineGuard.py` 通过
5. Guard: `python tests\\EventSubscriptionLifecycleGuard.py` 通过
6. Guard: `python tests\\StaticCacheLifecycleGuard.py` 通过
**未验证/需人工**: 需要游戏内验证重铸容量后连续切弹、切图后当前弹种属性、dev 模式高射速射击日志量，以及 UI 选择仅按 Caliber 兼容弹药后的首次换弹容量/时间。
**失败尝试**: 无

---
### 2026-07-04 Mode E/F 刷怪卡顿低风险优化

**状态**: fixed
**Finding**: 无（玩家反馈 + `docs/测试分析/2026-07-03_ModeE刷怪卡顿无行为优化审查.md` 静态审查建议）
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 用户要求按审查文档执行；当前工作区已补齐普通 Boss plan 化/隐藏物化、Mode E/F 共享 postprocess scheduler、提交屏障，以及三类自定义特殊 Boss 的 Mode E/F 显式 deferred activation。P0 现有 dev 日志仅覆盖 Mode E 开局，剩余三类场景仍需同机 profiler 复测。
**现象**: 玩家反馈 Mode E/F 刷怪时仍会"一卡一卡"，尤其单只 Boss 生成配置链和重刷道具连续生成时容易产生主观尖刺。
**根因**: 静态核对显示普通 Boss 的配装/倍率/激活/变异词条/掉落追踪与 Mode E/F 登记回调仍会压到相邻帧；BossRegen 词条开启时 Mode E/F 还会每帧重建存活 Boss 列表；挑衅烟雾弹首次使用时会同步解析原版烟雾 VFX；最近点选择仍对全量点排序。
**修复内容**:
- 新增文件: 无
- 修改文件: `ModBehaviour.cs`
- 修改文件: `Utilities/EnemySpawnCore.cs`
- 修改文件: `Utilities/ModeRuntimeHooks.cs`
- 修改文件: `ModeE/ModeE.cs`
- 修改文件: `ModeE/ModeEStartup.cs`
- 修改文件: `ModeE/ModeEIntegrityAndHelpers.cs`
- 修改文件: `ModeE/ModeERespawnItems.cs`
- 修改文件: `ModeE/ModeEBattle.cs`
- 修改文件: `ModeF/ModeFEntry.cs`
- 修改文件: `ModeF/ModeFPhases.cs`
- 修改文件: `ModeF/ModeFRespawn.cs`
 - 修改文件: `Integration/DragonDescendant/DragonDescendantBoss.cs`
 - 修改文件: `Integration/DragonKing/DragonKingBoss.cs`
 - 修改文件: `Integration/PhantomWitch/PhantomWitchBoss.cs`
 - 修改文件: `tests/ModeEFSpawnPostprocessSchedulerGuard.py`
 - 修改文件: `tests/ManagedSpecialBossDeferredActivationGuard.py`
 - 修改文件: `tests/ModeESpawnFailureResolutionGuard.py`
**兼容性影响**: 不改 Boss 数量、阵营、刷怪点、安全距离、重刷 250ms 节奏、掉落/奖励、TypeID、配置或存档 schema。新增普通 Boss 激活前一帧屏障默认关闭，仅 Mode E/F 显式启用；不会出现已激活但未登记的跨帧窗口。BossRegen 仍在有非空缓存时每帧调用 `TickBossRegen` 推进内部 10 秒计时。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_dev.bat"` 通过
2. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过
3. Guard: `python tests\\ModeEFSpawnPostprocessSchedulerGuard.py` 通过
4. Guard: `python tests\\ManagedSpecialBossDeferredActivationGuard.py` 通过
5. Guard: `python tests\\EnemySpawnCoreObservableGuard.py` 通过
6. Guard: `python tests\\ModeFRespawnObservableSpawnGuard.py` 通过
7. Guard: `python tests\\ModeEFSpawnParityGuard.py` 通过
8. Guard: `python tests\\ModeESpawnFailureResolutionGuard.py` 通过
9. Guard: `python tests\\ArchitectureStructureGuard.py` 通过
10. 人工 smoke: 未运行
11. dev/profiler 日志: 2026-07-04 `latest.log` / `2026-07-04_11-20-22.log` 已覆盖 `PrepareModeEStartup`、`StartModeE` 与普通 Boss `ModeEFSpawnPostprocess`
**未验证/需人工**: 现有本机 dev 日志未覆盖 `ModeERespawn`（挑衅烟雾弹重刷）、`StartModeF`（Mode F 开局批量刷怪）、`ModeFRespawn`（Mode F 死亡补位）。已按文档实现并通过编译/guard，但仍需实机 profiler 与游戏内 smoke 才能断言问题已解决。
**失败尝试**: 无

---
### 2026-07-02 进存档时卡死在许愿台弹幕预热
**状态**: fixed
**Finding**: `Player.log` 排查
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要
**现象**: 进入基地存档后主线程卡死，最新日志停在 `WishFountain` 注册完 `OnBuildingBuilt` / `OnBuildingDestroyed` 事件后，不再继续打印"布满了灰尘的星愿许愿台建筑系统初始化完成"。
**根因**: `WishFountainView.CreateRuntime()` 在初始化阶段调用弹幕层预热，随后进入 `WishFountainDanmakuView.EnsurePoolCapacity()`。原实现用 `AcquireItem()` 在 `while (allocatedItemCount < targetCount)` 里反复从池中取出再放回；首个对象创建后，后续循环只会复用池中对象，不再增加 `allocatedItemCount`，导致循环永不退出，主线程卡在许愿台 View 运行时构建阶段。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainDanmakuView.cs`
- 修改文件: `FIX_TRACKER.md`

**兼容性影响**: 不涉及存档、配置、TypeID、外部协议或反射目标变更；仅修正弹幕对象池预热的容量补足逻辑，保持既有 UI 行为与资源结构不变。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: 未运行（本次未触及相关 guard 断言结构）
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏重新加载问题存档，确认不再卡死，且许愿台面板首次打开时弹幕层仍能正常显示。
**失败尝试**: 无

## 修复记录

---
### 2026-07-02 BossRush 动态物品重启后显示白底问号

**状态**: fixed
**Finding**: 玩家反馈 / 鸭科夫源码核对
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 玩家反馈 Mod 启用/禁用后装备和道具会恢复可用，但完整重启游戏后又变成白底问号占位图标；前置存在且排序在本 Mod 前。
**根因**: 官方 `ItemAssetsCollection.GetMetaData/GetPrefab/InstantiateSync/InstantiateAsync` 在 BossRush 延迟内容 bootstrap 完成前被存档、仓库、商店或 UI 按 TypeID 调用时，动态 prefab 尚未注册；`InstantiateSync` 会创建 `FallbackItem_<id>`，`GetMetaData` 也会返回默认 metadata，最终表现为白底问号或不可用占位。
**修复内容**:
- 新增文件: `Integration/BossRushDynamicItemRegistry.cs`（已加入 `compile_official.bat`）
- 新增文件: `Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs`（已加入 `compile_official.bat`）
- 新增文件: `tests/BossRushDynamicItemRegistryGuard.py`
- 修改文件: `Integration/BossRushIntegration.cs`
- 修改文件: `Integration/EquipmentContentRegistry.cs`
- 修改文件: `Integration/BossRushIntegration_StartAndScene.cs`
- 修改文件: `Integration/NewWeapons/Common/NewWeaponPlaceholderRegistry.cs`
- 修改文件: `Integration/Bonus/SetBonusPlaceholderRegistry.cs`
- 修改文件: `Integration/WikiBookItem.cs`
- 修改文件: `LootAndRewards/LootAndRewardsSpecialLoot.cs`
- 修改文件: `Integration/PhantomWitch/PhantomWitchScytheBootstrap.cs`
- 修改文件: `tests/DragonBossRewardContentPreloadGuard.py`
- 修改文件: `tests/PhantomWitchScytheRewardBundleGuard.py`
- 修改文件: `docs/contracts.md`
- 修改文件: `docs/架构说明/Harmony补丁契约稳定性.md`
- 修改文件: `docs/Bossrush使用物品ID表.md`
- 修改文件: `docs/制作教程/WikiBookUI_Guide.md`

**兼容性影响**: 不改 TypeID、存档 key、配置 schema 或资源命名；新增 Harmony prefix 覆盖官方按 TypeID 查询/实例化入口，按需精确加载已登记的 BossRush 资源。统一注册表优先复用现有 Config 常量和集中 TypeID 数组，避免多处重复维护 bundle/TypeID 映射；冒险家日志旧教程临时 ID `500100` 会在运行时收敛为发布 ID `500007`。属于向后兼容运行时兜底。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\BossRushDynamicItemRegistryGuard.py` 通过
3. Guard: `python tests\DragonBossRewardContentPreloadGuard.py` 通过
4. Guard: `python tests\PhantomWitchScytheRewardBundleGuard.py` 通过
5. Guard: `python tests\ContentRegistryGuard.py` 通过
6. Guard: `python tests\DeferredIntegrationBootstrapGuard.py` 通过
7. Guard: `python tests\SetBonusLifecycleGuard.py` 通过
**未验证/需人工**: 需要游戏内用包含 BossRush 自定义物品/装备的存档完整重启后进入基地/仓库/背包，确认 `500001-500056` 内已发布物品不再显示白底问号，装备可正常实例化、装备和使用。
**失败尝试**: 无

---
### 2026-07-02 已建许愿台在旧存档进基地时可能不显示

**状态**: fixed
**Finding**: `Player.log` 排查
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 基地加载时原版 `BuildingArea.Display` 在许愿台注入前先报 `No prefab for building starwish_fountain`；如果玩家存档里已经放过许愿台，该建筑可能不会在本次进档时被实例化出来，导致场景里不可见也无法交互。
**根因**: 许愿台建筑数据原本只在 deferred base-scene setup 阶段注入，时机晚于原版建筑区首轮显示；注入完成后又没有像婚礼教堂那样走一次基地建筑区重绘，因此"首轮缺 prefab"不会被补刷回来。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainBuilder.cs`
- 修改文件: `Integration/BossRushIntegration_StartAndScene.cs`
- 修改文件: `Integration/Wedding/WeddingBuildingInjector.cs`
- 修改文件: `Integration/Wedding/WeddingBuildingInjector_DataEventsAndRuntime.cs`

**兼容性影响**: 不涉及存档 schema、配置 key、TypeID、反射目标或资源命名；仅把许愿台建筑注入提前到基地场景早期，并复用基地建筑区重绘 helper 作为晚注入兜底。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\DeferredIntegrationBootstrapGuard.py` 通过
3. Guard: `python tests\\SceneObjectTypeCacheGuard.py` 通过
4. Guard: `python tests\\StaticCacheLifecycleGuard.py` 通过
5. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏加载一个已经建过许愿台的存档，确认基地首帧后建筑可见、可交互，且不再出现同轮次的缺 prefab 回归。

---
### 2026-07-02 许愿台弹幕当前打开轮次不接最新数据

**状态**: fixed
**Finding**: CR-2026-07-02-001
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 许愿台面板先用本地缓存或内存缓存启动弹幕后，联网成功拿到的新弹幕数据不会作用到当前这次打开；玩家要关掉再打开一次，才会看到更新后的内容池。
**根因**: `WishFountainUI.RefreshDanmakuDisplay()` 为避免中途整层重置，在"已有可见弹幕"场景下直接跳过成功回调里的显示更新，导致当前轮次只更新缓存、不更新在播来源。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainUI.cs`
- 修改文件: `Integration/WishFountain/WishFountainDanmakuView.cs`
- 修改文件: `CODE_REVIEW_FINDINGS.md`

**兼容性影响**: 不涉及存档、配置、TypeID、反射或外部协议；仅把联网成功后的新数据无缝切入后续入场弹幕，保留已有对象池与滚动状态。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\WishDanmakuJsonEscapeGuard.py` 通过
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏确认已有缓存时打开许愿台，联网返回后不会整层闪烁，且后续入场弹幕会换成最新数据源。

---
### 2026-07-02 许愿台弹幕失败结果被短 TTL 缓存，重开面板也不会立刻重试

**状态**: fixed
**Finding**: CR-2026-07-02-002
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 只要飞书鉴权或弹幕列表请求瞬时失败一次，玩家在接下来约 45 秒里重复关闭再打开许愿台，也只会马上复用失败结果或旧缓存，不会重新联网拉取。
**根因**: `TryReturnRecentDanmakuResult()` 把"最近一次失败"也纳入和成功结果相同的 TTL 快取，导致 reopen 无法触发新的网络请求。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainFetchPipeline.cs`
- 修改文件: `Integration/WishFountain/WishFountainService.cs`
- 修改文件: `CODE_REVIEW_FINDINGS.md`
- 修改文件: `FIX_TRACKER.md`

**兼容性影响**: 不涉及存档、配置、TypeID、反射或外部协议；仅将 TTL 缓存限定为成功结果，失败后允许玩家重开面板立即重试。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\WishDanmakuFetchLifecycleGuard.py` 通过
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏在弱网或临时断网后反复开关许愿台，确认恢复联网后无需等待 45 秒即可重新拉到弹幕。

---
### 2026-07-02 许愿台关闭后未解绑静态弹幕回调

**状态**: fixed
**Finding**: CR-2026-07-02-003
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 在慢网、切场景或频繁开关许愿台时，旧面板实例会一直被静态拉取回调引用到请求结束；虽然版本号判断会把回调变成 no-op，但这些闭包和 View 引用会额外滞留一段时间。
**根因**: `WishFountainService.RequestRecentWishes()` 把 success/failure lambda 追加到静态 waiter 委托中，而 `WishFountainView.CancelDanmakuFetch()` 只递增本地版本号，没有显式从静态 waiter 中退订。
**修复内容**:
- 新增文件: `tests/WishDanmakuFetchLifecycleGuard.py`
- 修改文件: `Integration/WishFountain/WishFountainFetchPipeline.cs`
- 修改文件: `Integration/WishFountain/WishFountainUI.cs`
- 修改文件: `CODE_REVIEW_FINDINGS.md`
- 修改文件: `FIX_TRACKER.md`

**兼容性影响**: 不涉及存档、配置、TypeID、Harmony/反射或资源结构；仅为弹幕拉取 waiter 增加显式解绑路径，降低旧 View 在慢网场景下的无效保留。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\WishDanmakuFetchLifecycleGuard.py` 通过
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏弱网下频繁打开/关闭许愿台并切图，确认不会报错、不会出现旧 UI 干扰下一次打开。

---
### 2026-07-01 "小明"非 Boss 预设误入 Boss 池

**状态**: fixed
**Finding**: 玩家反馈 / `Player.log` 排查
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: `Player.log` 先只能看到鸭鸭市场通知队列输出 `<color=red>小明</color> 将在 ... 秒后抵达战场` 与 `第 6/7 波: <color=red>小明</color> ...`；打开开发日志后确认该官方角色预设 `nameKey` 为 `Character_Ming`。
**根因**: Boss 池动态扫描当前按 `CharacterRandomPreset.showName`、阵营和基础血量筛选敌对预设；"小明"属于有显示名且满足基础条件的非 Boss 角色，因此绕过了既有 `showName=false` 小怪清理规则。
**修复内容**:
- 新增文件: 无
- 修改文件: `WavesArena/WavesArena.cs`

**兼容性影响**: 不涉及存档 schema、配置 key、TypeID、Harmony/反射或资源文件变更；仅在 Boss 池初始化/缓存清理阶段按稳定预设名 `Character_Ming` 硬排除已知非 Boss 角色"小明"。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\ModeEFPrewarmCacheGuard.py` 通过
3. Guard: `python tests\\ArchitectureStructureGuard.py` 通过
4. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏重新开一局 BossRush，确认 Boss 池配置窗口和波次预告不再出现"小明"。

---
### 2026-07-01 龙皇孩儿护我失效、龙裔同源复活风险与 Boss 名称兼容

**状态**: fixed  
**Finding**: 日志排查 / 玩家反馈  
**兼容分类**: COMPAT / WIRE+  
**版本/Commit**: 未提交  
**Owner decision**: 不需要  

**现象**: 玩家反馈更新后焚天龙皇"孩儿护我"不再触发；对照代码后确认龙裔遗族的一次性复活也存在同源风险。部分外部模组会把 BossRush 自定义 Boss 名称显示成 `Unknown`。最新 `Player.log` 里还能看到婚礼建筑初始化阶段触发的 `BuildingArea.RepaintAll()` 原版 `Debug.LogError`。  
**根因**:  
1. 原版 `Health.Hurt()` 在当前官源中的执行顺序为"扣血 -> 死亡分支 -> `OnDeadEvent` -> `SetActive(false)` -> `OnHurtEvent`"。龙皇孩儿护我和龙裔复活都挂在 `OnHurtEvent`，致死伤害会先把对象送入死亡路径，导致保命机制来不及触发。  
2. 三只自定义 Boss 的运行时 `CharacterRandomPreset.name` 使用内部 `*_Preset` 名称，且龙皇/幽灵女巫缺少 `Characters_` / 运行时 preset 名称别名，本地化兼容键不完整，外部模组若读取 `preset.name` 或兼容键，容易回退成 `Unknown`。  
3. 婚礼建筑系统初始化时无条件重绘基地建筑区，即使当前存档没有放置婚礼教堂，也会提前触发一次原版建筑重绘。  
**修复内容**:
- 新增文件: `Patches/Combat/BossLethalHealthProtectionPatch.cs`（已加入 `compile_official.bat`）
- 修改文件: `Integration/DragonKing/DragonKingAbilityController_AttackFlow.cs`
- 修改文件: `Integration/DragonDescendant/DragonDescendantAbilities_ResurrectionAndPhase.cs`
- 修改文件: `Integration/DragonKing/DragonKingBoss.cs`
- 修改文件: `Integration/DragonDescendant/DragonDescendantBoss.cs`
- 修改文件: `Integration/DragonDescendant/DragonDescendantBoss_RuntimeAndCleanup.cs`
- 修改文件: `Integration/PhantomWitch/PhantomWitchBoss.cs`
- 修改文件: `Localization/EquipmentLocalization.cs`
- 修改文件: `Integration/Wedding/WeddingBuildingInjector.cs`
- 修改文件: `compile_official.bat`

**兼容性影响**: 不涉及存档 schema、配置 key、TypeID 变更；新增一处针对 `Health.Hurt` / `Health.CurrentHealth` 的 Harmony 兼容补丁；运行时 Boss preset 的 `name` 统一改为稳定 `BossNameKey`，仅影响运行时识别兼容性。  
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat`
2. Guard: `python tests\\BossCleanupSharedHelperGuard.py`
3. Guard: `python tests\\DragonKingBossEventLifecycleGuard.py`
4. Guard: `python tests\\DragonKingChildProtectionTransformCacheGuard.py`
5. Guard: `python tests\\SceneObjectTypeCacheGuard.py`
6. Guard: `python tests\\DeferredIntegrationBootstrapGuard.py`
**未验证/需人工**:
- 游戏内实机确认龙皇"孩儿护我"可再次触发，且击杀其召出的龙裔后能正常联动处死龙皇。
- 游戏内实机确认龙裔遗族一次性复活恢复为 50% 血量后仍能完整进入狂暴二阶段。
- 使用玩家提到的外部模组实机确认三只自定义 Boss 不再显示为 `Unknown`。
- 若存档里已经放置婚礼教堂，需要在基地场景实机确认跳过无意义重绘后，旧存档中的教堂仍能正确显示。

---
### 2026-07-01 售货机 UI 崩溃（延迟注入导致 itemInstances 未缓存）

**状态**: fixed  
**Finding**: CR-2026-07-01-001  
**兼容分类**: WIRE- / OPERATIONAL  
**版本/Commit**: `64c4572`（旧记录）  
**Owner decision**: 不需要，属于 P0 回归修复  

**现象**: 进入基地打开售货机，UI 无法显示，`Player.log` 出现 `StockShopItemEntry.Setup()` 相关 `NullReferenceException`。

**根因**: 性能优化把商店条目注入改为延迟执行，晚于原生 `StockShop.Start()` 的 `CacheItemInstances()`。延迟注入条目的物品实例没有进入 `itemInstances` 缓存，后续 UI 取实例返回 null。

**修复内容**:

- 新增文件: `Patches/Economy/StockShopGetItemInstanceDirectPatch.cs`（旧记录显示已加入 `compile_official.bat`）
- 修改文件: `compile_official.bat`

**修复原理**: 在 `StockShop.GetItemInstanceDirect(typeID)` 入口兜底缓存未命中的延迟注入条目，使补丁与注入时机解耦。

**兼容性影响**: 原生商品缓存命中路径不变；BossRush 延迟注入商品首次访问多一次同步实例化。

**验证方法**:

1. Windows 编译：`compile_official.bat`
2. 人工 smoke：基地打开售货机，确认 BossRush 注入商品显示且可购买。

**未验证/需人工**: 本次文档收敛未重新运行游戏，仅迁移旧 confirmed 记录。

**失败尝试**: 旧记录中的 `StockShop.Awake` 早期注入补丁受 `ModBehaviour.Instance` 初始化时序影响，不能稳定解决。

---
### 2026-08-07 Wiki 右书页越界及偶数页跳过

**状态**: fixed
**Finding**: 两轮玩家截图反馈 / 第二轮实机复测
**兼容分类**: COMPAT
**版本/Commit**: 第一轮 `7dd1e15` / 本轮未提交
**Owner decision**: 不需要

**现象**: 第一轮 Wiki 文章右侧书页末尾文字绘制到书页及底部 UI 之外；第一轮修复后右书页变为空白，每次翻页只显示奇数物理页，内容不全且跳过偶数页。

**根因**: 初始实现让右页把左页分页结果切片后以无边界 `Overflow` 二次排版，导致密集内容越界。第一轮修复又让两个独立 TMP 对全文分别计算 `Page` 并用奇偶 `pageToDisplay` 取页；实机中右 TMP 没有产生与左 TMP 等价的高页码网格，偶数页因此全部空白，但书本索引仍按两页递增。

**修复内容**:

- 修改 `Integration/WikiUIManager.cs`：仅用左 TMP 对全文生成一次权威分页边界，将连续且无间隙的源文本区间缓存为页块；左右页各显示对应页块的第 1 个 TMP 页，不再让右 TMP 独立重算全文高页码。页块边界补齐富文本标签，显示端继续使用 `Page` 模式限制页面矩形。
- 二次审核新增逐页校正：每个缓存页块在最终落位的左右 TMP 组件上强制排版，产生多页时继续按第 2 页边界拆分，避免富文本补齐或左右矩形差异再次隐藏尾部内容。
- 每次打开 Wiki 都幂等调用 `LoadCatalog()`；官网入口不再作为默认内容分类，也不会路由到不存在的 `_wiki_link__home.md`。
- 更新 `tests/WikiUIPageOverflowGuard.py`：同时守卫连续页块边界、左右页按相邻索引消费、显示端固定第 1 页及禁止 `Overflow`/右页独立全文分页。
- 新增 `tests/WikiUIRuntimeFlowGuard.py`：守卫重新打开时恢复目录、官网入口路由，以及 99 个游戏内条目的中英文内容文件完整性。

**验证方法**:

1. Windows 编译：`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过；最新构建因 `Duckov.exe` 正在运行而未覆盖游戏目录 DLL
2. Guard：`python tests\\WikiUIPageOverflowGuard.py` 通过
3. Guard：`python tests\\OfficialCompileListFileExistenceGuard.py` 通过
4. Wiki guards：`BossWikiGuideContentGuard.py`、`WikiUIPageOverflowGuard.py`、`WikiUIRuntimeFlowGuard.py`、`ZombieModeMutantWikiGuard.py` 全部通过
5. 目录静态审计：100 个 catalog 路由无重复；排除 1 个官网伪条目后，99 个游戏内条目均有中英文 Markdown，且无孤儿文件
6. 当前 `Player.log` 未发现 Wiki/TMP 异常，但本轮没有打开 Wiki 的运行时日志证据

**未验证/需人工**: 需进游戏打开 Wiki 的"末日丧尸模式"文章，连续检查第 7/15、13/15、14/15 页，确认左右页都有内容、文字不越界、相邻页首尾连续且末页无正文缺失。

**失败尝试**: `7dd1e15` 删除右页页块渲染，改为右 TMP 对全文独立设置偶数 `pageToDisplay`；静态 guard 只检查了 API 形态，没有覆盖两个 TMP 的实机分页状态是否等价，导致右页空白和偶数页跳过。

---
### 2026-08-07 丧尸模式奖励有效性、终端反馈与临时 NPC 保护性能

**状态**: fixed
**Finding**: 奖励/终端专项审查
**兼容分类**: COMPAT
**版本/Commit**: 本提交
**Owner decision**: 不需要

**现象**:
- `CurrentNodeFreeRefresh` 与 `NextNodeFreeRefresh` 会进入随机奖励，但每节点免费刷新初始值已经等于上限；前者实际变成未写入文案的 30 净化点，后者会被同一上限截断。
- 补给保底、付费刷新半价和护士终端在对应状态已生效时仍可重复抽到，重复选择没有叠加收益或会生成不可访问的重复终端。
- 补给/医疗终端不显示当前净化点，也不区分余额不足；副标题还包含硬编码中文。
- 临时 NPC 保护轮询会为每个 NPC 重复扫描敌方目标，并反复分配 `GetComponentsInChildren<Health>()` 结果；已销毁的 RewardUi run-only 记录也会随反复刷新/开关逐步累积。

**修复内容**:
- 从随机目录移除两个当前无法兑现的免费刷新选项，保留 enum、状态与本地化兼容面；为已生效的保底、半价和护士终端增加选择上限过滤。
- 补给终端复用既有 `Drink` 标签和本地化增加饮料库存；终端显示净化点余额、价格、库存及余额不足状态，付费刷新同步提供余额颜色反馈。
- 缓存临时 NPC 的 `Health[]`，威胁目标扫描改为生成时一次、每次保护轮询末尾一次；注册新 RewardUi 时剪枝已销毁且无 cleanup action 的旧记录。
- 新增 `tests/ZombieModeRewardTerminalOptimizationGuard.py`，并同步本地化、商店显示名和临时 NPC 保护 guards。

**验证方法**:
1. Windows 编译：`cmd.exe /c compile_official.bat` 通过；自动部署失败，未覆盖游戏目录 DLL。
2. 相关 16 个奖励/终端/生命周期 guards 全部通过。
3. 全量 ZombieMode guards：148/148 通过；`ZombieModeSpawnSelectionSqrGuard.py` 已同步动态距离 helper 并通过。
4. `git diff --check` 通过。

**未验证/需人工**: 需游戏内确认普通/Boss 终端的饮料库存、余额不足颜色与提示、护士服务、奖励刷新、重复进出终端、撤离/失败及第二局清理。

---
### 2026-08-07 丧尸模式 Boss 数量取消地图规模联动

**状态**: superseded
**Finding**: 进游戏前专项复审
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 初版固定单 Boss；后续已被 owner 最新"Boss 数量随波次无上限递增"决策覆盖

**现象**:
- Boss 波数量由有效刷怪点数量计算，使用 `modeESpawnPoints` 后，不同地图第 5 波可能生成约 4~46 个 Boss。
- 奖励页预告显示统一移速倍率，但 Boss 本身跳过该速度曲线，容易误解为 Boss 也被减速。

**修复内容**:
- 每个 Boss 波固定生成 1 个 Boss，彻底取消地图大小、刷怪点数量和波次数量换算。
- 保留 Boss 类型轮换、支援怪压力、污染与技能强度作为后续难度来源。
- 奖励预告和中英文 Wiki 明确速度曲线只作用于非 Boss 敌人；Boss 潮表格标明仅支援怪使用该倍率。
- 扩展 `ZombieModePacingTuningGuard.py`，守卫固定单 Boss 常量，并禁止重新引入 `GetZombieModeBossCount` 或刷怪点计数依赖。

**验证方法**:
1. Windows 编译：`cmd.exe /c compile_official.bat` 通过。
2. 自动部署：Build 与游戏加载目录 DLL 的 SHA-256 均为 `F4D9E0C3B1AD9DC3D08316EE002CA4E15495859A1F0932512C9287E289979DAB`。
3. 全量 ZombieMode guards：148/148 通过。
4. `ZombieModePacingTuningGuard.py`、`ZombieModeLocalizationGuard.py`、`OfficialCompileListFileExistenceGuard.py`、`ArchitectureStructureGuard.py` 通过。
5. VitePress Wiki 构建通过；`git diff --check` 通过。

**未验证/需人工**: 需进游戏推进到第 5 波，确认只出现 1 个 Boss、支援怪压力约为 8、击败 Boss 后正常进入奖励与撤离机会。

**后续覆盖**: 地图规模解耦继续保留；"所有 Boss 波固定 1 只"已由后续的纯波次递增公式替代，详见"丧尸模式 Boss 数量与后期变异权重持续成长"。

---
### 2026-08-07 丧尸模式 Boss 风险收益与肉鸽成长曲线

**状态**: fixed
**Finding**: Owner 玩法反馈 / 进游戏前专项复审
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 保持 Boss 数量与地图无关；初版固定单 Boss，数量部分已被后续的无上限轮次成长决策覆盖；后续轮次必须同时提高难度、战利品和肉鸽成型速度

**现象**:
- 固定单 Boss 解决了追逐战同时塞入大量 Boss 的问题，但 Boss 本体属性、击杀收益、奖励箱和奖励选择没有按 Boss 轮次成长，后期风险收益曲线被压平。
- 既有支援尸潮会逐轮增加，但玩家每个 Boss 节点始终只能拿一项奖励，缺少"越难、越富、成型后越爽"的正反馈。

**修复内容**:
- 第 5/10/15/20 波 Boss 生命倍率为 100%/130%/160%/190%，伤害倍率为 100%/112%/124%/136%；生命最高 250%，伤害最高 180%。倍率覆盖普攻、泰坦冲击波、猎手突进、腐化区域/毒径及 Boss 死亡效果，不增加 Boss 移速。
- 保留单 Boss 和支援压力 8/11/14/17/20；Boss 击杀净化点和奖励页净化点按每轮 +25% 成长，最高 300%。
- Boss 奖励箱每轮增加 1 件物品，数量从 6~9 成长到 10~13；最高品质随轮次提升至 Q8，最低品质每两轮提升 1 级。界面将百分比明确标为净化收益，避免误解为整箱数量倍率。
- 第 10 波起先提供一次属性/弹道/触发/局内变异的纯战斗强化 4 选 1，再提供原完整奖励池 4 选 1；奖励入口校验选项仍属于当前节点，避免失效按钮重复提交。
- 奖励页 Boss 预告显示生命、伤害、支援压力和战利品倍率；中英文 Wiki 与相关 guards 同步。

**验证方法**:
1. Windows 编译：`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过并自动部署。
2. Build 与游戏加载目录 DLL 的 SHA-256 均为 `00C95DADC5A769CAE555B51C816E266F7800977D6EA97E506029AAC77BE630D5`。
3. 全量 ZombieMode guards：148/148 通过。
4. VitePress Wiki 构建通过；`git diff --check` 通过。

**未验证/需人工**: 需游戏内至少推进到第 10 波，确认第 5 波仍为一次完整奖励，第 10 波先后出现两次奖励选择且只在第二次选择后进入撤离机会；同时检查 Boss 技能伤害、11 只支援压力、125% 净化收益和 7~10 件奖励箱内容。

**失败尝试**: 首次最终编译发现奖励选项有效性校验的局部变量作用域错误；已把校验收口到 `SelectZombieModeReward()` 入口并重新编译通过。

---
### 2026-08-07 丧尸模式 Boss 数量与后期变异权重持续成长

**状态**: fixed
**Finding**: Owner 最新玩法反馈 / 进游戏前专项复审
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: Boss 数量只随 Boss 波次持续递增、不按地图计算且不设玩法上限；后期精英与特殊丧尸应占绝大多数

**现象**:
- 固定单 Boss 使数量维度在后期不再成长，虽然单体、支援和奖励质量提高，但缺少持续升级的压迫感。
- 第 6 波后的精英/特殊概率仅由污染档位决定，存在明确上限，无法形成后期由变异丧尸主导的尸潮。

**修复内容**:
- Boss 数量改为 `1 + 已进入的五波轮次数`：第 5/10/15/20/25 波为 1/2/3/4/5 只，之后每个 Boss 波继续 +1；公式不读取地图、刷怪点或场景数据，也没有 Boss 数量 Maximum。
- 多 Boss 波会等待本波全部 Boss 死亡后再结算；Boss 类型沿现有五种顺序轮换。每只 Boss 继续独立掉落净化星和奖励箱，因此总风险与总收益都随 Boss 数量持续成长。
- Boss HUD 的总数改用同一波次公式，不再随逐帧生成的实例数从 0 开始增长；生成期间即可稳定显示本波完整进度。
- 第 6 波起普通权重固定为 100，精英/特殊权重分别在污染基础上每波 +3/+5；合计概率持续逼近 100%，同时保留精英和特殊两类。污染 0 时，第 20/50/100 波的精英+特殊占比约为 55.8%/78.5%/88.5%。
- 奖励页 Boss 预告增加数量，中英文 Wiki 同步数量表、无上限规则、逐只掉落和后期概率示例。
- 更新 `ZombieModePacingTuningGuard.py`，守卫波次公式、无数量上限、地图解耦、后期权重、全 Boss 结算和逐只掉落。

**验证方法**:
1. Windows 编译：`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过。
2. 自动部署失败后使用显式目标路径完成部署；Build 与游戏加载目录 DLL 的 SHA-256 均为 `A5AE49DDF9C86AC269C69FF97266F064AC0AAB5C7D5441ABA0A767194FBE627B`。
3. 全量 ZombieMode guards：148/148 通过；`ZombieModePacingTuningGuard.py` 覆盖本轮新契约。
4. VitePress Wiki 构建通过，198 篇内容完成同步；中英文丧尸 Wiki 与生成页守卫通过。
5. `git diff --check` 无 whitespace error，仅输出仓库既有 LF/CRLF 转换提示。

**未验证/需人工**: 需游戏内推进或调试跳转至第 5/10/15 波，确认 Boss 数量为 1/2/3、HUD 进度递减、最后一只死亡后才结算、每只均有净化星和奖励箱；极高波次大量 Boss 的帧率与寻路压力仍需实机观察。按 owner 明确要求未添加数量上限。

---
### 2026-08-07 Mode E 挑衅烟雾弹与混沌引爆器无法使用

**状态**: fixed
**Finding**: 玩家实机反馈 + `Player-prev.log` 运行时证据
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: Mode E 中挑衅烟雾弹和混沌引爆器的使用按钮长期为不可用状态；日志反复提示场上 Boss 数量已达上限。

**根因**: 重刷安全门把存活 Boss 上限固定为 64，但本次实机地图的 Mode E 初始生成任务为 100。玩家击杀 10 个 Boss 获得挑衅烟雾弹时，剩余人口仍高于 64，因此两个重刷道具必然被 `CanBeUsed()` 拒绝；该固定上限低于模式自身的合法初始规模。

**修复内容**:
- 初版曾把固定 64 上限放宽为 `max(64, 当前地图 Mode E 初始刷怪点数量)`；该方案已被 owner 最新决定覆盖。
- 删除存活 Boss、待生成 Boss、地图初始人口、可用名额和超额裁剪的全部门控；道具可用性不再受场上 Boss 数量影响。
- 挑衅烟雾弹始终尝试最近 10 个刷怪点，混沌引爆器始终尝试全图全部刷怪点。
- 保留单一重刷任务、Mode E 激活检查、切图/模式结束后的会话校验和每个 Boss 间 250ms 的分帧生成，避免并发任务破坏状态或误消耗道具。
- 更新相关 Mode E guards，明确禁止重刷人口上限、名额计算和点位裁剪回归。

**验证方法**:
1. Windows 编译：`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过；构建 SHA-256 为 `755C9C57F114F0F5EA2A7978D39172424E7791C0B0BB97E6C01C19BC0A99065D`。
2. Mode E 相关 guards 39/39 通过；另有 `ContentRegistryGuard.py`、`OfficialCompileListFileExistenceGuard.py` 通过。
3. `git diff --check` 通过，仅有仓库既有的 LF/CRLF 转换提示。

**未验证/需人工**: 编译时游戏仍在运行，加载目录 DLL 被 `Duckov` 进程占用，未能覆盖部署；关闭游戏后需部署新 DLL 并重启。实机需确认人口高于 64 或地图初始规模时按钮仍可用；挑衅烟雾弹生成 10 个 Boss、混沌引爆器覆盖全图全部点；前一任务完成前第二次使用仍被拒绝且不误扣道具。

---
### 2026-08-07 Mode E 抽奖布局与全阵营 Boss 雇佣

**状态**: fixed
**Finding**: 玩家实机截图反馈 + `鸭科夫源码/TeamSoda.Duckov.Core/Duckov/Economy/UI/StockShopView.cs`、`Health.cs`、`CharacterMainControl.cs` 静态复核
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 未提交
**Owner decision**: 贝壳余额与原版现金/银行存款放在左上角同一货币栏；抽奖按钮回到商店面板外；所有 Mode E Boss 不限原阵营均可雇佣，雇佣 Boss 造成的击杀按玩家击杀结算

**现象**:
- 抽奖按钮挂在 `refreshCountDown` 的窄父节点上，分辨率或 Canvas 缩放变化后会与余额、倒计时错位。
- 第一版独立操作行仍作为共同标题容器的子节点，实际标题容器只有单行可见范围；操作行位于倒计时下方后被父级裁剪，导致余额和抽奖同时消失。
- 第二版改挂根节点后仍用 `merchantNameText` 决定左边界；当前分类商店标题位于右侧，导致余额被带到画面中部。抽奖按钮同时使用纵向拉伸锚点和额外 40 高度，实际高度翻倍，并因手工拼装背景而退化成右上角的小号灰字。
- 雇佣交互仅为玩家出生阵营的 Boss 注册，敌对阵营和特殊托管 Boss 无法雇佣；雇佣单位击杀仍以 NPC 作为 `DamageInfo.fromCharacter`，原版经验、击杀计数和任务不会统一视为玩家击杀。

**修复内容**:
- 贝壳余额不再占用商店面板标题行；运行时复用原版 `ContextualMoneyAndCash.cashDisplay` 完整现金胶囊，在同一父布局中插到现金右侧，并优先把现金图标替换为 `SeaShell` 物品图标。克隆体移除原版 `CashDisplay`/`MoneyDisplay` 更新器，余额只由当前 Mode E 会话事件刷新；图标不可用时回退显示"贝壳/ Shells + 数量"。
- 抽奖按钮继续完整复用已正常显示的一键卖出按钮视觉层级并清除旧监听，但不再挂入商品网格标题行；按钮直接挂到无遮罩的 `StockShopView` 根节点，固定 132x32 单行尺寸，优先位于刷新倒计时左侧的商店外部空位，窄分辨率才回退到倒计时下方。
- 移除 Boss 雇佣的出生阵营限制。成交时复用原 Boss 实例，切换至玩家阵营，清空旧 AI 仇恨，迁移 Mode E 存活阵营追踪，重设死亡缩放基线并取消该实例后续贝壳奖励；任一步失败会恢复阵营、AI、追踪、缩放和奖励状态并返还本次扣款。成交后由唯一 owner 阵营守卫阻止托管技能把龙王、龙裔、幽灵女巫等实例改回中立或敌对阵营。
- 复用 `FrostmourneZombieFollower` 处理普通与托管 Boss 跟随；无玩法数量上限，当前价格仍按 Boss 出生最大生命计算，并按存活雇佣数逐次翻倍。
- 在原版 `Health.Hurt(DamageInfo)` 已确认死亡、但尚未发放经验和触发死亡事件的位置注入击杀归属：仅 Mode E 且来源命中已雇佣 Boss 字典时，把死亡结算使用的 `fromCharacter` 改为唯一 owner。普通受伤及致死伤害的 Buff、护甲、AI 战斗系数和扣血均保留 Boss 原始来源；归属查询只在死亡时执行，且为 O(1)、无分配。
- Boss 生成时只刷新新建雇佣交互，避免批量生成形成 O(n²)；普通 Boss 死亡不再全量刷新报价，仅已雇佣 Boss 死亡导致倍率变化时刷新。
- Boss 贝壳基础奖励取消最大生命分档与 64,000 生命封顶，直接按出生最大生命连续计算：按 owner 最新数值决定以 500 生命锚定 10 贝壳，每翻倍增加 3 贝壳，只在最终结果向上取整；低生命同样连续下降且最低 1，高生命继续增长。小怪晋升、近距离助攻和首个正奖励倍率继续复用原结算规则。

**验证方法**:
1. Windows 正式编译 `compile_official.bat` 通过；最新构建与游戏加载目录 DLL 的 SHA-256 均为 `E71CF588FA1F378CB5FFD07CB93DE228A492C81F0524D3974CA3E36C299B74D8`，已成功部署。
2. Mode E、编译清单、事件生命周期和致死保护相关 guards 31/31 通过；其中包含 `ModeEBossHiringGuard.py`、`ModeELotteryGuard.py`、`ModeEShellHarmonyUiContractGuard.py`、`OfficialCompileListFileExistenceGuard.py`、`ReverseScaleLethalProtectionGuard.py` 与 `EventSubscriptionLifecycleGuard.py`。
3. `git diff --check` 通过，仅有仓库既有 LF/CRLF 转换提示。
4. 全量 Python guard 398/404 通过；其余 6 项均已在 `HEAD` 复现，分别为火箭分裂视觉旧断言、空 catch/大文件预算旧债务、`ModBehaviour.Instance` 分类基线过期、run-scoped registry 旧断言及部署后尚未重跑游戏导致的 stale smoke log。本轮空 catch 总数由 `HEAD` 的 869 降为 868。

**未验证/需人工**: 需重启游戏后实机确认贝壳胶囊紧跟左上角现金/银行存款、显示海贝图标且余额实时刷新，反复关闭/打开不同分类商店时不会重复或残留；抽奖按钮应完整显示在刷新倒计时左侧且不遮挡倒计时与商店。另需分别雇佣普通 Boss、龙王、龙裔、幽灵女巫等特殊 Boss，确认转换后不攻击玩家、能正常使用技能攻击其他阵营，且其击杀会增加原版经验、存档击杀计数、任务进度和 Mode E 玩家成长。

**失败尝试**: 初版在 `Health.Hurt` Prefix 中提前把每次雇佣 Boss 伤害的 `fromCharacter` 改为玩家，会跳过 NPC 对 NPC 的 `aiCombatFactor` 计算，并改变 Buff 来源和受击仇恨；提交前复审已改为死亡确认点注入。

---
### 2026-08-07 丧尸模式前期密度与可靠补怪

**状态**: fixed
**Finding**: 玩家实机反馈 / 刷怪链路专项复审
**兼容分类**: COMPAT
**版本/Commit**: `05db0b0`
**Owner decision**: 前期起始压力、补怪频率和普通丧尸硬上限至少提高到旧值 3 倍；准备期继续在安全区外刷怪，安全区阻挡与仇恨压制保持不变

**现象**:
- 前几波场上丧尸太少，第一波目标压力仅 8、补怪间隔 3 秒、硬上限 50，玩家很快清空视野。
- 严格距离地图点和虚拟 NavMesh 点都失败时会整只跳过；远处仍存活或被外部销毁但未走死亡事件的丧尸还会占用存活计数，表现为附近看不到怪且不再补怪。

**修复内容**:
- 第一轮普通波场上压力从 8 提到 24，后续阶段增量、每五波压力增量、准备期 12~48 压力、Boss 支援 24~60 和普通丧尸硬上限 150 均按至少 3 倍调整；击杀目标保持旧曲线 18/24/30/38，每五波仍只增加 18。
- 准备期补怪间隔从 5 秒缩到 1.65 秒；普通波从 3.0/2.6/2.2/1.8 秒缩到 1.00/0.86/0.72/0.58 秒，Boss 支援同样按至少 3 倍频率补充。单批数量保持原值，继续逐只异步生成并用 pending 名额守住上限，避免单帧同步创建大批 AI。
- 统一可靠刷新选择器：优先使用玩家周围 12 点轮转的可达 NavMesh 环，再使用一次扫描同时得到严格距离地图点与不小于 12 米的安全回退；前两波/第 3~5 波/后续优先距离调整为 22/20/18 米。安全区半径仍为 8 米，准备期生成点仍在安全区外。
- 每轮补怪前复用运行时 marker scratch 校准普通/总存活计数；非 Boss 丧尸持续 8 秒位于玩家 60 米外时复用同一可靠刷新选择器回收到附近，防止失联怪长期占满名额。没有新增每帧全场景扫描、集合分配或同步批量刷怪。
- 更新 4 份中英文 Wiki 与丧尸模式节奏、上限、刷怪选择和平方距离 guards。

**验证方法**:
1. Windows 正式编译 `cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过；本轮首次构建与部署 SHA-256 均为 `81064A3B6D7A7980CEC556B51A84E06015F3FEB18E8E858F73AF25DFA500B970`。随后并行流程在游戏启动前写入当前部署 DLL（SHA-256 `3FA0E2F0944541ADF3BE4A058982D42AE9658D0ECC4F286BF2408A3B475E53C5`）；反射核对确认其击杀目标、压力、上限、补怪间隔及可靠刷新/计数校准/远怪回收方法均为本轮新实现。
2. 全量 ZombieMode guards 148/148 通过；敌人恢复、架构和编译清单相关 guards 全部通过。
3. VitePress Wiki 同步并构建通过，198 篇内容完成同步。
4. `git diff --check` 无 whitespace error，仅有仓库既有 LF/CRLF 转换提示。

**未验证/需人工**: 当前部署 DLL 已由 `Duckov` 进程加载，尚需游戏内 smoke。重点检查初始准备期约 12 只、第一波约 24 只场上压力但击杀目标仍为 18、1 秒补怪、环形分散生成，以及故意远离尸群后 8 秒左右重新回收到附近。

**失败尝试**: 首次 Wiki 构建调用 `npm.ps1` 被本机 PowerShell 执行策略拦截；改用同一 Node 安装下的 `npm.cmd run build` 后通过。

---
### 2026-08-10 Mode E 抽奖按钮按真实倒计时层级定位
**状态**: fixed
**Finding**: 玩家实机 `Player.log` 的 `[ModeE/UIHierarchy]` 诊断
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**根因**: 旧逻辑按纵向邻近合并整个 `StockShopView` 的 TMP 字形边界，把顶部中央"交易"和左右标题一起纳入倒计时范围，导致右侧倒计时中心 `x≈1069` 被错误拉到 `x=12`。
**修复内容**:
- 直接使用官方 `StockShopView/Content/MerchantStuff/Title/CountDown` 完整行作为唯一锚点，不再扫描或合并其他 TMP 文本。
- 抽奖按钮占用原倒计时行中心；完整"下次刷新 + 时间"行迁到根节点并放在按钮正上方，绕开 `Title` 的 `VerticalLayoutGroup` 裁剪与重排。
- 按钮扩大到 `256x54`，字体自动范围提高到 `18-24`。
- 缓存倒计时行的父级、兄弟序号、RectTransform、旋转、缩放和激活状态；关闭、失效、重复 Attach、静态清理或创建异常时恢复官方层级并请求布局重建。
- 实机确认位置正确后，删除用于定位的完整 UI 层级诊断及其编译登记，避免 DevMode 开店时输出约 9,400 行日志。
**验证方法**:
1. `ModeELotteryGuard.py`、`ModeEMerchantOpenReuseGuard.py`、`ModeEShellRuntimeLifecycleGuard.py`、`OfficialCompileListFileExistenceGuard.py` 通过。
2. 删除诊断后，`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 正式编译成功并部署。
3. 玩家已确认位置正确；重复开关商店的恢复路径仍由静态守卫覆盖。

### 2026-08-13 Mode G 完整性收口

**状态**: fixed（代码/自动化验证）；正式入口继续 fail-closed，人工 smoke 未完成
**兼容分类**: COMPAT / SCHEMA+ / OPERATIONAL
**修复**:
- 固定九波计划、首胜/复战署名节拍、宿敌第 3/6/9 波 R1/R2/R3 与墓碑语义；自动入口统一进入契约二选一确认页，取消不扣船票/信物。
- 距离轴只消费第 1/4/7 波最后一名 Boss 的可信 terminal distance；属性轴、R3 契约和 Last Stand 统一走 metadata 直伤 family。Last Stand 仅 12 秒内可计分直接处决得 Resolve，超时只触发复仇强化。
- 移除“最近 0.5 秒开枪”猜 Gun/Melee：要求 `fromWeaponItemID>0`，使用 `GetMetaData` 明确 tag 并以 64 项有界缓存复用；TypeID 0、未知、Buff/effect、污染均不计分。幽灵女巫镰刀领域增加 exact-Health 同步 telemetry suppression，实际伤害、死亡归因和 Legacy 不变。
- 弹药威胁改为 `TargetBulletID -> GetPrefab -> BulletThreatProfile` 的 32 项纯数据缓存，修复最后一发 `BulletItem` 为空/回退；候选排除已点名，支持单发 35% / 多发 15% 门槛，排序后重新计算权重。学习波前 2 秒 CalmGate、生成期预射污染、休整公布即武装、违规跨 BeginWave 保留均已显式接线；弹药 Resolve 改为有效禁令 + 零违规 + 可计分直接终结。
- profile/nemesis typed Save 增加关键字段回读；共享 coordinator 在官方保存繁忙时有界顺延、冻结存档槽 generation、续接运行中 dirty，每批最多一次 `SaveFile(false)`。切槽/删档取消旧批次，单局结束不退订，故障在扣票前 fail-closed。
- 入口事务收口：地图 UI 预扣船票在契约页取消、打开失败或启动前失败时幂等退回；Mode G 意图在过图前冻结，避免加载帧玩家对象未就绪时误判。通用过图路径依冻结意图延后 `bossRushArenaActive`、刷怪器禁用和清场，避免确认期 NPC/商店误判；三者只在 runtime 成功推进 Active 后、首波生成前统一提交。
- 补齐奖励 strict 单件失败语义、背包满缓冲、胜利无敌窗口、factory 15 秒超时与 late cleanup；信物 TypeID 500057 的售货机、编译清单和 Bundle 自动部署链路保持接通。
**验证**: 2026-08-14 Windows `compile_official.bat` 正式编译通过并部署 `Build/BossRush.dll`，构建/部署 DLL SHA-256 均为 `D5D610BFC0515914E31DCF20FE1D356C1EF2ED34913C6A476EAA936CEB281FEF`；全部 28 个 `ModeG*Guard.py` 通过；Mode G 展示 bundle 112399 B（<256 KiB），信物 bundle `Assets/Items/fate_echo_relic` 已部署且源/部署哈希一致；`git diff --check` 通过（仅 LF/CRLF 提示）。`ModeGAvailability.IsProductionReady=false`、`AllowDevTestEntry=false` 保持不变。
**待人工**: 售货机购买 500057、裸装+船票+信物自动传送、契约确认/取消、九波三幕、R1/R2/R3 宿敌、距离/弹药/属性三轴、CalmGate/生成期预射/禁令公布期、Last Stand 成功/超时/非计分末击、R3 直伤契约、奖励背包满/死亡/切图中断、factory 超时与迟到清理、多存档槽/删档/保存 busy/故障注入、Legacy/Mode D/E/F/Zombie 回归及 Profiler。复玩率与经济仍需真实玩家样本，不能由静态守卫代替。

### 2026-08-17 Mode G 二次完整审查与发布门禁收口

**状态**: fixed（静态实现与自动化）；正式入口继续 fail-closed
**兼容分类**: COMPAT / SAFE / OPERATIONAL
**修复**:
- 奖励交付按实际归属核对 `AddAndMerge` / `SendToPlayerStorage` 的回调异常，背包、仓库、地面 agent 三路均失败后才销毁；Rewarding 死亡/切图/销毁先失效 nonce 再取消 materializer，死亡路由独占终局原因。
- 启动支付显式转移退款所有权，首波同步失败、HUD/后续异常不再双退或错退；Runtime 退款逐项检查真实交付结果。
- 幕伤害、距离适应、Rank 与性格 Modifier 在 Boss 槽提交前逐项验证，候选失败只回滚本候选并尝试 reserve，统一 cleanup 仍精确移除全部已记录 Modifier。
- profile/nemesis 对未知 schema、不可读 payload 与 key 分类失败建立 per-key 写屏障；宿主销毁前同步尽力提交最后批次。持久宿敌临时不可用时标记 run-local `SuspendedPersistentV1`，玩家死于临时替补不会覆盖原 key/Rank。
- 新增默认拒绝的官方 Boss eligibility registry，冻结 stable key、revision、副作用和适应能力摘要；快照/lookup 只接收已审计 key，同 key 多 preset 引用 fail-closed。没有真实审计数据时空表保持入口关闭。
- 官方 Boss 最低池只计算唯一 stable key，重复审计记录不能凑足 6 条且查询直接 fail-closed；地图注册表补齐显式 `NotVerified/Verified`，首发候选保持 `NotVerified`，所有查询统一验证状态/revision/风险与安全摘要，无 Verified 地图不再创建 preview。
- 九波计划改为每槽冻结一个 reserve，同波 primary/reserve 互斥，官方确定性 draw bag 耗尽后才跨波复用；三 Boss 波只要求正确的 6-key 合格池，不再错误要求整局十余个 key 全局不重复。单个托管署名不可用时只替换该槽。
- `ModeGRuntimeModule` 的公共 API/终止路径原样拆到 partial 文件，使状态机主文件回到 1200 行预算内；入口意图改为显式布尔快照传递，难度入口 singleton 读取由 4 次收敛为 1 次，本轮新增异常边界均保留防崩并记录低频诊断。
**自动验证**: 全部 28 个 `ModeG*Guard.py` 通过；工作区内可执行的全库脚本 431/433 通过，剩余两项是未触及的龙王火箭分裂视觉旧断言与 ZombieMode RunScopedRegistry 旧断言。最新代码因构建脚本必须读取工作区外游戏 DLL 并部署到工作区外目录，本轮未重新编译；此前 Windows 编译早于本轮地图状态、partial 拆分和最终 flush 变更，不能作为当前构建通过证据。工作区 `Build/BossRush.dll`（2026-08-17 21:29:00 +08:00，3092480 bytes，SHA-256 `E6B1DA7A6A782A75F701C4E66FF63770994DD7F5AE0BA6B829725DD3A7777599`）同样标记为旧构建，不作为当前源码验证结果。
**发布阻断**: 官方至少 6 个 Boss 的逐 key 实审、DEMO 地图 `Verified` 矩阵、九波三托管 Boss 实机 smoke、Legacy/Mode D/E/F/Zombie 回归、Profiler 与真实复玩/经济样本尚未完成；`IsProductionReady=false`、两个开发开关=false。

### 2026-08-27 Mode G 呈现层补齐规格 §15 与三轴口径单点化

**状态**: fixed（静态实现 + 专项门禁 + Roslyn 语法/绑定探针）；当前源码尚未 Windows 正式编译
**Finding**: 全面代码/玩法审核（本轮）
**兼容分类**: COMPAT（新增本地化 key 与只读视图；无 schema/存档 key 变更）
**版本/Commit**: 未提交
**Owner decision**: 不需要（全部为「规格已规定但实现缺失」的补齐与死代码清理）；本轮**未**改动任何 owner 冻结的数值/结构决策，另见文末待裁决清单。

**现象**:
1. 战斗 HUD 只显示「幕/波次 + 轴名 + Resolve + 目标数」。玩家看不到距离反制方向、被点名弹药、被封锁武器系，也看不到 35%/20% 双门槛进度，三轴玩法实际不可操作——只能在波末看到「破解/没破解」，无法判断为何失败。
2. 弹药轴波恒为宿敌波，而 HUD 在宿敌波把轴名整体替换成「宿敌降临」，因此「弹药点名」文本在 HUD 上永不可达；被点名弹药只在休整开始时用一次性 toast 公布，错过即无法找回，而单发违规即永久作废本波 Resolve。
3. 入口确认页缺少规格 §3.1 强制披露：死亡损失遵循当前地图规则、高 Resolve 备装建议。
4. 遥测/自适应存在多处死代码与重复实现，其中 `EvaluateDistanceAxis` 是被 guard 明令禁止使用的「整波分布推断」替代实现，留在源码中即为误用陷阱。
5. 同一宿敌在波 3/6/9 重复登场，`defeatsByPlayer` 每局累加 3 次。

**根因**: 规格 §15 的 HUD 契约从未实现，且没有对应 guard 冻结；呈现层直接反向读取 `RunState`/遥测/自适应对象，没有单一视图构建点，导致「显示什么」与「结算什么」两套口径各自演化。

**修复内容**:
- 新增文件: 无 `.cs` 新增（`ModeGHudModel`/`ModeGObjectiveState` 就近定义在 `ModeG/ModeGHUD.cs`），因此 `compile_official.bat` 无需改动；新增 guard `tests/ModeGHudContractGuard.py`。
- `ModeGRuntimeModule_PublicApiAndShutdown.cs`: 新增唯一视图构建点 `BuildHudModel()` + `FillObjective()` + `ComposeNextWavePreview()` + `TickAmmoViolationAnnounce()`。
- `ModeGHUD.cs`: 改为纯格式化层，只消费 `BuildHudModel()`；按 §15 呈现距离方向（需贴近 <=8m / 需拉开 >=18m）、被点名弹药名与未违禁/已违禁、被封锁武器系与所需相反系、双门槛进度、宿敌名称与本次 Rank、本局契约短标题；宿敌波不再替换轴名；Last Stand 改为附加倒计时行（§15：不构成第二份常驻目标清单）；休整期预告下一波反制。百分比一律 `FloorToInt`，保证「显示达标」不早于「实际达标」。
- `ModeGAdaptiveCombat.cs`: 新增 `ModeGAxisProgress` 与 `EvaluateDistanceProgress`/`EvaluateAttributeProgress`/`GetDistanceTargetBand`/`AttributeBreakFamily`/`PredictAttributeLockFamily`；`IsDistanceAxisBroken`/`IsAttributeAxisBroken`/`ApplyAttributeLock` 全部复用之，破解结算与 HUD 进度从此单点化。删除死代码 `EvaluateDistanceAxis`（guard 已明令禁止其用法）与重复实现 `ClampAmmoViolationDamage`；`ClearAttributeLock` 改为复用 `RemoveModifiersFromSource`（原为两份等价实现）。距离/属性轴共用门槛改轴中立命名 `AxisBreakDamageShare`/`AxisBreakHealthContribution`。
- `ModeGCombatTelemetry.cs`: 新增有界缓存溢出降级标记 `IsTelemetryDegraded`（§15 要求 overflow 显示「本波挑战无效」）；单次开火 clamp 改为消费 `ModeGAdaptiveCombat.AmmoBanClampShare` 而非硬编码 `0.10`；删除无消费者的 `AverageEngagementDistance`、极端带命中计数与 `CloseExtremeShare/FarExtremeShare`（热路径每次计分命中少一次距离累加与三次自增）。保留 `_bossDirectDamage`/`BossCacheCapacity`：其预分配容量是 guard 冻结契约，且属规格 §4.1 多 Boss 采样面。
- `ModeGRuntimeModule.cs`: `defeatsByPlayer` 改为按局累加一次（rank/墓碑仍每次登场结算）；新增弹药禁令首次违规一次性播报。
- `ModeGWavePlan.cs`: 去掉两处重复 `HashSet.Add`，改为「Add 恒执行一次 + 小池允许复用」的等价单次表达。
- `ModeGInteractable.cs`: 确认页补 §3.1 两行强制披露。
- `Localization/LocalizationInjector.cs`: 新增 24 个 `BossRush_ModeG_Hud_*` 与 2 个 `BossRush_ModeG_Entry_*` 双语 key。
- guard 同步: `ModeGAdaptiveCombatGuard.py`（常量改名 + 新增「结算与 HUD 同口径」「属性预测单点」断言 + 把 `EvaluateDistanceAxis`/`ClampAmmoViolationDamage` 升级为存在即失败的禁项）；`ModeGPerformanceGuard.py`（StringBuilder 由冻结字面量 128 改为「已预分配且容量 >= 128」的语义断言）。
- 文档同步: `wiki-site/docs`(zh/en) 与 `WikiContent`(zh/en) 的 mode-g 与 strategy 四对文件——修正「Last Stand 每一波都可能出现」（实际只在多 Boss 波 2/5/8，全局最多 3 次，与 Resolve 上限 3 一致）与「每波横幅先公布反制」（横幅在波开始时；提前量来自新增的休整期 HUD 预告）；`.qoder/repowiki` 架构卡与游戏模式系统内容文档同步。

**自动验证**:
- 全部 29 个 `ModeG*Guard.py` 通过（含新增 `ModeGHudContractGuard.py`）。
- 全库 `tests/*.py` 438/440 通过；剩余 2 项与本次改动无关：`DragonKingBossGunRocketSplitGuard`（既有龙王火箭视觉旧断言）与 `SmokeLogScan`（扫描历史 `Player.log`，非源码守卫）。
- Roslyn 探针（SDK 8.0.302 `csc.dll`，`-langversion:7.3 -nostdlib+ -noconfig`）解析 `ModeG/*.cs` + `Localization/*.cs`：0 个 CS1xxx 语法错误，0 个 CS0117/CS1061/CS7036/CS1503/CS0029/CS0161/CS0165 成员/签名/流分析错误，且无任何错误指向本次新增成员；其余错误全部是缺少游戏/Unity 引用导致的 CS0518/CS0246。

**未验证/需人工**: 本机未安装《鸭科夫》（游戏 `Managed` 目录缺失），**未执行 `compile_official.bat`**，因此不能声称已正式编译或运行时验证。仍需人工 smoke：九波 HUD 实际排版与不换行（文本框已由 420x150/22f 调整为 560x210/20f）、双语切换、弹药违规播报时机、休整预告与实际反制一致性、属性封锁 HUD 与真实伤害一致性。

**待 owner 裁决（本轮刻意未改，均属规格已冻结的产品/数值决策，见 AGENTS.md §7/§10）**:
- 奖励分带：前 6 件恒为 `<P75` GeneralBase，Resolve 0→5 仅多 1 件低档物品，中段激励极弱。规格 §7 已指定补救方向是「扩大 Premium 与 GeneralBase 价值差」，且须先有经济实测样本，不得由实现方擅改档位表。
- 宿命契约零收益：规格 §3.2 明文「不得为契约新增战斗轴、事件订阅或奖励」，因此 `EdgeWalker`/`ArsenalDiscipline`/`NemesisDenied` 近乎系统侧自动完成且无机制回报，属设计取舍而非缺陷。
- 休整 8s/20s 与 Last Stand 12s 为 owner 冻结值，未做可配置化。
- 距离轴只取上一署名波末击单点采样（规格 §4.2 明文，guard 明令禁止改用整波分布）：一次不可计分末击即静默失去 1 Resolve 并断连击。本轮已用 HUD「本波无反制」把该静默失败显式化，但采样模型本身未改。

### 2026-08-17 Mode G owner 发布裁决与正式入口开放

**状态**: fixed（代码与专项静态门禁）；当前源码尚未重新 Windows 编译
**兼容分类**: COMPAT / OPERATIONAL
**Owner decision**: 现有 `GetFilteredEnemyPresets()` 过滤 Boss 池内 Boss 均可用于 Mode G；地图选择 UI 的全部有效地图均视为安全地图；只开启 `IsProductionReady`，两个开发开关保持关闭。
**修复内容**:
- `ModeGOfficialBossEligibilityRegistry.TrustConfiguredBossPool=true`，生产资格直接消费现有过滤池；`CreateModeGBossSnapshot()` 继续排除托管 Boss、拒绝空 key/同 key 多 preset 引用，并要求至少 6 个唯一官方 key。
- `ModeGMapSupportRegistry` 直接复用 `ModBehaviour.GetAllMapConfigs()`，有效 `sceneName + sceneID + spawnPoints` 配置形成 Verified 快照；preview 按当前 active scene 冻结玩家实际选择地图，不再固定第一个候选。
- `ModeGAvailability.IsProductionReady=true`；`AllowDevTestEntry=false`、`AllowDevRawPngFallback=false` 保持不变。
- 同步 Mode G eligibility/map/release/structure guards、contracts、findings、设计提案与 repowiki。
**自动验证**: 全部 28 个 `ModeG*Guard.py` 通过；地图 JSON 一致性、排序、部署、UI 注入复用和无硬编码 fallback 守卫通过；工作区内允许执行的全库脚本 431/433 通过，剩余仍是未触及的龙王火箭分裂视觉旧断言与 ZombieMode RunScopedRegistry 旧断言；`git diff --check`、EmptyCatch、LargeFileBudget、ArchitectureStructure、EventSubscriptionLifecycle 通过。当前源码未重新 Windows 编译。
**未验证/需人工**: 当前轮次未获工作区外游戏 DLL/部署目录访问授权，因此尚未执行 `compile_official.bat`；售货机购买、九波实机、旧模式回归与 Profiler 仍需交付 smoke。

---
### 2026-08-17 Mode F 全阶段持续补怪

**状态**: fixed（静态实现与自动化验证；正式编译/人工 smoke 未完成）
**Finding**: 玩家反馈 / 静态调用链确认
**兼容分类**: COMPAT
**版本/Commit**: 本次提交
**Owner decision**: Mode F 从开局起一直刷怪，不允许准备阶段暂停补位
**现象**: Mode F 大多数时候击杀 Boss 后长时间不再刷怪，但有时又会持续补怪。
**根因**:
- `QueueModeFBossRespawn` 和 `TryFulfillModeFPendingRespawns` 都对 `ModeFPhase.Preparation` 提前返回。开局准备阶段长达 180 秒，期间死亡只累计待补数量；进入悬赏阶段后同一队列恢复，因此玩家会观察到同一功能随阶段时有时无。
- 进一步按“整局完全不补怪”复审后确认第二个永久丢债缺口：`OnModeFBossDied` 先从活跃列表移除 Boss，之后执行日志、奖励与掉落收尾，最后才调用 `QueueModeFBossRespawn`。中间任一未隔离异常都会跳入外层 catch；完整性检查又无法看到已移除对象，导致该人口缺口整局不再补回。
**修复内容**:
- `ModeF/ModeFRespawn.cs` 移除准备阶段的两处补位门控；死亡事件和每秒完整性检查产生的缺口现在会在准备、悬赏、猎潮、撤离四阶段立即调度。
- 死亡处理与每秒完整性兜底都在成功移除活跃引用后设置 `replacementRequired`，并在 `finally` 中且仅在模式仍活跃时入队一次；后续日志、奖励、掉落或清理异常不再吞掉补位，重复死亡回调也不会产生双补。
- 保留既有单 in-flight 限流与逐缺口补 1 只语义：持续恢复开局压力，不无上限叠加场上数量；生成失败仍回队并由每秒完整性 tick 重试。
- `tests/ModeFPreparationSpawnGuard.py` 改为锁定全阶段持续补位，同时继续保护初始 Boss 数量、点位与单任务限流不变。
- 同步 Mode F repowiki 知识卡、专项玩法文档与模式总览。
**兼容性影响**: 不改存档、配置、TypeID、本地化 key、资源、Harmony/反射目标或开局 Boss 数量；只让准备阶段与其余阶段采用一致的死亡补位规则。
**验证方法**:
1. 全部 13 个 `ModeF*Guard.py` 通过，其中 `ModeFPreparationSpawnGuard.py` 明确禁止准备阶段补位门控并锁定入队后立即调度；`ModeFRespawnObservableSpawnGuard.py` 锁定“移除引用 → 建立补位债务 → `finally` 唯一入队”的异常安全顺序、单 in-flight 限流与可观察生成完成语义。
2. `EnemySpawnCoreObservableGuard.py`、`ModeEFSpawnParityGuard.py`、`ModeEFSpawnPostprocessSchedulerGuard.py`、`ModeEFNoGameplayThrottleGuard.py`、`EventSubscriptionLifecycleGuard.py`、`ArchitectureStructureGuard.py`、`OfficialCompileListFileExistenceGuard.py` 通过；相关验证合计 20/20。
3. 本轮未执行 `compile_official.bat`：该脚本必须读取工作区外游戏 DLL 并向工作区外加载目录部署，而当前会话明确禁止访问或写入工作区外路径，不能越权把旧构建当作当前源码验证。
**未验证/需人工**: 当前源码未正式编译，也未做游戏内 smoke。需在获准的 Windows 游戏环境中编译后，从准备阶段开始连续击杀多只 Boss，确认每次都会逐只补位；并跨悬赏、猎潮、撤离阶段验证补位不暂停、场上数量不累加失控。
**失败尝试**: 无。

### 2026-08-18 Mode G 编译错误修复

**状态**: fixed（源码修复与静态守卫验证；Windows 正式编译待授权环境执行）
**兼容分类**: SAFE / COMPAT
**问题**:
- `ModeG/ModeGEntry.cs` 的静态 `ModeG` 清理入口直接调用实例 partial 上下文中的 `DevLog`，导致 `CS0103`。
- `ModeGRuntimeModule_PublicApiAndShutdown.cs` 将值类型 `SceneRuntimeContext` 与 `null` 比较，导致 `CS0019`。
**修复**:
- 三处宿主销毁日志改为显式 `ModBehaviour.DevLog`。
- 场景生命周期检查移除值类型空比较，继续按冻结 scene pair/revision fail-closed；空场景名仅用于日志显示 `<empty>`。
**验证**: 28 个 Mode G 守卫、`ArchitectureStructureGuard.py`、`ModBehaviourInstanceClassificationGuard.py`、`OfficialCompileListFileExistenceGuard.py`、目标文件 `git diff --check` 全部通过。当前会话未执行 `compile_official.bat`，避免访问工作区外游戏 DLL 和部署目录。

### 2026-08-18 Mode G 自带装备、小 Boss 池复制与入口收口

**状态**: fixed（源码、文档与专项静态守卫；Windows 正式编译/实机 smoke 待执行）
**兼容分类**: COMPAT
**Owner decision**: 玩家允许携带自己的装备进入 Mode G；官方 Boss 少于 6 个时从现有合格 key 中按本局 seed 随机复制；旧 BossRush 路牌不再显示 Mode G 独立选项。
**修复**:
- Mode G 自动分流和最终启动不再调用裸装扫描，不卸下、不移动、不发放 StarterKit，也不改写玩家现有装备、弹药或消耗品；营旗和血猎收发器的 Mode E/F 优先级保持不变。
- 官方 Boss 启动最低值由 6 个唯一 key 调整为 1 个；6 个保留为完整 primary/reserve 编排目标。1-5 个时 `ModeGWavePlan` 使用同一 `runSeed` 的确定性随机流从现有 stable key 复制，允许同波 primary/reserve 重复，但不伪造 `EnemyPresetInfo`、不修改 run-scoped 快照，空池和同 key 多 preset 引用仍 fail-closed。
- 移除 `BossRushOption_ModeG` 路牌注入；Mode G 继续沿地图选择/传送前冻结意图，过图后由短命 `ModeGInteractable` presenter 打开契约二选一确认页。
- Mode G 奖励候选桥接显式过滤为 Q5-Q8，不改变 Legacy Q1-Q8 候选搜索；确认页补充核心玩法与奖励说明：后续波次针对上一波距离、弹药和伤害类型，改变打法获得 Resolve；第 9 波胜利按 Resolve 发放 6-10 件 Q5-Q8 奖励并返还信物。
- 同步 `ModeGManagedBossEligibilityGuard.py`、`ModeGPlayerLoadoutGuard.py`、`ModeGEntryPreviewGuard.py`、`docs/contracts.md`、设计提案与 repowiki。
**静态验证**: 全部 28 个 `ModeG*Guard.py` 通过；`ArchitectureStructureGuard.py`、`ModBehaviourInstanceClassificationGuard.py`、`OfficialCompileListFileExistenceGuard.py`、`EventSubscriptionLifecycleGuard.py`、`EmptyCatchGuard.py` 与 `git diff --check` 通过（仅既有 LF/CRLF 提示）。
**未验证/需人工**: 当前轮次未获工作区外游戏 DLL/部署目录访问授权，未执行 `compile_official.bat`。需实机确认携带装备自动分流、1/2/5 个官方 Boss 池的九波生成、确认页布局、胜利奖励与旧三难度路牌回归。

## 变更日志

| 日期 | 变更 | 说明 |
| --- | --- | --- |
| 2026-08-28 | 新增遗种巢（PetNest）养崽系统 | 基地侧养成收集：遗种蛋+遗魂双轨掉落（TypeID 500059）、全 Boss 谱系幼体化、孵化 roll 与命名、单席随从进局与捡漏背包、重伤退场与战痕、天灾远征与真死、博物馆图鉴与纪念碑、驯养成就。零新增 Harmony 补丁、零新增 Unity 资源、只占一个 TypeID；唯一反射写点 `LevelManager.petCharacter`（借席不夺席）。默认关闭。契约见 docs/contracts.md §6.2。 |
| 2026-08-27 | 全项目 UI 收口与观感升级 | 新增共享 UI 库（设计 token、层级表、程序化圆角九宫格、图集注入点）；修高分屏面板不缩放与模态互压；legacy Arial 清零，中文不再显示为方块。 |
| 2026-08-27 | 审查修复：命火量纲、安全区仇恨泄漏与双槽改造 | 命火成长改入场上限 4%/杀使机制真正可达；修复被弹出敌人永久失去仇恨的卡波次风险；便携安全区改为战斗期替换、准备期并存；恢复手雷不取消安全区的武器 tag 门控。 |
| 2026-08-26 | 修复 Mode F 退出生命并新增命火过载 | 生命成长封顶 +50%，溢出转为带烧伤和双倍失血风险的短时火力/移速强化；退出时清理全部临时属性并钳制当前生命。 |
| 2026-08-25 | 完善丧尸模式波次奖励与安全区体验 | 散落物按波次清理、Boss 解卡阈值下调、骚扰弹道可读化、可选休息时长 15～300 秒，新增背包废品回收与便携安全区装置（TypeID 500058）。 |
| 2026-08-17 | 修复 Mode F 准备阶段停刷 | 移除 180 秒准备阶段的补位门控，四阶段均按死亡/失效缺口持续逐只补位，并保留单任务限流。 |
| 2026-08-07 | 提高丧尸模式前期密度并修复停刷 | 击杀目标保持旧值；场上压力、补怪频率与硬上限至少提高 3 倍，并增加可靠刷新、计数校准和远怪回收。 |
| 2026-08-07 | 修复 Mode E 两个重刷道具不可用 | 按 owner 决定完全移除重刷人口上限和点位裁剪，仅保留单任务互斥与会话安全。 |
| 2026-08-07 | 丧尸模式 Boss 与变异尸潮持续成长 | Boss 数量按五波轮次无上限递增，后期精英/特殊权重持续提高，总风险与逐只掉落收益同步成长。 |
| 2026-08-07 | 补齐丧尸模式 Boss 风险收益曲线 | 按轮次同步提升 Boss 本体、支援、净化收益、奖励箱和肉鸽奖励次数；固定单 Boss 部分已被后续设计覆盖。 |
| 2026-08-07 | 丧尸模式 Boss 波取消地图联动 | 移除地图刷怪点数量联动并明确非 Boss 速度曲线；初版固定单 Boss 已被后续递增设计覆盖。 |
| 2026-08-07 | 优化丧尸模式奖励与终端 | 移除死刷新选项，补齐终端余额/饮料库存，并收敛临时 NPC 保护扫描和失效 UI 记录。 |
| 2026-08-07 | 修复 Wiki 右书页越界与跳页 | 全文只生成一次分页边界，左右页显示连续缓存页块并以 TMP `Page` 限制越界。 |
| 2026-07-01 | AI 协作文档收敛 | 从旧 `docs/协作/FIX_TRACKER.md` 迁移 confirmed 修复记录；新增状态、owner decision、兼容分类字段。 |

## 2026-08-30 三系统落地：鸭皇图鉴 / 局内随机事件 / 词缀锻造

分类：`COMPAT` + `SCHEMA+`（新存档 key 与物品 KV 前缀，均为向后兼容新增）
+ `OPERATIONAL`（编译脚本改用 Roslyn 响应文件，见下）。

### 交付内容

| 系统 | 目录 | 规模 | 开关（默认） |
| --- | --- | --- | --- |
| 鸭皇图鉴 | `Integration/Codex/` + `Localization/CodexLocalization.cs` + `Config/ConfigCodex.cs` | 15 文件 | `BossRush_CodexEnabled`（true） |
| 局内随机事件「鸭生无常」 | `RandomEvents/` + `Config/ConfigRandomEvents.cs` | 12 文件 | `BossRush_RandomEventsEnabled`（true）+ 频率档 |
| 词缀锻造 | `Integration/AffixForge/` + `Integration/Reforge/ReforgeUIManager_AffixForge*.cs` + `Localization/AffixForgeLocalization.cs` + `Config/ConfigAffixForge.cs` | 14 文件 | `BossRush_AffixForgeEnabled`（true） |

新增 TypeID：`500060` 词缀熔石、`500061` 鸭皇图鉴（台账三处已同步，下一可用 `500062`）。
新增存档 key：`BossRush_Codex_v1`（槽位级）。词缀数据寄生官方物品 KV 的 `AFX_` 前缀，随机事件零存档。
成就分类枚举末尾追加 `Codex`（未插入中间，老档 int 值不漂移）。
零新增 Harmony patch。

### 修复的实际缺陷

1. `ItemContentRegistry.cs` 注册的类名写错（`CodexBookItem` → 实际 `CodexBookConfig`），编译期 CS0103。
2. `RandomEventEffectsBridge_Loot.cs` 把官方 `TagsData.AllTags`（`ReadOnlyCollection<Tag>`）
   直接赋给 `List<Tag>`，CS0029。改用 `IList<Tag>` 接住，只做顺序遍历，避免多余拷贝。
3. 三系统的宿主销毁清理原本内联在 `ModBehaviour.OnDestroy` 末尾，越过了
   `StaticCacheLifecycleGuard` 判定「调用是否在 OnDestroy 路径上」的回溯窗口，
   导致 4 个类被误判漏清理。按仓库既有约定收口成三个具名方法：
   `CleanupCodexRuntimeOnDestroy` / `CleanupAffixForgeRuntimeOnDestroy`
   / `CleanupRandomEventsRuntimeOnDestroy`（后者新增 `AffixForgeHostCleanup.cs` 承载）。

### OPERATIONAL：编译脚本改用响应文件（需 owner 知悉）

**症状**：登记 40 个新文件后 `compile_official.bat` 直接失败，输出
`The system cannot execute the specified program.` 并以 60 退出，**没有任何 C# 错误行**。

**根因**：清单增至约 690 个文件后，展开后的 csc 命令行超过进程创建上限，csc 根本没被启动。
用响应文件单独调 csc 编译同一份清单则 0 错误通过，据此定位。

**改法**：`compile_official.bat` 里 csc 的全部参数改为先写进 `Build\bossrush.rsp`，
再 `csc @rsp`。源码清单仍逐条显式列出（不用通配符），编译清单守卫照常双向生效。
写法上踩过三个坑，已在脚本顶部注释固化：
  - 必须「括号块 + 块尾一次重定向」；行首重定向（`>>"file" echo ...`）在本机 cmd 上
    直接报 ERROR_INVALID_NAME(123)；行尾重定向会让 `.cs` 后跟 `>>`，守卫正则扫不到。
  - 必须用 `echo(` 而非 `echo`：未开 DEV 时 `%BOSSRUSH_DEFINE_ARGS%` 为空，
    `echo` 会把 "ECHO is on." 写进响应文件。
  - 含括号的路径（`C:\Program Files (x86)\...`）在引号内已实测安全。
备份留在 `compile_official.bat.bak`，确认无碍后可删。

### 同步的守卫（AGENTS.md 4.10）

- 新增：`RandomEventsWaveIsolationGuard.py`（波次符号零触碰 + 敌对性安全网 + 事件清理成对）、
  `RandomEventsModeGateGuard.py`（禁入 5 模式 + fail-closed + 禁引内部符号）、
  `RandomEventsRuntimeModuleGuard.py`（单实例 + dormant + 热路径零日志）、
  `AffixForgeInvariantGuard.py`（AFX_ 互斥 + 订阅逐事件成对 + 12 词缀 + 死契 1 血保命）。
  另有实现阶段产出的 `CodexPersistenceGuard.py`、`CodexKillTrackingGuard.py`。
- 因结构变化而同步（非放宽掩盖）：
  - 三个 ZombieMode 清单守卫与 `ModeHCompileManifestGuard`：适配响应文件行格式。
  - `PetNestEggItemRegistryGuard`：台账断言由硬编码 500059/500060 改为动态上界
    （守的是「遗种蛋的号已被占用」，不是「它必须是最后一个」）。
  - `PetNestAchievementCategoryGuard`：由「Taming 必须是最后一项」改为「冻结前缀逐字相等」，
    允许末尾追加新分类，同时仍然禁止插入与重排。
  - `LocalizationInjectionGuard`：新增识别字典索引式注入（`map[XxxConfig.LOC_KEY] = ...`），
    这是新一代本地化文件的写法，旧正则只认 `Inject(...)` 调用式会误报。
  - `ModBehaviourInstanceClassificationGuard` + 对应文档：基线 361 → 374，新增 `RandomEvents` 组。
  - `tests/empty_catch_budget.txt`：919 → 968。新增的 49 处都是
    `base.Awake()` / `onFailed()` 这类一行防御式空 catch（AGENTS.md 4.7 明令不成批清理）；
    刷怪与敌对性安全网等关键路径**已带 DevLog**，未以空 catch 掩盖失败。

### 验证状态

- Windows `compile_official.bat`：**通过**（691 源文件，0 error）。
- `python tools/run_guards.py`：**PASS=499 / NEW-FAIL=0 / KNOWN-RED=1**（既有 DragonKing 红项）。
- **实机 smoke：未做**。以下必须人工验证后才能认为交付完成：
  1. 图鉴：买书开面板 → 杀 2 Boss 验证解锁与成就 → 回基地落盘 → 重启读档 → 切槽位隔离；
     ModeG 托管龙裔计入、丧尸 Titan 计入、遗种巢随从不计。
  2. 随机事件：F3 逐事件强制触发；**关键回归**——乱入 Boss 在场时打死波次 Boss，
     波次应正常推进且乱入仍在；五条清理路径（到时/死亡/撤离/切图/关开关）零残留。
     另需实测：竞技场是否存在 `WeatherManager.Instance`；零伤害爆炸是否带击退。
  3. 词缀：门控四连测（手持/切换/空手/穿卸 → 订阅计数归零）；旁观 NPC 同武器不触发；
     仓库闲置零订阅；存读档 KV 完整；卖店买回/快递往返/掉落拾回 KV 保留。
- 美术资产（约 35~43 张 Boss 立绘 + 熔石图标 + 12 词缀图标 + 8 事件图标）**尚未生成**，
  当前全部走占位链（图鉴用官方 Boss 图标、其余用文字/程序化底）。
- `.qoder/repowiki/` 与 Wiki 站词条**尚未同步**（AGENTS.md 4.13 要求，属未完成部分）。

### 2026-08-30 补：文档同步与美术资产（承接上条）

分类：`SAFE`（文档）+ `OPERATIONAL`（新增美术资产与 Unity 构建器）。

**repowiki 同步（AGENTS.md 4.13，此前列为未完成项，现已补齐）**

- 新增 3 张模块知识卡（`.qoder/repowiki/knowledge/zh/`）：鸭皇图鉴、局内随机事件、词缀锻造。
  每张都按既有知识卡体例写清系统概述、关键文件职责、架构与设计约定、性能、契约面、已知未完成项，
  并把「为什么否掉另一条路」的决策记进去（例如词缀为何不用官方 Effect 挂件、
  图鉴为何不接 AchievementTracker、随机事件为何不复用变异词条 roll 基建）。
- `knowledge/zh/_index.yaml` 登记 3 个模块（codex / random_events / affix_forge），YAML 解析校验通过。
- 新增 3 篇主题详解（`.qoder/repowiki/zh/content/高级功能/`），并挂进「高级功能」索引页的
  三处清单（简介、分层说明、目录）。

**游戏内 Wiki 与在线站**

- `WikiContent/{zh,en}/` 各新增 3 篇玩家向词条（图鉴 / 随机事件 / 词缀锻造），文风对齐日报词条。
- `WikiContent/catalog.tsv` 追加 3 行（order 10/11/12）。
- `wiki-site/scripts/sync-content.mjs` 的 `ENTRY_TO_PATH` 补 3 条映射，
  `docs/.vitepress/config.mts` 侧边栏中英各补 3 条，`vitepress build` 通过。
- **既有缺口（非本次引入，供 owner 决策）**：`system__pet_nest` 与 `system__daily_report`
  从未进入 `sync-content.mjs` 的映射表，因此遗种巢与日报在**在线站上没有页面**（游戏内 Wiki 有）。
  同步脚本现在仍报「跳过 3」，就是它们加上另一条。本次未擅自补，因为不属于本批范围。

**美术资产**

- 新增 `tools/gen_codex_art.py`：一次性批量生成脚本，可断点续跑（目标文件存在即跳过），
  含网关抽风的指数退避重试。共 58 项：36 张 Boss 立绘 + 2 个物品图标 + 12 个词缀图标 + 8 个事件图标。
- Boss 名册来自静态汇总而非实机导出：ModeH `BossProfiles.json` 的 `profileTemplates`（12）
  ∪ `excludedStableKeys` 里的官方 Boss ∪ 官方 `AchievementManager` 的 `KillCountAchievement` 表
  ∪ 3 个自定义 Boss ∪ 5 个丧尸合成条目 = 28 官方 + 3 自定义 + 5 丧尸 = 36。
  **注意**：运行时真实 Boss 池由 `showName` + 血量阈值决定，静态汇总可能与之有出入；
  立绘缓存是 fail-open，多出来的 Boss 会走占位链，不会报错。
- 流程：gpt-image-2 出色键图（#ff00ff）→ `remove_chroma_key.py` 抠图 → 裁包围盒 → 等比缩放 →
  居中贴进透明正方形画布。立绘/物品 512px、词缀 256px、事件 128px。
- 新增 Unity 构建器 `CodexPortraitBundleBuilder.cs`（兄弟工程 `Assets/Editor/`）：
  扫 `Assets/UI/Codex` 目录、程序化打 bundle 标签（不依赖手工 .meta）、
  构建后回读校验 asset 数量与命名契约（必须全小写、必须带 `codex_portrait_` 前缀）、
  体积硬上限 8 MiB。与 ModeG/ModeH 那两个「固定两张图」的构建器不同，这个是批量扫目录型。

**美术与 bundle 已全部完成（此前列为未完成项，现已补齐）**

- 58 项资产全部生成落位：36 张 Boss 立绘（512px）、2 个物品图标（512px）、
  12 个词缀图标（256px）、8 个事件图标（128px）。
- 生成过程分三轮：首轮 33 成功 / 25 失败，失败全是 `APIConnectionError`。
  根因是**网关限流约 1 次/分钟而脚本只隔 3 秒**，不是 prompt 问题。
  把间隔改为可配置（`ART_GEN_DELAY`，默认 20 秒）并加三次指数退避重试后，
  第二轮 23/25、第三轮 2/2 全部补齐。
- 立绘 AssetBundle 已实际构建并落位 `Assets/ui/codex_portraits`（3.1 MB，8 MiB 上限内），
  构建器回读校验通过（36 个 asset、命名全小写、前缀正确）。
- `compile_official.bat` 最终验证：`Build succeeded`，并成功部署立绘 bundle、
  词缀图标、事件图标三类资产到游戏目录。

**Unity 构建踩的三个坑（已固化进 tools/build_codex_bundle.ps1 注释）**

1. PowerShell 5.1 按 ANSI 读 `.ps1`，含中文的脚本必须存成 **UTF-8 with BOM**，否则解析报
   `Unexpected token`。
2. Unity.exe 是 GUI 程序，用 `&` 调**不会等待**，`$LASTEXITCODE` 为空，且外层任务结束时
   会把正在启动的 Unity 子进程带走（表现为日志停在 `Begin MonoManager ReloadAssembly`、无产物）。
   必须用 `Start-Process -Wait -PassThru`。
3. 上一条留下的孤儿 Unity 实例会占住工程锁，导致后续调用**刚切到工程路径就以返回码 0 退出**
   （日志里只有 `Exiting without the bug reporter`，极易误判成构建器没跑）。
   排查方法：看 `Temp/UnityLockfile` 与 `Unity.exe` 进程启动时间。

**仍未完成**

- **实机 smoke 未做**（owner 指示本轮不做）。这是当前唯一的未完成项，
  各系统的必测清单见本条目上方与 2026-08-30 主条目。
- 立绘的 Boss 名册是**静态汇总**得来（ModeH 档案 + 官方成就表 + 自定义/丧尸常量 = 36，
  后续又补了 `Cname_Ghost` 共 37；补它的依据是 mod 旧版构建里存在
  `!(enemyPresetInfo.name == "Cname_Ghost")` 这种「从 Boss 选取中显式排除」的写法，
  说明它本来能通过 Boss 池筛选、会出现在图鉴目录里。全仓其余无立绘的 `Cname_*`
  —— Wolf / Usec / GunTurret / Zombie —— 已逐个确认是杂兵或 AI 预设，不进 Boss 池），
  而运行时真实 Boss 池由 `showName` + 血量阈值决定。两者若有出入，多出来的 Boss 会走
  占位链（fail-open），不会报错；实机跑一次 F3 的目录导出即可核对差集并补图。
- 立绘是按 nameKey 语义生成的**风格化演绎**，不是游戏内模型的还原（模型无法读取）。

## 2026-08-30 鸭王征程 / 竞技场后山 全面审核

审核范围：本轮新增的 Campaign、Integration/BackMountain、BossBgmCoordinator、
BossRushUISkinLoader 与相关接线。编译 + 503 guard + 内容一致性 + 逻辑走查。

| # | 分类 | 问题 | 处理 |
| --- | --- | --- | --- |
| 1 | 回归 | `EmptyCatchGuard` 由绿转红：本轮新增 35 个空 catch 把全仓库总数从预算 968 顶到 1003。此前误报为"守卫全绿"——runner 只打印输出末行，看起来像一个无关文件 | 逐处按 AGENTS §4.7 处理：27 处冷路径（存档/注册/通知漏斗/清理）补 `DevLog`，8 处必须静默（日志函数自身会递归、`OnGlobalHurt` 是最热事件、每帧 tick）写明理由。**不动预算**，计数回到 968 |
| 2 | BREAKING(玩法) | 幽影蘑菇物品描述中英文均写"受到的伤害更少 / takes less damage"，实现却加 `MaxHealth` +10%。两者不是一回事，玩家会被误导 | 改用 `ElementFactor_Physics` −10% PercentageAdd——受击侧伤害倍率，`Health` 结算时读取（`DragonSetConfig.cs:11` 有说明，丧尸模式守护护盾同款 −25%）。描述按项目惯例明写"物理伤害" |
| 3 | 数据丢失 | `RaidMealService.ApplyForRun` 先清登记再 `switch`，遇到旧存档里的陌生 TypeID 会走 `default` 直接 return——饭被吃掉、加成没给、玩家无任何提示 | 改为先用 `GetDefinition` 确认认识该餐品再消费；无法识别时记 WARNING 并清掉（避免每局重试） |
| 4 | 卡死 | 第一章"第 3 波前无伤"靠"波次超过门槛"置达成，而标准模式总波次 = BossFilter 过滤后的 Boss 数 / 每波数。玩家把 Boss 池筛小后波次可能永远到不了 4，目标卡死在未达成——哪怕全程没掉血 | 新增 `SatisfyNoDamageOnRunComplete()`，通关或撤离成功时补判（走完整局已蕴含"熬过全部波次"）；已破防的不会被救回 |
| 5 | 冲突 | 终章决战门禁漏了 ModeG（宿命回响）与 ModeH（黑市鸭王杯），两者都在竞技场里跑，会与决战抢场地 | 门禁补上 `modeGActive` 与 `ModeHRuntime.RunState.Lifecycle != None` |

已核对无问题：编译清单登记、TypeID 台账 500062-500067、本地化注入配对与
`Note_<key>_Title` 键格式、订阅/退订配对（3/3、1/1、采集器 2/2）、19 处
`ResetStaticCaches` 全部被中央复位调用、契约文档登记、恒开策略与 ModConfig
白名单、repowiki 同步、JSON 合法性、六章硬编码内容完整。

历史审核时 `Assets/Data/Campaign/*.json` 尚不存在；2026-08-31 已按发布门槛新增并改为正式来源，
硬编码仅作整表校验失败时的灾备 fallback。

---
### 2026-08-31 征程/后山审核修复：四个 P1 + 两个 P2

**状态**: fixed（P1×4、P2×2）；同轮 7 项 P3 为 needs-owner-decision，见 CR-2026-08-31-007
**Finding**: CR-2026-08-31-001 ~ -006
**兼容分类**: `COMPAT`（四个 P1，行为修正、不动 schema）+ `SAFE`（两个 P2，纯性能）
**版本/Commit**: 未提交
**Owner decision**: 不需要（六项都是「实现与既定设计不符」，不涉产品取舍）；
同轮 7 项 P3 需要，已单列 CR-2026-08-31-007

**现象**

1. 出击餐吃了没效果，每局都如此（三种餐全部）。
2. 展示柜「登记得越多越经打」在战局里不成立，进图后加成为零。
3. 终章决战打输一次，召唤石永不再现，终章无法重打（本会话内）。
4. 同会话换档后，A 档的展示柜收藏在 B 档生效；在 B 档登记会把 A 档收藏整体写进 B 档存档。
5. / 6. 两处每帧路径持续产生 gen0 垃圾（召唤石维护的场景名查询、HUD 的先构建后比较）。

**根因**

- 1 与 2 同源：官方主角由 `LevelManager.CreateMainCharacterAsync` **异步**创建，
  而两处加成都挂在 `SceneManager.sceneLoaded` 驱动的场景回调上，那一刻
  `CharacterMainControl.Main` 必为 null；早返后无任何重试路径。
  `RaidMealService.cs` 的头注释本就写明该用 `OnLevelInitialized`，实现没接上。
- 3：`CleanupCampaignFinalBoss` 只有「死亡回调」「让路 tick」两个调用点，
  玩家打输时 Boss 随场景销毁、回调永不触发，`campaignFinalBossActive` 卡死。
- 4：`ShowcaseService` 缓存无槽位烙印且不订阅换槽事件，`NotifySlotChanged()` 全仓零调用。
- 5：判定顺序把每帧分配字符串的场景查询排在了零分配的契约查询之前。
- 6：脏检查放在字符串构建**之后**，等于每帧都付了分配代价。

**修复内容**

- 修改文件（均为既有文件，无新增 `.cs`，`compile_official.bat` 无需改动）:
  - `Integration/BackMountain/BackMountainRuntimeModule.cs`：新增 `LevelManager.OnAfterLevelInitialized`
    与 `SavesSystem.OnSetFile` / `OnSaveDeleted` 两组幂等订阅（命名方法 + 成对退订，AGENTS.md 4.6）；
    把「设施注入」与「角色加成」拆成 `RefreshFacilitiesForScene` / `RefreshCharacterBoundEffects`
    两个时机；切场景走 `ClearCharacterBoundEffects`；bootstrap 迟到时用 `LevelManager.AfterInit` 补一次。
  - `Integration/BackMountain/ShowcaseService.cs`：缓存加槽位烙印（`_loadedSlot` + `ReadCurrentSlotSafe`），
    `EnsureLoaded` 检测到槽位漂移即摘旧加成并重读；`NotifySlotChanged` 一并复位烙印。
  - `Campaign/CampaignFinalBoss.cs`：新增 `campaignFinalBossRunId`（作废在飞的异步生成）与
    `campaignFinalBossSpawnResolved`（区分「生成中」与「Boss 已不在场」）；让路 tick 补
    「Boss 已不在场」收尾；收尾复位终章局内追踪；召唤石判定改序 +
    `IsCampaignArenaSceneCached` 按 scene generation 缓存场景判定。
  - `Campaign/CampaignRuntimeModule.cs`：`OnSceneLoaded` 幂等调用 `CleanupCampaignFinalBoss(false)`。
  - `Campaign/CampaignHud.cs`：改为构建前零分配脏检查（复用 `List<int>` / `List<bool>` 快照）。
- 同步文档: `.qoder/repowiki/` 两张知识卡（后山 3.2/3.3、征程 3.4 与文件职责表）、
  `CODE_REVIEW_FINDINGS.md`（新增 7 条 + 状态汇总）。

**兼容性影响**

- 存档：无 schema 变更。修复 4 之后，此前若已发生跨槽污染的存档不会被自动纠正
  （已写进 B 档的收藏就是 B 档的数据），玩家可在展示柜面板自行确认；新污染不再产生。
- 配置 / TypeID / Harmony / 反射 / 资源 / 部署：均无影响。
- 新增两处官方事件订阅（`OnAfterLevelInitialized`、`OnSetFile` / `OnSaveDeleted`），
  均为静态事件，已按 4.6 做幂等 + dormant 与宿主销毁两条路径退订。

**验证方法**

1. 编译: `compile_official.bat` → `Build succeeded!`，exit 0，DLL 与资源已部署。
2. Guard: `python tools/run_guards.py` → PASS=503 / NEW-FAIL=0 / KNOWN-RED=1
   （既有红项 `DragonKingBossGunRocketSplitGuard`，与本轮无关）。
3. 静态复核: 订阅/退订 3/3 配对；`ApplyForRun` / `ReapplyBonuses` / `NotifySlotChanged`
   调用点逐一确认；`CleanupCampaignFinalBoss` 现有 4 个调用点覆盖击杀/让路/Boss消失/切场景。

**未验证/需人工**

四条 P1 的实机 smoke 尚未做（脚本无法驱动进基地/进局）。建议顺序：
① 基地吃出击餐 → 进局确认飘字与属性；② 登记一件 Q≥5 战利品 → 进局确认最大生命提升；
③ 召唤终章 → 故意战死 → 回基地再进竞技场确认召唤石重现、HUD 不残留；
④ A 档登记 → 不退游戏切 B 档 → 确认 B 档为空且加成为 0。
先用 `set BOSSRUSH_DEV_BUILD=1 && compile_official.bat` 出 dev 包，否则 `DevLog` 被剥离。

---

### 2026-08-31 全内容开启与可用性闭环

**状态**: fixed（静态实现、Windows 编译与 guard 已完成；实机玩法 smoke 待人工）
**Finding**: CR-2026-08-29-018、CR-2026-08-29-021、CR-2026-08-31-007、CR-2026-08-31-008
**兼容分类**: `COMPAT` + `SCHEMA+`（日报未读提示、征程待交付态仅新增可选字段）
**版本/Commit**: 未提交

**内容开启策略**

- 遗种巢、日报、鸭皇图鉴、词缀锻造、鸭生无常、鸭王征程、竞技场后山、模式H
  共八个内容系统的字段默认值均为 `true`。
- `LoadConfigFromFile` 后统一执行 `ForceContentSystemSwitchesOn()`，抹平历史配置和旧档遗留的
  `false`；八个总开关不再从 ModConfig 读取，也不再注册到 UI，避免玩家把正式内容误关。
- `BossRush_BackMountainUnlockAll` 仍是默认关闭、可配置的调试旁路，不属于内容开关。
- 模式H 的真实仓库押注仍保持 fail-closed；这不是内容总开关，正式玩法使用完整的虚拟押注与结算，
  不对玩家仓库物品做未获授权的高风险扣押。

**模式H 可玩闭环**

- 新增 `ModeHRuntimeModule_CombatFlow.cs` 与查询/克隆辅助 partial
  `ModeHRuntimeModule_CombatProfiles.cs` 并登记 `compile_official.bat`：完成确定性出战名单、
  套装选择、公开分/胜率/赔率、分帧生成、战斗、拍铃接力、遥测、结算、伤病/战痕、奖励、
  赛间恢复、转会、总决赛与名人堂更新，不再在生成成功后以 `combat_wiring_pending` 回滚看盘。
- 报告恢复在继续前先核对已结算/奖励状态，技术重试回滚报告与奖励，避免重放和重复结算。
- 地图选择页只展示 ModeH 支持的 sceneName/sceneID 组合，并按原地图配置索引重新冻结目标，
  修复选中项与预扣票 intent 漂移。
- 原生敌人隔离只清明确敌对角色，保留主玩家、玩家队、遗种巢随从及 `INPCController` 功能 NPC。

**其余内容修复**

- 鸭皇图鉴：书不可出售、价格系数正常；商店暂不可注入时保留库存缓存；存档订阅完整退订。
- 鸭生无常随机商人：反射绑定前置，配置完成前 inactive；首次激活单次补货，时间戳稳定；
  弹药/医疗按 99 堆叠，高品质物品单件出售。
- 日报：`PendingIssueBanner` 作为可选 JSON 字段落盘；日报 UI 的 `OnDestroy` 调用基类清理。
- 词缀锻造：全部 KV 写入读回核验；重铸/锁定按事务顺序扣款与扣材料，失败恢复槽位，
  退款或回滚失败会给玩家明确错误，不再静默吞资源。
- 鸭王征程：`ReadyToDeliver` 向后兼容持久化；第一章无伤降为 2 波，第三章文案统一 8 名头目；
  终章召唤石仅在契约进行中出现；交付改为克隆存档事务，存档成功后才发布解锁 token，
  现金或写盘失败均回滚并允许安全重试。
- 竞技场后山：展示柜可登记手持或穿戴的高品质战利品，排除自产种子/餐品；登记、移除、
  出击餐登记和清除均写后读回，失败恢复内存/加成；陌生旧餐品 ID 不再静默吞掉。
- 七项原 P3 取舍已按用户明确指示闭环，详见 CR-2026-08-31-007。

**守卫与文档**

- 同步 ModeH 结构、入口地图、竞技场隔离、词缀事务、图鉴持久化、日报持久化、随机事件、
  征程与后山结构守卫；相关九个 guard 单独执行均 PASS。
- `.qoder/repowiki/` 已同步 ModeH、图鉴、日报、随机事件、词缀锻造、征程、后山知识卡和主题文档，
  删除「ModeH 战斗未接线」「内容默认关闭」等过期描述。
- `ModeHRuntimeModule_CombatFlow.cs` 拆分后为 1137 行，低于新文件 1200 行硬预算；
  `ErrorRecoveryPending → Recovering` 改为显式状态机出口，恢复通道无死态。
- Windows `compile_official.bat`：PASS（0 error，仅既有 JSON DTO `CS0649` warning），
  DLL 与资源已部署到本机游戏 Mod 目录。
- `test_logic_official.bat`：8/8 PASS。
- `python tools/run_guards.py`：PASS=503 / NEW-FAIL=0 / KNOWN-RED=1；唯一已知红项仍是
  `DragonKingBossGunRocketSplitGuard.py`，在本次基线提交之前已存在、与本轮无关。
- `git diff --check` 与最终差异复核结果见交付回复。

**实机待测**

- 模式H：受支持地图筛选、六场完整赛季、拍铃接力、失败重试、总决赛/名人堂、友方 NPC 共图。
- 存档故障注入：征程交付、词缀锻造、展示柜和出击餐在写失败后无资源丢失且可重试。
- 商店：图鉴书刷新/回购限制、随机商人首次库存与跨日刷新。
- 跨会话：日报未读提示、征程 ReadyToDeliver、后山登记与出击餐恢复。

---

### 2026-08-31 新玩法可靠性修复与 F3 一键验收

**状态**: implemented（代码、静态守卫与 Windows 编译完成；F3 全套实机报告待 owner 运行）
**兼容分类**: `COMPAT` + `SCHEMA+`（PetNest Bundle_v2）+ `OPERATIONAL`（Dev F3 验收与 Campaign JSON 部署）
**版本/Commit**: 未提交

**修复内容**

- Mode H：认证入口改为四元签名缓存优先，未命中从静态生产目录认证；首次进入不再依赖空的
  `ProductionKeys`。缓存写失败只告警，本次通过的赛季继续；地图审计与双租约门槛保持前置。
- 图鉴 / 遗种巢：官方敌人预设池改为两个消费者共享的一次性初始化；过滤变更同时失效两方，
  增加扫描次数与图鉴构建次数诊断。
- PetNest：新增权威 `BossRush_PetNest_Bundle_v2`，三个 v1 key 只读迁移且不删除；v2 损坏或过新
  fail-closed。巢、远征、博物馆写入统一候选包事务。远征奖励以 `cashGranted` /
  `grantedLootUnits` 续发，取消六次永久放弃，资源就绪后固定退避继续，采用至少一次语义。
- UI / 输入：遗种巢全交互面板统一清理；Mode G 确认/放弃弹窗可幂等关闭；公开模态租约计数，
  结束、死亡、切图、禁用和宿主销毁均走安全清理。
- Mode F：血火负担同时挂 Move/Walk/Run 三项 1.15 倍速度与原伤害修正，统一 tracker 在所有出口移除。
- Campaign：新增并部署 `Assets/Data/Campaign/Chapters.json`，严格六章整表校验，暴露 Json/Fallback
  与内容签名；F3 只接受 Json + 六章 + 冻结签名匹配。
- 日报：签到、跨日、里程碑、种子、横幅和奖励重投递统一候选提交；悬赏先落待发债务再触碰经济。
  Dev 用例真实执行签到、跨日、保存、清缓存回读，并注入 Store 失败验证状态不变。
- 音频 / 终章：Boss BGM 改 owner 租约，同 key 引用计数、异 key 恢复、切图清空；冠军之影只由
  Campaign 发一次终章死亡表现、胜利与 stinger，公共清理仍幂等执行。
- F3：新增五阶段串行完整玩法验收、专用档/运行标记、取消安全清理、超时、性能 p95/峰值采样、
  独立 `BossRushTestReports/BossRushValidation_<runId>.log` 报告及崩溃后中断提示。

**静态验证**

- 新增源文件已登记 `compile_official.bat`，Campaign JSON 已加入正式部署步骤；
  `tools/verify_syntax.py` 已支持 `echo(<file.cs`，736 个编译源语法探针通过。
- DragonKing 火箭 guard 已改验当前对象池播放、容量、生成与清理不变式，不恢复旧 `Destroy` 路径。
- 新增/更新 ModeH、PetNest、日报、图鉴、Campaign、BGM、F3 Runner 等守卫。
- Windows 正式版与 Dev 版均编译成功（仅 JSON DTO 的既有 `CS0649` 警告）；Dev DLL 最后部署。
- `test_logic_official.bat`：8/8 PASS。
- `python tools/run_guards.py`：PASS=507 / NEW-FAIL=0 / KNOWN-RED=0。
- `git diff --check`：无空白错误；`tools/verify_syntax.py`：737 个编译源语法探针 PASS。

**实机待验收**

在 Dev 构建、基地、专用测试档中打开 F3 → “验收测试”，先标记当前档，再运行完整验收。
只有独立报告无 `FAIL` 且最终清场/性能门槛通过后，才把本条状态从 implemented 改为 fixed。

---

### 2026-08-31 首次 F3 报告同步与运行时回归修复

**状态**: implemented（正式/Dev 编译与全量 guard 已通过；完整实机复测待 owner）
**Finding**: CR-2026-08-31-009
**兼容分类**: `COMPAT` + `OPERATIONAL`
**版本/Commit**: 未提交

**首次测试进度**

- 报告：`BossRushValidation_20260831_125247_246.log`。
- 已到阶段 3：13 PASS / 2 FAIL / 0 SKIP；基线 p95 18.19ms、峰值 47.84ms。
- 已通过：数据图鉴、图鉴过滤刷新、后山数据、日报回滚、PetNest v2 与奖励债务、词缀临时物品、
  UI 幂等清理、竞技场加载、标准模式启动。
- 失败 1：Campaign 正式 JSON 被 `JsonUtility` 静默解析为空表，回退 Fallback。
- 失败 2：清场把一个友方角色计成遗留敌人，触发安全中止。因此 Mode D/E/F/G/H、Zombie、
  终章/BGM 与最终回读尚未执行，本次性能也没有最终样本。
- Player.log 额外确认：Boss 乱入实际五次预设解析失败却被总表误报 PASS；动态商人出现
  MagicBlend 空 Playable 异常和“未配置商人”；Harmony scanner 对三个普通 `Cleanup` 方法误报；
  日报/征程运行时交互组件在 `base.Awake` 前缺官方私有分组空表。
- `MakeTimeQuacker.Bed2Interactable` 的 NRE 来自另一 Mod，不属于 BossRush 修改范围；
  DragonKing 缺少可选 trail prefab 已有对象池 fallback，本次日志未显示玩法失败。

**本轮修复与测试代码升级**

- Campaign 改用 `ModeHCanonicalDigest` 严格 parser；守卫禁止退回 `JsonUtility.FromJson`。
- Boss 乱入调用 SpawnCore 前初始化官方 preset cache；F3 为八个事件输出独立
  `RANDOM_EVENT_*` case，等待空投落地、Boss/商人生成、声源/烟花序列、现金堆和巡游鸭完成。
- 清场统计改为明确敌对角色，并把残留对象名、runtime team、preset key 写入报告。
- 限时商店以 `Merchant_Normal` 引导官方 Awake，同帧恢复稳定 Mod ID 后注入库存；新增
  MagicBlend 初始化顺序兼容补丁，最多等待 10 帧且只重放仍处于当前状态的回调。
- Harmony 逐类扫描先验证类级/方法级补丁元数据，不再让普通 `Cleanup` 方法进入 processor。
- 日报、征程公告板/终章召唤石、后山展示柜、词缀锻造和随机商店子交互在
  `base.Awake` 前统一初始化官方私有分组列表。
- 完整验收运行期间不向官方普通消息/大横幅队列写入，取消、异常和正常完成均复位抑制标记。
- 新增 `LatestPlayerLogRegressionGuard.py`，并加强 Campaign、随机事件、F3 与 Harmony 架构守卫。

**当前静态验证**

- Windows 正式版与 Dev 版 `compile_official.bat`：PASS（0 error，仅既有 JSON DTO `CS0649` warning）；
  Dev DLL 已最后部署到游戏 Mod 目录。
- `python tools/run_guards.py`：PASS=508 / NEW-FAIL=0 / KNOWN-RED=0；定向 Campaign、随机事件、
  F3、Harmony 与最新日志回归守卫也全部 PASS。
- `test_logic_official.bat`：8/8 PASS。
- `tools/verify_syntax.py --with-bcl`：739 个编译源语法探针 PASS；生产源码与编译清单双向核对 739 项。
- `git diff --check`：无空白错误。

**下一步实机门槛**

重新启动游戏后在同一专用测试档运行 F3 完整验收。CR-2026-08-31-009 在完整报告无 FAIL、
最终清场通过且 Player.log 无对应异常之前保持 Open；不因本轮静态成功提前标 Fixed。

---

### 2026-09-01 第二次 F3 报告同步与模式生命周期修复

**状态**: implemented（代码、定向守卫与 Windows 正式编译已通过；完整实机复测待 owner）
**Finding**: CR-2026-09-01-010
**兼容分类**: `COMPAT` + `OPERATIONAL`
**版本/Commit**: 未提交

**第二次测试进度**

- 报告：`BossRushValidation_20260831_152526_013.log`，对应最新 `Player.log`。
- 已到阶段 3：23 PASS / 4 FAIL。四个失败由三个根因产生，Mode E 清场失败后按安全规则中止。
- 已通过：Campaign 正式 JSON、图鉴及过滤刷新、日报回滚、PetNest v2 与奖励债务、词缀临时物品、
  UI 幂等清理、标准模式、7/8 随机事件、随机商人、标准清场与 Mode E 启动。
- 性能中途样本：baseline p95 17.55ms、peak 51.32ms、无单帧超过 200ms；因安全中止没有最终样本。
- 外部日志中的 `MakeTimeQuacker.Bed2Interactable.Awake` 与
  `TriangleDuckAttachmentExpansion.CleanupAllSystems` NRE 不属于 BossRush，不在本轮修改范围。

**确认根因与修复**

- 随机事件 Boss 乱入进入共享 `ModeEFSpawnPostprocess` 队列后，标准 WavesArena 的 early-return
  使 scheduler 不再 tick，任务一直 Pending，下一模式启动时才以 `scheduler_cleared` 失败。
  现把 scheduler 提到模式组 early-return 前；空队列路径仅一次 Count 判断。
- Mode D 只设置 AI target，未保证 runtime team 对玩家敌对；F3 因此看到已登记角色却没有可玩敌人。
  生成登记前现执行 wolf 安全网并回读敌对状态，仍非敌对则拒绝登记并销毁。结束模式会注销恢复、
  禁掉落并销毁本波角色；重复结束也会清理可能残留的登记实体。
- Mode E/F 为角色克隆 `characterPreset`，旧清理先 Destroy preset，再访问角色的掉落、Health 与
  OnDestroy 链，Unity 伪 null 窗口导致 14 个清理 NRE 和 3 个 `no_preset` 残留。现由角色上的
  `ModeECharacterPresetLease` 持有克隆预设，角色销毁后再延迟释放；模式结束按
  禁掉落 → 注销运行时 → 停用 → 销毁角色的顺序执行，不再用 Hurt 制造死亡副作用。
- Mode E 分类 `StockShop` 现与随机商人相同：inactive 创建、先写 `Merchant_Normal` 引导 Awake，
  同帧 Start 前恢复稳定 `ModeE_*` ID，再覆盖分类库存，消除每个分类以默认 `Albert` 查询的噪声。

**F3 测试升级**

- Mode D 用例同时输出登记对象的 active、Health、team 与 hostile 回读，避免只看总敌人数。
- 清场新增模式自有角色扫描，覆盖 inactive 的 `BossRush_` / `ModeD_` / `ModeE_` / `ModeF_` /
  `RndEvt_` / `ZombieMode_` 对象；普通敌对角色统计仍单独保留。
- 每次模式结束后的清场由固定 0.5 秒改为最多 2 秒逐帧轮询，等待 Unity 延迟 Destroy 完成；
  轮询失败会输出 owned/hostile 明细并立即中止，最终清场也复用同一诊断口径。

**当前验证**

- Windows 正式版与 Dev 版 `compile_official.bat`：PASS（0 error，仅既有 JSON DTO `CS0649` warning）；
  Dev 版最后部署。Build 与游戏目录 DLL 的 SHA-256 均为
  `6806798E82F9F015EF041C4E31A5D165566AEB5B616AA2B44CEDAD7BA8316625`。
- 生产源码与编译清单双向核对：740 项 PASS。
- `GameplayValidationRunnerGuard.py`、`LatestPlayerLogRegressionGuard.py`、
  `RandomEventsWaveIsolationGuard.py`、`ModeEShellHarmonyUiContractGuard.py`、
  `ModeEFSpawnPostprocessSchedulerGuard.py` 与相关 Mode D/E 守卫：PASS。
- `python tools/run_guards.py`：PASS=508 / NEW-FAIL=0 / KNOWN-RED=0。
- `tools/verify_syntax.py --with-bcl`：740 个编译源语法探针 PASS；`test_logic_official.bat`：8/8 PASS。
- Campaign 源/部署 JSON SHA-256 均为
  `45EB48336A246D88081349A040BC647045C224B32B2D8E480E8A11986B9C1E96`；
  `compile_official.bat` 全部 1113 个换行均为 CRLF；`git -c core.autocrlf=false diff --check`：PASS。

**下一步实机门槛**

重新启动游戏后在专用测试档运行 F3 完整验收。CR-2026-09-01-010 只有在 Boss 乱入、Mode D、
Mode E 清场及后续 Mode F/G/H、Zombie、终章、最终回读全部通过后才能转 Fixed。
