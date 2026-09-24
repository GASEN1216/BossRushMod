"""ZombieModeReuseCompatibilityGuard: ZombieMode should reuse Duckov/BossRush shared paths."""
from pathlib import Path
import sys
import re
from cs_source_util import clean_source

REWARD_PARTS = [
    Path("ZombieMode/ZombieModeRewards.cs"),
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]
POLLUTION_PARTS = [
    Path("ZombieMode/ZombieModePollution.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs"),
]


def read_rewards() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REWARD_PARTS)


def read_pollution() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in POLLUTION_PARTS)


def fail(message: str) -> int:
    print("ZombieModeReuseCompatibilityGuard: FAIL - " + message)
    return 1


def require(text: str, needle: str, message: str):
    if needle not in text:
        raise AssertionError(message + " -> " + needle)


def forbid(text: str, needle: str, message: str):
    if needle in text:
        raise AssertionError(message + " -> " + needle)


def extract_method(text: str, name: str):
    matches = list(re.finditer(r"\b" + re.escape(name) + r"\s*\([^)]*\)\s*\{", text))
    if len(matches) != 1:
        return None
    opening = text.find("{", matches[0].start())
    depth = 0
    for index in range(opening, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[matches[0].start():index + 1]
    return None


def main() -> int:
    spawner = Path("ZombieMode/ZombieModeSpawner.cs").read_text(encoding="utf-8")
    spawn_core = Path("Utilities/EnemySpawnCore.cs").read_text(encoding="utf-8")
    extraction = Path("ZombieMode/ZombieModeExtractionController.cs").read_text(encoding="utf-8")
    isolation = Path("ZombieMode/ZombieModeMapIsolation.cs").read_text(encoding="utf-8")
    inventory_bridge = clean_source(Path("ZombieMode/ZombieModeInventoryTransfer.cs").read_text(encoding="utf-8"))
    runtime_module = clean_source(Path("ZombieMode/ZombieModeRuntimeModule_InventoryTransfer.cs").read_text(encoding="utf-8"))
    rewards = read_rewards()
    pollution = read_pollution()
    marker = Path("ZombieMode/ZombieModeEnemyRuntime.cs").read_text(encoding="utf-8")

    try:
        require(spawner, "SpawnEnemyCore(", "ZombieMode enemy spawning must reuse shared spawn core")
        require(spawner, "EnsureCharacterPresetsCacheReady();", "ZombieMode must reuse shared preset cache warmup")
        require(spawner, "SpawnPositionHelper.TryFindAroundPlayer", "ZombieMode virtual spawn points must reuse shared geometry helper")
        require(spawner, "SpawnPositionHelper.TrySampleNavMesh", "ZombieMode spawn sampling must reuse shared NavMesh helper")
        require(spawner, "skipBossRushLootTracking: true", "ZombieMode bosses must stay compatible with BossRush loot ownership")
        forbid(spawner, "Resources.FindObjectsOfTypeAll<", "ZombieMode spawner must not restore its own preset scan")
        require(spawn_core, "SpawnEnemyCoreInternalAsync", "shared spawn core must expose observable async completion")
        require(spawn_core, "InvokeSpawnCoreFailureCallback(onFailed, reason)", "shared spawn core must complete callback-backed async wrappers when mode ends")

        require(extraction, "ModeExtractionPointFactory.CreateExtractionPoint(request)", "ZombieMode extraction must reuse shared extraction factory")
        require(extraction, "EvacuationCountdownUI.Request", "ZombieMode extraction must use Duckov evacuation countdown UI")
        require(extraction, "EvacuationCountdownUI.Release", "ZombieMode extraction must release Duckov evacuation countdown UI")
        require(extraction, "LevelManager.Instance.NotifyEvacuated(info)", "ZombieMode success extraction must notify Duckov evacuation flow")
        require(extraction, "SimplePointOfInterest.Create", "ZombieMode safe-zone map marker must use Duckov minimap POI")

        require(isolation, "OriginalExtractionPointIsolationHelper.Disable", "ZombieMode map isolation must reuse shared extraction isolation")
        require(isolation, "OriginalExtractionPointIsolationHelper.Restore", "ZombieMode extraction isolation must restore through shared helper")

        transfer = extract_method(runtime_module, "TryMoveZombieModeEntryItemToStorageOrInbox")
        rollback = extract_method(runtime_module, "RollbackZombieModeInventoryTransfer")
        require(transfer or "", "ReforgeDataPersistence.SyncCurrentReforgeState(item);", "ZombieMode runtime-module inventory transfer must sync reforge state before storage/inbox handoff")
        require(transfer or "", "PlayerStorageBuffer.Buffer.Add(itemData);", "ZombieMode runtime-module inbox fallback must use direct storage buffer writes like courier service")
        forbid(transfer or "", "PlayerStorage.Push(item, true);", "ZombieMode inventory transfer must not use opaque PlayerStorage.Push for pre-active inbox fallback")
        require(rollback or "", "ItemUtilities.SendToPlayer(item, false, false);", "ZombieMode runtime-module rollback must use Duckov item return helper")
        require(inventory_bridge, "module.TryMoveZombieModeEntryItemToStorageOrInbox(item)", "host inventory compatibility method must forward to runtime module")
        require(inventory_bridge, "module.RollbackZombieModeInventoryTransfer()", "host rollback compatibility method must forward to runtime module")

        require(rewards, "ReforgeDataPersistence.SyncCurrentReforgeState(item);", "ZombieMode insurance/storage handoff must sync reforge state")
        require(rewards, "ItemUtilities.SendToPlayerCharacterInventory(item, false)", "ZombieMode rewards must use Duckov inventory helper first")
        require(rewards, "item.Drop(dropPosition, true", "ZombieMode reward fallback must use Duckov item.Drop")

        require(marker, "public readonly System.Collections.Generic.List<BossRushStatModifierRecord> RuntimeModifierRecords", "ZombieMode enemies must track runtime stat modifiers")
        require(pollution, "RuntimeStatModifierTracker.TryAdd", "ZombieMode pollution/enemy buffs must use shared runtime modifier tracker")
        require(pollution, "RuntimeStatModifierTracker.RemoveAll", "ZombieMode pollution/enemy buffs must use shared runtime modifier cleanup")
        forbid(pollution, "new Modifier(", "ZombieMode pollution must not hand-roll runtime stat modifiers")
        forbid(pollution, ".AddModifier(", "ZombieMode pollution must not bypass RuntimeStatModifierTracker")
    except AssertionError as exc:
        return fail(str(exc))

    print("ZombieModeReuseCompatibilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
