"""Guard: F3 debug kit must grant Dragon King boss gun plus every supported ammo profile."""

from pathlib import Path
import re
import sys


UI_SOURCE = Path("DebugAndTools/F3DebugCheatMenuUi.cs")
ACTIONS_SOURCE = Path("DebugAndTools/F3DebugCheatMenuActions.cs")
PROFILES_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs")
EXPECTED_TYPE_IDS = [326, 594, 603, 612, 621, 630, 640, 648, 650, 944, 1262, 1303, 1351, 1434, 1523]


def fail(message: str) -> int:
    print("F3DragonKingBossGunDebugKitGuard: FAIL - " + message)
    return 1


def main() -> int:
    ui_text = UI_SOURCE.read_text(encoding="utf-8-sig")
    actions_text = ACTIONS_SOURCE.read_text(encoding="utf-8-sig")
    profiles_text = PROFILES_SOURCE.read_text(encoding="utf-8-sig")

    if 'L10n.T("焚天龙铳套装", "Dragon Gun Kit")' not in ui_text:
        return fail("missing F3 resources-page button label")

    if "SpawnDragonKingBossGunDebugKitFromF3" not in ui_text:
        return fail("F3 button is not wired to the Dragon King boss gun debug kit action")

    required_action_snippets = [
        "private const int DragonKingBossGunDebugAmmoStackCount = 120;",
        "ItemAssetsCollection.InstantiateSync(DragonKingBossGunConfig.WeaponTypeId)",
        "foreach (int ammoTypeId in DragonKingBossGunProfiles.SupportedTypeIds)",
        "ammo.StackCount = ResolveDragonKingBossGunDebugAmmoStackCount(ammo);",
        "ammoSuccess < DragonKingBossGunProfiles.SupportedTypeIds.Count",
    ]
    for snippet in required_action_snippets:
        if snippet not in actions_text:
            return fail("missing debug-kit action snippet -> " + snippet)

    type_ids = [int(value) for value in re.findall(r"TypeIds\s*=\s*new\[\]\s*\{\s*(\d+)\s*\}", profiles_text)]
    if sorted(type_ids) != EXPECTED_TYPE_IDS:
        return fail("Dragon King boss gun supported ammo TypeIDs changed without updating the F3 kit guard")

    print("F3DragonKingBossGunDebugKitGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
