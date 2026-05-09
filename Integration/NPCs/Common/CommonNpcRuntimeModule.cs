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
            owner = null;
        }
    }
}
