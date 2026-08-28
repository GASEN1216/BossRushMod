# BossRushMod 外部契约与破坏性变更边界

> 本文件汇总 Mod 与玩家存档、配置、资源、官方游戏、外部服务之间的契约。改动前先按 `AGENTS.md` 的兼容性分类标注影响。

## 1. TypeID 与存档身份

- 自定义物品/装备 TypeID 使用 5000xx 区间。
- 已登记范围：`500001-500058`，空洞 `500009`、`500047` 不回填。
- `500058` 已登记为便携安全区装置；下一可用 ID 以 `docs/Bossrush使用物品ID表.md` 实际末尾为准。

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

Mode H 尚未实现；未来拟议的可选配置 `modeHEnabled=false`、`modeHRealWarehouseStakeEnabled=false` 见第 6.1 节，当前不得当作已存在的配置 key。

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

Mode H 尚未实现；未来拟做可选 `SCHEMA+` 扩展：`modeHSpawnPoints`、`modeHStagingPos`、`modeHSpectatorPos`、`modeHPlayerSpawnPos`、`modeHExitPos`。旧 JSON 缺字段时只能使用已通过同一构建版本实机 smoke 的硬编码 fallback；没有有效擂台、隔离生成点、看台、玩家或出口点位就拒绝进入。Mode H entry intent 必须在切图前冻结精确 `sceneName + sceneID + sceneGeneration`，非目标或旧 generation 的 scene callback 不得消费。

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
