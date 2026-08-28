from pathlib import Path
import sys


# BossRushItemIds 是跨文件的 partial class：主表在 ConfigItemIds.cs，
# 各子系统可在自己的 Config 分片里追加（如 ConfigPetNest.cs 的 RelicEgg）。
# 这里把承载 TypeID 常量的分片全部读进来再断言，避免常量换文件就误报。
CONFIG_ITEM_ID_PARTS = [
    Path("Config/ConfigItemIds.cs"),
    Path("Config/Config.cs"),
]
INVITATION = Path("Integration/Items/ZombieTideInvitationConfig.cs")
BEACON = Path("Integration/Items/ZombieTideBeaconConfig.cs")
BEACON_USAGE = Path("Integration/Items/ZombieTideBeaconUsage.cs")
PORTABLE_SAFE_ZONE = Path("Integration/Items/PortableSafeZoneDeviceConfig.cs")
PORTABLE_SAFE_ZONE_USAGE = Path("Integration/Items/PortableSafeZoneDeviceUsage.cs")
BLACKLIST = Path("Config/LootBlacklistRegistry.cs")
INTEGRATION_PARTS = [
    Path("Integration/BossRushIntegration.cs"),
    Path("Integration/BossRushIntegration_StartAndScene.cs"),
    Path("Integration/BossRushIntegration_TravelAndSetup.cs"),
    Path("Integration/BossRushIntegration_MapObjectsAndDragonBreath.cs"),
]
ITEM_CONTENT_REGISTRY = Path("Integration/Items/ItemContentRegistry.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def read_boss_rush_integration() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in INTEGRATION_PARTS)


def main() -> int:
    config_text = "\n".join(
        path.read_text(encoding="utf-8", errors="ignore")
        for path in CONFIG_ITEM_ID_PARTS
        if path.exists()
    )
    invitation_text = INVITATION.read_text(encoding="utf-8")
    beacon_text = BEACON.read_text(encoding="utf-8")
    usage_text = BEACON_USAGE.read_text(encoding="utf-8")
    portable_safe_zone_text = PORTABLE_SAFE_ZONE.read_text(encoding="utf-8")
    portable_safe_zone_usage_text = PORTABLE_SAFE_ZONE_USAGE.read_text(encoding="utf-8")
    blacklist_text = BLACKLIST.read_text(encoding="utf-8")
    integration_text = read_boss_rush_integration()
    item_content_registry_text = ITEM_CONTENT_REGISTRY.read_text(encoding="utf-8")
    localization_text = LOCALIZATION.read_text(encoding="utf-8")

    required_config = [
        "public const int ZombieTideInvitation = 500045;",
        "public const int ZombieTideBeacon = 500046;",
        "public const int PortableSafeZoneDevice = 500058;",
    ]
    for snippet in required_config:
        if snippet not in config_text:
            return fail("ZombieModeItemIdentityGuard: missing item id -> " + snippet)

    if "BossRushItemIds.ZombieTideInvitation" not in invitation_text:
        return fail("ZombieModeItemIdentityGuard: invitation does not use BossRushItemIds")

    if "BossRushItemIds.ZombieTideBeacon" not in beacon_text:
        return fail("ZombieModeItemIdentityGuard: beacon does not use BossRushItemIds")

    if "BossRushItemIds.PortableSafeZoneDevice" not in portable_safe_zone_text:
        return fail("ZombieModeItemIdentityGuard: portable safe zone does not use BossRushItemIds")

    for snippet in [
        "ZombieTideInvitationConfig.RegisterConfigurator();",
        "ZombieTideBeaconConfig.RegisterConfigurator();",
        "PortableSafeZoneDeviceConfig.RegisterConfigurator();",
    ]:
        if snippet not in item_content_registry_text:
            return fail("ZombieModeItemIdentityGuard: missing registration snippet -> " + snippet)

    for snippet in [
        "ZombieTideInvitationConfig.InjectLocalization();",
        "ZombieTideBeaconConfig.InjectLocalization();",
        "PortableSafeZoneDeviceConfig.InjectLocalization();",
    ]:
        if snippet not in integration_text and snippet not in localization_text:
            return fail("ZombieModeItemIdentityGuard: missing registration/localization snippet -> " + snippet)

    if "ZombieTideBeaconConfig.TYPE_ID" not in blacklist_text:
        return fail("ZombieModeItemIdentityGuard: beacon is not isolated from loot blacklist")

    if "PortableSafeZoneDeviceConfig.TYPE_ID" not in blacklist_text:
        return fail("ZombieModeItemIdentityGuard: portable safe zone is not isolated from loot blacklist")

    for snippet in [
        "inst.CanUseZombieModeBeacon()",
        "inst.TryUseZombieModeBeacon()",
        "BossRush_ZombieMode_Notify_BeaconNotZombieMode",
        "inst.GetZombieModeBeaconUnavailableReasonKey()",
    ]:
        if snippet not in usage_text:
            return fail("ZombieModeItemIdentityGuard: beacon usage missing runtime hook -> " + snippet)

    for snippet in [
        "inst.CanUseZombieModePortableSafeZoneDevice()",
        "inst.TryUseZombieModePortableSafeZoneDevice()",
        "BossRush_ZombieMode_Notify_PortableSafeZoneNotZombieMode",
        "inst.GetZombieModePortableSafeZoneUnavailableReasonKey()",
    ]:
        if snippet not in portable_safe_zone_usage_text:
            return fail("ZombieModeItemIdentityGuard: portable safe-zone usage missing runtime hook -> " + snippet)

    print("ZombieModeItemIdentityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
