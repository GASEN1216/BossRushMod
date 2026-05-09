using UnityEngine.SceneManagement;

namespace BossRush
{
    internal interface IBossRushRuntimeModule
    {
        string ModuleName { get; }

        void OnAwake(ModBehaviour owner);
        void OnStart();
        void OnSceneLoaded(SceneRuntimeContext context);
        void OnUpdate(float deltaTime, float unscaledDeltaTime);
        void OnLateUpdate();
        void OnDestroy();
    }
}
