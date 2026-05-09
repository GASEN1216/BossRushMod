"""ArchitectureStructureGuard: core architecture helpers must stay wired without gameplay rewrites."""

from pathlib import Path
import re
import sys


COMPILE = Path("compile_official.bat")
MOD = Path("ModBehaviour.cs")
MODED_WAVES = Path("ModeD/ModeDWaves.cs")
MODED_RUNTIME_MODULE = Path("ModeD/ModeDRuntimeModule.cs")
DEBUG_RUNTIME_MODULE = Path("DebugAndTools/DebugToolsRuntimeModule.cs")
DEBUG_RUNTIME_HOOKS = Path("DebugAndTools/DebugToolsRuntimeHooks.cs")

REQUIRED_COMPILE_SOURCES = [
    "Common/Lifecycle/IBossRushRuntimeModule.cs",
    "Common/Lifecycle/SceneRuntimeContext.cs",
    "Common/Lifecycle/BossRushRuntimeModuleHost.cs",
    "Common/Lifecycle/BossRushRuntimeModuleBase.cs",
    "Common/Lifecycle/ArchitectureSentinelRuntimeModule.cs",
    "Common/Lifecycle/BossRushRuntimeModuleRegistration.cs",
    "ModeD/ModeDRuntimeModule.cs",
    "DebugAndTools/DebugToolsRuntimeModule.cs",
    "DebugAndTools/DebugToolsRuntimeHooks.cs",
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
    if "TryFixStuckWaveIfNoModeDEnemyAlive();" in update_body:
        return fail("ArchitectureStructureGuard: ModBehaviour.Update must not call Mode D stuck-wave self-check directly")

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
    for forbidden in [
        "BossPoolLateUpdate();",
        "NPCTeleportUILateUpdate();",
        "F3DebugCheatMenuLateUpdate();",
    ]:
        if forbidden in late_update_body:
            return fail("ArchitectureStructureGuard: ModBehaviour.LateUpdate still directly calls debug tool token: " + forbidden)

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
