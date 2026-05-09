namespace BossRush
{
    internal abstract class BossRushRuntimeModuleBase : IBossRushRuntimeModule
    {
        public abstract string ModuleName { get; }

        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnStart() { }
        public virtual void OnSceneLoaded(SceneRuntimeContext context) { }
        public virtual void OnUpdate(float deltaTime, float unscaledDeltaTime) { }
        public virtual void OnLateUpdate() { }
        public virtual void OnDestroy() { }
    }
}
