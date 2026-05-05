from pathlib import Path
import sys


CATALOG = Path("ZombieMode/ZombieModeNpcCatalog.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print("ZombieModeMerchantDisplayNameGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    catalog = CATALOG.read_text(encoding="utf-8")
    localization = LOCALIZATION.read_text(encoding="utf-8")

    for snippet in [
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Gun"',
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Melee"',
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Accessory"',
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Bullet"',
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Helmat"',
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Armor"',
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Backpack"',
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Totem"',
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Mask"',
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Medical"',
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Food"',
        'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Bait"',
    ]:
        result = require(catalog, snippet, "Zombie merchant display key")
        if result:
            return result

    for snippet in [
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Gun"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Melee"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Accessory"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Bullet"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Helmat"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Armor"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Backpack"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Totem"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Mask"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Medical"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Food"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Bait"',
    ]:
        result = require(localization, snippet, "Zombie merchant localization")
        if result:
            return result

    forbidden = [
        'DisplayKey = "BossRush_ModeE_Shop_',
    ]
    for token in forbidden:
        if token in catalog:
            return fail("Zombie merchant stock must not directly expose ModeE shop keys -> " + token)

    print("ZombieModeMerchantDisplayNameGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
