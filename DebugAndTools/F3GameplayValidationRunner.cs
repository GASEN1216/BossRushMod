using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using ItemStatsSystem;
using Saves;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private Text f3GameplayValidationStatusText;
        internal bool GameplayValidationSuppressNotifications;

        private void BuildF3GameplayValidationPage()
        {
            Font font = BossRushUI.GetLegacyChineseFont();
            GameObject section = CreateF3Section(
                L10n.T("完整玩法验收", "Full Gameplay Validation"),
                L10n.T(
                    "仅限 Dev 构建、基地和专用测试档。会切图、推进并保存测试档。自动验收同时生成完整人工清单；自动 PASS 不代表所有功能已验收。",
                    "Dev build, base scene and marked test save required. Changes and saves that slot. Includes a manual checklist; automatic PASS does not mean full coverage."),
                font);

            f3GameplayValidationStatusText = CreateLabel(
                section.transform, font, F3GameplayValidationRunner.GetStatusText(), 17,
                new Color(0.84f, 0.89f, 0.95f, 1f), FontStyle.Normal);

            GameObject row1 = CreateF3Row(section.transform);
            CreateActionButton(row1.transform, font,
                L10n.T("将当前槽标记为专用测试档", "Mark Current Slot as Test Save"),
                new Color(0.42f, 0.34f, 0.18f, 1f), MarkCurrentSlotForGameplayValidation);

            GameObject row2 = CreateF3Row(section.transform);
            CreateActionButton(row2.transform, font,
                L10n.T("自动验收 + 完整待测清单", "Auto Validation + Full Checklist"),
                new Color(0.22f, 0.44f, 0.28f, 1f), StartFullGameplayValidationFromF3);
            CreateActionButton(row2.transform, font,
                L10n.T("取消并安全清理", "Cancel and Safe Cleanup"),
                new Color(0.44f, 0.22f, 0.22f, 1f), CancelFullGameplayValidationFromF3);
        }

        private void MarkCurrentSlotForGameplayValidation()
        {
            string reason;
            bool ok = F3GameplayValidationRunner.TryMarkCurrentSlot(this, out reason);
            SetF3DebugCheatStatus(reason, !ok);
            RefreshF3GameplayValidationStatus();
        }

        private void StartFullGameplayValidationFromF3()
        {
            string reason;
            if (!F3GameplayValidationRunner.TryStart(this, out reason))
            {
                SetF3DebugCheatStatus(reason, true);
                RefreshF3GameplayValidationStatus();
                return;
            }
            HideF3DebugCheatMenu();
        }

        private void CancelFullGameplayValidationFromF3()
        {
            string reason;
            bool ok = F3GameplayValidationRunner.TryCancel(out reason);
            SetF3DebugCheatStatus(reason, !ok);
            RefreshF3GameplayValidationStatus();
        }

        private void RefreshF3GameplayValidationStatus()
        {
            if (f3GameplayValidationStatusText != null)
                f3GameplayValidationStatusText.text = F3GameplayValidationRunner.GetStatusText();
        }

        internal bool ValidationHasActiveMode(out string reason)
        {
            reason = null;
            if (IsActive) { reason = "BossRush"; return true; }
            if (modeDActive) { reason = "ModeD"; return true; }
            if (modeEActive) { reason = "ModeE"; return true; }
            if (modeFActive) { reason = "ModeF"; return true; }
            if (modeGActive || ModeGRuntimeGates.IsModeGEntryBlocked) { reason = "ModeG"; return true; }
            if (IsZombieModeActive || IsZombieModeStartupInProgress()) { reason = "Zombie"; return true; }
            if (ModeHRuntime != null && ModeHRuntime.HasActiveRun) { reason = "ModeH"; return true; }
            if (campaignFinalBossActive) { reason = "CampaignFinal"; return true; }
            return false;
        }

        internal void ValidationSafeCleanup()
        {
            try { PetNestRuntimeModule.CloseAllInteractiveViewsForSceneChange(); }
            catch (Exception e) { DevLog("[Validation] PetNest UI 清理失败: " + e.Message); }
            try { ModeGInteractable.CloseActiveConfirmation(); }
            catch (Exception e) { DevLog("[Validation] ModeG 确认页清理失败: " + e.Message); }
            try { ModeGAbandonPresenter.CloseIfOpen(); }
            catch (Exception e) { DevLog("[Validation] ModeG 放弃页清理失败: " + e.Message); }
            try
            {
                if (RandomEventsRuntime != null && RandomEventsRuntime.Director != null)
                    RandomEventsRuntime.Director.ForceEndActive();
            }
            catch (Exception e) { DevLog("[Validation] 随机事件清理失败: " + e.Message); }
            try { CleanupCampaignFinalBoss(true); }
            catch (Exception e) { DevLog("[Validation] Campaign 清理失败: " + e.Message); }
            try { if (modeDActive) EndModeD(); }
            catch (Exception e) { DevLog("[Validation] ModeD 清理失败: " + e.Message); }
            try { if (modeEActive) EndModeE(false); }
            catch (Exception e) { DevLog("[Validation] ModeE 清理失败: " + e.Message); }
            try { if (modeFActive) ExitModeF(false); }
            catch (Exception e) { DevLog("[Validation] ModeF 清理失败: " + e.Message); }
            try
            {
                if (modeGRuntime != null) modeGRuntime.End(ModeGExitReason.ManualExit);
                ShutdownModeG();
            }
            catch (Exception e) { DevLog("[Validation] ModeG 清理失败: " + e.Message); }
            try { DebugResetZombieModeShell(); }
            catch (Exception e) { DevLog("[Validation] Zombie 清理失败: " + e.Message); }
            try
            {
                if (ModeHRuntime != null && ModeHRuntime.HasActiveRun)
                {
                    // 验证期间不能调 RequestExit，它会切回基地打断后续用例。
                    // 直接置空状态让运行时停摆，切场景由验证套件自己控制。
                    ModeHRuntime.ForceResetStateForValidation();
                }
            }
            catch (Exception e) { DevLog("[Validation] ModeH 清理失败: " + e.Message); }
            try
            {
                SetBossRushRuntimeActive(false);
                bossRushArenaActive = false;
                bossRushArenaPlanned = false;
                Health.OnDead -= OnEnemyDiedWithDamageInfo;
                string sceneName = SceneManager.GetActiveScene().name;
                if (string.Equals(sceneName, BossRushArenaSceneName, StringComparison.Ordinal))
                    ClearEnemiesForBossRush();
            }
            catch (Exception e) { DevLog("[Validation] BossRush 清理失败: " + e.Message); }
            try { BossBgmCoordinator.ResetStaticCaches(); }
            catch (Exception e) { DevLog("[Validation] BGM 清理失败: " + e.Message); }
            try { PetNestExpeditionService.ResetValidationRewardBackend(); }
            catch (Exception e) { DevLog("[Validation] 奖励后端复位失败: " + e.Message); }
            try { DailyReportPersistence.SetValidationRejectStore(false); }
            catch (Exception e) { DevLog("[Validation] 日报存储注入复位失败: " + e.Message); }
        }

        /// <summary>
        /// Dev 验收只统计实际敌对玩家的存活角色。场景里的友军、宠物、设施 NPC
        /// 不是模式清场债务；旧口径把它们统统算作敌人，会在清掉最后一只 Boss 后假失败。
        /// </summary>
        internal int ValidationCountHostileCharacters(out string details)
        {
            details = string.Empty;
            if (string.Equals(SceneManager.GetActiveScene().name, BaseSceneName, StringComparison.Ordinal))
                return 0;
            int count = 0;
            List<string> labels = new List<string>();
            CharacterMainControl[] all = FindObjectsOfType<CharacterMainControl>();
            for (int i = 0; i < all.Length; i++)
            {
                CharacterMainControl c = all[i];
                if (c == null || c == CharacterMainControl.Main) continue;
                try
                {
                    if (c.Health == null || c.Health.IsDead) continue;
                    if (PetNestCompanionAgent.IsCompanionCharacter(c)) continue;
                    if (!Team.IsEnemy(Teams.player, c.Team)) continue;

                    count++;
                    string preset = c.characterPreset != null ? c.characterPreset.nameKey : "no_preset";
                    labels.Add(c.gameObject.name + "@" + c.Team + "/" + preset);
                }
                catch (Exception e)
                {
                    // 无法证明是友军时 fail-closed，并把具体实例写入报告方便定位。
                    count++;
                    labels.Add((c.gameObject != null ? c.gameObject.name : "unknown")
                        + "@query_error:" + e.GetType().Name);
                }
            }
            details = string.Join(";", labels.ToArray());
            return count;
        }

        internal int ValidationTemporaryModifierCount
        {
            get { return modeFBloodfireModifiers != null ? modeFBloodfireModifiers.Count : 0; }
        }

        internal bool ValidationStartModeE() { return StartModeE(Teams.scav); }
        internal bool ValidationStartModeF() { return StartModeF(); }

        internal bool ValidationStartModeG(out string reason)
        {
            reason = null;
            ModeGEntryPreview preview = GetOrCreateModeGEntryPreview();
            if (!IsModeGEntryPreviewValidForCurrentScene(preview))
            {
                reason = "preview_invalid";
                return false;
            }
            bool runtimeOwnsRefund;
            bool ok = StartModeGRuntime(preview, false, false, out runtimeOwnsRefund);
            if (!ok) reason = "runtime_start_rejected";
            return ok;
        }

        internal void ValidationEndModeG()
        {
            if (modeGRuntime != null) modeGRuntime.End(ModeGExitReason.ManualExit);
            ShutdownModeG();
        }

        internal bool ValidationStartZombie(out string reason)
        {
            reason = null;
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                int runId = BeginZombieModeRunShell(scene.buildIndex, scene.name);
                zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun;
                zombieModeRunState.CombatPhase = ZombieModeCombatPhase.None;
                zombieModeRunState.PurificationPoints = ZOMBIE_MODE_INITIAL_PURIFICATION_POINTS;
                if (!InitializeZombieModeRunAfterMapLoaded(runId))
                {
                    reason = "initialize_failed";
                    DebugResetZombieModeShell();
                    return false;
                }
                SelectZombieModeStarterLoadout(runId, ZombieModeStarterLoadout.Gunner);
                return true;
            }
            catch (Exception e)
            {
                reason = e.GetType().Name + ":" + e.Message;
                try { DebugResetZombieModeShell(); }
                catch (Exception cleanupError) { DevLog("[Validation] Zombie 失败回滚异常: " + cleanupError.Message); }
                return false;
            }
        }
    }

    internal sealed partial class F3GameplayValidationRunner : MonoBehaviour
    {
        private const string DedicatedSlotKey = "BossRush_Validation_DedicatedSlot_v1";
        private const string RunMarkerKey = "BossRush_Validation_RunMarker_v1";
        private const float SuiteTimeoutSeconds = 2700f;
        private const float SceneTimeoutSeconds = 90f;
        // 「点击继续」喂点击的间隔。官方那一屏只要收到一次点击就会继续，
        // 但加载完成的时刻不可预知，所以按固定节奏重复喂而不是只喂一次。
        private const float SceneClickFeedIntervalSeconds = 0.5f;
        private const float CaseTimeoutSeconds = 30f;
        private const float ModeHTimeoutSeconds = 180f;

        private static F3GameplayValidationRunner _instance;
        private static string _status = "未运行";
        private static string _lastReportPath = string.Empty;

        private ModBehaviour _host;
        private Coroutine _routine;
        private bool _cancelRequested;
        private bool _fatalAbort;
        private bool _recoveryChecked;
        private string _runId;
        private string _reportPath;
        private float _suiteStartedAt;
        private int _passed;
        private int _failed;
        private int _skipped;
        private int _warnings;
        private float _baselineP95Ms;
        private float _finalP95Ms;
        private float _peakFrameMs;
        private string _peakStage;
        private long _baselineMemory;
        private long _finalMemory;
        private bool _operationSucceeded;
        private string _operationReason;
        private int _lastSceneClicksFed;

        /// <summary>
        /// 连续「清场不可恢复」次数。单次脏状态多半是异步收尾没跑完，强化清场后能恢复；
        /// 连续两次说明真有 owner 泄漏，此时继续跑后续用例只会产出不可信结论，才该中止。
        /// </summary>
        private int _dirtyStreak;

        /// <summary>本轮 FAIL / SKIP 的用例 ID，写进 SUMMARY 省得翻全文找红项。</summary>
        private readonly List<string> _failedIds = new List<string>();
        private readonly List<string> _skippedIds = new List<string>();

        /// <summary>套件超时：不再整套 CANCELLED，改为停止启动新用例并正常收尾。</summary>
        private bool _suiteTimedOut;

        internal static void EnsureAttached(ModBehaviour host)
        {
            if (!ModBehaviour.DevModeEnabled || host == null) return;
            if (_instance == null)
            {
                GameObject go = new GameObject("BossRushGameplayValidationRunner");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<F3GameplayValidationRunner>();
            }
            _instance._host = host;
        }

        internal static string GetStatusText()
        {
            string path = string.IsNullOrEmpty(_lastReportPath) ? string.Empty : "\n报告: " + _lastReportPath;
            return _status + path;
        }

        internal static bool TryMarkCurrentSlot(ModBehaviour host, out string reason)
        {
            reason = null;
            EnsureAttached(host);
            if (!ModBehaviour.DevModeEnabled) { reason = "仅 Dev 构建可用"; return false; }
            if (!IsBaseScene()) { reason = "只能在基地场景标记测试档"; return false; }
            if (SavesSystem.IsSaving) { reason = "存档系统正忙，请稍后重试"; return false; }
            if (_instance != null && _instance._routine != null) { reason = "验收正在运行"; return false; }
            try
            {
                string marker = BuildDedicatedMarker();
                SavesSystem.Save<string>(DedicatedSlotKey, marker);
                string readback = SavesSystem.Load<string>(DedicatedSlotKey);
                if (!string.Equals(marker, readback, StringComparison.Ordinal))
                {
                    reason = "测试档标记回读不一致";
                    return false;
                }
                SavesSystem.SaveFile(false);
                reason = "当前槽已标记为专用验收测试档（槽位 " + SavesSystem.CurrentSlot + "）";
                _status = reason;
                return true;
            }
            catch (Exception e)
            {
                reason = "标记测试档失败: " + e.Message;
                return false;
            }
        }

        internal static bool TryStart(ModBehaviour host, out string reason)
        {
            reason = null;
            EnsureAttached(host);
            if (_instance == null) { reason = "验收运行器未就绪"; return false; }
            if (_instance._routine != null) { reason = "已有验收正在运行"; return false; }
            if (!_instance.CheckStartGate(out reason)) return false;
            _instance._cancelRequested = false;
            _instance._fatalAbort = false;
            _instance._routine = _instance.StartCoroutine(_instance.RunSuite());
            return true;
        }

        internal static bool TryCancel(out string reason)
        {
            if (_instance == null || _instance._routine == null)
            {
                reason = "当前没有正在运行的验收";
                return false;
            }
            _instance._cancelRequested = true;
            reason = "已请求取消；运行器将在当前安全点清理并生成 CANCELLED 报告";
            return true;
        }

        private void Update()
        {
            if (_host == null) _host = ModBehaviour.Instance;
            if (!_recoveryChecked && _routine == null && _host != null && IsBaseScene()
                && LevelManager.Instance != null && LevelManager.AfterInit)
            {
                _recoveryChecked = true;
                RecoverInterruptedRunIfNeeded();
            }
        }

        private bool CheckStartGate(out string reason)
        {
            reason = null;
            if (!ModBehaviour.DevModeEnabled) { reason = "仅 Dev 构建可用"; return false; }
            if (_host == null) { reason = "ModBehaviour 未就绪"; return false; }
            if (!IsBaseScene()) { reason = "必须从基地场景启动"; return false; }
            if (!IsDedicatedCurrentSlot()) { reason = "当前槽不是专用测试档，请先点击标记按钮"; return false; }
            if (SavesSystem.IsSaving) { reason = "存档系统正忙"; return false; }
            if (LevelManager.Instance == null || !LevelManager.AfterInit) { reason = "LevelManager 未初始化"; return false; }
            if (CharacterMainControl.Main == null || CharacterMainControl.Main.CharacterItem == null) { reason = "玩家或资源未就绪"; return false; }
            if (SceneLoader.Instance == null) { reason = "SceneLoader 未就绪"; return false; }
            string mode;
            if (_host.ValidationHasActiveMode(out mode)) { reason = "检测到活动玩法: " + mode; return false; }
            if (ZombieModeUIHelper.ModalInputLeaseCount != 0)
            {
                reason = "存在其他模态输入租约: " + ZombieModeUIHelper.ModalInputLeaseCount;
                return false;
            }
            return true;
        }

        private IEnumerator RunSuite()
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

            if (!WriteRunMarker())
            {
                Record("RUN_MARKER", "FAIL", 0L, string.Empty, "无法写入运行标记");
                if (_host != null) _host.GameplayValidationSuppressNotifications = false;
                Finish(false, false);
                yield break;
            }

            bool cancelled = false;
            try
            {
                SetStage("1/7 基线与数据");
                yield return SamplePerformance("BASELINE_10S", 10f, true);
                CaptureLeakBaseline("SUITE");
                RunSyncCase("DATA_CAMPAIGN_JSON", ValidateCampaignJson);
                RunSyncCase("DATA_CODEX_CATALOG", ValidateCodexCatalog);
                RunSyncCase("DATA_CODEX_FILTER_REFRESH", ValidateCodexFilterRefresh);
                RunSyncCase("DATA_BACKMOUNTAIN", ValidateBackMountainData);

                SetStage("2/7 基地玩法");
                RunSyncCase("DAILY_REPORT_ROLLBACK", _host.ValidateDailyReportRollback);
                RunSyncCase("PETNEST_BUNDLE_V2", ValidatePetNestBundle);
                RunSyncCase("PETNEST_REWARD_DEBT", ValidatePetNestRewardDebt);
                RunSyncCase("AFFIX_TEMP_ITEM_LIFECYCLE", ValidateAffixTemporaryItem);
                RunSyncCase("UI_IDEMPOTENT_CLEANUP", ValidateUiCleanup);
                yield return RunPublishedItemCases();

                SetStage("3/7 后山与经济");
                yield return RunBaseEconomyCases();

                SetStage("4/7 竞技场与模式");
                yield return LoadScene(BossRushArenaSceneIDForValidation(), "SCENE_ENTER_ARENA");
                if (!_operationSucceeded)
                {
                    // 进不了竞技场时后面所有场内用例都无从谈起：老实记 SKIP，不伪造结论。
                    SkipRemainingArenaCases("arena_scene_load_failed");
                }
                else
                {
                    yield return WaitRuntimeReady("ARENA_READY", SceneTimeoutSeconds);
                    if (!_operationSucceeded) SkipRemainingArenaCases("arena_runtime_not_ready");
                    else yield return RunArenaStages();
                }

                SetStage("7/7 最终清场、泄漏与回读");
                _host.ValidationSafeCleanup();
                yield return LoadScene(null, "SCENE_RETURN_BASE", returnToBase: true);
                if (_operationSucceeded)
                {
                    yield return WaitRuntimeReady("BASE_READY_FINAL", SceneTimeoutSeconds,
                        BaseSceneNameForValidation(), true);
                }
                else Record("BASE_READY_FINAL", "SKIP", 0L, string.Empty, "base_scene_load_failed");
                if (_operationSucceeded)
                {
                    yield return SamplePerformance("FINAL_5S", 5f, false);
                    RunSyncCase("FINAL_CLEAN_STATE", ValidateFinalCleanState);
                    RunSyncCase("FINAL_LEAK_DELTA", ValidateSuiteLeakDelta);
                    RunSyncCase("FINAL_SAVE_READBACK", ValidateFinalSaveReadback);
                }
                else
                {
                    foreach (string id in new[] { "FINAL_5S", "FINAL_CLEAN_STATE", "FINAL_LEAK_DELTA", "FINAL_SAVE_READBACK" })
                        Record(id, "SKIP", 0L, string.Empty, "base_runtime_not_ready");
                }
            }
            finally
            {
                try { _host?.ValidationSafeCleanup(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 终态清理失败: " + e.Message); }
                try { PetNestExpeditionService.ResetValidationRewardBackend(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 终态奖励后端复位失败: " + e.Message); }
                try { DailyReportPersistence.SetValidationRejectStore(false); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 终态日报注入复位失败: " + e.Message); }
                if (_host != null) _host.GameplayValidationSuppressNotifications = false;
                if (_cancelRequested) cancelled = true;
                Finish(!cancelled && _failed == 0, cancelled);
            }
        }

        private IEnumerator SamplePerformance(string caseId, float seconds, bool baseline)
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<float> frames = new List<float>(2048);
            long memoryStart = GC.GetTotalMemory(false);
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                float ms = Time.unscaledDeltaTime * 1000f;
                if (ms > 0f) frames.Add(ms);
                if (ms > _peakFrameMs)
                {
                    _peakFrameMs = ms;
                    _peakStage = _status;
                }
                yield return null;
            }
            frames.Sort();
            float p95 = frames.Count > 0 ? frames[Mathf.Clamp(Mathf.CeilToInt(frames.Count * 0.95f) - 1, 0, frames.Count - 1)] : 0f;
            if (baseline)
            {
                _baselineP95Ms = p95;
                _baselineMemory = memoryStart;
            }
            else
            {
                _finalP95Ms = p95;
                _finalMemory = GC.GetTotalMemory(false);
            }
            string metrics = "samples=" + frames.Count + ",p95_ms=" + p95.ToString("F2")
                + ",memory=" + GC.GetTotalMemory(false);
            if (baseline || p95 <= Mathf.Max(50f, _baselineP95Ms * 1.75f))
                Record(caseId, "PASS", sw.ElapsedMilliseconds, metrics, string.Empty);
            else
                Record(caseId, "FAIL", sw.ElapsedMilliseconds, metrics, "超过性能阈值");
        }

        private IEnumerator WaitSeconds(float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline && !ShouldAbort()) yield return null;
        }

        /// <summary>
        /// 清场核对。单次不干净不再直接中止整套：先做一次强化清场并复检，
        /// 只有**连续两次**不可恢复才置 _fatalAbort——那时后续用例的结论已被脏状态污染。
        /// </summary>
        private IEnumerator VerifyArenaCleanup(string caseId)
        {
            Stopwatch sw = Stopwatch.StartNew();
            _host.ValidationSafeCleanup();
            bool clean = false;
            string metrics = string.Empty;
            float deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline && !_cancelRequested)
            {
                clean = _host.ValidationTryGetArenaCleanState(out metrics);
                if (clean) break;
                yield return null;
            }

            if (!clean)
            {
                // 第二轮：强清一次并多等几帧。异步收尾（UniTask 生成、掉落结算）
                // 常常只是比 2 秒窗口慢一点。
                yield return ForceReclaimArena();
                clean = _host.ValidationTryGetArenaCleanState(out metrics);
            }

            if (clean)
            {
                _dirtyStreak = 0;
                Record(caseId, "PASS", sw.ElapsedMilliseconds, metrics, string.Empty);
                yield break;
            }

            _dirtyStreak++;
            Record(caseId, "FAIL", sw.ElapsedMilliseconds,
                metrics + ",dirty_streak=" + _dirtyStreak,
                _dirtyStreak >= 2
                    ? "连续两次清场未达到空闲不变式，后续结论已不可信，终止"
                    : "清场未达到空闲不变式，已强清并继续下一用例");
            if (_dirtyStreak >= 2) _fatalAbort = true;
        }


        private IEnumerator RunCampaignFinalBoss()
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (!_host.DebugStartCampaignFinalBossForValidation())
            {
                Record("CAMPAIGN_FINAL_BOSS", "FAIL", sw.ElapsedMilliseconds, string.Empty, "start_rejected");
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + CaseTimeoutSeconds;
            while (_host.CampaignFinalBossInstanceForValidation == null
                && Time.realtimeSinceStartup < deadline && !ShouldAbort()) yield return null;
            CharacterMainControl boss = _host.CampaignFinalBossInstanceForValidation;
            if (boss == null || boss.Health == null)
            {
                Record("CAMPAIGN_FINAL_BOSS", "FAIL", sw.ElapsedMilliseconds, string.Empty, "spawn_timeout");
                _host.ValidationSafeCleanup();
                yield break;
            }
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                // DamageInfo 是 struct；无参 new 不调用带可选参数的构造器，
                // elementFactors 会保持 null，官方 Health.Hurt 访问 Count 时抛异常。
                DamageInfo damage = new DamageInfo(player);
                damage.damageValue = boss.Health.MaxHealth * 20f;
                damage.ignoreArmor = true;
                damage.toDamageReceiver = boss.mainDamageReceiver;
                damage.damagePoint = boss.transform.position;
                boss.Health.Hurt(damage);
            }
            catch (Exception e)
            {
                Record("CAMPAIGN_FINAL_BOSS", "FAIL", sw.ElapsedMilliseconds, string.Empty, e.ToString());
                _host.ValidationSafeCleanup();
                yield break;
            }
            yield return WaitSeconds(2f);
            bool once = _host.CampaignFinalBossDeathPresentationCount == 1;
            bool clean = !_host.IsCampaignFinalBossActive && BossBgmCoordinator.ActiveOwnerLeaseCount == 0;
            Record("CAMPAIGN_FINAL_BOSS", once && clean ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                "death_presentations=" + _host.CampaignFinalBossDeathPresentationCount
                    + ",bgm_owners=" + BossBgmCoordinator.ActiveOwnerLeaseCount,
                once && clean ? string.Empty : "duplicate_presentation_or_cleanup_failed");
            _host.ValidationSafeCleanup();
        }

        private bool ValidateCampaignJson(out string metrics, out string reason)
        {
            metrics = "source=" + CampaignContentCatalog.Source + ",chapters=" + CampaignContentCatalog.Chapters.Count
                + ",signature=" + CampaignContentCatalog.ContentSignature
                + ",expected=" + CampaignContentCatalog.ExpectedContentSignature;
            reason = null;
            bool ok = CampaignContentCatalog.Source == "Json"
                && CampaignContentCatalog.Chapters.Count == CampaignTuning.ChapterCount
                && string.Equals(CampaignContentCatalog.ContentSignature,
                    CampaignContentCatalog.ExpectedContentSignature, StringComparison.Ordinal);
            if (!ok) reason = "必须使用已部署 JSON、六章且签名与冻结内容匹配";
            return ok;
        }

        private bool ValidateCodexCatalog(out string metrics, out string reason)
        {
            reason = null;
            IList<CodexBossInfo> all = _host.CodexRuntime != null ? _host.CodexRuntime.GetCatalogSnapshot() : null;
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            int official = 0;
            bool unique = all != null;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    CodexBossInfo info = all[i];
                    if (info == null || string.IsNullOrEmpty(info.Key) || !keys.Add(info.Key)) unique = false;
                    if (info != null && !info.IsCustomBoss && !info.IsZombieBoss && !info.IsHistoricalOnly) official++;
                }
            }
            metrics = "total=" + (all != null ? all.Count : 0) + ",official=" + official + ",unique=" + unique;
            if (all == null || all.Count == 0 || official == 0 || !unique) reason = "图鉴目录为空、无官方条目或稳定键重复";
            return all != null && all.Count > 0 && official > 0 && unique;
        }

        private bool ValidateBackMountainData(out string metrics, out string reason)
        {
            int count = BackMountainItems.Definitions != null ? BackMountainItems.Definitions.Length : 0;
            bool mappings = BackMountainItems.GetHarvestResultFor(BossRushItemIds.DragonSeed) == BossRushItemIds.DragonFruit
                && BackMountainItems.GetHarvestResultFor(BossRushItemIds.EmberSeed) == BossRushItemIds.EmberChili
                && BackMountainItems.GetHarvestResultFor(BossRushItemIds.PhantomSpore) == BossRushItemIds.PhantomMushroom;
            metrics = "definitions=" + count + ",mappings=" + mappings;
            reason = count == 6 && mappings ? null : "后山定义或种植映射不完整";
            return reason == null;
        }

        private bool ValidateCodexFilterRefresh(out string metrics, out string reason)
        {
            bool ok = _host.DebugValidateCodexFilterRefresh(out metrics);
            reason = ok ? null : "过滤前后官方条目/分母或扫描次数不符合预期";
            return ok;
        }

        private bool ValidatePetNestBundle(out string metrics, out string reason)
        {
            PetNestBundleData bundle = PetNestPersistence.Bundle.Current;
            string json = PetNestCodec.EncodeBundle(bundle);
            PetNestBundleData decoded = PetNestCodec.DecodeBundle(PetNestJson.Parse(json));
            bool ok = decoded != null && decoded.nest != null && decoded.expedition != null && decoded.museum != null
                && !PetNestPersistence.HasAnyWriteBarrier && !PetNestPersistence.IsAnyStoreFaulted;
            metrics = "generation=" + (bundle != null ? bundle.generation : -1)
                + ",pending=" + PetNestPersistence.HasAnyPendingWrite + ",json_bytes=" + (json != null ? json.Length : 0);
            reason = ok ? null : "Bundle_v2 不可读或处于写屏障/故障";
            return ok;
        }

        private sealed class RewardProbeBackend : PetNestExpeditionService.IValidationRewardBackend
        {
            internal bool Ready = true;
            internal bool CashAllowed;
            internal int ItemSuccessBudget;
            public bool IsReady { get { return Ready; } }
            public bool TryGrantCash(long amount) { return CashAllowed; }
            public bool TryGrantItem(int typeId, PetNestExpeditionRecord record)
            {
                if (ItemSuccessBudget <= 0) return false;
                ItemSuccessBudget--;
                return true;
            }
        }

        private bool ValidatePetNestRewardDebt(out string metrics, out string reason)
        {
            RewardProbeBackend backend = new RewardProbeBackend();
            PetNestExpeditionService.SetValidationRewardBackend(backend);
            PetNestExpeditionRecord record = new PetNestExpeditionRecord();
            record.id = "validation_reward_debt";
            record.settled = true;
            record.outcomeCash = 100L;
            record.outcomeLootTypeIds = new List<int> { RelicEggConfig.TYPE_ID };
            record.outcomeLootCounts = new List<int> { 2 };
            record.Normalize();

            backend.CashAllowed = false;
            bool first = PetNestExpeditionService.DebugGrantRewards(record);
            bool unavailableKept = !first && !record.cashGranted && record.grantedLootUnits == 0;

            backend.CashAllowed = true;
            backend.ItemSuccessBudget = 1;
            bool partial = PetNestExpeditionService.DebugGrantRewards(record);
            bool cursorKept = !partial && record.cashGranted && record.grantedLootUnits == 1;

            record.rewardGrantAttempts = PetNestTuning.MaxRewardGrantAttempts + 2;
            PetNestBundleData bundle = PetNestCodec.CreateDefaultBundle();
            bundle.expedition.records.Add(record);
            PetNestBundleData rebooted = PetNestCodec.DecodeBundle(PetNestJson.Parse(PetNestCodec.EncodeBundle(bundle)));
            PetNestExpeditionRecord recovered = rebooted.expedition.records[0];
            backend.ItemSuccessBudget = 1;
            bool completed = PetNestExpeditionService.DebugGrantRewards(recovered);
            bool recoveredOk = completed && recovered.cashGranted && recovered.grantedLootUnits == 2
                && !recovered.rewardsGranted;
            PetNestExpeditionService.ResetValidationRewardBackend();

            metrics = "unavailable_kept=" + unavailableKept + ",partial_cursor=" + cursorKept
                + ",reboot_recovered=" + recoveredOk + ",attempts=" + recovered.rewardGrantAttempts;
            reason = unavailableKept && cursorKept && recoveredOk ? null : "奖励债务游标或重启恢复失败";
            return reason == null;
        }

        private bool ValidateAffixTemporaryItem(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            Item item = null;
            try
            {
                item = ItemAssetsCollection.InstantiateSync(DragonKingBossGunConfig.WeaponTypeId);
                if (item == null)
                {
                    reason = "无法创建临时武器";
                    return false;
                }
                bool initialized = AffixItemData.EnsureInitialized(item, 1);
                bool written = AffixItemData.WriteSlot(item, 1, AffixDefinitions.Id_Lifesteal, 1);
                bool locked = AffixItemData.SetLock(item, 1, true) && AffixItemData.IsLocked(item, 1);
                string runtimeMetrics;
                bool mounted = AffixRuntimeService.DebugValidateTemporaryMount(item, out runtimeMetrics);
                int stripped = AffixItemData.StripAll(item);
                bool rolledBack = stripped > 0 && !AffixItemData.HasAffixData(item);
                metrics = "initialized=" + initialized + ",written=" + written + ",locked=" + locked
                    + ",mounted=" + mounted + ",rolled_back=" + rolledBack + "," + runtimeMetrics;
                if (!initialized || !written || !locked || !mounted || !rolledBack)
                    reason = "词缀临时物品生成/锁定/挂载/回滚链不完整";
                return reason == null;
            }
            catch (Exception e)
            {
                reason = e.ToString();
                return false;
            }
            finally
            {
                try
                {
                    if (item != null)
                    {
                        AffixItemData.StripAll(item);
                        Destroy(item.gameObject);
                    }
                    AffixRuntimeService.EnsureRuntime();
                }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 词缀临时物品回滚失败: " + e.Message); }
            }
        }

        private bool ValidateUiCleanup(out string metrics, out string reason)
        {
            int before = ZombieModeUIHelper.ModalInputLeaseCount;
            PetNestRuntimeModule.CloseAllInteractiveViewsForSceneChange();
            PetNestRuntimeModule.CloseAllInteractiveViewsForSceneChange();
            ModeGInteractable.CloseActiveConfirmation();
            ModeGInteractable.CloseActiveConfirmation();
            ModeGAbandonPresenter.CloseIfOpen();
            ModeGAbandonPresenter.CloseIfOpen();
            int after = ZombieModeUIHelper.ModalInputLeaseCount;
            metrics = "before=" + before + ",after=" + after + ",timeScale=" + Time.timeScale;
            reason = after == 0 && Time.timeScale > 0f ? null : "模态租约或时间缩放未恢复";
            return reason == null;
        }

        private bool ValidateBgmOwnerLeases(out string metrics, out string reason)
        {
            BossBgmCoordinator.ResetStaticCaches();
            GameObject a = new GameObject("ValidationBgmOwnerA");
            GameObject b = new GameObject("ValidationBgmOwnerB");
            GameObject c = new GameObject("ValidationBgmOwnerC");
            bool aa = BossBgmCoordinator.AcquireBossBgm(BossBgmKeys.PhantomWitch, a);
            bool ab = BossBgmCoordinator.AcquireBossBgm(BossBgmKeys.PhantomWitch, b);
            BossBgmCoordinator.ReleaseBossBgm(BossBgmKeys.PhantomWitch, a);
            bool sharedStayed = BossBgmCoordinator.ActiveOwnerLeaseCount == 1;
            bool ac = BossBgmCoordinator.AcquireBossBgm(BossBgmKeys.DragonDescendant, c);
            BossBgmCoordinator.ReleaseBossBgm(BossBgmKeys.DragonDescendant, c);
            bool restored = ac && string.Equals(BossBgmCoordinator.PlayingBossKey, BossBgmKeys.PhantomWitch, StringComparison.Ordinal);
            BossBgmCoordinator.ReleaseBossBgm(BossBgmKeys.PhantomWitch, b);
            bool empty = BossBgmCoordinator.ActiveOwnerLeaseCount == 0;
            Destroy(a); Destroy(b); Destroy(c);
            metrics = "same_key=" + (aa && ab) + ",shared_stayed=" + sharedStayed
                + ",different_key_available=" + ac + ",restored=" + restored + ",empty=" + empty;
            reason = aa && ab && sharedStayed && ac && restored && empty ? null : "BGM owner 租约不满足共享/恢复/清空不变式";
            return reason == null;
        }

        private bool ValidateFinalCleanState(out string metrics, out string reason)
        {
            string mode;
            bool active = _host.ValidationHasActiveMode(out mode);
            string hostileDetails;
            int enemies = _host.ValidationCountHostileCharacters(out hostileDetails);
            string ownedDetails;
            int owned = _host.ValidationCountModeOwnedCharacters(out ownedDetails);
            int modal = ZombieModeUIHelper.ModalInputLeaseCount;
            int bgm = BossBgmCoordinator.ActiveOwnerLeaseCount;
            bool debt = PetNestExpeditionService.HasPendingRewardDebt;
            int modifiers = _host.ValidationTemporaryModifierCount;
            RandomEventDirector director = _host.RandomEventsRuntime != null ? _host.RandomEventsRuntime.Director : null;
            bool randomClear = director == null || director.ActiveEventId == RandomEventId.None;
            metrics = "active=" + active + ",mode=" + mode + ",enemies=" + enemies
                + ",owned=" + owned
                + ",modal=" + modal + ",bgm=" + bgm + ",reward_debt=" + debt
                + ",modifiers=" + modifiers + ",random_clear=" + randomClear
                + ",hostiles=" + hostileDetails + ",owned_details=" + ownedDetails;
            reason = !active && enemies == 0 && owned == 0 && modal == 0 && bgm == 0 && !debt
                && modifiers == 0 && randomClear
                ? null : "最终清场仍有活动 owner、敌人、模态、BGM、临时 modifier、随机事件或奖励债务";
            return reason == null;
        }

        private bool ValidateFinalSaveReadback(out string metrics, out string reason)
        {
            try
            {
                PetNestSaveCoordinator.RequestFlush();
                DailyReportSaveCoordinator.RequestFlush();
                SavesSystem.SaveFile(false);
                bool marker = IsDedicatedCurrentSlot();
                bool campaign = CampaignContentCatalog.Source == "Json";
                metrics = "dedicated_marker=" + marker + ",campaign_json=" + campaign;
                reason = marker && campaign ? null : "测试档或内容回读失败";
                return reason == null;
            }
            catch (Exception e)
            {
                metrics = string.Empty;
                reason = e.ToString();
                return false;
            }
        }

        private delegate bool SyncValidation(out string metrics, out string reason);

        private void RunSyncCase(string id, SyncValidation validation)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string metrics = string.Empty;
            string reason = string.Empty;
            try
            {
                bool ok = validation(out metrics, out reason);
                Record(id, ok ? "PASS" : "FAIL", sw.ElapsedMilliseconds, metrics, reason);
            }
            catch (Exception e)
            {
                Record(id, "FAIL", sw.ElapsedMilliseconds, metrics, e.ToString());
            }
        }

        private void Record(string id, string outcome, long elapsedMs, string metrics, string reason)
        {
            if (_coverage != null) _coverage.Record(id, outcome);
            if (outcome == "PASS") _passed++;
            else if (outcome == "FAIL") { _failed++; _failedIds.Add(id); }
            else if (outcome == "SKIP") { _skipped++; _skippedIds.Add(id); }
            else if (outcome == "WARN") _warnings++;
            string scene = SceneManager.GetActiveScene().name;
            string line = id + " | " + outcome + " | " + elapsedMs + "ms | " + scene + " | "
                + Sanitize(metrics) + " | " + Sanitize(reason);
            WriteRaw(line);
        }

        private void SetStage(string stage)
        {
            _status = stage + " | PASS=" + _passed + " FAIL=" + _failed + " SKIP=" + _skipped;
            WriteRaw("STAGE | " + stage);
            try { WriteCoverageSnapshot(); }
            catch (Exception e) { Record("COVERAGE_REPORT", "FAIL", 0L, string.Empty, e.ToString()); }
        }

        /// <summary>
        /// 是否应停止推进。三种来源语义不同：
        /// 玩家取消 → CANCELLED；套件超时 → TIMEOUT（剩余用例记 SKIP，仍走完最终收尾）；
        /// 连续脏状态 → 真中止，后续结论已不可信。
        /// </summary>
        private bool ShouldAbort()
        {
            if (_fatalAbort) return true;
            if (_cancelRequested) return true;
            if (_suiteTimedOut) return true;
            if (Time.realtimeSinceStartup - _suiteStartedAt > SuiteTimeoutSeconds)
            {
                _suiteTimedOut = true;
                WriteRaw("SUITE | TIMEOUT | suite_timeout");
                return true;
            }
            return false;
        }

        private void Finish(bool passed, bool cancelled)
        {
            try
            {
                string coverageState;
                try { coverageState = FinishCoverage(); }
                catch (Exception e) { coverageState = "ERROR"; Record("COVERAGE_REPORT", "FAIL", 0L, string.Empty, e.ToString()); }
                passed = passed && _failed == 0;
                if (_peakFrameMs > 200f)
                {
                    _warnings++;
                    WriteRaw("PERFORMANCE_WARNING | peak_ms=" + _peakFrameMs.ToString("F2") + " | stage=" + _peakStage);
                }
                // 状态优先级：玩家取消 > 脏状态中止 > 套件超时 > 用例结果。
                // 超时与取消必须分开：前者是「测试太长」，后者是「人喊停」，
                // 混成一个 CANCELLED 会让人分不清这轮结论能不能用。
                string status;
                if (cancelled && _cancelRequested) status = "CANCELLED";
                else if (_fatalAbort) status = "ABORTED_DIRTY";
                else if (_suiteTimedOut) status = "TIMEOUT";
                else status = passed ? "PASS" : "FAIL";

                WriteRaw("SUMMARY | " + status + " | pass=" + _passed + " fail=" + _failed
                    + " skip=" + _skipped + " warn=" + _warnings
                    + " | baseline_p95_ms=" + _baselineP95Ms.ToString("F2")
                    + " final_p95_ms=" + _finalP95Ms.ToString("F2")
                    + " peak_ms=" + _peakFrameMs.ToString("F2")
                    + " memory_baseline=" + _baselineMemory + " memory_final=" + _finalMemory
                    + " | failed_ids=" + string.Join(",", _failedIds.ToArray())
                    + " | skipped_ids=" + string.Join(",", _skippedIds.ToArray())
                    + " | coverage=" + coverageState
                    + " | report=" + _reportPath);
                ClearRunMarker();
                _status = "验收 " + status + "：PASS=" + _passed + " FAIL=" + _failed
                    + " SKIP=" + _skipped + " WARN=" + _warnings
                    + "\n覆盖=" + coverageState + "；人工待验=" + (_coverage != null ? _coverage.ManualCount.ToString() : "未知")
                    + (_failedIds.Count > 0 ? "\n红项: " + string.Join(",", _failedIds.ToArray()) : string.Empty);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[BossRushValidation] 完成报告失败: " + e);
            }
            _routine = null;
            _cancelRequested = false;
            _fatalAbort = false;
        }

        private bool WriteRunMarker()
        {
            try
            {
                string marker = _runId + "|" + DateTime.UtcNow.Ticks;
                SavesSystem.Save<string>(RunMarkerKey, marker);
                if (!string.Equals(SavesSystem.Load<string>(RunMarkerKey), marker, StringComparison.Ordinal)) return false;
                SavesSystem.SaveFile(false);
                return true;
            }
            catch { return false; }
        }

        private static void ClearRunMarker()
        {
            try
            {
                SavesSystem.Save<string>(RunMarkerKey, string.Empty);
                SavesSystem.SaveFile(false);
            }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 清除运行标记失败: " + e.Message); }
        }

        private void RecoverInterruptedRunIfNeeded()
        {
            try
            {
                if (!SavesSystem.KeyExisits(RunMarkerKey)) return;
                string marker = SavesSystem.Load<string>(RunMarkerKey);
                if (string.IsNullOrEmpty(marker)) return;
                _host.ValidationSafeCleanup();
                string interruptedId = marker.Split('|')[0];
                string dir = Path.Combine(Application.persistentDataPath, "BossRushTestReports");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "BossRushValidation_" + interruptedId + ".log");
                File.AppendAllText(path, Environment.NewLine + "RECOVERY | CANCELLED | 上次测试中断，已在基地执行安全清理"
                    + Environment.NewLine, System.Text.Encoding.UTF8);
                _lastReportPath = path;
                _status = "检测到上次验收中断，已安全清理并标记 CANCELLED";
                ClearRunMarker();
            }
            catch (Exception e)
            {
                _status = "上次验收中断恢复失败: " + e.Message;
            }
        }

        private void WriteRaw(string line)
        {
            try { File.AppendAllText(_reportPath, line + Environment.NewLine, System.Text.Encoding.UTF8); }
            catch (Exception e) { UnityEngine.Debug.LogError("[BossRushValidation] 写报告失败: " + e.Message); }
            UnityEngine.Debug.Log("[BossRushValidation] " + line);
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace('\r', ' ').Replace('\n', ' ');
        }

        private static bool IsBaseScene()
        {
            return string.Equals(SceneManager.GetActiveScene().name, "Base_SceneV2", StringComparison.Ordinal);
        }

        private static string BuildDedicatedMarker()
        {
            return "v1:slot:" + SavesSystem.CurrentSlot;
        }

        private static bool IsDedicatedCurrentSlot()
        {
            try
            {
                return SavesSystem.KeyExisits(DedicatedSlotKey)
                    && string.Equals(SavesSystem.Load<string>(DedicatedSlotKey), BuildDedicatedMarker(), StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static string BossRushArenaSceneIDForValidation() { return "Level_DemoChallenge_Main"; }
        private static string BaseSceneNameForValidation() { return "Base_SceneV2"; }
    }
}
