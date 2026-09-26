#!/usr/bin/env python3
"""商店注入与库存存档状态必须由唯一 IntegrationRuntimeModule 持有。"""

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source


ROOT = Path(__file__).resolve().parent.parent
MODULE_PATH = ROOT / "Integration/BossRushIntegrationRuntimeModule.cs"
MODULE_RUNTIME_HOOKS_PATH = ROOT / "Integration/BossRushIntegrationRuntimeModule_RuntimeHooks.cs"
MODULE_MAP_OBJECTS_PATH = ROOT / "Integration/BossRushIntegrationRuntimeModule_MapObjects.cs"
MODULE_TRAVEL_PATH = ROOT / "Integration/BossRushIntegrationRuntimeModule_Travel.cs"
MODULE_INITIALIZATION_PATH = ROOT / "Integration/BossRushIntegrationRuntimeModule_Initialization.cs"
MODULE_CODEX_BOOK_PATH = ROOT / "Integration/BossRushIntegrationRuntimeModule_CodexBook.cs"
MODULE_SCENE_LIFECYCLE_PATH = ROOT / "Integration/BossRushIntegrationRuntimeModule_SceneLifecycle.cs"
HOST_PATH = ROOT / "Integration/BossRushIntegration.cs"
CODEX_BOOK_HOST_PATH = ROOT / "Integration/Codex/CodexBookItem.cs"
LIFECYCLE_PATH = ROOT / "Integration/BossRushIntegration_StartAndScene.cs"
MAP_HOST_PATH = ROOT / "Integration/BossRushIntegration_MapObjectsAndDragonBreath.cs"
TRAVEL_HOST_PATH = ROOT / "Integration/BossRushIntegration_TravelAndSetup.cs"
REGISTRATION_PATH = ROOT / "ModBehaviourRuntimeModules.cs"
COMPILE_PATH = ROOT / "compile_official.bat"


def fail(message):
    print("IntegrationRuntimeModuleGuard: FAIL: " + message)
    return 1


def method_body(source, signature):
    start = source.find(signature)
    if start < 0:
        return ""
    opening = source.find("{", start + len(signature))
    if opening < 0:
        return ""

    depth = 0
    quoted = None
    escaped = False
    for index in range(opening, len(source)):
        char = source[index]
        if quoted is not None:
            if escaped:
                escaped = False
            elif char == "\\" and quoted == '"':
                escaped = True
            elif char == quoted:
                quoted = None
            continue

        if char in ('"', "'"):
            quoted = char
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:index]
    return ""


def require_order(body, tokens, label):
    positions = [body.find(token) for token in tokens]
    if any(position < 0 for position in positions):
        missing = [token for token, position in zip(tokens, positions) if position < 0]
        return label + " missing: " + ", ".join(missing)
    if positions != sorted(positions):
        return label + " changed required order: " + " -> ".join(tokens)
    return ""


def main():
    module_core = clean_source(MODULE_PATH.read_text(encoding="utf-8", errors="ignore"))
    module_runtime_hooks = clean_source(MODULE_RUNTIME_HOOKS_PATH.read_text(encoding="utf-8", errors="ignore"))
    module_map_objects = clean_source(MODULE_MAP_OBJECTS_PATH.read_text(encoding="utf-8", errors="ignore"))
    module_travel = clean_source(MODULE_TRAVEL_PATH.read_text(encoding="utf-8", errors="ignore"))
    module_initialization = clean_source(MODULE_INITIALIZATION_PATH.read_text(encoding="utf-8", errors="ignore"))
    module_codex_book = clean_source(MODULE_CODEX_BOOK_PATH.read_text(encoding="utf-8", errors="ignore"))
    module_scene_lifecycle = clean_source(MODULE_SCENE_LIFECYCLE_PATH.read_text(encoding="utf-8", errors="ignore"))
    module = module_core + "\n" + module_runtime_hooks + "\n" + module_map_objects + "\n" + module_travel + "\n" + module_initialization + "\n" + module_codex_book + "\n" + module_scene_lifecycle
    host = clean_source(HOST_PATH.read_text(encoding="utf-8", errors="ignore"))
    codex_book_host = clean_source(CODEX_BOOK_HOST_PATH.read_text(encoding="utf-8", errors="ignore"))
    lifecycle = clean_source(LIFECYCLE_PATH.read_text(encoding="utf-8", errors="ignore"))
    map_host = clean_source(MAP_HOST_PATH.read_text(encoding="utf-8", errors="ignore"))
    travel_host = clean_source(TRAVEL_HOST_PATH.read_text(encoding="utf-8", errors="ignore"))
    registration = clean_source(REGISTRATION_PATH.read_text(encoding="utf-8", errors="ignore"))
    compile_text = COMPILE_PATH.read_text(encoding="utf-8-sig", errors="ignore").replace("\\", "/")

    if module_core.count("internal sealed partial class IntegrationRuntimeModule : BossRushRuntimeModuleBase") != 1:
        return fail("the dedicated IntegrationRuntimeModule core must retain its single registered base declaration")
    if module_runtime_hooks.count("internal sealed partial class IntegrationRuntimeModule") != 1:
        return fail("runtime hooks must be a single partial declaration of the registered module type")
    if module_map_objects.count("internal sealed partial class IntegrationRuntimeModule") != 1:
        return fail("map-object behavior must be a single partial declaration of the registered module type")
    if module_travel.count("internal sealed partial class IntegrationRuntimeModule") != 1:
        return fail("travel behavior must be a single partial declaration of the registered module type")
    if module_initialization.count("internal sealed partial class IntegrationRuntimeModule") != 1:
        return fail("initialization and content wiring must be a single partial declaration of the registered module type")
    if module_codex_book.count("internal sealed partial class IntegrationRuntimeModule") != 1:
        return fail("Codex Book stock behavior must be a single partial declaration of the registered module type")
    if module_scene_lifecycle.count("internal sealed partial class IntegrationRuntimeModule") != 1:
        return fail("scene lifecycle scheduling must be a single partial declaration of the registered module type")
    if "partial class ModBehaviour" in module:
        return fail("the runtime module source must not declare a ModBehaviour partial")
    if "private ModBehaviour _owner;" not in module or "_owner = owner;" not in method_body(module, "public override void OnAwake(ModBehaviour owner)"):
        return fail("OnAwake must bind the module to its registered host")

    owned_fields = (
        "cachedTicketStock", "injectedTicketEntry", "cachedJournalStock", "injectedJournalEntry",
        "cachedBrickStoneStock", "injectedBrickStoneEntry", "integrationNextWarningLogTimes",
        "cachedIntegrationStockShops", "cachedIntegrationStockShopsSceneName",
        "_purchaseEventsSubscribed", "_dragonBreathEffectEventSubscribed", "_cachedMainCharForEffect",
        "_runtimeStateMonitorCoroutine", "_item105PurchaseCount", "cachedCodexBookStock",
        "injectedCodexBookEntry", "_codexBookStockEventsSubscribed",
    )
    for field in owned_fields:
        if field not in module or field in host:
            return fail("IntegrationRuntimeModule must be the sole owner of " + field)
    if "cachedCodexBookStock" in codex_book_host or "injectedCodexBookEntry" in codex_book_host:
        return fail("Codex Book stock state must not remain in the host partial")
    if "private static int cachedTicketStock" not in module or "private static int cachedJournalStock" not in module or "private static int cachedBrickStoneStock" not in module:
        return fail("the save-backed stock cache fields must remain static")
    if "private static int cachedCodexBookStock" not in module_codex_book or "private static StockShop.Entry injectedCodexBookEntry" not in module_codex_book:
        return fail("Codex Book stock cache and entry reference must be private module-owned state")

    stock_event_suffixes = {
        "Ticket": "TicketStock",
        "Journal": "JournalStock",
        "BrickStone": "BrickStoneStock",
        "CodexBook": "CodexBookStock",
    }
    for name, flag in (
        ("Ticket", "_ticketStockEventsSubscribed"),
        ("Journal", "_journalStockEventsSubscribed"),
        ("BrickStone", "_brickStoneStockEventsSubscribed"),
        ("CodexBook", "_codexBookStockEventsSubscribed"),
    ):
        subscribe = method_body(module, "internal void Subscribe" + name + "StockEvents()")
        unsubscribe = method_body(module, "internal void Unsubscribe" + name + "StockEvents()")
        if not subscribe or "if (" + flag + ") return;" not in subscribe or flag + " = true;" not in subscribe:
            return fail(name + " save event subscription must be idempotent and owned by the module")
        if not unsubscribe or "if (!" + flag + ") return;" not in unsubscribe or flag + " = false;" not in unsubscribe:
            return fail(name + " save event unsubscription must release only its recorded delegate")
        suffix = stock_event_suffixes[name]
        if "SavesSystem.OnCollectSaveData += OnCollectSaveData_" + suffix not in subscribe:
            return fail(name + " OnCollectSaveData delegate must be subscribed by its module owner")
        if "SavesSystem.OnSetFile += OnSetFile_" + suffix not in subscribe:
            return fail(name + " OnSetFile delegate must be subscribed by its module owner")

    codex_try_inject = method_body(module_codex_book, "internal bool TryInjectCodexBookIntoShop(StockShop shop)")
    if require_order(codex_try_inject, [
        "if (!IsBaseHubNormalMerchantShop(shop))", "foreach (StockShop.Entry entry in shop.entries)",
        "if (alreadyExists)", "float priceFactor = 1f;", "int stockToSet = LoadCodexBookStockFromSave();",
        "wrapped.CurrentStock = stockToSet;", "wrapped.Show = true;", "injectedCodexBookEntry = wrapped;",
        "shop.entries.Insert(0, wrapped);",
    ], "TryInjectCodexBookIntoShop"):
        return fail("Codex Book injection must keep the base-hub filter, stock restore, visibility, and front insertion order")
    for token in (
        "itemEntry.typeID = CodexBookConfig.TYPE_ID;",
        "itemEntry.maxStock = CodexBookConfig.DEFAULT_MAX_STOCK;",
        "itemEntry.forceUnlock = true;", "itemEntry.priceFactor = priceFactor;",
        "itemEntry.possibility = 1f;", "itemEntry.lockInDemo = false;",
    ):
        if token not in codex_try_inject:
            return fail("Codex Book stock entry lost its frozen shop parameter: " + token)

    codex_inject_shops = method_body(module_codex_book, "internal void InjectCodexBookIntoShops(string targetSceneName = null)")
    if require_order(codex_inject_shops, [
        "if (!_owner.IsCodexConfiguredEnabled())", "string currentScene = targetSceneName;",
        "SceneManager.GetActiveScene().name", "if (currentScene != _owner.IntegrationBaseSceneName)",
        "StockShop[] shops = ObjectCache.GetStockShops();", "for (int i = 0; i < shops.Length; i++)",
        "TryInjectCodexBookIntoShop(shop)",
    ], "InjectCodexBookIntoShops"):
        return fail("Codex Book shop scan must preserve the dormant, scene, and shop iteration gates")

    codex_load = method_body(module_codex_book, "private int LoadCodexBookStockFromSave()")
    if require_order(codex_load, [
        "if (cachedCodexBookStock >= 0)", "SavesSystem.KeyExisits(CodexBookConfig.STOCK_SAVE_KEY)",
        "SavesSystem.Load<int>(CodexBookConfig.STOCK_SAVE_KEY)",
        "cachedCodexBookStock = CodexBookConfig.DEFAULT_MAX_STOCK;",
    ], "LoadCodexBookStockFromSave"):
        return fail("Codex Book stock load must preserve the -1 cache sentinel, official API spelling, and default")

    codex_save = method_body(module_codex_book, "private void OnCollectSaveData_CodexBookStock()")
    if require_order(codex_save, [
        "if (injectedCodexBookEntry != null)", "else if (cachedCodexBookStock >= 0)",
        "stockToSave = cachedCodexBookStock;", "SavesSystem.Save<int>(CodexBookConfig.STOCK_SAVE_KEY, stockToSave);",
        "cachedCodexBookStock = stockToSave;",
    ], "OnCollectSaveData_CodexBookStock"):
        return fail("Codex Book save must preserve the current or cached sold-out stock before updating the cache")
    codex_set_file = method_body(module_codex_book, "private void OnSetFile_CodexBookStock()")
    if require_order(codex_set_file, ["cachedCodexBookStock = -1;", "injectedCodexBookEntry = null;"], "OnSetFile_CodexBookStock"):
        return fail("Codex Book OnSetFile must reset the stock cache and injected entry")

    cleanup = method_body(module, "internal void CleanupRuntimeEvents()")
    if require_order(cleanup, [
        "UnsubscribeTicketStockEvents();", "UnsubscribeJournalStockEvents();", "UnsubscribeBrickStoneStockEvents();",
    ], "CleanupRuntimeEvents"):
        return fail("module fallback cleanup must release all three stock event owners")
    destroy = method_body(module, "public override void OnDestroy()")
    if require_order(destroy, [
        "StopRuntimeStateMonitor();", "CleanupRuntimeEvents();", "UnsubscribePurchaseEvents();",
        "UnsubscribeDragonBreathEffectEvent();", "_owner = null;",
    ], "OnDestroy"):
        return fail("OnDestroy must stop owned work and release module events before detaching its host")

    purchase_subscribe = method_body(module, "internal void SubscribePurchaseEvents()")
    purchase_unsubscribe = method_body(module, "internal void UnsubscribePurchaseEvents()")
    if not purchase_subscribe or "if (_purchaseEventsSubscribed) return;" not in purchase_subscribe or "StockShop.OnItemPurchased += OnItemPurchased_Integration;" not in purchase_subscribe or "_purchaseEventsSubscribed = true;" not in purchase_subscribe:
        return fail("purchase callback subscription must be idempotently owned by IntegrationRuntimeModule")
    if not purchase_unsubscribe or "if (!_purchaseEventsSubscribed) return;" not in purchase_unsubscribe or "StockShop.OnItemPurchased -= OnItemPurchased_Integration;" not in purchase_unsubscribe or "_purchaseEventsSubscribed = false;" not in purchase_unsubscribe:
        return fail("purchase callback unsubscription must release the module-owned delegate")
    if "private int _item105PurchaseCount;" not in module or "private int item105PurchaseCount =" in host:
        return fail("purchase counter state must live only in IntegrationRuntimeModule")
    if "get { return bossRushIntegrationRuntime.Item105PurchaseCount; }" not in host or "set { bossRushIntegrationRuntime.Item105PurchaseCount = value; }" not in host:
        return fail("the old purchase counter reset point must remain a module-backed compatibility property")

    dynamic_init = method_body(module_initialization, "internal void InitializeDynamicItems_Integration()")
    if require_order(dynamic_init, [
        "if (DynamicItemsInitialized)", "DynamicItemsInitialized = true;",
        "EnsureBossRushTicketItemRegisteredForDynamicRegistry()",
        "EnsureItemContentConfiguratorsRegisteredForDynamicRegistry();",
        "int itemCount = ItemFactory.LoadedItemCount;", "AwenLootSweepTokenConfig.EnsureRuntimeRegistration();",
        "ZombieTideInvitationConfig.EnsureRuntimeFallbackRegistrationShell();",
        "ZombieTideBeaconConfig.EnsureRuntimeFallbackRegistrationShell();",
        "PortableSafeZoneDeviceConfig.EnsureRuntimeFallbackRegistrationShell();",
        "PeaceCharmRuntime.InitializeRuntime();",
    ], "InitializeDynamicItems_Integration"):
        return fail("dynamic item initialization must preserve its one-shot latch and original registration order")
    if "dynamicItemsInitialized" in module or "private bool _integrationDynamicItemsInitialized" in module:
        return fail("dynamic initialization latch must remain a single module-owned static state")
    if "get { return IntegrationRuntimeModule.DynamicItemsInitialized; }" not in host or "set { IntegrationRuntimeModule.DynamicItemsInitialized = value; }" not in host:
        return fail("dynamic initialization host entry must bridge the module one-shot latch without duplicating it")
    if "bossRushIntegrationRuntime.InitializeDynamicItems_Integration();" not in method_body(host, "private void InitializeDynamicItems_Integration()"):
        return fail("legacy dynamic item initializer must remain a thin module bridge")

    ticket_localization = method_body(module_initialization, "internal static void InjectBossRushTicketLocalization_Integration(int ticketTypeId)")
    if require_order(ticket_localization, [
        "LocalizationInjector.InjectTicketLocalization(ticketTypeId);",
        "ModBehaviour.DevLog(\"[BossRush] 船票本地化注入完成\");",
    ], "InjectBossRushTicketLocalization_Integration"):
        return fail("ticket localization must preserve the existing type id and completion log")
    if "IntegrationRuntimeModule.InjectBossRushTicketLocalization_Integration(bossRushTicketTypeId);" not in method_body(host, "private static void InjectBossRushTicketLocalization_Integration()"):
        return fail("legacy ticket localization entrypoint must forward the existing registered type id")
    mode_f_localization = method_body(module_initialization, "internal static void InjectModeFItemLocalization()")
    if require_order(mode_f_localization, [
        "bool isChinese = L10n.IsChinese;",
        "InjectModeFItemLoc(BloodhuntTransponderConfig.LOC_KEY_DISPLAY",
        "InjectModeFItemLoc(FoldableCoverPackConfig.LOC_KEY_DISPLAY",
        "InjectModeFItemLoc(ReinforcedRoadblockPackConfig.LOC_KEY_DISPLAY",
        "InjectModeFItemLoc(BarbedWirePackConfig.LOC_KEY_DISPLAY",
        "InjectModeFItemLoc(EmergencyRepairSprayConfig.LOC_KEY_DISPLAY",
        "InjectModeFItemLoc(FateEchoRelicConfig.LOC_KEY_DISPLAY",
    ], "InjectModeFItemLocalization"):
        return fail("Mode F localization must resolve the current language and retain every configured item")
    loc_helper = method_body(module_initialization, "internal static void InjectModeFItemLoc(")
    for token in (
        'LocalizationHelper.InjectLocalization(locKey, displayName);',
        'LocalizationHelper.InjectLocalization(locKey + "_Desc", description);',
        'LocalizationHelper.InjectLocalization("Item_" + typeId, displayName);',
        'LocalizationHelper.InjectLocalization("Item_" + typeId + "_Desc", description);',
        'LocalizationHelper.InjectLocalization(nameCN, displayName);',
        'LocalizationHelper.InjectLocalization(nameEN, displayName);',
    ):
        if token not in loc_helper:
            return fail("Mode F localization must preserve the existing localization key set -> " + token)
    if "IntegrationRuntimeModule.InjectModeFItemLocalization();" not in method_body(host, "private static void InjectModeFItemLocalization()"):
        return fail("legacy Mode F localization entrypoint must remain a thin module bridge")
    if "IntegrationRuntimeModule.InjectModeFItemLoc(locKey, typeId, nameCN, nameEN, descCN, descEN, isChinese);" not in method_body(host, "private static void InjectModeFItemLoc("):
        return fail("legacy Mode F localization helper must retain its signature as a thin module bridge")

    weapon_configs = method_body(module_initialization, "internal void RegisterCustomWeaponRuntimeConfigs()")
    if require_order(weapon_configs, [
        "NewWeaponRuntime.RegisterRuntimeConfigs();",
        "DragonBreathWeaponConfig.WEAPON_TYPE_ID", "DragonKingBossGunConfig.WeaponTypeId",
        "FenHuangHalberdIds.WeaponTypeId", "FrostmourneIds.WeaponTypeId",
        "PhantomWitchConfig.ReservedScytheTypeId",
    ], "RegisterCustomWeaponRuntimeConfigs"):
        return fail("custom weapon runtime registration must retain its configured type order")
    if "bossRushIntegrationRuntime.RegisterCustomWeaponRuntimeConfigs();" not in method_body(host, "private void RegisterCustomWeaponRuntimeConfigs()"):
        return fail("legacy weapon registration entrypoint must remain a thin module bridge")
    for callback in ("FenHuangHalberd", "Frostmourne", "PhantomWitchScythe", "AdventureJournal"):
        signature = "internal void On" + callback + "Loaded(Item itemPrefab)"
        if not method_body(module_initialization, signature):
            return fail("ItemFactory configurator callback must be owned by IntegrationRuntimeModule -> " + signature)
        host_signature = "private void On" + callback + "Loaded(Item itemPrefab)"
        if "bossRushIntegrationRuntime.On" + callback + "Loaded(itemPrefab);" not in method_body(host, host_signature):
            return fail("old ItemFactory callback entrypoint must remain a thin module bridge -> " + host_signature)

    all_shop_injection = method_body(module_initialization, "internal int TryInjectAllBossRushItemsIntoShop(StockShop shop)")
    if require_order(all_shop_injection, [
        "TryInjectBossRushTicketIntoShop(shop)", "TryInjectAdventureJournalIntoShop(shop)",
        "_owner.TryInjectAchievementMedalIntoShop(shop)", "AwenCourierTokenConfig.TryInjectIntoShop(shop, _owner)",
        "TryInjectBrickStoneIntoShop(shop)", "ZombieTideInvitationConfig.TryInjectIntoShop(shop, _owner)",
        "FactionFlagConfig.TryInjectIntoShop(shop)", "BloodhuntTransponderConfig.TryInjectIntoShop(shop, _owner)",
        "FateEchoRelicConfig.TryInjectIntoShop(shop, _owner)", "_owner.TryInjectCodexBookIntoShop(shop)",
        "BackMountainItems.TryInjectSeedsIntoShop(shop, _owner)",
    ], "TryInjectAllBossRushItemsIntoShop"):
        return fail("shop aggregation must preserve item injection order and the module-owned bridge owner")
    if "return bossRushIntegrationRuntime.TryInjectAllBossRushItemsIntoShop(shop);" not in method_body(host, "internal int TryInjectAllBossRushItemsIntoShop(StockShop shop)"):
        return fail("legacy shop aggregation entrypoint must remain a thin module bridge")

    dragon_subscribe = method_body(module, "internal void SubscribeDragonBreathEffectEvent()")
    dragon_unsubscribe = method_body(module, "internal void UnsubscribeDragonBreathEffectEvent()")
    if not dragon_subscribe or "mainChar.OnHoldAgentChanged += OnPlayerHoldAgentChanged;" not in dragon_subscribe or "_cachedMainCharForEffect = mainChar;" not in dragon_subscribe or "_dragonBreathEffectEventSubscribed = true;" not in dragon_subscribe:
        return fail("dragon-breath delegate, cached character, and owner state must be established by the module")
    if not dragon_unsubscribe or "subscribedCharacter.OnHoldAgentChanged -= OnPlayerHoldAgentChanged;" not in dragon_unsubscribe or "_cachedMainCharForEffect = null;" not in dragon_unsubscribe or "_dragonBreathEffectEventSubscribed = false;" not in dragon_unsubscribe:
        return fail("dragon-breath delegate teardown and cached-character release must share its module owner")
    if "private bool dragonBreathEffectEventSubscribed" in map_host or "private CharacterMainControl cachedMainCharForEffect" in map_host:
        return fail("host partials must not retain dragon-breath subscription state")
    if "bossRushIntegrationRuntime.UnsubscribeDragonBreathEffectEvent();" not in method_body(map_host, "private void UnsubscribeDragonBreathEffectEvent()"):
        return fail("legacy equipment cleanup entrypoint must forward to the module owner")

    for signature in (
        "internal System.Collections.IEnumerator DelayedRestoreReforgeDataForInventory()",
        "private System.Collections.IEnumerator MonitorLateRuntimeStateRestore()",
        "private static int RestoreRuntimeStateForInventory(Inventory inventory, string reason)",
        "private static int RestoreRuntimeStateForSlots(Item characterItem, string reason)",
        "private static int RestoreRuntimeStateForHoldAgent(DuckovItemAgent holdAgent, string reason)",
    ):
        if not method_body(module, signature):
            return fail("recovery coroutine/helper must live in the Integration runtime module: " + signature)
    for signature in (
        "private System.Collections.IEnumerator DelayedRestoreReforgeDataForInventory()",
        "private System.Collections.IEnumerator MonitorLateRuntimeStateRestore()",
        "private static int RestoreRuntimeStateForInventory(Inventory inventory, string reason)",
        "private static int RestoreRuntimeStateForSlots(Item characterItem, string reason)",
        "private static int RestoreRuntimeStateForHoldAgent(DuckovItemAgent holdAgent, string reason)",
    ):
        if method_body(lifecycle, signature):
            return fail("host lifecycle partial must not own recovery logic: " + signature)

    start_monitor = method_body(module, "internal void StartRuntimeStateMonitor()")
    stop_monitor = method_body(module, "internal void StopRuntimeStateMonitor()")
    if "_owner.StartCoroutine(MonitorLateRuntimeStateRestore())" not in start_monitor or "_owner.StopCoroutine(_runtimeStateMonitorCoroutine)" not in stop_monitor:
        return fail("runtime monitor coroutine handle must be owned and stopped by the module")
    delayed_dragon = method_body(module, "internal System.Collections.IEnumerator DelayedSubscribeDragonBreathEvents()")
    if "yield return _owner.IntegrationSharedWait05s;" not in delayed_dragon:
        return fail("dragon-breath delayed subscription must preserve the host's shared wait instance")

    travel_wait = method_body(module_travel, "internal IEnumerator WaitForCustomTeleportSceneReady()")
    if require_order(travel_wait, [
        "const float maxWait = 30f;", "const float interval = 0.1f;",
        'ReadMainExistsWithWarning("TeleportPlayerToCustomPosition")',
        'ReadLevelInitedWithWarning("TeleportPlayerToCustomPosition")',
        "yield return new WaitForSeconds(interval);", "elapsed += interval;",
        "yield return _owner.IntegrationSharedWait05s;",
    ], "WaitForCustomTeleportSceneReady"):
        return fail("custom teleport readiness polling must preserve its interval, shared wait, and order")

    landing = method_body(module_travel, "internal Vector3 ApplyCustomTeleportPosition(Vector3 targetPosition, CharacterMainControl main, bool isModeEEntry)")
    if not landing or landing.find("if (isModeEEntry)") > landing.find("Physics.RaycastAll("):
        return fail("Mode E must return before applying the custom teleport landing")
    if require_order(landing, [
        "Physics.RaycastAll(rayStart, Vector3.down, 5f)",
        "Mathf.Abs(h.point.y - configY) < 1f", "h.point.y < lowestY",
        "Physics.Raycast(rayStart, Vector3.down, out hit, 5f)",
        "camera.transform.position - main.transform.position", "main.SetPosition(finalPosition);",
        "main.transform.position = finalPosition;",
        "camera.transform.position = main.transform.position + cameraOffset;",
    ], "ApplyCustomTeleportPosition"):
        return fail("custom teleport must preserve ground selection, camera offset, and SetPosition fallback")
    if landing.rfind("return finalPosition;") <= landing.find("Physics.RaycastAll("):
        return fail("custom teleport must return the resolved landing after applying it")

    travel_host_teleport = method_body(travel_host, "private System.Collections.IEnumerator TeleportPlayerToCustomPosition(Vector3 targetPosition)")
    mode_h_gate = "if (ShouldSkipLegacySceneSetupForModeH()) yield break;"
    first_gate = travel_host_teleport.find(mode_h_gate)
    wait_call = travel_host_teleport.find("WaitForCustomTeleportSceneReady()")
    second_gate = travel_host_teleport.find(mode_h_gate, first_gate + len(mode_h_gate))
    entry_selection = travel_host_teleport.find("DetermineBossRushEntryMode(\"TeleportPlayerToCustomPosition\")")
    apply_landing = travel_host_teleport.find("ApplyCustomTeleportPosition(")
    zombie_guard = travel_host_teleport.find("IsZombieModeStartupInProgress()")
    ground_zero_schedule = travel_host_teleport.find("StartCoroutine(SetupBossRushInGroundZero(finalPosition, entryMode));")
    travel_positions = [first_gate, wait_call, second_gate, entry_selection, apply_landing, zombie_guard, ground_zero_schedule]
    if not travel_host_teleport or any(position < 0 for position in travel_positions) or travel_positions != sorted(travel_positions):
        return fail("host travel coordination must keep both Mode H gates, entry selection, teleport, and ZombieMode guard order")

    force_teleport = method_body(module_travel, "internal IEnumerator ForceTeleportToSubScene(string targetSubSceneID, Vector3 targetPosition)")
    if require_order(force_teleport, [
        "const float maxWait = 10f;", "const float interval = 0.1f;",
        'ReadMainExistsWithWarning("ForceTeleportToSubScene")',
        'ReadLevelInitedWithWarning("ForceTeleportToSubScene")',
        "yield return new WaitForSeconds(interval);", "yield return _owner.IntegrationSharedWait1s;",
        "FindObjectsOfType<MultiSceneTeleporter>(true)", "targetSceneID == targetSubSceneID",
        'targetSubSceneID == "Level_StormZone_B0"', "targetTeleporter.DoTeleport();",
        "multiSceneCore.LoadAndTeleport(targetSubSceneID, targetPosition, true)",
        "SetBossRushArenaPlannedForIntegration(false)",
        "TeleportPlayerToCustomPositionForIntegration(targetPosition)",
        "ClearBossRushPendingMapEntryForIntegration()", "ClearBossRushPendingEntryFlowStateForIntegration()",
    ], "ForceTeleportToSubScene"):
        return fail("subscene teleport must preserve readiness, teleporter preference, fallback, and pending-entry cleanup order")
    force_host_bridge = method_body(travel_host, "private System.Collections.IEnumerator ForceTeleportToSubScene(string targetSubSceneID, Vector3 targetPosition)")
    if "return bossRushIntegrationRuntime.ForceTeleportToSubScene(targetSubSceneID, targetPosition);" not in force_host_bridge:
        return fail("legacy ForceTeleportToSubScene entrypoint must forward to the module")

    set_spawn_host = method_body(travel_host, "private void SetCurrentMapSpawnPoints(string sceneName)")
    if "currentMapSpawnPoints = bossRushIntegrationRuntime.ResolveMapSpawnPointsForScene(sceneName);" not in set_spawn_host:
        return fail("legacy spawn-point entrypoint must keep its shared host state while delegating config resolution")
    spawn_resolver = method_body(module_travel, "internal Vector3[] ResolveMapSpawnPointsForScene(string sceneName)")
    if not spawn_resolver or "ModBehaviour.GetMapConfigBySceneName(sceneName)" not in spawn_resolver or "return mapConfig.spawnPoints;" not in spawn_resolver:
        return fail("spawn-point config resolution must live in IntegrationRuntimeModule")

    map_spawn = method_body(module_map_objects, "internal void SpawnBossRushMapObjects()")
    if require_order(map_spawn, [
        "if (_owner.IsModeEActive)", "GetMapCloneConfigs(currentScene)",
        "_owner.StartCoroutine(SpawnMapObjectsAsync(configs));", "CreateBossRushExitForScene(currentScene);",
    ], "SpawnBossRushMapObjects"):
        return fail("map-object generation must keep the Mode E gate, async start, and exit creation order")
    map_spawn_async = method_body(module_map_objects, "private System.Collections.IEnumerator SpawnMapObjectsAsync(List<MapObjectCloneConfig> configs)")
    if require_order(map_spawn_async, [
        "yield return new WaitForSeconds(0.3f);", "ObjectCache.GetSceneObjectsByType(typeof(Transform));",
        "const int batchSize = 3;", "const float batchInterval = 0.016f;",
        "CloneMapObjectFast(template, parentTransform, config);", "yield return new WaitForSeconds(batchInterval);",
    ], "SpawnMapObjectsAsync"):
        return fail("map-object clone coroutine must preserve its scan and frame-batch timing")
    map_configs = method_body(module_map_objects, "private List<MapObjectCloneConfig> GetMapCloneConfigs(string sceneName)")
    for token in (
        'sceneName == "Level_GroundZero_1"', 'sceneName == "Level_HiddenWarehouse"',
        '"BossRush_Barrier_84"', 'return configs;',
    ):
        if token not in map_configs:
            return fail("map clone configuration data must remain module-owned -> " + token)

    native_exit = method_body(module_map_objects, "private void CreateBossRushExit(Vector3 position, string exitName)")
    if require_order(native_exit, [
        "LevelManager.Instance.ExitCreator.exitPrefab", "DisableExitSmokeEffects(exit);",
        "CreateSimpleExit(position, exitName);",
    ], "CreateBossRushExit"):
        return fail("native exit creation must retain smoke suppression and simple-exit fallback")
    simple_exit = method_body(module_map_objects, "private void CreateSimpleExit(Vector3 position, string exitName)")
    for token in (
        "exit.AddComponent<BoxCollider>()", "exit.AddComponent<CountDownArea>()", "requiredExtrationTime", "NotifyEvacuated(info)",
        "EvacuationCountdownUI.Request(area)", "EvacuationCountdownUI.Release(area)",
    ):
        if token not in simple_exit:
            return fail("simple exit behavior must remain module-owned -> " + token)

    wait_for_level = method_body(module_map_objects, "internal System.Collections.IEnumerator WaitForLevelInitializedThenSetup_Integration(Scene scene)")
    if require_order(wait_for_level, [
        "const float maxWait = 30f;", "const float interval = 0.1f;", "scene.isLoaded",
        "ReadSceneLoaderDoneWithWarning(\"WaitForLevelInitializedThenSetup\")",
        "ReadMainExistsWithWarning(\"WaitForLevelInitializedThenSetup\")",
        "ReadCameraExistsWithWarning(\"WaitForLevelInitializedThenSetup\")",
        "ReadLevelInitedWithWarning(\"WaitForLevelInitializedThenSetup\")",
        "yield return new WaitForSeconds(interval);", "_owner.StartBossRushDemoChallengeSetupForScene(scene);",
    ], "WaitForLevelInitializedThenSetup_Integration"):
        return fail("level-ready polling must preserve its condition order, wait, and original setup handoff")
    for signature, bridge in (
        ("private void SpawnBossRushMapObjects()", "bossRushIntegrationRuntime.SpawnBossRushMapObjects();"),
        ("private System.Collections.IEnumerator WaitForLevelInitializedThenSetup_Integration(Scene scene)", "return bossRushIntegrationRuntime.WaitForLevelInitializedThenSetup_Integration(scene);"),
    ):
        body = method_body(map_host, signature)
        if not body or bridge not in body:
            return fail("original map host entrypoint must remain a thin module bridge -> " + signature)
    setup_bridge = method_body(map_host, "internal void StartBossRushDemoChallengeSetupForScene(Scene scene)")
    if "StartCoroutine(SetupBossRushInDemoChallenge(scene));" not in setup_bridge:
        return fail("level-ready module must hand off the setup coroutine through the narrow host bridge")
    for name in (
        "MapObjectCloneConfig", "GetMapCloneConfigs(", "CreateBossRushExit(",
        "DisableExitSmokeEffects(", "CreateSimpleExit(",
    ):
        if name in travel_host or name in map_host:
            return fail("map clone and exit business logic must not remain in host partials -> " + name)
    ground_zero_setup = method_body(travel_host, "private System.Collections.IEnumerator SetupBossRushInGroundZero")
    map_spawn_position = ground_zero_setup.find("SpawnBossRushMapObjects();")
    if map_spawn_position < 0:
        return fail("SetupBossRushInGroundZero must retain the map-object call site")
    if require_order(ground_zero_setup[map_spawn_position:], [
        "SpawnBossRushMapObjects();", "DisableAllSpawners();",
        "StartCoroutine(ContinuousClearEnemiesUntilWaveStart());", "ClearEnemiesForBossRush();",
        "SetCurrentMapSpawnPoints(currentSceneName);", "bossRushArenaActive = true;",
    ], "SetupBossRushInGroundZero"):
        return fail("ground-zero lifecycle must keep map objects ahead of spawner cleanup and preserve arena setup order")

    for signature, call in (
        ("private bool ReadMainExistsWithWarning(string context)", "ReadMainExistsWithWarning(context)"),
        ("private bool ReadLevelInitedWithWarning(string context)", "ReadLevelInitedWithWarning(context)"),
        ("private bool ReadSceneLoaderDoneWithWarning(string context)", "ReadSceneLoaderDoneWithWarning(context)"),
        ("private bool ReadCameraExistsWithWarning(string context)", "ReadCameraExistsWithWarning(context)"),
        ("private string ReadActiveSceneNameWithWarning(string context)", "ReadActiveSceneNameWithWarning(context)"),
        ("private void InvalidateIntegrationStockShopCache()", "InvalidateIntegrationStockShopCache()"),
        ("internal bool IsBaseHubNormalMerchantShop(StockShop shop)", "IsBaseHubNormalMerchantShop(shop)"),
        ("internal bool TryInjectBossRushTicketIntoShop(StockShop shop)", "TryInjectBossRushTicketIntoShop(shop)"),
        ("internal bool TryInjectAdventureJournalIntoShop(StockShop shop)", "TryInjectAdventureJournalIntoShop(shop)"),
        ("internal bool TryInjectBrickStoneIntoShop(StockShop shop)", "TryInjectBrickStoneIntoShop(shop)"),
        ("private void InjectBossRushTicketIntoShops_Integration(string targetSceneName = null)", "InjectBossRushTicketIntoShops_Integration(targetSceneName)"),
        ("private void InjectAdventureJournalIntoShops_Integration(string targetSceneName = null)", "InjectAdventureJournalIntoShops_Integration(targetSceneName)"),
        ("private void InjectBrickStoneIntoShops(string targetSceneName = null)", "InjectBrickStoneIntoShops(targetSceneName)"),
    ):
        body = method_body(host, signature)
        if not body or "bossRushIntegrationRuntime." + call not in body:
            return fail("legacy host entrypoint must forward to the registered module: " + signature)

    ticket_scan = method_body(module, "internal void InjectBossRushTicketIntoShops_Integration(string targetSceneName = null)")
    journal_scan = method_body(module, "internal void InjectAdventureJournalIntoShops_Integration(string targetSceneName = null)")
    brick_scan = method_body(module, "internal void InjectBrickStoneIntoShops(string targetSceneName = null)")
    for name, body in (("ticket", ticket_scan), ("journal", journal_scan), ("brick", brick_scan)):
        if not body or "GetIntegrationStockShops(" not in body or "foreach (StockShop shop in shops)" not in body:
            return fail(name + " shop scan business logic must live in the runtime module")

    start = method_body(lifecycle, "void Start_Integration()")
    if require_order(start, [
        "RefreshDeathWraithEventBindings_DeathWraith();", "ApplyDevModeRuntimeState();",
        "InjectLocalization();", "EnsureLanguageChangeSubscription();",
        "RegisterCustomWeaponRuntimeConfigs();", "bossRushIntegrationRuntime.StartRuntimeStateMonitor();",
    ], "Start_Integration weapon configuration"):
        return fail("custom weapon runtime registrations must keep their original Start order")
    error = require_order(start, [
        "bossRushIntegrationRuntime.StartRuntimeStateMonitor();",
        "SceneManager.sceneLoaded += OnSceneLoaded;",
        "SceneLoader.onAfterSceneInitialize += OnAfterSceneInitialize_Integration;",
        "bossRushIntegrationRuntime.SubscribePurchaseEvents();",
        "bossRushIntegrationRuntime.SubscribeTicketStockEvents();",
        "bossRushIntegrationRuntime.SubscribeJournalStockEvents();",
        "achievementRuntime.SubscribeMedalStockEvents();",
        "bossRushIntegrationRuntime.SubscribeBrickStoneStockEvents();",
        "bossRushIntegrationRuntime.SubscribeCodexBookStockEvents();",
        "OnSetFile_DeathWraith;",
    ], "Start_Integration")
    if error:
        return fail(error)
    destroy_host = method_body(lifecycle, "void OnDestroy_Integration()")
    error = require_order(destroy_host, [
        "bossRushIntegrationRuntime.StopRuntimeStateMonitor();",
        "SceneManager.sceneLoaded -= OnSceneLoaded;",
        "SceneLoader.onAfterSceneInitialize -= OnAfterSceneInitialize_Integration;",
        "bossRushIntegrationRuntime.UnsubscribePurchaseEvents();",
        "bossRushIntegrationRuntime.UnsubscribeDragonBreathEffectEvent();",
        "bossRushIntegrationRuntime.UnsubscribeTicketStockEvents();",
        "bossRushIntegrationRuntime.UnsubscribeJournalStockEvents();",
        "achievementRuntime.UnsubscribeMedalStockEvents();",
        "bossRushIntegrationRuntime.UnsubscribeBrickStoneStockEvents();",
        "bossRushIntegrationRuntime.UnsubscribeCodexBookStockEvents();",
        "OnSetFile_DeathWraith;",
    ], "OnDestroy_Integration")
    if error:
        return fail(error)
    if "SavesSystem.OnCollectSaveData += OnCollectSaveData_TicketStock;" in lifecycle or "SavesSystem.OnSetFile += OnSetFile_TicketStock;" in lifecycle:
        return fail("host must not own ticket stock delegates")
    if "SavesSystem.OnCollectSaveData += OnCollectSaveData_CodexBookStock;" in lifecycle or "SavesSystem.OnSetFile += OnSetFile_CodexBookStock;" in lifecycle:
        return fail("host must not own Codex Book stock delegates")
    codex_try_bridge = method_body(codex_book_host, "internal bool TryInjectCodexBookIntoShop(StockShop shop)")
    if "return bossRushIntegrationRuntime.TryInjectCodexBookIntoShop(shop);" not in codex_try_bridge:
        return fail("the old Codex Book shop injection entry must remain a thin host compatibility bridge")
    codex_inject_bridge = method_body(codex_book_host, "internal void InjectCodexBookIntoShops(string targetSceneName = null)")
    if "bossRushIntegrationRuntime.InjectCodexBookIntoShops(targetSceneName);" not in codex_inject_bridge:
        return fail("the old Codex Book scene injection entry must remain a thin host compatibility bridge")
    if "StockShop.OnItemPurchased += OnItemPurchased_Integration;" in lifecycle or "StockShop.OnItemPurchased -= OnItemPurchased_Integration;" in lifecycle:
        return fail("host must not own the purchase callback delegate")
    if "private void OnItemPurchased_Integration(StockShop shop, Item item)" in lifecycle:
        return fail("unused host purchase callback bridge must be removed after module ownership")
    scene_loaded = method_body(lifecycle, "private void OnSceneLoaded_Integration(Scene scene, LoadSceneMode mode)")
    if require_order(scene_loaded, [
        "StartCoroutine(bossRushIntegrationRuntime.DelayedRestoreReforgeDataForInventory());",
        "StartCoroutine(bossRushIntegrationRuntime.DelayedSubscribeDragonBreathEvents());",
        "StartCoroutine(bossRushIntegrationRuntime.DelayedApplyDragonGunAmmoOverride());",
    ], "OnSceneLoaded_Integration"):
        return fail("scene lifecycle must start the extracted integration routines in the original order")
    common_npc_gate = scene_loaded.find("if (ShouldSpawnCommonNPCsInScene(scene.name))")
    common_npc_schedule = scene_loaded.find("bossRushIntegrationRuntime.ScheduleDelayedSpawnCommonNPCsInNormalMode(scene.name);")
    if common_npc_gate < 0 or common_npc_schedule < common_npc_gate:
        return fail("normal-scene common NPC spawn must retain its host scene gate and schedule the module coroutine")
    if "StartCoroutine(DelayedSpawnCommonNPCsInNormalMode(scene.name))" in scene_loaded:
        return fail("OnSceneLoaded_Integration must schedule delayed common NPC work through IntegrationRuntimeModule")
    if "System.Collections.IEnumerator DelayedSpawnCommonNPCsInNormalMode(string sceneName)" in lifecycle:
        return fail("delayed common NPC readiness coroutine must be module-owned")
    npc_bridge = method_body(lifecycle, "internal void SpawnCommonNPCsForIntegrationRuntimeModule(string context)")
    if "SpawnCommonNPCs(context);" not in npc_bridge:
        return fail("runtime module common NPC dispatch must use the existing host compatibility bridge")

    wish_warmup = method_body(module_scene_lifecycle, "internal void ScheduleWishRewardPoolWarmup()")
    if "_owner.StartCoroutine(WishFountainService.WarmupWishRewardPoolAfterDelay());" not in wish_warmup:
        return fail("wish reward pool warmup must be scheduled by the IntegrationRuntimeModule owner")
    npc_schedule = method_body(module_scene_lifecycle, "internal void ScheduleDelayedSpawnCommonNPCsInNormalMode(string sceneName)")
    if "_owner.StartCoroutine(DelayedSpawnCommonNPCsInNormalMode(sceneName));" not in npc_schedule:
        return fail("the IntegrationRuntimeModule must own the delayed common NPC coroutine scheduling")
    npc_coroutine = method_body(module_scene_lifecycle, "private IEnumerator DelayedSpawnCommonNPCsInNormalMode(string sceneName)")
    if require_order(npc_coroutine, [
        "const float maxWait = 10f;",
        "const float interval = 0.2f;",
        "while (elapsed < maxWait)",
        "ReadMainExistsWithWarning(\"DelayedSpawnCommonNPCsInNormalMode\")",
        "ReadLevelInitedWithWarning(\"DelayedSpawnCommonNPCsInNormalMode\")",
        "yield return new WaitForSeconds(interval);",
        "yield return new WaitForSeconds(0.5f);",
        "ReadActiveSceneNameWithWarning(\"DelayedSpawnCommonNPCsInNormalMode\")",
        "if (currentScene != sceneName)",
        "_owner.ShouldSuppressBaseNpcSpawnForCurrentMode()",
        "_owner.SpawnCommonNPCsForIntegrationRuntimeModule(\"普通模式场景初始化完成\");",
        "_owner.ScheduleRestoreFollowingSpouse(sceneName, \"普通模式场景初始化完成\");",
    ], "delayed common NPC coroutine"):
        return fail("delayed common NPC coroutine must preserve readiness, scene, mode, spawn and spouse-restore order")

    if registration.count("bossRushIntegrationRuntime = new IntegrationRuntimeModule();") != 1:
        return fail("host must create one IntegrationRuntimeModule instance")
    if registration.count("runtimeModuleHost.Register(bossRushIntegrationRuntime);") != 1:
        return fail("host must register the same IntegrationRuntimeModule instance")
    death = registration.find("runtimeModuleHost.Register(deathWraithRuntimeModule);")
    integration = registration.find("bossRushIntegrationRuntime = new IntegrationRuntimeModule();")
    affinity = registration.find("affinityRuntime = new AffinityRuntimeModule();")
    if not (0 <= death < integration < affinity):
        return fail("IntegrationRuntimeModule registration order must be after DeathWraith and before Affinity")
    if "Integration/BossRushIntegrationRuntimeModule.cs" not in compile_text:
        return fail("compile_official.bat must include the runtime module source")
    if "Integration/BossRushIntegrationRuntimeModule_RuntimeHooks.cs" not in compile_text:
        return fail("compile_official.bat must include the extracted runtime-hooks partial")
    if "Integration/BossRushIntegrationRuntimeModule_MapObjects.cs" not in compile_text:
        return fail("compile_official.bat must include the extracted map-objects partial")
    if "Integration/BossRushIntegrationRuntimeModule_Travel.cs" not in compile_text:
        return fail("compile_official.bat must include the extracted travel partial")
    if "Integration/BossRushIntegrationRuntimeModule_Initialization.cs" not in compile_text:
        return fail("compile_official.bat must include the initialization and content wiring partial")
    if "Integration/BossRushIntegrationRuntimeModule_SceneLifecycle.cs" not in compile_text:
        return fail("compile_official.bat must include the scene lifecycle scheduling partial")

    print("IntegrationRuntimeModuleGuard: PASS（商店状态 owner、库存委托、兼容入口与原订阅顺序）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
