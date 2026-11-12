#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationLastShiftDrill.cs - 末班编组站 Dev 演练（**非只读**，一次跑完，2026-11-11）
// ============================================================================
// 照潮闸镇演练（F3GameplayValidationTidegateDrill.cs）的样子做：人从仓库区剪票进末班编组站（顺带验了入口与落地），
// 按 F3 →「验收测试」页的「末班编组站演练」，这一套就在这一趟出击里：
//   装配核对 → 首班教学（被拒的道岔、不在护栏后不发车、撞推车、岔心前等调度）→ 扳道岔与条件出口（含拉铃 14 秒）→
//   车体碰撞盒与导航封线 → 箱柜排货与有条件的箱子 → 押运队（人数、同时活动、阵营、押运长底模）→ 线索与剧情不落盘 →
//   撤离读条（不读满）→ 出生点帧时间 → 还原。
// 等车用快进：LastShiftTrains.DevFastForward 按 0.1 秒一步反复调生产 Tick（不是另一套模拟），所以这一趟的班次会被往前拨几分钟。
// 报告写 BossRushTestReports/BossRushValidation_<runId>.log，AI 只读文字。
// 本文件放入口、开跑门、会话外壳、站位与快进小工具、还原与纯判据；各条用例在 F3GameplayValidationLastShiftDrillCases.cs。
//
// 隔离（tests/LastShiftDrillGuard.py 守着）：
//   1. 整个文件包在 #if BOSSRUSH_DEV 里：正式构建里没有这些代码，F3 面板上也没有这个按钮。
//   2. 独立入口 TryStartLastShiftDrill、独立按钮；先过 CheckLastShiftStartGate（Dev + 专用测试档 + 人在编组站且就绪 + 没开模态），
//      再置 _lastShiftDrill，最后开会话。报告头写 read_only=false / drill=true。只读套件不引用这里任何东西。
//   3. 会话外壳按 _lastShiftDrill 不写运行标记、不抑制提示条、收尾不跑主套件的全宿主清理；主角无敌、回满血照主套件（报告头写明）。
//   4. **不写存档、不收录剧情**：不调 LastShiftStory.CommitRun / TryHandIn、不碰 LastShiftStoryPersistence、不读线索（读线索会进本趟记录、
//      三条齐了还发票根）、不开箱、不签收封签货、不发物品、不写官方时钟。会改的只有这一趟出击的内存状态：
//      班次被快进、道岔被扳过、拉过一次铃（还原时撤掉）、押运队被绕场刷出来。最后一步还原，CompleteSession 里再兜一次。
//   5. 站位一律离出口 5 m 以上、离两股道中心线 4.5 m 以上（LastShiftDrillSafePoint）：站进开着的撤离环读满会真的撤离并写剧情存档，
//      站在道上会被车推走。撤离读条那一条只站 1.5 秒，finally 里必定离开。
// 它证明的仍然只是「这一趟、这一台机器上这几分钟里的行为」：画面、声音、手感与真撤离照旧人工看。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Saves;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>末班编组站演练的用例 ID。会话没起来时批量记 SKIP，报告里不留空白。</summary>
        private static readonly string[] LastShiftDrillCaseIds =
        {
            "LSY_DRILL_ASSEMBLY", "LSY_DRILL_FIRST_TRAIN", "LSY_DRILL_SWITCH_AND_EXITS", "LSY_DRILL_TRAIN_BODIES_AND_NAV", "LSY_DRILL_LOOT",
            "LSY_DRILL_ENEMIES", "LSY_DRILL_STORY", "LSY_DRILL_EXTRACTION_HOLD", "LSY_PERF_SPAWN_10S", "LSY_DRILL_RESTORE",
        };
        /// <summary>单条用例的预算（秒）：快进是按帧分摊的，等一列停靠车最多快进约 10 分钟游戏时间，现实不过几秒。</summary>
        private const float LastShiftDrillCaseTimeoutSeconds = 150f;
        /// <summary>每帧最多快进的游戏秒数（50 步 × 0.1 秒）。</summary>
        private const double LastShiftDrillChunkSeconds = 5.0;
        /// <summary>站位离两股道中心线的最小距离（m）：车体半宽 1.65 + 推挤余量 0.24，往外再留一倍多。</summary>
        private const double LastShiftDrillTrackMargin = 4.5;
        /// <summary>场景包里应有的 11 个交互标记（布局 interacts）。</summary>
        private static readonly string[] LastShiftDrillInteractIds =
            { "switch", "manifest", "bell", "note1", "note2", "note3", "boss", "stamp", "repairBuzzer", "steamValve", "benchTest" };
        /// <summary>刷点分组（布局 spawns 的 group）：三处固定岗、东西两场、外线与押运长。</summary>
        private static readonly string[] LastShiftDrillSpawnGroups = { "west", "east", "outer", "postNorth", "postSouth", "postBond", "boss" };

        private int _lastShiftDrillSceneHandle, _lastShiftDrillRunsBefore, _lastShiftDrillNotesBefore, _lastShiftDrillRecordsBefore;
        private int _lastShiftDrillOpenedBefore, _lastShiftDrillLivePeak;
        private bool _lastShiftDrillJournalBefore, _lastShiftDrillCargoBefore, _lastShiftDrillBellRung, _lastShiftDrillRestored;
        private Vector3 _lastShiftDrillStart;
        private double _lastShiftDrillForwarded;

        internal static bool TryStartLastShiftDrill(ModBehaviour host, out string reason)
        {
            reason = null;
            EnsureAttached(host);
            if (_instance == null) { reason = "验收运行器未就绪"; return false; }
            if (_instance._running) { reason = "已有验收正在运行"; return false; }
            // Dev 构建 + 专用测试档 + 人在编组站且会话就绪 + 没开着模态界面。
            if (!_instance.CheckLastShiftStartGate(out reason)) return false;
            _instance._cancelRequested = false;
            _instance._fatalAbort = false;
            _instance._lastShiftDrill = true;
            _instance.PrepareLastShiftDrillState();
            if (!_instance.BeginSession(out reason)) return false;
            _instance.WriteRaw("SUITE | LAST_SHIFT_DRILL | read_only=false | drill=true | scene=" + SceneManager.GetActiveScene().path);
            _instance._routine = _instance.StartCoroutine(_instance.RunLastShiftDrillSession());
            return true;
        }

        private bool CheckLastShiftStartGate(out string reason)
        {
            reason = null;
            if (!ModBehaviour.DevModeEnabled) { reason = "仅 Dev 构建可用"; return false; }
            if (_host == null) { reason = "ModBehaviour 未就绪"; return false; }
            LastShiftSession session = _host.GetComponent<LastShiftSession>();
            if (session == null) { reason = "请先从仓库区剪票进末班编组站，再运行末班编组站演练"; return false; }
            if (session.IsClosed || !session.IsReady) { reason = "末班编组站尚未就绪或正在返航，请稍后重试"; return false; }
            if (_host.GetComponent<ArenaPrototypeSession>() != null) { reason = "请先退出自建试验场"; return false; }
            if (!LastShiftRaidScene.IsScene(SceneManager.GetActiveScene())) { reason = "当前活动场景不是末班编组站独立关卡"; return false; }
            if (!IsDedicatedCurrentSlot()) { reason = "当前槽不是专用测试档，请回基地先点标记按钮"; return false; }
            if (SavesSystem.IsSaving) { reason = "存档系统正忙"; return false; }
            if (LevelManager.Instance == null || !LevelManager.AfterInit) { reason = "LevelManager 未初始化"; return false; }
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null || player.Health == null || player.Health.IsDead)
            { reason = "玩家或资源未就绪"; return false; }
            if (SceneLoader.IsSceneLoading) { reason = "正在切图"; return false; }
            if (ZombieModeUIHelper.ModalInputLeaseCount != 0) { reason = "存在其他模态输入租约: " + ZombieModeUIHelper.ModalInputLeaseCount; return false; }
            if (session.DevWorld == null || session.DevTrains == null || session.DevExtraction == null)
            { reason = "末班编组站世界、列车或撤离点还没装好"; return false; }
            // 签收过封签货的一趟，箱柜那条的「封签柜还锁着」就判不了，还原也回不去：回仓库区再剪票进一趟。
            if (session.DevLoot != null && session.DevLoot.CargoClaimed)
            { reason = "这一趟已经签收过封签货：回仓库区再剪票进一趟，进站后先跑演练"; return false; }
            return true;
        }

        /// <summary>开跑前记下要还原、要比对的东西（剧情存档快照只读不写）。</summary>
        private void PrepareLastShiftDrillState()
        {
            LastShiftSession session = LastShiftSessionOrNull();
            CharacterMainControl player = CharacterMainControl.Main;
            _lastShiftDrillSceneHandle = SceneManager.GetActiveScene().handle;
            _lastShiftDrillStart = player != null ? player.transform.position : Vector3.zero;
            _lastShiftDrillRunsBefore = LastShiftStory.Runs;
            _lastShiftDrillNotesBefore = LastShiftStory.Notes;
            _lastShiftDrillRecordsBefore = LastShiftStory.Records;
            _lastShiftDrillJournalBefore = LastShiftStory.Journal;
            _lastShiftDrillOpenedBefore = session != null && session.DevLoot != null ? session.DevLoot.DevOpenedCount : 0;
            _lastShiftDrillCargoBefore = session != null && session.DevLoot != null && session.DevLoot.CargoClaimed;
            _lastShiftDrillLivePeak = session != null && session.DevEnemies != null ? session.DevEnemies.DevLive : 0;
            _lastShiftDrillBellRung = false;
            _lastShiftDrillRestored = false;
            _lastShiftDrillForwarded = 0;
        }

        /// <summary>演练只有一个阶段，收尾不切图、不跑主套件清理（CompleteSession 按 _lastShiftDrill 处理）。</summary>
        private IEnumerator RunLastShiftDrillSession()
        {
            yield return null;
            try
            {
                yield return DriveSessionPhase(RunLastShiftDrillSuite(), false);
            }
            finally { CompleteSession(); }
        }

        private IEnumerator RunLastShiftDrillSuite()
        {
            LastShiftSession live;
            string gone;
            if (!LastShiftDrillLive(out live, out gone))
            {
                for (int i = 0; i < LastShiftDrillCaseIds.Length; i++) Record(LastShiftDrillCaseIds[i], "SKIP", 0L, string.Empty, gone);
                yield break;
            }
            SetStage("末班编组站演练 1/7 装配核对与首班教学");
            yield return RunLastShiftDrillCase("LSY_DRILL_ASSEMBLY", RunLastShiftDrillAssembly);
            yield return RunLastShiftDrillCase("LSY_DRILL_FIRST_TRAIN", RunLastShiftDrillFirstTrain);
            SetStage("末班编组站演练 2/7 道岔、条件出口与拉铃（快进班次）");
            yield return RunLastShiftDrillCase("LSY_DRILL_SWITCH_AND_EXITS", RunLastShiftDrillSwitchAndExits);
            SetStage("末班编组站演练 3/7 车体碰撞盒与导航封线（快进班次）");
            yield return RunLastShiftDrillCase("LSY_DRILL_TRAIN_BODIES_AND_NAV", RunLastShiftDrillTrainBodies);
            SetStage("末班编组站演练 4/7 箱柜与有条件的箱子（快进班次）");
            yield return RunLastShiftDrillCase("LSY_DRILL_LOOT", RunLastShiftDrillLoot);
            SetStage("末班编组站演练 5/7 押运队与线索");
            yield return RunLastShiftDrillCase("LSY_DRILL_ENEMIES", RunLastShiftDrillEnemies);
            yield return RunLastShiftDrillCase("LSY_DRILL_STORY", RunLastShiftDrillStory);
            SetStage("末班编组站演练 6/7 撤离读条与出生点帧时间");
            yield return RunLastShiftDrillCase("LSY_DRILL_EXTRACTION_HOLD", RunLastShiftDrillExtractionHold);
            yield return LastShiftDrillPerformance();
            SetStage("末班编组站演练 7/7 还原");
            RecordLastShiftDrillRestore();
        }

        // ====================================================================
        // 外壳与公共小工具
        // ====================================================================

        /// <summary>
        /// 末班编组站演练用例的隔离壳（同 RunTidegateDrillCase）：单条异常或超时只记 _UNHANDLED，不拖垮后面；不做清场。
        /// 用例自己记自己的 PASS / FAIL / WARN / SKIP。
        /// </summary>
        private IEnumerator RunLastShiftDrillCase(string caseId, Func<IEnumerator> factory)
        {
            if (ShouldAbort())
            {
                Record(caseId, "SKIP", 0L, string.Empty, DescribeAbortReason());
                yield break;
            }
            LastShiftSession live;
            string gone;
            if (!LastShiftDrillLive(out live, out gone))
            {
                Record(caseId, "SKIP", 0L, string.Empty, gone);
                yield break;
            }
            IEnumerator inner = null;
            bool recorded = false;
            try { inner = factory(); }
            catch (Exception e)
            {
                Record(caseId, "FAIL", 0L, string.Empty, "case_factory_threw:" + e);
                recorded = true;
            }
            if (inner == null)
            {
                if (!recorded) Record(caseId, "FAIL", 0L, string.Empty, "case_factory_returned_null");
                yield break;
            }
            Stopwatch sw = Stopwatch.StartNew();
            ValidationCoroutineStack stack = new ValidationCoroutineStack(inner);
            try
            {
                while (!ShouldAbort())
                {
                    object current;
                    Exception error;
                    bool more = TryStep(stack, out current, out error);
                    if (error != null || sw.Elapsed.TotalSeconds > LastShiftDrillCaseTimeoutSeconds)
                    {
                        Record(caseId + "_UNHANDLED", "FAIL", sw.ElapsedMilliseconds, "budget_s=" + LastShiftDrillCaseTimeoutSeconds,
                            error != null ? error.ToString() : "case_timeout");
                        break;
                    }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                try { stack.Dispose(); }
                catch (Exception e) { Record(caseId + "_DISPOSE", "FAIL", sw.ElapsedMilliseconds, string.Empty, e.ToString()); }
            }
        }

        private LastShiftSession LastShiftSessionOrNull()
        {
            return _host == null ? null : _host.GetComponent<LastShiftSession>();
        }

        /// <summary>这一趟还在：会话就绪、还是开跑时那个场景实例、主角活着、列车与撤离 owner 都在。</summary>
        private bool LastShiftDrillLive(out LastShiftSession session, out string gone)
        {
            gone = null;
            session = LastShiftSessionOrNull();
            CharacterMainControl player = CharacterMainControl.Main;
            if (session == null) gone = "last_shift_session_missing";
            else if (session.IsClosed || !session.IsReady) gone = "last_shift_session_not_ready";
            else if (SceneManager.GetActiveScene().handle != _lastShiftDrillSceneHandle) gone = "last_shift_scene_changed";
            else if (player == null || player.Health == null || player.Health.IsDead) gone = "player_unavailable";
            else if (session.DevWorld == null || session.DevTrains == null || session.DevExtraction == null) gone = "last_shift_world_missing";
            return gone == null;
        }

        private void RecordLastShiftDrill(string caseId, Stopwatch sw, List<string> errors, List<string> notes)
        {
            string metrics = string.Join(" | ", notes.ToArray());
            if (errors.Count == 0) Record(caseId, "PASS", sw.ElapsedMilliseconds, metrics, string.Empty);
            else Record(caseId, "FAIL", sw.ElapsedMilliseconds, metrics, string.Join(",", errors.ToArray()));
        }

        /// <summary>站到离 <paramref name="near"/> 最近的安全可走面（离出口 5 m、离道 4.5 m 外）并等落稳。</summary>
        private IEnumerator LastShiftDrillStand(LastShiftSession session, Vector3 near, List<string> errors, string tag)
        {
            Vector3 point;
            if (!LastShiftDrillSafePoint(session, near, out point)) { errors.Add(tag + ":no_safe_point"); yield break; }
            string why;
            if (!session.DevTeleport(point, out why)) { errors.Add(tag + ":teleport_" + why); yield break; }
            yield return WaitSeconds(0.5f);
        }

        /// <summary>
        /// 安全站位：在 <paramref name="near"/> 周围由近到远（0–15 m，八个方向）找本图可走面，离任何出口不到 5 m、
        /// 或在地面层且离两股道中心线不到 4.5 m 的点都不要（司机室楼面与跨线桥 y ≥ 4.5 不受车影响）。
        /// </summary>
        private static bool LastShiftDrillSafePoint(LastShiftSession session, Vector3 near, out Vector3 point)
        {
            point = near;
            for (int ring = 0; ring <= 5; ring++)
                for (int k = 0; k < (ring == 0 ? 1 : 8); k++)
                {
                    float angle = k * Mathf.PI * 0.25f, radius = ring * 3f;
                    Vector3 probe = near + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    Vector3 snapped;
                    if (!session.DevSnapToWalkable(probe, out snapped)) continue;
                    if (LastShiftDrillExitWithin(session.DevWorld, snapped, 5f) != null) continue;
                    if (snapped.y < 4.5f && !LastShiftDrillTrackClear(snapped.x, -snapped.z, LastShiftDrillTrackMargin)) continue;
                    point = snapped;
                    return true;
                }
            return false;
        }

        private static Transform LastShiftDrillExitWithin(LastShiftWorld world, Vector3 position, float within)
        {
            foreach (Transform exit in world.Exits.Values)
            {
                Vector3 offset = exit.position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude < within * within) return exit;
            }
            return null;
        }

        private static float LastShiftDrillFlat(Vector3 a, Vector3 b)
        {
            Vector3 offset = a - b;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>主角此刻是否站在南护栏后（班次表 AtSouthRail，three.js 坐标）。</summary>
        private static bool LastShiftDrillAtRail()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return false;
            Vector3 p = player.transform.position;
            return LastShiftTimetable.AtSouthRail(p.x, -p.z);
        }

        /// <summary>站到南护栏后（布局 facilities.watch 往南 2 m）；找不到落在护栏区里的安全点时报错。</summary>
        private IEnumerator LastShiftDrillStandAtRail(LastShiftSession session, List<string> errors)
        {
            Vector3 target = new Vector3((float)LastShiftTimetable.WatchX, 0f, -(float)(LastShiftTimetable.WatchZ + 2));
            Vector3 point;
            if (session.DevSnapToWalkable(target, out point) && LastShiftTimetable.AtSouthRail(point.x, -point.z)
                && LastShiftDrillExitWithin(session.DevWorld, point, 5f) == null
                && LastShiftDrillTrackClear(point.x, -point.z, LastShiftDrillTrackMargin))
            {
                string why;
                if (!session.DevTeleport(point, out why)) errors.Add("rail:teleport_" + why);
                yield return WaitSeconds(0.4f);
            }
            else errors.Add("rail:no_safe_point_behind_south_rail");
            if (!LastShiftDrillAtRail()) errors.Add("rail:player_not_behind_south_rail");
        }

        /// <summary>站到南护栏之外（运单牌旁边，离护栏区 2 m 以上），首班会停表等人。</summary>
        private IEnumerator LastShiftDrillStandAwayFromRail(LastShiftSession session, List<string> errors)
        {
            Transform manifest;
            Vector3 near = session.DevWorld.Interacts.TryGetValue("manifest", out manifest)
                ? manifest.position + new Vector3(1.5f, 0f, 3f)
                : new Vector3((float)LastShiftTimetable.WatchX, 0f, -(float)(LastShiftTimetable.WatchZ - 10));
            yield return LastShiftDrillStand(session, near, errors, "away");
            if (LastShiftDrillAtRail()) errors.Add("away:still_behind_south_rail");
        }

        /// <summary>同时活动人数的峰值（快进与等待时顺手采样，敌人用例按峰值判 ≤ 8）。</summary>
        private void SampleLastShiftLive(LastShiftSession session)
        {
            if (session == null || session.DevEnemies == null) return;
            int live = session.DevEnemies.DevLive;
            if (live > _lastShiftDrillLivePeak) _lastShiftDrillLivePeak = live;
        }

        /// <summary>按帧分摊快进 <paramref name="seconds"/> 游戏秒（每帧最多 5 秒），走生产 Tick。</summary>
        private IEnumerator LastShiftDrillAdvance(double seconds)
        {
            double left = seconds;
            while (left > 1e-6 && !ShouldAbort())
            {
                LastShiftSession session;
                string gone;
                if (!LastShiftDrillLive(out session, out gone)) yield break;
                double done = session.DevTrains.DevFastForward(CharacterMainControl.Main, Math.Min(LastShiftDrillChunkSeconds, left));
                if (done <= 0) yield break;
                _lastShiftDrillForwarded += done;
                left -= done;
                SampleLastShiftLive(session);
                yield return null;
            }
        }

        /// <summary>
        /// 快进到 <paramref name="reached"/> 成立（逐 0.1 秒判，停在第一次成立的那一步），最多 <paramref name="maxSeconds"/> 游戏秒。
        /// <paramref name="resolveTraining"/> 时顺手解开首班教学的两道等待：首班等人就站到南护栏后，岔心前等调度就扳一次道岔（记进 notes）。
        /// 到不了时往 errors 里记 tag:not_reached。
        /// </summary>
        private IEnumerator LastShiftDrillAdvanceTo(Func<LastShiftTimetable, bool> reached, double maxSeconds, bool resolveTraining,
            List<string> errors, List<string> notes, string tag)
        {
            double spent = 0;
            int railTries = 0, switchTries = 0;
            while (!ShouldAbort())
            {
                LastShiftSession session;
                string gone;
                if (!LastShiftDrillLive(out session, out gone)) { errors.Add(tag + ":" + gone); yield break; }
                LastShiftTrains trains = session.DevTrains;
                LastShiftTimetable t = trains.Timetable;
                if (reached(t)) yield break;
                if (spent >= maxSeconds) { errors.Add(tag + ":not_reached_phase_" + t.Phase + "_trip_" + t.Trip); yield break; }
                if (resolveTraining && t.Training && t.Phase == LastShiftPhase.Approach && !t.CartHit && !t.DemoForced && !LastShiftDrillAtRail())
                {
                    if (railTries++ >= 2) { errors.Add(tag + ":cannot_stand_behind_rail"); yield break; }
                    notes.Add(tag + ":stood_behind_rail_for_first_train");
                    yield return LastShiftDrillStandAtRail(session, errors);
                    continue;
                }
                if (resolveTraining && t.DispatchWait)
                {
                    if (switchTries++ >= 2) { errors.Add(tag + ":dispatch_switch_refused"); yield break; }
                    notes.Add(tag + ":switch_thrown_for_dispatch_wait");
                    trains.DevThrowSwitch();
                    yield return null;
                    continue;
                }
                CharacterMainControl player = CharacterMainControl.Main;
                double before = spent;
                for (int i = 0; i < 50 && spent < maxSeconds; i++)
                {
                    double done = trains.DevFastForward(player, LastShiftTrains.DevStep);
                    if (done <= 0) break;
                    spent += done;
                    if (reached(t) || (resolveTraining && t.DispatchWait)) break;
                }
                _lastShiftDrillForwarded += spent - before;
                if (spent == before) { errors.Add(tag + ":fast_forward_stalled"); yield break; }
                SampleLastShiftLive(session);
                yield return null;
            }
        }

        // ====================================================================
        // 9. 出生点帧时间（复用 SamplePerformance：10 秒、p95 ≤ 50 ms）
        // ====================================================================

        private IEnumerator LastShiftDrillPerformance()
        {
            LastShiftSession session;
            string gone;
            var errors = new List<string>();
            if (LastShiftDrillLive(out session, out gone))
            {
                yield return LastShiftDrillStand(session, session.DevWorld.PlayerSpawn.position, errors, "perf_spawn");
                yield return WaitSeconds(1f);
            }
            else errors.Add(gone);
            if (errors.Count != 0)
            {
                Record("LSY_PERF_SPAWN_10S", "FAIL", 0L, "station=spawn,samples=0", string.Join(",", errors.ToArray()));
                yield break;
            }
            WriteRaw("LSY_PERF_SPAWN_10S | station=spawn | position=" + CharacterMainControl.Main.transform.position.ToString("F1")
                + " | live_enemies=" + (session.DevEnemies == null ? -1 : session.DevEnemies.DevLive)
                + " | phase=" + session.DevTrains.Timetable.Phase);
            yield return SamplePerformance("LSY_PERF_SPAWN_10S", 10f, false);
        }

        // ====================================================================
        // 10. 还原
        // ====================================================================

        private void RecordLastShiftDrillRestore()
        {
            string metrics;
            List<string> errors = RestoreLastShiftDrillState(out metrics);
            Record("LSY_DRILL_RESTORE", errors.Count == 0 ? "PASS" : "FAIL", 0L, metrics,
                errors.Count == 0 ? string.Empty : "还原不完整：" + string.Join(",", errors.ToArray()));
        }

        /// <summary>
        /// 还原这一趟出击的内存状态（幂等）：撤掉演练拉过的铃（接客亭关上）、地面箭头收起、主角送回开跑处（安全站位），再逐项读回核对；
        /// 剧情存档快照逐项比对（演练不写，变了就是缺陷）。班次快进过、道岔扳过不往回拨，报告里写明。
        /// </summary>
        private List<string> RestoreLastShiftDrillState(out string metrics)
        {
            var errors = new List<string>();
            var notes = new List<string>();
            LastShiftSession session = LastShiftSessionOrNull();
            bool alive = session != null && !session.IsClosed && session.IsReady && session.DevTrains != null;
            if (alive)
            {
                LastShiftTrains trains = session.DevTrains;
                LastShiftTimetable t = trains.Timetable;
                CharacterMainControl player = CharacterMainControl.Main;
                if (_lastShiftDrillBellRung) t.DevClearBell();
                trains.Ringing = false;
                trains.DevHideArrows();
                trains.DevSyncExits(player);
                Vector3 back;
                if (!LastShiftDrillSafePoint(session, _lastShiftDrillStart, out back)) back = session.DevWorld.PlayerSpawn.position;
                string why;
                if (player != null && player.Health != null && !player.Health.IsDead && !session.DevTeleport(back, out why))
                    errors.Add("player_not_returned_" + why);
                if (_lastShiftDrillBellRung && (t.BellAt >= 0 || session.DevExtraction.IsOpen("bell"))) errors.Add("bell_not_cleared");
                if (trains.DevArrowCount != 0) errors.Add("dispatch_arrows_left_" + trains.DevArrowCount);
                LastShiftLoot loot = session.DevLoot;
                if (loot != null && loot.DevOpenedCount != _lastShiftDrillOpenedBefore) errors.Add("containers_opened_by_drill");
                if (loot != null && loot.CargoClaimed != _lastShiftDrillCargoBefore) errors.Add("cargo_claimed_by_drill");
                notes.Add("forwarded_game_s=" + _lastShiftDrillForwarded.ToString("F0") + ",trip=" + t.Trip + ",phase=" + t.Phase
                    + ",line=" + t.Line + ",training=" + t.Training + ",bell_cleared=" + _lastShiftDrillBellRung
                    + ",live_peak=" + _lastShiftDrillLivePeak);
                notes.Add("班次快进与道岔方向不往回拨；押运队按绕场刷出的保留（这一趟之后照样能打）");
            }
            else notes.Add("session_gone");
            if (LastShiftStory.Runs != _lastShiftDrillRunsBefore || LastShiftStory.Notes != _lastShiftDrillNotesBefore
                || LastShiftStory.Records != _lastShiftDrillRecordsBefore || LastShiftStory.Journal != _lastShiftDrillJournalBefore)
                errors.Add("story_save_changed_by_drill");
            _lastShiftDrillRestored = true;
            metrics = string.Join(",", notes.ToArray());
            return errors;
        }

        /// <summary>CompleteSession 的兜底（取消、异常、超时、宿主销毁也走到）：套件自己的还原没跑到时在这里同步还原一次。</summary>
        private void FinishLastShiftDrillRestoreSynchronously()
        {
            if (_lastShiftDrillRestored) return;
            try
            {
                string metrics;
                List<string> errors = RestoreLastShiftDrillState(out metrics);
                WriteRaw("LSY_DRILL_RESTORE_FALLBACK | " + (errors.Count == 0 ? "ok" : string.Join(",", errors.ToArray())) + " | " + metrics);
            }
            catch (Exception e) { WriteRaw("LSY_DRILL_RESTORE_FALLBACK | threw | " + e.Message); }
        }

        // ====================================================================
        // 纯判据（tests/fixtures/LastShiftDrillJudges 逐字抽出来执行）
        // ====================================================================

        #region 纯判据
        /// <summary>three.js 坐标 (x, z) 离西、东两股道中心线都超过 <paramref name="margin"/> 米（高处由调用方按高度豁免）。</summary>
        internal static bool LastShiftDrillTrackClear(double x, double z, double margin)
        {
            return Math.Abs(x - LastShiftTimetable.RailX(z, 'W')) > margin && Math.Abs(x - LastShiftTimetable.RailX(z, 'E')) > margin;
        }

        /// <summary>首班离开南护栏还能等几秒（离开累计到 60 秒照常发车）：留 4 秒余量，最多取 8 秒做「不发车」的证据；不足 3 秒返回 0。</summary>
        internal static double LastShiftDrillAwayBudget(double awayFromRail, double fallback)
        {
            double budget = Math.Min(8.0, fallback - awayFromRail - 4.0);
            return budget >= 3.0 ? budget : 0.0;
        }

        /// <summary>首班等人：离开护栏期间相位时间不走（容差 0.01 秒），离开累计至少涨了快进量的 90%。</summary>
        internal static bool LastShiftDrillHeldForPlayer(double phaseTimeBefore, double phaseTimeAfter, double awayBefore, double awayAfter,
            double forwarded, out string reason)
        {
            reason = null;
            if (Math.Abs(phaseTimeAfter - phaseTimeBefore) > 0.01) { reason = "phase_time_advanced_" + (phaseTimeAfter - phaseTimeBefore).ToString("F2"); return false; }
            if (awayAfter - awayBefore < forwarded * 0.9) { reason = "away_not_accumulated_" + (awayAfter - awayBefore).ToString("F2"); return false; }
            return true;
        }

        /// <summary>首班停在岔心前等调度：还在进站、等扳岔、相位时间钉在教学停车点、车头在 z = 20。</summary>
        internal static bool LastShiftDrillTrainingHoldOk(LastShiftPhase phase, bool dispatchWait, double phaseTime, double leadZ, out string reason)
        {
            reason = null;
            if (phase != LastShiftPhase.Arrival) reason = "phase_" + phase;
            else if (!dispatchWait) reason = "not_waiting_for_dispatch";
            else if (Math.Abs(phaseTime - LastShiftTimetable.TrainingHold) > 1e-6) reason = "phase_time_" + phaseTime.ToString("F3");
            else if (Math.Abs(leadZ - 20.0) > 1e-3) reason = "lead_z_" + leadZ.ToString("F2");
            return reason == null;
        }

        /// <summary>一处条件出口此刻：撤离 owner 的开关与班次规则一致，贴地环跟着开关显隐。</summary>
        internal static bool LastShiftDrillExitAgrees(string id, bool ownerOpen, bool ringVisible, bool ruleOpen, out string reason)
        {
            reason = null;
            if (ownerOpen != ruleOpen) reason = id + "_owner_" + (ownerOpen ? "open" : "closed") + "_rule_" + (ruleOpen ? "open" : "closed");
            else if (ringVisible != ownerOpen) reason = id + "_ring_" + (ringVisible ? "shown" : "hidden");
            return reason == null;
        }

        /// <summary>接客亭：拉铃后满 14 秒才开；贴着门槛 ±0.15 秒（帧边界）不判。没拉过铃（负数）必须关着。</summary>
        internal static bool LastShiftDrillBellOk(double sinceRing, bool open)
        {
            if (double.IsNaN(sinceRing) || double.IsInfinity(sinceRing)) return false;
            if (sinceRing < 0) return !open;
            if (Math.Abs(sinceRing - LastShiftTimetable.BellWait) < 0.15) return true;
            return open == (sinceRing >= LastShiftTimetable.BellWait);
        }

        /// <summary>车体碰撞盒落在车厢上（three.js 坐标，容差 5 cm）、并在本线车体包络里。</summary>
        internal static bool LastShiftDrillBodyOnLine(double carX, double carZ, double bodyX, double bodyZ, char line, out string reason)
        {
            reason = null;
            if (Math.Abs(carX - bodyX) > 0.05 || Math.Abs(carZ - bodyZ) > 0.05)
                reason = "body_off_car_" + (bodyX - carX).ToString("F2") + "_" + (bodyZ - carZ).ToString("F2");
            else if (!LastShiftTimetable.InEnvelope(bodyX, bodyZ, line)) reason = "body_off_line_" + line;
            return reason == null;
        }

        /// <summary>
        /// 列车封线：本线包络里要有导航三角形、净空时可走；停靠时一个都不可走；线路净空后包络里的可走数与全图可走数回到开车前；
        /// 只在另一股道包络里的三角形停靠前后不受影响。
        /// </summary>
        internal static bool LastShiftDrillNavOk(int envelopeNodes, int baselineWalkable, int dwellWalkable, int clearWalkable,
            int baselineTotal, int clearTotal, int otherBaseline, int otherDwell, out string reason)
        {
            var problems = new List<string>();
            if (envelopeNodes <= 0) problems.Add("no_nav_nodes_on_line");
            if (baselineWalkable <= 0) problems.Add("line_not_walkable_when_clear");
            if (dwellWalkable != 0) problems.Add("walkable_inside_envelope_during_dwell_" + dwellWalkable);
            if (clearWalkable != baselineWalkable) problems.Add("envelope_not_restored_" + clearWalkable + "_of_" + baselineWalkable);
            if (clearTotal != baselineTotal) problems.Add("walkable_total_" + clearTotal + "_was_" + baselineTotal);
            if (otherDwell != otherBaseline) problems.Add("other_line_changed_" + otherDwell + "_was_" + otherBaseline);
            reason = string.Join(",", problems.ToArray());
            return problems.Count == 0;
        }

        /// <summary>刷怪账目：全趟 24–30 人，已刷不超过全趟，同时活动（此刻与采样峰值）不超过 8，流动不超过 5。</summary>
        internal static bool LastShiftDrillPopulationOk(int population, int spawned, int liveNow, int livePeak, int mobile, out string reason)
        {
            var problems = new List<string>();
            if (population < 24 || population > 30) problems.Add("population_" + population);
            if (spawned < 0 || spawned > population) problems.Add("spawned_" + spawned + "_of_" + population);
            if (liveNow > 8 || livePeak > 8) problems.Add("live_over_cap_" + Math.Max(liveNow, livePeak));
            if (mobile > 5) problems.Add("mobile_over_cap_" + mobile);
            reason = string.Join(",", problems.ToArray());
            return problems.Count == 0;
        }

        /// <summary>
        /// 冻着的围圈小队成员没被官方 AI 带走：离自己的站位水平 ≤ 1 m、身子朝圈心偏差 ≤ 35°（两者都是官方 AI 一旦在冻结期间跑起来就会坏的量）。
        /// </summary>
        internal static bool LastShiftDrillSquadHoldOk(double drift, double facingDegrees, out string reason)
        {
            reason = null;
            if (double.IsNaN(drift) || drift > 1.0) reason = "drift_" + drift.ToString("F2");
            else if (double.IsNaN(facingDegrees) || facingDegrees > 35.0) reason = "facing_" + facingDegrees.ToString("F0");
            return reason == null;
        }

        /// <summary>撤离读条在北公路读到一截（≥ 0.8 秒）且没读满（5 秒）。</summary>
        internal static bool LastShiftDrillHoldOk(string exit, double held, out string reason)
        {
            reason = null;
            if (exit != "north") reason = "countdown_not_on_north_" + (exit ?? "none");
            else if (double.IsNaN(held) || held < 0.8) reason = "countdown_not_advancing_" + held.ToString("F2");
            else if (held >= 5.0) reason = "countdown_filled";
            return reason == null;
        }

        /// <summary>HUD 小标「末班票 n/3」：故事收尾后或一条没找到时不显示；否则以 n/3 结尾。</summary>
        internal static bool LastShiftDrillCounterOk(string counter, bool journal, int found, int total)
        {
            if (journal || found <= 0) return counter == null;
            return counter != null && counter.EndsWith(found + "/" + total, StringComparison.Ordinal);
        }

        internal static string LastShiftDrillJoinSorted(IEnumerable<string> ids)
        {
            var list = new List<string>(ids);
            list.Sort(StringComparer.Ordinal);
            return string.Join(",", list.ToArray());
        }

        /// <summary>几条字幕里有几条含任一关键词（中英两种语言各给一个）。</summary>
        internal static int LastShiftDrillCount(IList<string> lines, string[] needles)
        {
            int count = 0;
            if (lines == null) return 0;
            foreach (string line in lines) if (LastShiftDrillContainsAny(line, needles)) count++;
            return count;
        }

        internal static bool LastShiftDrillContainsAny(string text, params string[] needles)
        {
            if (string.IsNullOrEmpty(text) || needles == null) return false;
            foreach (string needle in needles)
                if (!string.IsNullOrEmpty(needle) && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
        #endregion
    }
}
#endif
