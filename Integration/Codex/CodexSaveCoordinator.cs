// ============================================================================
// CodexSaveCoordinator.cs - 鸭皇图鉴落盘协调器（门面）
// ============================================================================
// 状态机（pending 合并 / IsSaving 推迟 / 只在基地物理落盘 / 每帧闸 / 重试预算 /
// 「欠一次 SaveFile」独立记账）自 2026-09-06 起只有一份实现：
// Common/Lifecycle/BossRushSaveCoordinatorEngine.cs。本文件只保留一行式门面，
// CodexSaveCoordinator.* 的调用面不变。
//
// 契约仍然成立（CodexPersistenceGuard）：本类是图鉴**唯一**的物理落盘入口（经引擎，
// 每批至多一次 SaveFile）；采集器只调 CodexPersistence.Store() 入队，绝不自己落盘；
// 图鉴的写入点就是击杀 Boss 那一帧，必然落在交火中，因此非基地一律推迟到回基地补写，
// 宿主销毁与关停绕闸兜底。
// ============================================================================

namespace BossRush
{
    /// <summary>图鉴落盘协调器门面。pending 合并成一批，一批一次 SaveFile（由共享引擎执行）。</summary>
    internal static class CodexSaveCoordinator
    {
        private static readonly BossRushSaveCoordinatorEngine _engine =
            new BossRushSaveCoordinatorEngine(new Source(), true);

        /// <summary>是否存在等待落盘的批次。</summary>
        internal static bool HasDeferredFlush { get { return _engine.HasDeferredFlush; } }

        /// <summary>最后一次失败原因。</summary>
        internal static string LastError { get { return _engine.LastError; } }

        #region 对外入口

        /// <summary>幂等订阅存档生命周期。</summary>
        internal static void EnsureSubscribed()
        {
            CodexPersistence.EnsureSubscribed();
        }

        /// <summary>幂等退订。</summary>
        internal static void ShutdownSubscription()
        {
            CodexPersistence.ShutdownSubscription();
        }

        /// <summary>
        /// 请求把当前 pending 落盘。成功返回 true；
        /// IsSaving / 非基地 / 写失败时返回 false 并保留 pending（由 Tick 重试）。
        /// </summary>
        internal static bool RequestFlush(out string error)
        {
            return _engine.RequestFlush(out error);
        }

        /// <summary>无返回值的便捷入口（调用方不关心失败细节时用）。</summary>
        internal static void RequestFlush()
        {
            string error;
            _engine.RequestFlush(out error);
        }

        /// <summary>切档 / 删档：清空 deferred 状态。</summary>
        internal static void NotifySlotChanged()
        {
            _engine.NotifySlotChanged();
        }

        /// <summary>宿主 tick：重试被推迟的批次。未 deferred 时 O(1) 早返。</summary>
        internal static void Tick()
        {
            _engine.Tick();
        }

        /// <summary>宿主销毁时尽力提交一次（绕过基地闸与每帧闸）；失败只记录，不抛出。</summary>
        internal static bool TryFlushOnHostDestroy()
        {
            return _engine.TryFlushOnHostDestroy();
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            _engine.Reset();
            CodexPersistence.ResetStaticCaches();
        }

        #endregion

        #region 引擎数据源

        private sealed class Source : IBossRushSaveBatchSource
        {
            public string LogPrefix { get { return CodexTuning.LogPrefix; } }

            public bool HasPendingWrite { get { return CodexPersistence.HasPendingWrite; } }

            public bool IsStoreFaulted { get { return CodexPersistence.IsStoreFaulted; } }

            /// <summary>图鉴没有现金 / 实物快照义务。</summary>
            public bool HasSnapshotObligation { get { return false; } }

            public string LastError { get { return CodexPersistence.LastError; } }

            public bool CollectSnapshot(out string error)
            {
                error = null;
                return true;
            }

            public bool FlushPending()
            {
                return CodexPersistence.FlushPending();
            }

            public void OnPhysicalSaveSucceeded()
            {
            }
        }

        #endregion
    }
}
