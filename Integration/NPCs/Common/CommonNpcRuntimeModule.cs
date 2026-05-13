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
            // 静态缓存兜底清理：阿稳寄存服务
            StorageDepositService.ResetStaticCaches();
            owner = null;
        }
    }
}
