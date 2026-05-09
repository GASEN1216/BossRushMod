from pathlib import Path
import sys


CATALOG = Path("ZombieMode/ZombieModeNpcCatalog.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")


def fail(message: str) -> int:
    print("ZombieModeMerchantBulletSlotAmmoGuard: FAIL - " + message)
    return 1


def main() -> int:
    if not CATALOG.exists() or not REWARDS.exists():
        return fail("missing ZombieMode NPC catalog or reward file")

    catalog = CATALOG.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")

    if catalog.count('BasePrice = 100, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Bullet"') < 2:
        return fail("normal and boss bullet stock must both cost 100 base purification points")

    required_tokens = [
        "IsZombieModeMerchantBulletStock(entry)",
        "TryGiveZombieModeMerchantAmmoForEquippedWeapon(entry)",
        "TryGetZombieModePreferredWeaponForMerchantAmmo()",
        "PrimWeaponSlot()",
        "SecWeaponSlot()",
        "TryReadZombieModeItemCaliber(weapon)",
        "TryGiveZombieModeAmmo(caliber, 100",
    ]
    for token in required_tokens:
        if token not in rewards:
            return fail("missing token -> " + token)

    if rewards.find("PrimWeaponSlot()") > rewards.find("SecWeaponSlot()"):
        return fail("primary weapon slot must be checked before secondary weapon slot")

    print("ZombieModeMerchantBulletSlotAmmoGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
