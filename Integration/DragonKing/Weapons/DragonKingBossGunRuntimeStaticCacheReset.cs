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
            // 龙王 / 龙皇铳共用的命中爆闪发射器（VB-15）：模块销毁时一并收掉；切场景另由 DragonKingAbilityController.ClearStaticMaterialCache 收。
            DragonKingFxShared.ClearStaticCaches();
        }
    }
}
