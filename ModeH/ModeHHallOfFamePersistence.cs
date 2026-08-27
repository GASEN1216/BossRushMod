using System;
using System.Collections.Generic;
using Saves;

namespace BossRush
{
    /// <summary>
    /// Mode H 名人堂与生产认证缓存存档（设计提案 §17.8、§17.2、§20.3、§25.1）。
    ///
    /// 硬约束：
    /// - 独立 v1 key：BossRush_ModeH_HallOfFame_v1，跨赛季存在，不随 Season 清空；
    /// - 最多 32 条记录，按 hallOfFameId 去重，插入第 33 条时按记录快照中已冻结的
    ///   (createdUtc, hallOfFameId) 删除最旧一条，重试不得重新生成时间；
    /// - 只读展示，不提供数值继承，不作为下一季可招募单位；
    /// - envelope 额外承载按四签名缓存的生产认证结果（§17.2），命中即可跳过逐 key 诊断，
    ///   但绝不跳过 arena isolation lease、spectator lease 与地图点位审计；
    /// - 写 cache 后回读并核对 payloadDigest；旧构建记录只读兼容，不因签名不同删除或改写。
    /// </summary>
    public static class ModeHHallOfFamePersistence
    {
        #region 状态

        private static readonly object _lock = new object();
        private static bool _subscribed;
        private static ModeHHallOfFameEnvelopeDto _cache;
        private static ModeHHallOfFameEnvelopeDto _pending;
        private static string _pendingDigest;
        private static bool _storeFaulted;
        private static bool _writeBarrier;
        private static string _lastError;

        #endregion

        #region 只读

        /// <summary>存档 key。</summary>
        public static string StorageKey { get { return ModeHConfig.HallOfFameStorageKey; } }

        /// <summary>store 是否已进入单向故障状态。</summary>
        public static bool IsStoreFaulted { get { return _storeFaulted; } }

        /// <summary>当前 key 是否处于写入保护。</summary>
        public static bool IsWriteBarrier { get { return _writeBarrier; } }

        /// <summary>最后一次失败原因。</summary>
        public static string LastError { get { return _lastError; } }

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
                _lastError = "hof_subscribe_failed:" + e.GetType().Name;
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
                    _cache = null;
                    _pending = null;
                    _pendingDigest = null;
                    _writeBarrier = false;
                    _lastError = null;
                }
                LoadCurrent();
            }
            catch (Exception e)
            {
                _lastError = "hof_setfile_failed:" + e.GetType().Name;
            }
        }

        private static void HandleSaveDeleted()
        {
            try
            {
                lock (_lock)
                {
                    _cache = null;
                    _pending = null;
                    _pendingDigest = null;
                    _writeBarrier = false;
                    _lastError = null;
                }
            }
            catch (Exception e)
            {
                _lastError = "hof_savedeleted_failed:" + e.GetType().Name;
            }
        }

        #endregion

        #region 读取

        /// <summary>读取 envelope；不存在返回空 envelope；不可读进入写入保护。</summary>
        public static ModeHHallOfFameEnvelopeDto LoadCurrent()
        {
            try
            {
                if (!SavesSystem.KeyExisits(StorageKey))
                {
                    ModeHHallOfFameEnvelopeDto empty = CreateEmpty();
                    lock (_lock)
                    {
                        _cache = empty;
                    }
                    return empty;
                }

                ModeHHallOfFameEnvelopeDto loaded = SavesSystem.Load<ModeHHallOfFameEnvelopeDto>(StorageKey);
                if (loaded == null)
                {
                    SetWriteBarrier("hof_payload_null");
                    return null;
                }
                if (loaded.schemaVersion != ModeHConfig.CurrentSchemaVersion
                    || loaded.signatureAlgorithmVersion != ModeHConfig.CurrentSignatureAlgorithmVersion)
                {
                    SetWriteBarrier("hof_schema_incompatible");
                    return null;
                }
                if (!VerifyDigest(loaded))
                {
                    SetWriteBarrier("hof_digest_mismatch");
                    return null;
                }
                if (loaded.records == null) loaded.records = new List<ModeHHallOfFameRecordDto>();

                lock (_lock)
                {
                    _cache = loaded;
                }
                return loaded;
            }
            catch (Exception e)
            {
                SetWriteBarrier("hof_load_exception:" + e.GetType().Name);
                return null;
            }
        }

        private static ModeHHallOfFameEnvelopeDto CreateEmpty()
        {
            ModeHHallOfFameEnvelopeDto envelope = new ModeHHallOfFameEnvelopeDto();
            envelope.schemaVersion = ModeHConfig.CurrentSchemaVersion;
            envelope.signatureAlgorithmVersion = ModeHConfig.CurrentSignatureAlgorithmVersion;
            envelope.records = new List<ModeHHallOfFameRecordDto>();
            return envelope;
        }

        private static bool VerifyDigest(ModeHHallOfFameEnvelopeDto dto)
        {
            if (dto == null) return false;
            if (!ModeHCanonicalDigest.IsValidDigest(dto.payloadDigest)) return false;
            string computed;
            string error;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(dto, "payloadDigest", out computed, out error))
            {
                return false;
            }
            return string.Equals(computed, dto.payloadDigest, StringComparison.Ordinal);
        }

        private static void SetWriteBarrier(string reasonId)
        {
            lock (_lock)
            {
                _writeBarrier = true;
                _cache = null;
                _lastError = reasonId;
            }
        }

        #endregion

        #region 名人堂记录（跨 key 幂等插入）

        /// <summary>只读展示用：当前记录（按 createdUtc、hallOfFameId 稳定排序）。</summary>
        public static List<ModeHHallOfFameRecordDto> GetRecords()
        {
            ModeHHallOfFameEnvelopeDto envelope = _cache;
            if (envelope == null) envelope = LoadCurrent();
            List<ModeHHallOfFameRecordDto> result = new List<ModeHHallOfFameRecordDto>();
            if (envelope == null || envelope.records == null) return result;
            result.AddRange(envelope.records);
            result.Sort(CompareRecords);
            return result;
        }

        private static int CompareRecords(ModeHHallOfFameRecordDto a, ModeHHallOfFameRecordDto b)
        {
            string ac = a != null && a.createdUtc != null ? a.createdUtc : string.Empty;
            string bc = b != null && b.createdUtc != null ? b.createdUtc : string.Empty;
            int byTime = string.CompareOrdinal(ac, bc);
            if (byTime != 0) return byTime;
            string ai = a != null && a.hallOfFameId != null ? a.hallOfFameId : string.Empty;
            string bi = b != null && b.hallOfFameId != null ? b.hallOfFameId : string.Empty;
            return string.CompareOrdinal(ai, bi);
        }

        /// <summary>
        /// 按稳定 hallOfFameId 幂等插入一条记录（§17.8）。
        /// 已存在同 ID 时直接返回 true，不重复写入、不重新生成时间；
        /// 超过 32 条时按 (createdUtc, hallOfFameId) 删除最旧一条。
        /// </summary>
        public static bool StageRecordInsert(ModeHHallOfFameRecordDto record, out string error)
        {
            error = null;
            if (record == null || string.IsNullOrEmpty(record.hallOfFameId))
            {
                error = "hof_record_invalid";
                return false;
            }
            if (_writeBarrier)
            {
                error = "hof_write_barrier";
                return false;
            }
            if (_storeFaulted)
            {
                error = "hof_store_faulted";
                return false;
            }

            ModeHHallOfFameEnvelopeDto envelope = _cache;
            if (envelope == null) envelope = LoadCurrent();
            if (envelope == null)
            {
                error = _lastError != null ? _lastError : "hof_envelope_unavailable";
                return false;
            }
            if (envelope.records == null) envelope.records = new List<ModeHHallOfFameRecordDto>();

            for (int i = 0; i < envelope.records.Count; i++)
            {
                ModeHHallOfFameRecordDto existing = envelope.records[i];
                if (existing != null
                    && string.Equals(existing.hallOfFameId, record.hallOfFameId, StringComparison.Ordinal))
                {
                    return true; // 幂等：同 ID 已存在
                }
            }

            envelope.records.Add(record);
            envelope.records.Sort(CompareRecords);
            while (envelope.records.Count > ModeHConfig.MaxHallOfFameRecords)
            {
                envelope.records.RemoveAt(0);
            }

            return StageEnvelope(envelope, out error);
        }

        #endregion

        #region 生产认证缓存（四签名）

        /// <summary>
        /// 读取与当前四签名完全匹配且 overallPassed=true 的认证缓存；
        /// 任一签名变化、未通过或摘要不符都返回 null，要求重跑。
        /// </summary>
        public static ModeHProductionCertificationDto TryGetCertificationCache(
            string gameBuildSignature, string modBuildSignature, string contentCatalogSignature, int slotGeneration)
        {
            try
            {
                ModeHHallOfFameEnvelopeDto envelope = _cache;
                if (envelope == null) envelope = LoadCurrent();
                if (envelope == null) return null;

                ModeHCertificationCacheDto cache = envelope.productionCertificationCache;
                if (cache == null || cache.snapshot == null) return null;
                if (!string.Equals(cache.gameBuildSignature, gameBuildSignature, StringComparison.Ordinal)) return null;
                if (!string.Equals(cache.modBuildSignature, modBuildSignature, StringComparison.Ordinal)) return null;
                if (!string.Equals(cache.contentCatalogSignature, contentCatalogSignature, StringComparison.Ordinal))
                {
                    return null;
                }
                if (cache.slotGeneration != slotGeneration) return null;
                if (!cache.snapshot.overallPassed) return null;

                string computed;
                string error;
                if (!ModeHCanonicalDigest.TryComputeObjectDigest(cache.snapshot, null, out computed, out error))
                {
                    return null;
                }
                if (!string.Equals(computed, cache.snapshotDigest, StringComparison.Ordinal)) return null;

                return cache.snapshot;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>写入按四签名键控的认证缓存（只暂存，落盘由 coordinator 调度）。</summary>
        public static bool StageCertificationCache(
            ModeHProductionCertificationDto snapshot, int slotGeneration, out string error)
        {
            error = null;
            if (snapshot == null)
            {
                error = "hof_cache_snapshot_null";
                return false;
            }
            if (_writeBarrier || _storeFaulted)
            {
                error = _writeBarrier ? "hof_write_barrier" : "hof_store_faulted";
                return false;
            }

            ModeHHallOfFameEnvelopeDto envelope = _cache;
            if (envelope == null) envelope = LoadCurrent();
            if (envelope == null)
            {
                error = _lastError != null ? _lastError : "hof_envelope_unavailable";
                return false;
            }

            string digest;
            string digestError;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(snapshot, null, out digest, out digestError))
            {
                error = "hof_cache_digest_failed:" + digestError;
                return false;
            }

            ModeHCertificationCacheDto cache = new ModeHCertificationCacheDto();
            cache.gameBuildSignature = snapshot.gameBuildSignature;
            cache.modBuildSignature = snapshot.modBuildSignature;
            cache.contentCatalogSignature = snapshot.contentCatalogSignature;
            cache.slotGeneration = slotGeneration;
            cache.snapshot = snapshot;
            cache.snapshotDigest = digest;
            envelope.productionCertificationCache = cache;

            return StageEnvelope(envelope, out error);
        }

        /// <summary>作废认证缓存（诊断页的“强制重新认证”）。</summary>
        public static bool StageInvalidateCertificationCache(out string error)
        {
            error = null;
            ModeHHallOfFameEnvelopeDto envelope = _cache;
            if (envelope == null) envelope = LoadCurrent();
            if (envelope == null)
            {
                error = _lastError != null ? _lastError : "hof_envelope_unavailable";
                return false;
            }
            envelope.productionCertificationCache = null;
            return StageEnvelope(envelope, out error);
        }

        #endregion

        #region 写入

        private static bool StageEnvelope(ModeHHallOfFameEnvelopeDto envelope, out string error)
        {
            error = null;
            string gameSignature;
            string modSignature;
            string signatureError;
            if (!ModeHCanonicalDigest.TryGetGameBuildSignature(out gameSignature, out signatureError)
                || !ModeHCanonicalDigest.TryGetModBuildSignature(out modSignature, out signatureError))
            {
                error = "hof_signature_unavailable:" + signatureError;
                return false;
            }

            envelope.schemaVersion = ModeHConfig.CurrentSchemaVersion;
            envelope.signatureAlgorithmVersion = ModeHConfig.CurrentSignatureAlgorithmVersion;
            envelope.gameBuildSignature = gameSignature;
            envelope.modBuildSignature = modSignature;
            envelope.contentCatalogSignature = ModeHContentCatalog.ContentCatalogSignature;
            envelope.payloadDigest = null;

            string digest;
            string digestError;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(envelope, "payloadDigest", out digest, out digestError))
            {
                error = "hof_digest_failed:" + digestError;
                return false;
            }
            envelope.payloadDigest = digest;

            lock (_lock)
            {
                _pending = envelope;
                _pendingDigest = digest;
            }
            return true;
        }

        /// <summary>
        /// 写入 cache 并回读核对。本方法绝不调用 SavesSystem.SaveFile：
        /// 物理落盘的唯一调用点在 ModeHSaveFlushCoordinator。
        /// </summary>
        public static bool FlushPending()
        {
            ModeHHallOfFameEnvelopeDto pending;
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

                SavesSystem.Save<ModeHHallOfFameEnvelopeDto>(StorageKey, pending);

                ModeHHallOfFameEnvelopeDto readback = SavesSystem.Load<ModeHHallOfFameEnvelopeDto>(StorageKey);
                if (readback == null
                    || !string.Equals(readback.payloadDigest, expectedDigest, StringComparison.Ordinal)
                    || !VerifyDigest(readback))
                {
                    _storeFaulted = true;
                    _lastError = "hof_readback_mismatch";
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
                _lastError = "hof_flush_exception:" + e.GetType().Name;
                return false;
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
