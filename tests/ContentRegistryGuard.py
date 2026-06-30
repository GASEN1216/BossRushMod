"""ContentRegistryGuard: item/equipment content bootstrap must stay centralized."""

from pathlib import Path
import re
import sys


COMPILE = Path("compile_official.bat")
INTEGRATION_PARTS = [
    Path("Integration/BossRushIntegration.cs"),
    Path("Integration/BossRushIntegration_StartAndScene.cs"),
    Path("Integration/BossRushIntegration_TravelAndSetup.cs"),
    Path("Integration/BossRushIntegration_MapObjectsAndDragonBreath.cs"),
    Path("Integration/IntegrationDeferredBootstrap.cs"),
]
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


def read_boss_rush_integration() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in INTEGRATION_PARTS)


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


def require_exactly_once(text: str, token: str, label: str) -> str | None:
    count = text.count(token)
    if count != 1:
        return label + " token occurrence count for '" + token + "' must be 1, got " + str(count)
    return None


def main() -> int:
    compile_text = normalize_slashes(COMPILE.read_text(encoding="utf-8", errors="ignore"))
    integration_text = read_boss_rush_integration()

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

    for token in ITEM_REGISTRATION_CALLS:
        occurrence_error = require_exactly_once(item_text, token, "ContentRegistryGuard: item registry")
        if occurrence_error:
            return fail(occurrence_error)

    integration_item_order_error = require_ordered_tokens(
        integration_text,
        [
            "RegisterItemContentConfigurators();",
            "int itemCount = ItemFactory.LoadAllItems();",
            "PeaceCharmRuntime.InitializeRuntime();",
        ],
        "ContentRegistryGuard: integration item bootstrap")
    if integration_item_order_error:
        return fail(integration_item_order_error)

    for token in [
        "RegisterItemContentConfigurators();",
        "int itemCount = ItemFactory.LoadAllItems();",
    ]:
        occurrence_error = require_exactly_once(integration_text, token, "ContentRegistryGuard: integration item bootstrap")
        if occurrence_error:
            return fail(occurrence_error)

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

    for token in EQUIPMENT_TOKENS:
        occurrence_error = require_exactly_once(equipment_text, token, "ContentRegistryGuard: equipment registry")
        if occurrence_error:
            return fail(occurrence_error)

    # 装备内容/能力系统初始化已从 Start_Integration 同步路径下沉到
    # IntegrationDeferredBootstrap.cs 的跨帧协程（性能优化：避免过图帧同步重负载）。
    # 这里改为校验“延迟引导协程”内的有序性，并确认 Start_Integration 不再做同步重初始化。
    deferred_text = Path("Integration/IntegrationDeferredBootstrap.cs").read_text(
        encoding="utf-8", errors="ignore")
    start_text = Path("Integration/BossRushIntegration_StartAndScene.cs").read_text(
        encoding="utf-8", errors="ignore")

    integration_equipment_start_order_error = require_ordered_tokens(
        deferred_text,
        [
            "() => LoadEquipmentContent()",
            "() => InitializeEarlyEquipmentAbilitySystems()",
            "() => InitializeLateEquipmentAbilitySystems()",
        ],
        "ContentRegistryGuard: deferred equipment bootstrap")
    if integration_equipment_start_order_error:
        return fail(integration_equipment_start_order_error)

    start_register_order_error = require_ordered_tokens(
        start_text,
        [
            "SceneManager.sceneLoaded += OnSceneLoaded;",
            "RegisterDragonSetEvents();",
            "EnsureIntegrationContentBootstrapScheduled(",
            "StartCoroutine(FindInteractionTargets(5));",
        ],
        "ContentRegistryGuard: integration start registration")
    if start_register_order_error:
        return fail(start_register_order_error)

    integration_equipment_cleanup_order_error = require_ordered_tokens(
        integration_text,
        [
            "UnregisterDragonSetEvents();",
            "CleanupEquipmentAbilitySystems();",
            "PeaceCharmRuntime.ShutdownRuntime();",
        ],
        "ContentRegistryGuard: integration equipment cleanup")
    if integration_equipment_cleanup_order_error:
        return fail(integration_equipment_cleanup_order_error)

    for token in [
        "() => LoadEquipmentContent()",
        "() => InitializeEarlyEquipmentAbilitySystems()",
        "() => InitializeLateEquipmentAbilitySystems()",
        "CleanupEquipmentAbilitySystems();",
    ]:
        occurrence_error = require_exactly_once(integration_text, token, "ContentRegistryGuard: integration equipment wrapper")
        if occurrence_error:
            return fail(occurrence_error)

    inline_equipment_tokens = [token for token in INLINE_EQUIPMENT_TOKENS if token in integration_text]
    if inline_equipment_tokens:
        return fail("ContentRegistryGuard: integration still contains inline equipment token(s): " + " | ".join(inline_equipment_tokens))

    print("ContentRegistryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
