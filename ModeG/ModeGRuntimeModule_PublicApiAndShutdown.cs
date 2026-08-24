using System;

namespace BossRush
{
    internal sealed partial class ModeGRuntimeModule
    {
        #region Public API（DeathRouting / HUD / SpawnTransaction 消费）

        public ModeGRunState State { get { return _state; } }
        public ModBehaviour Host { get { return _host; } }
        public ModeGEntryPreview Preview { get { return _preview; } }
        public ModeGWavePlan WavePlan { get { return _wavePlan; } }
        public ModeGCombatTelemetry Telemetry { get { return _telemetry; } }
        public ModeGSpawnTransaction SpawnTransaction { get { return _spawnTransaction; } }
        public ModeGAdaptiveCombat Adaptive { get { return _adaptive; } }
        public int TotalBossKills { get { return _totalBossKills; } }
        public bool IsNemesisDefeatedThisRun { get { return _nemesisDefeatedThisRun; } }

        public bool ArmVictorySafety()
        {
            if (_victorySafetyArmed) return true;
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.Health == null) return false;
                _victorySafetyHealth = player.Health;
                _victorySafetyPreviousInvincible = player.Health.Invincible;
                player.Health.SetInvincible(true);
                _victorySafetyArmed = true;
                return true;
            }
            catch { return false; }
        }

        public void ReleaseVictorySafety()
        {
            if (!_victorySafetyArmed) return;
            _victorySafetyArmed = false;
            try
            {
                if (_victorySafetyHealth != null)
                    _victorySafetyHealth.SetInvincible(_victorySafetyPreviousInvincible);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 胜利保护恢复失败: " + e.Message);
            }
            _victorySafetyHealth = null;
        }

        public void ArmStartupRefund(bool refundTicket, bool refundRelic)
        {
            _startupTicketRefundable = refundTicket;
            _startupRelicRefundable = refundRelic;
            _startupRefundSettled = false;
        }

        public void DisarmStartupRefund()
        {
            _startupRefundSettled = true;
            _startupTicketRefundable = false;
            _startupRelicRefundable = false;
        }

        public int AxisAttemptDistance { get { return _axisAttemptDistance; } }
        public int AxisAttemptAmmo { get { return _axisAttemptAmmo; } }
        public int AxisAttemptAttribute { get { return _axisAttemptAttribute; } }

        public float RunElapsedSeconds
        {
            get
            {
                if (_state == null) return 0f;
                return (float)((DateTime.UtcNow.Ticks - _state.runTimestampTicks) / (double)TimeSpan.TicksPerSecond);
            }
        }

        public bool TryGetRegisteredBossPresetKey(CharacterMainControl character, out string presetKey)
        {
            presetKey = null;
            if (_spawnTransaction == null) return false;
            return _spawnTransaction.TryGetCommittedKey(character, out presetKey);
        }

        public void RegisterManagedHandle(ManagedBossRuntimeHandle handle)
        {
            if (handle == null) return;
            lock (_managedHandles)
            {
                _managedHandles.Add(handle);
            }
        }

        public ModeGContractProgress BuildContractProgress()
        {
            ModeGContractProgress p = new ModeGContractProgress();
            if (_adaptive != null)
            {
                p.distanceResolves = _adaptive.ResolveDistance;
                p.ammoResolves = _adaptive.ResolveAmmo;
                p.attributeResolves = _adaptive.ResolveAttribute;
            }
            p.lastStandCount = _lastStandCount;
            p.nemesisR3FinalBlowDirect = _nemesisR3FinalBlowDirect;
            p.maxConsecutiveAxisBreaks = _maxAxisBreakChain;
            p.resolvesPerAct = (int[])_resolvesPerAct.Clone();
            p.distanceEchoCount = _distanceEchoCount;
            p.ammoBanCount = _ammoBanCount;
            p.attributeLockCount = _attributeLockCount;
            p.ammoBanAvailableOnNemesisWaves = _ammoBanNemesisWaveCount;
            return p;
        }

        #endregion

        #region Module Lifecycle Hooks（BossRushRuntimeModuleBase）

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            try
            {
                if (_state == null || _ended || _disposed) return;
                if (_state.lifecyclePhase == ModeGLifecyclePhase.None) return;
                bool remainsInFrozenScenePair = _preview != null
                    && string.Equals(context.SceneName, _preview.sceneName, StringComparison.Ordinal)
                    && ModeGMapSupportRegistry.IsSupported(
                        _preview.sceneName, _preview.sceneId, _preview.verificationRevision);
                if (!remainsInFrozenScenePair)
                {
                    ModBehaviour.DevLog("[ModeG] 离开本局冻结 scene pair: "
                        + (string.IsNullOrEmpty(context.SceneName) ? "<empty>" : context.SceneName));
                    End(ModeGExitReason.SceneChanged);
                }
            }
            catch { /* no-throw */ }
        }

        public override void OnDestroy()
        {
            try { ModeG.PrepareHostDestroy(); } catch { /* no-throw 契约 */ }
        }

        #endregion

        #region End（九种终局统一幂等出口）

        /// <summary>
        /// 统一幂等 End：Cleanup 状态机 -> 退订 -> Modifier 恢复 -> managed handle 清理 ->
        /// Dispatcher 复位 -> 成就 session 结束。
        /// </summary>
        public void End(ModeGExitReason reason)
        {
            if (_ended || _state == null) return;
            _ended = true;
            try
            {
                if (reason != ModeGExitReason.Victory && _state.IsRewarding)
                {
                    _state.rewardNonceInvalidated = true;
                    ModeGRewardTransaction.InvalidateAttemptNonce();
                    try
                    {
                        if (_host != null) _host.CancelModeGRewardMaterialization_LootAndRewards();
                    }
                    catch (Exception cancelException)
                    {
                        ModBehaviour.DevLog("[ModeG] [WARNING] 非胜利退出取消奖励物化失败: "
                            + cancelException.Message);
                    }
                }

                RefundStartupPaymentOnTechnicalFailure(reason);
                ModeGCleanupController.Cleanup(_state, reason);

                try
                {
                    if (_telemetry != null)
                    {
                        _telemetry.UnsubscribeCombat();
                        _telemetry.UnsubscribeDead();
                    }
                    UnsubscribePlayerDeath();
                }
                catch { }

                try { if (_adaptive != null) _adaptive.RestoreAllModifiers(); } catch { }
                try
                {
                    ReleaseVictorySafety();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 胜利保护清理失败: " + e.Message);
                }

                try
                {
                    lock (_managedHandles)
                    {
                        for (int i = 0; i < _managedHandles.Count; i++)
                        {
                            if (_managedHandles[i] != null)
                                _managedHandles[i].CleanupOnce(ManagedBossCleanupReason.RunEnded);
                        }
                        _managedHandles.Clear();
                    }
                }
                catch { }

                try { _committedAuxiliaries.Clear(); } catch { }

                try
                {
                    if (_dispatcherRef != null
                        && ReferenceEquals(ModBehaviour.ManagedBossSpawnDispatcher, _dispatcherRef))
                    {
                        ModBehaviour.ManagedBossSpawnDispatcher = null;
                    }
                }
                catch { }

                try { if (_host != null) _host.EndModeGAchievementSession(); } catch { }
                try { ModeGRewardTransaction.ResetRelicReturnGate(); } catch { }

                ModBehaviour.DevLog("[ModeG] run 结束 reason=" + reason
                    + " result=" + _state.battleResult + " wave=" + (_state.waveEpoch + 1));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] End 异常: " + e.Message);
            }
        }

        private void RefundStartupPaymentOnTechnicalFailure(ModeGExitReason reason)
        {
            if (_startupRefundSettled || _firstWaveCombatStarted || _host == null) return;
            if (reason != ModeGExitReason.SpawnExhausted
                && reason != ModeGExitReason.TechnicalIntegrityLoss) return;

            _startupRefundSettled = true;
            bool fullyRefunded = true;
            if (_startupTicketRefundable)
            {
                fullyRefunded = _host.TryRefundModeGStartupItem(
                    _host.GetModeGTicketTypeId(), L10n.T("船票", "Boss Rush Ticket"))
                    && fullyRefunded;
            }
            if (_startupRelicRefundable)
            {
                fullyRefunded = _host.TryRefundModeGStartupItem(
                    FateEchoRelicConfig.TYPE_ID, L10n.T("宿命回响信物", "Fate Echo Relic"))
                    && fullyRefunded;
            }
            _startupTicketRefundable = false;
            _startupRelicRefundable = false;
            _host.ShowMessage(fullyRefunded
                ? L10n.T(
                    "宿命回响首波未能启动，已返还入场道具。",
                    "Fate Echo failed before wave one; entry items were refunded.")
                : L10n.T(
                    "宿命回响首波未能启动，入场道具返还未完整交付，请保留日志。",
                    "Fate Echo failed before wave one; entry-item refund was incomplete. Keep the log."));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (!_ended && _state != null)
                {
                    End(_state.exitReason != ModeGExitReason.None
                        ? _state.exitReason
                        : ModeGExitReason.ModDestroyed);
                }
            }
            catch { }
            try { UnsubscribePlayerDeath(); } catch { }
            _snapshot = null;
        }

        #endregion
    }
}
