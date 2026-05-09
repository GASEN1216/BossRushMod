namespace BossRush
{
    internal sealed class ModeDRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;

        public override string ModuleName
        {
            get { return "ModeD"; }
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
