"""Guard: BossRush 自定义物品必须通过统一按需注册表兜底。"""

from pathlib import Path
import sys


COMPILE = Path("compile_official.bat")
REGISTRY = Path("Integration/BossRushDynamicItemRegistry.cs")
PATCH = Path("Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs")
EQUIPMENT_REGISTRY = Path("Integration/EquipmentContentRegistry.cs")
START = Path("Integration/BossRushIntegration_StartAndScene.cs")
WIKI_BOOK = Path("Integration/WikiBookItem.cs")
LOOT = Path("LootAndRewards/LootAndRewardsSpecialLoot.cs")
PHANTOM = Path("Integration/PhantomWitch/PhantomWitchScytheBootstrap.cs")
NEW_WEAPON_PLACEHOLDER = Path("Integration/NewWeapons/Common/NewWeaponPlaceholderRegistry.cs")
SET_BONUS_PLACEHOLDER = Path("Integration/Bonus/SetBonusPlaceholderRegistry.cs")


def fail(message: str) -> int:
    print("BossRushDynamicItemRegistryGuard: FAIL - " + message)
    return 1


def normalize_slashes(text: str) -> str:
    return text.replace("\\", "/")


def main() -> int:
    compile_text = normalize_slashes(COMPILE.read_text(encoding="utf-8", errors="ignore"))
    registry = REGISTRY.read_text(encoding="utf-8", errors="ignore")
    patch = PATCH.read_text(encoding="utf-8", errors="ignore")
    equipment = EQUIPMENT_REGISTRY.read_text(encoding="utf-8", errors="ignore")
    start = START.read_text(encoding="utf-8", errors="ignore")
    wiki_book = WIKI_BOOK.read_text(encoding="utf-8", errors="ignore")
    loot = LOOT.read_text(encoding="utf-8", errors="ignore")
    phantom = PHANTOM.read_text(encoding="utf-8", errors="ignore")
    new_weapon = NEW_WEAPON_PLACEHOLDER.read_text(encoding="utf-8", errors="ignore")
    set_bonus = SET_BONUS_PLACEHOLDER.read_text(encoding="utf-8", errors="ignore")

    for source in (
        "Integration/BossRushDynamicItemRegistry.cs",
        "Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs",
    ):
        if source not in compile_text:
            return fail("compile_official.bat missing source: " + source)

    for token in (
        '"GetMetaData"',
        '"GetPrefab"',
        '"InstantiateSync"',
        '"InstantiateAsync"',
        '"InstantiateAsync_Local"',
        "typeof(ItemTreeData)",
        "data.entries",
    ):
        if token not in patch:
            return fail("patch missing token: " + token)

    required_registry_tokens = (
        "EnsureBossRushTicket",
        "EnsureBirthdayCake",
        "EnsureAdventureJournal",
        "DragonDescendantConfig.DRAGON_HELM_TYPE_ID",
        "DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID",
        "DragonDescendantConfig.DRAGON_BREATH_TYPE_ID",
        "DragonKingConfig.DRAGON_KING_LOOT_TYPE_ID",
        "DragonKingConfig.DRAGON_KING_HELM_TYPE_ID",
        "DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID",
        "DragonKingConfig.REVERSE_SCALE_TYPE_ID",
        "DragonKingConfig.FEN_HUANG_HALBERD_TYPE_ID",
        "DragonKingBossGunConfig.WeaponTypeId",
        "FrostmourneIds.WeaponTypeId",
        "PhantomWitchScytheIds.WeaponTypeId",
        'EquipmentOnly("dragon_equipment")',
        'EquipmentOnly("flight_totem")',
        'EquipmentOnly("dragonking_equipment")',
        '"fenhuang_halberd_model"',
        '"fenhuang_halberd_item"',
        '"frostmourne_model"',
        '"frostmourne_item"',
        'EquipmentOnly("phantom_scythe")',
        "AwenCourierTokenConfig.BUNDLE_NAME",
        "AwenCourierTokenConfig.TYPE_ID",
        "ColdQuenchFluidConfig.BUNDLE_NAME",
        "ColdQuenchFluidConfig.TYPE_ID",
        "BrickStoneConfig.BUNDLE_NAME",
        "BrickStoneConfig.TYPE_ID",
        "DingdangDrawingConfig.BUNDLE_NAME",
        "DingdangDrawingConfig.TYPE_ID",
        "DiamondConfig.BUNDLE_NAME",
        "DiamondConfig.TYPE_ID",
        "AchievementMedalConfig.BUNDLE_NAME",
        "AchievementMedalConfig.TYPE_ID",
        "WildHornConfig.BUNDLE_NAME",
        "WildHornConfig.TYPE_ID",
        "FactionFlagConfig.ALL_FLAG_TYPE_IDS",
        "RespawnItemConfig.ALL_RESPAWN_ITEM_TYPE_IDS",
        "DiamondRingConfig.BUNDLE_NAME",
        "DiamondRingConfig.TYPE_ID",
        "CalmingDropsConfig.BUNDLE_NAME",
        "CalmingDropsConfig.TYPE_ID",
        "PeaceCharmConfig.BUNDLE_NAME",
        "PeaceCharmConfig.TYPE_ID",
        "BloodhuntTransponderConfig.BUNDLE_NAME",
        "BloodhuntTransponderConfig.TYPE_ID",
        "FoldableCoverPackConfig.BUNDLE_NAME",
        "FoldableCoverPackConfig.TYPE_ID",
        "ReinforcedRoadblockPackConfig.BUNDLE_NAME",
        "ReinforcedRoadblockPackConfig.TYPE_ID",
        "BarbedWirePackConfig.BUNDLE_NAME",
        "BarbedWirePackConfig.TYPE_ID",
        "EmergencyRepairSprayConfig.BUNDLE_NAME",
        "EmergencyRepairSprayConfig.TYPE_ID",
        "AwenLootSweepTokenConfig.BUNDLE_NAME",
        "ZombieTideInvitationConfig.BUNDLE_NAME",
        "ZombieTideBeaconConfig.BUNDLE_NAME",
        '"viperdagger_melee_model"',
        '"viperdagger_item"',
        '"summonstaff_melee_model"',
        '"summonstaff_item"',
        '"energyshield_totem_model"',
        '"energyshield_item"',
        '"frostspear_melee_model"',
        '"frostspear_item"',
        '"thunderring_item"',
        '"frost_set"',
        '"thunder_set"',
        "NewWeaponPlaceholderRegistry.EnsureRegistered(typeId)",
        "SetBonusPlaceholderRegistry.EnsureRegistered(typeId)",
    )
    for token in required_registry_tokens:
        if token not in registry:
            return fail("registry missing mapping token: " + token)

    if "EnsureDragonBossRewardContentPreloaded" in start + equipment:
        return fail("dragon reward content must not be synchronously preloaded at Start")

    if "private const int WIKI_BOOK_TYPE_ID = BossRushItemIds.AdventureJournal;" not in wiki_book:
        return fail("WikiBookItem must use the published AdventureJournal TypeID")
    if "itemPrefab.SetTypeID(WIKI_BOOK_TYPE_ID);" not in wiki_book:
        return fail("WikiBookItem must correct legacy prefab TypeID to AdventureJournal")

    if "BossRushDynamicItemRegistry.EnsureRegistered(typeId);" not in loot:
        return fail("dragon reward drop fallback must delegate to unified registry")

    if "BossRushDynamicItemRegistry.EnsureRegistered(PhantomWitchScytheIds.WeaponTypeId)" not in phantom:
        return fail("phantom scythe drop fallback must delegate to unified registry")

    if "public static bool EnsureRegistered(int typeId)" not in new_weapon:
        return fail("new weapon placeholder registry missing single-TypeID entry")

    if "public static bool EnsureRegistered(int typeId)" not in set_bonus:
        return fail("set bonus placeholder registry missing single-TypeID entry")

    print("BossRushDynamicItemRegistryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
