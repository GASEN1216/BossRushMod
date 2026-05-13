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
            customMeleeWeaponTypeIds.Clear();
            modDirectory = null;
            gameShader = null;
        }
    }
}
