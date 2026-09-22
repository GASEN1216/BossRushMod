namespace BossRush
{
    internal sealed class WavesArenaRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private static WavesArenaRuntimeModule current;
        private int waveGeneration;
        private InfiniteHellMilestoneDelivery milestoneDelivery;

        internal static void ResetMilestones(ModBehaviour expectedOwner)
        {
            if (current != null && current.owner == expectedOwner)
                current.milestoneDelivery = new InfiniteHellMilestoneDelivery();
        }

        internal static void EnqueueMilestone(ModBehaviour expectedOwner, int tier, UnityEngine.Vector3 position)
        {
            if (current != null && current.owner == expectedOwner && current.milestoneDelivery != null)
                current.milestoneDelivery.Enqueue(tier, position);
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (milestoneDelivery != null) milestoneDelivery.Tick(owner);
        }


        internal static System.Func<bool> CaptureValidity(ModBehaviour expectedOwner, bool beginWave, bool requireActive)
        {
            WavesArenaRuntimeModule runtime = current;
            if (runtime == null || runtime.owner != expectedOwner) return () => false;
            if (beginWave) runtime.waveGeneration++;
            int generation = runtime.waveGeneration;
            int sceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            CharacterMainControl player = CharacterMainControl.Main;
            return () => runtime.owner != null && runtime.owner == expectedOwner
                && current == runtime && runtime.waveGeneration == generation
                && UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle == sceneHandle
                && player != null && CharacterMainControl.Main == player
                && player.Health != null && !player.Health.IsDead
                && (!requireActive || expectedOwner.IsActive);
        }

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            waveGeneration++;
            milestoneDelivery = null;
        }

        public override string ModuleName
        {
            get { return "WavesArena"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            current = this;
        }

        public override void OnDestroy()
        {
            waveGeneration++;
            if (current == this) current = null;
            milestoneDelivery = null;
            owner = null;
        }
    }
}
