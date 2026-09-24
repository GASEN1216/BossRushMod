# 模块导航

本表由 `architecture/modules.json` 生成。先按职责找到模块 id，再运行 `python tools/task_context.py --module <id> --task "具体任务"` 获取阅读顺序。旧的根目录路径表原文在 `archive/AGENTS_SUBSYSTEM_MAP_2026-09-24.md`。

<!-- BEGIN GENERATED MODULES -->
| id | 职责 | 入口 | 规则文件 |
| --- | --- | --- | --- |
| `host` | 生命周期、调度与模块装配 | `ModBehaviour.cs` | `AGENTS.md` |
| `config` | 运行时配置与参数 | `Config/Config.cs` | `AGENTS.md` |
| `localization` | 玩家文本注入 | `Localization/LocalizationInjector.cs` | `AGENTS.md` |
| `common` | UI、存档、装备与数据共享能力 | `Common/Lifecycle/BossRushRuntimeModuleHost.cs` | `AGENTS.md`、`Common/UI/AGENTS.md` |
| `utilities` | 刷怪、场景门控与跨模块 hooks | `Utilities/ModeRuntimeHooks.cs` | `AGENTS.md`、`Utilities/AGENTS.md` |
| `patches` | 跨模块 Harmony 接点 | `Patches/AI/StaleAITaskCallbackPatch.cs` | `AGENTS.md`、`Patches/AGENTS.md` |
| `arena` | 标准与无间炼狱波次 | `WavesArena/WavesArenaRuntimeModule.cs` | `AGENTS.md` |
| `rewards` | 掉落、奖励与欠账 | `LootAndRewards/InfiniteHellMilestoneDelivery.cs` | `AGENTS.md` |
| `mode-d` | 白手起家 | `ModeD/ModeDRuntimeModule.cs` | `AGENTS.md` |
| `mode-e` | 划地为营 | `ModeE/ModeERuntimeModule.cs` | `AGENTS.md` |
| `mode-f` | 血猎追击 | `ModeF/ModeFRuntimeModule.cs` | `AGENTS.md` |
| `mode-g` | 宿命回响 | `ModeG/ModeGRuntimeModule.cs` | `AGENTS.md` |
| `mode-h` | 百战留痕 | `ModeH/ModeHRuntimeModule.cs` | `AGENTS.md`、`Common/UI/AGENTS.md` |
| `zombie` | 末日丧尸 | `ZombieMode/ZombieModeRuntimeModule.cs` | `AGENTS.md`、`ZombieMode/AGENTS.md`、`Common/UI/AGENTS.md` |
| `campaign` | 鸭王征程 | `Campaign/CampaignRuntimeModule.cs` | `AGENTS.md` |
| `pet-nest` | 遗种巢 | `PetNest/PetNestRuntimeModule.cs` | `AGENTS.md`、`Common/UI/AGENTS.md` |
| `random-events` | 局内随机事件 | `RandomEvents/RandomEventsRuntimeModule.cs` | `AGENTS.md` |
| `achievement` | 成就 | `Achievement/AchievementEntryUI.cs` | `AGENTS.md` |
| `audio` | 音频与 Boss BGM | `Audio/BossBgmCoordinator.cs` | `AGENTS.md` |
| `boss-filter` | Boss 筛选 | `BossFilter/BossFilter.cs` | `AGENTS.md` |
| `ui-signs` | 路牌与通用界面入口 | `UIAndSigns/BossRushInteractionScan.cs` | `AGENTS.md`、`Common/UI/AGENTS.md` |
| `legacy-injection` | 旧注入占位 | `Injection/Injection.cs` | `AGENTS.md` |
| `interactables` | 共享交互体 | `Interactables/BossRushBuildingInteractableBase.cs` | `AGENTS.md` |
| `map-selection` | 地图选择 | `MapSelection/BossRushMapSelectionHelper.cs` | `AGENTS.md` |
| `sky-island` | 天空岛正式地图、居民与剧情 | `DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs` | `AGENTS.md`、`DebugAndTools/SkyIsland/AGENTS.md`、`Common/UI/AGENTS.md` |
| `devtools` | F3、调试与场景原型 | `DebugAndTools/F3GameplayValidationRunner.cs` | `AGENTS.md`、`DebugAndTools/AGENTS.md`、`Common/UI/AGENTS.md` |
| `integration-core` | 物品、装备工厂与集成生命周期 | `Integration/BossRushIntegration.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `npcs` | NPC 与建筑内容 | `Integration/NPCs/Common/CommonNpcRuntimeHooks.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `items` | 物品内容注册 | `Integration/Items/AwenDepositTokenConfig.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `affinity` | 好感与商店 | `Integration/Affinity/AffinityConfig.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `daily-report` | 日报、悬赏与报箱 | `Integration/DailyReport/DailyReportRuntimeModule.cs` | `AGENTS.md`、`Integration/AGENTS.md`、`Common/UI/AGENTS.md` |
| `back-mountain` | 竞技场后山 | `Integration/BackMountain/BackMountainRuntimeModule.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `wish-fountain` | 星愿许愿台 | `Integration/WishFountain/WishFountainService.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `codex` | 鸭皇图鉴 | `Integration/Codex/CodexRuntimeModule.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `reforge` | 重铸 | `Integration/Reforge/ColdQuenchFluidConfig.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `affix-forge` | 词缀锻造 | `Integration/AffixForge/AffixBuffFactory.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `dragon-king` | 龙王 Boss 与武器 | `Integration/DragonKing/DragonKingAbilityController.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `dragon-descendant` | 龙裔 Boss | `Integration/DragonDescendant/DragonBreathBuffHandler.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `phantom-witch` | 幽灵女巫 Boss | `Integration/PhantomWitch/PhantomWitchAbilityController.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `new-weapons` | 新武器 | `Integration/NewWeapons/Common/NewWeaponBootstrap.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `set-bonus` | 套装效果 | `Integration/Bonus/DragonSetBonus.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `frostmourne` | 霜之哀伤 | `Integration/Frostmourne/FrostmourneAbilityManager.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `flight-totem` | 飞行图腾 | `Integration/FlightTotem/CA_Flight.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `death-wraith` | 死亡亡魂 | `Integration/DeathWraith/DeathWraithCombatLoadout.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `reverse-scale` | 逆鳞 | `Integration/ReverseScale/ReverseScaleAbilityManager.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `wedding` | 婚姻 | `Integration/Wedding/NPCMarriageSystem.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `sky-items` | 天空岛物品 | `Integration/SkyIsland/SkyIslandBossGearConfig.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
| `mutators` | 变异词条 | `Integration/Mutators/MutatorDefinitions.cs` | `AGENTS.md`、`Integration/AGENTS.md` |
<!-- END GENERATED MODULES -->
