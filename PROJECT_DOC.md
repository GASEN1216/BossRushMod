# BossRushMod 项目文档（开发向）

## 1. 项目定位
- 目标：为《Escape from Duckov》提供 BossRush 竞技场玩法与扩展内容（Boss 波次挑战、无间炼狱、白手起家、附加 NPC/装备/百科等）。
- 主要玩法：弹指可灭（1 Boss/波）、有点意思（3 Boss/波）、无间炼狱（无限波，Boss 数可配置）、白手起家 Mode D（裸体入场+随机开局装备）。

## 2. 快速上手（构建/运行）
- 构建入口：`compile_official.bat` 或 `compile_temp.ps1`，输出为 `BossRush.dll`。
- 依赖：引用游戏目录 `Duckov_Data/Managed/` 下的程序集（Unity/TeamSoda/Duckov 相关 DLL）。
- 基础流程：购买 Boss Rush 船票 -> 交互路牌/传送 UI -> 选择地图与难度 -> 开始第一波。

## 3. 核心流程与状态机（BossRush）
- 初始化：`ModBehaviour.Awake`/`Start_Integration` 负责加载配置、本地化注入、动态物品/装备加载、事件订阅（场景、商店、存档）。
- 地图选择与传送：`BossRushMapConfig` 定义地图数据；`BossRushMapSelectionHelper` 向官方 MapSelection UI 注入 BossRush 条目，费用为 1 张船票，并用 pending map index 管理后续传送。
- 竞技场准备：`OnSceneLoaded_Integration` 根据 `bossRushArenaPlanned` 判断是否进入 BossRush，禁用 spawner、清理敌人、设置竞技场中心、创建路牌与垃圾桶，并在必要时进行自定义传送。
- 波次生成：`StartFirstWave` 随机洗牌 Boss 池（前 10 波剔除强力 Boss），订阅 `Health.OnDead`，启动 `SpawnNextEnemy` 生成首个 Boss。
- 刷怪细节：`SpawnEnemyAtPositionAsync` 负责生成并修正 Boss 位置；自定义 Boss（龙裔遗族/龙王）走专门分支。
- 波次间隔：`StartNextWaveCountdown` 处理倒计时或交互点开启下一波（由配置决定）。
- 掉落与奖励：`LootAndRewards` 提供 Boss 掉落随机化、奖励箱与无间炼狱现金池逻辑。
- 退出与清理：离开竞技场场景时重置状态、清理缓存、销毁临时对象（ammoShop/NPC 等）。

## 4. Mode D（白手起家）流程
- 入场条件：`IsPlayerNaked` 检查所有装备槽位为空，背包仅允许船票，宠物背包为空。
- 开局装备：`ModeDEquipment` 负责随机发放武器/弹药/医疗/护甲等；并为武器自动装配随机配件。
- 波次规则：`ModeDWaves` 使用固定规则分配 Boss/小怪数量并逐波强化，`ModeDNextWaveInteractable` 手动触发下一波。
- 掉落池：`ModeDGlobalLoot` 构建全局可掉落物品池（排除黑名单/禁用 Tag）。

## 5. 关键数据结构与全局状态
- `BossRushMapConfig`：地图配置（sceneName/sceneID、spawnPoints、customSpawnPos、defaultSignPos、previewImage、mapNorth）。
- `BossRushConfig`：本地/ModConfig 参数（waveInterval、bossStatMultiplier、infiniteHellBossesPerWave、modeDEnemiesPerWave、disabledBosses 等）。
- 运行标记：`IsActive`、`bossRushArenaPlanned`、`bossRushArenaActive`、`infiniteHellMode`、`modeDActive`。
- Boss 池：`enemyPresets` + `BossFilter` 过滤（Ctrl+F10 面板）。
- 缓存体系：`ReflectionCache`、`ObjectCache`、敌人预设初始化标记、物品价值缓存。

## 6. 目录与模块概览
- `ModBehaviour.cs`：主入口与核心状态机（地图配置、传送、波次、清理、全局状态）。
- `TeleportDebugMonitor.cs`：传送流程监听与 F8 坐标记录。
- `ModConfigApi.cs`：ModConfig 安全 API 封装（模板）。
- `BossFilter/`：Boss 池筛选 UI 与配置同步。
- `Common/Effects/`：通用特效基类（`RingParticleEffect`）。
- `Common/Equipment/`：装备能力系统基类（Action/Manager/EffectManager）。
- `Common/Utils/`：通用反射缓存工具。
- `Config/`：BossRush 配置与 NPC 刷新点配置。
- `DebugAndTools/`：DevLog、交互/开枪调试、放置模式、物品生成器等。
- `Injection/`：占位，注入逻辑已合并到 `ModBehaviour`。
- `Integration/BossRushIntegration.cs`：动态物品、商店注入、事件订阅与场景处理。
- `Integration/EquipmentFactory.cs`：AssetBundle 装备/武器加载与匹配规则。
- `Integration/EquipmentHelper.cs`：装备属性/Tag/Constant 配置辅助。
- `Integration/BirthdayCakeItem.cs`：生日蛋糕物品与 Buff。
- `Integration/WikiBookItem.cs`：冒险家日志物品与 Wiki UI 入口。
- `Integration/WikiUIManager.cs`：Wiki UI 生命周期与分页。
- `Integration/WikiContentManager.cs`：Wiki 内容解析（catalog + markdown）。
- `Integration/Bonus/DragonSetBonus.cs`：龙套装套装效果。
- `Integration/Config/DragonSetConfig.cs`：龙头/龙甲属性配置。
- `Integration/Config/FlightTotemConfig.cs`：飞行图腾属性配置。
- `Integration/Dialogue/DialogueActorFactory.cs`：对话角色工厂。
- `Integration/Dialogue/DialogueManager.cs`：大对话封装。
- `Integration/DragonDescendant/`：龙裔遗族 Boss、龙息武器、灼烧 Buff。
- `Integration/DragonKing/`：龙王 Boss、技能控制器与特效。
- `Integration/FlightTotem/`：飞行图腾能力系统（Manager/Action/Effect/特效）。
- `Integration/NPCs/`：快递员 NPC、快递服务、寄存服务与数据持久化。
- `Interactables/`：路牌/难度/下一波/补给/修理/传送/清理箱子/携带箱子等交互。
- `Localization/`：L10n 工具、本地化注入与装备本地化。
- `LootAndRewards/`：掉落随机化、奖励箱、无间炼狱现金池。
- `MapSelection/`：地图选择 UI 注入与费用计算。
- `ModeD/`：白手起家模式入口、装备、波次与掉落。
- `UIAndSigns/`：消息提示、大横幅、方位提示、路牌与垃圾桶创建。
- `Utilities/`：弹药商店、Boss 数值倍率、模型工厂。
- `WavesArena/`：波次与竞技场核心逻辑、刷怪点修正。

## 7. 资源与数据文件
- `Assets/`：AssetBundle 资源（bossrush_ticket、birthday_cake、flight_totem、npcs、wiki UI、entity 模型等）。
- `WikiContent/`：Wiki 目录与条目内容（`catalog.tsv` + markdown）。
- 配置文件：`StreamingAssets/BossRushModConfig.txt`。
- 元数据：`info.ini`。

## 8. 扩展指南
- 添加新地图：1) 在 `BossRushMapConfigs` 添加新配置；2) 提供 spawnPoints、sceneName/sceneID、signPos、previewImage、mapNorth；3) 如需自定义传送位置设置 `customSpawnPos`。
- 添加/改 Boss：普通 Boss 由 `InitializeEnemyPresets` 自动扫描；自定义 Boss 参考 DragonDescendant/DragonKing 的注册与 `SpawnEnemyAtPositionAsync` 分支。
- 添加自定义装备/武器：遵循 `EquipmentFactory` 命名规则放入 `Assets/Equipment/`，并在配置类中注入属性与本地化。
- 扩展 Wiki：更新 `WikiContent/catalog.tsv` 并新增条目 markdown 文件，Wiki UI 会自动读取与分页。

## 9. 调试与开发
- `DevModeEnabled = true` 开启调试功能。
- 常用快捷键：F2 物品生成器、F4 武器信息、F5/F6 放置模式、F7 交互点信息、F8 坐标采集、F9 直接开始 BossRush、F10 直接通关、F11 赠送生日蛋糕。
- 其他：`TeleportDebugMonitor` 记录传送流程并输出日志。

## 10. 注意事项
- 模块大量依赖反射访问游戏内部字段，游戏版本更新可能导致字段变更。
- 场景切换需要清理缓存与事件订阅，核心逻辑集中在 `OnSceneLoaded_Integration`/`OnDestroy_Integration`。
- `bossRushArenaPlanned` 与 `bossRushArenaActive` 是传送与波次逻辑的关键旗标，扩展时务必保持语义一致。
