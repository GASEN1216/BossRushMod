@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ===================================
echo Boss Rush Mod - 鐎规ɑ鏌烝PI缂傛牞鐦ч懘姘拱
echo ===================================
echo.

set OUTPUT_DIR=Build
set MOD_NAME=BossRush
set GAME_PATH=D:\sofrware\steam\steamapps\common\Escape from Duckov
set WORKSHOP_PATH=D:\sofrware\steam\steamapps\workshop\content\3167020
:: HarmonyLoadMod 閸撳秶鐤唌od (閸掓稒鍓板銉ユ綉ID: 3588386576)
:: 閻劍鍩涢棁鈧憰浣筋吂闂冨懏顒漨od: https://steamcommunity.com/sharedfiles/filedetails/?id=3588386576
set HARMONY_MOD_ID=3588386576
echo 濞撳憡鍨欑捄顖氱窞 GAME_PATH=%GAME_PATH%

:: 閸掓稑缂撴潏鎾冲毉閻╊喖缍?
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

echo 濮濓絽婀紓鏍槯 %MOD_NAME%.dll...
echo.

:: 缂傛牞鐦ч崨鎴掓姢 - 娴ｈ法鏁?.NET SDK Roslyn 缂傛牞鐦ч崳銊︽暜閹?C# 7.3
:: 閼奉亜濮╅弻銉﹀閺堚偓閺傛壆娈?.NET SDK
for /f "delims=" %%i in ('dir /b /ad /o-n "C:\Program Files\dotnet\sdk" 2^>nul') do (
    set "DOTNET_SDK=C:\Program Files\dotnet\sdk\%%i"
    goto :found_sdk
)
:found_sdk
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
    Localization\EquipmentLocalization.cs ^
    Common\Utils\ReflectionCache.cs ^
    Common\Utils\NPCBubbleAnimator.cs ^
    Common\Effects\RingParticleEffect.cs ^
    Common\Equipment\EquipmentAbilityConfig.cs ^
    Common\Equipment\EquipmentAbilityAction.cs ^
    Common\Equipment\EquipmentAbilityManager.cs ^
    Common\Equipment\EquipmentEffectManager.cs ^
    Common\Equipment\AbilitySystemHelper.cs ^
    ModBehaviour.cs ^
    ModConfigApi.cs ^
    UIAndSigns\UIAndSigns.cs ^
    DebugAndTools\DebugAndTools.cs ^
    DebugAndTools\ItemSpawner.cs ^
    DebugAndTools\NPCTeleportUI.cs ^
    Integration\BossRushIntegration.cs ^
    Integration\BirthdayCakeItem.cs ^
    Integration\EquipmentFactory.cs ^
    Integration\EquipmentHelper.cs ^
    Integration\Bonus\DragonSetBonus.cs ^
    Integration\Config\DragonSetConfig.cs ^
    Integration\Config\FlightTotemConfig.cs ^
    Integration\Config\DragonKingSetConfig.cs ^
    Utilities\Utilities.cs ^
    Utilities\EntityModelFactory.cs ^
    Utilities\SimpleJsonHelper.cs ^
    Utilities\EnemySpawnCore.cs ^
    Config\Config.cs ^
    Config\NPCSpawnConfig.cs ^
    Config\LootBlacklistRegistry.cs ^
    WavesArena\WavesArena.cs ^
    LootAndRewards\LootAndRewards.cs ^
    Interactables\BossRushInteractables.cs ^
    TeleportDebugMonitor.cs ^
    ModeD\ModeD.cs ^
    ModeD\ModeDEquipment.cs ^
    ModeD\ModeDWaves.cs ^
    ModeD\ModeDInteractables.cs ^
    ModeD\ModeDGlobalLoot.cs ^
    ModeE\ModeE.cs ^
    ModeE\ModeEMerchant.cs ^
    ModeE\ModeESpawnAllocation.cs ^
    ModeE\ModeEBattle.cs ^
    ModeE\FactionFlagConfig.cs ^
    ModeE\ModeEHarmonyPatch.cs ^
    ModeE\RespawnItemConfig.cs ^
    ModeE\ModeERespawnItems.cs ^
    ModeE\RespawnItemUsage.cs ^
    BossFilter\BossFilter.cs ^
    MapSelection\BossRushMapSelectionHelper.cs ^
    Integration\DragonDescendant\DragonDescendantConfig.cs ^
    Integration\DragonDescendant\DragonDescendantAbilities.cs ^
    Integration\DragonDescendant\DragonDescendantBoss.cs ^
    Integration\DragonDescendant\DragonBreathConfig.cs ^
    Integration\DragonDescendant\DragonBreathBuffHandler.cs ^
    Integration\DragonDescendant\DragonBreathWeaponConfig.cs ^
    Integration\DragonKing\DragonKingConfig.cs ^
    Integration\DragonKing\DragonKingAssetManager.cs ^
    Integration\DragonKing\DragonKingAbilityController.cs ^
    Integration\DragonKing\DragonKingShockwaveEffect.cs ^
    Integration\DragonKing\DragonKingBoss.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdIds.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdConfig.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdRuntime.cs ^
    Integration\DragonKing\Weapons\DragonFlameMarkTracker.cs ^
    Integration\DragonKing\Weapons\DragonKingBossGunConfig.cs ^
    Integration\DragonKing\Weapons\DragonKingBossGunRuntime.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdAction.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdAbilityManager.cs ^
    Integration\DragonKing\Weapons\FenHuangComboManager.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdBootstrap.cs ^
    Integration\DragonKing\Weapons\FenHuangHalberdWeaponConfig.cs ^
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
    Integration\Utils\NPCExceptionHandler.cs ^
    Integration\Utils\NPCAssetBundleHelper.cs ^
    Integration\Utils\NPCUIAssetCache.cs ^
    Integration\Utils\NPCHeartBubbleHelper.cs ^
    Integration\Utils\NPCNameTagHelper.cs ^
    Integration\Utils\NPCPathingHelper.cs ^
    Integration\Utils\NPCInteractionGroupHelper.cs ^
    Integration\Utils\NPCCommonUtils.cs ^
    Integration\NPCs\Common\NPCModuleRegistry.cs ^
    Integration\NPCs\Courier\CourierNPC.cs ^
    Integration\NPCs\Goblin\GoblinNPC.cs ^
    Integration\NPCs\Goblin\GoblinNPCController.cs ^
    Integration\NPCs\Goblin\GoblinNPCAnimation.cs ^
    Integration\NPCs\Goblin\GoblinNPCDialogue.cs ^
    Integration\NPCs\Goblin\GoblinNPCReward.cs ^
    Integration\NPCs\Goblin\GoblinMovement.cs ^
    Integration\NPCs\Courier\CourierService.cs ^
    Integration\NPCs\Courier\DepositDataManager.cs ^
    Integration\NPCs\Courier\StorageDepositService.cs ^
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
    Integration\Reforge\ReforgeUIManager.cs ^
    Integration\Reforge\ReforgeDataPersistence.cs ^
    Integration\ItemFactory.cs ^
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
    Integration\UI\ImageViewerUI.cs ^
    Integration\Affinity\INPCAffinityConfig.cs ^
    Integration\Affinity\AffinityConfig.cs ^
    Integration\Affinity\AffinityData.cs ^
    Integration\Affinity\AffinityJsonSerializer.cs ^
    Integration\Affinity\AffinityManager.cs ^
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
    Integration\Affinity\Interactables\NPCInteractableBase.cs ^
    Integration\Affinity\Interactables\NPCGiftInteractable.cs ^
    Integration\Affinity\Interactables\NPCShopInteractable.cs ^
    Integration\Affinity\UI\ConfirmDialogUI.cs ^
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
    Audio\BossRushAudioManager.cs ^
    DebugAndTools\\InventoryInspector.cs ^
    WavesArena\InfiniteHellCashMagnet.cs ^
    Integration\Wedding\NPCMarriageSystem.cs ^
    Integration\Wedding\WeddingChapelInteractable.cs ^
    Integration\Wedding\WeddingBuildingInjector.cs ^
    Integration\Wedding\WeddingModBehaviourBridge.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ===================================
    echo Build succeeded!
    echo ===================================
    echo.
    echo Output file: %OUTPUT_DIR%\%MOD_NAME%.dll
    
    REM Auto deploy to the game mod load path
    copy /Y "%OUTPUT_DIR%\%MOD_NAME%.dll" "%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\%MOD_NAME%.dll" >nul 2>nul
    if errorlevel 1 (
        echo WARNING: Auto deploy failed. Please copy DLL manually.
    ) else (
        echo Deployed to: %GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%\%MOD_NAME%.dll
    )
) else (
    echo.
    echo ===================================
    echo Build failed!
    echo ===================================
    echo Check errors above.
)

pause
