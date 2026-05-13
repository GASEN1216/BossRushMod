namespace BossRush
{
    public static partial class CustomItemRuntimeStateHelper
    {
        public static void ResetStaticCaches()
        {
            runtimeConfigEntries.Clear();
            meleeRefField = null;
            gunRefField = null;
            currentUsingSocketCacheField = null;
            itemAgentItemField = null;
            meleeSettingField = null;
            meleeSoundKeyField = null;
            meleeCompatFieldsCached = false;
        }
    }
}
