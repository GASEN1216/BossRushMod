from pathlib import Path
import sys


COURIER = Path("Integration/NPCs/Courier/CourierNPC.cs")
TRACKER = Path("LootAndRewards/ModeEFLootboxTracker.cs")
TOKEN = Path("Integration/Items/AwenLootSweepTokenConfig.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    courier_text = COURIER.read_text(encoding="utf-8")
    tracker_text = TRACKER.read_text(encoding="utf-8")
    token_text = TOKEN.read_text(encoding="utf-8")

    if "CourierPaidLootSweepService.TryRunPaidSweep" not in courier_text:
        return fail("AwenPaidLootSweepFlowGuard: missing paid sweep service hook")

    if '_overrideInteractNameKey = CourierPaidLootSweepService.PaidSweepInteractKey' not in courier_text:
        return fail("AwenPaidLootSweepFlowGuard: courier main interact is not sweep")

    if "CourierServiceOptionInteractable" not in courier_text:
        return fail("AwenPaidLootSweepFlowGuard: missing courier service sub option")

    service_index = courier_text.find("AddSubInteractable<CourierServiceOptionInteractable>")
    storage_index = courier_text.find("AddSubInteractable<CourierStorageInteractable>")
    if service_index < 0 or storage_index < 0:
        return fail("AwenPaidLootSweepFlowGuard: missing grouped courier option ordering")
    if service_index > storage_index:
        return fail("AwenPaidLootSweepFlowGuard: courier service option is not inserted before storage option")
    if "AddSubInteractable<CourierPaidLootSweepInteractable>" in courier_text:
        return fail("AwenPaidLootSweepFlowGuard: duplicate paid sweep sub option is still being created")

    if "CanUseAwenLootSweepInCurrentMode" not in tracker_text:
        return fail("AwenPaidLootSweepFlowGuard: missing shared sweep range gate")

    if "CopyFreshAwenLootSweepTargets" not in tracker_text:
        return fail("AwenPaidLootSweepFlowGuard: missing fresh target snapshot path for instant price refresh")

    if "CanUseAwenLootSweepToken" in tracker_text and "CopyCurrentAwenLootSweepTargets(awenLootSweepTargetScratch)" in tracker_text:
        return fail("AwenPaidLootSweepFlowGuard: sweep token path still uses cached target snapshot")

    if "仅可在模式E/F中使用" in token_text:
        return fail("AwenPaidLootSweepFlowGuard: token description still says Mode E/F only")

    service_text = Path("Integration/NPCs/Courier/CourierPaidLootSweepService.cs").read_text(encoding="utf-8")

    if "HasPendingSweepResult" not in service_text:
        return fail("AwenPaidLootSweepFlowGuard: missing pending result container state")

    if "DiscardPendingSweepResult" not in service_text:
        return fail("AwenPaidLootSweepFlowGuard: missing next-sweep discard action")

    if "开启下次扫箱" not in service_text:
        return fail("AwenPaidLootSweepFlowGuard: missing next-sweep button text")

    if "PaidSweepTransientLootboxCleanup" in service_text:
        return fail("AwenPaidLootSweepFlowGuard: result crate still destroys itself on loot view close")

    if "SetInService(true)" not in courier_text or "SetInService(false)" not in service_text:
        return fail("AwenPaidLootSweepFlowGuard: paid sweep does not enter/exit courier service state")

    if 'return L10n.T("扫箱", "Sweep Loot")' not in service_text:
        return fail("AwenPaidLootSweepFlowGuard: interact text is not static sweep label")

    custom_confirm_type = "Confirm" + "Dialog" + "UI"
    if custom_confirm_type in service_text:
        return fail("AwenPaidLootSweepFlowGuard: paid sweep still depends on legacy prompt type")

    if "OriginalConfirmDialogueAdapter.Execute(" not in service_text:
        return fail("AwenPaidLootSweepFlowGuard: missing original ConfirmDialogue popup flow")

    if "TryExecuteFreshPaidSweep(" not in service_text:
        return fail("AwenPaidLootSweepFlowGuard: missing confirm-time fresh recompute path")

    if "if (!TryExecuteFreshPaidSweep" not in service_text:
        return fail("AwenPaidLootSweepFlowGuard: confirm result does not branch on fresh execution result")

    if "TryCreateTransientLootbox" in service_text and "ExitServiceState();" not in service_text:
        return fail("AwenPaidLootSweepFlowGuard: service state cleanup is missing from failure paths")

    if "SortSweepResultInventory(" not in service_text:
        return fail("AwenPaidLootSweepFlowGuard: missing sweep result inventory sorting")

    if "CompareSweepResultItems(" not in service_text:
        return fail("AwenPaidLootSweepFlowGuard: missing sweep result item comparer")

    if "NPCInteractionGroupHelper.GetOrCreateGroupList(transientLootbox" not in Path("Integration/NPCs/Courier/CourierService.cs").read_text(encoding="utf-8"):
        return fail("AwenPaidLootSweepFlowGuard: transient lootbox helper does not prepare grouped interaction internals")

    interactables_text = Path("Interactables/BossRushInteractables.cs").read_text(encoding="utf-8")
    if "for (int attempt = 0;" not in interactables_text or "yield return null;" not in interactables_text:
        return fail("AwenPaidLootSweepFlowGuard: lootbox decoration still does not retry across multiple frames")

    if "黑掉 1 件" in service_text or "consume 1 item" in service_text:
        return fail("AwenPaidLootSweepFlowGuard: paid sweep popup still reveals hidden loot tax")

    print("AwenPaidLootSweepFlowGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
