@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ===================================
echo Boss Rush Mod - Compile (Official)
echo ===================================
echo.

set OUTPUT_DIR=Build
set MOD_NAME=BossRush
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
dotnet "%DOTNET_SDK%\Roslyn\bincore\csc.dll" ^
    /langversion:7.3 ^
    /target:library ^
    /out:"%OUTPUT_DIR%\%MOD_NAME%.dll" ^
    /lib:"%GAME_PATH%\Duckov_Data\Managed" ^
    /reference:UnityEngine.dll ^
    /reference:UnityEngine.CoreModule.dll ^
    /reference:UnityEngine.PhysicsModule.dll ^
    /reference:UnityEngine.UI.dll ^
    /reference:UnityEngine.JSONSerializeModule.dll ^
    /reference:UnityEngine.AIModule.dll ^
    /reference:UnityEngine.AudioModule.dll ^
    /reference:UnityEngine.UnityWebRequestWWWModule.dll ^
    /reference:UnityEngine.UnityWebRequestModule.dll ^
    /reference:UnityEngine.UnityWebRequestAudioModule.dll ^
    /reference:Eflatun.SceneReference.dll ^
    /reference:Assembly-CSharp.dll ^
    /reference:UnityEngine.UIModule.dll ^
    /reference:UnityEngine.InputLegacyModule.dll ^
    /reference:UnityEngine.IMGUIModule.dll ^
    /reference:UnityEngine.ImageConversionModule.dll ^
    /reference:UnityEngine.TextRenderingModule.dll ^
    /reference:UnityEngine.AssetBundleModule.dll ^
    /reference:UnityEngine.AnimationModule.dll ^
    /reference:UnityEngine.ParticleSystemModule.dll ^
    /reference:UnityEngine.VideoModule.dll ^
    /reference:Unity.TextMeshPro.dll ^
    /reference:ItemStatsSystem.dll ^
    /reference:UniTask.dll ^
    /reference:Sirenix.OdinInspector.Attributes.dll ^
    /reference:TeamSoda.Duckov.Core.dll ^
    /reference:TeamSoda.Duckov.Utilities.dll ^
    /reference:AstarPathfindingProject.dll ^
    /reference:PackageTools.dll ^
    /reference:Drawing.dll ^
    /reference:SodaLocalization.dll ^
    /reference:NodeCanvas.dll ^
    /reference:ParadoxNotion.dll ^
    /reference:System.Core.dll ^
    /reference:System.dll ^
    /reference:mscorlib.dll ^
    /reference:netstandard.dll ^
    /reference:"%WORKSHOP_PATH%\%HARMONY_MOD_ID%\0Harmony.dll" ^
    /nowarn:CS0436,CS0162,CS0414 ^
    Localization\L10n.cs ^
    Localization\LocalizationHelper.cs ^
    Localization\LocalizationInjector.cs ^
    Localization\LocalizationInjector_NpcUiAndItems.cs ^
    Localization\EquipmentLocalization.cs ^
    Common\Lifecycle\IBossRushRuntimeModule.cs ^
    Common\Lifecycle\SceneRuntimeContext.cs ^
    Common\Lifecycle\BossRushRuntimeModuleHost.cs ^
    Common\Lifecycle\BossRushRuntimeModuleBase.cs ^
    Common\Lifecycle\ArchitectureSentinelRuntimeModule.cs ^
    Common\Lifecycle\BossRushRuntimeModuleRegistration.cs ^
    Common\Events\BossRushEventBus.cs ^
    Common\Infrastructure\ReflectionCache.cs ^
    Common\Infrastructure\ObjectCache.cs ^
    Common\Infrastructure\IHarmonyPatchGroup.cs ^
    Common\Infrastructure\HarmonyPatchGroupRegistrar.cs ^
    Common\Data\JsonDataRegistry.cs ^
    Common\MapConfig\BossRushMapConfig.cs ^
    Common\MapConfig\MapSpawnPointRegistry.cs ^
    Common\Utils\ReflectionCache.cs ^
    Common\Utils\NPCBubbleAnimator.cs ^
    Common\Effects\RingParticleEffect.cs ^
    Common\Effects\MeleeWeaponFxPolicy.cs ^
    Common\Equipment\EquipmentAbilityConfig.cs ^
    Common\Equipment\EquipmentAbilityAction.cs ^
    Common\Equipment\EquipmentAbilityManager.cs ^
    Common\Equipment\EquipmentEffectManager.cs ^
    Common\Equipment\AbilitySystemHelper.cs ^
    Common\Stats\RuntimeStatModifierTracker.cs ^
    ModBehaviour.cs ^
    ModConfigApi.cs ^
    UIAndSigns\UIAndSigns.cs ^
    UIAndSigns\BossRushInteractionScan.cs ^
    UIAndSigns\UIAndSignsRuntimeBridges.cs ^
    DebugAndTools\DebugAndTools.cs ^
    DebugAndTools\DebugAndToolsPlacementAndInspection.cs ^
    DebugAndTools\DebugAndToolsStaticCacheReset.cs ^
    DebugAndTools\DebugToolsRuntimeModule.cs ^
    DebugAndTools\DebugToolsRuntimeHooks.cs ^
    DebugAndTools\MarriageTestDebugUI.cs ^
    DebugAndTools\ItemSpawner.cs ^
    DebugAndTools\F3DebugCheatMenu.cs ^
    DebugAndTools\F3DebugCheatMenuUi.cs ^
    DebugAndTools\F3DebugCheatMenuPlayerStats.cs ^
    DebugAndTools\F3DebugCheatMenuActions.cs ^
    DebugAndTools\NPCTeleportUI.cs ^
    Integration\BossRushIntegration.cs ^
    Integration\BossRushIntegration_StartAndScene.cs ^
    Integration\IntegrationDeferredBootstrap.cs ^
    Integration\BossRushIntegration_TravelAndSetup.cs ^
    Integration\BossRushIntegration_MapObjectsAndDragonBreath.cs ^
    Integration\Mutators\MutatorDefinitions.cs ^
    Integration\Mutators\MutatorManager.cs ^
    Integration\Mutators\MutatorUI.cs ^
    Integration\Mutators\MutatorRuntimeBridge.cs ^
    Integration\ZombieModeIntegration.cs ^
    Integration\DeathWraith\DeathWraithSystem.cs ^
    Integration\DeathWraith\DeathWraithRecording.cs ^
    Integration\DeathWraith\DeathWraithOriginalDeadBodyBridge.cs ^
    Integration\DeathWraith\DeathWraithSpawnFlow.cs ^
    Integration\DeathWraith\DeathWraithCombatLoadout.cs ^
    Integration\DeathWraith\DeathWraithLifecycleAndPersistence.cs ^
    Patches\BaseHub\BaseHubShopAwakePatch.cs ^
    Patches\BaseHub\BaseHubBoatPatch.cs ^
    Patches\BaseHub\BaseHubPatchGroup.cs ^
    Patches\Combat\CharacterOnDeadPatch.cs ^
    Patches\Combat\BossLethalHealthProtectionPatch.cs ^
    Patches\Combat\ProjectileHalfObstaclePatch.cs ^
    Patches\Combat\CombatPatchGroup.cs ^
    Patches\Death\DeadBodyAppendPatch.cs ^
    Patches\Death\DeadBodySpawnPatch.cs ^
    Patches\Death\TombLootboxPatch.cs ^
    Patches\Death\DeadBodyTouchedPatch.cs ^
    Patches\Death\DeathPatchGroup.cs ^
    Patches\Economy\StockShopGetItemInstanceDirectPatch.cs ^
    Integration\BirthdayCakeItem.cs ^
    Integration\EquipmentFactory.cs ^
    Integration\EquipmentFactory_ItemProcessing.cs ^
    Integration\EquipmentFactoryStaticCacheReset.cs ^
    Integration\EquipmentContentRegistry.cs ^
    Integration\EquipmentRuntimeHooks.cs ^
    Integration\IntegrationRuntimeHooks.cs ^
    Integration\EquipmentHelper.cs ^
    Integration\EquipmentHelperIcon.cs ^
    Integration\Bonus\DragonSetBonus.cs ^
    Integration\Bonus\DragonSetBonus_Dash.cs ^
    Integration\Bonus\SetBonusManager.cs ^
    Integration\Bonus\FrostSetBonus.cs ^
    Integration\Bonus\ThunderSetBonus.cs ^
    Integration\Bonus\SetBonusPlaceholderRegistry.cs ^
    Integration\Config\DragonSetConfig.cs ^
    Integration\Config\FlightTotemConfig.cs ^
    Integration\Config\DragonKingSetConfig.cs ^
    Integration\Config\FrostThunderSetConfig.cs ^
    Utilities\Utilities.cs ^
    Utilities\AlwaysOnRuntimeHooks.cs ^
    Utilities\PlayerLifecycleRuntimeHooks.cs ^
    Utilities\EntityModelFactory.cs ^
    Utilities\SimpleJsonHelper.cs ^
    Utilities\AwenLootSweepMath.cs ^
    Utilities\VictoryRewardShadowMath.cs ^
    Utilities\F3DebugCheatMath.cs ^
    Utilities\EnemySpawnCore.cs ^
    Utilities\EnemyRecoveryMonitor.cs ^
    Utilities\GameplayRuntimeHooks.cs ^
    Utilities\ModeRuntimeHooks.cs ^
    Utilities\SpawnPositionHelper.cs ^
    Utilities\RunScopedRegistry.cs ^
    Utilities\RuntimeScope.cs ^
    Utilities\SceneRuntimeGate.cs ^
    Utilities\SafeRuntime.cs ^
    Utilities\SteamHelper.cs ^
    Utilities\BossCleanupHelpers.cs ^
    Utilities\InteractableLootboxInventoryHelper.cs ^
    Utilities\OriginalCharacterIsolationHelper.cs ^
    Utilities\OriginalExtractionPointIsolationHelper.cs ^
    Utilities\ModeExtractionPointFactory.cs ^
    Utilities\MapSelectionEntryInjectionHelper.cs ^
    Config\Config.cs ^
    Config\NPCSpawnConfig.cs ^
    Config\LootBlacklistRegistry.cs ^
    WavesArena\WavesArena.cs ^
    WavesArena\WavesArenaEntryAndTeleport.cs ^
    WavesArena\WavesArenaBossSpawning.cs ^
    WavesArena\WavesArenaRuntimeModule.cs ^
    WavesArena\WavesArenaRuntimeHooks.cs ^
    WavesArena\BossRushEntryFlow.cs ^
    WavesArena\WavesArenaEnemyMaintenance.cs ^
    WavesArena\WavesArenaSpawnerControl.cs ^
    LootAndRewards\LegacyBossLootProbabilityModel.cs ^
    LootAndRewards\LootAndRewards.cs ^
    LootAndRewards\LootAndRewardsStaticCacheReset.cs ^
    LootAndRewards\LootAndRewardsInfiniteHell.cs ^
    LootAndRewards\LootAndRewardsVictoryRewards.cs ^
    LootAndRewards\LootAndRewardsRandomBossLoot.cs ^
    LootAndRewards\LootAndRewardsSpecialLoot.cs ^
    LootAndRewards\LootAndRewardsRuntimeHooks.cs ^
    LootAndRewards\VictoryRewardShadowCrateController.cs ^
    LootAndRewards\ModeEFLootboxTracker.cs ^
    Interactables\BossRushInteractables.cs ^
    Interactables\BossRushLootboxInteractables.cs ^
    TeleportDebugMonitor.cs ^
    ModeD\ModeD.cs ^
    ModeD\ModeDStaticCacheReset.cs ^
    ModeD\ModeDRuntimeModule.cs ^
    ModeD\ModeDEquipment.cs ^
    ModeD\ModeDEquipment_StarterKit.cs ^
    ModeD\ModeDWaves.cs ^
    ModeD\ModeDInteractables.cs ^
    ModeD\ModeDGlobalLoot.cs ^
    ModeD\ModeDGlobalLootStaticCacheReset.cs ^
    ModeE\ModeE.cs ^
    ModeE\ModeEUiAndHealthBars.cs ^
    ModeE\ModeEStartup.cs ^
    ModeE\ModeELifecycle.cs ^
    ModeE\ModeEIntegrityAndHelpers.cs ^
    ModeE\ModeERuntimeModule.cs ^
    ModeE\ModeERuntimeHooks.cs ^
    ModeE\ModeEMerchant.cs ^
    ModeE\ModeEMerchantSupportClasses.cs ^
    ModeE\ModeESpawnAllocation.cs ^
    ModeE\ModeEBattle.cs ^
    ModeE\ModeEBattle_ScalingAndRuntime.cs ^
    ModeE\FactionFlagConfig.cs ^
    ModeE\ModeEHarmonyPatch.cs ^
    ModeE\RespawnItemConfig.cs ^
    ModeE\ModeERespawnItems.cs ^
    ModeE\RespawnItemUsage.cs ^
    ModeF\ModeFModels.cs ^
    ModeF\ModeFRuntimeModule.cs ^
    ModeF\ModeFRuntimeHooks.cs ^
    ModeF\ModeFEntry.cs ^
    ModeF\ModeFPhases.cs ^
    ModeF\ModeFBounty.cs ^
    ModeF\ModeFBounty_EquipmentAndLoot.cs ^
    ModeF\ModeFRespawn.cs ^
    ModeF\ModeFExtraction.cs ^
    ModeF\ModeFFortifications.cs ^
    ModeF\ModeFFortifications_RuntimePlacement.cs ^
    ModeF\ModeFFortifications_RepairRewardsCleanup.cs ^
    ModeF\ModeFItemUsageAndTriggers.cs ^
    ModeF\ModeFUIStaticCacheReset.cs ^
    ModeF\ModeFUI.cs ^
    ModeF\ModeFUI_BountyRadarAndHealthBars.cs ^
    ModeF\ModeFMerchant.cs ^
    ZombieMode\ZombieModeModels.cs ^
    ZombieMode\ZombieModeTuning.cs ^
    ZombieMode\ZombieModeRuntimeModule.cs ^
    ZombieMode\ZombieModeRuntimeHooks.cs ^
    ZombieMode\ZombieModeEntry.cs ^
    ZombieMode\ZombieModeEntry_StarterLoadout.cs ^
    ZombieMode\ZombieModeMapSelection.cs ^
    ZombieMode\ZombieModeMapSelectionHelper.cs ^
    ZombieMode\ZombieModeInventoryTransfer.cs ^
    ZombieMode\ZombieModeMapIsolation.cs ^
    ZombieMode\ZombieModePollution.cs ^
    ZombieMode\ZombieModePollution_RuntimeSkills.cs ^
    ZombieMode\ZombieModePollution_RuntimeComponents.cs ^
    ZombieMode\ZombieModeBossController.cs ^
    ZombieMode\ZombieModePlayerSlowRuntime.cs ^
    ZombieMode\ZombieModeSpawner.cs ^
    ZombieMode\ZombieModeWaveController.cs ^
    ZombieMode\ZombieModeEnemyRuntime.cs ^
    ZombieMode\ZombieModeRewards.cs ^
    ZombieMode\ZombieModeRewardCatalogAndSelection.cs ^
    ZombieMode\ZombieModeRewardEffectsAndNpc.cs ^
    ZombieMode\ZombieModeRewardItemGrants.cs ^
    ZombieMode\ZombieModeRewardNpcServices.cs ^
    ZombieMode\ZombieModeRewardEffects.cs ^
    ZombieMode\ZombieModeRewardOptionCore.cs ^
    ZombieMode\ZombieModeRewardProjectileSpread.cs ^
    ZombieMode\ZombieModeRewardRuntimeModifiers.cs ^
    ZombieMode\ZombieModeRewardTriggerEffects.cs ^
    ZombieMode\ZombieModeRewardProjectilePatch.cs ^
    ZombieMode\ZombieModeDropsAndPerformance.cs ^
    ZombieMode\ZombiePurificationPointController.cs ^
    ZombieMode\ZombieModeSafeZoneController.cs ^
    ZombieMode\ZombieModeExtractionController.cs ^
    ZombieMode\ZombieModeHudController.cs ^
    ZombieMode\ZombieModeUIHelper.cs ^
    ZombieMode\ZombieModeCleanup.cs ^
    ZombieMode\ZombieModeDebug.cs ^
    ZombieMode\ZombieModeNpcCatalog.cs ^
    ZombieMode\ZombieModeCashInvestmentView.cs ^
    BossFilter\BossFilter.cs ^
    BossFilter\BossFilterUi.cs ^
    MapSelection\BossRushMapSelectionHelper.cs ^
    MapSelection\MapThumbnailCache.cs ^
    Integration\DragonDescendant\DragonDescendantConfig.cs ^
    Integration\DragonDescendant\DragonDescendantAbilities.cs ^
    Integration\DragonDescendant\DragonDescendantAbilities_ProjectilesAndGrenades.cs ^
    Integration\DragonDescendant\DragonDescendantAbilities_ResurrectionAndPhase.cs ^
    Integration\DragonDescendant\DragonDescendantAbilities_Phase2Combat.cs ^
    Integration\DragonDescendant\DragonDescendantAbilities_CollisionAndIce.cs ^
    Integration\DragonDescendant\DragonDescendantBoss.cs ^
    Integration\DragonDescendant\DragonDescendantBoss_RuntimeAndCleanup.cs ^
    Integration\DragonDescendant\DragonDescendantBossStaticCacheReset.cs ^
    Integration\DragonDescendant\DragonBreathConfig.cs ^
    Integration\DragonDescendant\DragonBreathBuffHandler.cs ^
    Integration\DragonDescendant\DragonBreathWeaponConfig.cs ^
    Integration\DragonDescendant\DragonBreathWeaponConfig_FireEffects.cs ^
    Integration\DragonKing\DragonKingConfig.cs ^
    Integration\DragonKing\DragonKingAssetManager.cs ^
    Integration\DragonKing\DragonKingAbilityController.cs ^
    Integration\DragonKing\DragonKingAbilityController_AttackFlow.cs ^
    Integration\DragonKing\DragonKingAbilityController_ProjectileAndMovement.cs ^
    Integration\DragonKing\DragonKingAbilityController_SpecialAttacks.cs ^
    Integration\DragonKing\DragonKingAbilityController_ChildProtection.cs ^
    Integration\DragonKing\DragonKingAbilityHelpers.cs ^
    Integration\DragonKing\DragonKingShockwaveEffect.cs ^
    Integration\DragonKing\DragonKingBoss.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdIds.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdConfig.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdRuntime.cs ^
    Integration\DragonKing\Weapons\DragonFlameMarkTracker.cs ^
    Integration\DragonKing\Weapons\DragonKingBossGunConfig.cs ^
    Integration\DragonKing\Weapons\DragonKingBossGunProfiles.cs ^
    Integration\DragonKing\Weapons\DragonKingBossGunProjectileAgent.cs ^
    Integration\DragonKing\Weapons\DragonKingBossGunProjectileZones.cs ^
    Integration\DragonKing\Weapons\DragonKingBossGunRuntime.cs ^
    Integration\DragonKing\Weapons\DragonKingBossGunRuntime_ProjectilesAndPatches.cs ^
    Integration\DragonKing\Weapons\DragonKingBossGunRuntimeStaticCacheReset.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdAction.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdAbilityManager.cs ^
    Integration\DragonKing\Weapons\FenHuangComboManager.cs ^
    Integration\DragonKing\Weapons\FenHuangComboPatchesAndFx.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdBootstrap.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdWeaponConfig.cs ^
    Integration\PhantomWitch\PhantomWitchConfig.cs ^
    Integration\PhantomWitch\PhantomWitchPerformancePolicy.cs ^
    Integration\PhantomWitch\PhantomWitchAmbientPresence.cs ^
    Integration\PhantomWitch\PhantomWitchVfxRedesign.cs ^
    Integration\PhantomWitch\PhantomWitchVfxRedesign_EmittersAndTextures.cs ^
    Integration\PhantomWitch\PhantomWitchVfxRedesign_RuntimeComponents.cs ^
    Integration\PhantomWitch\PhantomWitchVfxRedesignStaticCacheReset.cs ^
    Integration\PhantomWitch\PhantomWitchAssetManager.cs ^
    Integration\PhantomWitch\PhantomWitchAssetManager_RuntimeComponents.cs ^
    Integration\PhantomWitch\PhantomWitchAbilityController.cs ^
    Integration\PhantomWitch\PhantomWitchAbilityController_PackageScheduler.cs ^
    Integration\PhantomWitch\PhantomWitchAbilityController_StealthAndAttacks.cs ^
    Integration\PhantomWitch\PhantomWitchAbilityController_Minions.cs ^
    Integration\PhantomWitch\PhantomWitchAbilityController_RuntimeTicks.cs ^
    Integration\PhantomWitch\PhantomWitchAbilityController_PhaseAndLifecycle.cs ^
    Integration\PhantomWitch\PhantomWitchAbilityController_MovementAndDamage.cs ^
    Integration\PhantomWitch\PhantomWitchAbilityController_CleanupAndTelemetry.cs ^
    Integration\PhantomWitch\PhantomWitchBossCurseRealmRuntime.cs ^
    Integration\PhantomWitch\PhantomWitchBoss.cs ^
    Integration\PhantomWitch\PhantomWitchScytheIds.cs ^
    Integration\PhantomWitch\PhantomWitchScytheConfig.cs ^
    Integration\PhantomWitch\PhantomWitchScytheSwingFx.cs ^
    Integration\PhantomWitch\PhantomWitchScytheWeaponConfig.cs ^
    Integration\PhantomWitch\PhantomWitchScytheAction.cs ^
    Integration\PhantomWitch\PhantomWitchScytheAction_RuntimeComponents.cs ^
    Integration\PhantomWitch\PhantomWitchScytheAbilityManager.cs ^
    Integration\PhantomWitch\PhantomWitchCurseSweatVfx.cs ^
    Integration\PhantomWitch\PhantomWitchScytheBootstrap.cs ^
    Integration\Frostmourne\FrostmourneIds.cs ^
    Integration\Frostmourne\FrostmourneConfig.cs ^
    Integration\Frostmourne\FrostmourneWeaponConfig.cs ^
    Integration\Frostmourne\FrostmourneSwingFx.cs ^
    Integration\Frostmourne\FrostmourneAction.cs ^
    Integration\Frostmourne\FrostmourneAbilityManager.cs ^
    Integration\Frostmourne\FrostmourneBootstrap.cs ^
    Integration\NewWeapons\Common\NewWeaponIds.cs ^
    Integration\NewWeapons\Common\NewWeaponBootstrap.cs ^
    Integration\NewWeapons\Common\NewWeaponPlaceholderRegistry.cs ^
    Integration\NewWeapons\ViperDagger\ViperDaggerConfig.cs ^
    Integration\NewWeapons\ViperDagger\ViperDaggerWeaponConfig.cs ^
    Integration\NewWeapons\ViperDagger\ViperDaggerRuntime.cs ^
    Integration\NewWeapons\SummonStaff\SummonStaffConfig.cs ^
    Integration\NewWeapons\SummonStaff\SummonStaffWeaponConfig.cs ^
    Integration\NewWeapons\SummonStaff\SummonStaffAction.cs ^
    Integration\NewWeapons\SummonStaff\SummonStaffManager.cs ^
    Integration\NewWeapons\EnergyShield\EnergyShieldConfig.cs ^
    Integration\NewWeapons\EnergyShield\EnergyShieldWeaponConfig.cs ^
    Integration\NewWeapons\EnergyShield\EnergyShieldRuntime.cs ^
    Integration\NewWeapons\FrostSpear\FrostSpearConfig.cs ^
    Integration\NewWeapons\FrostSpear\FrostSpearWeaponConfig.cs ^
    Integration\NewWeapons\ThunderRing\ThunderRingConfig.cs ^
    Integration\NewWeapons\ThunderRing\ThunderRingWeaponConfig.cs ^
    Integration\NewWeapons\ThunderRing\ThunderRingRuntime.cs ^
    Integration\FlightTotem\FlightConfig.cs ^
    Integration\FlightTotem\FlightTotemFactory.cs ^
    Integration\FlightTotem\FlightTotemBootstrap.cs ^
    Integration\FlightTotem\FlightAbilityManager.cs ^
    Integration\FlightTotem\FlightTotemEffectManager.cs ^
    Integration\FlightTotem\FlightCloudEffect.cs ^
    Integration\FlightTotem\CA_Flight.cs ^
    Integration\Constants\GoblinNPCConstants.cs ^
    Integration\Constants\GoblinMovementConstants.cs ^
    Integration\Constants\NurseNPCConstants.cs ^
    Utilities\AssetBundleUnloadHelper.cs ^
    Integration\Utils\NPCExceptionHandler.cs ^
    Integration\Utils\NPCAssetBundleHelper.cs ^
    Integration\Utils\NPCUIAssetCache.cs ^
    Integration\Utils\NPCHeartBubbleHelper.cs ^
    Integration\Utils\NPCNameTagHelper.cs ^
    Integration\Utils\NPCPathingHelper.cs ^
    Integration\Utils\NPCFollowMovementBase.cs ^
    Integration\Utils\NPCInteractionGroupHelper.cs ^
    Integration\Utils\NPCCommonUtils.cs ^
    Integration\NPCs\Common\NPCModuleRegistry.cs ^
    Integration\NPCs\Common\CommonNpcRuntimeModule.cs ^
    Integration\NPCs\Common\CommonNpcRuntimeHooks.cs ^
    Integration\NPCs\Courier\CourierNPC.cs ^
    Integration\NPCs\Courier\CourierNPCController.cs ^
    Integration\NPCs\Courier\CourierMovement.cs ^
    Integration\NPCs\Courier\CourierInteractables.cs ^
    Integration\NPCs\Courier\CourierLootSweepRunner.cs ^
    Integration\NPCs\Courier\OriginalConfirmDialogueAdapter.cs ^
    Integration\NPCs\Courier\CourierPaidLootSweepService.cs ^
    Integration\NPCs\Goblin\GoblinNPC.cs ^
    Integration\NPCs\Goblin\GoblinNPCController.cs ^
    Integration\NPCs\Goblin\GoblinNPCAnimation.cs ^
    Integration\NPCs\Goblin\GoblinNPCDialogue.cs ^
    Integration\NPCs\Goblin\GoblinNPCReward.cs ^
    Integration\NPCs\Goblin\GoblinMovement.cs ^
    Integration\NPCs\Courier\CourierService.cs ^
    Integration\NPCs\Courier\CourierService_Buttons.cs ^
    Integration\NPCs\Courier\CourierService_CloseAndCleanup.cs ^
    Integration\NPCs\Courier\DepositDataManager.cs ^
    Integration\NPCs\Courier\StorageDepositService.cs ^
    Integration\NPCs\Courier\CourierPaidLootSweepAccountingAndSort.cs ^
    Integration\NPCs\Courier\StorageDepositLifecycle.cs ^
    Integration\NPCs\Courier\StorageDepositTransactions.cs ^
    Integration\NPCs\Courier\StorageDepositSingleRetrieve.cs ^
    Integration\NPCs\Courier\StorageDepositInventoryQuickDeposit.cs ^
    Integration\NPCs\Courier\StorageDepositBulkActions.cs ^
    Integration\NPCs\Nurse\NurseNPC.cs ^
    Integration\NPCs\Nurse\NurseNPCController.cs ^
    Integration\NPCs\Nurse\NurseMovement.cs ^
    Integration\NPCs\Nurse\NurseHealingService.cs ^
    Integration\NPCs\Nurse\NurseHealInteractable.cs ^
    Integration\NPCs\Nurse\NurseInteractable.cs ^
    Integration\Reforge\GoblinReforgeInteractable.cs ^
    Integration\Reforge\PropertyLockSystem.cs ^
    Integration\Reforge\ColdQuenchFluidConfig.cs ^
    Integration\Reforge\ReforgeSystem.cs ^
    Integration\Reforge\ReforgeSystem_ApplyAndResults.cs ^
    Integration\Reforge\ReforgeUIManager.cs ^
    Integration\Reforge\ReforgeUIManager_ComparisonAndState.cs ^
    Integration\Reforge\ReforgeUIManager_RuntimeAndCleanup.cs ^
    Integration\Reforge\ReforgeDataPersistence.cs ^
    Integration\Reforge\ReforgeDataPersistenceCleanup.cs ^
    Integration\Reforge\CustomItemRuntimeStateHelperStaticCacheReset.cs ^
    Integration\ItemFactory.cs ^
    Integration\Items\AwenDepositTokenConfig.cs ^
    Integration\Items\AwenDepositTokenUsage.cs ^
    Integration\Items\ItemContentRegistry.cs ^
    Integration\Items\AwenLootSweepTokenConfig.cs ^
    Integration\Items\AwenLootSweepTokenUsage.cs ^
    Integration\Items\BrickStoneConfig.cs ^
    Integration\Items\BrickStoneUsage.cs ^
    Integration\Items\DiamondConfig.cs ^
    Integration\Items\DiamondUsage.cs ^
    Integration\Items\DiamondRingConfig.cs ^
    Integration\Items\CalmingDropsConfig.cs ^
    Integration\Items\CalmingDropsUsage.cs ^
    Integration\Items\PeaceCharmConfig.cs ^
    Integration\Items\PeaceCharmRuntime.cs ^
    Integration\Items\DingdangDrawingConfig.cs ^
    Integration\Items\DingdangDrawingUsage.cs ^
    Integration\Items\WildHornConfig.cs ^
    Integration\Items\WildHornUsage.cs ^
    Integration\Items\BloodhuntTransponderConfig.cs ^
    Integration\Items\ModeFItemConfigHelper.cs ^
    Integration\Items\FoldableCoverPackConfig.cs ^
    Integration\Items\ReinforcedRoadblockPackConfig.cs ^
    Integration\Items\BarbedWirePackConfig.cs ^
    Integration\Items\EmergencyRepairSprayConfig.cs ^
    Integration\Items\ZombieTideInvitationConfig.cs ^
    Integration\Items\ZombieTideInvitationUsage.cs ^
    Integration\Items\ZombieTideBeaconConfig.cs ^
    Integration\Items\ZombieTideBeaconUsage.cs ^
    Integration\UI\ImageViewerUI.cs ^
    Integration\Affinity\INPCAffinityConfig.cs ^
    Integration\Affinity\AffinityConfig.cs ^
    Integration\Affinity\AffinityData.cs ^
    Integration\Affinity\AffinityJsonSerializer.cs ^
    Integration\Affinity\AffinityManager.cs ^
    Integration\Affinity\AffinityManagerPersistenceAndDecay.cs ^
    Integration\Affinity\AffinityManagerStaticCacheReset.cs ^
    Integration\Affinity\Core\INPCGiftConfig.cs ^
    Integration\Affinity\Core\INPCDialogueConfig.cs ^
    Integration\Affinity\Core\INPCRelationshipDialogueConfig.cs ^
    Integration\Affinity\Core\INPCController.cs ^
    Integration\Affinity\Core\INPCShopConfig.cs ^
    Integration\Affinity\Core\INPCGiftContainerConfig.cs ^
    Integration\Affinity\Core\NPCGiftContainerConfigDefaults.cs ^
    Integration\Affinity\Services\NPCGiftContainerService.cs ^
    Integration\Affinity\Systems\NPCGiftSystem.cs ^
    Integration\Affinity\Systems\NPCDialogueSystem.cs ^
    Integration\Affinity\Systems\NPCShopSystem.cs ^
    Integration\Affinity\Systems\NPCAffinityInteractionHelper.cs ^
    Integration\Affinity\AffinityRuntimeHooks.cs ^
    Integration\Affinity\Interactables\NPCInteractableBase.cs ^
    Integration\Affinity\Interactables\NPCGiftInteractable.cs ^
    Integration\Affinity\Interactables\NPCShopInteractable.cs ^
    Integration\Affinity\NPCs\GoblinAffinityConfig.cs ^
    Integration\Affinity\NPCs\NurseAffinityConfig.cs ^
    Integration\Affinity\AffinityUIManager.cs ^
    Integration\Dialogue\DialogueManager.cs ^
    Integration\Dialogue\DialogueActorFactory.cs ^
    Integration\WikiBookItem.cs ^
    Integration\WikiUIManager.cs ^
    Integration\WikiContentManager.cs ^
    Integration\ReverseScale\ReverseScaleConfig.cs ^
    Integration\ReverseScale\ReverseScaleEffectManager.cs ^
    Integration\ReverseScale\ReverseScaleAbilityManager.cs ^
    Integration\ReverseScale\ReverseScaleBootstrap.cs ^
    Integration\ReverseScale\ReverseScaleFactory.cs ^
    Injection\Injection.cs ^
    Achievement\AchievementRuntimeModule.cs ^
    Achievement\AchievementRuntimeHooks.cs ^
    Achievement\BossRushAchievementDef.cs ^
    Achievement\AchievementTracker.cs ^
    Achievement\BossRushAchievementManager.cs ^
    Achievement\AchievementIconLoader.cs ^
    Achievement\SteamAchievementPopup.cs ^
    Achievement\AchievementTriggers.cs ^
    Achievement\AchievementUIStrings.cs ^
    Achievement\AchievementEntryUI.cs ^
    Achievement\AchievementView.cs ^
    Achievement\AchievementMedalConfig.cs ^
    Achievement\AchievementMedalItem.cs ^
    Audio\BossRushAudioHooks.cs ^
    Audio\BossRushAudioManager.cs ^
    DebugAndTools\InventoryInspector.cs ^
    WavesArena\InfiniteHellCashMagnet.cs ^
    Integration\Wedding\NPCMarriageSystem.cs ^
    Integration\Wedding\WeddingChapelInteractable.cs ^
    Integration\Wedding\WeddingBuildingInjector.cs ^
    Integration\Wedding\WeddingBuildingInjector_DataEventsAndRuntime.cs ^
    Integration\Wedding\WeddingModBehaviourBridge.cs ^
    Integration\WishFountain\WishFountainService.cs ^
    Integration\WishFountain\WishFountainConfigAndValidation.cs ^
    Integration\WishFountain\WishFountainRewardPoolBuild.cs ^
    Integration\WishFountain\WishFountainRewardSelection.cs ^
    Integration\WishFountain\WishFountainSendPipeline.cs ^
    Integration\WishFountain\WishFountainInteractable.cs ^
    Integration\WishFountain\WishFountainUI.cs ^
    Integration\WishFountain\WishFountainUIBridge.cs ^
    Integration\WishFountain\WishFountainRewardAnimationView.cs ^
    Integration\WishFountain\WishFountainBuilder.cs ^
    Integration\WishFountain\WishFountainBuilder_DataEventsAndRuntime.cs

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
call :try_game_path "D:\sofrware\steam\steamapps\common\Escape from Duckov"
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
call :try_workshop_path "D:\sofrware\steam\steamapps\workshop\content\3167020"
if defined WORKSHOP_PATH goto :eof
call :try_workshop_path "C:\Program Files (x86)\Steam\steamapps\workshop\content\3167020"
goto :eof

:try_workshop_path
if exist "%~1\%HARMONY_MOD_ID%\0Harmony.dll" (
    for %%P in ("%~1") do set "WORKSHOP_PATH=%%~fP"
)
goto :eof
