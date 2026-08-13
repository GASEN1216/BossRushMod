using System;
using Saves;

namespace BossRush
{
    /// <summary>
    /// Mode G 个人记录持久化（§10 裁决重写版）。
    ///
    /// 硬约束（规格 §20 guard 23/24）：
    /// - 独立 v1 key：BossRush_ModeG_Profile_v1；
    /// - 原生 Saves.SavesSystem typed API；KeyExisits 前置分类；
    /// - DTO 禁字段初始化器；schemaVersion 保持默认 0；
    /// - battleResultToken 只记录 Victory/Defeat 终局（CAS 语义由 RunState 保证）；
    /// - contractStreakBreakToken 仅在有效 ManualExit 时清 streak；
    /// - Victory 后记录保留（不清零历史）；
    /// - 不从 profile 发物品/货币/加成（纯展示/匹配数据）；
    /// - 每槽至多一个 pending flush；IsSaving 时只合并；
    /// - Store 异常单向 StoreFaulted，入口 fail-closed。
    /// </summary>
    public static class ModeGProfilePersistence
    {
        #region DTO（禁字段初始化器；schemaVersion 保持默认 0）

        [Serializable]
        public sealed class ProfileDto
        {
            public int schemaVersion;        // 保持默认 0
            public int totalRuns;
            public int totalVictories;
            public int totalDefeats;
            public int bestWaveReached;
            public float bestClearTimeSeconds;
            public int totalBossKills;
            public int totalNemesisDefeated;
            public int contractStreak;              // 契约连胜（ManualExit 有效清除）
            public long lastUpdatedTicks;
            public string lastBattleResultToken;    // victory_/defeat_ + runId hex
        }

        /// <summary>存档 key（v1 冻结，独立于宿敌 key）</summary>
        public const string StorageKey = "BossRush_ModeG_Profile_v1";

        #endregion

        #region State

        private static readonly object _lock = new object();
        private static ProfileDto _cache;
        private static ProfileDto _pending;
        private static bool _pendingFlushActive;
        private static bool _storeFaulted;
        private static bool _subscribed;

        #endregion

        #region Subscription（幂等）

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
                    ModBehaviour.DevLog("[ModeG] [WARNING] 个人记录存档订阅失败: " + e.Message);
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
            try { FlushPending(writeFile: false); } catch { /* no-throw */ }
        }

        private static void HandleSetFile()
        {
            try
            {
                lock (_lock)
                {
                    _cache = null;
                    _pending = null;
                    _pendingFlushActive = false;
                }
                LoadOrInit();
            }
            catch { /* no-throw */ }
        }

        private static void HandleSaveDeleted()
        {
            try
            {
                lock (_lock)
                {
                    _cache = null;
                    _pending = null;
                    _pendingFlushActive = false;
                }
            }
            catch { /* no-throw */ }
        }

        #endregion

        #region Load / Store

        public static bool IsStoreFaulted { get { return _storeFaulted; } }

        public static ProfileDto Current
        {
            get { lock (_lock) { return _cache; } }
        }

        /// <summary>
        /// 加载或初始化（KeyExisits 前置分类）。幂等。
        /// </summary>
        public static ProfileDto LoadOrInit()
        {
            EnsureSubscribed();
            lock (_lock)
            {
                if (_cache != null) return _cache;
                try
                {
                    if (SavesSystem.KeyExisits(StorageKey))
                    {
                        ProfileDto loaded = SavesSystem.Load<ProfileDto>(StorageKey);
                        if (loaded != null && loaded.schemaVersion == 0)
                        {
                            _cache = loaded;
                            return _cache;
                        }
                        // schema 不匹配：重置（profile 无挂起语义，直接新建）
                    }
                    _cache = new ProfileDto();
                    return _cache;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 个人记录加载失败: " + e.Message);
                    _cache = new ProfileDto();
                    return _cache;
                }
            }
        }

        /// <summary>
        /// 写屏障内入队存储。Store 异常 -> 单向 StoreFaulted。
        /// </summary>
        public static bool Store(ProfileDto record)
        {
            if (record == null) return false;
            if (_storeFaulted) return false;
            try
            {
                lock (_lock)
                {
                    record.lastUpdatedTicks = DateTime.UtcNow.Ticks;
                    _pending = record;
                    _cache = record;
                    _pendingFlushActive = true;

                    if (!SavesSystem.IsSaving)
                    {
                        FlushPendingLocked(writeFile: true);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                _storeFaulted = true;
                ModBehaviour.DevLog("[ModeG] [ERROR] 个人记录 Store 异常，进入 StoreFaulted: " + e.Message);
                return false;
            }
        }

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
                if (SavesSystem.IsSaving) return;
                SavesSystem.Save<ProfileDto>(StorageKey, _pending);
                ProfileDto readback = SavesSystem.Load<ProfileDto>(StorageKey);
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
                ModBehaviour.DevLog("[ModeG] [ERROR] 个人记录 flush 异常，进入 StoreFaulted: " + e.Message);
            }
        }

        #endregion

        #region Queries

        /// <summary>
        /// 是否有胜利记录（决定 runFormat：RematchMix / FirstClearNarrative）。
        /// 纯读取，不发任何物品/货币/加成。
        /// </summary>
        public static bool HasAnyVictory()
        {
            ProfileDto dto = LoadOrInit();
            return dto != null && dto.totalVictories > 0;
        }

        #endregion

        #region Record

        /// <summary>
        /// 记录一局结果（终局时调用一次；battleResultToken 幂等防重）。
        /// Victory 后历史记录保留，不清零。
        /// </summary>
        public static void RecordRun(ModeGBattleResult result, int waveReached,
            float clearTimeSeconds, int bossKills, bool nemesisDefeated, string battleResultToken)
        {
            try
            {
                ProfileDto dto = LoadOrInit();
                if (dto == null) dto = new ProfileDto();

                // battleResultToken 幂等：同一 token 不重复记账
                string token = battleResultToken ?? string.Empty;
                if (token.Length > 0 && dto.lastBattleResultToken == token) return;

                ProfileDto copy = CloneDto(dto);
                copy.totalRuns++;
                if (result == ModeGBattleResult.Victory)
                {
                    copy.totalVictories++;
                    if (copy.bestClearTimeSeconds <= 0f || clearTimeSeconds < copy.bestClearTimeSeconds)
                    {
                        copy.bestClearTimeSeconds = clearTimeSeconds;
                    }
                }
                else if (result == ModeGBattleResult.Defeat)
                {
                    copy.totalDefeats++;
                }

                if (waveReached > copy.bestWaveReached) copy.bestWaveReached = waveReached;
                copy.totalBossKills += bossKills;
                if (nemesisDefeated) copy.totalNemesisDefeated++;
                copy.lastBattleResultToken = token;

                Store(copy);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] RecordRun 失败: " + e.Message);
            }
        }

        /// <summary>
        /// contractStreakBreakToken：有效 ManualExit 清契约连胜（一次）。
        /// </summary>
        public static void ClearContractStreakOnManualExit()
        {
            try
            {
                ProfileDto dto = LoadOrInit();
                if (dto == null || dto.contractStreak == 0) return;
                ProfileDto copy = CloneDto(dto);
                copy.contractStreak = 0;
                Store(copy);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] ClearContractStreakOnManualExit 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 契约达成时递增连胜。
        /// </summary>
        public static void IncrementContractStreak()
        {
            try
            {
                ProfileDto dto = LoadOrInit();
                if (dto == null) dto = new ProfileDto();
                ProfileDto copy = CloneDto(dto);
                copy.contractStreak++;
                Store(copy);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] IncrementContractStreak 失败: " + e.Message);
            }
        }

        private static ProfileDto CloneDto(ProfileDto src)
        {
            return new ProfileDto
            {
                schemaVersion = src.schemaVersion,
                totalRuns = src.totalRuns,
                totalVictories = src.totalVictories,
                totalDefeats = src.totalDefeats,
                bestWaveReached = src.bestWaveReached,
                bestClearTimeSeconds = src.bestClearTimeSeconds,
                totalBossKills = src.totalBossKills,
                totalNemesisDefeated = src.totalNemesisDefeated,
                contractStreak = src.contractStreak,
                lastUpdatedTicks = src.lastUpdatedTicks,
                lastBattleResultToken = src.lastBattleResultToken
            };
        }

        #endregion
    }
}
