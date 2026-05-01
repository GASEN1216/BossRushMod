from pathlib import Path
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
INVENTORY = Path("ZombieMode/ZombieModeInventoryTransfer.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
PURIFICATION = Path("ZombieMode/ZombiePurificationPointController.cs")
EXTRACTION = Path("ZombieMode/ZombieModeExtractionController.cs")
BEACON_USAGE = Path("Integration/Items/ZombieTideBeaconUsage.cs")
F3_DEBUG = Path("DebugAndTools/F3DebugCheatMenu.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModePhase2LoopGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    entry = ENTRY.read_text(encoding="utf-8")
    inventory = INVENTORY.read_text(encoding="utf-8")
    spawner = SPAWNER.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    rewards = Path("ZombieMode/ZombieModeRewards.cs").read_text(encoding="utf-8")
    purification = PURIFICATION.read_text(encoding="utf-8")
    extraction = EXTRACTION.read_text(encoding="utf-8")
    beacon_usage = BEACON_USAGE.read_text(encoding="utf-8")
    f3_debug = F3_DEBUG.read_text(encoding="utf-8")

    for snippet in [
        "ZombieModeLifecyclePhase.InitializingRun",
        "InitializeZombieModeRunAfterMapLoaded",
        "FinalizeZombieModeEntryResources",
        "RefundZombieModeInvitationIfNeeded",
        "EntryResourcesFinalized",
        "ItemAssetsCollection.Search(filter)",
        "ResolveZombieModeTags",
    ]:
        result = require(entry, snippet, "entry transaction/loadout")
        if result:
            return result

    for snippet in [
        "CollectZombieModeTopLevelPlayerItems",
        "PlayerStorage.Inventory",
        "storage.AddItem(item)",
        "RollbackZombieModeInventoryTransferShell",
        "BossRushItemIds.ZombieTideInvitation",
        "BossRushItemIds.ZombieTideBeacon",
    ]:
        result = require(inventory, snippet, "naked-entry transfer")
        if result:
            return result
    if "PlayerStorage.Push(item, true)" in inventory:
        return fail("ZombieModePhase2LoopGuard: inventory transfer must not destroy items before Active rollback boundary")

    for snippet in [
        "ZOMBIE_MODE_NORMAL_PRESET_NAME = \"Cname_Zombie\"",
        "CreateCharacterAsync",
        "mapConfig.modeESpawnPoints",
        "AddZombieModeSpawnPoint(center + offset, true)",
        "RegisterZombieModeEnemyRuntimeShell(runId, zombie, false, ZombieModeBossKind.Titan, -1, enemyKind, specialKind, eliteAffixes)",
    ]:
        result = require(spawner, snippet, "zombie spawn path")
        if result:
            return result
    if "SpawnEnemyCore" in spawner:
        return fail("ZombieModePhase2LoopGuard: ZombieModeSpawner must not call SpawnEnemyCore")

    for snippet in [
        "Health.OnDead += zombieModeOnDeadHandler",
        "HandleZombieModeHealthDead(runId",
        "marker.RunId != runId",
        "ZombieModeTuning.PreparationCountdownSeconds",
        "zombieModeRunState.PreparationTimer -= Time.unscaledDeltaTime",
        "effectiveSpawnPointCount + (zombieModeRunState.CurrentWave - 1) * 5",
        "zombieModeRunState.EffectiveSpawnPoints.Count",
        "zombieModeRunState.BeaconChanneling || zombieModeRunState.ExtractionChanneling",
        "BeginZombieModePreparation",
        "SpawnZombieModeWaveAsync",
        "CompleteZombieModeWave",
        "wave % 5 == 0",
        "CreateZombieModePurificationPoint(runId",
    ]:
        result = require(waves, snippet, "wave/death loop")
        if result:
            return result

    if "BeginZombieModeExtractionOpportunity(runId)" not in waves and "BeginZombieModeExtractionOpportunity(runId)" not in rewards:
        return fail("ZombieModePhase2LoopGuard: missing extraction opportunity transition")

    for snippet in [
        "SpendZombieModePurificationPoints",
        "zombieModeRunState.PurificationPoints -= cost",
        "BossRush_ZombieMode_Notify_RefreshNoPoints",
        "ZombieModeRewardType.AttributeMaxHealth",
        "ZombieModeRewardType.TempMerchant",
        "ZombieModeRewardType.TempNurse",
        "ZombieModeRewardType.FortificationPack",
        "ZombieModeRewardType.ContractPollutionDeal",
        "ZombieModeRewardType.InsuranceKeepOne",
        "ApplyZombieModeAttributeReward",
        "SpawnZombieModeTemporaryNpc",
        "GrantZombieModeFortificationPack",
        "ApplyZombieModeContractPollutionDeal",
        "ApplyZombieModeInsuranceReward",
        "SettleZombieModeFailureInsuranceShell",
        "CollectZombieModeInsuranceCandidates",
        "TryMoveZombieModeInsuredItemToStorage",
        "BossRush_ZombieMode_Settle_InsuranceSaved",
        "zombieModeRunState.InsuranceState.SpecifiedKeepItem",
        "zombieModeRunState.InsuranceState.SpecifiedKeepItem =",
        "string pendingTemporaryNpcServiceType",
        "if (!string.IsNullOrEmpty(pendingTemporaryNpcServiceType))",
        "FoldableCoverPackConfig.TYPE_ID",
        "ReinforcedRoadblockPackConfig.TYPE_ID",
        "BarbedWirePackConfig.TYPE_ID",
        "EmergencyRepairSprayConfig.TYPE_ID",
        "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.TemporaryNpc",
        "zombieModeRunState.TemporaryNpcs.Add",
        "zombieModeRunState.InsuranceState.RandomKeepRatio",
        "zombieModeRunState.PollutionFromContracts",
        "zombieModeRunState.ContractAffixWeights",
    ]:
        result = require(rewards, snippet, "reward economy/npc/insurance")
        if result:
            return result
    if "EconomyManager.Pay" in rewards or "new Cost((long)cost)" in rewards:
        return fail("ZombieModePhase2LoopGuard: reward refresh and services must spend PurificationPoints, not external cash")

    for snippet in [
        "CreateZombieModePurificationPoint",
        "CollectZombieModePurificationPoint",
        "ZombieModeRunOnlyObjectKind.PurificationPoint",
        "PICKUP_DISTANCE",
        "AUTO_COLLECT_SECONDS",
    ]:
        result = require(purification, snippet, "purification pickup")
        if result:
            return result

    for snippet in [
        "CanUseZombieModeBeacon",
        "TryUseZombieModeBeacon",
        "BeaconChanneling",
        "ExtractionChanneling",
        "ExtractionSuccessHandled",
        "ZombieModePhaseGuards.AllowsBeacon",
        "ZombieModePhaseGuards.AllowsExtraction",
        "StartZombieModeWave(zombieModeRunState.RunId)",
        "StartZombieModeCoroutine(ZombieModeExtractionCountdownCoroutine(runId), runId)",
        "ZombieModeExtractionCountdownCoroutine",
        "CompleteZombieModeExtractionSuccess",
        "SettleZombieModeExtractionCashShell",
        "ZombieModeFailureReason.SuccessfulExtraction",
    ]:
        result = require(extraction, snippet, "beacon/extraction")
        if result:
            return result

    for snippet in [
        "zombieModeRunState.ExtractionSuccessHandled = true;",
        "zombieModeRunState.ExtractionChanneling ||",
        "zombieModeRunState.ExtractionSuccessHandled)",
        "!zombieModeRunState.ExtractionSuccessHandled",
    ]:
        result = require(extraction, snippet, "extraction dedupe guard")
        if result:
            return result
    if "DeliverZombieModeExtractionRewards" in extraction:
        return fail("ZombieModePhase2LoopGuard: extraction success must not grant random reward items")
    if "StartZombieModeExtraction(zombieModeRunState.RunId)" in extraction:
        return fail("ZombieModePhase2LoopGuard: beacon must not start extraction")

    for snippet in [
        "inst.CanUseZombieModeBeacon()",
        "inst.TryUseZombieModeBeacon()",
    ]:
        result = require(beacon_usage, snippet, "beacon usage hook")
        if result:
            return result

    for snippet in [
        "GrantZombieInvitationAndOpenMapSelectionFromF3",
        "TriggerZombieModeExtractionFromF3",
        "ResetZombieModeFromF3",
        "ZombieModeMapSelectionHelper.ShowZombieModeMapSelection",
        "TryUseZombieModeBeacon()",
        "DebugResetZombieModeShell()",
    ]:
        result = require(f3_debug, snippet, "F3 zombie debug hook")
        if result:
            return result

    print("ZombieModePhase2LoopGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
