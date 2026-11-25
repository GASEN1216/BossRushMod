namespace BossRush
{
    internal sealed partial class CommonNpcRuntimeModule : BossRushRuntimeModuleBase
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

        /// <summary>进场景时确保友好 NPC 名牌画布在（走近显示，没有配置项；幂等）。</summary>
        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            FriendlyNpcNameplates.Refresh();
        }

        public override void OnDestroy()
        {
            // 静态缓存兜底清理：快递员付费扫箱服务
            CourierPaidLootSweepService.ResetStaticCaches();
            // 静态缓存兜底清理：阿稳寄存服务
            StorageDepositService.ResetStaticCaches();
            // 友好 NPC 名牌（走近显示）的唯一清理 owner
            FriendlyNpcNameplates.Shutdown();
            owner = null;
        }
    }
}
