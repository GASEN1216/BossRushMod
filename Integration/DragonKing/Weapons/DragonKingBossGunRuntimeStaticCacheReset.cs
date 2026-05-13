namespace BossRush
{
    public static partial class DragonKingBossGunRuntime
    {
        public static void ResetStaticCaches()
        {
            CleanupRuntime();
            ClearSceneCaches();
            System.Array.Clear(SharedColliderBuffer, 0, SharedColliderBuffer.Length);
            SharedReceiverIdSet.Clear();
            reusableBulletTypeDict.Clear();
            emptyBulletTypeDict.Clear();
        }
    }
}
