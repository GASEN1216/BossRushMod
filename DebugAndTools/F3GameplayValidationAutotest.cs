#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationAutotest.cs - 全自动实机验收编排（Dev 构建；「自动验收 + 完整待测清单」按钮的后半程）
// ============================================================================
// owner 只在基地用专用测试档按一次按钮，结果由 AI 读结果目录判断。主套件（RunSuite / RunFinalChecks）跑完回到基地之后，接着：
//
//   8/10  读步骤表（Assets/Data/SkyIslandAutotest.json）→ 基地侧步骤 → 给测试档的天空岛剧情存快照 → 经基地船点真实出发（找不到船点才退回生产 Enter）
//   9/10  岛上：落地步骤 → 岛内只读套件 + 收尾快照 + Dev 演练（原样复用）→ 真实存档状态下的步骤 →
//         清空成新档，按阶段推进（双航标 → 打过噬风未敲钟 → 结局 → 结局 + 七盏风晶灯），每阶段一轮步骤 → 换语言复拍 → 走码头撤离圈回基地
//   10/10 还原（岛上先写回、回基地读回核对、不一致再写回）→ 环境复位 → 收回发出去的物品、记金钱 → 基地侧检查 → 写 manifest / summary
//
// 纪律（tests/F3AutotestOrchestratorGuard.py 守着）：
// - 整份 #if BOSSRUSH_DEV；正式构建的 DLL 里查不到这些标识（tools/check_dll_identifiers.py 构建后实查）。
// - 任何写入之前先过 AutotestWriteAllowed（Dev + 专用测试档 + 这一轮正在跑）；快照在第一次写入（出发）之前取。
// - 还原有两道：收尾阶段（取消也会走到，_closingSession 让它不被取消打断）与 CompleteSession 里的同步兜底；
//   两道都复位语言、强制夜里、无敌与时间流速。岛上只读套件的文件不引用这里任何符号。
// - 每步有预算、每次切图有看门狗；失败记 FAIL 继续。每步在 Player.log 写 [AUTOTEST] step=… begin / end result=…。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Saves;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private const float AutotestIslandReadyTimeoutSeconds = 200f;
        /// <summary>切图看门狗：官方 SceneLoader 持续「加载中」超过这么久就判卡死（上次 Mode F 撤离黑屏约 290 秒）。</summary>
        private const float AutotestLoadWatchdogSeconds = 150f;
        private const float AutotestReturnTimeoutSeconds = 190f;

        private sealed class AutotestRun
        {
            internal bool Active, SnapshotPersisted, Snapshotting, StoryResetDone, RestoreDone, LanguageSwitched, StepFailed, FrameSampled, IslandReached;
            internal string RunDir, ShotsDir, CurrentStageId, LastFrameReason;
            internal F3AutotestTable Table;
            internal AutotestSnapshot Snapshot;
            internal int ExpectedSteps, LastKilled;
            internal long ShotBytes;
            internal readonly F3AutotestRunInfo Info = new F3AutotestRunInfo();
            internal readonly List<F3AutotestStepRecord> Records = new List<F3AutotestStepRecord>();
            internal readonly Dictionary<string, SkyIslandStoryData> StageExpectations = new Dictionary<string, SkyIslandStoryData>(StringComparer.Ordinal);
            internal readonly HashSet<string> StagesApplied = new HashSet<string>(StringComparer.Ordinal);
            internal readonly Dictionary<Health, bool> InvincibleOriginal = new Dictionary<Health, bool>();
            internal readonly Dictionary<Health, float> HealthOriginal = new Dictionary<Health, float>();
            internal readonly Dictionary<string, Vector3> StaticProbes = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            internal Dictionary<int, int> StepStartCounts = new Dictionary<int, int>();
            internal Dictionary<int, int> LastLoot;
            internal AutotestShotAnalysis LastShot;
        }

        private AutotestRun _autotest;
        /// <summary>自动验收自己发起的那一次出发：SkyIslandSession.CanEnter 经 IsRunning → AllowsSkyIslandEntry 放行。</summary>
        private bool _autotestDepartureOpen;

        /// <summary>任何写入（剧情、背包、搬玩家、快照键）之前的门：Dev 构建 + 专用测试档 + 这一轮自动验收正在跑（或正在崩溃恢复）。</summary>
        internal static bool AutotestWriteAllowed(out string reason)
        {
            reason = null;
            if (!ModBehaviour.DevModeEnabled) { reason = "dev_build_required"; return false; }
            if (!IsDedicatedCurrentSlot()) { reason = "dedicated_test_slot_required"; return false; }
            if (_autotestRecovering) return true;
            F3GameplayValidationRunner runner = _instance;
            if (runner == null || !runner._running || runner._autotest == null || !runner._autotest.Active)
            {
                reason = "autotest_not_running";
                return false;
            }
            if (runner._slotChanged || SavesSystem.CurrentSlot != runner._sessionSlot) { reason = "slot_changed"; return false; }
            // 快照先于第一次写入：快照键落盘之前只放行取快照的那一次（TakeAutotestSnapshot 置 Snapshotting）。
            if (!runner._autotest.SnapshotPersisted && !runner._autotest.Snapshotting) { reason = "snapshot_not_taken"; return false; }
            return true;
        }

        private string AutotestLegSkipReason()
        {
            if (_slotChanged) return "slot_changed";
            if (_hostLost || _host == null) return "host_lost";
            if (_cancelRequested) return "player_cancelled";
            if (_fatalAbort) return "aborted_dirty_state";
            if (_suiteTimedOut) return "suite_timeout";
            if (!ModBehaviour.DevModeEnabled) return "dev_build_required";
            if (!IsDedicatedCurrentSlot()) return "dedicated_test_slot_required";
            if (!IsBaseScene() || !IsRuntimeReady(BaseSceneNameForValidation())) return "not_back_in_base_after_main_suite";
            return null;
        }

        /// <summary>主套件收尾回到基地之后由 RunSession 调用。自己开两段会话阶段：主段可被取消，还原段不可。</summary>
        private IEnumerator RunSkyIslandAutotestLeg()
        {
            _autotest = new AutotestRun();
            bool dirReady = false;
            try
            {
                InitAutotestRunDirectory();
                dirReady = true;
            }
            catch (Exception e) { Record("AUTOTEST_LEG", "FAIL", 0L, string.Empty, "result_dir:" + e.Message); }
            if (!dirReady) { _autotest = null; yield break; }
            string skip = AutotestLegSkipReason();
            if (skip != null)
            {
                Record("AUTOTEST_LEG", "SKIP", 0L, string.Empty, skip);
                _autotest.Info.Status = "SKIPPED";
                _autotest.Info.RestoreStory = "NOT_NEEDED";
                _autotest.Info.EnvironmentRestore = "NOT_NEEDED";
                _autotest.Info.RestoreDetail = skip;
                _autotest.RestoreDone = true;
                WriteAutotestReport();
                yield break;
            }
            Record("AUTOTEST_LEG", "PASS", 0L, "result_dir=" + _autotest.RunDir, string.Empty);
            // 后半程自带一份套件预算：主套件已经用掉的时间不算进来。
            _closingSession = false;
            _suiteStartedAt = Time.realtimeSinceStartup;
            _autotest.Active = true;
            WriteRaw("SUITE | SKY_ISLAND_AUTOTEST | read_only=false | story_snapshot=true | result_dir=" + _autotest.RunDir);
            // 三段各自一份会话阶段预算（DriveSessionPhase 的截止时间按调用那一刻算，整段 45 分钟不够跑完岛上三段）。
            yield return DriveSessionPhase(AutotestLegDepartAndReal(), false);
            if (_autotest.IslandReached)
            {
                _suiteStartedAt = Time.realtimeSinceStartup;
                yield return DriveSessionPhase(AutotestLegStoryStages(), false);
                _suiteStartedAt = Time.realtimeSinceStartup;
                yield return DriveSessionPhase(AutotestLegAltAndExtract(), false);
            }
            _closingSession = true;
            yield return DriveSessionPhase(AutotestLegRestore(), true);
        }

        private IEnumerator AutotestLegDepartAndReal()
        {
            SetStage("8/10 全自动 · 步骤表、快照与出发");
            string metrics, reason;
            bool tableOk = LoadAutotestTable(out metrics, out reason);
            Record("AUTOTEST_STEP_TABLE", tableOk ? "PASS" : "FAIL", 0L, metrics, reason);
            if (!tableOk) yield break;
            yield return RunAutotestStages("base_before");

            bool snapshotOk = TakeAutotestSnapshot(out metrics, out reason);
            Record("AUTOTEST_STORY_SNAPSHOT", snapshotOk ? "PASS" : "FAIL", 0L, metrics, reason);
            WriteAutotestReport();
            if (!snapshotOk) yield break;

            yield return AutotestDepart();
            if (!_operationSucceeded) yield break;
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) yield break;
            // 岛上这一段按岛内口径跑：不接管无敌与回血、帧时间分项计时开录、会话有效性以这个场景实例为准。
            _skyIslandMode = true;
            _skyIslandSceneHandle = session.ValidationScene.handle;
            _skyIslandBaselineLeases = ZombieModeUIHelper.ModalInputLeaseCount;
            // 读条期间主套件的 Update 已经给岛上新主角上了无敌（ProtectCurrentPlayer 按 !_skyIslandMode 门控）：切到岛内口径后撤掉，
            // 否则演练里跟叮咬、掉血有关的判据全部空转。回基地之后 Update 会重新接管，CompleteSession 统一还原。
            RestoreProtectedPlayers();
            _autotest.IslandReached = true;
            yield return RunAutotestStages("landing");

            SetStage("9/10 全自动 · 岛内只读套件与演练");
            // 岛内套件的 SKY_PERF_* 会改写主套件 SUMMARY 用的性能基线与收尾数字：跑完（含中途打断）写回，SUMMARY 仍是主套件的数。
            float mainBaselineP95 = _baselineP95Ms, mainFinalP95 = _finalP95Ms;
            long mainBaselineMemory = _baselineMemory, mainFinalMemory = _finalMemory;
            try
            {
                yield return RunSkyIslandSuite();
                yield return RunSkyIslandFinalChecks();
            }
            finally
            {
                _baselineP95Ms = mainBaselineP95;
                _finalP95Ms = mainFinalP95;
                _baselineMemory = mainBaselineMemory;
                _finalMemory = mainFinalMemory;
            }
            yield return RunSkyIslandDrillSuite();
            WriteAutotestReport();
            yield return RunAutotestStages("real");
        }

        private IEnumerator AutotestLegStoryStages()
        {
            string metrics = string.Empty, reason = null;
            foreach (F3AutotestStage stage in _autotest.Table.Stages)
            {
                if (stage.When != "story") continue;
                if (ShouldAbort()) break;
                SetStage("9/10 全自动 · 剧情阶段「" + stage.Title + "」");
                _autotest.CurrentStageId = stage.Id;
                string gone;
                bool applied = false;
                if (!SkyIslandSessionStillValid(out gone)) reason = gone;
                else applied = ApplyAutotestStage(stage, out metrics, out reason);
                Record("AUTOTEST_STAGE", applied ? "PASS" : "FAIL", 0L, metrics, applied ? string.Empty : reason);
                AutotestLog("AUTOTEST_STAGE_" + stage.Id, "applied", applied ? "PASS" : "FAIL");
                yield return RunAutotestStage(stage);
            }
            _autotest.CurrentStageId = null;
        }

        private IEnumerator AutotestLegAltAndExtract()
        {
            SetStage("9/10 全自动 · 换语言复拍");
            yield return RunAutotestStages("alt_language");

            SetStage("9/10 全自动 · 撤离回基地");
            yield return AutotestExtract();
        }

        private IEnumerator AutotestLegRestore()
        {
            SetStage("10/10 全自动 · 还原与汇总");
            if (_autotest == null) yield break;
            var notes = new List<string>();
            string env;
            RestoreAutotestEnvironment(out env);

            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session != null)
            {
                string island = "no_snapshot";
                bool islandOk = false;
                if (_autotest.Snapshot != null) islandOk = RestoreAutotestStoryOnIsland(out island);
                notes.Add("island_restore=" + islandOk + "(" + island + ")");
                CloseAutotestPanels();
                string dispatch;
                notes.Add("return_dispatched=" + session.DevAutotestReturnToBase("restore", out dispatch) + (dispatch == null ? string.Empty : "(" + dispatch + ")"));
                yield return AutotestWaitBase(notes);
            }
            _skyIslandMode = false;

            float saveUntil = Time.realtimeSinceStartup + 30f;
            while ((SkyIslandSessionOrNull() != null || SkyIslandStorySaveRecovery.IsPending()) && Time.realtimeSinceStartup < saveUntil)
                yield return null;

            bool storyOk;
            string storyDetail;
            if (_autotest.Snapshot == null)
            {
                storyOk = true;
                storyDetail = "no_snapshot_taken_nothing_written";
                _autotest.Info.RestoreStory = "NOT_NEEDED";
            }
            else if (!IsBaseScene() || SkyIslandSessionOrNull() != null)
            {
                storyOk = false;
                storyDetail = "not_back_in_base:snapshot_key_kept_for_recovery";
                _autotest.Info.RestoreStory = "PENDING_RECOVERY";
                _autotestRecoveryCheckedSlot = int.MinValue;
            }
            else
            {
                storyOk = ReadbackAutotestStory(_autotest.Snapshot.Data, out storyDetail);
                if (!storyOk)
                {
                    string again;
                    storyOk = RestoreAutotestStoryAtBase(_autotest.Snapshot.Data, out again);
                    storyDetail += ";base_restore=" + again;
                }
                _autotest.Info.RestoreStory = storyOk ? "PASS" : "FAIL";
            }
            _autotest.Info.RestoreDetail = storyDetail + ";" + string.Join(";", notes.ToArray());
            Record("AUTOTEST_STORY_RESTORE", storyOk ? "PASS" : "FAIL", 0L, _autotest.Info.RestoreDetail, storyOk ? string.Empty : "story_restore_failed");

            bool envOk = RestoreAutotestEnvironment(out env);
            Record("AUTOTEST_ENV_RESTORE", envOk ? "PASS" : "FAIL", 0L, env, envOk ? string.Empty : "environment_not_restored");

            if (IsBaseScene() && _autotest.Snapshot != null)
            {
                _autotest.Info.ItemLedger = ReclaimAutotestItems(_autotest.Snapshot);
                Record("AUTOTEST_ITEMS_RECLAIM", "PASS", 0L, _autotest.Info.ItemLedger, string.Empty);
                _autotest.Info.MoneyLedger = AutotestMoneyLedger();
                // 金钱只记账不还原（2026-09-14 拍板）：验收步骤不买服务，账面变了就记 WARN，AI 对着步骤日志查是哪一步花的。
                bool moneyUnchanged = AutotestMoneyUnchanged();
                Record("AUTOTEST_MONEY_LEDGER", moneyUnchanged ? "PASS" : "WARN", 0L, _autotest.Info.MoneyLedger,
                    moneyUnchanged ? string.Empty : "money_changed_during_autotest_not_restored");
                // 基地侧检查放在还原与收回之后：纪念品件数回到开跑前、官方图鉴镜像与还原后的存档对照。
                RunSyncCase("SKY_KEEPSAKE_ITEMS_BASE", ValidateSkyIslandKeepsakesAtBase);
                string notesMetrics, notesReason;
                bool mirror = JudgeAutotestOfficialNotesAfterRestore(out notesMetrics, out notesReason);
                Record("AUTOTEST_BASE_OFFICIAL_NOTES", mirror ? "PASS" : "FAIL", 0L, notesMetrics, notesReason);
                yield return RunAutotestStages("base_after");
            }

            if (storyOk && _autotest.SnapshotPersisted) ClearAutotestSnapshotKey();
            _autotest.Info.Status = F3AutotestJudges.RunStatus(storyOk, _cancelRequested, _fatalAbort || _hostLost || _slotChanged,
                _autotest.Records, _autotest.ExpectedSteps);
            _autotest.RestoreDone = true;
            WriteAutotestReport();
            WriteRaw("AUTOTEST | " + _autotest.Info.Status + " | restore=" + _autotest.Info.RestoreStory + " | result_dir=" + _autotest.RunDir);
            _autotest.Active = false;
        }

        /// <summary>
        /// CompleteSession（RunSession 的 finally）里的同步兜底：收尾阶段没跑到（宿主销毁、阶段超时、异常）时，
        /// 至少把语言、强制夜里、无敌、时间流速复位，剧情能写回就写回；岛上写回的不算数（会话退出前还会再写），快照键留给下次回基地恢复。
        /// </summary>
        private void FinishAutotestRestoreSynchronously()
        {
            if (_autotest == null || _autotest.RestoreDone) return;
            _autotest.RestoreDone = true;
            try
            {
                string env;
                RestoreAutotestEnvironment(out env);
                if (_autotest.Snapshot != null)
                {
                    string detail;
                    bool onIsland = SkyIslandSessionOrNull() != null;
                    bool ok;
                    if (onIsland) ok = RestoreAutotestStoryOnIsland(out detail);
                    else if (IsBaseScene() && !SkyIslandStorySaveRecovery.IsPending()) ok = RestoreAutotestStoryAtBase(_autotest.Snapshot.Data, out detail);
                    else { ok = false; detail = "deferred_to_recovery"; }
                    bool final = ok && !onIsland;
                    _autotest.Info.RestoreStory = final ? "PASS" : "PENDING_RECOVERY";
                    _autotest.Info.RestoreDetail = "sync_fallback:" + detail + (final ? string.Empty : ";snapshot_key_kept_for_next_base_arrival");
                    if (final)
                    {
                        // 清快照键之前先收回发出去的岛上物品：键一清，唯一会收物品的崩溃恢复就再也走不到了。
                        _autotest.Info.ItemLedger = "sync_fallback:" + ReclaimAutotestItems(_autotest.Snapshot);
                        if (_autotest.SnapshotPersisted) ClearAutotestSnapshotKey();
                    }
                    if (!final) _autotestRecoveryCheckedSlot = int.MinValue;
                }
                else _autotest.Info.RestoreStory = "NOT_NEEDED";
                _autotest.Info.Status = F3AutotestJudges.RunStatus(!F3AutotestJudges.RestoreNeedsAttention(_autotest.Info.RestoreStory),
                    _cancelRequested, true, _autotest.Records, _autotest.ExpectedSteps);
            }
            catch (Exception e)
            {
                _autotest.Info.RestoreDetail += ";sync_restore_threw:" + e.GetType().Name + ":" + e.Message;
            }
            WriteAutotestReport();
            _autotest.Active = false;
        }

        private bool LoadAutotestTable(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            string json;
            if (!JsonDataRegistry.TryReadDataFile(F3AutotestJudges.TableFile, out json)) { reason = "step_table_unreadable"; return false; }
            var errors = new List<string>();
            F3AutotestTable table = F3AutotestJudges.ParseTable(json, errors);
            if (table != null) F3AutotestJudges.ValidateTable(table, null, null, null, errors);
            var expectations = new List<SkyIslandStoryData>();
            if (table != null && errors.Count == 0) F3AutotestJudges.SimulateStages(table, expectations, errors);
            metrics = "stages=" + (table == null ? 0 : table.Stages.Count) + ",steps=" + (table == null ? 0 : table.Steps.Count)
                + ",coverage_rows=" + (table == null ? 0 : table.Coverage.Count);
            if (errors.Count > 0) { reason = F3AutotestJudges.Join(errors, 12); return false; }
            _autotest.Table = table;
            _autotest.ExpectedSteps = table.Steps.Count;
            int index = 0;
            foreach (F3AutotestStage stage in table.Stages)
                if (stage.When == "story" && index < expectations.Count) _autotest.StageExpectations[stage.Id] = expectations[index++];
            return true;
        }

        /// <summary>覆盖账本展开 SKY_AUTOTEST_*（InitializeCoverage 调用）：步骤表里的每一步都进 SKY_ISLAND 的自动项。读不到表就不展开。</summary>
        private void ExpandAutotestCoverage()
        {
            string json;
            if (_coverage == null || !JsonDataRegistry.TryReadDataFile(F3AutotestJudges.TableFile, out json)) return;
            F3AutotestTable table = F3AutotestJudges.ParseTable(json, new List<string>());
            if (table == null || table.Steps.Count == 0) return;
            var ids = new List<string>(table.Steps.Count);
            foreach (F3AutotestStep step in table.Steps) ids.Add(step.Id);
            _coverage.Expand("SKY_AUTOTEST_*", ids);
        }

        private bool JudgeAutotestOfficialNotesAfterRestore(out string metrics, out string reason)
        {
            AutotestSnapshot snapshot = _autotest.Snapshot;
            var recorded = new HashSet<string>(StringComparer.Ordinal);
            var now = new HashSet<string>(CollectOfficialSkyNotes(), StringComparer.Ordinal);
            var before = new HashSet<string>(snapshot.OfficialUnlocked, StringComparer.Ordinal);
            var during = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in now) if (!before.Contains(key)) during.Add(key);
            SkyIslandStoryData data = SavesSystem.KeyExisits(SkyIslandStoryRules.StorageKey)
                ? SkyIslandStoryCodec.Decode(SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey)) : SkyIslandStoryRules.CreateDefault();
            if (data == null) { metrics = "story_unreadable"; reason = "story_unreadable_after_restore"; return false; }
            // 官方镜像只收 20 处见闻（Search_*）；来信、名册、纪念品、灯与蛙卵也记在 discoveredNotes 里，但不进官方图鉴。
            foreach (string note in data.discoveredNotes)
                if (note != null && note.StartsWith("Search_", StringComparison.Ordinal)) recorded.Add(SkyIslandNoteBridge.NoteKeyPrefix + note);
            return F3AutotestJudges.JudgeOfficialNotesAfterAutotest(recorded, now, before, during, out metrics, out reason);
        }

        #region 出发与返航

        private static SkyIslandDepartureInteractable FindAutotestDeparture()
        {
            foreach (SkyIslandDepartureInteractable candidate in UnityEngine.Object.FindObjectsOfType<SkyIslandDepartureInteractable>())
                if (candidate != null && candidate.isActiveAndEnabled) return candidate;
            return null;
        }

        /// <summary>
        /// 基地船点在按需加载的子场景里（Base_SceneV2_Sub_01 的 Envir/Prfb_BoatBetweenBaseAndFarm）。从官方子场景缓存的位置里
        /// 找路径带 Boat 的那一个；找不到时把全部缓存位置写进 metrics，下一轮据此改。
        /// </summary>
        private static bool TryFindAutotestBoatLocation(out string sceneId, out Vector3 position, List<string> notes)
        {
            sceneId = null;
            position = Vector3.zero;
            MultiSceneCore core = MultiSceneCore.Instance;
            if (core == null || core.SubScenes == null) { notes.Add("multiscene_core_missing"); return false; }
            var seen = new List<string>();
            foreach (SubSceneEntry entry in core.SubScenes)
            {
                if (entry == null || entry.cachedLocations == null) continue;
                foreach (SubSceneEntry.Location location in entry.cachedLocations)
                {
                    if (location == null || string.IsNullOrEmpty(location.path)) continue;
                    seen.Add(entry.sceneID + "/" + location.path);
                    if (sceneId == null && (location.path.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0
                        || location.path.IndexOf("Dock", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        sceneId = entry.sceneID;
                        position = location.position;
                    }
                }
            }
            notes.Add("boat_location=" + (sceneId == null ? "not_found" : sceneId + "@" + position));
            if (sceneId == null) notes.Add("cached_locations=" + F3AutotestJudges.Join(seen, 24));
            return sceneId != null;
        }

        private IEnumerator AutotestDepart()
        {
            _operationSucceeded = false;
            Stopwatch sw = Stopwatch.StartNew();
            AutotestLog("AUTOTEST_DEPART_BOAT", "begin", null);
            var notes = new List<string>();
            SkyIslandDepartureInteractable departure = FindAutotestDeparture();
            notes.Add("departure_loaded=" + (departure != null));
            if (departure == null)
            {
                string sceneId;
                Vector3 boat;
                if (TryFindAutotestBoatLocation(out sceneId, out boat, notes))
                {
                    UniTask<bool> teleport = default(UniTask<bool>);
                    bool started = false;
                    try
                    {
                        teleport = MultiSceneCore.Instance.LoadAndTeleport(sceneId, boat, true);
                        started = true;
                    }
                    catch (Exception e) { notes.Add("load_and_teleport_threw:" + e.GetType().Name); }
                    float until = Time.realtimeSinceStartup + 30f;
                    while (started && teleport.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < until && !ShouldAbort())
                        yield return null;
                    if (started)
                    {
                        notes.Add("teleport_status=" + teleport.Status);
                        if (teleport.Status == UniTaskStatus.Pending) teleport.Forget();
                        else
                        {
                            try { notes.Add("teleport_result=" + teleport.GetAwaiter().GetResult()); }
                            catch (Exception e) { notes.Add("teleport_threw:" + e.GetType().Name); }
                        }
                    }
                    until = Time.realtimeSinceStartup + 20f;
                    while (departure == null && Time.realtimeSinceStartup < until && !ShouldAbort())
                    {
                        yield return null;
                        departure = FindAutotestDeparture();
                    }
                    notes.Add("departure_after_teleport=" + (departure != null));
                }
            }

            bool realEntry = false, boatTried = false;
            // 放行窗口一定要关：中途取消、换槽、阶段截止时栈被 Dispose，finally 照样执行（CompleteSession 里还有一道复位）。
            _autotestDepartureOpen = true;
            try
            {
                if (departure != null && !ShouldAbort())
                {
                    bool interacted = false, started = false;
                    try
                    {
                        CharacterMainControl.Main.Interact(departure);
                        interacted = true;
                    }
                    catch (Exception e) { notes.Add("interact_threw:" + e.GetType().Name); }
                    // 官方 Interact 在已有动作、搬着东西或拿不到交互目标（离得不够近）时静默不做：读条真的开始了才算交互发出去了。
                    float startBy = Time.realtimeSinceStartup + 1.5f;
                    while (interacted && departure != null && !started && SkyIslandSessionOrNull() == null && Time.realtimeSinceStartup < startBy && !ShouldAbort())
                    {
                        started = departure.Interacting;
                        if (!started) yield return null;
                    }
                    float until = Time.realtimeSinceStartup + Mathf.Max(1f, departure == null ? 1f : departure.InteractTime) + 8f;
                    while (started && SkyIslandSessionOrNull() == null && Time.realtimeSinceStartup < until && !ShouldAbort()) yield return null;
                    realEntry = SkyIslandSessionOrNull() != null;
                    boatTried = started || realEntry;
                    CharacterMainControl main = CharacterMainControl.Main;
                    float distance = main == null || departure == null ? -1f : Vector3.Distance(main.transform.position, departure.transform.position);
                    notes.Add("boat_interact=" + interacted + ",interact_started=" + started + ",session_created=" + realEntry
                        + ",distance_m=" + distance.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                }
                if (!realEntry && !ShouldAbort())
                {
                    string enterMessage = null;
                    try { SkyIslandSession.Enter(_host, delegate (string message, bool error) { if (error) enterMessage = message; }); }
                    catch (Exception e) { enterMessage = e.GetType().Name; }
                    notes.Add("fallback_production_enter=" + (SkyIslandSessionOrNull() != null)
                        + (enterMessage == null ? string.Empty : "(" + enterMessage + ")"));
                }
            }
            finally { _autotestDepartureOpen = false; }
            // 船点交互的读条开始了却没进岛：那是缺陷，记 FAIL（后面照样经生产入口进岛，把岛上检查跑完）；
            // 船点没加载出来、或读条根本没开始（离得不够近、身上有别的动作）才记 SKIP，notes 里有距离可查。
            string departResult = realEntry ? "PASS" : boatTried ? "FAIL" : "SKIP";
            Record("AUTOTEST_DEPART_BOAT", departResult, sw.ElapsedMilliseconds, string.Join(",", notes.ToArray()),
                realEntry ? string.Empty : boatTried ? "boat_interaction_started_but_did_not_enter_island" : "boat_interaction_not_started_used_production_enter");
            AutotestLog("AUTOTEST_DEPART_BOAT", "end", departResult);

            AutotestLog("AUTOTEST_ISLAND_READY", "begin", null);
            sw = Stopwatch.StartNew();
            float deadline = Time.realtimeSinceStartup + AutotestIslandReadyTimeoutSeconds;
            float loadingSince = -1f, nextClick = 0f;
            string failure = null;
            while (!ShouldAbort())
            {
                SkyIslandSession session = SkyIslandSessionOrNull();
                if (session == null) { failure = "session_closed_before_ready"; break; }
                if (session.IsReady && SkyIslandRaidLease.IsRaidScene(SceneManager.GetActiveScene())) { _operationSucceeded = true; break; }
                if (SceneLoader.IsSceneLoading)
                {
                    if (loadingSince < 0f) loadingSince = Time.realtimeSinceStartup;
                    if (Time.realtimeSinceStartup >= nextClick)
                    {
                        nextClick = Time.realtimeSinceStartup + SceneClickFeedIntervalSeconds;
                        FeedSceneContinueClick();
                    }
                    if (Time.realtimeSinceStartup - loadingSince > AutotestLoadWatchdogSeconds) { failure = "load_watchdog"; break; }
                }
                else loadingSince = -1f;
                if (Time.realtimeSinceStartup > deadline) { failure = "island_ready_timeout"; break; }
                yield return null;
            }
            if (!_operationSucceeded && failure == null) failure = DescribeAbortReason();
            Record("AUTOTEST_ISLAND_READY", _operationSucceeded ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                DescribeSceneReadiness(SkyIslandSceneReferenceBridge.SceneName), _operationSucceeded ? string.Empty : failure);
            AutotestLog("AUTOTEST_ISLAND_READY", "end", _operationSucceeded ? "PASS" : "FAIL");
        }

        private IEnumerator AutotestExtract()
        {
            Stopwatch sw = Stopwatch.StartNew();
            AutotestLog("AUTOTEST_RETURN_BASE", "begin", null);
            var notes = new List<string>();
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session != null && session.IsReady)
            {
                CloseAutotestPanels();
                string reason;
                bool moved = session.DevAutotestTeleport(session.ValidationExitMarker, 0f, 0f, out reason);
                notes.Add("teleport_dock_exit=" + moved + (moved ? string.Empty : "(" + reason + ")"));
                float until = Time.realtimeSinceStartup + 12f;
                while (SkyIslandSessionOrNull() != null && SkyIslandSessionOrNull().IsReady && Time.realtimeSinceStartup < until) yield return null;
                SkyIslandSession after = SkyIslandSessionOrNull();
                bool extracting = after == null || !after.IsReady;
                notes.Add("dock_extraction_started=" + extracting);
                if (!extracting && after != null)
                {
                    string fallback;
                    notes.Add("fallback_return=" + after.DevAutotestReturnToBase("extract_fallback", out fallback)
                        + (fallback == null ? string.Empty : "(" + fallback + ")"));
                }
            }
            else notes.Add("session_ready=false");
            yield return AutotestWaitBase(notes);
            _skyIslandMode = false;
            Record("AUTOTEST_RETURN_BASE", _operationSucceeded ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                string.Join(",", notes.ToArray()) + "," + DescribeSceneReadiness(BaseSceneNameForValidation()),
                _operationSucceeded ? string.Empty : _operationReason);
            AutotestLog("AUTOTEST_RETURN_BASE", "end", _operationSucceeded ? "PASS" : "FAIL");
        }

        /// <summary>
        /// 等回到基地且就绪。看门狗：官方加载持续超过 <see cref="AutotestLoadWatchdogSeconds"/> 判卡死；
        /// 不在加载、会话已经没了、却迟迟不在基地（黑幕之后没派发返航）超过 8 秒，走主套件同一条官方返基地入口一次。
        /// </summary>
        private IEnumerator AutotestWaitBase(List<string> notes)
        {
            _operationSucceeded = false;
            _operationReason = null;
            float deadline = Time.realtimeSinceStartup + AutotestReturnTimeoutSeconds;
            float loadingSince = -1f, strandedSince = -1f;
            bool recovered = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (SkyIslandSessionOrNull() == null && IsRuntimeReady(BaseSceneNameForValidation())) { _operationSucceeded = true; yield break; }
                if (SceneLoader.IsSceneLoading)
                {
                    strandedSince = -1f;
                    if (loadingSince < 0f) loadingSince = Time.realtimeSinceStartup;
                    if (Time.realtimeSinceStartup - loadingSince > AutotestLoadWatchdogSeconds)
                    {
                        notes.Add("return_load_watchdog");
                        _operationReason = "return_load_watchdog";
                        yield break;
                    }
                }
                else
                {
                    loadingSince = -1f;
                    bool stranded = SkyIslandSessionOrNull() == null && !IsBaseScene();
                    if (!stranded) strandedSince = -1f;
                    else if (strandedSince < 0f) strandedSince = Time.realtimeSinceStartup;
                    else if (!recovered && Time.realtimeSinceStartup - strandedSince > 8f)
                    {
                        recovered = true;
                        notes.Add("recovery_load_base_scene");
                        yield return LoadScene(null, "AUTOTEST_RETURN_BASE_RECOVERY", false, true);
                    }
                }
                yield return null;
            }
            _operationReason = "return_base_timeout";
        }

        #endregion
    }
}
#endif
