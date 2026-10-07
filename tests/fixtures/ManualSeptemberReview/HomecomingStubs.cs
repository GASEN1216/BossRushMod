using System;

namespace BossRush
{
    class SceneRuntimeContext { }
    abstract class BossRushRuntimeModuleBase
    {
        public virtual void OnSceneLoaded(SceneRuntimeContext context) { }
        public virtual void OnUpdate(float deltaTime, float unscaledDeltaTime) { }
    }
    // 宿主与无关基地业务替身；场景回调、Update、基地维护和归巢结算均逐字提取生产方法。
    partial class PetNestRuntimeModule : BossRushRuntimeModuleBase
    {
        ModBehaviour _owner;
        int _sceneGeneration;
        bool _bootstrapped;
        bool _baseMaintenancePending;
        float _nextBaseMaintenanceTime;
        string _homecomingPetId;
        const float BaseMaintenanceIntervalSeconds = 5f;
        internal bool IsEnabled { get { return _owner != null; } }
        internal PetNestRuntimeModule(ModBehaviour owner) { _owner = owner; }
        void EnsureBootstrapped() { _bootstrapped = true; }
        void ShutdownIfEnabledTurnedOff() { _bootstrapped = false; }
        void EnsureOfficialLineagesPrimed() { }
        static void CloseAllInteractiveViewsForSceneChange() { }
        static void LogFailure(string stage, Exception error) { throw new Exception(stage, error); }
    }
    static class PetNestSaveCoordinator { public static void Tick() { } }
    static class PetNestMuseumStats { public static void ClearCountedKills() { } public static void RecordLevel(PetNestPetRecord pet) { } }
    static class PetNestDropService { public static void ClearAllTracking() { } }
}
