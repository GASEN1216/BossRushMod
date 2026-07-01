"""Frost/Thunder set bonuses must have scoped triggers and paired cleanup."""

from pathlib import Path
import sys


FROST = Path("Integration/Bonus/FrostSetBonus.cs")
THUNDER = Path("Integration/Bonus/ThunderSetBonus.cs")
MANAGER = Path("Integration/Bonus/SetBonusManager.cs")
PLACEHOLDER = Path("Integration/Bonus/SetBonusPlaceholderRegistry.cs")
FACTORY = Path("Integration/EquipmentFactory.cs")
CONFIG = Path("Integration/Config/FrostThunderSetConfig.cs")


def fail(message: str) -> int:
    print("SetBonusLifecycleGuard: FAIL - " + message)
    return 1


def main() -> int:
    frost = FROST.read_text(encoding="utf-8")
    thunder = THUNDER.read_text(encoding="utf-8")
    manager = MANAGER.read_text(encoding="utf-8")
    placeholder = PLACEHOLDER.read_text(encoding="utf-8")
    factory = FACTORY.read_text(encoding="utf-8")
    config = CONFIG.read_text(encoding="utf-8")

    for snippet in (
        "private const float FROST_SET_CLOSE_RANGE",
        "private Stat frostSetIceResistStat = null;",
        "frostSetIceResistStat = iceFactorStat;",
        "frostSetIceResistStat.RemoveModifier(frostSetIceResistModifier);",
        "if (damageInfo.fromCharacter == null) return;",
        "delta.sqrMagnitude > FROST_SET_CLOSE_RANGE * FROST_SET_CLOSE_RANGE",
        "private sealed class FrostFallbackSlowState",
        "RemoveFrostFallbackSlowModifiers(state);",
        "state.WalkSpeedStat.RemoveModifier(state.WalkSpeedModifier);",
        "state.RunSpeedStat.RemoveModifier(state.RunSpeedModifier);",
    ):
        if snippet not in frost:
            return fail("frost bonus missing snippet -> " + snippet)

    for snippet in (
        "private Stat thunderSetElecResistStat = null;",
        "thunderSetElecResistStat = elecFactorStat;",
        "thunderSetElecResistStat.RemoveModifier(thunderSetElecResistModifier);",
        "if (damageInfo.fromCharacter == null) return;",
        "object.ReferenceEquals(damageInfo.fromCharacter, player)",
    ):
        if snippet not in thunder:
            return fail("thunder bonus missing snippet -> " + snippet)

    for snippet in (
        "private bool setBonusLevelEventRegistered = false;",
        "if (setBonusEventRegistered && setBonusLevelEventRegistered) return;",
        "LevelManager.OnAfterLevelInitialized += OnLevelInitializedCheckSetBonus;",
        "LevelManager.OnAfterLevelInitialized -= OnLevelInitializedCheckSetBonus;",
        "DeactivateFrostSetBonus();",
        "DeactivateThunderSetBonus();",
    ):
        if snippet not in manager:
            return fail("set bonus manager missing snippet -> " + snippet)

    if "if (!setBonusEventRegistered) return;" in manager:
        return fail("unregister still returns before level-event cleanup")

    for snippet in (
        "public static bool TryBindLoadedEquipmentModel(Item itemPrefab, string modelBaseName)",
        'SetAgentUtilityPrefab(itemPrefab, "EquipmentModel", modelAgent);',
        "InjectItemGraphicForEquipment(itemPrefab, modelAgent, true);",
    ):
        if snippet not in factory:
            return fail("equipment factory missing resource-model binding snippet -> " + snippet)

    for snippet in (
        'private const string FROST_HELMET_BASE = "FrostCrown_Helmet";',
        'private const string FROST_ARMOR_BASE = "IceArmor_Armor";',
        'private const string THUNDER_HELMET_BASE = "ThunderHorn_Helmet";',
        'private const string THUNDER_ARMOR_BASE = "ThunderArmor_Armor";',
        "public static bool TryConfigure(Item item, string baseName)",
        "public static bool TryConfigureByTypeId(Item item)",
        "EquipmentFactory.TryBindLoadedEquipmentModel(item, modelBaseName);",
        "EquipmentHelperIcon.TryInjectIcon(item, bundleName, iconAssetName);",
    ):
        if snippet not in config:
            return fail("frost/thunder config missing snippet -> " + snippet)

    for snippet in (
        "FrostThunderSetConfig.TryConfigureByTypeId(existing);",
        "FrostThunderSetConfig.TryConfigureByTypeId(clone);",
        "Item clone = UnityEngine.Object.Instantiate(source);",
    ):
        if snippet not in placeholder:
            return fail("set bonus placeholder missing fallback-delegation snippet -> " + snippet)

    print("SetBonusLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
