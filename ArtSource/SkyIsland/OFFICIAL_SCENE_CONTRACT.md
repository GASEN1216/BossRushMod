# 天空岛独立官方场景装配合同

2026-09-08，分类 `COMPAT / WIRE+ / SCHEMA+`。身份冻结为 `BossRush_SkyIsland`；真实 Scene 为 `SkyIslandRaid`，bundle 路径 `Assets/SkyIsland/SkyIslandRaid.unity`。单 Scene 同时作为官方 MultiSceneCore 主场景与子场景，不保留基地 LevelManager、主角、背包或相机实例。

以下规格来自本机 `TeamSoda.Duckov.Core.dll` 的实际 IL 和对应官方源码，DLL MVID `a106892a-7763-4b22-b7b4-1149fa638550`。它是制作与回归合同，不能代替真实游戏验收。

## 作者场景必需对象与序列化字段

| 对象/组件 | 必需装配 |
| --- | --- |
| `LevelConfig` | `isBaseLevel=false`、`isRaidMap=true`、`spawnTomb=true`、`saveCharacter=true`、`savePet=true`；`startBuffPrefabs` 显式空列表；`minExitCount/maxExitCount=0` 使用天空岛自己的撤离交互 |
| `LevelConfig.timeOfDayConfig` | 完整有效的官方 `TimeOfDayConfig` 资产或完整副本；所有可遇到天气必须有时段 entries，不能 new 空配置。官方时间组件 Start 和 Update 均直接解引用 |
| `MultiSceneCore` | `subScenes` 非空列表，单项 `sceneID=BossRush_SkyIsland`；`cachedLocations` 至少含 `StartPoints/PlayerSpawn` 及真实世界坐标；`cachedTeleporters` 空列表；`playStinger` 可空，`levelStateName` 使用有效音频状态 |
| `SceneLocationsProvider` | 子物体层次真实存在 `StartPoints/PlayerSpawn`，世界坐标与 cachedLocations 一致。该组件以自身为根解析路径 |
| 地形/模型 | 正常地面/墙体碰撞层、可用导航、天空岛独立材质和光照；加载时必须已经有可站立出生点 |
| 身份桥 | 精确 Scene/reference 绑定 `BossRush_SkyIsland`，bundle 的 `buildIndex=-1` 不能全局映射给其他自定义 Scene。自己的 `MultiSceneCore.LoadSubScene` 激活单 Scene 并发出官方 `OnSubSceneLoaded` |

`LevelConfig.Awake` 会自己 `Instantiate(GameplayDataSettings.Prefabs.LevelManagerPrefab)`，这是官方完整服务模板，已包含 Input、CharacterCreator、Camera、FOW、TimeOfDay、Explosion、BulletPool 等引用。不要用零件式空 `LevelManager` 替代该 prefab；也不要把基地现有 prefab 实例搬进来。

墓碑、敌人、物品创建会走 `MultiSceneCore.MoveToActiveWithScene`。该方法的 `GetSetActiveWithSceneParent(-1)` 只允许对本场景的 core 和实际 Scene 做窄桥；若直接落到未知 SceneInfo 的官方 fallback，父物体被设为 inactive，墓碑/角色将静默消失。

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
