# BossRushMod 外部契约与破坏性变更边界

> 本文件汇总 Mod 与玩家存档、配置、资源、官方游戏、外部服务之间的契约。改动前先按 `AGENTS.md` 的兼容性分类标注影响。

## 1. TypeID 与存档身份

- 自定义物品/装备 TypeID 使用 500xxx 区间。
- 已登记范围：`500001-500103`，空洞 `500009`、`500047` 不回填。
- `500058` 已登记为便携安全区装置，`500059` 已登记为遗种蛋（遗种巢通用蛋，血脉写在物品 KV `PetNest_Lineage` 上，全谱系共用一个号）；`500060` 已登记为词缀熔石（词缀锻造材料，词缀身份写在装备 KV `AFX_*` 上），`500061` 已登记为鸭皇图鉴（图鉴面板入口物品，使用不消耗）；`500062-500064` 已登记为后山菜地种子（龙裔之种、龙皇焰种、幽魂孢子），`500065-500067` 已登记为后山出击餐（龙息果、焚心椒、幽影蘑菇）；`500068-500072` 已登记为天空岛物品（晴岚航徽、噬风之核两件纪念品，风标罗盘道具，归航菜便当、星苔药膏两种岛上特产；纪念品发放记录写在天空岛剧情存档的 `discoveredNotes` 里，id 前缀 `Keepsake_`）；`500073-500082` 已登记为天空岛内容批次三（云苔纤维、青穗草、浮木、残铜片、风晶碎片、星屑六种采集材料，晴岚风晶一件碎片凑整物品，风灯、驱风香、晴岚护符三种局内耗材；采集点与增益按出击刷新，不加存档字段）；`500083-500085` 已登记为天空岛内容批次四「云蚋」（云苔纱笠随身装备、风晶灭蚊灯局内耗材、药烟蒲扇岛上工具；放回蛙鸣池的蛙卵记在天空岛剧情存档的 `discoveredNotes` 里，id 前缀 `Frog_`，不加存档字段）；`500086-500089` 已登记为天空岛头目 / 岛主的专属装备（星铜护目盔、星炉背甲、星炉背囊出自岛主残星匠首，观星镜盔出自头目瞭台观星手；走装备 bundle `skyisland_boss_gear` 注册、全部登记掉落黑名单；首杀记录写在天空岛剧情存档的 `discoveredNotes` 里，id 为 `Lord_Foreman` / `Chief_Stargazer`，不加存档字段）；`500090-500102` 已登记为天空岛头目 / 岛主 R2–R4 的专属装备（根须面罩、藤编甲、悬根箭囊出自岛主悬根猎首，旧邮包出自头目截信人，青穗斗笠、蓑衣甲、谷囊出自岛主穗镰，静听耳罩出自头目听雨人，苔纱面罩出自头目蚋笛翁，镜纹甲出自头目镜中客，断风兜帽、断风披甲、断风行囊分别出自断风游猎 · 守 / 追 / 伏；同一装备 bundle、全部登记掉落黑名单；首杀记录同样写进 `discoveredNotes`，id 前缀 `Lord_` / `Chief_`，不加存档字段）；`500103` 已登记为失落的航向仪（Jeff 序章「云上的坐标」的交付物：零号区的断风游猎 · 守倒下后留在官方尸体箱里，带回基地交给 Jeff 换 5000 金钱与晴岚航线，交付即消耗；售价 0、登记掉落黑名单，不加存档字段）；下一可用 ID 以 `docs/reference/Bossrush使用物品ID表.md` 实际末尾为准。

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

`randomEventsEnabled` 默认 true，玩家可在 ModConfig 开关；不再被恒开策略覆盖。`backMountainUnlockAll` 旧字段保留兼容，正式构建恒返 false，不注册设置。

Mode H 保留唯一配置字段 `modeHEnabled`（当前默认 true，内容恒开策略），详见第 6.1 节。
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

鸭王征程 / 竞技场后山 冻结 key（M0 起）：

- `BossRush_Campaign_Progress_v1` — 章节进度、契约状态、线索解锁、已授予 token
- `BossRush_BackMountain_Showcase_v1`（2026-09-22 `SCHEMA+`：新增可选 `sourceVersion`，缺失 = 1 老登记簿、2 = 官方枪械展示架 / 假人实摆；`schemaVersion` 保持 1，升版会让老档被 `EnsureLoaded` 永久写保护；语义改为「官方陈列柜里现在摆着的 Mod 战利品」，老登记簿只在基地找到官方柜时被覆盖） — 展示柜收藏
- `BossRush_BackMountain_RaidMeal_v1` — 出击餐待生效登记
- `BossRush_BackMountain_GardenRatchet_v1` — 槽位级 `bool`，由
  `GardenSeedInjector.RatchetSaveKey` 定义。缺键或读取失败视为 false，由当前解锁状态决定是否注入；
  解锁并注入作物后单向写 true，表示本槽可能已经种过 Mod 作物，以后即使解锁查询暂不可用也维持注入。
  它不是 JSON，没有独立 `schemaVersion` 字段；不得改名、重置为 false 或清掉旧值，否则可能使已种作物失去引用。
- `BossRush_BackMountain_StarterSeeds_v1` — 槽位级 `bool`（2026-09-23 `SCHEMA+`，`GardenSeedInjector.StarterSeedsSaveKey`）。缺键 = 起步种子还没发；
  菜地开放后主角在基地就绪时先写 true 并回读、再发三种种子各 2 颗。回读失败或一颗都没送出去时写回 false 以便下次重试；
  已发过的槽不再发。旧档没有这个键，第一次在基地就绪时补发一次。

常量单点分别在 `Campaign/CampaignTuning.cs` 与
`Integration/BackMountain/BackMountainConfig.cs`，由
`tests/CampaignSkeletonGuard.py`、`tests/BackMountainStructureGuard.py` 钉住字面值。

鸭皇图鉴冻结 key：

- `BossRush_Codex_v1` — `CodexTuning.StorageKey`，槽位级 JSON 字符串，`schemaVersion = 1`。
  已有条目的 Boss key、击杀进度与收藏身份属于兼容面；使用当前 `CodexCodec` 读取，
  缺键创建默认空数据，未知版本或不可读内容由共享存档门面建立本槽写屏障，禁止覆盖原数据。
  门面的 `StoreFaulted` 是本 runtime 的单向故障状态，不能通过换槽绕过。
  `CodexPersistence.Store` 只接受待保存快照，物理落盘由 `CodexSaveCoordinator` 负责。
  切槽／删档／槽位漂移必须同步复位目录、采集器及保存协调器，不得把上一槽的收藏带入下一槽。
  击杀在候选快照上修改，Store 接受后才发布收藏、同步目录和评估成就；拒写不改变当前进度。
  v1 的 entries 必须是数组，已有条目 key 不得重复或缺失；错误字段、非有限计时、超上限数组整体拒读，
  由共享门面保护原始数据，不能跳过坏条目或截断后覆盖。缺少可选统计字段仍按 0 / 空串读取。

以上两键于 2026-09-06 补齐登记（D-5，`SAFE`）：只记录既有实际格式，不增加字段或迁移数据。

晴岚群岛冻结 key（2026-09-08，`SCHEMA+`）：

- `BossRush_SkyIsland_Story_v1` — `SkyIslandStoryRules.StorageKey`，独立槽位 JSON 字符串，`schemaVersion = 1`。
  `flags` 保存两航标、四支线、三捷径与两个角色的互斥结果、终章；`visitedRegions` 的位 0–7 对应 A–H，
  位 8–11 对应 S1–S4；`clearedEncounters` / `discoveredNotes` 保存稳定内容 ID。枚举位与 ID 不随显示名变化。
  2026-09-16 扩展三个入口事实位：`PreludeAccepted = 65536`、`PreludeInstrumentRecovered = 131072`、
  `RouteUnlocked = 262144`；同日再扩六位岛上主线任务的接取 / 交付事实 `BeaconQuestAccepted = 524288` … `HomecomingQuestDelivered = 16777216`
  （`KnownFlags = 33554431`）。已解锁航线的旧槽按既有事实一次性回填对应任务位（`TryBackfillIslandQuests`）；Codec 拒绝「交付无接取」「交付无对应事实」「未解锁航线却有任务位」。既有槽只有出现旧旗标、到访、清场或手记任一真实群岛事实时才一次性补齐三位；
  完全空白槽不迁移。schemaVersion 与 key 保持不变，Codec 拒绝「未接任务却已取物证」或「未完成前两步却已解锁航线」的矛盾数据。
  `islandQuestMigrationComplete` 是可选布尔字段（2026-09-17，`SCHEMA+`）：缺省 false；存在但不是布尔值时拒绝解码。
  正常解锁航线 / 接取任务时置 true；旧槽回填只执行一次，已有任意岛上任务位时只标记迁移完成，绝不替玩家交付。
  钟庭事件解决且两端航标已亮即可在钟守处接「归航钟」，不必先回码头交「钟庭之争」；可先敲钟、就地交付，再顺路向浮舟复命。
  不保存 Unity 对象，不与 Campaign 或好感字段混用；未知版本、坏字段、矛盾结局经共享 store 建立写屏障。
  独立出击地图仅在会话确认无战斗时经共享 coordinator 保存；离岛推迟由 `SkyIslandStorySaveRecovery` 保留 owner 重试。
  入口在同槽恢复 owner 尚未结束时不得创建第二个 store；换槽、同槽删档必须使旧会话失效。

成就现金领奖补偿（2026-09-22，`SCHEMA+ / COMPAT`，修 `CR-2026-09-21-007`）：

- `BossRush_AchievementCashIntent_v1`：全局字符串，空值表示无待结清领奖；非空采用 `1|slot|achievementId`，先成功落盘才允许发钱。其他槽不能接管未结清意向。
- `BossRush_AchievementCashReceipts_v1`：当前槽 JSON，`schemaVersion=1`，`receipts` 为已发现金的成就 ID 数组；缺键即空，未知版本、坏数组或重复 ID 建立写屏障。

`AchievementRewardJournal` 复用共享槽位 store 与保存协调器。现金快照与本槽收据同批持久化后才提交既有全局领奖标记，最后清意向。物理保存失败可在原槽重试，不重复到账；全局标记已成功但清意向失败时按已领事实补清。既有成就 ID、全局解锁/领取 key 与奖金不变。天空岛任务现金同样先确认实际到账，再把 `EconomyData` 快照与交付旗标交给同一存档批次；不得恢复为先交付旗标、后无保证发钱。

Dev 专用测试档的 `BossRush_Validation_AutotestSnapshot_v1` 保持 version=1，2026-09-22 增加可选布尔 `bufferCountsIncluded`（SCHEMA+）。新快照为 true，物品数量包括背包、仓库和官方收件箱 Buffer；缺字段视为 false。旧快照遇到相关 Buffer 物品时无法恢复测试前基线，必须保留恢复键并报告 `legacy_snapshot_buffer_baseline_missing`，不能按零基线删物。新快照先回收增量、核对三处总数并采集全部容器，成功后才清恢复键；任一读取或保存失败都保留恢复入口。

末日丧尸模式入场欠账冻结 key（2026-09-12，`SCHEMA+`，修 `CR-2026-09-11-019`）：

- `BossRush_ZombieMode_RefundDebt_Cash` — `long`，入场回滚时退不出去的现金总额。
- `BossRush_ZombieMode_RefundDebt_Invitations` — `int`，退不出去的尸潮邀请函张数。

两个 key 由 `ZombieMode/ZombieModeEntryDebt.cs` 独占读写，**老档缺键即为 0（不欠）**，
因此是纯扩展、不需要迁移。之所以必须落存档：入场回滚发生在切图途中，官方
`EconomyManager.Instance` 已随场景销毁（`Add` 返回 false）、`ItemAssetsCollection.InstantiateSync`
可能因资源未就绪返回 null，只靠当前对象重试会随对象一起消失。
纪律：写入后回读核对；结账**先到账再销账**（销账失败只会重发，绝不吞玩家的钱物）；
邀请函逐张推进，中途资源或接收方掉线时剩余张数留在账上。实例化成功不等于送达：先确认角色或完整仓库就绪，
再核对角色物品树、仓库、有效拾取代理或官方 Buffer 内同实例 ID 的树回执。投递前失败保留欠账，
投递后通知异常已有回执则不重发。结账点是官方 `EconomyManager.OnEconomyManagerLoaded`、
`LevelManager.OnAfterLevelInitialized`（命名方法、幂等订阅、随模块销毁成对退订）与每次入场扣款之前。
由 `tests/ZombieModeEntryDebtGuard.py` 与执行回归 `tests/fixtures/ZombieModeEntryDebt` 守卫。

Breaking:

- 改名旧 key 且无迁移。
- 多 key 存储改成单 key 但不兼容读取旧格式。
- 清空玩家成就、好感度、婚姻、寄存、配置、Wiki 状态等持久数据。

## 3.1 战役 → 后山 跨系统解锁契约（M0 起）

两个子系统之间唯一的耦合面，实现在 `Campaign/CampaignFacilityUnlocks.cs`。
选静态 API 而非「后山去读战役的存档 key」：键名改一次两边会静默失联，
方法签名改一次编译期就报错。

冻结面：

- token 字面值 `BossRush_Campaign_Unlock_Ch1` … `_Ch6`
  （前缀常量 `CampaignTuning.FacilityTokenPrefix`，发布后不得改名）。
- 语义：战役只发「第 N 章通行 token」；token → 具体设施的映射由后山自持
  （`BackMountainConfig.GetRequiredChapter`），两侧解耦，调整映射不动战役侧。
- `IsTokenGranted` / `GetGrantedTokens`：权威查询，**未装载存档时 fail-closed**。
- `OnFacilityTokenGranted`：**只在本会话真正新授予时触发**；读档回放按契约不发事件。
  因此消费方必须在自身 init 与每次场景加载时做全量查询，不得只依赖事件，
  也不得缓存查询结果——否则玩家上次通关解锁的设施在重进游戏后会消失。
- 换槽：`LoadGrantedTokens` 整体替换而非追加；`ResetForSlotReload` 负责复位，
  防止 A 档解锁泄漏到 B 档。

Breaking:

- 改 token 字面值或前缀。
- 让读档装载改为触发 `OnFacilityTokenGranted`（会导致每次读档重播解锁提示）。
- 把查询侧改成缓存或默认已解锁（fail-open）。

## 3.2 鸭王征程章节数据契约

正式内容文件为 `Assets/Data/Campaign/Chapters.json`，构建脚本必须部署到
`BossRush/Assets/Data/Campaign/Chapters.json`。运行时整表校验 version、六章数量与顺序、
模式、目标阈值、奖励、唯一 token/线索及终章位置；任一字段不合约即整表回退硬编码。
解析必须使用 `ModeHCanonicalDigest` 的严格 token parser；实机已证实 Unity `JsonUtility`
对该两层对象数组会只填 version、静默把 chapters 留空，禁止再用它解析本表。
运行时公开 `CampaignContentCatalog.Source`（`Json` / `Fallback`）与
`ContentSignature`。硬编码表是灾备而非正式发布来源：F3 完整验收只接受
`Source=Json`、六章及与冻结 fallback 相同的内容签名。

冻结 ID（2026-09-22 换故事《册子上的名字》时确认）：`chapterId` 固定 `ch1`–`ch6`、`clueId` 固定 `clue_ch1`–`clue_ch6`。
它们分别进了存档 `chapters[].chapterId` / `unlockedClues[]` 与官方笔记键 `Note_BossRushCampaign_clue_chN_*`（官方存档），
改名 = 老档进度静默归零 + 官方图鉴留孤儿条目（BREAKING）。换故事只换标题、目标类型 / 阈值 / 文案、线索正文与对话台词；
章节 ↔ 官方 Quest ID 映射为 `590100 + order`（`CampaignQuestTable`）。目标类型 `garden_built` / `trophy_displayed` 是**基地侧**目标：
不进局内追踪器、不落盘，事实由 `Integration/BackMountain` 经 `CampaignBaseObjectives` 注册提供者给出；`ReadyToDeliver` 仍只由局内目标同局达成触发，交付另核对基地侧目标全真。

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
- 头盔 EquipmentModel 根节点为单位变换；官方挂载清零根节点位置/旋转而保留缩放，校准放网格子节点。源网格轴向、尺寸/中心及子节点姿态以 `tools/helmet_fit_profiles.json` 为准，经 `tools/helmet_fit.py --sync` 同步作者工程。生成时只绝对赋值一次，不能同时烤入 FBX。新增头盔或换源网格必须重校准；全量构建与头盔构建都先验证，包内清单/姿态须回读。盔壳定位不等于整体包围盒居中，实机捏脸与动作另行验收。
- 已发布自定义物品/装备 TypeID 必须登记到 `Integration/BossRushDynamicItemRegistry.cs`。官方存档、仓库、商店和 UI 可能早于延迟 bootstrap 调用 `ItemAssetsCollection.GetMetaData/GetPrefab/Instantiate*`，统一注册表是避免白底问号和 `FallbackItem_<id>` 的按需兜底入口。

Breaking:

- 改 AssetBundle 文件名或 Prefab base name 但不更新工厂配置。
- 把已有资源移动到新目录导致工厂扫描不到。
- 删除已有模型、图标、Buff、Projectile 依赖。
- 新增或迁移已发布 TypeID 后未同步 `BossRushDynamicItemRegistry`，导致重启后存档物品按官方 fallback 还原。

## 6.1 Mode H 外部契约（百战留痕：黑市鸭王杯，已实现）

Mode H 的正式入口、五席试棚、三幕六战、虚拟整备与下注、口令/伤病/战痕、ERROR 互换、
真实仓库 escrow/journal/清算、转会、名人堂、恢复与存档已接线。恢复按同场看盘重开，
不支持快照位置上的局中续战。代码接线、隔离回归与编译不等于所有实机玩法已验收。

**正式入场与开发认证（2026-09-23，COMPAT / SCHEMA+）。** 正式入口在地图与两种租约就绪后
同步检查发布目录，直接进入选人页；正式版与 Dev 版的新局和续赛均采用相同入口。
逐 key 生成、伤害与口令动态认证仅由 F3 专项按钮在专用测试档、选人页显式启动；
完成、失败或取消后恢复原选人页与发布目录，不重抽候选、不写赛季或认证缓存。
发布目录仍核对实际 preset 静态资格、官方控制点及既有候选/原型/口令数量门槛。
`ModeHCommandCompatibilityStatus` 末尾追加 `ReleaseSupported=6`：表示发布能力支持，不是本局实测；
旧 0–5 值及 DTO 字段不变，旧摘要不变，未知的 7 及以上继续拒绝。
复用既有报告载体，静态记录标 `spawnTimelineDigest=release_contract_v1`、`durationMs=0`，
不写动态认证缓存。该状态可用于口令、伤病/战痕与赔率，动态实测继续区分 VerifiedBehavior / ActionApplied。
擂台 AI 复用本场存活对手扫描，仅给缺失/已死亡目标补 `searchedEnemy` 与警觉；
保留有效目标，排除中立看台、inactive 选手与 ERROR 当前受控者。观战拍铃门在每场成功入场时重开，
结算/中止关闭；HUD 与点击使用同一租约门。

**擂台与敌军伤势（2026-09-18，COMPAT / WIRE+）。** owner 授权补全效果并允许玩法与数值调整。
`ModeHMatchRules` 只管理实际入场的临时参赛者，所有规则双边生效；属性复用官方 Stat Modifier
及共享 tracker，医疗限制通过实例 Health.OnHealthChange 递归门实现并对称退订，不新增 Harmony。
场地区域使用现有贴地圈，判定与画圈使用同一个半径；缺可见圈拒绝危险场。
`woundedUnits` 按最高威胁优先分配到计划槽位，敌军以 75% 生命入场。伤势侦察与赔率共用该
计划 helper；旧伤势数量被实际数量封顶，未实现的敌方胆怯/ERROR 字段不计分。
位置与入场时机的优势不额外换算成固定原型地形分。旧 ID、schema 与存档键不变。
本节替换 2026-08-26 的旧稿：**不再存在“先做 H0 技术样机”的阶段划分**，
也不再存在旧稿列出的真实资产开关与四门口径。

**配置面（COMPAT，2026-09-07 按当前代码校正）。** `ModBehaviour.BossRushConfig` 保留字段 `modeHEnabled=true`，
运行时只通过 `ModBehaviour.IsModeHConfiguredEnabled()` 读取。
该系统已纳入 `ConfigContentSystemSwitches.ForceContentSystemSwitchesOn` 的内容恒开策略：
总开关不再注册到 ModConfig UI，不读取旧镜像 `BossRush_ModeHEnabled`；读本地配置后把历史 false 归为 true。
字段和只读入口保留供兼容，未新增或改名配置键。本节旧“缺省关闭”描述已失效，见 `docs/ai-docs-migration.md`。

**没有真实资产开关。** `modeHRealWarehouseStakeEnabled`、
`IsModeHRealWarehouseStakeConfiguredEnabled` 与 `ModeHStakeJournal.GatePassed`
三个符号一律不得出现，`ModeHConfigApiGuard.py` 与 `ModeHStakeJournalGuard.py` 显式断言这一点。
同意通过“进入模式”表达：入口页、模式说明与 `ModeHInteractable` 三处都固定显示
`BossRush_ModeH_RealStakeRiskNotice` 风险行。系统唯一会自行禁用真实押品的情况是
只读派生结果 `ModeHWarehouseStakeJournal.IsSlotConsistent` 为假，它不是可写开关。

**运行时门。** `ModeHRuntimeGates` 提供**五个**互不混用的 no-throw 只读结果：

- `IsModeHRunOwnerActive`：当前唯一 Mode H runtime 是否持有 owner。
- `IsModeHRiskScanReady`：当前槽的轻量持久风险头是否读取完成。
- `IsModeHContentReady`：配置、资源、地图、候选池与口令兼容矩阵是否具备发布目录检查的条件。
- `IsModeHExternalAssetRiskBlocked`：是否存在未终结真实资产 journal/operation 或风险未知。
- `IsModeHRecoveryOnlyBlocked`：是否存在 Season 恢复壳、late cleanup 或 slot barrier。

旧模式最终入口**只**读取 `IsModeHRiskScanReady` 与 `IsModeHExternalAssetRiskBlocked`，
绝不等待 H 内容或恢复壳状态；recovery-only 阻断不得并入普通 NPC 或场景分类 predicate。
三个 Mode H key 均不存在时，轻量扫描同步得到 ready 且 unblocked。
`OnSetFile` 后按新 `slotGeneration` 重新读取风险头，I/O 异常时最终模式入口 fail-closed 并提供重试。

**赛季与旧仓库押品存档键（SCHEMA+）。** 三个 typed key 全部已实现：

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

**押钱 / 押背包物品账本（2026-09-25，COMPAT / SCHEMA+）。** 独立冻结 key
`BossRush_ModeHCashBet_v1`，用 `Save<string>` 保存 JSON，复用 `BossRushSlotJsonStore` 与
`BossRushSaveCoordinatorEngine`。当前 `schemaVersion=2`，兼容 v1；更高版本与损坏数据仍进写屏障。
v1 新字段缺省为未准备结算、无待交付项、缺失估值 0，下一次正常写入才升级；不迁移或删除旧 key。

- 押品编码追加第六列身份，旧五列可读；每次锁盘给实际押品的 Variables 写
  `BossRush_ModeHBetIdentity`，只按 TypeID + 唯一身份恢复。身份缺失或重复时不认领同型号替代品，
  沿用原有缺失估值补偿。主角物品树经官方 `Save("MainCharacterItemData")` 采集，随账本同批保存；不依赖基地仓库。
- 追加 `itemSettlement`（0 未准备 / 1 赢 / 2 输）、`pendingItems`、`missingValue`。
  先接受固定输赢与奖品计划，再保存实物与剩余义务，全部实物完成后才结清现金与统计。
  失败保持 Reserved；恢复、放弃或开新季都不能退款或覆盖已经准备的实物结算。
- 奖品仅通过官方 `SendToPlayerCharacterInventory(prize, true)` 不合并地入包；满包时保留欠账，
  不把无法随主角快照保存的落地物当成持久交付。身份回执防止已入包奖品重发；缺失估值与收走后的树同存，
  重启不得再次扣同一笔。宿主驱动共享保存重试，待交付最多每秒尝试一次，无待交付时只做常量时间检查。
- 金额变更仍与 `EconomyData` 快照同批保存；零金额计划不能清掉之前尚未完成的现金快照义务。
  实物或钱包变更期间关闭采集门，避免通知重入保存半完成的账本。

结构守卫 `ModeHCashBetGuard` / `ModeHIsolationGuard`，故障恢复执行回归 `SaveFailureRecovery`。

**战痕选择凭据（2026-09-06，COMPAT）。** 复用现有 `appliedEventTokenIds` 保存
`scar_offered|<operationId>|<scarId>` 与 `scar_resolved|<operationId>|<scarId>`，不新增字段、
key 或 schemaVersion，不改变 canonical digest 算法。归属由持久 report / operation 恢复；
接受、拒绝或替换与 profile / 名声变化在同一 Season 持久屏障中完成，重复或过期动作不再次发奖。
候选可跨幕间保留，终局前处理完成。旧版没有凭据且未持有该候选的记录无法区分「未选择」与
「已拒绝领取名声」：保留记录并提示，不猜测补发、不清除旧记录、不阻断继续赛季。

**技术重试押品边界（2026-09-06，COMPAT）。** 旧场真实押品必须完成 journal 退款屏障，
才能还原虚拟预约、清理旧锁盘并重新选择；失败保留快照与恢复入口。零件选择同样检查历史
journal；已提交结果不得转成退款重打，已退款的旧场不得标记为新场押品。

**玩家资产边界。** 白名单按 `ModeHIsolationGuard` 的实际职责限制：
`ModeHEntry.TryRefundPrepaidTicket()`（唯一退款实现点）、
`ModeHLoadoutKitApplicator`（只访问 owner 标记且 inactive 的临时选手实例）、
`ModeHWarehouseStakeJournal`（唯一真实仓库写入者，经 `ModeHInventoryPersistenceBridge` 落地），
以及 `ModeHItemBetStake`（背包押品身份、收走、奖品交付与主角物品快照，不碰仓库）。
旧仓库物品树的规范化与恢复仍归 `ModeHItemTreeNormalizer` / `ModeHItemTreeRestoration`。
其余 Mode H 文件不得出现
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

遗种巢按 `docs/design/2026-08-28_养崽系统创意脑暴.md` 与其附录 A（spec 定稿）实现：
遗种蛋 + 遗魂双轨获取、全 Boss 谱系幼体化、孵化 roll 与命名、单席随从进局、
重伤退场与战痕、天灾远征与真死、博物馆图鉴与纪念碑、驯养成就。
**数值与远征产出已实装（2026-09-18，COMPAT）。** 远征战利品池接入 `Common/Loot/BossRushQualityItemPool`（官方全物品表按品质随机 + 黑名单过滤，与日报签到奖品同源共享缓存）；随从数值成长与性格表集中在 `PetNest/PetNestTuning.cs` 与 `PetNest/PetNestPersonality.cs`；步骤 0 的实机闸门五项待 owner 实机验证。

**配置面（COMPAT）。** `ModBehaviour.BossRushConfig` 只新增**一个**字段
`petNestEnabled=true`，运行时只通过 `ModBehaviour.IsPetNestConfiguredEnabled()` 读取
（定义在 `Config/ConfigPetNest.cs`，与 `Config/Config.cs` 同一 partial class，
拆开只为 1200 行预算）。**owner 2026-08-30 定：遗种巢属于默认内容，总开关不再
暴露给玩家**——`RegisterPetNestModConfigOption` 不再接线，`BossRush_PetNestEnabled`
也不再从 ModConfig 读取，且由 `ForceContentSystemSwitchesOn()`
（`Config/ConfigContentSystemSwitches.cs`）在读档后强制拉回 true，
抹掉老版本可能存下的 false。关闭时整个子系统 dormant：不订阅存档、不建血脉目录、
不 tick 协调器、不产蛋、不生成任何角色。开关运行时可变，
bootstrap 幂等且可退回 dormant。

**TypeID（SCHEMA+）。** 只占 `500059` 遗种蛋一个号。**通用蛋 + KV 记血脉**：
全 Boss 谱系共用这一个 TypeID，血脉写在 `Item.Variables` 的 `PetNest_Lineage` 上，
随 `ItemTreeData` 持久化；展示名走 `Var_PetNest_Lineage`。
`MaxStackCount = 1` 是硬要求——堆叠合并会把两枚不同血脉的蛋并成一枚，血脉信息丢失。
遗魂与遗物不占号（遗魂是纯账本，不掉实体）。

**存档键（SCHEMA+）。** `BossRush_PetNest_Bundle_v2` 是唯一权威状态，使用
`SavesSystem.Save<string>` JSON 整存，顶层固定包含 `schemaVersion=2`、`generation`、
`nest`、`expedition`、`museum`。所有运行时写操作均以深拷贝候选包提交，三个分区不会再
独立写盘。

2026-09-11 `SCHEMA+`：远征记录增加可选 `petLineageKey`（原 `schemaVersion=2` 不变）。
新远征在出发时冻结原崽血脉；旧记录缺字段读为 null，Bundle 规范化仅从相同 `petId` 的
原崽补齐，并随下一份候选包保存。已有非空快照不覆盖，不能借用其他崽、显示名或名册顺序猜测。
待发遗种蛋使用这份快照，放生或后续远征阵亡不改变奖励身份；血脉仍缺失或盖章失败时
保留欠账与原游标，禁止当作已投递。已被旧代码错误标记为发完且移除的奖励无法自动重建。

以下三个 v1 key 永久保留为**只读迁移材料**，不得删除或复用：

- `BossRush_PetNest_Nest_v1`（崽列表 / 出战席位 / 遗魂账本 / 巢容量）
- `BossRush_PetNest_Expedition_v1`（进行中远征 + 已结算未翻牌）
- `BossRush_PetNest_Museum_v1`（图鉴统计 / 纪念碑 / 异色收集）

没有 v2 时才读取三份 v1 envelope（`{schemaVersion, payload}`），合并成候选 Bundle；
v2 写入并回读成功后才完成迁移。v1 原键不删除，但之后不再分拆写入。v2 已存在却损坏、
不可读或版本过新时 fail-closed 建立整包写屏障，**不得回退 v1**，以免用过期快照覆盖新状态。
**不用 typed `Save<T>`**：ES3 会把 assembly-qualified 类型名写进存档，mod 程序集改名或
类型重构就会让老档读不回来。写入路径异常进入单向 `StoreFaulted`。
三个官方存档事件 `OnCollectSaveData` / `OnSetFile` / `OnSaveDeleted` 幂等订阅与退订。
`PetNest/PetNestSaveCoordinator.cs` 是遗种巢**唯一**的物理落盘入口（2026-09-06 起它持有一个
共享引擎 `Common/Lifecycle/BossRushSaveCoordinatorEngine.cs` 实例，`SavesSystem.SaveFile` 本身只在引擎里；
PetNest/ 目录零处直调），每批至多一次；`IsSaving` 时改走 deferred，重试有预算上限，超预算保留 pending 并报错。
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

远征奖励属于持久化债务：`cashGranted` 与 `grantedLootUnits` 是续发游标，
`rewardsGranted` 仅在现金和全部物品单位送达后置位。基地 `LevelManager`、经济和物品资源
未就绪时不尝试；失败按固定退避无限保留，不再因达到历史尝试次数而吞奖。官方经济/背包 API
与 Mod 存档无法原子提交，采用至少一次语义：极端崩溃窗口下宁可重复，不允许静默少发。

Breaking：

- 复用或回收 `500059`。
- 改名 `BossRush_PetNest_Bundle_v2` 或三个 v1 迁移源、删除 v1 原键、让 v2 损坏时回退 v1。
- 把 `AchievementCategory.Taming` 插到枚举中间。
- 改动 `Health.Hurt` Prefix 的既有签名。
- 把捡漏背包从"借席不夺席"改成夺席不还。

## 6.3 鸭科夫日报外部契约（DailyReport 日报系统，已实现）

日报按 `docs/design/P2-日报系统.md` 实现：自算游戏日出刊、昨日战绩新闻化、
每日悬赏、签到梯度奖励、明日天气预报与趣味杂谈，投递载体是玩家自建的报箱建筑。

**配置面（COMPAT）。** `ModBehaviour.BossRushConfig` 只新增**一个**字段
`dailyReportEnabled=true`，运行时只通过 `ModBehaviour.IsDailyReportConfiguredEnabled()` 读取
（定义在 `Config/ConfigDailyReport.cs`，与 `Config/Config.cs` 同一 partial class）。
字段与 `BossRush_DailyReportEnabled` 镜像键为兼容保留；当前 ModConfig 注册流程不再调用日报总开关注册。
默认开启的理由：报箱要玩家花 500 金自建，已是天然门槛，不必再用开关拦一道。

普通签到每天赠送一件品质 2 奖品，复用快递欠奖队列。v1 新增可选 `lastDailyRewardDayIndex`（默认 0）及债务字段 `m{n}_isDaily`（默认 false），不改变既有 key/schemaVersion；日常债务不占里程碑掩码。升级当日已签到但未领普通礼可补一次，不追溯没有记录的历史普通日。

**存档面（SCHEMA+）。** 新增**一个**槽级 key `BossRush_DailyReport_v1`，
`SavesSystem.Save<string>` 整存扁平 JSON，顶层带 `schemaVersion`。
不用 typed `Save<T>`：ES3 会把 assembly-qualified 类型名写进存档，
mod 程序集改名/重构就会让老档读不回来。
未知或更高 `schemaVersion`、payload 不可读时进写屏障，**只读不写，绝不覆盖该 key**。
`Integration/DailyReport/DailyReportSaveCoordinator.cs` 是日报**唯一**的物理落盘入口
（2026-09-06 起它持有一个共享引擎 `Common/Lifecycle/BossRushSaveCoordinatorEngine.cs` 实例，
`SavesSystem.SaveFile` 本身只在引擎里），且每批至多一次；`IsSaving` 时只登记 deferred 由宿主 tick 重试。
单 key 整存门面的状态机同样共享：`Common/Lifecycle/BossRushSlotJsonStore.cs`（征程 / 图鉴 / 日报三者共用）。

所有状态变化必须先修改 `DailyReportData.Clone()` 候选副本，只有 `Store` 接受后才替换
权威内存状态；签到、跨日、里程碑、悬赏种子、未读提示和补发路径都遵守同一规则。
跨日计时以 **Store 接受**为推进边界：接受后立即消费对应的 `_carrySeconds`，再请求物理保存；
写盘失败由协调器重试同一份状态，不能让已消费的一天再次推进。Store 拒绝则保留计时并退避重试。
跨日悬赏先把完成结果作为“待发债务”随 rollover 提交，再触碰官方经济，最后以第二次候选提交标记领取。
面向玩家的领取入口仍保留物理保存失败反馈，不因为跨日计时修复而把硬写失败显示为成功。

**DTO 扁平化是契约的一部分。** 里程碑领取用位掩码 `periodClaimedMask` 而不是 token 列表，
往期数据用定长编码而不是嵌套对象；这是发布后冻结的存档字段面，不因解析器升级而改。
写出仍走 `Utilities/SimpleJsonHelper.cs` 的 `Append*` 系列（字节格式不变）；读取自 2026-09-06 起
改走全 Mod 共享的节点解析器 `Common/Data/BossRushJsonValue.cs`（原 `ModeH/ModeHJsonValue.cs`，
遗种巢的 `PetNestJson` 已并入），不再依赖「key 互不为带引号前缀」「envelope 只能有一个数组」
这类提取器约束。仓库只保留这一套嵌套 JSON 解析器。

**里程碑欠奖（2026-09-06，SCHEMA+）。** `schemaVersion=1` 与旧字段保留；
追加可选 `pendingMilestoneCount`，以及每笔 `m{n}_periodIndex` / `m{n}_slot` /
`m{n}_signDayIndex` / `m{n}_quality` / `m{n}_seed` 扁平字段，`n` 从 0 开始。
它们冻结已经赚取但未送达的奖励身份，独立于当前签到墙；断签和翻期只重置原进度，
不能清除欠账。旧档缺少追加字段时，先从尚存的已签格位与未领掩码补建，再允许重置；
历史版本已经丢失的信息不猜测回填。计数、身份或种子损坏时整体拒绝解码，不能静默删债或重抽。
实际发物前必须确认存储层未故障且无写屏障；发后才删除对应欠账并标记匹配的当前格位。
一次送达后恰逢写失败仍保留至少一次恢复语义，已知永久故障之后则不得反复送出同一奖励。

**悬赏欠款合并（2026-09-18，SCHEMA+）。** 保留现有 key、schemaVersion=1 与 Bounty* 字段；追加可选 `bountyCashReward`（最新结算冻结的奖金）和 `pendingBountyCash`（更早各期尚欠现金合计），旧档默认 0。旧未领结果未冻结金额时，按既有种类与目标还原原档奖金，不重抽题。跨天先把旧未领金额归入合计，再照常结算当天；发钱仍经同一现金快照义务，成功提交后清除合计并标记最新已完成结果。损坏的已声明金额拒收整份 payload；不可还原的旧债务阻止该次结算，不能清统计吞奖。合计使存储与补发成本不随欠款天数增长。曾被旧版本跳过的历史奖励无事实可还原，不猜测回填。

悬赏 `no_death` 要求当日成功撤离至少一次且截至结算零死亡；跨天出击记入实际撤离日。`earn_money` 保持进账口径，不扣支出，日报奖金不自我计入。Mode H 整个采集器排除；击杀/输出只计玩家对敌方角色，承伤包含环境来源。

签到随机奖励用官方 `GetAllTypeIds` 精确过滤品质，再过既有黑名单，候选排序后使用原随机流。空池保留欠奖而不降级；实例化前复核 prefab，避免把官方同 TypeID 空壳当成奖品。奖品池内容与品质梯度不变。日报、Mode H 同品质奖励与天灾远征共用 `BossRushQualityItemPool` 的候选数组和缓存，缓存清理由集成层统一负责；各调用方不另做全表扫描。

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
（经 `LootBlacklistRegistry` 过滤），不新造物品；报箱优先加载专属 bundle，
缺席时借用已加载的许愿台模型，否则使用几何报箱模型（石砌基座 + 箱体 +
半圆柱顶 + 小红旗 + 报纸卷，`CreatePrimitive` 自带的 Collider 必须删）。
报箱 PNG 已有本地制品；专属模型只需在 `Assets/buildings/` 放同名 bundle，代码零改动。

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

官方更新后按 `docs/architecture/Harmony补丁契约稳定性.md` 复查。

补丁启动扫描只允许把类级或方法级带 `[HarmonyPatch]` 元数据的类型交给 class processor；
普通业务方法名为 `Cleanup` 不得被 Harmony 当作 cleanup 回调。动态角色的
`MagicBlendState.OnStateEnter` 可能早于 `MagicBlending.Start`，兼容补丁只推迟未初始化的首个回调，
已初始化角色必须完整走官方方法。

套装元素吸收另有兼容观察补丁 `SetBonusDamageObservation`（2026-09-06，`COMPAT` / `WIRE+`），
目标仍为官方 `Health.Hurt`。安装必须唯一匹配元素抗性调用、连乘、最低 1 点、累加与
`finalDamage` 的数据流，只插入观察调用，不改写伤害。缺失或失配明确诊断并跳过元素治疗；
官方更新必须复验实际程序集 IL，不能回退为减免前因子占比估算。上下文只在主玩家启用相应
套装时采集，按一次 Hurt 调用持有并由 Finalizer 清理，嵌套调用互不污染。

收获提示 `GardenHarvestNoticePatch`（COMPAT / WIRE+）只匹配 `Crop.Harvest()` 内唯一的
`Cost.Return(bool, bool, int, List<Item>) → UniTaskExtensions.Forget(UniTask)`，保留原交付与 Forget，
在两者之间包装等待任务。发货正常完成后才提示名称、数量及仓库/马蜂自提点去向；失败继续由原
Forget 观察。失配保留全部原 IL 并警告；切图、换槽、换主角、停用或卸载后不迟发通知。

## 7.1 官方游戏行为：静默失败类陷阱

- 非激活克隆物品不会执行 Awake；官方 ItemAssetsCollection 的同步/异步 Instantiate 不保证 Initialize。动态注册补丁在同步、异步本地与 fallback 返回前幂等补初始化，确保 AgentUtilities.Master 指向实际实例，否则使用与丢弃都会失败。

下面每条都能在反编译源（`鸭科夫源码/`）里核实，而编译和 guard 都查不出来。共同点是**不报错**，表现只是「功能不工作」。
写到相关 API 时先对照这里；发现新的同类行为就追加一条，并写明核实位置。

**生成与激活**

- `CreateCharacterAsync` 传 `relatedScene != -1` 且 preset 的 `setActiveByPlayerDistance` 为 true 时，角色进官方
  `SetActiveByPlayerDistance`：玩家跑出约 100 m 就被 `SetActive(false)`，`IsDead` 仍为 false，波次永不结算、NPC 服务凭空消失。
  Mod 刷怪统一经 `Utilities/SpawnedEnemyActivationHelper` 解除；NPC 用 `SetRelatedScene(sceneIndex, false)`。
- 官方 preset 的队伍可能是中立，生成后必须走敌对性安全网（`AGENTS.md` §4.5）。
- `LootBoxLoader.Awake` 按位置哈希随机 `SetActive(false)` 箱子。复用官方 `InteractableLootbox` 预制体时：
  在**未激活**的暂存父节点下 Instantiate；用 `DestroyImmediate` 摘掉 `LootBoxLoader`（`Destroy` 帧末才生效，挂到活动父节点会本帧触发 `Awake`）；
  激活前摆好世界坐标；再调 `InteractableLootboxInventoryHelper.EnsureLocalInventory`（官方按位置哈希共享 Inventory，靠近的箱子会串味）。
- 官方 `OnDead` 在倒下位置 +0.1 m 生成尸体箱，而 `CA_Interact` 按到交互体轴心的距离**严格小于**取唯一目标：同点的第二个交互体整局选不中。
- 改敌人体型只缩放 `characterModel`，不改角色 transform：`CreateCharacterAsync` 返回时角色已初始化，事后缩放会让碰撞体、导航半径与官方口径失步。染色走 `MaterialPropertyBlock`，碰 `sharedMaterial` 会污染同款的所有敌人。
- `AICharacterController.noticed` **不是「看见玩家」**：它在「听见一处声音」（`OnHeardSound`，距离 < `sound.radius * hearingAbility`）或「挨了打」（`OnHurt`）时置 true，**而且全程不复位**。
  队友在几十米外开一枪、两伙敌人自己打起来，都会把它点亮。拿它当「发现玩家」用，表现就是敌人对着空气喊话、Boss 在玩家露面前念出场白——不报错、不掉帧。
  判断当前目标优先读 `searchedEnemy` 并与主玩家 `mainDamageReceiver` 比；视觉搜索和强制追踪可直接写该字段，不一定先有声音。无当前目标时才将 `NoticeFromCharacter` 与主玩家比，并同时要求 `isNoticing(timeThreshold)`，否则旧听声来源会误报；目标已转向别人时不能被旧来源覆盖。只要「最近有动静」用 `isNoticing(timeThreshold)`。
  核实位置：`鸭科夫源码/TeamSoda.Duckov.Core/AICharacterController.cs` 的 `noticed`、`NoticeFromCharacter`、`isNoticing` 与 `Update` 的 `searchedEnemy` 追踪分支。
- 该组件挂在角色的**子物体**上，根节点 `GetComponent<AICharacterController>()` 恒为 null。

**物品与属性**

- `ItemAssetsCollection.Search` 结果为空时会自行下调品质区间反复重搜。要精确品质带用 `GetAllTypeIds`；它经过 HashSet、顺序不稳定，固定 seed 抽样前先 `Sort()`。
- `InstantiateSync` 缺资源时返回空壳 `FallbackItem`（非 null、不抛），还会把同一个 TypeID 写回空壳，回读 `TypeID` 分辨不出。实例化**之前**先问 `ItemAssetsCollection.GetPrefab(typeId)`。
- 角色没有 `MoveSpeed`、`ReloadSpeedMultiplier` 这类 stat：移动只读 `WalkSpeed` / `RunSpeed` / `Moveability`，`MoveSpeed` 是 Animator 参数名。
  挂到不存在的 stat 会被 `RuntimeStatModifierTracker` 静默丢弃（`StatKeyExistenceGuard`）。
- 不设 `item.Value` 时 NPC 商店标价 0（价格 = Value × 耐久比 × priceFactor）；有耐久的装备要 `EquipmentHelper.AddRepairableTag`，维修入列门是 `ItemRepairView.CanRepair`。
- `EquipmentHelper.AddModifierToItem` 不幂等，配置器又可能被重复调用；要「保证存在一条」时用 `EnsureModifierOnItem`。
- 每次进图官方都重建主角与 `CharacterItem`（`LevelManager.LoadOrCreateCharacterItemInstance`），挂在旧 Item 上的运行时 Modifier 与旧角色上的特效随之作废。
  装备类「激活态」要在 `LevelManager.OnAfterLevelInitialized` 先停用再重查。
- `InteractableBase.interactTime` 是私有序列化字段，`AddComponent` 出来恒为 0（首帧就完成）。要读条时经 `ModeFItemConfigHelper.SetHiddenMember` 写入，**读回 `InteractTime` 核对**，不一致打警告。

- 官方投掷物按物品身份取 `ItemAssetsCollection.GetPrefab(typeId)` → `ItemSetting_Skill.Skill` → `Skill_Grenade.grenadePfb`，不按 `fire` 名称子串或 `fxType` 猜燃烧弹（`Firework` 烟花同样命中）。龙裔使用官方 #941 / `Item_FireGrenade`；缺失时走明确后备，不能随机挑手雷。
  `Skill_Grenade.OnRelease` 还会写 `createExplosion`、`explosionShakeStrength`、`SkillContext.effectRange`、`delayFromCollide`、`delay`、`isLandmine`、`landmineTriggerRange`；仅克隆 Grenade 会漏掉技能上的设置。核实位置：`鸭科夫源码/TeamSoda.Duckov.Core/Skill_Grenade.cs`、`Grenade.cs`。

**伤害与掉落**

- 玩家装备 / 奖励自建爆炸显式传 `canHurtSelf: false`（官方默认 true 时 `selfTeam = Teams.all`）；明确带伤己风险的玩法如共享变异「天降殉爆」保留 true。玩家效果 `DamageInfo` 置 `isFromBuffOrEffect = true`、`fromWeaponItemID = 0`，避免被只认直接击杀的系统当成击杀起链；敌方技能保持原来的受击反应语义。
  `OnHurt` 与 `OnDead` 都可能正处在官方 `ExplosionManager` 的循环里，嵌套 `CreateExplosion` 会覆写它共用的 colliders / damagedHealth，追加爆炸须先让出一帧。延迟请求在暂停时等待，换局 / 切图 / 装备 context 失效 / 玩家死亡时作废；丧尸模式复用 RunOnlyObjects，执行完即移除记录。
  原爆炸禁止自伤时，接口失败后的 player-only fallback 也不能伤害施放者。`AffixCombat` 以生产入口与官方缓冲语义替身验证这些边界，实际物理与性能仍待实机。
- 想让**原版地图**击杀也掉落，挂 `CharacterMainControl.OnDead` 前缀（`Patches/Combat/CharacterOnDeadPatch.cs`），只在 Mod 奖励箱协程里加是不够的。
  走 OnDead 必须补齐 defer 协议的四处接线，掉落 roll 在死亡帧定下并随 pending 携带（`ExtraBossDropDeferGuard`）。

**时间、界面与输入**

- `TimeScaleManager` 在暂停菜单（`GameManager.Paused`，它是 `UIPanel` 不是 `View`）与拍照模式时把 `timeScale` 置 0；`CountDownArea` 按 `Time.time` 计时。
  玩法计时走游戏时间；表现层走 unscaled 时，暂停中要停推进（`BossRushUI.IsGamePaused()`）。
- 官方 HUD 显示条件是：无存活隐藏令牌、`View.ActiveView == null`、`!DialogueUI.Active`、`CustomFaceUI.ActiveView == null`、`!CameraMode.Active`，本库判定收在 `BossRushUI.IsOfficialHudHidden()`。
  官方 Views 画在 sortingOrder 100，本库 `HudOverlay` 是 1200，常驻 HUD 不跟随显隐就会压在背包、地图、对话上面。
- 官方 HUD 画布是 2560×1440 按短边缩放，本库是 1920×1080 Expand，1 本库单位恒等于 4/3 官方单位。避让官方 HUD 按预制体实测，不按截图估。
- 输入资产 `Duckov Controls` 只有键鼠方案，没有手柄绑定；`UIInputManager.OnNavigate` 在 started / performed / canceled 各发一次，自绘面板导航按边沿走一步。
- `NoteIndex.SetNoteDynamic` 只写查询字典、不写 `notes` 列表，两边都写界面才看得到；`titleKey` / `contentKey` 是只读派生属性，文案走 `LocalizationHelper.InjectLocalizations`。
- `NoteIndex` 的解锁状态随官方存档写进 `NoteIndexData`（`Save()` 挂在 `OnCollectSaveData` 上，`unlockedNotes` 与 `readNotes` 两份列表都从 `unlockedNotes` 拷）。所以镜像我们自己的图鉴条目**就是在写官方存档键**：卸载 Mod 后只剩带本 Mod 前缀的孤儿 key，官方读档不报错，图鉴「已解锁数」可能虚高。
  2026-09-14 拍板接受为例外（天空岛见闻、征程线索）。我们的存档仍是权威，镜像双向同步：官方点亮而我们存档里没有的，经公开属性 `UnlockedNotes`（返回的就是解锁集合本身）收回并照官方写法调 `onNoteStatusChanged`。
- 切图前要禁输入，就用**当前场景内的临时对象**调 `InputManager.DisableInput`：`blockInputSources` 只在源销毁或失活时解封，挂 DontDestroyOnLoad 会让输入永久锁死。
  `SceneLoader.LoadScene` 同步拒绝时 `LoadFinished` 立刻为 true，等待场景的循环必须看它。
- `DialogueBubblesManager.Show` 在 manager 缺席，或无可复用气泡且 prefab 缺失时，正常完成 UniTask 而不显示。异常也会进入返回任务，`Forget` 没有同步抛错不代表发送成功。
  天空岛只发送非交互、正时长气泡：主线程调用后正常展示必定跨帧；已完成任务只消费一次结果并拒绝记账，挂起的请求用异常观察回调消费一次。该判断依赖官方当前 `Show` / `ShowTask` 合同；官方更新时需复核，计数不是像素可见性证据。
- 岛上判夜 22–6（`SkyIslandNight.StartHour / EndHour`），刻意等于官方 `TimeOfDayController` 运行时的 `nightStart = 22 / morningStart = 6`（官方 Volume 与敌人夜间感知同相；反编译源字段初值 19 / 5 会被 `LevelManagerPrefab` 序列化值覆盖，2026-09-25 F3 实机读出）；仍只经 `SkyIslandLighting.ClockHours()` 读 `GameClock`，不读 `AtNight`。官方改这两个值要跟着改（Dev 只读用例 `SKY_NIGHT_BOUNDARY_OFFICIAL` 实机比对）。
- `SceneLoader.LoadBaseScene` 恒传 `clickToConinue: true`（`<LoadBaseScene>d__47` IL 实查）：基地读完后停在「点击继续」，等 `clicked` 的循环没有超时；进等待前先 `SetActive(true)` 点击接收器 `pointerClickEventRecevier` 并把 `clicked` 复位。
  无人值守的流程要在接收器激活后调 `NotifyPointerClick`，否则玩法代码发起的返基地（Mode F / 丧尸撤离）会一直停在加载屏。卡加载时先看 `SceneLoader.LoadingComment`，官方每个等待点都写了一句（如 `Wait for click...`）。
- `Duckov.Quests` 接天空岛跨局主线（任务表 `SkyIslandOfficialQuestTable`，2026-09-16 授权）与鸭王征程六章（任务表 `CampaignQuestTable`，2026-09-22 owner 授权）；投影核心只有 `Utilities/OfficialQuests/` 一份：

  | Quest | 给予者（`QuestGiverID` 整数） | 接取 / 交付位 |
  | --- | --- | --- |
  | `590001` 云上的坐标 | 官方 Jeff（1），只在基地接、回基地交 | `PreludeAccepted` / `RouteUnlocked` |
  | `590101`–`590106` 鸭王征程 ch1–ch6（`590100 + order`） | 官方 Jeff（1），在基地接、在基地交 | 章节状态 `ContractActive` / `Completed`（权威 `BossRush_Campaign_Progress_v1`，见 §3.2）；交付另核对基地侧目标（菜地建成 / 战利品陈列，`CampaignBaseObjectives`） |
  | `590011` 点亮两端航标 | 苇白 `5901`（缺席时 `Search_B` 委托板） | `BeaconQuestAccepted` / `BeaconQuestDelivered` |
  | `590012` 钟庭之争 | 浮舟 `5902`（缺席时 `Search_A` 渡口工台） | `BellCourtQuestAccepted` / `BellCourtQuestDelivered` |
  | `590013` 归航钟 | 钟守 `5903`（缺席时 `Search_H` 钟庭装置） | `HomecomingQuestAccepted` / `HomecomingQuestDelivered` |

  区间 5900–5949 归 BossRush 的自定义给予者（官方 UI 不显示给予者名，`Quest.Compare` 只做整数减法，`GetAllQuestsByQuestGiverID` 只做相等比较）。
  BossRush 保留任务 ID 段：`590001`–`590099` 天空岛入口、`590011`–`590013` 岛上主线、`590101`–`590106` 鸭王征程六章；下一可用 `590107`。征程六章的奖金由 `CampaignProgressService.TryDeliver` 的补偿式事务发放（官方奖励行只展示 `def.RewardCash`，`PayReward = null`），线索与设施 token 同一事务；官方 UI 没有「放弃任务」入口，`TryAbandonContract` 只留 Dev 演练。
  运行时向官方 `QuestCollection` 注册 prefab，接取、任务日志、目标完成通知与交付按钮均走官方 `QuestManager` / `Quest` / `Task` / `QuestGiverView`；岛上三条只在岛上接、岛上交，返航后仍留在官方任务日志里。
  `BossRush_SkyIsland_Story_v1` 仍是唯一权威；官方 `GenerateSaveData` / `SetupSaveData` 快照会剥离这些 ID 的 active、history、completed、ever-inspected 记录（`Quest.SaveData.questGiverID` 随整条记录一起剥掉，卸载后不留野枚举值），
  加载后从 Mod 事实重建官方投影，保证卸载后 `"Quest"/"Data"` 没有孤儿 ID；`completedQuests` 的残留还会把 `IsQuestAvaliable` 永久钉死，所以四类一个都不能少。
  已接受的副作用：已交付任务的 history 投影缺失时用 `ForceComplete` 重建，会再发一次 `Quest.onQuestCompleted`（`AchievementManager` 查不到 `Quest_59xxxx` 直接返回，`BDSManager` 上报一条匿名遥测），每次加载每条至多一次。
  所有补丁与清理都按条目验证专用模板所有权（ID + 对象名 + 核心专用 `OfficialQuestProjectionTask` 组件）；ID 冲突时那一条 fail closed，不得改动占用相同整数 ID 的其它内容。
  实现落点（2026-09-22）：注册 / 投影 / 四个 Harmony 补丁 / 四类快照过滤 / 给予者扫描全在共享核心 `Utilities/OfficialQuests/`（唯一实例由 `OfficialQuestRuntimeModule` 持有，注册顺序先于天空岛与征程；守卫 `tests/OfficialQuestProjectionGuard.py`）；天空岛桥只是客户端（`IOfficialQuestClient` + `OfficialQuestBinding` 闭包）。
  官方 `QuestGiverView` 在出击图缺席时不挂岛上给予者（fail-closed，自绘面板照常）。岛上按出击刷新的居民委托不接跨局 Quest。

**渲染与程序集**

- 项目色彩空间是 **Linear**（`ProjectSettings` 的 `m_ActiveColorSpace=1`）：uGUI 的半透明在线性光里混合，屏幕上的相对亮度 `Y = Y(前景)·α + Y(背景)·(1-α)`。
  在 sRGB 数值上做 alpha 混合算出来的对比度严重偏高——深色半透明压暗底上的小字，sRGB 口径 5.95:1，线性实际 2.13:1。
  对比度复算一律按线性模型（`tests/SkyIslandUiContrastGuard.py`，模型钉在审核报告的复算值上），观感修复以实机截图取色为准。

- 游戏跑在 URP Deferred：自研世界着色器必须带 `UniversalGBuffer` pass，缺了进得去、走得动，但画面全黑（`docs/architecture/自研着色器与官方渲染管线约定.md`，闸门 `tools/verify_sky_island_bundle_shaders.py`）。
- 对字节数组加载的程序集，`Assembly.Location` 返回空串；在静态字段初始化器里拼它会抛 `TypeInitializationException`，把类型永久毒化。Mod 根目录一律走 `ModBehaviour.GetModPath()`。

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

## 12. Dev F3 完整玩法验收契约（OPERATIONAL）

`DebugAndTools/F3GameplayValidationRunner.cs` 只允许 Dev 构建在基地、专用测试档、无活动模式、
无模态租约且存档空闲时启动。专用档标记与运行中标记均存当前槽；崩溃后运行标记保留，
下次启动先提示中断并执行安全清理。测试会推进该测试档的日报、遗种巢、模式与战役状态，
不得对普通档开放。

独立报告固定写到
`Application.persistentDataPath/BossRushTestReports/BossRushValidation_<runId>.log`；每个用例为
`CASE_ID | PASS/FAIL/SKIP | 耗时 | 场景 | 指标 | 原因`。取消必须输出 `CANCELLED`，清理失败
立即中止。最终空闲不变式包含模式、敌人、随机事件、临时 modifier、弹窗/输入租约、BGM owner
及遗种巢奖励债务全部为零。

“敌人”只指 runtime team 明确敌对 `Teams.player` 的存活角色；友军、设施 NPC 与遗种随从不构成
清场债务。全部已登记随机事件必须分别等待实际副作用完成并输出独立 `RANDOM_EVENT_*` case，不能只把
`TryForceTrigger=true` 当成功。完整验收期间普通消息和大横幅不进入官方通知队列，所有退出路径
必须复位该抑制标记。

清场同时统计带 `BossRush_` / `ModeD_` / `ModeE_` / `ModeF_` / `RndEvt_` / `ZombieMode_`
前缀的模式自有角色，即使对象已 inactive 也不能跨用例残留。调用安全清理后最多逐帧等待 2 秒，
允许 Unity 完成延迟 Destroy；仍不满足空闲不变式时必须输出 hostile/owned 明细并立即中止。

## 12.1 共享刷怪后处理与模式角色生命周期契约

`ModeEFSpawnPostprocess` 是共享刷怪核心的分帧队列，不只服务 Mode E/F；标准模式随机事件 Boss
也会借用。`TickModeEFSpawnPostprocessScheduler()` 必须在 `TickWavesArenaRuntime` 的 early-return
之前推进，不能以 Mode E/F active 状态门控。空队列路径必须保持 O(1) 且无分配。

Mode D 角色进入 `modeDCurrentWaveEnemies` 前必须确认 runtime team 对 `Teams.player` 敌对；
仅设置 AI target 不构成敌对性证明。无法修正的角色必须拒绝登记并销毁。模式结束、重复结束与
异常退出都必须注销恢复监控、禁掉落、销毁已登记实体，再清登记表。

Mode E/F 的克隆 `CharacterRandomPreset` 归角色对象级 lease 所有，禁止在角色本体销毁前释放；
Health、血条与 OnDestroy 链仍可能读取 `characterPreset`。模式结束按禁掉落、注销运行时、停用、
销毁角色的顺序执行，不通过 `Health.Hurt` 伪造正常死亡。

动态 Mode E `StockShop` 必须在 inactive 对象上创建，先写已存在的官方 bootstrap merchant ID，
激活触发 Awake 后在同帧 Start 前恢复稳定 `ModeE_*` ID，再覆盖分类库存。身份反射回读失败时
整个商人构建 fail-closed，不得退回默认 `Albert` 或现金商店路径。

## 13. Boss BGM owner 租约契约

Boss 音频调用使用 `AcquireBossBgm(bossKey, owner)` / `ReleaseBossBgm(bossKey, owner)`。
同 key 多 owner 只在最后一个释放后停止；不同 key 按最近仍存活的 owner 抢占并在其释放后恢复。
切场景统一清空租约。幽灵女巫普通局保留标准死亡表现；鸭王征程终章只允许 Campaign 发一次
终章文案、胜利与 stinger，公共死亡清理始终执行且重复回调幂等。
