// ============================================================================
// F3GameplayValidationModes.cs - 完整验收的各模式生命周期用例
// ============================================================================
// 模块说明：
//   Mode D/E/F/G/H/丧尸的入场—存活敌人—清理链验收。从主 runner 拆出来，
//   让主 runner 守住 1200 行预算（LargeFileBudgetGuard），内容逐字保持原语义。
//
//   每个用例都由 RunIsolatedCase 包壳调用（见 F3GameplayValidationStages.cs），
//   所以这里不需要自己做异常兜底，但仍必须在退出前 ValidationSafeCleanup。
// ============================================================================

using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private IEnumerator RunModeD()
        {
            Stopwatch sw = Stopwatch.StartNew();
            bool started = false;
            try
            {
                _host.StartModeD();
                started = _host.IsModeDActive && _host.ModeDStartNextWave();
                yield return WaitSeconds(8f);
                string hostileDetails;
                int enemies = _host.ValidationCountHostileCharacters(out hostileDetails);
                string trackedDetails;
                int playable = _host.ValidationCountPlayableModeDEnemies(out trackedDetails);
                bool passed = started && _host.IsModeDActive && playable > 0 && enemies > 0;
                Record("MODE_D_LIFECYCLE", passed ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                    "wave=" + _host.ModeDWaveIndex + ",enemies=" + enemies
                        + ",playable_tracked=" + playable + ",hostiles=" + hostileDetails
                        + ",tracked_details=" + trackedDetails,
                    !started ? "start_rejected" : (passed ? string.Empty
                        : "mode_d_has_no_active_alive_hostile_tracked_enemy"));
            }
            finally { _host.ValidationSafeCleanup(); }
        }

        private IEnumerator RunModeE()
        {
            Stopwatch sw = Stopwatch.StartNew();
            bool started = _host.ValidationStartModeE();
            yield return WaitSeconds(8f);
            string hostileDetails;
            int enemies = _host.ValidationCountHostileCharacters(out hostileDetails);
            Record("MODE_E_LIFECYCLE", started && _host.IsModeEActive && enemies > 0 ? "PASS" : "FAIL",
                sw.ElapsedMilliseconds, "enemies=" + enemies + ",hostiles=" + hostileDetails,
                started ? string.Empty : "start_rejected");
            _host.ValidationSafeCleanup();
        }

        private IEnumerator RunModeF()
        {
            Stopwatch sw = Stopwatch.StartNew();
            bool started = _host.ValidationStartModeF();
            yield return WaitSeconds(8f);
            string bloodfireMetrics;
            bool bloodfire = _host.DebugValidateModeFBloodfire(out bloodfireMetrics);
            string hostileDetails;
            int enemies = _host.ValidationCountHostileCharacters(out hostileDetails);
            Record("MODE_F_LIFECYCLE", started && _host.IsModeFActive && enemies > 0 ? "PASS" : "FAIL",
                sw.ElapsedMilliseconds, "enemies=" + enemies + ",hostiles=" + hostileDetails,
                started ? string.Empty : "start_rejected");
            Record("MODE_F_BLOODFIRE", bloodfire ? "PASS" : "FAIL", 0L, bloodfireMetrics,
                bloodfire ? string.Empty : "speed_modifier_validation_failed");
            _host.ValidationSafeCleanup();
        }

        private IEnumerator RunModeG() { return RunModeGNineWaves(); }

        private IEnumerator RunModeH(bool fullSeason)
        {
            string id = fullSeason ? "MODE_H_PLAYER_REENTRY" : "MODE_H_PLAYER_ENTRY";
            Stopwatch sw = Stopwatch.StartNew();
            ModeHSupportedMap map;
            if (!ModeHEntry.ResolveTargetMap(SceneManager.GetActiveScene().name, out map) || map == null)
            {
                Record(id, "FAIL", sw.ElapsedMilliseconds, string.Empty, "map_unsupported");
                yield break;
            }
            BossRushMapSelectionHelper.FreezeModeHEntryIntent(map.SceneName, map.SceneId);
            _host.ModeHRuntime.OnSceneLoaded(new SceneRuntimeContext(SceneManager.GetActiveScene(), LoadSceneMode.Single));
            float deadline = Time.realtimeSinceStartup + CaseTimeoutSeconds;
            bool sawDiagnostics = _host.ModeHRuntime.IsCertificationDiagnosticRunning;
            while (Time.realtimeSinceStartup < deadline && !ShouldAbort())
            {
                sawDiagnostics |= _host.ModeHRuntime.IsCertificationDiagnosticRunning;
                ModeHRunState state = _host.ModeHRuntime.RunState;
                if (state != null && state.Lifecycle == ModeHLifecycle.Drafting) break;
                if ((!_host.ModeHRuntime.HasActiveRun && !_host.ModeHRuntime.IsSceneEntryPending) || SceneLoader.IsSceneLoading
                    || !string.Equals(SceneManager.GetActiveScene().name, map.SceneName, System.StringComparison.Ordinal)) break;
                yield return null;
            }
            ModeHRunState finalState = _host.ModeHRuntime.RunState;
            bool drafting = finalState != null && finalState.Lifecycle == ModeHLifecycle.Drafting;
            Record(id, drafting && !sawDiagnostics ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                "drafting=" + drafting + ",dynamic_diagnostics=" + sawDiagnostics,
                !drafting ? "player_entry_timeout_or_abort" : (sawDiagnostics ? "player_entry_started_diagnostics" : null));
            if (fullSeason)
            {
                if (drafting) yield return RunModeHStarterKits(map);
                else Record("MODE_H_STARTER_KITS", "SKIP", 0L, string.Empty, "player_drafting_not_ready");
                if (drafting) yield return RunModeHErrorSwap(map);
                else Record("MODE_H_ERROR_SWAP", "SKIP", 0L, string.Empty, "player_drafting_not_ready");
                if (drafting) yield return RunModeHFullSeason();
                else Record("MODE_H_FULL_SEASON", "SKIP", 0L, string.Empty, "player_drafting_not_ready");
            }
            bool archived = fullSeason ? !_host.ModeHRuntime.HasActiveRun
                : drafting && _host.ModeHRuntime.DebugFinishValidationSeason();
            bool intentCleared = !BossRushMapSelectionHelper.HasPendingModeHEntryIntent();
            Record(id + "_CLEANUP", drafting && !sawDiagnostics && archived && intentCleared ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                "drafting=" + drafting + ",dynamic_diagnostics=" + sawDiagnostics
                    + ",intent_cleared=" + intentCleared
                    + ",archived=" + archived + ",exit_reason=" + _host.ModeHRuntime.LastExitReasonId,
                !intentCleared ? "entry_intent_not_consumed"
                    : (!archived ? "season_not_archived:" + ModeHSaveFlushCoordinator.LastError
                    : (sawDiagnostics ? "player_entry_started_diagnostics" : string.Empty)));
            _host.ValidationSafeCleanup();
            yield return WaitSeconds(0.5f);
        }

        private IEnumerator RunZombie()
        {
            Stopwatch sw = Stopwatch.StartNew();
            string reason;
            bool started = _host.ValidationStartZombie(out reason);
            yield return WaitSeconds(6f);
            Record("MODE_ZOMBIE_LIFECYCLE", started && _host.IsZombieModeActive ? "PASS" : "FAIL",
                sw.ElapsedMilliseconds, "active=" + _host.IsZombieModeActive, reason);
            _host.ValidationSafeCleanup();
        }
    }
}
