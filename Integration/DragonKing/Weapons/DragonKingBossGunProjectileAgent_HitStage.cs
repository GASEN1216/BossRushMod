namespace BossRush
{
    internal sealed partial class DragonKingBossGunProjectileAgent
    {
        private DragonKingBossGunRuntime.DragonKingBossGunHitStage ResolveHitStage()
        {
            if (secondaryProjectile)
            {
                return DragonKingBossGunRuntime.DragonKingBossGunHitStage.Secondary;
            }

            if (returning)
            {
                return DragonKingBossGunRuntime.DragonKingBossGunHitStage.Return;
            }

            if (successfulHits > 0)
            {
                return DragonKingBossGunRuntime.DragonKingBossGunHitStage.Followup;
            }

            return DragonKingBossGunRuntime.DragonKingBossGunHitStage.Primary;
        }
    }
}
