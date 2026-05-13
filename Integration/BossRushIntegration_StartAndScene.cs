using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BossRush.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Saves;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 注入扩展本地化（委托给 LocalizationInjector）
        /// </summary>
        private void InjectLocalization_Extra_Integration()
        {
            // 统一调用 LocalizationInjector 注入所有本地化
            LocalizationInjector.InjectUILocalization();
            LocalizationInjector.InjectMapNameLocalizations();
            LocalizationInjector.InjectCommonNPCLocalization();   // 通用NPC交互键（聊天/赠礼/商店）
            LocalizationInjector.InjectCourierNPCLocalization();  // 快递员NPC本地化
            LocalizationInjector.InjectGoblinNPCLocalization();   // 哥布林NPC本地化（重铸服务）
            LocalizationInjector.InjectNurseNPCLocalization();    // 护士NPC本地化
            AwenCourierTokenConfig.InjectLocalization();  // 阿稳快递牌物品本地化
            LocalizationInjector.InjectColdQuenchFluidLocalization();  // 冷淬液物品本地化
            LocalizationInjector.InjectBrickStoneLocalization();  // 砖石物品本地化
            LocalizationInjector.InjectDiamondLocalization();     // 钻石物品本地化
            LocalizationInjector.InjectDiamondRingLocalization(); // 钻石戒指物品本地化
            LocalizationInjector.InjectCalmingDropsLocalization(); // 安神滴剂物品本地化
            LocalizationInjector.InjectPeaceCharmLocalization();   // 平安护身符物品本地化
            DingdangDrawingConfig.InjectLocalization();  // 叮当涂鸦物品本地化
            WildHornConfig.InjectLocalization();  // 荒野号角物品本地化
            AwenLootSweepTokenConfig.InjectLocalization();  // 阿稳扫箱令物品本地化
            FactionFlagConfig.InjectLocalization();  // 营旗物品本地化（Mode E）
            RespawnItemConfig.InjectLocalization();  // 刷怪消耗品本地化（Mode E）
            LocalizationInjector.InjectZombieModeLocalization();  // 末日丧尸模式全部本地化（含物品名 + OpenMapFailed/NoInvitation/InvitationUseDesc 等消息键）
            InjectModeFItemLocalization();  // Mode F 物品本地化
            AwenCourierTokenConfig.InjectIntoShops();  // 将阿稳快递牌注入到售货机
            FactionFlagConfig.InjectIntoShops();  // 将营旗注入到售货机
            BloodhuntTransponderConfig.InjectIntoShops();  // 将血猎收发器注入到售货机（Mode F）
            ZombieTideInvitationConfig.InjectIntoShops();
            EquipmentLocalization.InjectAllEquipmentLocalizations();
            InjectReverseScaleLocalization();  // 逆鳞图腾本地化
            LocalizationInjector.InjectWeddingBuildingLocalization();  // 婚礼教堂建筑本地化
            DevLog("[BossRush] 扩展本地化注入完成");
        }

        void Start_Integration()
        {
            LoadConfigFromFile();
            Type modConfigType = FindModConfigType("ModConfig.ModBehaviour");
            if (modConfigType != null)
            {
                SetupModConfig();
                // ModConfig 已安装：以 ModConfig 当前值为准回拉并覆盖本地 config.cfg，
                // 保证玩家在 ModConfig 面板里的设置始终是唯一事实源。
                LoadConfigFromModConfig();
                DevLog("[BossRush] 已从 ModConfig 同步配置并将覆盖本地 config.cfg");
                SaveConfigToFile();
            }
            RefreshDeathWraithEventBindings_DeathWraith();
            ApplyDevModeRuntimeState();

            // 尝试注入本地化字典
            InjectLocalization();

            // 注册需要在实例恢复后再次补配的自定义武器
            RegisterCustomWeaponRuntimeConfigs();

            if (runtimeStateMonitorCoroutine == null)
            {
                runtimeStateMonitorCoroutine = StartCoroutine(MonitorLateRuntimeStateRestore());
            }

            InitializeDynamicItems();
            InjectBossRushTicketLocalization();
            InjectBossRushTicketIntoShops();

            // 初始化生日蛋糕物品
            InitializeBirthdayCakeItem();
            InjectBirthdayCakeLocalization();
            // 不再注入商店，生日蛋糕仅通过12月自动赠送获得

            // 初始化 Wiki Book 物品（冒险家日志）
            InitializeWikiBookItem();
            InjectWikiBookLocalization();
            InjectAdventureJournalIntoShops_Integration();  // 将冒险家日志注入到售货机

            // 初始化成就勋章物品（配置器已在 ItemFactory.LoadAllItems 之前注册）
            InjectAchievementMedalLocalization();
            InjectAchievementMedalIntoShops();  // 将成就勋章注入到售货机（排在最前面）
            AwenCourierTokenConfig.InjectIntoShops();  // 将阿稳快递牌注入到售货机
            InjectBrickStoneIntoShops();  // 将砖石注入到售货机
            ZombieTideInvitationConfig.InjectIntoShops();

            // 初始化自定义装备（自动扫描加载 Assets/Equipment/ 目录）
            LoadEquipmentContent();

            // 初始化早期装备能力系统
            InitializeEarlyEquipmentAbilitySystems();

            // 注意：龙息武器Buff处理器现在是按需订阅
            // 只在玩家装备龙息武器时才订阅Health.OnHurt事件，卸下时取消订阅
            // 这样可以避免在所有伤害事件中进行武器ID检查，提升性能

            DevLog("[BossRush] ========================================");
            DevLog("[BossRush] Boss Rush Mod v1.0 已加载");
            DevLog("[BossRush] ========================================");
            DevLog("[BossRush] 使用方法:");
            DevLog("[BossRush]   1. 购买'Boss Rush船票'（在商店中）");
            // 延迟初始化，等待游戏系统完全加载

            // 注册场景加载事件，在进入新场景时注入交互
            SceneManager.sceneLoaded += OnSceneLoaded;
            // 注册商店购买事件，用于检测进货行为
            StockShop.OnItemPurchased += OnItemPurchased_Integration;

            // 注册存档系统事件，用于持久化船票库存
            SavesSystem.OnCollectSaveData += OnCollectSaveData_TicketStock;
            SavesSystem.OnSetFile += OnSetFile_TicketStock;

            // 注册存档系统事件，用于持久化冒险家日志库存
            SavesSystem.OnCollectSaveData += OnCollectSaveData_JournalStock;
            SavesSystem.OnSetFile += OnSetFile_JournalStock;

            // 注册存档系统事件，用于持久化成就勋章库存
            SavesSystem.OnCollectSaveData += OnCollectSaveData_MedalStock;
            SavesSystem.OnSetFile += OnSetFile_MedalStock;

            // 注册存档系统事件，用于持久化砖石库存
            SavesSystem.OnCollectSaveData += OnCollectSaveData_BrickStoneStock;
            SavesSystem.OnSetFile += OnSetFile_BrickStoneStock;

            // 亡魂系统：切换存档时重置当前文件的亡魂清理状态
            SavesSystem.OnSetFile += OnSetFile_DeathWraith;

            // 注册龙套装装备槽变化事件
            RegisterDragonSetEvents();

            // 初始化后置装备能力系统
            InitializeLateEquipmentAbilitySystems();

            // 如果当前已经在场景中，立即执行一次
            if (CanRunGameplayRuntimeNow(SceneManager.GetActiveScene().name))
            {
                StartCoroutine(FindInteractionTargets(5)); // 立即扫描5次
            }
        }

        void OnDestroy_Integration()
        {
            if (runtimeStateMonitorCoroutine != null)
            {
                StopCoroutine(runtimeStateMonitorCoroutine);
                runtimeStateMonitorCoroutine = null;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            StockShop.OnItemPurchased -= OnItemPurchased_Integration;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_TicketStock;
            SavesSystem.OnSetFile -= OnSetFile_TicketStock;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_JournalStock;
            SavesSystem.OnSetFile -= OnSetFile_JournalStock;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_MedalStock;
            SavesSystem.OnSetFile -= OnSetFile_MedalStock;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_BrickStoneStock;
            SavesSystem.OnSetFile -= OnSetFile_BrickStoneStock;
            SavesSystem.OnSetFile -= OnSetFile_DeathWraith;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_BoundMeleeSnapshot_DeathWraith;
            Health.OnDead -= OnWraithDied_DeathWraith;
            Health.OnDead -= OnEnemyDiedWithDamageInfo;
            ClearDeathWraithState_DeathWraith();

            // 统一销毁公共 NPC，避免卸载时残留引用
            DestroyCommonNPCs("Mod 卸载");

            // 取消注册龙套装事件
            UnregisterDragonSetEvents();

            // 清理装备能力系统
            CleanupEquipmentAbilitySystems();

            // 清理平安护身符运行时事件
            PeaceCharmRuntime.ShutdownRuntime();

            // 清理婚礼教堂建筑系统
            CleanupWeddingBuilding();

            // 清理布满了灰尘的星愿许愿台建筑系统
            CleanupWishFountainBuilding();
            // 静态缓存兜底清理：星愿许愿台抽奖动画
            WishFountainRewardAnimationView.ResetStaticCaches();
            ItemFactory.ResetStaticCaches();
            EquipmentFactory.ResetStaticCaches();
            FactionFlagConfig.ResetStaticCaches();
            DragonBreathWeaponConfig.ClearStaticCache();
            DragonBreathBuffHandler.ClearStaticCache();
            DragonDescendantAbilityController.ClearStaticCache();
            DragonKingAbilityController.ClearStaticCache();
            DragonKingAssetManager.ForceCleanup();
            PhantomWitchAbilityController.ClearStaticCache();
            PhantomWitchAssetManager.ForceCleanup();
            PhantomWitchVfxRedesign.ResetStaticCaches();
            PhantomWitchScytheWeaponConfig.ResetStaticCaches();
            FrostmourneWeaponConfig.ResetStaticCaches();
            FenHuangHalberdWeaponConfig.ResetStaticCaches();
            DragonKingBossGunConfig.ResetStaticCaches();
            DragonKingBossGunRuntime.ResetStaticCaches();
            DragonKingBossGunProjectileAgent.ClearStaticCaches();
            DragonKingBossGunGroundZone.ClearStaticCaches();
            DragonFlameMarkTracker.ResetStaticCaches();
            CustomItemRuntimeStateHelper.ResetStaticCaches();
            NPCShopSystem.ResetStaticCaches();
            BossRush.Utils.NPCNameTagHelper.ResetStaticCaches();
            ModBehaviour.ResetDragonDescendantBossStaticCaches();
            ModBehaviour.ResetLootAndRewardsStaticCaches();
            ReforgeUIManager.ResetStaticCaches();
            try
            {
                Type modBehaviourType = FindModConfigType("ModConfig.ModBehaviour");
                if (modBehaviourType != null)
                {
                    MethodInfo removeDelegateMethod = modBehaviourType.GetMethod("RemoveOnOptionsChangedDelegate", BindingFlags.Public | BindingFlags.Static);
                    if (removeDelegateMethod != null)
                    {
                        Action<string> handler = new Action<string>(OnModConfigOptionsChanged);
                        removeDelegateMethod.Invoke(null, new object[] { handler });
                        DevLog("[BossRush] 已移除配置变更事件监听");
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 移除配置变更事件监听失败: " + e.Message);
            }

            // 清理 Boss 池 UI
            DestroyBossPoolUI();

            // [性能优化] 重置敌人预设初始化标记，下次加载Mod时重新扫描
            _enemyPresetsInitialized = false;

            DevLog("[BossRush] Boss Rush Mod已卸载");
        }

        /// <summary>
        /// 商店购买事件处理：检测玩家是否大量购买 ID 105 物品（进货行为）
        /// 仅在 BossRush 加油站（ammoShop）中生效
        /// </summary>
        private void OnItemPurchased_Integration(StockShop shop, Item item)
        {
            try
            {
                if (shop == null || item == null) return;

                // 仅在 BossRush 加油站中检测
                if (ammoShop == null || shop != ammoShop) return;

                // 检测是否购买了 ID 105 的物品
                if (item.TypeID == 105)
                {
                    item105PurchaseCount++;

                    // 达到 10 个时显示横幅提示
                    if (item105PurchaseCount == 10)
                    {
                        ShowBigBanner(L10n.T("喂喂，你这家伙来这进货了是吗(*´･д･)?", "Hey, are you here to stock up? (*´･д･)?"));
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 处理商店购买事件失败: " + e.Message);
            }
        }

        private void ScheduleWishRewardPoolWarmup()
        {
            StartCoroutine(WishFountainService.WarmupWishRewardPoolAfterDelay());
        }

        private void OnSceneLoaded_Integration(Scene scene, LoadSceneMode mode)
        {
            DevLog("[BossRush] 场景加载: " + scene.name);
            InvalidateIntegrationStockShopCache();
            bool isGameplayScene = IsGameplaySceneName(scene.name);

            // 亡魂系统：场景切换时清理状态
            ClearDeathWraithState_DeathWraith();

            // [内存优化] 场景切换时清理龙裔遗族相关的静态缓存，防止持有已销毁对象引用
            ClearDragonDescendantStaticCache();

            // [内存优化] 场景切换时清理龙王相关的静态缓存
            ClearDragonKingStaticCache();

            // [内存优化] 场景切换时清理幽灵女巫相关的静态缓存
            ClearPhantomWitchStaticCache();

            // [内存优化] 场景切换时清理荒野号角坐骑缓存
            WildHornUsage.ClearMountCache();

            // [修复] 场景切换时清除重铸恢复追踪，允许重新恢复重铸属性
            // 背包物品是 DontDestroyOnLoad，InstanceID 不变，
            // 如果不清除，ReapplyModifiers 的 Harmony Prefix 会跳过恢复
            ReforgeDataPersistence.ClearRestoredTracking();

            // 平安护身符在每个场景仅可触发一次，切图时重置
            PeaceCharmRuntime.ResetSceneTrigger();

            if (isGameplayScene)
            {
                StartCoroutine(DelayedRestoreReforgeDataForInventory());
                StartCoroutine(DelayedSubscribeDragonBreathEvents());
            }

            SetupFlightTotemForScene(scene);
            SetupFenHuangHalberdForScene(scene);
            SetupFrostmourneForScene(scene);
            SetupPhantomWitchScytheForScene(scene);

            if (isGameplayScene)
            {
                StartCoroutine(DelayedApplyDragonGunAmmoOverride());
            }

            // 亡魂系统改为直接绑定原版遗失物创建链，这里不再做场景重试生成。
            if (!IsDeathWraithSystemEnabled())
            {
                InvalidateStoredDeathWraithRecords_DeathWraith("场景加载时配置关闭");
            }

            try
            {
                InjectBossRushTicketIntoShops_Integration(scene.name);
                InjectAdventureJournalIntoShops_Integration(scene.name);
                InjectAchievementMedalIntoShops(scene.name);  // 注入成就勋章
                AwenCourierTokenConfig.InjectIntoShops(scene.name);  // 注入阿稳快递牌
                InjectBrickStoneIntoShops(scene.name);  // 注入砖石
                ZombieTideInvitationConfig.InjectIntoShops(scene.name);
                FactionFlagConfig.InjectIntoShops(scene.name);  // 注入营旗（Mode E）
                BloodhuntTransponderConfig.InjectIntoShops(scene.name);  // 注入血猎收发器（Mode F）
                // 不再注入商店，生日蛋糕仅通过12月自动赠送获得

                // 在基地场景检查并赠送12月份生日蛋糕
                if (scene.name == "Base_SceneV2")
                {
                    StartCoroutine(DelayedBirthdayCakeGift());

                    // 初始化婚礼教堂建筑系统（注入建筑数据 + 恢复已放置建筑的NPC）
                    InitWeddingBuilding();
                    RestoreWeddingBuildingNPC();

                    // 初始化布满了灰尘的星愿许愿台建筑系统
                    InitWishFountainBuilding();
                    RestoreWishFountainBuildings();
                    ScheduleWishRewardPoolWarmup();
                }

                // 使用配置系统检查是否是有效的 BossRush 竞技场场景
                BossRushMapConfig loadedMapConfig = GetMapConfigBySceneName(scene.name);
                if (TryHandleZombieModePendingMapSceneLoaded(scene, loadedMapConfig))
                {
                    return;
                }
                if (loadedMapConfig != null && !loadedMapConfig.customSpawnPos.HasValue)
                {
                    // 只有在通过 BossRush 启动且是默认传送位置的地图时才执行竞技场逻辑
                    if (bossRushArenaPlanned)
                    {
                        BossRushMapSelectionHelper.MarkTargetSceneLoadStarted();
                        InitializeEnemyPresets();
                        InitializeItemValueCacheAsync(); // 异步初始化物品价值缓存
                        InitializeBossPoolFilter();
                        bossRushArenaActive = true;
                        bossRushArenaPlanned = false;

                        // [Bug修复] 确保订阅龙息Buff处理器，使龙裔遗族Boss的龙息能触发龙焰灼烧
                        DragonBreathBuffHandler.Subscribe();

                        // 设置当前地图的刷新点
                        SetCurrentMapSpawnPoints(scene.name);

                        // [性能优化] 立即设置竞技场中心，确保后续清理有范围限制
                        SetArenaCenterFromMapConfig(scene.name);
                        spawnersDisabled = false; // 重置标志确保能重新禁用

                        // [Mode E] 在禁用 spawner 之前预缓存原地图刷怪点位置
                        // Mode E 需要使用这些位置作为阵营刷怪点
                        PreCacheMapSpawnerPositions();
                        ScheduleModeEStartupWarmup("OnSceneLoaded");

                        // [修复] 立即禁用 spawner，防止敌人生成
                        // 场景加载时 spawner 已经存在，必须立即禁用
                        DisableAllSpawners();
                        DevLog("[BossRush] 场景加载后立即禁用 spawner");

                        // [修复] 立即启动持续清理协程，清理已生成的敌人
                        StartCoroutine(ContinuousClearEnemiesUntilWaveStart());
                        DevLog("[BossRush] 场景加载后立即启动持续清理协程");

                        // 设置BossRush竞技场
                        demoChallengeStartPosition = Vector3.zero;
                        // 延迟到地图完全加载后再执行传送、禁用spawner和创建交互点
                        StartCoroutine(WaitForLevelInitializedThenSetup(scene));
                    }
                }
                // 处理 BossRush 自定义传送位置（如零号区等其他地图）
                // 只在目标子场景加载后才执行传送，避免在 LoadingScreen 场景就触发
                else if (bossRushArenaPlanned)
                {
                    // 检查当前场景是否是待处理地图的目标子场景
                    string targetSubScene = BossRushMapSelectionHelper.GetPendingTargetSubSceneName();
                    string targetMainScene = BossRushMapSelectionHelper.GetPendingMainSceneName();
                    Vector3? customPos = BossRushMapSelectionHelper.GetPendingCustomPosition();

                    // 只有当前场景匹配目标子场景时才执行传送
                    if (targetSubScene != null && scene.name == targetSubScene && customPos.HasValue)
                    {
                        DevLog("[BossRush] 检测到目标子场景加载: " + scene.name + ", 执行自定义传送到: " + customPos.Value);
                        BossRushMapSelectionHelper.MarkTargetSceneLoadStarted();
                        bossRushArenaPlanned = false;

                        // 延迟传送玩家到自定义位置
                        StartCoroutine(TeleportPlayerToCustomPosition(customPos.Value));

                        // 清除待处理的地图条目
                        BossRushMapSelectionHelper.ClearPendingMapEntry();
                    }
                    // 如果是加载屏幕、菜单场景或主场景（_Main），保持标记等待子场景加载
                    else if (scene.name.Contains("Loading") || scene.name.Contains("Menu") ||
                             (targetMainScene != null && scene.name == targetMainScene))
                    {
                        DevLog("[BossRush] 检测到中间场景: " + scene.name + ", 保持传送标记等待目标子场景");
                        // 不重置 bossRushArenaPlanned，等待目标场景加载
                    }
                    // 特殊处理：风暴区地下场景 - 如果加载了错误的子场景（地上），强制传送到地下
                    else if (targetSubScene == "Level_StormZone_B0" && scene.name == "Level_StormZone_1" && customPos.HasValue)
                    {
                        DevLog("[BossRush] 检测到加载了错误的子场景: " + scene.name + " (期望: " + targetSubScene + "), 强制传送到地下场景");
                        // 使用 MultiSceneCore.LoadAndTeleport 强制传送到地下场景
                        StartCoroutine(ForceTeleportToSubScene(targetSubScene, customPos.Value));
                    }
                    // 特殊处理：迷宫冷藏区 - 如果加载了雪地军事基地主场景，强制传送到冷藏区子场景
                    else if (targetSubScene == "Level_SnowMilitaryBase_ColdStorage" && scene.name == "Level_SnowMilitaryBase" && customPos.HasValue)
                    {
                        DevLog("[BossRush] 检测到加载了错误的子场景: " + scene.name + " (期望: " + targetSubScene + "), 强制传送到冷藏区");
                        StartCoroutine(ForceTeleportToSubScene(targetSubScene, customPos.Value));
                    }
                    else
                    {
                        // 其他场景，重置标记
                        DevLog("[BossRush] 非目标场景: " + scene.name + ", 重置传送标记");
                        bossRushArenaPlanned = false;
                        BossRushMapSelectionHelper.ClearPendingMapEntry();
                        BossRushMapSelectionHelper.ClearPendingEntryFlowState();
                    }
                }
                else
                {
                    // Bug #1 修复：从竞技场撤离到其他场景时重置状态
                    if (IsActive || bossRushArenaActive)
                    {
                        DevLog("[BossRush] 检测到场景切换（离开竞技场），重置 BossRush 状态");
                        // 优先清空 BossRush 内部的下一波倒计时/提示状态和 NotificationText 队列，防止在基地继续播报 "下一波将在 X 秒后开始"
                        waitingForNextWave = false;
                        waveCountdown = 0f;
                        lastWaveCountdownSeconds = -1;
                        statusMessage = string.Empty;
                        messageTimer = 0f;

                        // [多次进入优化] 清理波次相关状态
                        currentWaveBosses.Clear();
                        bossesInCurrentWaveTotal = 0;
                        bossesInCurrentWaveRemaining = 0;
                        currentEnemyIndex = 0;
                        defeatedEnemies = 0;
                        totalEnemies = 0;

                        // [多次进入优化] 清理大兴兴追踪集合，防止持有已销毁对象引用
                        bossRushOwnedDaXingXing.Clear();

                        // 清理龙裔遗族实例
                        CleanupDragonDescendant();

                        // [多龙皇修复] 清理龙皇实例字典和套装效果
                        CleanupTrackedDragonKingsOnArenaExit();
                        if (dragonKingSetBonusRegistered)
                        {
                            try
                            {
                                Health.OnHurt -= OnDragonKingBossHurt;
                            }
                            catch (Exception e)
                            {
                                DevLog("[BossRush] [WARNING] 离开竞技场时注销龙皇受伤事件失败: " + e.Message);
                            }
                            dragonKingSetBonusRegistered = false;
                        }
                        activeDragonKingHealths.Clear();

                        // 清理幽灵女巫实例字典
                        CleanupPhantomWitchTrackedStateOnArenaExit();

                        // 清理 NotificationText.pendingTexts 队列
                        try
                        {
                            System.Type notifType = typeof(NotificationText);
                            System.Reflection.FieldInfo pendingField = notifType.GetField("pendingTexts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                            if (pendingField != null)
                            {
                                System.Collections.Generic.Queue<string> q = pendingField.GetValue(null) as System.Collections.Generic.Queue<string>;
                                if (q != null)
                                {
                                    q.Clear();
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            DevLog("[BossRush] [WARNING] 离开竞技场时清理通知队列失败: " + e.Message);
                        }

                        // 最后再重置 BossRush 状态标志，避免因为 IsActive 过早被置为 false 导致某些清理逻辑被跳过
                        SetBossRushRuntimeActive(false);
                        bossRushArenaActive = false;
                        bossRushArenaPlanned = false;
                        currentBoss = null;

                        // 统一销毁公共 NPC（快递员/哥布林/护士）
                        DestroyCommonNPCs("离开竞技场场景");

                        // 重置 spawner 禁用标志，以便下次进入竞技场时能重新禁用
                        spawnersDisabled = false;
                        try
                        {
                            if (ammoShop != null)
                            {
                                try
                                {
                                    if (ammoShop.gameObject != null)
                                    {
                                        UnityEngine.Object.Destroy(ammoShop.gameObject);
                                    }
                                }
                                catch (Exception e)
                                {
                                    DevLog("[BossRush] [WARNING] 离开竞技场时销毁加油站商店对象失败: " + e.Message);
                                }
                                ammoShop = null;
                            }
                        }
                        catch (Exception e)
                        {
                            DevLog("[BossRush] [WARNING] 离开竞技场时清理加油站商店失败: " + e.Message);
                        }

                        // 取消敌人死亡监听
                        Health.OnDead -= OnEnemyDiedWithDamageInfo;

                        // 如果是 Mode D 模式，结束 Mode D
                        if (modeDActive)
                        {
                            EndModeD();
                        }

                        // 如果是 Mode E 模式，结束 Mode E
                        if (modeEActive)
                        {
                            EndModeE();
                        }
                    }

                    // 普通模式下：若当前场景有任意公共 NPC 模块可生成，则延迟统一生成
                    if (ShouldSpawnCommonNPCsInScene(scene.name))
                    {
                        DevLog("[NPCSpawn] 普通模式检测到可生成公共NPC场景: " + scene.name + ", 延迟统一生成");
                        StartCoroutine(DelayedSpawnCommonNPCsInNormalMode(scene.name));
                    }

                    ScheduleRestoreFollowingSpouse(scene.name, "普通场景加载");

                    // 其他场景：注入传送到竞技场的交互选项
                if (isGameplayScene)
                {
                    StartCoroutine(FindInteractionTargets(10));
                }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] OnSceneLoaded_Integration 处理失败: scene=" + scene.name + ", " + e.Message);
            }
        }

        /// <summary>
        /// 延迟恢复背包物品的重铸数据
        /// 解决纯 Stats 物品（如焚皇断界戟）不触发 ReapplyModifiers 导致重铸效果丢失的问题
        /// </summary>
        private System.Collections.IEnumerator DelayedRestoreReforgeDataForInventory()
        {
            // 等待玩家角色可用
            float waitTime = 0f;
            while (CharacterMainControl.Main == null && waitTime < 10f)
            {
                yield return new UnityEngine.WaitForSeconds(0.5f);
                waitTime += 0.5f;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null) yield break;

            Inventory inventory = player.CharacterItem.Inventory;
            if (inventory == null) yield break;

            int restored = 0;
            try
            {
                foreach (Item item in inventory)
                {
                    if (item == null) continue;
                    if (CustomItemRuntimeStateHelper.RestoreRuntimeState(item, "PlayerInventory"))
                    {
                        restored++;
                    }
                }

                restored += RestoreRuntimeStateForSlots(player.CharacterItem, "CharacterSlots");
                restored += RestoreRuntimeStateForHoldAgent(player.CurrentHoldItemAgent, "CurrentHoldItemAgent");

                if (PlayerStorage.Inventory != null)
                {
                    foreach (Item item in PlayerStorage.Inventory)
                    {
                        if (item == null) continue;
                        if (CustomItemRuntimeStateHelper.RestoreRuntimeState(item, "PlayerStorage"))
                        {
                            restored++;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                DevLog("[Reforge] 主动恢复重铸数据异常: " + e.Message);
            }

            if (restored > 0)
            {
                DevLog("[Reforge] 场景切换后主动恢复了 " + restored + " 件物品的重铸数据");
            }
        }

        private System.Collections.IEnumerator MonitorLateRuntimeStateRestore()
        {
            WaitForSeconds wait = new WaitForSeconds(0.5f);

            while (true)
            {
                if (!CanRunGameplayRuntimeNow(SceneManager.GetActiveScene().name))
                {
                    yield return wait;
                    continue;
                }

                int restored = 0;

                try
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null && player.CharacterItem != null)
                    {
                        restored += RestoreRuntimeStateForInventory(player.CharacterItem.Inventory, "PlayerInventoryMonitor");
                        restored += RestoreRuntimeStateForSlots(player.CharacterItem, "CharacterSlotsMonitor");
                        restored += RestoreRuntimeStateForHoldAgent(player.CurrentHoldItemAgent, "CurrentHoldItemMonitor");
                    }

                    restored += RestoreRuntimeStateForInventory(PlayerStorage.Inventory, "PlayerStorageMonitor");
                }
                catch (System.Exception e)
                {
                    DevLog("[Reforge] 运行时状态监控异常: " + e.Message);
                }

                if (restored > 0)
                {
                    DevLog("[Reforge] 监控协程补恢复了 " + restored + " 件延迟实例化物品");
                }

                yield return wait;
            }
        }

        private static int RestoreRuntimeStateForInventory(Inventory inventory, string reason)
        {
            if (inventory == null)
            {
                return 0;
            }

            int restored = 0;
            foreach (Item item in inventory)
            {
                if (item == null)
                {
                    continue;
                }

                bool shouldRestore =
                    CustomItemRuntimeStateHelper.IsRuntimeConfiguredType(item.TypeID) ||
                    ReforgeDataPersistence.HasReforgeData(item);

                if (!shouldRestore)
                {
                    continue;
                }

                if (CustomItemRuntimeStateHelper.RestoreRuntimeState(item, reason))
                {
                    restored++;
                }
            }

            return restored;
        }

        private static int RestoreRuntimeStateForSlots(Item characterItem, string reason)
        {
            if (characterItem == null || characterItem.Slots == null)
            {
                return 0;
            }

            int restored = 0;
            foreach (Slot slot in characterItem.Slots)
            {
                if (slot == null || slot.Content == null)
                {
                    continue;
                }

                Item item = slot.Content;
                bool shouldRestore =
                    CustomItemRuntimeStateHelper.IsRuntimeConfiguredType(item.TypeID) ||
                    ReforgeDataPersistence.HasReforgeData(item);

                if (!shouldRestore)
                {
                    continue;
                }

                if (CustomItemRuntimeStateHelper.RestoreRuntimeState(item, reason + ":" + slot.Key))
                {
                    restored++;
                }
            }

            return restored;
        }

        private static int RestoreRuntimeStateForHoldAgent(DuckovItemAgent holdAgent, string reason)
        {
            if (holdAgent == null || holdAgent.Item == null)
            {
                return 0;
            }

            Item item = holdAgent.Item;
            bool shouldRestore =
                CustomItemRuntimeStateHelper.IsRuntimeConfiguredType(item.TypeID) ||
                ReforgeDataPersistence.HasReforgeData(item);

            if (!shouldRestore)
            {
                return 0;
            }

            return CustomItemRuntimeStateHelper.RestoreRuntimeState(item, reason) ? 1 : 0;
        }

        /// <summary>
        /// 普通模式下延迟生成公共NPC
        /// 等待场景完全初始化后再生成，确保地面碰撞体等已加载
        /// </summary>
        private System.Collections.IEnumerator DelayedSpawnCommonNPCsInNormalMode(string sceneName)
        {
            // 等待场景完全加载
            const float maxWait = 10f;
            const float interval = 0.2f;
            float elapsed = 0f;

            while (elapsed < maxWait)
            {
                bool mainExists = ReadMainExistsWithWarning("DelayedSpawnCommonNPCsInNormalMode");
                bool levelInited = ReadLevelInitedWithWarning("DelayedSpawnCommonNPCsInNormalMode");

                if (mainExists && levelInited)
                {
                    break;
                }

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            // 额外等待确保场景物理碰撞体已加载
            yield return new WaitForSeconds(0.5f);

            // 再次检查是否仍在目标场景（玩家可能已切换场景）
            string currentScene = ReadActiveSceneNameWithWarning("DelayedSpawnCommonNPCsInNormalMode");

            if (currentScene != sceneName)
            {
                DevLog("[NPCSpawn] 场景已切换，取消普通模式公共NPC生成");
                yield break;
            }

            // 检查是否已进入 BossRush 模式（玩家可能在等待期间启动了 BossRush）
            if (ShouldSuppressBaseNpcSpawnForCurrentMode())
            {
                DevLog("[NPCSpawn] 已进入 BossRush 模式，跳过普通模式公共NPC生成");
                yield break;
            }

            SpawnCommonNPCs("普通模式场景初始化完成");
            ScheduleRestoreFollowingSpouse(sceneName, "普通模式场景初始化完成");
        }

    }
}
