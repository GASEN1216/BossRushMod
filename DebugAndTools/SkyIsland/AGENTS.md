# DebugAndTools/SkyIsland/AGENTS.md — 天空岛（晴岚群岛）专项规则

> 先读根目录 `AGENTS.md`。本文件只记天空岛独有的约束与踩过的坑。
> 每轮的数字、包体大小、验证记录写在 `FIX_TRACKER.md`；设计稿、待拍板与人工验证清单在 `docs/reports/sky-island/天空岛_*.md`、`docs/guides/sky-island/`（local-only）。
> 官方游戏 API 的静默失败类陷阱（刷怪距离休眠、搜刮箱随机关闭、品质静默降级等）全仓共用，收在 `docs/contracts.md` §7.1。

## 1. 范围

天空岛是从基地船点进入的**独立出击地图**：经官方 `SceneLoader` 切图，基地场景卸载，普通构建即可进入。代码从原型期起放在 `DebugAndTools/` 下，但它是正式内容，不要按目录名当调试代码处理。

| 位置 | 内容 |
| --- | --- |
| `DebugAndTools/SkyIsland/` | 入口 `SkyIslandRuntimeModule`、切图租约 `SkyIslandRaidLease`、会话 `SkyIslandSession*`、剧情（纯规则 `SkyIslandStoryRules`，存档 `SkyIslandStoryService` / `SkyIslandStoryCodec`）、遭遇、搜刮、采集、居民服务、云蚋、HUD 与面板 |
| `Integration/SkyIsland/` | 岛上物品的注册与使用行为 |
| `DebugAndTools/F3GameplayValidationSkyIsland*.cs` | 岛内只读 F3 验收套件（Dev 构建） |
| `ArtSource/SkyIsland/`、`tools/generate_sky_island*.py`、`tools/sky_island_*.py` | 布局、导航、小地图等可重复生成的数据与生成器 |
| 作者 Unity 工程 `D:/code/ykf/duckov_modding-main/UnityFiles/BossRush/` | 场景、自研着色器、构建器 `SkyIslandRaidBuilder.BuildAndExit`；**独立 git 仓库**，那边的改动在那边提交 |

## 2. 证据分级

每条结论标明级别，不能往上抬：

- **L1 静态接线**：从玩家入口一路读到生产逻辑。
- **L2 隔离回归**：守卫、执行回归、离线几何 / 导航 / 属性测试全绿。
- **L3 实机**：真实游戏进程里跑出来的结果。

编译绿 + 守卫绿 + 部署成功不等于「已生效」「实际可用」。离线能证的（几何可达、导航连通、落点复算、同点交互竞争、掉落池品质带）做成可重跑的属性测试；证不了的写进 `docs/guides/sky-island/天空岛_待人工验证清单.md`，粒度到按哪个键、看哪行 HUD、什么算不合格。`Assets/Data/GameplayCoverage.json` 里没跑过的用例不标 PASS。

## 3. 内容设计

- 新物品、新系统、新 TypeID、`SCHEMA+` 的存档扩展**都可以做**。前提是每件内容写清三栏：从哪来 / 岛上拿来做什么（卖钱不算）/ 串到哪条剧情或系统线。功能重叠的拉开定位；能写成结构守卫的写成守卫（范例 `SkyIslandContentWeaveGuard`）。
- 新物品除 `Integration/AGENTS.md` 的通用接线外，还要进 `SkyIslandItemRules`（中英名、`ValueOf` 正价值、`AllTypeIds`），由 `SkyIslandFieldcraftGuard` 逐项核对。
- **头目 / 岛主「配装即掉落」**（`SkyIslandBossRules` 档案表，`SkyIslandBossEcologyGuard`）：
  - 档案挂在已有自动组的带队位上，只改内容表的 lead 档次，不动 id / marker / count。
  - 夜限定（`NightOnly`）在遭遇层等夜，不在 `SkyIslandBossForge` 里跳过：自动组每个位置每趟只刷一次，Forge 一跳过就把带队位烧成白板拾荒者。`SkyIslandEncounters` 白天留着带队位、不算缺人、不挡清场，入夜再单独补刷；判夜照 §4 的唯一口径。
  - 换阵营（`RivalFaction`）整组 `SetTeam(Teams.bear)`，写在 `Spawn` 里 `SetTeam(Teams.wolf)` 安全网之后、身份层之前，不替代安全网（根 `AGENTS.md` §4.5）。
  - 每次都穿全套专属装备：配装前先 `ItemAssetsCollection.GetPrefab` 预检，刷新模型用 `ForceInvokeSlotContentChangedEvent`，不重复调 `SetItem`。
  - 死后只在该角色实例的 `BeforeCharacterSpawnLootOnDead`（官方建尸体箱之前）按权重留一件、其余配装卸下销毁，箱里其余照官方掉落；不走 BossRush 奖励箱、不挂 `OnDead` 前缀。
  - 专属装备走装备 bundle `skyisland_boss_gear` 注册，名字与价值进 `SkyIslandItemRules`，但**不进** `AllTypeIds`（那张表是 500068 起连续的岛上克隆物品），登记在 `SkyIslandBossRules.AllGearTypeIds`。
  - 模型尺寸、原点与朝向按已上线装备实测：挂点空间 Y 上 Z 前、原点居中；贴图写在 `_MainTex`（`tools/sky_island_boss_gear_import.py` 文件头）。
- 按品质带抽物资要**加权**。在筛出来的清单上均匀抽，一档被抽中的概率会正比于这一档的物品种类数。岛上物资池有单件价值上限，挡住高价官方物品。
- 做收集品的节奏门之前，先算它把完成路径拉长多少、拉长的那段有没有新内容。
- 按出击刷新的状态（搜刮、委托进度、局内 buff、采集点）不进存档。持久事实优先复用剧情存档的 `discoveredNotes`，按 id 前缀区分。确需新旗标走 `SCHEMA+` 并同步 `SkyIslandStoryRules.KnownFlags`，否则 Codec 拒绝整份存档；新区域同步 `RegionBit`、Codec 区域掩码、marker 与作者布局。

## 4. 运行时规则

- **判夜只有一个口径**：`SkyIslandNight` + `SkyIslandLighting.ClockHours()`。没有 `GameClock` 实例时 `TimeOfDay` 恒为 00:00 且不抛异常；不要用官方 `TimeOfDayController.AtNight`。夜是 22–6 点，刻意等于官方 `TimeOfDayController` 运行时的 `nightStart / morningStart`（官方 Volume 与敌人夜间感知同相；反编译源的字段初值 19 / 5 会被 `LevelManagerPrefab` 序列化值覆盖，官方数值以 F3 `SKY_NIGHT_BOUNDARY_OFFICIAL` 实机读数为准，不照抄反编译初值）；一昼夜约 24 现实分钟、夜里约 8 分钟（`clockTimeScale = 60`），写夜间内容先按这个算（`SkyIslandMosquitoGuard`）。
- **玩法计时走游戏时间**（撤离读秒、救援），暂停菜单与拍照模式会冻结它；表现层可以走 unscaled，但暂停时停推进。
- **常驻 HUD 跟随官方 HUD 显隐**（`BossRushUI.IsOfficialHudHidden()`），右上角卡片排在官方「操作说明」提示栈下沿之下（`BossRushUI.GetTopRightHudTop`）。
- **当前区域按脚下地面判定**（`COL_Ground_{区域}` 碰撞体），不按离地标的距离。场景包的 `POI_` 节点数不等于区域数，可完成量一律取地面切分出的区域表。
- **交互体落点**走 `SkyIslandRewardCrate.TryFindCratePosition`，不要把箱子放在角色倒下的位置。新增静态交互体后跑 `SkyIslandInteractionCompetitionPropertyTest` 两两复算。
- **选项先判再挂**：「能不能挂」与「点了会不会被拒」共用 `SkyIslandStoryRules` 里同一份判据；不挂灰项；同页超过 3–4 项分二级（`SkyIslandChoiceGateGuard`）。
- **叙事走官方对话，图鉴走官方 `NoteIndex`**（`SkyIslandNoteBridge`），**跨局主线走官方 `Duckov.Quests`**：任务表只有 `SkyIslandOfficialQuestTable` 一份（序章 590001 挂 Jeff；岛上 590011–590013 挂苇白 / 浮舟 / 钟守，官方 enum 之外的整数给予者 5901–5903，居民缺席那趟由委托板 / 渡口工台 / 钟庭装置兜底），Mod 分槽存档为权威、每条任务 Accepted + Delivered 两位、官方四类快照剥离自己的 ID；桥常驻 `SkyIslandRuntimeModule` 且先于「岛上会话存在就早退」运行（`SkyIslandPreludeGuard`）。按出击刷新的委托不接（`SkyIslandOfficialApiReuseGuard`）。自绘面板只因 `timeScale = 0` 模态保留，理由写在文件头。
- **云蚋这类可被打中的轻量目标不克隆角色**：先失活，建伤害接收体层非触发球 + 运动学刚体 + `DamageReceiver`（`useSimpleHealth`）+ `HealthSimpleBase`（阵营 wolf）再激活；死亡看 `activeSelf`；挪完 `Physics.SyncTransforms()`。躲子弹在 `Projectile.Init(ProjectileContext)` 的后缀里拿弹道，起点用 `firstFrameCheckStartPoint`；瞄准辅助会吸附伤害接收体，所以瞄准线扫过时也要预闪。伤害只在 `SkyIslandGnats`，局内 owner `SkyIslandFieldcraft` 不出现伤害。
- **头目 / 岛主招式控制器走 `SkyIslandBossProps` 共用件**（`SkyIslandBossEcologyGuard`）：
  - 换位只走 `Teleport`：`CancelCurrentPathRequest(true)` →（要硬直时）`BossAIController.Pause` → `SetPosition` → `Physics.SyncTransforms()`，硬直结束由调用方 `Resume`。`StopMove` 不取消在途寻路，回调会把旧路径装回来。
  - 倒影一类分身用 `CreateDecoy`（`BakeMesh` 烤成静态网格挂在轻量接收体上），不 `Instantiate` 角色或模型：会连身上的 `Item` / `ItemAgent` 一起复制。
  - 官方 `Health.Hurt` 在出击图上只磨头盔（暴击）与身甲（非暴击），不磨面罩与耳机：穿这两槽专属装备的 Boss 由自己的控制器在 `Health.OnHurtEvent` 里调 `WearSoftPiece`（照头盔口径），`OnDestroy` 退订。
- **主角穿戴只在一处读**：`SkyIslandFieldcraftBossGear` 按局内 owner 的节拍读主角装备槽，写进快照 `SkyIslandBossGearWorn`；招式控制器、剧情、搜刮箱与云蚋只读快照，不自己读槽，离岛与模块销毁时复位。要进剧情判据的（镜纹甲放行折翎）写 `SkyIslandStoryData` 的 `[NonSerialized]` 运行时字段，不加存档字段。
- 独立出击关卡不保证有 `StockShopView`，岛上服务自带 UI；维修入列门照官方 `ItemRepairView.CanRepair`。每项居民服务都要有装置兜底，居民生成失败的那一趟服务也不断线。
- `SkyIslandSession.cs` 主文件有行数上限，新批次的接线挂到 `SkyIslandWorldStory` 等 owner 上，不往主文件加；落脚点与坠落捞回在同一 partial 的 `SkyIslandSessionFooting.cs`。
- **落脚点必须站得住**：地面探针只把净空的落点（`Blocked`，墙体层胶囊，口径同头目冲步落点）记成 `safePosition`；同一处连捞 3 次换成地标锚点并记一条带坐标的 `FALL_RESCUE_LOOP`。落点自己站不住时捞回去会再掉一次，人在原地弹球（2026-09-16 第八轮 F3 在 `EnemySpawn_C` 一步之内连捞 11 次，步骤还记 PASS）。捞回次数经 `RescueCount` 进 F3，一步之内 ≥3 次记红。

## 5. 岛内 F3 验收套件

- 主套件从基地出发、收尾切图，天空岛不能用。岛内套件要求人已在岛上、只在岛内跑、收尾不切图。
- **只读**：不写剧情、不搬玩家、不刷怪、不开箱、不写运行标记、不给无敌、不跑全宿主清理。`SkyIslandValidationSuiteGuard` 用禁用清单钉死，并区分纯函数 `SkyIslandStoryRules.TryApply`（允许）与会落盘的 `SkyIslandStoryService.TryApply`（禁止）。
- 判据不成立时抛 `SkyIslandSkipCase` 记 SKIP，不记 PASS；会话中途结束后的用例记 SKIP 并附原因。
- F3 只存在于 Dev 构建（编译命令见根 `AGENTS.md` §2），验收完换回正式构建再部署。
- 会改状态的检查（刷怪、强制夜里、弹对话）不进只读套件，进 Dev 演练套件；演练的隔离、还原与不落盘规则见根 `AGENTS.md` §4.17。
- 全自动实机回归（根 `AGENTS.md` §4.17 第三档）在岛上只经两个 Dev 入口文件动会话与存档：`SkyIslandSessionAutotest.cs`（先核地面再瞬移、兜底返航）与 `SkyIslandStoryServiceAutotest.cs`（整份清空 / 还原剧情，只走共享 store 与协调器）。两者整份 `#if BOSSRUSH_DEV`、写入口第一句过 `AutotestWriteAllowed`；剧情阶段推进只走生产入口（`TryApply`、`RecordEncounterCleared`、`RecordNote`、`SkyIslandFieldcraft.LightLamp`）。步骤表里写的标记、居民站位、采集点、面板与字幕文字要对得上生产代码（`SkyIslandAutotestTableGuard`）：改了这些名字或文案，同步 `Assets/Data/SkyIslandAutotest.json`。

## 6. 场景、布局与打包

- 改 `ArtSource/SkyIsland/*.json` 不等于改了游戏里的场景：生成器 → 作者工程重打包 → 判包 → 部署，每一环留证据。
- 岛位、障碍、聚落摆件、铺路、布景、门与木牌的坐标统一经 `tools/sky_island_frame.py` 换算。导航与聚落互相依赖，要反复跑到标记不再移动。挪内容锚点时连门的位置一起看「开门前从哪边够得着」（`SkyIslandGateNavigationPropertyTest`）；岛距一变，按旧岛距调的触发、追踪半径全要跟。
- 贴路、靠墙这类语义化摆放不要用 `tools/sky_island_dressing.py` 的 `PlantingSpace.free()` 当可放判据：它是给植被远离路径撒点用的排除语义，会全部拒绝。
- 导航网格顶点硬上限 4095，超了进岛前就抛异常。余量按运行时 `mesh.vertexCount` 算，不按 UnityPy 离线计数。
- 中继平台是桥的一段（中心标记 `Relay_<桥 ID>`），不是新岛：`RegionBit`、`COL_Ground_<岛>`、门语义都不因它改变。
- 布局、聚落规划、小地图、验证台账与 C# 门坐标、遭遇标记互相引用：先在草稿目录里整条跑通（`SKY_ISLAND_SETTLEMENT_PLAN` 覆盖规划路径，小地图脚本有 `--layout` / `--out-dir` / `--out-json`），再整体换进仓库，避免别的会话看到半红的守卫。
- **重打包不是隔离操作**，会把作者工程当下的全部资产一起发出去：打包前看作者工程资产的修改时间；打包后用 UnityPy 读已构建 bundle 与上一版逐项对照，再跑 `python tools/verify_sky_island_bundle_shaders.py`。作者 / 仓库 / 游戏 Mod 目录三份 SHA-256 写回 `ArtSource/SkyIsland/Validation/raid_deployment_hashes.json`。
- Unity 批处理：先确认没有别的实例占用工程；每步单独执行并检查退出码与产物，不用分号或 `&&` 串联（会把失败伪装成成功）。Blender 后台加 `--factory-startup --python-exit-code 1`，并在日志里找 PASS 标记。
- 光色与天色硬编码在四处（玩家看到的天空主要是云 shader 的 haze 常量），改一处同步四处。画风基准是原版暖琥珀色带，材质 `_BaseColor` 全白、颜色在贴图里（`ArtSource/SkyIsland/VANILLA_GRADE.md`）。
- 小地图形状按几何烘焙、颜色取对齐后的生成图。布局几何一变，`tools/build_sky_island_minimap.py` 会拒绝旧底图：重走 `tools/sky_island_minimap_art.py` 的 reference / generate / align，或加 `--flat`。

## 7. Wiki 与文本

- `WikiContent/{zh,en}/map__sky_island.md` 由 `SkyIslandWikiParityGuard` 按章节形状与语义锚点对齐，改一边就改另一边。
- 码位区间断言写整数常量（如 `0x4E00`），不写字面汉字或 `\u` 转义。

## 8. 命令

```bash
python tools/run_guards.py --filter SkyIsland
python tools/run_runtime_regressions.py --filter SkyIsland
python tools/verify_sky_island_bundle_shaders.py
```

执行回归里依赖官方 DLL 的夹具要能找到游戏程序集，环境变量见 `tests/AGENTS.md`。

## 9. 相关文档

- `ArtSource/SkyIsland/README.md`（资源与打包）、`OFFICIAL_SCENE_CONTRACT.md`（独立官方场景合同）、`NAVIGATION.md`（导航与重建顺序）。
- `docs/architecture/自研着色器与官方渲染管线约定.md`。
- `docs/guides/sky-island/天空岛_待人工验证清单.md`：实机验收从这里开始。
- `docs/guides/从零搭建自定义场景_Blender到Unity到Mod完整教程.md`：新增一张官方级 Mod 地图的完整流程。
