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
            LocalizationInjector.InjectUILocalization();
            LocalizationInjector.InjectMapNameLocalizations();
            LocalizationInjector.InjectCommonNPCLocalization();
            LocalizationInjector.InjectCourierNPCLocalization();
            LocalizationInjector.InjectGoblinNPCLocalization();
            LocalizationInjector.InjectNurseNPCLocalization();
            AwenCourierTokenConfig.InjectLocalization();
            LocalizationInjector.InjectColdQuenchFluidLocalization();
            LocalizationInjector.InjectBrickStoneLocalization();
            LocalizationInjector.InjectDiamondLocalization();
            LocalizationInjector.InjectDiamondRingLocalization();
            LocalizationInjector.InjectCalmingDropsLocalization();
            LocalizationInjector.InjectPeaceCharmLocalization();
            DingdangDrawingConfig.InjectLocalization();
            WildHornConfig.InjectLocalization();
            AwenLootSweepTokenConfig.InjectLocalization();
            FactionFlagConfig.InjectLocalization();
            // Mode H 的 BossRush_ModeH_ 键统一来自 Localization/ModeHLocalization.cs
            ModeHLocalization.Inject();
            // 遗种巢的 BossRush_PetNest_ 键统一来自 Localization/PetNestLocalization.cs
            PetNestLocalization.Inject();

            // 随机事件的 BossRush_RandomEvent_ 键：该模块绝大多数文案走内联 L10n.T，
            // 只有商人交互名走官方按 key 查表的 _overrideInteractNameKey，必须注入。
            RandomEventsLocalization.Inject();
            // 日报的 BossRush_DailyReport_ 键统一来自 Localization/DailyReportLocalization.cs
            DailyReportLocalization.Inject();
            // 鸭皇图鉴的 BossRush_Codex_ 键统一来自 Localization/CodexLocalization.cs
            CodexLocalization.Inject();
            // 词缀锻造的 BossRush_Affix 键统一来自 Localization/AffixForgeLocalization.cs
            AffixForgeLocalization.Inject();
            // 鸭王征程的建筑/交互/线索键统一来自 Localization/CampaignLocalization.cs
            CampaignLocalization.Inject();
            // 后山种子与出击餐的 DisplayNameRaw 注入（AGENTS.md 4.4）
            BackMountainItems.InjectLocalization();
            // 后山建筑与交互键统一来自 Localization/BackMountainLocalization.cs
            BackMountainLocalization.Inject();
            RespawnItemConfig.InjectLocalization();
            LocalizationInjector.InjectZombieModeLocalization();
            InjectModeFItemLocalization();
            EquipmentLocalization.InjectAllEquipmentLocalizations();
            NewWeaponPlaceholderRegistry.InjectLocalization();
            InjectReverseScaleLocalization();
            LocalizationInjector.InjectWeddingBuildingLocalization();
            DevLog("[BossRush] extension localization injected");
        }

        void Start_Integration()
        {
            LoadConfigFromFile();
            Type modConfigType = FindModConfigType("ModConfig.ModBehaviour");
            if (modConfigType != null)
            {
                SetupModConfig();
                LoadConfigFromModConfig();
                DevLog("[BossRush] loaded config from ModConfig and synced local config.cfg");
                SaveConfigToFile();
            }

            RefreshDeathWraithEventBindings_DeathWraith();
            ApplyDevModeRuntimeState();
            InjectLocalization();
            EnsureLanguageChangeSubscription();
            RegisterCustomWeaponRuntimeConfigs();

            if (runtimeStateMonitorCoroutine == null)
            {
                runtimeStateMonitorCoroutine = StartCoroutine(MonitorLateRuntimeStateRestore());
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneLoader.onAfterSceneInitialize += OnAfterSceneInitialize_Integration;
            StockShop.OnItemPurchased += OnItemPurchased_Integration;

            SavesSystem.OnCollectSaveData += OnCollectSaveData_TicketStock;
            SavesSystem.OnSetFile += OnSetFile_TicketStock;
            SavesSystem.OnCollectSaveData += OnCollectSaveData_JournalStock;
            SavesSystem.OnSetFile += OnSetFile_JournalStock;
            SavesSystem.OnCollectSaveData += OnCollectSaveData_MedalStock;
            SavesSystem.OnSetFile += OnSetFile_MedalStock;
            SavesSystem.OnCollectSaveData += OnCollectSaveData_BrickStoneStock;
            SavesSystem.OnSetFile += OnSetFile_BrickStoneStock;
            SavesSystem.OnCollectSaveData += OnCollectSaveData_CodexBookStock;
            SavesSystem.OnSetFile += OnSetFile_CodexBookStock;
            SavesSystem.OnSetFile += OnSetFile_DeathWraith;

            RegisterDragonSetEvents();
            RegisterSetBonusEvents();

            EnsureIntegrationContentBootstrapScheduled("Start");
            ScheduleDeferredSceneSetupForActiveScene("Start");

            if (CanRunGameplayRuntimeNow(SceneManager.GetActiveScene().name))
            {
                StartCoroutine(FindInteractionTargets(5));
            }
        }

        void OnDestroy_Integration()
        {
            if (runtimeStateMonitorCoroutine != null)
            {
                StopCoroutine(runtimeStateMonitorCoroutine);
                runtimeStateMonitorCoroutine = null;
            }

            CleanupDeferredIntegrationBootstrap_Integration();

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneLoader.onAfterSceneInitialize -= OnAfterSceneInitialize_Integration;
            StockShop.OnItemPurchased -= OnItemPurchased_Integration;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_TicketStock;
            SavesSystem.OnSetFile -= OnSetFile_TicketStock;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_JournalStock;
            SavesSystem.OnSetFile -= OnSetFile_JournalStock;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_MedalStock;
            SavesSystem.OnSetFile -= OnSetFile_MedalStock;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_BrickStoneStock;
            SavesSystem.OnSetFile -= OnSetFile_BrickStoneStock;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_CodexBookStock;
            SavesSystem.OnSetFile -= OnSetFile_CodexBookStock;
            SavesSystem.OnSetFile -= OnSetFile_DeathWraith;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_BoundMeleeSnapshot_DeathWraith;
            // 卸载前把内存中尚未写盘的亡魂列表刷一次，再解绑刷写回调，避免丢失死亡记录。
            FlushDeathWraithListIfDirty_DeathWraith();
            SavesSystem.OnCollectSaveData -= FlushDeathWraithListIfDirty_DeathWraith;
            Health.OnDead -= OnWraithDied_DeathWraith;
            Health.OnDead -= OnEnemyDiedWithDamageInfo;
            ClearDeathWraithState_DeathWraith();

            // 统一销毁公共 NPC，避免卸载时残留引用
            DestroyCommonNPCs("Mod 卸载");

            // 取消注册龙套装事件
            UnregisterDragonSetEvents();

            // 取消注册霜冠/雷神套装事件
            UnregisterSetBonusEvents();

            // 清理装备能力系统
            CleanupEquipmentAbilitySystems();

            // 清理平安护身符运行时事件
            PeaceCharmRuntime.ShutdownRuntime();

            // 清理婚礼教堂建筑系统
            CleanupWeddingBuilding();

            // 清理布满了灰尘的星愿许愿台建筑系统
            CleanupWishFountainBuilding();

            // 清理遗种巢建筑系统
            CleanupPetNestBuilding();

            // 清理鸭王征程公告板建筑系统
            CleanupCampaignBoardBuilding();

            // 清理竞技场后山战利品展示柜建筑系统
            CleanupBackMountainShowcase();
            PetNestUIBridge.ResetStaticCaches();
            // 静态缓存兜底清理：星愿许愿台抽奖动画
            WishFountainRewardAnimationView.ResetStaticCaches();
            BossRushDynamicItemRegistry.ResetStaticCaches();
            ItemFactory.ResetStaticCaches();
            EquipmentFactory.ResetStaticCaches();
            NewWeaponPlaceholderRegistry.ResetStaticCaches();
            SetBonusPlaceholderRegistry.ResetStaticCaches();
            FactionFlagConfig.ResetStaticCaches();
            ModeGEncounterVariation.ResetStaticCaches();
            ModeGMapSupportRegistry.ResetStaticCaches();
            DragonBreathWeaponConfig.ClearStaticCache();
            DragonBreathBuffHandler.ClearStaticCache();
            // Mode G 分流（加法分支）：仍被 Mode G late-cleanup sink 持有 lease 时，
            // 延后 Mode G 相关 controller/AssetManager 的 ForceCleanup 与静态缓存清理，
            // 由 sink 归零后以同一 owner lease 执行（Felix 侧）。
            // 无 Mode G lease 时条件恒 false，逐字保持当前即时 teardown。
            bool modeGLateSinkBusy = false;
            try
            {
                modeGLateSinkBusy = ModeGRuntimeGates.IsModeGGlobalQuarantineActive;
            }
            catch
            {
                modeGLateSinkBusy = false;
            }

            if (!modeGLateSinkBusy)
            {
                DragonDescendantAbilityController.ClearStaticCache();
                DragonKingAbilityController.ClearStaticCache();
                DragonKingAssetManager.ForceCleanup();
                PhantomWitchAbilityController.ClearStaticCache();
                PhantomWitchAssetManager.ForceCleanup();
            }
            else
            {
                DevLog("[BossRush] [WARNING] Mode G late sink 仍有 pending lease，延后 DragonKing/PhantomWitch ForceCleanup 与静态缓存清理");
            }
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
            NPCPlayerLookupCache.ResetStaticCaches();
            PhantomWitchPerformancePolicy.ResetStaticCaches();
            EquipmentHelperIcon.ResetStaticCaches();
            ModBehaviour.ResetDragonDescendantBossStaticCaches();
            ModBehaviour.ResetLootAndRewardsStaticCaches();
            CleanupIntegrationRuntimeStaticCaches();
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
            _enemyPresetInitializationScanCount = 0;

            DevLog("[BossRush] Boss Rush Mod已卸载");
        }

        /// <summary>
        /// OnDestroy 集成清理链上的共享静态缓存终点。单独成方法既缩短宿主销毁方法，
        /// 也让 StaticCacheLifecycleGuard 能稳定识别这两个调用确实位于销毁路径。
        /// </summary>
        private void CleanupIntegrationRuntimeStaticCaches()
        {
            ReforgeUIManager.ResetStaticCaches();
            ObjectCache.ResetStaticCaches();
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
            DevLog("[BossRush] scene loaded: " + scene.name);
            ObjectCache.ForceRefresh();
            InvalidateIntegrationStockShopCache();
            bool isGameplayScene = IsGameplaySceneName(scene.name);

            ClearDeathWraithState_DeathWraith();
            ClearDragonDescendantStaticCache();
            ClearDragonKingStaticCache();
            ClearPhantomWitchStaticCache();
            WildHornUsage.ClearMountCache();
            ReforgeDataPersistence.ClearRestoredTracking();
            PeaceCharmRuntime.ResetSceneTrigger();

            if (IsBaseHubSceneName(scene.name))
            {
                TryInitializeWeddingBuildingEarly();
                TryInitializeWishFountainEarly();
                TryInitializePetNestEarly();
                TryInitializeDailyReportMailboxEarly();
                // 老档已建过公告板/展示柜时，必须赶在 BuildingArea.Start 之前注册 prefab，
                // 否则官方会先报「缺 prefab」留下幽灵建筑
                TryInitializeCampaignBoardEarly();
                TryInitializeBackMountainShowcaseEarly();
            }

            if (isGameplayScene)
            {
                StartCoroutine(DelayedRestoreReforgeDataForInventory());
                StartCoroutine(DelayedSubscribeDragonBreathEvents());
            }

            SetupFlightTotemForScene(scene);
            SetupReverseScaleForScene(scene);
            SetupFenHuangHalberdForScene(scene);
            SetupFrostmourneForScene(scene);
            SetupPhantomWitchScytheForScene(scene);
            SetupNewWeaponsForScene(scene);

            if (isGameplayScene)
            {
                StartCoroutine(DelayedApplyDragonGunAmmoOverride());
            }

            if (!IsDeathWraithSystemEnabled())
            {
                InvalidateStoredDeathWraithRecords_DeathWraith("scene load with death-wraith disabled");
            }

            ScheduleDeferredSceneSetupForActiveScene("SceneLoaded:" + scene.name);

            try
            {
                BossRushMapConfig loadedMapConfig = GetMapConfigBySceneName(scene.name);
                if (TryHandleZombieModePendingMapSceneLoaded(scene, loadedMapConfig))
                {
                    return;
                }

                if (loadedMapConfig != null && !loadedMapConfig.customSpawnPos.HasValue)
                {
                    if (bossRushArenaPlanned)
                    {
                        BossRushMapSelectionHelper.MarkTargetSceneLoadStarted();
                        bool deferArenaCommitForModeG =
                            BossRushMapSelectionHelper.HasPendingModeGEntryIntent();
                        // Mode H 与 Mode G 同形：识别 typed H intent 后跳过 Legacy 接管四件事
                        //（bossRushArenaActive、Mode E warmup、DisableAllSpawners、ContinuousClear），
                        // 随后由 ModeHArenaIsolationLease 完成原图 spawner 与原生敌人隔离（§17.1、§19.2）。
                        bool deferArenaCommitForModeH =
                            BossRushMapSelectionHelper.HasPendingModeHEntryIntent();
                        bool deferArenaCommit = deferArenaCommitForModeG || deferArenaCommitForModeH;
                        InitializeEnemyPresets();
                        InitializeItemValueCacheAsync();
                        InitializeBossPoolFilter();
                        if (!deferArenaCommit)
                        {
                            bossRushArenaActive = true;
                        }
                        bossRushArenaPlanned = false;
                        DragonBreathBuffHandler.Subscribe();
                        SetCurrentMapSpawnPoints(scene.name);
                        SetArenaCenterFromMapConfig(scene.name);
                        spawnersDisabled = false;
                        if (!deferArenaCommit)
                        {
                            PreCacheMapSpawnerPositions();
                            ScheduleModeEStartupWarmup("OnSceneLoaded");
                            DisableAllSpawners();
                            StartCoroutine(ContinuousClearEnemiesUntilWaveStart());
                        }
                        demoChallengeStartPosition = Vector3.zero;
                        StartCoroutine(WaitForLevelInitializedThenSetup(scene));
                    }
                }
                else if (bossRushArenaPlanned)
                {
                    string targetSubScene = BossRushMapSelectionHelper.GetPendingTargetSubSceneName();
                    string targetMainScene = BossRushMapSelectionHelper.GetPendingMainSceneName();
                    Vector3? customPos = BossRushMapSelectionHelper.GetPendingCustomPosition();

                    if (targetSubScene != null && scene.name == targetSubScene && customPos.HasValue)
                    {
                        BossRushMapSelectionHelper.MarkTargetSceneLoadStarted();
                        bossRushArenaPlanned = false;
                        StartCoroutine(TeleportPlayerToCustomPosition(customPos.Value));
                        BossRushMapSelectionHelper.ClearPendingMapEntry();
                    }
                    else if (scene.name.Contains("Loading") || scene.name.Contains("Menu") ||
                             (targetMainScene != null && scene.name == targetMainScene))
                    {
                    }
                    else if (targetSubScene == "Level_StormZone_B0" && scene.name == "Level_StormZone_1" && customPos.HasValue)
                    {
                        StartCoroutine(ForceTeleportToSubScene(targetSubScene, customPos.Value));
                    }
                    else if (targetSubScene == "Level_SnowMilitaryBase_ColdStorage" && scene.name == "Level_SnowMilitaryBase" && customPos.HasValue)
                    {
                        StartCoroutine(ForceTeleportToSubScene(targetSubScene, customPos.Value));
                    }
                    else
                    {
                        bossRushArenaPlanned = false;
                        BossRushMapSelectionHelper.ClearPendingMapEntry();
                        BossRushMapSelectionHelper.ClearPendingEntryFlowState();
                    }
                }
                else
                {
                    if (IsActive || bossRushArenaActive)
                    {
                        // Mode G 分流（加法分支，默认惰性）：Mode G Starting/Active/Rewarding/Exiting
                        // 期间不清 bossRushOwnedDaXingXing、不调三个 Legacy Boss arena-exit cleanup、
                        // 不清 Mode G 可能登记的共享引用；只清 Legacy 计数/UI。
                        // 场景事实交给 Mode G 的 EndModeGRunAsync(SceneChanged)（Felix 侧）。
                        // 查询 no-throw；无 Mode G run 时条件恒 false，逐字保持原块。
                        bool modeGRunActiveNow = false;
                        try
                        {
                            modeGRunActiveNow = ModeGRuntimeGates.IsModeGRunInProgress;
                        }
                        catch
                        {
                            modeGRunActiveNow = false;
                        }

                        waitingForNextWave = false;
                        waveCountdown = 0f;
                        lastWaveCountdownSeconds = -1;
                        statusMessage = string.Empty;
                        messageTimer = 0f;
                        currentWaveBosses.Clear();
                        bossesInCurrentWaveTotal = 0;
                        bossesInCurrentWaveRemaining = 0;
                        currentEnemyIndex = 0;
                        defeatedEnemies = 0;
                        totalEnemies = 0;

                        if (!modeGRunActiveNow)
                        {
                            bossRushOwnedDaXingXing.Clear();
                            CleanupDragonDescendant();
                            CleanupTrackedDragonKingsOnArenaExit();
                            if (dragonKingSetBonusRegistered)
                            {
                                try { Health.OnHurt -= OnDragonKingBossHurt; } catch { }
                                dragonKingSetBonusRegistered = false;
                            }
                            activeDragonKingHealths.Clear();
                            CleanupPhantomWitchTrackedStateOnArenaExit();
                        }

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
                        catch { }

                        SetBossRushRuntimeActive(false);
                        bossRushArenaActive = false;
                        bossRushArenaPlanned = false;
                        currentBoss = null;
                        DestroyCommonNPCs("LeaveBossRushScene");
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
                                catch { }
                                ammoShop = null;
                            }
                        }
                        catch { }

                        Health.OnDead -= OnEnemyDiedWithDamageInfo;

                        if (modeDActive)
                        {
                            EndModeD();
                        }

                        if (modeEActive)
                        {
                            EndModeE();
                        }
                    }

                    if (ShouldSpawnCommonNPCsInScene(scene.name))
                    {
                        StartCoroutine(DelayedSpawnCommonNPCsInNormalMode(scene.name));
                    }

                    ScheduleRestoreFollowingSpouse(scene.name, "NormalSceneLoaded");

                    if (isGameplayScene)
                    {
                        StartCoroutine(FindInteractionTargets(10));
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] OnSceneLoaded_Integration failed: scene=" + scene.name + ", " + e.Message);
            }
        }

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
