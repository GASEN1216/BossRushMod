using System;
using BossRush;
using ItemStatsSystem;
using SodaCraft.Localizations;
using UnityEngine;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string reason)
    {
        assertions++;
        if (!value) throw new Exception(reason);
    }

    private static void Main()
    {
        var module = new IntegrationRuntimeModule();
        var config = new PhantomWitchScytheConfig();
        var existing = new Item { TypeID = 500044, DisplayNameRaw = PhantomWitchScytheIds.WeaponPrefabName };
        LocalizationManager.CurrentLanguage = SystemLanguage.ChineseSimplified;
        module.InjectLocalization_Extra_Integration();
        Check(existing.DisplayName == config.DisplayNameCN && existing.Description == config.DescriptionCN,
            "startup must name existing and not-yet-held scythes without item configuration");
        Check(existing.ConfigureCalls == 0, "text injection must not load or configure equipment");
        foreach (var key in new[] { "Item_500044", "phantom_witch_scythe", "PhantomScythe_Melee_Item", "phantomscythe_melee_item", "phantomscyth_melee_item", config.DisplayNameCN, config.DisplayNameEN })
        {
            Check(LocalizationManager.GetPlainText(key) == config.DisplayNameCN, "Chinese name missing for " + key);
            Check(LocalizationManager.GetPlainText(key + "_Desc") == config.DescriptionCN, "Chinese description missing for " + key);
        }
        LocalizationManager.CurrentLanguage = SystemLanguage.English;
        module.InjectLocalization_Extra_Integration();
        Check(existing.DisplayName == config.DisplayNameEN && existing.Description == config.DescriptionEN,
            "language reinjection must update the same existing item");
        Check(LocalizationManager.GetPlainText("phantomscyth_melee_item") == config.DisplayNameEN,
            "reported spelling must also switch to English");

        EquipmentConfiguratorBootstrap.RegisterAll();
        EquipmentConfiguratorBootstrap.RegisterAll();
        LocalizationManager.Text.Clear();
        EquipmentFactory.Apply(existing, "PhantomScythe_Melee");
        Check(existing.ConfigureCalls == 1, "equipment bundle must configure a scythe exactly once after repeated registration");
        Check(existing.DisplayName == config.DisplayNameEN, "bundle metadata must be localized before first use");

        LocalizationManager.Text.Clear();
        var interrupted = new Item { TypeID = 500044, DisplayNameRaw = "OldScythePrefabKey", FailStats = true };
        Check(!PhantomWitchScytheWeaponConfig.TryConfigure(interrupted), "injected configuration failure must propagate as false");
        Check(interrupted.DisplayName == config.DisplayNameEN && interrupted.Description == config.DescriptionEN,
            "actual raw key must be localized even when component setup fails");
        var foreign = new Item { TypeID = 1, DisplayNameRaw = "Foreign" };
        EquipmentFactory.Apply(foreign, "PhantomScythe_Melee");
        Check(foreign.ConfigureCalls == 0 && foreign.DisplayName == "*Foreign*", "other item identities must remain untouched");
        Check(!PhantomWitchScytheWeaponConfig.TryConfigure(foreign, "PhantomScythe_Melee"),
            "base-name compatibility must also require the correct item identity");
        Check(PhantomWitchScytheWeaponConfig.TryConfigure(existing, "PhantomScythe_Melee"),
            "factory-extracted melee base name must be accepted");
        Check(PhantomWitchScytheWeaponConfig.TryConfigure(existing, "PhantomScythe"), "legacy base name must remain accepted");
        Check(!PhantomWitchScytheWeaponConfig.TryConfigure(existing, "Other"), "unrelated base name must be rejected");
        Check(existing.TypeID == 500044 && existing.DisplayNameRaw == "PhantomScythe_Melee_Item",
            "existing identity and raw key must be preserved");
        Console.WriteLine("PhantomScytheLocalization: PASS " + assertions + " assertions");
    }
}
