"""Enemy drops: solo inventory first, co-op host creates shared native ground items."""

from pathlib import Path
import sys
import re
from cs_source_util import clean_source


DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")


def fail(message: str) -> int:
    print("ZombieModeEnemyDropInventoryGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = DROPS.read_text(encoding="utf-8")

    if "private GameObject TryDropZombieModeItemNearPosition" not in text:
        return fail("TryDropZombieModeItemNearPosition not found")

    required_tokens = [
        "float projectedWeight = player.CharacterItem.TotalWeight;",
        "player.carryAction != null && player.carryAction.Running",
        "projectedWeight += player.carryAction.GetWeight();",
        "itemWeight = item.TotalWeight;",
        "projectedWeight += itemWeight;",
        "projectedOverweight = projectedWeight > maxWeight;",
        "DropZombieModeItemAtPlayerFeet(",
        "player.CharacterItem.Inventory.AddAndMerge(item, 0)",
        "RegisterZombieModeDropCandidate(runId, obj, highValue, bossDrop);",
    ]
    for token in required_tokens:
        if token not in text:
            return fail("missing required drop contract token -> " + token)

    forbidden_tokens = [
        "item.Drop(position + Vector3.up * 0.35f, true, UnityEngine.Random.insideUnitSphere.normalized, bossDrop ? 30f : 18f);",
    ]
    for token in forbidden_tokens:
        if token in text:
            return fail("enemy drop still falls back to corpse-position direct drop -> " + token)

    overweight_gate = text.index("if (projectedOverweight)")
    add_and_merge = text.index("player.CharacterItem.Inventory.AddAndMerge(item, 0)")
    if overweight_gate > add_and_merge:
        return fail("projected weight gate must run before AddAndMerge")
    for snapshot_token in [
        "GameObject itemObject = item.gameObject;",
        "string itemName =",
        "int itemQuality = 1;",
        "itemWeight = item.TotalWeight;",
    ]:
        if text.index(snapshot_token) > add_and_merge:
            return fail("drop display/weight snapshot must precede AddAndMerge -> " + snapshot_token)

    enemy_drop_start = text.index("internal void TrySpawnZombieModeEnemyDrop")
    enemy_drop_end = text.index("private float GetZombieModeEnemyDropChance", enemy_drop_start)
    enemy_drop_body = text[enemy_drop_start:enemy_drop_end]
    if "FindRandomItemTypeByTags(null" in enemy_drop_body:
        return fail("enemy direct drops must not fall back to an untagged pool")

    delivery_start = text.index("private GameObject TryDropZombieModeItemNearPosition")
    delivery_end = text.index("private void DropZombieModeItemAtPlayerFeet", delivery_start)
    delivery_body = text[delivery_start:delivery_end]
    clean_delivery = re.sub(r"\s+", " ", clean_source(delivery_body))
    if "if (!BossRushCoop.HasWorldAuthority || !IsZombieModeRunValid(runId) || typeId <= 0)" not in clean_delivery:
        return fail("only world authority may create enemy item drops")
    if "CharacterMainControl player = BossRushCoop.IsConnected ? null : CharacterMainControl.Main;" not in clean_delivery:
        return fail("co-op enemy drops must use the native shared ground path instead of the host inventory")
    if delivery_body.count("DropZombieModeItemAtPlayerFeet(") < 2:
        return fail("overweight and all other player-valid ground paths must share the feet-drop helper")
    if "item.Drop(player, true);" in delivery_body:
        return fail("player-valid ground delivery must not inline item.Drop outside the shared helper")

    refill_start = text.index("private int RefillZombieModeLootboxInventory")
    refill_end = text.index("private void LockZombieModeContainerUntilStarterChoice", refill_start)
    if "FindRandomItemTypeByTags(null, minQuality, maxQuality)" not in text[refill_start:refill_end]:
        return fail("lootbox refill fallback must remain unchanged")

    print("ZombieModeEnemyDropInventoryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
