"""装备工厂只执行按原顺序登记的配置器，重复登记不得重复执行。"""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source

CONFIGS = (
    ("Integration/DragonKing/Weapons/DragonKingBossGunConfig.cs", "DragonKingBossGunConfig", "RegisterGunConfigurator"),
    ("Integration/Config/DragonSetConfig.cs", "DragonSetConfig", "RegisterConfigurator"),
    ("Integration/Config/DragonKingSetConfig.cs", "DragonKingSetConfig", "RegisterConfigurator"),
    ("Integration/Config/FrostThunderSetConfig.cs", "FrostThunderSetConfig", "RegisterConfigurator"),
    ("Integration/SkyIsland/SkyIslandBossGearConfig.cs", "SkyIslandBossGearConfig", "RegisterConfigurator"),
    ("Integration/Config/FlightTotemConfig.cs", "FlightTotemConfig", "RegisterConfigurator"),
    ("Integration/ReverseScale/ReverseScaleConfig.cs", "ReverseScaleConfig", "RegisterConfigurator"),
    ("Integration/DragonDescendant/DragonBreathWeaponConfig.cs", "DragonBreathWeaponConfig", "RegisterConfigurator"),
    ("Integration/NewWeapons/ViperDagger/ViperDaggerWeaponConfig.cs", "ViperDaggerWeaponConfig", "RegisterConfigurator"),
    ("Integration/NewWeapons/SummonStaff/SummonStaffWeaponConfig.cs", "SummonStaffWeaponConfig", "RegisterConfigurator"),
    ("Integration/NewWeapons/EnergyShield/EnergyShieldWeaponConfig.cs", "EnergyShieldWeaponConfig", "RegisterConfigurator"),
    ("Integration/NewWeapons/FrostSpear/FrostSpearWeaponConfig.cs", "FrostSpearWeaponConfig", "RegisterConfigurator"),
    ("Integration/NewWeapons/ThunderRing/ThunderRingWeaponConfig.cs", "ThunderRingWeaponConfig", "RegisterConfigurator"),
)


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def main():
    try:
        factory = source("Integration/EquipmentFactory.cs")
        bootstrap = source("Integration/EquipmentConfiguratorBootstrap.cs")
        reset = source("Integration/EquipmentFactoryStaticCacheReset.cs")
        item_registry = source("Integration/Items/ItemContentRegistry.cs")
        deferred = source("Integration/IntegrationDeferredBootstrap.cs")
        assert "ApplyRegisteredConfigurators(gunConfigurators, itemPrefab, baseName);" in factory
        assert "ApplyRegisteredConfigurators(equipmentConfigurators, itemPrefab, baseName);" in factory
        assert "if (indexes.TryGetValue(key, out index))" in factory
        assert "ordered[index] = configurator;" in factory
        assert "indexes.Add(key, ordered.Count);" in factory
        assert "EquipmentConfiguratorBootstrap.RegisterAll();" in item_registry
        assert deferred.index("FactoryResourceLoading.InitializeItems(this, InitializeDynamicItems)") < deferred.index("EquipmentFactory.LoadAllEquipmentAsync(this)")
        for name in ("gunConfigurators", "gunConfiguratorIndexes", "equipmentConfigurators", "equipmentConfiguratorIndexes"):
            assert name + ".Clear();" in reset, name + " must clear at factory shutdown"

        start = bootstrap.index("internal static void RegisterAll()")
        actual = []
        for line in bootstrap[start:].splitlines():
            line = line.strip()
            if line.endswith(".RegisterEquipmentConfigurator();"):
                actual.append(line.split(".", 1)[0])
        expected = [name for _, name, _ in CONFIGS]
        assert actual == expected, "registration order changed"
        for path, name, method in CONFIGS:
            config = source(path)
            assert "public static void RegisterEquipmentConfigurator()" in config, path
            token = 'EquipmentFactory.' + method + '("' + name + '",'
            assert token in config, path + " does not register itself"
            assert name + ".TryConfigure(" not in factory, "factory still references concrete " + name
    except (AssertionError, ValueError) as error:
        print("EquipmentFactoryConfiguratorRegistrationGuard: FAIL - " + str(error))
        return 1
    print("EquipmentFactoryConfiguratorRegistrationGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
