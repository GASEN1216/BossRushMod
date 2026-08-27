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

## 6.1 Mode H 拟议外部契约（尚未实现）

截至 2026-08-26，当前 `Config/Config.cs`、运行时代码、`Assets/Data/ModeH/` 和 `tests/` 中均没有 Mode H 实现。本节只保留 `docs/设计提案/2026-08-17_斗蛐蛐新模式创意脑暴.md` 已收敛的未来兼容面，不能当作现有代码事实，也不能据此宣称 Mode H 已可进入生产。当前只允许先实现不创建长期存档、不接触玩家资产的 H0 技术样机；H0 实机证据通过后，才可开始完整 Lite。

未来配置按 `COMPAT` 扩展处理：`Config/Config.cs` 的 `ModBehaviour.BossRushConfig` 拟增加 `modeHEnabled=false` 与 `modeHRealWarehouseStakeEnabled=false`，运行时代码拟只通过 `ModBehaviour.IsModeHConfiguredEnabled()` 和 `ModBehaviour.IsModeHRealWarehouseStakeConfiguredEnabled()` 读取。隔离状态拟由 no-throw 只读 `ModeHRuntimeGates` 提供四个互不混用的结果：

- `IsModeHRunOwnerActive`：当前唯一 Mode H runtime 是否持有 owner。
- `IsModeHRiskScanReady`：当前槽的轻量持久风险头是否读取完成。
- `IsModeHContentReady`：Mode H 配置、资源、地图、候选池和口令兼容矩阵是否可用。
- `IsModeHPersistentRiskBlocked`：是否存在未终结 journal、恢复壳、late cleanup 或 slot barrier。

Mode H 自身入口拟同时要求配置启用、风险扫描完成、内容就绪且没有持久风险。旧模式最终入口只允许读取 `IsModeHRiskScanReady` 和 `IsModeHPersistentRiskBlocked`，不得等待 H 内容扫描；三个 Mode H key 均不存在时，轻量扫描应同步得到 ready 且 unblocked。`OnSetFile` 后必须按新 `slotGeneration` 重新读取风险头，I/O 异常时最终模式入口 fail-closed 并提供重试。持久风险门只进入最终模式/地图入口，不并入普通 NPC 或场景分类 predicate。配置文件拟使用同名 camelCase 字段；可选 ModConfig 镜像键保留为 `BossRush_ModeHEnabled` 与 `BossRush_ModeHRealWarehouseStakeEnabled`，不存在时保持默认关闭。

H0 不创建任何正式 Mode H key。Lite 未来拟增加两个 `SCHEMA+` typed key，真实资产实验另保留一个独立 key：

- Lite：`BossRush_ModeH_Season_v1`
- Lite：`BossRush_ModeH_HallOfFame_v1`
- 真实资产实验：`BossRush_ModeH_StakeJournal_v1`

启用后的 envelope 必须带 `schemaVersion`、`gameBuildSignature`、`modBuildSignature`、`payloadDigest` 和 `slotGeneration`。存档处理必须幂等订阅/退订 `SavesSystem.OnCollectSaveData`、`OnSetFile`、`OnSaveDeleted`。Lite 只使用 `Assets/Data/ModeH/LoadoutKits.json` 的虚拟套装和赛季内虚拟奖励，不枚举、读取或写入玩家 `Inventory`、`PlayerStorage` 或 `ItemTreeData`，也不创建 active stake journal。赛季结算只在一个完整 Season payload 中保存 report、profile、roster 和唯一虚拟奖励 operation；report 只引用 operation ID。名人堂跨 key 写入使用带完整记录快照和稳定 `hallOfFameId` 的 pending command，按 ID 幂等插入并读回后再标记完成。真实物品奖励、仓库抵押及其 `ModeHRewardOperationDto`/journal 只能在后续实验中启用；删档时必须清空对应 cache、pending barrier、recovery shell、owner/token、presentation 引用和 slot generation。

构建签名对 H0 报告、活动 Season 和 preset/command/kit 审计是执行兼容门：签名变化后不得继续生成或战斗，活动 Season 进入写保护并等待用户从恢复面板明确结束。已完成的 HallOfFame 记录只把签名作为来源标记，旧构建记录仍可只读展示，不得因当前构建不同而删除或改写。

Lite 拟议数据目录为 `Assets/Data/ModeH/`，至少包含 `BossProfiles.json`、`Commands.json`、`CommandCompatibility.json`、`LoadoutKits.json`、`ThreatPlans.json`、`Scars.json` 与 `OddsWeights.json`。地图配置拟增加经审计的 `modeHSpawnPoints`、`modeHStagingPos`、`modeHSpectatorPos`、`modeHPlayerSpawnPos` 和 `modeHExitPos`；字段缺失或构建签名不匹配时 `IsModeHContentReady=false`。

实现后，本地化 key 统一使用 `BossRush_ModeH_` 前缀，代码入口拟为 `Localization/ModeHLocalization.cs`，并由 `Integration/BossRushIntegration_StartAndScene.cs` 的 `ModBehaviour.InjectLocalization_Extra_Integration()` 注入，不创建第二个 JSON parser/registry。

正式 Lite 视觉制品拟作为外部 local-only 资源交付，不由根仓库源码 checkout 自动生成：

- bundle：`Assets/ui/modeh_presentation`
- Unity 格式：`UnityFS`，恰好两个 Sprite，零依赖，大小不超过 `256 KiB`
- Sprite 短名：`ModeH_BlackMarketCup_Emblem`、`ModeH_BlackMarketCup_Banner`
- 对应完整输入路径：`Assets/UI/ModeH/ModeH_BlackMarketCup_Emblem.png`、`Assets/UI/ModeH/ModeH_BlackMarketCup_Banner.png`
- 构建输出：兄弟 Unity 工程 `ModeHExport/modeh_presentation`

实现生产运行时后，必须一次预检并同时加载两张 Sprite；缺包或缺资源时 fail-closed。开发 raw PNG fallback 只由编译期开发门控制，不能进入配置、存档或发布制品。销毁 UI 根、清空 Sprite 引用后才能 `Unload(true)`。届时 `compile_official.bat` 与 `test_bossrush_official.bat` 必须显式复制 `Assets\\Data\\ModeH\\*.json` 和上述 bundle；缺正式 bundle 时 guard/预检失败，不静默依赖 fallback。由于当前 `/Assets/*` 约定会忽略 UI 二进制，发布流程必须从外部 Unity 制品目录复制并记录 source/input/bundle SHA-256；若未来要把它们纳入 Git，需另行登记 `OPERATIONAL` 例外。

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
