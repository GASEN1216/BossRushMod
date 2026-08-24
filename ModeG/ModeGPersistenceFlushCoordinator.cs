using System;
using Cysharp.Threading.Tasks;
using Saves;

namespace BossRush
{
    /// <summary>
    /// Per-save-slot flush barrier shared by the Mode G profile and nemesis DTOs.
    /// Each DTO is typed-saved/read back independently; the host file is flushed
    /// at most once for the batch.
    /// </summary>
    internal static class ModeGPersistenceFlushCoordinator
    {
        private static readonly object Sync = new object();
        private static bool _running;
        private static bool _pending;
        private static bool _scheduled;
        private static bool _faulted;
        private static int _slotGeneration;

        private const int MaxBusyWaitFrames = 120;

        internal static bool IsFaulted { get { lock (Sync) return _faulted; } }

        internal static void RequestFlush()
        {
            bool schedule = false;
            lock (Sync)
            {
                _pending = true;
                if (!_running && !_scheduled && !_faulted)
                {
                    _scheduled = true;
                    schedule = true;
                }
            }

            if (schedule) FlushNextFrame().Forget();
        }

        private static async UniTaskVoid FlushNextFrame()
        {
            await UniTask.Yield();

            int busyFrames = 0;
            while (SavesSystem.IsSaving && busyFrames < MaxBusyWaitFrames)
            {
                busyFrames++;
                await UniTask.Yield();
            }

            int slot;
            int generation;
            bool retryBusy = false;
            lock (Sync)
            {
                _scheduled = false;
                if (_running || _faulted) return;
                if (SavesSystem.IsSaving)
                {
                    retryBusy = true;
                }
                else
                {
                    _running = true;
                }
                if (retryBusy)
                {
                    slot = 0;
                    generation = 0;
                }
                else
                {
                    _pending = false;
                    slot = SavesSystem.CurrentSlot;
                    generation = _slotGeneration;
                }
            }
            if (retryBusy)
            {
                RequestFlush();
                return;
            }
            try
            {
                FlushBatch(slot, generation);
            }
            catch (Exception e)
            {
                MarkFaulted(e);
            }
            finally
            {
                CompleteBatchAndMaybeReschedule();
            }
        }

        /// <summary>
        /// 宿主销毁前同步尽力提交一次。官方保存繁忙或已有批次运行时只合并请求，
        /// 不重入/打断宿主保存；实际 SaveFile 调用仍只有 FlushBatch 一个位置。
        /// </summary>
        internal static void TryFlushOnHostDestroy()
        {
            int slot = 0;
            int generation = 0;
            bool runNow = false;
            lock (Sync)
            {
                if (_faulted) return;
                _pending = true;
                if (!_running && !SavesSystem.IsSaving)
                {
                    _scheduled = false;
                    _running = true;
                    _pending = false;
                    slot = SavesSystem.CurrentSlot;
                    generation = _slotGeneration;
                    runNow = true;
                }
            }

            if (!runNow)
            {
                RequestFlush();
                return;
            }

            try
            {
                FlushBatch(slot, generation);
            }
            catch (Exception e)
            {
                MarkFaulted(e);
            }
            finally
            {
                CompleteBatchAndMaybeReschedule();
            }
        }

        private static void FlushBatch(int slot, int generation)
        {
            bool hadDirty = ModeGNemesisPersistence.HasPendingFlush
                || ModeGProfilePersistence.HasPendingFlush;
            if (!hadDirty || !IsSameSlot(slot, generation)) return;

            ModeGNemesisPersistence.FlushPending(writeFile: false);
            if (!IsSameSlot(slot, generation)) return;
            ModeGProfilePersistence.FlushPending(writeFile: false);
            if (ModeGNemesisPersistence.IsStoreFaulted
                || ModeGProfilePersistence.IsStoreFaulted)
            {
                throw new InvalidOperationException(
                    "typed persistence flush failed before host SaveFile");
            }
            if (hadDirty && !SavesSystem.IsSaving && IsSameSlot(slot, generation))
            {
                SavesSystem.SaveFile(false);
            }
        }

        private static void MarkFaulted(Exception e)
        {
            lock (Sync)
            {
                _faulted = true;
                _pending = false;
            }
            ModeGNemesisPersistence.MarkCoordinatorFaulted(e);
            ModeGProfilePersistence.MarkCoordinatorFaulted(e);
            ModBehaviour.DevLog("[ModeG] [ERROR] persistence flush coordinator faulted: " + e.Message);
        }

        private static void CompleteBatchAndMaybeReschedule()
        {
            bool reschedule = false;
            lock (Sync)
            {
                _running = false;
                _scheduled = false;
                if (!_faulted
                    && (_pending
                        || ModeGNemesisPersistence.HasPendingFlush
                        || ModeGProfilePersistence.HasPendingFlush)
                    && !SavesSystem.IsSaving)
                {
                    _scheduled = true;
                    reschedule = true;
                }
            }
            if (reschedule) FlushNextFrame().Forget();
        }

        private static bool IsSameSlot(int slot, int generation)
        {
            lock (Sync)
            {
                return generation == _slotGeneration && SavesSystem.CurrentSlot == slot;
            }
        }

        internal static void NotifySlotChanged()
        {
            lock (Sync)
            {
                _slotGeneration++;
                _pending = false;
                _scheduled = false;
            }
        }
    }
}
