using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode H 战斗控制（设计提案 §17.4、§17.6、§25.1）。
    ///
    /// 本类是一场比赛的控制中枢：驱动计时与终局优先级、拍铃、单向接力、
    /// 三类胆怯判定与 ERROR 触发判定，并把每一次事实转给遥测、伤病/战痕与快照。
    ///
    /// 冻结契约：
    /// - 终局优先级严格按 §17.4 的 1..5 顺序，由 `ModeHCombatTelemetry` 的
    ///   `ModeHBattleResultToken` CAS 保证唯一；
    /// - 拍铃每场唯一一次，不暂停战斗、不弹菜单；
    /// - 接力只有一次自动窗口，且只在先发倒地且接力者存活时开启；
    /// - 逃跑型胆怯立即判负，不触发接力，也不计为身体倒地；
    /// - 胆怯与 ERROR 各自每场至多判定一次，判定结果进入战场快照防重复触发；
    /// - 口令窗口、伤病窗口与战痕窗口共用同一套重申/还原设施，
    ///   任一终止路径都调用同一幂等 `RestoreAll`。
    ///
    /// ERROR 完整互换的执行序列（§17.6.5）由 `ModeHErrorSwapState` 描述，
    /// 具体的看台解冻与表演驱动由 `ModeHHarmonyPatches` 与 `ModeHStandInPerformer` 承担。
    /// </summary>
    internal sealed class ModeHCombatControl
    {
        #region 状态

        private readonly ModeHCommandController _commandController = new ModeHCommandController();
        private readonly ModeHInjuryAndScarSystem _injuryAndScar = new ModeHInjuryAndScarSystem();
        private readonly ModeHBattleSnapshot _snapshot = new ModeHBattleSnapshot();
        private readonly ModeHCommandFireContext _fireContext = new ModeHCommandFireContext();
        private readonly HashSet<string> _cowardChecksDone = new HashSet<string>(StringComparer.Ordinal);

        private ModeHCombatTelemetry _telemetry;
        private ModeHRunState _runState;
        private ModeHSupportedMap _map;

        private ModeHParticipantRef _activeFighter;
        private ModeHParticipantRef _relayFighter;
        private AICharacterController _activeAi;
        private string _activeProfileId;
        private string _activeStableKey;
        private string _activeAnomalyId;

        private readonly ModeHStandInPerformer _standInPerformer = new ModeHStandInPerformer();

        private bool _relayWindowOpen;
        private bool _errorCheckDone;
        private bool _errorTriggered;

        // ERROR 完整互换状态（§17.6.5）
        private ModeHErrorSwapPhase _swapPhase = ModeHErrorSwapPhase.None;
        private CharacterMainControl _playerBody;
        private CharacterMainControl _controlledFighter;
        private ModeHParticipantRef _controlledParticipant;
        private Teams _playerOriginalTeam;
        private Vector3 _playerOriginalPosition;
        private bool _playerOriginalInvincible;
        private bool _playerStateCaptured;
        private float _swapDeadlineRemaining;
        private string _swapProfileId;
        private string _swapPatternId;
        private int _matchIndex;
        private long _runSeed;
        private int _entryBatchIndex;

        #endregion

        #region 只读

        /// <summary>本场遥测。</summary>
        public ModeHCombatTelemetry Telemetry { get { return _telemetry; } }

        /// <summary>本场口令控制器。</summary>
        public ModeHCommandController CommandController { get { return _commandController; } }

        /// <summary>本场伤病与战痕系统。</summary>
        public ModeHInjuryAndScarSystem InjuryAndScar { get { return _injuryAndScar; } }

        /// <summary>本场战场快照。</summary>
        public ModeHBattleSnapshot Snapshot { get { return _snapshot; } }

        /// <summary>本场是否触发过 ERROR。</summary>
        public bool ErrorTriggered { get { return _errorTriggered; } }

        /// <summary>ERROR 判定是否已消耗。</summary>
        public bool ErrorCheckDone { get { return _errorCheckDone; } }

        /// <summary>ERROR 互换是否生效中。</summary>
        public bool IsErrorSwapActive { get { return _swapPhase == ModeHErrorSwapPhase.Active; } }

        /// <summary>被互换的 profileId（空表示无）。</summary>
        public string ErrorSwapProfileId { get { return _swapProfileId != null ? _swapProfileId : string.Empty; } }

        /// <summary>当前看台表演模式 ID。</summary>
        public string StandInPatternId { get { return _swapPatternId != null ? _swapPatternId : string.Empty; } }

        /// <summary>看台表演驱动（只读，供诊断页展示）。</summary>
        public ModeHStandInPerformer StandInPerformer { get { return _standInPerformer; } }

        /// <summary>接力自动窗口是否开启中。</summary>
        public bool IsRelayWindowOpen { get { return _relayWindowOpen; } }

        /// <summary>当前登场选手的 profileId。</summary>
        public string ActiveProfileId { get { return _activeProfileId; } }

        #endregion

        #region 本场生命周期

        /// <summary>开始一场比赛。</summary>
        public void BeginMatch(
            ModeHRunState runState,
            ModeHSupportedMap map,
            ModeHCombatTelemetry telemetry,
            int matchIndex,
            long runSeed,
            string highThreatCoreStableKey)
        {
            _runState = runState;
            _map = map;
            _telemetry = telemetry;
            _matchIndex = matchIndex;
            _runSeed = runSeed;
            _entryBatchIndex = 0;
            _relayWindowOpen = false;
            _errorCheckDone = false;
            _errorTriggered = false;
            _cowardChecksDone.Clear();
            _activeFighter = null;
            _relayFighter = null;
            _activeAi = null;
            _activeProfileId = null;
            _activeStableKey = null;
            _activeAnomalyId = null;

            _telemetry.BeginMatch(matchIndex, runSeed, highThreatCoreStableKey);
            _snapshot.BeginMatch();
        }

        /// <summary>登记本场先发与接力者引用（接力者可为 null）。</summary>
        public void SetRoster(ModeHParticipantRef starter, ModeHParticipantRef relay)
        {
            _activeFighter = starter;
            _relayFighter = relay;
        }

        /// <summary>
        /// 一名选手实际登场：绑定 AI、施加既有伤病与常驻战痕，并写入遥测。
        /// 这是休息判定的唯一来源。
        /// </summary>
        public bool OnFighterEntered(
            ModeHParticipantRef fighter, ModeHProfileDto profile, out string failureReasonId)
        {
            failureReasonId = null;
            if (fighter == null || profile == null)
            {
                failureReasonId = "combat_entered_invalid";
                return false;
            }

            _activeFighter = fighter;
            _activeProfileId = profile.profileId;
            _activeStableKey = profile.stableKey;
            _activeAnomalyId = profile.anomalyId;
            _activeAi = ResolveAi(fighter.Character);

            _telemetry.OnFighterEntered(fighter);
            _injuryAndScar.BindFighter(_activeAi, profile.profileId, profile.stableKey, _matchIndex);

            string reason;
            if (!_injuryAndScar.ApplyStandingInjury(profile.injuryId, out reason))
            {
                // 伤病不可用不阻断比赛：条目在抽池阶段已过滤，这里只记录
                ModBehaviour.DevLog("[ModeH] 伤病未生效: " + reason);
            }
            if (!_injuryAndScar.ApplyStandingScars(profile.scarIds, out reason))
            {
                ModBehaviour.DevLog("[ModeH] 战痕未生效: " + reason);
            }
            return true;
        }

        /// <summary>一批敌军入场完成：更新批次序号并触发一次快照采集。</summary>
        public void OnEnemyBatchEntered(int batchIndex, ModeHBattleSnapshotContext context)
        {
            _entryBatchIndex = batchIndex;
            CaptureSnapshot(ModeHSnapshotTrigger.BatchEntered, context);
        }

        /// <summary>一名敌军进入擂台。</summary>
        public void OnEnemyEntered(ModeHParticipantRef enemy)
        {
            if (_telemetry != null) _telemetry.OnEnemyEntered(enemy);
        }

        #endregion

        #region 每帧驱动

        /// <summary>
        /// 每帧推进：计时、口令窗口、伤病/战痕窗口、胆怯与 ERROR 判定、
        /// 终局条件与间隔快照。返回 true 表示本帧锁定了终局。
        /// </summary>
        public bool Tick(float deltaTime, ModeHBattleSnapshotContext snapshotContext)
        {
            if (_telemetry == null || _telemetry.HasResult) return false;

            RefreshFireContext();

            // 优先级 4：180 秒
            if (_telemetry.Tick(deltaTime)) return true;

            _commandController.Tick(
                deltaTime, _fireContext.ArenaCenter, _fireContext.NearestEnemy,
                _fireContext.LowestHealthEnemy, _fireContext.EnemyCount);
            _injuryAndScar.Tick(deltaTime, _fireContext);
            TickErrorSwap(deltaTime);

            EvaluateTriggeredInjuries();

            // 优先级 5：逃跑型胆怯（先于倒地与胜利判定，视为整队弃赛）
            string cowardiceType;
            if (TryEvaluateCowardice(out cowardiceType))
            {
                if (_telemetry.TryClaimDefeatByCowardice(cowardiceType))
                {
                    RestoreAll();
                    return true;
                }
            }

            TryEvaluateErrorTrigger();

            // 优先级 1：敌军全灭且我方存活
            if (_telemetry.LiveEnemyCount == 0 && IsAnyFighterAlive())
            {
                if (_telemetry.TryClaimVictory(true))
                {
                    RestoreAll();
                    return true;
                }
            }

            // 优先级 2：先发倒地 -> 一次自动接力窗口；无接力者判负
            string downProfileId = _telemetry.PendingDownProfileId;
            if (!string.IsNullOrEmpty(downProfileId))
            {
                _telemetry.ConsumePendingDown();
                if (HandleFighterDown(downProfileId, snapshotContext)) return true;
            }

            if (_snapshot.TickInterval(deltaTime))
            {
                CaptureSnapshot(ModeHSnapshotTrigger.Interval, snapshotContext);
            }
            return false;
        }

        /// <summary>
        /// 倒地处理：接力者存活则开一次自动接力窗口，否则按优先级 2 判负。
        /// 无论哪条路径都先幂等还原上一位选手的全部窗口。
        /// </summary>
        private bool HandleFighterDown(string downProfileId, ModeHBattleSnapshotContext snapshotContext)
        {
            _commandController.RestoreAll();
            _injuryAndScar.RestoreAll();
            CaptureSnapshot(ModeHSnapshotTrigger.DownOrRelay, snapshotContext);

            bool relayAvailable = _relayFighter != null
                && !string.IsNullOrEmpty(_relayFighter.ProfileId)
                && !_telemetry.RelayConsumed
                && !_telemetry.IsDown(_relayFighter.ProfileId)
                && !string.Equals(_relayFighter.ProfileId, downProfileId, StringComparison.Ordinal);

            if (relayAvailable)
            {
                _relayWindowOpen = true;
                return false;
            }

            _relayWindowOpen = false;
            return _telemetry.TryClaimDefeatByDown();
        }

        /// <summary>接力者实际登场，关闭接力窗口。</summary>
        public bool CommitRelay(ModeHProfileDto relayProfile, ModeHBattleSnapshotContext snapshotContext,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (!_relayWindowOpen)
            {
                failureReasonId = "relay_window_closed";
                return false;
            }
            if (_relayFighter == null || relayProfile == null)
            {
                failureReasonId = "relay_fighter_missing";
                return false;
            }
            _relayWindowOpen = false;
            if (!OnFighterEntered(_relayFighter, relayProfile, out failureReasonId)) return false;
            CaptureSnapshot(ModeHSnapshotTrigger.DownOrRelay, snapshotContext);
            return true;
        }

        private bool IsAnyFighterAlive()
        {
            if (_activeFighter != null && !_telemetry.IsDown(_activeFighter.ProfileId)) return true;
            return _relayFighter != null && !_telemetry.IsDown(_relayFighter.ProfileId);
        }

        #endregion

        #region 拍铃

        /// <summary>
        /// 拍铃：每场唯一一次，立即触发预选口令，不暂停战斗、不弹菜单。
        /// 调制幅度乘以伤病/战痕的 Mode H 自结算系数。
        /// </summary>
        public bool TryRingBell(ModeHBattleSnapshotContext snapshotContext, out string failureReasonId)
        {
            RefreshFireContext();
            bool relayEntered = _telemetry != null && _telemetry.RelayConsumed;
            bool ok = _commandController.TryRingBell(
                _activeAi,
                _activeProfileId,
                _injuryAndScar.SelfSettledCommandScale,
                _fireContext.ArenaCenter,
                _fireContext.NearestEnemy,
                _fireContext.LowestHealthEnemy,
                _fireContext.EnemyCount,
                _runState != null ? _runState.OwnerToken : 0L,
                relayEntered,
                out failureReasonId);

            if (!ok) return false;

            // 拍铃后 6 秒内的战痕窗口（bell_dependence）
            string reason;
            _injuryAndScar.TryOpenScarWindow("bell_dependence", "bell_rung", out reason);
            CaptureSnapshot(ModeHSnapshotTrigger.BellCommitted, snapshotContext);
            return true;
        }

        #endregion

        #region 胆怯与 ERROR

        /// <summary>
        /// 三类胆怯，每类每场至多判定一次。命中即整队弃赛。
        /// `steady` 口令生效时按 §17.6.3 把概率乘 0.75（Mode H 自结算）。
        /// </summary>
        private bool TryEvaluateCowardice(out string cowardiceType)
        {
            cowardiceType = null;
            if (string.IsNullOrEmpty(_activeAnomalyId)) return false;
            if (!ModeHCommandCompatibilityRegistry.HasVerifiedAnomalyBehavior(
                    _activeStableKey, _activeAnomalyId))
            {
                return false;
            }

            float mitigation = string.Equals(
                _commandController.ActiveCommandId, ModeHStableIds.CommandSteady, StringComparison.Ordinal)
                ? ModeHConfig.CowardSteadyMitigationMultiplier
                : 1f;

            if (string.Equals(_activeAnomalyId, ModeHStableIds.AnomalyCowardBlood, StringComparison.Ordinal))
            {
                float fraction = ModeHCombatTelemetry.ReadHealthFraction(
                    _activeFighter != null ? _activeFighter.Character : null);
                if (fraction > ModeHConfig.CowardBloodHealthFraction) return false;
                return RollCoward(ModeHStableIds.AnomalyCowardBlood,
                    ModeHConfig.CowardBloodBaseChance * mitigation, out cowardiceType);
            }
            if (string.Equals(_activeAnomalyId, ModeHStableIds.AnomalyCowardCrowd, StringComparison.Ordinal))
            {
                if (_telemetry.LiveEnemyCount < ModeHConfig.CowardCrowdEnemyThreshold) return false;
                return RollCoward(ModeHStableIds.AnomalyCowardCrowd,
                    ModeHConfig.CowardCrowdBaseChance * mitigation, out cowardiceType);
            }
            if (string.Equals(_activeAnomalyId, ModeHStableIds.AnomalyCowardStrong, StringComparison.Ordinal))
            {
                if (_telemetry.ElapsedSeconds < ModeHConfig.CowardStrongCoreSurvivalSeconds) return false;
                return RollCoward(ModeHStableIds.AnomalyCowardStrong,
                    ModeHConfig.CowardStrongBaseChance * mitigation, out cowardiceType);
            }
            return false;
        }

        private bool RollCoward(string anomalyId, float chance, out string cowardiceType)
        {
            cowardiceType = null;
            string checkId = anomalyId + "|" + _activeProfileId;
            if (!_cowardChecksDone.Add(checkId)) return false;

            ModeHSeedStream stream = ModeHSeedStream.Create(
                _runSeed, ModeHSeedStream.Domains.Coward, _matchIndex * 10 + anomalyId.Length);
            if (!stream.NextChance(chance)) return false;
            cowardiceType = anomalyId;
            return true;
        }

        /// <summary>
        /// ERROR 触发判定：白名单内角色、每场至多一次、8% 单次判定。
        /// 判定为真只置标记，实际互换由 §17.6.5 的执行序列完成。
        /// </summary>
        private void TryEvaluateErrorTrigger()
        {
            if (_errorCheckDone) return;
            if (!string.Equals(_activeAnomalyId, ModeHStableIds.AnomalyError, StringComparison.Ordinal)) return;
            if (!ModeHCommandCompatibilityRegistry.HasVerifiedAnomalyBehavior(
                    _activeStableKey, ModeHStableIds.AnomalyError))
            {
                _errorCheckDone = true;
                return;
            }
            _errorCheckDone = true;

            ModeHSeedStream stream = ModeHSeedStream.Create(
                _runSeed, ModeHSeedStream.Domains.Error, _matchIndex);
            _errorTriggered = stream.NextChance(ModeHConfig.ErrorTriggerChance);
        }

        /// <summary>触发型伤病与战痕的条件检查（每帧 O(1)）。</summary>
        private void EvaluateTriggeredInjuries()
        {
            if (_activeFighter == null) return;
            float fraction = ModeHCombatTelemetry.ReadHealthFraction(_activeFighter.Character);
            _injuryAndScar.OnHealthFractionChanged("old_wound", fraction);
            _injuryAndScar.OnEnemyCountChanged("spirit", _telemetry.LiveEnemyCount);

            string reason;
            if (fraction <= ModeHConfig.LastStandHealthFraction)
            {
                _injuryAndScar.TryOpenScarWindow("broken_shield_charge", "armor_broken", out reason);
            }
            if (_telemetry.LiveEnemyCount >= ModeHConfig.CowardCrowdEnemyThreshold)
            {
                _injuryAndScar.TryOpenScarWindow("crowd_favorite", "crowd_present", out reason);
            }
            if (_activeFighter.IsRelay)
            {
                _injuryAndScar.TryOpenScarWindow("relay_expert", "relay_entered", out reason);
            }
        }

        #endregion

        #region ERROR 完整互换（§17.6.5）

        /// <summary>
        /// 登记玩家身体与看台点，供互换与恢复使用。
        /// 由主循环在取得 spectator lease 之后调用一次。
        /// </summary>
        public void BindPlayerBody(CharacterMainControl playerBody)
        {
            _playerBody = playerBody;
        }

        /// <summary>
        /// 尝试开始一次 ERROR 完整互换。执行顺序严格按 §17.6.5：
        /// 1) 保存玩家身体的 team/position/invincible 与受控选手引用；
        /// 2) `ControlOtherCharacter(target, -1f)`（无限时长，不得改传正数再乘算）；
        /// 3) 2 秒 deadline 内确认 `LevelManager.ControllingCharacter` 变为目标选手；
        /// 4) 先中立无敌、再解冻移动、最后启动表演——顺序不可颠倒。
        /// </summary>
        public bool TryBeginErrorSwap(ModeHProfileDto profile, out string failureReasonId)
        {
            failureReasonId = null;
            if (_swapPhase != ModeHErrorSwapPhase.None)
            {
                failureReasonId = "error_swap_already_active";
                return false;
            }
            if (!_errorTriggered)
            {
                failureReasonId = "error_swap_not_triggered";
                return false;
            }
            if (_playerBody == null || _activeFighter == null || _activeFighter.Character == null
                || profile == null)
            {
                failureReasonId = "error_swap_missing_reference";
                return false;
            }

            // 步骤 1：保存一次性状态；互换期间禁止开始结算或换装
            if (!CaptureplayerState(out failureReasonId)) return false;

            _controlledFighter = _activeFighter.Character;
            _controlledParticipant = _activeFighter;
            _swapProfileId = profile.profileId;
            _swapPatternId = ModeHStandInPerformer.ResolvePatternId(profile.temperamentId);

            // 步骤 2：调用原版控制权切换。StartAction 有优先级仲裁，可能返回 false，
            // 由 2 秒 deadline 兜底；返回 false 也继续等待，不立刻判失败。
            try { _playerBody.ControlOtherCharacter(_controlledFighter, -1f); }
            catch (Exception e)
            {
                failureReasonId = "error_swap_control_exception:" + e.GetType().Name;
                RestoreErrorSwap();
                return false;
            }

            _swapPhase = ModeHErrorSwapPhase.AwaitingControl;
            _swapDeadlineRemaining = ModeHConfig.ErrorControlSwitchDeadlineSeconds;
            return true;
        }

        /// <summary>互换状态机的每帧推进（无协程，避免第二条生命周期）。</summary>
        private void TickErrorSwap(float deltaTime)
        {
            if (_swapPhase == ModeHErrorSwapPhase.None) return;

            if (_swapPhase == ModeHErrorSwapPhase.AwaitingControl)
            {
                _swapDeadlineRemaining -= deltaTime;
                if (IsControllingCharacter(_controlledFighter))
                {
                    CompleteSwapHandover();
                    return;
                }
                if (_swapDeadlineRemaining <= 0f)
                {
                    // 2 秒 deadline 超时：尚无任何影响 profile/settlement 的持久 token，
                    // 按 §17.6.5 第 7 条完整回滚，比赛照常继续
                    ModBehaviour.DevLog("[ModeH] ERROR 控制切换超时，已回滚");
                    RestoreErrorSwap();
                }
                return;
            }

            if (_swapPhase != ModeHErrorSwapPhase.Active) return;

            _standInPerformer.Tick(deltaTime);

            // 受控选手倒地或原版把控制目标还原：以先发生者为准立刻恢复
            if (_controlledFighter == null
                || (_telemetry != null && _controlledParticipant != null
                    && _telemetry.IsDown(_controlledParticipant.ProfileId))
                || !IsControllingCharacter(_controlledFighter))
            {
                RestoreErrorSwap();
            }
        }

        /// <summary>
        /// 控制权已切换：先把身体移到看台、设中立、设无敌，
        /// 然后才置解冻门并启动表演。顺序不可颠倒。
        /// </summary>
        private void CompleteSwapHandover()
        {
            Vector3 spectatorPos = _map != null ? _map.SpectatorPos : _playerOriginalPosition;

            TrySetPosition(_playerBody, spectatorPos);
            TrySetTeam(_playerBody, Teams.middle);
            TrySetInvincible(_playerBody, true);

            int bodyId = 0;
            try { bodyId = _playerBody.gameObject.GetInstanceID(); }
            catch (Exception)
            {
                // 身体已被回收：bodyId 保持 0，下面的门会因此拒绝置位
                bodyId = 0;
            }
            ModeHRuntimeGates.SetStandInActive(bodyId != 0, bodyId);

            string reason;
            if (!_standInPerformer.TryStart(_playerBody, spectatorPos, _swapPatternId, out reason))
            {
                // 表演启动失败不升级为技术中止：互换本身仍然成立，身体只是静止
                ModBehaviour.DevLog("[ModeH] 看台表演未启动: " + reason);
            }

            // 遥测归属映射：互换期间 fromCharacter 会被原版改写为 Main
            ModeHEventRouter.SetErrorSwapControlledParticipant(_controlledParticipant);
            _swapPhase = ModeHErrorSwapPhase.Active;
        }

        /// <summary>
        /// 幂等恢复，顺序按 §17.6.5 第 5 条：
        /// 停止表演 -> 清解冻门 -> 确认控制目标已还原 -> 受控选手重设回 scav
        /// -> 恢复玩家身体 team -> 恢复 invincible -> 恢复位置。
        /// 持有物显示由原版 `SwitchToWeaponBeforeUse` 自动恢复，这里不抢先改写。
        /// </summary>
        public void RestoreErrorSwap()
        {
            if (_swapPhase == ModeHErrorSwapPhase.None && !_playerStateCaptured) return;

            // 1) 停止表演驱动
            _standInPerformer.Stop();

            // 2) 清零解冻门（ModeHIsolationGuard 断言退出后必须为 false）
            ModeHRuntimeGates.SetStandInActive(false, 0);

            // 3) 独立确认控制目标已回到玩家身体，不只信任原版回调
            if (_playerBody != null && !IsControllingCharacter(_playerBody))
            {
                TryRestoreControllingCharacter(_playerBody);
            }

            // 4) 仍存活的受控选手重设回 scav（对冲原版开火时的 SetTeam(Teams.all) 改写）
            if (_controlledFighter != null && !IsDead(_controlledFighter))
            {
                TrySetTeam(_controlledFighter, Teams.scav);
            }

            // 5..7) 恢复玩家身体 team -> invincible -> 位置
            if (_playerStateCaptured && _playerBody != null)
            {
                TrySetTeam(_playerBody, _playerOriginalTeam);
                TrySetInvincible(_playerBody, _playerOriginalInvincible);
                TrySetPosition(_playerBody, _playerOriginalPosition);
            }

            ModeHEventRouter.SetErrorSwapControlledParticipant(null);
            _swapPhase = ModeHErrorSwapPhase.None;
            _controlledFighter = null;
            _controlledParticipant = null;
            _playerStateCaptured = false;
            _swapDeadlineRemaining = 0f;
        }

        /// <summary>保存玩家身体的一次性状态。任一项读不到即拒绝互换（fail-closed）。</summary>
        private bool CaptureplayerState(out string failureReasonId)
        {
            failureReasonId = null;
            try
            {
                _playerOriginalTeam = _playerBody.Team;
                _playerOriginalPosition = _playerBody.transform.position;
                _playerOriginalInvincible = _playerBody.Health != null && _playerBody.Health.Invincible;
                _playerStateCaptured = true;
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "error_swap_capture_failed:" + e.GetType().Name;
                _playerStateCaptured = false;
                return false;
            }
        }

        private static bool IsControllingCharacter(CharacterMainControl target)
        {
            if (target == null) return false;
            try
            {
                return LevelManager.Instance != null
                    && ReferenceEquals(LevelManager.Instance.ControllingCharacter, target);
            }
            catch (Exception)
            {
                // 关卡已卸载：按“未持有控制权”处理，恢复流程照常收尾
                return false;
            }
        }

        private static void TryRestoreControllingCharacter(CharacterMainControl body)
        {
            try
            {
                if (LevelManager.Instance != null) LevelManager.Instance.SetControllingCharacter(body);
            }
            catch (Exception)
            {
                // 关卡已卸载：控制目标随场景一并消失，无需补偿
            }
        }

        private static bool IsDead(CharacterMainControl character)
        {
            try { return character.Health == null || character.Health.IsDead; }
            catch (Exception)
            {
                // 读不到生命组件：按已死亡处理，不再改写阵营
                return true;
            }
        }

        private static void TrySetTeam(CharacterMainControl character, Teams team)
        {
            if (character == null) return;
            try { character.SetTeam(team); }
            catch (Exception)
            {
                // 单步失败不阻断恢复顺序的其余步骤
            }
        }

        private static void TrySetInvincible(CharacterMainControl character, bool invincible)
        {
            if (character == null) return;
            try
            {
                if (character.Health != null) character.Health.SetInvincible(invincible);
            }
            catch (Exception)
            {
                // 同上
            }
        }

        private static void TrySetPosition(CharacterMainControl character, Vector3 position)
        {
            if (character == null) return;
            try { character.SetPosition(position); }
            catch (Exception)
            {
                // 同上
            }
        }

        #endregion

        #region 快照与还原

        /// <summary>补齐上下文并采集一次快照。</summary>
        private void CaptureSnapshot(ModeHSnapshotTrigger trigger, ModeHBattleSnapshotContext context)
        {
            if (context == null) return;
            context.MatchIndex = _matchIndex;
            context.TechnicalRetrySequence = _runState != null ? _runState.TechnicalRetrySequence : 0;
            context.ElapsedSeconds = _telemetry != null ? _telemetry.ElapsedSeconds : 0f;
            context.EntryBatchIndex = _entryBatchIndex;
            context.BellConsumed = _commandController.BellConsumed;
            context.ActiveCommandId = _commandController.ActiveCommandId;
            context.CommandWindowRemainingSeconds = _commandController.CommandWindowRemainingSeconds;
            context.ScarWindowStates = _injuryAndScar.ExportScarWindows();
            context.CowardCheckDone = new List<string>(_cowardChecksDone);
            context.ErrorCheckDone = _errorCheckDone;
            context.AppliedEventTokenIds = _runState != null
                ? _runState.ExportEventTokens()
                : new List<string>();

            string reason;
            if (!_snapshot.Capture(trigger, context, out reason))
            {
                ModBehaviour.DevLog("[ModeH] 快照采集失败: " + reason);
            }
        }

        /// <summary>
        /// 统一幂等还原：窗口结束、倒地、接力、技术中止、切图与 shutdown 共用同一入口。
        /// </summary>
        public void RestoreAll()
        {
            RestoreErrorSwap();
            _commandController.RestoreAll();
            _injuryAndScar.RestoreAll();
            _relayWindowOpen = false;
            _activeAi = null;
        }

        /// <summary>从战场快照恢复本场一次性判定与窗口状态。</summary>
        public void RestoreFromSnapshot(ModeHBattleSnapshotDto snapshot)
        {
            if (snapshot == null) return;
            _entryBatchIndex = snapshot.entryBatchIndex;
            _errorCheckDone = snapshot.errorCheckDone;
            _cowardChecksDone.Clear();
            if (snapshot.cowardCheckDone != null)
            {
                for (int i = 0; i < snapshot.cowardCheckDone.Count; i++)
                {
                    _cowardChecksDone.Add(snapshot.cowardCheckDone[i]);
                }
            }
            _commandController.RestoreFromSnapshot(
                _commandController.LockedCommandId, snapshot.bellConsumed,
                snapshot.activeCommandId, snapshot.commandWindowRemainingSeconds);
            _injuryAndScar.RestoreScarWindows(snapshot.scarWindowStates);
            if (_telemetry != null)
            {
                _telemetry.RestoreFromSnapshot(
                    snapshot.elapsedSeconds,
                    snapshot.entrant != null && snapshot.entrant.isRelay,
                    null);
            }
        }

        /// <summary>刷新点火上下文（零分配复用）。</summary>
        private void RefreshFireContext()
        {
            _fireContext.ArenaCenter = _map != null ? _map.ArenaCenter : Vector3.zero;
            _fireContext.EnemyCount = _telemetry != null ? _telemetry.LiveEnemyCount : 0;
            // 最近/最残敌人由生成事务在每次登记时刷新；这里不做场景扫描
        }

        /// <summary>把最近/最残敌人引用交给点火上下文（由主循环在敌军变更时调用）。</summary>
        public void SetFireTargets(DamageReceiver nearestEnemy, DamageReceiver lowestHealthEnemy)
        {
            _fireContext.NearestEnemy = nearestEnemy;
            _fireContext.LowestHealthEnemy = lowestHealthEnemy;
        }

        /// <summary>
        /// 取选手的 AI 控制器。
        /// **不要**改回根节点 `GetComponent`：官方 `AICharacterController.Init` 会执行
        /// `transform.SetParent(characterMainControl.transform, false)`，AI 组件是**子对象**，
        /// 根节点上取恒为 null。旧实现如此，导致拍铃与伤病/战痕窗口整场静默失效
        /// （两者都以 `_ai == null` 判空即拒，只走 DevLog，玩家侧毫无反馈）。
        /// 先读官方缓存字段（`CharacterSpawnerRoot:254` 亦走这条），但它是 Inspector 序列化字段、
        /// 没有任何代码为其赋值，Mod 刷出来的选手上可能为空（参见
        /// `DeathWraithCombatLoadout` 克隆 AI 后手动回填）。所以 `GetComponentInChildren`
        /// 回退是**主路径而非冗余**，不要当成多余分支删掉；且必须传 `true`，
        /// 因为隔离期选手处于 deactivate 状态，不含未激活对象的重载同样取不到。
        /// </summary>
        private static AICharacterController ResolveAi(CharacterMainControl character)
        {
            if (character == null) return null;
            try
            {
                AICharacterController cached = character.aiCharacterController;
                if (cached != null) return cached;
                return character.GetComponentInChildren<AICharacterController>(true);
            }
            catch (Exception)
            {
                // 角色尚未装配 AI 组件：口令与伤病窗口会因 _ai == null 自然拒绝
                return null;
            }
        }

        #endregion
    }
}
