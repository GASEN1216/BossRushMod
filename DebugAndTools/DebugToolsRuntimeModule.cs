namespace BossRush
{
    internal sealed class DebugToolsRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;

        public override string ModuleName
        {
            get { return "DebugTools"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
#if BOSSRUSH_DEV
            OfficialViewTabsProbe.Install();
#endif
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (owner == null)
            {
                return;
            }

            owner.TickDebugTools(deltaTime, unscaledDeltaTime);
#if BOSSRUSH_DEV
            OfficialViewTabsProbe.Tick();
#endif
        }

        public override void OnLateUpdate()
        {
            if (owner == null)
            {
                return;
            }

            owner.LateUpdateDebugTools();
        }

        public override void OnDestroy()
        {
#if BOSSRUSH_DEV
            OfficialViewTabsProbe.Uninstall();
#endif
            owner = null;
        }
    }
}
