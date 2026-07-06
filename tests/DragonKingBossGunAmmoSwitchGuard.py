"""Guard: Dragon King boss gun ammo choice must sync on selection, reload, and shoot."""

from pathlib import Path
import sys


PATCH_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs")
RUNTIME_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs")


def fail(message: str) -> int:
    print("DragonKingBossGunAmmoSwitchGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = PATCH_SOURCE.read_text(encoding="utf-8-sig")
    runtime_text = RUNTIME_SOURCE.read_text(encoding="utf-8-sig")
    required = [
        '[HarmonyPatch(typeof(ItemSetting_Gun), "SetTargetBulletType", new Type[] { typeof(int) })]',
        "TryApplyAmmoProfileFromTargetType(__instance, typeID, \"SetTargetBulletType\")",
        '[HarmonyPatch(typeof(ItemAgent_Gun), "UpdateStates")]',
        "TryApplyAmmoProfileFromLoadedBullet(__instance.GunItemSetting, \"ReloadComplete\")",
        "TryApplyAmmoProfile(__instance.Item, profile, \"ShootOneBullet\")",
        "context.fromGunItemSetting = gun != null ? gun.GunItemSetting : null;",
    ]

    for snippet in required:
        if snippet not in text:
            return fail("missing ammo-switch snippet -> " + snippet)

    runtime_required = [
        "TryResolveTargetBulletProfile(gunSetting, targetTypeId, out profile)",
        "private static bool TryResolveTargetBulletProfile(ItemSetting_Gun gunSetting, int targetTypeId, out DragonKingBossGunShotProfile profile)",
        "DragonKingBossGunProfiles.TryResolveTypeId(targetTypeId, out profile)",
        "Item bulletItem = FindTargetBulletItem(gunSetting, targetTypeId);",
        "DragonKingBossGunProfiles.TryResolve(bulletItem, out profile)",
        "TryResolveTargetBulletProfile(gunSetting, gunSetting.TargetBulletID, out profile)",
        "private static Item FindTargetBulletItem(Inventory inventory, int targetTypeId)",
        "IsResolvableBulletOfType(item, targetTypeId)",
    ]

    for snippet in runtime_required:
        if snippet not in runtime_text:
            return fail("target-type profile sync must support caliber-resolved ammo -> " + snippet)

    forbidden = [
        "hasAppliedProfile",
        "lastAppliedProfileId",
    ]
    combined_text = text + "\n" + runtime_text
    for snippet in forbidden:
        if snippet in combined_text:
            return fail("shoot path still depends on global profile state -> " + snippet)

    print("DragonKingBossGunAmmoSwitchGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
