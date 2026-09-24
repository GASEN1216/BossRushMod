from pathlib import Path
import sys

COMPILE = Path("compile_official.bat")
START_AND_SCENE = Path("Integration/BossRushIntegration_StartAndScene.cs")
DEFERRED_HOST = Path("Integration/IntegrationDeferredBootstrap.cs")
DEFERRED_MODULE = Path("Integration/BossRushIntegrationRuntimeModule_DeferredBootstrap.cs")
RUNTIME_MODULE = Path("Integration/BossRushIntegrationRuntimeModule.cs")


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


def require_order(text: str, tokens: list[str], label: str) -> str:
    positions = [text.find(token) for token in tokens]
    for token, position in zip(tokens, positions):
        if position < 0:
            return f"{label} missing token -> {token}"
    if positions != sorted(positions):
        return f"{label} changed required order -> " + " -> ".join(tokens)
    return ""


def main() -> int:
    compile_text = COMPILE.read_text(encoding="utf-8", errors="ignore")
    start_text = START_AND_SCENE.read_text(encoding="utf-8", errors="ignore")
    host_text = DEFERRED_HOST.read_text(encoding="utf-8", errors="ignore")
    module_text = DEFERRED_MODULE.read_text(encoding="utf-8", errors="ignore")
    runtime_text = RUNTIME_MODULE.read_text(encoding="utf-8", errors="ignore")

    for token in [
        "Integration\\IntegrationDeferredBootstrap.cs",
        "Integration\\BossRushIntegrationRuntimeModule_DeferredBootstrap.cs",
    ]:
        if token not in compile_text:
            return fail("compile list missing -> " + token)

    for token in [
        "SceneLoader.onAfterSceneInitialize += OnAfterSceneInitialize_Integration;",
        "SceneLoader.onAfterSceneInitialize -= OnAfterSceneInitialize_Integration;",
        "CleanupDeferredIntegrationBootstrap_Integration();",
    ]:
        if token not in start_text:
            return fail("lifecycle wiring missing -> " + token)

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
        "InitializeDynamicItems();", "InitializeBirthdayCakeItem();", "InitializeWikiBookItem();",
        "InjectAdventureJournalIntoShops_Integration();", "InjectAchievementMedalIntoShops();",
        "InjectBrickStoneIntoShops();", "LoadEquipmentContent();",
        "InitializeEarlyEquipmentAbilitySystems();", "InitializeLateEquipmentAbilitySystems();",
    ]
    for token in forbidden_start_tokens:
        if token in start_method:
            return fail("Start_Integration still has direct deferred work -> " + token)

    on_scene = extract_method(start_text, "private void OnSceneLoaded_Integration")
    if not on_scene:
        return fail("could not find OnSceneLoaded_Integration")
    if 'ScheduleDeferredSceneSetupForActiveScene("SceneLoaded:" + scene.name);' not in on_scene:
        return fail("scene-loaded path must schedule deferred setup")
    if "ObjectCache.ForceRefresh();" not in on_scene:
        return fail("scene-loaded path must refresh scene object cache first")
    for token in [
        "InjectBossRushTicketIntoShops_Integration(scene.name);",
        "InjectAdventureJournalIntoShops_Integration(scene.name);",
        "InjectAchievementMedalIntoShops(scene.name);",
        "InitWeddingBuilding();", "RestoreWeddingBuildingNPC();",
        "InitWishFountainBuilding();", "RestoreWishFountainBuildings();",
    ]:
        if token in on_scene:
            return fail("scene-loaded path still performs deferred base work directly -> " + token)

    host_state = [
        "integrationContentBootstrapStarted", "integrationEssentialContentFinished",
        "integrationContentBootstrapFinished", "integrationContentBootstrapCoroutine",
        "deferredSceneSetupCoroutine", "deferredBaseSceneSetupCoroutine",
        "deferredSceneSetupHandle", "deferredBaseSceneSetupHandle",
        "appliedDeferredBaseSceneSetupHandle",
    ]
    for state in host_state:
        if state in host_text:
            return fail("bootstrap state must be owned by IntegrationRuntimeModule -> " + state)
        if state not in module_text:
            return fail("runtime module missing state -> " + state)

    for signature, target in [
        ("private void EnsureIntegrationContentBootstrapScheduled", "EnsureIntegrationContentBootstrapScheduled(source)"),
        ("private void OnAfterSceneInitialize_Integration", "OnAfterSceneInitialize_Integration(context)"),
        ("private void ScheduleDeferredSceneSetupForActiveScene", "ScheduleDeferredSceneSetupForActiveScene(reason)"),
        ("private void CleanupDeferredIntegrationBootstrap_Integration", "CleanupDeferredIntegrationBootstrap()"),
    ]:
        method = extract_method(host_text, signature)
        if not method or "bossRushIntegrationRuntime." + target not in method:
            return fail("host compatibility entry must be a thin module forward -> " + signature)

    actions_factory = extract_method(host_text, "internal IntegrationDeferredBootstrapActions CreateIntegrationDeferredBootstrapActions")
    if not actions_factory:
        return fail("host must bind its private deferred callbacks once for the runtime module")
    for token in [
        "InitializeDynamicItems = InitializeDynamicItems",
        "InjectBossRushTicketLocalization = InjectBossRushTicketLocalization",
        "InitializeBirthdayCakeItem = InitializeBirthdayCakeItem",
        "InjectBirthdayCakeLocalization = InjectBirthdayCakeLocalization",
        "InitializeWikiBookItem = InitializeWikiBookItem",
        "InjectWikiBookLocalization = InjectWikiBookLocalization",
        "InjectAchievementMedalLocalization = InjectAchievementMedalLocalization",
        "LoadEquipmentContent = LoadEquipmentContent",
        "InitializeEarlyEquipmentAbilitySystems = InitializeEarlyEquipmentAbilitySystems",
        "InitializeLateEquipmentAbilitySystems = InitializeLateEquipmentAbilitySystems",
        "SetupFlightTotemForScene = SetupFlightTotemForScene",
        "SetupReverseScaleForScene = SetupReverseScaleForScene",
        "SetupFenHuangHalberdForScene = SetupFenHuangHalberdForScene",
        "SetupFrostmourneForScene = SetupFrostmourneForScene",
        "SetupPhantomWitchScytheForScene = SetupPhantomWitchScytheForScene",
        "SetupNewWeaponsForScene = SetupNewWeaponsForScene",
        "InjectAchievementMedalIntoShops = InjectAchievementMedalIntoShops",
        "DelayedBirthdayCakeGift = DelayedBirthdayCakeGift",
        "ScheduleWishRewardPoolWarmup = ScheduleWishRewardPoolWarmup",
    ]:
        if token not in actions_factory:
            return fail("private host callback binding missing -> " + token)

    module_lifecycle = extract_method(runtime_text, "public override void OnAwake(ModBehaviour owner)")
    if "_deferredBootstrapActions = owner.CreateIntegrationDeferredBootstrapActions();" not in module_lifecycle:
        return fail("Integration runtime module must capture host callbacks during OnAwake")
    module_destroy = extract_method(runtime_text, "public override void OnDestroy()")
    if "CleanupDeferredIntegrationBootstrap();" not in module_destroy or "_deferredBootstrapActions = null;" not in module_destroy:
        return fail("Integration runtime module must clean deferred coroutines and release callback owner on destroy")

    run_bootstrap = extract_method(module_text, "private IEnumerator RunIntegrationContentBootstrapWhenReady")
    if not run_bootstrap:
        return fail("missing module RunIntegrationContentBootstrapWhenReady")
    order_error = require_order(run_bootstrap, [
        "_owner.InitializeAlwaysOnDeferredContent()",
        "FactoryResourceLoading.InitializeItems(_owner,",
        '"InjectBossRushTicketLocalization"',
        '"Assets/birthday_cake"',
        '"InjectBirthdayCakeLocalization"',
        '"Assets/ui/bossrush_wiki"',
        '"InjectWikiBookLocalization"',
        '"InjectAchievementMedalLocalization"',
        "integrationEssentialContentFinished = true;",
        "_owner.ScheduleRestoreFollowingSpouse(essentialScene.name, \"EssentialContentReady\")",
        'ScheduleDeferredSceneSetupForActiveScene("EssentialContentReady:" + source);',
        "EquipmentFactory.LoadAllEquipmentAsync(_owner)",
        '"LoadEquipmentContent"',
        '"InitializeEarlyEquipmentAbilitySystems"',
        '"InitializeLateEquipmentAbilitySystems"',
        '"ShowcaseTrophyCatalog.Refresh"',
        '"SetupFlightTotemForScene"',
        '"SetupNewWeaponsForScene"',
        "integrationContentBootstrapFinished = true;",
    ], "bootstrap phase ordering")
    if order_error:
        return fail(order_error)

    for token in [
        "InitializeDynamicItems", "InitializeBirthdayCakeItem", "InitializeWikiBookItem",
        "InjectAchievementMedalLocalization", "LoadEquipmentContent",
        "InitializeEarlyEquipmentAbilitySystems", "InitializeLateEquipmentAbilitySystems",
        "SetupFlightTotemForScene", "SetupReverseScaleForScene", "SetupFenHuangHalberdForScene",
        "SetupFrostmourneForScene", "SetupPhantomWitchScytheForScene", "SetupNewWeaponsForScene",
    ]:
        if token not in run_bootstrap:
            return fail("module deferred content bootstrap missing -> " + token)
    if run_bootstrap.count("IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle)") < 6:
        return fail("scene-bound startup setup must re-check the active scene between split steps")

    scheduler = extract_method(module_text, "internal void ScheduleDeferredSceneSetupForActiveScene")
    if not scheduler or "activeScene.IsValid()" not in scheduler or "_owner.StartCoroutine(" not in scheduler:
        return fail("runtime module must own active-scene deferred scheduling")
    if "deferredSceneSetupHandle == activeScene.handle" not in scheduler or "_owner.StopCoroutine(deferredSceneSetupCoroutine)" not in scheduler:
        return fail("scene deferred scheduling must preserve same-scene idempotence and replacement cleanup")

    run_scene_setup = extract_method(module_text, "private IEnumerator RunDeferredSceneSetupForActiveScene")
    if not run_scene_setup or "while (!integrationEssentialContentFinished)" not in run_scene_setup:
        return fail("scene setup must wait on EssentialContentReady")
    if "EnsureIntegrationContentBootstrapScheduled(reason);" not in run_scene_setup or "SceneManager.GetActiveScene().handle != sceneHandle" not in run_scene_setup:
        return fail("scene setup must retry bootstrap scheduling and abort stale scenes")

    deferred_scene = extract_method(module_text, "private void ApplyDeferredSceneSetup_Integration")
    if not deferred_scene or "RunDeferredBaseSceneSetup_Integration(sceneName" not in deferred_scene:
        return fail("ApplyDeferredSceneSetup_Integration must schedule split base-scene restore")
    deferred_scene_steps = extract_method(module_text, "private IEnumerator RunDeferredBaseSceneSetup_Integration")
    if not deferred_scene_steps:
        return fail("missing RunDeferredBaseSceneSetup_Integration")
    order_error = require_order(deferred_scene_steps, [
        "InjectBossRushTicketIntoShops_Integration(sceneName)",
        "InjectAdventureJournalIntoShops_Integration(sceneName)",
        "InjectAchievementMedalIntoShops(sceneName)",
        "AwenCourierTokenConfig.InjectIntoShops(sceneName)",
        "InjectBrickStoneIntoShops(sceneName)",
        "ZombieTideInvitationConfig.InjectIntoShops(sceneName)",
        "FactionFlagConfig.InjectIntoShops(sceneName)",
        "BloodhuntTransponderConfig.InjectIntoShops(sceneName)",
        "FateEchoRelicConfig.InjectIntoShops(sceneName)",
        "InjectCodexBookIntoShops(sceneName)",
        "DelayedBirthdayCakeGift()",
        '"Assets/buildings/weddingchapel"',
        "RestoreWeddingBuildingNPC()",
        '"Assets/buildings/starwish_fountain"',
        "RestoreWishFountainBuildings()",
        '"Assets/buildings/petnest_relic_nest"',
        "RestorePetNestBuildings()",
        '"Assets/buildings/bossrush_daily_mailbox"',
        "RestoreDailyReportMailboxes()",
        '"Assets/buildings/bossrush_campaign_board"',
        '"Assets/buildings/bossrush_backmountain_showcase"',
        "RegisterCampaignNotesForScene()",
        "ScheduleWishRewardPoolWarmup()",
    ], "base-scene restore order")
    if order_error:
        return fail(order_error)
    if deferred_scene_steps.count("yield return RunDeferredStep_Integration") < 8:
        return fail("base-scene setup must stay split across multiple frames")
    if deferred_scene_steps.count("ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle)") < 10:
        return fail("base-scene restore must stop after scene changes between operations")

    if "appliedDeferredBaseSceneSetupHandle" not in module_text:
        return fail("base-scene setup needs same-scene idempotence state")
    for token in ["ShouldContinueDeferredBaseSceneSetup_Integration", "ClearDeferredBaseSceneSetup_Integration(sceneHandle)"]:
        if token not in module_text:
            return fail("base-scene setup must clear stale coroutine state -> " + token)

    print("DeferredIntegrationBootstrapGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
