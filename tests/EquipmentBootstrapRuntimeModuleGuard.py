"""Bootstrap modules keep existing equipment and scene lifecycle order."""

from pathlib import Path
import sys

from cs_source_util import clean_source


FLIGHT = Path("Integration/FlightTotem/FlightTotemBootstrap.cs")
FLIGHT_FACTORY = Path("Integration/FlightTotem/FlightTotemFactory.cs")
FLIGHT_BRIDGE = Path("Integration/IntegrationHostCompatibility.cs")
FROST = Path("Integration/Frostmourne/FrostmourneBootstrap.cs")
FROST_BRIDGE = Path("Integration/IntegrationHostCompatibility.cs")
REVERSE = Path("Integration/ReverseScale/ReverseScaleBootstrap.cs")
REVERSE_FACTORY = Path("Integration/ReverseScale/ReverseScaleFactory.cs")
REVERSE_CONFIG = Path("Integration/ReverseScale/ReverseScaleConfig.cs")
REVERSE_BRIDGE = Path("Integration/IntegrationHostCompatibility.cs")
SCYTHE = Path("Integration/PhantomWitch/PhantomWitchScytheBootstrap.cs")
SCYTHE_BRIDGE = Path("Integration/IntegrationHostCompatibility.cs")
HALBERD = Path("Integration/DragonKing/Weapons/FenHuangHalberdBootstrap.cs")
HALBERD_BRIDGE = Path("Integration/IntegrationHostCompatibility.cs")
REGISTRATION = Path("ModBehaviourRuntimeModules.cs")
EQUIPMENT = Path("Integration/BossRushIntegration.cs")
SCENE = Path("Integration/BossRushIntegration_StartAndScene.cs")
DEFERRED = Path("Integration/BossRushIntegrationRuntimeModule_DeferredBootstrap.cs")
DEATH_PATCH = Path("Patches/Combat/CharacterOnDeadPatch.cs")
LOOT = Path("WavesArena/WavesArenaRuntimeModule_SpecialLoot.cs")
LOOT_HOST = Path("LootAndRewards/LootAndRewardsSpecialLoot.cs")


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
    paths = (FLIGHT, FLIGHT_FACTORY, FLIGHT_BRIDGE, FROST, FROST_BRIDGE, REVERSE, REVERSE_BRIDGE,
             REVERSE_FACTORY, REVERSE_CONFIG, SCYTHE, SCYTHE_BRIDGE, HALBERD, HALBERD_BRIDGE,
             REGISTRATION, EQUIPMENT, SCENE, DEFERRED, DEATH_PATCH, LOOT, LOOT_HOST)
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
        declaration = "internal sealed " + ("partial " if type_name in ("FlightTotemRuntimeModule", "ReverseScaleRuntimeModule") else "")
        if declaration + "class " + type_name + " : BossRushRuntimeModuleBase" not in module_text:
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
        "initializeItem: () => InitializeFlightTotemItem()",
        "injectLocalization: () => InjectFlightTotemLocalization()",
    ), "FlightTotem initialization")
    if rc:
        return rc
    flight_factory = source[FLIGHT_FACTORY]
    if "internal sealed partial class FlightTotemRuntimeModule" not in flight_factory or \
            "partial class ModBehaviour" in flight_factory:
        return fail("FlightTotem item state and configuration must be owned by its RuntimeModule")
    for field in ("private bool flightTotemInitialized = false;",
                  "private int flightTotemTypeId = FlightConfig.TotemTypeIdBase;",
                  "private Item flightTotemPrefab = null;"):
        if field not in flight_factory or field in source[FLIGHT_BRIDGE]:
            return fail("FlightTotem factory state has lost its single module owner -> " + field)
    for obsolete_bridge in ("InitializeFlightTotemItemFromRuntimeModule", "InjectFlightTotemLocalizationFromRuntimeModule"):
        if obsolete_bridge in flight_text or obsolete_bridge in source[FLIGHT_BRIDGE]:
            return fail("FlightTotem initialization must not bounce through the host -> " + obsolete_bridge)
    rc = ordered(method_body(flight_factory, "private void InitializeFlightTotemItem()"), (
        "if (flightTotemInitialized) return;", "flightTotemInitialized = true;",
        'EquipmentFactory.LoadBundle("flight_totem")', "flightTotemPrefab = GetLoadedFlightTotem();",
        "ConfigureFlightTotemEquipment(flightTotemPrefab);",
    ), "FlightTotem item initialization")
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
        "initializeItem: () => InitializeReverseScaleItem()",
        "injectLocalization: () => InjectReverseScaleLocalization()",
    ), "ReverseScale initialization")
    if rc:
        return rc
    reverse_factory = source[REVERSE_FACTORY]
    if "internal sealed partial class ReverseScaleRuntimeModule" not in reverse_factory or "partial class ModBehaviour" in reverse_factory:
        return fail("ReverseScale factory state must belong to its existing module")
    if "private bool reverseScaleInitialized = false;" not in reverse_factory:
        return fail("ReverseScale initialization latch must stay instance owned")
    rc = ordered(method_body(reverse_factory, "private void InitializeReverseScaleItem()"),
                 ("if (reverseScaleInitialized) return;", "reverseScaleInitialized = true;"), "ReverseScale item initialization")
    if rc:
        return rc
    if "ReverseScaleRuntimeModule.TryConfigureReverseScale(item, baseName);" not in source[REVERSE_CONFIG]:
        return fail("ReverseScale registration must target the module configurator")
    reverse_compatibility = method_body(source[REVERSE_BRIDGE], "public static bool TryConfigureReverseScale(")
    if "return ReverseScaleRuntimeModule.TryConfigureReverseScale(item, baseName);" not in reverse_compatibility:
        return fail("ReverseScale published configurator must preserve its compatibility forward")
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

    for path, bridge, module_name, field, label, coroutine, wait in (
        (SCYTHE, SCYTHE_BRIDGE, "PhantomWitchRuntimeModule", "phantomWitchRuntimeModule", "PhantomWitchScythe",
         "DelayedSetupPhantomWitchScytheAbility", "PhantomWitchScytheSharedWait05sForRuntime"),
        (HALBERD, HALBERD_BRIDGE, "DragonKingRuntimeModule", "dragonKingRuntimeModule", "FenHuangHalberd",
         "DelayedSetupHalberdAbility", "FenHuangHalberdSharedWait05sForRuntime"),
    ):
        module = source[path]
        if "internal sealed partial class " + module_name not in module or "partial class ModBehaviour" in module:
            return fail(label + " bootstrap must belong to its existing content module")
        for verb, parameters, arguments in (("Initialize", "", ""), ("Setup", "UnityEngine.SceneManagement.Scene scene", "scene"), ("Cleanup", "", "")):
            method = verb + label + ("ForScene" if verb == "Setup" else "System")
            declaration = "private void " + method + "(" + parameters + ")"
            expected = declaration + "{" + field + "." + method + "(" + arguments + ");}"
            if "".join(method_body(source[bridge], declaration).split()) != "".join(expected.split()):
                return fail(label + " host entry must be a thin forward -> " + method)
        setup = method_body(module, "internal void Setup" + label + "ForScene(Scene scene)")
        rc = ordered(setup, ("if (ModBehaviour.IsGameplaySceneName(scene.name))", "owner.StartCoroutine(" + coroutine + "());"), label + " scene gate")
        if rc:
            return rc
        delayed = method_body(module, "private IEnumerator " + coroutine + "()")
        for token in ("while (CharacterMainControl.Main == null && waitTime < 15f)",
                      "yield return ModBehaviour." + wait + ";", "waitTime += 0.5f;",
                      "if (!mgr.IsAbilityEnabled)", "mgr.RegisterAbility(player);", "mgr.RebindToCharacter(player);"):
            if token not in delayed:
                return fail(label + " polling/rebind contract missing -> " + token)
        if source[REGISTRATION].count(field + " = new " + module_name + "();") != 1 or source[REGISTRATION].count("runtimeModuleHost.Register(" + field + ");") != 1:
            return fail(label + " must use the already registered module instance")
    rc = ordered(method_body(source[SCYTHE], "internal void CleanupPhantomWitchScytheSystem()"), (
        "PhantomWitchCurseSweatVfx.UnregisterGlobalHook();", "PhantomWitchScytheAbilityManager.CleanupStatic();",
        "PhantomWitchScytheBossDropHandler.Cleanup();", "PhantomWitchCurseRealmVisual.ClearCache();",
        "PhantomWitchAssetManager.ClearCache();"), "PhantomWitchScythe cleanup")
    if rc:
        return rc
    rc = ordered(method_body(source[HALBERD], "internal void CleanupFenHuangHalberdSystem()"), (
        "FenHuangHalberdAbilityManager.CleanupStatic();", "UnityEngine.Object.Destroy(FenHuangComboManager.Instance.gameObject);",
        "DragonFlameMarkTracker.ClearAll();"), "FenHuangHalberd cleanup")
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
    if "wavesArenaRuntime.AddBossSpecialLootToLootboxCoroutine(" not in source[LOOT_HOST]:
        return fail("Frostmourne lootbox delivery bridge was lost")

    print("EquipmentBootstrapRuntimeModuleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
