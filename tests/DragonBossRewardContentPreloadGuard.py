"""Guard: 龙裔/龙王旧奖励内容必须走统一按需注册与掉落兜底。"""

from pathlib import Path
import sys


START_SOURCE = Path("Integration/BossRushIntegration_StartAndScene.cs")
UNIFIED_REGISTRY_SOURCE = Path("Integration/BossRushDynamicItemRegistry.cs")
LOOT_SOURCE = Path("LootAndRewards/LootAndRewardsSpecialLoot.cs")


def fail(message: str) -> int:
    print("DragonBossRewardContentPreloadGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    start_text = START_SOURCE.read_text(encoding="utf-8")
    unified_registry_text = UNIFIED_REGISTRY_SOURCE.read_text(encoding="utf-8")
    loot_text = LOOT_SOURCE.read_text(encoding="utf-8")

    start_block = extract_block(start_text, "void Start_Integration()")
    if not start_block:
        return fail("missing Start_Integration block")

    if "EnsureDragonBossRewardContentPreloaded();" in start_block:
        return fail("Start_Integration must not synchronously preload dragon reward bundles")

    required_unified_tokens = [
        'EquipmentOnly("dragon_equipment")',
        'EquipmentOnly("dragonking_equipment")',
        'EquipmentOnly("flight_totem")',
        '"fenhuang_halberd_model"',
        '"fenhuang_halberd_item"',
        "DragonDescendantConfig.DRAGON_HELM_TYPE_ID",
        "DragonKingBossGunConfig.WeaponTypeId",
    ]
    for token in required_unified_tokens:
        if token not in unified_registry_text:
            return fail("unified registry mapping missing token: " + token)

    ensure_block = extract_block(loot_text, "private bool EnsureDragonBossRewardPrefabLoaded(int typeId, string logPrefix)")
    if not ensure_block:
        return fail("missing EnsureDragonBossRewardPrefabLoaded block")

    if "BossRushDynamicItemRegistry.EnsureRegistered(typeId);" not in ensure_block:
        return fail("drop fallback helper must call BossRushDynamicItemRegistry.EnsureRegistered(typeId)")

    descendant_block = extract_block(loot_text, "private IEnumerator AddDragonDescendantLoot(Inventory inv)")
    if "EnsureDragonBossRewardPrefabLoaded(selectedTypeId, \"[DragonDescendant]\")" not in descendant_block:
        return fail("dragon descendant drop path must ensure prefab before InstantiateSync")

    king_block = extract_block(loot_text, "private bool TryAddDragonKingLootItem(Inventory inv, int typeId, string itemName)")
    if "EnsureDragonBossRewardPrefabLoaded(typeId, \"[DragonKing]\")" not in king_block:
        return fail("dragon king drop path must ensure prefab before InstantiateSync")

    print("DragonBossRewardContentPreloadGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
