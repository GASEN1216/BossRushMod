#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationTidegateDrill.cs - 潮闸镇 Dev 演练（**非只读**，一次跑完，2026-11-06）
// ============================================================================
// owner 2026-11-06：「直接做演练一次性测完……尽可能减少人工测试，最后告诉我跑完后我要测什么」。
// 人乘竹筏进潮闸镇（顺带验了入口与落地），按 F3 →「验收测试」页的「潮闸镇演练」，这一套就在这一趟出击里：
//   装配核对 → 拨潮相把退潮 / 低潮 / 涨潮 / 高潮各走一遍，核对水体、出口与撤离环、箱子、导航、HUD → 换相前提示 →
//   三阀一闸（含空手被拒）、泻湖放水、海堤增援 → 剧情箱柜里有什么 → 游泳（轻装浮、超重沉、边界墙）→ 深水箱子提示 →
//   淘沙帮跟潮走与被潮水卷走 → 灯塔（光束、灯数组、引怪、全图显示、45 秒来船）→ 撤离读条（不读满）→
//   刷怪（阵营、卡模、距离、守卫配枪、沙老大）→ 地名、海浪、浮标钟与提示音 → 码头 / 老镇帧时间 → 还原。
// 报告写 BossRushTestReports/BossRushValidation_<runId>.log，AI 只读文字。
// 本文件放入口、开跑门、会话外壳、站位小工具、还原与纯判据；各条用例在 F3GameplayValidationTidegateDrillCases.cs。
//
// 隔离（tests/TidegateDrillGuard.py 守着）：
//   1. 整个文件包在 #if BOSSRUSH_DEV 里：正式构建里没有这些代码，F3 面板上也没有这个按钮。
//   2. 独立入口 TryStartTidegateDrill、独立按钮；先过 CheckTidegateStartGate（Dev + 专用测试档 + 人在潮闸镇且就绪 + 没开模态），
//      再置 _tidegateDrill，最后开会话。报告头写 read_only=false / drill=true。只读套件不引用这里任何东西。
//   3. 会话外壳按 _tidegateDrill 不写运行标记、不抑制提示条、收尾不跑主套件的全宿主清理；主角无敌、回满血照主套件（报告头写明）。
//   4. **不写存档、不收录**：不开箱（只读箱子里有什么，不翻开，日志页不会记进剧情存档）、不发物品、不改剧情、
//      不写官方时钟（拨潮相只加在 TidegateTide.ClockHours）。会改的只有这一趟出击的内存状态，最后一步还原：
//      潮相偏移回到开跑前、游泳覆盖清掉、阀门 / 大闸 / 灯塔复位、全图显示提前结束、演练招来的海堤增援移走、主角送回开跑处。
//      还原另在 CompleteSession 里兜一次（取消、异常、超时、宿主销毁也走到）。
//   5. 站位一律离出口 5 m 以上（TidegateDrillSafePoint）：站进开着的撤离环读满会真的返航。撤离读条那一条只站 1.6 秒，finally 里必定离开。
//   6. 刷怪点每趟只刷一次：演练绕场一圈会把各处的敌人刷出来，这一趟之后的人工测试照样能打，但不会再有新的。
// 它证明的仍然只是「这一趟、这一台机器上这几分钟里的行为」：画面、声音好不好听、手感，照旧人工看。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Duckov.Utilities;
using Saves;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>潮闸镇演练的用例 ID。会话没起来时批量记 SKIP，报告里不留空白。</summary>
        private static readonly string[] TidegateDrillCaseIds =
        {
            "TIDE_DRILL_ASSEMBLY", "TIDE_DRILL_TIDE_PHASES", "TIDE_DRILL_TIDE_NOTICES", "TIDE_DRILL_VALVES_SLUICE",
            "TIDE_DRILL_STORY_CONTAINERS", "TIDE_DRILL_SWIM", "TIDE_DRILL_FLOOD_HINT", "TIDE_DRILL_DIGGERS", "TIDE_DRILL_LIGHTHOUSE",
            "TIDE_DRILL_EXTRACTION_HOLD", "TIDE_DRILL_ENEMIES", "TIDE_DRILL_REGION_SOUNDS", "TIDE_DRILL_RESTORE",
        };
        /// <summary>单条用例的预算（秒）：灯塔那条要等 45 秒来船，留足余量；超了记 _UNHANDLED 继续下一条。</summary>
        private const float TidegateDrillCaseTimeoutSeconds = 150f;
        /// <summary>刷怪绕场：镇上、水厂、码头、栈桥、泵站、潮位站、芦苇荡、检查站。</summary>
        private static readonly string[] TidegateDrillTourPois =
            { "POI_老镇", "POI_广场", "POI_第三水厂", "POI_码头", "POI_栈桥", "POI_镇区泵站", "POI_潮位站", "POI_芦苇荡", "POI_公路检查站" };
        /// <summary>场景包里应有的 17 处地标（布局 `pois`）。</summary>
        private static readonly string[] TidegateDrillPoiNames =
        {
            "公路检查站", "第三水厂", "老镇", "广场", "镇区泵站", "码头", "栈桥", "滩涂", "沉船", "挖沙船", "泻湖", "储蓄所", "大闸",
            "芦苇荡", "灯塔岬", "潮位站", "暗渠",
        };
        /// <summary>海浪发声点所在的地标（与 TidegateSounds.ShorePois 一致）。</summary>
        private static readonly string[] TidegateDrillShorePois = { "POI_码头", "POI_滩涂", "POI_栈桥", "POI_灯塔岬" };
        /// <summary>剧情箱柜（与 TidegateLoot.StoryContainers 一致；金库那只要开闸后的低潮才进得去，排最后）。</summary>
        private static readonly string[] TidegateDrillStoryMarkers =
            { "LOOT_safe_68", "LOOT_house_69", "LOOT_house_70", "LOOT_locker_72", "LOOT_safe_77", "LOOT_vault_71" };

        private int _tidegateDrillSceneHandle, _tidegateDrillPagesBefore;
        private Vector3 _tidegateDrillStart;
        private double _tidegateDrillOffsetBefore;
        private bool _tidegateDrillTouched, _tidegateDrillRestored;
        private TidegateTide _tidegateDrillTide;
        private Action<TidegateTidePhase, TidegateTidePhase> _tidegateDrillPhaseHandler;
        private readonly List<string> _tidegateDrillPhaseEvents = new List<string>();

        internal static bool TryStartTidegateDrill(ModBehaviour host, out string reason)
        {
            reason = null;
            EnsureAttached(host);
            if (_instance == null) { reason = "验收运行器未就绪"; return false; }
            if (_instance._running) { reason = "已有验收正在运行"; return false; }
            // Dev 构建 + 专用测试档 + 人在潮闸镇且会话就绪 + 没开着模态界面。
            if (!_instance.CheckTidegateStartGate(out reason)) return false;
            _instance._cancelRequested = false;
            _instance._fatalAbort = false;
            _instance._tidegateDrill = true;
            _instance.PrepareTidegateDrillState();
            if (!_instance.BeginSession(out reason)) return false;
            _instance.WriteRaw("SUITE | TIDEGATE_DRILL | read_only=false | drill=true | scene=" + SceneManager.GetActiveScene().path);
            _instance._routine = _instance.StartCoroutine(_instance.RunTidegateDrillSession());
            return true;
        }

        private bool CheckTidegateStartGate(out string reason)
        {
            reason = null;
            if (!ModBehaviour.DevModeEnabled) { reason = "仅 Dev 构建可用"; return false; }
            if (_host == null) { reason = "ModBehaviour 未就绪"; return false; }
            TidegateSession session = _host.GetComponent<TidegateSession>();
            if (session == null) { reason = "请先乘竹筏进潮闸镇，再运行潮闸镇演练"; return false; }
            if (session.IsClosed || !session.IsReady) { reason = "潮闸镇尚未就绪或正在返航，请稍后重试"; return false; }
            if (_host.GetComponent<ArenaPrototypeSession>() != null) { reason = "请先退出自建试验场"; return false; }
            if (!TidegateRaidScene.IsScene(SceneManager.GetActiveScene())) { reason = "当前活动场景不是潮闸镇独立关卡"; return false; }
            if (!IsDedicatedCurrentSlot()) { reason = "当前槽不是专用测试档，请回基地先点标记按钮"; return false; }
            if (SavesSystem.IsSaving) { reason = "存档系统正忙"; return false; }
            if (LevelManager.Instance == null || !LevelManager.AfterInit) { reason = "LevelManager 未初始化"; return false; }
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null || player.Health == null || player.Health.IsDead)
            { reason = "玩家或资源未就绪"; return false; }
            if (SceneLoader.IsSceneLoading) { reason = "正在切图"; return false; }
            if (ZombieModeUIHelper.ModalInputLeaseCount != 0) { reason = "存在其他模态输入租约: " + ZombieModeUIHelper.ModalInputLeaseCount; return false; }
            if (session.DevWorld == null || session.Tide == null) { reason = "潮闸镇世界或潮汐还没装好"; return false; }
            // 演练要从「刚进图」的状态开始：机关动过（手轮装上就没了、增援已经来过）或箱子翻过（官方搜空会把箱子藏起来），
            // 好几条会假红，还原也回不到动过之前。
            if (session.DevDevices != null && session.DevDevices.DevTouched)
            { reason = "这一趟已经拧过阀门或点过灯塔：回基地再乘竹筏进一趟，进图后先跑演练"; return false; }
            if (session.DevLoot != null && session.DevLoot.OpenedCount > 0)
            { reason = "这一趟已经翻过箱子：回基地再乘竹筏进一趟，进图后先跑演练"; return false; }
            return true;
        }

        /// <summary>开跑前记下要还原的东西，订阅换相事件（还原时退订）。</summary>
        private void PrepareTidegateDrillState()
        {
            TidegateSession session = TidegateSessionOrNull();
            CharacterMainControl player = CharacterMainControl.Main;
            _tidegateDrillSceneHandle = SceneManager.GetActiveScene().handle;
            _tidegateDrillStart = player != null ? player.transform.position : Vector3.zero;
            _tidegateDrillOffsetBefore = TidegateTide.DevOffsetHours;
            TidegateStoryData story = TidegateStory.Current;
            _tidegateDrillPagesBefore = story == null ? -1 : story.Pages;
            _tidegateDrillTouched = false;
            _tidegateDrillRestored = false;
            _tidegateDrillPhaseEvents.Clear();
            _tidegateDrillTide = session == null ? null : session.Tide;
            if (_tidegateDrillTide == null) return;
            if (_tidegateDrillPhaseHandler == null) _tidegateDrillPhaseHandler = OnTidegateDrillPhaseChanged;
            _tidegateDrillTide.PhaseChanged -= _tidegateDrillPhaseHandler;
            _tidegateDrillTide.PhaseChanged += _tidegateDrillPhaseHandler;
        }

        private void OnTidegateDrillPhaseChanged(TidegateTidePhase previous, TidegateTidePhase next)
        {
            _tidegateDrillPhaseEvents.Add(previous + ">" + next);
        }

        /// <summary>演练只有一个阶段，收尾不切图、不跑主套件清理（CompleteSession 按 _tidegateDrill 处理）。</summary>
        private IEnumerator RunTidegateDrillSession()
        {
            yield return null;
            try
            {
                yield return DriveSessionPhase(RunTidegateDrillSuite(), false);
            }
            finally { CompleteSession(); }
        }

        private IEnumerator RunTidegateDrillSuite()
        {
            TidegateSession live;
            string gone;
            if (!TidegateDrillLive(out live, out gone))
            {
                for (int i = 0; i < TidegateDrillCaseIds.Length; i++) Record(TidegateDrillCaseIds[i], "SKIP", 0L, string.Empty, gone);
                yield break;
            }
            SetStage("潮闸镇演练 1/8 装配核对与基线帧时间");
            yield return SamplePerformance("TIDE_PERF_BASELINE_5S", 5f, true, null);
            yield return RunTidegateDrillCase("TIDE_DRILL_ASSEMBLY", RunTidegateDrillAssembly);
            SetStage("潮闸镇演练 2/8 潮汐四相与换相前提示（拨潮相）");
            yield return RunTidegateDrillCase("TIDE_DRILL_TIDE_PHASES", RunTidegateDrillTidePhases);
            yield return RunTidegateDrillCase("TIDE_DRILL_TIDE_NOTICES", RunTidegateDrillTideNotices);
            SetStage("潮闸镇演练 3/8 三阀一闸与剧情箱柜");
            yield return RunTidegateDrillCase("TIDE_DRILL_VALVES_SLUICE", RunTidegateDrillValves);
            yield return RunTidegateDrillCase("TIDE_DRILL_STORY_CONTAINERS", RunTidegateDrillStoryContainers);
            SetStage("潮闸镇演练 4/8 游泳、深水箱子与淘沙帮");
            yield return RunTidegateDrillCase("TIDE_DRILL_SWIM", RunTidegateDrillSwim);
            yield return RunTidegateDrillCase("TIDE_DRILL_FLOOD_HINT", RunTidegateDrillFloodHint);
            yield return RunTidegateDrillCase("TIDE_DRILL_DIGGERS", RunTidegateDrillDiggers);
            SetStage("潮闸镇演练 5/8 灯塔与来船（约 50 秒）");
            yield return RunTidegateDrillCase("TIDE_DRILL_LIGHTHOUSE", RunTidegateDrillLighthouse);
            SetStage("潮闸镇演练 6/8 撤离读条与刷怪");
            yield return RunTidegateDrillCase("TIDE_DRILL_EXTRACTION_HOLD", RunTidegateDrillExtractionHold);
            yield return RunTidegateDrillCase("TIDE_DRILL_ENEMIES", RunTidegateDrillEnemies);
            SetStage("潮闸镇演练 7/8 地名、声音与帧时间");
            yield return RunTidegateDrillCase("TIDE_DRILL_REGION_SOUNDS", RunTidegateDrillRegionSounds);
            yield return SamplePerformance("TIDE_PERF_DOCK_10S", 10f, false, "POI_码头");
            yield return SamplePerformance("TIDE_PERF_TOWN_10S", 10f, false, "POI_老镇");
            SetStage("潮闸镇演练 8/8 还原");
            yield return TidegateDrillAwaitSpawns(8f);
            RecordTidegateDrillRestore();
        }

        // ====================================================================
        // 外壳与公共小工具
        // ====================================================================

        /// <summary>
        /// 潮闸镇演练用例的隔离壳（同 RunSkyIslandCase）：单条异常或超时只记 _UNHANDLED，不拖垮后面；不做清场。
        /// 用例自己记自己的 PASS / FAIL / SKIP。
        /// </summary>
        private IEnumerator RunTidegateDrillCase(string caseId, Func<IEnumerator> factory)
        {
            if (ShouldAbort())
            {
                Record(caseId, "SKIP", 0L, string.Empty, DescribeAbortReason());
                yield break;
            }
            TidegateSession live;
            string gone;
            if (!TidegateDrillLive(out live, out gone))
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
                    if (error != null || sw.Elapsed.TotalSeconds > TidegateDrillCaseTimeoutSeconds)
                    {
                        Record(caseId + "_UNHANDLED", "FAIL", sw.ElapsedMilliseconds, "budget_s=" + TidegateDrillCaseTimeoutSeconds,
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

        private TidegateSession TidegateSessionOrNull()
        {
            return _host == null ? null : _host.GetComponent<TidegateSession>();
        }

        /// <summary>这一趟还在：会话就绪、还是开跑时那个场景实例、主角活着。</summary>
        private bool TidegateDrillLive(out TidegateSession session, out string gone)
        {
            gone = null;
            session = TidegateSessionOrNull();
            CharacterMainControl player = CharacterMainControl.Main;
            if (session == null) gone = "tidegate_session_missing";
            else if (session.IsClosed || !session.IsReady) gone = "tidegate_session_not_ready";
            else if (SceneManager.GetActiveScene().handle != _tidegateDrillSceneHandle) gone = "tidegate_scene_changed";
            else if (player == null || player.Health == null || player.Health.IsDead) gone = "player_unavailable";
            else if (session.DevWorld == null || session.Tide == null) gone = "tidegate_world_missing";
            return gone == null;
        }

        private void RecordTidegateDrill(string id, Stopwatch sw, List<string> errors, List<string> notes)
        {
            string metrics = string.Join(" | ", notes.ToArray());
            if (errors.Count == 0) Record(id, "PASS", sw.ElapsedMilliseconds, metrics, string.Empty);
            else Record(id, "FAIL", sw.ElapsedMilliseconds, metrics, string.Join(",", errors.ToArray()));
        }

        /// <summary>站到离 <paramref name="near"/> 最近的安全可走面（离出口 5 m 外）并等落稳。</summary>
        private IEnumerator TidegateDrillStand(TidegateSession session, Vector3 near, List<string> errors, string tag)
        {
            Vector3 point;
            if (!TidegateDrillSafePoint(session, near, out point)) { errors.Add(tag + ":no_safe_point"); yield break; }
            string why;
            if (!session.DevTeleport(point, false, out why)) { errors.Add(tag + ":teleport_" + why); yield break; }
            yield return WaitSeconds(0.5f);
        }

        // 安全站位可避开 POI 的实体与出口，但不能退到另一条街还算作该地的采样。
        private static bool AtTidegatePerfStation(Vector3 position, Vector3 poi)
        {
            return TidegateDrillFlat(position, poi) <= 10f && Mathf.Abs(position.y - poi.y) <= 3f;
        }

        private static string TidegatePerfPosition(CharacterMainControl player)
        {
            return player == null ? "unavailable" : player.transform.position.ToString("F1");
        }

        private IEnumerator SamplePerformance(string id, float seconds, bool baseline, string poiName)
        {
            TidegateSession session;
            string gone;
            var errors = new List<string>();
            Vector3? target = null;
            if (!TidegateDrillLive(out session, out gone)) errors.Add(gone);
            else if (poiName != null)
            {
                Transform poi = session.DevFindMarker(poiName);
                if (poi == null) errors.Add("poi_missing:" + poiName);
                else
                {
                    target = poi.position;
                    yield return TidegateDrillStand(session, poi.position, errors, poiName);
                    if (errors.Count == 0) yield return WaitSeconds(1.5f);
                }
            }
            CharacterMainControl player = CharacterMainControl.Main;
            if (errors.Count == 0 && !TidegateDrillLive(out session, out gone)) errors.Add(gone);
            if (errors.Count == 0 && target.HasValue && !AtTidegatePerfStation(player.transform.position, target.Value))
                errors.Add("outside_sample_area");
            if (errors.Count != 0)
            {
                Record(id, "FAIL", 0L, "station=" + (poiName ?? "baseline") + ",target="
                    + (target.HasValue ? target.Value.ToString("F1") : "unavailable") + ",position=" + TidegatePerfPosition(player)
                    + ",samples=0,main_thread_p95_ms=unavailable,batches_p95=unavailable,gc_p95_bytes=unavailable"
                    + (poiName != null ? TidegatePerfWindow.MissingEnemyCounts : string.Empty),
                    string.Join(",", errors.ToArray()));
                yield break;
            }
            using (var probe = new TidegatePerfWindow(player, session.DevEnemies, poiName, target))
            {
                IEnumerator inner = SamplePerformanceWindow(id, seconds, baseline);
                yield return null; // 新计数器、样本缓冲与迭代器的分配不进采样窗。
                tidegatePerfWindow = probe;
                try { yield return inner; }
                finally { tidegatePerfWindow = null; }
            }
        }

        /// <summary>等在建的敌人都回来（海堤增援三连等）；超时或取消时，增援的迟到结果仍由取消标记兜底销毁。</summary>
        private IEnumerator TidegateDrillAwaitSpawns(float seconds)
        {
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone) || session.DevEnemies == null) yield break;
            float until = Time.realtimeSinceStartup + seconds;
            while (session.DevEnemies != null && session.DevEnemies.DevInFlight > 0 && Time.realtimeSinceStartup < until && !ShouldAbort())
                yield return null;
        }

        /// <summary>
        /// 拨到高潮（开始后 <paramref name="realSecondsIn"/> 现实秒）。当前非高潮时先拨到「低潮还剩 19 秒」等 14 秒：
        /// 下滩的淘沙帮按生产规则（涨潮前 20 秒撤）走回挖沙船，再拨高潮。直接跳过去会把他们和海堤增援一起泡在深水里卷走，
        /// 后面的淘沙帮用例和演练之后的人工测试都没人可看。
        /// </summary>
        private IEnumerator TidegateDrillJumpToHigh(TidegateSession session, double realSecondsIn)
        {
            TidegateTidePhase phase = session.Tide.Phase;
            if (phase != TidegateTidePhase.High) yield return TidegateDrillWaitForRetreat();
            TidegateTide.DevJumpInto(TidegateTidePhase.High, realSecondsIn);
            yield return WaitSeconds(1.3f);
        }

        /// <summary>快进涨潮之前留出生产规则的撤回窗口；四相循环与提示检查同样要走，避免演练先把后续待测对象卷走。</summary>
        private IEnumerator TidegateDrillWaitForRetreat()
        {
            TidegateTide.DevJumpBeforeEnd(TidegateTidePhase.Low, 19.0);
            yield return WaitSeconds(14f);
        }

        /// <summary>
        /// 安全站位：吸到最近的可走面；离任何出口不到 5 m 就往外挪 7 m 再吸（站进开着的撤离环读满会真的返航，环最大 2.6 m）。
        /// </summary>
        private static bool TidegateDrillSafePoint(TidegateSession session, Vector3 near, out Vector3 point)
        {
            point = near;
            Vector3 probe = near;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector3 snapped;
                if (!session.DevSnapToWalkable(probe, out snapped)) return false;
                Transform exit = TidegateDrillExitWithin(session.DevWorld, snapped, 5f);
                if (exit == null) { point = snapped; return true; }
                Vector3 away = snapped - exit.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.right;
                probe = exit.position + away.normalized * (7f + attempt * 2f);
            }
            return false;
        }

        private static Transform TidegateDrillExitWithin(TidegateWorld world, Vector3 position, float within)
        {
            foreach (Transform exit in world.Exits.Values)
            {
                Vector3 offset = exit.position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude < within * within) return exit;
            }
            return null;
        }

        /// <summary>
        /// 在 <paramref name="center"/> 周围找一处外海深水：水深不小于 <paramref name="minDepth"/>、不在泻湖、离可玩区边 4 m 以上、
        /// 离出口 6 m 以上、四周 0.6 m 内没有墙。找不到返回 false。
        /// </summary>
        private static bool TidegateDrillFindDeepWater(TidegateSession session, Vector3 center, float minDepth, out Vector3 found)
        {
            found = center;
            TidegateWorld world = session.DevWorld;
            TidegateTide tide = session.Tide;
            int wallMask = GameplayDataSettings.Layers.wallLayerMask.value;
            Rect area = TidegateWorld.PlayArea;
            Rect inner = Rect.MinMaxRect(area.xMin + 4f, area.yMin + 4f, area.xMax - 4f, area.yMax - 4f);
            for (float radius = 2f; radius <= 30f; radius += 2f)
                for (int step = 0; step < 16; step++)
                {
                    float angle = step * Mathf.PI * 2f / 16f;
                    Vector3 probe = new Vector3(center.x + Mathf.Cos(angle) * radius, tide.Level, center.z + Mathf.Sin(angle) * radius);
                    if (!inner.Contains(new Vector2(probe.x, probe.z)) || world.InLagoon(probe)) continue;
                    if (TidegateDrillExitWithin(world, probe, 6f) != null) continue;
                    float ground;
                    if (!world.TryGroundAt(probe, out ground) || tide.Level - ground < minDepth) continue;
                    Vector3 point = new Vector3(probe.x, tide.Level - 0.3f, probe.z);
                    if (Physics.CheckSphere(point, 0.6f, wallMask, QueryTriggerInteraction.Ignore)) continue;
                    found = point;
                    return true;
                }
            return false;
        }

        private static double TidegateDrillDepthAt(TidegateSession session, Vector3 position)
        {
            float ground;
            if (!session.DevWorld.TryGroundAt(position, out ground)) return double.NaN;
            return session.Tide.WaterLevelAt(position) - ground;
        }

        private static double TidegateDrillDrainDepth(TidegateSession session)
        {
            Transform drain;
            if (!session.DevWorld.Exits.TryGetValue("drain", out drain)) return double.NaN;
            float ground;
            if (!session.DevWorld.TryGroundAt(drain.position, out ground)) ground = drain.position.y;
            return session.Tide.WaterLevelAt(drain.position) - ground;
        }

        /// <summary>会被潮水淹、导航要跟着封的那几处：滩涂埋货、沉船货舱、沉船船长保险柜、排水暗渠出口。</summary>
        private static List<Transform> TidegateDrillTideMarkers(TidegateWorld world)
        {
            var list = new List<Transform>();
            foreach (Transform marker in world.LootMarkers)
                if (marker.name.StartsWith("LOOT_buried_", StringComparison.Ordinal) || marker.name.StartsWith("LOOT_wreck_", StringComparison.Ordinal)
                    || marker.name == "LOOT_safe_77") list.Add(marker);
            Transform drain;
            if (world.Exits.TryGetValue("drain", out drain)) list.Add(drain);
            return list;
        }

        private static List<TidegateEnemies.DevEnemyInfo> TidegateDrillGroup(TidegateEnemies enemies, string group, bool aliveOnly)
        {
            var list = new List<TidegateEnemies.DevEnemyInfo>();
            if (enemies == null) return list;
            foreach (TidegateEnemies.DevEnemyInfo info in enemies.DevEnemies())
                if (info.Group == group && !info.Extra && (!aliveOnly || info.Alive)) list.Add(info);
            return list;
        }

        private static float TidegateDrillFlat(Vector3 a, Vector3 b)
        {
            Vector3 offset = a - b;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>
        /// 这一刻潮汐各处读数对不对得上：相位与潮位（规则）、着色器全局量、Water 层水体顶面、出口开关与撤离环、HUD 小标行；
        /// <paramref name="plateau"/>（低潮 / 高潮平潮段）时再核已建好的箱子收没收起、滩涂 / 沉船 / 暗渠那几处导航封没封。
        /// </summary>
        private static void TidegateDrillCheckTide(TidegateSession session, TidegateTidePhase expected, bool plateau, List<string> errors, List<string> notes)
        {
            string tag = expected.ToString();
            TidegateTide tide = session.Tide;
            double clock = TidegateTide.ClockHours();
            if (tide.Phase != expected) errors.Add(tag + ":phase_" + tide.Phase);
            if (TidegateTideRules.PhaseAt(clock) != expected) errors.Add(tag + ":rule_phase_" + TidegateTideRules.PhaseAt(clock));
            float ruleLevel = TidegateTideRules.LevelAt(clock);
            if (!TidegateDrillLevelOk(clock, clock, 0.0)) errors.Add(tag + ":clock_invalid");
            if (!TidegateDrillLevelOk(tide.Level, ruleLevel, 0.03f)) errors.Add(tag + ":level_" + tide.Level.ToString("F2") + "_rule_" + ruleLevel.ToString("F2"));
            float shaderTide = Shader.GetGlobalFloat("_TidegateTide"), shaderLagoon = Shader.GetGlobalFloat("_TidegateLagoon");
            if (!TidegateDrillLevelOk(shaderTide, tide.Level, 0.01f)) errors.Add(tag + ":shader_tide_" + shaderTide.ToString("F2"));
            if (!TidegateDrillLevelOk(shaderLagoon, tide.LagoonLevel, 0.01f)) errors.Add(tag + ":shader_lagoon_" + shaderLagoon.ToString("F2"));
            TidegateWater water = session.DevWater;
            if (water == null) errors.Add(tag + ":water_missing");
            else
            {
                string why;
                List<float> seaTops = water.DevSeaTops, lagoonTops = water.DevLagoonTops;
                if (!TidegateDrillWaterTopsOk(seaTops, tide.Level, 0.03f, out why)) errors.Add(tag + ":sea_" + why);
                if (!TidegateDrillWaterTopsOk(lagoonTops, tide.LagoonLevel, 0.03f, out why)) errors.Add(tag + ":lagoon_" + why);
                notes.Add(tag + ":sea_volumes=" + seaTops.Count + ",lagoon_volumes=" + lagoonTops.Count);
            }
            TidegateExtraction extraction = session.DevExtraction;
            if (extraction == null) errors.Add(tag + ":extraction_missing");
            else
            {
                double drainDepth = TidegateDrillDrainDepth(session);
                bool boat = session.DevDevices != null && session.DevDevices.DevBoatArrived;
                List<string> openIds = extraction.DevOpenExits();
                string want = TidegateDrillExpectedExits(expected, drainDepth, boat);
                string open = TidegateDrillJoinSorted(openIds);
                string rings = TidegateDrillJoinSorted(extraction.DevVisibleRings());
                if (open != want) errors.Add(tag + ":exits_" + open + "_want_" + want);
                if (rings != open) errors.Add(tag + ":rings_" + rings + "_open_" + open);
                string chips = session.DevFieldStatus() ?? string.Empty;
                if (chips.IndexOf(TidegateText.PhaseName(expected), StringComparison.Ordinal) < 0) errors.Add(tag + ":chip_missing_phase");
                foreach (string id in openIds)
                    if (chips.IndexOf(TidegateText.ExitName(id), StringComparison.Ordinal) < 0) errors.Add(tag + ":chip_missing_exit_" + id);
                notes.Add(tag + ":level=" + tide.Level.ToString("F2") + ",lagoon=" + tide.LagoonLevel.ToString("F2") + ",exits=" + open
                    + ",drain_depth=" + drainDepth.ToString("F2"));
            }
            if (!plateau) return;
            TidegateLoot loot = session.DevLoot;
            CharacterMainControl player = CharacterMainControl.Main;
            int checkedLoot = 0, mismatched = 0;
            if (loot != null && player != null)
                foreach (TidegateLoot.DevPointInfo point in loot.DevPoints(false))
                {
                    if (!point.Built || point.Failed) continue;
                    // 玩家 4 m 内的箱子按规则不收起（搜刮界面可能开着），不在这里判。
                    if ((point.Position - player.transform.position).sqrMagnitude <= 4.5f * 4.5f) continue;
                    checkedLoot++;
                    bool deep = tide.IsDeepAt(point.Position);
                    if (point.Flooded == deep && point.BoxActive != point.Flooded) continue;
                    mismatched++;
                    if (mismatched <= 4)
                        errors.Add(tag + ":loot_" + point.Id + "_flooded_" + point.Flooded + "_deep_" + deep + "_active_" + point.BoxActive);
                }
            int checkedNav = 0;
            if (session.DevNavigation != null)
                foreach (Transform marker in TidegateDrillTideMarkers(session.DevWorld))
                {
                    double depth = TidegateDrillDepthAt(session, marker.position);
                    // 贴着「深水」门槛的点三角形最低顶点与箱子脚下高度可能差几厘米，只判明显淹 / 明显干的。
                    if (double.IsNaN(depth) || Math.Abs(depth - TidegateTideRules.DeepWater) < 0.3) continue;
                    bool walkable = session.DevNavigation.IsWalkableAt(marker.position);
                    checkedNav++;
                    if (walkable != depth <= TidegateTideRules.DeepWater)
                        errors.Add(tag + ":nav_" + marker.name + "_walkable_" + walkable + "_depth_" + depth.ToString("F2"));
                }
            notes.Add(tag + ":loot_checked=" + checkedLoot + ",nav_checked=" + checkedNav);
        }

        // ====================================================================
        // 8. 还原
        // ====================================================================

        private void RecordTidegateDrillRestore()
        {
            string metrics;
            List<string> errors = RestoreTidegateDrillState(out metrics);
            Record("TIDE_DRILL_RESTORE", errors.Count == 0 ? "PASS" : "FAIL", 0L, metrics,
                errors.Count == 0 ? string.Empty : "还原不完整：" + string.Join(",", errors.ToArray()));
        }

        /// <summary>
        /// 还原这一趟出击的内存状态（幂等）：退订换相、潮相偏移回到开跑前、游泳覆盖清掉、动过的机关复位（阀门、大闸、灯塔、来船）、
        /// 全图显示提前结束、演练招来的海堤增援移走、主角送回开跑处，再逐项读回核对。
        /// </summary>
        private List<string> RestoreTidegateDrillState(out string metrics)
        {
            var errors = new List<string>();
            var notes = new List<string>();
            if (_tidegateDrillTide != null && _tidegateDrillPhaseHandler != null) _tidegateDrillTide.PhaseChanged -= _tidegateDrillPhaseHandler;
            _tidegateDrillTide = null;
            TidegateSession session = TidegateSessionOrNull();
            bool alive = session != null && !session.IsClosed && session.Tide != null;
            if (alive)
            {
                // 潮相偏移只在这一趟还在时复位：出击已收尾时 TidegateTide.Dispose 已经清零，再写回开跑前的值会带进下一趟。
                TidegateTide.DevOffsetHours = _tidegateDrillOffsetBefore;
                if (session.DevWater != null) session.DevWater.DevSwimOverride = 0;
                if (_tidegateDrillTouched)
                {
                    if (session.DevDevices != null) session.DevDevices.DevReset();
                    if (session.DevEnemies != null)
                    {
                        session.DevEnemies.DevEndReveal();
                        int pending;
                        int removed = session.DevEnemies.DevRemoveExtras(out pending);
                        notes.Add("extras_removed=" + removed + ",extras_late_results_cancelled=" + pending);
                    }
                }
                CharacterMainControl player = CharacterMainControl.Main;
                string why;
                // 送回开跑处也走安全站位：潮相已经不同，原地附近的撤离环可能开着。
                Vector3 back;
                if (!TidegateDrillSafePoint(session, _tidegateDrillStart, out back)) back = _tidegateDrillStart;
                if (session.IsReady && player != null && player.Health != null && !player.Health.IsDead
                    && !session.DevTeleport(back, false, out why)) errors.Add("player_not_returned_" + why);
                TidegateDevices devices = session.DevDevices;
                if (devices != null && (devices.OpenValves != 0 || devices.DevLighthouseLit || devices.DevBoatArrived || devices.DevBeam != null))
                    errors.Add("devices_not_reset");
                if (session.Tide.SluiceOpen) errors.Add("sluice_still_open");
                if (session.Tide.DevBeaconOn) errors.Add("beacon_still_on");
                if (session.DevExtraction != null && session.DevExtraction.DevLighthouseBoat) errors.Add("lighthouse_boat_still_there");
                if (session.DevWater != null && session.DevWater.DevSwimOverride != 0) errors.Add("swim_override_left");
                if (session.DevEnemies != null && session.DevEnemies.DevRevealActive) errors.Add("reveal_left");
            }
            else notes.Add("session_gone");
            if (alive && Math.Abs(TidegateTide.DevOffsetHours - _tidegateDrillOffsetBefore) > 1e-9) errors.Add("tide_offset_not_restored");
            TidegateStoryData story = TidegateStory.Current;
            if (story != null && _tidegateDrillPagesBefore >= 0 && story.Pages != _tidegateDrillPagesBefore) errors.Add("story_pages_changed_by_drill");
            _tidegateDrillRestored = true;
            notes.Add("offset_hours=" + TidegateTide.DevOffsetHours.ToString("F3") + ",touched=" + _tidegateDrillTouched);
            metrics = string.Join(",", notes.ToArray());
            return errors;
        }

        /// <summary>CompleteSession 的兜底（取消、异常、超时、宿主销毁也走到）：套件自己的还原没跑到时在这里同步还原一次。</summary>
        private void FinishTidegateDrillRestoreSynchronously()
        {
            if (_tidegateDrillRestored) return;
            try
            {
                string metrics;
                List<string> errors = RestoreTidegateDrillState(out metrics);
                WriteRaw("TIDE_DRILL_RESTORE_FALLBACK | " + (errors.Count == 0 ? "ok" : string.Join(",", errors.ToArray())) + " | " + metrics);
            }
            catch (Exception e) { WriteRaw("TIDE_DRILL_RESTORE_FALLBACK | threw | " + e.Message); }
        }

        // ====================================================================
        // 纯判据（tests/fixtures/TidegateDrillJudges 逐字抽出来执行）
        // ====================================================================

        #region 纯判据
        /// <summary>某一相在一轮里的起点（游戏小时，0 起）：退潮 0、低潮 0.5、涨潮 2.5、高潮 3。</summary>
        internal static double TidegateDrillPhaseStart(TidegateTidePhase phase)
        {
            switch (phase)
            {
                case TidegateTidePhase.Ebb: return 0.0;
                case TidegateTidePhase.Low: return TidegateTideRules.EbbHours;
                case TidegateTidePhase.Flood: return TidegateTideRules.EbbHours + TidegateTideRules.LowHours;
                default: return TidegateTideRules.EbbHours + TidegateTideRules.LowHours + TidegateTideRules.FloodHours;
            }
        }

        /// <summary>某一相在一轮里的终点（游戏小时）。</summary>
        internal static double TidegateDrillPhaseEnd(TidegateTidePhase phase)
        {
            return phase == TidegateTidePhase.High ? TidegateTideRules.CycleHours : TidegateDrillPhaseStart((TidegateTidePhase)((int)phase + 1));
        }

        /// <summary>从轮内位置 <paramref name="from"/> 往前走到 <paramref name="to"/> 要多少游戏小时：[0, 一轮)，不往回拨。</summary>
        internal static double TidegateDrillForwardHours(double from, double to)
        {
            double cycle = TidegateTideRules.CycleHours;
            double delta = (to - from) % cycle;
            if (delta < 0) delta += cycle;
            if (delta < 1e-6) delta = 0.0;
            return delta;
        }

        /// <summary>这一相该开着哪些出口（逗号分隔、按字母排）：公路常驻；渡口只在高潮；暗渠出口水深 &lt; 0.25 m；灯塔码头要来过船。</summary>
        internal static string TidegateDrillExpectedExits(TidegateTidePhase phase, double drainDepth, bool boat)
        {
            var ids = new List<string> { "road" };
            if (phase == TidegateTidePhase.High) ids.Add("ferry");
            if (!double.IsNaN(drainDepth) && drainDepth < TidegateTideRules.DrainExposedDepth) ids.Add("drain");
            if (boat) ids.Add("lh");
            return TidegateDrillJoinSorted(ids);
        }

        internal static string TidegateDrillJoinSorted(IEnumerable<string> ids)
        {
            var list = new List<string>(ids);
            list.Sort(StringComparer.Ordinal);
            return string.Join(",", list.ToArray());
        }

        /// <summary>泻湖放水：每秒 0.085 m，降到外海潮位就停，容差 0.06 m（帧时间抖动）。</summary>
        internal static bool TidegateDrillLagoonDropOk(double before, double after, double sea, double seconds, out double expected)
        {
            expected = Math.Max(sea, before - TidegateTideRules.LagoonDrainRate * Math.Max(0.0, seconds));
            return Math.Abs(after - expected) <= 0.06;
        }

        /// <summary>光束转过的角度（度，[0, 360)）：Unity 欧拉角按 0–360 绕回。</summary>
        internal static double TidegateDrillYawAdvance(double before, double after)
        {
            double delta = (after - before) % 360.0;
            return delta < 0 ? delta + 360.0 : delta;
        }

        /// <summary>光束约 30°/秒：容差 ±12°/秒（帧时间与暂停的抖动）。</summary>
        internal static bool TidegateDrillYawOk(double advance, double seconds)
        {
            if (seconds <= 0.05) return false;
            double rate = advance / seconds;
            return rate >= 18.0 && rate <= 42.0;
        }

        /// <summary>刷怪在玩家 25–75 m 之间（容差 0.5 m：玩家这一帧与选点那一帧之间挪了一点）。</summary>
        internal static bool TidegateDrillSpawnDistanceOk(double distance)
        {
            return distance >= 24.5 && distance <= 75.5;
        }

        /// <summary>没有实际观测到的敌人检查不能算通过：逐槽开刷与距离样本齐全，守卫配枪和 Boss 标识都有活体证据。</summary>
        internal static bool TidegateDrillEnemyCoverageOk(int total, int started, int measured, int expectedGuards, int guards,
            int expectedBosses, int bosses, int alive, out string reason)
        {
            var missing = new List<string>();
            if (total <= 0 || started != total) missing.Add("spawn_slots_unobserved_" + (total - started));
            if (measured != started || measured <= 0) missing.Add("spawn_distance_samples_incomplete");
            if (alive <= 0) missing.Add("no_live_enemy_for_hostility_and_collision");
            if (expectedGuards <= 0 || guards != expectedGuards) missing.Add("guard_weapon_observations_" + guards + "_of_" + expectedGuards);
            if (expectedBosses <= 0 || bosses != expectedBosses) missing.Add("boss_preset_observations_" + bosses + "_of_" + expectedBosses);
            reason = string.Join(",", missing.ToArray());
            return missing.Count == 0;
        }

        /// <summary>水盒必须存在且每个读数都有效；NaN / Infinity 不能在容差比较中漏过。</summary>
        internal static bool TidegateDrillWaterTopsOk(IList<float> tops, double expected, double tolerance, out string reason)
        {
            reason = null;
            if (double.IsNaN(expected) || double.IsInfinity(expected)) { reason = "expected_level_invalid"; return false; }
            if (tops == null || tops.Count == 0) { reason = "volumes_missing"; return false; }
            for (int i = 0; i < tops.Count; i++)
            {
                double value = tops[i];
                if (double.IsNaN(value) || double.IsInfinity(value)) { reason = "volume_" + i + "_top_invalid"; return false; }
                if (Math.Abs(value - expected) > tolerance) { reason = "volume_" + i + "_top_" + value.ToString("F2"); return false; }
            }
            return true;
        }

        internal static bool TidegateDrillLevelOk(double actual, double expected, double tolerance)
        {
            return !double.IsNaN(actual) && !double.IsInfinity(actual) && !double.IsNaN(expected) && !double.IsInfinity(expected)
                && Math.Abs(actual - expected) <= tolerance;
        }

        /// <summary>灯塔点亮后 45 秒来船（容差 ±1.5 秒）。</summary>
        internal static bool TidegateDrillBoatOnTime(double seconds)
        {
            return seconds >= 43.5 && seconds <= 46.5;
        }

        /// <summary>几条字幕里有几条含任一关键词（中英两种语言各给一个）。</summary>
        internal static int TidegateDrillCount(IList<string> lines, string[] needles)
        {
            int count = 0;
            if (lines == null) return 0;
            foreach (string line in lines) if (TidegateDrillContainsAny(line, needles)) count++;
            return count;
        }

        internal static bool TidegateDrillContainsAny(string text, params string[] needles)
        {
            if (string.IsNullOrEmpty(text) || needles == null) return false;
            foreach (string needle in needles)
                if (!string.IsNullOrEmpty(needle) && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>换相字幕的关键词（会话换相字幕的中英两版原文）。</summary>
        internal static string[] TidegateDrillPhaseCaption(TidegateTidePhase phase)
        {
            switch (phase)
            {
                case TidegateTidePhase.Ebb: return new[] { "开始退潮了", "tide is going out" };
                case TidegateTidePhase.Low: return new[] { "低潮：", "Low tide:" };
                case TidegateTidePhase.Flood: return new[] { "涨潮了！", "tide is coming in" };
                default: return new[] { "高潮：", "High tide:" };
            }
        }
        #endregion
    }
}
#endif
