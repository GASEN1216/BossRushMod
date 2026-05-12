from pathlib import Path
import sys


CONFIG = Path("Config/Config.cs")
INVITATION = Path("Integration/Items/ZombieTideInvitationConfig.cs")
BEACON = Path("Integration/Items/ZombieTideBeaconConfig.cs")
BEACON_USAGE = Path("Integration/Items/ZombieTideBeaconUsage.cs")
BLACKLIST = Path("Config/LootBlacklistRegistry.cs")
INTEGRATION = Path("Integration/BossRushIntegration.cs")
ITEM_CONTENT_REGISTRY = Path("Integration/Items/ItemContentRegistry.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    config_text = CONFIG.read_text(encoding="utf-8")
    invitation_text = INVITATION.read_text(encoding="utf-8")
    beacon_text = BEACON.read_text(encoding="utf-8")
    usage_text = BEACON_USAGE.read_text(encoding="utf-8")
    blacklist_text = BLACKLIST.read_text(encoding="utf-8")
    integration_text = INTEGRATION.read_text(encoding="utf-8")
    item_content_registry_text = ITEM_CONTENT_REGISTRY.read_text(encoding="utf-8")
    localization_text = LOCALIZATION.read_text(encoding="utf-8")

    required_config = [
        "public const int ZombieTideInvitation = 500045;",
        "public const int ZombieTideBeacon = 500046;",
    ]
    for snippet in required_config:
        if snippet not in config_text:
            return fail("ZombieModeItemIdentityGuard: missing item id -> " + snippet)

    if "BossRushItemIds.ZombieTideInvitation" not in invitation_text:
        return fail("ZombieModeItemIdentityGuard: invitation does not use BossRushItemIds")

    if "BossRushItemIds.ZombieTideBeacon" not in beacon_text:
        return fail("ZombieModeItemIdentityGuard: beacon does not use BossRushItemIds")

    for snippet in [
        "ZombieTideInvitationConfig.RegisterConfigurator();",
        "ZombieTideBeaconConfig.RegisterConfigurator();",
    ]:
        if snippet not in item_content_registry_text:
            return fail("ZombieModeItemIdentityGuard: missing registration snippet -> " + snippet)

    for snippet in [
        "ZombieTideInvitationConfig.InjectLocalization();",
        "ZombieTideBeaconConfig.InjectLocalization();",
    ]:
        if snippet not in integration_text and snippet not in localization_text:
            return fail("ZombieModeItemIdentityGuard: missing registration/localization snippet -> " + snippet)

    if "ZombieTideBeaconConfig.TYPE_ID" not in blacklist_text:
        return fail("ZombieModeItemIdentityGuard: beacon is not isolated from loot blacklist")

    for snippet in [
        "inst.CanUseZombieModeBeacon()",
        "inst.TryUseZombieModeBeacon()",
        "BossRush_ZombieMode_Notify_BeaconNotZombieMode",
        "inst.GetZombieModeBeaconUnavailableReasonKey()",
    ]:
        if snippet not in usage_text:
            return fail("ZombieModeItemIdentityGuard: beacon usage missing runtime hook -> " + snippet)

    print("ZombieModeItemIdentityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
