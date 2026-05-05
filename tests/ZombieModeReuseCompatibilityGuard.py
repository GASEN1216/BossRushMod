"""ZombieModeReuseCompatibilityGuard: ZombieMode should reuse Duckov/BossRush shared paths."""
from pathlib import Path
import sys


def fail(message: str) -> int:
    print("ZombieModeReuseCompatibilityGuard: FAIL - " + message)
    return 1


def require(text: str, needle: str, message: str):
    if needle not in text:
        raise AssertionError(message + " -> " + needle)


def forbid(text: str, needle: str, message: str):
    if needle in text:
        raise AssertionError(message + " -> " + needle)


def main() -> int:
    spawner = Path("ZombieMode/ZombieModeSpawner.cs").read_text(encoding="utf-8")
    spawn_core = Path("Utilities/EnemySpawnCore.cs").read_text(encoding="utf-8")
    extraction = Path("ZombieMode/ZombieModeExtractionController.cs").read_text(encoding="utf-8")
    isolation = Path("ZombieMode/ZombieModeMapIsolation.cs").read_text(encoding="utf-8")
    inventory = Path("ZombieMode/ZombieModeInventoryTransfer.cs").read_text(encoding="utf-8")
    rewards = Path("ZombieMode/ZombieModeRewards.cs").read_text(encoding="utf-8")
    pollution = Path("ZombieMode/ZombieModePollution.cs").read_text(encoding="utf-8")
    marker = Path("ZombieMode/ZombieModeEnemyRuntime.cs").read_text(encoding="utf-8")

    try:
        require(spawner, "SpawnEnemyCore(", "ZombieMode enemy spawning must reuse shared spawn core")
        require(spawner, "EnsureCharacterPresetsCacheReady();", "ZombieMode must reuse shared preset cache warmup")
        require(spawner, "SpawnPositionHelper.TryFindAroundPlayer", "ZombieMode virtual spawn points must reuse shared geometry helper")
        require(spawner, "SpawnPositionHelper.TrySampleNavMesh", "ZombieMode spawn sampling must reuse shared NavMesh helper")
        require(spawner, "skipBossRushLootTracking: true", "ZombieMode bosses must stay compatible with BossRush loot ownership")
        forbid(spawner, "Resources.FindObjectsOfTypeAll<", "ZombieMode spawner must not restore its own preset scan")
        require(spawn_core, "InvokeSpawnCoreFailureCallback(onFailed, \"模式结束\")", "shared spawn core must complete callback-backed async wrappers when mode ends")

        require(extraction, "ModeExtractionPointFactory.CreateExtractionPoint(request)", "ZombieMode extraction must reuse shared extraction factory")
        require(extraction, "EvacuationCountdownUI.Request", "ZombieMode extraction must use Duckov evacuation countdown UI")
        require(extraction, "EvacuationCountdownUI.Release", "ZombieMode extraction must release Duckov evacuation countdown UI")
        require(extraction, "LevelManager.Instance.NotifyEvacuated(info)", "ZombieMode success extraction must notify Duckov evacuation flow")
        require(extraction, "SimplePointOfInterest.Create", "ZombieMode safe-zone map marker must use Duckov minimap POI")

        require(isolation, "OriginalExtractionPointIsolationHelper.Disable", "ZombieMode map isolation must reuse shared extraction isolation")
        require(isolation, "OriginalExtractionPointIsolationHelper.Restore", "ZombieMode extraction isolation must restore through shared helper")

        require(inventory, "ReforgeDataPersistence.SyncCurrentReforgeState(item);", "ZombieMode inventory transfer must sync reforge state before storage/inbox handoff")
        require(inventory, "PlayerStorageBuffer.Buffer.Add(itemData);", "ZombieMode inventory transfer inbox fallback must use direct storage buffer writes like courier service")
        forbid(inventory, "PlayerStorage.Push(item, true);", "ZombieMode inventory transfer must not use opaque PlayerStorage.Push for pre-active inbox fallback")
        require(inventory, "ItemUtilities.SendToPlayer(item, false, false);", "ZombieMode rollback must use Duckov item return helper")

        require(rewards, "ReforgeDataPersistence.SyncCurrentReforgeState(item);", "ZombieMode insurance/storage handoff must sync reforge state")
        require(rewards, "ItemUtilities.SendToPlayerCharacterInventory(item, false)", "ZombieMode rewards must use Duckov inventory helper first")
        require(rewards, "item.Drop(dropPosition, true", "ZombieMode reward fallback must use Duckov item.Drop")

        require(marker, "public readonly System.Collections.Generic.List<ZombieModeAttributeModifierRecord> RuntimeModifierRecords", "ZombieMode enemies must track runtime stat modifiers")
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
