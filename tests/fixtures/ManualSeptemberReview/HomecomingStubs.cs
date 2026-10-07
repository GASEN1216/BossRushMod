using System;
using System.Collections.Generic;

namespace Saves { static class SavesSystem { internal static int CurrentSlot = 1; } }

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
        int _homecomingSlot = -1;
        internal bool HasHomecomingPending { get { return _baseMaintenancePending; } }
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

    static partial class PetNestProgressionService
    {
        private static int _runKillExpGranted;
        private static readonly HashSet<Health> _countedVictims = new HashSet<Health>();
        internal static int RunKillExp { get { return _runKillExpGranted; } set { _runKillExpGranted = value; } }
    }
    sealed class PetNestBundleData { internal List<PetNestPetRecord> Pets = new List<PetNestPetRecord>(); }
    static class PetNestCodec
    {
        internal static PetNestBundleData CloneBundle(PetNestBundleData source)
        {
            var result = new PetNestBundleData();
            foreach (var pet in source.Pets)
                if (pet != null) result.Pets.Add(new PetNestPetRecord { id=pet.id,lineageKey=pet.lineageKey,
                    state=pet.state,level=pet.level,exp=pet.exp,careerCount=pet.careerCount,talents=pet.talents });
            return result;
        }
    }
    // 存储边界替身：可写性和 Store 接受与否由用例显式驱动；事务方法来自生产源码。
    sealed class HomecomingBundleStore
    {
        internal bool CanStore = true, HasWriteBarrier, RejectStore;
        internal int Accepted;
        internal PetNestBundleData Current { get { return new PetNestBundleData { Pets=PetNestService.AuthoritativePets }; } }
        internal bool Store(PetNestBundleData candidate)
        {
            if (RejectStore || !CanStore) return false;
            foreach (var updated in candidate.Pets)
                foreach (var target in PetNestService.AuthoritativePets)
                    if (target != null && target.id == updated.id)
                    { target.exp=updated.exp;target.level=updated.level;target.careerCount=updated.careerCount;target.state=updated.state; }
            Accepted++;
            return true;
        }
    }
    static partial class PetNestPersistence
    {
        private static PetNestBundleData _activeTransaction;
        internal static readonly HomecomingBundleStore _bundle = new HomecomingBundleStore();
        internal static List<PetNestPetRecord> ActivePets
        { get { return _activeTransaction != null ? _activeTransaction.Pets : PetNestService.AuthoritativePets; } }
    }
}
