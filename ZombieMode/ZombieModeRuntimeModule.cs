namespace BossRush
{
    internal sealed class ZombieModeRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;

        public override string ModuleName
        {
            get { return "ZombieMode"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            // 入场回滚的欠账账本：订阅官方「经济加载完成」，经济一回来就把欠玩家的现金与
            // 邀请函补上（CR-2026-09-11-019）。订阅幂等，OnDestroy 成对退订。
            ZombieModeEntryDebt.Attach();
        }

        public override void OnDestroy()
        {
            ZombieModeEntryDebt.ResetStaticCaches();
            owner = null;
        }
    }
}
