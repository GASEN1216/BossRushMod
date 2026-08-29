# BossRushMod 外部契约与破坏性变更边界

> 本文件汇总 Mod 与玩家存档、配置、资源、官方游戏、外部服务之间的契约。改动前先按 `AGENTS.md` 的兼容性分类标注影响。

## 1. TypeID 与存档身份

- 自定义物品/装备 TypeID 使用 5000xx 区间。
- 已登记范围：`500001-500059`，空洞 `500009`、`500047` 不回填。
- `500058` 已登记为便携安全区装置，`500059` 已登记为遗种蛋（遗种巢通用蛋，血脉写在物品 KV `PetNest_Lineage` 上，全谱系共用一个号）；下一可用 ID 以 `docs/Bossrush使用物品ID表.md` 实际末尾为准。

Breaking:

- 复用、删除、回填 TypeID。
- 改变已发布物品的 TypeID、存档 key、掉落引用。
- 把装备改成物品或把物品改成装备但不做迁移。

## 2. 配置契约

配置入口：

- `ModConfig`
- `StreamingAssets/BossRushModConfig.txt`（JSON）

已知配置 key 包括：

- `waveIntervalSeconds`
- `enableRandomBossLoot`
- `useLegacyBossLootProbabilities`
- `useInteractBetweenWaves`
- `lootBoxBlocksBullets`
- `infiniteHellBossesPerWave`
- `bossStatMultiplier`
- `modeDEnemiesPerWave`
- `disabledBosses`
- `bossInfiniteHellFactors`
- `enableDragonDash`
- `achievementHotkey`
- `useWolfModelForWildHorn`
- `enableDeathWraithSystem`
- `milestoneRestBonusSeconds`

Mode H 的唯一配置 key 是 `modeHEnabled`（默认 false），详见第 6.1 节。
**不存在也不得引入** `modeHRealWarehouseStakeEnabled`：真实押品没有开关，进入模式即知情同意（§22.1），`ModeHConfigApiGuard` 断言该符号一律不出现。

Breaking:

- 删除或重命名已有 key。
- 改变默认值导致旧玩家无操作时玩法明显变化。
- 改变值类型或单位，例如秒变帧、`KeyCode` 整数变字符串且无兼容解析。

## 3. 存档与持久化 key

新持久化 key 使用 `BossRush_` 前缀，避免与原版或其他 Mod 碰撞。

Mode G 冻结 key：

- `BossRush_ModeG_NemesisRecord_v1`
- `BossRush_ModeG_Profile_v1`

两个 key 独立 typed Save。未知 schema、payload 不可读或 key 分类失败时只为对应 key
建立当前槽写屏障，不覆盖未来版本；另一 key 仍可独立保存。`StoreFaulted` 在本 runtime
单向 fail-closed，不能靠切槽清除。

Breaking:

- 改名旧 key 且无迁移。
- 多 key 存储改成单 key 但不兼容读取旧格式。
- 清空玩家成就、好感度、婚姻、寄存、配置、Wiki 状态等持久数据。

## 4. 地图与 SpawnPoints JSON

`Assets/SpawnPoints/*.json` 是地图刷新点数据契约。字段至少包括：

- `sceneName`
- `sceneID`
- `spawnPoints`
- `modeESpawnPoints`
- `modeEPlayerSpawnPos`
- `customSpawnPos`
- `defaultSignPos`
- `mapNorth`
- `beaconIndex`
- `previewImageName`
- `displayNameCN`
- `displayNameEN`

Mode H 的地图点位是已实现的可选 `SCHEMA+` 扩展：`modeHSpawnPoints`、`modeHStagingPos`、`modeHSpectatorPos`、`modeHPlayerSpawnPos`、`modeHExitPos`。旧 JSON 缺字段时只能使用已通过同一构建版本实机 smoke 的硬编码 fallback；没有有效擂台、隔离生成点、看台、玩家或出口点位就拒绝进入。Mode H entry intent 必须在切图前冻结精确 `sceneName + sceneID + sceneGeneration`，非目标或旧 generation 的 scene callback 不得消费。

Breaking:

- 改字段名、删除字段、改变坐标单位或轴语义。
- 改 `sceneName` / `sceneID` 导致旧地图配置失配。
- 删除硬编码 fallback 但没有版本迁移和 guard 证明。

Mode G 当前生产契约（2026-08-17 owner 裁决）：

- 直接复用地图选择 UI 的 `ModBehaviour.GetAllMapConfigs()`；能够被 JSON registry 正常加载且包含非空 `sceneName`、`sceneID` 和 `spawnPoints` 的配置均为 Mode G 支持地图。首次有效读取形成零热路径分配的缓存快照，Mod runtime 销毁时由 `ResetStaticCaches()` 释放。
- 入口 preview 按当前 active scene 冻结玩家实际选择的 exact `sceneName + sceneID`，不再固定首张地图。
- 新增地图 JSON 会同时进入地图选择 UI 与 Mode G 支持集合；重复 pair、空字段或空刷新点仍 fail-closed。

## 5. 本地化契约

- `DisplayNameRaw = "BossRush_<Name>"` 必须有对应本地化注入。
- UI、Wiki、物品、装备、NPC 文本需要中文/英文双语路径，现状以 `L10n.T`、`LocalizationHelper`、`LocalizationInjector` 等为主。

Breaking:

- 删除已发布 key。
- 改 raw key 但不保留旧 key。
- 新玩家可见文本只写硬编码中文/英文且绕过本地化系统。

## 6. AssetBundle 与工厂命名契约

- 装备走 `Assets/Equipment/` + `EquipmentFactory`。
- 通用物品走 `Assets/Items/` + `ItemFactory`。
- 工厂依赖文件名、Prefab base name、`_Bullet` / `_Buff` 等命名规则。
- 历史拼写如 `dargon_Helmet`、`dargon_Armor` 不要随手纠正。
- 已发布自定义物品/装备 TypeID 必须登记到 `Integration/BossRushDynamicItemRegistry.cs`。官方存档、仓库、商店和 UI 可能早于延迟 bootstrap 调用 `ItemAssetsCollection.GetMetaData/GetPrefab/Instantiate*`，统一注册表是避免白底问号和 `FallbackItem_<id>` 的按需兜底入口。

Breaking:

- 改 AssetBundle 文件名或 Prefab base name 但不更新工厂配置。
- 把已有资源移动到新目录导致工厂扫描不到。
- 删除已有模型、图标、Buff、Projectile 依赖。
- 新增或迁移已发布 TypeID 后未同步 `BossRushDynamicItemRegistry`，导致重启后存档物品按官方 fallback 还原。

## 6.1 Mode H 外部契约（百战留痕：黑市鸭王杯，已实现）

Mode H 已按 `docs/设计提案/2026-08-17_斗蛐蛐新模式创意脑暴.md` §17–§29 一次性完整实现：
正式入口、五席试棚、三幕六战、虚拟整备与下注、口令/伤病/战痕、ERROR 完整互换、
战场快照续战、真实仓库 escrow/journal/清算、转会、名人堂、恢复与存档全部在本批交付。
本节替换 2026-08-26 的旧稿：**不再存在“先做 H0 技术样机”的阶段划分**，
也不再存在旧稿列出的真实资产开关与四门口径。

**配置面（COMPAT）。** `ModBehaviour.BossRushConfig` 只新增**一个**字段 `modeHEnabled=false`，
运行时只通过 `ModBehaviour.IsModeHConfiguredEnabled()` 读取。
ModConfig 镜像键只有 `BossRush_ModeHEnabled`，不存在时保持默认关闭。

**没有真实资产开关。** `modeHRealWarehouseStakeEnabled`、
`IsModeHRealWarehouseStakeConfiguredEnabled` 与 `ModeHStakeJournal.GatePassed`
三个符号一律不得出现，`ModeHConfigApiGuard.py` 与 `ModeHStakeJournalGuard.py` 显式断言这一点。
同意通过“进入模式”表达：入口页、模式说明与 `ModeHInteractable` 三处都固定显示
`BossRush_ModeH_RealStakeRiskNotice` 风险行。系统唯一会自行禁用真实押品的情况是
只读派生结果 `ModeHWarehouseStakeJournal.IsSlotConsistent` 为假，它不是可写开关。

**运行时门。** `ModeHRuntimeGates` 提供**五个**互不混用的 no-throw 只读结果：

- `IsModeHRunOwnerActive`：当前唯一 Mode H runtime 是否持有 owner。
- `IsModeHRiskScanReady`：当前槽的轻量持久风险头是否读取完成。
- `IsModeHContentReady`：配置、资源、地图、候选池与口令兼容矩阵是否具备运行正式自检的条件。
- `IsModeHExternalAssetRiskBlocked`：是否存在未终结真实资产 journal/operation 或风险未知。
- `IsModeHRecoveryOnlyBlocked`：是否存在 Season 恢复壳、late cleanup 或 slot barrier。

旧模式最终入口**只**读取 `IsModeHRiskScanReady` 与 `IsModeHExternalAssetRiskBlocked`，
绝不等待 H 内容或恢复壳状态；recovery-only 阻断不得并入普通 NPC 或场景分类 predicate。
三个 Mode H key 均不存在时，轻量扫描同步得到 ready 且 unblocked。
`OnSetFile` 后按新 `slotGeneration` 重新读取风险头，I/O 异常时最终模式入口 fail-closed 并提供重试。

**存档键（SCHEMA+）。** 三个 typed key 全部已实现：

- `BossRush_ModeH_Season_v1`
- `BossRush_ModeH_HallOfFame_v1`
- `BossRush_ModeH_StakeJournal_v1`

envelope 带 `schemaVersion`、`gameBuildSignature`、`modBuildSignature`、
`contentCatalogSignature`、`payloadDigest` 与 `slotGeneration`；
存档处理幂等订阅/退订 `SavesSystem.OnCollectSaveData`、`OnSetFile`、`OnSaveDeleted`。
`MatchSettling` 在一个完整 Season payload 中一次提交 report、profile、roster 与
唯一虚拟奖励 operation，report 只引用 operation ID；`Intermission` 不首次写入伤病或筹码。
名人堂跨 key 写入使用带完整记录快照和稳定 `hallOfFameId` 的 pending command，
按 ID 幂等插入、读回后再标记完成，上限 32 条。
删档清空对应 cache、pending barrier、recovery shell、owner/token、presentation 引用与 slot generation。

**玩家资产边界。** 只有三条白名单路径可以触碰玩家真实资产：
`ModeHEntry.TryRefundPrepaidTicket()`（唯一退款实现点）、
`ModeHLoadoutKitApplicator`（只访问 owner 标记且 inactive 的临时选手实例）、
以及 `ModeHWarehouseStakeJournal`（唯一真实仓库写入者，经
`ModeHInventoryPersistenceBridge` 落地）。其余 Mode H 文件不得出现
`Inventory`、`PlayerStorage` 或玩家 `ItemTreeData` 任一符号。
`ModeHSeasonRewardService` 与 `ModeHRewardTransaction` 都在白名单之外：
前者只发虚拟套装/名声，后者只生成不可变结果计划并经 journal 提交。

**构建签名兼容门。** 三签名对生产认证报告、活动 Season 与 preset/command/kit 审计是执行兼容门：
签名变化后不得继续生成或战斗，活动 Season 进入写保护并等待用户从恢复面板明确结束。
已完成的 HallOfFame 记录只把签名作为来源标记，旧构建记录仍可只读展示，不删除、不改写。

**数据目录。** `Assets/Data/ModeH/` 含七份 JSON：`BossProfiles.json`、`Commands.json`、
`CommandCompatibility.json`、`LoadoutKits.json`、`ThreatPlans.json`、`Scars.json`、`OddsWeights.json`。
每份都有 `schemaVersion`、稳定 ID 与自洽 `contentSignature`，加载后按 §20.2 生成
一个 `contentCatalogSignature`。安全审计数据没有跨构建 fallback；
只有纯数值权重（`OddsWeights.json`）允许同版本内置 fallback。
`LoadoutKits.json` 的 typeId 已固定为官方 `Item.TypeID`，运行时不再走
`ItemAssetsCollection.Search`；`resolveTags`/品质区间/序号保留为固定 id 失效时的降级检索口径。

**地图可选字段。** 地图配置支持经审计的 `modeHSpawnPoints`、`modeHStagingPos`、
`modeHSpectatorPos`、`modeHPlayerSpawnPos`、`modeHExitPos`；
字段缺失或构建签名不匹配时 `IsModeHContentReady=false`。

**本地化。** key 统一使用 `BossRush_ModeH_` 前缀，唯一来源是
`Localization/ModeHLocalization.cs`，由 `Integration/BossRushIntegration_StartAndScene.cs` 的
`ModBehaviour.InjectLocalization_Extra_Integration()` 注入；不创建第二个 JSON parser/registry。

**视觉制品（external / local-only）。** 展示资源不由根仓库源码 checkout 自动生成：

- bundle：`Assets/ui/modeh_presentation`
- Unity 格式：`UnityFS`，恰好两个 Sprite，零依赖，大小不超过 `256 KiB`
- Sprite 短名：`ModeH_BlackMarketCup_Emblem`、`ModeH_BlackMarketCup_Banner`
- 对应完整输入路径：`Assets/UI/ModeH/ModeH_BlackMarketCup_Emblem.png`、
  `Assets/UI/ModeH/ModeH_BlackMarketCup_Banner.png`
- 构建器：兄弟 Unity 工程 `Assets/Editor/ModeHPresentationBundleBuilder.cs`
- 构建输出：兄弟 Unity 工程 `ModeHExport/modeh_presentation`
- **2026-08-29 已落位基线**：`97,301` 字节，
  SHA-256 `04A9BFDFB7C9659A53A5E71CAFC453A133E2889D4E2AF2C9FEB7BF33233D24AC`，
  回载验证 `ModeH_BlackMarketCup_Emblem 256x256` / `ModeH_BlackMarketCup_Banner 1024x576`，
  依赖 `[]`。重新生成图片或换 Unity 版本后哈希变化属正常，但必须重跑全套验证再更新本行。

运行时一次预检并同时加载两张 Sprite；缺包或缺资源 fail-closed。
开发 raw PNG fallback 只由编译期常量 `ModeHAvailability.AllowDevRawPngFallback` 控制，
发布构建恒 false，不进配置、存档或发布制品。
卸载顺序固定为：销毁引用这两张 Sprite 的 UI 根 -> 清空 Sprite 引用 -> 幂等 `Unload(true)`。
`compile_official.bat` 与 `test_bossrush_official.bat` 显式复制 `Assets\Data\ModeH\*.json`
与上述 bundle；缺正式 bundle 时明确失败或把 Mode H 标成不可用，不静默依赖 fallback。
由于当前 `/Assets/*` 约定会忽略 UI 二进制，发布流程必须从外部 Unity 制品目录复制并记录
source/input/bundle SHA-256；若未来要把它们纳入 Git，需另行登记 `OPERATIONAL` 例外。

**Harmony 面。** Mode H 首发唯一允许的 Harmony 新增是
`CA_ControlOtherCharacter.CanMove` 与 `CanRun` 两个 postfix
（`ModeH/ModeHHarmonyPatches.cs`）。禁止扩大到 `CanUseHand`/`CanControlAim`
或全局 team、输入、索敌、死亡逻辑；额外死亡掉落抑制仍只走
`Patches/Combat/CharacterOnDeadPatch.cs` 的既有扩展。

**UI 层级面。** `BossRushUILayers` 新增四个常量，必须保持按数值升序声明：
`ModeHHud = 960`、`ModeHDiagnostics = 970`、`ModeHModal = 980`（插在 `ModeGEntry = 950`
与 `Hud = 1000` 之间），`ModeHRecovery = 3100`（插在 `Modal = 3000` 与 `ModalConfirm = 3200` 之间）。

## 6.2 遗种巢外部契约（PetNest 养崽系统，已实现）

遗种巢按 `docs/设计提案/2026-08-28_养崽系统创意脑暴.md` 与其附录 A（spec 定稿）实现：
遗种蛋 + 遗魂双轨获取、全 Boss 谱系幼体化、孵化 roll 与命名、单席随从进局、
重伤退场与战痕、天灾远征与真死、博物馆图鉴与纪念碑、驯养成就。
**数值全表为草案，待 owner 审定**（集中在 `PetNest/PetNestTuning.cs`）；
步骤 0 的实机闸门五项待 owner 验证。

**配置面（COMPAT）。** `ModBehaviour.BossRushConfig` 只新增**一个**字段
`petNestEnabled=false`，运行时只通过 `ModBehaviour.IsPetNestConfiguredEnabled()` 读取
（定义在 `Config/ConfigPetNest.cs`，与 `Config/Config.cs` 同一 partial class，
拆开只为 1200 行预算）。ModConfig 镜像键只有 `BossRush_PetNestEnabled`，
不存在时保持默认关闭。关闭时整个子系统 dormant：不订阅存档、不建血脉目录、
不 tick 协调器、不产蛋、不生成任何角色。开关运行时可变，
bootstrap 幂等且可退回 dormant。

**TypeID（SCHEMA+）。** 只占 `500059` 遗种蛋一个号。**通用蛋 + KV 记血脉**：
全 Boss 谱系共用这一个 TypeID，血脉写在 `Item.Variables` 的 `PetNest_Lineage` 上，
随 `ItemTreeData` 持久化；展示名走 `Var_PetNest_Lineage`。
`MaxStackCount = 1` 是硬要求——堆叠合并会把两枚不同血脉的蛋并成一枚，血脉信息丢失。
遗魂与遗物不占号（遗魂是纯账本，不掉实体）。

**存档键（SCHEMA+）。** 三个 key 全部 `SavesSystem.Save<string>` **JSON 整存**：

- `BossRush_PetNest_Nest_v1`（崽列表 / 出战席位 / 遗魂账本 / 巢容量）
- `BossRush_PetNest_Expedition_v1`（进行中远征 + 已结算未翻牌）
- `BossRush_PetNest_Museum_v1`（图鉴统计 / 纪念碑 / 异色收集）

envelope 固定为 `{schemaVersion, payload}`。**不用 typed `Save<T>`**：ES3 会把
assembly-qualified 类型名写进存档，mod 程序集改名或类型重构就会让老档读不回来。
`schemaVersion` 不符、payload 不可读或 key 分类失败时只为对应 key 建立**写屏障**
（只读不覆盖），另两个 key 仍可独立保存；写入路径异常进入单向 `StoreFaulted`。
三个官方存档事件 `OnCollectSaveData` / `OnSetFile` / `OnSaveDeleted` 幂等订阅与退订。
`PetNest/PetNestSaveCoordinator.cs` 是遗种巢**唯一**调用 `SavesSystem.SaveFile` 的地方，
每批至多一次；`IsSaving` 时改走 deferred，重试有预算上限，超预算保留 pending 并报错。
高频写（每次击杀记遗魂、统计计数）只入队不落盘，由官方采集与切图/回基地的 flush 写下去。

**Harmony 面：零新增补丁。** 遗种巢不新增任何 `[HarmonyPatch]`，只在两条既有链上加消费者：

- 致死钳制链（`Patches/Combat/BossLethalHealthProtectionPatch.cs`）第四消费者
  `TryClampPetNestCompanion`：钳 1 血 + 登记退场，先读静态 armed bool 早返；
- 死亡抑制链（`Patches/Combat/CharacterOnDeadPatch.cs`）第三 registry
  `PetNestDeathSuppressionRegistry`：命中时**只**跳过本 Mod 的两个额外掉落 handler，
  不返回 false、不改写原版 OnDead。

`Health.Hurt` 的 Prefix 签名**不得改动**（`ReverseScaleLethalProtectionGuard` 与
`ModeGPerformanceGuard` 断言该字面量）；战痕的凶手改由**只在随从在场期间**订阅的
官方 `Health.OnHurt` 静态事件记录，离场立刻退订。

**反射面：唯一一处反射写。** `PetNest/PetNestPetProxyBridge.cs` 反射写
`LevelManager` 的私有字段 `petCharacter`（`AccessTools.Field`），用于让官方
`PetProxy` 的捡漏背包跟随随从。**实测修正**：该字段并非"无可见赋值点"——每张图的
关卡初始化都会创建官方宠物并占席，因此实现的是**借席不夺席**：只在非基地图借席、
借席前记录原占位者、离场/死亡/切图必然还原、还席前核对席位仍是自己的随从、
反射解析失败或字段类型变更一律 fail-closed（随从无背包，不崩）。

**版本升级检查单（隐性契约）。** 官方更新后必须复查：

- `LevelManager.petCharacter`（私有字段名与类型）——唯一反射写点；
- `AICharacterController.leader`（public 字段）——跟随驱动；
- `CharacterMainControl.modelRoot`（public Transform）——幼体视觉缩放；
- `CharacterRandomPreset` 的 `hasSkill` / `exp` / `hasSoul` / `team` / `dropBoxOnDead`
  ——中性化五件套；
- `PetProxy.Update` 的门控条件与 `CharacterMainControl.PetCapcity` 容量同步语义
  （官方拼写就是 `PetCapcity`，少一个 a）；
- `Health.OnHurt` 静态事件签名——战痕凶手来源。

**本地化。** key 统一使用 `BossRush_PetNest_` 前缀，唯一来源是
`Localization/PetNestLocalization.cs`，由 `InjectLocalization_Extra_Integration()` 注入。
建筑键按官方约定用 `Building_petnest_relic_nest` / `_Desc`（不带模块前缀）。

**UI 层级面。** `BossRushUILayers` 新增三个常量，必须保持按数值升序声明：
`PetNestCompanionHud = 990`（插在 `ModeHModal = 980` 与 `Hud = 1000` 之间）、
`PetNestPanel = 2100`（插在 `Panel = 2000` 与 `Modal = 3000` 之间）、
`PetNestModal = 3150`（插在 `ModeHRecovery = 3100` 与 `ModalConfirm = 3200` 之间）。

**成就分类面（SCHEMA+）。** `AchievementCategory` 末尾追加 `Taming`。
新分类只能追加到末尾——分类排序与存档都依赖 int 值，插在中间会让老档里已解锁成就的
分类整体错位。

**资源制品（2026-08-29 已落位，此前为占位）。** 三件，全部随 `compile_official.bat` 部署：

| 制品 | 路径 | 规格 | 消费者 |
| --- | --- | --- | --- |
| 建筑模型 bundle | `Assets/buildings/petnest_relic_nest` | `UnityFS`，prefab 短名 `PetNestRelicNest`，14,387 B，43 renderer / 0 collider | `LoadPetNestBuildingModel()` |
| 建筑图标 | `Assets/buildings/petnest_relic_nest.png` | 256×256 RGBA，铅笔线稿（与既有建筑图标同风格） | `LoadPetNestBuildingIcon()` |
| 遗种蛋图标 | `Assets/Items/relic_egg.png` | 512×512 RGBA | `EquipmentHelperIcon.TryInjectIcon` |

构建器 `Assets/Editor/PetNestBuildingBundleBuilder.cs`（兄弟 Unity 工程），
prefab 由脚本确定性生成，不依赖手工场景摆放。

**fallback 仍是契约**：三件资源任一缺失都不得 fail——建筑退回运行时占位圆柱体、
图标退回官方默认。不要因为资源已落位就删掉 fallback 分支。

历史口径（首版规划）：建筑走占位模型（巢体 + 三枚蛋，`CreatePrimitive` 自带的
Collider 必须删），蛋图标复用官方 fallback 物品。占位路径至今保留为 fallback。
幼体外观始终是官方模型缩放，这一条不变。

**掉落范围。** 挂接点是 `LootAndRewards.RegisterBossRandomLootTracking` 体内单行并联，
覆盖标准三档 / Mode D / E / F，天然**不含** Mode G 托管路径（其 adapter 会
`ClearBossRandomLootTracking`）与丧尸模式。随从进局门控另有一刀切禁入名单：
Mode G、末日丧尸、Mode H（实装期新增的保守判定，`Needs owner confirmation`）。

Breaking：

- 复用或回收 `500059`。
- 改名三个存档 key 且无迁移。
- 把 `AchievementCategory.Taming` 插到枚举中间。
- 改动 `Health.Hurt` Prefix 的既有签名。
- 把捡漏背包从"借席不夺席"改成夺席不还。

## 6.3 鸭科夫日报外部契约（DailyReport 日报系统，已实现）

日报按 `docs/未来拓展/设计/P2-日报系统.md` 实现：自算游戏日出刊、昨日战绩新闻化、
每日悬赏、签到梯度奖励、明日天气预报与趣味杂谈，投递载体是玩家自建的报箱建筑。

**配置面（COMPAT）。** `ModBehaviour.BossRushConfig` 只新增**一个**字段
`dailyReportEnabled=true`，运行时只通过 `ModBehaviour.IsDailyReportConfiguredEnabled()` 读取
（定义在 `Config/ConfigDailyReport.cs`，与 `Config/Config.cs` 同一 partial class，
拆开只为 1200 行预算）。ModConfig 镜像键只有 `BossRush_DailyReportEnabled`。
默认开启的理由：报箱要玩家花 500 金自建，已是天然门槛，不必再用开关拦一道。

**存档面（SCHEMA+）。** 新增**一个**槽级 key `BossRush_DailyReport_v1`，
`SavesSystem.Save<string>` 整存扁平 JSON，顶层带 `schemaVersion`。
不用 typed `Save<T>`：ES3 会把 assembly-qualified 类型名写进存档，
mod 程序集改名/重构就会让老档读不回来。
未知或更高 `schemaVersion`、payload 不可读时进写屏障，**只读不写，绝不覆盖该 key**。
`Integration/DailyReport/DailyReportSaveCoordinator.cs` 是日报**唯一**调用
`SavesSystem.SaveFile` 的地方，且每批至多一次；`IsSaving` 时只登记 deferred 由宿主 tick 重试。

**DTO 扁平化是契约的一部分。** 里程碑领取用位掩码 `periodClaimedMask` 而不是 token 列表，
往期数据用定长编码而不是嵌套对象。这样编解码只需 `Utilities/SimpleJsonHelper.cs`，
不必引入第三套 JSON 解析器（ModeH 有 `ModeHJsonValue`、遗种巢有 `PetNestJson`，
两者都与各自模块语义绑定，互相 import 会让彼此成为对方的升级阻塞项）。

**计时口径（不可改）。** 一天 = **86300 游戏秒**，镜像官方 `GameClock.SecondsPerDay`
（**不是 86400**）。天数由 `DailyReportService` 自算：累计宿主
`deltaTime × clockTimeScale`，与官方 `GameClock.Update` 逐帧同源。
**禁止订阅 `GameClock.OnGameClockStep`，禁止改读 `GameClock.Day`**——
官方睡觉（`SleepView`）与 Continue 跳早 7 点（`LevelManager.OnNewBoot`）走 `StepTimeTil`
不经 `Update`，自算口径天然把这些跳变排除掉；改成跟随官方 Day 会让玩家睡一觉就白跳一期。
由 `tests/DailyReportPersistenceGuard.py` 守卫。

**建筑面（发布即冻结）。** 建筑 ID `bossrush_daily_mailbox`，占地 1x1，
造价 500 金，`maxAmount=1`。玩家放置记录以此 ID 进官方 `BuildingData` 存档，
**永不可改名**，否则老存档里的报箱会变成缺 prefab 的幽灵。
prefab 名 `BossRushDailyMailbox` 必须与 `BuildingInfo.prefabName` 严格一致
（官方 `GetPrefab` 按 `e.name == prefabName` 匹配）。

**本地化面。** key 统一使用 `BossRush_DailyReport_` 前缀，唯一来源是
`Localization/DailyReportLocalization.cs`，由 `InjectLocalization_Extra_Integration()` 注入。
建筑键按官方约定用 `Building_bossrush_daily_mailbox` / `_Desc`（不带模块前缀）。
**日报正文不进本地化表**：它是每天变的动态长文，塞进全局字符串表既污染表也无法按天变，
一律走 `L10n.T(cn, en)` 内联。

**UI 层级面。** 不新增层级常量，复用既有 `BossRushUILayers.Panel = 2000`。
纸张的浅色配色是局部参数不是第二套 token：`BossRushUI.ApplyPanelSkin` 只给形状不给色。

**零新增 TypeID、零新增资源制品。** 奖品全部从官方物品表按品质随机抽取
（经 `LootBlacklistRegistry` 过滤），不新造物品；报箱走占位模型
（立柱 + 箱体 + 斜盖 + 小红旗，`CreatePrimitive` 自带的 Collider 必须删）。
将来补美术只需在 `Assets/buildings/` 放同名 bundle 与 png，代码零改动。

**发奖路径。** 走 `CourierService.QuickDeliverItems` → `PlayerStorage` 快递缓冲
（官方 `StorageDock` 待领 UI），因此玩家在战斗中跨天也安全，不会往战斗背包里塞东西。
发放顺序是**先发后标记**：奖品到手才置领取掩码，宁可极端情况下重发也不吞奖励。

Breaking：

- 改名或复用建筑 ID `bossrush_daily_mailbox`。
- 改名存档 key `BossRush_DailyReport_v1` 且无迁移。
- 把一天的秒数从 86300 改成 86400（会与官方时钟累计漂移）。
- 把计时改成跟随 `GameClock.Day` 或订阅 `OnGameClockStep`。
- 把 DTO 改成嵌套结构（会逼出第三套 JSON 解析器）。

## 7. Harmony、反射与官方游戏契约

Mod 对官方游戏没有稳定 public API，依赖 Harmony patch、`AccessTools`、字符串反射和强绑定类型。

高风险：

- `[HarmonyPatch(typeof(...), "...")]` 目标方法。
- `Projectile.Init(ProjectileContext)` 等重载敏感目标。
- Projectile 私有字段 FieldRef。
- `CharacterMainControl` 事件字段、装备槽、商店、场景生命周期。

Breaking/Operational:

- 官方游戏更新后目标消失或签名漂移。
- 新 patch 未指定重载导致 `PatchAll()` 歧义。
- 反射失败被 catch 吞掉造成静默功能死亡。

官方更新后按 `docs/架构说明/Harmony补丁契约稳定性.md` 复查。

## 8. Wiki 内容契约

- `WikiContent/catalog.tsv` 索引游戏内 Wiki 条目。
- markdown 文件路径、条目 ID、标题需保持 catalog 一致。

Breaking:

- 删除 catalog 条目但 UI 仍引用。
- 改条目 ID 或文件路径但不更新索引。

## 9. 外部服务与密钥

星愿许愿台使用飞书 API 记录愿望。密钥资料在本地 docs 中，可能含敏感信息。

Off-limits:

- 泄露 App ID、secret、token、webhook。
- 改外部请求 schema、鉴权方式、目标表格或部署配置，除非 owner 明确确认。

## 10. Guard 契约

`tests/*.py` 是静态契约守卫。代码结构改变时同步 guard；不要为通过而删除关键断言。

Breaking:

- 删除 guard 覆盖的不变量。
- 把 guard 检查对象移走但不更新脚本。
- 新增子目录后让 guard 漏扫。

## 11. Mode G Boss 池发布契约

2026-08-17 owner 已确认现有过滤 Boss 池可整体用于 Mode G：

- 唯一来源是 `InitializeEnemyPresets()` + `GetFilteredEnemyPresets()`，不维护第二份硬编码 Boss 名单。
- 托管三 Boss 的 Legacy preset 仍从普通官方池排除；空 key、同 stable key 多 preset 引用仍 fail-closed。
- 至少 1 个唯一官方 key 即可启动。6 个是完整编排目标；池为 1-5 个时，`ModeGWavePlan` 使用本局 `runSeed` 从已有 stable key 确定性随机复制 primary/reserve，不伪造新 preset、不修改 run-scoped 快照。
- `TrustConfiguredBossPool=true` 只批准现有池成员，不允许绕过池快照按任意字符串生成 Boss。
- `disabledBosses` 继续影响过滤池；只有过滤后没有任何可用普通 Boss 时，Mode G 才不消费入场物品并拒绝启动。

Mode G 入场与旧路牌契约（2026-08-18 owner 裁决）：

- 玩家携带船票与宿命回响信物即可沿 Mode F 同类自动分流进入 Mode G，允许保留当前武器、装备、弹药和消耗品；营旗与血猎收发器仍按 Mode E/F 优先级拒绝 Mode G。
- 旧 BossRush 路牌只保留三个 Legacy 难度选项，不再注入 Mode G 第四项。过图后由短命 `ModeGInteractable` presenter 打开契约二选一确认页。
- Mode G 不移动、卸下、复制或保险玩家装备；死亡损失继续服从当前地图的官方规则。
