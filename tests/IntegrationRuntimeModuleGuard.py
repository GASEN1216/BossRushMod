#!/usr/bin/env python3
"""商店注入与库存存档状态必须由唯一 IntegrationRuntimeModule 持有。"""

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source


ROOT = Path(__file__).resolve().parent.parent
MODULE_PATH = ROOT / "Integration/BossRushIntegrationRuntimeModule.cs"
MODULE_RUNTIME_HOOKS_PATH = ROOT / "Integration/BossRushIntegrationRuntimeModule_RuntimeHooks.cs"
HOST_PATH = ROOT / "Integration/BossRushIntegration.cs"
LIFECYCLE_PATH = ROOT / "Integration/BossRushIntegration_StartAndScene.cs"
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
    module = module_core + "\n" + module_runtime_hooks
    host = clean_source(HOST_PATH.read_text(encoding="utf-8", errors="ignore"))
    lifecycle = clean_source(LIFECYCLE_PATH.read_text(encoding="utf-8", errors="ignore"))
    map_host = clean_source((ROOT / "Integration/BossRushIntegration_MapObjectsAndDragonBreath.cs").read_text(encoding="utf-8", errors="ignore"))
    registration = clean_source(REGISTRATION_PATH.read_text(encoding="utf-8", errors="ignore"))
    compile_text = COMPILE_PATH.read_text(encoding="utf-8-sig", errors="ignore").replace("\\", "/")

    if module_core.count("internal sealed partial class IntegrationRuntimeModule : BossRushRuntimeModuleBase") != 1:
        return fail("the dedicated IntegrationRuntimeModule core must retain its single registered base declaration")
    if module_runtime_hooks.count("internal sealed partial class IntegrationRuntimeModule") != 1:
        return fail("runtime hooks must be a single partial declaration of the registered module type")
    if "partial class ModBehaviour" in module:
        return fail("the runtime module source must not declare a ModBehaviour partial")
    if "private ModBehaviour _owner;" not in module or "_owner = owner;" not in method_body(module, "public override void OnAwake(ModBehaviour owner)"):
        return fail("OnAwake must bind the module to its registered host")

    owned_fields = (
        "cachedTicketStock", "injectedTicketEntry", "cachedJournalStock", "injectedJournalEntry",
        "cachedBrickStoneStock", "injectedBrickStoneEntry", "integrationNextWarningLogTimes",
        "cachedIntegrationStockShops", "cachedIntegrationStockShopsSceneName",
        "_purchaseEventsSubscribed", "_dragonBreathEffectEventSubscribed", "_cachedMainCharForEffect",
        "_runtimeStateMonitorCoroutine", "_item105PurchaseCount",
    )
    for field in owned_fields:
        if field not in module or field in host:
            return fail("IntegrationRuntimeModule must be the sole owner of " + field)
    if "private static int cachedTicketStock" not in module or "private static int cachedJournalStock" not in module or "private static int cachedBrickStoneStock" not in module:
        return fail("the save-backed stock cache fields must remain static")

    for name, flag in (
        ("Ticket", "_ticketStockEventsSubscribed"),
        ("Journal", "_journalStockEventsSubscribed"),
        ("BrickStone", "_brickStoneStockEventsSubscribed"),
    ):
        subscribe = method_body(module, "internal void Subscribe" + name + "StockEvents()")
        unsubscribe = method_body(module, "internal void Unsubscribe" + name + "StockEvents()")
        if not subscribe or "if (" + flag + ") return;" not in subscribe or flag + " = true;" not in subscribe:
            return fail(name + " save event subscription must be idempotent and owned by the module")
        if not unsubscribe or "if (!" + flag + ") return;" not in unsubscribe or flag + " = false;" not in unsubscribe:
            return fail(name + " save event unsubscription must release only its recorded delegate")
        suffix = "TicketStock" if name == "Ticket" else "JournalStock" if name == "Journal" else "BrickStoneStock"
        if "SavesSystem.OnCollectSaveData += OnCollectSaveData_" + suffix not in subscribe:
            return fail(name + " OnCollectSaveData delegate must be subscribed by its module owner")
        if "SavesSystem.OnSetFile += OnSetFile_" + suffix not in subscribe:
            return fail(name + " OnSetFile delegate must be subscribed by its module owner")

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
    error = require_order(start, [
        "bossRushIntegrationRuntime.StartRuntimeStateMonitor();",
        "SceneManager.sceneLoaded += OnSceneLoaded;",
        "SceneLoader.onAfterSceneInitialize += OnAfterSceneInitialize_Integration;",
        "bossRushIntegrationRuntime.SubscribePurchaseEvents();",
        "bossRushIntegrationRuntime.SubscribeTicketStockEvents();",
        "bossRushIntegrationRuntime.SubscribeJournalStockEvents();",
        "OnCollectSaveData_MedalStock;",
        "bossRushIntegrationRuntime.SubscribeBrickStoneStockEvents();",
        "OnCollectSaveData_CodexBookStock;",
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
        "OnCollectSaveData_MedalStock;",
        "bossRushIntegrationRuntime.UnsubscribeBrickStoneStockEvents();",
        "OnCollectSaveData_CodexBookStock;",
        "OnSetFile_DeathWraith;",
    ], "OnDestroy_Integration")
    if error:
        return fail(error)
    if "SavesSystem.OnCollectSaveData += OnCollectSaveData_TicketStock;" in lifecycle or "SavesSystem.OnSetFile += OnSetFile_TicketStock;" in lifecycle:
        return fail("host must not own ticket stock delegates")
    if "StockShop.OnItemPurchased += OnItemPurchased_Integration;" in lifecycle or "StockShop.OnItemPurchased -= OnItemPurchased_Integration;" in lifecycle:
        return fail("host must not own the purchase callback delegate")
    scene_loaded = method_body(lifecycle, "private void OnSceneLoaded_Integration(Scene scene, LoadSceneMode mode)")
    if require_order(scene_loaded, [
        "StartCoroutine(bossRushIntegrationRuntime.DelayedRestoreReforgeDataForInventory());",
        "StartCoroutine(bossRushIntegrationRuntime.DelayedSubscribeDragonBreathEvents());",
        "StartCoroutine(bossRushIntegrationRuntime.DelayedApplyDragonGunAmmoOverride());",
    ], "OnSceneLoaded_Integration"):
        return fail("scene lifecycle must start the extracted integration routines in the original order")

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

    print("IntegrationRuntimeModuleGuard: PASS（商店状态 owner、库存委托、兼容入口与原订阅顺序）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
