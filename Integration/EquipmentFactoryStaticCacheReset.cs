namespace BossRush
{
    public static partial class EquipmentFactory
    {
        public static void ResetStaticCaches()
        {
            loadedModels.Clear();
            loadedModelsByBaseName.Clear();
            loadedBuffs.Clear();
            loadedBullets.Clear();
            loadedGuns.Clear();
            loadedBundles.Clear();
            foreach (var bundle in ownedBundles)
                if (bundle != null) bundle.Unload(false);
            ownedBundles.Clear();
            customMeleeWeaponTypeIds.Clear();
            modDirectory = null;
            gameShader = null;
        }
    }
}
