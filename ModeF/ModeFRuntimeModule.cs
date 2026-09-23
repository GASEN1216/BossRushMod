namespace BossRush
{
    internal sealed class ModeFRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;

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
            owner = null;
        }
    }
}
