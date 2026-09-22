from pathlib import Path
import sys
from cs_source_util import clean_source


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
    text = clean_source(text)
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
    if not full_cleanup:
        return fail("full real NPC cleanup body not found")
    result = require_before(
        full_cleanup,
        "CloseZombieModeTemporaryRealNpcServices(npc);",
        "Destroy(npc.GameObject);",
        "full real NPC cleanup",
    )
    if result:
        return result

    # 安全区绑定的回收路径改为经 run-only 记录清理：RemoveZombieModeRunOnlyObjectRecord
    # 会执行注册时挂上的 CloseZombieModeTemporaryRealNpcServices 回调（见下方注册断言），
    # 同时清掉指向已销毁 NPC 的失效记录。契约不变——服务必须在销毁之前关闭。
    safe_zone_cleanup = extract_method_body(drops, "private void RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(int runId)")
    if not safe_zone_cleanup:
        return fail("safe-zone real NPC cleanup body not found")
    result = require_before(
        safe_zone_cleanup,
        "RemoveZombieModeRunOnlyObjectRecord(npc.GameObject);",
        "Destroy(npc.GameObject);",
        "safe-zone real NPC cleanup",
    )
    if result:
        return result

    for snippet in [
        "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.TemporaryNpc, npc, npc, () => CloseZombieModeTemporaryRealNpcServices(npc));",
    ]:
        result = require(rewards, snippet, "run-only real NPC service cleanup")
        if result:
            return result

    retrieve_all_body = extract_method_body(storage, "private static async UniTaskVoid RetrieveAllItemsAsync(int totalFee)")
    single_body = extract_method_body(storage, "internal static async UniTask<bool> RetrieveSingleAsync(int itemTypeID, int amount)")
    for body, label in [(retrieve_all_body, "bulk"), (single_body, "single")]:
        if not body:
            return fail(label + " retrieve body missing")
        for token in ["TryBeginTransaction()", "IsCurrentTransaction(transaction)", "DepositDataManager.RemoveItem(",
                      "TryDeliverRetrievedItem(", "RefundRetrieveFee(", "EndTransaction(transaction)"]:
            result = require(body, token, label + " retrieve ownership and compensation")
            if result: return result
        result = require_before(body, "await ", "TryPayRetrieveFee(", label + " restore before payment")
        if result: return result
        result = require_before(body, "TryDeliverRetrievedItem(", "DepositDataManager.RemoveItem(", label + " delivery before record commit")
        if result: return result
        if "DepositDataManager.ClearAll();" in body:
            return fail(label + " retrieve must preserve failed records")
    for token in ["DepositDataManager.RemoveItem(restored.DepositData);", "RefundRetrieveFee(paidFee - deliveredFee, paidFee > deliveredFee, transaction);",
                  "if (ReferenceEquals(transactionOwner, transaction))", "CleanupRestoredRetrieveAllItems(restoredItems);"]:
        result = require(retrieve_all_body, token, "bulk uses stable records and cannot release a replacement owner")
        if result: return result
    for token in ["DepositDataManager.RemoveItem(record);", "if (paid && !delivered) RefundRetrieveFee(fee, true, transaction);",
                  "CleanupSingleRetrievedItem(restoredItem);", "DepositDataManager.IndexOf(record) < 0"]:
        result = require(single_body, token, "single retrieve preserves failed records")
        if result: return result
    clicked = extract_method_body(storage, "private static void OnRetrieveAllClicked()")
    result = require_before(clicked, "if (!CanAffordRetrieveFee(totalFee))", "RetrieveAllItemsAsync(totalFee).Forget();", "bulk preflight")
    if result: return result
    for token in ["if (IsTransactionBusy || !DepositDataManager.CanWrite)", "TryRestoreAllDepositItemsForRetrieveAll(depositedItems, failedRestoreIndices, transaction)"]:
        result = require(storage, token, "shared transaction gate")
        if result: return result
    for token in ['[HarmonyLib.HarmonyPatch(typeof(StockShop), "Buy")]', "if (!StorageDepositService.OwnsShop(__instance)) return true;",
                  "__result = StorageDepositService.RetrieveSingleAsync(itemTypeID, amount);"]:
        result = require(storage, token, "owned shop intercepts before official charge and placeholder")
        if result: return result

    result = require(npc_shop, "UnregisterEvents();\n                    Cleanup();", "NPC shop ShowUI failure must unregister global events")
    if result:
        return result

    print("ZombieModeRewardServiceAtomicityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
