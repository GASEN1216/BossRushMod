"""Integration leaf flows keep their state owners and legacy call paths."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def body(source, signature):
    assert source.count(signature) == 1, "missing/duplicate signature: " + signature
    start = source.index("{", source.index(signature))
    level = 1
    end = start + 1
    while level:
        level += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start + 1:end - 1]


def compact(source):
    return re.sub(r"\s+", "", source)


def ordered(source, tokens, name):
    pos = 0
    for token in tokens:
        found = source.find(token, pos)
        assert found >= 0, name + " missing/order changed: " + token
        pos = found + len(token)


def main():
    nurse = read("Integration/NPCs/Nurse/NurseNPC.cs")
    host = read("Integration/NPCs/Nurse/NurseNPCRuntimeModuleHostBridge.cs")
    assert "partial class ModBehaviour" not in nurse, "nurse state returned to host"
    assert "internal sealed class NurseNpcRuntimeModule : BossRushRuntimeModuleBase" in nurse
    for token in ("private GameObject nurseNPCInstance = null;", "private NurseNPCController nurseController = null;",
                  "private static AssetBundle nurseAssetBundle = null;", "private static GameObject nursePrefab = null;"):
        assert token in nurse, "nurse state lifetime changed: " + token
    assert compact(body(nurse, "internal static GameObject GetPrefabForRuntime()")) == "returnLoadNurseAssetBundle()?nursePrefab:null;", "nurse prefab query bypassed loader"
    assert compact(body(nurse, "public override void OnDestroy()")) == "owner=null;", "nurse cleanup order changed"
    spawn = body(nurse, "internal void SpawnNurseNPC(")
    ordered(spawn, ["NPCAffinityInteractionHelper.ApplyDailyDecayOnSpawn", "AffinityManager.IsMarriedToPlayer", "if (nurseNPCInstance != null)",
                    "if (!LoadNurseAssetBundle())", "!ShouldSpawnNurse(currentSceneName)", "GetNurseSpawnPosition(currentSceneName)",
                    "Physics.Raycast", "UnityEngine.Object.Instantiate", "nurseNPCInstance.SetActive(true)",
                    "NPCCommonUtils.FixShaders", "NPCCommonUtils.SetLayerRecursively", "GetComponent<NurseNPCController>",
                    "GetComponent<NurseMovement>", "nurseMovement.SetSceneName", "if (stayStillOnSpawn)", "GetComponent<NurseInteractable>"], "nurse spawn")
    assert "nurseNpcRuntime.SpawnNurseNPC(overrideSpawnPos, stayStillOnSpawn, forceSpawn);" in body(host, "public void SpawnNurseNPC("), "nurse public bridge disconnected"
    registration = read("ModBehaviourRuntimeModules.cs")
    assert registration.count("new NurseNpcRuntimeModule()") == 1 and "runtimeModuleHost.Register(nurseNpcRuntime);" in registration, "nurse must register one stored instance"

    seed = read("Integration/BackMountain/BackMountainSeedDrops.cs")
    assert "internal sealed partial class BackMountainRuntimeModule" in seed and "partial class ModBehaviour" not in seed
    ordered(body(seed, "private Item TryRollBackMountainSeed("), ["_owner.IsBackMountainConfiguredEnabled()", "BackMountainUnlocks.IsFacilityUnlocked", "ResolveBackMountainSeedTypeId", "if (seedTypeId <= 0)", "UnityEngine.Random.value", "BackMountainItems.EnsureRuntimeRegistration", "ItemAssetsCollection.InstantiateSync"], "seed gate/roll")
    assert "private const float BackMountainSeedDropChance = 0.25f;" in seed
    seed_host = read("Integration/BackMountain/BackMountainSeedDropsHostBridge.cs")
    for name, args in (("TryAddBackMountainSeedLoot", "inv, bossMain"), ("TryAddBackMountainSeedToCharacterItem", "bossMain"), ("TryDropBackMountainSeedIntoWorld", "bossMain")):
        assert compact(body(seed_host, "private void " + name + "(")) == compact("backMountainRuntime." + name + "(" + args + ");"), "seed bridge disconnected: " + name

    registry = read("Integration/BossRushDynamicItemRegistry.cs")
    items = read("Integration/Items/ItemContentRegistry.cs")
    for source in (registry, items):
        assert "internal sealed partial class IntegrationRuntimeModule" in source and "partial class ModBehaviour" not in source, "item registration returned to host"
    assert "internal static bool DynamicItemsInitialized { get; set; }" in registry
    assert "internal static int BossRushTicketTypeId { get; set; } = -1;" in registry
    ticket = body(registry, "internal bool EnsureBossRushTicketItemRegisteredForDynamicRegistry()")
    ordered(ticket, ["HasRegisteredPrefabWithoutEnsuring", "BossRushTicketTypeId = BossRushItemIds.BossRushTicket", "ResourceBundleLoader.LoadFromFile", "ResourceBundleLoader.LoadAllAssets", 'AddTagsToItem(itemPrefab, new string[] { "Key", "SpecialKey" })', "ItemAssetsCollection.AddDynamicEntry", "itemPrefab.TypeID == BossRushItemIds.BossRushTicket", "BossRushTicketTypeId <= 0 && itemPrefab.TypeID > 0", "finally", "AssetBundleUnloadHelper.TryUnload"], "ticket registration")
    integration_host = read("Integration/BossRushIntegration.cs")
    for name in ("EnsureItemContentConfiguratorsRegisteredForDynamicRegistry", "EnsureBossRushTicketItemRegisteredForDynamicRegistry", "EnsureBirthdayCakeItemRegisteredForDynamicRegistry", "EnsureAdventureJournalItemRegisteredForDynamicRegistry"):
        assert compact(body(integration_host, "internal bool " + name + "()")) == "returnbossRushIntegrationRuntime." + name + "();", "item registration bridge disconnected: " + name

    flow = read("Integration/Mutators/MutatorModeFlow.cs")
    ordered(body(flow, "internal static void TryRollMutatorsForMode("), ["if (!enabled)", "CharacterMainControl.Main", "if (player == null)", "Mathf.Clamp(requestedCount, minimumCount, maximumCount)", "MutatorManager.RollAndApply(player, count, null, modeTag)", "MutatorUI.ShowBanner()"], "mutator roll")
    ordered(body(flow, "internal static void ClearMutatorsForMode("), ["MutatorManager.RemoveAll()", "MutatorUI.HideAll()"], "mutator cleanup")
    managed = read("Integration/ModeGManagedCharacterService.cs")
    assert "internal static class ModeGManagedCharacterService" in managed and "partial class ModBehaviour" not in managed
    ordered(body(managed, "internal static void CleanupModeGManagedCharacter("), ["state.UnregisterStagingBoss", "state.UnregisterTrackedBoss", "owner.UnregisterDragonDescendantEnemyRecovery", "owner.ClearDragonDescendantBossRandomLootTracking", "owner.FinalizeDragonDescendantBossRushLootboxPathTracking", "BossCleanupHelpers.DestroyRuntimePreset", "DestroyManagedCharacterQuiet"], "managed cleanup")
    managed_host = read("Integration/DragonDescendant/DragonDescendantRuntimeModuleHostBridge.cs")
    for name in ("CreateModeGManagedCharacterAsync", "BeginActivateModeGManagedCharacter", "CompleteActivateModeGManagedCharacter", "CleanupModeGManagedCharacter", "DestroyManagedCharacterQuiet", "HasModeGPlayerAuthoredBuff", "ActivateModeGManagedCharacter"):
        assert "ModeGManagedCharacterService." + name + "(" in managed_host, "managed bridge disconnected: " + name
    print("IntegrationLeafOwnershipGuard: PASS")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("IntegrationLeafOwnershipGuard: FAIL - " + str(error))
        raise SystemExit(1)
