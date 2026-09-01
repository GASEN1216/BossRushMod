// ============================================================================
// ModeHStakeJournalPersistence.cs - 真实仓库抵押 journal 的存档读写
// ============================================================================
// 为什么单独一个 key（而不是塞进 Season）：
//   ModeHRuntimeGates.InitializeRiskForSlot 在 OnSetFile 之后要做**轻量**风险扫描，
//   契约明确写着「只调 KeyExisits 并读取 stake journal 的 raw envelope header，
//   不加载 Season / HallOfFame / bundle / JSON / 候选池或完整 journal payload」。
//   把 journal 塞进 Season 会让那次扫描被迫读整个赛季，破坏该契约。
//   因此 journal 有自己的 v1 key：BossRush_ModeH_StakeJournal_v1。
//
// header 兼容契约（**不可破坏**）：
//   同一个 key 必须既能反序列化成 ModeHStakeJournalHeaderDto（风险扫描用），
//   也能反序列化成完整 ModeHStakeJournalDto（恢复流程用）。前者是后者的字段子集，
//   字段名逐字一致。给 journal 增删字段时不得改动 header 的 7 个字段名。
//
// 硬约束（照 ModeHProfilePersistence 的同一套纪律）：
//   - 原生 Saves.SavesSystem typed API，KeyExisits 前置分类；
//   - 每次只写一个完整 ModeHStakeJournalDto，禁止拆成多次 Save；
//   - 写 cache 后必须 Load 回读并核对 canonical payloadDigest；
//   - 未知 schemaVersion / 不可读 payload / 摘要不符一律进入写入保护，
//     **绝不覆盖旧值、绝不删除 journal**（那是玩家真实物品的唯一凭证）；
//   - 本类绝不调用 SavesSystem.SaveFile：物理落盘唯一在 ModeHSaveFlushCoordinator。
//
// 与 Season 的一个关键差异：
//   journal 不写 gameBuildSignature / contentCatalogSignature。押品事务只认
//   modBuildSignature（journal 自己在 TryPrepare 里已经写过），游戏版本升级
//   不该让玩家的未结算押品变成不可读——那会把可恢复的事务变成人工介入。
// ============================================================================

using System;
using Saves;

namespace BossRush
{
    /// <summary>Mode H 抵押 journal 存档读写。单 slot 单 active journal。</summary>
    public static class ModeHStakeJournalPersistence
    {
        #region 状态

        private static readonly object _lock = new object();
        private static bool _subscribed;
        private static ModeHStakeJournalDto _cache;
        private static ModeHStakeJournalDto _pending;
        private static string _pendingDigest;
        private static bool _storeFaulted;
        private static bool _writeBarrier;
        private static string _lastError;

        #endregion

        #region 只读

        /// <summary>存档 key。</summary>
        public static string StorageKey { get { return ModeHConfig.StakeJournalStorageKey; } }

        /// <summary>store 是否已进入单向故障状态。</summary>
        public static bool IsStoreFaulted { get { return _storeFaulted; } }

        /// <summary>当前 key 是否处于写入保护。</summary>
        public static bool IsWriteBarrier { get { return _writeBarrier; } }

        /// <summary>最后一次失败原因。</summary>
        public static string LastError { get { return _lastError; } }

        /// <summary>当前缓存的 journal（可能为 null = 该槽从未押过真实物品）。</summary>
        public static ModeHStakeJournalDto Current { get { return _cache; } }

        /// <summary>是否存在尚未落盘的写入。</summary>
        public static bool HasPendingWrite { get { return _pending != null; } }

        #endregion

        #region 订阅（幂等）

        /// <summary>
        /// 幂等订阅存档收集事件。
        ///
        /// **只订 OnCollectSaveData**：OnSetFile / OnSaveDeleted 由
        /// ModeHProfilePersistence 统一处理（它在那两个 handler 里已经调了
        /// ModeHRuntimeGates 的复位与风险重扫），本类只需要在切槽时被动清缓存，
        /// 由 ClearCache/ResetStaticCaches 经那条路径带到。
        /// 重复订阅同一个 SavesSystem 事件会让 journal 被写两次。
        /// </summary>
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
            }
            catch (Exception e)
            {
                _lastError = "journal_subscribe_failed:" + e.GetType().Name;
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

        #endregion

        #region 读取

        /// <summary>
        /// 读取当前槽的 journal。key 不存在返回 null（该槽从未押过真实物品）；
        /// 版本不兼容 / 摘要不符 / 不可读一律进入写入保护并返回 null——
        /// 此时 RecomputeSlotConsistency 会因读不到而保持押品禁用，赛季照常用虚拟筹码跑。
        /// </summary>
        public static ModeHStakeJournalDto LoadCurrent()
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

                ModeHStakeJournalDto loaded = SavesSystem.Load<ModeHStakeJournalDto>(StorageKey);
                if (loaded == null)
                {
                    SetWriteBarrier("journal_payload_null");
                    return null;
                }
                if (loaded.schemaVersion != ModeHConfig.CurrentSchemaVersion
                    || loaded.signatureAlgorithmVersion != ModeHConfig.CurrentSignatureAlgorithmVersion)
                {
                    SetWriteBarrier("journal_schema_incompatible");
                    return null;
                }
                if (!VerifyDigest(loaded))
                {
                    SetWriteBarrier("journal_digest_mismatch");
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
                SetWriteBarrier("journal_load_exception:" + e.GetType().Name);
                return null;
            }
        }

        private static bool VerifyDigest(ModeHStakeJournalDto dto)
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
                // **不清 _cache**：与 Season 的差异点。journal 是玩家真实物品的凭证，
                // 读不动的时候把它从内存里抹掉会让恢复面板连"有过这笔事务"都看不见。
                _lastError = reasonId;
            }
            ModeHRuntimeGates.SetExternalAssetRiskBlocked(true, reasonId);
        }

        #endregion

        #region 写入

        /// <summary>
        /// 暂存一个完整 journal payload。会补齐 schema 并重算 payloadDigest。
        /// 写入保护或 store 故障时拒绝。
        /// </summary>
        public static bool StageWrite(ModeHStakeJournalDto dto, out string error)
        {
            error = null;
            if (dto == null)
            {
                error = "journal_stage_null";
                return false;
            }
            if (_writeBarrier)
            {
                error = "journal_write_barrier";
                return false;
            }
            if (_storeFaulted)
            {
                error = "journal_store_faulted";
                return false;
            }

            dto.schemaVersion = ModeHConfig.CurrentSchemaVersion;
            dto.signatureAlgorithmVersion = ModeHConfig.CurrentSignatureAlgorithmVersion;
            dto.payloadDigest = null;

            string digest;
            string digestError;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(dto, "payloadDigest", out digest, out digestError))
            {
                error = "journal_digest_failed:" + digestError;
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
            ModeHStakeJournalDto pending;
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

                SavesSystem.Save<ModeHStakeJournalDto>(StorageKey, pending);

                ModeHStakeJournalDto readback = SavesSystem.Load<ModeHStakeJournalDto>(StorageKey);
                if (readback == null
                    || !string.Equals(readback.payloadDigest, expectedDigest, StringComparison.Ordinal))
                {
                    _storeFaulted = true;
                    _lastError = "journal_readback_mismatch";
                    return false;
                }
                if (!VerifyDigest(readback))
                {
                    _storeFaulted = true;
                    _lastError = "journal_readback_digest_invalid";
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
                _lastError = "journal_flush_exception:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>切槽时清空内存缓存（不删除磁盘上的 journal）。</summary>
        public static void ClearCache()
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
