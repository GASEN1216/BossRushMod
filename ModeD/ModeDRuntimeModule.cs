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

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (owner == null)
            {
                return;
            }

            owner.TickModeDIntegrity(deltaTime);
        }

        public override void OnDestroy()
        {
            owner = null;
        }
    }
}
