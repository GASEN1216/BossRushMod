"""ArchitectureStructureGuard: core architecture helpers must stay wired without gameplay rewrites."""

from pathlib import Path
import re
import sys


COMPILE = Path("compile_official.bat")
MOD = Path("ModBehaviour.cs")
MODED_WAVES = Path("ModeD/ModeDWaves.cs")
MODED_RUNTIME_MODULE = Path("ModeD/ModeDRuntimeModule.cs")
ALWAYS_ON_RUNTIME_HOOKS = Path("Utilities/AlwaysOnRuntimeHooks.cs")
DEBUG_RUNTIME_MODULE = Path("DebugAndTools/DebugToolsRuntimeModule.cs")
DEBUG_RUNTIME_HOOKS = Path("DebugAndTools/DebugToolsRuntimeHooks.cs")
ACHIEVEMENT_RUNTIME_MODULE = Path("Achievement/AchievementRuntimeModule.cs")
ACHIEVEMENT_RUNTIME_HOOKS = Path("Achievement/AchievementRuntimeHooks.cs")
COMMON_NPC_RUNTIME_MODULE = Path("Integration/NPCs/Common/CommonNpcRuntimeModule.cs")
COMMON_NPC_RUNTIME_HOOKS = Path("Integration/NPCs/Common/CommonNpcRuntimeHooks.cs")
EQUIPMENT_RUNTIME_HOOKS = Path("Integration/EquipmentRuntimeHooks.cs")
GAMEPLAY_RUNTIME_HOOKS = Path("Utilities/GameplayRuntimeHooks.cs")
WAVES_RUNTIME_HOOKS = Path("WavesArena/WavesArenaRuntimeHooks.cs")
MODEE_RUNTIME_HOOKS = Path("ModeE/ModeERuntimeHooks.cs")
MODEF_RUNTIME_HOOKS = Path("ModeF/ModeFRuntimeHooks.cs")
ZOMBIE_RUNTIME_HOOKS = Path("ZombieMode/ZombieModeRuntimeHooks.cs")
INTEGRATION = Path("Integration/BossRushIntegration.cs")

REQUIRED_COMPILE_SOURCES = [
    "Common/Lifecycle/IBossRushRuntimeModule.cs",
    "Common/Lifecycle/SceneRuntimeContext.cs",
    "Common/Lifecycle/BossRushRuntimeModuleHost.cs",
    "Common/Lifecycle/BossRushRuntimeModuleBase.cs",
    "Common/Lifecycle/ArchitectureSentinelRuntimeModule.cs",
    "Common/Lifecycle/BossRushRuntimeModuleRegistration.cs",
    "Utilities/AlwaysOnRuntimeHooks.cs",
    "ModeD/ModeDRuntimeModule.cs",
    "DebugAndTools/DebugToolsRuntimeModule.cs",
    "DebugAndTools/DebugToolsRuntimeHooks.cs",
    "Achievement/AchievementRuntimeModule.cs",
    "Achievement/AchievementRuntimeHooks.cs",
    "Integration/NPCs/Common/CommonNpcRuntimeModule.cs",
    "Integration/NPCs/Common/CommonNpcRuntimeHooks.cs",
    "Integration/EquipmentRuntimeHooks.cs",
    "Utilities/GameplayRuntimeHooks.cs",
    "WavesArena/WavesArenaRuntimeModule.cs",
    "WavesArena/WavesArenaRuntimeHooks.cs",
    "ModeE/ModeERuntimeModule.cs",
    "ModeE/ModeERuntimeHooks.cs",
    "ModeF/ModeFRuntimeModule.cs",
    "ModeF/ModeFRuntimeHooks.cs",
    "ZombieMode/ZombieModeRuntimeModule.cs",
    "ZombieMode/ZombieModeRuntimeHooks.cs",
    "Utilities/RuntimeScope.cs",
    "Utilities/SceneRuntimeGate.cs",
]

REQUIRED_MOD_NEEDLES = [
    "private readonly BossRushRuntimeModuleHost runtimeModuleHost = new BossRushRuntimeModuleHost();",
    "RegisterRuntimeModules();",
    "runtimeModuleHost.OnAwake(this);",
    "runtimeModuleHost.OnUpdate(Time.deltaTime, Time.unscaledDeltaTime);",
    "runtimeModuleHost.OnLateUpdate();",
    "runtimeModuleHost.OnStart();",
    "runtimeModuleHost.OnDestroy();",
    "runtimeModuleHost.OnSceneLoaded(new SceneRuntimeContext(scene, mode));",
    "return SceneRuntimeGate.IsBaseHubSceneName(sceneName);",
    "return SceneRuntimeGate.IsGameplaySceneName(sceneName);",
    "return SceneRuntimeGate.CanRunGameplayRuntimeNow(sceneName);",
]


def fail(message: str) -> int:
    print(message)
    return 1


def normalize_slashes(text: str) -> str:
    return re.sub(r"/+", "/", text.replace("\\", "/"))


def extract_method_body(text: str, signature: str) -> str:
    signature_index = text.find(signature)
    if signature_index < 0:
        return ""

    brace_index = text.find("{", signature_index)
    if brace_index < 0:
        return ""

    depth = 0
    for index in range(brace_index, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[brace_index:index + 1]

    return ""


def main() -> int:
    compile_text = normalize_slashes(COMPILE.read_text(encoding="utf-8", errors="ignore"))
    mod_text = MOD.read_text(encoding="utf-8", errors="ignore")

    missing_files = [path for path in REQUIRED_COMPILE_SOURCES if not Path(path).exists()]
    if missing_files:
        return fail("ArchitectureStructureGuard: missing architecture source file(s): " + ", ".join(missing_files))

    missing_compile_entries = [path for path in REQUIRED_COMPILE_SOURCES if path not in compile_text]
    if missing_compile_entries:
        return fail("ArchitectureStructureGuard: compile_official.bat missing architecture source(s): " + ", ".join(missing_compile_entries))

    missing_mod_hooks = [needle for needle in REQUIRED_MOD_NEEDLES if needle not in mod_text]
    if missing_mod_hooks:
        return fail("ArchitectureStructureGuard: ModBehaviour missing architecture hook(s): " + " | ".join(missing_mod_hooks))

    register_index = mod_text.find("RegisterRuntimeModules();")
    awake_index = mod_text.find("runtimeModuleHost.OnAwake(this);")
    if register_index < 0 or awake_index < 0 or register_index > awake_index:
        return fail("ArchitectureStructureGuard: RegisterRuntimeModules must run before runtimeModuleHost.OnAwake")

    registration_text = Path("Common/Lifecycle/BossRushRuntimeModuleRegistration.cs").read_text(encoding="utf-8", errors="ignore")
    if "runtimeModuleHost.Register(new ArchitectureSentinelRuntimeModule());" not in registration_text:
        return fail("ArchitectureStructureGuard: runtime module registration missing ArchitectureSentinelRuntimeModule")
    if "runtimeModuleHost.Register(new ModeDRuntimeModule());" not in registration_text:
        return fail("ArchitectureStructureGuard: runtime module registration missing ModeDRuntimeModule")
    if "runtimeModuleHost.Register(new DebugToolsRuntimeModule());" not in registration_text:
        return fail("ArchitectureStructureGuard: runtime module registration missing DebugToolsRuntimeModule")
    if "runtimeModuleHost.Register(new AchievementRuntimeModule());" not in registration_text:
        return fail("ArchitectureStructureGuard: runtime module registration missing AchievementRuntimeModule")
    if "runtimeModuleHost.Register(new CommonNpcRuntimeModule());" not in registration_text:
        return fail("ArchitectureStructureGuard: runtime module registration missing CommonNpcRuntimeModule")
    for module_name in [
        "WavesArenaRuntimeModule",
        "ModeERuntimeModule",
        "ModeFRuntimeModule",
        "ZombieModeRuntimeModule",
    ]:
        if "runtimeModuleHost.Register(new " + module_name + "());" not in registration_text:
            return fail("ArchitectureStructureGuard: runtime module registration missing " + module_name)

    mode_d_runtime_module = MODED_RUNTIME_MODULE.read_text(encoding="utf-8", errors="ignore")
    if "owner.TickModeDIntegrity(deltaTime);" not in mode_d_runtime_module:
        return fail("ArchitectureStructureGuard: ModeDRuntimeModule must route Mode D integrity ticking through owner wrapper")

    mode_d_waves = MODED_WAVES.read_text(encoding="utf-8", errors="ignore")
    mode_d_tick_body = extract_method_body(mode_d_waves, "internal void TickModeDIntegrity(float deltaTime)")
    if not mode_d_tick_body:
        return fail("ArchitectureStructureGuard: ModeD missing TickModeDIntegrity wrapper")
    if "TryFixStuckWaveIfNoModeDEnemyAlive();" not in mode_d_tick_body:
        return fail("ArchitectureStructureGuard: TickModeDIntegrity must preserve Mode D stuck-wave self-check")

    update_body = extract_method_body(mod_text, "void Update()")
    if not update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update body could not be parsed")

    always_on_hooks = ALWAYS_ON_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")
    always_on_tick_body = extract_method_body(always_on_hooks, "internal void TickAlwaysOnRuntime()")
    if not always_on_tick_body:
        return fail("ArchitectureStructureGuard: AlwaysOnRuntimeHooks missing TickAlwaysOnRuntime wrapper")
    for required in [
        "UpdateMessage();",
        "AffinityManager.UpdateDeferredSave();",
    ]:
        if required not in always_on_tick_body:
            return fail("ArchitectureStructureGuard: TickAlwaysOnRuntime missing token: " + required)
    if always_on_tick_body.find("UpdateMessage();") > always_on_tick_body.find("AffinityManager.UpdateDeferredSave();"):
        return fail("ArchitectureStructureGuard: TickAlwaysOnRuntime must preserve message update before deferred save")
    if "TickAlwaysOnRuntime();" not in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must route always-on runtime through wrapper")
    for forbidden in [
        "UpdateMessage();",
        "AffinityManager.UpdateDeferredSave();",
    ]:
        if forbidden in update_body:
            return fail("ArchitectureStructureGuard: ModBehaviour.Update still directly calls always-on token: " + forbidden)

    always_on_scene_unload_body = extract_method_body(always_on_hooks, "internal void OnSceneUnloadAlwaysOnRuntime()")
    if not always_on_scene_unload_body:
        return fail("ArchitectureStructureGuard: AlwaysOnRuntimeHooks missing OnSceneUnloadAlwaysOnRuntime wrapper")
    for required in [
        "AffinityUIManager.OnSceneUnload();",
        "AffinityManager.OnSceneUnload();",
    ]:
        if required not in always_on_scene_unload_body:
            return fail("ArchitectureStructureGuard: OnSceneUnloadAlwaysOnRuntime missing token: " + required)
    if always_on_scene_unload_body.find("AffinityUIManager.OnSceneUnload();") > always_on_scene_unload_body.find("AffinityManager.OnSceneUnload();"):
        return fail("ArchitectureStructureGuard: OnSceneUnloadAlwaysOnRuntime must preserve UI unload before affinity unload")

    always_on_destroy_body = extract_method_body(always_on_hooks, "internal void CleanupAlwaysOnRuntimeOnDestroy()")
    if not always_on_destroy_body:
        return fail("ArchitectureStructureGuard: AlwaysOnRuntimeHooks missing CleanupAlwaysOnRuntimeOnDestroy wrapper")
    for required in [
        "AffinityManager.OnAffinityChanged -= OnAffinityChanged;",
        "AffinityManager.OnLevelUp -= OnAffinityLevelUp;",
        "AffinityManager.Shutdown();",
        "AffinityUIManager.Cleanup();",
        "EntityModelFactory.Shutdown();",
        'DevLog("[BossRush] [WARNING] EntityModelFactory 卸载异常: " + e.Message);',
    ]:
        if required not in always_on_destroy_body:
            return fail("ArchitectureStructureGuard: CleanupAlwaysOnRuntimeOnDestroy missing token: " + required)
    if always_on_destroy_body.find("AffinityUIManager.Cleanup();") > always_on_destroy_body.find("EntityModelFactory.Shutdown();"):
        return fail("ArchitectureStructureGuard: CleanupAlwaysOnRuntimeOnDestroy must preserve affinity cleanup before entity model shutdown")

    if "TryFixStuckWaveIfNoModeDEnemyAlive();" in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must not call Mode D stuck-wave self-check directly")

    equipment_hooks = EQUIPMENT_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")
    equipment_tick_body = extract_method_body(equipment_hooks, "internal void TickEquipmentAbilityRuntime()")
    if not equipment_tick_body:
        return fail("ArchitectureStructureGuard: EquipmentRuntimeHooks missing TickEquipmentAbilityRuntime wrapper")
    if "UpdateDragonDash();" not in equipment_tick_body:
        return fail("ArchitectureStructureGuard: TickEquipmentAbilityRuntime missing UpdateDragonDash")
    if "TickEquipmentAbilityRuntime();" not in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must route equipment ability tick through wrapper")
    if "UpdateDragonDash();" in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must not directly call UpdateDragonDash")

    gameplay_hooks = GAMEPLAY_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")
    gameplay_tick_body = extract_method_body(gameplay_hooks, "internal void TickGameplaySupportRuntime()")
    if not gameplay_tick_body:
        return fail("ArchitectureStructureGuard: GameplayRuntimeHooks missing TickGameplaySupportRuntime wrapper")
    for required in [
        "UpdateCashMagnet();",
        "UpdateEnemyRecoveryMonitor();",
    ]:
        if required not in gameplay_tick_body:
            return fail("ArchitectureStructureGuard: TickGameplaySupportRuntime missing token: " + required)
    if gameplay_tick_body.find("UpdateCashMagnet();") > gameplay_tick_body.find("UpdateEnemyRecoveryMonitor();"):
        return fail("ArchitectureStructureGuard: TickGameplaySupportRuntime must preserve cash magnet before enemy recovery")
    if "TickGameplaySupportRuntime();" not in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must route gameplay support tick through wrapper")
    for forbidden in [
        "UpdateCashMagnet();",
        "UpdateEnemyRecoveryMonitor();",
    ]:
        if forbidden in update_body:
            return fail("ArchitectureStructureGuard: ModBehaviour.Update still directly calls gameplay support token: " + forbidden)

    gameplay_enemy_recovery_cleanup_body = extract_method_body(gameplay_hooks, "internal void CleanupEnemyRecoveryForSceneChange()")
    if not gameplay_enemy_recovery_cleanup_body:
        return fail("ArchitectureStructureGuard: GameplayRuntimeHooks missing CleanupEnemyRecoveryForSceneChange wrapper")
    if "ClearEnemyRecoveryMonitorState();" not in gameplay_enemy_recovery_cleanup_body:
        return fail("ArchitectureStructureGuard: CleanupEnemyRecoveryForSceneChange missing ClearEnemyRecoveryMonitorState")

    gameplay_scene_prepare_body = extract_method_body(gameplay_hooks, "internal void PrepareSceneRuntimeForLoad()")
    if not gameplay_scene_prepare_body:
        return fail("ArchitectureStructureGuard: GameplayRuntimeHooks missing PrepareSceneRuntimeForLoad wrapper")
    for required in [
        "_characterCacheNeedsRefresh = true;",
        "_characterCacheRefreshTimer = 0f;",
        "_arenaCenterSet = false;",
        "ObjectCache.RefreshIfNeeded();",
    ]:
        if required not in gameplay_scene_prepare_body:
            return fail("ArchitectureStructureGuard: PrepareSceneRuntimeForLoad missing token: " + required)

    gameplay_cash_cleanup_body = extract_method_body(gameplay_hooks, "internal void CleanupCashMagnetForSceneChange()")
    if not gameplay_cash_cleanup_body:
        return fail("ArchitectureStructureGuard: GameplayRuntimeHooks missing CleanupCashMagnetForSceneChange wrapper")
    for required in [
        "ClearCashMagnetState();",
        'DevLog($"[CashMagnet] 场景切换清理异常: {ex.Message}");',
    ]:
        if required not in gameplay_cash_cleanup_body:
            return fail("ArchitectureStructureGuard: CleanupCashMagnetForSceneChange missing token: " + required)

    waves_hooks = WAVES_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")
    waves_tick_body = extract_method_body(waves_hooks, "internal bool TickWavesArenaRuntime(float deltaTime)")
    if not waves_tick_body:
        return fail("ArchitectureStructureGuard: WavesArenaRuntimeHooks missing TickWavesArenaRuntime wrapper")
    for required in [
        "waitingForNextWave && waveCountdown > 0f",
        "waveCountdown -= deltaTime;",
        "GetWaveIntervalSeconds();",
        "ShowNextWaveCountdownBanner(seconds);",
        "SpawnNextEnemy();",
        "TryFixStuckWaveIfNoBossAlive();",
        "return true;",
        "return false;",
    ]:
        if required not in waves_tick_body:
            return fail("ArchitectureStructureGuard: TickWavesArenaRuntime missing token: " + required)

    waves_cleanup_body = extract_method_body(waves_hooks, "internal void TickWavesArenaBossCleanupRuntime(float deltaTime)")
    if not waves_cleanup_body:
        return fail("ArchitectureStructureGuard: WavesArenaRuntimeHooks missing TickWavesArenaBossCleanupRuntime wrapper")
    for required in [
        "daXingXingCleanTimer += deltaTime;",
        "daXingXingCleanTimer >= DaXingXingCleanInterval",
        "TryCleanNonBossRushDaXingXing();",
        "daXingXingCleanTimer = 0f;",
    ]:
        if required not in waves_cleanup_body:
            return fail("ArchitectureStructureGuard: TickWavesArenaBossCleanupRuntime missing token: " + required)

    if "if (TickWavesArenaRuntime(Time.deltaTime))" not in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must route WavesArena tick through wrapper")
    if "TickWavesArenaBossCleanupRuntime(Time.deltaTime);" not in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must route WavesArena cleanup through wrapper")
    for forbidden in [
        "TryFixStuckWaveIfNoBossAlive();",
        "TryCleanNonBossRushDaXingXing();",
        "SpawnNextEnemy();",
    ]:
        if forbidden in update_body:
            return fail("ArchitectureStructureGuard: ModBehaviour.Update still directly calls WavesArena token: " + forbidden)

    mode_e_hooks = MODEE_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")
    mode_e_tick_body = extract_method_body(mode_e_hooks, "internal void TickModeERuntime(float deltaTime)")
    if not mode_e_tick_body:
        return fail("ArchitectureStructureGuard: ModeERuntimeHooks missing TickModeERuntime wrapper")
    for required in [
        "UpdateModeEPlayerNameTag();",
        "modeEIntegrityTimer += deltaTime;",
        "modeEIntegrityTimer >= WaveIntegrityCheckInterval",
        "ModeEIntegrityCheck();",
        "ModeEScalingBatchUpdate();",
        "modeEIntegrityTimer = 0f;",
    ]:
        if required not in mode_e_tick_body:
            return fail("ArchitectureStructureGuard: TickModeERuntime missing token: " + required)

    if "TickModeERuntime(Time.deltaTime);" not in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must route Mode E tick through wrapper")
    for forbidden in [
        "UpdateModeEPlayerNameTag();",
        "ModeEIntegrityCheck();",
        "ModeEScalingBatchUpdate();",
    ]:
        if forbidden in update_body:
            return fail("ArchitectureStructureGuard: ModBehaviour.Update still directly calls Mode E token: " + forbidden)

    mode_f_hooks = MODEF_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")
    mode_f_tick_body = extract_method_body(mode_f_hooks, "internal void TickModeFRuntime(float deltaTime)")
    if not mode_f_tick_body:
        return fail("ArchitectureStructureGuard: ModeFRuntimeHooks missing TickModeFRuntime wrapper")
    for required in [
        "if (modeFActive)",
        "TickModeF(deltaTime);",
    ]:
        if required not in mode_f_tick_body:
            return fail("ArchitectureStructureGuard: TickModeFRuntime missing token: " + required)

    if "TickModeFRuntime(Time.deltaTime);" not in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must route Mode F tick through wrapper")
    if "TickModeF(Time.deltaTime);" in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must not directly call TickModeF")

    zombie_hooks = ZOMBIE_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")
    zombie_tick_body = extract_method_body(zombie_hooks, "internal void TickZombieModeRuntime(float unscaledDeltaTime)")
    if not zombie_tick_body:
        return fail("ArchitectureStructureGuard: ZombieModeRuntimeHooks missing TickZombieModeRuntime wrapper")
    if "TickZombieMode(unscaledDeltaTime);" not in zombie_tick_body:
        return fail("ArchitectureStructureGuard: TickZombieModeRuntime missing TickZombieMode call")

    if "TickZombieModeRuntime(Time.unscaledDeltaTime);" not in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must route ZombieMode tick through wrapper")
    if "TickZombieMode(Time.unscaledDeltaTime);" in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must not directly call TickZombieMode")

    debug_runtime_module = DEBUG_RUNTIME_MODULE.read_text(encoding="utf-8", errors="ignore")
    if "owner.TickDebugTools(deltaTime, unscaledDeltaTime);" not in debug_runtime_module:
        return fail("ArchitectureStructureGuard: DebugToolsRuntimeModule must route update through owner wrapper")
    if "owner.LateUpdateDebugTools();" not in debug_runtime_module:
        return fail("ArchitectureStructureGuard: DebugToolsRuntimeModule must route late update through owner wrapper")

    debug_hooks = DEBUG_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")
    debug_tick_body = extract_method_body(debug_hooks, "internal void TickDebugTools(float deltaTime, float unscaledDeltaTime)")
    if not debug_tick_body:
        return fail("ArchitectureStructureGuard: DebugToolsRuntimeHooks missing TickDebugTools wrapper")
    for required in [
        "UpdateFpsCounter();",
        "UpdateMapClickDebug();",
        "CheckBossPoolWindowHotkey();",
        "CheckItemSpawnerHotkey();",
        "CheckF3DebugCheatMenuHotkey();",
        "TickF3DebugCheatMenu();",
    ]:
        if required not in debug_tick_body:
            return fail("ArchitectureStructureGuard: TickDebugTools missing token: " + required)

    debug_late_body = extract_method_body(debug_hooks, "internal void LateUpdateDebugTools()")
    if not debug_late_body:
        return fail("ArchitectureStructureGuard: DebugToolsRuntimeHooks missing LateUpdateDebugTools wrapper")
    for required in [
        "BossPoolLateUpdate();",
        "NPCTeleportUILateUpdate();",
        "F3DebugCheatMenuLateUpdate();",
    ]:
        if required not in debug_late_body:
            return fail("ArchitectureStructureGuard: LateUpdateDebugTools missing token: " + required)

    debug_after_modal_body = extract_method_body(debug_hooks, "internal void TickDebugToolsAfterModalGate()")
    if not debug_after_modal_body:
        return fail("ArchitectureStructureGuard: DebugToolsRuntimeHooks missing TickDebugToolsAfterModalGate wrapper")
    for required in [
        "BossRushAchievementManager.DebugResetAll();",
        "LogNearbyBuildingInfo(playerPos, 15f);",
        "TogglePlacementMode();",
        "BossRushMapSelectionHelper.ShowBossRushMapSelection();",
        "LogNearbyGameObjects(playerPos, 10f, 30);",
        "UnityEngine.Object.FindObjectsOfType<InteractableBase>(true);",
        "ForceKillAllEnemies();",
        "ToggleNPCTeleportUI();",
        "HideNPCTeleportUI();",
    ]:
        if required not in debug_after_modal_body:
            return fail("ArchitectureStructureGuard: TickDebugToolsAfterModalGate missing token: " + required)

    if "TickDebugToolsAfterModalGate();" not in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must route post-modal debug hotkeys through wrapper")

    debug_destroy_body = extract_method_body(debug_hooks, "internal void CleanupDebugToolsOnDestroy()")
    if not debug_destroy_body:
        return fail("ArchitectureStructureGuard: DebugToolsRuntimeHooks missing CleanupDebugToolsOnDestroy wrapper")
    for required in [
        "OnDestroy_F3DebugCheatMenu();",
        "UnregisterInteractDebugListener();",
        "UnregisterShootDebugListener();",
    ]:
        if required not in debug_destroy_body:
            return fail("ArchitectureStructureGuard: CleanupDebugToolsOnDestroy missing token: " + required)

    for forbidden in [
        "UpdateFpsCounter();",
        "UpdateMapClickDebug();",
        "CheckBossPoolWindowHotkey();",
        "CheckItemSpawnerHotkey();",
        "CheckF3DebugCheatMenuHotkey();",
        "TickF3DebugCheatMenu();",
        "BossRushAchievementManager.DebugResetAll();",
        "LogNearbyBuildingInfo(playerPos, 15f);",
        "TogglePlacementMode();",
        "BossRushMapSelectionHelper.ShowBossRushMapSelection();",
        "LogNearbyGameObjects(playerPos, 10f, 30);",
        "UnityEngine.Object.FindObjectsOfType<InteractableBase>(true);",
        "ForceKillAllEnemies();",
        "ToggleNPCTeleportUI();",
        "HideNPCTeleportUI();",
    ]:
        if forbidden in update_body:
            return fail("ArchitectureStructureGuard: ModBehaviour.Update still directly calls debug tool token: " + forbidden)

    late_update_body = extract_method_body(mod_text, "void LateUpdate()")
    if not late_update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.LateUpdate body could not be parsed")
    zombie_late_body = extract_method_body(zombie_hooks, "internal void LateUpdateZombieModeRuntime()")
    if not zombie_late_body:
        return fail("ArchitectureStructureGuard: ZombieModeRuntimeHooks missing LateUpdateZombieModeRuntime wrapper")
    if "ZombieModeUIHelper.EnforceModalInputPause();" not in zombie_late_body:
        return fail("ArchitectureStructureGuard: LateUpdateZombieModeRuntime missing EnforceModalInputPause")
    if "LateUpdateZombieModeRuntime();" not in late_update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.LateUpdate must route ZombieMode late update through wrapper")
    for forbidden in [
        "BossPoolLateUpdate();",
        "NPCTeleportUILateUpdate();",
        "F3DebugCheatMenuLateUpdate();",
        "ZombieModeUIHelper.EnforceModalInputPause();",
    ]:
        if forbidden in late_update_body:
            return fail("ArchitectureStructureGuard: ModBehaviour.LateUpdate still directly calls debug tool token: " + forbidden)

    achievement_runtime_module = ACHIEVEMENT_RUNTIME_MODULE.read_text(encoding="utf-8", errors="ignore")
    if "owner.TickAchievementRuntime(deltaTime, unscaledDeltaTime);" not in achievement_runtime_module:
        return fail("ArchitectureStructureGuard: AchievementRuntimeModule must route update through owner wrapper")

    achievement_hooks = ACHIEVEMENT_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")
    achievement_init_body = extract_method_body(achievement_hooks, "internal void InitializeAchievementRuntime()")
    if not achievement_init_body:
        return fail("ArchitectureStructureGuard: AchievementRuntimeHooks missing InitializeAchievementRuntime wrapper")
    for required in [
        "InitializeAchievementSystem();",
        "AchievementView.EnsureInstance();",
    ]:
        if required not in achievement_init_body:
            return fail("ArchitectureStructureGuard: InitializeAchievementRuntime missing token: " + required)

    achievement_tick_body = extract_method_body(achievement_hooks, "internal void TickAchievementRuntime(float deltaTime, float unscaledDeltaTime)")
    if not achievement_tick_body:
        return fail("ArchitectureStructureGuard: AchievementRuntimeHooks missing TickAchievementRuntime wrapper")
    for required in [
        "config.achievementHotkey",
        "Duckov.UI.View.ActiveView == null",
        "AchievementView.Instance.Toggle();",
    ]:
        if required not in achievement_tick_body:
            return fail("ArchitectureStructureGuard: TickAchievementRuntime missing token: " + required)

    achievement_cleanup_body = extract_method_body(achievement_hooks, "internal void CleanupAchievementRuntime()")
    if not achievement_cleanup_body:
        return fail("ArchitectureStructureGuard: AchievementRuntimeHooks missing CleanupAchievementRuntime wrapper")
    for required in [
        "Health.OnHurt -= OnPlayerHurtForAchievement;",
        "UnsubscribeAchievementEvents();",
    ]:
        if required not in achievement_cleanup_body:
            return fail("ArchitectureStructureGuard: CleanupAchievementRuntime missing token: " + required)

    awake_body = extract_method_body(mod_text, "void Awake()")
    if not awake_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Awake body could not be parsed")
    if "InitializeAchievementRuntime();" not in awake_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Awake must route achievement initialization through wrapper")
    for forbidden in [
        "InitializeAchievementSystem();",
        "AchievementView.EnsureInstance();",
    ]:
        if forbidden in awake_body:
            return fail("ArchitectureStructureGuard: ModBehaviour.Awake still directly calls achievement token: " + forbidden)

    if "AchievementView.Instance.Toggle();" in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must not directly toggle AchievementView")

    destroy_body = extract_method_body(mod_text, "void OnDestroy()")
    if not destroy_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.OnDestroy body could not be parsed")
    if "CleanupAchievementRuntime();" not in destroy_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.OnDestroy must route achievement cleanup through wrapper")
    if "UnsubscribeAchievementEvents();" in destroy_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.OnDestroy must not directly unsubscribe achievement events")
    if "CleanupDebugToolsOnDestroy();" not in destroy_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.OnDestroy must route debug cleanup through wrapper")
    if "CleanupAlwaysOnRuntimeOnDestroy();" not in destroy_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.OnDestroy must route always-on cleanup through wrapper")
    for forbidden in [
        "OnDestroy_F3DebugCheatMenu();",
        "UnregisterInteractDebugListener();",
        "UnregisterShootDebugListener();",
        "AffinityManager.OnAffinityChanged -= OnAffinityChanged;",
        "AffinityManager.OnLevelUp -= OnAffinityLevelUp;",
        "AffinityManager.Shutdown();",
        "AffinityUIManager.Cleanup();",
        "EntityModelFactory.Shutdown();",
    ]:
        if forbidden in destroy_body:
            return fail("ArchitectureStructureGuard: ModBehaviour.OnDestroy still directly calls always-on cleanup token: " + forbidden)

    scene_loaded_body = extract_method_body(mod_text, "private void OnSceneLoaded(Scene scene, LoadSceneMode mode)")
    if not scene_loaded_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.OnSceneLoaded body could not be parsed")
    for required in [
        "PrepareSceneRuntimeForLoad();",
        "OnSceneUnloadAlwaysOnRuntime();",
        "CleanupEnemyRecoveryForSceneChange();",
        "CleanupCashMagnetForSceneChange();",
    ]:
        if required not in scene_loaded_body:
            return fail("ArchitectureStructureGuard: ModBehaviour.OnSceneLoaded missing wrapper call: " + required)
    for forbidden in [
        "_characterCacheNeedsRefresh = true;",
        "_characterCacheRefreshTimer = 0f;",
        "_arenaCenterSet = false;",
        "ObjectCache.RefreshIfNeeded();",
        "AffinityUIManager.OnSceneUnload();",
        "AffinityManager.OnSceneUnload();",
        "ClearEnemyRecoveryMonitorState();",
        "try { ClearCashMagnetState();",
    ]:
        if forbidden in scene_loaded_body:
            return fail("ArchitectureStructureGuard: ModBehaviour.OnSceneLoaded still directly calls scene-change token: " + forbidden)

    common_npc_runtime_module = COMMON_NPC_RUNTIME_MODULE.read_text(encoding="utf-8", errors="ignore")
    if 'get { return "CommonNPC"; }' not in common_npc_runtime_module:
        return fail("ArchitectureStructureGuard: CommonNpcRuntimeModule missing CommonNPC module name")

    common_npc_hooks = COMMON_NPC_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")
    for signature, required_tokens in {
        "private void SpawnCommonNPCs(string context)": [
            "NPCModuleRegistry.SpawnForCurrentScene(this, context);",
        ],
        "private bool ShouldSpawnCommonNPCsInScene(string sceneName)": [
            "NPCModuleRegistry.ShouldSpawnAnyInScene(this, sceneName);",
        ],
        "private void DestroyCommonNPCs(string context)": [
            "NPCModuleRegistry.DestroyAll(this, context);",
        ],
    }.items():
        body = extract_method_body(common_npc_hooks, signature)
        if not body:
            return fail("ArchitectureStructureGuard: CommonNpcRuntimeHooks missing wrapper: " + signature)
        for required in required_tokens:
            if required not in body:
                return fail("ArchitectureStructureGuard: CommonNpcRuntimeHooks wrapper missing token: " + required)

    if "NPCModuleRegistry.SpawnForCurrentScene(this, context);" in mod_text:
        return fail("ArchitectureStructureGuard: ModBehaviour.cs must not own common NPC spawn registry call")

    integration_text = INTEGRATION.read_text(encoding="utf-8", errors="ignore")
    for forbidden in [
        "NPCModuleRegistry.DestroyAll(this,",
        "NPCModuleRegistry.ShouldSpawnAnyInScene(this,",
    ]:
        if forbidden in integration_text:
            return fail("ArchitectureStructureGuard: BossRushIntegration must route common NPC registry token through wrapper: " + forbidden)

    for module_path, module_name in {
        "WavesArena/WavesArenaRuntimeModule.cs": "WavesArena",
        "ModeE/ModeERuntimeModule.cs": "ModeE",
        "ModeF/ModeFRuntimeModule.cs": "ModeF",
        "ZombieMode/ZombieModeRuntimeModule.cs": "ZombieMode",
    }.items():
        module_text = Path(module_path).read_text(encoding="utf-8", errors="ignore")
        if 'get { return "' + module_name + '"; }' not in module_text:
            return fail("ArchitectureStructureGuard: runtime module shell missing module name: " + module_name)
        if "private ModBehaviour owner;" not in module_text:
            return fail("ArchitectureStructureGuard: runtime module shell must keep owner reference: " + module_name)
        if "owner = null;" not in module_text:
            return fail("ArchitectureStructureGuard: runtime module shell must clear owner on destroy: " + module_name)

    scene_gate = Path("Utilities/SceneRuntimeGate.cs").read_text(encoding="utf-8", errors="ignore")
    for required in [
        'SceneNameEquals(sceneName, "MainMenu")',
        "SceneLoader.IsSceneLoading",
        "MultiSceneCore.Instance",
        "Base_SceneV2",
        "Level_HiddenWarehouse_CellarUnderGround",
    ]:
        if required not in scene_gate:
            return fail("ArchitectureStructureGuard: SceneRuntimeGate missing behavior-preserving token: " + required)

    print("ArchitectureStructureGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
