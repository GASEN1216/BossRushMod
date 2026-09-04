using System;
using Saves;

namespace BossRush
{
    /// <summary>
    /// Mode H 赛季存档读写（设计提案 §20.3、§25.1）。
    ///
    /// 硬约束：
    /// - 独立 v1 key：BossRush_ModeH_Season_v1；
    /// - 原生 Saves.SavesSystem typed API，KeyExisits 前置分类；
    /// - 每次只写一个完整 ModeHSeasonDto，禁止把 report / profile / roster / 虚拟奖励
    ///   拆成多次 Save；
    /// - 写 cache 后必须 Load 回读并核对 canonical payloadDigest；
    /// - 未知 schemaVersion / signatureAlgorithmVersion、不可读 payload、摘要不符
    ///   一律进入写入保护，只锁定 Mode H，不覆盖旧值、不删除 journal；
    /// - OnCollectSaveData / OnSetFile / OnSaveDeleted 均为幂等命名处理器。
    ///
    /// 实际落盘由 ModeHSaveFlushCoordinator 统一调度（每批至多一次 SaveFile(false)）。
    /// </summary>
    public static class ModeHProfilePersistence
    {
        #region 状态

        private static readonly object _lock = new object();
        private static bool _subscribed;
        private static ModeHSeasonDto _cache;
        private static ModeHSeasonDto _pending;
        private static string _pendingDigest;
        private static bool _storeFaulted;
        private static bool _writeBarrier;
        private static string _lastError;
        private static int _slotGeneration;

        #endregion

        #region 只读

        /// <summary>存档 key。</summary>
        public static string StorageKey { get { return ModeHConfig.SeasonStorageKey; } }

        /// <summary>store 是否已进入单向故障状态。</summary>
        public static bool IsStoreFaulted { get { return _storeFaulted; } }

        /// <summary>当前 key 是否处于写入保护。</summary>
        public static bool IsWriteBarrier { get { return _writeBarrier; } }

        /// <summary>最后一次失败原因。</summary>
        public static string LastError { get { return _lastError; } }

        /// <summary>当前缓存的赛季对象（可能为 null）。</summary>
        public static ModeHSeasonDto Current { get { return _cache; } }

        /// <summary>是否存在尚未落盘的写入。</summary>
        public static bool HasPendingWrite { get { return _pending != null; } }

        #endregion

        #region 订阅（幂等）

        /// <summary>幂等订阅存档生命周期事件。</summary>
        public static void EnsureSubscribed()
        {
            lock (_lock)
            {
                if (_subscribed) return;
                _subscribed = true;
            }
            try
            {
                SavesSystem.OnCollectSaveData += HandleCollectSaveData;
                SavesSystem.OnSetFile += HandleSetFile;
                SavesSystem.OnSaveDeleted += HandleSaveDeleted;
            }
            catch (Exception e)
            {
                _lastError = "season_subscribe_failed:" + e.GetType().Name;
                lock (_lock)
                {
                    _subscribed = false;
                }
            }
        }

        /// <summary>幂等退订。</summary>
        public static void ShutdownSubscription()
        {
            lock (_lock)
            {
                if (!_subscribed) return;
                _subscribed = false;
            }
            try
            {
                SavesSystem.OnCollectSaveData -= HandleCollectSaveData;
                SavesSystem.OnSetFile -= HandleSetFile;
                SavesSystem.OnSaveDeleted -= HandleSaveDeleted;
            }
            catch (Exception)
            {
                // 退订失败不阻断关闭流程
            }
        }

        private static void HandleCollectSaveData()
        {
            try
            {
                FlushPending();
            }
            catch (Exception)
            {
                // 存档收集路径不得抛出
            }
        }

        private static void HandleSetFile()
        {
            try
            {
                lock (_lock)
                {
                    _slotGeneration++;
                    _cache = null;
                    _pending = null;
                    _pendingDigest = null;
                    _writeBarrier = false;
                    // _storeFaulted 也必须随槽复位：它记录的是「这个槽的 store 出过问题」。
                    // 不清会让一次读回失败把**本进程剩下的所有存档槽**的赛季写入永久堵死，
                    // 而且不像 _writeBarrier 那样有恢复壳出口，玩家只会看到「按钮没反应」。
                    _storeFaulted = false;
                    _lastError = null;
                }
                ModeHRuntimeGates.ResetForSlotChange();
                ModeHRuntimeGates.InitializeRiskForSlot(_slotGeneration);
                LoadCurrent();
                // 抵押 journal 必须在 NotifySlotRestored 之前装载：恢复面板与押品选择器
                // 都读 IsSlotConsistent，而它只有在 LoadPersisted 之后才反映真实槽状态。
                // 漏这一步的后果是 _slotConsistent 恒 false，押品被永久禁用。
                ModeHStakeJournalPersistence.ClearCache();
                ModeHWarehouseStakeJournal.LoadPersisted(ModeHStakeJournalPersistence.LoadCurrent());
                // 新槽里如果有中断的赛季，必须重建内存 run owner 并立起 recovery-only 闸，
                // 否则玩家能直接开新赛季把它覆盖掉（CR-2026-08-29-012）
                ModeHRuntimeModule.NotifySlotRestored();
            }
            catch (Exception e)
            {
                _lastError = "season_setfile_failed:" + e.GetType().Name;
            }
        }

        private static void HandleSaveDeleted()
        {
            try
            {
                lock (_lock)
                {
                    _slotGeneration++;
                    _cache = null;
                    _pending = null;
                    _pendingDigest = null;
                    _writeBarrier = false;
                    // _storeFaulted 也必须随槽复位：它记录的是「这个槽的 store 出过问题」。
                    // 不清会让一次读回失败把**本进程剩下的所有存档槽**的赛季写入永久堵死，
                    // 而且不像 _writeBarrier 那样有恢复壳出口，玩家只会看到「按钮没反应」。
                    _storeFaulted = false;
                    _lastError = null;
                }
                ModeHRuntimeGates.ResetForSlotChange();
                ModeHRuntimeGates.InitializeRiskForSlot(_slotGeneration);
                // 删档：journal 随存档一起没了，内存态必须跟着清空，
                // 否则上一个槽的押品记录会挂在新槽上（LoadPersisted(null) 会把
                // _slotConsistent 置回 true，等于"这个槽没有未结算事务"）。
                ModeHStakeJournalPersistence.ClearCache();
                ModeHWarehouseStakeJournal.LoadPersisted(null);
                // 删档后同样要重建：读不到赛季时它会把 run owner 清空并撤下 recovery-only 闸
                ModeHRuntimeModule.NotifySlotRestored();
            }
            catch (Exception e)
            {
                _lastError = "season_savedeleted_failed:" + e.GetType().Name;
            }
        }

        #endregion

        #region 读取

        /// <summary>
        /// 读取当前槽的赛季对象。key 不存在返回 null（视为全新未进入 Mode H）；
        /// 版本不兼容 / 摘要不符 / 不可读一律进入写入保护并返回 null。
        /// </summary>
        public static ModeHSeasonDto LoadCurrent()
        {
            try
            {
                if (!SavesSystem.KeyExisits(StorageKey))
                {
                    lock (_lock)
                    {
                        _cache = null;
                    }
                    return null;
                }

                ModeHSeasonDto loaded = SavesSystem.Load<ModeHSeasonDto>(StorageKey);
                if (loaded == null)
                {
                    SetWriteBarrier("season_payload_null");
                    return null;
                }
                if (loaded.schemaVersion != ModeHConfig.CurrentSchemaVersion
                    || loaded.signatureAlgorithmVersion != ModeHConfig.CurrentSignatureAlgorithmVersion)
                {
                    SetWriteBarrier("season_schema_incompatible");
                    return null;
                }
                if (!VerifyDigest(loaded))
                {
                    SetWriteBarrier("season_digest_mismatch");
                    return null;
                }

                lock (_lock)
                {
                    _cache = loaded;
                }
                return loaded;
            }
            catch (Exception e)
            {
                SetWriteBarrier("season_load_exception:" + e.GetType().Name);
                return null;
            }
        }

        private static bool VerifyDigest(ModeHSeasonDto dto)
        {
            if (dto == null) return false;
            string declared = dto.payloadDigest;
            if (!ModeHCanonicalDigest.IsValidDigest(declared)) return false;

            string computed;
            string error;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(dto, "payloadDigest", out computed, out error))
            {
                return false;
            }
            return string.Equals(computed, declared, StringComparison.Ordinal);
        }

        private static void SetWriteBarrier(string reasonId)
        {
            lock (_lock)
            {
                _writeBarrier = true;
                _cache = null;
                _lastError = reasonId;
            }
            ModeHRuntimeGates.SetRecoveryOnlyBlocked(true, reasonId);
        }

        #endregion

        #region 写入

        /// <summary>
        /// 暂存一个完整赛季 payload。会补齐 schema / 三签名 / slotGeneration 并计算 payloadDigest。
        /// 写入保护或 store 故障时拒绝。
        /// </summary>
        public static bool StageWrite(ModeHSeasonDto dto, out string error)
        {
            error = null;
            if (dto == null)
            {
                error = "season_stage_null";
                return false;
            }
            if (_writeBarrier)
            {
                error = "season_write_barrier";
                return false;
            }
            if (_storeFaulted)
            {
                error = "season_store_faulted";
                return false;
            }

            string gameSignature;
            string modSignature;
            string signatureError;
            if (!ModeHCanonicalDigest.TryGetGameBuildSignature(out gameSignature, out signatureError)
                || !ModeHCanonicalDigest.TryGetModBuildSignature(out modSignature, out signatureError))
            {
                error = "season_signature_unavailable:" + signatureError;
                return false;
            }

            dto.schemaVersion = ModeHConfig.CurrentSchemaVersion;
            dto.signatureAlgorithmVersion = ModeHConfig.CurrentSignatureAlgorithmVersion;
            dto.gameBuildSignature = gameSignature;
            dto.modBuildSignature = modSignature;
            dto.contentCatalogSignature = ModeHContentCatalog.ContentCatalogSignature;
            dto.slotGeneration = _slotGeneration;
            dto.payloadDigest = null;

            string digest;
            string digestError;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(dto, "payloadDigest", out digest, out digestError))
            {
                error = "season_digest_failed:" + digestError;
                return false;
            }
            dto.payloadDigest = digest;

            lock (_lock)
            {
                _pending = dto;
                _pendingDigest = digest;
            }
            return true;
        }

        /// <summary>
        /// 把暂存 payload 写入 cache 并回读核对。
        /// 本方法绝不调用 SavesSystem.SaveFile：物理落盘的唯一调用点在
        /// ModeHSaveFlushCoordinator，保证每批至多一次。
        /// </summary>
        public static bool FlushPending()
        {
            ModeHSeasonDto pending;
            string expectedDigest;
            lock (_lock)
            {
                pending = _pending;
                expectedDigest = _pendingDigest;
            }
            if (pending == null) return true;
            if (_storeFaulted || _writeBarrier) return false;

            try
            {
                if (SavesSystem.IsSaving) return false;

                SavesSystem.Save<ModeHSeasonDto>(StorageKey, pending);

                ModeHSeasonDto readback = SavesSystem.Load<ModeHSeasonDto>(StorageKey);
                if (readback == null || !string.Equals(readback.payloadDigest, expectedDigest, StringComparison.Ordinal))
                {
                    _storeFaulted = true;
                    _lastError = "season_readback_mismatch";
                    return false;
                }
                if (!VerifyDigest(readback))
                {
                    _storeFaulted = true;
                    _lastError = "season_readback_digest_invalid";
                    return false;
                }

                lock (_lock)
                {
                    _cache = readback;
                    _pending = null;
                    _pendingDigest = null;
                }

                return true;
            }
            catch (Exception e)
            {
                _storeFaulted = true;
                _lastError = "season_flush_exception:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>删档 / 赛季终局后清空当前 key 的内存缓存（不删除玩家其他数据）。</summary>
        public static void ClearCache()
        {
            lock (_lock)
            {
                _cache = null;
                _pending = null;
                _pendingDigest = null;
            }
        }

        /// <summary>清空全部静态状态。</summary>
        public static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _cache = null;
                _pending = null;
                _pendingDigest = null;
                _storeFaulted = false;
                _writeBarrier = false;
                _lastError = null;
            }
        }

        #endregion
    }
}
