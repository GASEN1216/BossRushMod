namespace BossRush
{
    internal sealed class ModeERuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;

        public override string ModuleName
        {
            get { return "ModeE"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
        }

        public override void OnDestroy()
        {
            if (owner != null)
            {
                owner.DestroyModeEShellRuntimeState();
            }
            // 静态缓存兜底清理：Mode E 商人相关缓存
            ModBehaviour.ResetModeEMerchantStaticCaches();
            owner = null;
        }
    }
}
