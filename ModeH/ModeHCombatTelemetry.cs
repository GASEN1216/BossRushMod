using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace BossRush
{
    /// <summary>本场终局结论。</summary>
    internal sealed class ModeHMatchResult
    {
        /// <summary>结果 token（CAS 唯一）。</summary>
        public string ResultToken;
        /// <summary>胜负。</summary>
        public ModeHMatchOutcome Outcome;
        /// <summary>是否 180 秒超时判负。</summary>
        public bool Timeout;
        /// <summary>触发的胆怯类型（空表示无）。</summary>
        public string CowardiceType;
        /// <summary>特殊击杀标签（空表示无）。</summary>
        public string SpecialKillTag;
        /// <summary>特殊击杀归属的 profileId。</summary>
        public string SpecialKillProfileId;
        /// <summary>终局时的已用秒数。</summary>
        public float ElapsedSeconds;
    }

    /// <summary>
    /// Mode H 战斗遥测（设计提案 §17.4、§19.5、§25.1）。
    ///
    /// 冻结契约：
    /// - `ModeHFighterDownToken` 是唯一规范倒地事件，每个
    ///   `participantId + matchIndex` 至多一次；
    /// - `ModeHBattleResultToken` 由 `Interlocked` CAS 保证唯一，
    ///   两个终局事件同序号时按先 CAS 者；无法区分时判玩家失败；
    /// - 只有本场从未实际踏入擂台的选手才算完整休息；登场一次即不算；
    /// - 180 秒到时判玩家失败，不补伤害、不伪造击倒；
    /// - 逃跑型胆怯视为整队弃赛，立即失败且不触发接力；
    /// - `specialKillTag` 优先级：core -> relay -> last stand -> x3+ 胜利。
    ///
    /// 本类只采集与判定，不写存档、不发奖励；持久化由 `MatchSettling` 单批提交。
    /// </summary>
    internal sealed class ModeHCombatTelemetry : IModeHTelemetrySink
    {
        #region 状态

        private readonly HashSet<string> _downTokens = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _enteredProfileIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _lastKnownHealthFraction =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly List<ModeHParticipantRef> _liveEnemies = new List<ModeHParticipantRef>();

        private int _matchIndex;
        private long _runSeed;
        private float _elapsedSeconds;
        private int _resultClaimed;
        private ModeHMatchResult _result;

        private ModeHParticipantRef _activeFighter;
        private string _pendingDownProfileId;
        private bool _relayConsumed;
        private string _lastSpecialKillTag;
        private string _lastSpecialKillProfileId;
        private bool _highThreatCoreKilled;
        private string _highThreatCoreStableKey;

        #endregion

        #region 只读

        /// <summary>本场已用秒数。</summary>
        public float ElapsedSeconds { get { return _elapsedSeconds; } }

        /// <summary>本场剩余秒数（不为负）。</summary>
        public float RemainingSeconds
        {
            get
            {
                float remaining = ModeHConfig.MatchDurationSeconds - _elapsedSeconds;
                return remaining > 0f ? remaining : 0f;
            }
        }

        /// <summary>终局结论；未结束时为 null。</summary>
        public ModeHMatchResult Result { get { return _result; } }

        /// <summary>本场是否已锁定终局。</summary>
        public bool HasResult { get { return _result != null; } }

        /// <summary>本场实际登场过的 profileId（决定休息判定）。</summary>
        public List<string> EnteredProfileIds
        {
            get
            {
                List<string> ids = new List<string>(_enteredProfileIds);
                ids.Sort(StringComparer.Ordinal);
                return ids;
            }
        }

        /// <summary>接力是否已消耗。</summary>
        public bool RelayConsumed { get { return _relayConsumed; } }

        /// <summary>当前存活敌军数量。</summary>
        public int LiveEnemyCount { get { return _liveEnemies.Count; } }

        /// <summary>本场待处理的倒地 profileId（供接力判定读取后清空）。</summary>
        public string PendingDownProfileId { get { return _pendingDownProfileId; } }

        #endregion

        #region 本场生命周期

        /// <summary>开始一场比赛。</summary>
        public void BeginMatch(int matchIndex, long runSeed, string highThreatCoreStableKey)
        {
            _matchIndex = matchIndex;
            _runSeed = runSeed;
            _elapsedSeconds = 0f;
            _resultClaimed = 0;
            _result = null;
            _downTokens.Clear();
            _enteredProfileIds.Clear();
            _lastKnownHealthFraction.Clear();
            _liveEnemies.Clear();
            _activeFighter = null;
            _pendingDownProfileId = null;
            _relayConsumed = false;
            _lastSpecialKillTag = null;
            _lastSpecialKillProfileId = null;
            _highThreatCoreKilled = false;
            _highThreatCoreStableKey = highThreatCoreStableKey;
        }

        /// <summary>从战场快照续接计时与一次性事实。</summary>
        public void RestoreFromSnapshot(
            float elapsedSeconds, bool relayConsumed, IList<string> enteredProfileIds)
        {
            _elapsedSeconds = elapsedSeconds > 0f ? elapsedSeconds : 0f;
            _relayConsumed = relayConsumed;
            if (enteredProfileIds != null)
            {
                for (int i = 0; i < enteredProfileIds.Count; i++)
                {
                    if (!string.IsNullOrEmpty(enteredProfileIds[i])) _enteredProfileIds.Add(enteredProfileIds[i]);
                }
            }
        }

        /// <summary>一名敌军进入擂台。</summary>
        public void OnEnemyEntered(ModeHParticipantRef enemy)
        {
            if (enemy == null || !enemy.IsEnemy) return;
            if (!_liveEnemies.Contains(enemy)) _liveEnemies.Add(enemy);
        }

        /// <summary>
        /// 一名选手实际踏入擂台。这是休息判定的唯一来源：
        /// 列入 matchStarter/matchRelay 但未登场仍算休息。
        /// </summary>
        public void OnFighterEntered(ModeHParticipantRef fighter)
        {
            if (fighter == null || string.IsNullOrEmpty(fighter.ProfileId)) return;
            _activeFighter = fighter;
            _enteredProfileIds.Add(fighter.ProfileId);
            if (fighter.IsRelay) _relayConsumed = true;
        }

        /// <summary>本场是否有选手完整休息（从未实际登场）。</summary>
        public bool HasRested(string profileId)
        {
            return !string.IsNullOrEmpty(profileId) && !_enteredProfileIds.Contains(profileId);
        }

        /// <summary>推进计时。返回 true 表示本次推进触发了 180 秒终局。</summary>
        public bool Tick(float deltaTime)
        {
            if (_result != null) return false;
            _elapsedSeconds += deltaTime;
            if (_elapsedSeconds < ModeHConfig.MatchDurationSeconds) return false;
            // 优先级 4：180 秒到时判玩家失败，不自动补伤害、不伪造击倒
            return TryClaimResult(ModeHMatchOutcome.PlayerDefeat, true, null);
        }

        #endregion

        #region 事件接收

        /// <summary>受伤：只更新生命比例缓存，用于 last_stand 与伤病触发判定。</summary>
        public void OnParticipantHurt(
            ModeHParticipantRef target, ModeHParticipantRef attacker, float damageValue)
        {
            if (target == null) return;
            UpdateHealthFraction(target);
        }

        /// <summary>
        /// 死亡：敌军死亡推进终局条件；我方倒地生成唯一 `ModeHFighterDownToken`。
        /// 注意死亡帧上 GameObject 已 inactive，这里只用 token 语义判断。
        /// </summary>
        public void OnParticipantDead(ModeHParticipantRef target, ModeHParticipantRef killer)
        {
            if (target == null) return;

            if (target.IsEnemy)
            {
                _liveEnemies.Remove(target);
                if (!string.IsNullOrEmpty(_highThreatCoreStableKey)
                    && string.Equals(target.StableKey, _highThreatCoreStableKey, StringComparison.Ordinal))
                {
                    _highThreatCoreKilled = true;
                }
                ResolveSpecialKill(killer, target);
                return;
            }

            // 我方倒地：每个 participantId + matchIndex 至多一次
            string downToken = BuildDownToken(target.ProfileId);
            if (!_downTokens.Add(downToken)) return;
            _pendingDownProfileId = target.ProfileId;
            if (ReferenceEquals(target, _activeFighter)) _activeFighter = null;
        }

        /// <summary>取本场某选手的倒地 token（不存在返回空串）。</summary>
        public string GetDownToken(string profileId)
        {
            string token = BuildDownToken(profileId);
            return _downTokens.Contains(token) ? token : string.Empty;
        }

        /// <summary>该选手本场是否已被规范倒地事件确认。</summary>
        public bool IsDown(string profileId)
        {
            return _downTokens.Contains(BuildDownToken(profileId));
        }

        /// <summary>消费待处理倒地事实（接力判定读取后调用）。</summary>
        public void ConsumePendingDown()
        {
            _pendingDownProfileId = null;
        }

        private string BuildDownToken(string profileId)
        {
            return "down|" + _matchIndex + "|" + (profileId != null ? profileId : string.Empty);
        }

        private void UpdateHealthFraction(ModeHParticipantRef reference)
        {
            if (reference == null || reference.Character == null) return;
            string key = !string.IsNullOrEmpty(reference.ProfileId)
                ? reference.ProfileId
                : reference.StableKey + "#" + reference.PlanSlotIndex;
            _lastKnownHealthFraction[key] = ReadHealthFraction(reference.Character);
        }

        /// <summary>读取生命比例；读不到时返回 1（不因读数失败伪造濒死事实）。</summary>
        public static float ReadHealthFraction(CharacterMainControl character)
        {
            if (character == null) return 0f;
            try
            {
                Health health = character.Health;
                if (health == null) return 1f;
                float max = health.MaxHealth;
                if (max <= 0f) return 1f;
                float fraction = health.CurrentHealth / max;
                if (fraction < 0f) return 0f;
                return fraction > 1f ? 1f : fraction;
            }
            catch (Exception)
            {
                // 角色已被回收或组件缺失：按满血处理，避免误判 last_stand
                return 1f;
            }
        }

        /// <summary>取缓存的生命比例（快照采集与伤病触发共用）。</summary>
        public float GetCachedHealthFraction(string key)
        {
            float value;
            return _lastKnownHealthFraction.TryGetValue(key, out value) ? value : 1f;
        }

        #endregion

        #region 终局

        /// <summary>
        /// 终局优先级 1：敌军全部死亡且我方至少一名选手存活。
        /// 由主循环在敌军批次全部入场后调用。
        /// </summary>
        public bool TryClaimVictory(bool anyFighterAlive)
        {
            if (_liveEnemies.Count > 0 || !anyFighterAlive) return false;
            return TryClaimResult(ModeHMatchOutcome.PlayerVictory, false, null);
        }

        /// <summary>
        /// 终局优先级 2：当前先发倒地且无可用接力者。
        /// 接力者仍存活时由调用方进入一次自动接力窗口，不在这里判负。
        /// </summary>
        public bool TryClaimDefeatByDown()
        {
            return TryClaimResult(ModeHMatchOutcome.PlayerDefeat, false, null);
        }

        /// <summary>
        /// 终局优先级 5：逃跑型胆怯视为整队弃赛，立即失败，
        /// 不触发替补接力，也不把逃跑计为身体倒地。
        /// </summary>
        public bool TryClaimDefeatByCowardice(string cowardiceType)
        {
            return TryClaimResult(ModeHMatchOutcome.PlayerDefeat, false, cowardiceType);
        }

        /// <summary>
        /// 结果 CAS：两个终局事件竞争时只有先 CAS 者生效；
        /// 已有结果后一律拒绝，绝不执行二次伤害或二次奖励。
        /// </summary>
        private bool TryClaimResult(ModeHMatchOutcome outcome, bool timeout, string cowardiceType)
        {
            if (Interlocked.CompareExchange(ref _resultClaimed, 1, 0) != 0) return false;

            ModeHMatchResult result = new ModeHMatchResult();
            result.ResultToken = "result|" + _matchIndex + "|" + (int)outcome + "|"
                + Mathf.RoundToInt(_elapsedSeconds * 1000f);
            result.Outcome = outcome;
            result.Timeout = timeout;
            result.CowardiceType = cowardiceType != null ? cowardiceType : string.Empty;
            result.ElapsedSeconds = _elapsedSeconds;
            result.SpecialKillTag = string.Empty;
            result.SpecialKillProfileId = string.Empty;
            _result = result;
            return true;
        }

        /// <summary>
        /// 胜利后按 §17.4 冻结优先级补写 specialKillTag：
        /// core -> relay -> last stand -> x3+ 胜利。
        /// </summary>
        public void FinalizeSpecialKill(int lockedOdds, string survivingProfileId)
        {
            if (_result == null || _result.Outcome != ModeHMatchOutcome.PlayerVictory) return;

            if (_highThreatCoreKilled && !string.IsNullOrEmpty(_lastSpecialKillProfileId))
            {
                _result.SpecialKillTag = ModeHStableIds.SpecialKillHighThreatCore;
                _result.SpecialKillProfileId = _lastSpecialKillProfileId;
                return;
            }
            if (string.Equals(_lastSpecialKillTag, ModeHStableIds.SpecialKillRelayFinisher,
                    StringComparison.Ordinal))
            {
                _result.SpecialKillTag = ModeHStableIds.SpecialKillRelayFinisher;
                _result.SpecialKillProfileId = _lastSpecialKillProfileId;
                return;
            }
            if (string.Equals(_lastSpecialKillTag, ModeHStableIds.SpecialKillLastStand,
                    StringComparison.Ordinal))
            {
                _result.SpecialKillTag = ModeHStableIds.SpecialKillLastStand;
                _result.SpecialKillProfileId = _lastSpecialKillProfileId;
                return;
            }
            if (lockedOdds >= ModeHConfig.ScarOfferMinOdds)
            {
                // x3+ 胜利归属终局时场上的存活选手
                _result.SpecialKillTag = "odds_win";
                _result.SpecialKillProfileId = survivingProfileId != null ? survivingProfileId : string.Empty;
            }
        }

        /// <summary>
        /// 解析一次击杀的特殊标签归属。召唤物先解析其 participant owner；
        /// 无法解析时归属当时场上选手。
        /// </summary>
        private void ResolveSpecialKill(ModeHParticipantRef killer, ModeHParticipantRef victim)
        {
            ModeHParticipantRef owner = killer;
            if (owner == null || owner.IsEnemy) owner = _activeFighter;
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId)) return;

            // 只有击倒“最终敌人”才产生 relay/last_stand 标签
            bool finalEnemy = _liveEnemies.Count == 0;
            if (!string.IsNullOrEmpty(_highThreatCoreStableKey)
                && string.Equals(victim.StableKey, _highThreatCoreStableKey, StringComparison.Ordinal))
            {
                _lastSpecialKillTag = ModeHStableIds.SpecialKillHighThreatCore;
                _lastSpecialKillProfileId = owner.ProfileId;
                return;
            }
            if (!finalEnemy) return;

            if (owner.IsRelay)
            {
                _lastSpecialKillTag = ModeHStableIds.SpecialKillRelayFinisher;
                _lastSpecialKillProfileId = owner.ProfileId;
                return;
            }
            float fraction = ReadHealthFraction(owner.Character);
            if (fraction <= ModeHConfig.LastStandHealthFraction)
            {
                _lastSpecialKillTag = ModeHStableIds.SpecialKillLastStand;
                _lastSpecialKillProfileId = owner.ProfileId;
            }
        }

        #endregion

        #region 战报

        /// <summary>把本场遥测事实写入战报（只由 MatchSettling 单批调用）。</summary>
        public void WriteReport(ModeHMatchReportDto report, bool errorTriggered, string consumedCommandId,
            bool bellConsumed)
        {
            if (report == null || _result == null) return;
            report.matchIndex = _matchIndex;
            report.resultToken = _result.ResultToken;
            report.winner = (int)_result.Outcome;
            report.timeout = _result.Timeout;
            report.cowardiceType = _result.CowardiceType;
            report.errorTriggered = errorTriggered;
            report.entrantIds = EnteredProfileIds;
            report.consumedCommandId = consumedCommandId != null ? consumedCommandId : string.Empty;
            report.bellConsumed = bellConsumed;
            report.elapsedSeconds = _result.ElapsedSeconds;
        }

        /// <summary>本场使用的 seed 域序号（战痕/奖励派生共用）。</summary>
        public int GetSeedSequence()
        {
            return _matchIndex;
        }

        /// <summary>本场 runSeed（供确定性派生）。</summary>
        public long RunSeed { get { return _runSeed; } }

        #endregion
    }
}
