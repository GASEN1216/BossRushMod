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
INVITATION_USAGE = Path("Integration/Items/ZombieTideInvitationUsage.cs")
EXTRACTION = Path("ZombieMode/ZombieModeRuntimeModule_Extraction.cs")
BLACKLIST = Path("Config/LootBlacklistRegistry.cs")
INTEGRATION_PARTS = [
    Path("Integration/BossRushIntegration.cs"),
    Path("Integration/BossRushIntegration_StartAndScene.cs"),
    Path("Integration/BossRushIntegration_TravelAndSetup.cs"),
]
ITEM_CONTENT_REGISTRY = Path("Integration/Items/ItemContentRegistry.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def method_body(text: str, signature: str):
    """按花括号配对取方法体；找不到签名返回 None。"""
    start = text.find(signature)
    if start < 0:
        return None
    opening = text.find("{", start)
    if opening < 0:
        return None
    depth, index = 0, opening
    while index < len(text):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[opening:index + 1]
        index += 1
    return None


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
    ]:
        if snippet not in usage_text:
            return fail("ZombieModeItemIdentityGuard: beacon usage missing runtime hook -> " + snippet)

    for snippet in [
        "inst.CanUseZombieModePortableSafeZoneDevice()",
        "inst.TryUseZombieModePortableSafeZoneDevice()",
    ]:
        if snippet not in portable_safe_zone_usage_text:
            return fail("ZombieModeItemIdentityGuard: portable safe-zone usage missing runtime hook -> " + snippet)

    # 2026-10-01：CanBeUsed 会被官方背包悬停 / 右键菜单 / 快捷栏反复调用（ItemDisplay.CanUse、
    # ItemOperationMenu.UseButtonInteractable），在里面弹通知会让鼠标划过物品就刷屏。
    # 判断函数只返回结果；不可用原因归口到模式侧 Get*UnavailableReasonKey，由 TryUse* 在真正使用被拒时提示。
    invitation_usage_text = INVITATION_USAGE.read_text(encoding="utf-8")
    for name, text in [
        ("beacon", usage_text),
        ("portable safe-zone", portable_safe_zone_usage_text),
        ("invitation", invitation_usage_text),
    ]:
        body = method_body(text, "public override bool CanBeUsed(")
        if body is None:
            return fail("ZombieModeItemIdentityGuard: " + name + " usage missing CanBeUsed")
        if "NotificationText.Push" in body or "PopText(" in body:
            return fail("ZombieModeItemIdentityGuard: " + name + " CanBeUsed must not show notifications (hover spam)")

    extraction_text = EXTRACTION.read_text(encoding="utf-8")
    for signature, required in [
        ("internal string GetZombieModeBeaconUnavailableReasonKey()", "BossRush_ZombieMode_Notify_BeaconNotZombieMode"),
        ("internal string GetZombieModePortableSafeZoneUnavailableReasonKey()", "BossRush_ZombieMode_Notify_PortableSafeZoneNotZombieMode"),
        ("internal bool TryUseZombieModeBeacon()", "NotificationText.Push(L10n.T(GetZombieModeBeaconUnavailableReasonKey()));"),
        ("internal bool TryUseZombieModePortableSafeZoneDevice()", "NotificationText.Push(L10n.T(GetZombieModePortableSafeZoneUnavailableReasonKey()));"),
    ]:
        body = method_body(extraction_text, signature)
        if body is None or required not in body:
            return fail("ZombieModeItemIdentityGuard: unavailable reason must stay in mode-side use path -> "
                        + signature + " / " + required)

    print("ZombieModeItemIdentityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
