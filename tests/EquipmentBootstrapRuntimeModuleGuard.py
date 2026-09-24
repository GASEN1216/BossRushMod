"""Bootstrap modules keep existing equipment and scene lifecycle order."""

from pathlib import Path
import sys

from cs_source_util import clean_source


FLIGHT = Path("Integration/FlightTotem/FlightTotemBootstrap.cs")
FLIGHT_BRIDGE = Path("Integration/FlightTotem/FlightTotemRuntimeModuleHostBridge.cs")
FROST = Path("Integration/Frostmourne/FrostmourneBootstrap.cs")
FROST_BRIDGE = Path("Integration/Frostmourne/FrostmourneRuntimeModuleHostBridge.cs")
REVERSE = Path("Integration/ReverseScale/ReverseScaleBootstrap.cs")
REVERSE_BRIDGE = Path("Integration/ReverseScale/ReverseScaleRuntimeModuleHostBridge.cs")
REGISTRATION = Path("ModBehaviourRuntimeModules.cs")
EQUIPMENT = Path("Integration/EquipmentContentRegistry.cs")
SCENE = Path("Integration/BossRushIntegration_StartAndScene.cs")
DEFERRED = Path("Integration/BossRushIntegrationRuntimeModule_DeferredBootstrap.cs")
DEATH_PATCH = Path("Patches/Combat/CharacterOnDeadPatch.cs")
LOOT = Path("LootAndRewards/LootAndRewardsSpecialLoot.cs")


def fail(message: str) -> int:
    print("EquipmentBootstrapRuntimeModuleGuard: FAIL - " + message)
    return 1


def method_body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        return ""
    opening = source.find("{", start)
    if opening < 0:
        return ""
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    return ""


def ordered(source: str, snippets, label: str) -> int:
    positions = [source.find(snippet) for snippet in snippets]
    if min(positions) < 0 or positions != sorted(positions):
        return fail(label + " order changed or missing: " + " -> ".join(snippets))
    return 0


def main() -> int:
    paths = (FLIGHT, FLIGHT_BRIDGE, FROST, FROST_BRIDGE, REVERSE, REVERSE_BRIDGE,
             REGISTRATION, EQUIPMENT, SCENE, DEFERRED, DEATH_PATCH, LOOT)
    for path in paths:
        if not path.exists():
            return fail("missing source -> " + path.as_posix())

    source = {path: clean_source(path.read_text(encoding="utf-8-sig")) for path in paths}
    module_specs = (
        ("FlightTotemRuntimeModule", FLIGHT, FLIGHT_BRIDGE, "flightTotemRuntime", "FlightTotem"),
        ("FrostmourneRuntimeModule", FROST, FROST_BRIDGE, "frostmourneRuntime", "Frostmourne"),
        ("ReverseScaleRuntimeModule", REVERSE, REVERSE_BRIDGE, "reverseScaleRuntime", "ReverseScale"),
    )
    for type_name, module_path, bridge_path, field_name, label in module_specs:
        module_text = source[module_path]
        bridge_text = source[bridge_path]
        if "internal sealed class " + type_name + " : BossRushRuntimeModuleBase" not in module_text:
            return fail(label + " bootstrap must be owned by its RuntimeModule")
        if "public partial class ModBehaviour" in module_text:
            return fail(label + " bootstrap source still declares a host partial")
        if "public override string ModuleName { get { return \"" + label + "\"; } }" not in module_text:
            return fail(label + " RuntimeModule name is missing")
        lifecycle_methods = (
            ("Initialize" + label + "System", "()", "()"),
            ("Setup" + label + "ForScene", "(Scene scene)", "(scene)"),
            ("Cleanup" + label + "System", "()", "()"),
        )
        for method_name, parameters, call_arguments in lifecycle_methods:
            if "private void " + method_name + parameters in module_text:
                return fail(label + " host lifecycle method remains private on the module -> " + method_name)
            declaration = "private void " + method_name + parameters
            forwarded_call = field_name + "." + method_name + call_arguments + ";"
            if declaration not in bridge_text or forwarded_call not in bridge_text:
                return fail(label + " compatibility bridge does not forward -> " + method_name)

        registration_text = source[REGISTRATION]
        assignment = field_name + " = new " + type_name + "();"
        register = "runtimeModuleHost.Register(" + field_name + ");"
        if registration_text.count(assignment) != 1:
            return fail(label + " must have exactly one stored RuntimeModule instance")
        if registration_text.count(register) != 1:
            return fail(label + " stored RuntimeModule must be registered exactly once")
        if registration_text.find(assignment) > registration_text.find(register):
            return fail(label + " RuntimeModule must be stored before registering the same instance")
        if "runtimeModuleHost.Register(new " + type_name + "())" in registration_text:
            return fail(label + " RuntimeModule must not be registered as a second untracked instance")

    flight_text = source[FLIGHT]
    flight_initialize = method_body(flight_text, "internal void InitializeFlightTotemSystem()")
    rc = ordered(flight_initialize, (
        "ensureManagerInstance: () => FlightAbilityManager.EnsureInstance()",
        "ensureEffectManagerInstance: () => FlightTotemEffectManager.EnsureInstance()",
        "initializeItem: () => _owner.InitializeFlightTotemItemFromRuntimeModule()",
        "injectLocalization: () => _owner.InjectFlightTotemLocalizationFromRuntimeModule()",
    ), "FlightTotem initialization")
    if rc:
        return rc
    flight_setup = method_body(flight_text, "internal void SetupFlightTotemForScene(Scene scene)")
    if ("if (ModBehaviour.IsGameplaySceneName(scene.name))" not in flight_setup or
            "delayedCheckEquipment: DelayedCheckFlightTotemEquipment," not in flight_setup or
            "monoBehaviour: _owner" not in flight_setup):
        return fail("FlightTotem scene setup must keep the gameplay gate and host coroutine scheduler")
    if "yield return ModBehaviour.FlightTotemSharedWait05sForRuntime;" not in flight_text:
        return fail("FlightTotem delayed equipment callback must use the original shared half-second wait")

    reverse_text = source[REVERSE]
    reverse_initialize = method_body(reverse_text, "internal void InitializeReverseScaleSystem()")
    rc = ordered(reverse_initialize, (
        "ensureManagerInstance: () => ReverseScaleAbilityManager.EnsureInstance()",
        "ensureEffectManagerInstance: () => ReverseScaleEffectManager.EnsureInstance()",
        "initializeItem: () => _owner.InitializeReverseScaleItemFromRuntimeModule()",
        "injectLocalization: () => _owner.InjectReverseScaleLocalizationFromRuntimeModule()",
    ), "ReverseScale initialization")
    if rc:
        return rc
    reverse_setup = method_body(reverse_text, "internal void SetupReverseScaleForScene(Scene scene)")
    if ("if (ModBehaviour.IsGameplaySceneName(scene.name))" not in reverse_setup or
            "delayedCheckEquipment: DelayedCheckReverseScaleEquipment," not in reverse_setup or
            "monoBehaviour: _owner" not in reverse_setup):
        return fail("ReverseScale scene setup must keep the gameplay gate and host coroutine scheduler")
    if "yield return ModBehaviour.ReverseScaleSharedWait05sForRuntime;" not in reverse_text:
        return fail("ReverseScale delayed equipment callback must use the original shared half-second wait")

    frost_text = source[FROST]
    frost_setup = method_body(frost_text, "internal void SetupFrostmourneForScene(Scene scene)")
    if "if (ModBehaviour.IsGameplaySceneName(scene.name))" not in frost_setup or \
            "_owner.StartCoroutine(DelayedSetupFrostmourneAbility());" not in frost_setup:
        return fail("Frostmourne delayed ability setup must keep its gameplay gate and host scheduler")
    if "while (CharacterMainControl.Main == null && waitTime < 15f)" not in frost_text or \
            "yield return ModBehaviour.FrostmourneSharedWait05sForRuntime;" not in frost_text or \
            "waitTime += 0.5f;" not in frost_text:
        return fail("Frostmourne player readiness polling cadence must stay at 0.5 seconds for at most 15 seconds")
    rc = ordered(method_body(frost_text, "internal void CleanupFrostmourneSystem()"), (
        "FrostmourneAbilityManager.CleanupStatic();",
        "FrostmourneAction.CleanupAllSummonedZombies();",
        "FrostmourneBlueBossDropHandler.Cleanup();",
    ), "Frostmourne cleanup")
    if rc:
        return rc

    # Existing host entry points retain their initialization, scene and cleanup order.
    equipment_text = source[EQUIPMENT]
    rc = ordered(method_body(equipment_text, "private void InitializeEarlyEquipmentAbilitySystems()"),
                 ("InitializeFlightTotemSystem();",), "early equipment initialization")
    if rc:
        return rc
    rc = ordered(method_body(equipment_text, "private void InitializeLateEquipmentAbilitySystems()"), (
        "InitializeReverseScaleSystem();", "InitializeFenHuangHalberdSystem();", "InitializeFrostmourneSystem();"),
        "late equipment initialization")
    if rc:
        return rc
    rc = ordered(method_body(equipment_text, "private void CleanupEquipmentAbilitySystems()"), (
        "CleanupReverseScaleSystem();", "CleanupFenHuangHalberdSystem();", "CleanupFrostmourneSystem();",
        "CleanupFlightTotemSystem();"), "equipment cleanup")
    if rc:
        return rc

    scene_text = source[SCENE]
    scene_body = method_body(scene_text, "private void OnSceneLoaded_Integration")
    rc = ordered(scene_body, ("SetupFlightTotemForScene(scene);", "SetupReverseScaleForScene(scene);",
                              "SetupFrostmourneForScene(scene);"), "scene-loaded setup")
    if rc:
        return rc
    deferred_text = source[DEFERRED]
    rc = ordered(deferred_text, ("RunDeferredStep_Integration(\"SetupFlightTotemForScene\"",
                                 "RunDeferredStep_Integration(\"SetupReverseScaleForScene\"",
                                 "RunDeferredStep_Integration(\"SetupFrostmourneForScene\""),
                 "deferred setup")
    if rc:
        return rc

    # The extra-drop helper stays separate: its Harmony hook and reward-box/world-drop consumers remain stable.
    if "internal static class FrostmourneBlueBossDropHandler" not in frost_text or \
            "private static readonly HashSet<CharacterMainControl> pendingBossRushLootboxDrops" not in frost_text:
        return fail("Frostmourne blue-boss pending-drop helper must remain a separate static owner")
    if "FrostmourneBlueBossDropHandler.TryHandleBlueBossDeath(__instance);" not in source[DEATH_PATCH]:
        return fail("Frostmourne blue-boss death patch call was lost")
    if ("FrostmourneBlueBossDropHandler.TryConsumePendingBossRushLootboxDrop(bossMain, inv);" not in source[LOOT] or
            "FrostmourneBlueBossDropHandler.TryConsumePendingAsWorldDrop(bossMain, position);" not in source[LOOT]):
        return fail("Frostmourne pending drop consumers were lost")

    print("EquipmentBootstrapRuntimeModuleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
