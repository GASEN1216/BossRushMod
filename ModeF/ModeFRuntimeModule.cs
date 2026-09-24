namespace BossRush
{
    internal sealed class ModeFRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
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

        public override string ModuleName
        {
            get { return "ModeF"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
        }

        public override void OnDestroy()
        {
            // Mod 卸载时 Mode F 可能还在跑：状态卡是静态 owner，这里兜底销毁（切图走 ExitModeF 那一份）。
            ModeFStatusHud.Dispose();
            ResetPlayerBountyKillLatch();
            owner = null;
        }
    }
}
