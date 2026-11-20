namespace BossRush
{
    public static partial class EquipmentFactory
    {
        public static void ResetStaticCaches()
        {
            loadedModels.Clear();
            meleeModelsByHandheld.Clear();
            meleeHandheldsByType.Clear();
            loadedModelsByBaseName.Clear();
            loadedBuffs.Clear();
            loadedBullets.Clear();
            loadedGuns.Clear();
            loadedBundles.Clear();
            bundleRegistrations.Clear();
            bundleModels.Clear();
            modelWrappers.Clear();
            foreach (var bundle in ownedBundles)
                if (bundle != null) bundle.Unload(false);
            ownedBundles.Clear();
            customMeleeWeaponTypeIds.Clear();
            gunConfigurators.Clear();
            gunConfiguratorIndexes.Clear();
            equipmentConfigurators.Clear();
            equipmentConfiguratorIndexes.Clear();
            gameShader = null;
        }
    }
}
