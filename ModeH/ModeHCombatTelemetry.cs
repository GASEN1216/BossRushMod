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

        /// <summary>本场登场选手是否吃过远程伤害（longshot_memory 战痕的触发信号）。</summary>
        private bool _activeFighterTookRangedDamage;

        /// <summary>weaponTypeId -> 是否远程。避免每次挨打都查一遍物品元数据。</summary>
        private static readonly Dictionary<int, bool> _rangedWeaponCache = new Dictionary<int, bool>();

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

        /// <summary>
        /// 按下标取存活敌军。供口令点火上下文按 `LiveEnemyCount` 做零分配遍历：
        /// 不暴露内部 List、也不复制一份出去，热路径每帧都会走这条。
        /// 越界返回 null（调用方跳过即可），不抛。
        /// </summary>
        public ModeHParticipantRef GetLiveEnemyAt(int index)
        {
            if (index < 0 || index >= _liveEnemies.Count) return null;
            return _liveEnemies[index];
        }

        /// <summary>
        /// 指定入场批次是否还有活口。战痕分量的 `first_wave_alive` 用 batch 0 问它。
        /// 存活名单是个位数，按重申节奏（0.1 秒）扫一次，无分配。
        /// </summary>
        public bool HasLiveEnemyInBatch(int batchIndex)
        {
            for (int i = 0; i < _liveEnemies.Count; i++)
            {
                ModeHParticipantRef enemy = _liveEnemies[i];
                if (enemy != null && enemy.BatchIndex == batchIndex) return true;
            }
            return false;
        }

        /// <summary>本场待处理的倒地 profileId（供接力判定读取后清空）。</summary>
        public string PendingDownProfileId { get { return _pendingDownProfileId; } }

        /// <summary>本场登场选手是否已吃过远程伤害（战痕 longshot_memory 的触发条件）。</summary>
        public bool ActiveFighterTookRangedDamage { get { return _activeFighterTookRangedDamage; } }

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
            _activeFighterTookRangedDamage = false;
        }

        // 【RestoreFromSnapshot 已于 2026-09-03 移除】只被 ModeHCombatControl 的同名方法
        // 调用，而那条 §17.4 局中重建链全链零调用点、且在冻结转换表下不可达。
        // 理由见 ModeHBattleSnapshot.cs 的“重建校验（已随 §20.3 收敛而移除）”。

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
            ModeHParticipantRef target, ModeHParticipantRef attacker, float damageValue,
            int fromWeaponItemID)
        {
            if (target == null) return;
            UpdateHealthFraction(target);

            // 「本场登场选手是否吃过远程伤害」——longshot_memory 战痕的唯一信号源。
            // 只认我方登场选手挨打，敌军互殴与环境伤害不算。
            if (!_activeFighterTookRangedDamage
                && !target.IsEnemy
                && _activeFighter != null
                && ReferenceEquals(target, _activeFighter)
                && IsRangedWeapon(fromWeaponItemID))
            {
                _activeFighterTookRangedDamage = true;
            }
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
        /// <summary>
        /// 宿主销毁 / 关停时的静态缓存复位（StaticCacheLifecycleGuard 要求）。
        /// 只清武器类型缓存：它按 typeId 索引，官方物品表在 Mod 重载后可能变化。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            _rangedWeaponCache.Clear();
        }

        /// <summary>
        /// 该武器是否远程。判定口径与 ModeGCombatTelemetry / CampaignObjectiveCollector 一致：
        /// 物品 tag 里有 Gun 而没有 MeleeWeapon/Melee 才算远程，两者都有或都没有时不算。
        ///
        /// 按 AGENTS.md 4.9「不要因未来可能复用提前提升」，这 15 行判定沿用既有做法
        /// 本地复制，不去动那两处已稳定的实现。带缓存，热路径不重复查元数据。
        /// </summary>
        private static bool IsRangedWeapon(int weaponTypeId)
        {
            if (weaponTypeId <= 0) return false;

            bool cached;
            if (_rangedWeaponCache.TryGetValue(weaponTypeId, out cached)) return cached;

            bool ranged = false;
            try
            {
                ItemStatsSystem.ItemMetaData metaData =
                    ItemStatsSystem.ItemAssetsCollection.GetMetaData(weaponTypeId);
                bool gun = false;
                bool melee = false;
                if (metaData.id > 0 && metaData.tags != null)
                {
                    for (int i = 0; i < metaData.tags.Length; i++)
                    {
                        Duckov.Utilities.Tag tag = metaData.tags[i];
                        if (tag == null) continue;
                        if (string.Equals(tag.name, "Gun", StringComparison.Ordinal)) gun = true;
                        else if (string.Equals(tag.name, "MeleeWeapon", StringComparison.Ordinal)
                                 || string.Equals(tag.name, "Melee", StringComparison.Ordinal)) melee = true;
                    }
                }
                ranged = gun && !melee;
            }
            catch (Exception)
            {
                // 查不到元数据按「非远程」处理：宁可战痕不触发，也不误触发
                ranged = false;
            }

            _rangedWeaponCache[weaponTypeId] = ranged;
            return ranged;
        }

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
