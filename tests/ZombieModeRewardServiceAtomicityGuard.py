from pathlib import Path
import sys


REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REWARD_PARTS = [
    REWARDS,
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]


def read_rewards() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REWARD_PARTS)

EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")
EFFECT_PARTS = [
    EFFECTS,
    Path("ZombieMode/ZombieModeRewardOptionCore.cs"),
    Path("ZombieMode/ZombieModeRewardProjectileSpread.cs"),
    Path("ZombieMode/ZombieModeRewardRuntimeModifiers.cs"),
    Path("ZombieMode/ZombieModeRewardTriggerEffects.cs"),
]


def read_effects() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in EFFECT_PARTS)

DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
STORAGE = Path("Integration/NPCs/Courier/StorageDepositService.cs")
STORAGE_DEPOSIT_PARTS = [
    Path("Integration/NPCs/Courier/StorageDepositService.cs"),
    Path("Integration/NPCs/Courier/StorageDepositLifecycle.cs"),
    Path("Integration/NPCs/Courier/StorageDepositTransactions.cs"),
    Path("Integration/NPCs/Courier/StorageDepositSingleRetrieve.cs"),
    Path("Integration/NPCs/Courier/StorageDepositInventoryQuickDeposit.cs"),
    Path("Integration/NPCs/Courier/StorageDepositBulkActions.cs"),
]


def read_storage_deposit_service() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in STORAGE_DEPOSIT_PARTS)

NPC_SHOP = Path("Integration/Affinity/Systems/NPCShopSystem.cs")
REFORGE_PARTS = [
    Path("Integration/Reforge/ReforgeUIManager.cs"),
    Path("Integration/Reforge/ReforgeUIManager_ComparisonAndState.cs"),
    Path("Integration/Reforge/ReforgeUIManager_RuntimeAndCleanup.cs"),
]


def read_reforge_ui_manager() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REFORGE_PARTS)

COURIER_PARTS = [
    Path("Integration/NPCs/Courier/CourierService.cs"),
    Path("Integration/NPCs/Courier/CourierService_Buttons.cs"),
    Path("Integration/NPCs/Courier/CourierService_CloseAndCleanup.cs"),
]
PAID_SWEEP = Path("Integration/NPCs/Courier/CourierPaidLootSweepService.cs")


def read_courier_service() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in COURIER_PARTS)


def fail(message: str) -> int:
    print("ZombieModeRewardServiceAtomicityGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str:
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
                return text[brace + 1:index]
    return ""


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def require_before(text: str, before: str, after: str, label: str) -> int:
    before_index = text.find(before)
    after_index = text.find(after)
    if before_index < 0 or after_index < 0:
        return fail("missing ordering token for " + label)
    if before_index > after_index:
        return fail("wrong ordering for " + label + ": " + before + " must appear before " + after)
    return 0


def main() -> int:
    rewards = read_rewards()
    effects = read_effects()
    drops = DROPS.read_text(encoding="utf-8")
    storage = read_storage_deposit_service()
    npc_shop = NPC_SHOP.read_text(encoding="utf-8")
    reforge = read_reforge_ui_manager()
    courier = read_courier_service()
    paid_sweep = PAID_SWEEP.read_text(encoding="utf-8")

    select_body = extract_method_body(rewards, "public void SelectZombieModeReward(int runId, ZombieModeRewardType rewardType)")
    if not select_body:
        return fail("SelectZombieModeReward body not found")
    for snippet in [
        "if (!ApplyZombieModeReward(rewardType))",
        "return;",
        "zombieModeRunState.CurrentRewardNode = null;",
    ]:
        result = require(select_body, snippet, "reward selection success gate")
        if result:
            return result
    result = require_before(
        select_body,
        "if (!ApplyZombieModeReward(rewardType))",
        "zombieModeRunState.CurrentRewardNode = null;",
        "reward apply before consuming node")
    if result:
        return result

    for snippet in [
        "private bool ApplyZombieModeReward(ZombieModeRewardType rewardType)",
        "private bool GrantZombieModeRandomMeleeReward(bool bossNode)",
        "private bool GrantZombieModeRandomGunWithAmmoReward(bool bossNode)",
        "private bool GrantZombieModeAmmoSupplyReward()",
        "private bool GrantZombieModeMedicalSupplyReward()",
        "private bool GrantZombieModeArmorOrHelmetReward(bool bossNode)",
        "private bool GrantZombieModeFortificationPack(bool bossNode)",
        "private int GrantZombieModeItemRepeated(int typeId, int count)",
        "private bool TryDeliverZombieModeItemToPlayerOrDrop(Item item, string logContext)",
    ]:
        result = require(rewards, snippet, "reward grant result contract")
        if result:
            return result

    for snippet in [
        "return GrantZombieModeRandomMeleeReward(bossNode);",
        "return GrantZombieModeRandomGunWithAmmoReward(bossNode);",
        "return GrantZombieModeAmmoSupplyReward();",
        "return GrantZombieModeMedicalSupplyReward();",
        "return GrantZombieModeArmorOrHelmetReward(bossNode);",
        "return GrantZombieModeFortificationPack(bossNode);",
    ]:
        result = require(rewards, snippet, "reward apply must propagate helper result")
        if result:
            return result

    for token in [
        "GrantZombieModeFallbackPurificationReward(\"RandomMeleeRewardFail\"",
        "GrantZombieModeFallbackPurificationReward(\"RandomGunWithAmmoRewardFail",
        "GrantZombieModeFallbackPurificationReward(\"AmmoSupplyRewardFail\"",
        "GrantZombieModeFallbackPurificationReward(\"MedicalSupplyRewardFail\"",
        "GrantZombieModeFallbackPurificationReward(\"ArmorOrHelmetRewardFail\"",
        "GrantZombieModeFallbackPurificationReward(\"FortificationPackRewardFail\"",
    ]:
        result = require(rewards, token, "item reward fallback")
        if result:
            return result

    result = require(effects, "private bool ApplyZombieModeOptionReward(ZombieModeRewardType rewardType)", "option reward result contract")
    if result:
        return result

    for service_text, token, label in [
        (npc_shop, "public static void CloseShopIfOwnedBy(Transform npcTransform)", "NPC shop owned close"),
        (reforge, "public static void CloseUIIfOwnedBy(Transform npcTransform)", "reforge owned close"),
        (courier, "public static void CloseServiceIfOwnedBy(Transform npcTransform)", "courier owned close"),
        (storage, "public static void CloseServiceIfOwnedBy(Transform npcTransform)", "storage owned close"),
        (paid_sweep, "public static void CloseServiceIfOwnedBy(Transform npcTransform)", "paid sweep owned close"),
    ]:
        result = require(service_text, token, label)
        if result:
            return result

    for snippet in [
        "private static int serviceGeneration = 0;",
        "serviceGeneration++;",
        "int promptGeneration = serviceGeneration;",
        "if (!IsPromptStillValid(npcTransform, promptGeneration))",
        "private static bool IsPromptStillValid(Transform npcTransform, int promptGeneration)",
        "BindServiceNpc(null);",
    ]:
        result = require(paid_sweep, snippet, "paid sweep prompt cancellation")
        if result:
            return result

    cleanup_body = extract_method_body(drops, "private void CloseZombieModeTemporaryRealNpcServices(GameObject npcObject)")
    if not cleanup_body:
        return fail("real temporary NPC service-close helper not found")
    for token in [
        "NPCShopSystem.CloseShopIfOwnedBy(npcTransform);",
        "ReforgeUIManager.CloseUIIfOwnedBy(npcTransform);",
        "CourierService.CloseServiceIfOwnedBy(npcTransform);",
        "StorageDepositService.CloseServiceIfOwnedBy(npcTransform);",
        "CourierPaidLootSweepService.CloseServiceIfOwnedBy(npcTransform);",
    ]:
        result = require(cleanup_body, token, "real temporary NPC service close call")
        if result:
            return result

    full_cleanup = extract_method_body(drops, "private void RecycleZombieModeTemporaryRealNpcs(int runId)")
    safe_zone_cleanup = extract_method_body(drops, "private void RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(int runId)")
    for body, label in [(full_cleanup, "full real NPC cleanup"), (safe_zone_cleanup, "safe-zone real NPC cleanup")]:
        if not body:
            return fail(label + " body not found")
        result = require_before(body, "CloseZombieModeTemporaryRealNpcServices(npc);", "Destroy(npc.GameObject);", label)
        if result:
            return result

    for snippet in [
        "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.TemporaryNpc, npc, npc, () => CloseZombieModeTemporaryRealNpcServices(npc));",
    ]:
        result = require(rewards, snippet, "run-only real NPC service cleanup")
        if result:
            return result

    retrieve_all_body = extract_method_body(storage, "private static async UniTaskVoid RetrieveAllItemsAsync(int totalFee)")
    if not retrieve_all_body:
        return fail("RetrieveAllItemsAsync body not found")
    for snippet in [
        "private sealed class RetrieveAllDepositItem",
        "private static bool isRetrieveAllInProgress = false;",
        "private static async UniTask<List<RetrieveAllDepositItem>> TryRestoreAllDepositItemsForRetrieveAll",
        "private static bool TryPayRetrieveFee(int totalFee, string reason)",
        "private static void RefundRetrieveFee(int totalFee, bool shouldRefund)",
        "private static void RemoveRetrievedDepositItems(List<int> depositIndices)",
    ]:
        result = require(storage, snippet, "retrieve-all atomicity helper")
        if result:
            return result
    result = require_before(
        retrieve_all_body,
        "TryRestoreAllDepositItemsForRetrieveAll(depositedItems, failedRestoreIndices)",
        "TryPayRetrieveFee(payableFee, \"ZombieModeTempCourierDepositRetrieveAll\")",
        "restore before retrieve-all payment")
    if result:
        return result
    if "DepositDataManager.ClearAll();" in retrieve_all_body:
        return fail("RetrieveAllItemsAsync must not ClearAll after partial restore/send failures")
    retrieve_clicked_body = extract_method_body(storage, "private static void OnRetrieveAllClicked()")
    if not retrieve_clicked_body:
        return fail("OnRetrieveAllClicked body not found")
    for snippet in [
        "if (isRetrieveAllInProgress)",
        "int totalFee = CalculateTotalRetrieveFee();",
        "if (!CanAffordRetrieveFee(totalFee))",
        "isRetrieveAllInProgress = true;",
        "RetrieveAllItemsAsync(totalFee).Forget();",
    ]:
        result = require(retrieve_clicked_body, snippet, "retrieve-all click must preflight full fee before restoring items")
        if result:
            return result
    result = require_before(
        retrieve_clicked_body,
        "if (!CanAffordRetrieveFee(totalFee))",
        "RetrieveAllItemsAsync(totalFee).Forget();",
        "retrieve-all affordability precheck before item restore")
    if result:
        return result
    result = require(retrieve_all_body, "RemoveRetrievedDepositItems(deliveredIndices);", "remove only delivered deposit records")
    if result:
        return result
    result = require(retrieve_all_body, "RefundRetrieveFee(failedDeliveryFee, failedDeliveryFee > 0);", "refund failed delivery fees")
    if result:
        return result
    result = require(retrieve_all_body, "isRetrieveAllInProgress = false;", "retrieve-all async task must always release reentry guard")
    if result:
        return result

    single_retrieve_body = extract_method_body(storage, "private static void OnItemPurchased(StockShop shop, Item purchasedItem)")
    if not single_retrieve_body:
        return fail("single retrieve OnItemPurchased body not found")
    if "TrySpendZombieModePurificationPointsForRealNpc(" in single_retrieve_body:
        return fail("single temporary courier retrieve must not spend purification before item restore")
    result = require(
        single_retrieve_body,
        "CanAffordZombieModePurificationPointsForRealNpc(courierNPCTransform, fee)",
        "single temporary courier retrieve affordability precheck")
    if result:
        return result

    single_restore_body = extract_method_body(storage, "private static async UniTaskVoid RestoreAndReplaceItemAsync")
    if not single_restore_body:
        return fail("single retrieve RestoreAndReplaceItemAsync body not found")
    result = require(
        storage,
        "private static async UniTaskVoid RestoreAndReplaceItemAsync(Item emptyItem, ItemTreeData savedData, int depositIndex, int purificationFee = 0)",
        "single retrieve restore method accepts deferred purification fee")
    if result:
        return result
    for snippet in [
        "bool purificationPaymentDeducted = false;",
        "bool useTemporaryPurificationRetrieve = IsZombieModeTemporaryCourierPurificationService();",
        "bool deliveryCompleted = false;",
        "RollbackTemporarySingleRetrievePlaceholder(emptyItem);",
        "CleanupSingleRetrievedItem(restoredItem);",
        "string restoredItemName = restoredItem.DisplayName;",
        "ModBehaviour.DevLog(\"[StorageDepositService] 物品取回完成: \" + restoredItemName);",
        "bool shouldRefund = purificationPaymentDeducted && !deliveryCompleted;",
        "RefundZombieModePurificationPointsForRealNpc(courierNPCTransform, purificationFee, shouldRefund);",
    ]:
        result = require(single_restore_body, snippet, "single retrieve purification atomicity")
        if result:
            return result
    if "物品取回完成: \" + restoredItem.DisplayName" in single_restore_body:
        return fail("single retrieve logs restored item after cleanup sentinel is nulled")
    result = require_before(
        single_restore_body,
        "restoredItem = await ItemTreeData.InstantiateAsync(savedData);",
        "TrySpendZombieModePurificationPointsForRealNpc(",
        "single retrieve restore before purification payment")
    if result:
        return result
    result = require(
        single_restore_body,
        "ItemUtilities.SendToPlayer(restoredItem, true, true);\n                deliveryCompleted = true;",
        "single retrieve must mark delivery before removing deposit record")
    if result:
        return result

    result = require(npc_shop, "UnregisterEvents();\n                    Cleanup();", "NPC shop ShowUI failure must unregister global events")
    if result:
        return result

    print("ZombieModeRewardServiceAtomicityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
