namespace BossRush
{
    internal sealed class CommonNpcRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;

        public override string ModuleName
        {
            get { return "CommonNPC"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
        }

        public override void OnDestroy()
        {
            // 静态缓存兜底清理：快递员付费扫箱服务
            CourierPaidLootSweepService.ResetStaticCaches();
            owner = null;
        }
    }
}
