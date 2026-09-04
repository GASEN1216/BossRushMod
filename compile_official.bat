@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ===================================
echo Boss Rush Mod - Compile (Official)
echo ===================================
echo.

set OUTPUT_DIR=Build
set MOD_NAME=BossRush
set "BOSSRUSH_DEFINE_ARGS="
if defined BOSSRUSH_DEV_BUILD (
    set "BOSSRUSH_DEFINE_ARGS=/define:BOSSRUSH_DEV"
    echo [DEV] BOSSRUSH_DEV enabled: DevLog calls will be compiled in.
)
:: HarmonyLoadMod (Workshop ID: 3588386576)
set HARMONY_MOD_ID=3588386576
call :ensure_game_path
if not defined GAME_PATH (
    echo [FAIL] GAME_PATH was not found.
    echo        Set GAME_PATH to your Escape from Duckov install root, e.g.
    echo        set "GAME_PATH=E:\SteamLibrary\steamapps\common\Escape from Duckov"
    if not defined BOSSRUSH_NO_PAUSE pause
    exit /b 1
)
call :ensure_workshop_path
if not defined WORKSHOP_PATH (
    echo [FAIL] WORKSHOP_PATH was not found.
    echo        Set WORKSHOP_PATH to the Steam workshop content root containing %HARMONY_MOD_ID%\0Harmony.dll, e.g.
    echo        set "WORKSHOP_PATH=E:\SteamLibrary\steamapps\workshop\content\3167020"
    if not defined BOSSRUSH_NO_PAUSE pause
    exit /b 1
)
echo GAME_PATH=%GAME_PATH%
echo WORKSHOP_PATH=%WORKSHOP_PATH%

:: Create output directory
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

echo Compiling %MOD_NAME%.dll...
echo.

:: Find .NET SDK Roslyn compiler (C# 7.3)
for /f "delims=" %%i in ('dir /b /ad /o-n "C:\Program Files\dotnet\sdk" 2^>nul') do (
    set "DOTNET_SDK=C:\Program Files\dotnet\sdk\%%i"
    goto :found_sdk
)
:found_sdk
if not defined DOTNET_SDK (
    echo [FAIL] .NET SDK was not found under C:\Program Files\dotnet\sdk.
    echo        Install the .NET SDK or adjust compile_official.bat to point at csc.dll.
    if not defined BOSSRUSH_NO_PAUSE pause
    exit /b 1
)
if not exist "%DOTNET_SDK%\Roslyn\bincore\csc.dll" (
    echo [FAIL] Roslyn compiler was not found: "%DOTNET_SDK%\Roslyn\bincore\csc.dll"
    if not defined BOSSRUSH_NO_PAUSE pause
    exit /b 1
)
:: ---------------------------------------------------------------------------
:: 编译参数改经 Roslyn 响应文件传递(csc @file),不再拼成一条超长命令行.
:: 原因:源码清单增长到约 690 个文件后,展开后的命令行超过进程创建上限,csc 根本
:: 不会被启动,只报 "The system cannot execute the specified program" 并以 60 退出,
:: 看起来像编译失败,却没有任何 C# 错误行,极难排查.响应文件没有这个长度限制.
:: 写法要点(都踩过坑,勿随手改):
::   1) 用[括号块 + 块尾一次重定向].不要用行首重定向(>>"file" echo ...),
::      它在本机 cmd 上直接报 ERROR_INVALID_NAME(123);也不要用行尾重定向,
::      那会让 .cs 后面跟上 >>,OfficialCompileListFileExistenceGuard 的正则就扫不到.
::   2) 用 echo( 而不是 echo:未开启 DEV 时 %BOSSRUSH_DEFINE_ARGS% 为空,
::      echo 会写出 "ECHO is on." 污染响应文件,echo( 则正确写出空行.
::   3) 块内含括号的路径(如 C:\Program Files (x86)\...)已实测安全,因为在引号内.
:: 源码清单仍逐条显式列出(不用通配符),编译清单守卫照常双向生效.
:: ---------------------------------------------------------------------------
(
echo(/langversion:7.3
echo(%BOSSRUSH_DEFINE_ARGS%
echo(/target:library
echo(/out:"%OUTPUT_DIR%\%MOD_NAME%.dll"
echo(/lib:"%GAME_PATH%\Duckov_Data\Managed"
echo(/reference:UnityEngine.dll
echo(/reference:UnityEngine.CoreModule.dll
echo(/reference:UnityEngine.PhysicsModule.dll
echo(/reference:UnityEngine.UI.dll
echo(/reference:UnityEngine.JSONSerializeModule.dll
echo(/reference:UnityEngine.AIModule.dll
echo(/reference:UnityEngine.AudioModule.dll
echo(/reference:UnityEngine.UnityWebRequestWWWModule.dll
echo(/reference:UnityEngine.UnityWebRequestModule.dll
echo(/reference:UnityEngine.UnityWebRequestAudioModule.dll
echo(/reference:Eflatun.SceneReference.dll
echo(/reference:Assembly-CSharp.dll
echo(/reference:UnityEngine.UIModule.dll
echo(/reference:UnityEngine.InputLegacyModule.dll
echo(/reference:UnityEngine.IMGUIModule.dll
echo(/reference:UnityEngine.ImageConversionModule.dll
echo(/reference:UnityEngine.TextRenderingModule.dll
echo(/reference:UnityEngine.AssetBundleModule.dll
echo(/reference:UnityEngine.AnimationModule.dll
echo(/reference:UnityEngine.ParticleSystemModule.dll
echo(/reference:UnityEngine.VideoModule.dll
echo(/reference:Unity.TextMeshPro.dll
echo(/reference:ItemStatsSystem.dll
echo(/reference:UniTask.dll
echo(/reference:Sirenix.OdinInspector.Attributes.dll
echo(/reference:TeamSoda.Duckov.Core.dll
echo(/reference:TeamSoda.Duckov.Utilities.dll
echo(/reference:AstarPathfindingProject.dll
echo(/reference:PackageTools.dll
echo(/reference:Plugins.dll
echo(/reference:Drawing.dll
echo(/reference:SodaLocalization.dll
echo(/reference:NodeCanvas.dll
echo(/reference:ParadoxNotion.dll
echo(/reference:System.Core.dll
echo(/reference:System.dll
echo(/reference:mscorlib.dll
echo(/reference:netstandard.dll
echo(/reference:"%WORKSHOP_PATH%\%HARMONY_MOD_ID%\0Harmony.dll"
echo(/nowarn:CS0436,CS0162,CS0414
echo(Localization\L10n.cs
echo(Localization\LocalizationHelper.cs
echo(Localization\LocalizationInjector.cs
echo(Localization\LocalizationInjector_NpcUiAndItems.cs
echo(Localization\EquipmentLocalization.cs
echo(Localization\ModeHLocalization.cs
echo(Localization\PetNestLocalization.cs
echo(Localization\RandomEventsLocalization.cs
echo(Localization\DailyReportLocalization.cs
echo(Common\Lifecycle\IBossRushRuntimeModule.cs
echo(Common\Lifecycle\SceneRuntimeContext.cs
echo(Common\Lifecycle\BossRushRuntimeModuleHost.cs
echo(Common\Lifecycle\BossRushRuntimeModuleBase.cs
echo(Common\Lifecycle\ArchitectureSentinelRuntimeModule.cs
echo(Common\Lifecycle\BossRushRuntimeModuleRegistration.cs
echo(Common\Lifecycle\BossRushSaveFileThrottle.cs
echo(Common\Events\BossRushEventBus.cs
echo(Common\Infrastructure\BossRushEagerReflectionCache.cs
echo(Common\UI\BossRushUI.cs
echo(Common\Infrastructure\ObjectCache.cs
echo(Common\Infrastructure\IHarmonyPatchGroup.cs
echo(Common\Infrastructure\HarmonyPatchGroupRegistrar.cs
echo(Common\Infrastructure\HarmonyBindingSelfCheck.cs
echo(Common\Data\JsonDataRegistry.cs
echo(Common\MapConfig\BossRushMapConfig.cs
echo(Common\MapConfig\MapSpawnPointRegistry.cs
echo(Common\Utils\ReflectionCache.cs
echo(Common\Utils\NPCBubbleAnimator.cs
echo(Common\Effects\RingParticleEffect.cs
echo(Common\Effects\MeleeWeaponFxPolicy.cs
echo(Common\Equipment\EquipmentAbilityConfig.cs
echo(Common\Equipment\EquipmentAbilityAction.cs
echo(Common\Equipment\EquipmentAbilityManager.cs
echo(Common\Equipment\EquipmentEffectManager.cs
echo(Common\Equipment\AbilitySystemHelper.cs
echo(Common\Stats\RuntimeStatModifierTracker.cs
echo(ModBehaviour.cs
echo(ModConfigApi.cs
echo(UIAndSigns\UIAndSigns.cs
echo(UIAndSigns\BossRushInteractionScan.cs
echo(UIAndSigns\UIAndSignsRuntimeBridges.cs
echo(DebugAndTools\DebugAndTools.cs
echo(DebugAndTools\DebugAndToolsPlacementAndInspection.cs
echo(DebugAndTools\DebugAndToolsStaticCacheReset.cs
echo(DebugAndTools\DebugToolsRuntimeModule.cs
echo(DebugAndTools\DebugToolsRuntimeHooks.cs
echo(DebugAndTools\MarriageTestDebugUI.cs
echo(DebugAndTools\PermanentDuckNpcDebug.cs
echo(DebugAndTools\ItemSpawner.cs
echo(DebugAndTools\F3DebugCheatMenu.cs
echo(DebugAndTools\F3DebugCheatMenuUi.cs
echo(DebugAndTools\F3DebugCheatMenuPlayerStats.cs
echo(DebugAndTools\F3DebugCheatMenuActions.cs
echo(DebugAndTools\F3GameplayValidationRunner.cs
echo(DebugAndTools\F3GameplayValidationCoverage.cs
echo(DebugAndTools\F3GameplayValidationItems.cs
echo(DebugAndTools\F3GameplayValidationZombie.cs
echo(DebugAndTools\F3GameplayValidationModeHKits.cs
echo(DebugAndTools\F3GameplayValidationModeHErrorSwap.cs
echo(DebugAndTools\F3GameplayValidationScenes.cs
echo(DebugAndTools\F3GameplayValidationStages.cs
echo(DebugAndTools\F3GameplayValidationModes.cs
echo(DebugAndTools\F3GameplayValidationDiagnostics.cs
echo(DebugAndTools\F3GameplayValidationRandomEvents.cs
echo(DebugAndTools\F3GameplayValidationCodex.cs
echo(DebugAndTools\F3GameplayValidationPersistence.cs
echo(DebugAndTools\F3GameplayValidationBackMountain.cs
echo(DebugAndTools\F3GameplayValidationEconomy.cs
echo(DebugAndTools\F3GameplayValidationDepth.cs
echo(DebugAndTools\F3GameplayValidationDeepFlows.cs
echo(DebugAndTools\F3GameplayValidationLeaks.cs
echo(DebugAndTools\NPCTeleportUI.cs
echo(Integration\BossRushDynamicItemRegistry.cs
echo(Integration\BossRushIntegration.cs
echo(Integration\BossRushIntegration_StartAndScene.cs
echo(Integration\IntegrationDeferredBootstrap.cs
echo(Integration\BossRushIntegration_TravelAndSetup.cs
echo(Integration\BossRushIntegration_MapObjectsAndDragonBreath.cs
echo(Integration\Mutators\MutatorDefinitions.cs
echo(Integration\Mutators\MutatorManager.cs
echo(Integration\Mutators\MutatorUI.cs
echo(Integration\Mutators\MutatorRuntimeBridge.cs
echo(Integration\ZombieModeIntegration.cs
echo(Integration\DeathWraith\DeathWraithSystem.cs
echo(Integration\DeathWraith\DeathWraithRecording.cs
echo(Integration\DeathWraith\DeathWraithOriginalDeadBodyBridge.cs
echo(Integration\DeathWraith\DeathWraithSpawnFlow.cs
echo(Integration\DeathWraith\DeathWraithCombatLoadout.cs
echo(Integration\DeathWraith\DeathWraithLifecycleAndPersistence.cs
echo(Patches\BaseHub\BaseHubShopAwakePatch.cs
echo(Patches\BaseHub\BaseHubBoatPatch.cs
echo(Patches\BaseHub\BaseHubPatchGroup.cs
echo(Patches\Combat\CharacterOnDeadPatch.cs
echo(Patches\Combat\BossLethalHealthProtectionPatch.cs
echo(Patches\Combat\ProjectileHalfObstaclePatch.cs
echo(Patches\Combat\CombatPatchGroup.cs
echo(Patches\Death\DeadBodyAppendPatch.cs
echo(Patches\Death\DeadBodySpawnPatch.cs
echo(Patches\Death\TombLootboxPatch.cs
echo(Patches\Death\DeadBodyTouchedPatch.cs
echo(Patches\Death\DeathPatchGroup.cs
echo(Patches\Economy\StockShopGetItemInstanceDirectPatch.cs
echo(Patches\ItemStatsSystem\ItemAssetsCollectionDynamicRegistrationPatch.cs
echo(Patches\UI\ItemUIUtilitiesElementFactorFormatPatch.cs
echo(Patches\Compatibility\MagicBlendInitializationOrderPatch.cs
echo(Integration\BirthdayCakeItem.cs
echo(Integration\EquipmentFactory.cs
echo(Integration\EquipmentFactory_ItemProcessing.cs
echo(Integration\EquipmentFactoryStaticCacheReset.cs
echo(Integration\EquipmentContentRegistry.cs
echo(Integration\EquipmentRuntimeHooks.cs
echo(Integration\IntegrationRuntimeHooks.cs
echo(Integration\EquipmentHelper.cs
echo(Integration\EquipmentHelperIcon.cs
echo(Integration\Bonus\DragonSetBonus.cs
echo(Integration\Bonus\DragonSetBonus_Dash.cs
echo(Integration\Bonus\SetBonusManager.cs
echo(Integration\Bonus\FrostSetBonus.cs
echo(Integration\Bonus\ThunderSetBonus.cs
echo(Integration\Bonus\SetBonusPlaceholderRegistry.cs
echo(Integration\Config\DragonSetConfig.cs
echo(Integration\Config\FlightTotemConfig.cs
echo(Integration\Config\DragonKingSetConfig.cs
echo(Integration\Config\FrostThunderSetConfig.cs
echo(Utilities\Utilities.cs
echo(Utilities\AlwaysOnRuntimeHooks.cs
echo(Utilities\PlayerLifecycleRuntimeHooks.cs
echo(Utilities\EntityModelFactory.cs
echo(Utilities\SimpleJsonHelper.cs
echo(Utilities\AwenLootSweepMath.cs
echo(Utilities\VictoryRewardShadowMath.cs
echo(Utilities\F3DebugCheatMath.cs
echo(Utilities\ManagedBossSpawnContracts.cs
echo(Utilities\ModBossPresetLookup.cs
echo(Utilities\SpawnedEnemyActivationHelper.cs
echo(Utilities\EnemySpawnCore.cs
echo(Utilities\ZombieSpawnSanitizer.cs
echo(Utilities\EnemyRecoveryMonitor.cs
echo(Utilities\GameplayRuntimeHooks.cs
echo(Utilities\ModeRuntimeHooks.cs
echo(Utilities\SpawnPositionHelper.cs
echo(Utilities\RunScopedRegistry.cs
echo(Utilities\RuntimeScope.cs
echo(Utilities\SceneRuntimeGate.cs
echo(Utilities\SafeRuntime.cs
echo(Utilities\SteamHelper.cs
echo(Utilities\BossCleanupHelpers.cs
echo(Utilities\InteractableLootboxInventoryHelper.cs
echo(Utilities\OriginalCharacterIsolationHelper.cs
echo(Utilities\OriginalExtractionPointIsolationHelper.cs
echo(Utilities\ModeExtractionPointFactory.cs
echo(Utilities\MapSelectionEntryInjectionHelper.cs
echo(Config\Config.cs
echo(Config\ConfigModeG.cs
echo(Config\NPCSpawnConfig.cs
echo(Config\LootBlacklistRegistry.cs
echo(WavesArena\WavesArena.cs
echo(WavesArena\WavesArenaEntryAndTeleport.cs
echo(WavesArena\WavesArenaBossSpawning.cs
echo(WavesArena\WavesArenaRuntimeModule.cs
echo(WavesArena\WavesArenaRuntimeHooks.cs
echo(WavesArena\BossRushEntryFlow.cs
echo(WavesArena\WavesArenaEnemyMaintenance.cs
echo(WavesArena\WavesArenaSpawnerControl.cs
echo(LootAndRewards\LegacyBossLootProbabilityModel.cs
echo(LootAndRewards\LootAndRewards.cs
echo(LootAndRewards\LootAndRewardsStaticCacheReset.cs
echo(LootAndRewards\LootAndRewardsInfiniteHell.cs
echo(LootAndRewards\LootAndRewardsVictoryRewards.cs
echo(LootAndRewards\LootAndRewardsRandomBossLoot.cs
echo(LootAndRewards\LootAndRewardsSpecialLoot.cs
echo(LootAndRewards\LootAndRewardsRuntimeHooks.cs
echo(LootAndRewards\VictoryRewardShadowCrateController.cs
echo(LootAndRewards\ModeEFLootboxTracker.cs
echo(Interactables\BossRushInteractables.cs
echo(Interactables\BossRushLootboxInteractables.cs
echo(ModeD\ModeD.cs
echo(ModeD\ModeDStaticCacheReset.cs
echo(ModeD\ModeDRuntimeModule.cs
echo(ModeD\ModeDEquipment.cs
echo(ModeD\ModeDEquipment_StarterKit.cs
echo(ModeD\ModeDWaves.cs
echo(ModeD\ModeDInteractables.cs
echo(ModeD\ModeDGlobalLoot.cs
echo(ModeD\ModeDGlobalLootStaticCacheReset.cs
echo(ModeE\ModeE.cs
echo(ModeE\ModeEUiAndHealthBars.cs
echo(ModeE\ModeEStartup.cs
echo(ModeE\ModeELifecycle.cs
echo(ModeE\ModeEIntegrityAndHelpers.cs
echo(ModeE\ModeERuntimeModule.cs
echo(ModeE\ModeERuntimeHooks.cs
echo(ModeE\ModeEMerchant.cs
echo(ModeE\ModeEMerchantSupportClasses.cs
echo(ModeE\ModeELotteryAndHiring.cs
echo(ModeE\ModeESpawnAllocation.cs
echo(ModeE\ModeEBattle.cs
echo(ModeE\ModeEBattle_ScalingAndRuntime.cs
echo(ModeE\FactionFlagConfig.cs
echo(ModeE\ModeEHarmonyPatch.cs
echo(ModeE\RespawnItemConfig.cs
echo(ModeE\ModeERespawnItems.cs
echo(ModeE\RespawnItemUsage.cs
echo(ModeF\ModeFModels.cs
echo(ModeF\ModeFRuntimeModule.cs
echo(ModeF\ModeFRuntimeHooks.cs
echo(ModeF\ModeFEntry.cs
echo(ModeF\ModeFPhases.cs
echo(ModeF\ModeFBloodfire.cs
echo(ModeF\ModeFBounty.cs
echo(ModeG\ModeGEntry.cs
echo(ModeG\ModeGAvailability.cs
echo(ModeG\ModeGDeterministicRandom.cs
echo(ModeG\ModeGStateModel.cs
echo(ModeG\ModeGRunState.cs
echo(ModeG\ModeGWavePlan.cs
echo(ModeG\ModeGAdaptiveCombat.cs
echo(ModeG\ModeGCombatTelemetry.cs
echo(ModeG\ModeGWeaponScoringCompatibilityMatrix.cs
echo(ModeG\ModeGDeathRouting.cs
echo(ModeG\ModeGNemesisPersistence.cs
echo(ModeG\ModeGProfilePersistence.cs
echo(ModeG\ModeGPersistenceFlushCoordinator.cs
echo(ModeG\ModeGRewardTransaction.cs
echo(ModeG\ModeGMapSupportRegistry.cs
echo(ModeG\ModeGFateContract.cs
echo(ModeG\ModeGEncounterVariation.cs
echo(ModeG\ModeGPresentationAssetCache.cs
echo(ModeG\ModeGSpawnTransaction.cs
echo(ModeG\ModeGCleanupController.cs
echo(ModeG\ModeGRuntimeModule.cs
echo(ModeG\ModeGRuntimeModule_PublicApiAndShutdown.cs
echo(ModeG\ModeGRuntimeLifecycle.cs
echo(ModeG\ModeGRuntimeBridge.cs
echo(ModeG\ModeGHUD.cs
echo(ModeG\ModeGRecapPanel.cs
echo(ModeG\ModeGInteractable.cs
echo(ModeH\ModeHArenaIsolationLease.cs
echo(ModeH\ModeHAvailability.cs
echo(ModeH\ModeHBattleSnapshot.cs
echo(ModeH\ModeHCanonicalDigest.cs
echo(ModeH\ModeHCombatControl.cs
echo(ModeH\ModeHCombatTelemetry.cs
echo(ModeH\ModeHCommandAdapters.cs
echo(ModeH\ModeHCommandCompatibilityRegistry.cs
echo(ModeH\ModeHCommandController.cs
echo(ModeH\ModeHConfig.cs
echo(ModeH\ModeHContentCatalog.cs
echo(ModeH\ModeHContentCatalogParsers.cs
echo(ModeH\ModeHContentModels.cs
echo(ModeH\ModeHControlPointHarness.cs
echo(ModeH\ModeHDeathSuppressionRegistry.cs
echo(ModeH\ModeHDraftController.cs
echo(ModeH\ModeHEncounterPlanner.cs
echo(ModeH\ModeHEntry.cs
echo(ModeH\ModeHEventRouter.cs
echo(ModeH\ModeHHallOfFamePersistence.cs
echo(ModeH\ModeHHarmonyPatches.cs
echo(ModeH\ModeHInjuryAndScarSystem.cs
echo(ModeH\ModeHInteractable.cs
echo(ModeH\ModeHInventoryPersistenceBridge.cs
echo(ModeH\ModeHItemTreeNormalizer.cs
echo(ModeH\ModeHJsonValue.cs
echo(ModeH\ModeHLoadoutKitApplicator.cs
echo(ModeH\ModeHLoadoutKitRegistry.cs
echo(ModeH\ModeHMapSupportRegistry.cs
echo(ModeH\ModeHOddsController.cs
echo(ModeH\ModeHPresentationAssetCache.cs
echo(ModeH\ModeHPresetRegistry.cs
echo(ModeH\ModeHProductionCertification.cs
echo(ModeH\ModeHCommandCertificationProbe.cs
echo(ModeH\ModeHProfilePersistence.cs
echo(ModeH\ModeHStakeJournalPersistence.cs
echo(ModeH\ModeHRealStakeService.cs
echo(ModeH\ModeHProfileRegistry.cs
echo(ModeH\ModeHRecoveryPanel.cs
echo(ModeH\ModeHRewardItemPool.cs
echo(ModeH\ModeHRewardTransaction.cs
echo(ModeH\ModeHRunState.cs
echo(ModeH\ModeHRuntimeGates.cs
echo(ModeH\ModeHRuntimeModule.cs
echo(ModeH\ModeHRuntimeModule_SceneFlow.cs
echo(ModeH\ModeHRuntimeModule_UiFlow.cs
echo(ModeH\ModeHRuntimeModule_MatchFlow.cs
echo(ModeH\ModeHRuntimeModule_CombatFlow.cs
echo(ModeH\ModeHRuntimeModule_SettlementFlow.cs
echo(ModeH\ModeHRuntimeModule_CombatProfiles.cs
echo(ModeH\ModeHRuntimeModule_SeasonFlow.cs
echo(ModeH\ModeHSaveFlushCoordinator.cs
echo(ModeH\ModeHSeasonRewardService.cs
echo(ModeH\ModeHSeedStream.cs
echo(ModeH\ModeHSpawnBridge.cs
echo(ModeH\ModeHSpawnTransaction.cs
echo(ModeH\ModeHSpectatorLease.cs
echo(ModeH\ModeHStandInPerformer.cs
echo(ModeH\ModeHStateDtos.cs
echo(ModeH\ModeHStateMachine.cs
echo(ModeH\ModeHStateModel.cs
echo(ModeH\ModeHTransferMarket.cs
echo(ModeH\ModeHUI.cs
echo(ModeH\ModeHUIPages.cs
echo(ModeH\ModeHVirtualStakeController.cs
echo(ModeH\ModeHWarehouseStakeJournal.cs
echo(ModeH\ModeHWarehouseStakeJournalStorageBuffer.cs
echo(ModeF\ModeFBounty_EquipmentAndLoot.cs
echo(ModeF\ModeFRespawn.cs
echo(ModeF\ModeFExtraction.cs
echo(ModeF\ModeFFortifications.cs
echo(ModeF\ModeFFortifications_RuntimePlacement.cs
echo(ModeF\ModeFFortifications_RepairRewardsCleanup.cs
echo(ModeF\ModeFItemUsageAndTriggers.cs
echo(ModeF\ModeFUIStaticCacheReset.cs
echo(ModeF\ModeFUI.cs
echo(ModeF\ModeFUI_BountyRadarAndHealthBars.cs
echo(ModeF\ModeFUI_KillRewardBubble.cs
echo(ModeF\ModeFUI_BountyRadarAssets.cs
echo(ModeF\ModeFMerchant.cs
echo(ZombieMode\ZombieModeModels.cs
echo(ZombieMode\ZombieModeTuning.cs
echo(ZombieMode\ZombieModeRuntimeModule.cs
echo(ZombieMode\ZombieModeRuntimeHooks.cs
echo(ZombieMode\ZombieModeEntry.cs
echo(ZombieMode\ZombieModeEntry_StarterLoadout.cs
echo(ZombieMode\ZombieModeMapSelection.cs
echo(ZombieMode\ZombieModeMapSelectionHelper.cs
echo(ZombieMode\ZombieModeInventoryTransfer.cs
echo(ZombieMode\ZombieModeMapIsolation.cs
echo(ZombieMode\ZombieModePollution.cs
echo(ZombieMode\ZombieModePollution_RuntimeSkills.cs
echo(ZombieMode\ZombieModePollution_RuntimeComponents.cs
echo(ZombieMode\ZombieModeBossController.cs
echo(ZombieMode\ZombieModePlayerSlowRuntime.cs
echo(ZombieMode\ZombieModeSpawner.cs
echo(ZombieMode\ZombieModeWaveController.cs
echo(ZombieMode\ZombieModeEnemyRuntime.cs
echo(ZombieMode\ZombieModeRewards.cs
echo(ZombieMode\ZombieModeRewardCatalogAndSelection.cs
echo(ZombieMode\ZombieModeRewardPreparationDuration.cs
echo(ZombieMode\ZombieModeRewardEffectsAndNpc.cs
echo(ZombieMode\ZombieModeBackpackJunkRecycle.cs
echo(ZombieMode\ZombieModeRewardItemGrants.cs
echo(ZombieMode\ZombieModeRewardNpcServices.cs
echo(ZombieMode\ZombieModeRewardEffects.cs
echo(ZombieMode\ZombieModeRewardOptionCore.cs
echo(ZombieMode\ZombieModeRewardProjectileSpread.cs
echo(ZombieMode\ZombieModeRewardRuntimeModifiers.cs
echo(ZombieMode\ZombieModeRewardTriggerEffects.cs
echo(ZombieMode\ZombieModeRewardProjectilePatch.cs
echo(ZombieMode\ZombieModeDropsAndPerformance.cs
echo(ZombieMode\ZombiePurificationPointController.cs
echo(ZombieMode\ZombieModeSafeZoneController.cs
echo(ZombieMode\ZombieModeExtractionController.cs
echo(ZombieMode\ZombieModeHudController.cs
echo(ZombieMode\ZombieModeUIHelper.cs
echo(ZombieMode\ZombieModeCleanup.cs
echo(ZombieMode\ZombieModeDebug.cs
echo(ZombieMode\ZombieModeNpcCatalog.cs
echo(ZombieMode\ZombieModeCashInvestmentView.cs
echo(BossFilter\BossFilter.cs
echo(BossFilter\BossFilterUi.cs
echo(MapSelection\BossRushMapSelectionHelper.cs
echo(MapSelection\MapThumbnailCache.cs
echo(Integration\DragonDescendant\DragonDescendantConfig.cs
echo(Integration\DragonDescendant\DragonDescendantAbilities.cs
echo(Integration\DragonDescendant\DragonDescendantAbilities_ProjectilesAndGrenades.cs
echo(Integration\DragonDescendant\DragonDescendantAbilities_ResurrectionAndPhase.cs
echo(Integration\DragonDescendant\DragonDescendantAbilities_Phase2Combat.cs
echo(Integration\DragonDescendant\DragonDescendantAbilities_CollisionAndIce.cs
echo(Integration\DragonDescendant\DragonDescendantBoss.cs
echo(Integration\DragonDescendant\DragonDescendantBoss_RuntimeAndCleanup.cs
echo(Integration\DragonDescendant\DragonDescendantBossStaticCacheReset.cs
echo(Integration\DragonDescendant\DragonDescendantBoss_ModeGAdapter.cs
echo(Integration\DragonDescendant\DragonBreathConfig.cs
echo(Integration\DragonDescendant\DragonBreathBuffHandler.cs
echo(Integration\DragonDescendant\DragonBreathWeaponConfig.cs
echo(Integration\DragonDescendant\DragonBreathWeaponConfig_FireEffects.cs
echo(Integration\DragonKing\DragonKingConfig.cs
echo(Integration\DragonKing\DragonKingAssetManager.cs
echo(Integration\DragonKing\DragonKingAbilityController.cs
echo(Integration\DragonKing\DragonKingAbilityController_AttackFlow.cs
echo(Integration\DragonKing\DragonKingAbilityController_ProjectileAndMovement.cs
echo(Integration\DragonKing\DragonKingAbilityController_SpecialAttacks.cs
echo(Integration\DragonKing\DragonKingAbilityController_ChildProtection.cs
echo(Integration\DragonKing\DragonKingAbilityHelpers.cs
echo(Integration\DragonKing\DragonKingShockwaveEffect.cs
echo(Integration\DragonKing\DragonKingBoss.cs
echo(Integration\DragonKing\DragonKingBoss_ModeGAdapter.cs
echo(Integration\DragonKing\Weapons\FenHuangHalberdIds.cs
echo(Integration\DragonKing\Weapons\FenHuangHalberdConfig.cs
echo(Integration\DragonKing\Weapons\FenHuangHalberdRuntime.cs
echo(Integration\DragonKing\Weapons\DragonFlameMarkTracker.cs
echo(Integration\DragonKing\Weapons\DragonKingBossGunConfig.cs
echo(Integration\DragonKing\Weapons\DragonKingBossGunProfiles.cs
echo(Integration\DragonKing\Weapons\DragonKingBossGunProjectileAgent.cs
echo(Integration\DragonKing\Weapons\DragonKingBossGunProjectileZones.cs
echo(Integration\DragonKing\Weapons\DragonKingBossGunRuntime.cs
echo(Integration\DragonKing\Weapons\DragonKingBossGunRuntime_ProjectilesAndPatches.cs
echo(Integration\DragonKing\Weapons\DragonKingBossGunRuntimeStaticCacheReset.cs
echo(Integration\DragonKing\Weapons\FenHuangHalberdAction.cs
echo(Integration\DragonKing\Weapons\FenHuangHalberdAbilityManager.cs
echo(Integration\DragonKing\Weapons\FenHuangComboManager.cs
echo(Integration\DragonKing\Weapons\FenHuangComboPatchesAndFx.cs
echo(Integration\DragonKing\Weapons\FenHuangHalberdBootstrap.cs
echo(Integration\DragonKing\Weapons\FenHuangHalberdWeaponConfig.cs
echo(Integration\PhantomWitch\PhantomWitchConfig.cs
echo(Integration\PhantomWitch\PhantomWitchPerformancePolicy.cs
echo(Integration\PhantomWitch\PhantomWitchAmbientPresence.cs
echo(Integration\PhantomWitch\PhantomWitchVfxRedesign.cs
echo(Integration\PhantomWitch\PhantomWitchVfxRedesign_EmittersAndTextures.cs
echo(Integration\PhantomWitch\PhantomWitchVfxRedesign_RuntimeComponents.cs
echo(Integration\PhantomWitch\PhantomWitchVfxRedesignStaticCacheReset.cs
echo(Integration\PhantomWitch\PhantomWitchAssetManager.cs
echo(Integration\PhantomWitch\PhantomWitchAssetManager_RuntimeComponents.cs
echo(Integration\PhantomWitch\PhantomWitchAbilityController.cs
echo(Integration\PhantomWitch\PhantomWitchAbilityController_PackageScheduler.cs
echo(Integration\PhantomWitch\PhantomWitchAbilityController_StealthAndAttacks.cs
echo(Integration\PhantomWitch\PhantomWitchAbilityController_Minions.cs
echo(Integration\PhantomWitch\PhantomWitchAbilityController_RuntimeTicks.cs
echo(Integration\PhantomWitch\PhantomWitchAbilityController_PhaseAndLifecycle.cs
echo(Integration\PhantomWitch\PhantomWitchAbilityController_MovementAndDamage.cs
echo(Integration\PhantomWitch\PhantomWitchAbilityController_CleanupAndTelemetry.cs
echo(Integration\PhantomWitch\PhantomWitchBossCurseRealmRuntime.cs
echo(Integration\PhantomWitch\PhantomWitchBoss.cs
echo(Integration\PhantomWitch\PhantomWitchBoss_ModeGAdapter.cs
echo(Integration\PhantomWitch\PhantomWitchScytheIds.cs
echo(Integration\PhantomWitch\PhantomWitchScytheConfig.cs
echo(Integration\PhantomWitch\PhantomWitchScytheSwingFx.cs
echo(Integration\PhantomWitch\PhantomWitchScytheWeaponConfig.cs
echo(Integration\PhantomWitch\PhantomWitchScytheAction.cs
echo(Integration\PhantomWitch\PhantomWitchScytheAction_RuntimeComponents.cs
echo(Integration\PhantomWitch\PhantomWitchScytheAbilityManager.cs
echo(Integration\PhantomWitch\PhantomWitchCurseSweatVfx.cs
echo(Integration\PhantomWitch\PhantomWitchScytheBootstrap.cs
echo(Integration\Frostmourne\FrostmourneIds.cs
echo(Integration\Frostmourne\FrostmourneConfig.cs
echo(Integration\Frostmourne\FrostmourneWeaponConfig.cs
echo(Integration\Frostmourne\FrostmourneSwingFx.cs
echo(Integration\Frostmourne\FrostmourneAction.cs
echo(Integration\Frostmourne\FrostmourneAbilityManager.cs
echo(Integration\Frostmourne\FrostmourneBootstrap.cs
echo(Integration\NewWeapons\Common\NewWeaponIds.cs
echo(Integration\NewWeapons\Common\NewWeaponBootstrap.cs
echo(Integration\NewWeapons\Common\NewWeaponPlaceholderRegistry.cs
echo(Integration\NewWeapons\ViperDagger\ViperDaggerConfig.cs
echo(Integration\NewWeapons\ViperDagger\ViperDaggerWeaponConfig.cs
echo(Integration\NewWeapons\ViperDagger\ViperDaggerRuntime.cs
echo(Integration\NewWeapons\SummonStaff\SummonStaffConfig.cs
echo(Integration\NewWeapons\SummonStaff\SummonStaffWeaponConfig.cs
echo(Integration\NewWeapons\SummonStaff\SummonStaffAction.cs
echo(Integration\NewWeapons\SummonStaff\SummonStaffManager.cs
echo(Integration\NewWeapons\EnergyShield\EnergyShieldConfig.cs
echo(Integration\NewWeapons\EnergyShield\EnergyShieldWeaponConfig.cs
echo(Integration\NewWeapons\EnergyShield\EnergyShieldRuntime.cs
echo(Integration\NewWeapons\FrostSpear\FrostSpearConfig.cs
echo(Integration\NewWeapons\FrostSpear\FrostSpearWeaponConfig.cs
echo(Integration\NewWeapons\ThunderRing\ThunderRingConfig.cs
echo(Integration\NewWeapons\ThunderRing\ThunderRingWeaponConfig.cs
echo(Integration\NewWeapons\ThunderRing\ThunderRingRuntime.cs
echo(Integration\FlightTotem\FlightConfig.cs
echo(Integration\FlightTotem\FlightTotemFactory.cs
echo(Integration\FlightTotem\FlightTotemBootstrap.cs
echo(Integration\FlightTotem\FlightAbilityManager.cs
echo(Integration\FlightTotem\FlightTotemEffectManager.cs
echo(Integration\FlightTotem\FlightCloudEffect.cs
echo(Integration\FlightTotem\CA_Flight.cs
echo(Integration\Constants\GoblinNPCConstants.cs
echo(Integration\Constants\GoblinMovementConstants.cs
echo(Integration\Constants\NurseNPCConstants.cs
echo(Utilities\AssetBundleUnloadHelper.cs
echo(Integration\Utils\NPCExceptionHandler.cs
echo(Integration\Utils\NPCAssetBundleHelper.cs
echo(Integration\Utils\NPCUIAssetCache.cs
echo(Integration\Utils\NPCHeartBubbleHelper.cs
echo(Integration\Utils\NPCNameTagHelper.cs
echo(Integration\Utils\NPCPathingHelper.cs
echo(Integration\Utils\NPCFollowMovementBase.cs
echo(Integration\Utils\NPCInteractionGroupHelper.cs
echo(Integration\Utils\NPCCommonUtils.cs
echo(Integration\NPCs\Common\NPCModuleRegistry.cs
echo(Integration\NPCs\Common\CommonNpcRuntimeModule.cs
echo(Integration\NPCs\Common\CommonNpcRuntimeHooks.cs
echo(Integration\NPCs\Courier\CourierNPC.cs
echo(Integration\NPCs\Courier\CourierNPCController.cs
echo(Integration\NPCs\Courier\CourierMovement.cs
echo(Integration\NPCs\Courier\CourierInteractables.cs
echo(Integration\NPCs\Courier\CourierLootSweepRunner.cs
echo(Integration\NPCs\Courier\OriginalConfirmDialogueAdapter.cs
echo(Integration\NPCs\Courier\CourierPaidLootSweepService.cs
echo(Integration\NPCs\Goblin\GoblinNPC.cs
echo(Integration\NPCs\Goblin\GoblinNPCController.cs
echo(Integration\NPCs\Goblin\GoblinNPCAnimation.cs
echo(Integration\NPCs\Goblin\GoblinNPCDialogue.cs
echo(Integration\NPCs\Goblin\GoblinNPCReward.cs
echo(Integration\NPCs\Goblin\GoblinMovement.cs
echo(Integration\NPCs\Courier\CourierService.cs
echo(Integration\NPCs\Courier\CourierService_Buttons.cs
echo(Integration\NPCs\Courier\CourierService_CloseAndCleanup.cs
echo(Integration\NPCs\Courier\DepositDataManager.cs
echo(Integration\NPCs\Courier\StorageDepositService.cs
echo(Integration\NPCs\Courier\CourierPaidLootSweepAccountingAndSort.cs
echo(Integration\NPCs\Courier\StorageDepositLifecycle.cs
echo(Integration\NPCs\Courier\StorageDepositTransactions.cs
echo(Integration\NPCs\Courier\StorageDepositSingleRetrieve.cs
echo(Integration\NPCs\Courier\StorageDepositInventoryQuickDeposit.cs
echo(Integration\NPCs\Courier\StorageDepositBulkActions.cs
echo(Integration\NPCs\Nurse\NurseNPC.cs
echo(Integration\NPCs\Nurse\NurseNPCController.cs
echo(Integration\NPCs\Nurse\NurseMovement.cs
echo(Integration\NPCs\Nurse\NurseHealingService.cs
echo(Integration\NPCs\Nurse\NurseHealInteractable.cs
echo(Integration\NPCs\Nurse\NurseInteractable.cs
echo(Integration\NPCs\DuckNpc\DuckNpcFaceCatalog.cs
echo(Integration\NPCs\DuckNpc\DuckNpcFaceCodec.cs
echo(Integration\NPCs\DuckNpc\DuckNpcBlueprint.cs
echo(Integration\NPCs\DuckNpc\DuckNpcRegistry.cs
echo(Integration\NPCs\DuckNpc\DuckNpcSpawner.cs
echo(Integration\NPCs\DuckNpc\DuckNpcMovement.cs
echo(Integration\NPCs\DuckNpc\DuckNpcModule.cs
echo(Integration\NPCs\DuckNpc\Permanent\PermanentDuckNpcData.cs
echo(Integration\NPCs\DuckNpc\Permanent\PermanentDuckNpcRegistry.cs
echo(Integration\NPCs\DuckNpc\Permanent\PermanentDuckNpcAffinityConfig.cs
echo(Integration\NPCs\DuckNpc\Permanent\PermanentDuckNpcInteractable.cs
echo(Integration\NPCs\DuckNpc\Permanent\PermanentDuckNpcModule.cs
echo(Integration\NPCs\DuckNpc\DuckNpcFaceRandomizer.cs
echo(Integration\NPCs\DuckNpc\DuckNpcOutfitter.cs
echo(Integration\NPCs\DuckNpc\DuckNpcRuntimeMarker.cs
echo(Integration\NPCs\DuckNpc\DuckNpcFactory.cs
echo(Integration\NPCs\DuckNpc\DuckNpcDebugProbe.cs
echo(Integration\Reforge\GoblinReforgeInteractable.cs
echo(Integration\Reforge\PropertyLockSystem.cs
echo(Integration\Reforge\ColdQuenchFluidConfig.cs
echo(Integration\Reforge\ReforgeSystem.cs
echo(Integration\Reforge\ReforgeSystem_ApplyAndResults.cs
echo(Integration\Reforge\ReforgeUIManager.cs
echo(Integration\Reforge\ReforgeUIManager_ComparisonAndState.cs
echo(Integration\Reforge\ReforgeUIManager_RuntimeAndCleanup.cs
echo(Integration\Reforge\ReforgeDataPersistence.cs
echo(Integration\Reforge\ReforgeDataPersistenceCleanup.cs
echo(Integration\Reforge\CustomItemRuntimeStateHelperStaticCacheReset.cs
echo(Integration\ItemFactory.cs
echo(Integration\Items\AwenDepositTokenConfig.cs
echo(Integration\Items\AwenDepositTokenUsage.cs
echo(Integration\Items\ItemContentRegistry.cs
echo(Integration\Items\RelicEggConfig.cs
echo(Integration\Items\AwenLootSweepTokenConfig.cs
echo(Integration\Items\AwenLootSweepTokenUsage.cs
echo(Integration\Items\BrickStoneConfig.cs
echo(Integration\Items\BrickStoneUsage.cs
echo(Integration\Items\DiamondConfig.cs
echo(Integration\Items\DiamondUsage.cs
echo(Integration\Items\DiamondRingConfig.cs
echo(Integration\Items\CalmingDropsConfig.cs
echo(Integration\Items\CalmingDropsUsage.cs
echo(Integration\Items\PeaceCharmConfig.cs
echo(Integration\Items\PeaceCharmRuntime.cs
echo(Integration\Items\DingdangDrawingConfig.cs
echo(Integration\Items\DingdangDrawingUsage.cs
echo(Integration\Items\WildHornConfig.cs
echo(Integration\Items\WildHornUsage.cs
echo(Integration\Items\BloodhuntTransponderConfig.cs
echo(Integration\Items\FateEchoRelicConfig.cs
echo(Integration\Items\ModeFItemConfigHelper.cs
echo(Integration\Items\FoldableCoverPackConfig.cs
echo(Integration\Items\ReinforcedRoadblockPackConfig.cs
echo(Integration\Items\BarbedWirePackConfig.cs
echo(Integration\Items\EmergencyRepairSprayConfig.cs
echo(Integration\Items\ZombieTideInvitationConfig.cs
echo(Integration\Items\ZombieTideInvitationUsage.cs
echo(Integration\Items\ZombieTideBeaconConfig.cs
echo(Integration\Items\ZombieTideBeaconUsage.cs
echo(Integration\Items\PortableSafeZoneDeviceConfig.cs
echo(Integration\Items\PortableSafeZoneDeviceUsage.cs
echo(Integration\UI\ImageViewerUI.cs
echo(Integration\Affinity\INPCAffinityConfig.cs
echo(Integration\Affinity\AffinityConfig.cs
echo(Integration\Affinity\AffinityData.cs
echo(Integration\Affinity\AffinityJsonSerializer.cs
echo(Integration\Affinity\AffinityManager.cs
echo(Integration\Affinity\AffinityManagerPersistenceAndDecay.cs
echo(Integration\Affinity\AffinityManagerStaticCacheReset.cs
echo(Integration\Affinity\Core\INPCGiftConfig.cs
echo(Integration\Affinity\Core\INPCDialogueConfig.cs
echo(Integration\Affinity\Core\INPCRelationshipDialogueConfig.cs
echo(Integration\Affinity\Core\INPCController.cs
echo(Integration\Affinity\Core\INPCShopConfig.cs
echo(Integration\Affinity\Core\INPCGiftContainerConfig.cs
echo(Integration\Affinity\Core\NPCGiftContainerConfigDefaults.cs
echo(Integration\Affinity\Services\NPCGiftContainerService.cs
echo(Integration\Affinity\Systems\NPCGiftSystem.cs
echo(Integration\Affinity\Systems\NPCDialogueSystem.cs
echo(Integration\Affinity\Systems\NPCShopSystem.cs
echo(Integration\Affinity\Systems\NPCAffinityInteractionHelper.cs
echo(Integration\Affinity\AffinityRuntimeHooks.cs
echo(Integration\Affinity\Interactables\NPCInteractableBase.cs
echo(Integration\Affinity\Interactables\NPCGiftInteractable.cs
echo(Integration\Affinity\Interactables\NPCShopInteractable.cs
echo(Integration\Affinity\NPCs\GoblinAffinityConfig.cs
echo(Integration\Affinity\NPCs\NurseAffinityConfig.cs
echo(Integration\Affinity\AffinityUIManager.cs
echo(Integration\Dialogue\DialogueManager.cs
echo(Integration\Dialogue\DialogueActorFactory.cs
echo(Integration\WikiBookItem.cs
echo(Integration\WikiUIManager.cs
echo(Integration\WikiContentManager.cs
echo(Integration\ReverseScale\ReverseScaleConfig.cs
echo(Integration\ReverseScale\ReverseScaleEffectManager.cs
echo(Integration\ReverseScale\ReverseScaleAbilityManager.cs
echo(Integration\ReverseScale\ReverseScaleBootstrap.cs
echo(Integration\ReverseScale\ReverseScaleFactory.cs
echo(Injection\Injection.cs
echo(Achievement\AchievementRuntimeModule.cs
echo(Achievement\AchievementRuntimeHooks.cs
echo(Achievement\BossRushAchievementDef.cs
echo(Achievement\AchievementTracker.cs
echo(Achievement\BossRushAchievementManager.cs
echo(Achievement\AchievementIconLoader.cs
echo(Achievement\SteamAchievementPopup.cs
echo(Achievement\AchievementTriggers.cs
echo(Achievement\AchievementUIStrings.cs
echo(Achievement\AchievementEntryUI.cs
echo(Achievement\AchievementView.cs
echo(Achievement\AchievementMedalConfig.cs
echo(Achievement\AchievementMedalItem.cs
echo(Audio\BossRushAudioHooks.cs
echo(Audio\BossRushAudioManager.cs
echo(DebugAndTools\InventoryInspector.cs
echo(WavesArena\InfiniteHellCashMagnet.cs
echo(Integration\Wedding\NPCMarriageSystem.cs
echo(Integration\Wedding\WeddingChapelInteractable.cs
echo(Integration\Wedding\WeddingBuildingInjector.cs
echo(Integration\Wedding\WeddingBuildingInjector_DataEventsAndRuntime.cs
echo(Integration\Wedding\WeddingModBehaviourBridge.cs
echo(Integration\WishFountain\WishFountainService.cs
echo(Integration\WishFountain\WishFountainConfigAndValidation.cs
echo(Integration\WishFountain\WishFountainRewardPoolBuild.cs
echo(Integration\WishFountain\WishFountainRewardSelection.cs
echo(Integration\WishFountain\WishFountainSendPipeline.cs
echo(Integration\WishFountain\WishFountainFetchPipeline.cs
echo(Integration\WishFountain\WishFountainInteractable.cs
echo(Integration\WishFountain\WishFountainDanmakuView.cs
echo(Integration\WishFountain\WishFountainUI.cs
echo(Integration\WishFountain\WishFountainUIBridge.cs
echo(Integration\WishFountain\WishFountainRewardAnimationView.cs
echo(Integration\WishFountain\WishFountainBuilder.cs
echo(Integration\WishFountain\WishFountainBuilder_DataEventsAndRuntime.cs
echo(PetNest\PetNestModels.cs
echo(PetNest\PetNestTuning.cs
echo(PetNest\PetNestLineageCatalog.cs
echo(Config\ConfigPetNest.cs
echo(PetNest\PetNestJson.cs
echo(PetNest\PetNestPersistenceCodec.cs
echo(PetNest\PetNestPersistence.cs
echo(PetNest\PetNestSaveCoordinator.cs
echo(PetNest\PetNestService.cs
echo(PetNest\PetNestDropService.cs
echo(PetNest\PetNestHatchService.cs
echo(PetNest\PetNestModeGate.cs
echo(PetNest\PetNestCompanionRuntime.cs
echo(PetNest\PetNestDeathSuppressionRegistry.cs
echo(PetNest\PetNestDownedHandler.cs
echo(PetNest\PetNestExpeditionService.cs
echo(PetNest\PetNestUIBridge.cs
echo(PetNest\PetNestInteractable.cs
echo(PetNest\PetNestBuilder.cs
echo(PetNest\PetNestBuilder_DataEventsAndRuntime.cs
echo(PetNest\PetNestUIPages.cs
echo(PetNest\PetNestUI.cs
echo(PetNest\PetNestHatchRevealView.cs
echo(PetNest\PetNestExpeditionRevealView.cs
echo(PetNest\PetNestCompanionHudView.cs
echo(PetNest\PetNestMuseumStats.cs
echo(PetNest\PetNestBaseIdleSpawner.cs
echo(PetNest\PetNestRenameModal.cs
echo(PetNest\PetNestReleaseConfirmModal.cs
echo(PetNest\PetNestProgressionService.cs
echo(PetNest\PetNestRuntimeModule.cs
echo(PetNest\PetNestCompanionAgent.cs
echo(PetNest\PetNestCompanionSpawner.cs
echo(PetNest\PetNestPetProxyBridge.cs
echo(PetNest\PetNestDebugProbe.cs
echo(Config\ConfigItemIds.cs
echo(Config\ConfigDailyReport.cs
echo(Integration\DailyReport\DailyReportTuning.cs
echo(Integration\DailyReport\DailyReportModels.cs
echo(Integration\DailyReport\DailyReportCodec.cs
echo(Integration\DailyReport\DailyReportPersistence.cs
echo(Integration\DailyReport\DailyReportSaveCoordinator.cs
echo(Integration\DailyReport\DailyReportRewards.cs
echo(Integration\DailyReport\DailyReportBounty.cs
echo(Integration\DailyReport\DailyReportService.cs
echo(Integration\DailyReport\DailyReportContent.cs
echo(Integration\DailyReport\DailyReportStatsCollector.cs
echo(Integration\DailyReport\DailyReportInteractable.cs
echo(Integration\DailyReport\DailyReportUI.cs
echo(Integration\DailyReport\DailyReportUIBridge.cs
echo(Integration\DailyReport\DailyReportMailboxBuilder.cs
echo(Integration\DailyReport\DailyReportMailboxRuntime.cs
echo(Integration\DailyReport\DailyReportRuntimeModule.cs
echo(Config\ConfigCodex.cs
echo(Integration\Codex\CodexTuning.cs
echo(Integration\Codex\CodexModels.cs
echo(Integration\Codex\CodexCodec.cs
echo(Integration\Codex\CodexPersistence.cs
echo(Integration\Codex\CodexSaveCoordinator.cs
echo(Integration\Codex\CodexBossCatalog.cs
echo(Integration\Codex\CodexKillCollector.cs
echo(Integration\Codex\CodexMilestones.cs
echo(Integration\Codex\CodexPortraitCache.cs
echo(Integration\Codex\CodexView.cs
echo(Integration\Codex\CodexView_Grid.cs
echo(Integration\Codex\CodexBookItem.cs
echo(Integration\Codex\CodexRuntimeModule.cs
echo(Localization\CodexLocalization.cs
echo(Config\ConfigRandomEvents.cs
echo(RandomEvents\RandomEventsTuning.cs
echo(RandomEvents\RandomEventModels.cs
echo(RandomEvents\RandomEventModeGate.cs
echo(RandomEvents\RandomEventDirector.cs
echo(RandomEvents\RandomEventCatalog.cs
echo(RandomEvents\RandomEventCatalog_Fun.cs
echo(RandomEvents\RandomEventAirdropHold.cs
echo(RandomEvents\RandomEventEffectsBridge.cs
echo(RandomEvents\RandomEventEffectsBridge_Loot.cs
echo(RandomEvents\RandomEventEffectsBridge_Spawn.cs
echo(RandomEvents\RandomEventHud.cs
echo(RandomEvents\RandomEventsRuntimeModule.cs
echo(Config\ConfigAffixForge.cs
echo(Integration\AffixForge\AffixDefinitions.cs
echo(Integration\AffixForge\AffixItemData.cs
echo(Integration\AffixForge\AffixForgeSystem.cs
echo(Integration\AffixForge\AffixForgeStoneConfig.cs
echo(Integration\AffixForge\AffixForgeStoneDropService.cs
echo(Integration\AffixForge\AffixRuntimeService.cs
echo(Integration\AffixForge\AffixRuntimeService_Effects.cs
echo(Integration\AffixForge\AffixRuntimeTicker.cs
echo(Integration\AffixForge\AffixBuffFactory.cs
echo(Integration\AffixForge\GoblinAffixForgeInteractable.cs
echo(Integration\AffixForge\AffixForgeHostCleanup.cs
echo(Integration\Reforge\ReforgeUIManager_AffixForge.cs
echo(Integration\Reforge\ReforgeUIManager_AffixForgePanel.cs
echo(Localization\AffixForgeLocalization.cs
echo(Audio\BossBgmCoordinator.cs
echo(Audio\BossBgmTrackTable.cs
echo(Common\UI\BossRushUISkinLoader.cs
echo(Config\ConfigModConfigKeys.cs
echo(Config\ConfigContentSystemSwitches.cs
echo(Config\ConfigCampaign.cs
echo(Campaign\CampaignTuning.cs
echo(Campaign\CampaignModels.cs
echo(Campaign\CampaignFacilityUnlocks.cs
echo(Campaign\CampaignPersistence.cs
echo(Campaign\CampaignSaveCoordinator.cs
echo(Campaign\CampaignContentCatalog.cs
echo(Campaign\CampaignObjectiveTracker.cs
echo(Campaign\CampaignObjectiveCollector.cs
echo(Campaign\CampaignProgressService.cs
echo(Campaign\CampaignModeBridge.cs
echo(Campaign\CampaignAssetCache.cs
echo(Campaign\CampaignNoteBridge.cs
echo(Campaign\CampaignDialoguePlayer.cs
echo(Campaign\CampaignBoardView.cs
echo(Campaign\CampaignBoardInteractable.cs
echo(Campaign\CampaignBoardBuilder.cs
echo(Campaign\CampaignHud.cs
echo(Campaign\CampaignFinalBossInteractable.cs
echo(Campaign\CampaignFinalBoss.cs
echo(Localization\CampaignLocalization.cs
echo(Campaign\CampaignRuntimeModule.cs
echo(Config\ConfigBackMountain.cs
echo(Integration\BackMountain\BackMountainConfig.cs
echo(Integration\BackMountain\BackMountainUnlocks.cs
echo(Integration\BackMountain\BackMountainItems.cs
echo(Integration\BackMountain\GardenSeedInjector.cs
echo(Integration\BackMountain\RaidMealUsageBehavior.cs
echo(Integration\BackMountain\RaidMealService.cs
echo(Integration\BackMountain\JukeboxTrackInjector.cs
echo(Integration\BackMountain\BackMountainSeedDrops.cs
echo(Integration\BackMountain\ShowcaseService.cs
echo(Integration\BackMountain\ShowcaseUI.cs
echo(Integration\BackMountain\ShowcaseInteractable.cs
echo(Integration\BackMountain\ShowcaseBuildingBuilder.cs
echo(Localization\BackMountainLocalization.cs
echo(Integration\BackMountain\BackMountainRuntimeModule.cs
)>"%OUTPUT_DIR%\bossrush.rsp"

dotnet "%DOTNET_SDK%\Roslyn\bincore\csc.dll" @"%OUTPUT_DIR%\bossrush.rsp"
set "BUILD_EXIT_CODE=%ERRORLEVEL%"

if %BUILD_EXIT_CODE% EQU 0 (
    echo.
    echo ===================================
    echo Build succeeded!
    echo ===================================
    echo.
    echo Output: %OUTPUT_DIR%\%MOD_NAME%.dll

    REM Auto deploy to the game mod load path
    copy /Y "%OUTPUT_DIR%\%MOD_NAME%.dll" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\%MOD_NAME%.dll" >nul 2>nul
    if errorlevel 1 (
        echo WARNING: Auto deploy failed. Please copy DLL manually.
    ) else (
        echo Deployed to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\%MOD_NAME%.dll
        if exist "Assets\SpawnPoints\*.json" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\SpawnPoints" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\SpawnPoints"
            xcopy /Y /I "Assets\SpawnPoints\*.json" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\SpawnPoints\" >nul
            if errorlevel 1 (
                echo WARNING: SpawnPoints JSON deploy failed.
            ) else (
                echo Deployed SpawnPoints JSON to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\SpawnPoints
            )
        )
        if exist "Assets\Data\*.json" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data"
            xcopy /Y /I "Assets\Data\*.json" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\" >nul
            if errorlevel 1 (
                echo WARNING: Data JSON deploy failed.
            ) else (
                echo Deployed Data JSON to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data
            )
        )
        REM 建筑图标:报箱等自定义建筑的 png 由 ModBehaviour 直接 File.ReadAllBytes 读取,不进 AssetBundle,所以必须随构建一起部署,否则建造界面只能显示官方默认图标.
        if exist "Assets\buildings\*.png" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\buildings" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\buildings"
            xcopy /Y /I "Assets\buildings\*.png" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\buildings\" >nul
            if errorlevel 1 (
                echo WARNING: building icon deploy failed.
            ) else (
                echo Deployed building icons to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\buildings
            )
        )
        REM 遗种巢建筑模型 bundle:LoadPetNestBuildingModel 直接 AssetBundle.LoadFromFile 读取.缺文件时 Mod 会退回运行时占位圆柱体,不会 fail,但基地里就看不到真模型.
        if exist "Assets\buildings\petnest_relic_nest" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\buildings" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\buildings"
            copy /Y "Assets\buildings\petnest_relic_nest" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\buildings\petnest_relic_nest" >nul 2>nul
            if errorlevel 1 (
                echo WARNING: PetNest building bundle deploy failed.
            ) else (
                echo Deployed PetNest building bundle to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\buildings
            )
        )
        REM 物品图标 PNG:EquipmentHelperIcon 直接 File.ReadAllBytes 读取,不进 AssetBundle,必须随构建一起部署,否则自定义物品会顶着克隆源物品的图标出现在背包里.
        if exist "Assets\Items\*.png" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Items" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Items"
            xcopy /Y /I "Assets\Items\*.png" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Items\" >nul
            if errorlevel 1 (
                echo WARNING: item icon deploy failed.
            ) else (
                echo Deployed item icons to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Items
            )
        )
        REM WikiContent is read at runtime from <modDir>\WikiContent by WikiContentManager.
        REM It is a multi-level tree (catalog.tsv + <lang>\<category>\*.md), so /E is required.
        REM Missing content only DevLogs at runtime, which release builds strip - deploy it explicitly.
        if exist "WikiContent\catalog.tsv" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\WikiContent" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\WikiContent"
            xcopy /E /Y /I "WikiContent" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\WikiContent\" >nul
            if errorlevel 1 (
                echo WARNING: WikiContent deploy failed.
            ) else (
                echo Deployed WikiContent to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\WikiContent
            )
        ) else (
            echo WARNING: WikiContent missing at WikiContent\catalog.tsv; in-game wiki will be empty.
        )
        if exist "Assets\Data\ModeH\*.json" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\ModeH" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\ModeH"
            xcopy /Y /I "Assets\Data\ModeH\*.json" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\ModeH\" >nul
            if errorlevel 1 (
                echo WARNING: Mode H data JSON deploy failed.
            ) else (
                echo Deployed Mode H data JSON to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\ModeH
            )
        ) else (
            echo WARNING: Mode H data JSON missing at Assets\Data\ModeH; Mode H reports content not ready.
        )
        if exist "Assets\Data\Campaign\*.json" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\Campaign" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\Campaign"
            xcopy /Y /I "Assets\Data\Campaign\*.json" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\Campaign\" >nul
            if errorlevel 1 (
                echo WARNING: Campaign data JSON deploy failed.
            ) else (
                echo Deployed Campaign data JSON to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\Campaign
            )
        ) else (
            echo WARNING: Campaign data JSON missing; campaign will use its compiled fallback.
        )
        if exist "Assets\Data\Audio\*.json" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\Audio" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\Audio"
            xcopy /Y /I "Assets\Data\Audio\*.json" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\Audio\" >nul
            if errorlevel 1 (
                echo WARNING: Audio data JSON deploy failed.
            ) else (
                echo Deployed Audio data JSON to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Data\Audio
            )
        )
        rem FMOD programmer sound 同时支持 wav 与 mp3，两种都要拷
        if exist "Assets\Sounds\BGM\*.*" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Sounds\BGM" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Sounds\BGM"
            xcopy /Y /I "Assets\Sounds\BGM\*.mp3" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Sounds\BGM\" >nul 2>nul
            xcopy /Y /I "Assets\Sounds\BGM\*.wav" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Sounds\BGM\" >nul 2>nul
            xcopy /Y /I "Assets\Sounds\BGM\*.ogg" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Sounds\BGM\" >nul 2>nul
            if errorlevel 1 (
                echo WARNING: BGM deploy failed.
            ) else (
                echo Deployed BGM to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Sounds\BGM
            )
        )
        if exist "Assets\ui\bossrush_ui_skin" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui"
            copy /Y "Assets\ui\bossrush_ui_skin" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\bossrush_ui_skin" >nul 2>nul
            if errorlevel 1 (
                echo WARNING: UI skin bundle deploy failed.
            ) else (
                echo Deployed UI skin bundle to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui
            )
        )
        if exist "Assets\ui\modeh_presentation" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui"
            copy /Y "Assets\ui\modeh_presentation" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\modeh_presentation" >nul 2>nul
            if errorlevel 1 (
                echo WARNING: Mode H presentation bundle deploy failed.
            ) else (
                echo Deployed Mode H presentation bundle to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui
            )
        ) else (
            echo WARNING: Mode H presentation bundle missing at Assets\ui\modeh_presentation; Mode H entry fails closed.
        )
        if exist "Assets\ui\modeg_presentation" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui"
            copy /Y "Assets\ui\modeg_presentation" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\modeg_presentation" >nul 2>nul
            if errorlevel 1 (
                echo WARNING: Mode G presentation bundle deploy failed.
            ) else (
                echo Deployed Mode G presentation bundle to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui
            )
        )
        if exist "Assets\ui\codex_portraits" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui"
            copy /Y "Assets\ui\codex_portraits" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\codex_portraits" >nul 2>nul
            if errorlevel 1 (
                echo WARNING: Codex portrait bundle deploy failed.
            ) else (
                echo Deployed Codex portrait bundle to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui
            )
        ) else (
            echo NOTE: Codex portrait bundle missing at Assets\ui\codex_portraits; codex falls back to placeholder icons.
        )
        if exist "Assets\ui\campaign_presentation" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui"
            copy /Y "Assets\ui\campaign_presentation" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\campaign_presentation" >nul 2>nul
            if errorlevel 1 (
                echo WARNING: Campaign presentation bundle deploy failed.
            ) else (
                echo Deployed Campaign presentation bundle to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui
            )
        ) else (
            echo NOTE: Campaign presentation bundle missing at Assets\ui\campaign_presentation; campaign falls back to raw PNG or no art.
        )
        if exist "Assets\ui\Campaign\*.png" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\Campaign" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\Campaign"
            xcopy /Y /I "Assets\ui\Campaign\*.png" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\Campaign\" >nul
            if errorlevel 1 (
                echo WARNING: Campaign raw PNG deploy failed.
            ) else (
                echo Deployed Campaign raw PNG to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\Campaign
            )
        )
        if exist "Assets\ui\AffixForge\*.png" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\AffixForge" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\AffixForge"
            xcopy /Y /I "Assets\ui\AffixForge\*.png" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\AffixForge\" >nul
            if errorlevel 1 (
                echo WARNING: Affix icon deploy failed.
            ) else (
                echo Deployed affix icons to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\AffixForge
            )
        )
        if exist "Assets\ui\random_events\*.png" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\random_events" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\random_events"
            xcopy /Y /I "Assets\ui\random_events\*.png" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\random_events\" >nul
            if errorlevel 1 (
                echo WARNING: Random event icon deploy failed.
            ) else (
                echo Deployed random event icons to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\ui\random_events
            )
        )
        if exist "Assets\Items\fate_echo_relic" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Items" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Items"
            copy /Y "Assets\Items\fate_echo_relic" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Items\fate_echo_relic" >nul 2>nul
            if errorlevel 1 echo WARNING: Fate Echo relic bundle deploy failed.
        )
        if exist "Assets\Items\portable_safe_zone_device" (
            if not exist "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Items" mkdir "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Items"
            copy /Y "Assets\Items\portable_safe_zone_device" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\Assets\Items\portable_safe_zone_device" >nul 2>nul
            if errorlevel 1 echo WARNING: Portable safe-zone device bundle deploy failed.
        )
    )
) else (
    echo.
    echo ===================================
    echo Build failed!
    echo ===================================
    echo Check errors above.
)

if not defined BOSSRUSH_NO_PAUSE pause
exit /b %BUILD_EXIT_CODE%

:ensure_game_path
if defined GAME_PATH (
    if exist "%GAME_PATH%\Duckov_Data\Managed\Assembly-CSharp.dll" goto :eof
    echo [WARN] Ignoring invalid GAME_PATH: %GAME_PATH%
    set "GAME_PATH="
)
call :try_game_path "%~dp0..\..\..\.."
if defined GAME_PATH goto :eof
call :try_game_path "E:\SteamLibrary\steamapps\common\Escape from Duckov"
if defined GAME_PATH goto :eof
call :try_game_path "D:\software\steam\steamapps\common\Escape from Duckov"
if defined GAME_PATH goto :eof
call :try_game_path "C:\Program Files (x86)\Steam\steamapps\common\Escape from Duckov"
goto :eof

:try_game_path
if exist "%~1\Duckov_Data\Managed\Assembly-CSharp.dll" (
    for %%P in ("%~1") do set "GAME_PATH=%%~fP"
)
goto :eof

:ensure_workshop_path
if defined WORKSHOP_PATH (
    if exist "%WORKSHOP_PATH%\%HARMONY_MOD_ID%\0Harmony.dll" goto :eof
    echo [WARN] Ignoring invalid WORKSHOP_PATH: %WORKSHOP_PATH%
    set "WORKSHOP_PATH="
)
if defined GAME_PATH call :try_workshop_path "%GAME_PATH%\..\..\workshop\content\3167020"
if defined WORKSHOP_PATH goto :eof
call :try_workshop_path "E:\SteamLibrary\steamapps\workshop\content\3167020"
if defined WORKSHOP_PATH goto :eof
call :try_workshop_path "D:\software\steam\steamapps\workshop\content\3167020"
if defined WORKSHOP_PATH goto :eof
call :try_workshop_path "C:\Program Files (x86)\Steam\steamapps\workshop\content\3167020"
goto :eof

:try_workshop_path
if exist "%~1\%HARMONY_MOD_ID%\0Harmony.dll" (
    for %%P in ("%~1") do set "WORKSHOP_PATH=%%~fP"
)
goto :eof
