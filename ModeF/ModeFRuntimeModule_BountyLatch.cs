namespace BossRush
{
    internal sealed partial class ModeFRuntimeModule
    {
        private int lastPlayerBountyKillVictimId;
        private bool lastPlayerBountyKillWasBounty;

        internal void LatchPlayerBountyKill(int victimId, bool isBounty)
        {
            lastPlayerBountyKillVictimId = victimId;
            lastPlayerBountyKillWasBounty = isBounty;
        }

        internal bool HasPlayerBountyKillLatch(int victimId)
        {
            return lastPlayerBountyKillVictimId != 0
                && lastPlayerBountyKillVictimId == victimId
                && lastPlayerBountyKillWasBounty;
        }

        internal bool ConsumePlayerBountyKillLatch(int victimId)
        {
            if (lastPlayerBountyKillVictimId == 0) return false;
            if (lastPlayerBountyKillVictimId != victimId) return false;
            bool wasBounty = lastPlayerBountyKillWasBounty;
            ResetPlayerBountyKillLatch();
            return wasBounty;
        }

        internal void ResetPlayerBountyKillLatch()
        {
            lastPlayerBountyKillVictimId = 0;
            lastPlayerBountyKillWasBounty = false;
        }

    }
}
