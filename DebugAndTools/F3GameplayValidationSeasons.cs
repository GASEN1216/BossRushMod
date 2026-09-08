using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private IEnumerator RunModeGNineWaves()
        {
            Stopwatch sw = Stopwatch.StartNew();
            string reason;
            bool started = _host.ValidationStartModeG(out reason);
            ModeGRuntimeModule module = ModeGRunContext.CurrentModule;
            ModeGRunState state = module != null ? module.State : null;
            bool[] seen = new bool[ModeGWavePlan.WaveCount];
            int waves = 0;
            bool playable = false;
            try
            {
                while (started && state != null && !state.IsTerminal && sw.Elapsed.TotalSeconds < 580 && !ShouldAbort())
                {
                    if (state.IsCombatActive && state.AreAllSlotsResolved)
                    {
                        List<CharacterMainControl> bosses = ModeGRuntimeGates.GetTrackedBosses();
                        int wave = state.waveEpoch;
                        if (wave < 0 || wave >= seen.Length) { reason = "invalid_wave_epoch"; break; }
                        if (!seen[wave])
                        {
                            bool active = state.SlotCommitted > 0 && bosses.Count == state.SlotCommitted;
                            foreach (CharacterMainControl boss in bosses)
                                active &= IsLiveCharacter(boss) && Team.IsEnemy(Teams.player, boss.Team);
                            if (!active) { reason = "committed_wave_not_playable:" + wave; break; }
                            seen[wave] = true;
                            waves++;
                            playable = true;
                            WriteRaw("MODE_G_WAVE | wave=" + (wave + 1) + " | committed=" + state.SlotCommitted);
                            yield return WaitSeconds(1f);
                        }
                        foreach (CharacterMainControl boss in bosses)
                        {
                            if (ShouldAbort() || !state.IsCombatActive) break;
                            if (IsLiveCharacter(boss)) ApplyValidationDamage(boss, CharacterMainControl.Main);
                            yield return null;
                        }
                    }
                    yield return WaitSeconds(0.2f);
                }
                ModeGProfilePersistence.ProfileDto profile = ModeGProfilePersistence.LoadOrInit();
                bool recorded = state != null && profile != null && profile.lastBattleResultToken == "victory_" + state.runId.ToString("x")
                    && profile.bestWaveReached == ModeGWavePlan.WaveCount && !ModeGProfilePersistence.IsStoreFaulted;
                bool victory = state != null && state.IsTerminal && state.IsVictory && state.exitReason == ModeGExitReason.Victory;
                string metrics = "waves=" + waves + ",victory=" + victory + ",profile=" + recorded
                    + ",kills=" + (module != null ? module.TotalBossKills : 0)
                    + ",exit=" + (state != null ? state.exitReason.ToString() : "no_state") + ",assisted=true";
                Record("MODE_G_LIFECYCLE", playable ? "PASS" : "FAIL", sw.ElapsedMilliseconds, metrics, reason);
                Record("MODE_G_NINE_WAVES", waves == ModeGWavePlan.WaveCount && victory && recorded ? "PASS" : "FAIL",
                    sw.ElapsedMilliseconds, metrics, reason ?? (victory ? null : "nine_waves_or_reward_terminal_not_reached"));
            }
            finally { _host.ValidationSafeCleanup(); }
        }

        private IEnumerator RunModeHFullSeason()
        {
            Stopwatch sw = Stopwatch.StartNew();
            ModeHRuntimeModule runtime = _host.ModeHRuntime;
            ModeHRunState initial = runtime.RunState;
            string runId = initial != null ? initial.RunId : null;
            HashSet<int> fights = new HashSet<int>();
            HashSet<int> transfers = new HashSet<int>();
            bool hall = false;
            int draftPick = 0;
            string reason = null;
            ModeHLifecycle previous = ModeHLifecycle.Unknown;
            float lastProgress = Time.realtimeSinceStartup;
            int previousMatch = -1;
            while (runtime.HasActiveRun && sw.Elapsed.TotalSeconds < 700 && !ShouldAbort())
            {
                ModeHRunState state = runtime.RunState;
                if (state == null || state.RunId != runId) { reason = "season_owner_changed"; break; }
                if (state.Lifecycle != previous || state.MatchIndex != previousMatch)
                {
                    previous = state.Lifecycle;
                    previousMatch = state.MatchIndex;
                    lastProgress = Time.realtimeSinceStartup;
                    WriteRaw("MODE_H_FLOW | match=" + state.MatchIndex + " | phase=" + state.Lifecycle
                        + " | stake_slot_ready=" + ModeHWarehouseStakeJournal.IsSlotConsistent
                        + " | stake_slot_deferred=" + ModeHWarehouseStakeJournal.IsSlotConsistencyDeferred
                        + " | stake_slot_reason=" + ModeHWarehouseStakeJournal.SlotInconsistentReasonId);
                }
                if (Time.realtimeSinceStartup - lastProgress > 100f) { reason = "phase_stalled:" + state.Lifecycle; break; }
                string confirm = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm");
                string lockIn = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_LockIn");
                switch (state.Lifecycle)
                {
                    case ModeHLifecycle.Drafting:
                        if (TryClickModeHButton("ModeH_CardAction_" + draftPick, null)) draftPick = 1;
                        break;
                    case ModeHLifecycle.RosterLocked:
                        TryClickModeHButton(null, confirm);
                        break;
                    case ModeHLifecycle.MatchBrief:
                    case ModeHLifecycle.LoadoutEditing:
                    case ModeHLifecycle.OddsPreview:
                        // 不选择真实押品：点击实际页面的锁盘按钮，走公开零押品路线。
                        TryClickModeHButton(null, lockIn);
                        break;
                    case ModeHLifecycle.MatchFighting:
                        CharacterMainControl[] allies = ModeHEventRouter.GetParticipantsForValidation(false);
                        CharacterMainControl[] enemies = ModeHEventRouter.GetParticipantsForValidation(true);
                        CharacterMainControl attacker = null;
                        foreach (CharacterMainControl ally in allies)
                            if (IsLiveCharacter(ally)) { attacker = ally; break; }
                        if (attacker != null && enemies.Length > 0)
                        {
                            foreach (CharacterMainControl enemy in enemies)
                            {
                                if (state.Lifecycle != ModeHLifecycle.MatchFighting || ShouldAbort()) break;
                                if (!IsLiveCharacter(enemy)) continue;
                                if (!Team.IsEnemy(attacker.Team, enemy.Team)) { reason = "participants_not_hostile"; break; }
                                fights.Add(state.MatchIndex);
                                ApplyValidationDamage(enemy, attacker);
                                yield return null;
                            }
                        }
                        break;
                    case ModeHLifecycle.Intermission:
                        if (!TryClickModeHButton(null, L10n.T("拒绝战痕，换取名声", "Decline scar for fame"))
                            && !TryClickModeHButton(null, L10n.T("放弃整备，换取名声", "Decline kits for fame")))
                            TryClickModeHButton(null, confirm);
                        break;
                    case ModeHLifecycle.TransferWindow:
                        transfers.Add(state.MatchIndex);
                        if (!TryClickModeHButton(null, L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Cancel")))
                            TryClickModeHButton(null, confirm);
                        break;
                    case ModeHLifecycle.HallOfFame:
                        hall = true;
                        TryClickModeHButton(null, confirm);
                        break;
                    case ModeHLifecycle.Suspended:
                        reason = "season_suspended:" + runtime.LastExitReasonId;
                        break;
                }
                if (reason != null) break;
                yield return WaitSeconds(0.25f);
            }
            float deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
            while (!IsRuntimeReady(BaseSceneNameForValidation()) && Time.realtimeSinceStartup < deadline && !ShouldAbort())
            {
                if (runtime.HasActiveRun) break;
                yield return null;
            }
            ModeHSeasonDto season = ModeHProfilePersistence.LoadCurrent();
            int reports = 0;
            HashSet<string> tokens = new HashSet<string>();
            HashSet<string> rewards = new HashSet<string>();
            bool exactOnce = season != null && season.runState != null && season.runState.runId == runId;
            if (exactOnce && season.matchReports != null)
                foreach (ModeHMatchReportDto report in season.matchReports)
                {
                    exactOnce &= report != null && report.reportStatus == (int)ModeHMatchReportStatus.Archived
                        && report.winner == (int)ModeHMatchOutcome.PlayerVictory && string.IsNullOrEmpty(report.stakeTxId)
                        && !string.IsNullOrEmpty(report.resultToken) && tokens.Add(report.resultToken)
                        && !string.IsNullOrEmpty(report.seasonRewardOperationId) && rewards.Add(report.seasonRewardOperationId);
                    reports++;
                }
            int rewardCount = 0;
            if (season != null && season.seasonRewardOperations != null)
                foreach (ModeHSeasonRewardOperationDto operation in season.seasonRewardOperations)
                {
                    exactOnce &= operation != null && operation.status == (int)ModeHSeasonRewardOperationStatus.Archived
                        && rewards.Remove(operation.operationId);
                    rewardCount++;
                }
            exactOnce &= reports == ModeHConfig.SeasonMatchCount && rewardCount == reports && rewards.Count == 0;
            bool clean = !runtime.HasActiveRun && IsRuntimeReady(BaseSceneNameForValidation())
                && ModeHEventRouter.ParticipantCount == 0 && ModeHEventRouter.DiagnosticCount == 0
                && !ModeHProfilePersistence.IsStoreFaulted && !ModeHProfilePersistence.IsWriteBarrier;
            bool passed = fights.Count == ModeHConfig.SeasonMatchCount && transfers.Contains(2) && transfers.Contains(4)
                && hall && exactOnce && clean;
            Record("MODE_H_FULL_SEASON", passed ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                "fights=" + fights.Count + ",reports=" + reports + ",transfer2=" + transfers.Contains(2)
                    + ",transfer4=" + transfers.Contains(4) + ",hall=" + hall + ",exact_once=" + exactOnce
                    + ",base_ready=" + clean + ",assisted=true,real_stake=false",
                reason ?? (passed ? null : "six_match_season_incomplete"));
        }

        private bool TryClickModeHButton(string objectName, string label)
        {
            GameObject page = GameObject.Find("ModeH_Modal");
            if (page == null) return false;
            foreach (Button button in page.GetComponentsInChildren<Button>(false))
            {
                if (!button.isActiveAndEnabled || !button.IsInteractable()) continue;
                if (objectName != null && button.name != objectName) continue;
                TMP_Text text = button.GetComponentInChildren<TMP_Text>();
                if (label != null && (text == null || text.text != label)) continue;
                WriteRaw("MODE_H_UI_ACTION | " + button.name + " | " + (text != null ? text.text : string.Empty));
                button.onClick.Invoke();
                return true;
            }
            return false;
        }

        private static bool IsLiveCharacter(CharacterMainControl character)
        {
            return character != null && character.gameObject.activeInHierarchy
                && character.Health != null && !character.Health.IsDead;
        }

        private static void ApplyValidationDamage(CharacterMainControl target, CharacterMainControl attacker)
        {
            DamageInfo damage = new DamageInfo(attacker);
            damage.damageValue = target.Health.MaxHealth * 10f;
            damage.ignoreArmor = true;
            damage.isFromBuffOrEffect = true;
            damage.fromWeaponItemID = 0;
            damage.toDamageReceiver = target.mainDamageReceiver;
            damage.damagePoint = target.transform.position;
            target.Health.Hurt(damage);
        }
    }
}
