using System;
using Saves;

namespace BossRush
{
    /// <summary>
    /// Mode H 存档落盘协调器（设计提案 §20.3、§25.1）。
    ///
    /// 硬约束：
    /// - 普通比赛只构造并写入一个完整 Season payload，禁止拆成多次 Save；
    /// - 本类是 Mode H **唯一** 调用 SavesSystem.SaveFile 的地方，且每批至多一次；
    /// - 名人堂使用稳定 hallOfFameId 的跨 key 幂等流程：先在 Season 中保存 pending command，
    ///   再按 ID 向 HallOfFame key 做集合插入并回读，最后把 Season command 标成完成；
    /// - SavesSystem.IsSaving 时不强写，只登记 deferred，由 Tick 重试；
    /// - SaveFile(false) 不触发 OnCollectSaveData，因此绝不能把它单独当作
    ///   仓库采集或物理落盘原子性的证明。
    /// </summary>
    public static class ModeHSaveFlushCoordinator
    {
        #region 状态

        private static readonly object _lock = new object();
        private static bool _deferredFlushPending;
        private static string _lastError;
        private static int _deferredRetryCount;

        /// <summary>deferred 重试上限；超过后保留 pending 并报告失败，不静默丢弃。</summary>
        private const int MaxDeferredRetries = 600;

        #endregion

        #region 只读

        /// <summary>是否存在等待落盘的批次。</summary>
        public static bool HasDeferredFlush { get { return _deferredFlushPending; } }

        /// <summary>最后一次失败原因。</summary>
        public static string LastError { get { return _lastError; } }

        #endregion

        #region 对外入口

        /// <summary>幂等订阅两个 Mode H 存档 key 的生命周期。</summary>
        public static void EnsureSubscribed()
        {
            ModeHProfilePersistence.EnsureSubscribed();
            ModeHHallOfFamePersistence.EnsureSubscribed();
            ModeHStakeJournalPersistence.EnsureSubscribed();
        }

        /// <summary>幂等退订。</summary>
        public static void ShutdownSubscription()
        {
            ModeHProfilePersistence.ShutdownSubscription();
            ModeHHallOfFamePersistence.ShutdownSubscription();
            ModeHStakeJournalPersistence.ShutdownSubscription();
        }

        /// <summary>
        /// 提交当前 active journal 并落盘。押品事务的每个阶段推进都必须调它——
        /// journal 的意义就是「磁盘上先有记录，才动玩家物品」，少一次落盘就等于
        /// 崩溃后无法证明那件装备去哪了。
        /// </summary>
        public static bool RequestStakeJournalWrite(ModeHStakeJournalDto journal, out string error)
        {
            error = null;
            if (!ModeHStakeJournalPersistence.StageWrite(journal, out error)) return false;
            return FlushBatch(out error);
        }

        /// <summary>
        /// 提交一个完整赛季 payload 并落盘。
        /// 成功返回 true；IsSaving 或读回失败时返回 false 并保留 pending。
        /// </summary>
        public static bool RequestSeasonWrite(ModeHSeasonDto season, out string error)
        {
            error = null;
            if (!ModeHProfilePersistence.StageWrite(season, out error)) return false;
            return FlushBatch(out error);
        }

        /// <summary>按稳定 ID 幂等插入一条名人堂记录并落盘。</summary>
        public static bool RequestHallOfFameInsert(ModeHHallOfFameRecordDto record, out string error)
        {
            error = null;
            if (!ModeHHallOfFamePersistence.StageRecordInsert(record, out error)) return false;
            return FlushBatch(out error);
        }

        /// <summary>写入按四签名键控的生产认证缓存并落盘。</summary>
        public static bool RequestCertificationCacheWrite(
            ModeHProductionCertificationDto snapshot, int slotGeneration, out string error)
        {
            error = null;
            if (!ModeHHallOfFamePersistence.StageCertificationCache(snapshot, slotGeneration, out error))
            {
                return false;
            }
            return FlushBatch(out error);
        }

        /// <summary>作废生产认证缓存（诊断页“强制重新认证”）并落盘。</summary>
        public static bool RequestCertificationCacheInvalidate(out string error)
        {
            error = null;
            if (!ModeHHallOfFamePersistence.StageInvalidateCertificationCache(out error)) return false;
            return FlushBatch(out error);
        }

        /// <summary>宿主 tick：重试被 IsSaving 推迟的批次。</summary>
        public static void Tick()
        {
            if (!_deferredFlushPending) return;
            lock (_lock)
            {
                _deferredRetryCount++;
                if (_deferredRetryCount > MaxDeferredRetries)
                {
                    _lastError = "flush_deferred_budget_exhausted";
                    _deferredFlushPending = false;
                    _deferredRetryCount = 0;
                    ModeHRuntimeGates.SetRecoveryOnlyBlocked(true, _lastError);
                    return;
                }
            }
            string error;
            FlushBatch(out error);
        }

        /// <summary>宿主销毁时尽力提交一次；失败只登记 deferred，不抛出。</summary>
        public static bool TryFlushOnHostDestroy()
        {
            try
            {
                string error;
                return FlushBatch(out error);
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 批次落盘（唯一物理写入点）

        private static bool FlushBatch(out string error)
        {
            error = null;
            bool seasonPending = ModeHProfilePersistence.HasPendingWrite;
            bool hallPending = ModeHHallOfFamePersistence.HasPendingWrite;
            bool journalPending = ModeHStakeJournalPersistence.HasPendingWrite;
            if (!seasonPending && !hallPending && !journalPending)
            {
                lock (_lock)
                {
                    _deferredFlushPending = false;
                    _deferredRetryCount = 0;
                }
                return true;
            }

            try
            {
                if (SavesSystem.IsSaving)
                {
                    lock (_lock)
                    {
                        _deferredFlushPending = true;
                    }
                    error = "flush_deferred_is_saving";
                    return false;
                }

                if (seasonPending && !ModeHProfilePersistence.FlushPending())
                {
                    error = ModeHProfilePersistence.LastError != null
                        ? ModeHProfilePersistence.LastError
                        : "season_flush_failed";
                    lock (_lock)
                    {
                        _deferredFlushPending = true;
                    }
                    return false;
                }

                if (hallPending && !ModeHHallOfFamePersistence.FlushPending())
                {
                    error = ModeHHallOfFamePersistence.LastError != null
                        ? ModeHHallOfFamePersistence.LastError
                        : "hof_flush_failed";
                    lock (_lock)
                    {
                        _deferredFlushPending = true;
                    }
                    return false;
                }

                // journal 排在 Season / 名人堂之后：押品事务的推进顺序是
                // 「先写 journal，再动物品」，而调用方每次只推进一个阶段并各自
                // RequestStakeJournalWrite，因此这里同批写入不会颠倒因果。
                if (journalPending && !ModeHStakeJournalPersistence.FlushPending())
                {
                    error = ModeHStakeJournalPersistence.LastError != null
                        ? ModeHStakeJournalPersistence.LastError
                        : "journal_flush_failed";
                    lock (_lock)
                    {
                        _deferredFlushPending = true;
                    }
                    return false;
                }

                if (ModeHProfilePersistence.IsStoreFaulted
                    || ModeHHallOfFamePersistence.IsStoreFaulted
                    || ModeHStakeJournalPersistence.IsStoreFaulted)
                {
                    error = "store_faulted";
                    return false;
                }

                // 每批至多一次物理落盘：这是 Mode H 唯一的 SaveFile 调用点。
                SavesSystem.SaveFile(false);

                lock (_lock)
                {
                    _deferredFlushPending = false;
                    _deferredRetryCount = 0;
                    _lastError = null;
                }
                return true;
            }
            catch (Exception e)
            {
                error = "flush_exception:" + e.GetType().Name;
                _lastError = error;
                lock (_lock)
                {
                    _deferredFlushPending = true;
                }
                return false;
            }
        }

        #endregion

        #region 清理

        /// <summary>清空全部静态状态（删档 / Mod 卸载 / 宿主重建）。</summary>
        public static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _deferredFlushPending = false;
                _deferredRetryCount = 0;
                _lastError = null;
            }
            ModeHProfilePersistence.ResetStaticCaches();
            ModeHHallOfFamePersistence.ResetStaticCaches();
        }

        #endregion
    }
}
