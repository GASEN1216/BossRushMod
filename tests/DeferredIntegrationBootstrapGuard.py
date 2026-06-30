from pathlib import Path
import sys


COMPILE = Path("compile_official.bat")
START_AND_SCENE = Path("Integration/BossRushIntegration_StartAndScene.cs")
DEFERRED = Path("Integration/IntegrationDeferredBootstrap.cs")


def fail(message: str) -> int:
    print("DeferredIntegrationBootstrapGuard: FAIL - " + message)
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


def main() -> int:
    compile_text = COMPILE.read_text(encoding="utf-8", errors="ignore")
    start_text = START_AND_SCENE.read_text(encoding="utf-8", errors="ignore")
    deferred_text = DEFERRED.read_text(encoding="utf-8", errors="ignore")

    for token in [
        "Integration\\IntegrationDeferredBootstrap.cs",
        "SceneLoader.onAfterSceneInitialize += OnAfterSceneInitialize_Integration;",
        "SceneLoader.onAfterSceneInitialize -= OnAfterSceneInitialize_Integration;",
        "CleanupDeferredIntegrationBootstrap_Integration();",
    ]:
        if token not in compile_text + "\n" + start_text:
            return fail("missing token -> " + token)

    start_method = extract_method(start_text, "void Start_Integration()")
    if not start_method:
        return fail("could not find Start_Integration")

    for token in [
        'EnsureIntegrationContentBootstrapScheduled("Start");',
        'ScheduleDeferredSceneSetupForActiveScene("Start");',
    ]:
        if token not in start_method:
            return fail("Start_Integration missing deferred bootstrap token -> " + token)

    forbidden_start_tokens = [
        "InitializeDynamicItems();",
        "InitializeBirthdayCakeItem();",
        "InitializeWikiBookItem();",
        "InjectAdventureJournalIntoShops_Integration();",
        "InjectAchievementMedalIntoShops();",
        "InjectBrickStoneIntoShops();",
        "LoadEquipmentContent();",
        "InitializeEarlyEquipmentAbilitySystems();",
        "InitializeLateEquipmentAbilitySystems();",
    ]
    for token in forbidden_start_tokens:
        if token in start_method:
            return fail("Start_Integration still does heavy direct initialization -> " + token)

    on_scene = extract_method(start_text, "private void OnSceneLoaded_Integration")
    if not on_scene:
        return fail("could not find OnSceneLoaded_Integration")

    if 'ScheduleDeferredSceneSetupForActiveScene("SceneLoaded:" + scene.name);' not in on_scene:
        return fail("scene-loaded path must schedule deferred setup")

    if "ObjectCache.ForceRefresh();" not in on_scene:
        return fail("scene-loaded path must invalidate scene object cache before deferred work")

    forbidden_scene_tokens = [
        "InjectBossRushTicketIntoShops_Integration(scene.name);",
        "InjectAdventureJournalIntoShops_Integration(scene.name);",
        "InjectAchievementMedalIntoShops(scene.name);",
        "InitWeddingBuilding();",
        "RestoreWeddingBuildingNPC();",
        "InitWishFountainBuilding();",
        "RestoreWishFountainBuildings();",
    ]
    for token in forbidden_scene_tokens:
        if token in on_scene:
            return fail("scene-loaded path still does base-scene heavy work directly -> " + token)

    run_bootstrap = extract_method(deferred_text, "private IEnumerator RunIntegrationContentBootstrapWhenReady")
    if not run_bootstrap:
        return fail("missing RunIntegrationContentBootstrapWhenReady")
    for token in [
        "InitializeAlwaysOnDeferredContent",
        "InitializeDynamicItems",
        "InitializeBirthdayCakeItem",
        "InitializeWikiBookItem",
        "InjectAchievementMedalLocalization",
        "LoadEquipmentContent",
        "InitializeEarlyEquipmentAbilitySystems",
        "InitializeLateEquipmentAbilitySystems",
        "SetupFlightTotemForScene",
        "SetupReverseScaleForScene",
        "SetupFenHuangHalberdForScene",
        "SetupFrostmourneForScene",
        "SetupPhantomWitchScytheForScene",
        "SetupNewWeaponsForScene",
    ]:
        if token not in run_bootstrap:
            return fail("deferred content bootstrap missing token -> " + token)

    for token in [
        "integrationEssentialContentFinished = true;",
        'ScheduleRestoreFollowingSpouse(essentialScene.name, "EssentialContentReady");',
        'ScheduleDeferredSceneSetupForActiveScene("EssentialContentReady:" + source);',
    ]:
        if token not in run_bootstrap:
            return fail("deferred content bootstrap missing essential-content token -> " + token)

    if run_bootstrap.find("integrationEssentialContentFinished = true;") > run_bootstrap.find("LoadEquipmentContent"):
        return fail("essential content must become ready before heavy equipment content starts")

    if run_bootstrap.count("IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle)") < 6:
        return fail("scene-bound startup setup must re-check the active scene between split steps")

    deferred_scene = extract_method(deferred_text, "private void ApplyDeferredSceneSetup_Integration")
    if not deferred_scene:
        return fail("missing ApplyDeferredSceneSetup_Integration")
    if "RunDeferredBaseSceneSetup_Integration(sceneName" not in deferred_scene:
        return fail("ApplyDeferredSceneSetup_Integration must schedule the split base-scene coroutine")

    deferred_scene_steps = extract_method(deferred_text, "private IEnumerator RunDeferredBaseSceneSetup_Integration")
    if not deferred_scene_steps:
        return fail("missing RunDeferredBaseSceneSetup_Integration")
    for token in [
        "InjectBossRushTicketIntoShops_Integration(sceneName)",
        "InjectAdventureJournalIntoShops_Integration(sceneName)",
        "InjectAchievementMedalIntoShops(sceneName)",
        "InitWeddingBuilding",
        "RestoreWeddingBuildingNPC",
        "InitWishFountainBuilding",
        "RestoreWishFountainBuildings",
    ]:
        if token not in deferred_scene_steps:
            return fail("deferred scene setup missing token -> " + token)

    if deferred_scene_steps.count("yield return RunDeferredStep_Integration") < 8:
        return fail("base-scene setup should be split across multiple frames")

    run_scene_setup = extract_method(deferred_text, "private IEnumerator RunDeferredSceneSetupForActiveScene")
    if not run_scene_setup:
        return fail("missing RunDeferredSceneSetupForActiveScene")
    if "while (!integrationEssentialContentFinished)" not in run_scene_setup:
        return fail("scene setup must wait on essential content readiness")

    if "appliedDeferredBaseSceneSetupHandle" not in deferred_text:
        return fail("base-scene setup needs same-scene idempotence guard")

    for token in [
        "ShouldContinueDeferredBaseSceneSetup_Integration",
        "ClearDeferredBaseSceneSetup_Integration(sceneHandle)",
    ]:
        if token not in deferred_text:
            return fail("base-scene setup must clear stale coroutine state -> " + token)

    print("DeferredIntegrationBootstrapGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
