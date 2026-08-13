using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>Tracks asynchronous Mode G work that must outlive the local run owner.</summary>
    public static class ModeGLateCleanupSink
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<int> Leases = new HashSet<int>();
        private static int _nextLeaseId;

        public static bool HasPendingLeases
        {
            get { lock (Sync) { return Leases.Count > 0; } }
        }

        public static int AcquireLease(string owner)
        {
            lock (Sync)
            {
                int id = ++_nextLeaseId;
                if (id <= 0)
                {
                    _nextLeaseId = 1;
                    id = 1;
                }
                Leases.Add(id);
                ModBehaviour.DevLog("[ModeG] late-cleanup lease acquired: " + id + " owner=" + (owner ?? "unknown"));
                return id;
            }
        }

        public static void ReleaseLease(int leaseId)
        {
            if (leaseId <= 0) return;
            lock (Sync) { Leases.Remove(leaseId); }
        }

        internal static void ClearAll()
        {
            lock (Sync) { Leases.Clear(); }
        }
    }

    public static class ModeGCleanupController
    {
        public const float LateCleanupMaxWaitSeconds = 15f;
        public const float LateCleanupCheckInterval = 0.5f;

        public static void Cleanup(ModeGRunState state, ModeGExitReason reason)
        {
            if (state == null) return;
            try
            {
                if (state.exitReason == ModeGExitReason.None) state.exitReason = reason;
                if (state.lifecyclePhase != ModeGLifecyclePhase.Exiting &&
                    state.lifecyclePhase != ModeGLifecyclePhase.None)
                {
                    state.TryAdvanceLifecycle(ModeGLifecyclePhase.Exiting);
                }

                state.spawnLeasesInvalidated = true;
                state.combatPhase = ModeGCombatPhase.None;
                state.lastStandActive = false;
                state.intermissionActive = false;
                state.ClearTrackedBosses();

                if (reason == ModeGExitReason.ManualExit && state.TryConsumeContractStreakBreakToken())
                {
                    ModeGProfilePersistence.ClearContractStreakOnManualExit();
                }

                if (state.lifecyclePhase == ModeGLifecyclePhase.Exiting)
                {
                    state.TryAdvanceLifecycle(ModeGLifecyclePhase.None);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] cleanup failed: " + e.Message);
                try
                {
                    state.ClearTrackedBosses();
                    state.lifecyclePhase = ModeGLifecyclePhase.None;
                    state.combatPhase = ModeGCombatPhase.None;
                }
                catch { }
            }
        }

        public static void LateCleanupSink(ModeGRunState state)
        {
            Cleanup(state, ModeGExitReason.SceneChanged);
        }

        public static void EmergencyShutdown(ModeGRunState state)
        {
            try
            {
                PrepareHostDestroyInternal(state);
                Cleanup(state, ModeGExitReason.ModDestroyed);
                ModeGPresentationAssetCache.Unload();
            }
            catch { }
        }

        public static void PrepareHostDestroyInternal(ModeGRunState state)
        {
            try
            {
                if (state != null)
                {
                    state.spawnLeasesInvalidated = true;
                    state.rewardNonceInvalidated = true;
                    state.ClearTrackedBosses();
                }
                ModeGRewardTransaction.InvalidateAllNonces();
                ModeGNemesisPersistence.ShutdownSubscription();
                ModeGProfilePersistence.ShutdownSubscription();
                ModBehaviour host = ModBehaviour.Instance;
                if (host != null) host.CancelModeGRewardMaterialization_LootAndRewards();
            }
            catch { }
        }
    }
}
