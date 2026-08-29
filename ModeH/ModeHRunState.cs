using System;

namespace BossRush
{
    /// <summary>
    /// Mode H 单季内存 owner（设计提案 §25.1、§21.2）。
    ///
    /// 硬约束：
    /// - runOwnerToken 只存在内存，每次 host 恢复生成新值，禁止写入任何 DTO；
    /// - 所有延迟回调都必须先比较 runId / sceneGeneration / ownerToken，失配即返回；
    /// - lifecycle 只能由 ModeHStateMachine.TryTransition 修改；
    /// - 同一 event token 第二次提交返回已处理结果，不重复接力、伤病或奖励。
    /// </summary>
    public sealed class ModeHRunState
    {
        #region 字段

        private static long _nextOwnerToken = 1;

        private readonly object _lock = new object();
        private readonly System.Collections.Generic.HashSet<string> _appliedEventTokens =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        private ModeHLifecycle _lifecycle;
        private int _stateSequence;
        private int _matchIndex;
        private int _technicalRetrySequence;
        private ModeHLifecycle _recoveryOriginalLifecycle;
        private ModeHLifecycle _recoveryResumeTarget;

        #endregion

        #region 构造

        /// <summary>创建一份内存 run owner。ownerToken 每次都是新值。</summary>
        public ModeHRunState(string runId, long runSeed, string sceneName, int sceneGeneration)
        {
            RunId = runId;
            RunSeed = runSeed;
            SceneName = sceneName;
            SceneGeneration = sceneGeneration;
            OwnerToken = System.Threading.Interlocked.Increment(ref _nextOwnerToken);
            _lifecycle = ModeHLifecycle.None;
            _stateSequence = 0;
            _matchIndex = 0;
            _technicalRetrySequence = 0;
            _recoveryOriginalLifecycle = ModeHLifecycle.Unknown;
            _recoveryResumeTarget = ModeHLifecycle.Unknown;
        }

        #endregion

        #region 只读

        /// <summary>赛季运行 ID（进入持久 DTO）。</summary>
        public string RunId { get; private set; }

        /// <summary>赛季随机种子。</summary>
        public long RunSeed { get; private set; }

        /// <summary>目标场景名。</summary>
        public string SceneName { get; private set; }

        /// <summary>场景 generation。</summary>
        public int SceneGeneration { get; private set; }

        /// <summary>内存 owner token，禁止持久化。</summary>
        public long OwnerToken { get; private set; }

        /// <summary>当前 lifecycle。</summary>
        public ModeHLifecycle Lifecycle { get { return _lifecycle; } }

        /// <summary>派生比赛相位。</summary>
        public ModeHMatchPhase MatchPhase { get { return ModeHStateModel.GetMatchPhase(_lifecycle); } }

        /// <summary>状态序号。</summary>
        public int StateSequence { get { return _stateSequence; } }

        /// <summary>当前比赛编号（0 表示尚未开赛）。</summary>
        public int MatchIndex { get { return _matchIndex; } }

        /// <summary>同场技术重试序号。</summary>
        public int TechnicalRetrySequence { get { return _technicalRetrySequence; } }

        /// <summary>故障源状态。</summary>
        public ModeHLifecycle RecoveryOriginalLifecycle { get { return _recoveryOriginalLifecycle; } }

        /// <summary>恢复目标状态。</summary>
        public ModeHLifecycle RecoveryResumeTarget { get { return _recoveryResumeTarget; } }

        #endregion

        #region owner / 回调门控

        /// <summary>延迟回调必须先校验 owner token。</summary>
        public bool IsOwnerTokenValid(long token)
        {
            return token != 0 && token == OwnerToken;
        }

        /// <summary>延迟回调的完整门控：owner token + runId + sceneGeneration。</summary>
        public bool IsCallbackValid(long token, string runId, int sceneGeneration)
        {
            if (!IsOwnerTokenValid(token)) return false;
            if (!string.Equals(runId, RunId, StringComparison.Ordinal)) return false;
            return sceneGeneration == SceneGeneration;
        }

        /// <summary>场景 generation 推进（同一 run 内切图）。</summary>
        public void UpdateSceneGeneration(int sceneGeneration)
        {
            SceneGeneration = sceneGeneration;
        }

        #endregion

        #region 状态推进（只由 ModeHStateMachine 调用）

        /// <summary>
        /// 应用一次已被状态机批准的转换。返回转换记录，供写入 Season payload。
        /// </summary>
        internal ModeHTransitionRecord ApplyTransition(ModeHLifecycle expected, ModeHLifecycle next, string reason)
        {
            lock (_lock)
            {
                if (_lifecycle != expected) return null;

                if (next == ModeHLifecycle.Recovering || next == ModeHLifecycle.ErrorRecoveryPending)
                {
                    if (_recoveryOriginalLifecycle == ModeHLifecycle.Unknown
                        || _lifecycle != ModeHLifecycle.Recovering)
                    {
                        // 第一次进入恢复通道时冻结故障源与恢复目标
                        if (_lifecycle != ModeHLifecycle.Recovering
                            && _lifecycle != ModeHLifecycle.ErrorRecoveryPending)
                        {
                            _recoveryOriginalLifecycle = _lifecycle;
                            _recoveryResumeTarget = _lifecycle;
                        }
                    }
                }
                else if (next != ModeHLifecycle.Suspended)
                {
                    // 恢复完成后清空恢复元数据
                    _recoveryOriginalLifecycle = ModeHLifecycle.Unknown;
                    _recoveryResumeTarget = ModeHLifecycle.Unknown;
                }

                _lifecycle = next;
                _stateSequence++;

                ModeHTransitionRecord record = new ModeHTransitionRecord();
                record.From = expected;
                record.To = next;
                record.StateSequence = _stateSequence;
                record.MatchIndex = _matchIndex;
                record.TechnicalRetrySequence = _technicalRetrySequence;
                record.RunId = RunId;
                record.RecoveryOriginalLifecycle = _recoveryOriginalLifecycle;
                record.RecoveryResumeTarget = _recoveryResumeTarget;
                record.Reason = reason;
                return record;
            }
        }

        /// <summary>
        /// 打开下一场：只有真正进入 MatchBrief 时才推进 matchIndex，且必须小于等于六场。
        /// </summary>
        public bool TryAdvanceMatchIndex(out string failureReasonId)
        {
            failureReasonId = null;
            lock (_lock)
            {
                int next = _matchIndex + 1;
                if (next > ModeHConfig.SeasonMatchCount)
                {
                    failureReasonId = "match_index_overflow";
                    return false;
                }
                _matchIndex = next;
                _technicalRetrySequence = 0;
                return true;
            }
        }

        /// <summary>从存档恢复时直接设置比赛编号。</summary>
        public void RestoreMatchIndex(int matchIndex, int technicalRetrySequence, int stateSequence)
        {
            lock (_lock)
            {
                _matchIndex = matchIndex;
                _technicalRetrySequence = technicalRetrySequence;
                _stateSequence = stateSequence;
            }
        }

        /// <summary>从存档恢复 lifecycle 与恢复元数据（不经过转换表）。</summary>
        public void RestoreLifecycle(
            ModeHLifecycle lifecycle,
            ModeHLifecycle recoveryOriginal,
            ModeHLifecycle recoveryResumeTarget)
        {
            lock (_lock)
            {
                _lifecycle = lifecycle;
                _recoveryOriginalLifecycle = recoveryOriginal;
                _recoveryResumeTarget = recoveryResumeTarget;
            }
        }

        /// <summary>同场技术重试计数 +1（不推进 matchIndex，绝不判负）。</summary>
        public int IncrementTechnicalRetry()
        {
            lock (_lock)
            {
                _technicalRetrySequence++;
                return _technicalRetrySequence;
            }
        }

        /// <summary>
        /// 重置同场技术重试预算。**只给玩家在恢复壳里手动「同场重开」用**：
        /// 自动预算耗尽后玩家主动再来一次，应当拿到全新的预算与新的计划候选
        /// （计划缓存判据含 technicalRetrySequence），否则重开按钮会立刻又被判定超预算，
        /// 并且复用刚刚失败的那份计划。不推进 matchIndex，绝不判负（§17.4）。
        /// </summary>
        public void ResetTechnicalRetry()
        {
            lock (_lock)
            {
                _technicalRetrySequence = 0;
            }
        }

        #endregion

        #region 事件 token CAS

        /// <summary>
        /// 提交一个事件 token。首次提交返回 true；同一 token 第二次提交返回 false，
        /// 调用方据此返回“已处理结果”，不重复接力、伤病、结算或奖励。
        /// </summary>
        public bool TryApplyEventToken(string eventTokenId)
        {
            if (string.IsNullOrEmpty(eventTokenId)) return false;
            lock (_lock)
            {
                return _appliedEventTokens.Add(eventTokenId);
            }
        }

        /// <summary>该 token 是否已被提交。</summary>
        public bool HasEventToken(string eventTokenId)
        {
            if (string.IsNullOrEmpty(eventTokenId)) return false;
            lock (_lock)
            {
                return _appliedEventTokens.Contains(eventTokenId);
            }
        }

        /// <summary>从存档恢复已提交 token 集合。</summary>
        public void RestoreEventTokens(System.Collections.Generic.IList<string> tokens)
        {
            lock (_lock)
            {
                _appliedEventTokens.Clear();
                if (tokens == null) return;
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (!string.IsNullOrEmpty(tokens[i])) _appliedEventTokens.Add(tokens[i]);
                }
            }
        }

        /// <summary>导出已提交 token 集合（写入 Season payload）。</summary>
        public System.Collections.Generic.List<string> ExportEventTokens()
        {
            lock (_lock)
            {
                return new System.Collections.Generic.List<string>(_appliedEventTokens);
            }
        }

        #endregion

        #region 持久化投影

        /// <summary>把内存状态投影成持久 DTO；owner token 不写入。</summary>
        public ModeHRunStateDto ToDto()
        {
            lock (_lock)
            {
                ModeHRunStateDto dto = new ModeHRunStateDto();
                dto.schemaVersion = ModeHConfig.CurrentSchemaVersion;
                dto.runId = RunId;
                dto.runSeed = RunSeed;
                dto.lifecycle = (int)_lifecycle;
                dto.stateSequence = _stateSequence;
                dto.sceneName = SceneName;
                dto.sceneGeneration = SceneGeneration;
                dto.matchIndex = _matchIndex;
                dto.phaseDeadlineUtc = string.Empty;
                dto.technicalRetrySequence = _technicalRetrySequence;
                dto.preMatchSnapshotHash = string.Empty;
                dto.recoveryOriginalLifecycle = (int)_recoveryOriginalLifecycle;
                dto.recoveryResumeTarget = (int)_recoveryResumeTarget;
                return dto;
            }
        }

        /// <summary>从持久 DTO 重建内存 run owner（生成新的 owner token）。</summary>
        public static ModeHRunState FromDto(ModeHRunStateDto dto)
        {
            if (dto == null) return null;
            ModeHRunState state = new ModeHRunState(dto.runId, dto.runSeed, dto.sceneName, dto.sceneGeneration);
            state.RestoreMatchIndex(dto.matchIndex, dto.technicalRetrySequence, dto.stateSequence);
            state.RestoreLifecycle(
                ModeHStateModel.ToLifecycle(dto.lifecycle),
                ModeHStateModel.ToLifecycle(dto.recoveryOriginalLifecycle),
                ModeHStateModel.ToLifecycle(dto.recoveryResumeTarget));
            return state;
        }

        #endregion
    }
}
