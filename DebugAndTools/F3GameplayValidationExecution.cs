using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Saves;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private bool _running, _closingSession, _slotChanged, _hostLost, _reportWriteFailed;
        private bool _sessionSubscribed;
        private bool _sessionCompleted;
        private int _sessionSlot, _runtimeErrors, _externalErrors, _errorSamples, _textDiagnostics;
        private float _nextHeartbeat;
        private ValidationCoroutineStack _sessionStack;
        private readonly Dictionary<Health, bool> _protectedPlayers = new Dictionary<Health, bool>();

        private bool BeginSession(out string reason)
        {
            reason = null;
            _reportWriteFailed = _slotChanged = _hostLost = _closingSession = false;
            _sessionCompleted = false;
            _runtimeErrors = _externalErrors = _errorSamples = _textDiagnostics = 0;
            _coverage = null;
            _sessionSlot = SavesSystem.CurrentSlot;
            try
            {
                InitializeSessionReport();
                if (_reportWriteFailed) throw new IOException("report_not_writable");
                if (!WriteRunMarker()) throw new IOException("run_marker_not_writable");
                _running = true;
                _nextHeartbeat = Time.realtimeSinceStartup + 10f;
                if (!_sessionSubscribed)
                {
                    SavesSystem.OnSetFile += OnSessionSetFile;
                    Application.logMessageReceived += OnSessionLog;
                    _sessionSubscribed = true;
                }
                ProtectCurrentPlayer();
                WriteRaw("SESSION | slot=" + _sessionSlot + " | assisted=true | player_invincible=true | player_health_refill=true | combat_balance=MANUAL_PENDING");
                return true;
            }
            catch (Exception e)
            {
                reason = "无法启动验收: " + e.Message;
                Record("RUN_MARKER", "FAIL", 0L, string.Empty, e.ToString());
                CompleteSession();
                return false;
            }
        }

        private void InitializeSessionReport()
        {
            if (_host != null) _host.GameplayValidationSuppressNotifications = true;
            _runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            _reportPath = Path.Combine(Application.persistentDataPath, "BossRushTestReports",
                "BossRushValidation_" + _runId + ".log");
            _lastReportPath = _reportPath;
            _suiteStartedAt = Time.realtimeSinceStartup;
            _passed = _failed = _skipped = _warnings = 0;
            _baselineP95Ms = _finalP95Ms = _peakFrameMs = 0f;
            _peakStage = string.Empty;
            _baselineMemory = _finalMemory = 0L;
            _dirtyStreak = 0;
            _suiteTimedOut = false;
            _failedIds.Clear();
            _skippedIds.Clear();
            ResetLeakBaselines();
            Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
            WriteRaw("BossRush 完整玩法验收 | runId=" + _runId + " | UTC=" + DateTime.UtcNow.ToString("O"));
            WriteRaw("BUILD | mvid=" + typeof(ModBehaviour).Module.ModuleVersionId);
            RunSyncCase("COVERAGE_MANIFEST", InitializeCoverage);
        }

        private IEnumerator RunSession()
        {
            // 防止 StartCoroutine 同步结束后又把已结束的句柄赋回 _routine。
            yield return null;
            try
            {
                yield return DriveSessionPhase(RunSuite(), false);
                if (!_slotChanged && !_hostLost && _host != null)
                {
                    _closingSession = true;
                    yield return DriveSessionPhase(RunFinalChecks(), true);
                }
            }
            finally { CompleteSession(); }
        }

        private IEnumerator DriveSessionPhase(IEnumerator phase, bool cleanup)
        {
            _sessionStack = new ValidationCoroutineStack(phase);
            float deadline = Time.realtimeSinceStartup + (cleanup ? SceneTimeoutSeconds * 3f : SuiteTimeoutSeconds);
            try
            {
                while (!ShouldAbort())
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Record("SUITE_EXECUTION", "FAIL", 0L, "cleanup=" + cleanup, "phase_timeout");
                        break;
                    }
                    object current;
                    Exception error;
                    bool more = TryStep(_sessionStack, out current, out error);
                    if (error != null)
                    {
                        Record("SUITE_EXECUTION", "FAIL", 0L, "cleanup=" + cleanup, error.ToString());
                        break;
                    }
                    if (!more) break;
                    yield return current;
                }
            }
            finally { DisposeSessionStack(); }
        }

        private static bool TryStep(ValidationCoroutineStack stack, out object current, out Exception error)
        {
            current = null;
            error = null;
            try
            {
                if (!stack.MoveNext()) return false;
                current = stack.Current;
                return true;
            }
            catch (Exception e) { error = e; return false; }
        }

        private void DisposeSessionStack()
        {
            ValidationCoroutineStack stack = _sessionStack;
            _sessionStack = null;
            if (stack == null) return;
            try { stack.Dispose(); }
            catch (Exception e) { Record("SUITE_EXECUTION", "FAIL", 0L, "dispose=true", e.ToString()); }
        }

        private void CompleteSession()
        {
            if (_sessionCompleted) return;
            _sessionCompleted = true;
            DisposeSessionStack();
            if (_sessionSubscribed)
            {
                SavesSystem.OnSetFile -= OnSessionSetFile;
                Application.logMessageReceived -= OnSessionLog;
                _sessionSubscribed = false;
            }
            // 换槽后不调用会保存/归还资产的旧模式清理，也不清除新槽的运行标记。
            if (!_slotChanged && SavesSystem.CurrentSlot == _sessionSlot)
            {
                try { if (_host != null) _host.ValidationSafeCleanup(); }
                catch (Exception e) { Record("SUITE_EXECUTION", "FAIL", 0L, "final_cleanup=true", e.ToString()); }
            }
            try { PetNestExpeditionService.ResetValidationRewardBackend(); }
            catch (Exception e) { Record("SUITE_EXECUTION", "FAIL", 0L, "reward_backend_reset=true", e.ToString()); }
            try { DailyReportPersistence.SetValidationRejectStore(false); }
            catch (Exception e) { Record("SUITE_EXECUTION", "FAIL", 0L, "daily_injection_reset=true", e.ToString()); }
            RestoreProtectedPlayers();
            if (_host != null) _host.GameplayValidationSuppressNotifications = false;
            if (!_running && _routine == null && string.IsNullOrEmpty(_reportPath)) return;
            if (_runtimeErrors > 0) Record("RUNTIME_ERRORS", "FAIL", 0L, "count=" + _runtimeErrors, "查看同一日志的 RUNTIME_ERROR");
            if (_externalErrors > 0) Record("EXTERNAL_ERRORS", "WARN", 0L, "count=" + _externalErrors, "宿主/其他 Mod 异常待日志复核");
            if (_textDiagnostics > 0) Record("LOG_DIAGNOSTICS", "WARN", 0L, "count=" + _textDiagnostics,
                "查看 RUNTIME_DIAGNOSTIC；含测试故障注入，须结合用例复核");
            Finish(_failed == 0, _cancelRequested);
            _closingSession = false;
        }

        private void Update()
        {
            if (_running)
            {
                if (SavesSystem.CurrentSlot != _sessionSlot) _slotChanged = true;
                if (_slotChanged) return;
                ProtectCurrentPlayer();
                if (Time.realtimeSinceStartup >= _nextHeartbeat)
                {
                    _nextHeartbeat = Time.realtimeSinceStartup + 10f;
                    WriteRaw("HEARTBEAT | elapsed_s=" + (Time.realtimeSinceStartup - _suiteStartedAt).ToString("F1")
                        + " | " + _status);
                }
            }
            else if (!_recoveryChecked && _host != null && IsBaseScene()
                && LevelManager.Instance != null && LevelManager.AfterInit)
            {
                _recoveryChecked = true;
                RecoverInterruptedRunIfNeeded();
            }
        }

        private void OnSessionSetFile() { _slotChanged = true; }

        internal static bool HasChangedSessionSlot
        {
            get { return _instance != null && _instance._running
                && (_instance._slotChanged || SavesSystem.CurrentSlot != _instance._sessionSlot); }
        }

        internal static void OpenReportFolder()
        {
            string directory = Path.Combine(Application.persistentDataPath, "BossRushTestReports");
            Directory.CreateDirectory(directory);
            Application.OpenURL(new Uri(directory + Path.DirectorySeparatorChar).AbsoluteUri);
        }

        internal static void CopyReportPath()
        {
            GUIUtility.systemCopyBuffer = _lastReportPath;
        }

        private void OnSessionLog(string message, string trace, LogType type)
        {
            if (!_running || string.IsNullOrEmpty(message)
                || message.StartsWith("[BossRushValidation]", StringComparison.Ordinal)) return;
            bool own = message.IndexOf("BossRush", StringComparison.OrdinalIgnoreCase) >= 0
                || (!string.IsNullOrEmpty(trace) && trace.IndexOf("BossRush", StringComparison.OrdinalIgnoreCase) >= 0);
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            {
                // DevLog 的 [ERROR] 实际按 LogType.Log 输出，CriticalLog 也可能为 Warning。
                // 故障注入用例会主动产生这类记录，保留原文供复核，不能一概判为产品失败。
                if (message.IndexOf("[ERROR]", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("[CRITICAL]", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _textDiagnostics++;
                    if (_textDiagnostics <= 80)
                        WriteRaw("RUNTIME_DIAGNOSTIC | source=" + (own ? "BossRush" : "unattributed")
                            + " | " + Sanitize(message) + " | stack=" + Sanitize(trace));
                }
                return;
            }
            if (own) _runtimeErrors++; else _externalErrors++;
            if (_errorSamples++ < 80)
                WriteRaw("RUNTIME_ERROR | source=" + (own ? "BossRush" : "host_or_other_mod")
                    + " | " + Sanitize(message) + " | stack=" + Sanitize(trace));
        }

        private void ProtectCurrentPlayer()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead
                || ModeHDeathSuppressionRegistry.IsModeHOnDeadSuppressionActive(player.Health)
                || PetNestCompanionAgent.IsCompanionCharacter(player)) return;
            if (!_protectedPlayers.ContainsKey(player.Health)) _protectedPlayers.Add(player.Health, player.Health.Invincible);
            player.Health.SetInvincible(true);
            // Mode F 规则失血直接改 CurrentHealth，并不受 Invincible 保护。
            // 流程验收辅助回血，失血/难度是否合理仍由人工用例验证。
            if (player.Health.CurrentHealth < player.Health.MaxHealth)
                player.Health.SetHealth(player.Health.MaxHealth);
        }

        private void RestoreProtectedPlayers()
        {
            foreach (KeyValuePair<Health, bool> pair in _protectedPlayers)
            {
                try { if (pair.Key != null) pair.Key.SetInvincible(pair.Value); }
                catch (Exception e) { Record("SUITE_EXECUTION", "FAIL", 0L, "restore_player=true", e.ToString()); }
            }
            _protectedPlayers.Clear();
        }

        internal static void Shutdown(ModBehaviour host)
        {
            if (_instance == null || _instance._host != host) return;
            F3GameplayValidationRunner runner = _instance;
            runner._hostLost = true;
            if (runner._routine != null) runner.StopCoroutine(runner._routine);
            if (runner._running) runner.CompleteSession();
            _instance = null;
            Destroy(runner.gameObject);
        }

        private void OnDisable()
        {
            if (!_running) return;
            _hostLost = true;
            CompleteSession();
        }
    }
}
