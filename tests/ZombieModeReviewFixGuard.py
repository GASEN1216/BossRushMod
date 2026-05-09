"""ZombieModeReviewFixGuard: review fixes stay in place.

This guard covers the concrete issues found in the Zombie Mode style/perf review:
- health scaling must not call the full boss stat multiplier;
- run-only enemy records must be pruned when enemies leave live gameplay;
- temporary NPC protection must be throttled;
- ZombieMode primitive visuals must not use renderer.material.color;
- entry prechecks must not block quest/bound/run-only top-level items; entry
  transfers all carried/equipped top-level items to storage after map load.
"""

from pathlib import Path
import re
import sys


ZOMBIE_FILES = list(Path("ZombieMode").glob("*.cs"))
MODELS = Path("ZombieMode/ZombieModeModels.cs")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
INVENTORY = Path("ZombieMode/ZombieModeInventoryTransfer.cs")
POLLUTION = Path("ZombieMode/ZombieModePollution.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
WAVE = Path("ZombieMode/ZombieModeWaveController.cs")
DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
CLEANUP = Path("ZombieMode/ZombieModeCleanup.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def uncommented_contains_call(text: str, call: str) -> bool:
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("//"):
            continue
        if call in stripped:
            return True
    return False


def main() -> int:
    combined = "\n".join(path.read_text(encoding="utf-8") for path in ZOMBIE_FILES)
    models = MODELS.read_text(encoding="utf-8")
    entry = ENTRY.read_text(encoding="utf-8")
    inventory = INVENTORY.read_text(encoding="utf-8")
    pollution = POLLUTION.read_text(encoding="utf-8")
    spawner = SPAWNER.read_text(encoding="utf-8")
    wave = WAVE.read_text(encoding="utf-8")
    drops = DROPS.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    cleanup = CLEANUP.read_text(encoding="utf-8")

    for path in ZOMBIE_FILES:
        text = path.read_text(encoding="utf-8")
        if uncommented_contains_call(text, "ApplyBossStatMultiplier("):
            return fail("ZombieModeReviewFixGuard: ZombieMode must not call full ApplyBossStatMultiplier -> " + str(path))

    for token, text, label in [
        ("ApplyZombieModeHealthOnlyMultiplier", pollution, "health-only helper"),
        ("ApplyZombieModeHealthOnlyMultiplier(enemy, healthMultiplier, marker);", pollution, "normal enemy health scaling"),
        ("ApplyZombieModeHealthOnlyMultiplier(boss, healthMultiplier", spawner, "boss health scaling"),
    ]:
        if token not in text:
            return fail("ZombieModeReviewFixGuard: missing " + label + " -> " + token)

    for token, text, label in [
        ("PruneZombieModeRunOnlyEnemyRecords", cleanup, "run-only enemy pruning helper"),
        ("PruneZombieModeRunOnlyEnemyRecords(runId);", wave, "death pruning call"),
        ("PruneZombieModeRunOnlyEnemyRecords(runId);", cleanup, "runtime cleanup pruning call"),
    ]:
        if token not in text:
            return fail("ZombieModeReviewFixGuard: missing " + label + " -> " + token)

    for token in [
        "RecycleZombieModeFarEnemiesForPerformance",
        "CanRecycleZombieModeEnemyForPerformance",
        "RecycleZombieModeEnemyForPerformance",
    ]:
        if token in combined:
            return fail("ZombieModeReviewFixGuard: performance recycle path should be removed -> " + token)

    for token in [
        "public float LastTemporaryNpcProtectionTickTime;",
        "public const float TemporaryNpcProtectionTickIntervalSeconds",
    ]:
        if token not in models:
            return fail("ZombieModeReviewFixGuard: missing throttled NPC state/tuning -> " + token)

    for token in [
        "Time.unscaledTime - zombieModeRunState.LastTemporaryNpcProtectionTickTime",
        "ZombieModeTuning.TemporaryNpcProtectionTickIntervalSeconds",
        "zombieModeRunState.LastTemporaryNpcProtectionTickTime = Time.unscaledTime;",
    ]:
        if token not in rewards:
            return fail("ZombieModeReviewFixGuard: temporary NPC protection is not throttled -> " + token)

    if ".material.color" in combined:
        return fail("ZombieModeReviewFixGuard: ZombieMode still uses renderer.material.color")
    if "MaterialPropertyBlock" not in combined or "SetPropertyBlock" not in combined:
        return fail("ZombieModeReviewFixGuard: ZombieMode visual colors must use MaterialPropertyBlock")

    for token, text, label in [
        ("HasZombieModeBlockedTransferItem", inventory, "blocked item detector"),
        ("TryGetZombieModeBlockedTransferMessage", inventory, "blocked item message helper"),
        ("IsZombieModeBlockedTransferItem", inventory, "blocked item classifier"),
        ("ItemHasZombieModeTransferBlockTag", inventory, "blocked item tag checker"),
        ("DontDropOnDeadInSlot", inventory, "bound item tag block"),
        ("ZombieModeFailureReason.BlockedTaskOrBoundItems", entry, "precheck failure reason"),
        ("BossRush_ZombieMode_Notify_HasBoundItems", inventory, "blocked item notification"),
    ]:
        if token in text:
            return fail("ZombieModeReviewFixGuard: entry must not block carried/equipped items via " + label + " -> " + token)

    transfer_candidate_match = re.search(
        r"private\s+void\s+AddZombieModeTransferCandidate\s*\([^)]*\)\s*\{(.+?)\n\s{8}\}",
        inventory,
        re.S,
    )
    if transfer_candidate_match is None:
        return fail("ZombieModeReviewFixGuard: AddZombieModeTransferCandidate not found")
    transfer_candidate_body = transfer_candidate_match.group(1)
    for token in [
        "BossRushItemIds.ZombieTideInvitation",
        "BossRushItemIds.ZombieTideBeacon",
        "DontDropOnDeadInSlot",
        "TryFindQuestTag",
    ]:
        if token in transfer_candidate_body:
            return fail("ZombieModeReviewFixGuard: transfer candidate must not exclude player items -> " + token)

    transfer_shell_match = re.search(
        r"private\s+bool\s+PrepareZombieModeInventoryTransferShell\s*\([^)]*\)\s*\{(.+?)\n\s{8}\}",
        inventory,
        re.S,
    )
    if transfer_shell_match is None:
        return fail("ZombieModeReviewFixGuard: PrepareZombieModeInventoryTransferShell not found")
    transfer_shell_body = transfer_shell_match.group(1)
    if "storage.AddItem(item)" in transfer_shell_body or "PlayerStorage.Inventory.AddItem(item)" in transfer_shell_body:
        return fail("ZombieModeReviewFixGuard: entry transfer must not fail just because storage grid is full")
    if "PlayerStorage.Instance == null" in transfer_shell_body:
        return fail("ZombieModeReviewFixGuard: entry transfer must not fail just because PlayerStorage instance is absent")

    transfer_helper_match = re.search(
        r"private\s+bool\s+TryMoveZombieModeEntryItemToStorageOrInbox\s*\([^)]*\)\s*\{(.+?)\n\s{8}\}",
        inventory,
        re.S,
    )
    if transfer_helper_match is None:
        return fail("ZombieModeReviewFixGuard: TryMoveZombieModeEntryItemToStorageOrInbox not found")
    transfer_helper_body = transfer_helper_match.group(1)
    detach_index = transfer_helper_body.find("item.Detach();")
    inbox_index = transfer_helper_body.find("PlayerStorageBuffer.Buffer.Add(itemData);")
    if detach_index < 0 or inbox_index < 0 or detach_index > inbox_index:
        return fail("ZombieModeReviewFixGuard: entry transfer must detach then write to storage inbox buffer fallback")
    for token in [
        "storage.GetFirstEmptyPosition(0)",
        "storage.AddAt(item, firstEmptyPosition)",
        "zombieModeEntryTransaction.InventoryTransferredItems.Add(item);",
        "ReforgeDataPersistence.SyncCurrentReforgeState(item);",
        "PlayerStorageBuffer.Buffer.Add(itemData);",
        "zombieModeEntryTransaction.InventoryTransferredInboxItems.Add(itemData);",
        "item.DestroyTree();",
    ]:
        if token not in transfer_helper_body:
            return fail("ZombieModeReviewFixGuard: transfer helper must store live items or inbox fallback -> " + token)
    if "PlayerStorage.Push(item, true)" in transfer_helper_body:
        return fail("ZombieModeReviewFixGuard: entry transfer inbox fallback must use direct PlayerStorageBuffer write, not PlayerStorage.Push")

    rollback_match = re.search(
        r"private\s+void\s+RollbackZombieModeInventoryTransferShell\s*\([^)]*\)\s*\{(.+?)\n\s{8}\}",
        inventory,
        re.S,
    )
    if rollback_match is None:
        return fail("ZombieModeReviewFixGuard: RollbackZombieModeInventoryTransferShell not found")
    rollback_body = rollback_match.group(1)
    for token in [
        "zombieModeEntryTransaction.InventoryTransferredInboxItems",
        "PlayerStorageBuffer.Buffer.Remove(itemData)",
        "zombieModeEntryTransaction.InventoryTransferredInboxItems.Clear();",
    ]:
        if token not in rollback_body:
            return fail("ZombieModeReviewFixGuard: rollback must remove direct inbox-buffer entries -> " + token)

    precheck_match = re.search(
        r"private\s+bool\s+TryRunZombieModePrechecks\s*\([^)]*\)\s*\{(.+?)\n\s{8}\}",
        entry,
        re.S,
    )
    if precheck_match is None:
        return fail("ZombieModeReviewFixGuard: TryRunZombieModePrechecks not found")
    precheck_body = precheck_match.group(1)
    if precheck_body.find("TryGetZombieModeBlockedTransferMessage") >= 0:
        return fail("ZombieModeReviewFixGuard: precheck still calls blocked item message helper")

    print("ZombieModeReviewFixGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
