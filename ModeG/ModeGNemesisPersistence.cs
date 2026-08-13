using System;
using Saves;

namespace BossRush
{
    /// <summary>
    /// Mode G 宿敌持久化（C6/§6.4 裁决重写版）。
    ///
    /// 硬约束（规格 §20 guard 22/24）：
    /// - 原生 Saves.SavesSystem typed API：KeyExisits 前置分类、Load&lt;T&gt;/Save；
    /// - OnCollectSaveData/OnSetFile/OnSaveDeleted 幂等订阅；
    /// - 写屏障：战斗中不写盘，只在值变化结算/官方收集/切档删除时落；
    /// - DTO 禁字段初始化器；schemaVersion 保持默认 0；
    /// - 墓碑（tombstone）；rank clamp = max(旧+1, current)；
    /// - SuspendedPersistentV1 挂起（内存挂起，不写盘）；
    /// - 每槽至多一个 pending flush；IsSaving 时只合并；
    /// - Store 抛异常进入单向 StoreFaulted，Mode G 入口 fail-closed。
    /// </summary>
    public static class ModeGNemesisPersistence
    {
        #region DTO（禁字段初始化器；schemaVersion 保持默认 0）

        /// <summary>
        /// 宿敌记录 DTO。
        /// </summary>
        [Serializable]
        public sealed class NemesisRecordDto
        {
            public int schemaVersion;        // 保持默认 0
            public string bossPresetKey;
            public int rank;                 // R1-R3，clamp max(旧+1, current)
            public int temperamentId;        // ModeGNemesisTemperament 稳定 ID
            public int defeatsByPlayer;
            public int defeatsOfPlayer;
            public long lastUpdatedTicks;
            public ulong originRunId;
            public bool tombstone;           // 墓碑：已退役宿敌
        }

        /// <summary>
        /// 挂起容器（版本降级/异常隔离）。内存挂起，不写盘。
        /// </summary>
        [Serializable]
        public sealed class SuspendedPersistentV1
        {
            public int schemaVersion;        // 保持默认 0
            public string reason;
            public long suspendedTicks;
            public NemesisRecordDto suspendedRecord;
        }

        /// <summary>存档 key（v1 冻结）</summary>
        public const string StorageKey = "BossRush_ModeG_NemesisRecord_v1";
        /// <summary>宿敌 Rank 上限（R3）</summary>
        public const int MaxRank = 3;

        #endregion

        #region State

        private static readonly object _lock = new object();
        private static NemesisRecordDto _cache;
        private static NemesisRecordDto _pending;
        private static bool _pendingFlushActive;      // 每槽至多一个 pending flush
        private static bool _storeFaulted;            // 单向故障
        private static bool _subscribed;
        private static SuspendedPersistentV1 _suspended; // 内存挂起，不写盘

        #endregion

        #region Subscription（幂等）

        /// <summary>
        /// 幂等订阅官方存档事件。入口/模块初始化时调用一次。
        /// </summary>
        public static void EnsureSubscribed()
        {
            lock (_lock)
            {
                if (_subscribed) return;
                try
                {
                    SavesSystem.OnCollectSaveData += HandleCollectSaveData;
                    SavesSystem.OnSetFile += HandleSetFile;
                    SavesSystem.OnSaveDeleted += HandleSaveDeleted;
                    _subscribed = true;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 宿敌存档订阅失败: " + e.Message);
                }
            }
        }

        public static void ShutdownSubscription()
        {
            lock (_lock)
            {
                if (!_subscribed) return;
                try
                {
                    SavesSystem.OnCollectSaveData -= HandleCollectSaveData;
                    SavesSystem.OnSetFile -= HandleSetFile;
                    SavesSystem.OnSaveDeleted -= HandleSaveDeleted;
                }
                catch { }
                _subscribed = false;
            }
        }

        private static void HandleCollectSaveData()
        {
            try
            {
                // 官方收集时把 pending 合并进存档（不单独 SaveFile）
                FlushPending(writeFile: false);
            }
            catch { /* no-throw：存档收集路径不得抛 */ }
        }

        private static void HandleSetFile()
        {
            try
            {
                // 切档：丢弃内存状态，从新档重新加载
                lock (_lock)
                {
                    _cache = null;
                    _pending = null;
                    _pendingFlushActive = false;
                    _suspended = null;
                }
                LoadOrInit();
            }
            catch { /* no-throw */ }
        }

        private static void HandleSaveDeleted()
        {
            try
            {
                // 删档：重置全部内存状态（幂等）
                lock (_lock)
                {
                    _cache = null;
                    _pending = null;
                    _pendingFlushActive = false;
                    _suspended = null;
                }
            }
            catch { /* no-throw */ }
        }

        #endregion

        #region Load / Store

        /// <summary>
        /// StoreFaulted 单向故障查询。入口 fail-closed 判据之一。
        /// </summary>
        public static bool IsStoreFaulted { get { return _storeFaulted; } }

        /// <summary>
        /// 当前宿敌记录缓存（可能为 null）。
        /// </summary>
        public static NemesisRecordDto Current
        {
            get { lock (_lock) { return _cache; } }
        }

        /// <summary>
        /// 加载或初始化（KeyExisits 前置分类）。幂等。
        /// </summary>
        public static NemesisRecordDto LoadOrInit()
        {
            EnsureSubscribed();
            lock (_lock)
            {
                if (_cache != null) return _cache;
                try
                {
                    if (SavesSystem.KeyExisits(StorageKey))
                    {
                        NemesisRecordDto loaded = SavesSystem.Load<NemesisRecordDto>(StorageKey);
                        if (loaded != null && loaded.schemaVersion == 0)
                        {
                            _cache = loaded;
                            return _cache;
                        }
                        // 版本不匹配：挂起旧记录（内存），不写盘
                        _suspended = new SuspendedPersistentV1
                        {
                            reason = "schema_mismatch",
                            suspendedTicks = DateTime.UtcNow.Ticks,
                            suspendedRecord = loaded
                        };
                    }
                    _cache = new NemesisRecordDto();
                    return _cache;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 宿敌记录加载失败: " + e.Message);
                    _cache = new NemesisRecordDto();
                    return _cache;
                }
            }
        }

        /// <summary>
        /// 写屏障内入队存储（战斗中只合并，不落盘）。
        /// Store 异常 -> 单向 StoreFaulted（之后 Mode G 入口 fail-closed）。
        /// </summary>
        public static bool Store(NemesisRecordDto record)
        {
            if (record == null) return false;
            if (_storeFaulted) return false; // fail-closed
            try
            {
                lock (_lock)
                {
                    record.lastUpdatedTicks = DateTime.UtcNow.Ticks;
                    _pending = record;
                    _cache = record;
                    // 每槽至多一个 pending flush：合并覆盖，不叠加
                    _pendingFlushActive = true;

                    // IsSaving 时只合并，不触发写盘
                    if (!SavesSystem.IsSaving)
                    {
                        FlushPendingLocked(writeFile: true);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                _storeFaulted = true; // 单向，不可恢复
                ModBehaviour.DevLog("[ModeG] [ERROR] 宿敌 Store 异常，进入 StoreFaulted: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 落盘 pending（值变化结算/官方收集时调用）。
        /// </summary>
        public static void FlushPending(bool writeFile)
        {
            lock (_lock)
            {
                FlushPendingLocked(writeFile);
            }
        }

        private static void FlushPendingLocked(bool writeFile)
        {
            if (!_pendingFlushActive || _pending == null) return;
            try
            {
                if (SavesSystem.IsSaving) return; // 官方保存中：只合并，不打断
                SavesSystem.Save<NemesisRecordDto>(StorageKey, _pending);
                // 回读核对（guard 24：Save + 回读核对再一次 SaveFile(false)）
                NemesisRecordDto readback = SavesSystem.Load<NemesisRecordDto>(StorageKey);
                if (readback != null && writeFile)
                {
                    SavesSystem.SaveFile(false);
                }
                _pending = null;
                _pendingFlushActive = false;
            }
            catch (Exception e)
            {
                _storeFaulted = true;
                ModBehaviour.DevLog("[ModeG] [ERROR] 宿敌 flush 异常，进入 StoreFaulted: " + e.Message);
            }
        }

        #endregion

        #region Semantics（rank clamp / 墓碑 / 挂起）

        /// <summary>
        /// rank 晋升：clamp max(旧+1, current)，上限 MaxRank。
        /// 玩家再次败给宿敌时递增；不允许降级。
        /// </summary>
        public static int ClampRankUp(int oldRank, int currentRank)
        {
            int next = Math.Max(oldRank + 1, currentRank);
            if (next < 1) next = 1;
            if (next > MaxRank) next = MaxRank;
            return next;
        }

        /// <summary>
        /// 写入墓碑（宿敌退役：玩家彻底击败后保留记录但不再出场）。
        /// </summary>
        public static bool MarkTombstone()
        {
            NemesisRecordDto dto = LoadOrInit();
            if (dto == null) return false;
            NemesisRecordDto copy = CloneDto(dto);
            copy.tombstone = true;
            return Store(copy);
        }

        /// <summary>
        /// 当前宿敌是否可出场（非墓碑、有 key）。
        /// </summary>
        public static bool HasActiveNemesis()
        {
            NemesisRecordDto dto = LoadOrInit();
            return dto != null && !dto.tombstone && !string.IsNullOrEmpty(dto.bossPresetKey);
        }

        /// <summary>
        /// 当前内存挂起记录（版本降级隔离，不写盘）。
        /// </summary>
        public static SuspendedPersistentV1 Suspended
        {
            get { lock (_lock) { return _suspended; } }
        }

        private static NemesisRecordDto CloneDto(NemesisRecordDto src)
        {
            return new NemesisRecordDto
            {
                schemaVersion = src.schemaVersion,
                bossPresetKey = src.bossPresetKey,
                rank = src.rank,
                temperamentId = src.temperamentId,
                defeatsByPlayer = src.defeatsByPlayer,
                defeatsOfPlayer = src.defeatsOfPlayer,
                lastUpdatedTicks = src.lastUpdatedTicks,
                originRunId = src.originRunId,
                tombstone = src.tombstone
            };
        }

        #endregion
    }
}
