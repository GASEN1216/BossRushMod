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

        private IEnumerator RunModeG()
        {
            Stopwatch sw = Stopwatch.StartNew();
            string reason;
            bool started = _host.ValidationStartModeG(out reason);
            yield return WaitSeconds(5f);
            ModeGInteractable.CloseActiveConfirmation();
            ModeGInteractable.CloseActiveConfirmation();
            ModeGAbandonPresenter.CloseIfOpen();
            ModeGAbandonPresenter.CloseIfOpen();
            Record("MODE_G_LIFECYCLE", started ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                "modal_leases=" + ZombieModeUIHelper.ModalInputLeaseCount, reason);
            _host.ValidationEndModeG();
            _host.ValidationSafeCleanup();
        }

        private IEnumerator RunModeH(bool expectCache)
        {
            string id = expectCache ? "MODE_H_CACHE_HIT" : "MODE_H_FIRST_CERTIFICATION";
            Stopwatch sw = Stopwatch.StartNew();
            if (!expectCache)
            {
                string invalidateError;
                if (!ModeHProductionCertification.InvalidateCache(out invalidateError))
                {
                    Record(id, "FAIL", sw.ElapsedMilliseconds, "cache_invalidated=false",
                        "无法清除旧认证缓存:" + invalidateError);
                    yield break;
                }
            }
            ModeHSupportedMap map;
            if (!ModeHEntry.ResolveTargetMap(SceneManager.GetActiveScene().name, out map) || map == null)
            {
                Record(id, "FAIL", sw.ElapsedMilliseconds, string.Empty, "map_unsupported");
                yield break;
            }
            BossRushMapSelectionHelper.FreezeModeHEntryIntent(map.SceneName, map.SceneId);
            _host.ModeHRuntime.OnSceneLoaded(new SceneRuntimeContext(SceneManager.GetActiveScene(), LoadSceneMode.Single));
            float timeout = expectCache ? CaseTimeoutSeconds : ModeHTimeoutSeconds;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline && !ShouldAbort())
            {
                ModeHRunState state = _host.ModeHRuntime.RunState;
                if (state != null && state.Lifecycle == ModeHLifecycle.Drafting) break;
                // 认证已拒绝会清空状态并返基地，不再原地等满 180 秒。
                if (!_host.ModeHRuntime.HasActiveRun || SceneLoader.IsSceneLoading
                    || !string.Equals(SceneManager.GetActiveScene().name, map.SceneName, System.StringComparison.Ordinal)) break;
                yield return null;
            }
            ModeHRunState finalState = _host.ModeHRuntime.RunState;
            bool drafting = finalState != null && finalState.Lifecycle == ModeHLifecycle.Drafting;
            bool cacheMatch = _host.ModeHRuntime.LastCertificationUsedCache == expectCache;
            if (expectCache)
            {
                if (drafting) yield return RunModeHStarterKits(map);
                else Record("MODE_H_STARTER_KITS", "SKIP", 0L, string.Empty, "certified_drafting_not_ready");
            }
            bool archived = drafting && _host.ModeHRuntime.DebugFinishValidationSeason();
            bool intentCleared = !BossRushMapSelectionHelper.HasPendingModeHEntryIntent();
            Record(id, drafting && cacheMatch && archived && intentCleared ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                "drafting=" + drafting + ",cache=" + _host.ModeHRuntime.LastCertificationUsedCache
                    + ",cache_invalidated=" + (!expectCache)
                    + ",intent_cleared=" + intentCleared
                    + ",archived=" + archived + ",exit_reason=" + _host.ModeHRuntime.LastExitReasonId,
                !intentCleared ? "entry_intent_not_consumed"
                    : (drafting ? (cacheMatch ? string.Empty : "cache_expectation_mismatch") : "certification_timeout_or_abort"));
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
