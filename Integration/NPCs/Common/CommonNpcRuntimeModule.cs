namespace BossRush
{
    internal sealed partial class CommonNpcRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private CoopNpcWorld coopWorld;

        public override string ModuleName
        {
            get { return "CommonNPC"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            coopWorld = new CoopNpcWorld(owner);
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        { if (coopWorld != null) coopWorld.Tick(); }

        public override void OnDestroy()
        {
            if (coopWorld != null) coopWorld.Dispose();
            coopWorld = null;
            // 静态缓存兜底清理：快递员付费扫箱服务
            CourierPaidLootSweepService.ResetStaticCaches();
            // 静态缓存兜底清理：阿稳寄存服务
            StorageDepositService.ResetStaticCaches();
            owner = null;
        }
    }
}
