"""ContentRegistryGuard: item/equipment content bootstrap must stay centralized."""

from pathlib import Path
import re
import sys


COMPILE = Path("compile_official.bat")
INTEGRATION = Path("Integration/BossRushIntegration.cs")
ITEM_REGISTRY = Path("Integration/Items/ItemContentRegistry.cs")
EQUIPMENT_REGISTRY = Path("Integration/EquipmentContentRegistry.cs")

ITEM_COMPILE_SOURCES = [
    "Integration/Items/ItemContentRegistry.cs",
    "Integration/EquipmentContentRegistry.cs",
]

ITEM_REGISTRATION_CALLS = [
    "AwenCourierTokenConfig.RegisterConfigurator();",
    "ColdQuenchFluidConfig.RegisterConfigurator();",
    "BrickStoneConfig.RegisterConfigurator();",
    "DiamondConfig.RegisterConfigurator();",
    "DiamondRingConfig.RegisterConfigurator();",
    "CalmingDropsConfig.RegisterConfigurator();",
    "PeaceCharmConfig.RegisterConfigurator();",
    "DingdangDrawingConfig.RegisterConfigurator();",
    "AchievementMedalConfig.RegisterConfigurator();",
    "ItemFactory.RegisterConfigurator(ReverseScaleConfig.TotemTypeId, ReverseScaleConfig.ConfigureItem);",
    "WildHornConfig.RegisterConfigurator();",
    "AwenLootSweepTokenConfig.RegisterConfigurator();",
    "FactionFlagConfig.RegisterConfigurators();",
    "RespawnItemConfig.RegisterConfigurators();",
    "BloodhuntTransponderConfig.RegisterConfigurator();",
    "FoldableCoverPackConfig.RegisterConfigurator();",
    "ReinforcedRoadblockPackConfig.RegisterConfigurator();",
    "BarbedWirePackConfig.RegisterConfigurator();",
    "EmergencyRepairSprayConfig.RegisterConfigurator();",
    "ZombieTideInvitationConfig.RegisterConfigurator();",
    "ZombieTideBeaconConfig.RegisterConfigurator();",
    "ItemFactory.RegisterConfigurator(ADVENTURE_JOURNAL_TYPE_ID, OnAdventureJournalLoaded);",
    "ItemFactory.RegisterConfigurator(FenHuangHalberdIds.WeaponTypeId, OnFenHuangHalberdLoaded);",
    "ItemFactory.RegisterConfigurator(FrostmourneIds.WeaponTypeId, OnFrostmourneLoaded);",
    "ItemFactory.RegisterConfigurator(PhantomWitchConfig.ReservedScytheTypeId, OnPhantomWitchScytheLoaded);",
]

INLINE_ITEM_TOKENS = [
    "ColdQuenchFluidConfig.RegisterConfigurator();",
    "RespawnItemConfig.RegisterConfigurators();",
    "BloodhuntTransponderConfig.RegisterConfigurator();",
    "ItemFactory.RegisterConfigurator(FrostmourneIds.WeaponTypeId, OnFrostmourneLoaded);",
]

EQUIPMENT_METHODS = [
    "LoadEquipmentContent",
    "InitializeEarlyEquipmentAbilitySystems",
    "InitializeLateEquipmentAbilitySystems",
    "CleanupEquipmentAbilitySystems",
]

EQUIPMENT_TOKENS = [
    "int equipCount = EquipmentFactory.LoadAllEquipment();",
    'DevLog("[BossRush] 自动加载装备完成，共 " + equipCount + " 个");',
    "DragonKingBossGunRuntime.InitializeRuntime();",
    "DragonKingBossGunRuntime.WarmupProjectileCache();",
    "Item fenHuangHalberd = ItemFactory.GetLoadedItem(FenHuangHalberdIds.WeaponTypeId);",
    'FenHuangHalberdWeaponConfig.TryConfigure(fenHuangHalberd, "FenHuangHalberd");',
    'DevLog("[BossRush] 绑定焚皇断界戟模型失败: " + e.Message);',
    "Item frostmourne = ItemFactory.GetLoadedItem(FrostmourneIds.WeaponTypeId);",
    'FrostmourneWeaponConfig.TryConfigure(frostmourne, "Frostmourne");',
    'DevLog("[BossRush] 绑定霜之哀伤模型失败: " + e.Message);',
    "InitializeFlightTotemSystem();",
    "InitializeReverseScaleSystem();",
    "InitializeFenHuangHalberdSystem();",
    "InitializeFrostmourneSystem();",
    "InitializePhantomWitchScytheSystem();",
    "CleanupReverseScaleSystem();",
    "CleanupFenHuangHalberdSystem();",
    "CleanupFrostmourneSystem();",
    "CleanupPhantomWitchScytheSystem();",
    "UnsubscribeDragonBreathEffectEvent();",
    "DragonBreathBuffHandler.Cleanup();",
    "CleanupFlightTotemSystem();",
]

INLINE_EQUIPMENT_TOKENS = [
    "int equipCount = EquipmentFactory.LoadAllEquipment();",
    "DragonKingBossGunRuntime.InitializeRuntime();",
    "InitializeReverseScaleSystem();",
    "CleanupReverseScaleSystem();",
    "DragonBreathBuffHandler.Cleanup();",
]


def fail(message: str) -> int:
    print(message)
    return 1


def normalize_slashes(text: str) -> str:
    return re.sub(r"/+", "/", text.replace("\\", "/"))


def require_ordered_tokens(text: str, tokens: list[str], label: str) -> str | None:
    position = -1
    for token in tokens:
        next_position = text.find(token, position + 1)
        if next_position < 0:
            return label + " missing token: " + token
        if next_position < position:
            return label + " token out of order: " + token
        position = next_position
    return None


def main() -> int:
    compile_text = normalize_slashes(COMPILE.read_text(encoding="utf-8", errors="ignore"))
    integration_text = INTEGRATION.read_text(encoding="utf-8", errors="ignore")

    missing_compile = [path for path in ITEM_COMPILE_SOURCES if path not in compile_text]
    if missing_compile:
        return fail("ContentRegistryGuard: missing compile source(s): " + ", ".join(missing_compile))

    if not ITEM_REGISTRY.exists():
        return fail("ContentRegistryGuard: missing item registry file: " + str(ITEM_REGISTRY))
    if not EQUIPMENT_REGISTRY.exists():
        return fail("ContentRegistryGuard: missing equipment registry file: " + str(EQUIPMENT_REGISTRY))

    item_text = ITEM_REGISTRY.read_text(encoding="utf-8", errors="ignore")
    equipment_text = EQUIPMENT_REGISTRY.read_text(encoding="utf-8", errors="ignore")

    if "private void RegisterItemContentConfigurators()" not in item_text:
        return fail("ContentRegistryGuard: item registry missing RegisterItemContentConfigurators")

    item_order_error = require_ordered_tokens(item_text, ITEM_REGISTRATION_CALLS, "ContentRegistryGuard: item registry")
    if item_order_error:
        return fail(item_order_error)

    if "RegisterItemContentConfigurators();" not in integration_text:
        return fail("ContentRegistryGuard: integration missing RegisterItemContentConfigurators call")

    inline_item_tokens = [token for token in INLINE_ITEM_TOKENS if token in integration_text]
    if inline_item_tokens:
        return fail("ContentRegistryGuard: integration still contains inline item registration token(s): " + " | ".join(inline_item_tokens))

    missing_methods = [
        method for method in EQUIPMENT_METHODS
        if ("private void " + method + "()") not in equipment_text
    ]
    if missing_methods:
        return fail("ContentRegistryGuard: equipment registry missing method(s): " + ", ".join(missing_methods))

    equipment_order_error = require_ordered_tokens(equipment_text, EQUIPMENT_TOKENS, "ContentRegistryGuard: equipment registry")
    if equipment_order_error:
        return fail(equipment_order_error)

    missing_equipment_calls = [
        call for call in [
            "LoadEquipmentContent();",
            "InitializeEarlyEquipmentAbilitySystems();",
            "InitializeLateEquipmentAbilitySystems();",
            "CleanupEquipmentAbilitySystems();",
        ]
        if call not in integration_text
    ]
    if missing_equipment_calls:
        return fail("ContentRegistryGuard: integration missing equipment wrapper call(s): " + ", ".join(missing_equipment_calls))

    inline_equipment_tokens = [token for token in INLINE_EQUIPMENT_TOKENS if token in integration_text]
    if inline_equipment_tokens:
        return fail("ContentRegistryGuard: integration still contains inline equipment token(s): " + " | ".join(inline_equipment_tokens))

    print("ContentRegistryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
