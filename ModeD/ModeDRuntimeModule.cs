namespace BossRush
{
    internal sealed class ModeDRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private static ModeDRuntimeModule current;
        private int generation;

        internal static void Invalidate(ModBehaviour expectedOwner)
        {
            if (current != null && current.owner == expectedOwner) current.generation++;
        }

        internal static System.Func<bool> CaptureValidity(ModBehaviour expectedOwner, bool beginWave)
        {
            ModeDRuntimeModule runtime = current;
            if (runtime == null || runtime.owner != expectedOwner) return () => false;
            if (beginWave) runtime.generation++;
            int capturedGeneration = runtime.generation;
            int wave = expectedOwner.ModeDWaveIndex;
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            return () => runtime.owner != null && runtime.owner == expectedOwner && current == runtime
                && runtime.generation == capturedGeneration && expectedOwner.IsModeDActive
                && expectedOwner.ModeDWaveIndex == wave
                && UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle == scene;
        }

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            generation++;
        }

        public override string ModuleName
        {
            get { return "ModeD"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            current = this;
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
            generation++;
            if (current == this) current = null;
            owner = null;
        }
    }
}
