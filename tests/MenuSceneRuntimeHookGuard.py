"""Guard: scene-loaded hooks use stable scene names; per-frame hooks also respect loading state."""

from pathlib import Path
import sys


MOD_BEHAVIOUR = Path("ModBehaviour.cs")
SCENE_RUNTIME_GATE = Path("Utilities/SceneRuntimeGate.cs")
INTEGRATION = Path("Integration/BossRushIntegration.cs")
ALWAYS_ON_RUNTIME_HOOKS = Path("Utilities/AlwaysOnRuntimeHooks.cs")
EQUIPMENT_RUNTIME_HOOKS = Path("Integration/EquipmentRuntimeHooks.cs")
STEAM_ACHIEVEMENT_POPUP = Path("Achievement/SteamAchievementPopup.cs")
BOOTSTRAP_FILES = [
    (
        Path("Integration/FlightTotem/FlightTotemBootstrap.cs"),
        "private void SetupFlightTotemForScene",
        "delayedCheckEquipment: DelayedCheckFlightTotemEquipment,",
    ),
    (
        Path("Integration/DragonKing/Weapons/FenHuangHalberdBootstrap.cs"),
        "private void SetupFenHuangHalberdForScene",
        "StartCoroutine(DelayedSetupHalberdAbility());",
    ),
    (
        Path("Integration/Frostmourne/FrostmourneBootstrap.cs"),
        "private void SetupFrostmourneForScene",
        "StartCoroutine(DelayedSetupFrostmourneAbility());",
    ),
    (
        Path("Integration/PhantomWitch/PhantomWitchScytheBootstrap.cs"),
        "private void SetupPhantomWitchScytheForScene",
        "StartCoroutine(DelayedSetupPhantomWitchScytheAbility());",
    ),
]
PER_FRAME_MANAGER_METHODS = [
    (
        Path("Integration/DragonKing/Weapons/FenHuangHalberdAbilityManager.cs"),
        "protected override void Update()",
    ),
    (
        Path("Integration/DragonKing/Weapons/FenHuangHalberdAbilityManager.cs"),
        "private void LateUpdate()",
    ),
    (
        Path("Integration/DragonKing/Weapons/FenHuangComboManager.cs"),
        "void Update()",
    ),
    (
        Path("Integration/FlightTotem/FlightAbilityManager.cs"),
        "protected override void Update()",
    ),
    (
        Path("Integration/Frostmourne/FrostmourneAbilityManager.cs"),
        "protected override void Update()",
    ),
    (
        Path("Integration/Frostmourne/FrostmourneAbilityManager.cs"),
        "private void LateUpdate()",
    ),
    (
        Path("Integration/PhantomWitch/PhantomWitchScytheAbilityManager.cs"),
        "protected override void Update()",
    ),
    (
        Path("Integration/PhantomWitch/PhantomWitchScytheAbilityManager.cs"),
        "private void LateUpdate()",
    ),
]
SCENE_LOADED_MANAGER_METHODS = [
    (
        Path("Integration/ReverseScale/ReverseScaleAbilityManager.cs"),
        "private void OnSceneLoaded",
        "StartCoroutine(DelayedRebindCharacter());",
    ),
    (
        Path("Integration/ReverseScale/ReverseScaleEffectManager.cs"),
        "protected override void OnSceneLoaded",
        "StartCoroutine(DelayedCheckEquipment());",
    ),
]
MENU_FRAME_COMPONENT_METHODS = [
    (
        Path("DebugAndTools/InventoryInspector.cs"),
        "private void Update()",
    ),
    (
        Path("DebugAndTools/InventoryInspector.cs"),
        "private void OnGUI()",
    ),
]


def fail(message: str) -> int:
    print("MenuSceneRuntimeHookGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def has_recent_guard(block: str, call: str, guard: str, window: int = 500) -> bool:
    pos = block.find(call)
    if pos < 0:
        return False
    search_start = max(0, pos - window)
    return guard in block[search_start:pos]


def main() -> int:
    mod_text = MOD_BEHAVIOUR.read_text(encoding="utf-8", errors="ignore")
    scene_gate_text = SCENE_RUNTIME_GATE.read_text(encoding="utf-8", errors="ignore")
    integration_text = INTEGRATION.read_text(encoding="utf-8", errors="ignore")
    always_on_runtime_text = ALWAYS_ON_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")
    equipment_runtime_text = EQUIPMENT_RUNTIME_HOOKS.read_text(encoding="utf-8", errors="ignore")

    if "IsGameplaySceneName" not in mod_text:
        return fail("missing stable gameplay scene-name helper")
    scene_guard = extract_method(scene_gate_text, "internal static bool IsGameplaySceneName")
    if not scene_guard:
        return fail("could not find stable gameplay scene-name helper body")
    runtime_guard = extract_method(scene_gate_text, "internal static bool CanRunGameplayRuntimeNow")
    if not runtime_guard:
        return fail("could not find gameplay runtime loading-state helper body")
    compatibility_guard = extract_method(mod_text, "internal static bool ShouldRunGameplaySceneRuntimeHooks")
    if not compatibility_guard:
        return fail("could not find legacy runtime hook helper body")

    for token in [
        "GameplayDataSettings.SceneManagement.MainMenuScene.Name",
    ]:
        if token not in scene_guard:
            return fail("scene-name guard must keep explicit menu/loading scene exclusions -> " + token)
    for token in [
        "SceneLoader.IsSceneLoading",
        "MultiSceneCore.Instance",
        "multiSceneCore.IsLoading",
    ]:
        if token in scene_guard:
            return fail("stable scene-name guard must not depend on transient loading state -> " + token)
        if token not in runtime_guard:
            return fail("runtime guard must still respect transient loading state -> " + token)
    if "IsGameplaySceneName(sceneName)" not in runtime_guard:
        return fail("runtime guard must delegate stable scene-name filtering to IsGameplaySceneName")
    if "CanRunGameplayRuntimeNow(sceneName)" not in compatibility_guard:
        return fail("legacy runtime hook helper must delegate to CanRunGameplayRuntimeNow")
    for signature, delegate_call in [
        ("internal static bool IsGameplaySceneName", "return SceneRuntimeGate.IsGameplaySceneName(sceneName);"),
        ("internal static bool CanRunGameplayRuntimeNow", "return SceneRuntimeGate.CanRunGameplayRuntimeNow(sceneName);"),
    ]:
        wrapper = extract_method(mod_text, signature)
        if delegate_call not in wrapper:
            return fail("ModBehaviour scene helper must remain a compatibility wrapper -> " + signature)
    for token in [
        "IndexOf(\"Menu\", StringComparison.OrdinalIgnoreCase)",
        "IndexOf(\"Loading\", StringComparison.OrdinalIgnoreCase)",
        ".Contains(\"Menu\")",
        ".Contains(\"Loading\")",
    ]:
        if token in scene_guard:
            return fail("scene guard must not use broad scene-name substring filters -> " + token)

    on_scene = extract_method(integration_text, "private void OnSceneLoaded_Integration")
    if not on_scene:
        return fail("could not find OnSceneLoaded_Integration")
    if "bool isGameplayScene = IsGameplaySceneName(scene.name);" not in on_scene:
        return fail("OnSceneLoaded_Integration must cache the stable gameplay scene-name guard")

    start_integration = extract_method(integration_text, "void Start_Integration()")
    if not start_integration:
        return fail("could not find Start_Integration")
    if not has_recent_guard(
        start_integration,
        "StartCoroutine(FindInteractionTargets(5));",
        "if (CanRunGameplayRuntimeNow(SceneManager.GetActiveScene().name))",
        window=300,
    ):
        return fail("startup interaction scan should not run in menu/loading scenes")

    gameplay_hook_calls = [
        "StartCoroutine(DelayedRestoreReforgeDataForInventory());",
        "StartCoroutine(DelayedSubscribeDragonBreathEvents());",
        "StartCoroutine(DelayedApplyDragonGunAmmoOverride());",
    ]
    for call in gameplay_hook_calls:
        if not has_recent_guard(on_scene, call, "if (isGameplayScene)"):
            return fail("scene-loaded gameplay hook must use stable scene-name guard -> " + call)

    if not has_recent_guard(
        on_scene,
        "StartCoroutine(FindInteractionTargets(10));",
        "if (isGameplayScene)",
        window=300,
    ):
        return fail("fallback interaction scan should not run in menu/loading scenes")

    for path, signature, delayed_call in BOOTSTRAP_FILES:
        text = path.read_text(encoding="utf-8", errors="ignore")
        method = extract_method(text, signature)
        if not method:
            return fail("could not find bootstrap method -> " + str(path))
        if not has_recent_guard(
            method,
            delayed_call,
            "if (IsGameplaySceneName(scene.name))",
            window=800,
        ):
            return fail("equipment delayed setup must be guarded in " + str(path))

    monitor = extract_method(integration_text, "private System.Collections.IEnumerator MonitorLateRuntimeStateRestore")
    if not monitor:
        return fail("could not find MonitorLateRuntimeStateRestore")
    guard_pos = monitor.find("if (!CanRunGameplayRuntimeNow(SceneManager.GetActiveScene().name))")
    storage_pos = monitor.find("PlayerStorage.Inventory")
    if guard_pos < 0 or storage_pos < 0 or guard_pos > storage_pos:
        return fail("runtime-state monitor must skip player/storage checks outside gameplay scenes")

    update = extract_method(mod_text, "void Update()")
    if not update:
        return fail("could not find ModBehaviour.Update")
    update_guard = update.find("if (!runGameplaySceneHooks)")
    equipment_runtime_tick = update.find("TickEquipmentAbilityRuntime();")
    always_on_runtime_tick = update.find("TickAlwaysOnRuntime();")
    if "bool runGameplaySceneHooks = CanRunGameplayRuntimeNow(SceneManager.GetActiveScene().name);" not in update:
        return fail("ModBehaviour.Update must cache the gameplay-scene guard")
    if update_guard < 0 or equipment_runtime_tick < 0 or update_guard > equipment_runtime_tick:
        return fail("ModBehaviour.Update must return before gameplay per-frame work in menu/loading scenes")
    if always_on_runtime_tick < 0 or always_on_runtime_tick > update_guard:
        return fail("always-on runtime tick must remain before the menu/loading early return")

    always_on_runtime_tick_block = extract_method(always_on_runtime_text, "internal void TickAlwaysOnRuntime()")
    if not always_on_runtime_tick_block:
        return fail("could not find TickAlwaysOnRuntime")
    if "AffinityManager.UpdateDeferredSave();" not in always_on_runtime_tick_block:
        return fail("Affinity deferred save must remain in the always-on runtime tick")

    equipment_runtime_tick_block = extract_method(equipment_runtime_text, "internal void TickEquipmentAbilityRuntime()")
    if not equipment_runtime_tick_block:
        return fail("could not find TickEquipmentAbilityRuntime")
    if "UpdateDragonDash();" not in equipment_runtime_tick_block:
        return fail("dragon dash must remain in the gameplay equipment runtime tick")

    for signature in ["void OnGUI()", "void LateUpdate()"]:
        block = extract_method(mod_text, signature)
        if not block:
            return fail("could not find ModBehaviour." + signature)
        if "if (!CanRunGameplayRuntimeNow(SceneManager.GetActiveScene().name))" not in block:
            return fail("ModBehaviour." + signature + " must skip menu/loading scenes")

    manager_guard = "if (!ModBehaviour.CanRunGameplayRuntimeNow(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))"
    for path, signature in PER_FRAME_MANAGER_METHODS:
        text = path.read_text(encoding="utf-8", errors="ignore")
        block = extract_method(text, signature)
        if not block:
            return fail("could not find per-frame manager method -> " + str(path) + " " + signature)
        if manager_guard not in block:
            return fail("per-frame equipment manager method must skip menu/loading scenes -> " + str(path) + " " + signature)

    scene_loaded_guard = "if (!ModBehaviour.IsGameplaySceneName(scene.name))"
    for path, signature, delayed_call in SCENE_LOADED_MANAGER_METHODS:
        text = path.read_text(encoding="utf-8", errors="ignore")
        block = extract_method(text, signature)
        if not block:
            return fail("could not find scene-loaded manager method -> " + str(path) + " " + signature)
        if not has_recent_guard(block, delayed_call, scene_loaded_guard, window=500):
            return fail("scene-loaded equipment manager method must skip delayed player polling in menu/loading scenes -> " + str(path))

    for path, signature in MENU_FRAME_COMPONENT_METHODS:
        text = path.read_text(encoding="utf-8", errors="ignore")
        block = extract_method(text, signature)
        if not block:
            return fail("could not find menu frame component method -> " + str(path) + " " + signature)
        if manager_guard not in block:
            return fail("dev/debug frame component must skip menu/loading scenes -> " + str(path) + " " + signature)

    popup_update = extract_method(
        STEAM_ACHIEVEMENT_POPUP.read_text(encoding="utf-8", errors="ignore"),
        "void Update()",
    )
    if not popup_update:
        return fail("could not find SteamAchievementPopup.Update")
    empty_popup_guard = popup_update.find("if (activePopups.Count == 0)")
    update_all_popups = popup_update.find("UpdateAllPopups();")
    if empty_popup_guard < 0 or update_all_popups < 0 or empty_popup_guard > update_all_popups:
        return fail("SteamAchievementPopup.Update must return before empty popup work")

    print("MenuSceneRuntimeHookGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
