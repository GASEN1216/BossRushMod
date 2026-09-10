# 天空岛独立官方场景装配合同

2026-09-08，分类 `COMPAT / WIRE+ / SCHEMA+`。身份冻结为 `BossRush_SkyIsland`；真实 Scene 为 `SkyIslandRaid`，bundle 路径 `Assets/SkyIsland/SkyIslandRaid.unity`。单 Scene 同时作为官方 MultiSceneCore 主场景与子场景，不保留基地 LevelManager、主角、背包或相机实例。

以下规格来自本机 `TeamSoda.Duckov.Core.dll` 的实际 IL 和对应官方源码，DLL MVID `a106892a-7763-4b22-b7b4-1149fa638550`。它是制作与回归合同，不能代替真实游戏验收。

## 作者场景必需对象与序列化字段

| 对象/组件 | 必需装配 |
| --- | --- |
| `LevelConfig` | `isBaseLevel=false`、`isRaidMap=true`、`spawnTomb=true`、`saveCharacter=true`、`savePet=true`；`startBuffPrefabs` 显式空列表；`minExitCount/maxExitCount=0` 使用天空岛自己的撤离交互 |
| `LevelConfig.accountAvailable` | 决定岛上付费服务能否动银行账户（官方 `ContextualMoneyAndCash` 也用它决定是否显示账户余额）。**序列化默认 true，目前场景包没有显式关掉**，因此渡口整备与苔药现在可以花存款。若要改成只收随身现金，把它设为 `false` 并重打包即可，代码侧 `SkyIslandServices.AccountAvailable` 会自动跟随。注意 `saveCharacter` **不是**账户门控——它只管「是否把主角写回存档」，raid 图里同样为 true |
| `LevelConfig.timeOfDayConfig` | 完整有效的官方 `TimeOfDayConfig` 资产或完整副本；所有可遇到天气必须有时段 entries，不能 new 空配置。官方时间组件 Start 和 Update 均直接解引用 |
| `MultiSceneCore` | `subScenes` 非空列表，单项 `sceneID=BossRush_SkyIsland`；`cachedLocations` 至少含 `StartPoints/PlayerSpawn` 及真实世界坐标；`cachedTeleporters` 空列表；`playStinger` 可空，`levelStateName` 使用有效音频状态 |
| `SceneLocationsProvider` | 子物体层次真实存在 `StartPoints/PlayerSpawn`，世界坐标与 cachedLocations 一致。该组件以自身为根解析路径 |
| 地形/模型 | 正常地面/墙体碰撞层、可用导航、天空岛独立材质和光照；加载时必须已经有可站立出生点 |
| 身份桥 | 精确 Scene/reference 绑定 `BossRush_SkyIsland`，bundle 的 `buildIndex=-1` 不能全局映射给其他自定义 Scene。自己的 `MultiSceneCore.LoadSubScene` 激活单 Scene 并发出官方 `OnSubSceneLoaded` |

`LevelConfig.Awake` 会自己 `Instantiate(GameplayDataSettings.Prefabs.LevelManagerPrefab)`，这是官方完整服务模板，已包含 Input、CharacterCreator、Camera、FOW、TimeOfDay、Explosion、BulletPool 等引用。不要用零件式空 `LevelManager` 替代该 prefab；也不要把基地现有 prefab 实例搬进来。

墓碑、敌人、物品创建会走 `MultiSceneCore.MoveToActiveWithScene`。该方法的 `GetSetActiveWithSceneParent(-1)` 只允许对本场景的 core 和实际 Scene 做窄桥；若直接落到未知 SceneInfo 的官方 fallback，父物体被设为 inactive，墓碑/角色将静默消失。

## 进出岛与原版一致性

进图与撤离必须与原版走同一套观感，否则玩家一眼就能看出这张图"不是官方的"：

| 环节 | 官方做法 | 天空岛 |
| --- | --- | --- |
| 出击读条 | `MapSelectionView.LoadTask` → `LoadScene(..., clickToConinue: true)`，读条完停在「点击继续」 | 同（`SkyIslandRaidLease.BeginLoad`） |
| 撤离切图 | `SceneLoaderProxy.Task`：`NotifyEvacuated` → `ClosureView.ShowAndReturnTask(1f)` → 幕布换成 `EvacuateScreenScene` → `LoadScene(..., clickToConinue: false)` | 同（`SkyIslandRaidLease.ReturnToBase`，结算画面 fail-open） |
| 撤离圈计时 | 官方出口预制体 `CountDownArea`：触发器计时，**任何官方 View 打开时不推进**，死亡中止 | Session 自算距离/停留（`ExtractionRadius` 2.5 m / `ExtractionHold` 3 s，HUD 文本倒计时）；推进分支带 `View.ActiveView == null` 门（2026-09-09 补），且官方界面打开时**冻结**读条而不是清零（顺延 `extractionStarted`，与官方「不推进」同语义；旧实现开一下背包就把 3 秒清零）；自绘剧情面板打开时同样冻结（它把 `timeScale` 压到 0，但 `unscaledTime` 照走）。**未复用官方倒计时控件** `EvacuationCountdownUI`，观感仍与原版有差异，属已知项 |
| 撤离点标识 | 官方出口预制体自带可见的撤离区域 | `SkyIslandExtractionRings` 在 `Exit` / `BellExtraction` 脚下画贴地圆环（共享 `SkyIslandGroundRing`，与噬风预警圈同一建造点）：码头蓝环恒亮、钟庭绿环随敲钟结局出现，半径即 `ExtractionRadius`。**作者场景不提供任何撤离视觉**——`tools/generate_sky_island.py` 的 `marker()` 建的是 Blender Empty，官方小地图也不标撤离点，所以这一层必须由代码补（CR-2026-09-09-012）|
| 返航前封锁输入 | `SceneLoaderProxy.LoadScene` 先 `InputManager.DisableInput(gameObject)` 再派发，黑幕淡入期间不能再移动/开火/交互 | `Close` 派发返航前 `BlockInputForReturn()`（2026-09-09 补）：封锁源是**岛场景内的临时对象**，随场景卸载解封；`Cleanup` 也销毁它。**不能挂在 DontDestroyOnLoad 的 Mod 宿主上**，`InputManager` 只在源对象销毁/失活时解封，否则回基地后输入永久锁死 |
| 加载被拒 | `SceneLoader.LoadScene` 遇到 `IsSceneLoading` 或身份未登记时只记一条 LogError 就同步返回 | `Build` 等 root 的循环观察 `lease.LoadFinished`（2026-09-09 补）：立刻按「未起航」清理（`loadStarted = false`），不空等 120 秒，也不发起多余的回基地加载 |
| 死亡 | 官方 `CharacterDieTask` 全权处理，内含 `ClosureView` 与 `LoadBaseScene` | 不接管，交给官方 |
| 出击费用 | `MapSelectionEntry.Cost`，确认面板显示并扣费 | **无**，天空岛出击免费（有意的产品决定） |
| 目的地确认 | 选图 → 目的地预览 + 费用 → 确认/取消 | **无**，与船点交互即开始加载 |

`NotifyEvacuated` 在官方路径里会被调用两次（`SceneLoaderProxy` 一次、`SceneLoader.LoadScene` 内部一次），
这是原版既有行为，天空岛如实照搬，不要"优化"掉其中一次。

`LevelManager.loadLevelBeaconIndex` 是公开静态、被消费后自清零，且找不到 `StartPoints_N` 时会回退到
`StartPoints`。天空岛只有一个出生点，因此不需要设置它，上一张图残留的值也不会造成错误出生。

### 已知的时序差异（不是缺陷，重打包时可收敛）

官方地图里 `LevelConfig` / `MultiSceneCore` / `SceneLocationsProvider` 随场景激活就存在；天空岛的服务根
`SkyIslandLevel` 在包里是未激活的，由 `SkyIslandRaidLease.OnSceneLoaded` 验证合同后才 `SetActive`。
`SceneManager.sceneLoaded` 按订阅顺序调用，Mod 启动时就订阅的处理器（`ModBehaviour.OnSceneLoaded_Integration` 等）
先于租约执行，它们看到的是「没有 `LevelManager` 的战斗场景」。仓库内这些处理器都走延迟协程或
`OnAfterLevelInitialized`，没有同步依赖；其他 Mod 不保证。另外地形根 `SkyIslandWorld` 由 Session 在材质/光照
替换后才激活，主角在此之前已由 `CreateMainCharacterAsync` 创建，黑幕期间会短暂坠落，靠 `InitLevel` 末尾的
`SetPosition(startPos)` 与 Session 的坠落救援兜底。若后续重打 bundle，建议地形根常开、只保留服务根未激活。

## 初始化顺序

官方 `InitLevel` 创建新的主角与角色物品树，调用 `LoadAndTeleport` 解析出生点，订阅主角死亡，创建宠物，等待外部 `IInitializedQueryHandler`，处理新 Raid，恢复血量，创建出口/地图元素，等待 0.25 秒后再次 `SetPosition(startPos)`，最后触发 `OnAfterLevelInitialized`。`afterInit=true` 在该事件之后才写入，因此正式玩法应在下一帧确认 `LevelManager.AfterInit`。

`CreateMainCharacterAsync` 复用官方 `LoadOrCreateCharacterItemInstance`、`CharacterCreator.CreateCharacter`、控制角色绑定、阵营、Sticky 接受设置、默认技能与输入初始化。`startBuffPrefabs` 无 null 防御。`MultiSceneCore` 对单地图也必需，因为官方宠物创建会读取 `MultiSceneCore.MainScene.Value`。

`WaitForOtherInitialization` 只等待已登记的初始化处理器，不要求额外的 `RandomCharacterSpawnPoint`。主角地图出生位置由 LocationsProvider 及 cachedLocations 决定。

## 官方死亡与取回

真实 `CharacterDieTask` 的顺序是：Raid 死亡通知 → 保存 DeathInfo 和 LastDeadCharacter → 官方 Tomb `CreateFromItem(..., filterDontDropOnDead=true)` → `DestroyAllItem` → 保存当前角色/CollectSaveData/SaveFile → 2.5 秒后死亡结算界面 → 官方 `LoadBaseScene`。不改动死亡物品过滤、难度、保险/Sticky、装备损失或官方死亡 UI。

`DeathInfo.subSceneID` 来自 `MultiSceneCore.ActiveSubSceneID`，记录必须精确属于 `BossRush_SkyIsland`。重进后的官方 `OnSubSceneLoaded` 只恢复当前难度允许、仍 valid、未 touched 且子图 ID 匹配的记录，数量受官方 `SaveDeadbodyCount` 约束。墓碑被打开后官方标记 touched，未取完内容的跨图处理遵守官方规则。天空岛不额外复制角色物品树或自己补发损失。

## 已验证与待验收

`SkyIslandOfficialContract.Verify` 只读验证实际服务与主角归属、初始化完成、死亡保存、时间配置、独立身份、出生点提供器与官方墓碑管理器。

`tests/fixtures/SkyIslandOfficialContract` 链接该生产门控，执行 21 个正常/破坏场景；另只读官方 DLL 核查关键状态机调用顺序与死亡身份过滤。当前合计 55 条断言通过。测试不会运行官方游戏代码或读取玩家存档。

真实 SceneLoader 进出、玩家背包重建、墓碑死亡/重进/打开/未取完再离场、宠物、光照、相机与性能必须在游戏中验收；不能以这些静态合同和替身测试宣称游戏验收完成。
