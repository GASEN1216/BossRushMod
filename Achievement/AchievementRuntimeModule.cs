namespace BossRush
{
    internal sealed class AchievementRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;

        public override string ModuleName
        {
            get { return "Achievement"; }
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

            owner.TickAchievementRuntime(deltaTime, unscaledDeltaTime);
        }

        public override void OnDestroy()
        {
            owner = null;
        }
    }
}
