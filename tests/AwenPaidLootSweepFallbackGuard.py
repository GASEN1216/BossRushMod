"""
Guard: paid sweep pending crates must clean up safely while keeping next-sweep entry in LootView only.
"""

from pathlib import Path
import sys


SERVICE = Path("Integration/NPCs/Courier/CourierPaidLootSweepService.cs")
COURIER = Path("Integration/NPCs/Courier/CourierNPC.cs")
TRACKER = Path("LootAndRewards/ModeEFLootboxTracker.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""

    brace_start = text.find("{", start)
    if brace_start < 0:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start:index + 1]

    return ""


def main() -> int:
    service_text = SERVICE.read_text(encoding="utf-8")
    courier_text = COURIER.read_text(encoding="utf-8")
    tracker_text = TRACKER.read_text(encoding="utf-8")

    if "ReleasePendingSweepResultToPlayer" not in service_text:
        return fail("AwenPaidLootSweepFallbackGuard: missing safe pending result release API")

    release_method = extract_method(service_text, "public static void ReleasePendingSweepResultToPlayer")
    if not release_method:
        return fail("AwenPaidLootSweepFallbackGuard: missing public safe release method body")

    for required in (
        "TryReturnResultItemsToPlayer(pendingResultInventory)",
        "DiscardPendingSweepResultInternal(closeLootView, false)",
    ):
        if required not in release_method:
            return fail("AwenPaidLootSweepFallbackGuard: safe release method missing " + required)

    destroy_method = extract_method(courier_text, "public void DestroyCourierNPC()")
    if "CourierPaidLootSweepService.ReleasePendingSweepResultToPlayer(true, false);" not in destroy_method:
        return fail("AwenPaidLootSweepFallbackGuard: courier destroy does not release pending sweep result")

    reset_method = extract_method(tracker_text, "private void ResetModeEFLootboxTrackerState()")
    if "CourierPaidLootSweepService.ReleasePendingSweepResultToPlayer(true, false);" not in reset_method:
        return fail("AwenPaidLootSweepFallbackGuard: Mode E/F tracker reset does not release pending sweep result")

    pending_method = extract_method(service_text, "private static async UniTaskVoid RunPendingSweepResultPromptAsync()")
    if not pending_method:
        return fail("AwenPaidLootSweepFallbackGuard: missing pending prompt method")

    for required in (
        "OriginalConfirmDialogueAdapter.Execute(",
        'L10n.T("打开箱子", "Open Crate")',
        'L10n.T("取消", "Cancel")',
    ):
        if required not in pending_method:
            return fail("AwenPaidLootSweepFallbackGuard: pending prompt missing " + required)

    for forbidden in (
        "RunPendingDiscardNextSweepPromptAsync",
        "其他操作",
        "丢弃旧箱并新扫",
        "保留旧箱",
    ):
        if forbidden in pending_method or forbidden in service_text:
            return fail("AwenPaidLootSweepFallbackGuard: pending confirm still exposes extra branch -> " + forbidden)

    for required in (
        "CreateStartNextSweepButton(",
        "OnStartNextSweepButtonClicked",
        "开启下次扫箱",
        "DiscardPendingSweepResultInternal(true, false)",
        "StartNextSweepDelayed(npc)",
    ):
        if required not in service_text:
            return fail("AwenPaidLootSweepFallbackGuard: missing LootView next-sweep button path -> " + required)

    print("AwenPaidLootSweepFallbackGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
